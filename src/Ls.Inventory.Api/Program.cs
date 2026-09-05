using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Ls.Inventory.Api;
using Ls.Inventory.Api.Data;
using Ls.Inventory.Api.Endpoints;
using Ls.Inventory.Api.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var initializeOnly = args.Length == 1 && args[0] == "--initialize-database";
var connectionString = builder.Configuration.GetConnectionString("Inventory")
    ?? throw new InvalidOperationException("请配置 ConnectionStrings:Inventory");
var runtimeConnection = new NpgsqlConnectionStringBuilder(connectionString);
if (string.IsNullOrWhiteSpace(runtimeConnection.SearchPath)) runtimeConnection.SearchPath = "lite";
connectionString = runtimeConnection.ConnectionString;
if (args.Length == 1 && args[0] == "--check-integer-quantities") {
    await using var checkDb = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>().UseNpgsql(connectionString).Options);
    await SchemaUpgrades.ValidateIntegerQuantitiesAsync(checkDb);
    Console.WriteLine("现有全部数量字段均为 int 范围内的整数，可以无损升级。");
    return;
}
if (args.Length == 1 && args[0] == "--database-info") {
    await using var check = new NpgsqlConnection(connectionString); await check.OpenAsync();
    await using var command = new NpgsqlCommand("SELECT current_database(), current_user, pg_get_userbyid(datdba), (SELECT rolcreatedb FROM pg_roles WHERE rolname = current_user), (SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public') FROM pg_database WHERE datname = current_database()", check);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) Console.WriteLine($"Database={reader.GetString(0)}; User={reader.GetString(1)}; Owner={reader.GetString(2)}; CreateDatabase={reader.GetBoolean(3)}; PublicTables={reader.GetInt64(4)}");
    await reader.CloseAsync();
    await using var schemas = new NpgsqlCommand("SELECT nspname FROM pg_namespace WHERE nspname NOT LIKE 'pg_%' AND nspname <> 'information_schema' ORDER BY nspname", check);
    await using var schemaReader = await schemas.ExecuteReaderAsync();
    while (await schemaReader.ReadAsync()) Console.WriteLine("Schema=" + schemaReader.GetString(0));
    return;
}
if (args.Length == 2 && args[0] == "--reset-structure")
{
    var connection = new NpgsqlConnectionStringBuilder(builder.Configuration.GetConnectionString("InventoryMigration") ?? connectionString);
    var target = runtimeConnection.Database ?? "";
    if (!builder.Environment.IsDevelopment() || target != args[1] || !target.StartsWith("ls_", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("仅允许在开发环境重建明确指定的 LS 项目库");
    if (connection.Database != target || connection.Host != runtimeConnection.Host || connection.Port != runtimeConnection.Port)
        throw new InvalidOperationException("结构管理连接与运行连接必须指向同一个项目库");
    await using var admin = new NpgsqlConnection(connection.ConnectionString);
    await admin.OpenAsync();
    await using var tx = await admin.BeginTransactionAsync();
    var schemaNames = new List<string>();
    await using (var query = new NpgsqlCommand("SELECT nspname FROM pg_namespace WHERE nspname NOT LIKE 'pg_%' AND nspname <> 'information_schema'", admin, tx))
    await using (var reader = await query.ExecuteReaderAsync()) while (await reader.ReadAsync()) schemaNames.Add(reader.GetString(0));
    foreach (var schema in schemaNames) {
        var identifier = new NpgsqlCommandBuilder().QuoteIdentifier(schema);
        await using var drop = new NpgsqlCommand($"DROP SCHEMA {identifier} CASCADE", admin, tx); await drop.ExecuteNonQueryAsync();
    }
    await using (var create = new NpgsqlCommand("CREATE SCHEMA lite", admin, tx)) await create.ExecuteNonQueryAsync();
    await tx.CommitAsync();
    Console.WriteLine($"已清空项目库 {target} 的原有结构与数据，保留数据库容器并创建 lite 结构空间。");
    return;
}
var signingKey = builder.Configuration["Jwt:SigningKey"] ?? "";
if (Encoding.UTF8.GetByteCount(signingKey) < 32 || signingKey.StartsWith("REPLACE_", StringComparison.Ordinal))
    throw new InvalidOperationException("Jwt:SigningKey 需配置至少 32 字节的随机密钥，不能使用示例占位值");
builder.Services.AddDbContext<InventoryDbContext>(o => o.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
builder.Services.AddScoped<PasswordHasher<AppUser>>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddAntiforgery(o => {
    o.HeaderName = "X-XSRF-TOKEN";
    o.Cookie.Name = "LS-LITE-XSRF";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => {
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters {
        ValidateIssuer = true, ValidIssuer = "LS.Inventory.Lite",
        ValidateAudience = true, ValidAudience = "LS.Inventory.Web",
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(15), NameClaimType = "name",
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
    };
    o.Events = new JwtBearerEvents {
        OnMessageReceived = c => { c.Token = c.Request.Cookies[AuthEndpoints.CookieName]; return Task.CompletedTask; },
        OnTokenValidated = async c => {
            if (!Guid.TryParse(c.Principal?.FindFirstValue("sid"), out var sid) || !Guid.TryParse(c.Principal?.FindFirstValue("sub"), out var uid)) { c.Fail("Invalid session"); return; }
            var db = c.HttpContext.RequestServices.GetRequiredService<InventoryDbContext>();
            var active = await db.LoginSessions.AnyAsync(x => x.Id == sid && x.UserId == uid && !x.Revoked && x.ExpiresAt > DateTimeOffset.UtcNow && x.User.IsActive);
            if (!active) c.Fail("Session expired");
        },
        OnChallenge = async c => { c.HandleResponse(); await ApiProblem.Create(c.HttpContext, 401, "UNAUTHENTICATED", "登录状态已失效，请重新登录").ExecuteAsync(c.HttpContext); }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o => {
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<SpaAntiforgeryMiddleware>();
// Initial-password accounts cannot bypass the mandatory password change through direct API calls.
app.Use(async (c, next) => {
    if (c.User.Identity?.IsAuthenticated == true && c.Request.Path.StartsWithSegments("/api/v1") && !c.Request.Path.StartsWithSegments("/api/v1/auth")) {
        var db = c.RequestServices.GetRequiredService<InventoryDbContext>();
        if (await db.Users.AnyAsync(x => x.Id == EndpointSupport.UserId(c) && x.MustChangePassword)) {
            await ApiProblem.Create(c, 403, "PASSWORD_CHANGE_REQUIRED", "请先修改初始密码").ExecuteAsync(c); return;
        }
    }
    await next(c);
});
app.MapGet("/health/live", () => Results.Ok(new { status = "healthy", edition = "lite" }));
app.MapGet("/health/ready", async (InventoryDbContext db) => await db.Database.CanConnectAsync()
    ? Results.Ok(new { status = "ready", edition = "lite" }) : Results.StatusCode(503));
app.MapAuthEndpoints();
app.MapProductEndpoints();
app.MapProductImportEndpoints();
app.MapInventoryEndpoints();
app.MapStocktakeEndpoints();
// Only UI routes fall back to the SPA; missing API routes and assets stay 404.
foreach (var page in new[] { "/", "/login", "/dashboard", "/analytics", "/stocktakes", "/products", "/stock-warnings", "/audit-logs" })
    app.MapFallbackToFile(page, "index.html");

if (initializeOnly || builder.Configuration.GetValue<bool>("DatabaseInitialization:Initialize")) {
    await using var scope = app.Services.CreateAsyncScope();
    var migrationConnection = new NpgsqlConnectionStringBuilder(builder.Configuration.GetConnectionString("InventoryMigration") ?? connectionString) { SearchPath = runtimeConnection.SearchPath };
    if (migrationConnection.Database != runtimeConnection.Database || migrationConnection.Host != runtimeConnection.Host || migrationConnection.Port != runtimeConnection.Port)
        throw new InvalidOperationException("结构管理连接与运行连接必须指向同一个项目库");
    await using var db = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>().UseNpgsql(migrationConnection.ConnectionString).UseSnakeCaseNamingConvention().Options);
    await db.Database.EnsureCreatedAsync();
    await SchemaUpgrades.ApplyAsync(db);
    if (!await db.Users.AnyAsync()) {
        var password = builder.Configuration["BootstrapAdmin:Password"];
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12) throw new InvalidOperationException("请配置至少 12 位的 BootstrapAdmin:Password");
        var user = new AppUser { UserName = "admin", DisplayName = "店主" };
        user.PasswordHash = scope.ServiceProvider.GetRequiredService<PasswordHasher<AppUser>>().HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }
    if (migrationConnection.Username != runtimeConnection.Username) {
        var userIdentifier = new NpgsqlCommandBuilder().QuoteIdentifier(runtimeConnection.Username!);
        // Runtime account has no schema-management or deletion privileges.
        await using var grantsConnection = new NpgsqlConnection(migrationConnection.ConnectionString);
        await grantsConnection.OpenAsync();
        await using var grants = new NpgsqlCommand($"GRANT USAGE ON SCHEMA lite TO {userIdentifier}; GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA lite TO {userIdentifier}; GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA lite TO {userIdentifier}; REVOKE UPDATE ON lite.movements, lite.audit_logs FROM {userIdentifier}", grantsConnection);
        await grants.ExecuteNonQueryAsync();
    }
}
if (initializeOnly) {
    Console.WriteLine("数据库结构与初始账号已检查完成，未启动网站。现有业务数据未清空。");
    return;
}
await app.RunAsync();
public partial class Program;
