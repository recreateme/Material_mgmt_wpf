using ClosedXML.Excel;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Core.Hardware;
using MaterialMgmt.Infrastructure.Export;
using MaterialMgmt.Infrastructure.Services;

namespace MaterialMgmt.Tests.Services;

public class ExportServiceTests : TestHost
{
    private ExportService Export => Get<ExportService>();
    private OrderService Orders => Get<OrderService>();

    /// <summary>导出后读出明细表：表头行之后的每一行（列 1..9）。</summary>
    private static (List<string> Header, List<List<string>> Rows) ReadDetail(string path, string? sheet = null)
    {
        using var wb = new XLWorkbook(path);
        var ws = sheet is null ? wb.Worksheet(1) : wb.Worksheet(sheet);
        var headerRow = ws.RowsUsed().First(r => r.Cell(1).GetString() == "步骤").RowNumber();
        List<string> Row(int r) => Enumerable.Range(1, 9).Select(c => ws.Cell(r, c).GetString()).ToList();
        var last = ws.LastRowUsed()!.RowNumber();
        return (Row(headerRow), Enumerable.Range(headerRow + 1, last - headerRow).Select(Row).ToList());
    }

    [Fact]
    public void Export_OnlyValidRecords_WithScanSourceColumn()
    {
        var (f, items) = AddFormula("FML-T00001", (AddMaterial("MAT-T00001", "甲"), 1, 2), (AddMaterial("MAT-T00002", "乙"), 3, 4));
        var wo = AddOrder(f.Id);
        Orders.SavePendingWeight(wo.Id, items[0].Id, "MAT-T00001", 1.1, DateTime.Now.AddMinutes(-3), ScanSources.Serial);
        Orders.InvalidateValidRecords(wo.Id, items[0].Id, "tester", "重称");
        Orders.SavePendingWeight(wo.Id, items[0].Id, "MAT-T00001", 1.5, DateTime.Now.AddMinutes(-2), ScanSources.Hid);
        Orders.SavePendingWeight(wo.Id, items[1].Id, "MAT-T00002", 3.5, DateTime.Now.AddMinutes(-1), ScanSources.Serial);

        var path = Export.ExportWorkOrder(wo.Id, TempFile("one.xlsx"));
        var (header, rows) = ReadDetail(path);

        Assert.Equal("扫码来源", header[8]);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "1.5", "3.5" }, rows.Select(r => r[5]));
        Assert.Equal(new[] { ScanSources.ToDisplay(ScanSources.Hid), ScanSources.ToDisplay(ScanSources.Serial) },
            rows.Select(r => r[8]));
    }

    [Fact]
    public void Export_UsesSnapshot_EvenAfterFormulaEdit()
    {
        var mat = AddMaterial("MAT-T00001", "甲");
        var (f, items) = AddFormula("FML-T00001", (mat, 1, 2));
        var wo = AddOrder(f.Id);
        Orders.SavePendingWeight(wo.Id, items[0].Id, "MAT-T00001", 1.5, DateTime.Now);

        using (var db = Factory.CreateDbContext())
        {
            db.FormulaItems.Single(i => i.Id == items[0].Id).WeightMin = 10;
            db.Materials.Single(m => m.Id == mat.Id).Name = "甲改名";
            db.SaveChanges();
        }

        var (_, rows) = ReadDetail(Export.ExportWorkOrder(wo.Id, TempFile("snap.xlsx")));
        Assert.Equal(("甲", "1", "2"), (rows[0][2], rows[0][3], rows[0][4]));
    }

    [Fact]
    public void Export_LegacyRecordWithoutSnapshot_FallsBackToFormula()
    {
        var (f, items) = AddFormula("FML-T00001", (AddMaterial("MAT-T00001", "甲"), 1, 2));
        var wo = AddOrder(f.Id);
        using (var db = Factory.CreateDbContext())
        {
            db.WeightRecords.Add(new WeightRecord
            {
                WorkOrderId = wo.Id, FormulaItemId = items[0].Id, BarcodeScanned = "MAT-T00001",
                ActualWeight = 2.5, Timestamp = DateTime.Now, IsValid = true
            });
            db.SaveChanges();
        }

        var (_, rows) = ReadDetail(Export.ExportWorkOrder(wo.Id, TempFile("legacy.xlsx")));
        Assert.Equal(("MAT-T00001", "甲", "1", "2", ""), (rows[0][1], rows[0][2], rows[0][3], rows[0][4], rows[0][8]));
    }

    [Fact]
    public void ExportMany_OneSheetPerOrder_SkipsDeleted()
    {
        var (f, items) = AddFormula("FML-T00001", (AddMaterial("MAT-T00001", "甲"), 1, 2));
        var a = AddOrder(f.Id, "completed");
        var b = AddOrder(f.Id, "completed");
        var gone = AddOrder(f.Id, "completed");
        Orders.SavePendingWeight(a.Id, items[0].Id, "MAT-T00001", 1.5, DateTime.Now);
        Orders.Delete(gone.Id, allowCompleted: true);

        var path = Export.ExportWorkOrders(new[] { a.Id, b.Id, gone.Id }, TempFile("many.xlsx"));
        using var wb = new XLWorkbook(path);
        Assert.Equal(new[] { $"工单{a.Id}", $"工单{b.Id}" }, wb.Worksheets.Select(w => w.Name));
        Assert.Single(ReadDetail(path, $"工单{a.Id}").Rows);
    }

    [Fact]
    public void ExportMany_AllMissing_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Export.ExportWorkOrders(new[] { 998, 999 }, TempFile("x.xlsx")));
        Assert.Throws<InvalidOperationException>(() => Export.ExportWorkOrders(Array.Empty<int>(), TempFile("y.xlsx")));
    }

    [Theory]
    [InlineData(5, null, "工单5.xlsx")]
    [InlineData(5, "A/B:C", "工单5_A_B_C.xlsx")]
    [InlineData(5, "  ", "工单5.xlsx")]
    public void SuggestFilename_SanitizesName(int id, string? name, string expected)
    {
        Assert.Equal(expected, ExportService.SuggestFilename(id, name));
    }
}
