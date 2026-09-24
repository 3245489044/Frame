using System.Text.Json.Nodes;
using Modules.Node.Diagnostics;

namespace Modules.Node.Spec;

/// <summary>声明式改图指令种类（G9；U8 = 独立声明段：指令经本枚举归一，宿主类型投影到此）。</summary>
public enum DirectiveKind
{
    AddNode,
    RemoveNode,
    AddEdge,
    RemoveEdge,
    ReplaceNode,
    RenameKey,
    SetParam,
    MergeNodes,
    SplitNode,
    RedirectEdge,
    Composite,
}

/// <summary>单条指令（可审计重放：含版本号）。</summary>
public sealed record NodeDirective(int Version, DirectiveKind Kind, string Target, string? MatchPattern, string? PayloadJson);

/// <summary>计划项（框架只产计划，不直写库）。</summary>
public sealed record DirectiveAction(int Version, DirectiveKind Kind, string Target, string Description);

public sealed record DirectivePlan(
    int FromVersion,
    int ToVersion,
    IReadOnlyList<DirectiveAction> Actions,
    IReadOnlyList<NodeDiagnostic> Diagnostics);

/// <summary>
/// 改图指令集（G9 收编）：指令进声明面、版本单调递增、只产计划（不直写库）。
/// </summary>
public sealed class NodeDirectiveSet
{
    private readonly SortedDictionary<int, List<NodeDirective>> _byVersion = new();
    private readonly INodeDiagnosticsSink _diagnostics;

    public NodeDirectiveSet(INodeDiagnosticsSink? diagnostics = null) =>
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;

    public int Count => _byVersion.Values.Sum(list => list.Count);

    public int CurrentVersion => _byVersion.Count == 0 ? 0 : _byVersion.Keys.Max();

    public IReadOnlyList<NodeDirective> All =>
        _byVersion.Values.SelectMany(list => list).ToArray();

    /// <summary>装载指令声明（JSON 数组）。未知种类 / 版本回退 → 诊断（不静默）。</summary>
    public static NodeDirectiveSet Parse(string directivesJson, INodeDiagnosticsSink? diagnostics = null)
    {
        var set = new NodeDirectiveSet(diagnostics);

        if (string.IsNullOrWhiteSpace(directivesJson))
        {
            return set;
        }

        if (!NodeJson.TryParse(directivesJson, out var root, out var parseError))
        {
            set._diagnostics.Report(new NodeDiagnostic(
                "directive.parse-error",
                NodeDiagnosticSeverity.Error,
                $"指令声明解析失败: {parseError ?? "空声明"}",
                "directives"));
            return set;
        }

        if (root is not JsonArray array)
        {
            set._diagnostics.Report(new NodeDiagnostic(
                "directive.parse-error",
                NodeDiagnosticSeverity.Error,
                "指令声明须为 JSON 数组",
                "directives"));
            return set;
        }

        var index = 0;
        foreach (var item in array)
        {
            index++;
            if (item is not JsonObject obj)
            {
                set._diagnostics.Report(new NodeDiagnostic(
                    "directive.parse-error",
                    NodeDiagnosticSeverity.Error,
                    $"第 {index} 条指令非对象：已跳过（不静默）",
                    "directives"));
                continue;
            }

            var kindText = NodeJson.TryReadString(obj["kind"], out var kindValue) ? kindValue ?? string.Empty : string.Empty;
            if (!Enum.TryParse<DirectiveKind>(kindText, ignoreCase: true, out var kind))
            {
                set._diagnostics.Report(new NodeDiagnostic(
                    "directive.unknown-kind",
                    NodeDiagnosticSeverity.Error,
                    $"未知指令种类 '{kindText}'：已跳过",
                    "directives"));
                continue;
            }

            var version = 0;
            if (obj["version"] is { } versionNode && !NodeJson.TryReadInt(versionNode, out version))
            {
                set._diagnostics.Report(new NodeDiagnostic(
                    "directive.version-invalid",
                    NodeDiagnosticSeverity.Error,
                    "指令 version 须为整数：已回退为 0（不静默）",
                    "directives"));
            }

            var target = NodeJson.TryReadString(obj["target"], out var targetValue) ? targetValue ?? string.Empty : string.Empty;
            var match = NodeJson.TryReadString(obj["match"], out var matchValue) ? matchValue : null;
            var payload = obj["payload"]?.ToJsonString(NodeJson.WriteOptions);

            set.Add(new NodeDirective(version, kind, target, match, payload));
        }

        return set;
    }

    /// <summary>
    /// 装载单条指令：<b>数据面</b>版本回退 → 诊断 + 丢弃（不抛，宿主可继续装配）；
    /// 与 <see cref="Plan"/> 的 fail-fast（查询未来版本属调用方编程错误）口径不同，二者不混用。
    /// </summary>
    public void Add(NodeDirective directive)
    {
        if (directive.Version < CurrentVersion)
        {
            _diagnostics.Report(new NodeDiagnostic(
                "directive.version-regression",
                NodeDiagnosticSeverity.Error,
                $"指令版本回退（当前 {CurrentVersion} → {directive.Version}）：拒绝装载",
                directive.Target));
            return;
        }

        if (string.IsNullOrWhiteSpace(directive.Target))
        {
            _diagnostics.Report(new NodeDiagnostic(
                "directive.target-missing",
                NodeDiagnosticSeverity.Warning,
                "指令缺少目标：缺口可见化（不静默执行）",
                directive.Kind.ToString()));
        }

        if (!_byVersion.TryGetValue(directive.Version, out var list))
        {
            list = new List<NodeDirective>();
            _byVersion[directive.Version] = list;
        }

        list.Add(directive);
    }

    public IReadOnlyList<NodeDirective> Pending(int fromVersion) =>
        _byVersion.Where(pair => pair.Key > fromVersion).SelectMany(pair => pair.Value).ToArray();

    /// <summary>
    /// 产出执行计划（不直写库；消费方据此调用 NodeMaterializer / 图写入通道）。
    /// <paramref name="fromVersion"/> 超出当前版本水位属调用方编程错误 ⇒ fail-fast 抛出（不静默降级）。
    /// </summary>
    public DirectivePlan Plan(int fromVersion = 0)
    {
        if (CurrentVersion < fromVersion)
        {
            throw new InvalidOperationException($"版本回退：fromVersion={fromVersion} > current={CurrentVersion}");
        }

        var pending = Pending(fromVersion);
        var actions = pending
            .Select(d => new DirectiveAction(d.Version, d.Kind, d.Target, Describe(d)))
            .ToArray();

        var diagnostics = pending.Count == 0
            ? new[] { new NodeDiagnostic("directive.empty", NodeDiagnosticSeverity.Info, "无待执行指令", $"v{fromVersion}") }
            : Array.Empty<NodeDiagnostic>();

        return new DirectivePlan(fromVersion, CurrentVersion, actions, diagnostics);
    }

    private static string Describe(NodeDirective directive) => directive.MatchPattern is null
        ? $"{directive.Kind} {directive.Target}"
        : $"{directive.Kind} {directive.Target} (match={directive.MatchPattern})";
}
