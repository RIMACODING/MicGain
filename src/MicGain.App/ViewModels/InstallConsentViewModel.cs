using MicGain.Core.Models;
using MicGain.Core.Services;

namespace MicGain.App.ViewModels;

/// <summary>
/// ViewModel for the install-consent dialog (T2.1 / <see href="https://gitlab.com/paburukunsempai-group/YesMicGain/-/work_items/3">#3</see>). Detects the default output
/// device via <see cref="IAudioDeviceService"/>, names it in the consent message, and drives
/// the pure <see cref="InstallConsentStateMachine"/> from MicGain.Core.
/// Performs no system changes whatsoever — install logic is T2.2 (AGENTS.md rule 2).
/// </summary>
public sealed class InstallConsentViewModel : ViewModelBase
{
    private readonly IAudioDeviceService _deviceService;
    private string? _consentMessage;

    public InstallConsentViewModel(IAudioDeviceService deviceService)
    {
        _deviceService = deviceService;
        AcceptCommand = new RelayCommand(Accept, CanRespond);
        DeclineCommand = new RelayCommand(Decline, CanRespond);
    }

    public InstallConsentStateMachine StateMachine { get; } = new();

    public InstallFlowState State => StateMachine.State;

    public AudioDeviceInfo? DefaultOutputDevice => StateMachine.DefaultOutputDevice;

    /// <summary>Consent text shown in the dialog; names the actual default output device.</summary>
    public string? ConsentMessage
    {
        get => _consentMessage;
        private set => SetProperty(ref _consentMessage, value);
    }

    /// <summary>Shown when no active output device exists (acceptance criterion 2).</summary>
    public string NoDeviceMessage =>
        "No active output device was found.\n" +
        "Connect or enable a speaker or headphone output, then start MicGain again.\n" +
        "No changes were made to your system.";

    public RelayCommand AcceptCommand { get; }

    public RelayCommand DeclineCommand { get; }

    /// <summary>Raised when the dialog should close (after Accept or Decline).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Enumerates devices and either starts the consent step or reports no output device.</summary>
    public void Initialize()
    {
        var renderDevices = _deviceService.GetDevices()
            .Where(d => d.Flow == DeviceFlow.Render)
            .ToList();

        if (renderDevices.Count == 0)
        {
            StateMachine.ReportNoOutputDevice();
            NotifyStateChanged();
            return;
        }

        // Acceptance criterion 3: name the ACTUAL default output device. If no endpoint is
        // flagged as default (rare — e.g. the enumerator could not resolve it), fall back to
        // the first active render device. Disabled-vs-unplugged edge cases are
        // NEEDS-VM-VERIFICATION (#3).
        var defaultDevice = renderDevices.FirstOrDefault(d => d.IsDefaultDevice) ?? renderDevices[0];
        StateMachine.BeginConsent(defaultDevice);
        ConsentMessage =
            "Equalizer APO is not installed.\n\n" +
            "MicGain can install it for your default output device:\n\n" +
            $"        {defaultDevice.FriendlyName}\n\n" +
            "Installing will make the following changes to your system:\n" +
            "  • Install Equalizer APO (GPLv2) into Program Files\n" +
            "  • Register it as an audio processor for the device above\n" +
            "  • Disable the Windows APO signature check system-wide\n" +
            "    (sets DisableProtectedAudioDG=1 — this affects all audio apps)\n" +
            "  • Restart the Windows Audio service\n\n" +
            "Administrator approval is required. Nothing happens without your consent.";
        NotifyStateChanged();
    }

    /// <summary>Closing the dialog without answering counts as a decline (no consent = no changes).</summary>
    public void HandleDialogClosed()
    {
        if (StateMachine.State == InstallFlowState.AwaitingConsent)
        {
            StateMachine.Decline();
            NotifyStateChanged();
        }
    }

    private bool CanRespond() => StateMachine.State == InstallFlowState.AwaitingConsent;

    private void Accept()
    {
        StateMachine.Accept(); // → Ready (stub; T2.2 inserts Installing + the actual install)
        NotifyStateChanged();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Decline()
    {
        StateMachine.Decline();
        NotifyStateChanged();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(DefaultOutputDevice));
        AcceptCommand.RaiseCanExecuteChanged();
        DeclineCommand.RaiseCanExecuteChanged();
    }
}