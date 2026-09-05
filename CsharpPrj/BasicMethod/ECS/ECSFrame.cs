using TConfigContextFrame = TConfig.ContextFrame.Abstractions;
using ECS.Core;
using System.Runtime.CompilerServices;
#nullable enable

#region ECSRegion

#region ECS.Core
namespace ECS.Core
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using TConfig.ContextFrame.Abstractions;

    using Entity = TConfig.ECS.Entity;
    using IComponent = TConfig.ECS.IComponent;

    // Vec2 / Vec3 已迁移至 TConfig.GameMath（见 TConfig/GameMath/Vec2.cs、Vec3.cs），此处仅保留 ECS 框架核心。

    internal ref struct ReadLockScope
    {
        private readonly ReaderWriterLockSlim _rwLock;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadLockScope(ReaderWriterLockSlim rwLock)
        {
            _rwLock = rwLock ?? throw new ArgumentNullException(nameof(rwLock));
            _rwLock.EnterReadLock();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (_rwLock != null)
            {
                try
                {
                    if (_rwLock.IsReadLockHeld)
                        _rwLock.ExitReadLock();
                }
                catch (ObjectDisposedException) { }
            }
        }
    }

    internal ref struct WriteLockScope
    {
        private readonly ReaderWriterLockSlim _rwLock;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public WriteLockScope(ReaderWriterLockSlim rwLock)
        {
            _rwLock = rwLock ?? throw new ArgumentNullException(nameof(rwLock));
            _rwLock.EnterWriteLock();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (_rwLock != null)
            {
                try
                {
                    if (_rwLock.IsWriteLockHeld)
                        _rwLock.ExitWriteLock();
                }
                catch (ObjectDisposedException) { }
            }
        }
    }

    public sealed class EntityManager : IDisposable
    {
        private readonly ArchetypeManager _archetypeManager;
        private bool _disposed;

        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

        public EntityManager(
            TConfig.ContextFrame.Abstractions.IECSStorage ecsStorage,
            TConfig.ContextFrame.Abstractions.IStorageRegistry storageRegistry)
        {
            var componentTypeId = new ComponentTypeId(storageRegistry);
            _archetypeManager = new ArchetypeManager(ecsStorage, componentTypeId);
        }


        public event Action<Entity>? EntityCreated;

        public event Action<Entity>? EntityDestroying;

        public event Action<Entity>? EntityDestroyed;

        public event Action<Entity, Type>? ComponentAdded, ComponentRemoved;
        public event Action<Entity, Type, object?, object>? ComponentUpdated;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EntityManager));
        }

        private void WithWriteLock(Action action)
        {
            ThrowIfDisposed();
            using (new WriteLockScope(_lock))
                action();
        }

        private T WithWriteLock<T>(Func<T> func)
        {
            ThrowIfDisposed();
            using (new WriteLockScope(_lock))
                return func();
        }

        private T WithReadLock<T>(Func<T> func)
        {
            ThrowIfDisposed();
            using (new ReadLockScope(_lock))
                return func();
        }

        public Entity CreateEntity()
        {
            var entity = WithWriteLock(() => _archetypeManager.CreateEntity());
            EntityCreated?.Invoke(entity);
            return entity;
        }

        public void DestroyEntity(Entity entity)
        {
            WithWriteLock(() =>
            {
                if (!_archetypeManager.IsEntityValid(entity)) return;

                EntityDestroying?.Invoke(entity);

                _archetypeManager.DestroyEntity(entity);
                EntityDestroyed?.Invoke(entity);
            });
        }

        public bool IsEntityValid(Entity entity)
            => WithReadLock(() => _archetypeManager.IsEntityValid(entity));

        public int GetEntityCount()
            => WithReadLock(() => _archetypeManager.GetEntityCount());

        public void AddComponent<T>(Entity entity, T component) where T : struct, IComponent
        {
            WithWriteLock(() =>
            {
                if (!_archetypeManager.IsEntityValid(entity))
                    throw new ArgumentException($"Invalid entity: {entity}", nameof(entity));
                _archetypeManager.AddComponent(entity, component);
                ComponentAdded?.Invoke(entity, typeof(T));
            });
        }

        public void RemoveComponent<T>(Entity entity) where T : struct, IComponent
        {
            WithWriteLock(() =>
            {
                if (!_archetypeManager.IsEntityValid(entity)) return;
                if (_archetypeManager.HasComponent<T>(entity))
                {
                    _archetypeManager.RemoveComponent<T>(entity);
                    ComponentRemoved?.Invoke(entity, typeof(T));
                }
            });
        }

        public T GetComponent<T>(Entity entity) where T : struct, IComponent
        {
            return WithReadLock(() =>
            {
                if (!_archetypeManager.IsEntityValid(entity))
                    throw new ArgumentException($"Invalid entity: {entity}", nameof(entity));
                if (!_archetypeManager.HasComponent<T>(entity))
                    throw new InvalidOperationException($"Entity {entity} does not have component {typeof(T).Name}");
                return _archetypeManager.GetComponent<T>(entity);
            });
        }

        public void SetComponent<T>(Entity entity, T component) where T : struct, IComponent
        {
            WithWriteLock(() =>
            {
                if (!_archetypeManager.IsEntityValid(entity))
                    throw new ArgumentException($"Invalid entity: {entity}", nameof(entity));
                if (!_archetypeManager.HasComponent<T>(entity))
                    throw new InvalidOperationException($"Entity {entity} does not have component {typeof(T).Name}");
                T old = _archetypeManager.GetComponent<T>(entity);
                _archetypeManager.SetComponent(entity, component);
                ComponentUpdated?.Invoke(entity, typeof(T), old, component);
            });
        }

        /// <summary>
        /// 原子 AddOrSet（幂等）：检查与写入在同一写锁内完成，无 check-then-act 竞态。
        /// 修复「并发写同一实体（如事件输出回写 + 物理输入回灌同时对同一控件 SetValue）→ 双 AddComponent
        /// 同时通过 HasComponent=false → 第二个 AddComponent 抛 'already has component'，整次事件分发失败」。
        /// 语义：组件已存在则更新（SetComponent），否则添加（AddComponent）；线程安全。
        /// </summary>
        public void AddOrSetComponent<T>(Entity entity, T component) where T : struct, IComponent
        {
            WithWriteLock(() =>
            {
                if (!_archetypeManager.IsEntityValid(entity))
                    throw new ArgumentException($"Invalid entity: {entity}", nameof(entity));
                if (_archetypeManager.HasComponent<T>(entity))
                {
                    T old = _archetypeManager.GetComponent<T>(entity);
                    _archetypeManager.SetComponent(entity, component);
                    ComponentUpdated?.Invoke(entity, typeof(T), old, component);
                }
                else
                {
                    _archetypeManager.AddComponent(entity, component);
                    ComponentAdded?.Invoke(entity, typeof(T));
                }
            });
        }

        public bool TryGetComponent<T>(Entity entity, out T component) where T : struct, IComponent
        {
            ThrowIfDisposed();
            using (new ReadLockScope(_lock))
            {
                return _archetypeManager.TryGetComponent(entity, out component);
            }
        }

        public bool HasComponent<T>(Entity entity) where T : struct, IComponent
        {
            return WithReadLock(() => _archetypeManager.IsEntityValid(entity) && _archetypeManager.HasComponent<T>(entity));
        }

        public bool HasComponent(Entity entity, Type componentType)
        {
            if (componentType == null)
                throw new ArgumentNullException(nameof(componentType));
            return WithReadLock(() => _archetypeManager.HasComponent(entity, componentType));
        }

        public IEnumerable<Entity> GetEntities(params Type[] componentTypes)
        {
            if (componentTypes.Length == 0) return Enumerable.Empty<Entity>();
            return WithReadLock(() => _archetypeManager.QueryEntities(componentTypes).ToList());
        }

        public IEnumerable<Entity> GetEntities<T>() where T : struct, IComponent
            => GetEntities(typeof(T));

        public IEnumerable<Entity> GetEntities<T1, T2>() where T1 : struct, IComponent where T2 : struct, IComponent
            => GetEntities(typeof(T1), typeof(T2));

        public IEnumerable<Entity> GetAllEntities()
        {
            return WithReadLock(() => _archetypeManager.GetAllEntities().ToList());
        }

        public void Clear()
        {
            var allEntities = WithWriteLock(() =>
            {
                var list = _archetypeManager.GetAllEntities().ToList();
                _archetypeManager.Clear();
                return list;
            });
            foreach (var e in allEntities)
            {
                EntityDestroyed?.Invoke(e);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Clear();
            _lock.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }




    public interface ISystem
    {
        void Initialize(EntityManager entityManager);
        void Update(float deltaTime);
        bool Enabled { get; set; }
    }

    public abstract class SystemBase : ISystem, IDisposable
    {
        protected EntityManager EntityManager { get; private set; } = null!;
        public bool Enabled { get; set; } = true;
        public virtual void Initialize(EntityManager em) => EntityManager = em ?? throw new ArgumentNullException(nameof(em));
        public abstract void Update(float deltaTime);
        public virtual void Dispose() => OnDispose();
        protected virtual void OnDispose() { }
    }

    public sealed class SystemManager : IDisposable
    {
        private readonly EntityManager _em;
        private readonly List<ISystem> _systems = new();
        private readonly Dictionary<Type, ISystem> _systemByType = new();
        private bool _disposed;

        public SystemManager(EntityManager em) => _em = em ?? throw new ArgumentNullException(nameof(em));
        public T RegisterSystem<T>() where T : ISystem, new() => RegisterSystem(new T());
        public T RegisterSystem<T>(T system) where T : ISystem
        {
            ThrowIfDisposed();
            system.Initialize(_em);
            _systems.Add(system);
            _systemByType[typeof(T)] = system;
            return system;
        }
        public T GetSystem<T>() where T : ISystem => TryGetSystem<T>(out var s) ? s : throw new KeyNotFoundException($"System {typeof(T).Name} not found");
        public bool TryGetSystem<T>([NotNullWhen(true)] out T? s) where T : ISystem
        {
            if (_systemByType.TryGetValue(typeof(T), out var sys)) { s = (T)sys; return true; }
            s = default; return false;
        }
        public void UpdateAll(float dt) { ThrowIfDisposed(); foreach (var sys in _systems.Where(s => s.Enabled)) sys.Update(dt); }
        public void Dispose()
        {
            if (_disposed) return;
            foreach (var sys in _systems.OfType<IDisposable>()) sys.Dispose();
            _systems.Clear(); _systemByType.Clear(); _disposed = true; GC.SuppressFinalize(this);
        }
        private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(SystemManager)); }
    }

    public sealed class EcsWorld : IDisposable
    {
        public EntityManager EntityManager { get; }
        public SystemManager SystemManager { get; }
        private readonly ComponentTypeId _componentTypeId;
        private bool _disposed;

        public static Func<TConfig.ContextFrame.Abstractions.IECSStorage>? StorageFactory { get; set; }
        public static Func<TConfig.ContextFrame.Abstractions.IStorageRegistry>? RegistryFactory { get; set; }

        public EcsWorld()
            : this(
                StorageFactory?.Invoke() ?? throw new InvalidOperationException(
                    "EcsWorld.StorageFactory 未设置。请先设置 StorageFactory 和 RegistryFactory，或使用带参数的构造函数。"),
                RegistryFactory?.Invoke() ?? throw new InvalidOperationException(
                    "EcsWorld.RegistryFactory 未设置。请先设置 RegistryFactory 和 StorageFactory，或使用带参数的构造函数。"))
        {
        }

        public EcsWorld(
            TConfig.ContextFrame.Abstractions.IECSStorage ecsStorage,
            TConfig.ContextFrame.Abstractions.IStorageRegistry storageRegistry)
        {
            _componentTypeId = new ComponentTypeId(storageRegistry);
            EntityManager = new EntityManager(ecsStorage, storageRegistry);
            SystemManager = new SystemManager(EntityManager);
        }
        public void Update(float dt) { ThrowIfDisposed(); SystemManager.UpdateAll(dt); }
        public EntityQueryBuilder.Builder CreateQuery() => new EntityQueryBuilder.Builder(_componentTypeId);

        public void Dispose()
        {
            if (_disposed) return;
            SystemManager.Dispose(); EntityManager.Dispose(); _disposed = true; GC.SuppressFinalize(this);
        }
        private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(EcsWorld)); }
    }


}
#endregion

#endregion
