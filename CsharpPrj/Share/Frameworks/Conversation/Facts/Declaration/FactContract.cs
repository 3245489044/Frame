using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Share.Conversation.Facts.Declaration;

/// <summary>
/// 会话事实契约（声明面产物的一次性不可变快照）—— 事实类别 + 载荷契约 + 受控词表。
/// <para>
/// 三级来源（对齐 <c>Agent.Core\Nodes\SessionFactSpecLoader</c>）：<c>Declared</c>（<c>Config\session-facts.ushar</c>）
/// → <c>Generated</c>（<see cref="LoadGenerated"/>，本程序集嵌入的 <c>session-fact.v1.schema.json</c>）
/// → <c>BuiltinDefault</c>（载体侧内建缺省契约，**词表归属载体**，平台层不内置词表）。
/// </para>
/// </summary>
/// <param name="Source">契约来源标识（声明面路径或生成物标识）。</param>
/// <param name="Events">事件契约（声明序）。</param>
/// <param name="Vocabularies">受控词表（表名 → 词条序）。</param>
public sealed record FactContract(
    string Source,
    IReadOnlyList<FactEventContract> Events,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Vocabularies)
{
    /// <summary>嵌入资源名后缀（**默认 = 线格式契约文件名**；载体可经对象初始化器覆盖，避免平台层硬编码载体文件名）。</summary>
    public string ResourceSuffix { get; init; } = DefaultResourceSuffix;

    /// <summary>默认嵌入资源名后缀（跨语言线格式契约的默认物理载体名）。</summary>
    public const string DefaultResourceSuffix = "session-fact.v1.schema.json";

    /// <summary>按事件名取契约（未声明 ⇒ null）。</summary>
    /// <param name="name">事件名（= 线格式 kind 值）。</param>
    public FactEventContract? Event(string name)
        => Events.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.Ordinal));

    /// <summary>是否声明了该事实类别（声明面为判据真源；未声明即不得产该类事实）。</summary>
    /// <param name="name">事件名。</param>
    public bool HasEvent(string name) => Event(name) is not null;

    /// <summary>取受控词表（未声明 ⇒ 空表）。</summary>
    /// <param name="name">词表名。</param>
    public IReadOnlyList<string> Vocabulary(string name)
        => Vocabularies.TryGetValue(name, out var words) ? words : Array.Empty<string>();

    /// <summary>由契约 JSON 解析（非法即抛：契约为判据依据，不可静默降级）。</summary>
    /// <param name="json">契约 JSON 文本。</param>
    /// <param name="resourceSuffix">来源标识缺省值（null = <see cref="DefaultResourceSuffix"/>）。</param>
    public static FactContract FromJson(string json, string? resourceSuffix = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("契约 JSON 不得为空", nameof(json));

        var suffix = Suffix(resourceSuffix);
        var dto = JsonSerializer.Deserialize<SchemaDto>(json, JsonOptions)
            ?? throw new InvalidOperationException($"契约 JSON 无法解析（{suffix}）");

        var events = (dto.Events ?? new List<EventDto>())
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .Select(e => new FactEventContract(
                e.Name!,
                e.Entity,
                e.Required ?? new List<string>(),
                e.Metrics ?? new List<string>(),
                (e.Enum ?? new Dictionary<string, List<string>>())
                    .ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.Ordinal)))
            .ToList();

        var vocabularies = (dto.Vocabularies ?? new Dictionary<string, List<string>>())
            .ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.Ordinal);

        return new FactContract(
            string.IsNullOrWhiteSpace(dto.Source) ? suffix : dto.Source!,
            events,
            vocabularies);
    }

    /// <summary>读取嵌入的生成物契约（<c>Generated</c> 级；资源缺失即抛，防「静默空契约」）。</summary>
    /// <param name="resourceSuffix">资源名后缀（null = 默认线格式契约名）。</param>
    public static FactContract LoadGenerated(string? resourceSuffix = null)
        => FromJson(ReadGeneratedJson(resourceSuffix), resourceSuffix);

    /// <summary>嵌入资源是否存在（自带宿主 / 裁剪场景的诊断可见性）。</summary>
    /// <param name="resourceSuffix">资源名后缀（null = 默认线格式契约名）。</param>
    public static bool GeneratedExists(string? resourceSuffix = null)
        => ResolveResourceName(typeof(FactContract).Assembly, resourceSuffix) is not null;

    /// <summary>读取嵌入契约文本（跨语言线格式 + 受控词表的唯一物理载体）。</summary>
    /// <param name="resourceSuffix">资源名后缀（null = 默认线格式契约名）。</param>
    public static string ReadGeneratedJson(string? resourceSuffix = null)
    {
        var suffix = Suffix(resourceSuffix);
        var assembly = typeof(FactContract).Assembly;
        var name = ResolveResourceName(assembly, resourceSuffix)
            ?? throw new InvalidOperationException($"程序集缺少嵌入资源 {suffix}（会话事实契约）");

        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"无法打开嵌入资源 {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>嵌入资源全名解析（唯一实现：存在性判定与读取共用同一扫描）。</summary>
    /// <param name="assembly">承载契约的程序集。</param>
    /// <param name="resourceSuffix">资源名后缀（null / 空白 → 默认值）。</param>
    private static string? ResolveResourceName(System.Reflection.Assembly assembly, string? resourceSuffix)
    {
        var suffix = Suffix(resourceSuffix);
        return assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>资源名后缀归一（null / 空白 → 默认值）。</summary>
    private static string Suffix(string? resourceSuffix)
        => string.IsNullOrWhiteSpace(resourceSuffix) ? DefaultResourceSuffix : resourceSuffix!;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>契约 JSON 传输对象（仅本类型内部使用）。</summary>
    private sealed record SchemaDto
    {
        public string? V { get; init; }

        public string? Source { get; init; }

        public Dictionary<string, List<string>>? Vocabularies { get; init; }

        public List<EventDto>? Events { get; init; }
    }

    /// <summary>事件契约传输对象。</summary>
    private sealed record EventDto
    {
        public string? Name { get; init; }

        public string? Entity { get; init; }

        public List<string>? Required { get; init; }

        public List<string>? Metrics { get; init; }

        [JsonPropertyName("enum")]
        public Dictionary<string, List<string>>? Enum { get; init; }
    }
}
