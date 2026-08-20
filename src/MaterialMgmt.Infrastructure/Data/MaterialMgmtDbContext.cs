using Microsoft.EntityFrameworkCore;
using MaterialMgmt.Core.Entities;

namespace MaterialMgmt.Infrastructure.Data;

public sealed class MaterialMgmtDbContext : DbContext
{
    public MaterialMgmtDbContext(DbContextOptions<MaterialMgmtDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<Formula> Formulas => Set<Formula>();
    public DbSet<FormulaItem> FormulaItems => Set<FormulaItem>();
    public DbSet<FormulaErpBinding> FormulaErpBindings => Set<FormulaErpBinding>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WeightRecord> WeightRecords => Set<WeightRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Username).HasColumnName("username").HasMaxLength(64).IsRequired();
            e.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(256).IsRequired();
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(64).IsRequired();
            e.Property(x => x.Role).HasColumnName("role").HasMaxLength(16).IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.Username).IsUnique();
        });

        modelBuilder.Entity<Material>(e =>
        {
            e.ToTable("materials");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
            e.Property(x => x.Description).HasColumnName("description").HasMaxLength(512);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<Formula>(e =>
        {
            e.ToTable("formulas");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
            e.Property(x => x.Description).HasColumnName("description").HasMaxLength(512);
            e.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(64);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.HasMany(x => x.Items).WithOne(x => x.Formula).HasForeignKey(x => x.FormulaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FormulaItem>(e =>
        {
            e.ToTable("formula_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FormulaId).HasColumnName("formula_id").IsRequired();
            e.Property(x => x.MaterialId).HasColumnName("material_id").IsRequired();
            e.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
            e.Property(x => x.WeightMin).HasColumnName("weight_min").IsRequired();
            e.Property(x => x.WeightMax).HasColumnName("weight_max").IsRequired();
            e.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(256);
            e.HasIndex(x => new { x.FormulaId, x.Sequence }).IsUnique();
            e.HasOne(x => x.Material).WithMany(x => x.FormulaItems).HasForeignKey(x => x.MaterialId);
        });

        modelBuilder.Entity<FormulaErpBinding>(e =>
        {
            e.ToTable("formula_erp_bindings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FormulaId).HasColumnName("formula_id").IsRequired();
            e.Property(x => x.ErpTypeCode).HasColumnName("erp_type_code").HasMaxLength(64).IsRequired();
            e.Property(x => x.ErpLabel).HasColumnName("erp_label").HasMaxLength(128);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.HasIndex(x => x.ErpTypeCode).IsUnique();
            e.HasOne(x => x.Formula).WithMany(x => x.ErpBindings).HasForeignKey(x => x.FormulaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkOrder>(e =>
        {
            e.ToTable("work_orders");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FormulaId).HasColumnName("formula_id").IsRequired();
            e.Property(x => x.OperatorId).HasColumnName("operator_id");
            e.Property(x => x.Operator).HasColumnName("operator").HasMaxLength(64).IsRequired();
            e.Property(x => x.ErpTypeCode).HasColumnName("erp_type_code").HasMaxLength(64);
            e.Property(x => x.ExternalOrderNo).HasColumnName("external_order_no").HasMaxLength(64);
            e.Property(x => x.StartTime).HasColumnName("start_time").IsRequired();
            e.Property(x => x.EndTime).HasColumnName("end_time");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
            e.Property(x => x.Temperature).HasColumnName("temperature");
            e.Property(x => x.Humidity).HasColumnName("humidity");
            e.Property(x => x.ProductionNo).HasColumnName("production_no").HasMaxLength(64);
            e.Property(x => x.D50).HasColumnName("d50");
            e.Property(x => x.MixingException).HasColumnName("mixing_exception");
            e.Property(x => x.BaseFilm).HasColumnName("base_film").HasMaxLength(128);
            e.Property(x => x.BatchNo).HasColumnName("batch_no").HasMaxLength(64);
            e.Property(x => x.Thickness).HasColumnName("thickness");
            e.Property(x => x.CoatingException).HasColumnName("coating_exception");
            e.Property(x => x.MixingExceptionNote).HasColumnName("mixing_exception_note").HasMaxLength(512);
            e.Property(x => x.CoatingExceptionNote).HasColumnName("coating_exception_note").HasMaxLength(512);
            e.HasIndex(x => x.ExternalOrderNo);
            e.HasOne(x => x.Formula).WithMany(x => x.WorkOrders).HasForeignKey(x => x.FormulaId);
            e.HasOne(x => x.User).WithMany(x => x.WorkOrders).HasForeignKey(x => x.OperatorId);
            e.HasMany(x => x.Records).WithOne(x => x.WorkOrder).HasForeignKey(x => x.WorkOrderId);
        });

        modelBuilder.Entity<WeightRecord>(e =>
        {
            e.ToTable("weight_records");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired();
            e.Property(x => x.FormulaItemId).HasColumnName("formula_item_id").IsRequired();
            e.Property(x => x.BarcodeScanned).HasColumnName("barcode_scanned").HasMaxLength(128).IsRequired();
            e.Property(x => x.TareWeight).HasColumnName("tare_weight");
            e.Property(x => x.ActualWeight).HasColumnName("actual_weight").IsRequired();
            e.Property(x => x.Timestamp).HasColumnName("timestamp").IsRequired();
            e.Property(x => x.IsValid).HasColumnName("is_valid").IsRequired();
            e.Property(x => x.Note).HasColumnName("note").HasMaxLength(256);
            e.HasOne(x => x.FormulaItem).WithMany(x => x.WeightRecords).HasForeignKey(x => x.FormulaItemId);
        });
    }
}
