using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MaterialMgmt.App.Themes;

namespace MaterialMgmt.App.Controls;

/// <summary>
/// DataGrid 行拖拽排序：只从拖拽手柄列开始拖，不影响行点击、Shift / Ctrl 多选和勾选框。
/// 拖动时在目标位置画一条插入线，靠近上下边缘自动滚动；松手回调 (行对象, 间隙序号)。
/// </summary>
public static class RowDragReorder
{
    private const string HandleTag = "RowDragHandle";
    private const string DataFormat = "MaterialMgmt.RowDragReorder";
    private const double AutoScrollMargin = 28;

    public static DataGridTemplateColumn HandleColumn()
    {
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetValue(TextBlock.TextProperty, "⋮⋮");
        factory.SetValue(FrameworkElement.TagProperty, HandleTag);
        factory.SetValue(TextBlock.FontSizeProperty, 15.0);
        factory.SetValue(UIElement.OpacityProperty, 0.6);
        factory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        factory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        factory.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        factory.SetValue(TextBlock.PaddingProperty, new Thickness(0, 6, 0, 6));
        factory.SetValue(TextBlock.BackgroundProperty, Brushes.Transparent);
        factory.SetValue(FrameworkElement.CursorProperty, Cursors.SizeNS);
        factory.SetValue(FrameworkElement.ToolTipProperty, "按住拖动调整顺序");
        return new DataGridTemplateColumn
        {
            Header = "",
            Width = 36,
            CanUserResize = false,
            CanUserSort = false,
            CellTemplate = new DataTemplate { VisualTree = factory }
        };
    }

    public static void Attach(DataGrid grid, Func<bool> canDrag, Action<object, int> onDrop)
    {
        Point? start = null;
        object? dragItem = null;
        InsertionAdorner? adorner = null;

        grid.AllowDrop = true;

        grid.PreviewMouseLeftButtonDown += (_, e) =>
        {
            start = null;
            dragItem = null;
            if (!canDrag()) return;
            if (FindAncestor<FrameworkElement>(e.OriginalSource as DependencyObject, fe => HandleTag.Equals(fe.Tag)) is null)
                return;
            if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject, _ => true) is not { } row)
                return;
            start = e.GetPosition(grid);
            dragItem = row.Item;
            grid.SelectedItem = row.Item;
            row.Focus();
            e.Handled = true;
        };

        grid.PreviewMouseMove += (_, e) =>
        {
            if (start is not { } p || dragItem is null) return;
            if (e.LeftButton != MouseButtonState.Pressed) { start = null; dragItem = null; return; }
            var delta = e.GetPosition(grid) - p;
            if (Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance &&
                Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance)
                return;
            var item = dragItem;
            start = null;
            dragItem = null;
            DragDrop.DoDragDrop(grid, new DataObject(DataFormat, item), DragDropEffects.Move);
            RemoveAdorner(grid, ref adorner);
        };

        grid.PreviewMouseLeftButtonUp += (_, _) => { start = null; dragItem = null; };

        grid.DragOver += (_, e) =>
        {
            e.Handled = true;
            if (!e.Data.GetDataPresent(DataFormat))
            {
                e.Effects = DragDropEffects.None;
                return;
            }
            e.Effects = DragDropEffects.Move;
            AutoScroll(grid, e.GetPosition(grid).Y);
            if (FindGap(grid, e) is not { } gap)
            {
                RemoveAdorner(grid, ref adorner);
                return;
            }
            if (adorner is null && AdornerLayer.GetAdornerLayer(grid) is { } layer)
            {
                adorner = new InsertionAdorner(grid);
                layer.Add(adorner);
            }
            adorner?.MoveTo(gap.LineY);
        };

        grid.DragLeave += (_, e) =>
        {
            var pos = e.GetPosition(grid);
            if (pos.X < 0 || pos.Y < 0 || pos.X >= grid.ActualWidth || pos.Y >= grid.ActualHeight)
                RemoveAdorner(grid, ref adorner);
        };

        grid.Drop += (_, e) =>
        {
            e.Handled = true;
            RemoveAdorner(grid, ref adorner);
            if (e.Data.GetData(DataFormat) is not { } item) return;
            if (FindGap(grid, e) is not { } gap) return;
            onDrop(item, gap.Index);
        };
    }

    private static (int Index, double LineY)? FindGap(DataGrid grid, DragEventArgs e)
    {
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject, _ => true);
        if (row is not null)
        {
            var index = grid.ItemContainerGenerator.IndexFromContainer(row);
            if (index < 0) return null;
            var lower = e.GetPosition(row).Y > row.ActualHeight / 2;
            var y = row.TranslatePoint(new Point(0, lower ? row.ActualHeight : 0), grid).Y;
            return (lower ? index + 1 : index, y);
        }

        // 列表下方空白处：放到最后
        var count = grid.Items.Count;
        if (count == 0) return null;
        if (grid.ItemContainerGenerator.ContainerFromIndex(count - 1) is DataGridRow last)
        {
            var bottom = last.TranslatePoint(new Point(0, last.ActualHeight), grid).Y;
            if (e.GetPosition(grid).Y >= bottom) return (count, bottom);
        }
        return null;
    }

    private static void AutoScroll(DataGrid grid, double y)
    {
        if (FindDescendant<ScrollViewer>(grid) is not { } sv) return;
        if (y < AutoScrollMargin + 32) sv.LineUp();
        else if (y > grid.ActualHeight - AutoScrollMargin) sv.LineDown();
    }

    private static void RemoveAdorner(DataGrid grid, ref InsertionAdorner? adorner)
    {
        if (adorner is null) return;
        AdornerLayer.GetAdornerLayer(grid)?.Remove(adorner);
        adorner = null;
    }

    private static T? FindAncestor<T>(DependencyObject? d, Func<T, bool> match) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T t && match(t)) return t;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    private static T? FindDescendant<T>(DependencyObject d) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var child = VisualTreeHelper.GetChild(d, i);
            if (child is T t) return t;
            if (FindDescendant<T>(child) is { } found) return found;
        }
        return null;
    }

    private sealed class InsertionAdorner : Adorner
    {
        private readonly Pen _pen = new(UiTheme.Brush(UiTheme.Primary), 2);
        private readonly Brush _fill = UiTheme.Brush(UiTheme.Primary);
        private double _y = -1;

        public InsertionAdorner(UIElement adorned) : base(adorned)
        {
            IsHitTestVisible = false;
        }

        public void MoveTo(double y)
        {
            if (Math.Abs(_y - y) < 0.5) return;
            _y = y;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (_y < 0) return;
            var width = AdornedElement.RenderSize.Width;
            dc.DrawLine(_pen, new Point(4, _y), new Point(width - 4, _y));
            dc.DrawEllipse(_fill, null, new Point(6, _y), 4, 4);
        }
    }
}
