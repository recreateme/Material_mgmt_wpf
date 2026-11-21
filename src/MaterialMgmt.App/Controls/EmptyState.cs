using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Themes;

namespace MaterialMgmt.App.Controls;

/// <summary>
/// 列表空状态：盖在表格中央。列表本身为空时显示说明和主操作（如“新建配方”）；
/// 筛选后无结果时显示“没有匹配的记录”和“清除筛选”。
/// </summary>
public sealed class EmptyState : Border
{
    private readonly string _emptyText;
    private readonly string? _actionText;
    private readonly Action? _action;
    private readonly TextBlock _text = new()
    {
        FontSize = UiTheme.FontBody,
        Foreground = UiTheme.Brush(UiTheme.Muted),
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 12)
    };
    private readonly Button _button = UiTheme.PrimaryButton("");
    private Action? _current;

    /// <summary>筛选无结果时点“清除筛选”执行的动作；为 null 时不显示该按钮。</summary>
    public Action? ClearFilter { get; set; }

    public bool ActionEnabled
    {
        get => _button.IsEnabled;
        set => _button.IsEnabled = value;
    }

    public EmptyState(string emptyText, string? actionText = null, Action? action = null)
    {
        _emptyText = emptyText;
        _actionText = actionText;
        _action = action;

        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(0, 42, 0, 0);
        MaxWidth = 360;
        Visibility = Visibility.Collapsed;

        _button.Margin = new Thickness(0);
        _button.HorizontalAlignment = HorizontalAlignment.Center;
        _button.Click += (_, _) => _current?.Invoke();

        var stack = new StackPanel();
        stack.Children.Add(_text);
        stack.Children.Add(_button);
        Child = stack;
    }

    /// <param name="shown">当前显示的条数。</param>
    /// <param name="filtered">是否有筛选条件生效。</param>
    public void Update(int shown, bool filtered)
    {
        if (shown > 0)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;
        if (filtered)
        {
            _text.Text = "没有匹配的记录";
            SetAction("清除筛选", ClearFilter);
        }
        else
        {
            _text.Text = _emptyText;
            SetAction(_actionText, _action);
        }
    }

    private void SetAction(string? text, Action? action)
    {
        _current = action;
        _button.Content = text;
        _button.Visibility = text is null || action is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>把表格和空状态叠在同一格里。</summary>
    public static Grid Host(UIElement grid, EmptyState empty)
    {
        var host = new Grid();
        host.Children.Add(grid);
        host.Children.Add(empty);
        return host;
    }
}
