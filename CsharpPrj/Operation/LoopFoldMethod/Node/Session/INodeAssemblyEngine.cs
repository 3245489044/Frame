using System.Collections.Generic;
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
/// 装配引擎端口 —— 消费 <see cref="INodeComponentRegistry"/>（组件单一声明）+ 动态节点/源 → 产出 <see cref="AssemblyOutcome"/>。
/// 框架的唯一装配入口；子智能体 / task / history 只换注册表与动态源/请求，不另起一套拼装（TH-12）。
/// 纯编排、零 LLM、Agent 无关；本引擎只装配不推进循环（步进由折叠引擎驱动）。
/// </summary>
public interface INodeAssemblyEngine
{
    /// <summary>
    /// 同步装配：注册表解析（AlwaysOn + 点名）→ 合并动态节点 → 三段排布 → 产出一致多面结果。
    /// </summary>
    /// <param name="registry">组件注册表（唯一声明来源）。</param>
    /// <param name="request">装配请求（作用域 + 点名组件 + 步骤键）。</param>
    /// <param name="dynamicNodes">动态上下文节点（历史 / task 动态内容 / 上步产出；可选）。</param>
    /// <param name="policy">槽位策略（null = 通用稳定前缀策略 <see cref="CacheStableSlotPolicy"/>）。</param>
    /// <param name="maxPerBand">每段上限（&gt;0 生效，超出截断；null = 不限）。</param>
    AssemblyOutcome Assemble(
        INodeComponentRegistry registry,
        AssemblyRequest request,
        IEnumerable<NodeItem>? dynamicNodes = null,
        INodeSlotPolicy? policy = null,
        int? maxPerBand = null);

    /// <summary>
    /// 异步装配（带动态源）：先从 <see cref="INodeSource{TState,TVertex}"/> 聚合收集动态节点，再交 <see cref="Assemble(INodeComponentRegistry,AssemblyRequest,IEnumerable{NodeItem},INodeSlotPolicy,System.Nullable{int})"/>。
    /// </summary>
    /// <typeparam name="TState">折叠状态点类型。</typeparam>
    /// <typeparam name="TVertex">每步算子结论类型。</typeparam>
    /// <param name="registry">组件注册表。</param>
    /// <param name="request">装配请求。</param>
    /// <param name="dynamicSources">动态源集（可选；null = 无动态上下文）。</param>
    /// <param name="state">当前状态点（动态源收集入参）。</param>
    /// <param name="signal">当前驱动信号（动态源收集入参）。</param>
    /// <param name="policy">槽位策略。</param>
    /// <param name="maxPerBand">每段上限。</param>
    /// <param name="ct">取消令牌。</param>
    Task<AssemblyOutcome> AssembleAsync<TState, TVertex>(
        INodeComponentRegistry registry,
        AssemblyRequest request,
        IEnumerable<INodeSource<TState, TVertex>>? dynamicSources,
        TState state,
        FoldSignal<TVertex> signal,
        INodeSlotPolicy? policy = null,
        int? maxPerBand = null,
        CancellationToken ct = default);
}
