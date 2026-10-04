namespace KidaGoSync.Connection;

/// <summary>An MTP device as the OS reports it. <see cref="Id"/> is stable while the device stays plugged in.</summary>
public sealed record PdaDevice(string Id, string Name);

/// <summary>Where the monitor learns which MTP devices are present. The OS-level boundary (Project Architecture).</summary>
public interface IMtpDeviceSource
{
    IReadOnlyList<PdaDevice> ListDevices();
}

public enum ConnectionState
{
    Disconnected,
    /// <summary>Several MTP devices are present and none is chosen yet; the actions stay unavailable (C15).</summary>
    ChoosingDevice,
    Connected,
}

/// <summary>What the panel shows: the state, the devices to choose from, and the device in use (only when Connected).</summary>
public sealed record ConnectionStatus(ConnectionState State, IReadOnlyList<PdaDevice> Devices, PdaDevice? Selected)
{
    public static readonly ConnectionStatus Disconnected = new(ConnectionState.Disconnected, [], null);
}

/// <summary>
/// Detects the PDA's connection state by polling (FEAT-005 TD, ConnectionMonitor). One MTP device is used directly;
/// with several the user picks one via <see cref="Select"/>, and Connected is reported only once a device is selected
/// (C15). A selection is kept while that device stays present and dropped when it goes away (C1). Not thread-safe:
/// call <see cref="Poll"/> and <see cref="Select"/> from one thread, and expect <see cref="Changed"/> on that thread.
/// </summary>
public sealed class ConnectionMonitor
{
    private readonly IMtpDeviceSource _source;
    private PdaDevice? _chosen;

    public ConnectionMonitor(IMtpDeviceSource source) => _source = source;

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

    /// <summary>Raised whenever <see cref="Status"/> changes.</summary>
    public event Action<ConnectionStatus>? Changed;

    /// <summary>One detection opportunity.</summary>
    public void Poll() => Update(ReadDevices());

    /// <summary>The user chose which of the listed devices is the PDA. An unknown id is ignored.</summary>
    public void Select(string deviceId)
    {
        var device = Status.Devices.FirstOrDefault(d => d.Id == deviceId);
        if (device is null) return;
        _chosen = device;
        Update(Status.Devices);
    }

    /// <summary>Polls every <paramref name="interval"/> until cancelled. The exact interval is not contractual.</summary>
    public async Task RunAsync(TimeSpan interval, CancellationToken ct)
    {
        Poll();
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct)) Poll();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private IReadOnlyList<PdaDevice> ReadDevices()
    {
        try
        {
            return _source.ListDevices();
        }
        catch (Exception)
        {
            return []; // the OS could not be asked: no device can be used right now, the next poll tries again
        }
    }

    private void Update(IReadOnlyList<PdaDevice> devices)
    {
        if (_chosen is not null && !devices.Any(d => d.Id == _chosen.Id)) _chosen = null;
        // A device used directly counts as chosen: a second device appearing later does not take the PDA away mid-use.
        if (devices.Count == 1) _chosen = devices[0];

        var next = devices.Count switch
        {
            0 => ConnectionStatus.Disconnected,
            1 => new ConnectionStatus(ConnectionState.Connected, devices, devices[0]), // used without asking
            _ when _chosen is not null => new ConnectionStatus(ConnectionState.Connected, devices, devices.First(d => d.Id == _chosen.Id)),
            _ => new ConnectionStatus(ConnectionState.ChoosingDevice, devices, null),
        };

        if (SameAs(next, Status)) return;
        Status = next;
        Changed?.Invoke(next);
    }

    private static bool SameAs(ConnectionStatus a, ConnectionStatus b) =>
        a.State == b.State && a.Selected == b.Selected && a.Devices.SequenceEqual(b.Devices);
}
