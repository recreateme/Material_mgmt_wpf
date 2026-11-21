using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Themes;

namespace MaterialMgmt.App.Controls;

/// <summary>
/// 列表页统一头部：标题（可带副标题）+ 搜索框 + 操作按钮一行；筛选条件单独一行。
/// 五个列表页共用，保证间距与字号一致。
/// </summary>
public sealed class PageHeader : StackPanel
{
    private readonly WrapPanel _actions = new() { Orientation = Orientation.Horizontal };
    private WrapPanel? _filters;

    public PageHeader(string title, string? subtitle = null)
    {
        Margin = new Thickness(0, 0, 0, 12);

        var titleRow = new WrapPanel { Orientation = Orientation.Horizontal };
        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 8) };
        var t = UiTheme.PageTitle(title);
        t.Margin = new Thickness(0);
        titleStack.Children.Add(t);
        if (!string.IsNullOrEmpty(subtitle))
        {
            var s = UiTheme.Hint(subtitle);
            s.Margin = new Thickness(0, 2, 0, 0);
            titleStack.Children.Add(s);
        }
        titleRow.Children.Add(titleStack);
        titleRow.Children.Add(_actions);
        Children.Add(titleRow);
    }

    /// <summary>加带占位提示的搜索框（放在按钮前面）。</summary>
    public void AddSearch(TextBox box, string placeholder)
    {
        box.Width = 220;
        box.Margin = new Thickness(0);
        var hint = new TextBlock
        {
            Text = placeholder,
            Foreground = UiTheme.Brush(UiTheme.Muted),
            FontSize = UiTheme.FontCaption,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        void Sync() => hint.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => Sync();
        Sync();

        var host = new Grid { Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(box);
        host.Children.Add(hint);
        _actions.Children.Insert(0, host);
    }

    public T AddAction<T>(T element) where T : FrameworkElement
    {
        var m = element.Margin;
        element.Margin = new Thickness(m.Left, m.Top, Math.Max(m.Right, 8), 8);
        element.VerticalAlignment = VerticalAlignment.Center;
        _actions.Children.Add(element);
        return element;
    }

    /// <summary>在第二行加一个“标签 + 控件”筛选项。</summary>
    public T AddFilter<T>(string label, T input) where T : FrameworkElement
    {
        if (_filters is null)
        {
            _filters = new WrapPanel { Orientation = Orientation.Horizontal };
            Children.Add(_filters);
        }
        var l = UiTheme.FieldLabel(label);
        l.Margin = new Thickness(0, 0, 6, 8);
        l.VerticalAlignment = VerticalAlignment.Center;
        input.Margin = new Thickness(0, 0, 16, 8);
        input.VerticalAlignment = VerticalAlignment.Center;
        _filters.Children.Add(l);
        _filters.Children.Add(input);
        return input;
    }

    /// <summary>在筛选行末尾加按钮（如“清除筛选”）。</summary>
    public Button AddFilterButton(Button button)
    {
        _filters ??= new WrapPanel { Orientation = Orientation.Horizontal };
        if (!Children.Contains(_filters)) Children.Add(_filters);
        button.Margin = new Thickness(0, 0, 0, 8);
        button.VerticalAlignment = VerticalAlignment.Center;
        _filters.Children.Add(button);
        return button;
    }
}
