using System.Diagnostics;
using System.IO;
using MaterialMgmt.App.Controls;
using MaterialMgmt.App.Views.Dialogs;

namespace MaterialMgmt.App.Themes;

/// <summary>
/// 统一反馈。成功与“请先选择…”这类轻提示走非模态 Toast，不打断操作；
/// 需要用户读完再继续的警告 / 错误用主题样式的 <see cref="MessageDialog"/>；破坏性操作的确认用 <see cref="ConfirmDialog"/>。
/// </summary>
public static class UiFeedback
{
    /// <summary>增删改成功等轻提示。<paramref name="title"/> 仅为兼容旧调用，Toast 不显示标题。</summary>
    public static void Success(string message, string title = "完成")
        => Toast.Success(message);

    /// <summary>操作前置条件不满足（未选择、选错状态等），提示后用户自行调整即可。</summary>
    public static void Hint(string message)
        => Toast.Warning(message);

    public static void Info(string message, string title = "提示")
        => MessageDialog.Show(message, title, MessageKind.Info);

    public static void Warn(string message, string title = "提示")
        => MessageDialog.Show(message, title, MessageKind.Warning);

    public static void Error(string message, string title = "出错了")
        => MessageDialog.Show(message, title, MessageKind.Error);

    /// <summary>执行导出：成功时 Toast 显示文件名并可一键在资源管理器中定位文件；失败时说明原因。</summary>
    public static void Export(string path, Action export)
    {
        try
        {
            export();
        }
        catch (Exception ex)
        {
            Error($"导出失败：{ex.GetBaseException().Message}\n\n如果该文件正在 Excel 中打开，请先关闭后重试。", "导出失败");
            return;
        }
        Toast.Success($"已导出 {Path.GetFileName(path)}", "打开所在文件夹", () => RevealInExplorer(path));
    }

    private static void RevealInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Warn($"无法打开文件夹：{ex.Message}");
        }
    }
}
