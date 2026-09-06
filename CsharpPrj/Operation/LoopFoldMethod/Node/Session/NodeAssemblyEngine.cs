using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.LoopFoldMethod.Node.Assemble;
using Operation.LoopFoldMethod.Node.Component;
using Operation.LoopFoldMethod.Node.Model;
using Operation.LoopFoldMethod.Node.Source;
using Operation.LoopFoldMethod.Operator;
using Operation.LoopFoldMethod.Signal;

namespace Operation.LoopFoldMethod.Node.Session;

/// <summary>
/// 节点装配引擎（端到端唯一装配入口，TH-12）—— 把「组件单一声明 + 动态上下文节点」装配为一致的多面产物：
/// <list type="number">
///   <item>从 <see cref="INodeComponentRegistry"/> 解析 AlwaysOn + 点名组件 → 组件提示词节点（无散文的纯工具/执行组件只进工具面，不进提示词节点）；</item>
///   <item>合并动态节点（历史 / task 动态内容 / 上步产出，调用方经源收集或直接传入）；</item>
///   <item><see cref="NodeBuilder"/> 依 <see cref="INodeSlotPolicy"/> 排布为头/中/尾三段；</item>
///   <item>产出 <see cref="AssemblyOutcome"/>（装配 + 命中/未注册诊断 + 组件工具面）。</item>
/// </list>
/// 纯编排、零 LLM、Agent 无关：组件声明与工具元数据由 <see cref="NodeComponent"/> 提供，不引任何消费域类型；
/// 子智能体 / task / history 的差异 = 注入的注册表与动态源不同，非各写一套拼装。
/// 本引擎只装配不推进循环（步进仍由折叠引擎驱动，见 <c>SignalDrivenFoldEngine</c>）。
/// </summary>
public sealed class NodeAssemblyEngine : INodeAssemblyEngine
{
    /// <summary>共享实例（无状态纯装配；线程安全由注入的注册表承担）。</summary>
    public static readonly NodeAssemblyEngine Default = new();

    /// <inheritdoc />
    public AssemblyOutcome Assemble(
        INodeComponentRegistry registry,
        AssemblyRequest request,
        IEnumerable<NodeItem>? dynamicNodes = null,
        INodeSlotPolicy? policy = null,
        int? maxPerBand = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(request);

        var sel = registry.ResolveSelected(request.RequestedNames);

        // ① 组件 → 提示词节点：有散文才进提示词节点；纯工具/纯执行组件仅进工具面（见 ④）。
        var nodes = new List<NodeItem>();
        foreach (var c in sel.Selected)
        {
            if (string.IsNullOrWhiteSpace(c.PromptText)) continue;
            nodes.Add(new NodeItem(c.Category, c.Order, "component:" + c.Name, c.PromptText, Key: c.Name));
        }

        // ② 合并动态上下文节点（外部已收集；历史/上步产出等）。
        if (dynamicNodes is not null) nodes.AddRange(dynamicNodes);

        // ③ 三段排布（槽位策略注入，默认稳定前缀）。
        var assembly = NodeBuilder.Build(nodes, policy, maxPerBand);

        // ④ 一致工具面：与提示词面同源于同一组命中组件。
        var applied = sel.Selected.Select(c => c.Name).ToArray();
        var tools = sel.Selected.Where(c => c.Tool is not null).Select(c => c.Tool!).ToArray();
        var executors = sel.Selected
            .Where(c => !string.IsNullOrWhiteSpace(c.ExecutorKey))
            .ToDictionary(c => c.Name, c => c.ExecutorKey!, StringComparer.OrdinalIgnoreCase);

        return new AssemblyOutcome(assembly, applied, sel.UnmetNames, new ComponentToolFace(tools, executors));
    }

    /// <inheritdoc />
    public async Task<AssemblyOutcome> AssembleAsync<TState, TVertex>(
        INodeComponentRegistry registry,
        AssemblyRequest request,
        IEnumerable<INodeSource<TState, TVertex>>? dynamicSources,
        TState state,
        FoldSignal<TVertex> signal,
        INodeSlotPolicy? policy = null,
        int? maxPerBand = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<NodeItem>? dynamicNodes = null;
        if (dynamicSources is not null)
            dynamicNodes = await new AggregateNodeSource<TState, TVertex>(dynamicSources)
                .CollectAsync(state, signal, request.StepKey ?? request.ScopeId ?? string.Empty, ct)
                .ConfigureAwait(false);

        return Assemble(registry, request, dynamicNodes, policy, maxPerBand);
    }
}
