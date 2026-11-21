using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialMgmt.App.Themes;

namespace MaterialMgmt.App.Views.Dialogs;

/// <summary>
/// 对话框基类：自动设置 Owner、居中；确认按钮为默认按钮（Enter 确认）、取消按钮为取消按钮（Esc 关闭）；
/// 统一内边距与底部按钮区；提供“字段 + 行内错误”辅助，校验失败时错误显示在字段旁而不是弹模态框。
/// 子类在构造函数里用 <see cref="AddField"/> / <see cref="Body"/> 搭表单，重写 <see cref="OnConfirm"/> 处理确认。
/// </summary>
public abstract class DialogWindow : Window
{
    private readonly Dictionary<FrameworkElement, TextBlock> _errors = new();
    private readonly StackPanel _footer;

    protected StackPanel Body { get; } = new() { Margin = new Thickness(24, 20, 24, 8) };
    protected Button ConfirmButton { get; }
    protected Button? CancelButton { get; }

    protected DialogWindow(string title, double width, string confirmText = "保存", string? cancelText = "取消")
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        WindowSizing.CapToWorkArea(this);
        Owner = FindOwner();

        ConfirmButton = UiTheme.PrimaryButton(confirmText);
        ConfirmButton.Margin = new Thickness(0);
        ConfirmButton.IsDefault = true;
        ConfirmButton.Click += (_, _) => OnConfirm();

        var buttons = new List<Button>();
        if (cancelText is not null)
        {
            CancelButton = UiTheme.GhostButton(cancelText);
            CancelButton.Margin = new Thickness(0, 0, 8, 0);
            CancelButton.IsCancel = true;
            buttons.Add(CancelButton);
        }
        buttons.Add(ConfirmButton);

        _footer = UiTheme.DialogFooterButtons(buttons.ToArray());
        Content = UiTheme.DialogBody(Body, _footer);
        Loaded += (_, _) => FocusFirstInput();
    }

    /// <summary>在确认按钮左侧再加一个按钮（如“保存并继续添加”“仅禁用”）。</summary>
    protected Button AddFooterButton(Button button)
    {
        button.Margin = new Thickness(0, 0, 8, 0);
        _footer.Children.Insert(_footer.Children.IndexOf(ConfirmButton), button);
        return button;
    }

    /// <summary>内容较多的对话框（如工单详情）改为可调整大小的固定尺寸窗口。</summary>
    protected void MakeResizable(double height, double minWidth, double minHeight)
    {
        SizeToContent = SizeToContent.Manual;
        ResizeMode = ResizeMode.CanResize;
        WindowSizing.Fit(this, Width, height, minWidth, minHeight);
    }

    /// <summary>以对话框结果关闭但不经过 <see cref="OnConfirm"/>（第三个按钮用）。</summary>
    protected void CloseWith(bool? result)
    {
        DialogResult = result;
        Close();
    }

    /// <summary>用户点击确认（或按 Enter）。校验失败调用 <see cref="ShowError"/>；成功调用 <see cref="Accept"/>。</summary>
    protected abstract void OnConfirm();

    /// <summary>加一个“标签 + 输入控件 + 行内错误”字段，返回输入控件本身。</summary>
    protected T AddField<T>(string label, T input, string? hint = null) where T : FrameworkElement
    {
        var group = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        group.Children.Add(UiTheme.FieldLabel(label));
        input.Margin = new Thickness(0);
        group.Children.Add(input);

        var error = new TextBlock
        {
            FontSize = UiTheme.FontCaption,
            Foreground = UiTheme.Brush(UiTheme.Danger),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed
        };
        group.Children.Add(error);
        if (!string.IsNullOrEmpty(hint))
        {
            var h = UiTheme.Hint(hint);
            h.Margin = new Thickness(0, 4, 0, 0);
            group.Children.Add(h);
        }

        _errors[input] = error;
        ClearErrorOnEdit(input);
        Body.Children.Add(group);
        return input;
    }

    /// <summary>在字段旁显示错误并聚焦该字段。</summary>
    protected void ShowError(FrameworkElement field, string message)
    {
        ClearErrors();
        if (_errors.TryGetValue(field, out var tb))
        {
            tb.Text = message;
            tb.Visibility = Visibility.Visible;
        }
        UiState.SetHasError(field, true);
        field.Focus();
    }

    protected void ClearErrors()
    {
        foreach (var (field, tb) in _errors)
        {
            tb.Visibility = Visibility.Collapsed;
            UiState.SetHasError(field, false);
        }
    }

    protected void Accept()
    {
        DialogResult = true;
        Close();
    }

    private void ClearErrorOnEdit(FrameworkElement input)
    {
        switch (input)
        {
            case TextBox t: t.TextChanged += (_, _) => ClearError(t); break;
            case PasswordBox p: p.PasswordChanged += (_, _) => ClearError(p); break;
            case ComboBox c: c.SelectionChanged += (_, _) => ClearError(c); break;
        }
    }

    private void ClearError(FrameworkElement field)
    {
        if (!_errors.TryGetValue(field, out var tb)) return;
        tb.Visibility = Visibility.Collapsed;
        UiState.SetHasError(field, false);
    }

    private void FocusFirstInput()
    {
        foreach (var field in _errors.Keys)
        {
            if (field.IsEnabled && field.IsVisible && field.Focusable)
            {
                field.Focus();
                return;
            }
        }
    }

    private Window? FindOwner()
    {
        var windows = Application.Current?.Windows.OfType<Window>()
            .Where(w => w != this && w.IsVisible && w is not Controls.ToastWindow)
            .ToList() ?? new List<Window>();
        var active = windows.FirstOrDefault(w => w.IsActive);
        if (active is not null) return active;
        var main = Application.Current?.MainWindow;
        if (main is { IsVisible: true } && main != this) return main;
        return windows.FirstOrDefault();
    }
}
