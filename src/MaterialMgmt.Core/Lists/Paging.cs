namespace MaterialMgmt.Core.Lists;

/// <summary>分页计算：页码从 1 开始；总数为 0 时视为 1 页。</summary>
public static class Paging
{
    public static readonly IReadOnlyList<int> PageSizes = new[] { 10, 20, 50 };
    public const int DefaultPageSize = 10;

    /// <summary>不在可选列表里的每页条数回退为默认值（配置文件被手改时）。</summary>
    public static int NormalizePageSize(int size) => PageSizes.Contains(size) ? size : DefaultPageSize;

    public static int PageCount(int total, int pageSize)
    {
        if (pageSize <= 0) pageSize = DefaultPageSize;
        return total <= 0 ? 1 : (total + pageSize - 1) / pageSize;
    }

    /// <summary>把页码修正到 [1, 页数]。</summary>
    public static int Clamp(int page, int total, int pageSize)
        => Math.Min(Math.Max(1, page), PageCount(total, pageSize));

    /// <summary>当前页的 [Start, End) 下标。</summary>
    public static (int Start, int End) Slice(int page, int total, int pageSize)
    {
        if (total <= 0) return (0, 0);
        var p = Clamp(page, total, pageSize);
        var start = (p - 1) * pageSize;
        return (start, Math.Min(start + pageSize, total));
    }
}
