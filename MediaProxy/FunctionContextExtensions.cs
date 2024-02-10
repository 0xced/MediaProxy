using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;

namespace MediaProxy;

internal static class FunctionContextExtensions
{
    public static (HttpRequest Request, HttpResponse Response) GetRequestAndResponse(this FunctionContext context)
    {
        var httpContext = GetHttpContext(context);
        return (httpContext.Request, httpContext.Response);
    }

    public static HttpRequest GetRequest(this FunctionContext context)
        => GetHttpContext(context).Request;

    public static HttpResponse GetResponse(this FunctionContext context)
        => GetHttpContext(context).Response;

    private static HttpContext GetHttpContext(FunctionContext context)
        => context.GetHttpContext() ?? throw new InvalidOperationException("HttpContext is not available");
}