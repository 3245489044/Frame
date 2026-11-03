using System;
using System.Collections.Generic;
using System.Linq;
using Plate.DslMethod;
using Plate.DslMethod.Model;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Adapters.Agent;

/// <summary>行为面：模式词表（<c>[Space] HistoryMode</c> 坐标）+ mode→行为规则（<c>[Rule]</c>）。</summary>
public sealed record AgentBehaviorTable(IReadOnlyList<string> Modes, IReadOnlyList<AgentRuleEdge> Rules);

/// <summary>一条行为规则边（<c>[Rule] Name: scope=… | when=… | action=…</c>）。</summary>
public sealed record AgentRuleEdge(string RuleId, string? Scope, string? When, string? Action, string SourceFile, int LineNumber);

/// <summary>
/// 行为面领域反转包（P5）：<c>history-modes.ushar</c> 的模式词表 + <c>[Rule]</c> 的 mode→行为映射
/// 反转为 facet；<c>US7005</c> 对账 <c>[Rule].action ∈ [Node]</c>（消 <c>StatePromptCatalog</c> 手写表）。
/// </summary>
public sealed class AgentBehaviorPack : UsharpDomainPackSpec
{
    /// <summary>US7606：<c>[Rule] action=</c> 须 ∈ 已声明 <c>[Node]</c> / 动作注册表。</summary>
    public const string CodeActionUnknown = UsharpCodes.BehaviorActionUnknown;

    /// <summary>模式词表空间 id（历史命名的内置档；通用取用时经构造参数可换）。</summary>
    public string ModeSpaceId { get; init; } = "HistoryMode";

    public AgentBehaviorPack(IReadOnlyCollection<string>? knownActions = null, string? modeSpaceId = null)
        : base("agent.behavior",
            new IUsharpFacetProvider[] { new BehaviorFacetProvider(modeSpaceId ?? "HistoryMode") },
            new IGateRule<UsharpGateModel>[] { new BehaviorActionGateRule(knownActions ?? Array.Empty<string>()) })
    {
        if (!string.IsNullOrWhiteSpace(modeSpaceId)) ModeSpaceId = modeSpaceId!;
    }
}

/// <summary>行为面 facet（<see cref="AgentBehaviorTable"/>）。</summary>
internal sealed class BehaviorFacetProvider : IUsharpFacetProvider
{
    private readonly string _modeSpaceId;

    public BehaviorFacetProvider(string modeSpaceId) => _modeSpaceId = modeSpaceId;

    public Type FacetType => typeof(AgentBehaviorTable);
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Space, DslSymbolKind.Rule };

    public object Project(UsharpFacetContext ctx)
    {
        var modes = new List<string>();
        foreach (var decl in ctx.Declarations.OfKind(DslSymbolKind.Space))
        {
            var space = UsharpSpaceDecl.From(decl);
            if (space is null || !string.Equals(space.SpaceId, _modeSpaceId, StringComparison.Ordinal)) continue;
            foreach (var axis in space.Axes) modes.Add(axis.Name);
        }

        var rules = ctx.Declarations.OfKind(DslSymbolKind.Rule)
            .Select(d => new AgentRuleEdge(
                d.Name ?? string.Empty,
                UsharpLineReader.NullIfEmpty(d.Field("scope")),
                UsharpLineReader.NullIfEmpty(d.Field("when")),
                UsharpLineReader.NullIfEmpty(d.Field("action")),
                d.SourceFile, d.LineNumber))
            .ToList();

        return new AgentBehaviorTable(modes, rules);
    }
}

/// <summary>US7005：<c>[Rule] action=</c> 须 ∈ 已声明 <c>[Node]</c> / 动作注册表（未注入 ⇒ 仍按 [Node] 判）。</summary>
internal sealed class BehaviorActionGateRule : IGateRule<UsharpGateModel>
{
    private readonly HashSet<string> _knownActions;

    public BehaviorActionGateRule(IReadOnlyCollection<string> knownActions)
        => _knownActions = new HashSet<string>(knownActions, StringComparer.Ordinal);

    public string Name => "agent.behavior.action";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        if (!model.TryGetFacet<AgentBehaviorTable>(out var table)) return;
        var nodes = model.Declarations.NodeNames;

        foreach (var r in table.Rules)
        {
            if (string.IsNullOrWhiteSpace(r.Action)) continue;
            if (_knownActions.Contains(r.Action!) || nodes.Contains(r.Action!)) continue;
            sink.Report(ConversionDiagnostic.Error(AgentBehaviorPack.CodeActionUnknown,
                $"[Rule] {r.RuleId}: action='{r.Action}' 未在已声明 [Node] / 动作注册表中（闸A：声明↔实现对账）",
                new SourceSpan(r.SourceFile, r.LineNumber, 0)));
        }
    }
}
