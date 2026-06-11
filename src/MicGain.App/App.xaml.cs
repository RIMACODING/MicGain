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
            // T2.1 (issue #6): consent flow + default output device detection.
            // The actual installation is T2.2 and is stubbed here.
            RunInstallConsentFlow(deviceService);
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

    private void RunInstallConsentFlow(IAudioDeviceService deviceService)
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
            // Acceptance criterion 2: clear message, then clean exit with zero system changes.
            MessageBox.Show(viewModel.NoDeviceMessage, "MicGain", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var dialog = new InstallConsentDialog { DataContext = viewModel };
        dialog.ShowDialog();
        viewModel.HandleDialogClosed(); // closing via 'X' counts as a decline

        if (viewModel.State == InstallFlowState.Ready)
        {
            // T2.2 stub (issue #6 out of scope): consent was given, but no install logic exists yet.
            MessageBox.Show(
                "Automatic installation is not implemented yet (planned as T2.2).\n" +
                "Please install Equalizer APO manually, then start MicGain again.\n" +
                "No changes were made to your system.",
                "MicGain", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Declined (acceptance criterion 1) or stubbed accept: clean exit, zero system changes.
        Shutdown();
    }
}