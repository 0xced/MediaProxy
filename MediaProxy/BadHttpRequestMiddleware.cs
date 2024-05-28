using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Net.Http.Headers;

namespace MediaProxy;

public class BadHttpRequestMiddleware : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (BadHttpRequestException exception)
        {
            var response = context.GetResponse();
            response.StatusCode = exception.StatusCode;
            response.Headers[HeaderNames.ContentType] = "text/plain; charset=utf-8";
            await response.BodyWriter.WriteAsync(Encoding.UTF8.GetBytes(exception.Message), context.CancellationToken);
        }
    }
}