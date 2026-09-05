using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ls.Inventory.Api.Middleware;

public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is BadHttpRequestException { StatusCode: 400 })
            exception = new BusinessRuleException("INVALID_REQUEST", "提交内容格式不正确；数量必须是 0 至 2147483647 的整数");
        if (exception is DbUpdateConcurrencyException || exception is DbUpdateException { InnerException: PostgresException { SqlState: "23505" } })
            exception = new BusinessRuleException("CONFLICT", "数据已变化或记录重复，请刷新后重试", 409);
        if (exception is BusinessRuleException businessException)
        {
            var businessProblem = new ProblemDetails
            {
                Status = businessException.StatusCode,
                Title = businessException.Message,
                Type = $"https://ls-inventory.local/problems/{businessException.ErrorCode.ToLowerInvariant()}",
                Instance = httpContext.Request.Path
            };
            businessProblem.Extensions["errorCode"] = businessException.ErrorCode;
            businessProblem.Extensions["traceId"] = httpContext.TraceIdentifier;
            httpContext.Response.StatusCode = businessException.StatusCode;
            await httpContext.Response.WriteAsJsonAsync(businessProblem, cancellationToken);
            return true;
        }

        logger.LogError(exception, "Unhandled request exception. TraceId: {TraceId}", httpContext.TraceIdentifier);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "系统处理请求时发生错误",
            Type = "https://ls-inventory.local/problems/internal-error",
            Instance = httpContext.Request.Path
        };
        problem.Extensions["errorCode"] = "INTERNAL_ERROR";
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        if (environment.IsDevelopment())
        {
            problem.Extensions["developerMessage"] = exception.Message;
        }

        httpContext.Response.StatusCode = problem.Status.Value;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
