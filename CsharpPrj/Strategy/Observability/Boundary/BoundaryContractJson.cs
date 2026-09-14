using System.Text.Json;
using Share.HierarchySupport;
using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Boundary;

/// <summary>
/// 边界契约 JSON 加载器（E9）：一份契约文件声明「端口 / 数据 / 事件」三类边界
/// （原测试侧 kind 身份的迁移处）。事件段沿用既有语义文件格式，不另立模型。
/// </summary>
public static partial class BoundaryContractJson
{
    /// <summary>加载契约文件（段名：<c>ports</c> / <c>data</c> / <c>events</c>；缺段即跳过）。</summary>
    public static IReadOnlyList<BoundaryContract> Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var contracts = new List<BoundaryContract>();
        if (TrySection(root, "ports", out var ports)) contracts.AddRange(ReadEntries(ports, BoundaryKind.Port));
        if (TrySection(root, "data", out var data)) contracts.AddRange(ReadEntries(data, BoundaryKind.Data));
        if (TrySection(root, "events", out var events)) contracts.AddRange(ReadEvents(events));
        return contracts;
    }

    /// <summary>仅加载端口段（端口目录装配用）。</summary>
    public static IReadOnlyList<BoundaryContract> LoadPorts(string json)
        => Load(json).Where(c => c.Kind == BoundaryKind.Port).ToArray();

    private static IEnumerable<BoundaryContract> ReadEntries(JsonElement section, BoundaryKind defaultKind)
    {
        if (section.ValueKind != JsonValueKind.Object)
            throw new BoundaryContractViolationException("(契约文件)", "ports/data 段须为对象：键 → 契约体");

        foreach (var entry in section.EnumerateObject())
        {
            var spec = entry.Value;
            var kind = ParseKind(Str(spec, "kind")) ?? defaultKind;

            var fields = new List<FieldSpec>();
            fields.AddRange(ReadFields(Property(spec, "in")));
            fields.AddRange(ReadFields(Property(spec, "fields")));
            fields.AddRange(ReadFields(Property(spec, "out")));

            var metrics = Strings(spec, "metrics");
            if (metrics.Count == 0 && Property(spec, "out") is { ValueKind: JsonValueKind.Object } outSpec)
                metrics = Strings(outSpec, "metrics");

            yield return new BoundaryContract(
                entry.Name,
                kind,
                ParseDirection(Str(spec, "direction"), kind),
                fields,
                metrics,
                ReadDepends(Property(spec, "depends")),
                ParseScope(Str(spec, "scope")),
                null,
                Int(spec, "version") ?? 1)
            {
                Emits = Strings(spec, "emits"),
            };
        }
    }

    private static IEnumerable<BoundaryContract> ReadEvents(JsonElement section)
    {
        if (section.ValueKind != JsonValueKind.Object)
            throw new BoundaryContractViolationException("(契约文件)", "events 段须为对象：分类 → 语义体");

        foreach (var entry in section.EnumerateObject())
        {
            var spec = entry.Value;
            var fields = Strings(spec, "required").Select(n => new FieldSpec(n, FieldTypes.Any)).ToList();
            foreach (var (name, allowed) in ReadEnums(spec))
                fields.Add(new FieldSpec(name, FieldTypes.String, Required: false, Enum: allowed));

            yield return BoundaryContract.Event(
                entry.Name,
                fields,
                Strings(spec, "metrics"),
                Str(spec, "entity"),
                Int(spec, "version") ?? 1);
        }
    }
}
