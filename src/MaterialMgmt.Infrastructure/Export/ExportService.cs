using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using MaterialMgmt.Core.Orders;
using MaterialMgmt.Infrastructure.Data;

namespace MaterialMgmt.Infrastructure.Export;

public sealed class ExportService
{
    private static readonly Regex SafeName = new(@"[\\/:*?""<>|\s]+", RegexOptions.Compiled);

    private readonly IDbContextFactory<MaterialMgmtDbContext> _factory;

    public ExportService(IDbContextFactory<MaterialMgmtDbContext> factory)
    {
        _factory = factory;
    }

    public string ExportWorkOrder(int workOrderId, string savePath)
    {
        using var db = _factory.CreateDbContext();
        var wo = Load(db, workOrderId) ?? throw new InvalidOperationException($"工单 {workOrderId} 不存在");
        using var wb = new XLWorkbook();
        wb.Style.Font.FontName = "宋体";
        var ws = wb.Worksheets.Add("称重记录");
        WriteSheet(ws, wo);
        return Save(wb, savePath);
    }

    public string ExportWorkOrders(IEnumerable<int> ids, string savePath)
    {
        var list = ids.ToList();
        if (list.Count == 0) throw new InvalidOperationException("未指定要导出的工单");
        if (list.Count == 1) return ExportWorkOrder(list[0], savePath);

        using var db = _factory.CreateDbContext();
        using var wb = new XLWorkbook();
        wb.Style.Font.FontName = "宋体";
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in list)
        {
            var wo = Load(db, id);
            if (wo is null) continue;
            var title = UniqueTitle($"工单{id}", used);
            var ws = wb.Worksheets.Add(title);
            WriteSheet(ws, wo);
        }
        if (!wb.Worksheets.Any()) throw new InvalidOperationException("所选工单均不存在，无法导出");
        return Save(wb, savePath);
    }

    public static string SuggestFilename(int workOrderId, string? formulaName = null)
    {
        if (!string.IsNullOrWhiteSpace(formulaName))
        {
            var safe = SafeName.Replace(formulaName, "_").Trim('_');
            if (safe.Length > 40) safe = safe[..40];
            return string.IsNullOrEmpty(safe) ? $"工单{workOrderId}.xlsx" : $"工单{workOrderId}_{safe}.xlsx";
        }
        return $"工单{workOrderId}.xlsx";
    }

    public static string SuggestBatchFilename()
        => $"工单批量导出_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

    private static Core.Entities.WorkOrder? Load(MaterialMgmtDbContext db, int id)
    {
        return db.WorkOrders
            .Include(w => w.Formula)
            .Include(w => w.Records)
            .ThenInclude(r => r.FormulaItem)
            .ThenInclude(i => i.Material)
            .FirstOrDefault(w => w.Id == id);
    }

    private static void WriteSheet(IXLWorksheet ws, Core.Entities.WorkOrder wo)
    {
        var status = OrderRules.ToDisplay(wo.Status);
        var row = 1;
        void Kv(string k, string? v)
        {
            ws.Cell(row, 1).Value = k;
            ws.Cell(row, 2).Value = v ?? "";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#2563EB");
            row++;
        }

        Kv("工单号", wo.Id.ToString());
        Kv("配方", $"{wo.Formula?.Code} {wo.Formula?.Name}");
        Kv("操作员", wo.Operator);
        Kv("状态", status);
        Kv("开始时间", wo.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
        Kv("结束时间", wo.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "");
        Kv("温度(℃)", wo.Temperature?.ToString("F1"));
        Kv("湿度(%)", wo.Humidity?.ToString("F1"));
        Kv("生产编号", wo.ProductionNo);
        Kv("D50", wo.D50?.ToString("F3"));
        Kv("混料异常", FormatBool(wo.MixingException));
        Kv("混料异常说明", wo.MixingExceptionNote);
        Kv("基膜", wo.BaseFilm);
        Kv("批次", wo.BatchNo);
        Kv("厚度", wo.Thickness?.ToString("F3"));
        Kv("涂布异常", FormatBool(wo.CoatingException));
        Kv("涂布异常说明", wo.CoatingExceptionNote);
        row++;

        ws.Cell(row, 1).Value = "步骤";
        ws.Cell(row, 2).Value = "材料编码";
        ws.Cell(row, 3).Value = "材料名称";
        ws.Cell(row, 4).Value = "下限(g)";
        ws.Cell(row, 5).Value = "上限(g)";
        ws.Cell(row, 6).Value = "实际(g)";
        ws.Cell(row, 7).Value = "扫码";
        ws.Cell(row, 8).Value = "时间";
        for (var c = 1; c <= 8; c++)
        {
            ws.Cell(row, c).Style.Font.Bold = true;
            ws.Cell(row, c).Style.Fill.BackgroundColor = XLColor.FromHtml("#2563EB");
            ws.Cell(row, c).Style.Font.FontColor = XLColor.White;
        }
        row++;

        foreach (var r in wo.Records.OrderBy(x => x.Timestamp))
        {
            var item = r.FormulaItem;
            var min = item?.WeightMin ?? 0;
            var max = item?.WeightMax ?? 0;
            ws.Cell(row, 1).Value = item?.Sequence ?? 0;
            ws.Cell(row, 2).Value = item?.Material?.Code ?? "";
            ws.Cell(row, 3).Value = item?.Material?.Name ?? "";
            ws.Cell(row, 4).Value = min;
            ws.Cell(row, 5).Value = max;
            ws.Cell(row, 6).Value = r.ActualWeight;
            if (r.ActualWeight < min || r.ActualWeight > max)
                ws.Cell(row, 6).Style.Font.FontColor = XLColor.FromHtml("#CC0000");
            ws.Cell(row, 7).Value = r.BarcodeScanned;
            ws.Cell(row, 8).Value = r.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
            row++;
        }

        // 统一字体为宋体，保留已设置的加粗、颜色、背景等样式
        var used = ws.RangeUsed();
        if (used is not null)
            used.Style.Font.FontName = "宋体";

        ws.Columns().AdjustToContents();
    }

    private static string FormatBool(bool? v) => v switch
    {
        true => "是",
        false => "否",
        null => ""
    };

    private static string UniqueTitle(string baseTitle, HashSet<string> used)
    {
        var t = baseTitle.Length > 31 ? baseTitle[..31] : baseTitle;
        var n = 1;
        var candidate = t;
        while (!used.Add(candidate))
        {
            n++;
            var suffix = $"_{n}";
            candidate = (t.Length + suffix.Length > 31 ? t[..(31 - suffix.Length)] : t) + suffix;
        }
        return candidate;
    }

    private static string Save(XLWorkbook wb, string savePath)
    {
        var dir = Path.GetDirectoryName(savePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        wb.SaveAs(savePath);
        return savePath;
    }
}
