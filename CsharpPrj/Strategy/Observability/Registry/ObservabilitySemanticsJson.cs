using System.Text.Json;

namespace Strategy.Observability.Registry;

/// <summary>
/// 契约文件（observability_semantics.json）加载器（E3）：沿用 *_log_messages.json 模式。
/// 该文件属于应用侧语义实例；本类仅提供「JSON → 契约」机制，不持有任何领域语义。
/// </summary>
public static class ObservabilitySemanticsJson
{
    private static readonly JsonSerializerOptions Opt = new() { PropertyNameCaseInsensitive = true };

    public sealed class SemanticsFile
    {
        public Dictionary<string, EventSpec> Events { get; set; } = new(StringComparer.Ordinal);
    }

    public sealed class EventSpec
    {
        public string? Entity { get; set; }
        public string[] Required { get; set; } = Array.Empty<string>();
        public string[] Metrics { get; set; } = Array.Empty<string>();
        public Dictionary<string, string[]> Enum { get; set; } = new();
    }

    /// <summary>加载并编译为契约列表；entity 名称可选。</summary>
    public static IReadOnlyList<EventContract> Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var file = JsonSerializer.Deserialize<SemanticsFile>(json, Opt)
            ?? throw new InvalidOperationException("无法解析观测语义契约文件");
        var list = new List<EventContract>();
        foreach (var (kind, spec) in file.Events)
        {
            list.Add(new EventContract
            {
                EventKind = kind,
                EntityType = string.IsNullOrWhiteSpace(spec.Entity) ? null : spec.Entity,
                Required = spec.Required ?? Array.Empty<string>(),
                MetricKeys = spec.Metrics ?? Array.Empty<string>(),
                Enums = spec.Enum?.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value) ?? new Dictionary<string, IReadOnlyList<string>>(),
            });
        }
        return list;
    }
}
