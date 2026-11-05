using System;
using System.Collections.Generic;
using Modules.DeclarativeDataflow.Model;

namespace Modules.DeclarativeDataflow.Processing;

/// <summary>
/// 层处理器 —— "该层怎么被处理"的代数。每层（经 <see cref="HierarchyLevelSpec.ProcessId"/>）绑定一个实现；
/// 框架本体不含任何处理逻辑，LLM / 规则 / 纯确定性都只是本端口的一种实现（零 LLM）。
/// </summary>
public interface ILevelProcessor
{
    /// <summary>处理器 Id（与层声明的 ProcessId 匹配）。</summary>
    string ProcessId { get; }

    /// <summary>处理单个层节点；返回该节点结论（可为聚合子层结论后的结果）。</summary>
    Task<LevelProcessResult> ProcessAsync(LevelRef node, ILevelProcessContext context, CancellationToken ct = default);
}

/// <summary>
/// 层处理上下文（只读）：本节点 + 根→当前路径 + 该轮计划 + 方向性输入
/// （<see cref="HierarchyTraversalOrder.TopDown"/> 用 <see cref="Downstream"/>；
/// <see cref="HierarchyTraversalOrder.BottomUp"/> 用 <see cref="ChildResults"/>）。
/// </summary>
public interface ILevelProcessContext
{
    /// <summary>本轮处理计划（预算 / 方向 / 覆盖处理器）。</summary>
    HierarchyProcessPlan Plan { get; }

    /// <summary>根 → 当前节点的路径（含两端）。</summary>
    LevelPath Path { get; }

    /// <summary>当前节点。</summary>
    LevelRef Node { get; }

    /// <summary>当前深度（根 = 1）。</summary>
    int Depth { get; }

    /// <summary>
    /// TopDown 方向：父层已产出的结论（根为 null），供向下传递上下文。
    /// <para>
    /// 该结论是父层**节点局部**的结论，不含子层合并结果：子层可能因预算被截断而使整次处理
    /// 最终 <c>Success=false</c>（父层结论在 <see cref="LevelProcessResult.Merge"/> 时被回滚），
    /// 故处理器不得把 <see cref="Downstream"/> 当作"整棵子树已成功"的证据。
    /// </para>
    /// </summary>
    LevelProcessResult? Downstream { get; }

    /// <summary>BottomUp 方向：子层结论（按遍历序；叶层为空集），供向上聚合。</summary>
    IReadOnlyList<LevelProcessResult> ChildResults { get; }
}

/// <summary>层处理器注册表端口（按 Id 解析；未命中返回 null → 引擎按容器层透传）。</summary>
public interface ILevelProcessorRegistry
{
    /// <summary>解析处理器；Id 为空 / 未命中返回 null。</summary>
    ILevelProcessor? Resolve(string? processId);
}

/// <summary>层处理器注册表（进程内字典；装配期一次性注册，运行期只读）。</summary>
public sealed class LevelProcessorRegistry : ILevelProcessorRegistry
{
    private readonly Dictionary<string, ILevelProcessor> _byId;

    /// <summary>空注册表（所有层透传，用于纯结构遍历 / 单测）。</summary>
    public static LevelProcessorRegistry Empty { get; } = new(Array.Empty<ILevelProcessor>());

    public LevelProcessorRegistry(IEnumerable<ILevelProcessor> processors)
    {
        ArgumentNullException.ThrowIfNull(processors);
        _byId = new Dictionary<string, ILevelProcessor>(StringComparer.Ordinal);
        foreach (var processor in processors)
        {
            if (processor is null) continue;
            if (string.IsNullOrWhiteSpace(processor.ProcessId))
                throw new ArgumentException($"层处理器 {processor.GetType().Name} 的 ProcessId 不可为空。", nameof(processors));
            if (!_byId.TryAdd(processor.ProcessId, processor))
                throw new ArgumentException($"层处理器 Id 重复：'{processor.ProcessId}'。", nameof(processors));
        }
    }

    public LevelProcessorRegistry(params ILevelProcessor[] processors)
        : this((IEnumerable<ILevelProcessor>)processors)
    {
    }

    /// <summary>已注册处理器数。</summary>
    public int Count => _byId.Count;

    public ILevelProcessor? Resolve(string? processId)
        => !string.IsNullOrWhiteSpace(processId) && _byId.TryGetValue(processId, out var processor)
            ? processor
            : null;
}
