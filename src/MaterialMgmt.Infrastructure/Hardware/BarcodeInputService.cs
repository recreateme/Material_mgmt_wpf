namespace MaterialMgmt.Infrastructure.Hardware;

/// <summary>
/// HID 扫码枪键盘楔入聚合（对齐 Python BarcodeFilter）。
/// 由 UI 层在按键预览中喂入；快速连输 + Enter 触发。
/// </summary>
public sealed class BarcodeInputService
{
    private readonly List<char> _buf = new();
    private readonly object _lock = new();
    private CancellationTokenSource? _timeoutCts;
    private const int TimeoutMs = 100;
    private const int MinLen = 4;

    public event Action<string>? BarcodeScanned;
    public event Action? ActivityDetected;

    /// <param name="isEnter">是否为回车键</param>
    /// <param name="text">可打印字符（长度 1）</param>
    public void OnKey(bool isEnter, string? text)
    {
        lock (_lock)
        {
            if (isEnter)
            {
                CancelTimeout();
                var code = new string(_buf.ToArray()).Trim();
                _buf.Clear();
                if (code.Length >= MinLen)
                {
                    ActivityDetected?.Invoke();
                    BarcodeScanned?.Invoke(code);
                }
                return;
            }

            if (!string.IsNullOrEmpty(text) && text.Length == 1 && !char.IsWhiteSpace(text[0]))
            {
                _buf.Add(text[0]);
                RestartTimeout();
            }
        }
    }

    public void Simulate(string code)
    {
        ActivityDetected?.Invoke();
        BarcodeScanned?.Invoke(code.Trim());
    }

    private void RestartTimeout()
    {
        CancelTimeout();
        _timeoutCts = new CancellationTokenSource();
        var token = _timeoutCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeoutMs, token);
                lock (_lock) _buf.Clear();
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private void CancelTimeout()
    {
        try { _timeoutCts?.Cancel(); } catch { /* ignore */ }
        _timeoutCts?.Dispose();
        _timeoutCts = null;
    }
}
