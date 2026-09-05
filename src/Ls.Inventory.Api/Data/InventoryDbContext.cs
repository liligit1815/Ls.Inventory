using Microsoft.EntityFrameworkCore;
namespace Ls.Inventory.Api.Data;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<LoginSession> LoginSessions => Set<LoginSession>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Movement> Movements => Set<Movement>();
    public DbSet<Stocktake> Stocktakes => Set<Stocktake>();
    public DbSet<StocktakeLine> StocktakeLines => Set<StocktakeLine>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>().HasIndex(x => x.UserName).IsUnique();
        b.Entity<AppUser>().Property(x => x.UserName).HasMaxLength(80);
        b.Entity<AppUser>().Property(x => x.DisplayName).HasMaxLength(100);
        b.Entity<LoginSession>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Product>(e => {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => new { x.Name, x.ProductSpecification, x.RawMaterialSpecification, x.Unit }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(80);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.ProductSpecification).HasMaxLength(100);
            e.Property(x => x.RawMaterialSpecification).HasMaxLength(100);
            e.Property(x => x.Unit).HasMaxLength(30);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.Quantity).HasColumnType("integer");
            e.Property(x => x.WarningQuantity).HasColumnType("integer");
            e.ToTable(t => t.HasCheckConstraint("ck_product_warning_quantity", "warning_quantity IS NULL OR warning_quantity >= 0"));
            e.Property(x => x.Version).IsConcurrencyToken();
            e.ToTable(t => t.HasCheckConstraint("ck_product_quantity", "quantity >= 0"));
        });
        b.Entity<Movement>(e => {
            e.HasIndex(x => x.RequestId).IsUnique();
            e.HasIndex(x => new { x.ProductId, x.PostedAt });
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Stocktake).WithMany().HasForeignKey(x => x.StocktakeId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.QuantityChange).HasColumnType("integer");
            e.Property(x => x.QuantityAfter).HasColumnType("integer");
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.Kind).HasMaxLength(20);
            e.ToTable(t => {
                t.HasCheckConstraint("ck_movement_balance", "quantity_after >= 0");
                t.HasCheckConstraint("ck_movement_direction", "(kind IN ('Inbound', 'StockGain') AND quantity_change > 0) OR (kind IN ('Outbound', 'StockLoss') AND quantity_change < 0)");
            });
        });
        b.Entity<Stocktake>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StocktakeLine>(e => {
            e.HasKey(x => new { x.StocktakeId, x.ProductId });
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.ExpectedQuantity).HasColumnType("integer");
            e.Property(x => x.CountedQuantity).HasColumnType("integer");
        });
        b.Entity<AuditLog>().HasIndex(x => x.OccurredAt);
        b.Entity<AuditLog>().Property(x => x.Action).HasMaxLength(80);
    }
}
