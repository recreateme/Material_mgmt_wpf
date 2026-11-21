using System.Windows;
using MaterialMgmt.App.Controls;

namespace MaterialMgmt.App.Themes;

/// <summary>统一反馈：成功走非模态 Toast（不打断操作）；需要用户注意的警告仍用模态框。</summary>
public static class UiFeedback
{
    /// <summary>增删改成功等轻提示。<paramref name="title"/> 仅为兼容旧调用，Toast 不显示标题。</summary>
    public static void Success(string message, string title = "完成")
        => Toast.Success(message);

    public static void Warn(string message, string title = "提示")
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
}
