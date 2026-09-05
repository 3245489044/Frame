
using ECS.Bridge;
using ECS.Conversion;
using ECS.Core;
using ECS.Tests.Mocks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TConfig;
using TConfig.ContextFrame.Abstractions;
using TConfig.ECS;
using TConfig.ECS.Components;

namespace ECS.Tests.BridgeTests;

[TestClass]
public class ConfigToEntityConverterTests
{
    private IECSStorage _storage = null!;
    private IStorageRegistry _registry = null!;
    private EntityManager _entityManager = null!;
    private ControlFactory _controlFactory = null!;
    private ConfigToEntityConverter _converter = null!;

    [TestInitialize]
    public void Setup()
    {
        _storage = new MockECSStorage();
        _registry = new MockStorageRegistry();
        _entityManager = new EntityManager(_storage, _registry);
        _controlFactory = new ControlFactory(_entityManager);
        _converter = new ConfigToEntityConverter(_controlFactory, _entityManager);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _entityManager.Dispose();
    }

    [TestMethod]
    public void Convert_EmptyConfig_ShouldCreateRootEntity()
    {
        var config = new UIConfiguration();

        var rootEntity = _converter.Convert(config);

        Assert.IsTrue(rootEntity.IsValid);
        Assert.IsTrue(_entityManager.IsEntityValid(rootEntity));

        var typeComp = _entityManager.GetComponent<ControlTypeComponent>(rootEntity);
        Assert.AreEqual("form", typeComp.Type);
        Assert.AreEqual("root", typeComp.Name);
    }

    [TestMethod]
    public void Convert_WithFormTitle_ShouldSetRootText()
    {
        var config = new UIConfiguration
        {
            Form = new FormConfig { Title = "My App" }
        };

        var rootEntity = _converter.Convert(config);

        var typeComp = _entityManager.GetComponent<ControlTypeComponent>(rootEntity);
        Assert.AreEqual("My App", typeComp.Text);
    }

    [TestMethod]
    public void Convert_WithSingleView_ShouldCreateViewEntity()
    {
        var config = new UIConfiguration
        {
            Views = new List<ViewConfig>
            {
                new() { Name = "MainView" }
            }
        };

        var rootEntity = _converter.Convert(config);

        var rootHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(rootEntity);
        Assert.AreEqual(1, rootHierarchy.Children.Count, "根 Entity 应有 1 个子 Entity");

        var viewEntity = rootHierarchy.Children[0];
        var viewType = _entityManager.GetComponent<ControlTypeComponent>(viewEntity);
        Assert.AreEqual("view", viewType.Type);
        Assert.AreEqual("MainView", viewType.Name);

        var viewHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(viewEntity);
        Assert.AreEqual(rootEntity, viewHierarchy.Parent, "View 的 Parent 应为根 Entity");
    }

    [TestMethod]
    public void Convert_WithMultipleViews_ShouldCreateAllViews()
    {
        var config = new UIConfiguration
        {
            Views = new List<ViewConfig>
            {
                new() { Name = "View1" },
                new() { Name = "View2" },
                new() { Name = "View3" }
            }
        };

        var rootEntity = _converter.Convert(config);

        var rootHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(rootEntity);
        Assert.AreEqual(3, rootHierarchy.Children.Count);
    }

    [TestMethod]
    public void Convert_WithControlsInView_ShouldCreateControlEntities()
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
                        new() { Type = "button", Name = "btnOK", Text = "OK" },
                        new() { Type = "label", Name = "lblStatus", Text = "Ready" }
                    }
                }
            }
        };

        var rootEntity = _converter.Convert(config);

        var rootHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(rootEntity);
        var viewEntity = rootHierarchy.Children[0];
        var viewHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(viewEntity);

        Assert.AreEqual(2, viewHierarchy.Children.Count, "View 应有 2 个子控件");

        var btnEntity = viewHierarchy.Children[0];
        var btnType = _entityManager.GetComponent<ControlTypeComponent>(btnEntity);
        Assert.AreEqual("button", btnType.Type);
        Assert.AreEqual("btnOK", btnType.Name);

        var lblEntity = viewHierarchy.Children[1];
        var lblType = _entityManager.GetComponent<ControlTypeComponent>(lblEntity);
        Assert.AreEqual("label", lblType.Type);
        Assert.AreEqual("lblStatus", lblType.Name);
    }

    [TestMethod]
    public void Convert_WithNestedControls_ShouldCreateHierarchy()
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
                        new()
                        {
                            Type = "groupbox",
                            Name = "gbSettings",
                            Controls = new List<ControlConfig>
                            {
                                new() { Type = "label", Name = "lblName" },
                                new() { Type = "textbox", Name = "txtName" }
                            }
                        }
                    }
                }
            }
        };

        var rootEntity = _converter.Convert(config);

        var rootHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(rootEntity);
        var viewEntity = rootHierarchy.Children[0];
        var viewHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(viewEntity);
        var groupBoxEntity = viewHierarchy.Children[0];
        var groupBoxHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(groupBoxEntity);

        Assert.AreEqual(2, groupBoxHierarchy.Children.Count, "GroupBox 应有 2 个子控件");

        var groupBoxType = _entityManager.GetComponent<ControlTypeComponent>(groupBoxEntity);
        Assert.AreEqual("groupbox", groupBoxType.Type);
    }

    [TestMethod]
    public void Convert_WithControlConfig_ShouldPreserveAllProperties()
    {
        var config = new UIConfiguration
        {
            Views = new List<ViewConfig>
            {
                new()
                {
                    Name = "FormView",
                    Controls = new List<ControlConfig>
                    {
                        new()
                        {
                            Type = "button",
                            Name = "btnSubmit",
                            Text = "Submit",
                            X = 10,
                            Y = 20,
                            Width = 100,
                            Height = 30,
                            Layout = new LayoutPosition
                            {
                                Left = 0.1, Top = 0.2, Width = 0.3, Height = 0.1
                            }
                        }
                    }
                }
            }
        };

        var rootEntity = _converter.Convert(config);
        var rootHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(rootEntity);
        var viewHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(rootHierarchy.Children[0]);
        var btnEntity = viewHierarchy.Children[0];

        var typeComp = _entityManager.GetComponent<ControlTypeComponent>(btnEntity);
        Assert.AreEqual("button", typeComp.Type);
        Assert.AreEqual("btnSubmit", typeComp.Name);
        Assert.AreEqual("Submit", typeComp.Text);

        var pixelLayout = _entityManager.GetComponent<PixelLayoutComponent>(btnEntity);
        Assert.AreEqual(10, pixelLayout.Left);
        Assert.AreEqual(20, pixelLayout.Top);
        Assert.AreEqual(100, pixelLayout.Width);
        Assert.AreEqual(30, pixelLayout.Height);

        var propLayout = _entityManager.GetComponent<ProportionalLayoutComponent>(btnEntity);
        Assert.AreEqual(0.1f, propLayout.Left);
        Assert.AreEqual(0.2f, propLayout.Top);
        Assert.AreEqual(0.3f, propLayout.Width);
        Assert.AreEqual(0.1f, propLayout.Height);
    }

    [TestMethod]
    public void Convert_NoViews_ShouldCreateRootOnly()
    {
        var config = new UIConfiguration
        {
            Views = null
        };

        var rootEntity = _converter.Convert(config);

        var rootHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(rootEntity);
        Assert.AreEqual(0, rootHierarchy.Children.Count, "无 View 时根 Entity 应无子 Entity");
    }

    [TestMethod]
    public void Convert_EmptyViewsList_ShouldCreateRootOnly()
    {
        var config = new UIConfiguration
        {
            Views = new List<ViewConfig>()
        };

        var rootEntity = _converter.Convert(config);

        var rootHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(rootEntity);
        Assert.AreEqual(0, rootHierarchy.Children.Count);
    }

    [TestMethod]
    public void Convert_ViewWithNoControls_ShouldCreateViewWithNoChildren()
    {
        var config = new UIConfiguration
        {
            Views = new List<ViewConfig>
            {
                new() { Name = "EmptyView", Controls = null }
            }
        };

        var rootEntity = _converter.Convert(config);
        var rootHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(rootEntity);
        var viewEntity = rootHierarchy.Children[0];
        var viewHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(viewEntity);

        Assert.AreEqual(0, viewHierarchy.Children.Count, "无控件的 View 应无子 Entity");
    }
}
