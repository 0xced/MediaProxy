using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace MediaProxy;

public partial class HttpProxy
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;

    public HttpProxy(ILoggerFactory loggerFactory, IHttpClientFactory clientFactory)
    {
        _logger = loggerFactory.CreateLogger<HttpProxy>();
        _httpClient = clientFactory.CreateClient("proxy");
    }

    [Function("proxy")]
    public async Task<HttpResponseData> RunAsync([HttpTrigger(AuthorizationLevel.Function, "get", Route = "{*ignored}")] HttpRequestData req, FunctionContext executionContext, string ignored = "")
    {
        var cancellationToken = executionContext.CancellationToken;

        var url = req.Query["url"];
        if (url == null)
        {
            return await BadRequestAsync(req, "An URL must be specified in the `url` query parameter", cancellationToken);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return await BadRequestAsync(req, $"The URL ({url}) is invalid", cancellationToken);
        }

        var request = CreateRequest(req, uri);
        var response = await _httpClient.SendAsync(request, cancellationToken);

        var result = req.CreateResponse(response.StatusCode);

        if (response.Content.Headers.TryGetValues("Content-Type", out var contentType))
        {
            result.Headers.Add("Content-Type", contentType);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType != null && (mediaType.Equals("application/x-mpegurl", StringComparison.OrdinalIgnoreCase) ||
                                  mediaType.Equals("audio/mpegurl", StringComparison.OrdinalIgnoreCase) ||
                                  mediaType.Equals("application/vnd.apple.mpegurl", StringComparison.OrdinalIgnoreCase)))
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
                    var match = UriRegex().Match(line);
                    if (match.Success)
                    {
                        var uriMatch = match.Groups[1];
                        var proxyLine = $"{line[..uriMatch.Index]}{GetProxyUrl(req, uri, uriMatch.Value)}{line[(uriMatch.Index + uriMatch.Length)..]}";
                        await result.WriteStringAsync($"{proxyLine}\n", cancellationToken);
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

    private HttpRequestMessage CreateRequest(HttpRequestData req, Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        foreach (var (headerKey, headerValue) in req.Headers.Where(e => e.Key != "Host"))
        {
            request.Headers.Add(headerKey, headerValue);
        }

        _logger.LogInformation("GET {Uri}", uri);
        foreach (var (headerKey, headerValues) in request.Headers)
        {
            foreach (var headerValue in headerValues)
            {
                _logger.LogInformation("{HeaderKey}: {HeaderValue}", headerKey, headerValue);
            }
        }

        return request;
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

    [GeneratedRegex("URI=\"([^\"]+)\"")]
    private static partial Regex UriRegex();
}