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
    /// <summary>
    /// 装载框架声明。位置反解表（<paramref name="locToPlacement"/>）**必填**——唯一来源 = 位置反解声明面
    /// （U12 L2：<c>[Matrix] LocToPlacement</c>），本装载器不再从 JSON 读它（手写即双真源 P22）。
    /// </summary>
    /// <param name="json">框架声明 JSON（域/身份/位置/选择/保留/缺省/引用字段/指令）。</param>
    /// <param name="locToPlacement">位置反解表（loc → 位置 id 集）；缺失/非法由调用方 fail-fast（不留兜底）。</param>
    /// <param name="diagnostics">诊断出口。</param>
    public static NodeFrameworkSpec Load(
        string json,
        IReadOnlyDictionary<string, IReadOnlyList<string>> locToPlacement,
        INodeDiagnosticsSink? diagnostics = null)
        => LoadCore(json, _ => locToPlacement, diagnostics);

    /// <summary>
    /// 装载框架声明（映射**工厂**形态）：位置反解表按**已解析的位置注册表**现算 ——
    /// 声明面是全域并集，而解析器按域实例化，故须按域注册表裁剪（否则跨域条目触发
    /// <c>placement.reverse-ambiguous</c>）。单次解析，无二遍装载。
    /// </summary>
    /// <param name="json">框架声明 JSON。</param>
    /// <param name="locToPlacementFactory">位置反解表工厂（入参 = 本域位置注册表）。</param>
    /// <param name="diagnostics">诊断出口。</param>
    public static NodeFrameworkSpec Load(
        string json,
        Func<PlacementRegistry, IReadOnlyDictionary<string, IReadOnlyList<string>>> locToPlacementFactory,
        INodeDiagnosticsSink? diagnostics = null)
        => LoadCore(json, locToPlacementFactory, diagnostics);

    private static NodeFrameworkSpec LoadCore(
        string json,
        Func<PlacementRegistry, IReadOnlyDictionary<string, IReadOnlyList<string>>> locToPlacementFactory,
        INodeDiagnosticsSink? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(locToPlacementFactory);
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

        // 手写反解表禁入（U21=B 仅语言路径）：声明面若仍携带 locToPlacement ⇒ Error（不静默忽略）
        if (root["locToPlacement"] is not null)
        {
            collected.Add(new NodeDiagnostic(
                "spec.loc-to-placement-forbidden",
                NodeDiagnosticSeverity.Error,
                "声明含 locToPlacement：位置反解表唯一来源 = 位置反解声明面（[Matrix] LocToPlacement；手写即双真源 P22）",
                "locToPlacement"));
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

        // 位置反解表：唯一来源 = 位置反解声明面（U12 L2），按**本域位置注册表**现算（禁静默兜底）
        var locToPlacement = locToPlacementFactory(placements)
            ?? throw new NodeFrameworkSpecException(
                "位置反解表不可为空：唯一来源 = 位置反解声明面（[Matrix] LocToPlacement）");

        var selections = ParseSelections(root, collected);
        var retentions = ParseMap(root, "retentions", collected);
        var defaults = new SpecDefaults(ReadObjectJson(root, "defaults"));
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

    /// <summary>
    /// 按文件装载。<paramref name="locToPlacementFactory"/> 语义同 <see cref="Load(string, Func{PlacementRegistry, IReadOnlyDictionary{string, IReadOnlyList{string}}}, INodeDiagnosticsSink?)"/>
    /// （必填：唯一来源 = 位置反解声明面）。
    /// </summary>
    /// <param name="path">声明文件路径。</param>
    /// <param name="locToPlacementFactory">位置反解表工厂（入参 = 本域位置注册表）。</param>
    /// <param name="diagnostics">诊断出口。</param>
    public static NodeFrameworkSpec LoadFile(
        string path,
        Func<PlacementRegistry, IReadOnlyDictionary<string, IReadOnlyList<string>>> locToPlacementFactory,
        INodeDiagnosticsSink? diagnostics = null) =>
        Load(File.ReadAllText(path), locToPlacementFactory, diagnostics);

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
            var localCoordinates = ReadLocalCoordinates(declaration, collected);

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
                ParsePlacementForm(declaration, collected),
                ParseEnum(declaration, "retention", PlacementRetention.Contextual),
                ReadString(declaration, "selection") ?? ExpressionSelectionPredicate.AllSelectionId,
                writerId,
                ParseEnum(declaration, "state", PlacementState.Active),
                localCoordinates));
        }

        return registry;
    }

    /// <summary>
    /// 位置**自有坐标**声明（<c>local</c>）：键模板中出现、但不属域身份占位符的坐标名（如轮域 <c>task-node</c> 的
    /// <c>storage</c>/<c>node</c>）。接受字符串数组或逗号/分号分隔字符串；缺省 ⇒ <c>null</c>（未声明）。
    /// <para>声明化动机：位置键与身份键可能分属两套坐标空间（轮域归档/会话/兼容位置按 <c>container/round</c> 寻址，
    /// 而任务面板位置按 <c>storage/node</c> 寻址）。此前该差异只能靠"装配期判据未接线"回避，
    /// 故位置键一旦偏离身份占位符即静默（<c>spec.placement-template-unknown-field</c> 从未生效）。</para>
    /// </summary>
    /// <summary>
    /// 解析位置形态（<c>form</c>）：未声明 ⇒ 缺省 <see cref="PlacementForm.Raw"/>；**声明了但不可识别 ⇒ 可见化**
    /// （不静默吞成 Raw —— 否则「聚合流」拼写漂移会退化为逐事件形态而无任何归因面）。
    /// </summary>
    private static PlacementForm ParsePlacementForm(JsonObject declaration, List<NodeDiagnostic> collected)
    {
        var text = ReadString(declaration, "form");
        if (string.IsNullOrWhiteSpace(text))
        {
            return PlacementForm.Raw;
        }

        if (Enum.TryParse<PlacementForm>(text, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        collected.Add(new NodeDiagnostic(
            "spec.form-unknown",
            NodeDiagnosticSeverity.Warning,
            $"位置形态 'form' 值 '{text}' 不可识别：已按缺省 '{PlacementForm.Raw}' 处理（不静默隐藏）",
            "form"));
        return PlacementForm.Raw;
    }

    private static IReadOnlyList<string>? ReadLocalCoordinates(JsonObject declaration, List<NodeDiagnostic> collected)
    {
        var declared = declaration["local"];
        if (declared is null)
        {
            return null;
        }

        if (declared is JsonArray)
        {
            var values = ReadStringArray(declaration, "local", collected);
            return values.Count == 0 ? null : values;
        }

        var text = ReadString(declaration, "local");
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split(
            new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : parts;
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
