using Ls.Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
namespace Ls.Inventory.Api.Endpoints;

public static class StocktakeEndpoints
{
    public sealed record CountValue(Guid ProductId, int Quantity);
    public sealed record CountRequest(IReadOnlyList<CountValue> Counts);
    public static void MapStocktakeEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1/stocktakes").RequireAuthorization();
        g.MapGet("/", async (HttpContext c, InventoryDbContext db) => EndpointSupport.Ok(c,
            await db.Stocktakes.OrderByDescending(x => x.CreatedAt).Take(100).Select(x => new { x.Id, x.Status, x.CreatedAt, x.PostedAt, lineCount = x.Lines.Count }).ToArrayAsync()));
        g.MapGet("/{id:guid}", async (Guid id, HttpContext c, InventoryDbContext db) => {
            var stocktake = await db.Stocktakes.AsNoTracking().Include(x => x.Lines).ThenInclude(x => x.Product).SingleOrDefaultAsync(x => x.Id == id)
                ?? throw new BusinessRuleException("NOT_FOUND", "盘库记录不存在", 404);
            return EndpointSupport.Ok(c, View(stocktake));
        });
        g.MapPost("/", async (HttpContext c, InventoryDbContext db) => {
            await using var tx = await db.Database.BeginTransactionAsync();
            // A transaction-scoped lock prevents duplicate active stocktakes from rapid clicks.
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(70611001)");
            var existing = await db.Stocktakes.SingleOrDefaultAsync(x => x.Status == "Draft");
            if (existing is not null) return EndpointSupport.Ok(c, new { existing.Id });
            var products = await db.Products.Where(x => x.IsActive).OrderBy(x => x.Id).ToArrayAsync();
            if (products.Length == 0) throw new BusinessRuleException("NO_PRODUCTS", "请先建立商品档案");
            var s = new Stocktake { UserId = EndpointSupport.UserId(c), Lines = products.Select(p => new StocktakeLine {
                ProductId = p.Id, ExpectedQuantity = p.Quantity, ExpectedVersion = p.Version }).ToList() };
            db.Stocktakes.Add(s);
            EndpointSupport.Audit(db, c, "开始盘库", new { s.Id, products = products.Length });
            await db.SaveChangesAsync(); await tx.CommitAsync(); return EndpointSupport.Ok(c, new { s.Id });
        });
        g.MapPost("/{id:guid}/confirm", Confirm);
        g.MapPost("/{id:guid}/cancel", async (Guid id, HttpContext c, InventoryDbContext db) => {
            await using var tx = await db.Database.BeginTransactionAsync();
            var s = await db.Stocktakes.FromSqlInterpolated($"SELECT * FROM stocktakes WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync()
                ?? throw new BusinessRuleException("NOT_FOUND", "盘库记录不存在", 404);
            if (s.Status != "Draft") throw new BusinessRuleException("INVALID_STATE", "只能取消未完成的盘库");
            s.Status = "Cancelled";
            EndpointSupport.Audit(db, c, "取消盘库", new { s.Id });
            await db.SaveChangesAsync(); await tx.CommitAsync(); return EndpointSupport.Ok(c, new { s.Id });
        });
    }
    private static object View(Stocktake s) => new { s.Id, s.Status, s.CreatedAt, s.PostedAt,
        lines = s.Lines.OrderBy(x => x.Product.Code).Select(x => new { x.ProductId, x.Product.Code, x.Product.Name,
            x.Product.ProductSpecification, x.Product.RawMaterialSpecification, x.Product.Unit, x.ExpectedQuantity,
            x.CountedQuantity, currentQuantity = x.Product.Quantity, hasChanged = x.Product.Version != x.ExpectedVersion }) };

    private static async Task<IResult> Confirm(Guid id, CountRequest request, HttpContext c, InventoryDbContext db)
    {
        if (request.Counts is null || request.Counts.Count == 0 || request.Counts.Select(x => x.ProductId).Distinct().Count() != request.Counts.Count)
            throw new BusinessRuleException("INVALID_COUNTS", "请填写完整且不重复的实盘数量");
        foreach (var value in request.Counts) InventoryRules.Quantity(value.Quantity, true);
        await using var tx = await db.Database.BeginTransactionAsync();
        var s = await db.Stocktakes.FromSqlInterpolated($"SELECT * FROM stocktakes WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync()
            ?? throw new BusinessRuleException("NOT_FOUND", "盘库记录不存在", 404);
        await db.Entry(s).Collection(x => x.Lines).LoadAsync();
        var counts = request.Counts.ToDictionary(x => x.ProductId, x => x.Quantity);
        if (counts.Count != s.Lines.Count || s.Lines.Any(x => !counts.ContainsKey(x.ProductId))) throw new BusinessRuleException("INVALID_COUNTS", "请填写全部商品的实盘数量");
        if (s.Status == "Posted") {
            if (s.Lines.Any(x => x.CountedQuantity != counts[x.ProductId])) throw new BusinessRuleException("REQUEST_CONFLICT", "盘库已确认，不能修改结果", 409);
            return EndpointSupport.Ok(c, new { s.Id, alreadyPosted = true });
        }
        if (s.Status != "Draft") throw new BusinessRuleException("INVALID_STATE", "该盘库已取消");
        var uid = EndpointSupport.UserId(c);
        foreach (var line in s.Lines.OrderBy(x => x.ProductId)) {
            var p = await db.Products.FromSqlInterpolated($"SELECT * FROM products WHERE id = {line.ProductId} FOR UPDATE").SingleAsync();
            if (p.Version != line.ExpectedVersion) throw new BusinessRuleException("STOCKTAKE_STALE", $"{p.Name} 在盘库期间发生变化，请取消本次盘库并重新开始，避免覆盖最新库存", 409);
            line.CountedQuantity = counts[p.Id];
            var difference = line.CountedQuantity.Value - p.Quantity;
            if (difference == 0) continue;
            p.Quantity = InventoryRules.Apply(p.Quantity, difference); p.Version++;
            db.Movements.Add(new Movement { RequestId = Guid.NewGuid(), ProductId = p.Id, UserId = uid,
                Kind = difference > 0 ? "StockGain" : "StockLoss", QuantityChange = difference,
                QuantityAfter = p.Quantity, Note = "盘库确认差异", StocktakeId = s.Id });
        }
        s.Status = "Posted"; s.PostedAt = DateTimeOffset.UtcNow;
        EndpointSupport.Audit(db, c, "确认盘库", new { s.Id, lines = s.Lines.Select(x => new { x.ProductId, x.ExpectedQuantity, x.CountedQuantity }) });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return EndpointSupport.Ok(c, new { s.Id, alreadyPosted = false });
    }
}
