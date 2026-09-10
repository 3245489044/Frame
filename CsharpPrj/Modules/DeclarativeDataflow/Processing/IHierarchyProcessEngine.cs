using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.DeclarativeDataflow.Model;

namespace Modules.DeclarativeDataflow.Processing;

/// <summary>多层处理观测端口（可选）：进入 / 退出 / 护栏触发。用于诊断与测试断言，不影响处理结论。</summary>
public interface ILevelProcessObserver
{
    /// <summary>进入某节点处理前。</summary>
    void OnEnter(LevelRef node, LevelPath path);

    /// <summary>某节点处理完成后。</summary>
    void OnExit(LevelRef node, LevelPath path, LevelProcessResult result);

    /// <summary>护栏触发（已访问去重由引擎静默处理，不触发本回调）。</summary>
    void OnGuardTriggered(LevelRef node, string reason);
}

/// <summary>
/// 多层处理引擎端口 —— 层次外置框架的唯一执行入口：给定根节点与计划，按 schema 递归处理整棵层次。
/// </summary>
public interface IHierarchyProcessEngine
{
    /// <summary>处理以 <paramref name="root"/> 为根的层次；返回根节点结论（子层结论已按方向聚合）。</summary>
    Task<LevelProcessResult> ProcessAsync(LevelRef root, HierarchyProcessPlan? plan = null, CancellationToken ct = default);
}

/// <summary>层处理上下文默认实现（框架内部构造并注入处理器；只读）。</summary>
internal sealed class LevelProcessContext : ILevelProcessContext
{
    public LevelProcessContext(
        HierarchyProcessPlan plan,
        LevelPath path,
        LevelRef node,
        LevelProcessResult? downstream,
        IReadOnlyList<LevelProcessResult> childResults)
    {
        Plan = plan;
        Path = path;
        Node = node;
        Downstream = downstream;
        ChildResults = childResults;
    }

    public HierarchyProcessPlan Plan { get; }

    public LevelPath Path { get; }

    public LevelRef Node { get; }

    public int Depth => Path.Depth;

    public LevelProcessResult? Downstream { get; }

    public IReadOnlyList<LevelProcessResult> ChildResults { get; }
}
