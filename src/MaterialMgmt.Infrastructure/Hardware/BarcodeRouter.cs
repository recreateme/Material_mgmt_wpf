using MaterialMgmt.Core.Hardware;

namespace MaterialMgmt.Infrastructure.Hardware;

/// <summary>
/// 扫码分发规则：规范化、过滤过短码、记录来源，然后交给当前页面处理器；
/// 非独占时再广播（主窗筛选 / 弹窗）。走串口通道时忽略 HID 键盘楔入的码。
/// </summary>
public sealed class BarcodeRouter
{
    public const int MinLength = 4;

    /// <summary>当前走串口扫码通道：此时 HID 来的码一律丢弃，避免同一枪码进两次。</summary>
    public bool SerialPathActive { get; set; }

    /// <summary>称量进行中为 true：只交给页面处理器，不广播。</summary>
    public Func<bool>? IsExclusive { get; set; }

    /// <summary>解析当前应接收扫码的处理器（code, simulated）。</summary>
    public Func<Action<string, bool>?>? ResolveHandler { get; set; }

    /// <summary>把分发切到 UI 线程；为 null 时在调用线程直接分发。</summary>
    public Action<Action>? Marshal { get; set; }

    /// <summary>非独占时广播（code, simulated）。</summary>
    public event Action<string, bool>? Broadcast;

    /// <summary>一条码被接受（已更新 Last*），分发之前触发。</summary>
    public event Action? Accepted;

    public string LastCode { get; private set; } = "";
    /// <summary>最近一次扫码来源（<see cref="ScanSources"/>）；分发前已更新，处理器内读取即为本次来源。</summary>
    public string LastSource { get; private set; } = "";
    public DateTime LastAt { get; private set; } = DateTime.MinValue;

    public static string Normalize(string? code) => (code ?? "").Trim();

    /// <summary>接收一条码；被过滤时返回 false。</summary>
    public bool Publish(string? code, string source)
    {
        if (source == ScanSources.Hid && SerialPathActive) return false;
        var normalized = Normalize(code);
        if (normalized.Length < MinLength) return false;

        LastCode = normalized;
        LastSource = source;
        LastAt = DateTime.Now;
        Accepted?.Invoke();

        var simulated = source == ScanSources.Simulated;
        void Dispatch()
        {
            var exclusive = IsExclusive?.Invoke() == true;
            ResolveHandler?.Invoke()?.Invoke(normalized, simulated);
            if (!exclusive) Broadcast?.Invoke(normalized, simulated);
        }

        if (Marshal is null) Dispatch();
        else Marshal(Dispatch);
        return true;
    }
}
