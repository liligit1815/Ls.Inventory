using Ls.Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
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
            foreach (var p in products) SetWarning(db, c, p, r.WarningQuantity);
            EndpointSupport.Audit(db, c, "批量设置库存预警", new { before, after = r.WarningQuantity, count = products.Count });
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return EndpointSupport.Ok(c, products.Select(Snapshot).ToArray());
        });
        g.MapGet("/", async (bool? includeInactive, HttpContext c, InventoryDbContext db) => EndpointSupport.Ok(c,
            await db.Products.AsNoTracking().Where(x => includeInactive == true || x.IsActive)
                .OrderByDescending(x => x.IsActive).ThenBy(x => x.Code).Select(x => new { x.Id, x.Code, x.Name,
                x.Category, x.ProductSpecification, x.RawMaterialSpecification, x.Unit, x.Note, x.IsActive, x.Quantity, x.WarningQuantity, x.Version }).ToArrayAsync()));
        g.MapPut("/{id:guid}/warning", async (Guid id, WarningRequest r, HttpContext c, InventoryDbContext db) => {
            if (r.WarningQuantity is not null) InventoryRules.Quantity(r.WarningQuantity.Value, true);
            await using var tx = await db.Database.BeginTransactionAsync();
            await LockCatalog(db);
            var product = await db.Products.FromSqlInterpolated($"SELECT * FROM products WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync()
                ?? throw new BusinessRuleException("NOT_FOUND", "商品不存在", 404);
            if (product.Version != r.Version) throw new BusinessRuleException("CONFLICT", "商品已发生变化，请刷新后重新设置", 409);
            var before = product.WarningQuantity;
            SetWarning(db, c, product, r.WarningQuantity);
            EndpointSupport.Audit(db, c, "设置库存预警", new { product.Id, product.Name, before, after = product.WarningQuantity });
            await db.SaveChangesAsync(); await tx.CommitAsync(); return EndpointSupport.Ok(c, Snapshot(product));
        });
        g.MapPost("/", async (ProductRequest r, HttpContext c, InventoryDbContext db) => {
            await using var tx = await db.Database.BeginTransactionAsync();
            await LockCatalog(db);
            if (await db.Products.CountAsync() >= 5000) throw new BusinessRuleException("PRODUCT_LIMIT", "轻量版最多维护 5000 个商品");
            var product = new Product(); Set(product, r);
            if (product.Code.Length == 0) product.Code = "P" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
            db.Products.Add(product);
            RecordHistory(db, c, product);
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
            var previousDetails = ProductDetails.From(product);
            Set(product, r);
            if (!product.IsActive && product.Quantity != 0) throw new BusinessRuleException("PRODUCT_HAS_STOCK", "仍有库存的商品不能停用");
            if (previousDetails == ProductDetails.From(product)) return EndpointSupport.Ok(c, Snapshot(product));
            if (!r.ConfirmChanges) throw new BusinessRuleException("CONFIRMATION_REQUIRED", "请核对修改前后的商品资料并确认保存");
            product.Version++;
            RecordHistory(db, c, product, previousDetails);
            EndpointSupport.Audit(db, c, "修改商品", new { before, after = Snapshot(product) });
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return EndpointSupport.Ok(c, Snapshot(product));
        });
        g.MapGet("/{id:guid}/history", async (Guid id, int? page, HttpContext c, InventoryDbContext db) => {
            const int pageSize = 10;
            var pageNumber = page ?? 1;
            if (pageNumber < 1 || pageNumber > int.MaxValue / pageSize)
                throw new BusinessRuleException("INVALID_PAGE", "历史页码无效");
            if (!await db.Products.AnyAsync(p => p.Id == id))
                throw new BusinessRuleException("NOT_FOUND", "商品不存在", 404);
            var history = db.ProductHistories.AsNoTracking().Where(h => h.ProductId == id);
            var total = await history.CountAsync();
            var items = await history.OrderByDescending(h => h.ChangedAt).ThenByDescending(h => h.Id)
                .Skip((pageNumber - 1) * pageSize).Take(pageSize)
                .Select(h => new { h.Id, h.ChangedAt, h.UserName, h.Quantity, h.Before, h.After }).ToArrayAsync();
            return EndpointSupport.Ok(c, new { items, total, pageSize });
        });
    }
    internal static void RecordHistory(InventoryDbContext db, HttpContext c, Product p, ProductDetails? before = null)
        => ProductHistory.Record(db, p, before, c.User.Identity!.Name!);
    private static void SetWarning(InventoryDbContext db, HttpContext c, Product p, int? threshold)
    {
        var before = ProductDetails.From(p);
        p.WarningQuantity = threshold;
        p.Version++;
        RecordHistory(db, c, p, before);
    }
    public static object Snapshot(Product p) => new { p.Id, p.Code, p.Name, p.Category, p.ProductSpecification, p.RawMaterialSpecification, p.Unit, p.Note, p.IsActive, p.Quantity, p.WarningQuantity, p.Version };
    public sealed record WarningRequest(int? WarningQuantity, long Version);
    public sealed record WarningTarget(Guid Id, long Version);
    public sealed record BatchWarningRequest(int? WarningQuantity, WarningTarget[]? Products);
    public static Task<int> LockCatalog(InventoryDbContext db) => db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(82461001)");
    private static void Set(Product p, ProductRequest r)
    {
        if (r.Code != null) p.Code = InventoryRules.Text(r.Code, 80, "商品编码", p.Code.Length > 0);
        if (r.WarningQuantity.ValueKind != JsonValueKind.Undefined) {
            if (r.WarningQuantity.ValueKind == JsonValueKind.Null) p.WarningQuantity = null;
            else if (r.WarningQuantity.ValueKind == JsonValueKind.Number && r.WarningQuantity.TryGetInt32(out var value) && value >= 0) p.WarningQuantity = value;
            else throw new BusinessRuleException("INVALID_QUANTITY", "预警数量必须是非负整数");
        }
        p.Name = InventoryRules.Text(r.Name, 200, "商品名称", true);
        p.Category = InventoryRules.Text(r.Category, 100, "商品类目");
        p.ProductSpecification = InventoryRules.Text(r.ProductSpecification, 100, "产品规格");
        p.RawMaterialSpecification = InventoryRules.Text(r.RawMaterialSpecification, 100, "原料规格");
        p.Unit = InventoryRules.Text(r.Unit, 30, "单位", true);
        p.Note = InventoryRules.Text(r.Note, 500, "备注"); p.IsActive = r.IsActive;
    }
    public sealed record ProductRequest(string Name, string? ProductSpecification, string? RawMaterialSpecification,
        string Unit, string? Note, bool IsActive = true, long Version = 0, string? Category = null, bool ConfirmChanges = false,
        string? Code = null, JsonElement WarningQuantity = default);
}
