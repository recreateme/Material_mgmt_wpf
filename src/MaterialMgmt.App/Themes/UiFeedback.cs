using System.Windows;

namespace MaterialMgmt.App.Themes;

/// <summary>列表页写操作等统一成功提示（弹窗 CRUD 仍静默关窗）。</summary>
public static class UiFeedback
{
    public static void Success(string message, string title = "完成")
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Warn(string message, string title = "提示")
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
}
