#nullable enable

using System;
using ECS.Core;
using TConfig.ECS;

namespace FrontEndDriven.Shared.Framework;

/// <summary>
/// 标记驱动 ECS 系统基类（通用框架）：扫描带 <typeparamref name="TMarker"/> 的实体，
/// 逐个执行 <see cref="Process"/> 后移除标记。
/// 收敛 StyleSystem 等「Pending*Component → 处理 → 移除标记」的同构系统循环；
/// 处理过程产出（写 Applied/Computed 组件）由派生类在 <see cref="Process"/> 内完成。
/// 注：带自定义过滤（跳过类）/多趟收敛的系统（如 LayoutSystem / EcsCapabilityApplier）不适合继承本基类。
/// </summary>
/// <typeparam name="TMarker">标记组件（Pending 语义）。</typeparam>
public abstract class PendingEcsSystem<TMarker>
    where TMarker : struct, IComponent
{
    protected readonly EntityManager EntityManager;

    protected PendingEcsSystem(EntityManager entityManager)
    {
        EntityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
    }

    /// <summary>扫描全部带标记实体：逐个 <see cref="Process"/> 后移除标记。</summary>
    public void Update()
    {
        foreach (var entity in EntityManager.GetEntities<TMarker>())
        {
            Process(entity);
            EntityManager.RemoveComponent<TMarker>(entity);
        }
    }

    protected abstract void Process(Entity entity);
}
