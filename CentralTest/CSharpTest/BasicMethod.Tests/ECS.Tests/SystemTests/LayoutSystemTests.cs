
using ECS.Core;
using ECS.Systems;
using ECS.Tests.Mocks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TConfig.ContextFrame.Abstractions;
using TConfig.ECS;
using TConfig.ECS.Components;

namespace ECS.Tests.SystemTests;

[TestClass]
public class LayoutSystemTests
{
    private IECSStorage _storage = null!;
    private IStorageRegistry _registry = null!;
    private EntityManager _entityManager = null!;
    private LayoutSystem _layoutSystem = null!;

    [TestInitialize]
    public void Setup()
    {
        _storage = new MockECSStorage();
        _registry = new MockStorageRegistry();
        _entityManager = new EntityManager(_storage, _registry);
        _layoutSystem = new LayoutSystem(_entityManager, null);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _entityManager.Dispose();
    }

    private Entity CreateControlWithLayout(float left, float top, float width, float height)
    {
        var entity = _entityManager.CreateEntity();
        _entityManager.AddComponent(entity, new ControlTypeComponent { Type = "button", Name = "btn" });
        _entityManager.AddComponent(entity, new ProportionalLayoutComponent
        {
            Left = left,
            Top = top,
            Width = width,
            Height = height
        });
        _entityManager.AddComponent(entity, new ControlHierarchyComponent
        {
            Parent = Entity.Invalid,
            Children = new List<Entity>()
        });
        _entityManager.AddComponent(entity, new ControlStateComponent());
        return entity;
    }

    [TestMethod]
    public void Update_WithParentLayout_ShouldCalculatePixelValues()
    {
        var parent = _entityManager.CreateEntity();
        _entityManager.AddComponent(parent, new ControlTypeComponent { Type = "panel", Name = "pnl" });
        _entityManager.AddComponent(parent, new ComputedLayoutComponent
        {
            Left = 0, Top = 0, Width = 800, Height = 600,
            ParentWidth = 800, ParentHeight = 600
        });
        _entityManager.AddComponent(parent, new ControlHierarchyComponent
        {
            Parent = Entity.Invalid,
            Children = new List<Entity>()
        });
        _entityManager.AddComponent(parent, new ControlStateComponent { IsLayoutApplied = true });

        var child = CreateControlWithLayout(0.1f, 0.2f, 0.5f, 0.3f);
        var childHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(child);
        childHierarchy.Parent = parent;
        _entityManager.SetComponent(child, childHierarchy);

        _layoutSystem.Update();

        Assert.IsTrue(_entityManager.HasComponent<ComputedLayoutComponent>(child));
        var computed = _entityManager.GetComponent<ComputedLayoutComponent>(child);
        Assert.AreEqual(80, computed.Left);
        Assert.AreEqual(120, computed.Top);
        Assert.AreEqual(400, computed.Width);
        Assert.AreEqual(180, computed.Height);
    }

    [TestMethod]
    public void Update_WithScreenRoot_ShouldUseScreenDimensions()
    {
        var entity = CreateControlWithLayout(0f, 0f, 1f, 1f);
        _entityManager.AddComponent(entity, new ScreenRootComponent
        {
            Width = 1024,
            Height = 768
        });

        _layoutSystem.Update();

        var computed = _entityManager.GetComponent<ComputedLayoutComponent>(entity);
        Assert.AreEqual(1024, computed.Width);
        Assert.AreEqual(768, computed.Height);
    }

    [TestMethod]
    public void Update_AlreadyApplied_ShouldSkip()
    {
        var entity = CreateControlWithLayout(0.5f, 0.5f, 0.5f, 0.5f);
        var state = _entityManager.GetComponent<ControlStateComponent>(entity);
        state.IsLayoutApplied = true;
        _entityManager.SetComponent(entity, state);

        _layoutSystem.Update();

        Assert.IsFalse(_entityManager.HasComponent<ComputedLayoutComponent>(entity),
            "已应用布局的 Entity 不应重新计算");
    }

    [TestMethod]
    public void Update_NoParentAndNoScreenRoot_ShouldSkip()
    {
        var entity = CreateControlWithLayout(0.1f, 0.1f, 0.5f, 0.5f);

        _layoutSystem.Update();

        Assert.IsFalse(_entityManager.HasComponent<ComputedLayoutComponent>(entity),
            "无父容器且无屏幕根信息时应跳过");
    }

    [TestMethod]
    public void Update_ShouldSetIsLayoutApplied()
    {
        var entity = CreateControlWithLayout(0f, 0f, 1f, 1f);
        _entityManager.AddComponent(entity, new ScreenRootComponent { Width = 800, Height = 600 });

        _layoutSystem.Update();

        var state = _entityManager.GetComponent<ControlStateComponent>(entity);
        Assert.IsTrue(state.IsLayoutApplied);
    }

    [TestMethod]
    public void RecalculateAll_ShouldResetAndRecalculate()
    {
        var entity = CreateControlWithLayout(0f, 0f, 1f, 1f);
        _entityManager.AddComponent(entity, new ScreenRootComponent { Width = 800, Height = 600 });

        _layoutSystem.Update();
        var computed = _entityManager.GetComponent<ComputedLayoutComponent>(entity);
        Assert.AreEqual(800, computed.Width);

        _entityManager.SetComponent(entity, new ScreenRootComponent { Width = 1024, Height = 768 });

        _layoutSystem.RecalculateAll();

        var recomputed = _entityManager.GetComponent<ComputedLayoutComponent>(entity);
        Assert.AreEqual(1024, recomputed.Width, "RecalculateAll 后应使用新的屏幕尺寸");
    }
}
