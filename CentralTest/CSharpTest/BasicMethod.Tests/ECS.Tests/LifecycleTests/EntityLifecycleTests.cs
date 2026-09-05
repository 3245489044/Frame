
using ECS.Core;
using ECS.Core.Lifecycle;
using ECS.Tests.Mocks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TConfig.ContextFrame.Abstractions;
using TConfig.ECS;

namespace ECS.Tests.LifecycleTests;

[TestClass]
public class EntityLifecycleBusTests
{
    private IECSStorage _storage = null!;
    private IStorageRegistry _registry = null!;
    private EntityManager _entityManager = null!;
    private EntityLifecycleBus _bus = null!;

    [TestInitialize]
    public void Setup()
    {
        _storage = new MockECSStorage();
        _registry = new MockStorageRegistry();
        _entityManager = new EntityManager(_storage, _registry);
        _bus = new EntityLifecycleBus(_entityManager);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _bus.Dispose();
        _entityManager.Dispose();
    }

    [TestMethod]
    public void InvalidateScope_ShouldNotifySubscribers_WithScopeAndEntities()
    {
        var collector = new RecordingSubscriber();
        _bus.Register(collector);
        var e1 = _entityManager.CreateEntity();
        var e2 = _entityManager.CreateEntity();

        _bus.InvalidateScope("Side", new[] { e1, e2 });

        Assert.AreEqual("Side", collector.ScopeName);
        CollectionAssert.AreEqual(new[] { e1, e2 }, collector.ScopeEntities);
    }

    [TestMethod]
    public void DestroyEntity_ShouldNotifySubscribers_OnEntityInvalidated()
    {
        var collector = new RecordingSubscriber();
        _bus.Register(collector);
        var entity = _entityManager.CreateEntity();

        _entityManager.DestroyEntity(entity);

        Assert.AreEqual(entity, collector.InvalidatedEntity);
    }

    [TestMethod]
    public void InvalidateScope_SubscriberException_ShouldNotAbortOtherSubscribers()
    {
        _bus.Register(new ThrowingSubscriber());
        var collector = new RecordingSubscriber();
        _bus.Register(collector);
        var e1 = _entityManager.CreateEntity();

        _bus.InvalidateScope("Side", new[] { e1 });

        Assert.AreEqual("Side", collector.ScopeName, "前一订阅者抛异常不应阻断后续订阅者");
    }

    private sealed class RecordingSubscriber : IEntityLifecycleSubscriber
    {
        public Entity InvalidatedEntity { get; private set; }
        public string? ScopeName { get; private set; }
        public IReadOnlyList<Entity> ScopeEntities { get; private set; } = Array.Empty<Entity>();

        public void OnEntityInvalidated(Entity entity) => InvalidatedEntity = entity;

        public void OnScopeInvalidated(string scopeName, IReadOnlyList<Entity> scopeEntities)
        {
            ScopeName = scopeName;
            ScopeEntities = scopeEntities;
        }
    }

    private sealed class ThrowingSubscriber : IEntityLifecycleSubscriber
    {
        public void OnEntityInvalidated(Entity entity) { }

        public void OnScopeInvalidated(string scopeName, IReadOnlyList<Entity> scopeEntities)
        {
            throw new InvalidOperationException("模拟订阅者清理失败");
        }
    }
}

[TestClass]
public class EntityLifetimeBridgeTests
{
    private IECSStorage _storage = null!;
    private IStorageRegistry _registry = null!;
    private EntityManager _entityManager = null!;
    private EntityLifecycleBus _bus = null!;
    private EntityLifetimeBridge _bridge = null!;

    [TestInitialize]
    public void Setup()
    {
        _storage = new MockECSStorage();
        _registry = new MockStorageRegistry();
        _entityManager = new EntityManager(_storage, _registry);
        _bus = new EntityLifecycleBus(_entityManager);
        _bridge = new EntityLifetimeBridge(_entityManager);
        _bus.Register(_bridge);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _bus.Dispose();
        _entityManager.Dispose();
    }

    [TestMethod]
    public void RegisterBinding_TryResolve_ShouldReturnCurrentEntity()
    {
        var entity = _entityManager.CreateEntity();
        _bridge.RegisterBinding("titleLabel", entity);

        Assert.IsTrue(_bridge.TryResolve("titleLabel", out var resolved));
        Assert.AreEqual(entity, resolved);
    }

    [TestMethod]
    public void TryResolve_UnknownName_ShouldReturnFalse()
    {
        Assert.IsFalse(_bridge.TryResolve("noSuchControl", out _));
    }

    [TestMethod]
    public void DestroyEntity_ShouldInvalidateBinding()
    {
        var entity = _entityManager.CreateEntity();
        _bridge.RegisterBinding("titleLabel", entity);

        _entityManager.DestroyEntity(entity);

        Assert.IsFalse(_bridge.TryResolve("titleLabel", out _));
    }

    [TestMethod]
    public void InvalidateScope_ShouldClearScopeBindings()
    {
        var e1 = _entityManager.CreateEntity();
        var e2 = _entityManager.CreateEntity();
        _bridge.RegisterBinding("ctrl1", e1);
        _bridge.RegisterBinding("ctrl2", e2);

        _bus.InvalidateScope("Side", new[] { e1, e2 });

        Assert.IsFalse(_bridge.TryResolve("ctrl1", out _));
        Assert.IsFalse(_bridge.TryResolve("ctrl2", out _));
    }

    [TestMethod]
    public void RebindSameName_ShouldResolveNewGeneration()
    {
        var oldEntity = _entityManager.CreateEntity();
        _bridge.RegisterBinding("ctrl1", oldEntity);
        _entityManager.DestroyEntity(oldEntity);

        // 模拟 ID 复用：同 ID 新代实体重新登记
        var newEntity = _entityManager.CreateEntity();
        _bridge.RegisterBinding("ctrl1", newEntity);

        Assert.IsTrue(_bridge.TryResolve("ctrl1", out var resolved));
        Assert.AreEqual(newEntity, resolved);
    }
}
