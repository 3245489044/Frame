using System;
using System.Collections.Generic;
using System.Linq;
using ECS.Core;

namespace ECS.Core.Lifecycle
{
    /// <summary>
    /// Entity 生命周期订阅契约：任何持 Entity 引用的组件实现此接口并注册到
    /// <see cref="EntityLifecycleBus"/>，即自动接入生命周期治理——销毁联动由总线广播驱动，
    /// 不再手改 DestroyScopeEntities 的逐项回收清单（方案：注册-广播-自愈）。
    /// </summary>
    public interface IEntityLifecycleSubscriber
    {
        /// <summary>单实体失效（EntityManager.EntityDestroying 强类型化转发）。</summary>
        void OnEntityInvalidated(Entity entity);

        /// <summary>
        /// scope 整体失效（作用域销毁的唯一广播：实体+控件+委托+索引批量自清理）。
        /// scopeEntities = 该 scope 的实体清单（销毁前快照），订阅者无需自行查询 ScopeComponent。
        /// </summary>
        void OnScopeInvalidated(string scopeName, IReadOnlyList<Entity> scopeEntities);
    }

    /// <summary>
    /// Entity 生命周期总线（Kernel 层统一治理设施）：
    /// 单实体失效经 EntityManager.EntityDestroying 转发；scope 级失效经 <see cref="InvalidateScope"/> 广播。
    /// 销毁入口（RenderStageRunner.DestroyScopeEntities）只调 <see cref="InvalidateScope"/>，各订阅者自清理。
    /// 装配：PipelineCompositionFactory 创建并注册到 AppServiceProvider（订阅者懒解析）；订阅者构造时经 Provider 注册。
    /// </summary>
    public sealed class EntityLifecycleBus : IDisposable
    {
        private readonly EntityManager _entityManager;
        private readonly List<IEntityLifecycleSubscriber> _subscribers = new();
        private bool _disposed;

        public EntityLifecycleBus(EntityManager entityManager)
        {
            _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
            _entityManager.EntityDestroying += OnEntityDestroying;
        }

        /// <summary>注册订阅者（幂等）。任何新引用持有点 = 实现接口 + 本方法注册，不改销毁入口。</summary>
        public void Register(IEntityLifecycleSubscriber subscriber)
        {
            if (subscriber == null) throw new ArgumentNullException(nameof(subscriber));
            if (!_subscribers.Contains(subscriber))
                _subscribers.Add(subscriber);
        }

        public void Unregister(IEntityLifecycleSubscriber subscriber)
        {
            if (subscriber == null) return;
            _subscribers.Remove(subscriber);
        }

        /// <summary>
        /// scope 级失效广播（作用域销毁的唯一通知源，由 DestroyScopeEntities 调用）。
        /// 广播发生时 scope 实体仍有效（销毁前），scopeEntities 为销毁前快照，
        /// 订阅者据此清理自身持有的旧控件/旧引用/旧索引。
        /// </summary>
        public void InvalidateScope(string scopeName, IReadOnlyList<Entity> scopeEntities)
        {
            if (string.IsNullOrEmpty(scopeName)) return;

            foreach (var s in _subscribers.ToArray())
            {
                try
                {
                    s.OnScopeInvalidated(scopeName, scopeEntities);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"EntityLifecycleBus.InvalidateScope: subscriber {s.GetType().Name} 清理失败: {ex.Message}");
                }
            }
        }

        private void OnEntityDestroying(Entity entity)
        {
            foreach (var s in _subscribers.ToArray())
            {
                try
                {
                    s.OnEntityInvalidated(entity);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"EntityLifecycleBus.OnEntityDestroying: subscriber {s.GetType().Name} 清理失败: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _entityManager.EntityDestroying -= OnEntityDestroying;
            _subscribers.Clear();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
