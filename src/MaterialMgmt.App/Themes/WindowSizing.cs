using System.Windows;

namespace MaterialMgmt.App.Themes;

/// <summary>窗口尺寸自适应：设计尺寸不超过当前工作区（去掉任务栏）的 90%，小屏 / 高缩放下不再被截断。</summary>
public static class WindowSizing
{
    private const double MaxWorkAreaRatio = 0.9;

    /// <summary>按设计尺寸设置初始宽高与最小宽高，均不超过工作区 90%。</summary>
    public static void Fit(Window window, double designWidth, double designHeight, double minWidth, double minHeight)
    {
        var (maxW, maxH) = WorkAreaLimit();
        window.MinWidth = Math.Min(minWidth, maxW);
        window.MinHeight = Math.Min(minHeight, maxH);
        window.Width = Math.Max(window.MinWidth, Math.Min(designWidth, maxW));
        window.Height = Math.Max(window.MinHeight, Math.Min(designHeight, maxH));
    }

    /// <summary>仅限制上限（用于 SizeToContent 的对话框：内容过高时出现滚动条而不是超出屏幕）。</summary>
    public static void CapToWorkArea(Window window)
    {
        var (maxW, maxH) = WorkAreaLimit();
        window.MaxWidth = maxW;
        window.MaxHeight = maxH;
    }

    private static (double Width, double Height) WorkAreaLimit()
    {
        var wa = SystemParameters.WorkArea;
        return (wa.Width * MaxWorkAreaRatio, wa.Height * MaxWorkAreaRatio);
    }
}
