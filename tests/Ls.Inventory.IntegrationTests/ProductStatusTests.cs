using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ls.Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ls.Inventory.IntegrationTests;

public sealed partial class LiteFlowTests
{
    private static async Task<JsonElement> SetProductStatus(HttpClient c, Guid id, bool isActive)
    {
        var p = (await Data(await c.GetAsync("/api/v1/products/?includeInactive=true")))
            .EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id);
        return await Data(await c.PutAsJsonAsync($"/api/v1/products/{id}", new {
            name = p.GetProperty("name").GetString(), category = p.GetProperty("category").GetString(),
            productSpecification = p.GetProperty("productSpecification").GetString(),
            rawMaterialSpecification = p.GetProperty("rawMaterialSpecification").GetString(),
            unit = p.GetProperty("unit").GetString(), note = p.GetProperty("note").GetString(),
            version = p.GetProperty("version").GetInt64(), isActive
        }));
    }

    [Fact]
    public async Task Disabled_products_only_appear_at_end_of_catalog_and_reenable_restores_history()
    {
        using var c = await Login();
        var category = "状态筛选" + Guid.NewGuid();
        async Task<Guid> New(string name) => (await Data(await c.PostAsJsonAsync("/api/v1/products/",
            new { name = name + category, category, unit = "箱" }))).GetProperty("id").GetGuid();
        var active = await New("启用"); var disabled = await New("停用");
        await Data(await Move(c, active, "Inbound", 5));
        await Data(await Move(c, disabled, "Inbound", 7));
        await Data(await Move(c, disabled, "Outbound", 7));
        var stocktake = await Stocktake(c);
        var stocktakeId = stocktake.GetProperty("id").GetGuid();
        await Data(await c.PostAsJsonAsync($"/api/v1/stocktakes/{stocktakeId}/confirm", Counts(stocktake, disabled, 0)));
        await SetProductStatus(c, disabled, false);

        var normal = (await Data(await c.GetAsync("/api/v1/products/"))).EnumerateArray().ToArray();
        Assert.All(normal, p => Assert.True(p.GetProperty("isActive").GetBoolean()));
        Assert.Contains(normal, p => p.GetProperty("id").GetGuid() == active);
        Assert.DoesNotContain(normal, p => p.GetProperty("id").GetGuid() == disabled);
        var catalog = (await Data(await c.GetAsync("/api/v1/products/?includeInactive=true"))).EnumerateArray().ToArray();
        Assert.Contains(catalog, p => p.GetProperty("id").GetGuid() == disabled);
        Assert.All(catalog.SkipWhile(p => p.GetProperty("isActive").GetBoolean()), p => Assert.False(p.GetProperty("isActive").GetBoolean()));
        Assert.DoesNotContain((await Data(await c.GetAsync("/api/v1/products/?includeInactive=false"))).EnumerateArray(), p => p.GetProperty("id").GetGuid() == disabled);

        var analytics = await Data(await c.PostAsJsonAsync("/api/v1/analytics/query", new { category, productIds = new[] { active, disabled } }));
        Assert.Equal(active, Assert.Single(analytics.GetProperty("rows").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(5, analytics.GetProperty("trend").EnumerateArray().Sum(x => x.GetProperty("inbound").GetInt64()));
        Assert.Equal(0, analytics.GetProperty("trend").EnumerateArray().Sum(x => x.GetProperty("outbound").GetInt64()));
        var disabledAnalytics = await Data(await c.GetAsync($"/api/v1/analytics?productId={disabled}"));
        Assert.Empty(disabledAnalytics.GetProperty("rows").EnumerateArray());
        Assert.All(disabledAnalytics.GetProperty("trend").EnumerateArray(), x => Assert.Equal(0, x.GetProperty("inboundCount").GetInt32()));
        var daily = await Data(await c.GetAsync("/api/v1/movements/daily?category=" + Uri.EscapeDataString(category)));
        Assert.Equal(active, Assert.Single(daily.GetProperty("entries").EnumerateArray()).GetProperty("productId").GetGuid());
        Assert.DoesNotContain((await Data(await c.GetAsync("/api/v1/movements"))).GetProperty("items").EnumerateArray(), p => p.GetProperty("productId").GetGuid() == disabled);
        var ledger = await Data(await c.GetAsync($"/api/v1/movements?productId={disabled}"));
        Assert.Equal(0, ledger.GetProperty("total").GetInt32());
        Assert.Empty(ledger.GetProperty("items").EnumerateArray());
        var recent = await Data(await c.GetAsync($"/api/v1/products/{disabled}/recent-movements"));
        Assert.Equal(0, recent.GetProperty("total").GetInt32());
        Assert.Equal(0, recent.GetProperty("inbound").GetInt64());
        Assert.Equal(0, recent.GetProperty("outbound").GetInt64());
        Assert.Empty(recent.GetProperty("items").EnumerateArray());
        var history = await Data(await c.GetAsync($"/api/v1/stocktakes/{stocktakeId}"));
        Assert.DoesNotContain(history.GetProperty("lines").EnumerateArray(), p => p.GetProperty("productId").GetGuid() == disabled);
        var summary = (await Data(await c.GetAsync("/api/v1/stocktakes/"))).EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == stocktakeId);
        Assert.Equal(history.GetProperty("lines").GetArrayLength(), summary.GetProperty("lineCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await Move(c, disabled, "Inbound", 1)).StatusCode);
        using (var scope = fixture.Factory.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            Assert.Equal(2, await db.Movements.CountAsync(x => x.ProductId == disabled));
            Assert.True(await db.Stocktakes.AnyAsync(x => x.Id == stocktakeId && x.Lines.Any(l => l.ProductId == disabled)));
        }

        await SetProductStatus(c, disabled, true);
        Assert.Contains((await Data(await c.GetAsync("/api/v1/products/"))).EnumerateArray(), p => p.GetProperty("id").GetGuid() == disabled);
        Assert.Equal(2, (await Data(await c.GetAsync($"/api/v1/movements?productId={disabled}"))).GetProperty("total").GetInt32());
        var restored = await Data(await c.PostAsJsonAsync("/api/v1/analytics/query", new { category }));
        Assert.Equal(2, restored.GetProperty("rows").GetArrayLength());
        Assert.Equal(12, restored.GetProperty("trend").EnumerateArray().Sum(x => x.GetProperty("inbound").GetInt64()));
        Assert.Contains((await Data(await c.GetAsync($"/api/v1/stocktakes/{stocktakeId}"))).GetProperty("lines").EnumerateArray(), p => p.GetProperty("productId").GetGuid() == disabled);
    }

    [Fact]
    public async Task Disabling_a_product_during_stocktake_hides_it_but_requires_restarting_the_snapshot()
    {
        using var c = await Login();
        var disabled = await Product(c);
        await Product(c);
        var stocktake = await Stocktake(c);
        var id = stocktake.GetProperty("id").GetGuid();
        await SetProductStatus(c, disabled, false);
        var visible = await Data(await c.GetAsync($"/api/v1/stocktakes/{id}"));
        Assert.True(visible.GetProperty("hasChanged").GetBoolean());
        Assert.DoesNotContain(visible.GetProperty("lines").EnumerateArray(), x => x.GetProperty("productId").GetGuid() == disabled);
        foreach (var counts in new[] { Counts(stocktake, disabled, 0), Counts(visible, disabled, 0) }) {
            var response = await c.PostAsJsonAsync($"/api/v1/stocktakes/{id}/confirm", counts);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("STOCKTAKE_STALE", await response.Content.ReadAsStringAsync());
        }
        await Data(await c.PostAsJsonAsync($"/api/v1/stocktakes/{id}/cancel", new { }));
        var restarted = await Stocktake(c);
        Assert.False(restarted.GetProperty("hasChanged").GetBoolean());
        Assert.DoesNotContain(restarted.GetProperty("lines").EnumerateArray(), x => x.GetProperty("productId").GetGuid() == disabled);
        await Data(await c.PostAsJsonAsync($"/api/v1/stocktakes/{restarted.GetProperty("id").GetGuid()}/cancel", new { }));
    }
}
