using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ls.Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ls.Inventory.IntegrationTests;
[CollectionDefinition("Presentation", DisableParallelization = true)]
public sealed class PresentationCollection : ICollectionFixture<LiteDatabaseFixture>;
[Collection("Presentation")]
public sealed class PresentationTests(LiteDatabaseFixture fixture)
{
    [Fact]
    public async Task Password_change_accepts_six_digits_and_rejects_short_or_incorrect_passwords() {
        var userName="password-test-"+Guid.NewGuid().ToString("N");
        using(var scope=fixture.Factory.Services.CreateScope()) {
            var db=scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var user=new AppUser{UserName=userName,DisplayName="密码测试",MustChangePassword=true};
            user.PasswordHash=new Microsoft.AspNetCore.Identity.PasswordHasher<AppUser>().HashPassword(user,"OriginalTestPass!");
            db.Users.Add(user);await db.SaveChangesAsync();
        }
        using var c=fixture.Factory.CreateClient();
        async Task Csrf(){var token=await Data(await c.GetAsync("/api/v1/auth/csrf"));c.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");c.DefaultRequestHeaders.Add("X-XSRF-TOKEN",token.GetProperty("requestToken").GetString());}
        await Csrf();await Data(await c.PostAsJsonAsync("/api/v1/auth/login",new{userName,password="OriginalTestPass!"}));await Csrf();
        foreach(var invalid in new[]{"12345",new string('1',129)})Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/v1/auth/change-password",new{currentPassword="OriginalTestPass!",newPassword=invalid})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/v1/auth/change-password",new{currentPassword="wrong",newPassword="123456"})).StatusCode);
        await Data(await c.PostAsJsonAsync("/api/v1/auth/change-password",new{currentPassword="OriginalTestPass!",newPassword="123456"}));await Csrf();
        var me=await Data(await c.GetAsync("/api/v1/auth/me"));Assert.False(me.GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/v1/auth/change-password",new{currentPassword="123456",newPassword="123456"})).StatusCode);
        await Data(await c.PostAsJsonAsync("/api/v1/auth/logout",new{}));await Csrf();
        await Data(await c.PostAsJsonAsync("/api/v1/auth/login",new{userName,password="123456"}));
        using var checkScope=fixture.Factory.Services.CreateScope();var checkDb=checkScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var hash=(await checkDb.Users.SingleAsync(x=>x.UserName==userName)).PasswordHash;Assert.NotEqual("123456",hash);
    }
    [Fact]
    public async Task Batch_warning_saves_only_selected_and_conflicts_roll_back_all() {
        using var c=await Login();
        async Task<JsonElement> New()=>await Data(await c.PostAsJsonAsync("/api/v1/products/",new{name="批量预警"+Guid.NewGuid(),unit="箱"}));
        var a=await New();var b=await New();var other=await New();
        object Target(JsonElement p)=>new{id=p.GetProperty("id").GetGuid(),version=p.GetProperty("version").GetInt64()};
        var saved=await Data(await c.PutAsJsonAsync("/api/v1/products/warnings/batch",new{warningQuantity=100,products=new[]{Target(a),Target(b)}}));
        Assert.Equal(2,saved.GetArrayLength());Assert.All(saved.EnumerateArray(),p=>{Assert.Equal(100,p.GetProperty("warningQuantity").GetInt32());Assert.Equal(0,p.GetProperty("quantity").GetInt32());});
        var ids=saved.EnumerateArray().OrderBy(p=>p.GetProperty("id").GetGuid()).ToArray();
        var stale=ids[1];await Data(await c.PutAsJsonAsync($"/api/v1/products/{stale.GetProperty("id").GetGuid()}/warning",new{warningQuantity=200,version=stale.GetProperty("version").GetInt64()}));
        Assert.Equal(HttpStatusCode.Conflict,(await c.PutAsJsonAsync("/api/v1/products/warnings/batch",new{warningQuantity=300,products=ids.Select(Target)})).StatusCode);
        var all=await Data(await c.GetAsync("/api/v1/products/"));
        JsonElement Find(Guid id)=>all.EnumerateArray().Single(p=>p.GetProperty("id").GetGuid()==id);
        Assert.Equal(100,Find(ids[0].GetProperty("id").GetGuid()).GetProperty("warningQuantity").GetInt32());
        Assert.Equal(JsonValueKind.Null,Find(other.GetProperty("id").GetGuid()).GetProperty("warningQuantity").ValueKind);
        var cleared=await Data(await c.PutAsJsonAsync("/api/v1/products/warnings/batch",new{warningQuantity=(int?)null,products=ids.Select(p=>Target(Find(p.GetProperty("id").GetGuid())))}));
        Assert.All(cleared.EnumerateArray(),p=>Assert.Equal(JsonValueKind.Null,p.GetProperty("warningQuantity").ValueKind));
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PutAsJsonAsync("/api/v1/products/warnings/batch",new{warningQuantity=1,products=Array.Empty<object>()})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PutAsJsonAsync("/api/v1/products/warnings/batch",new{warningQuantity=1,products=new[]{Target(other),Target(other)}})).StatusCode);
        foreach(var invalid in new[]{-1m,1.5m,2147483648m}) Assert.Equal(HttpStatusCode.BadRequest,(await c.PutAsJsonAsync("/api/v1/products/warnings/batch",new{warningQuantity=invalid,products=new[]{Target(other)}})).StatusCode);
    }

    [Fact]
    public async Task Analytics_multi_product_filter_covers_metrics_and_trends() {
        using var c=await Login();
        async Task<Guid> New(string unit)=>(await Data(await c.PostAsJsonAsync("/api/v1/products/",new{name="多选看板"+Guid.NewGuid(),unit}))).GetProperty("id").GetGuid();
        var a=await New("箱");var b=await New("个");var other=await New("箱");
        foreach(var (id,quantity) in new[]{(a,3),(b,7),(other,19)}) await Data(await c.PostAsJsonAsync("/api/v1/movements",new{requestId=Guid.NewGuid(),productId=id,kind="Inbound",quantity}));
        var data=await Data(await c.PostAsJsonAsync("/api/v1/analytics/query",new{productIds=new[]{a,b,a}}));
        Assert.Equal(2,data.GetProperty("rows").GetArrayLength());Assert.DoesNotContain(data.GetProperty("rows").EnumerateArray(),p=>p.GetProperty("id").GetGuid()==other);
        Assert.Equal(10L,data.GetProperty("rows").EnumerateArray().Sum(p=>p.GetProperty("inbound").GetInt64()));
        Assert.Equal(2,data.GetProperty("trend").EnumerateArray().Sum(p=>p.GetProperty("inboundCount").GetInt32()));
        var single=await Data(await c.PostAsJsonAsync("/api/v1/analytics/query",new{productIds=new[]{a}}));Assert.Single(single.GetProperty("rows").EnumerateArray());
        var all=await Data(await c.PostAsJsonAsync("/api/v1/analytics/query",new{productIds=Array.Empty<Guid>()}));Assert.Contains(all.GetProperty("rows").EnumerateArray(),p=>p.GetProperty("id").GetGuid()==other);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/v1/analytics/query",new{productIds=new[]{Guid.Empty}})).StatusCode);
    }
    private static async Task<JsonElement> Data(HttpResponseMessage r) { var body=await r.Content.ReadAsStringAsync();Assert.True(r.IsSuccessStatusCode,body);return JsonDocument.Parse(body).RootElement.GetProperty("data").Clone(); }
    private async Task<HttpClient> Login() {
        var c=fixture.Factory.CreateClient();
        var csrf=await Data(await c.GetAsync("/api/v1/auth/csrf"));c.DefaultRequestHeaders.Add("X-XSRF-TOKEN",csrf.GetProperty("requestToken").GetString());
        await Data(await c.PostAsJsonAsync("/api/v1/auth/login",new{userName="admin",password="TestOwnerPass2026!"}));
        csrf=await Data(await c.GetAsync("/api/v1/auth/csrf"));c.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");c.DefaultRequestHeaders.Add("X-XSRF-TOKEN",csrf.GetProperty("requestToken").GetString());return c;
    }
    [Fact]
    public async Task Warning_threshold_boundaries_persist_and_audit_has_no_technical_fields() {
        using var c=await Login();
        var p=await Data(await c.PostAsJsonAsync("/api/v1/products/",new{name="预警蓝色"+Guid.NewGuid(),unit="箱",note="测试备注"}));var id=p.GetProperty("id").GetGuid();
        Assert.Equal(JsonValueKind.Null,p.GetProperty("warningQuantity").ValueKind);
        p=await Data(await c.PutAsJsonAsync($"/api/v1/products/{id}/warning",new{warningQuantity=5,version=p.GetProperty("version").GetInt64()}));
        async Task<JsonElement> Report()=> (await Data(await c.GetAsync($"/api/v1/analytics?productId={id}"))).GetProperty("rows")[0];
        Assert.True((await Report()).GetProperty("isLowStock").GetBoolean());
        await Data(await c.PostAsJsonAsync("/api/v1/movements",new{requestId=Guid.NewGuid(),productId=id,kind="Inbound",quantity=5}));
        Assert.False((await Report()).GetProperty("isLowStock").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict,(await c.PutAsJsonAsync($"/api/v1/products/{id}/warning",new{warningQuantity=6,version=p.GetProperty("version").GetInt64()})).StatusCode);
        p=(await Data(await c.GetAsync("/api/v1/products/"))).EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==id);
        foreach(var invalid in new[]{-1m,.0000001m,1.5m,2147483648m})Assert.Equal(HttpStatusCode.BadRequest,(await c.PutAsJsonAsync($"/api/v1/products/{id}/warning",new{warningQuantity=invalid,version=p.GetProperty("version").GetInt64()})).StatusCode);
        p=await Data(await c.PutAsJsonAsync($"/api/v1/products/{id}/warning",new{warningQuantity=(decimal?)null,version=p.GetProperty("version").GetInt64()}));
        Assert.False((await Report()).GetProperty("isLowStock").GetBoolean());Assert.Equal(5,p.GetProperty("quantity").GetDecimal());
        var logs=await Data(await c.GetAsync("/api/v1/audit-logs?search=设置库存预警"));Assert.True(logs.GetProperty("total").GetInt32()>0);
        Assert.All(logs.GetProperty("items").EnumerateArray(),x=>{Assert.False(x.TryGetProperty("detail",out _));Assert.False(x.TryGetProperty("traceId",out _));});
    }
    [Fact]
    public async Task Analytics_filters_units_and_fills_days_without_operations() {
        using var c=await Login();var unique=Guid.NewGuid().ToString("N")[..8];var unit="箱"+unique;
        async Task<Guid> New(string u)=>(await Data(await c.PostAsJsonAsync("/api/v1/products/",new{name="看板"+Guid.NewGuid(),unit=u,note="可见备注"}))).GetProperty("id").GetGuid();
        var a=await New(unit);var b=await New("个"+unique);
        foreach(var id in new[]{a,b})await Data(await c.PostAsJsonAsync("/api/v1/movements",new{requestId=Guid.NewGuid(),productId=id,kind="Inbound",quantity=3}));
        var end=DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));var start=end.AddDays(-2);
        var data=await Data(await c.GetAsync($"/api/v1/analytics?from={start:yyyy-MM-dd}&to={end:yyyy-MM-dd}&unit={Uri.EscapeDataString(unit)}"));
        var row=Assert.Single(data.GetProperty("rows").EnumerateArray());Assert.Equal(a,row.GetProperty("id").GetGuid());Assert.Equal(3,row.GetProperty("inbound").GetDecimal());Assert.Equal(1,row.GetProperty("inboundCount").GetInt32());Assert.Equal("可见备注",row.GetProperty("note").GetString());
        Assert.Equal(3,data.GetProperty("trend").GetArrayLength());Assert.Equal(0,data.GetProperty("trend")[0].GetProperty("inbound").GetDecimal());
    }
    [Fact]
    public async Task Additive_upgrade_is_repeatable_and_preserves_existing_inventory() {
        using var scope=fixture.Factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var p=new Product{Code="UPGRADE"+Guid.NewGuid(),Name="保留数据",Unit="箱",Quantity=7,Note="保留备注"};db.Products.Add(p);await db.SaveChangesAsync();
        // This fixture owns a random ls_lite_test_* schema. No business schema is touched.
        var schema=await db.Database.SqlQueryRaw<string>("SELECT current_schema() AS \"Value\"").SingleAsync();Assert.StartsWith("ls_lite_test_",schema);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE products DROP COLUMN warning_quantity; ALTER TABLE products ALTER COLUMN quantity TYPE numeric(20,6)");
        await SchemaUpgrades.ApplyAsync(db);await SchemaUpgrades.ApplyAsync(db);db.ChangeTracker.Clear();
        var preserved=await db.Products.SingleAsync(x=>x.Id==p.Id);Assert.Equal(7,preserved.Quantity);Assert.Equal("保留备注",preserved.Note);Assert.Null(preserved.WarningQuantity);
        var type=await db.Database.SqlQueryRaw<string>("SELECT format_type(atttypid,atttypmod) AS \"Value\" FROM pg_attribute WHERE attrelid='products'::regclass AND attname='quantity'").SingleAsync();Assert.Equal("integer",type);
    }

    [Fact]
    public async Task Integer_upgrade_refuses_fractional_history_without_rounding() {
        using var scope=fixture.Factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var schema=await db.Database.SqlQueryRaw<string>("SELECT current_schema() AS \"Value\"").SingleAsync();Assert.StartsWith("ls_lite_test_",schema);
        var p=new Product{Code="FRACTION"+Guid.NewGuid(),Name="检查小数保护",Unit="箱",Quantity=8};db.Products.Add(p);await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE products ALTER COLUMN quantity TYPE numeric(20,6)");
        try {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE products SET quantity=8.5 WHERE id={p.Id}");
            await Assert.ThrowsAsync<InvalidOperationException>(()=>SchemaUpgrades.ApplyAsync(db));
            var value=await db.Database.SqlQuery<decimal>($"SELECT quantity AS \"Value\" FROM products WHERE id={p.Id}").SingleAsync();Assert.Equal(8.5m,value);
        } finally {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE products SET quantity=8 WHERE id={p.Id}");
            await SchemaUpgrades.ApplyAsync(db);
        }
    }

    [Fact]
    public async Task All_quantity_endpoints_reject_fractions_and_overflow_without_stock_changes() {
        using var c=await Login();var p=await Data(await c.PostAsJsonAsync("/api/v1/products/",new{name="整数边界"+Guid.NewGuid(),unit="箱"}));var id=p.GetProperty("id").GetGuid();
        foreach(var invalid in new[]{1.5m,2147483648m}) {
            Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/v1/movements",new{requestId=Guid.NewGuid(),productId=id,kind="Inbound",quantity=invalid})).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync($"/api/v1/stocktakes/{Guid.NewGuid()}/confirm",new{counts=new[]{new{productId=id,quantity=invalid}}})).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/v1/products/import/confirm",new{requestId=Guid.NewGuid(),fileName="invalid.xlsx",rows=new[]{new{name="不可导入"+Guid.NewGuid(),productSpecification="a",rawMaterialSpecification="b",unit="箱",currentQuantity=invalid}}})).StatusCode);
        }
        await Data(await c.PostAsJsonAsync("/api/v1/movements",new{requestId=Guid.NewGuid(),productId=id,kind="Inbound",quantity=int.MaxValue}));
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/v1/movements",new{requestId=Guid.NewGuid(),productId=id,kind="Inbound",quantity=1})).StatusCode);
        await Data(await c.PostAsJsonAsync("/api/v1/movements",new{requestId=Guid.NewGuid(),productId=id,kind="Outbound",quantity=1}));
        await Data(await c.PostAsJsonAsync("/api/v1/movements",new{requestId=Guid.NewGuid(),productId=id,kind="Inbound",quantity=1}));
        var report=(await Data(await c.GetAsync($"/api/v1/analytics?productId={id}"))).GetProperty("rows")[0];
        Assert.Equal(int.MaxValue,report.GetProperty("currentQuantity").GetInt32());Assert.Equal((long)int.MaxValue+1,report.GetProperty("inbound").GetInt64());
    }
}
