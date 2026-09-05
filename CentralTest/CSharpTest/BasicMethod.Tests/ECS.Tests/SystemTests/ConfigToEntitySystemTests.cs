
using ECS.Bridge;
using ECS.Conversion;
using ECS.Core;
using ECS.Systems;
using ECS.Tests.Mocks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TConfig;
using TConfig.ContextFrame.Abstractions;
using TConfig.ECS;
using TConfig.ECS.Components;

namespace ECS.Tests.SystemTests;

[TestClass]
public class ConfigToEntitySystemTests
{
    private IECSStorage _storage = null!;
    private IStorageRegistry _registry = null!;
    private EntityManager _entityManager = null!;
    private ControlFactory _controlFactory = null!;
    private ConfigToEntitySystem _system = null!;

    [TestInitialize]
    public void Setup()
    {
        _storage = new MockECSStorage();
        _registry = new MockStorageRegistry();
        _entityManager = new EntityManager(_storage, _registry);
        _controlFactory = new ControlFactory(_entityManager);
        _system = new ConfigToEntitySystem(_entityManager, _controlFactory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _entityManager.Dispose();
    }

    [TestMethod]
    public void Execute_WithConfig_ShouldCreateRootEntity()
    {
        var config = new UIConfiguration
        {
            Form = new FormConfig { Title = "Test App" },
            Views = new List<ViewConfig>
            {
                new()
                {
                    Name = "MainView",
                    Controls = new List<ControlConfig>
                    {
                        new() { Type = "button", Name = "btnOK", Text = "OK" }
                    }
                }
            }
        };

        _system.Execute(config);

        Assert.IsTrue(_system.RootEntity.IsValid);
        Assert.IsTrue(_entityManager.IsEntityValid(_system.RootEntity));

        var typeComp = _entityManager.GetComponent<ControlTypeComponent>(_system.RootEntity);
        Assert.AreEqual("form", typeComp.Type);
        Assert.AreEqual("Test App", typeComp.Text);
    }

    [TestMethod]
    public void Execute_WithConfig_ShouldCreateFullHierarchy()
    {
        var config = new UIConfiguration
        {
            Views = new List<ViewConfig>
            {
                new()
                {
                    Name = "MainView",
                    Controls = new List<ControlConfig>
                    {
                        new() { Type = "button", Name = "btn1" },
                        new() { Type = "label", Name = "lbl1" }
                    }
                }
            }
        };

        _system.Execute(config);

        var rootHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(_system.RootEntity);
        Assert.AreEqual(1, rootHierarchy.Children.Count, "根应有 1 个 View");

        var viewEntity = rootHierarchy.Children[0];
        var viewHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(viewEntity);
        Assert.AreEqual(2, viewHierarchy.Children.Count, "View 应有 2 个控件");
    }

    [TestMethod]
    public void Execute_ShouldAddStateComponentToAllEntities()
    {
        var config = new UIConfiguration
        {
            Views = new List<ViewConfig>
            {
                new()
                {
                    Name = "MainView",
                    Controls = new List<ControlConfig>
                    {
                        new() { Type = "button", Name = "btn1" }
                    }
                }
            }
        };

        _system.Execute(config);

        var allEntities = _entityManager.GetAllEntities().ToList();
        foreach (var entity in allEntities)
        {
            Assert.IsTrue(_entityManager.HasComponent<ControlStateComponent>(entity),
                $"Entity {entity.Id} 应包含 ControlStateComponent");
        }
    }

    [TestMethod]
    public void Execute_ShouldSetInitialState()
    {
        var config = new UIConfiguration
        {
            Views = new List<ViewConfig>
            {
                new()
                {
                    Name = "MainView",
                    Controls = new List<ControlConfig>
                    {
                        new() { Type = "button", Name = "btn1" }
                    }
                }
            }
        };

        _system.Execute(config);

        var allEntities = _entityManager.GetAllEntities().ToList();
        foreach (var entity in allEntities)
        {
            var state = _entityManager.GetComponent<ControlStateComponent>(entity);
            Assert.IsFalse(state.IsCreated, "初始状态 IsCreated 应为 false");
            Assert.IsFalse(state.IsLayoutApplied, "初始状态 IsLayoutApplied 应为 false");
            Assert.IsFalse(state.IsStyleApplied, "初始状态 IsStyleApplied 应为 false");
            Assert.IsFalse(state.IsEventsBound, "初始状态 IsEventsBound 应为 false");
        }
    }

    [TestMethod]
    public void Execute_WithNullConfig_ShouldThrow()
    {
        try
        {
            _system.Execute(null!);
            Assert.Fail("应抛出 ArgumentNullException");
        }
        catch (ArgumentNullException)
        {
        }
    }

    [TestMethod]
    public void Execute_Twice_ShouldReplaceRootEntity()
    {
        var config1 = new UIConfiguration
        {
            Form = new FormConfig { Title = "First" }
        };
        var config2 = new UIConfiguration
        {
            Form = new FormConfig { Title = "Second" }
        };

        _system.Execute(config1);
        var firstRoot = _system.RootEntity;

        _system.Execute(config2);
        var secondRoot = _system.RootEntity;

        Assert.AreNotEqual(firstRoot, secondRoot, "两次 Execute 应产生不同的根 Entity");
        Assert.IsFalse(_entityManager.IsEntityValid(firstRoot), "第一次的根 Entity 应已销毁");

        var typeComp = _entityManager.GetComponent<ControlTypeComponent>(secondRoot);
        Assert.AreEqual("Second", typeComp.Text);
    }
}
