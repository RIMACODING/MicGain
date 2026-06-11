using MicGain.Core.Models;
using MicGain.Core.Services;

namespace MicGain.App.ViewModels;

/// <summary>
/// One capture-device row: friendly name, endpoint GUID and a gain slider bound to the
/// allowable Preamp range defined in MicGain.Core (<see cref="GainRange"/>).
///
/// Behavior (issue #4 / MAIN PLAN T1.3):
/// <list type="bullet">
/// <item>User slider changes are debounced (~150 ms) and persisted via
/// <see cref="IApoConfigService.WriteGain"/> — the service guarantees writes stay inside the
/// <c># BEGIN micgain</c>/<c># END micgain</c> marker region.</item>
/// <item>Writes are serialized through a lock shared across all devices, because WriteGain
/// may also rewrite the shared <c>config.txt</c> (no concurrent writes).</item>
/// <item>Failures surface through a non-blocking error callback; the app never crashes.</item>
/// <item>Loading the stored value never triggers a write — the app only writes config after
/// user slider interaction.</item>
/// </list>
/// </summary>
public sealed class DeviceGainViewModel : ViewModelBase
{
    /// <summary>MAIN PLAN T1.3 suggests ~150 ms; final tuning is NEEDS-VM-VERIFICATION.</summary>
    public static readonly TimeSpan DefaultDebounceInterval = TimeSpan.FromMilliseconds(150);

    private readonly IApoConfigService _configService;
    private readonly SemaphoreSlim _writeLock; // shared app-wide; owned by MainViewModel
    private readonly Action<string> _reportError;
    private readonly TimeSpan _debounceInterval;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    private CancellationTokenSource? _pendingDebounce;
    private double _gainDb;
    private bool _isWriting;

    /// <param name="writeLock">Shared write lock serializing all config writes app-wide.</param>
    /// <param name="reportError">Non-blocking error sink (status line on the main window).</param>
    /// <param name="initialGainDb">Stored gain from the config files; assigning it never triggers a write.</param>
    /// <param name="debounceInterval">Override for tests; defaults to <see cref="DefaultDebounceInterval"/>.</param>
    /// <param name="delay">Delay strategy override so tests control the debounce deterministically.</param>
    public DeviceGainViewModel(
        AudioDeviceInfo device,
        IApoConfigService configService,
        SemaphoreSlim writeLock,
        Action<string> reportError,
        double initialGainDb,
        TimeSpan? debounceInterval = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        Device = device;
        _configService = configService;
        _writeLock = writeLock;
        _reportError = reportError;
        _debounceInterval = debounceInterval ?? DefaultDebounceInterval;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));

        // Direct field assignment: reflecting stored state must never write config.
        _gainDb = GainRange.Clamp(initialGainDb);
    }

    public AudioDeviceInfo Device { get; }

    public string FriendlyName => Device.FriendlyName;

    public string EndpointGuid => Device.EndpointGuid;

    public bool IsApoEnabled => Device.IsApoEnabled;

    public bool IsDefaultDevice => Device.IsDefaultDevice;

    public double MinimumDb => GainRange.MinDb;

    public double MaximumDb => GainRange.MaxDb;

    /// <summary>Completion of the most recently scheduled write (test/diagnostic hook).</summary>
    public Task PendingWrite { get; private set; } = Task.CompletedTask;

    public bool IsWriting
    {
        get => _isWriting;
        private set => SetProperty(ref _isWriting, value);
    }

    /// <summary>Slider value in dB, clamped to <see cref="GainRange"/>. User changes persist after the debounce interval.</summary>
    public double GainDb
    {
        get => _gainDb;
        set
        {
            var clamped = GainRange.Clamp(value);
            if (SetProperty(ref _gainDb, clamped))
            {
                PendingWrite = WriteAfterDebounceAsync();
            }
        }
    }

    private async Task WriteAfterDebounceAsync()
    {
        // Supersede any not-yet-flushed write for this device (debounce).
        _pendingDebounce?.Cancel();
        var cts = new CancellationTokenSource();
        _pendingDebounce = cts;

        try
        {
            await _delay(_debounceInterval, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // a newer slider value took over
        }

        // Serialize writes app-wide: WriteGain can rewrite the shared config.txt.
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            IsWriting = true;
            var gainDb = GainDb;
            await Task.Run(() => _configService.WriteGain(EndpointGuid, gainDb)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Non-blocking (issue #4): surface in the status line, keep the app alive.
            _reportError($"Could not save gain for '{FriendlyName}': {ex.Message}");
        }
        finally
        {
            IsWriting = false;
            _writeLock.Release();
        }
    }
}
