using System;
using System.Collections.Generic;
using System.Linq;
using Plate.DslMethod;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Adapters.Agent;

/// <summary>端口节点（<c>[Node] Name: fn=… | skill=… | llm=… | kind=… | inports=… | outports=…</c>）。
/// <para>执行体归属三态（<see cref="Fn"/>/<see cref="Skill"/>/<see cref="Llm"/>）互斥完备，由闸A <c>US7802</c> 保证。</para></summary>
public sealed record AgentPortNode(
    string Name, string? Fn, IReadOnlyList<string> Inports, IReadOnlyList<string> Outports, string SourceFile, int LineNumber,
    string? Skill = null, string? Llm = null, string? Kind = null);

/// <summary>
/// 端口面领域反转包（P5）：<c>[Node]</c> 端口节点反转为 facet；
/// 闸A 对账 <c>fn ∈ 函数注册表</c>（US7003）· <c>skill ∈ [Skill]</c>（US7801）·
/// <c>fn/skill/llm 恰一</c>（US7802）· <c>kind ∈ 已声明类别空间</c>（US7803）。
/// </summary>
public sealed class AgentPortPack : IUsharpDomainPack
{
    /// <summary>US7003：<c>[Node] fn=</c> 须 ∈ 函数注册表。</summary>
    public const string CodeFnUnknown = "US7003";

    /// <summary>US7801：<c>[Node] skill=</c> 须 ∈ 已声明 <c>[Skill]</c> 空间。</summary>
    public const string CodeSkillUnknown = UsharpCodes.PortSkillUndeclared;

    /// <summary>US7802：<c>[Node]</c> 的 <c>fn</c>/<c>skill</c>/<c>llm</c> 恰一（互斥完备）。</summary>
    public const string CodeExecutorNotExclusive = UsharpCodes.PortExecutorNotExclusive;

    /// <summary>US7803：<c>[Node] kind=</c> 须 ∈ 已声明执行体类别空间。</summary>
    public const string CodeKindUnknown = UsharpCodes.PortKindUnknown;

    /// <summary>执行体类别空间 id（会话步执行体归属类别；取值域由声明面兜）。</summary>
    public const string ExecutorKindSpaceId = "SessionStepExecutor";

    public AgentPortPack(IReadOnlyCollection<string>? knownFunctions = null)
    {
        FacetProviders = new IUsharpFacetProvider[] { new PortNodeFacetProvider() };
        GateRules = new IGateRule<UsharpGateModel>[]
        {
            new PortFnGateRule(knownFunctions ?? Array.Empty<string>()),
            new PortSkillGateRule(),
            new PortExecutorExclusivityGateRule(),
            new PortKindGateRule(),
        };
        FacetProjectors = new IUsharpFacetProjector[] { new PortContractProjector() };
    }

    public string Id => "agent.port";
    public IReadOnlyList<string> DiagnosticCodePrefixes => new[] { "US" };
    public IReadOnlyList<IUsharpFacetProvider> FacetProviders { get; }
    public IReadOnlyList<IGateRule<UsharpGateModel>> GateRules { get; }
    public IReadOnlyList<IUsharpFacetProjector> FacetProjectors { get; }
}

/// <summary>端口节点 facet（<c>IReadOnlyList&lt;AgentPortNode&gt;</c>；声明序）。</summary>
internal sealed class PortNodeFacetProvider : IUsharpFacetProvider
{
    public Type FacetType => typeof(IReadOnlyList<AgentPortNode>);
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Node };

    public object Project(UsharpFacetContext ctx)
        => ctx.Declarations.OfKind(DslSymbolKind.Node)
            .Where(d => !string.IsNullOrWhiteSpace(d.Name))
            .Select(d => new AgentPortNode(
                d.Name!,
                NullIfEmpty(d.Field("fn")),
                SplitList(d.Field("inports")),
                SplitList(d.Field("outports")),
                d.SourceFile, d.LineNumber,
                NullIfEmpty(d.Field("skill")),
                NullIfEmpty(d.Field("llm")),
                NullIfEmpty(d.Field("kind"))))
            .ToList();

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> SplitList(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>US7003：<c>[Node] fn=</c> 须 ∈ 函数注册表（未注入 ⇒ 不判定）。</summary>
internal sealed class PortFnGateRule : IGateRule<UsharpGateModel>
{
    private readonly HashSet<string> _knownFunctions;

    public PortFnGateRule(IReadOnlyCollection<string> knownFunctions)
        => _knownFunctions = new HashSet<string>(knownFunctions, StringComparer.Ordinal);

    public string Name => "agent.port.fn";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        if (_knownFunctions.Count == 0) return;
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
/// US7802：会话步 <c>[Node]</c> 的执行体归属须「恰一」（<c>fn</c>/<c>skill</c>/<c>llm</c> 互斥完备）。
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
            var count = Count(n.Fn) + Count(n.Skill) + Count(n.Llm);
            if (count == 1) continue;
            sink.Report(ConversionDiagnostic.Error(AgentPortPack.CodeExecutorNotExclusive,
                $"[Node] {n.Name}: 执行体归属须 fn=/skill=/llm= 恰一（实际 {count} 个：{(count == 0 ? "全缺" : "多声明")}）",
                new SourceSpan(n.SourceFile, n.LineNumber, 0)));
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

        var allowed = new HashSet<string>(BuiltinKinds, StringComparer.Ordinal);
        if (model.Spaces.TryGet(AgentPortPack.ExecutorKindSpaceId, out var space))
            foreach (var axis in space.Axes) allowed.Add(axis.Name);

        foreach (var n in nodes)
        {
            if (string.IsNullOrWhiteSpace(n.Kind)) continue;
            if (allowed.Contains(n.Kind!)) continue;
            sink.Report(ConversionDiagnostic.Error(AgentPortPack.CodeKindUnknown,
                $"[Node] {n.Name}: kind='{n.Kind}' 不在已声明执行体类别空间（{string.Join("|", allowed.OrderBy(k => k, StringComparer.Ordinal))}）",
                new SourceSpan(n.SourceFile, n.LineNumber, 0)));
        }
    }
}

/// <summary>投影器 <c>port.contract</c>：端口契约产物（节点 fn/inports/outports）。</summary>
internal sealed class PortContractProjector : IUsharpFacetProjector
{
    public string Id => "port.contract";
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Node };

    public object Emit(UsharpCompilation compilation) => compilation.Facet<IReadOnlyList<AgentPortNode>>();
}
