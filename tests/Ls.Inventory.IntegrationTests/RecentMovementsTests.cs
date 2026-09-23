using System.Net;
using Ls.Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ls.Inventory.IntegrationTests;

public sealed partial class LiteFlowTests
{
    [Fact]
    public async Task Recent_movements_filter_by_product_and_China_calendar_days_and_exclude_adjustments()
    {
        using var c = await Login();
        var product = await Product(c);
        var other = await Product(c);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var start = new DateTimeOffset(today.AddDays(-6).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8)).ToUniversalTime();
        var tomorrow = start.AddDays(7);
        using (var scope = fixture.Factory.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var user = await db.Users.SingleAsync(x => x.UserName == "admin");
            void Add(Guid id, string kind, int change, DateTimeOffset at) => db.Movements.Add(new Movement {
                RequestId = Guid.NewGuid(), ProductId = id, UserId = user.Id, Kind = kind,
                QuantityChange = change, QuantityAfter = 100, PostedAt = at, Note = "日期边界验证"
            });
            Add(product, "Inbound", 10, start);
            Add(product, "Outbound", -3, tomorrow.AddTicks(-10));
            Add(product, "Inbound", 20, start.AddTicks(-10));
            Add(product, "Inbound", 40, tomorrow);
            Add(product, "StockGain", 50, start.AddHours(1));
            Add(product, "StockLoss", -2, start.AddHours(2));
            Add(other, "Inbound", 70, start);
            await db.SaveChangesAsync();
        }
        var data = await Data(await c.GetAsync($"/api/v1/products/{product}/recent-movements"));
        Assert.Equal(7, data.GetProperty("days").GetInt32());
        Assert.Equal(today.AddDays(-6).ToString("yyyy-MM-dd"), data.GetProperty("from").GetString());
        Assert.Equal(2, data.GetProperty("total").GetInt32());
        Assert.Equal(10, data.GetProperty("inbound").GetInt64());
        Assert.Equal(3, data.GetProperty("outbound").GetInt64());
        Assert.Equal(1, data.GetProperty("inboundCount").GetInt32());
        Assert.Equal(1, data.GetProperty("outboundCount").GetInt32());
        Assert.Equal("Outbound", data.GetProperty("items")[0].GetProperty("kind").GetString());
        var longer = await Data(await c.GetAsync($"/api/v1/products/{product}/recent-movements?days=8"));
        Assert.Equal(30, longer.GetProperty("inbound").GetInt64());
        var oneDay = await Data(await c.GetAsync($"/api/v1/products/{product}/recent-movements?days=1"));
        Assert.Equal(0, oneDay.GetProperty("inbound").GetInt64());
        Assert.Equal(3, oneDay.GetProperty("outbound").GetInt64());
        // Existing all-time ledger must still include adjustments and older records.
        var ledger = await Data(await c.GetAsync($"/api/v1/movements?productId={product}"));
        Assert.Equal(6, ledger.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Recent_movements_totals_cover_all_pages_and_use_64_bit_quantities()
    {
        using var c = await Login();
        var product = await Product(c);
        using (var scope = fixture.Factory.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var user = await db.Users.SingleAsync(x => x.UserName == "admin");
            for (var i = 0; i < 31; i++) db.Movements.Add(new Movement {
                RequestId = Guid.NewGuid(), ProductId = product, UserId = user.Id,
                Kind = "Inbound", QuantityChange = int.MaxValue, QuantityAfter = int.MaxValue
            });
            await db.SaveChangesAsync();
        }
        var first = await Data(await c.GetAsync($"/api/v1/products/{product}/recent-movements?days=367"));
        var second = await Data(await c.GetAsync($"/api/v1/products/{product}/recent-movements?days=367&page=2"));
        Assert.Equal(31, first.GetProperty("total").GetInt32());
        Assert.Equal(31L * int.MaxValue, first.GetProperty("inbound").GetInt64());
        Assert.Equal(first.GetProperty("inbound").GetInt64(), second.GetProperty("inbound").GetInt64());
        Assert.Equal(30, first.GetProperty("items").GetArrayLength());
        Assert.Equal(1, second.GetProperty("items").GetArrayLength());
        Assert.DoesNotContain(first.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetInt64() == second.GetProperty("items")[0].GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task Recent_movements_handle_empty_ranges_and_reject_invalid_inputs()
    {
        using var c = await Login();
        var product = await Product(c);
        var empty = await Data(await c.GetAsync($"/api/v1/products/{product}/recent-movements"));
        Assert.Equal(0, empty.GetProperty("total").GetInt32());
        Assert.Equal(0, empty.GetProperty("inbound").GetInt64());
        Assert.Equal(0, empty.GetProperty("outbound").GetInt64());
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        foreach (var query in new[] { "days=0", "days=-1", "days=368", "days=1.5", "page=0", "page=2147483647" })
            Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/api/v1/products/{product}/recent-movements?{query}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/v1/products/{Guid.NewGuid()}/recent-movements")).StatusCode);
        using var anonymous = fixture.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/products/{product}/recent-movements")).StatusCode);
    }
}
