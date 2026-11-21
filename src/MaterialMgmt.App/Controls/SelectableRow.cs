using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MaterialMgmt.App.Themes;

namespace MaterialMgmt.App.Controls;

public interface ISelectable
{
    bool IsSelected { get; set; }
}

public sealed class SelectableRow<T> : ISelectable, INotifyPropertyChanged
{
    public SelectableRow(T item) => Item = item;

    public T Item { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class GridKit
{
    public static DataGridTemplateColumn CheckColumn()
    {
        var factory = new FrameworkElementFactory(typeof(CheckBox));
        factory.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(ISelectable.IsSelected))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        factory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        factory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.SetValue(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand);

        var header = new CheckBox
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "全选 / 取消全选当前页"
        };
        header.Click += (_, _) =>
        {
            if (FindAncestor<DataGrid>(header) is not { } grid) return;
            var rows = grid.Items.OfType<ISelectable>().ToList();
            SetAll(grid, !(rows.Count > 0 && rows.All(r => r.IsSelected)));
        };

        return new DataGridTemplateColumn
        {
            Header = header,
            Width = 58,
            CanUserResize = false,
            CanUserSort = false,
            CellTemplate = new DataTemplate { VisualTree = factory }
        };
    }

    /// <summary>表头勾选框：全选为勾、部分为横线、全不选为空。</summary>
    private static void RefreshHeader(DataGrid grid)
    {
        var header = grid.Columns.Select(c => c.Header).OfType<CheckBox>().FirstOrDefault();
        if (header is null) return;
        var rows = grid.Items.OfType<ISelectable>().ToList();
        var n = rows.Count(r => r.IsSelected);
        header.IsChecked = n == 0 ? false : n == rows.Count ? true : null;
        header.IsEnabled = rows.Count > 0;
    }

    public static Style CenteredTextStyle { get; } = CreateCenteredTextStyle();

    private static Style CreateCenteredTextStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
        return style;
    }

    public static DataGridTextColumn Text(string header, string path, DataGridLength width, string? format = null,
        TextAlignment align = TextAlignment.Center)
    {
        var style = align == TextAlignment.Left ? LeftTextStyle : CenteredTextStyle;
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path) { StringFormat = format },
            Width = width,
            ElementStyle = style
        };
    }

    public static DataGridTextColumn Text(string header, string path, double width, string? format = null,
        TextAlignment align = TextAlignment.Center)
        => Text(header, path, new DataGridLength(width), format, align);

    public static DataGridTemplateColumn StatusColumn(string header, string statusPath, string labelPath, double width = 96)
    {
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(TextBlock.TextProperty, new Binding(labelPath));
        factory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        factory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        factory.SetValue(TextBlock.FontSizeProperty, 12.0);
        factory.SetBinding(TextBlock.ForegroundProperty, new Binding(statusPath)
        {
            Converter = new StatusBrushConverter()
        });
        return new DataGridTemplateColumn
        {
            Header = header,
            Width = width,
            CellTemplate = new DataTemplate { VisualTree = factory }
        };
    }

    private static Style LeftTextStyle { get; } = CreateTextStyle(TextAlignment.Left, HorizontalAlignment.Left);

    private static Style CreateTextStyle(TextAlignment textAlign, HorizontalAlignment align)
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, align));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, textAlign));
        return style;
    }

    private sealed class StatusBrushConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => UiTheme.Brush(UiTheme.StatusColor(value as string ?? ""));

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }

    public static List<SelectableRow<T>> Wrap<T>(IEnumerable<T> items)
        => items.Select(i => new SelectableRow<T>(i)).ToList();

    public static List<T> CheckedItems<T>(DataGrid grid)
        => grid.Items.OfType<SelectableRow<T>>().Where(x => x.IsSelected).Select(x => x.Item).ToList();

    public static SelectableRow<T>? Current<T>(DataGrid grid)
        => grid.SelectedItem as SelectableRow<T>;

    public static void SetAll(DataGrid grid, bool selected)
    {
        var state = GetLinkState(grid);
        state.Syncing = true;
        try
        {
            foreach (var row in grid.Items.OfType<ISelectable>())
                row.IsSelected = selected;

            grid.SelectedItems.Clear();
            if (selected)
            {
                foreach (var item in grid.Items)
                    grid.SelectedItems.Add(item);
            }
        }
        finally
        {
            state.Syncing = false;
        }

        Notify(grid, state);
    }

    private static void Notify(DataGrid grid, LinkState state)
    {
        RefreshHeader(grid);
        state.OnSelectionChanged?.Invoke();
    }

    /// <summary>切换一行的勾选并同步高亮集，不影响其它已选行（勾选框单击、空格键共用）。</summary>
    private static void ToggleRow(DataGrid grid, LinkState state, object rowObj)
    {
        if (rowObj is not ISelectable sel) return;
        state.Syncing = true;
        try
        {
            sel.IsSelected = !sel.IsSelected;
            if (sel.IsSelected)
            {
                if (!grid.SelectedItems.Contains(rowObj))
                    grid.SelectedItems.Add(rowObj);
            }
            else
            {
                grid.SelectedItems.Remove(rowObj);
            }
        }
        finally
        {
            state.Syncing = false;
        }
        Notify(grid, state);
    }

    /// <summary>
    /// 高亮集 ↔ 勾选集双向同步；勾选框单击只切换当前行（不打乱其它已选）。
    /// </summary>
    public static void EnableLinkedSelection(DataGrid grid, Action? onSelectionChanged = null)
    {
        var state = GetLinkState(grid);
        state.OnSelectionChanged = onSelectionChanged;

        if (state.Hooked) return;
        state.Hooked = true;

        grid.SelectionMode = DataGridSelectionMode.Extended;
        grid.SelectionUnit = DataGridSelectionUnit.FullRow;

        grid.SelectionChanged += (_, _) =>
        {
            if (state.Syncing) return;
            state.Syncing = true;
            try
            {
                var selected = new HashSet<object>(grid.SelectedItems.Cast<object>());
                foreach (var row in grid.Items.OfType<ISelectable>())
                    row.IsSelected = selected.Contains(row);
            }
            finally
            {
                state.Syncing = false;
            }

            Notify(grid, state);
        };

        // 点行内勾选框：自行切换勾选与 SelectedItems，阻止 DataGrid 把它当成「只选这一行」。表头勾选框走它自己的 Click。
        grid.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, e) =>
        {
            if (FindAncestor<CheckBox>(e.OriginalSource as DependencyObject) is not { } cb)
                return;
            if (FindAncestor<DataGridRow>(cb) is null)
                return;
            if (FindRowItem(cb, grid) is not { } rowObj)
                return;

            e.Handled = true;
            cb.Focus();
            ToggleRow(grid, state, rowObj);
        }), handledEventsToo: true);

        // 空格键：切换当前行勾选（否则勾选框自己切换，高亮集不同步）
        grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Space) return;
            if (Keyboard.FocusedElement is DependencyObject focused && FindAncestor<TextBox>(focused) is not null)
                return;
            var rowObj = (Keyboard.FocusedElement as DependencyObject) is { } f && FindAncestor<DataGridRow>(f) is { } r
                ? r.Item
                : grid.CurrentItem ?? grid.SelectedItem;
            if (rowObj is null || rowObj == CollectionView.NewItemPlaceholder) return;
            e.Handled = true;
            ToggleRow(grid, state, rowObj);
        };

        // 翻页 / 刷新换了数据源：表头状态跟着重算
        var itemsSource = System.ComponentModel.DependencyPropertyDescriptor
            .FromProperty(ItemsControl.ItemsSourceProperty, typeof(DataGrid));
        itemsSource.AddValueChanged(grid, (_, _) => RefreshHeader(grid));
    }

    /// <summary>多选时禁用单条入口；恰 1 条时启用。</summary>
    public static void BindSingleActions(DataGrid grid, params UIElement[] singleActions)
    {
        void Update()
        {
            var enable = grid.SelectedItems.Count == 1;
            foreach (var el in singleActions)
                el.IsEnabled = enable;
        }

        EnableLinkedSelection(grid, Update);
        Update();
    }

    public static int SelectedCount(DataGrid grid) => grid.SelectedItems.Count;

    private static T? FindAncestor<T>(DependencyObject? current) where T : class
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static object? FindRowItem(DependencyObject from, DataGrid grid)
    {
        var row = FindAncestor<DataGridRow>(from);
        return row?.Item ?? (from as FrameworkElement)?.DataContext;
    }

    private static readonly DependencyProperty LinkStateProperty =
        DependencyProperty.RegisterAttached(
            "LinkState", typeof(LinkState), typeof(GridKit), new PropertyMetadata(null));

    private static LinkState GetLinkState(DataGrid grid)
    {
        if (grid.GetValue(LinkStateProperty) is LinkState existing)
            return existing;
        var created = new LinkState();
        grid.SetValue(LinkStateProperty, created);
        return created;
    }

    private sealed class LinkState
    {
        public bool Hooked;
        public bool Syncing;
        public Action? OnSelectionChanged;
    }
}
