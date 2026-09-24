using System.Text.Json.Nodes;
using Modules.Node.Diagnostics;
using Modules.Node.Identity;
using Modules.Node.Materialization;
using Modules.Node.Placement;

namespace Modules.Node.Spec;

/// <summary>框架声明（域 + 身份 + 位置 + 选择 + 缺省 + 反解表 + 引用字段 + 指令）。</summary>
public sealed record NodeFrameworkSpec(
    string Domain,
    IdentitySpec Identity,
    IReadOnlyList<string> ContextKeys,
    PlacementRegistry Placements,
    SelectionSet Selections,
    IReadOnlyDictionary<string, string> Retentions,
    SpecDefaults Defaults,
    IReadOnlyDictionary<string, IReadOnlyList<string>> LocToPlacement,
    IReadOnlyList<string> ReferenceFields,
    NodeDirectiveSet Directives,
    IReadOnlyList<NodeDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == NodeDiagnosticSeverity.Error);

    /// <summary>反解端口（唯一来源 = 本声明；框架内无 loc→carrier 硬编码）。</summary>
    public IPlacementResolver CreateResolver(INodeDiagnosticsSink? diagnostics = null) =>
        new DeclaredPlacementResolver(Placements, LocToPlacement, diagnostics);

    public NodeReferenceIndex CreateReferenceIndex(INodeDiagnosticsSink? diagnostics = null) =>
        new NodeReferenceIndex(ReferenceFields, diagnostics);

    public NodeChainComposer CreateComposer(INodeDiagnosticsSink? diagnostics = null) =>
        new NodeChainComposer(CreateReferenceIndex(diagnostics), diagnostics);
}

/// <summary>
/// 声明装载（N8）：JSON 声明 → 注册表。<b>声明是位置/键模板/保留/选择/映射的唯一来源</b>，
/// 代码内无策略常量（禁 P13）。不实现领域语义（领域词只作为数据流入）。
/// </summary>
public static class NodeFrameworkSpecLoader
{
    public static NodeFrameworkSpec Load(string json, INodeDiagnosticsSink? diagnostics = null)
    {
        var sink = diagnostics ?? NullDiagnosticsSink.Instance;
        var collected = new List<NodeDiagnostic>();

        if (!NodeJson.TryParse(json, out var parsed, out var parseError))
        {
            throw new NodeFrameworkSpecException($"框架声明解析失败: {parseError ?? "空声明"}");
        }

        if (parsed is not JsonObject root)
        {
            throw new NodeFrameworkSpecException("框架声明须为 JSON 对象");
        }

        var domain = ReadString(root, "domain") ?? "default";
        if (root["domain"] is null)
        {
            collected.Add(new NodeDiagnostic(
                "spec.domain-missing",
                NodeDiagnosticSeverity.Warning,
                "声明未给出 domain：回退为 'default'",
                "domain"));
        }

        var identityTemplate = ReadString(root, "identity") ??
                               throw new NodeFrameworkSpecException("声明缺少 identity（身份模板）");
        var sequenceKeys = ReadStringArray(root, "sequence", collected);
        var contextKeys = ReadStringArray(root, "context", collected);
        var identity = IdentitySpec.Create(identityTemplate, sequenceKeys);

        var placements = ParsePlacements(root, domain, collected);
        var selections = ParseSelections(root, collected);
        var retentions = ParseMap(root, "retentions", collected);
        var defaults = new SpecDefaults(ReadObjectJson(root, "defaults"));
        var locToPlacement = ParseLocToPlacement(root, collected);
        var referenceFields = ReadStringArray(root, "referenceFields", collected);
        var directives = NodeDirectiveSet.Parse(ReadArrayJson(root, "directives"), sink);

        foreach (var diagnostic in collected)
        {
            sink.Report(diagnostic);
        }

        return new NodeFrameworkSpec(
            domain,
            identity,
            contextKeys,
            placements,
            selections,
            retentions,
            defaults,
            locToPlacement,
            referenceFields,
            directives,
            collected);
    }

    public static NodeFrameworkSpec LoadFile(string path, INodeDiagnosticsSink? diagnostics = null) =>
        Load(File.ReadAllText(path), diagnostics);

    private static PlacementRegistry ParsePlacements(JsonObject root, string domain, List<NodeDiagnostic> collected)
    {
        var registry = new PlacementRegistry();

        if (root["placements"] is not JsonArray array)
        {
            collected.Add(new NodeDiagnostic(
                "spec.placements-missing",
                NodeDiagnosticSeverity.Error,
                "声明缺少 placements（位置声明是唯一写读面）",
                "placements"));
            return registry;
        }

        foreach (var item in array)
        {
            if (item is not JsonObject declaration)
            {
                collected.Add(new NodeDiagnostic(
                    "spec.placement-invalid",
                    NodeDiagnosticSeverity.Error,
                    "位置声明须为对象：已跳过",
                    "placements"));
                continue;
            }

            var placementId = ReadString(declaration, "id") ?? string.Empty;
            var carrierId = ReadString(declaration, "carrier") ?? string.Empty;
            var keyTemplate = ReadString(declaration, "key") ?? string.Empty;
            var writerId = ReadString(declaration, "writer") ?? string.Empty;

            if (placementId.Length == 0 || carrierId.Length == 0 || keyTemplate.Length == 0 || writerId.Length == 0)
            {
                collected.Add(new NodeDiagnostic(
                    "spec.placement-incomplete",
                    NodeDiagnosticSeverity.Error,
                    "位置声明缺少 id / carrier / key / writer 之一：已跳过",
                    placementId));
                continue;
            }

            registry.AddOrReplace(new PlacementSpec(
                placementId,
                ReadString(declaration, "domain") ?? domain,
                ReadString(declaration, "level") ?? string.Empty,
                carrierId,
                keyTemplate,
                ParseEnum(declaration, "form", PlacementForm.Raw),
                ParseEnum(declaration, "retention", PlacementRetention.Contextual),
                ReadString(declaration, "selection") ?? ExpressionSelectionPredicate.AllSelectionId,
                writerId,
                ParseEnum(declaration, "state", PlacementState.Active)));
        }

        return registry;
    }

    private static SelectionSet ParseSelections(JsonObject root, List<NodeDiagnostic> collected)
    {
        var selections = new SelectionSet();

        if (root["selections"] is not JsonObject declared)
        {
            return selections;
        }

        foreach (var pair in declared)
        {
            var expression = NodeJson.TryReadString(pair.Value, out var declaredExpression) ? declaredExpression : null;
            if (string.IsNullOrWhiteSpace(expression))
            {
                collected.Add(new NodeDiagnostic(
                    "spec.selection-value-invalid",
                    NodeDiagnosticSeverity.Warning,
                    $"选择 '{pair.Key}' 的表达式须为非空字符串：已按 'true' 处理（不静默）",
                    pair.Key));
                expression = "true";
            }

            selections.Add(new ExpressionSelectionPredicate(pair.Key, expression));
        }

        return selections;
    }

    private static IReadOnlyDictionary<string, string> ParseMap(JsonObject root, string propertyName, List<NodeDiagnostic> collected)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        if (root[propertyName] is not JsonObject declared)
        {
            return map;
        }

        foreach (var pair in declared)
        {
            if (pair.Value is null)
            {
                map[pair.Key] = string.Empty;
                continue;
            }

            if (NodeJson.TryReadString(pair.Value, out var value) && value is not null)
            {
                map[pair.Key] = value;
                continue;
            }

            map[pair.Key] = string.Empty;
            collected.Add(new NodeDiagnostic(
                "spec.map-value-invalid",
                NodeDiagnosticSeverity.Warning,
                $"声明 '{propertyName}' 的 '{pair.Key}' 须为字符串：已置空（不静默）",
                pair.Key));
        }

        return map;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseLocToPlacement(JsonObject root, List<NodeDiagnostic> collected)
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        if (root["locToPlacement"] is not JsonObject declared)
        {
            return map;
        }

        foreach (var pair in declared)
        {
            if (pair.Value is JsonArray ids)
            {
                var resolved = new List<string>(ids.Count);
                foreach (var id in ids)
                {
                    if (NodeJson.TryReadString(id, out var text) && !string.IsNullOrEmpty(text))
                    {
                        resolved.Add(text);
                        continue;
                    }

                    collected.Add(new NodeDiagnostic(
                        "spec.loc-entry-invalid",
                        NodeDiagnosticSeverity.Warning,
                        $"反解表 '{pair.Key}' 含非字符串位置 id：已跳过（不静默）",
                        pair.Key));
                }

                map[pair.Key] = resolved;
                continue;
            }

            if (NodeJson.TryReadString(pair.Value, out var single) && !string.IsNullOrEmpty(single))
            {
                map[pair.Key] = new[] { single };
                continue;
            }

            if (pair.Value is not null)
            {
                collected.Add(new NodeDiagnostic(
                    "spec.loc-entry-invalid",
                    NodeDiagnosticSeverity.Warning,
                    $"反解表 '{pair.Key}' 须为字符串或字符串数组：已忽略（不静默）",
                    pair.Key));
            }
        }

        return map;
    }

    private static string? ReadString(JsonObject obj, string propertyName) =>
        NodeJson.TryReadString(obj[propertyName], out var text) ? text : null;

    private static IReadOnlyList<string> ReadStringArray(JsonObject obj, string propertyName, List<NodeDiagnostic> collected)
    {
        if (obj[propertyName] is not JsonArray array)
        {
            return Array.Empty<string>();
        }

        var values = new List<string>(array.Count);

        foreach (var item in array)
        {
            if (NodeJson.TryReadString(item, out var text))
            {
                if (!string.IsNullOrEmpty(text))
                {
                    values.Add(text);
                }

                continue;
            }

            collected.Add(new NodeDiagnostic(
                "spec.array-item-invalid",
                NodeDiagnosticSeverity.Warning,
                $"声明 '{propertyName}' 含非字符串项：已跳过（不静默）",
                propertyName));
        }

        return values;
    }

    private static string? ReadObjectJson(JsonObject obj, string propertyName) =>
        obj[propertyName] is JsonObject nested ? nested.ToJsonString(NodeJson.WriteOptions) : null;

    private static string? ReadArrayJson(JsonObject obj, string propertyName) =>
        obj[propertyName] is JsonArray array ? array.ToJsonString(NodeJson.WriteOptions) : null;

    private static TEnum ParseEnum<TEnum>(JsonObject obj, string propertyName, TEnum fallback)
        where TEnum : struct, Enum
    {
        var text = ReadString(obj, propertyName);
        return !string.IsNullOrWhiteSpace(text) && Enum.TryParse<TEnum>(text, ignoreCase: true, out var parsed)
            ? parsed
            : fallback;
    }
}

public sealed class NodeFrameworkSpecException : InvalidOperationException
{
    public NodeFrameworkSpecException(string message)
        : base(message)
    {
    }
}
