using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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

        return new DataGridTemplateColumn
        {
            Header = "选择",
            Width = 58,
            CanUserResize = false,
            CanUserSort = false,
            CellTemplate = new DataTemplate { VisualTree = factory }
        };
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
        foreach (var row in grid.Items.OfType<ISelectable>())
            row.IsSelected = selected;
    }

    public static void AddSelectButtons(Panel bar, DataGrid grid)
    {
        var all = UiTheme.GhostButton("全选");
        all.Click += (_, _) => SetAll(grid, true);
        var none = UiTheme.GhostButton("取消全选");
        none.Click += (_, _) => SetAll(grid, false);
        bar.Children.Add(all);
        bar.Children.Add(none);
    }
}
