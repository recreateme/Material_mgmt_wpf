using MaterialMgmt.Core.Lists;

namespace MaterialMgmt.Tests;

public class PaginationTests
{
    [Fact]
    public void Slice_SecondPage()
    {
        Assert.Equal((10, 20), Paging.Slice(2, 25, 10));
        Assert.Equal(3, Paging.PageCount(25, 10));
    }

    [Fact]
    public void Empty_total_is_one_page_with_empty_slice()
    {
        Assert.Equal(1, Paging.PageCount(0, 10));
        Assert.Equal(1, Paging.Clamp(5, 0, 10));
        Assert.Equal((0, 0), Paging.Slice(1, 0, 10));
    }

    [Fact]
    public void Exact_full_pages()
    {
        Assert.Equal(2, Paging.PageCount(40, 20));
        Assert.Equal((20, 40), Paging.Slice(2, 40, 20));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(3, 3)]
    [InlineData(99, 3)]
    public void Jump_out_of_range_is_clamped(int page, int expected)
        => Assert.Equal(expected, Paging.Clamp(page, 25, 10));

    [Theory]
    [InlineData(10, 10)]
    [InlineData(50, 50)]
    [InlineData(15, 10)]
    [InlineData(0, 10)]
    public void Unknown_page_size_falls_back_to_default(int size, int expected)
        => Assert.Equal(expected, Paging.NormalizePageSize(size));
}
