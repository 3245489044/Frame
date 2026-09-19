using System.Text.Json;

namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>平铺组（D-12）：宿主键与卫星键同为兄弟 ⇒ 卫星被读取器忽略（静默语义丢失）。</summary>
internal sealed record LintNestedGroup(
    string Defect, IReadOnlyList<string> Hosts, IReadOnlyList<string> Keys, string Hint);

/// <summary>运行期不可达算子（D-10 / D-11）：<c>Form</c> = <c>array</c>（仅数组形态不可达）/ <c>any</c>。</summary>
internal sealed record LintUnreachableOperator(string Defect, string Key, string Form, string Hint);

/// <summary>
/// 干跑校验策略（数据视图 <c>lint</c> 段的只读投影）：**规则机制**在 <see cref="LintCommand"/>，
/// **政策**（严重度 / 平铺组 / 不可达算子 / 文案）在本段 —— 机制缺陷修复后删对应条目即失效，不改代码。
/// </summary>
internal sealed record LintPolicy(
    IReadOnlyDictionary<string, string> Severity,
    IReadOnlyList<LintNestedGroup> NestedGroups,
    IReadOnlyList<LintUnreachableOperator> Unreachable,
    IReadOnlyDictionary<string, string> Messages)
{
    private const string Error = "error";
    private const string Warn = "warn";

    /// <summary>按规则 id 取严重度（未登记 → error，防静默降级）。</summary>
    internal string SeverityOf(string rule)
        => Severity.TryGetValue(rule, out var severity) ? severity : Error;

    /// <summary>是否为阻断项。</summary>
    internal static bool IsError(string severity) => string.Equals(severity, Error, StringComparison.Ordinal);

    /// <summary>是否为提示项。</summary>
    internal static bool IsWarn(string severity) => string.Equals(severity, Warn, StringComparison.Ordinal);

    /// <summary>规则文案（缺省 → 可见缺失标记，不静默给空串）。</summary>
    internal string Text(string rule, params object[] args)
    {
        var text = Messages.TryGetValue(rule, out var value) ? value : $"<missing:{rule}>";
        return args.Length == 0 ? text : string.Format(text, args);
    }

    /// <summary>装载策略（同上数据视图；缺 <c>lint</c> 段 → 空策略，全部按 error 兜底）。</summary>
    internal static LintPolicy Load(string dataPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(dataPath));
        if (!document.RootElement.TryGetProperty("lint", out var lint) || lint.ValueKind != JsonValueKind.Object)
            return new LintPolicy(Empty, [], [], Empty);

        var severity = new Dictionary<string, string>(StringComparer.Ordinal);
        if (lint.TryGetProperty("severity", out var severityNode) && severityNode.ValueKind == JsonValueKind.Object)
            foreach (var property in severityNode.EnumerateObject())
                if (property.Value.ValueKind == JsonValueKind.String)
                    severity[property.Name] = property.Value.GetString()!;

        var messages = new Dictionary<string, string>(StringComparer.Ordinal);
        if (lint.TryGetProperty("messages", out var messageNode) && messageNode.ValueKind == JsonValueKind.Object)
            foreach (var property in messageNode.EnumerateObject())
                if (property.Value.ValueKind == JsonValueKind.String)
                    messages[property.Name] = property.Value.GetString()!;

        var nested = new List<LintNestedGroup>();
        if (lint.TryGetProperty("nestedGroups", out var nestedNode) && nestedNode.ValueKind == JsonValueKind.Array)
            foreach (var item in nestedNode.EnumerateArray())
                nested.Add(new LintNestedGroup(
                    Text(item, "defect"), Strings(item, "hosts"), Strings(item, "keys"), Text(item, "hint")));

        var unreachable = new List<LintUnreachableOperator>();
        if (lint.TryGetProperty("unreachableOperators", out var unreachableNode)
            && unreachableNode.ValueKind == JsonValueKind.Array)
            foreach (var item in unreachableNode.EnumerateArray())
                unreachable.Add(new LintUnreachableOperator(
                    Text(item, "defect"), Text(item, "key"), Text(item, "form") is { Length: > 0 } form ? form : "any",
                    Text(item, "hint")));

        return new LintPolicy(severity, nested, unreachable, messages);
    }

    private static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static string Text(JsonElement owner, string name)
        => owner.ValueKind == JsonValueKind.Object
           && owner.TryGetProperty(name, out var node)
           && node.ValueKind == JsonValueKind.String
            ? node.GetString() ?? string.Empty
            : string.Empty;

    private static IReadOnlyList<string> Strings(JsonElement owner, string name)
        => owner.ValueKind == JsonValueKind.Object
           && owner.TryGetProperty(name, out var node)
           && node.ValueKind == JsonValueKind.Array
            ? node.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty).ToArray()
            : [];
}
