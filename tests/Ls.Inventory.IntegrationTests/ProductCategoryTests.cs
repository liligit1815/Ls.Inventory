using System.Net;
using System.Net.Http.Json;
using Ls.Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ls.Inventory.IntegrationTests;

public sealed partial class LiteFlowTests
{
    [Fact]
    public async Task Product_category_is_trimmed_editable_with_history_and_does_not_change_identity_or_stock()
    {
        using var c = await Login();
        var name = "分类测试" + Guid.NewGuid();
        var p = await Data(await c.PostAsJsonAsync("/api/v1/products/", new { name, unit = "箱", category = " 食品 " }));
        var id = p.GetProperty("id").GetGuid();
        Assert.Equal("食品", p.GetProperty("category").GetString());
        await Data(await Move(c, id, "Inbound", 9));
        p = (await Data(await c.GetAsync("/api/v1/products/"))).EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id);
        var version = p.GetProperty("version").GetInt64();
        var changed = await Data(await c.PutAsJsonAsync($"/api/v1/products/{id}", new { name, unit = "箱", category = "饮品", version }));
        Assert.Equal("饮品", changed.GetProperty("category").GetString());
        Assert.Equal(9, changed.GetProperty("quantity").GetInt32());
        Assert.Equal(version + 1, changed.GetProperty("version").GetInt64());
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/v1/products/{id}", new { name, unit = "箱", category = "食品", version })).StatusCode);
        Assert.False((await c.PostAsJsonAsync("/api/v1/products/", new { name, unit = "箱", category = "其他" })).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/products/", new { name = "过长分类", unit = "箱", category = new string('a', 101) })).StatusCode);
        var uncategorized = await Product(c);
        var old = (await Data(await c.GetAsync("/api/v1/products/"))).EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == uncategorized);
        Assert.Equal("", old.GetProperty("category").GetString());
    }

    [Fact]
    public async Task Daily_movements_filter_current_category_and_inclusive_China_date_range()
    {
        using var c = await Login();
        var a = await Product(c); var b = await Product(c);
        var category = "分类" + Guid.NewGuid();
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)).AddDays(-10);
        var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8)).ToUniversalTime();
        using (var scope = fixture.Factory.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var product = await db.Products.SingleAsync(x => x.Id == a);
            product.Category = category; product.IsActive = false;
            var user = await db.Users.SingleAsync(x => x.UserName == "admin");
            void Add(Guid id, string kind, int quantity, DateTimeOffset at) => db.Movements.Add(new Movement {
                RequestId = Guid.NewGuid(), ProductId = id, UserId = user.Id, Kind = kind,
                QuantityChange = quantity, QuantityAfter = 100, PostedAt = at
            });
            Add(a, "Inbound", 3, start);
            Add(a, "Outbound", -2, start.AddDays(1).AddTicks(-10));
            Add(a, "Inbound", 50, start.AddTicks(-10));
            Add(a, "Inbound", 50, start.AddDays(1));
            Add(a, "StockGain", 50, start.AddHours(1));
            Add(b, "Inbound", 7, start);
            await db.SaveChangesAsync();
        }
        var url = $"/api/v1/movements/daily?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}";
        var result = await Data(await c.GetAsync(url + "&category=" + Uri.EscapeDataString(category)));
        Assert.Equal(1, result.GetProperty("days").GetInt32());
        var row = Assert.Single(result.GetProperty("entries").EnumerateArray());
        Assert.Equal(a, row.GetProperty("productId").GetGuid());
        Assert.Equal(3, row.GetProperty("inbound").GetInt64()); Assert.Equal(2, row.GetProperty("outbound").GetInt64());
        var unclassified = await Data(await c.GetAsync(url + "&category="));
        Assert.Contains(unclassified.GetProperty("entries").EnumerateArray(), x => x.GetProperty("productId").GetGuid() == b);
        Assert.DoesNotContain(unclassified.GetProperty("entries").EnumerateArray(), x => x.GetProperty("productId").GetGuid() == a);
        var all = await Data(await c.GetAsync(url));
        Assert.Contains(all.GetProperty("entries").EnumerateArray(), x => x.GetProperty("productId").GetGuid() == a);
        Assert.Contains(all.GetProperty("entries").EnumerateArray(), x => x.GetProperty("productId").GetGuid() == b);
        Assert.Empty((await Data(await c.GetAsync(url + "&category=" + Guid.NewGuid()))).GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task Daily_movements_reject_incomplete_reversed_future_and_excessive_date_ranges()
    {
        using var c = await Login();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        foreach (var query in new[] {
            $"from={today:yyyy-MM-dd}", $"to={today:yyyy-MM-dd}",
            $"from={today:yyyy-MM-dd}&to={today.AddDays(-1):yyyy-MM-dd}",
            $"from={today:yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}",
            $"from={today.AddDays(-367):yyyy-MM-dd}&to={today:yyyy-MM-dd}",
            "from=invalid&to=2026-01-01", "category=" + new string('a', 101)
        }) Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/v1/movements/daily?" + query)).StatusCode);
        var valid = await Data(await c.GetAsync($"/api/v1/movements/daily?from={today.AddDays(-366):yyyy-MM-dd}&to={today:yyyy-MM-dd}"));
        Assert.Equal(367, valid.GetProperty("days").GetInt32());
    }
}
