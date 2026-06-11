using System.Windows;
using MicGain.App.ViewModels;
using MicGain.App.Views;
using MicGain.Core.Audio;
using MicGain.Core.IO;
using MicGain.Core.Models;
using MicGain.Core.Services;

namespace MicGain.App;

/// <summary>Composition root: wires the real Windows-backed implementations into the ViewModels.</summary>
public partial class App : Application
{
    /// <summary>
    /// Bundled installer file name — version VM-verified in research-notes §11. The binary is
    /// not committed; see assets/installer/README.md. Resolved relative to
    /// <see cref="AppContext.BaseDirectory"/> so packaging (T3.1) only has to copy the folder
    /// next to the executable.
    /// </summary>
    private const string InstallerFileName = "EqualizerAPO-x64-1.4.2.exe";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var registry = new WindowsRegistryReader();
        var fileSystem = new PhysicalFileSystem();
        var detectionService = new ApoDetectionService(registry, fileSystem);
        var deviceService = new AudioDeviceService(new NAudioDeviceEnumerator(), registry);

        bool apoInstalled;
        try
        {
            apoInstalled = detectionService.Detect().IsInstalled;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not check the Equalizer APO installation: {ex.Message}",
                "MicGain", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        if (!apoInstalled)
        {
            // T2.1 (issue #6): consent flow. T2.2 (issue #3): guided install wired below.
            RunInstallConsentFlow(registry, fileSystem, detectionService, deviceService);
            return;
        }

        ShowMainWindow(detectionService, deviceService, fileSystem);
    }

    private void ShowMainWindow(
        IApoDetectionService detectionService,
        IAudioDeviceService deviceService,
        IFileSystem fileSystem)
    {
        var viewModel = new MainViewModel(
            detectionService,
            deviceService,
            configDirectory => new ApoConfigService(fileSystem, configDirectory));

        var window = new MainWindow { DataContext = viewModel };
        window.Closed += (_, _) => Shutdown();
        window.Show();

        _ = viewModel.LoadAsync();
    }

    /// <summary>
    /// T2.1 consent (issue #6) + T2.2 guided install (issue #3). 'async void' is acceptable
    /// here: it is a top-level WPF entry point, every await is guarded by try/catch, and
    /// ShutdownMode=OnExplicitShutdown keeps the app alive across the awaits.
    /// </summary>
    private async void RunInstallConsentFlow(
        WindowsRegistryReader registry,
        PhysicalFileSystem fileSystem,
        IApoDetectionService detectionService,
        IAudioDeviceService deviceService)
    {
        var viewModel = new InstallConsentViewModel(deviceService);
        try
        {
            viewModel.Initialize();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not inspect audio devices: {ex.Message}",
                "MicGain", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        if (viewModel.State == InstallFlowState.NoDevice)
        {
            // T2.1 acceptance criterion 2: clear message, then clean exit with zero system changes.
            MessageBox.Show(viewModel.NoDeviceMessage, "MicGain", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var dialog = new InstallConsentDialog { DataContext = viewModel };
        dialog.ShowDialog();
        viewModel.HandleDialogClosed(); // closing via 'X' counts as a decline

        if (viewModel.State != InstallFlowState.Installing)
        {
            // Declined (T2.1 acceptance criterion 1): clean exit, zero system changes.
            Shutdown();
            return;
        }

        // T2.2 (issue #3): dialog consent given — run the guided install (primary path,
        // research-notes §11). Every individual system change below is still gated by
        // WpfInstallInteraction.ConfirmSystemChangeAsync (AC2; AGENTS.md rule 2).
        var installerPath = Path.Combine(
            AppContext.BaseDirectory, "assets", "installer", InstallerFileName);

        var installService = new ApoInstallService(
            fileSystem,
            registry,
            new WindowsRegistryWriter(),
            new SystemProcessRunner(),
            new WpfInstallInteraction(),
            installerPath);

        InstallOutcome outcome;
        try
        {
            outcome = await installService.RunGuidedInstallAsync(viewModel.DefaultOutputDevice!);
        }
        catch (Exception ex)
        {
            viewModel.StateMachine.FailInstall(); // Installing → InstallFailed
            MessageBox.Show(
                $"The installation failed unexpectedly: {ex.Message}\n\n" +
                "Any registry changes made by MicGain are rolled back automatically where " +
                "possible — see docs/rollback.md for how to verify and undo manually.",
                "MicGain", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        HandleInstallOutcome(
            outcome, viewModel.StateMachine, installerPath, detectionService, deviceService, fileSystem);
    }

    /// <summary>
    /// Maps every <see cref="InstallOutcome"/> to a state-machine transition plus a
    /// user-facing message (issue #3 AC1). Success paths transition Installing → Ready and
    /// show the main window; all other outcomes transition Installing → InstallFailed and
    /// exit cleanly.
    /// </summary>
    private void HandleInstallOutcome(
        InstallOutcome outcome,
        InstallConsentStateMachine stateMachine,
        string installerPath,
        IApoDetectionService detectionService,
        IAudioDeviceService deviceService,
        IFileSystem fileSystem)
    {
        switch (outcome)
        {
            case InstallOutcome.Succeeded:
                stateMachine.CompleteInstall();
                MessageBox.Show(
                    "Equalizer APO is installed and enabled for your output device.\n" +
                    "The Windows audio service was restarted — MicGain is ready to use.",
                    "MicGain", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowMainWindow(detectionService, deviceService, fileSystem);
                return;

            case InstallOutcome.SucceededPendingRestart:
                stateMachine.CompleteInstall();
                MessageBox.Show(
                    "Equalizer APO is installed and enabled for your output device.\n" +
                    "Because the audio-service restart was declined, it becomes active after " +
                    "the Windows audio service restarts or after the next reboot.",
                    "MicGain", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowMainWindow(detectionService, deviceService, fileSystem);
                return;

            case InstallOutcome.ConsentDeclined:
                stateMachine.FailInstall();
                MessageBox.Show(
                    "Installation was cancelled: consent was not given for a required step " +
                    "(or the administrator prompt was cancelled).\n" +
                    "No further changes were made to your system.",
                    "MicGain", MessageBoxButton.OK, MessageBoxImage.Information);
                break;

            case InstallOutcome.InstallerNotFound:
                stateMachine.FailInstall();
                MessageBox.Show(
                    "The bundled Equalizer APO installer was not found:\n" +
                    $"{installerPath}\n\n" +
                    "Nothing was executed and no changes were made to your system.\n" +
                    "Place the installer there (see assets/installer/README.md) or install " +
                    "Equalizer APO manually, then start MicGain again.",
                    "MicGain", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;

            case InstallOutcome.DeviceNotEnabled:
                stateMachine.FailInstall();
                MessageBox.Show(
                    "Equalizer APO is not enabled for your output device — it may not have " +
                    "been selected in the Configurator.\n\n" +
                    "Run the Configurator manually (Configurator.exe in the Equalizer APO " +
                    "install folder), select your device, then start MicGain again.",
                    "MicGain", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;

            case InstallOutcome.FailedRolledBack:
                stateMachine.FailInstall();
                MessageBox.Show(
                    "A registry write failed during installation. The changes MicGain made " +
                    "were rolled back automatically, so your system is back in its previous " +
                    "state.\nSee docs/rollback.md for how to verify this manually.",
                    "MicGain", MessageBoxButton.OK, MessageBoxImage.Error);
                break;

            default:
                stateMachine.FailInstall();
                MessageBox.Show(
                    $"Unexpected installation result '{outcome}'. No further action was taken.",
                    "MicGain", MessageBoxButton.OK, MessageBoxImage.Error);
                break;
        }

        Shutdown(); // terminal non-success states: clean exit
    }
}
