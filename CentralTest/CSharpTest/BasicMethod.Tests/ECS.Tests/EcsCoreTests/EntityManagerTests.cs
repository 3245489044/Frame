
using ECS.Core;
using ECS.Tests.Mocks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TConfig.ContextFrame.Abstractions;
using TConfig.ECS;
using TConfig.ECS.Components;

namespace ECS.Tests.EcsCoreTests;

[TestClass]
public class EntityManagerTests
{
    private IECSStorage _storage = null!;
    private IStorageRegistry _registry = null!;
    private EntityManager _entityManager = null!;

    [TestInitialize]
    public void Setup()
    {
        _storage = new MockECSStorage();
        _registry = new MockStorageRegistry();
        _entityManager = new EntityManager(_storage, _registry);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _entityManager.Dispose();
    }


    [TestMethod]
    public void CreateEntity_ShouldReturnValidEntity()
    {
        var entity = _entityManager.CreateEntity();

        Assert.IsTrue(entity.IsValid, "创建的 Entity 应为有效");
        Assert.IsTrue(_entityManager.IsEntityValid(entity), "EntityManager 应确认 Entity 有效");
    }

    [TestMethod]
    public void CreateEntity_ShouldIncrementEntityCount()
    {
        var e1 = _entityManager.CreateEntity();
        var e2 = _entityManager.CreateEntity();
        var e3 = _entityManager.CreateEntity();

        Assert.AreEqual(3, _entityManager.GetEntityCount());
    }

    [TestMethod]
    public void DestroyEntity_ShouldMakeEntityInvalid()
    {
        var entity = _entityManager.CreateEntity();

        _entityManager.DestroyEntity(entity);

        Assert.IsFalse(_entityManager.IsEntityValid(entity), "销毁后 Entity 应无效");
    }

    [TestMethod]
    public void DestroyEntity_ShouldDecrementEntityCount()
    {
        var e1 = _entityManager.CreateEntity();
        var e2 = _entityManager.CreateEntity();

        _entityManager.DestroyEntity(e1);

        Assert.AreEqual(1, _entityManager.GetEntityCount());
    }

    [TestMethod]
    public void DestroyEntity_InvalidEntity_ShouldNotThrow()
    {
        var invalidEntity = new Entity(999, 1);

        _entityManager.DestroyEntity(invalidEntity);
    }


    [TestMethod]
    public void AddComponent_ShouldStoreComponent()
    {
        var entity = _entityManager.CreateEntity();
        var component = new ControlTypeComponent
        {
            Type = "button",
            Name = "testButton",
            Text = "Click Me"
        };

        _entityManager.AddComponent(entity, component);

        Assert.IsTrue(_entityManager.HasComponent<ControlTypeComponent>(entity));
        var retrieved = _entityManager.GetComponent<ControlTypeComponent>(entity);
        Assert.AreEqual("button", retrieved.Type);
        Assert.AreEqual("testButton", retrieved.Name);
        Assert.AreEqual("Click Me", retrieved.Text);
    }

    [TestMethod]
    public void AddComponent_MultipleComponents_ShouldAllBeAccessible()
    {
        var entity = _entityManager.CreateEntity();

        _entityManager.AddComponent(entity, new ControlTypeComponent { Type = "label", Name = "lbl1" });
        _entityManager.AddComponent(entity, new ControlStateComponent { IsCreated = false });

        Assert.IsTrue(_entityManager.HasComponent<ControlTypeComponent>(entity));
        Assert.IsTrue(_entityManager.HasComponent<ControlStateComponent>(entity));
    }

    [TestMethod]
    public void AddComponent_InvalidEntity_ShouldThrow()
    {
        var invalidEntity = new Entity(999, 1);

        try
        {
            _entityManager.AddComponent(invalidEntity, new ControlTypeComponent { Type = "button" });
            Assert.Fail("应抛出 ArgumentException");
        }
        catch (ArgumentException)
        {
        }
    }


    [TestMethod]
    public void SetComponent_ShouldUpdateExistingComponent()
    {
        var entity = _entityManager.CreateEntity();
        _entityManager.AddComponent(entity, new ControlTypeComponent
        {
            Type = "button",
            Name = "oldName",
            Text = "Old Text"
        });

        _entityManager.SetComponent(entity, new ControlTypeComponent
        {
            Type = "button",
            Name = "newName",
            Text = "New Text"
        });

        var retrieved = _entityManager.GetComponent<ControlTypeComponent>(entity);
        Assert.AreEqual("newName", retrieved.Name);
        Assert.AreEqual("New Text", retrieved.Text);
    }

    [TestMethod]
    public void SetComponent_NonExistentComponent_ShouldThrow()
    {
        var entity = _entityManager.CreateEntity();

        try
        {
            _entityManager.SetComponent(entity, new ControlTypeComponent { Type = "button" });
            Assert.Fail("应抛出 InvalidOperationException");
        }
        catch (InvalidOperationException)
        {
        }
    }


    [TestMethod]
    public void RemoveComponent_ShouldRemoveComponent()
    {
        var entity = _entityManager.CreateEntity();
        _entityManager.AddComponent(entity, new ControlTypeComponent { Type = "button" });

        _entityManager.RemoveComponent<ControlTypeComponent>(entity);

        Assert.IsFalse(_entityManager.HasComponent<ControlTypeComponent>(entity));
    }

    [TestMethod]
    public void RemoveComponent_NonExistentComponent_ShouldNotThrow()
    {
        var entity = _entityManager.CreateEntity();

        _entityManager.RemoveComponent<ControlTypeComponent>(entity);
    }


    [TestMethod]
    public void TryGetComponent_ExistingComponent_ShouldReturnTrue()
    {
        var entity = _entityManager.CreateEntity();
        _entityManager.AddComponent(entity, new ControlTypeComponent { Type = "label" });

        var result = _entityManager.TryGetComponent<ControlTypeComponent>(entity, out var component);

        Assert.IsTrue(result);
        Assert.AreEqual("label", component.Type);
    }

    [TestMethod]
    public void TryGetComponent_NonExistentComponent_ShouldReturnFalse()
    {
        var entity = _entityManager.CreateEntity();

        var result = _entityManager.TryGetComponent<ControlTypeComponent>(entity, out _);

        Assert.IsFalse(result);
    }


    [TestMethod]
    public void GetEntities_WithComponentType_ShouldReturnMatchingEntities()
    {
        var e1 = _entityManager.CreateEntity();
        var e2 = _entityManager.CreateEntity();
        var e3 = _entityManager.CreateEntity();

        _entityManager.AddComponent(e1, new ControlTypeComponent { Type = "button" });
        _entityManager.AddComponent(e2, new ControlTypeComponent { Type = "label" });

        var entities = _entityManager.GetEntities<ControlTypeComponent>().ToList();

        Assert.AreEqual(2, entities.Count);
    }

    [TestMethod]
    public void GetEntities_MultipleComponentTypes_ShouldReturnIntersection()
    {
        var e1 = _entityManager.CreateEntity();
        var e2 = _entityManager.CreateEntity();

        _entityManager.AddComponent(e1, new ControlTypeComponent { Type = "button" });
        _entityManager.AddComponent(e1, new ControlStateComponent { IsCreated = true });

        _entityManager.AddComponent(e2, new ControlTypeComponent { Type = "label" });

        var entities = _entityManager.GetEntities<ControlTypeComponent, ControlStateComponent>().ToList();

        Assert.AreEqual(1, entities.Count);
        Assert.AreEqual(e1, entities[0]);
    }

    [TestMethod]
    public void GetAllEntities_ShouldReturnAllEntities()
    {
        var e1 = _entityManager.CreateEntity();
        var e2 = _entityManager.CreateEntity();
        var e3 = _entityManager.CreateEntity();

        var all = _entityManager.GetAllEntities().ToList();

        Assert.AreEqual(3, all.Count);
    }


    [TestMethod]
    public void Clear_ShouldRemoveAllEntities()
    {
        _entityManager.CreateEntity();
        _entityManager.CreateEntity();
        _entityManager.CreateEntity();

        _entityManager.Clear();

        Assert.AreEqual(0, _entityManager.GetEntityCount());
    }


    [TestMethod]
    public void EntityCreated_ShouldFireOnCreate()
    {
        Entity? createdEntity = null;
        _entityManager.EntityCreated += (e) => createdEntity = e;

        var entity = _entityManager.CreateEntity();

        Assert.IsNotNull(createdEntity);
        Assert.AreEqual(entity, createdEntity.Value);
    }

    [TestMethod]
    public void EntityDestroyed_ShouldFireOnDestroy()
    {
        Entity? destroyedEntity = null;
        _entityManager.EntityDestroyed += (e) => destroyedEntity = e;
        var entity = _entityManager.CreateEntity();

        _entityManager.DestroyEntity(entity);

        Assert.IsNotNull(destroyedEntity);
        Assert.AreEqual(entity, destroyedEntity.Value);
    }

    [TestMethod]
    public void ComponentAdded_ShouldFireOnAdd()
    {
        Entity? addedEntity = null;
        Type? addedType = null;
        _entityManager.ComponentAdded += (e, t) => { addedEntity = e; addedType = t; };
        var entity = _entityManager.CreateEntity();

        _entityManager.AddComponent(entity, new ControlTypeComponent { Type = "button" });

        Assert.AreEqual(entity, addedEntity);
        Assert.AreEqual(typeof(ControlTypeComponent), addedType);
    }

    [TestMethod]
    public void ComponentRemoved_ShouldFireOnRemove()
    {
        Entity? removedEntity = null;
        Type? removedType = null;
        _entityManager.ComponentRemoved += (e, t) => { removedEntity = e; removedType = t; };
        var entity = _entityManager.CreateEntity();
        _entityManager.AddComponent(entity, new ControlTypeComponent { Type = "button" });

        _entityManager.RemoveComponent<ControlTypeComponent>(entity);

        Assert.AreEqual(entity, removedEntity);
        Assert.AreEqual(typeof(ControlTypeComponent), removedType);
    }
}
