using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ls.Inventory.Api.Data;
using Ls.Inventory.Api.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ls.Inventory.IntegrationTests;

public sealed partial class LiteFlowTests
{
    [Fact]
    public async Task Upgrade_recovers_full_audited_changes_once_without_inventing_missing_history()
    {
        var legacy = new LiteDatabaseFixture();
        await legacy.InitializeAsync();
        try {
            using var scope = legacy.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var p = new Product { Name = "旧名称", Code = "A01", Unit = "箱", Quantity = 12 };
            db.Products.Add(p);
            var old = ProductEndpoints.Snapshot(p);
            p.Name = "已更名";
            var changed = DateTimeOffset.UtcNow.AddDays(-4);
            db.AuditLogs.AddRange(
                new AuditLog { Action = "新增商品", Detail = JsonSerializer.Serialize(old), OccurredAt = changed.AddDays(-1) },
                new AuditLog { Action = "修改商品", UserName = "原经办人", OccurredAt = changed,
                    Detail = JsonSerializer.Serialize(new { before = old, after = ProductEndpoints.Snapshot(p) }) },
                new AuditLog { Action = "修改商品", Detail = "没有快照" });
            p.Quantity = 20;
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("DROP TABLE product_histories");
            await SchemaUpgrades.ApplyAsync(db);
            await SchemaUpgrades.ApplyAsync(db);
            var entries = await db.ProductHistories.OrderBy(x => x.ChangedAt).ToListAsync();
            Assert.Equal(2, entries.Count);
            Assert.Null(entries[0].Before);
            Assert.Equal("旧名称", entries[1].Before!.Name);
            Assert.Equal("已更名", entries[1].After.Name);
            Assert.Equal(12, entries[1].Quantity);
            Assert.Equal("原经办人", entries[1].UserName);
            Assert.Equal(changed.ToUnixTimeMilliseconds(), entries[1].ChangedAt.ToUnixTimeMilliseconds());
            Assert.Equal(20, (await db.Products.SingleAsync()).Quantity);
        } finally { await legacy.DisposeAsync(); }
    }

    [Fact]
    public async Task Catalog_changes_require_confirmation_and_keep_complete_snapshots_at_the_actual_balance()
    {
        using var c = await Login();
        var p = await Data(await c.PostAsJsonAsync("/api/v1/products/", new {
            name = "原商品" + Guid.NewGuid(), unit = "箱", category = "五金", warningQuantity = 2
        }));
        var id = p.GetProperty("id").GetGuid();
        var path = $"/api/v1/products/{id}";
        await Data(await Move(c, id, "Inbound", 9));
        var version = p.GetProperty("version").GetInt64() + 1;
        var code = "新版-" + id;
        object Change(bool confirmChanges, long version) => new { name = "新商品" + id, code, unit = "件", category = "零件",
            productSpecification = "M10", rawMaterialSpecification = "钢", note = "换规格", warningQuantity = 6, confirmChanges, version };
        var rejected = await c.PutAsJsonAsync(path, Change(false, version));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains("CONFIRMATION_REQUIRED", await rejected.Content.ReadAsStringAsync());
        var writes = await Task.WhenAll(c.PutAsJsonAsync(path, Change(true, version)), c.PutAsJsonAsync(path, Change(true, version)));
        var saved = await Data(Assert.Single(writes, res => res.IsSuccessStatusCode));
        Assert.Single(writes, res => res.StatusCode == HttpStatusCode.Conflict);
        var newVersion = saved.GetProperty("version").GetInt64();
        Assert.Equal(newVersion, (await Data(await c.PutAsJsonAsync(path, Change(true, newVersion)))).GetProperty("version").GetInt64());
        await Data(await Move(c, id, "Outbound", 4));
        await Data(await c.PutAsJsonAsync("/api/v1/products/warnings/batch", new {
            warningQuantity = 8, products = new[] { new { id, version = newVersion + 1 } }
        }));
        await Data(await c.PutAsJsonAsync(path + "/warning", new { warningQuantity = (int?)null, version = newVersion + 2 }));
        var history = await Data(await c.GetAsync(path + "/history"));
        var entries = history.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(4, history.GetProperty("total").GetInt32());
        Assert.Equal(new[] {5, 5, 9, 0}, entries.Select(e => e.GetProperty("quantity").GetInt32()));
        Assert.Equal(8, entries[0].GetProperty("before").GetProperty("warningQuantity").GetInt32());
        Assert.Equal(JsonValueKind.Null, entries[0].GetProperty("after").GetProperty("warningQuantity").ValueKind);
        var before = entries[2].GetProperty("before"); var after = entries[2].GetProperty("after");
        foreach (var key in new[] { "code", "name", "unit", "category", "productSpecification", "rawMaterialSpecification", "note", "warningQuantity", "isActive" }) {
            Assert.Equal(p.GetProperty(key).ToString(), before.GetProperty(key).ToString());
            Assert.Equal(saved.GetProperty(key).ToString(), after.GetProperty(key).ToString());
        }
        Assert.Equal("admin", entries[2].GetProperty("userName").GetString());
        Assert.Equal(JsonValueKind.Null, entries[3].GetProperty("before").ValueKind);
        var ledger = await Data(await c.GetAsync($"/api/v1/movements?productId={id}"));
        Assert.Equal(2, ledger.GetProperty("total").GetInt32());
        Assert.All(ledger.GetProperty("items").EnumerateArray(), m => Assert.Equal(id, m.GetProperty("productId").GetGuid()));
    }
}
