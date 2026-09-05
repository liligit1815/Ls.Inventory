using Microsoft.AspNetCore.Antiforgery;

namespace Ls.Inventory.Api.Middleware;

public sealed class SpaAntiforgeryMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (context.Request.Path.StartsWithSegments("/api/v1") &&
            !SafeMethods.Contains(context.Request.Method))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ApiProblem.Create(
                        context,
                        StatusCodes.Status400BadRequest,
                        "INVALID_CSRF_TOKEN",
                        "页面安全令牌已失效，请刷新页面后重试")
                    .ExecuteAsync(context);
                return;
            }
        }

        await next(context);
    }
}
