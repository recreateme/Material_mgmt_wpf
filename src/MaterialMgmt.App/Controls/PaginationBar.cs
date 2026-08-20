using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialMgmt.App.Themes;

namespace MaterialMgmt.App.Controls;

/// <summary>通用分页栏（对齐 Python table_kit.PaginationBar）。</summary>
public sealed class PaginationBar : UserControl
{
    private readonly Button _prev;
    private readonly Button _next;
    private readonly TextBlock _label = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(12, 0, 0, 0),
        Foreground = UiTheme.Brush(UiTheme.Muted),
        FontSize = 12
    };

    private int _page = 1;
    private int _total;
    private readonly int _pageSize;

    public event Action? PageChanged;

    public PaginationBar(int pageSize = 10)
    {
        _pageSize = pageSize;
        _prev = UiTheme.GhostButton("上一页");
        _next = UiTheme.GhostButton("下一页");
        _prev.Margin = new Thickness(0);
        _next.Margin = new Thickness(6, 0, 0, 0);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0)
        };
        _prev.Click += (_, _) =>
        {
            if (_page > 1)
            {
                _page--;
                UpdateUi();
                PageChanged?.Invoke();
            }
        };
        _next.Click += (_, _) =>
        {
            if (_page < PageCount)
            {
                _page++;
                UpdateUi();
                PageChanged?.Invoke();
            }
        };
        row.Children.Add(_prev);
        row.Children.Add(_next);
        row.Children.Add(_label);
        Content = row;
        UpdateUi();
    }

    public int CurrentPage => _page;
    public int PageSize => _pageSize;
    public int PageCount => _total <= 0 ? 1 : (_total + _pageSize - 1) / _pageSize;

    public void SetTotal(int total)
    {
        _total = Math.Max(0, total);
        var last = Math.Max(1, PageCount);
        if (_page > last) _page = last;
        UpdateUi();
    }

    public void Reset()
    {
        _page = 1;
        UpdateUi();
    }

    public (int Start, int End) Slice()
    {
        var start = (_page - 1) * _pageSize;
        var end = Math.Min(start + _pageSize, _total);
        return (start, end);
    }

    public IEnumerable<T> Apply<T>(IReadOnlyList<T> source)
    {
        SetTotal(source.Count);
        var (start, end) = Slice();
        if (start >= end) return Array.Empty<T>();
        return source.Skip(start).Take(end - start);
    }

    private void UpdateUi()
    {
        _label.Text = $"第 {_page} / {PageCount} 页  ·  共 {_total} 条  ·  每页 {_pageSize} 条";
        _prev.IsEnabled = _page > 1;
        _next.IsEnabled = _page < PageCount;
    }
}
