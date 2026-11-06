using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Ls.Inventory.Api.Data;

// Only full catalog snapshots can establish both the previous details and the stock at that instant.
internal static class LegacyProductHistory
{
    public static async Task ImportAsync(InventoryDbContext db)
    {
        var productIds = (await db.Products.Select(p => p.Id).ToListAsync()).ToHashSet();
        long cursor = 0;
        while (true) {
            var logs = await db.AuditLogs.AsNoTracking().Where(a => a.Id > cursor && (a.Action == "新增商品" || a.Action == "修改商品"))
                .OrderBy(a => a.Id).Take(250).ToListAsync();
            if (logs.Count == 0) break;
            var batch = new List<ProductHistory>();
            foreach (var log in logs) {
                var entry = Read(log);
                if (entry != null && productIds.Contains(entry.ProductId)) batch.Add(entry);
            }
            db.ProductHistories.AddRange(batch);
            await db.SaveChangesAsync();
            foreach (var entry in batch) db.Entry(entry).State = EntityState.Detached;
            cursor = logs[^1].Id;
        }
    }

    private sealed record Snapshot(Guid? Id, decimal? Quantity, string? Code, string? Name, string? Category, string? ProductSpecification,
        string? RawMaterialSpecification, string? Unit, string? Note, bool? IsActive, decimal? WarningQuantity)
    {
        public ProductDetails? Details() => Id != null && Code != null && Name != null && Category != null && ProductSpecification != null
            && RawMaterialSpecification != null && Unit != null && Note != null && IsActive != null
            && (WarningQuantity == null || ValidQuantity(WarningQuantity))
            ? new(Code, Name, Category, ProductSpecification, RawMaterialSpecification, Unit, Note, IsActive.Value, (int?)WarningQuantity) : null;
    }

    private static bool ValidQuantity(decimal? number) => number is >= 0 and <= int.MaxValue && decimal.IsInteger(number.Value);

    private static ProductHistory? Read(AuditLog log)
    {
        try {
            using var doc = JsonDocument.Parse(log.Detail);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            Snapshot? prior = null;
            var afterJson = root;
            if (log.Action == "修改商品") {
                if (!root.TryGetProperty("before", out var old) || !root.TryGetProperty("after", out afterJson)) return null;
                prior = old.Deserialize<Snapshot>();
            }
            var snapshot = afterJson.Deserialize<Snapshot>();
            var after = snapshot?.Details();
            var before = prior?.Details();
            if (log.Action == "修改商品" && (before == null || prior!.Id != snapshot?.Id)) return null;
            if (after == null || before == after || !ValidQuantity(snapshot!.Quantity)) return null;
            return new ProductHistory { ProductId = snapshot.Id!.Value, Quantity = (int)snapshot.Quantity!.Value,
                ChangedAt = log.OccurredAt, UserName = log.UserName, Before = before, After = after };
        } catch (JsonException) {
            // Old or incomplete audit formats stay in the audit log.
            return null;
        }
    }
}
