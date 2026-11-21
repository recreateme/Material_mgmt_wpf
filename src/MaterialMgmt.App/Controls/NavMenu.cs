using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;

namespace MaterialMgmt.App.Controls;

/// <summary>导航图标（Segoe MDL2 Assets / Segoe Fluent Icons 码位）。</summary>
public static class NavGlyphs
{
    public const string Home = "\uE80F";
    public const string StartWeigh = "\uE768";
    public const string MyOrders = "\uE8FD";
    public const string Materials = "\uE7B8";
    public const string Formulas = "\uE9D5";
    public const string History = "\uE81C";
    public const string Users = "\uE716";
    public const string Settings = "\uE713";
}

/// <summary>
/// 导航项。<see cref="Page"/> 为页面跳转；<see cref="Invoke"/> 为动作（如打开称量窗），点击后高亮留在原页。
/// </summary>
public sealed record NavMenuItem(string Title, string Glyph, AppPage? Page = null, Action? Invoke = null, string? Group = null);

/// <summary>左侧导航（主窗与称量窗共用）：Segoe 图标 + 可选分组标题。</summary>
public sealed class NavMenu : ListBox
{
    /// <summary>Win11 优先用 Segoe Fluent Icons，Win10 回退 Segoe MDL2 Assets（两者码位一致）。</summary>
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private bool _syncing;
    private AppPage? _current;

    /// <summary>用户点击页面项。处理方拒绝跳转时调用 <see cref="Highlight"/> 恢复原高亮即可。</summary>
    public event Action<AppPage>? PageRequested;

    public NavMenu()
    {
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Foreground = UiTheme.Brush(UiTheme.NavText);
        ScrollViewer.SetHorizontalScrollBarVisibility(this, ScrollBarVisibility.Disabled);
        SelectionChanged += OnSelectionChanged;
    }

    public void SetItems(IEnumerable<NavMenuItem> items)
    {
        _syncing = true;
        try
        {
            Items.Clear();
            string? group = null;
            foreach (var item in items)
            {
                if (item.Group is not null && item.Group != group)
                {
                    group = item.Group;
                    Items.Add(GroupHeader(group, first: Items.Count == 0));
                }
                Items.Add(Entry(item));
            }
        }
        finally { _syncing = false; }
    }

    /// <summary>高亮页面对应的导航项，不触发跳转。</summary>
    public void Highlight(AppPage page)
    {
        _current = page;
        _syncing = true;
        try
        {
            SelectedItem = Items.OfType<ListBoxItem>()
                .FirstOrDefault(x => x.Tag is NavMenuItem { Page: { } p } && p == page);
        }
        finally { _syncing = false; }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        if (SelectedItem is not ListBoxItem { Tag: NavMenuItem item }) return;
        if (item.Invoke is not null)
        {
            if (_current is { } cur) Highlight(cur);
            item.Invoke();
            return;
        }
        if (item.Page is { } page) PageRequested?.Invoke(page);
    }

    private ListBoxItem Entry(NavMenuItem item)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = item.Glyph,
            FontFamily = IconFont,
            FontSize = 15,
            Width = 22,
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(new TextBlock
        {
            Text = item.Title,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        var lbi = new ListBoxItem { Content = row, Tag = item };
        if (TryFindResource("NavListBoxItemStyle") is Style style) lbi.Style = style;
        if (item.Invoke is not null) lbi.ToolTip = "在称量窗中打开";
        return lbi;
    }

    private static ListBoxItem GroupHeader(string title, bool first) => new()
    {
        Content = title,
        IsEnabled = false,
        Focusable = false,
        IsHitTestVisible = false,
        Foreground = UiTheme.Brush(UiTheme.NavText),
        Opacity = 0.6,
        FontSize = UiTheme.FontCaption,
        Padding = new Thickness(20, first ? 4 : 14, 0, 4),
        Template = HeaderTemplate
    };

    private static readonly ControlTemplate HeaderTemplate = BuildHeaderTemplate();

    private static ControlTemplate BuildHeaderTemplate()
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding(nameof(Padding))
        {
            RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
        });
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border };
    }
}
