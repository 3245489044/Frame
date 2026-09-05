
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using ECS.Core;
using ECS.Bridge;
using TConfig.ECS.Components;
using UniversalMiddleware.Abstractions;
using FrontEndDriven.Diagnostics;

namespace FrontEndDriven.ECS.Services;

public class EntityTreeService : IEntityTreeService
{
    private readonly EntityManager _entityManager;
    private readonly ControlPool _controlPool;

    public EntityTreeService(EntityManager entityManager, ControlPool controlPool)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _controlPool = controlPool ?? throw new ArgumentNullException(nameof(controlPool));
    }

    /// <summary>
    /// 名称→实体解析（E2/E3 修复）：改走 ControlPool.name_to_entity 权威索引（IsUnique 语义 + EnsureCurrent 当前代校验），
    /// 不再维护平行内存缓存（原 _entityNameCache 忽略 root 作用域、销毁后残留旧代 Entity 值）。
    /// 未命中返回 <see cref="Entity.Invalid"/>（不再抛 KeyNotFoundException，与 IEntityTreeService 接口 null 语义统一）。
    /// </summary>
    public Entity FindEntityByName(Entity rootEntity, string name)
    {
        if (!rootEntity.IsValid)
            throw new InvalidOperationException(
                $"无法查找 Entity '{name}'：根 Entity 无效。请确保根 Entity 已正确初始化。");

        if (string.IsNullOrEmpty(name))
            throw new ArgumentNullException(nameof(name), "要查找的 Entity 名称不能为空。");

        var entity = _controlPool.GetEntity(name);
        if (entity.IsValid)
        {
            FrontEndDrivenLog.Log("EntityTreeService_FindEntityByName_found",
                new { entity = entity.ToString(), name });
        }
        return entity;
    }

    private T? FindByName<T>(Entity rootEntity, string name) where T : class
    {
        if (string.IsNullOrEmpty(name))
            return null;

        if (!rootEntity.IsValid)
            return null;

        var control = _controlPool.GetControl(name);
        if (control is T typed)
            return typed;

        var entity = FindEntityByName(rootEntity, name);
        if (!entity.IsValid)
            return null;

        control = _controlPool.GetControl(entity);
        if (control is T typed2)
            return typed2;

        return null;
    }

    /// <summary>按名查控件（未命中返回 null，不再抛 KeyNotFoundException——E3 异常契约统一）。</summary>
    public Control? FindControlByName(Entity rootEntity, string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        return FindByName<Control>(rootEntity, name);
    }

    /// <summary>按名查容器（未命中返回 null，不再抛 KeyNotFoundException——E3 异常契约统一）。</summary>
    public IConfigContainer? FindContainerByName(Entity rootEntity, string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        return FindByName<IConfigContainer>(rootEntity, name);
    }

    Control? IEntityTreeService.FindControlByName(Entity rootEntity, string name)
        => FindControlByName(rootEntity, name);

    IConfigContainer? IEntityTreeService.FindContainerByName(Entity rootEntity, string name)
        => FindContainerByName(rootEntity, name);

    IEnumerable<IConfigContainer> IEntityTreeService.FindAllContainers(Entity rootEntity)
    {
        if (!rootEntity.IsValid)
            yield break;

        foreach (var entity in _entityManager.GetEntities<ControlTypeComponent>())
        {
            if (_entityManager.TryGetComponent<ControlTypeComponent>(entity, out var typeComp) &&
                string.Equals(typeComp.Type, "container", StringComparison.OrdinalIgnoreCase))
            {
                var control = _controlPool.GetControl(entity);
                if (control is IConfigContainer container)
                    yield return container;
            }
        }
    }
}
