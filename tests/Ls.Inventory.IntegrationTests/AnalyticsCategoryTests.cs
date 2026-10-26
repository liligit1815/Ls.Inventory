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
    public async Task Analytics_category_filters_rows_and_trend_and_intersects_product_selection()
    {
        using var c = await Login();
        var a = await Product(c); var b = await Product(c); var zero = await Product(c);
        var category = "看板分类" + Guid.NewGuid();
        await Data(await Move(c, a, "Inbound", 3));
        await Data(await Move(c, b, "Inbound", 7));
        using (var scope = fixture.Factory.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var products = await db.Products.Where(p => p.Id == a || p.Id == zero).ToArrayAsync();
            foreach (var p in products) p.Category = category;
            products.Single(p => p.Id == zero).IsActive = false;
            await db.SaveChangesAsync();
        }
        var result = await Data(await c.PostAsJsonAsync("/api/v1/analytics/query", new { category }));
        Assert.Equal(category, result.GetProperty("category").GetString());
        var rows = result.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.All(rows, row => Assert.Equal(category, row.GetProperty("category").GetString()));
        Assert.Equal(3, rows.Sum(row => row.GetProperty("inbound").GetInt64()));
        Assert.Equal(3, result.GetProperty("trend").EnumerateArray().Sum(day => day.GetProperty("inbound").GetInt64()));
        Assert.Equal(1, result.GetProperty("trend").EnumerateArray().Sum(day => day.GetProperty("inboundCount").GetInt32()));
        Assert.Contains(rows, row => row.GetProperty("id").GetGuid() == zero && !row.GetProperty("isActive").GetBoolean());

        var outside = await Data(await c.PostAsJsonAsync("/api/v1/analytics/query", new { category, productIds = new[] { b } }));
        Assert.Empty(outside.GetProperty("rows").EnumerateArray());
        Assert.All(outside.GetProperty("trend").EnumerateArray(), day => Assert.Equal(0, day.GetProperty("inboundCount").GetInt32()));
        var unclassified = await Data(await c.PostAsJsonAsync("/api/v1/analytics/query", new { category = "", productIds = new[] { a, b } }));
        Assert.Equal(b, Assert.Single(unclassified.GetProperty("rows").EnumerateArray()).GetProperty("id").GetGuid());
        var all = await Data(await c.PostAsJsonAsync("/api/v1/analytics/query", new { productIds = new[] { a, b } }));
        Assert.Equal(2, all.GetProperty("rows").GetArrayLength());
        Assert.Equal(10, all.GetProperty("trend").EnumerateArray().Sum(day => day.GetProperty("inbound").GetInt64()));
        var missing = await Data(await c.PostAsJsonAsync("/api/v1/analytics/query", new { category = "不存在" + Guid.NewGuid() }));
        Assert.Empty(missing.GetProperty("rows").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/analytics/query", new { category = new string('x', 101) })).StatusCode);
    }
}
