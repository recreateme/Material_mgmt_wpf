using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Infrastructure.Data;
using MaterialMgmt.Infrastructure.Export;
using MaterialMgmt.Infrastructure.Hardware;
using MaterialMgmt.Infrastructure.Services;

namespace MaterialMgmt.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMaterialMgmtInfrastructure(
        this IServiceCollection services,
        string? appRoot = null)
    {
        var paths = new AppPaths(appRoot);
        services.AddSingleton<IAppPaths>(paths);
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<IAuditLog, AuditLogService>();
        services.AddSingleton<BarcodeInputService>();
        services.AddSingleton<BarcodeSerialService>();

        services.AddDbContextFactory<MaterialMgmtDbContext>(opt =>
            opt.UseSqlite($"Data Source={paths.DatabasePath}"));

        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<DbBootstrap>();
        services.AddSingleton<OrderService>();
        services.AddSingleton<FormulaService>();
        services.AddSingleton<MaterialService>();
        services.AddSingleton<UserService>();
        services.AddSingleton<ExportService>();

        return services;
    }
}
