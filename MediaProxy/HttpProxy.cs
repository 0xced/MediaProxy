using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace MediaProxy;

public class HttpProxy
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;

    public HttpProxy(ILoggerFactory loggerFactory, IHttpClientFactory clientFactory)
    {
        _logger = loggerFactory.CreateLogger<HttpProxy>();
        _httpClient = clientFactory.CreateClient("proxy");
    }

    [Function("proxy")]
    public async Task<HttpResponseData> RunAsync([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req, FunctionContext executionContext)
    {
        var cancellationToken = executionContext.CancellationToken;

        _logger.LogInformation("C# HTTP trigger function processed a request");

        var url = req.Query["url"];
        if (url == null)
        {
            return await BadRequestAsync(req, "An URL must be specified in the `url` query parameter", cancellationToken);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return await BadRequestAsync(req, $"The URL ({url}) is invalid", cancellationToken);
        }

        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        foreach (var (headerKey, headerValue) in req.Headers.Where(e => e.Key is "Accept" or "Range"))
        {
            request.Headers.Add(headerKey, headerValue);
        }
        var response = await _httpClient.SendAsync(request, cancellationToken);

        var result = req.CreateResponse(response.StatusCode);

        if (response.Content.Headers.TryGetValues("Content-Type", out var contentType))
        {
            result.Headers.Add("Content-Type", contentType);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        if (response.Content.Headers.ContentType?.MediaType == "application/x-mpegURL")
        {
            using var reader = new StreamReader(stream);
            string? previousLine = null;
            while (await reader.ReadLineAsync(cancellationToken) is {} line)
            {
                if (previousLine != null && (previousLine.StartsWith("#EXT-X-STREAM-INF") || previousLine.StartsWith("#EXTINF")))
                {
                    await result.WriteStringAsync($"{GetProxyUrl(req, uri, line)}\n", cancellationToken);
                }
                else
                {
                    const string pattern = "URI=\"([^\"]+)\"";
                    var match = Regex.Match(line, pattern);
                    if (match.Success)
                    {
                        var replacedLine = Regex.Replace(line, pattern, $"URI=\"{GetProxyUrl(req, uri, match.Groups[1].Value)}\"");
                        await result.WriteStringAsync($"{replacedLine}\n", cancellationToken);
                    }
                    else
                    {
                        await result.WriteStringAsync($"{line}\n", cancellationToken);
                    }
                }
                previousLine = line;
            }
        }
        else
        {
            await stream.CopyToAsync(result.Body, cancellationToken);
        }

        return result;
    }

    private static async Task<HttpResponseData> BadRequestAsync(HttpRequestData req, string error, CancellationToken cancellationToken)
    {
        var result = req.CreateResponse(HttpStatusCode.BadRequest);
        result.Headers.Add("Content-Type", "text/plain; charset=utf-8");
        await result.WriteStringAsync($"❌ {error}\n", cancellationToken);
        return result;
    }

    private static string GetProxyUrl(HttpRequestData req, Uri baseUri, string relativeUri)
    {
        var absoluteUrl = Uri.TryCreate(relativeUri, UriKind.Absolute, out var absoluteUri) ? absoluteUri : new Uri(baseUri, relativeUri);
        var escapedUrl = Uri.EscapeDataString(absoluteUrl.AbsoluteUri);
        var proxyUrl = $"{req.Url.GetLeftPart(UriPartial.Path)}?url={escapedUrl}";
        var code = req.Query["code"];
        return code == null ? proxyUrl : $"{proxyUrl}&code={code}";
    }
}