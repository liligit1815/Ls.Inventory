using Ls.Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
namespace Ls.Inventory.Api.Endpoints;

public static class InventoryEndpoints
{
    public static void MapInventoryEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1").RequireAuthorization();
        g.MapPost("/movements", PostMovement);
        g.MapGet("/movements/daily", DailyMovements);
        g.MapGet("/products/{productId:guid}/recent-movements", RecentMovements);
        g.MapGet("/movements", async (Guid? productId, int? page, HttpContext c, InventoryDbContext db) => {
            var q = db.Movements.AsNoTracking().Where(x => productId == null || x.ProductId == productId);
            var p = Math.Max(1, page ?? 1);
            var total = await q.CountAsync();
            var items = await q.OrderByDescending(x => x.Id).Skip((p - 1) * 30).Take(30).Select(x => new {
                x.Id, x.ProductId, productName = x.Product.Name, productNote = x.Product.Note, x.Product.ProductSpecification, x.Product.RawMaterialSpecification, x.Product.Unit, x.Kind, x.QuantityChange, x.QuantityAfter, x.Note, x.PostedAt, userName = x.User.DisplayName
            }).ToArrayAsync();
            return EndpointSupport.Ok(c, new { total, items });
        });
        g.MapGet("/analytics", (DateOnly? from, DateOnly? to, Guid? productId, string? unit, HttpContext c, InventoryDbContext db) => Analytics(from, to, productId, unit, c, db));
        g.MapPost("/analytics/query", (AnalyticsQuery r, HttpContext c, InventoryDbContext db) => Analytics(r.From, r.To, null, null, c, db, r.ProductIds));
        g.MapGet("/audit-logs", async (string? search, int? page, HttpContext c, InventoryDbContext db) => {
            var q = db.AuditLogs.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Action.Contains(search) || x.UserName.Contains(search));
            var total = await q.CountAsync();
            var items = await q.OrderByDescending(x => x.Id).Skip((Math.Max(1, page ?? 1) - 1) * 30).Take(30)
                .Select(x => new { x.Id, x.OccurredAt, x.UserName, x.Action, x.IpAddress }).ToArrayAsync();
            return EndpointSupport.Ok(c, new { total, items });
        });
    }
    private static async Task<IResult> DailyMovements(int? days, HttpContext c, InventoryDbContext db)
    {
        var rangeDays = days ?? 7;
        if (rangeDays is < 1 or > 367)
            throw new BusinessRuleException("INVALID_RANGE", "请输入 1～367 之间的整数天数");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var from = today.AddDays(1 - rangeDays);
        var startUtc = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8)).ToUniversalTime();
        var endUtc = new DateTimeOffset(today.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8)).ToUniversalTime();
        var groups = await db.Movements.AsNoTracking()
            .Where(x => x.PostedAt >= startUtc && x.PostedAt < endUtc && (x.Kind == "Inbound" || x.Kind == "Outbound"))
            .GroupBy(x => new { x.ProductId, Year = x.PostedAt.AddHours(8).Year, Month = x.PostedAt.AddHours(8).Month, Day = x.PostedAt.AddHours(8).Day })
            .Select(g => new {
                g.Key.ProductId, g.Key.Year, g.Key.Month, g.Key.Day,
                inbound = g.Sum(x => x.Kind == "Inbound" ? (long)x.QuantityChange : 0),
                outbound = -g.Sum(x => x.Kind == "Outbound" ? (long)x.QuantityChange : 0),
                inboundCount = g.Count(x => x.Kind == "Inbound"), outboundCount = g.Count(x => x.Kind == "Outbound")
            }).OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.Day).ThenBy(x => x.ProductId).ToArrayAsync();
        return EndpointSupport.Ok(c, new {
            from, to = today, days = rangeDays,
            entries = groups.Select(x => new { date = new DateOnly(x.Year, x.Month, x.Day), x.ProductId, x.inbound, x.outbound, x.inboundCount, x.outboundCount })
        });
    }
    private static async Task<IResult> RecentMovements(Guid productId, int? days, int? page, HttpContext c, InventoryDbContext db)
    {
        var rangeDays = days ?? 7;
        if (rangeDays is < 1 or > 367)
            throw new BusinessRuleException("INVALID_RANGE", "请输入 1～367 之间的整数天数");
        var p = page ?? 1;
        if (p < 1 || p > int.MaxValue / 30)
            throw new BusinessRuleException("INVALID_PAGE", "页码无效");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var from = today.AddDays(1 - rangeDays);
        var startUtc = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8)).ToUniversalTime();
        var endUtc = new DateTimeOffset(today.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8)).ToUniversalTime();
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        if (!await db.Products.AnyAsync(x => x.Id == productId))
            throw new BusinessRuleException("NOT_FOUND", "商品不存在", 404);
        var q = db.Movements.AsNoTracking().Where(x => x.ProductId == productId
            && x.PostedAt >= startUtc && x.PostedAt < endUtc && (x.Kind == "Inbound" || x.Kind == "Outbound"));
        var totals = await q.GroupBy(x => x.Kind).Select(g => new {
            kind = g.Key, count = g.Count(), quantity = g.Sum(x => (long)x.QuantityChange)
        }).ToArrayAsync();
        var inbound = totals.SingleOrDefault(x => x.kind == "Inbound");
        var outbound = totals.SingleOrDefault(x => x.kind == "Outbound");
        var items = await q.OrderByDescending(x => x.PostedAt).ThenByDescending(x => x.Id)
            .Skip((p - 1) * 30).Take(30).Select(x => new {
                x.Id, x.ProductId, productName = x.Product.Name, x.Product.Unit, x.Kind,
                x.QuantityChange, x.QuantityAfter, x.Note, x.PostedAt, userName = x.User.DisplayName
            }).ToArrayAsync();
        await tx.CommitAsync();
        return EndpointSupport.Ok(c, new {
            from, to = today, days = rangeDays, total = totals.Sum(x => x.count), items,
            inbound = inbound?.quantity ?? 0, outbound = -(outbound?.quantity ?? 0),
            inboundCount = inbound?.count ?? 0, outboundCount = outbound?.count ?? 0
        });
    }
    public sealed record MovementRequest(Guid RequestId, Guid ProductId, string Kind, int Quantity, string? Note);
    private static async Task<IResult> PostMovement(MovementRequest r, HttpContext c, InventoryDbContext db)
    {
        if (r.RequestId == Guid.Empty || r.Kind is not ("Inbound" or "Outbound")) throw new BusinessRuleException("INVALID_MOVEMENT", "请选择入库或出库");
        InventoryRules.Quantity(r.Quantity);
        var note = InventoryRules.Text(r.Note, 500, "备注");
        var uid = EndpointSupport.UserId(c);
        await using var tx = await db.Database.BeginTransactionAsync();
        var product = await db.Products.FromSqlInterpolated($"SELECT * FROM products WHERE id = {r.ProductId} FOR UPDATE").SingleOrDefaultAsync()
            ?? throw new BusinessRuleException("NOT_FOUND", "商品不存在", 404);
        var existing = await db.Movements.SingleOrDefaultAsync(x => x.RequestId == r.RequestId);
        var change = r.Kind == "Inbound" ? r.Quantity : -r.Quantity;
        if (existing is not null) {
            if (existing.UserId != uid || existing.ProductId != r.ProductId || existing.Kind != r.Kind || existing.QuantityChange != change || existing.Note != note)
                throw new BusinessRuleException("REQUEST_CONFLICT", "同一请求编号不能用于不同操作", 409);
            return EndpointSupport.Ok(c, new { existing.Id, existing.QuantityAfter, alreadyPosted = true });
        }
        if (!product.IsActive) throw new BusinessRuleException("PRODUCT_DISABLED", "商品已停用");
        product.Quantity = InventoryRules.Apply(product.Quantity, change); product.Version++;
        var movement = new Movement { RequestId = r.RequestId, ProductId = product.Id, UserId = uid,
            Kind = r.Kind, QuantityChange = change, QuantityAfter = product.Quantity, Note = note };
        db.Movements.Add(movement);
        EndpointSupport.Audit(db, c, r.Kind == "Inbound" ? "快捷入库" : "快捷出库",
            new { product.Code, product.Name, change, balance = product.Quantity, note, r.RequestId });
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return EndpointSupport.Ok(c, new { movement.Id, movement.QuantityAfter, alreadyPosted = false });
    }

    public sealed record AnalyticsQuery(DateOnly? From, DateOnly? To, Guid[]? ProductIds);
    private static async Task<IResult> Analytics(DateOnly? from, DateOnly? to, Guid? productId, string? unit, HttpContext c, InventoryDbContext db, Guid[]? selectedIds = null)
    {
        if (selectedIds is { Length: > 5000 } || selectedIds?.Any(x => x == Guid.Empty) == true)
            throw new BusinessRuleException("INVALID_PRODUCTS", "商品筛选无效，最多选择 5000 种商品");
        var ids = selectedIds?.Distinct().ToArray() ?? [];
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = to ?? today;
        if (end < start || end.DayNumber - start.DayNumber > 366 || end > today)
            throw new BusinessRuleException("INVALID_RANGE", "请选择不超过 367 天且不晚于今天的日期范围");
        var startUtc = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8)).ToUniversalTime();
        var endUtc = new DateTimeOffset(end.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8)).ToUniversalTime();
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var products = await db.Products.AsNoTracking().Where(x => (productId == null || x.Id == productId) && (unit == null || x.Unit == unit) && (ids.Length == 0 || ids.Contains(x.Id))).OrderBy(x => x.Code).ToArrayAsync();
        var movements = await db.Movements.AsNoTracking().Where(x => x.PostedAt >= startUtc && x.PostedAt < endUtc && (productId == null || x.ProductId == productId) && (unit == null || x.Product.Unit == unit) && (ids.Length == 0 || ids.Contains(x.ProductId))).ToArrayAsync();
        var groups = movements.ToLookup(x => x.ProductId);
        var rows = products.Select(p => {
            var items = groups[p.Id].ToArray();
            var inbound = items.Where(x => x.Kind == "Inbound").Sum(x => (long)x.QuantityChange);
            var outbound = -items.Where(x => x.Kind == "Outbound").Sum(x => (long)x.QuantityChange);
            var adjustment = items.Where(x => x.Kind is "StockGain" or "StockLoss").Sum(x => (long)x.QuantityChange);
            return new { p.Id, p.Code, p.Name, p.ProductSpecification, p.RawMaterialSpecification, p.Unit, p.IsActive, p.Note, p.WarningQuantity,
                isLowStock = p.IsActive && p.WarningQuantity != null && p.Quantity < p.WarningQuantity,
                inboundCount = items.Count(x => x.Kind == "Inbound"), outboundCount = items.Count(x => x.Kind == "Outbound"),
                inbound, outbound, adjustment, netChange = inbound - outbound + adjustment, currentQuantity = p.Quantity };
        }).ToArray();
        var dates = movements.ToLookup(x => DateOnly.FromDateTime(x.PostedAt.UtcDateTime.AddHours(8)));
        var trend = Enumerable.Range(0, end.DayNumber - start.DayNumber + 1).Select(offset => {
            var date = start.AddDays(offset); var g = dates[date];
            return new { date, inbound = g.Where(x => x.Kind == "Inbound").Sum(x => (long)x.QuantityChange),
                outbound = -g.Where(x => x.Kind == "Outbound").Sum(x => (long)x.QuantityChange),
                inboundCount = g.Count(x => x.Kind == "Inbound"), outboundCount = g.Count(x => x.Kind == "Outbound"),
                adjustment = g.Where(x => x.Kind is "StockGain" or "StockLoss").Sum(x => (long)x.QuantityChange) }; }).ToArray();
        await tx.CommitAsync();
        return EndpointSupport.Ok(c, new { from = start, to = end, rows, trend });
    }
}
