using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.HierarchyMethod.Reference;

/// <summary>
/// 统一执行标识符管理端口（全局层级状态单一承载，StateRootSingleSource / TH-12）。
/// 读/写/批量/生命周期四类操作；写边统一委托 <see cref="UniversalWorkflowFramework.StorageFramework.Abstractions.IResourceReferenceStore"/>（SingleImplementation），
/// 本服务只编排。装配可空：未装配 store 时读返回 null/默认、写 no-op，不阻断既有 SchedulingMode / TaskPool 路径（向后兼容，零回归）。
/// 权威设计：执行标识符框架设计 §8。
/// </summary>
public interface IExecutionFlagService
{
    /// <summary>读自身声明标志；未装配/未声明 → null（Auto 语义由 <see cref="ResolveFlagAsync"/> 沿边继承）。</summary>
    ExecutionFlag? GetFlag(EntityRef entity);

    /// <summary>沿引用边解析生效标志：自身非 Auto/Std → 自身；否则沿 history_owns_task / cross_db_link 入边向上找最近非 Auto；全 Auto → Deterministic（零 LLM）。</summary>
    Task<ExecutionFlag> ResolveFlagAsync(EntityRef entity, CancellationToken ct = default);

    /// <summary>单实体设标志（写 exec.flag + lifecycle=flagged；反应式：向后台 <see cref="FlagConductor"/> 发信号触发 解析→路由→运行→回切）。</summary>
    Task SetFlagAsync(EntityRef entity, ExecutionFlag flag, CancellationToken ct = default);

    /// <summary>层级/批量设标志（task 选 nodes / history 切 tasks；沿引用边向下游批量写）。</summary>
    Task SetFlagScopeAsync(FlagScope scope, ExecutionFlag flag, CancellationToken ct = default);

    /// <summary>生命周期：启动运行（lifecycle=running）。</summary>
    Task<FlagLifecycleState> StartAsync(EntityRef entity, CancellationToken ct = default);

    /// <summary>生命周期：标记完成（lifecycle=completed）。</summary>
    Task<FlagLifecycleState> CompleteAsync(EntityRef entity, CancellationToken ct = default);

    /// <summary>生命周期：回切标准标识符（exec.flag=Std、lifecycle=idle）。</summary>
    Task ResetAsync(EntityRef entity, CancellationToken ct = default);

    /// <summary>全局被标识符管理的实体列表（读 exec.flag 非空的图资源）。</summary>
    Task<IReadOnlyList<EntityRef>> ListFlaggedAsync(CancellationToken ct = default);
}
