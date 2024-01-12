using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace MediaProxy;

public partial class HttpProxy
{
    // See https://datatracker.ietf.org/doc/html/draft-ietf-httpbis-p1-messaging-14#section-7.1.3.1
    private static readonly string[] HopByHopHeaders = [
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization", "TE", "Trailer", "Transfer-Encoding", "Upgrade"
    ];

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
        var httpResponse = await _httpClient.SendAsync(request, cancellationToken);
        var response = CreateResponse(req, httpResponse);

        await using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);

        var mediaType = httpResponse.Content.Headers.ContentType?.MediaType;
        if (mediaType != null && (mediaType.Equals("application/x-mpegurl", StringComparison.OrdinalIgnoreCase) ||
                                  mediaType.Equals("audio/mpegurl", StringComparison.OrdinalIgnoreCase) ||
                                  mediaType.Equals("application/vnd.apple.mpegurl", StringComparison.OrdinalIgnoreCase)))
        {
            using var reader = new StreamReader(stream);
            string? previousLine = null;
            var contentLength = 0;
            while (await reader.ReadLineAsync(cancellationToken) is {} line)
            {
                int lineLength;
                if (previousLine != null && (previousLine.StartsWith("#EXT-X-STREAM-INF") || previousLine.StartsWith("#EXTINF")))
                {
                    lineLength = await response.WriteLineAsync(GetProxyUrl(req, uri, line), cancellationToken);
                }
                else
                {
                    var match = UriRegex().Match(line);
                    if (match.Success)
                    {
                        var uriMatch = match.Groups[1];
                        var proxyLine = $"{line[..uriMatch.Index]}{GetProxyUrl(req, uri, uriMatch.Value)}{line[(uriMatch.Index + uriMatch.Length)..]}";
                        lineLength = await response.WriteLineAsync(proxyLine, cancellationToken);
                    }
                    else
                    {
                        lineLength = await response.WriteLineAsync(line, cancellationToken);
                    }
                }
                contentLength += lineLength;
                previousLine = line;
            }
            response.Headers.Remove("Content-Length");
            response.Headers.Add("Content-Length", contentLength.ToString(NumberFormatInfo.InvariantInfo));
        }
        else
        {
            await stream.CopyToAsync(response.Body, cancellationToken);
        }

        return response;
    }

    private HttpRequestMessage CreateRequest(HttpRequestData req, Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        // Remove Hop-by-hop Headers, see https://www.mnot.net/blog/2011/07/11/what_proxies_must_do.html#1-remove-hop-by-hop-headers
        foreach (var (headerKey, headerValue) in req.Headers.Where(e => ShouldIncludeHeader(e.Key, request.Headers)))
        {
            request.Headers.Add(headerKey, headerValue);
        }
        // Make sure to set the proper host, see https://www.mnot.net/blog/2011/07/11/what_proxies_must_do.html#3-route-well
        request.Headers.Host = uri.Authority;

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

    private static HttpResponseData CreateResponse(HttpRequestData req, HttpResponseMessage response)
    {
        var responseData = req.CreateResponse(response.StatusCode);

        // Remove Hop-by-hop Headers, see https://www.mnot.net/blog/2011/07/11/what_proxies_must_do.html#1-remove-hop-by-hop-headers
        var headers = new HttpHeadersCollection(response.Content.Headers.Concat(response.Headers));
        foreach (var header in headers.Where(e => ShouldIncludeHeader(e.Key, headers)))
        {
            responseData.Headers.Add(header.Key, header.Value);
        }

        return responseData;
    }

    // See https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Connection
    private static bool ShouldIncludeHeader(string header, HttpHeaders headers)
    {
        if (HopByHopHeaders.Contains(header, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (headers.TryGetValues("Connection", out var connection))
        {
            return !connection.Contains(header, StringComparer.OrdinalIgnoreCase);
        }

        return true;
    }

    private static async Task<HttpResponseData> BadRequestAsync(HttpRequestData req, string error, CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(HttpStatusCode.BadRequest);
        response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
        await response.WriteLineAsync($"❌ {error}", cancellationToken);
        return response;
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