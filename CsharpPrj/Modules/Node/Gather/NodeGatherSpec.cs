using System.Text.Json.Nodes;
using Modules.Node.Diagnostics;

namespace Modules.Node.Gather;

/// <summary>
/// 聚拢声明的**读轴**（键集取得方式；声明化，禁 C# 分支）。未声明 ⇒ null = 容器轴默认。
/// </summary>
/// <param name="Scope">层级锚点（task/history/node/container/full）。</param>
/// <param name="Source">取得方式（edge/container/key-prefix/full）。</param>
/// <param name="Edge">边类型（source=edge 时必填，如 cross_db_link）。</param>
/// <param name="Normalize">归一实现名（默认 nodeEndpoint）。</param>
/// <param name="Order">序实现名（默认 nodeKeyOrdinal）。</param>
/// <param name="Empty">合法空因词（如 no-binding）。</param>
public sealed record NodeGatherReadSpec(
    string Scope,
    string Source,
    string? Edge = null,
    string? Normalize = null,
    string? Order = null,
    string? Empty = null);

/// <summary>
/// 节点**聚拢**声明（<c>node-gather.json</c> 读入模型）——**声明化**：新增一种节点类别的呈现
/// = 改声明 + 词表条目，不改 C#。<b>零领域字面量</b>（族名/字段名/徽标一律来自 JSON）。
/// <para>
/// 两段职责：<c>families</c> 供**聚拢轴**（kind → 族）；<c>kind-shaping</c> 供**塑形**（kind → 取值字段）。
/// 解析宽容度复用框架 <see cref="NodeJson"/>（尾逗号/注释跳过）；非法/缺项 → 诊断 + 空系（不抛）。
/// </para>
/// </summary>
/// <param name="Domain">域键（声明自述）。</param>
/// <param name="KindField">载荷判别字段名。</param>
/// <param name="FamilyOrder">族声明序（聚拢组间序）。</param>
/// <param name="Families">族名 → kind 集。</param>
/// <param name="Shapings">kind → 塑形声明。</param>
/// <param name="GroupByField">二级分组轴字段名（未命中族时按该字段值分组；如 <c>nodeType</c>）。</param>
/// <param name="Read">读轴（键集取得方式；null = 容器轴默认）。</param>
public sealed record NodeGatherSpec(
    string Domain,
    string KindField,
    IReadOnlyList<string> FamilyOrder,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Families,
    IReadOnlyDictionary<string, KindShaping> Shapings,
    string? GroupByField = null,
    NodeGatherReadSpec? Read = null)
{
    /// <summary>载荷判别字段缺省名（未在声明给 <c>kindField</c> 时的单点默认）。</summary>
    public const string DefaultKindField = "kind";

    /// <summary>未知类别归入的容错族（D-5：跨语言版本漂移不崩、不丢行）。</summary>
    public const string GenericFamily = "generic";

    /// <summary>通配塑形键：未显式声明 kind 的默认塑形（治「未声明 kind 被丢行」）。</summary>
    public const string DefaultShapingKey = "*";

    /// <summary>kind → 所属族名（未声明/未知 ⇒ <see cref="GenericFamily"/>）。</summary>
    public string FamilyOf(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return GenericFamily;
        }

        foreach (var pair in Families)
        {
            if (pair.Value.Contains(kind, StringComparer.Ordinal))
            {
                return pair.Key;
            }
        }

        return Shapings.TryGetValue(kind, out var shaping) && !string.IsNullOrWhiteSpace(shaping.Family)
            ? shaping.Family
            : GenericFamily;
    }

    /// <summary>取塑形声明；未声明 ⇒ <c>false</c>（调用方按容错处理，不崩）。</summary>
    public bool TryGetShaping(string kind, out KindShaping shaping)
    {
        if (!string.IsNullOrWhiteSpace(kind) && Shapings.TryGetValue(kind, out var found))
        {
            shaping = found;
            return true;
        }

        // 通配塑形："*" 为未显式声明 kind 的默认塑形（治「未声明 kind 被丢行」）。
        if (Shapings.TryGetValue(DefaultShapingKey, out var fallback))
        {
            shaping = fallback;
            return true;
        }

        shaping = null!;
        return false;
    }

    /// <summary>
    /// 多轴分组值：判别值命中声明族 ⇒ 族名；否则退 <see cref="GroupByField"/> 字段值（如 <c>nodeType</c>）；
    /// 二者皆无 ⇒ <see cref="GenericFamily"/>。域无关（轴字段名来自声明）。
    /// </summary>
    /// <param name="fields">读项展平字段袋。</param>
    public string GroupOf(IReadOnlyDictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var family = FamilyOf(KindOf(fields));
        if (!string.Equals(family, GenericFamily, StringComparison.Ordinal))
        {
            return family;
        }

        if (!string.IsNullOrWhiteSpace(GroupByField) &&
            fields.TryGetValue(GroupByField!, out var secondary) &&
            !string.IsNullOrWhiteSpace(secondary))
        {
            return secondary!;
        }

        return GenericFamily;
    }

    /// <summary>从展平字段袋取判别值（<c>kindField</c>）；缺失/空白 ⇒ 空串（由 <see cref="FamilyOf"/> 归 generic）。</summary>
    public string KindOf(IReadOnlyDictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return fields.TryGetValue(KindField, out var kind) && !string.IsNullOrWhiteSpace(kind)
            ? kind
            : string.Empty;
    }

    /// <summary>空系（解析失败/未装配的可见化归宿；不伪造族/塑形）。</summary>
    public static NodeGatherSpec Empty(string domain) =>
        new(
            domain,
            DefaultKindField,
            Array.Empty<string>(),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            new Dictionary<string, KindShaping>(StringComparer.Ordinal));

    /// <summary>
    /// 装载聚拢声明（纯 <c>string → model</c>，文件 I/O 由上层适配器持有）。
    /// 未装配诊断出口时静默为空系；非法声明一律**可见化**（<c>gather.*</c> 族），不抛。
    /// </summary>
    /// <param name="json">声明 JSON 文本。</param>
    /// <param name="diagnostics">诊断出口（可空）。</param>
    public static NodeGatherSpec Load(string json, INodeDiagnosticsSink? diagnostics = null)
    {
        var sink = diagnostics ?? NullDiagnosticsSink.Instance;

        if (!NodeJson.TryParse(json, out var node, out var error) || node is not JsonObject root)
        {
            sink.Report(new NodeDiagnostic(
                "gather.spec-invalid",
                NodeDiagnosticSeverity.Error,
                $"聚拢声明解析失败：{error ?? "空声明"}（回退空系，不伪造）",
                "node-gather"));
            return Empty(domain: string.Empty);
        }

        var domain = ReadText(root["domain"]) ?? string.Empty;
        var kindField = ReadText(root["kindField"]);
        if (string.IsNullOrWhiteSpace(kindField))
        {
            kindField = DefaultKindField;
        }

        var groupByField = ReadText(root["groupByField"]);
        var (familyOrder, families) = ReadFamilies(root["families"], sink);
        var shapings = ReadShapings(root["kind-shaping"], sink);
        var read = ReadRead(root["read"]);

        return new NodeGatherSpec(domain, kindField!, familyOrder, families, shapings, groupByField, read);
    }

    /// <summary>读轴段（受控词原样读入；非法/缺项由闸A 前置拦截，本层不解释）。</summary>
    private static NodeGatherReadSpec? ReadRead(JsonNode? node)
    {
        if (node is not JsonObject read)
        {
            return null;
        }

        var scope = ReadText(read["read-scope"]);
        var source = ReadText(read["read-source"]);
        if (string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        return new NodeGatherReadSpec(
            scope!,
            source!,
            ReadText(read["read-edge"]),
            ReadText(read["read-normalize"]),
            ReadText(read["read-order"]),
            ReadText(read["read-empty"]));
    }

    private static (List<string> Order, Dictionary<string, IReadOnlyList<string>> Families) ReadFamilies(
        JsonNode? node, INodeDiagnosticsSink sink)
    {
        var order = new List<string>();
        var families = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (node is not JsonObject familiesNode)
        {
            return (order, families);
        }

        foreach (var pair in familiesNode)
        {
            if (pair.Value is not JsonArray array)
            {
                sink.Report(new NodeDiagnostic(
                    "gather.family-invalid",
                    NodeDiagnosticSeverity.Warning,
                    $"家族 '{pair.Key}' 须为 kind 数组：已跳过",
                    pair.Key));
                continue;
            }

            var kinds = new List<string>(array.Count);
            foreach (var entry in array)
            {
                var kind = ReadText(entry);
                if (!string.IsNullOrWhiteSpace(kind))
                {
                    kinds.Add(kind!);
                }
            }

            families[pair.Key] = kinds;
            order.Add(pair.Key);
        }

        return (order, families);
    }

    private static Dictionary<string, KindShaping> ReadShapings(JsonNode? node, INodeDiagnosticsSink sink)
    {
        var shapings = new Dictionary<string, KindShaping>(StringComparer.Ordinal);
        if (node is not JsonObject shapingNode)
        {
            return shapings;
        }

        foreach (var pair in shapingNode)
        {
            if (pair.Value is not JsonObject entry)
            {
                sink.Report(new NodeDiagnostic(
                    "gather.shaping-invalid",
                    NodeDiagnosticSeverity.Warning,
                    $"类别 '{pair.Key}' 的塑形须为对象：已跳过",
                    pair.Key));
                continue;
            }

            var family = ReadText(entry["family"]) ?? GenericFamily;
            var shaped = entry["shaped"] is not JsonValue shapedValue ||
                         !shapedValue.TryGetValue<bool>(out var shapedFlag) ||
                         shapedFlag;

            shapings[pair.Key] = new KindShaping(
                pair.Key,
                family,
                ReadText(entry["titleKey"]),
                ReadTextArray(entry["summary"]),
                ReadText(entry["badge"]),
                ReadText(entry["timeKey"]),
                shaped,
                ReadTextList(entry["ports"]),
                ReadText(entry["role"]));
        }

        return shapings;
    }

    private static string? ReadText(JsonNode? node) =>
        NodeJson.TryReadString(node, out var text) ? text : null;

    private static IReadOnlyList<string> ReadTextArray(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>(array.Count);
        foreach (var entry in array)
        {
            var text = ReadText(entry);
            if (!string.IsNullOrWhiteSpace(text))
            {
                result.Add(text!);
            }
        }

        return result;
    }

    /// <summary>读文本列表（容两形）：JSON 数组 ⇒ 逐项；标量字符串 ⇒ 按 <c>,</c>/<c>;</c>/空格拆分（发射器对未特例化字段原样透传为字符串）。</summary>
    private static IReadOnlyList<string> ReadTextList(JsonNode? node)
    {
        if (node is JsonArray)
        {
            return ReadTextArray(node);
        }

        var text = ReadText(node);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var parts = text!.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var token = part.Trim();
            if (token.Length > 0)
            {
                result.Add(token);
            }
        }

        return result;
    }
}
