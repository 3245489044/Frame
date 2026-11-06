using System;
using System.Collections.Generic;
using System.Linq;
using Plate.DslMethod;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Share.LogicComposition.Conversion.Vocabulary;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Adapters.Agent;

/// <summary>端口节点（<c>[Node] Name: fn=… | skill=… | llm=… | kind=… | inports=… | outports=…</c>）。
/// <para><c>fn=</c> 为可执行体绑定（必填）；<c>skill=</c>/<c>llm=</c> 为可选执行体归属且二者互斥，
/// 由闸A <c>US7802</c> 保证。</para></summary>
public sealed record AgentPortNode(
    string Name, string? Fn, IReadOnlyList<string> Inports, IReadOnlyList<string> Outports, string SourceFile, int LineNumber,
    string? Skill = null, string? Llm = null, string? Kind = null);

/// <summary>
/// 端口面领域反转包（P5）：<c>[Node]</c> 端口节点反转为 facet；
/// 闸A 对账 <c>fn ∈ 函数注册表</c>（US7003）· <c>skill ∈ [Skill]</c>（US7801）·
/// <c>fn= 必填 + skill=/llm= 可选互斥</c>（US7802）· <c>kind ∈ 已声明类别空间</c>（US7803）。
/// </summary>
public sealed class AgentPortPack : UsharpDomainPackSpec
{
    /// <summary>US7604：<c>[Node] fn=</c> 须 ∈ 函数注册表。</summary>
    public const string CodeFnUnknown = UsharpCodes.PortFnUnknown;

    /// <summary>US7801：<c>[Node] skill=</c> 须 ∈ 已声明 <c>[Skill]</c> 空间。</summary>
    public const string CodeSkillUnknown = UsharpCodes.PortSkillUndeclared;

    /// <summary>US7802：会话步 <c>[Node]</c> 的 <c>fn</c> 必填（<c>skill</c>/<c>llm</c> 可选互斥，可与之并存）。</summary>
    public const string CodeExecutorNotExclusive = UsharpCodes.PortExecutorNotExclusive;

    /// <summary>US7803：<c>[Node] kind=</c> 须 ∈ 已声明执行体类别空间。</summary>
    public const string CodeKindUnknown = UsharpCodes.PortKindUnknown;

    /// <summary>执行体类别空间 id（会话步执行体归属类别；取值域由声明面兜）。</summary>
    public const string ExecutorKindSpaceId = "SessionStepExecutor";

    public AgentPortPack(IReadOnlyCollection<string>? knownFunctions = null)
        : base("agent.port",
            new IUsharpFacetProvider[]
            {
                new UsharpFacetProvider<AgentPortNode>(DslSymbolKind.Node, ctx =>
                    ctx.Declarations.OfKind(DslSymbolKind.Node)
                        .Where(d => !string.IsNullOrWhiteSpace(d.Name))
                        .Select(d => new AgentPortNode(
                            d.Name!,
                            UsharpLineReader.NullIfEmpty(d.Field("fn")),
                            UsharpLineReader.SplitList(d.Field("inports"), ',', ' '),
                            UsharpLineReader.SplitList(d.Field("outports"), ',', ' '),
                            d.SourceFile, d.LineNumber,
                            UsharpLineReader.NullIfEmpty(d.Field("skill")),
                            UsharpLineReader.NullIfEmpty(d.Field("llm")),
                            UsharpLineReader.NullIfEmpty(d.Field("kind"))))),
            },
            new IGateRule<UsharpGateModel>[]
            {
                new PortFnGateRule(knownFunctions ?? Array.Empty<string>()),
                new PortSkillGateRule(),
                new PortExecutorExclusivityGateRule(),
                new PortKindGateRule(),
            })
    {
    }
}

/// <summary>US7604：<c>[Node] fn=</c> 须 ∈ 函数注册表（未注入 ⇒ 不判定）。</summary>
internal sealed class PortFnGateRule : IGateRule<UsharpGateModel>
{
    private readonly OrderedWhitelist _knownFunctions;

    public PortFnGateRule(IReadOnlyCollection<string> knownFunctions)
        => _knownFunctions = new OrderedWhitelist(knownFunctions);

    public string Name => "agent.port.fn";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        if (_knownFunctions.Items.Count == 0) return;
        if (!model.TryGetFacet<IReadOnlyList<AgentPortNode>>(out var nodes)) return;

        foreach (var n in nodes)
        {
            if (string.IsNullOrWhiteSpace(n.Fn)) continue;
            if (_knownFunctions.Contains(n.Fn!)) continue;
            sink.Report(ConversionDiagnostic.Error(AgentPortPack.CodeFnUnknown,
                $"[Node] {n.Name}: fn='{n.Fn}' 未在函数注册表中（闸A：声明↔实现对账）",
                new SourceSpan(n.SourceFile, n.LineNumber, 0)));
        }
    }
}

/// <summary>
/// US7801：<c>[Node] skill=</c> 须 ∈ 已声明 <c>[Skill]</c>（引用即边：未声明即编译报错）。
/// <para>本编译未含任何 <c>[Skill]</c> 声明（技能面不在编译范围内）⇒ 不判定（零回归，语义同 US7003 的「未注入 ⇒ 不判定」）。</para>
/// </summary>
internal sealed class PortSkillGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "agent.port.skill";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        if (!model.TryGetFacet<IReadOnlyList<AgentPortNode>>(out var nodes)) return;
        if (!model.TryGetFacet<IReadOnlyList<UsharpSkillDecl>>(out var skills) || skills.Count == 0) return;

        var declared = new HashSet<string>(skills.Select(s => s.Name), StringComparer.Ordinal);
        foreach (var n in nodes)
        {
            if (string.IsNullOrWhiteSpace(n.Skill)) continue;
            if (declared.Contains(n.Skill!)) continue;
            sink.Report(ConversionDiagnostic.Error(AgentPortPack.CodeSkillUnknown,
                $"[Node] {n.Name}: skill='{n.Skill}' 未在 [Skill] 声明面中登记（闸A：引用必须落在已声明空间）",
                new SourceSpan(n.SourceFile, n.LineNumber, 0)));
        }
    }
}

/// <summary>
/// US7802：会话步 <c>[Node]</c> 的执行体绑定须完备 —— <c>fn=</c> 必填（运行时按 <c>fn</c> 直调），
/// <c>skill=</c>/<c>llm=</c> 为可选执行体归属且二者互斥（可与 <c>fn=</c> 并存）。
/// <para>放宽理由（消除「节点名兜底」隐式耦合）：旧 JSON 的 <c>NodeId</c> 与 <c>FuncName</c> 本为两个字段，
/// 「恰一」约束迫使会话步用 <c>skill=</c> 表达归属时放弃 <c>fn=</c>，只能回退节点名 ⇒ 迁移信息丢失。
/// 允许 <c>fn=</c> 与 <c>skill=</c> 并存后，声明面可无损表达「节点身份 ≠ 可执行体」。</para>
/// <para>作用域 = 被 <c>session*</c> 流步骤引用的节点（逐 spec 迁移，不一次打红其余工作流）；
/// 无法确定会话流（本编译无 <c>[Flow]</c>）⇒ 不判定。</para>
/// </summary>
internal sealed class PortExecutorExclusivityGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "agent.port.executor";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        if (!model.TryGetFacet<IReadOnlyList<AgentPortNode>>(out var nodes)) return;

        var sessionSteps = SessionStepKeys(model);
        if (sessionSteps.Count == 0) return;

        foreach (var n in nodes)
        {
            if (!sessionSteps.Contains(n.Name)) continue;

            // fn= 必填：运行时按 fn 直调注册函数；缺失即无法解析可执行体（禁节点名兜底）。
            if (Count(n.Fn) == 0)
            {
                sink.Report(ConversionDiagnostic.Error(AgentPortPack.CodeExecutorNotExclusive,
                    $"[Node] {n.Name}: 会话步须显式声明 fn=（运行时按 fn 直调注册函数；禁以节点名兜底）",
                    new SourceSpan(n.SourceFile, n.LineNumber, 0)));
                continue;
            }

            // skill= / llm= 为可选归属，二者互斥（且可与 fn= 并存）。
            if (Count(n.Skill) + Count(n.Llm) > 1)
            {
                sink.Report(ConversionDiagnostic.Error(AgentPortPack.CodeExecutorNotExclusive,
                    $"[Node] {n.Name}: skill=/llm= 互斥（实际 {Count(n.Skill) + Count(n.Llm)} 个）",
                    new SourceSpan(n.SourceFile, n.LineNumber, 0)));
            }
        }
    }

    /// <summary>会话流覆盖的步骤键（<c>session*</c> 流声明序；唯一口径，禁各规则各写一遍）。</summary>
    internal static HashSet<string> SessionStepKeys(UsharpGateModel model)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var flow in model.Flows)
        {
            if (string.IsNullOrWhiteSpace(flow.FlowId)) continue;
            if (!flow.FlowId.StartsWith("session", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var step in flow.Steps)
                if (!string.IsNullOrWhiteSpace(step.Key)) keys.Add(step.Key);
        }
        return keys;
    }

    private static int Count(string? value) => string.IsNullOrWhiteSpace(value) ? 0 : 1;
}

/// <summary>US7803：<c>[Node] kind=</c> 须 ∈ 已声明执行体类别空间（<c>SessionStepExecutor</c> 坐标 ∪ 内置三态）。</summary>
internal sealed class PortKindGateRule : IGateRule<UsharpGateModel>
{
    private static readonly string[] BuiltinKinds = { "internal", "external", "composition" };

    public string Name => "agent.port.kind";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        if (!model.TryGetFacet<IReadOnlyList<AgentPortNode>>(out var nodes)) return;

        var allowed = new OrderedWhitelist(
            BuiltinKinds,
            model.Spaces.TryGet(AgentPortPack.ExecutorKindSpaceId, out var space)
                ? space.Axes.Select(axis => axis.Name)
                : null);

        foreach (var n in nodes)
        {
            if (string.IsNullOrWhiteSpace(n.Kind)) continue;
            if (allowed.Contains(n.Kind!)) continue;
            sink.Report(ConversionDiagnostic.Error(AgentPortPack.CodeKindUnknown,
                $"[Node] {n.Name}: kind='{n.Kind}' 不在已声明执行体类别空间（{string.Join("|", allowed.Items.OrderBy(k => k, StringComparer.Ordinal))}）",
                new SourceSpan(n.SourceFile, n.LineNumber, 0)));
        }
    }
}
