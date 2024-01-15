using System.Collections.Frozen;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace MediaProxy;

public class HttpProxy
{
    // See https://datatracker.ietf.org/doc/html/draft-ietf-httpbis-p1-messaging-14#section-7.1.3.1
    private static readonly FrozenSet<string> HopByHopHeaders = new[] {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization", "TE", "Trailer", "Transfer-Encoding", "Upgrade"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> HlsMediaTypes = new[]
    {
        "application/x-mpegurl", "audio/mpegurl", "application/vnd.apple.mpegurl"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;

    public HttpProxy(ILoggerFactory loggerFactory, IHttpClientFactory clientFactory)
    {
        _logger = loggerFactory.CreateLogger<HttpProxy>();
        _httpClient = clientFactory.CreateClient("proxy");
    }

    [Function("proxy")]
    public async Task RunAsync([HttpTrigger(AuthorizationLevel.Function, "get", Route = "{*ignored}")] FunctionContext context, string ignored = "")
    {
        var cancellationToken = context.CancellationToken;
        var (request, response) = GetRequestAndResponse(context);

        var uri = GetUri(request);
        var httpRequest = CreateRequest(request, uri);
        var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var contentLength = httpResponse.Content.Headers.ContentLength;

        await using var httpStream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);

        var mediaType = httpResponse.Content.Headers.ContentType?.MediaType;
        if (mediaType != null && HlsMediaTypes.Contains(mediaType))
        {
            var transformer = new PlaylistTransformer($"{request.Scheme}{Uri.SchemeDelimiter}{request.Host}", request.Query["code"].FirstOrDefault());
            await using var playlistStream = new MemoryStream(contentLength.HasValue ? Convert.ToInt32(contentLength.Value) * 2 : 2048);
            await transformer.ProxifyAsync(uri, httpStream, playlistStream, cancellationToken);
            playlistStream.Position = 0;
            await WriteResponseAsync(httpResponse, response, playlistStream, playlistStream.Length, cancellationToken);
        }
        else
        {
            await WriteResponseAsync(httpResponse, response, httpStream, contentLength, cancellationToken);
        }
    }

    private static Uri GetUri(HttpRequest request)
    {
        var url = request.Query["url"];
        switch (url.Count)
        {
            case 0: throw new BadHttpRequestException("An URL must be specified in the `url` query parameter");
            case > 1: throw new BadHttpRequestException("An single `url` query parameter must be specified");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new BadHttpRequestException($"The URL ({url}) is invalid");
        }

        return uri;
    }

    private static (HttpRequest Request, HttpResponse Response) GetRequestAndResponse(FunctionContext context)
    {
        var httpContext = context.GetHttpContext() ?? throw new InvalidOperationException("HttpContext is not available");
        return (httpContext.Request, httpContext.Response);
    }

    private HttpRequestMessage CreateRequest(HttpRequest request, Uri uri)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
        // Ignore Hop-by-hop Headers, see https://www.mnot.net/blog/2011/07/11/what_proxies_must_do.html#1-remove-hop-by-hop-headers
        foreach (var (headerKey, headerValues) in request.Headers.Where(e => ShouldIncludeHeader(e.Key, httpRequest.Headers)))
        {
            foreach (var headerValue in headerValues)
            {
                httpRequest.Headers.Add(headerKey, headerValue);
            }
        }
        // Make sure to set the proper host, see https://www.mnot.net/blog/2011/07/11/what_proxies_must_do.html#3-route-well
        httpRequest.Headers.Host = uri.Authority;

        _logger.LogInformation("GET {Uri}", uri);
        foreach (var (headerKey, headerValues) in httpRequest.Headers)
        {
            foreach (var headerValue in headerValues)
            {
                _logger.LogInformation("{HeaderKey}: {HeaderValue}", headerKey, headerValue);
            }
        }

        return httpRequest;
    }

    private static async Task WriteResponseAsync(HttpResponseMessage httpResponse, HttpResponse response, Stream stream, long? contentLength, CancellationToken cancellationToken)
    {
        response.StatusCode = (int)httpResponse.StatusCode;

        // Ignore Hop-by-hop Headers, see https://www.mnot.net/blog/2011/07/11/what_proxies_must_do.html#1-remove-hop-by-hop-headers
        var headers = new HttpHeadersCollection(httpResponse.Content.Headers.Concat(httpResponse.Headers));
        foreach (var header in headers.Where(e => ShouldIncludeHeader(e.Key, headers)))
        {
            foreach (var headerValue in header.Value)
            {
                response.Headers.Append(header.Key, headerValue);
            }
        }

        response.GetTypedHeaders().ContentLength = contentLength;
        await stream.CopyToAsync(response.Body, cancellationToken);
    }

    private static bool ShouldIncludeHeader(string header, HttpHeaders headers)
    {
        // The content length is set later
        if (string.Equals(header, "Content-Length", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (HopByHopHeaders.Contains(header))
        {
            return false;
        }

        // See https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Connection
        if (headers.TryGetValues("Connection", out var connection))
        {
            return !connection.Contains(header, StringComparer.OrdinalIgnoreCase);
        }

        return true;
    }
}