using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ls.Inventory.Api;
using Ls.Inventory.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;
using Xunit;

namespace Ls.Inventory.IntegrationTests;

[CollectionDefinition("Postgres", DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<LiteDatabaseFixture>;

public sealed class LiteDatabaseFixture : IAsyncLifetime
{
    private readonly string database = "ls_lite_test_" + Guid.NewGuid().ToString("N");
    private string adminConnection = "";
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public async Task InitializeAsync()
    {
        var supplied = Environment.GetEnvironmentVariable("LS_TEST_ADMIN_CONNECTION")
            ?? throw new InvalidOperationException("Set LS_TEST_ADMIN_CONNECTION to a development PostgreSQL connection with schema-creation privileges. Tests create an isolated ls_lite_test_* schema.");
        var cs = new NpgsqlConnectionStringBuilder(supplied) { Pooling = false };
        adminConnection = cs.ConnectionString;
        await using (var admin = new NpgsqlConnection(adminConnection)) {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand("CREATE SCHEMA " + new NpgsqlCommandBuilder().QuoteIdentifier(database), admin);
            await create.ExecuteNonQueryAsync();
        }
        cs.SearchPath = database;
        Factory = new LiteFactory(cs.ConnectionString);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        var user = new AppUser { UserName = "admin", DisplayName = "测试店主", MustChangePassword = false };
        user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, "TestOwnerPass2026!");
        db.Users.Add(user); await db.SaveChangesAsync();
    }
    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        if (string.IsNullOrEmpty(adminConnection)) return;
        if (!database.StartsWith("ls_lite_test_", StringComparison.Ordinal) || database.Length != 45) throw new InvalidOperationException("Unsafe test cleanup target");
        await using var admin = new NpgsqlConnection(adminConnection); await admin.OpenAsync();
        await using var drop = new NpgsqlCommand("DROP SCHEMA IF EXISTS " + new NpgsqlCommandBuilder().QuoteIdentifier(database) + " CASCADE", admin);
        await drop.ExecuteNonQueryAsync();
    }
    private sealed class LiteFactory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Inventory", connection);
            builder.UseSetting("DatabaseInitialization:Initialize", "false");
            builder.UseSetting("BootstrapAdmin:Password", "TestOwnerPass2026!");
            builder.UseSetting("Jwt:SigningKey", "Isolated-test-key-not-for-production-12345678901234");
            builder.ConfigureServices(services => {
                // Business tests share one test IP; their logins must not exhaust each other's quota.
                services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
                services.AddRateLimiter(options => options.AddFixedWindowLimiter("login", limiter => {
                    limiter.PermitLimit = 1000;
                    limiter.Window = TimeSpan.FromMinutes(1);
                }));
            });
        }
    }
}

[Collection("Postgres")]
public sealed partial class LiteFlowTests(LiteDatabaseFixture fixture)
{
    private string lastAuthCookie = "";
    private async Task<HttpClient> Login()
    {
        var client = fixture.Factory.CreateClient();
        await Csrf(client);
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = "admin", password = "TestOwnerPass2026!", rememberMe = false });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("LS-LITE-AUTH="));
        lastAuthCookie = cookie.Split(';')[0];
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, cookie.Split(';')[0].Split('=')[1].Split('.').Length);
        await Csrf(client); return client;
    }
    private static async Task Csrf(HttpClient c)
    {
        var value = await Data(await c.GetAsync("/api/v1/auth/csrf"));
        c.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        c.DefaultRequestHeaders.Add("X-XSRF-TOKEN", value.GetProperty("requestToken").GetString());
    }
    private static async Task<JsonElement> Data(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.GetProperty("data").Clone();
    }
    private static async Task<Guid> Product(HttpClient c) => (await Data(await c.PostAsJsonAsync("/api/v1/products/",
        new { name = "测试商品" + Guid.NewGuid().ToString("N"), productSpecification = "2.3", rawMaterialSpecification = "0.06*598", unit = "箱", isActive = true }))).GetProperty("id").GetGuid();
    private static Task<HttpResponseMessage> Move(HttpClient c, Guid p, string kind, decimal quantity, Guid? request = null) =>
        c.PostAsJsonAsync("/api/v1/movements", new { requestId = request ?? Guid.NewGuid(), productId = p, kind, quantity, note = "自动化验收" });
    private static async Task<JsonElement> Stocktake(HttpClient c)
    {
        var id = (await Data(await c.PostAsJsonAsync("/api/v1/stocktakes/", new { }))).GetProperty("id").GetGuid();
        return await Data(await c.GetAsync($"/api/v1/stocktakes/{id}"));
    }
    private static object Counts(JsonElement s, Guid product, decimal actual) => new { counts = s.GetProperty("lines").EnumerateArray().Select(x => new {
        productId = x.GetProperty("productId").GetGuid(), quantity = x.GetProperty("productId").GetGuid() == product ? actual : x.GetProperty("expectedQuantity").GetDecimal()
    }).ToArray() };

    [Fact]
    public async Task Movement_idempotency_balance_and_analytics_agree()
    {
        using var c = await Login(); var p = await Product(c);
        await Data(await Move(c, p, "Inbound", 10)); var key = Guid.NewGuid();
        Assert.Equal(7, (await Data(await Move(c, p, "Outbound", 3, key))).GetProperty("quantityAfter").GetDecimal());
        Assert.True((await Data(await Move(c, p, "Outbound", 3, key))).GetProperty("alreadyPosted").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await Move(c, p, "Outbound", 2, key)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Move(c, p, "Outbound", 8)).StatusCode);
        var ledger = await Data(await c.GetAsync($"/api/v1/movements?productId={p}")); Assert.Equal(2, ledger.GetProperty("total").GetInt32());
        var report = await Data(await c.GetAsync($"/api/v1/analytics?productId={p}")); var row = report.GetProperty("rows")[0];
        Assert.Equal(10, row.GetProperty("inbound").GetDecimal()); Assert.Equal(3, row.GetProperty("outbound").GetDecimal());
        Assert.Equal(7, row.GetProperty("currentQuantity").GetDecimal()); Assert.Equal(7, row.GetProperty("netChange").GetDecimal());
    }
    [Fact]
    public async Task Concurrent_outbound_cannot_overdraw()
    {
        using var c = await Login(); var p = await Product(c); await Data(await Move(c,p,"Inbound",5));
        var replies = await Task.WhenAll(Move(c,p,"Outbound",4), Move(c,p,"Outbound",4));
        Assert.Single(replies, x => x.IsSuccessStatusCode); Assert.Single(replies, x => x.StatusCode == HttpStatusCode.BadRequest);
        var r=await Data(await c.GetAsync($"/api/v1/analytics?productId={p}")); Assert.Equal(1,r.GetProperty("rows")[0].GetProperty("currentQuantity").GetDecimal());
    }
    [Fact]
    public async Task Concurrent_identical_request_posts_once()
    {
        using var c=await Login(); var p=await Product(c); var key=Guid.NewGuid();
        var replies=await Task.WhenAll(Move(c,p,"Inbound",4,key),Move(c,p,"Inbound",4,key));
        foreach(var reply in replies) await Data(reply);
        var r=await Data(await c.GetAsync($"/api/v1/movements?productId={p}")); Assert.Equal(1,r.GetProperty("total").GetInt32());
    }
    [Fact]
    public async Task Stocktake_adjustment_is_posted_once_and_audited()
    {
        using var c=await Login(); var p=await Product(c); await Data(await Move(c,p,"Inbound",10));
        var s=await Stocktake(c); var id=s.GetProperty("id").GetGuid(); var counts=Counts(s,p,7);
        await Data(await c.PostAsJsonAsync($"/api/v1/stocktakes/{id}/confirm",counts));
        Assert.True((await Data(await c.PostAsJsonAsync($"/api/v1/stocktakes/{id}/confirm",counts))).GetProperty("alreadyPosted").GetBoolean());
        var r=await Data(await c.GetAsync($"/api/v1/analytics?productId={p}")); var row=r.GetProperty("rows")[0];
        Assert.Equal(-3,row.GetProperty("adjustment").GetDecimal()); Assert.Equal(7,row.GetProperty("currentQuantity").GetDecimal());
        var logs=await Data(await c.GetAsync("/api/v1/audit-logs?search=确认盘库")); Assert.True(logs.GetProperty("total").GetInt32()>0);
    }
    [Fact]
    public async Task Stale_stocktake_does_not_overwrite_new_stock()
    {
        using var c=await Login(); var p=await Product(c); await Data(await Move(c,p,"Inbound",10));
        var s=await Stocktake(c); var id=s.GetProperty("id").GetGuid(); await Data(await Move(c,p,"Outbound",2));
        Assert.Equal(HttpStatusCode.Conflict,(await c.PostAsJsonAsync($"/api/v1/stocktakes/{id}/confirm",Counts(s,p,7))).StatusCode);
        await Data(await c.PostAsJsonAsync($"/api/v1/stocktakes/{id}/cancel",new{}));
        var r=await Data(await c.GetAsync($"/api/v1/analytics?productId={p}")); Assert.Equal(8,r.GetProperty("rows")[0].GetProperty("currentQuantity").GetDecimal());
    }
    [Fact]
    public async Task Jwt_logout_revokes_session_and_legacy_routes_are_gone()
    {
        using var c=await Login(); await Data(await c.GetAsync("/api/v1/auth/me"));
        Assert.Equal(HttpStatusCode.NotFound,(await c.GetAsync("/api/v1/excel-import/template")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await c.GetAsync("/api/v1/warehouses/")).StatusCode);
        await Data(await c.PostAsJsonAsync("/api/v1/auth/logout",new{}));
        Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/api/v1/auth/me")).StatusCode);
        using var replay=fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies=false });
        replay.DefaultRequestHeaders.Add("Cookie",lastAuthCookie);
        Assert.Equal(HttpStatusCode.Unauthorized,(await replay.GetAsync("/api/v1/auth/me")).StatusCode);
    }
    [Fact]
    public async Task Missing_csrf_prevents_write()
    {
        using var c=await Login(); c.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/v1/products/",new {name="forbidden",unit="箱"})).StatusCode);
    }
    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(0.0000001)]
    public void Invalid_movement_quantities_rejected(decimal quantity) => Assert.Throws<BusinessRuleException>(()=>InventoryRules.Quantity(quantity));
}
