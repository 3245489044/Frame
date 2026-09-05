
using ECS.Core;
using ECS.Tests.Mocks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TConfig.ContextFrame.Abstractions;
using TConfig.ECS;
using TConfig.ECS.Components;

namespace ECS.Tests.ComponentTests;

[TestClass]
public class ControlHierarchyComponentTests
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
    public void CreateHierarchyComponent_ShouldHaveEmptyChildren()
    {
        var entity = _entityManager.CreateEntity();

        _entityManager.AddComponent(entity, new ControlHierarchyComponent
        {
            Parent = Entity.Invalid,
            Children = new List<Entity>()
        });

        var hierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(entity);
        Assert.AreEqual(Entity.Invalid, hierarchy.Parent);
        Assert.IsNotNull(hierarchy.Children);
        Assert.AreEqual(0, hierarchy.Children.Count);
    }

    [TestMethod]
    public void AddChild_ShouldUpdateChildrenList()
    {
        var parent = _entityManager.CreateEntity();
        var child = _entityManager.CreateEntity();

        _entityManager.AddComponent(parent, new ControlHierarchyComponent
        {
            Parent = Entity.Invalid,
            Children = new List<Entity>()
        });

        _entityManager.AddComponent(child, new ControlHierarchyComponent
        {
            Parent = parent,
            Children = new List<Entity>()
        });

        var hierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(parent);
        hierarchy.Children.Add(child);
        _entityManager.SetComponent(parent, hierarchy);

        var updatedHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(parent);
        Assert.AreEqual(1, updatedHierarchy.Children.Count);
        Assert.AreEqual(child, updatedHierarchy.Children[0]);
    }

    [TestMethod]
    public void AddMultipleChildren_ShouldMaintainOrder()
    {
        var parent = _entityManager.CreateEntity();
        var child1 = _entityManager.CreateEntity();
        var child2 = _entityManager.CreateEntity();
        var child3 = _entityManager.CreateEntity();

        _entityManager.AddComponent(parent, new ControlHierarchyComponent
        {
            Parent = Entity.Invalid,
            Children = new List<Entity>()
        });

        _entityManager.AddComponent(child1, new ControlHierarchyComponent { Parent = parent, Children = new List<Entity>() });
        _entityManager.AddComponent(child2, new ControlHierarchyComponent { Parent = parent, Children = new List<Entity>() });
        _entityManager.AddComponent(child3, new ControlHierarchyComponent { Parent = parent, Children = new List<Entity>() });

        var hierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(parent);
        hierarchy.Children.Add(child1);
        hierarchy.Children.Add(child2);
        hierarchy.Children.Add(child3);
        _entityManager.SetComponent(parent, hierarchy);

        var updated = _entityManager.GetComponent<ControlHierarchyComponent>(parent);
        Assert.AreEqual(3, updated.Children.Count);
        Assert.AreEqual(child1, updated.Children[0]);
        Assert.AreEqual(child2, updated.Children[1]);
        Assert.AreEqual(child3, updated.Children[2]);
    }

    [TestMethod]
    public void RemoveChild_ShouldUpdateChildrenList()
    {
        var parent = _entityManager.CreateEntity();
        var child = _entityManager.CreateEntity();

        _entityManager.AddComponent(parent, new ControlHierarchyComponent
        {
            Parent = Entity.Invalid,
            Children = new List<Entity> { child }
        });

        var hierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(parent);
        hierarchy.Children.Remove(child);
        _entityManager.SetComponent(parent, hierarchy);

        var updated = _entityManager.GetComponent<ControlHierarchyComponent>(parent);
        Assert.AreEqual(0, updated.Children.Count);
    }

    [TestMethod]
    public void ParentChildBidirectional_ShouldBeConsistent()
    {
        var parent = _entityManager.CreateEntity();
        var child = _entityManager.CreateEntity();

        _entityManager.AddComponent(parent, new ControlHierarchyComponent
        {
            Parent = Entity.Invalid,
            Children = new List<Entity>()
        });

        _entityManager.AddComponent(child, new ControlHierarchyComponent
        {
            Parent = parent,
            Children = new List<Entity>()
        });

        var parentHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(parent);
        parentHierarchy.Children.Add(child);
        _entityManager.SetComponent(parent, parentHierarchy);

        var childHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(child);
        Assert.AreEqual(parent, childHierarchy.Parent, "子 Entity 的 Parent 应指向父 Entity");

        var updatedParent = _entityManager.GetComponent<ControlHierarchyComponent>(parent);
        Assert.IsTrue(updatedParent.Children.Contains(child), "父 Entity 的 Children 应包含子 Entity");
    }
}
