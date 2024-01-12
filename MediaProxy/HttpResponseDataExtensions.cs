using System.Text;
using Microsoft.Azure.Functions.Worker.Http;

namespace MediaProxy;

internal static class HttpResponseDataExtensions
{
    private static readonly ReadOnlyMemory<byte> NewLine = new("\n"u8.ToArray());

    public static async Task<int> WriteLineAsync(this HttpResponseData response, string text, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await response.Body.WriteAsync(bytes, cancellationToken);
        await response.Body.WriteAsync(NewLine, cancellationToken);
        return bytes.Length + NewLine.Length;
    }
}