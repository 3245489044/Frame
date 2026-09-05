
using ECS.Bridge;
using ECS.Core;
using ECS.Tests.Mocks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TConfig;
using TConfig.ContextFrame.Abstractions;
using TConfig.ECS;
using TConfig.ECS.Components;

namespace ECS.Tests.BridgeTests;

[TestClass]
public class ControlFactoryTests
{
    private IECSStorage _storage = null!;
    private IStorageRegistry _registry = null!;
    private EntityManager _entityManager = null!;
    private ControlFactory _controlFactory = null!;

    [TestInitialize]
    public void Setup()
    {
        _storage = new MockECSStorage();
        _registry = new MockStorageRegistry();
        _entityManager = new EntityManager(_storage, _registry);
        _controlFactory = new ControlFactory(_entityManager);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _entityManager.Dispose();
    }

    [TestMethod]
    public void CreateFromConfig_ShouldCreateValidEntity()
    {
        var config = new ControlConfig
        {
            Type = "button",
            Name = "btnSubmit",
            Text = "Submit"
        };

        var entity = _controlFactory.CreateFromConfig(config, Entity.Invalid);

        Assert.IsTrue(entity.IsValid);
        Assert.IsTrue(_entityManager.IsEntityValid(entity));
    }

    [TestMethod]
    public void CreateFromConfig_ShouldAddControlTypeComponent()
    {
        var config = new ControlConfig
        {
            Type = "button",
            Name = "btnSubmit",
            Text = "Submit",
            Enabled = true
        };

        var entity = _controlFactory.CreateFromConfig(config, Entity.Invalid);

        Assert.IsTrue(_entityManager.HasComponent<ControlTypeComponent>(entity));
        var typeComp = _entityManager.GetComponent<ControlTypeComponent>(entity);
        Assert.AreEqual("button", typeComp.Type);
        Assert.AreEqual("btnSubmit", typeComp.Name);
        Assert.AreEqual("Submit", typeComp.Text);
        Assert.IsTrue(typeComp.Enabled);
    }

    [TestMethod]
    public void CreateFromConfig_ShouldAddHierarchyComponent()
    {
        var parent = _entityManager.CreateEntity();
        var config = new ControlConfig { Type = "label", Name = "lblTest" };

        var entity = _controlFactory.CreateFromConfig(config, parent);

        Assert.IsTrue(_entityManager.HasComponent<ControlHierarchyComponent>(entity));
        var hierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(entity);
        Assert.AreEqual(parent, hierarchy.Parent);
        Assert.IsNotNull(hierarchy.Children);
        Assert.AreEqual(0, hierarchy.Children.Count);
    }

    [TestMethod]
    public void CreateFromConfig_WithLayoutPosition_ShouldAddProportionalLayout()
    {
        var config = new ControlConfig
        {
            Type = "panel",
            Name = "pnlMain",
            Layout = new LayoutPosition
            {
                Left = 0.1,
                Top = 0.2,
                Width = 0.8,
                Height = 0.6
            }
        };

        var entity = _controlFactory.CreateFromConfig(config, Entity.Invalid);

        Assert.IsTrue(_entityManager.HasComponent<ProportionalLayoutComponent>(entity));
        var layout = _entityManager.GetComponent<ProportionalLayoutComponent>(entity);
        Assert.AreEqual(0.1f, layout.Left);
        Assert.AreEqual(0.2f, layout.Top);
        Assert.AreEqual(0.8f, layout.Width);
        Assert.AreEqual(0.6f, layout.Height);
    }

    [TestMethod]
    public void CreateFromConfig_WithPixelLayout_ShouldAddPixelLayout()
    {
        var config = new ControlConfig
        {
            Type = "button",
            Name = "btnOK",
            X = 100,
            Y = 200,
            Width = 150,
            Height = 30
        };

        var entity = _controlFactory.CreateFromConfig(config, Entity.Invalid);

        Assert.IsTrue(_entityManager.HasComponent<PixelLayoutComponent>(entity));
        var pixel = _entityManager.GetComponent<PixelLayoutComponent>(entity);
        Assert.AreEqual(100, pixel.Left);
        Assert.AreEqual(200, pixel.Top);
        Assert.AreEqual(150, pixel.Width);
        Assert.AreEqual(30, pixel.Height);
    }

    [TestMethod]
    public void CreateFromConfig_WithStyle_ShouldAddColorAndFontComponents()
    {
        var config = new ControlConfig
        {
            Type = "label",
            Name = "lblTitle",
            Style = new StyleConfig
            {
                BackColor = "#FFFFFF",
                ForeColor = "#000000",
                FontFamily = "Arial",
                FontSize = 12f,
                FontStyle = "Bold"
            }
        };

        var entity = _controlFactory.CreateFromConfig(config, Entity.Invalid);

        Assert.IsTrue(_entityManager.HasComponent<ColorStyleComponent>(entity));
        Assert.IsTrue(_entityManager.HasComponent<FontStyleComponent>(entity));

        var color = _entityManager.GetComponent<ColorStyleComponent>(entity);
        Assert.AreEqual("#FFFFFF", color.BackColor);
        Assert.AreEqual("#000000", color.ForeColor);

        var font = _entityManager.GetComponent<FontStyleComponent>(entity);
        Assert.AreEqual("Arial", font.FontFamily);
        Assert.AreEqual(12f, font.FontSize);
        Assert.IsTrue(font.Bold);
    }

    [TestMethod]
    public void CreateFromConfig_WithEvents_ShouldAddEventBindings()
    {
        var config = new ControlConfig
        {
            Type = "button",
            Name = "btnCalc",
            Events = new EventConfig
            {
                EventsDictionary = new Dictionary<string, List<string>>
                {
                    ["click"] = new() { "Calculate" }
                }
            }
        };

        var entity = _controlFactory.CreateFromConfig(config, Entity.Invalid);

        Assert.IsTrue(_entityManager.HasComponent<EventBindingsComponent>(entity));
        var events = _entityManager.GetComponent<EventBindingsComponent>(entity);
        Assert.AreEqual(1, events.Bindings.Count);
        Assert.AreEqual("click", events.Bindings[0].EventName);
        Assert.AreEqual("Calculate", events.Bindings[0].FunctionName);
    }

    [TestMethod]
    public void CreateFromConfig_ShouldAddStateComponent()
    {
        var config = new ControlConfig { Type = "textbox", Name = "txtInput" };

        var entity = _controlFactory.CreateFromConfig(config, Entity.Invalid);

        Assert.IsTrue(_entityManager.HasComponent<ControlStateComponent>(entity));
        var state = _entityManager.GetComponent<ControlStateComponent>(entity);
        Assert.IsFalse(state.IsCreated);
        Assert.IsFalse(state.IsLayoutApplied);
        Assert.IsFalse(state.IsStyleApplied);
        Assert.IsFalse(state.IsEventsBound);
    }

    [TestMethod]
    public void CreateFromConfig_WithoutLayout_ShouldNotAddProportionalLayout()
    {
        var config = new ControlConfig { Type = "label", Name = "lblSimple" };

        var entity = _controlFactory.CreateFromConfig(config, Entity.Invalid);

        Assert.IsFalse(_entityManager.HasComponent<ProportionalLayoutComponent>(entity));
    }

    [TestMethod]
    public void CreateFromConfig_WithoutStyle_ShouldNotAddStyleComponents()
    {
        var config = new ControlConfig { Type = "label", Name = "lblSimple" };

        var entity = _controlFactory.CreateFromConfig(config, Entity.Invalid);

        Assert.IsFalse(_entityManager.HasComponent<ColorStyleComponent>(entity));
        Assert.IsFalse(_entityManager.HasComponent<FontStyleComponent>(entity));
    }

    [TestMethod]
    public void CreateFromConfig_WithoutEvents_ShouldNotAddEventBindings()
    {
        var config = new ControlConfig { Type = "label", Name = "lblSimple" };

        var entity = _controlFactory.CreateFromConfig(config, Entity.Invalid);

        Assert.IsFalse(_entityManager.HasComponent<EventBindingsComponent>(entity));
    }
}
