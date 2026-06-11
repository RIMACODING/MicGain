using System.Collections.ObjectModel;
using MicGain.Core.Models;
using MicGain.Core.Services;

namespace MicGain.App.ViewModels;

/// <summary>Top-level UI state of the main window (T1.3 / issue #4).</summary>
public enum AppState
{
    Loading,
    Ready,
    NoCaptureDevices,
    ApoNotInstalled,
    Error,
}

/// <summary>
/// Main window ViewModel: detect Equalizer APO, list capture devices (microphones) with
/// friendly name + GUID, and host one <see cref="DeviceGainViewModel"/> per device.
/// Pure BCL types only, so it is unit-testable with mocked Core services.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly IApoDetectionService _detectionService;
    private readonly IAudioDeviceService _deviceService;
    private readonly Func<string, IApoConfigService> _configServiceFactory;
    private readonly TimeSpan? _debounceInterval;
    private readonly Func<TimeSpan, CancellationToken, Task>? _delay;
    private readonly SemaphoreSlim _writeLock = new(1, 1); // one config write at a time, app-wide

    private AppState _state = AppState.Loading;
    private string? _statusMessage;

    /// <param name="configServiceFactory">
    /// Creates the config service from the detected config directory — the directory is only
    /// known after detection succeeds and is always taken from the registry, never hardcoded.
    /// </param>
    /// <param name="debounceInterval">Override for tests; null uses the production default.</param>
    /// <param name="delay">Delay strategy override for tests; null uses Task.Delay.</param>
    public MainViewModel(
        IApoDetectionService detectionService,
        IAudioDeviceService deviceService,
        Func<string, IApoConfigService> configServiceFactory,
        TimeSpan? debounceInterval = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _detectionService = detectionService;
        _deviceService = deviceService;
        _configServiceFactory = configServiceFactory;
        _debounceInterval = debounceInterval;
        _delay = delay;
        RefreshCommand = new RelayCommand(() => _ = LoadAsync());
    }

    public ObservableCollection<DeviceGainViewModel> Devices { get; } = new();

    public RelayCommand RefreshCommand { get; }

    public AppState State
    {
        get => _state;
        private set => SetProperty(ref _state, value);
    }

    /// <summary>Non-blocking status line; write/read failures land here instead of dialogs.</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public async Task LoadAsync()
    {
        State = AppState.Loading;
        StatusMessage = null;
        Devices.Clear();

        try
        {
            var detection = await Task.Run(_detectionService.Detect);
            if (!detection.IsInstalled)
            {
                State = AppState.ApoNotInstalled;
                return;
            }

            var configService = _configServiceFactory(detection.ConfigPath!);
            var allDevices = await Task.Run(_deviceService.GetDevices);

            // Issue #4 scope: the main window lists capture devices (microphones) only.
            var captureDevices = allDevices.Where(d => d.Flow == DeviceFlow.Capture).ToList();
            if (captureDevices.Count == 0)
            {
                State = AppState.NoCaptureDevices;
                return;
            }

            foreach (var device in captureDevices)
            {
                var storedGain = ReadStoredGain(configService, device);
                Devices.Add(new DeviceGainViewModel(
                    device,
                    configService,
                    _writeLock,
                    ReportError,
                    storedGain ?? GainRange.DefaultDb,
                    _debounceInterval,
                    _delay));
            }

            State = AppState.Ready;
        }
        catch (Exception ex)
        {
            State = AppState.Error;
            StatusMessage = $"Failed to load devices: {ex.Message}";
        }
    }

    private double? ReadStoredGain(IApoConfigService configService, AudioDeviceInfo device)
    {
        try
        {
            // null = device not managed by MicGain yet (lenient reader) → default gain.
            return configService.ReadGain(device.EndpointGuid);
        }
        catch (Exception ex)
        {
            ReportError($"Could not read stored gain for '{device.FriendlyName}': {ex.Message}");
            return null;
        }
    }

    private void ReportError(string message) => StatusMessage = message;
}
