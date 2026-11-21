using System.Windows;

namespace MaterialMgmt.App.Themes;

/// <summary>控件附加状态：供样式触发器使用（如输入框错误红框）。</summary>
public static class UiState
{
    public static readonly DependencyProperty HasErrorProperty =
        DependencyProperty.RegisterAttached("HasError", typeof(bool), typeof(UiState), new PropertyMetadata(false));

    public static bool GetHasError(DependencyObject obj) => (bool)obj.GetValue(HasErrorProperty);

    public static void SetHasError(DependencyObject obj, bool value) => obj.SetValue(HasErrorProperty, value);
}
