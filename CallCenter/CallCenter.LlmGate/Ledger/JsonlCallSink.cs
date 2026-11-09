using System.Globalization;
using System.Text.Json;
using CallCenter.LlmGate.Model;

namespace CallCenter.LlmGate.Ledger;

/// <summary>
/// 冷存：按日追加 JSONL（单写）。每行一条 span；事件写 events-yyyyMMdd.jsonl。
/// 未知金额以 <c>null</c> 落盘（不省略字段），保证「未知 ≠ 0」可复算。
/// </summary>
public sealed class JsonlCallSink
{
    private readonly string _directory;
    private readonly SemaphoreSlim _io = new(1, 1);

    public JsonlCallSink(string? dataRoot = null)
    {
        _directory = GateDataPaths.ResolveCallDirectory(dataRoot);
    }

    public string Directory => _directory;

    public string CallFilePath(DateTimeOffset at)
    {
        var day = at.ToLocalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return Path.Combine(_directory, $"calls-{day}.jsonl");
    }

    public string EventFilePath(DateTimeOffset at)
    {
        var day = at.ToLocalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return Path.Combine(_directory, $"events-{day}.jsonl");
    }

    public async ValueTask AppendCallAsync(LlmCallSpan span, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(span);
        var line = JsonSerializer.Serialize(span, GateJson.Options);
        await AppendLineAsync(CallFilePath(span.StartedAt), line, ct).ConfigureAwait(false);
    }

    public async ValueTask AppendEventAsync(GateEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var line = JsonSerializer.Serialize(evt, GateJson.Options);
        await AppendLineAsync(EventFilePath(evt.At), line, ct).ConfigureAwait(false);
    }

    private async ValueTask AppendLineAsync(string path, string line, CancellationToken ct)
    {
        System.IO.Directory.CreateDirectory(_directory);
        await _io.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(path, line + Environment.NewLine, ct).ConfigureAwait(false);
        }
        finally
        {
            _io.Release();
        }
    }
}
