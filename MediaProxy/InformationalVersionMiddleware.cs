using System.Reflection;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace MediaProxy;

public class InformationalVersionMiddleware : IFunctionsWorkerMiddleware
{
    private static readonly string? InformationalVersion = typeof(InformationalVersionMiddleware).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var httpContext = context.GetHttpContext();
        if (httpContext != null)
        {
            httpContext.Response.Headers["X-InformationalVersion"] = InformationalVersion;
        }
        await next(context);
    }
}