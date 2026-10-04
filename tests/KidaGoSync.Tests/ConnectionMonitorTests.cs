using KidaGoSync.Connection;

namespace KidaGoSync.Tests;

public class ConnectionMonitorTests
{
    private sealed class FakeSource : IMtpDeviceSource
    {
        public List<PdaDevice> Devices { get; } = [];
        public bool Throws { get; set; }
        public IReadOnlyList<PdaDevice> ListDevices() => Throws ? throw new InvalidOperationException() : Devices.ToList();
    }

    private static readonly PdaDevice Pda = new("pda", "Pixel");
    private static readonly PdaDevice Phone = new("phone", "Other phone");

    private readonly FakeSource _source = new();
    private readonly List<ConnectionStatus> _changes = [];

    private ConnectionMonitor Monitor()
    {
        var m = new ConnectionMonitor(_source);
        m.Changed += _changes.Add;
        return m;
    }

    [Fact]
    public void StartsDisconnectedAndStaysSoWithNoDevice()
    {
        var m = Monitor();
        m.Poll();
        Assert.Equal(ConnectionState.Disconnected, m.Status.State);
        Assert.Empty(_changes);
    }

    [Fact]
    public void ConnectAndDisconnectAreReflected()
    {
        var m = Monitor();
        _source.Devices.Add(Pda);
        m.Poll();
        Assert.Equal(ConnectionState.Connected, m.Status.State);
        _source.Devices.Clear();
        m.Poll();
        Assert.Equal(ConnectionState.Disconnected, m.Status.State);
        Assert.Equal([ConnectionState.Connected, ConnectionState.Disconnected], _changes.Select(c => c.State));
    }

    [Fact]
    public void OneDeviceIsUsedWithoutAsking()
    {
        var m = Monitor();
        _source.Devices.Add(Pda);
        m.Poll();
        Assert.Equal(Pda, m.Status.Selected);
    }

    [Fact]
    public void SeveralDevicesWaitForTheUserAndOnlyThenReportConnected()
    {
        var m = Monitor();
        _source.Devices.AddRange([Pda, Phone]);
        m.Poll();
        Assert.Equal(ConnectionState.ChoosingDevice, m.Status.State);
        Assert.Null(m.Status.Selected);
        Assert.Equal([Pda, Phone], m.Status.Devices);

        m.Select("phone");
        Assert.Equal(ConnectionState.Connected, m.Status.State);
        Assert.Equal(Phone, m.Status.Selected);
    }

    [Fact]
    public void AnUnknownSelectionIsIgnored()
    {
        var m = Monitor();
        _source.Devices.AddRange([Pda, Phone]);
        m.Poll();
        m.Select("nope");
        Assert.Equal(ConnectionState.ChoosingDevice, m.Status.State);
    }

    [Fact]
    public void TheChoiceSurvivesPollsAndIsDroppedWhenThatDeviceLeaves()
    {
        var m = Monitor();
        _source.Devices.AddRange([Pda, Phone]);
        m.Poll();
        m.Select("pda");
        m.Poll();
        Assert.Equal(Pda, m.Status.Selected);
        var before = _changes.Count;
        m.Poll();
        Assert.Equal(before, _changes.Count); // an unchanged poll raises nothing

        _source.Devices.Remove(Pda); // the chosen one leaves: the other is now the only device
        m.Poll();
        Assert.Equal(Phone, m.Status.Selected);

        _source.Devices.Add(Pda); // back to two devices; the old choice must not be resurrected
        m.Poll();
        Assert.Equal(ConnectionState.Connected, m.Status.State); // Phone stays in use, it never left
        Assert.Equal(Phone, m.Status.Selected);
    }

    [Fact]
    public void LosingTheChosenDeviceAmongSeveralAsksAgain()
    {
        var m = Monitor();
        var third = new PdaDevice("third", "Third");
        _source.Devices.AddRange([Pda, Phone, third]);
        m.Poll();
        m.Select("pda");
        _source.Devices.Remove(Pda);
        m.Poll();
        Assert.Equal(ConnectionState.ChoosingDevice, m.Status.State);
        Assert.Null(m.Status.Selected);
    }

    [Fact]
    public void AFailingOsQueryCountsAsDisconnectedAndRecovers()
    {
        var m = Monitor();
        _source.Devices.Add(Pda);
        m.Poll();
        _source.Throws = true;
        m.Poll();
        Assert.Equal(ConnectionState.Disconnected, m.Status.State);
        _source.Throws = false;
        m.Poll();
        Assert.Equal(ConnectionState.Connected, m.Status.State);
    }

    [Fact]
    public async Task RunAsyncPollsUntilCancelled()
    {
        var m = Monitor();
        using var cts = new CancellationTokenSource();
        var run = m.RunAsync(TimeSpan.FromMilliseconds(10), cts.Token);
        _source.Devices.Add(Pda);
        await Task.Delay(200);
        Assert.Equal(ConnectionState.Connected, m.Status.State);
        cts.Cancel();
        await run;
    }
}
