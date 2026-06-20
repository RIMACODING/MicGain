using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
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
        DebugLog.Initialize(e.Args);
        if (DebugLog.Enabled)
        {
            DispatcherUnhandledException += (_, args) =>
            {
                DebugLog.WriteException(args.Exception, "DispatcherUnhandledException");
            };
        }

        base.OnStartup(e);

        DebugLog.WriteLine($"App starting — BaseDirectory={AppContext.BaseDirectory}");

        var registry = new WindowsRegistryReader();
        var fileSystem = new PhysicalFileSystem();
        var detectionService = new ApoDetectionService(registry, fileSystem);
        var deviceService = new AudioDeviceService(new NAudioDeviceEnumerator(), registry);

        ApoDetectionResult detection;
        try
        {
            DebugLog.WriteLine("Running APO detection...");
            detection = detectionService.Detect();
            DebugLog.WriteLine($"Detection result: IsInstalled={detection.IsInstalled}, ConfigPath={detection.ConfigPath ?? "(null)"}");
        }
        catch (Exception ex)
        {
            DebugLog.WriteException(ex);
            MessageBox.Show(
                $"Could not check the Equalizer APO installation: {ex.Message}",
                "MicGain", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        if (!detection.IsInstalled)
        {
            DebugLog.WriteLine("APO not installed — entering install-consent flow.");
            RunInstallConsentFlow(registry, fileSystem, detectionService, deviceService);
            return;
        }

        DebugLog.WriteLine($"APO is installed — configPath={detection.ConfigPath!}");

        // Check if any capture device has APO enabled. If none do, offer to launch
        // the Configurator so the user can enable it on their microphone.
        IReadOnlyList<AudioDeviceInfo> allDevices;
        try
        {
            allDevices = deviceService.GetDevices();
        }
        catch (Exception ex)
        {
            DebugLog.WriteException(ex);
            // Fall back to showing the main window with whatever error state LoadAsync produces.
            ShowMainWindow(detection.ConfigPath!, detectionService, deviceService, fileSystem);
            return;
        }

        var captureDevices = allDevices.Where(d => d.Flow == DeviceFlow.Capture).ToList();
        var captureHasApo = captureDevices.Any(d => d.IsApoEnabled);
        DebugLog.WriteLine($"Capture devices with APO enabled: {captureHasApo}; total capture devices: {captureDevices.Count}");

        if (captureDevices.Count == 0)
        {
            DebugLog.WriteLine("No capture devices — showing main window in NoCaptureDevices state.");
            ShowMainWindow(detection.ConfigPath!, detectionService, deviceService, fileSystem);
            return;
        }

        if (!captureHasApo)
        {
            DebugLog.WriteLine("APO installed but no capture device has APO enabled — offering Configurator launch.");
            RunConfiguratorOnlyFlow(
                registry, fileSystem, detectionService, deviceService,
                detection.ConfigPath!, captureDevices);
            return;
        }

        DebugLog.WriteLine("At least one capture device has APO enabled — showing main window.");
        ShowMainWindow(detection.ConfigPath!, detectionService, deviceService, fileSystem);
    }

    // -----------------------------------------------------------------------------------------
    // Flow: APO installed but no capture device has it enabled → launch Configurator
    // -----------------------------------------------------------------------------------------

    private async void RunConfiguratorOnlyFlow(
        WindowsRegistryReader registry,
        PhysicalFileSystem fileSystem,
        IApoDetectionService detectionService,
        IAudioDeviceService deviceService,
        string configPath,
        List<AudioDeviceInfo> captureDevices)
    {
        // Pick the first active capture device to guide the user towards.
        var primaryCapture = captureDevices.First();

        DebugLog.WriteLine($"Offering Configurator launch for capture device '{primaryCapture.FriendlyName}' (GUID={primaryCapture.EndpointGuid})");

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
            outcome = await installService.RunConfiguratorOnlyAsync(primaryCapture);
            DebugLog.WriteLine($"Configurator-only flow completed — outcome={outcome}");
        }
        catch (Exception ex)
        {
            DebugLog.WriteException(ex);
            MessageBox.Show(
                $"Could not launch the Configurator: {ex.Message}",
                "MicGain", MessageBoxButton.OK, MessageBoxImage.Error);
            // Fall through to the main window anyway — the user can still see device list.
            ShowMainWindow(configPath, detectionService, deviceService, fileSystem);
            return;
        }

        HandleConfiguratorOutcome(
            outcome, configPath, detectionService, deviceService, fileSystem);
    }

    private void HandleConfiguratorOutcome(
        InstallOutcome outcome,
        string configPath,
        IApoDetectionService detectionService,
        IAudioDeviceService deviceService,
        IFileSystem fileSystem)
    {
        DebugLog.WriteLine($"HandleConfiguratorOutcome: outcome={outcome}");

        switch (outcome)
        {
            case InstallOutcome.Succeeded:
                DebugLog.WriteLine("Configurator flow: Succeeded — audio service restarted, showing main window.");
                MessageBox.Show(
                    "Equalizer APO is now enabled for your input device.\n" +
                    "The Windows audio service was restarted — MicGain is ready to use.",
                    "MicGain", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowMainWindow(configPath, detectionService, deviceService, fileSystem);
                return;

            case InstallOutcome.SucceededPendingRestart:
                DebugLog.WriteLine("Configurator flow: SucceededPendingRestart — pending reboot, showing main window.");
                MessageBox.Show(
                    "Equalizer APO is now enabled for your input device.\n" +
                    "Because the audio-service restart was declined, it becomes active after " +
                    "the Windows audio service restarts or after the next reboot.",
                    "MicGain", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowMainWindow(configPath, detectionService, deviceService, fileSystem);
                return;

            case InstallOutcome.ConsentDeclined:
                DebugLog.WriteLine("Configurator flow: ConsentDeclined.");
                MessageBox.Show(
                    "Configurator launch was cancelled.\nMicGain will open, but gain control " +
                    "will be disabled until APO is enabled on a device. Run the Configurator " +
                    "manually (Configurator.exe in the Equalizer APO install folder), then " +
                    "click Refresh in the main window.",
                    "MicGain", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowMainWindow(configPath, detectionService, deviceService, fileSystem);
                return;

            case InstallOutcome.InstallerNotFound:
                DebugLog.WriteLine("Configurator flow: InstallerNotFound — Configurator.exe missing.");
                MessageBox.Show(
                    "Could not find Configurator.exe in the Equalizer APO install folder.\n" +
                    "The APO registry says it is installed, but the Configurator is missing. " +
                    "Reinstall Equalizer APO, then start MicGain again.",
                    "MicGain", MessageBoxButton.OK, MessageBoxImage.Warning);
                ShowMainWindow(configPath, detectionService, deviceService, fileSystem);
                return;

            case InstallOutcome.DeviceNotEnabled:
                DebugLog.WriteLine("Configurator flow: DeviceNotEnabled — user didn't select device.");
                MessageBox.Show(
                    "Equalizer APO does not appear to be enabled for the device — " +
                    "it may not have been selected in the Configurator.\n\n" +
                    "You can run the Configurator manually (Configurator.exe in the Equalizer " +
                    "APO install folder), select your microphone, then click Refresh in the main window.",
                    "MicGain", MessageBoxButton.OK, MessageBoxImage.Warning);
                ShowMainWindow(configPath, detectionService, deviceService, fileSystem);
                return;

            default:
                DebugLog.WriteLine($"Configurator flow: UNEXPECTED '{outcome}'");
                MessageBox.Show(
                    $"Unexpected result from Configurator launch: '{outcome}'. MicGain will open anyway.",
                    "MicGain", MessageBoxButton.OK, MessageBoxImage.Error);
                ShowMainWindow(configPath, detectionService, deviceService, fileSystem);
                return;
        }
    }

    // -----------------------------------------------------------------------------------------
    // Main window
    // -----------------------------------------------------------------------------------------

    private void ShowMainWindow(
        string configPath,
        IApoDetectionService detectionService,
        IAudioDeviceService deviceService,
        IFileSystem fileSystem)
    {
        DebugLog.WriteLine($"Creating MainViewModel with configPath={configPath}");

        var viewModel = new MainViewModel(
            configPath,
            detectionService,
            deviceService,
            configDirectory => new ApoConfigService(fileSystem, configDirectory));

        var window = new MainWindow { DataContext = viewModel };
        window.Closed += (_, _) => Shutdown();
        window.Show();

        DebugLog.WriteLine("MainWindow shown, firing LoadAsync...");
        _ = viewModel.LoadAsync();
    }

    // -----------------------------------------------------------------------------------------
    // Install consent flow (APO not installed)
    // -----------------------------------------------------------------------------------------

    private async void RunInstallConsentFlow(
        WindowsRegistryReader registry,
        PhysicalFileSystem fileSystem,
        IApoDetectionService detectionService,
        IAudioDeviceService deviceService)
    {
        DebugLog.WriteLine("Creating InstallConsentViewModel...");
        var viewModel = new InstallConsentViewModel(deviceService);
        try
        {
            viewModel.Initialize();
            DebugLog.WriteLine($"InstallConsent initialized: State={viewModel.State}, DefaultOutputDevice={viewModel.DefaultOutputDevice?.FriendlyName ?? "(null)"}");
        }
        catch (Exception ex)
        {
            DebugLog.WriteException(ex);
            MessageBox.Show(
                $"Could not inspect audio devices: {ex.Message}",
                "MicGain", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        if (viewModel.State == InstallFlowState.NoDevice)
        {
            DebugLog.WriteLine("No output device found — aborting install flow.");
            MessageBox.Show(viewModel.NoDeviceMessage, "MicGain", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DebugLog.WriteLine($"Showing InstallConsentDialog for device '{viewModel.DefaultOutputDevice!.FriendlyName}' (GUID={viewModel.DefaultOutputDevice.EndpointGuid})...");

        var dialog = new InstallConsentDialog { DataContext = viewModel };
        dialog.ShowDialog();
        viewModel.HandleDialogClosed();

        DebugLog.WriteLine($"InstallConsent dialog closed — State={viewModel.State}");

        if (viewModel.State != InstallFlowState.Installing)
        {
            DebugLog.WriteLine("User declined install — shutting down.");
            Shutdown();
            return;
        }

        DebugLog.WriteLine("User accepted install — beginning guided install.");

        var installerPath = Path.Combine(
            AppContext.BaseDirectory, "assets", "installer", InstallerFileName);

        DebugLog.WriteLine($"Installer path resolved to: {installerPath}");
        DebugLog.WriteLine($"Installer exists on disk: {fileSystem.FileExists(installerPath)}");

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
            DebugLog.WriteLine($"Calling RunGuidedInstallAsync for device '{viewModel.DefaultOutputDevice.FriendlyName}'...");
            outcome = await installService.RunGuidedInstallAsync(viewModel.DefaultOutputDevice!);
            DebugLog.WriteLine($"RunGuidedInstallAsync completed — outcome={outcome}");
        }
        catch (Exception ex)
        {
            DebugLog.WriteException(ex);
            viewModel.StateMachine.FailInstall();
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

    private void HandleInstallOutcome(
        InstallOutcome outcome,
        InstallConsentStateMachine stateMachine,
        string installerPath,
        IApoDetectionService detectionService,
        IAudioDeviceService deviceService,
        IFileSystem fileSystem)
    {
        DebugLog.WriteLine($"HandleInstallOutcome: outcome={outcome}");

        switch (outcome)
        {
            case InstallOutcome.Succeeded:
                DebugLog.WriteLine("Install outcome branch: Succeeded — restarting audio service + main window.");
                stateMachine.CompleteInstall();
                MessageBox.Show(
                    "Equalizer APO is installed and enabled for your output device.\n" +
                    "The Windows audio service was restarted — MicGain is ready to use.",
                    "MicGain", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowMainWindow(detectionService.Detect().ConfigPath!, detectionService, deviceService, fileSystem);
                return;

            case InstallOutcome.SucceededPendingRestart:
                DebugLog.WriteLine("Install outcome branch: SucceededPendingRestart — pending reboot, main window.");
                stateMachine.CompleteInstall();
                MessageBox.Show(
                    "Equalizer APO is installed and enabled for your output device.\n" +
                    "Because the audio-service restart was declined, it becomes active after " +
                    "the Windows audio service restarts or after the next reboot.",
                    "MicGain", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowMainWindow(detectionService.Detect().ConfigPath!, detectionService, deviceService, fileSystem);
                return;

            case InstallOutcome.ConsentDeclined:
                DebugLog.WriteLine("Install outcome branch: ConsentDeclined.");
                stateMachine.FailInstall();
                MessageBox.Show(
                    "Installation was cancelled: consent was not given for a required step " +
                    "(or the administrator prompt was cancelled).\n" +
                    "No further changes were made to your system.",
                    "MicGain", MessageBoxButton.OK, MessageBoxImage.Information);
                break;

            case InstallOutcome.InstallerNotFound:
                DebugLog.WriteLine($"Install outcome branch: InstallerNotFound — path={installerPath}");
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
                DebugLog.WriteLine("Install outcome branch: DeviceNotEnabled.");
                stateMachine.FailInstall();
                MessageBox.Show(
                    "Equalizer APO is not enabled for your output device — it may not have " +
                    "been selected in the Configurator.\n\n" +
                    "Run the Configurator manually (Configurator.exe in the Equalizer APO " +
                    "install folder), select your device, then start MicGain again.",
                    "MicGain", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;

            case InstallOutcome.FailedRolledBack:
                DebugLog.WriteLine("Install outcome branch: FailedRolledBack.");
                stateMachine.FailInstall();
                MessageBox.Show(
                    "A registry write failed during installation. The changes MicGain made " +
                    "were rolled back automatically, so your system is back in its previous " +
                    "state.\nSee docs/rollback.md for how to verify this manually.",
                    "MicGain", MessageBoxButton.OK, MessageBoxImage.Error);
                break;

            default:
                DebugLog.WriteLine($"Install outcome branch: UNEXPECTED '{outcome}'");
                stateMachine.FailInstall();
                MessageBox.Show(
                    $"Unexpected installation result '{outcome}'. No further action was taken.",
                    "MicGain", MessageBoxButton.OK, MessageBoxImage.Error);
                break;
        }

        Shutdown();
    }
}