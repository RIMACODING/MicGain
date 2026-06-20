using System.Windows;
using MicGain.Core.Models;
using MicGain.Core.Services;

namespace MicGain.App.Views;

/// <summary>
/// WPF implementation of <see cref="IInstallInteraction"/> (T2.2, issue #3). Every
/// <see cref="SystemChange"/> is gated by a modal Yes/No MessageBox with 'No' as the
/// default answer, so Enter/Esc can never grant consent accidentally (issue #3 AC2;
/// AGENTS.md rule 2: no silent system mutations). The guided Configurator step is a
/// blocking guidance dialog — the primary path per research-notes §11: the installer's
/// /S flag does NOT suppress the Configurator device selector [VM-VERIFIED].
/// All dialogs are marshalled to the UI dispatcher because <see cref="ApoInstallService"/>
/// awaits with ConfigureAwait(false) and may call back on a thread-pool thread.
/// </summary>
public sealed class WpfInstallInteraction : IInstallInteraction
{
    public Task<bool> ConfirmSystemChangeAsync(SystemChange change, string details)
    {
        var title = change switch
        {
            SystemChange.RunInstaller => "MicGain — Run the Equalizer APO installer?",
            SystemChange.RunConfigurator => "MicGain — Launch the Configurator to enable APO?",
            SystemChange.WriteRegistry => "MicGain — Change audio registry settings?",
            SystemChange.RestartAudioService => "MicGain — Restart the Windows audio service?",
            _ => "MicGain — Approve system change?",
        };

        var result = ShowOnUiThread(
            details, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

        return Task.FromResult(result == MessageBoxResult.Yes);
    }

    public Task WaitForConfiguratorAsync(AudioDeviceInfo device)
    {
        // Blocks until the user confirms they finished the Configurator step. Enablement is
        // then verified by ApoInstallService.IsDeviceEnabled (AC5) — this dialog itself
        // performs no system change. Sequence VM-verified 2025-06-13: Configurator runs its
        // setup workflow first; the "Select your device" window appears at the END.
        var deviceKind = device.Flow == DeviceFlow.Capture ? "input" : "output";
        ShowOnUiThread(
            "The Equalizer APO Configurator is now opening.\n\n" +
            "Configurator will go through its setup workflow first.\n" +
            "AT THE END, a \"Select your device\" window will appear.\n\n" +
            "In that window:\n" +
            $"  1. Tick the checkbox next to your {deviceKind} device:\n\n" +
            $"          {device.FriendlyName}\n\n" +
            "  2. Click OK to confirm your device selection.\n" +
            "  3. Click OK again to close the Configurator.\n\n" +
            "When you have done both, click OK here to continue.",
            "MicGain — Select your device in the Configurator",
            MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK);

        return Task.CompletedTask;
    }

    private static MessageBoxResult ShowOnUiThread(
        string text,
        string caption,
        MessageBoxButton button,
        MessageBoxImage image,
        MessageBoxResult defaultResult)
    {
        var dispatcher = Application.Current?.Dispatcher;
        return dispatcher is null || dispatcher.CheckAccess()
            ? MessageBox.Show(text, caption, button, image, defaultResult)
            : dispatcher.Invoke(() => MessageBox.Show(text, caption, button, image, defaultResult));
    }
}
