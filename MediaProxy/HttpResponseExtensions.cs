using System.Text;
using Microsoft.AspNetCore.Http;

namespace MediaProxy;

internal static class HttpResponseExtensions
{
    private static readonly ReadOnlyMemory<byte> NewLine = new("\n"u8.ToArray());

    public static async Task WriteLineAsync(this HttpResponse response, string text, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await response.Body.WriteAsync(bytes, cancellationToken);
        await response.Body.WriteAsync(NewLine, cancellationToken);
    }
}