using System.Security.Claims;
using System.Text.Json;
using Ls.Inventory.Api.Data;

namespace Ls.Inventory.Api.Endpoints;
public static class EndpointSupport
{
    public static Guid UserId(HttpContext c) => Guid.Parse(c.User.FindFirstValue("sub")!);
    public static IResult Ok(HttpContext c, object data) => Results.Ok(new { data, traceId = c.TraceIdentifier });
    public static void Audit(InventoryDbContext db, HttpContext c, string action, object detail, AppUser? user = null)
    {
        db.AuditLogs.Add(new AuditLog {
            UserId = user?.Id ?? (Guid.TryParse(c.User.FindFirstValue("sub"), out var id) ? id : null),
            UserName = user?.UserName ?? c.User.Identity?.Name ?? "未登录",
            Action = action, Detail = JsonSerializer.Serialize(detail),
            IpAddress = c.Connection.RemoteIpAddress?.ToString() ?? "", TraceId = c.TraceIdentifier
        });
    }
}
