namespace MaterialMgmt.Tests;

public class PaginationTests
{
    [Fact]
    public void Slice_SecondPage()
    {
        const int pageSize = 10;
        const int total = 25;
        const int page = 2;
        var start = (page - 1) * pageSize;
        var end = Math.Min(start + pageSize, total);
        Assert.Equal(10, start);
        Assert.Equal(20, end);
        Assert.Equal(3, (total + pageSize - 1) / pageSize);
    }
}
