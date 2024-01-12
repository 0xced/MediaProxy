using System.Text.RegularExpressions;

namespace MediaProxy;

internal partial class PlaylistTransformer
{
    private readonly string _proxyAuthority;
    private readonly string? _code;

    public PlaylistTransformer(string proxyAuthority, string? code)
    {
        _proxyAuthority = proxyAuthority;
        _code = code;
    }

    public async Task ProxifyAsync(Uri uri, Stream input, Stream output, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(input, leaveOpen: true);
        await using var writer = new StreamWriter(output, leaveOpen: true);
        var readingExtInf = false;
        while (await reader.ReadLineAsync(cancellationToken) is {} line)
        {
            if (readingExtInf && !line.StartsWith('#'))
            {
                await writer.WriteLineAsync(GetProxyUrl(uri, line).AsMemory(), cancellationToken);
                readingExtInf = false;
            }
            else
            {
                var match = UriRegex().Match(line);
                if (match.Success)
                {
                    var uriMatch = match.Groups[1];
                    var proxyLine = $"{line[..uriMatch.Index]}{GetProxyUrl(uri, uriMatch.Value)}{line[(uriMatch.Index + uriMatch.Length)..]}";
                    await writer.WriteLineAsync(proxyLine.AsMemory(), cancellationToken);
                }
                else
                {
                    await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
                }
            }
            if (line.StartsWith("#EXT-X-STREAM-INF") || line.StartsWith("#EXTINF"))
            {
                readingExtInf = true;
            }
        }
    }

    private string GetProxyUrl(Uri baseUri, string relativeUri)
    {
        var absoluteUrl = Uri.TryCreate(relativeUri, UriKind.Absolute, out var absoluteUri) ? absoluteUri : new Uri(baseUri, relativeUri);
        var escapedUrl = Uri.EscapeDataString(absoluteUrl.AbsoluteUri);
        var proxyUrl = $"{_proxyAuthority}/?url={escapedUrl}";
        return string.IsNullOrEmpty(_code) ? proxyUrl : $"{proxyUrl}&code={_code}";
    }

    [GeneratedRegex("URI=\"([^\"]+)\"")]
    private static partial Regex UriRegex();
}