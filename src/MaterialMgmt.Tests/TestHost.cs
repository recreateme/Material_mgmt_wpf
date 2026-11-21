using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Infrastructure;
using MaterialMgmt.Infrastructure.Data;

namespace MaterialMgmt.Tests;

/// <summary>临时目录里的一套完整基础设施（已初始化的空库）；测试结束删除。</summary>
public abstract class TestHost : IAsyncLifetime
{
    private string _root = "";
    protected ServiceProvider Services { get; private set; } = null!;
    protected IDbContextFactory<MaterialMgmtDbContext> Factory { get; private set; } = null!;

    protected T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public virtual async Task InitializeAsync()
    {
        _root = Path.Combine(Path.GetTempPath(), "mm_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMaterialMgmtInfrastructure(_root);
        Services = services.BuildServiceProvider();
        await Get<DbBootstrap>().InitializeAsync();
        Factory = Get<IDbContextFactory<MaterialMgmtDbContext>>();
    }

    public async Task DisposeAsync()
    {
        await Services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* 临时目录，忽略 */ }
    }

    protected string TempFile(string name) => Path.Combine(_root, name);

    protected Material AddMaterial(string code, string name)
    {
        using var db = Factory.CreateDbContext();
        var m = new Material { Code = code, Name = name };
        db.Materials.Add(m);
        db.SaveChanges();
        return m;
    }

    /// <summary>新建配方，步骤按给定 (原料, 下限, 上限) 顺序编号 1..n。</summary>
    protected (Formula Formula, List<FormulaItem> Items) AddFormula(string code, params (Material M, double Min, double Max)[] steps)
    {
        using var db = Factory.CreateDbContext();
        var f = new Formula { Code = code, Name = "配方" + code, IsActive = true };
        db.Formulas.Add(f);
        db.SaveChanges();
        var items = steps.Select((s, i) => new FormulaItem
        {
            FormulaId = f.Id, MaterialId = s.M.Id, Sequence = i + 1, WeightMin = s.Min, WeightMax = s.Max
        }).ToList();
        db.FormulaItems.AddRange(items);
        db.SaveChanges();
        return (f, items);
    }

    protected WorkOrder AddOrder(int formulaId, string status = "in_progress", int? operatorId = null)
    {
        using var db = Factory.CreateDbContext();
        var wo = new WorkOrder { FormulaId = formulaId, Operator = "tester", OperatorId = operatorId, Status = status, StartTime = DateTime.Now };
        db.WorkOrders.Add(wo);
        db.SaveChanges();
        return wo;
    }
}
