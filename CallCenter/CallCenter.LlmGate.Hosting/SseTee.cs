using System.Text;

namespace CallCenter.LlmGate.Hosting;

/// <summary>
/// SSE 直通泵：逐块原样写给客户端（不缓冲、不改写），旁路解析 <c>data:</c> 行用于取末帧 usage。
/// </summary>
public static class SseTee
{
    public static async Task PumpAsync(
        Stream source, Stream destination, Action<string> onDataJson, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(onDataJson);

        var buffer = new StringBuilder();
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[8192];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];

        int read;
        while ((read = await source.ReadAsync(bytes.AsMemory(0, bytes.Length), ct).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(bytes.AsMemory(0, read), ct).ConfigureAwait(false);
            await destination.FlushAsync(ct).ConfigureAwait(false);

            var count = decoder.GetChars(bytes, 0, read, chars, 0);
            buffer.Append(chars, 0, count);
            DrainLines(buffer, onDataJson);
        }

        if (buffer.Length > 0)
        {
            ProcessLine(buffer.ToString(), onDataJson);
        }
    }

    private static void DrainLines(StringBuilder buffer, Action<string> onDataJson)
    {
        while (true)
        {
            var index = IndexOfNewLine(buffer);
            if (index < 0)
            {
                return;
            }

            var line = buffer.ToString(0, index);
            buffer.Remove(0, index + 1);
            ProcessLine(line, onDataJson);
        }
    }

    private static int IndexOfNewLine(StringBuilder buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] == '\n')
            {
                return i;
            }
        }

        return -1;
    }

    private static void ProcessLine(string line, Action<string> onDataJson)
    {
        var text = line.TrimEnd('\r');
        if (!text.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var payload = text[5..].Trim();
        if (payload.Length == 0 || string.Equals(payload, "[DONE]", StringComparison.Ordinal))
        {
            return;
        }

        onDataJson(payload);
    }
}
