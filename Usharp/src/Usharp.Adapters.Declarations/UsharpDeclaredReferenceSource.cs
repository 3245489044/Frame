using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Modules.Node.Relations;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;
using Usharp.Emit;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// 声明引用源（G3/G4 的 USharp 投影实现）：把 <c>.ushar</c> 声明（workflows 的 <c>[Node] fn/inports/outports</c>
/// + <c>[Flow]</c> 步骤序）投影为引用库边集 —— 静态边（工作流组合 <c>Owns</c> / 控制流 <c>Calls</c>）与
/// 数据流边（<c>node_to_node</c>：上游 <c>outports</c> ∩ 下游 <c>inports</c> 在**构建期**确定）。
/// <para>权威链 = .ushar 声明 → 引用库（D3/D4）；取代运行期从节点内 <c>RelationsJson</c> 推导。</para>
/// <para>纪律：编译期零写库（只产边集）；本类不引存储写面（写仍归唯一 owner <c>ReferenceGraphCoordinator</c>）。</para>
/// </summary>
public sealed class UsharpDeclaredReferenceSource : IDeclaredReferenceSource
{
    private readonly IReadOnlyList<UsharpFlowDecl> _flows;
    private readonly IReadOnlyDictionary<string, UsharpNodeSpec> _nodes;

    /// <summary>由已编译声明构建（flows + [Node] 端口）。</summary>
    public UsharpDeclaredReferenceSource(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        _flows = compilation.Flows;
        _nodes = UsharpNodePortReader.Read(compilation.Declarations);
    }

    /// <summary>从配置根装载（<c>nodes/*.ushar</c> + <c>workflows/*.ushar</c>）并编译。</summary>
    public static UsharpDeclaredReferenceSource FromConfigDirectory(string configRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configRoot);
        var documents = new List<(string Path, string Text)>();
        AppendDirectory(documents, Path.Combine(configRoot, "nodes"));
        AppendDirectory(documents, Path.Combine(configRoot, "workflows"));
        return new UsharpDeclaredReferenceSource(UsharpCompilation.Compile(documents));
    }

    /// <summary>从配置根装载（配置根缺省 ⇒ null，调用方回退 RelationsJson 兼容读）。</summary>
    public static UsharpDeclaredReferenceSource? TryFromConfigDirectory(string? configRoot)
        => string.IsNullOrWhiteSpace(configRoot) ? null : FromConfigDirectory(configRoot!);

    /// <inheritdoc />
    public IReadOnlyList<string> WorkflowIds
        => _flows.Select(f => f.FlowId).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();

    /// <inheritdoc />
    public IReadOnlyList<StaticReference> GetStaticEdges(string workflowId)
    {
        var flow = FindFlow(workflowId);
        if (flow is null) return Array.Empty<StaticReference>();
        var steps = flow.Steps
            .Select(s => s.Key)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToList();
        return NodeRelationDeriver.DeriveWorkflowControlFlowEdges(workflowId, steps);
    }

    /// <inheritdoc />
    public IReadOnlyList<NodeRelation> GetNodeRelations(string workflowId, string nodeKey)
    {
        if (string.IsNullOrWhiteSpace(nodeKey)) return Array.Empty<NodeRelation>();
        var resolved = ResolveStep(nodeKey);
        if (resolved is null) return Array.Empty<NodeRelation>();
        var (flow, stepKey) = resolved.Value;
        if (!_nodes.TryGetValue(stepKey, out var target)) return Array.Empty<NodeRelation>();

        var wires = new List<NodeRelationDeriver.DeclaredPortWire>();
        foreach (var step in flow.Steps)
        {
            if (string.Equals(step.Key, stepKey, StringComparison.Ordinal)) continue;
            if (!_nodes.TryGetValue(step.Key, out var upstream)) continue;
            foreach (var inPort in target.InputPorts)
                if (upstream.OutputPorts.Contains(inPort, StringComparer.Ordinal))
                    // 目标端 = 运行期节点键（边挂到实际节点）；上游端 = 声明步骤键（构建期确定）。
                    wires.Add(new NodeRelationDeriver.DeclaredPortWire(step.Key, inPort, nodeKey, inPort));
        }

        return wires.Count == 0
            ? Array.Empty<NodeRelation>()
            : NodeRelationDeriver.DeriveDeclaredDataflowEdges(workflowId, wires).ToList();
    }

    private UsharpFlowDecl? FindFlow(string flowId)
        => _flows.FirstOrDefault(f => string.Equals(f.FlowId, flowId, StringComparison.Ordinal));

    /// <summary>解析运行期节点键到（声明流, 声明步骤键）：先精确匹配，再按 '.' 分段匹配（覆盖模板化运行期键）。</summary>
    private (UsharpFlowDecl Flow, string StepKey)? ResolveStep(string nodeKey)
    {
        foreach (var flow in _flows)
            foreach (var step in flow.Steps)
                if (string.Equals(step.Key, nodeKey, StringComparison.Ordinal))
                    return (flow, step.Key);

        var segments = nodeKey.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;
        foreach (var flow in _flows)
            foreach (var step in flow.Steps)
                if (segments.Contains(step.Key, StringComparer.Ordinal))
                    return (flow, step.Key);
        return null;
    }

    private static void AppendDirectory(List<(string Path, string Text)> sink, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
        foreach (var file in Directory.EnumerateFiles(directory, "*.ushar", SearchOption.TopDirectoryOnly)
                     .OrderBy(p => p, StringComparer.Ordinal))
            sink.Add((file, File.ReadAllText(file)));
    }
}
