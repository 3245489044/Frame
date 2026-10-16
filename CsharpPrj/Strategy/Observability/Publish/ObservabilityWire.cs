using System.Text.Json;
using Strategy.Observability.Persistence;

namespace Strategy.Observability.Publish;

/// <summary>
/// 实时出口线格式（E5 投影）：一行一条 JSON，与文件事件区行 schema <b>完全一致</b>
/// （<c>{ScopeId,EventSeq,Event{...},TraceId,SessionId,CorrelationScopeKey,RunId,RoundId}</c>），
/// 使既有 PowerShell 消费口径（<c>ConvertFrom-Json</c> / 键名匹配）零改动。
/// </summary>
public static class ObservabilityWire
{
    /// <summary>线序列化选项（与 <c>FileAppendEventStore</c> 同口径：不区分大小写、字段名原样）。</summary>
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>序列化为单行 NDJSON。</summary>
    public static string Line(PersistedEvent evt) => JsonSerializer.Serialize(evt, Json);

    /// <summary>反序列化一行（损坏行返回 null，不抛）。</summary>
    public static PersistedEvent? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try { return JsonSerializer.Deserialize<PersistedEvent>(line, Json); }
        catch (JsonException) { return null; }
    }
}
