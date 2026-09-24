using System.Text.Json;
using System.Text.Json.Nodes;
using Modules.Node.Diagnostics;

namespace Modules.Node.Spec;

/// <summary>声明式改图指令种类（G9）。</summary>
public enum DirectiveKind
{
    AddNode,
    RemoveNode,
    AddEdge,
    RemoveEdge,
    ReplaceNode,
    RenameKey,
    SetParam,
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

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(directivesJson, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException ex)
        {
            set._diagnostics.Report(new NodeDiagnostic(
                "directive.parse-error",
                NodeDiagnosticSeverity.Error,
                $"指令声明解析失败: {ex.Message}",
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

            var kindText = obj["kind"]?.GetValue<string>() ?? string.Empty;
            if (!Enum.TryParse<DirectiveKind>(kindText, ignoreCase: true, out var kind))
            {
                set._diagnostics.Report(new NodeDiagnostic(
                    "directive.unknown-kind",
                    NodeDiagnosticSeverity.Error,
                    $"未知指令种类 '{kindText}'：已跳过",
                    "directives"));
                continue;
            }

            var version = obj["version"] is { } versionNode ? versionNode.GetValue<int>() : 0;
            var target = obj["target"]?.GetValue<string>() ?? string.Empty;
            var match = obj["match"]?.GetValue<string>();
            var payload = obj["payload"]?.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

            set.Add(new NodeDirective(version, kind, target, match, payload));
        }

        return set;
    }

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

    /// <summary>产出执行计划（不直写库；消费方据此调用 NodeMaterializer / 图写入通道）。</summary>
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
