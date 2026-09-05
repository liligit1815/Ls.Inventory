using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Ls.Inventory.Api.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Ls.Inventory.Api.Endpoints;
public static class AuthEndpoints
{
    public const string CookieName = "LS-LITE-AUTH";
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/auth");
        group.MapGet("/csrf", (HttpContext c, IAntiforgery a) => EndpointSupport.Ok(c, new { requestToken = a.GetAndStoreTokens(c).RequestToken }));
        group.MapPost("/login", Login).RequireRateLimiting("login");
        group.MapGet("/me", async (HttpContext c, InventoryDbContext db) => EndpointSupport.Ok(c,
            Current(await db.Users.SingleAsync(x => x.Id == EndpointSupport.UserId(c))))).RequireAuthorization();
        group.MapPost("/logout", async (HttpContext c, InventoryDbContext db) => {
            var sid = Guid.Parse(c.User.FindFirstValue("sid")!);
            var session = await db.LoginSessions.FindAsync(sid);
            if (session is not null) session.Revoked = true;
            EndpointSupport.Audit(db, c, "退出登录", new { });
            await db.SaveChangesAsync();
            c.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/", SameSite = SameSiteMode.Strict });
            return EndpointSupport.Ok(c, new { });
        }).RequireAuthorization();
        group.MapPost("/change-password", ChangePassword).RequireAuthorization();
    }
    private static object Current(AppUser u) => new { u.Id, u.UserName, u.DisplayName, u.MustChangePassword };

    private static async Task<IResult> Login(LoginRequest request, HttpContext c, InventoryDbContext db,
        PasswordHasher<AppUser> hasher, IConfiguration configuration, IHostEnvironment env)
    {
        var name = InventoryRules.Text(request.UserName, 80, "用户名", true).ToLowerInvariant();
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length > 256) throw new BusinessRuleException("INVALID_CREDENTIALS", "用户名或密码错误", 401);
        await using var tx = await db.Database.BeginTransactionAsync();
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE user_name = {name} FOR UPDATE").SingleOrDefaultAsync();
        if (user?.LockedUntil is { } expired && expired <= DateTimeOffset.UtcNow) { user.FailedAttempts = 0; user.LockedUntil = null; }
        if (user is null || !user.IsActive || user.LockedUntil > DateTimeOffset.UtcNow ||
            hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        {
            if (user is not null && !(user.LockedUntil > DateTimeOffset.UtcNow)) {
                user.FailedAttempts++;
                if (user.FailedAttempts >= 5) user.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(15);
            }
            EndpointSupport.Audit(db, c, "登录失败", new { reason = "用户名或密码错误，或账号暂时锁定" }, user);
            await db.SaveChangesAsync(); await tx.CommitAsync();
            throw new BusinessRuleException("INVALID_CREDENTIALS", "用户名或密码错误，或账号已暂时锁定", 401);
        }
        user.FailedAttempts = 0; user.LockedUntil = null;
        var session = Issue(c, db, user, request.RememberMe, configuration, env);
        EndpointSupport.Audit(db, c, "登录成功", new { session.Id }, user);
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return EndpointSupport.Ok(c, Current(user));
    }

    private static LoginSession Issue(HttpContext c, InventoryDbContext db, AppUser user, bool remember, IConfiguration config, IHostEnvironment env)
    {
        var session = new LoginSession { UserId = user.Id, ExpiresAt = DateTimeOffset.UtcNow.AddHours(remember ? 24 : 8) };
        db.LoginSessions.Add(session);
        var token = new JwtSecurityToken("LS.Inventory.Lite", "LS.Inventory.Web",
            [new Claim("sub", user.Id.ToString()), new Claim("sid", session.Id.ToString()), new Claim("name", user.UserName), new Claim("jti", Guid.NewGuid().ToString())],
            DateTime.UtcNow, session.ExpiresAt.UtcDateTime,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:SigningKey"]!)), SecurityAlgorithms.HmacSha256));
        c.Response.Cookies.Append(CookieName, new JwtSecurityTokenHandler().WriteToken(token), new CookieOptions {
            HttpOnly = true, Secure = !env.IsDevelopment() || c.Request.IsHttps, SameSite = SameSiteMode.Strict,
            Path = "/", Expires = remember ? session.ExpiresAt : null
        });
        return session;
    }

    private static async Task<IResult> ChangePassword(ChangePasswordRequest request, HttpContext c, InventoryDbContext db,
        PasswordHasher<AppUser> hasher, IConfiguration config, IHostEnvironment env)
    {
        if (request.NewPassword is null || request.NewPassword.Length is < 6 or > 128)
            throw new BusinessRuleException("INVALID_PASSWORD", "新密码需为 6—128 位，支持纯数字");
        await using var tx = await db.Database.BeginTransactionAsync();
        var uid = EndpointSupport.UserId(c);
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE id = {uid} FOR UPDATE").SingleAsync();
        if (string.IsNullOrEmpty(request.CurrentPassword) || hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
            throw new BusinessRuleException("INVALID_PASSWORD", "当前密码不正确");
        if (request.CurrentPassword == request.NewPassword) throw new BusinessRuleException("INVALID_PASSWORD", "新密码不能与原密码相同");
        user.PasswordHash = hasher.HashPassword(user, request.NewPassword); user.MustChangePassword = false;
        await db.LoginSessions.Where(x => x.UserId == uid && !x.Revoked).ExecuteUpdateAsync(s => s.SetProperty(x => x.Revoked, true));
        Issue(c, db, user, false, config, env);
        EndpointSupport.Audit(db, c, "修改密码", new { });
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return EndpointSupport.Ok(c, new { });
    }
    public sealed record LoginRequest(string UserName, string Password, bool RememberMe);
    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
}
