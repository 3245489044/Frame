using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ECS.Core;

namespace ECS.Core.Lifecycle
{
    /// <summary>
    /// Entity 生命周期桥（Kernel 层"按名解析当前代"设施）：
    /// 维护 控件名 → 当前代 Entity 索引（ControlPool.Register 单点联动写入），
    /// <see cref="TryResolve"/> 带 IsEntityValid 校验（旧代/已销毁 → 不命中），
    /// 实体销毁 / scope 失效经 <see cref="EntityLifecycleBus"/> 广播自清理。
    /// 所有"名称→实体"查找统一走本桥，杜绝旧代 Entity 值泄漏给调用方。
    /// </summary>
    public sealed class EntityLifetimeBridge : IEntityLifecycleSubscriber
    {
        private readonly EntityManager _entityManager;
        private readonly ConcurrentDictionary<string, Entity> _currentByName =
            new(StringComparer.OrdinalIgnoreCase);

        public EntityLifetimeBridge(EntityManager entityManager)
        {
            _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        }

        /// <summary>登记名称→当前代绑定（ControlPool.Register 单点联动，幂等覆盖）。</summary>
        public void RegisterBinding(string controlName, Entity entity)
        {
            if (string.IsNullOrEmpty(controlName) || !entity.IsValid)
                return;
            _currentByName[controlName] = entity;
        }

        /// <summary>按名解析当前代：命中且实体当前有效才返回（旧代/已销毁 → false，无 Entity 值泄漏）。</summary>
        public bool TryResolve(string controlName, out Entity entity)
        {
            if (!string.IsNullOrEmpty(controlName)
                && _currentByName.TryGetValue(controlName, out entity)
                && entity.IsValid
                && _entityManager.IsEntityValid(entity))
            {
                return true;
            }
            entity = default;
            return false;
        }

        /// <summary>当前登记的名称→实体快照（诊断/遍历用）。</summary>
        public IEnumerable<KeyValuePair<string, Entity>> Snapshot() => _currentByName;

        // ── IEntityLifecycleSubscriber ──

        /// <summary>单实体失效：移除该实体对应的名称条目（旧代不再可解析）。</summary>
        public void OnEntityInvalidated(Entity entity)
        {
            foreach (var (name, e) in _currentByName.ToArray())
            {
                if (e == entity)
                    _currentByName.TryRemove(name, out _);
            }
        }

        /// <summary>scope 整体失效：移除本 scope 实体对应的名称条目（按销毁前快照批量清理）。</summary>
        public void OnScopeInvalidated(string scopeName, IReadOnlyList<Entity> scopeEntities)
        {
            if (scopeEntities == null || scopeEntities.Count == 0)
                return;

            var invalid = new HashSet<Entity>(scopeEntities);
            foreach (var (name, e) in _currentByName.ToArray())
            {
                if (invalid.Contains(e))
                    _currentByName.TryRemove(name, out _);
            }
        }
    }
}
