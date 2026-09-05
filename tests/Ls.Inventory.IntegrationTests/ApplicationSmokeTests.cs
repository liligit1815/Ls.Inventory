using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Ls.Inventory.IntegrationTests;

public sealed class ApplicationSmokeTests : IClassFixture<ApplicationSmokeTests.InventoryApplicationFactory>
{
    private readonly HttpClient client;

    public ApplicationSmokeTests(InventoryApplicationFactory factory)
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task Live_health_endpoint_is_available()
    {
        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Csrf_endpoint_issues_a_request_token()
    {
        var response = await client.GetAsync("/api/v1/auth/csrf");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("requestToken", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Protected_endpoint_returns_readable_session_expired_problem()
    {
        var response = await client.GetAsync("/api/v1/products/");
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var problem = await JsonDocument.ParseAsync(stream);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("登录状态已失效，请重新登录", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("UNAUTHENTICATED", problem.RootElement.GetProperty("errorCode").GetString());
    }

    public sealed class InventoryApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Inventory", "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused");
            builder.UseSetting("DatabaseInitialization:Initialize", "false");
            builder.UseSetting("Jwt:SigningKey", "Test-only-signing-key-not-for-production-123456789");
        }
    }

    [Fact]
    public async Task Published_spa_routes_work_without_masking_api_or_asset_errors()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), "ls-spa-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(webRoot);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<!doctype html><title>LS publish test</title>");
            await File.WriteAllTextAsync(Path.Combine(webRoot, "app.css"), "body{color:blue}");
            using var factory = new InventoryApplicationFactory().WithWebHostBuilder(builder => {
                builder.UseEnvironment("Development");
                builder.UseWebRoot(webRoot);
            });
            using var browser = factory.CreateClient();
            foreach (var route in new[] { "/", "/login", "/dashboard", "/analytics", "/stocktakes", "/products", "/stock-warnings", "/audit-logs" })
            {
                var response = await browser.GetAsync(route);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains("LS publish test", await response.Content.ReadAsStringAsync());
            }
            Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/app.css")).StatusCode);
            foreach (var route in new[] { "/api/v1/unknown", "/api/v1/warehouses/", "/missing.js", "/appsettings.Production.json" })
                Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(route)).StatusCode);
        }
        finally { Directory.Delete(webRoot, recursive: true); }
    }
}
