using Microsoft.AspNetCore.Mvc;

namespace Ls.Inventory.Api;

public static class ApiProblem
{
    public static IResult Create(
        HttpContext context,
        int statusCode,
        string errorCode,
        string message)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = message,
            Type = $"https://ls-inventory.local/problems/{errorCode.ToLowerInvariant()}",
            Instance = context.Request.Path
        };
        problem.Extensions["errorCode"] = errorCode;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        return Results.Problem(problem);
    }
}
