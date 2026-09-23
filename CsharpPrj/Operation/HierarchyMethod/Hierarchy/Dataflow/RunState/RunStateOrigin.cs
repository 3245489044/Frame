namespace Operation.HierarchyMethod.Hierarchy.Dataflow.RunState;

/// <summary>
/// 运行态来源（**优先级即合并序**）—— 表达"这条运行态事实从哪来"，使"在途 / 已落库 / 派生聚合"三态
/// 在同一模型中共存且可辨别（消费方据此决定显示与置信度，不再猜测）。
/// <para>优先级（低 → 高）：<see cref="Derived"/> &lt; <see cref="Snapshot"/> &lt; <see cref="Live"/>；
/// 合并时高优先级源的非空字段覆盖低优先级源（见 <see cref="LevelRunState.Merge"/>）。</para>
/// </summary>
public enum RunStateOrigin
{
    /// <summary>派生聚合（如容器层由子层状态汇总；无自身在途事实）。</summary>
    Derived = 0,

    /// <summary>持久快照（已落库的运行态；跨重启可见，粒度=轮末）。</summary>
    Snapshot = 1,

    /// <summary>在途实时登记（进程内在途事实；粒度=派发即见，重启即空）。</summary>
    Live = 2,
}
