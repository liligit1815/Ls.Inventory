using Ls.Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
namespace Ls.Inventory.Api.Endpoints;

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1/products").RequireAuthorization();
        g.MapPut("/warnings/batch", async (BatchWarningRequest r, HttpContext c, InventoryDbContext db) => {
            if (r.Products is null || r.Products.Length is 0 or > 5000 || r.Products.Any(x => x is null || x.Id == Guid.Empty)
                || r.Products.Select(x => x.Id).Distinct().Count() != r.Products.Length)
                throw new BusinessRuleException("INVALID_PRODUCTS", "请选择 1 至 5000 个不重复的商品");
            if (r.WarningQuantity is not null) InventoryRules.Quantity(r.WarningQuantity.Value, true);
            await using var tx = await db.Database.BeginTransactionAsync();
            await LockCatalog(db);
            var products = new List<Product>();
            foreach (var item in r.Products.OrderBy(x => x.Id)) {
                var p = await db.Products.FromSqlInterpolated($"SELECT * FROM products WHERE id = {item.Id} FOR UPDATE").SingleOrDefaultAsync()
                    ?? throw new BusinessRuleException("NOT_FOUND", "部分商品已不存在，本次未保存，请刷新后重新选择", 404);
                if (p.Version != item.Version) throw new BusinessRuleException("CONFLICT", $"{p.Name} 已发生变化，本次全部未保存，请刷新后重新设置", 409);
                products.Add(p);
            }
            var before = products.Select(p => new { p.Id, p.Name, p.WarningQuantity }).ToArray();
            foreach (var p in products) { p.WarningQuantity = r.WarningQuantity; p.Version++; }
            EndpointSupport.Audit(db, c, "批量设置库存预警", new { before, after = r.WarningQuantity, count = products.Count });
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return EndpointSupport.Ok(c, products.Select(Snapshot).ToArray());
        });
        g.MapGet("/", async (HttpContext c, InventoryDbContext db) => EndpointSupport.Ok(c,
            await db.Products.AsNoTracking().OrderBy(x => x.Code).Select(x => new { x.Id, x.Code, x.Name,
                x.ProductSpecification, x.RawMaterialSpecification, x.Unit, x.Note, x.IsActive, x.Quantity, x.WarningQuantity, x.Version }).ToArrayAsync()));
        g.MapPut("/{id:guid}/warning", async (Guid id, WarningRequest r, HttpContext c, InventoryDbContext db) => {
            if (r.WarningQuantity is not null) InventoryRules.Quantity(r.WarningQuantity.Value, true);
            await using var tx = await db.Database.BeginTransactionAsync();
            await LockCatalog(db);
            var product = await db.Products.FromSqlInterpolated($"SELECT * FROM products WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync()
                ?? throw new BusinessRuleException("NOT_FOUND", "商品不存在", 404);
            if (product.Version != r.Version) throw new BusinessRuleException("CONFLICT", "商品已发生变化，请刷新后重新设置", 409);
            var before = product.WarningQuantity;
            product.WarningQuantity = r.WarningQuantity; product.Version++;
            EndpointSupport.Audit(db, c, "设置库存预警", new { product.Id, product.Name, before, after = product.WarningQuantity });
            await db.SaveChangesAsync(); await tx.CommitAsync(); return EndpointSupport.Ok(c, Snapshot(product));
        });
        g.MapPost("/", async (ProductRequest r, HttpContext c, InventoryDbContext db) => {
            await using var tx = await db.Database.BeginTransactionAsync();
            await LockCatalog(db);
            if (await db.Products.CountAsync() >= 5000) throw new BusinessRuleException("PRODUCT_LIMIT", "轻量版最多维护 5000 个商品");
            var product = new Product(); Set(product, r);
            product.Code = "P" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
            db.Products.Add(product);
            EndpointSupport.Audit(db, c, "新增商品", Snapshot(product));
            await db.SaveChangesAsync(); await tx.CommitAsync(); return EndpointSupport.Ok(c, Snapshot(product));
        });
        g.MapPut("/{id:guid}", async (Guid id, ProductRequest r, HttpContext c, InventoryDbContext db) => {
            await using var tx = await db.Database.BeginTransactionAsync();
            await LockCatalog(db);
            var product = await db.Products.FromSqlInterpolated($"SELECT * FROM products WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync()
                ?? throw new BusinessRuleException("NOT_FOUND", "商品不存在", 404);
            if (product.Version != r.Version) throw new BusinessRuleException("CONFLICT", "商品或库存已变化，请刷新后再保存", 409);
            var before = Snapshot(product);
            var candidate = new Product(); Set(candidate, r);
            if (await db.Movements.AnyAsync(x => x.ProductId == id) && (product.Name != candidate.Name || product.ProductSpecification != candidate.ProductSpecification || product.RawMaterialSpecification != candidate.RawMaterialSpecification || product.Unit != candidate.Unit))
                throw new BusinessRuleException("PRODUCT_HAS_HISTORY", "已有流水的商品不能更换名称、规格或单位；请另建商品，以免历史数据混淆");
            if (!candidate.IsActive && product.Quantity != 0) throw new BusinessRuleException("PRODUCT_HAS_STOCK", "仍有库存的商品不能停用");
            Set(product, r); product.Version++;
            EndpointSupport.Audit(db, c, "修改商品", new { before, after = Snapshot(product) });
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return EndpointSupport.Ok(c, Snapshot(product));
        });
    }
    public static object Snapshot(Product p) => new { p.Id, p.Code, p.Name, p.ProductSpecification, p.RawMaterialSpecification, p.Unit, p.Note, p.IsActive, p.Quantity, p.WarningQuantity, p.Version };
    public sealed record WarningRequest(int? WarningQuantity, long Version);
    public sealed record WarningTarget(Guid Id, long Version);
    public sealed record BatchWarningRequest(int? WarningQuantity, WarningTarget[]? Products);
    public static Task<int> LockCatalog(InventoryDbContext db) => db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(82461001)");
    private static void Set(Product p, ProductRequest r)
    {
        p.Name = InventoryRules.Text(r.Name, 200, "商品名称", true);
        p.ProductSpecification = InventoryRules.Text(r.ProductSpecification, 100, "产品规格");
        p.RawMaterialSpecification = InventoryRules.Text(r.RawMaterialSpecification, 100, "原料规格");
        p.Unit = InventoryRules.Text(r.Unit, 30, "单位", true);
        p.Note = InventoryRules.Text(r.Note, 500, "备注"); p.IsActive = r.IsActive;
    }
    public sealed record ProductRequest(string Name, string? ProductSpecification, string? RawMaterialSpecification,
        string Unit, string? Note, bool IsActive = true, long Version = 0);
}
