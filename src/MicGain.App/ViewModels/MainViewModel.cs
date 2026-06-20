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
/// Main window ViewModel: list capture devices (microphones) with friendly name + GUID
/// and host one <see cref="DeviceGainViewModel"/> per device.
/// The caller (App.xaml.cs) provides the pre-detected config path so detection runs
/// exactly once at startup and the main window never re-detects on initial load.
/// On Refresh, re-detection runs via the detection service if provided.
/// Pure BCL types only, so it is unit-testable with mocked Core services.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly string _configPath;
    private readonly IApoDetectionService? _detectionService;
    private readonly IAudioDeviceService _deviceService;
    private readonly Func<string, IApoConfigService> _configServiceFactory;
    private readonly TimeSpan? _debounceInterval;
    private readonly Func<TimeSpan, CancellationToken, Task>? _delay;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private AppState _state = AppState.Loading;
    private string? _statusMessage;

    /// <param name="configPath">Pre-detected Equalizer APO config directory (from the registry, never hardcoded).</param>
    /// <param name="detectionService">Used only on Refresh to re-verify APO presence. Nullable for tests.</param>
    /// <param name="deviceService">Audio device enumerator.</param>
    /// <param name="configServiceFactory">Creates the config service from the config directory.</param>
    /// <param name="debounceInterval">Override for tests; null uses the production default.</param>
    /// <param name="delay">Delay strategy override for tests; null uses Task.Delay.</param>
    public MainViewModel(
        string configPath,
        IApoDetectionService? detectionService,
        IAudioDeviceService deviceService,
        Func<string, IApoConfigService> configServiceFactory,
        TimeSpan? debounceInterval = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _configPath = configPath;
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
            // On Refresh, re-verify APO is still installed. On initial load, skip — the
            // caller already confirmed APO is installed and provided the config path.
            if (_detectionService is not null)
            {
                var recheck = await Task.Run(_detectionService.Detect);
                if (!recheck.IsInstalled)
                {
                    State = AppState.ApoNotInstalled;
                    return;
                }
            }

            var configService = _configServiceFactory(_configPath);
            var allDevices = await Task.Run(_deviceService.GetDevices);

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