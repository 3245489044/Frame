using System.Text.Json;
using Strategy.Observability.Entity;

namespace Strategy.Observability.Envelope;

/// <summary>信封构建器（fluent）：收集可选元数据后一次性 Build（事件不可变）。</summary>
public sealed class ObservabilityEventBuilder
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly EntityId _entity;
    private readonly string _eventKind;
    private string? _phase;
    private string? _stage;
    private string? _payload;
    private int _version = 1;
    private readonly Dictionary<string, double> _metrics = new(StringComparer.Ordinal);

    public ObservabilityEventBuilder(EntityId entity, string eventKind)
    {
        _entity = entity;
        _eventKind = PortGuard.Required(eventKind, nameof(eventKind));
    }

    public ObservabilityEventBuilder Phase(string phase) { _phase = phase; return this; }
    public ObservabilityEventBuilder Stage(string stage) { _stage = stage; return this; }
    public ObservabilityEventBuilder Version(int version) { _version = version; return this; }

    /// <summary>序列化一个对象为载荷 JSON。传字符串则按原始字符串存入。</summary>
    public ObservabilityEventBuilder Payload(object? payload)
    {
        _payload = payload switch
        {
            null => null,
            string s => s,
            _ => JsonSerializer.Serialize(payload, Json),
        };
        return this;
    }

    public ObservabilityEventBuilder WithMetric(string key, double value)
    {
        _metrics[key] = value;
        return this;
    }

    public ObservabilityEvent Build(long eventSeq, DateTime? createdAt = null)
    {
        return new ObservabilityEvent
        {
            Entity = _entity,
            EventSeq = eventSeq,
            EventKind = _eventKind,
            Phase = _phase,
            Stage = _stage,
            Metrics = _metrics.Count > 0 ? new Dictionary<string, double>(_metrics) : null,
            PayloadJson = _payload,
            CreatedAt = (createdAt ?? DateTime.UtcNow).ToString("O"),
            Version = _version,
        };
    }
}
