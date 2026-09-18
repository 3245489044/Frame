using System.Text.Json;
using DataCenter.Protocol.Standards;
using Plate.JsonMethod.Utilities;
using static Plate.JsonMethod.Utilities.JsonTolerantReader;

namespace DataCenter.Standards;

/// <summary>
/// 台账 JSON → 中立模型（设计文档 §3.2 <c>StandardJson</c>）：
/// <c>$comment</c> 元数据键忽略；其余未知键 **fail-fast**（防声明漂移静默）。
/// 另提供 <see cref="DuplicateKeys"/>（C1 的机器判据载体：JSON 对象重复键在
/// <see cref="JsonDocument"/> 层被静默合并，故须在原始文本层扫描）。
/// <para>
/// 机制分工：读取骨架 / 原语 / 重复键扫描唯一实现于平台 JSON 框架（<see cref="Plate.JsonMethod"/>）；
/// 本件只保留域文案与异常身份（<see cref="StandardViolationException"/>）。
/// </para>
/// </summary>
public static partial class StandardJson
{
    private static readonly string[] PortTopKeys = ["$comment", "ports", "data", "events", "fakes"];
    private static readonly string[] PortEntryKeys = ["in", "out", "depends", "scope", "version", "emits", "impl"];
    private static readonly string[] PortFieldKeys = ["type", "required"];
    private static readonly string[] DataEntryKeys =
        ["direction", "scope", "fields", "producer", "producers", "consumers", "version", "crossCutting"];
    private static readonly string[] EventTopKeys = ["$comment", "version", "entities", "events"];
    private static readonly string[] EventEntryKeys = ["entity", "carries", "required", "metrics", "enum", "scope", "version"];
    private static readonly string[] FakeTopKeys = ["$comment", "fakes"];
    private static readonly string[] FakeEntryKeys = ["kind", "seed", "version"];

    /// <summary>端口标准（<c>ports</c> 段；键 Ordinal 升序）。</summary>
    public static IReadOnlyList<PortStandard> ReadPorts(string portsJson)
        => ParseRoot<PortStandard[]>(portsJson, root =>
        {
            EnsureKnownKeys("ports.json", root, PortTopKeys);
            if (!TrySection(root, "ports", out var ports)) return [];

            return ports.EnumerateObject().Select(p =>
            {
                EnsureKnownKeys($"ports.{p.Name}", p.Value, PortEntryKeys);
                return new PortStandard(
                    p.Name,
                    ReadInputs($"ports.{p.Name}.in", p.Value),
                    PathStrings(p.Value, "out.fields"),
                    Strings(p.Value, "depends"),
                    Text(p.Value, "scope") ?? string.Empty,
                    (int)(Number(p.Value, "version") ?? 1),
                    Strings(p.Value, "emits"),
                    ReadImpl(p.Name, p.Value));
            }).OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
        });

    /// <summary>数据标准（<c>data</c> 段；键 Ordinal 升序）。</summary>
    public static IReadOnlyList<DataStandardItem> ReadData(string portsJson)
        => ParseRoot<DataStandardItem[]>(portsJson, root =>
        {
            EnsureKnownKeys("ports.json", root, PortTopKeys);
            if (!TrySection(root, "data", out var data)) return [];

            return data.EnumerateObject().Select(p =>
            {
                EnsureKnownKeys($"data.{p.Name}", p.Value, DataEntryKeys);
                var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                if (p.Value.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Object)
                    foreach (var field in f.EnumerateObject())
                        fields[field.Name] = FieldType(field.Value);

                return new DataStandardItem(
                    p.Name,
                    Text(p.Value, "direction") ?? string.Empty,
                    Text(p.Value, "scope") ?? string.Empty,
                    fields,
                    Producers(p.Value),
                    Strings(p.Value, "consumers"),
                    (int)(Number(p.Value, "version") ?? 1));
            }).OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
        });

    /// <summary>事件标准（<c>events</c> 段；键 Ordinal 升序）。</summary>
    public static IReadOnlyList<EventStandard> ReadEvents(string eventsJson)
        => ParseRoot<EventStandard[]>(eventsJson, root =>
        {
            EnsureKnownKeys("events.json", root, EventTopKeys);
            if (!TrySection(root, "events", out var events)) return [];

            return events.EnumerateObject().Select(p =>
            {
                EnsureKnownKeys($"events.{p.Name}", p.Value, EventEntryKeys);
                return new EventStandard(
                    p.Name,
                    Text(p.Value, "entity"),
                    Strings(p.Value, "carries"),
                    Strings(p.Value, "required"),
                    Strings(p.Value, "metrics"),
                    Text(p.Value, "scope"),
                    (int)(Number(p.Value, "version") ?? 1),
                    Enums(p.Value));
            }).OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
        });

    /// <summary>替身标准（<c>fakes</c> 段；端口键 Ordinal 升序）。</summary>
    public static IReadOnlyList<FakeStandard> ReadFakes(string fakesJson)
        => ParseRoot<FakeStandard[]>(fakesJson, root =>
        {
            EnsureKnownKeys("fakes.json", root, FakeTopKeys);
            if (!TrySection(root, "fakes", out var fakes)) return [];

            return fakes.EnumerateObject().Select(p =>
            {
                EnsureKnownKeys($"fakes.{p.Name}", p.Value, FakeEntryKeys);
                return new FakeStandard(
                    p.Name,
                    Text(p.Value, "kind") ?? string.Empty,
                    p.Value.TryGetProperty("seed", out var seed) ? seed.GetRawText() : null,
                    (int)(Number(p.Value, "version") ?? 1));
            }).OrderBy(x => x.PortKey, StringComparer.Ordinal).ToArray();
        });

    /// <summary>扫描 JSON 文本中的重复键（同一对象内同名；Ordinal）。机制在平台 JSON 框架（原文层扫描）。</summary>
    public static IReadOnlyList<string> DuplicateKeys(string json) => JsonDuplicateKeyScanner.Scan(json);

    /// <summary>
    /// 逐层未知键闭合（层适配器）：取值域 = <see cref="PortImplVocabulary.KeysOf"/> 派生的键表；
    /// **未登记层 → 不判**（防越界误杀）。实现单源 = <see cref="EnsureKnownKeys"/>（本件不另写一遍收集/抛出）。
    /// </summary>
    private static void EnsureLayerKeys(string level, string context, JsonElement obj)
    {
        if (PortImplVocabulary.KeysOf(level) is not { Count: > 0 } known) return;

        EnsureKnownKeys(context, obj, known);
    }

    /// <summary>未知键 fail-fast（一次报全：全部未声明键带键路径入明细；首条文案与迁移前逐字一致）。
    /// 键闭合**唯一实现**：逐层入口经 <see cref="EnsureLayerKeys"/> 转调。</summary>
    private static void EnsureKnownKeys(string context, JsonElement obj, IReadOnlyList<string> known)
    {
        var violations = new List<string>();
        CollectUnknownKeys(obj, known, violations, name => UnknownKeyMessage(context, name));
        if (violations.Count > 0) throw new StandardViolationException(violations[0], violations);
    }

    /// <summary>未知键诊断文案（单一构造点：域文案留本层；<c>$comment</c> 为元数据出口）。</summary>
    private static string UnknownKeyMessage(string context, string name)
        => $"未知键 '{context}.{name}'（fail-fast：非标准字段不得静默进入标准面，元数据请用 $comment）";

    private static IReadOnlyList<PortFieldStandard> ReadInputs(string context, JsonElement entry)
    {
        if (!entry.TryGetProperty("in", out var input) || input.ValueKind != JsonValueKind.Object) return [];

        return input.EnumerateObject().Select(p =>
        {
            EnsureKnownKeys(context, p.Value, PortFieldKeys);
            var optional = p.Value.ValueKind == JsonValueKind.Object
                && p.Value.TryGetProperty("required", out var required)
                && required.ValueKind == JsonValueKind.False;

            return new PortFieldStandard(p.Name, FieldType(p.Value), !optional);
        }).ToArray();
    }

    private static string FieldType(JsonElement field)
    {
        if (field.ValueKind == JsonValueKind.Object && field.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
            return type.GetString() ?? string.Empty;

        return field.ValueKind == JsonValueKind.String ? field.GetString() ?? string.Empty : string.Empty;
    }

    /// <summary>字段枚举域（<c>enum</c> 段：字段名 → 允许值；非对象段 → 空域）；计数为零的域同样保留。</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Enums(JsonElement entry)
    {
        var enums = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (!entry.TryGetProperty("enum", out var node) || node.ValueKind != JsonValueKind.Object) return enums;

        foreach (var field in node.EnumerateObject())
            enums[field.Name] = field.Value.ValueKind == JsonValueKind.Array
                ? field.Value.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .ToArray()
                : [];

        return enums;
    }

    private static IReadOnlyList<string> Producers(JsonElement entry)
    {
        var declared = Strings(entry, "producers");
        return declared.Count > 0 ? declared : Strings(entry, "producer");
    }
}
