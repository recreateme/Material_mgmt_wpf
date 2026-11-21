using MaterialMgmt.Core.Hardware;
using MaterialMgmt.Infrastructure.Hardware;

namespace MaterialMgmt.Tests.Hardware;

/// <summary>扫码分发：用真实 HID 聚合器模拟扫码枪键入，用直接 Publish 模拟串口扫码枪。</summary>
public class BarcodeRouterTests
{
    private readonly BarcodeRouter _router = new();
    private readonly List<(string Code, bool Simulated, string Source)> _page = new();
    private readonly List<string> _broadcast = new();
    private bool _exclusive;

    public BarcodeRouterTests()
    {
        _router.ResolveHandler = () => (code, sim) => _page.Add((code, sim, _router.LastSource));
        _router.IsExclusive = () => _exclusive;
        _router.Broadcast += (code, _) => _broadcast.Add(code);
    }

    /// <summary>假扫码枪（HID 键盘楔入）：逐字符键入后回车。</summary>
    private static void Type(BarcodeInputService hid, string text)
    {
        foreach (var ch in text) hid.OnKey(false, ch.ToString());
        hid.OnKey(true, null);
    }

    [Fact]
    public void HidScan_ReachesPage_AndBroadcast()
    {
        var hid = new BarcodeInputService();
        hid.BarcodeScanned += c => _router.Publish(c, ScanSources.Hid);
        Type(hid, "MAT-000001");

        Assert.Equal(new[] { ("MAT-000001", false, ScanSources.Hid) }, _page);
        Assert.Equal(new[] { "MAT-000001" }, _broadcast);
        Assert.Equal(ScanSources.Hid, _router.LastSource);
    }

    [Fact]
    public void ShortCode_IsDropped()
    {
        Assert.False(_router.Publish(" ab ", ScanSources.Serial));
        Assert.Empty(_page);
        Assert.Equal("", _router.LastCode);
    }

    [Fact]
    public void Code_IsTrimmed()
    {
        _router.Publish("  FML-000002 \r\n", ScanSources.Serial);
        Assert.Equal("FML-000002", _page.Single().Code);
        Assert.Equal("FML-000002", _router.LastCode);
    }

    [Fact]
    public void Exclusive_DeliversToPageOnly()
    {
        _exclusive = true;
        _router.Publish("MAT-000001", ScanSources.Serial);
        Assert.Single(_page);
        Assert.Empty(_broadcast);
    }

    [Fact]
    public void SerialPath_IgnoresHid_ButAcceptsSerial()
    {
        _router.SerialPathActive = true;
        Assert.False(_router.Publish("MAT-000001", ScanSources.Hid));
        Assert.True(_router.Publish("MAT-000002", ScanSources.Serial));
        Assert.Equal(new[] { ("MAT-000002", false, ScanSources.Serial) }, _page);
    }

    [Fact]
    public void Simulated_IsFlagged()
    {
        _router.Publish("MAT-000001", ScanSources.Simulated);
        Assert.True(_page.Single().Simulated);
        Assert.Equal(ScanSources.Simulated, _page.Single().Source);
    }

    [Fact]
    public void Dispatch_GoesThroughMarshal_AfterAccepted()
    {
        var log = new List<string>();
        _router.Accepted += () => log.Add("accepted");
        _router.Marshal = d => { log.Add("marshal"); d(); };
        _router.ResolveHandler = () => (_, _) => log.Add("page");

        _router.Publish("MAT-000001", ScanSources.Serial);
        Assert.Equal(new[] { "accepted", "marshal", "page" }, log);
    }

    [Fact]
    public void NoHandler_StillBroadcasts()
    {
        _router.ResolveHandler = () => null;
        _router.Publish("MAT-000001", ScanSources.Serial);
        Assert.Equal(new[] { "MAT-000001" }, _broadcast);
    }
}
