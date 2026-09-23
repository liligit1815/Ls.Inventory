using System.Net;
using Ls.Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ls.Inventory.IntegrationTests;

public sealed partial class LiteFlowTests
{
    [Fact]
    public async Task Daily_movements_group_by_China_date_and_product_without_netting_or_adjustments()
    {
        using var c = await Login();
        var product = await Product(c); var other = await Product(c);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var start = new DateTimeOffset(today.AddDays(-6).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8)).ToUniversalTime();
        using (var scope = fixture.Factory.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var user = await db.Users.SingleAsync(x => x.UserName == "admin");
            (await db.Products.SingleAsync(x => x.Id == other)).Unit = "个";
            void Add(Guid id, string kind, int change, DateTimeOffset at) => db.Movements.Add(new Movement {
                RequestId = Guid.NewGuid(), ProductId = id, UserId = user.Id, Kind = kind,
                QuantityChange = change, QuantityAfter = 100, PostedAt = at
            });
            Add(product, "Inbound", int.MaxValue, start);
            Add(product, "Inbound", int.MaxValue, start.AddHours(1));
            Add(product, "Outbound", -30, start.AddHours(2));
            Add(other, "Inbound", 50, start.AddHours(2));
            Add(product, "Inbound", 10, start.AddDays(1));
            Add(product, "Inbound", 20, start.AddTicks(-10));
            Add(product, "Inbound", 40, start.AddDays(7));
            Add(product, "StockGain", 5, start.AddHours(3));
            Add(product, "StockLoss", -2, start.AddHours(4));
            await db.SaveChangesAsync();
        }
        var data = await Data(await c.GetAsync("/api/v1/movements/daily"));
        Assert.Equal(7, data.GetProperty("days").GetInt32());
        var rows = data.GetProperty("entries").EnumerateArray().Where(x => x.GetProperty("productId").GetGuid() == product).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal(today.AddDays(-6).ToString("yyyy-MM-dd"), rows[0].GetProperty("date").GetString());
        Assert.Equal(2L * int.MaxValue, rows[0].GetProperty("inbound").GetInt64());
        Assert.Equal(30, rows[0].GetProperty("outbound").GetInt64());
        Assert.Equal(2, rows[0].GetProperty("inboundCount").GetInt32());
        Assert.Equal(1, rows[0].GetProperty("outboundCount").GetInt32());
        Assert.Equal(today.AddDays(-5).ToString("yyyy-MM-dd"), rows[1].GetProperty("date").GetString());
        Assert.Equal(10, rows[1].GetProperty("inbound").GetInt64());
        var otherRow = Assert.Single(data.GetProperty("entries").EnumerateArray(), x => x.GetProperty("productId").GetGuid() == other);
        Assert.Equal(50, otherRow.GetProperty("inbound").GetInt64());
        var longer = await Data(await c.GetAsync("/api/v1/movements/daily?days=8"));
        Assert.Equal(3, longer.GetProperty("entries").EnumerateArray().Count(x => x.GetProperty("productId").GetGuid() == product));
        var oneDay = await Data(await c.GetAsync("/api/v1/movements/daily?days=1"));
        Assert.DoesNotContain(oneDay.GetProperty("entries").EnumerateArray(), x => x.GetProperty("productId").GetGuid() == product);
    }

    [Fact]
    public async Task Daily_movements_require_login_and_validate_ranges()
    {
        using var anonymous = fixture.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/movements/daily")).StatusCode);
        using var c = await Login();
        foreach (var days in new[] { "0", "-1", "368", "1.5" })
            Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/api/v1/movements/daily?days={days}")).StatusCode);
        Assert.Equal(367, (await Data(await c.GetAsync("/api/v1/movements/daily?days=367"))).GetProperty("days").GetInt32());
    }
}
