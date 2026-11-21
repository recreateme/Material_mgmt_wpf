using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Lists;

namespace MaterialMgmt.App.Controls;

/// <summary>通用分页栏：上一页 / 下一页、页码跳转、每页条数（10 / 20 / 50，按页面记在 settings.ini 的 [ui] 段）。</summary>
public sealed class PaginationBar : UserControl
{
    private readonly Button _prev;
    private readonly Button _next;
    private readonly TextBox _pageBox = new()
    {
        Width = 52,
        MinHeight = 30,
        Height = 30,
        Padding = new Thickness(6, 0, 6, 0),
        HorizontalContentAlignment = HorizontalAlignment.Center
    };
    private readonly TextBlock _pageCount = Caption("");
    private readonly TextBlock _total = Caption("");
    private readonly ComboBox _size = new() { Width = 72, MinHeight = 30, Height = 30 };
    private readonly IConfigService? _config;
    private readonly string? _key;

    private int _page = 1;
    private int _totalCount;
    private int _pageSize;

    public event Action? PageChanged;

    /// <param name="key">记住每页条数用的页面标识；为 null 时不记。</param>
    public PaginationBar(IConfigService? config = null, string? key = null)
    {
        _config = config;
        _key = key;
        _pageSize = LoadPageSize();

        _prev = UiTheme.GhostButton("上一页");
        _next = UiTheme.GhostButton("下一页");
        _prev.Margin = new Thickness(0);
        _next.Margin = new Thickness(6, 0, 12, 0);
        _prev.Click += (_, _) => GoTo(_page - 1);
        _next.Click += (_, _) => GoTo(_page + 1);

        _pageBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            JumpFromBox();
            e.Handled = true;
        };
        _pageBox.LostKeyboardFocus += (_, _) => JumpFromBox();
        _pageBox.ToolTip = "输入页码后按 Enter 跳转";

        _size.ItemsSource = Paging.PageSizes;
        _size.SelectedItem = _pageSize;
        _size.SelectionChanged += (_, _) =>
        {
            if (_size.SelectedItem is not int s || s == _pageSize) return;
            // 切换条数时尽量让当前页第一条仍可见
            var firstIndex = (_page - 1) * _pageSize;
            _pageSize = s;
            _page = firstIndex / _pageSize + 1;
            SavePageSize();
            UpdateUi();
            PageChanged?.Invoke();
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        row.Children.Add(_prev);
        row.Children.Add(_next);
        row.Children.Add(Caption("第"));
        row.Children.Add(_pageBox);
        row.Children.Add(_pageCount);
        row.Children.Add(_total);
        row.Children.Add(Caption("每页"));
        row.Children.Add(_size);
        row.Children.Add(Caption("条"));
        foreach (var c in row.Children.OfType<FrameworkElement>()) c.VerticalAlignment = VerticalAlignment.Center;
        Content = row;
        UpdateUi();
    }

    public int CurrentPage => _page;
    public int PageSize => _pageSize;
    public int PageCount => Paging.PageCount(_totalCount, _pageSize);

    public void SetTotal(int total)
    {
        _totalCount = Math.Max(0, total);
        _page = Paging.Clamp(_page, _totalCount, _pageSize);
        UpdateUi();
    }

    public void Reset()
    {
        _page = 1;
        UpdateUi();
    }

    public (int Start, int End) Slice() => Paging.Slice(_page, _totalCount, _pageSize);

    public IEnumerable<T> Apply<T>(IReadOnlyList<T> source)
    {
        SetTotal(source.Count);
        var (start, end) = Slice();
        if (start >= end) return Array.Empty<T>();
        return source.Skip(start).Take(end - start);
    }

    private void GoTo(int page)
    {
        var target = Paging.Clamp(page, _totalCount, _pageSize);
        if (target == _page)
        {
            UpdateUi();
            return;
        }
        _page = target;
        UpdateUi();
        PageChanged?.Invoke();
    }

    private void JumpFromBox()
    {
        if (int.TryParse(_pageBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p))
            GoTo(p);
        else
            UpdateUi();
    }

    private void UpdateUi()
    {
        _pageBox.Text = _page.ToString(CultureInfo.InvariantCulture);
        _pageCount.Text = $"/ {PageCount} 页";
        _total.Text = $"·  共 {_totalCount} 条  ·";
        _prev.IsEnabled = _page > 1;
        _next.IsEnabled = _page < PageCount;
    }

    private int LoadPageSize()
    {
        if (_config is null || _key is null) return Paging.DefaultPageSize;
        var raw = _config.GetValue("ui", $"page_size_{_key}", Paging.DefaultPageSize.ToString(CultureInfo.InvariantCulture));
        return Paging.NormalizePageSize(int.TryParse(raw, out var v) ? v : Paging.DefaultPageSize);
    }

    private void SavePageSize()
    {
        if (_config is null || _key is null) return;
        try
        {
            _config.SetValue("ui", $"page_size_{_key}", _pageSize.ToString(CultureInfo.InvariantCulture));
        }
        catch (IOException)
        {
            // 配置文件被占用时不影响本次使用，下次再记
        }
    }

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        Foreground = UiTheme.Brush(UiTheme.Muted),
        FontSize = UiTheme.FontCaption,
        Margin = new Thickness(6, 0, 6, 0)
    };
}
