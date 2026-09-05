
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

namespace ECS.Tests.IntegrationTests;

[TestClass]
public class PipelineTests
{
    private IECSStorage _storage = null!;
    private IStorageRegistry _registry = null!;
    private EntityManager _entityManager = null!;
    private ControlFactory _controlFactory = null!;
    private ConfigToEntitySystem _configToEntitySystem = null!;
    private LayoutSystem _layoutSystem = null!;
    private StyleSystem _styleSystem = null!;

    [TestInitialize]
    public void Setup()
    {
        _storage = new MockECSStorage();
        _registry = new MockStorageRegistry();
        _entityManager = new EntityManager(_storage, _registry);
        _controlFactory = new ControlFactory(_entityManager);
        _configToEntitySystem = new ConfigToEntitySystem(_entityManager, _controlFactory);
        _layoutSystem = new LayoutSystem(_entityManager, null);
        _styleSystem = new StyleSystem(_entityManager);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _entityManager.Dispose();
    }

    [TestMethod]
    public void FullPipeline_ShouldProcessAllStages()
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
                        new()
                        {
                            Type = "button",
                            Name = "btnSubmit",
                            Text = "Submit",
                            Layout = new LayoutPosition
                            {
                                Left = 0.1, Top = 0.2,
                                Width = 0.3, Height = 0.1
                            },
                            Style = new StyleConfig
                            {
                                BackColor = "#0078D4",
                                ForeColor = "#FFFFFF",
                                FontFamily = "Segoe UI",
                                FontSize = 10f
                            }
                        }
                    }
                }
            }
        };

        _configToEntitySystem.Execute(config);

        _entityManager.AddComponent(_configToEntitySystem.RootEntity, new ScreenRootComponent
        {
            Width = 800,
            Height = 600
        });

        foreach (var entity in _entityManager.GetAllEntities())
        {
            if (!_entityManager.HasComponent<ControlStateComponent>(entity))
            {
                _entityManager.AddComponent(entity, new ControlStateComponent());
            }
        }

        _layoutSystem.Update();

        _styleSystem.Update();

        foreach (var entity in _entityManager.GetAllEntities())
        {
            if (!_entityManager.HasComponent<ProportionalLayoutComponent>(entity))
                continue;

            var state = _entityManager.GetComponent<ControlStateComponent>(entity);
            Assert.IsTrue(state.IsLayoutApplied, $"Entity {entity.Id} 布局应已应用");
            Assert.IsTrue(state.IsStyleApplied, $"Entity {entity.Id} 样式应已应用");
        }

        var rootHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(_configToEntitySystem.RootEntity);
        var viewEntity = rootHierarchy.Children[0];
        var viewHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(viewEntity);
        var btnEntity = viewHierarchy.Children[0];

        Assert.IsTrue(_entityManager.HasComponent<ComputedLayoutComponent>(btnEntity));
        var computed = _entityManager.GetComponent<ComputedLayoutComponent>(btnEntity);
        Assert.AreEqual(80, computed.Left);
        Assert.AreEqual(120, computed.Top);
        Assert.AreEqual(240, computed.Width);
        Assert.AreEqual(60, computed.Height);

        Assert.IsTrue(_entityManager.HasComponent<MergedStyleComponent>(btnEntity));
        var merged = _entityManager.GetComponent<MergedStyleComponent>(btnEntity);
        Assert.AreEqual("#0078D4", merged.BackColor);
        Assert.AreEqual("#FFFFFF", merged.ForeColor);
        Assert.AreEqual("Segoe UI", merged.FontFamily);
        Assert.AreEqual(10f, merged.FontSize);
    }

    [TestMethod]
    public void FullPipeline_WithNestedControls_ShouldProcessAll()
    {
        var config = new UIConfiguration
        {
            Form = new FormConfig { Title = "Nested Test" },
            Views = new List<ViewConfig>
            {
                new()
                {
                    Name = "MainView",
                    Controls = new List<ControlConfig>
                    {
                        new()
                        {
                            Type = "panel",
                            Name = "pnlMain",
                            Layout = new LayoutPosition
                            {
                                Left = 0, Top = 0, Width = 1, Height = 1
                            },
                            Controls = new List<ControlConfig>
                            {
                                new()
                                {
                                    Type = "button",
                                    Name = "btnInner",
                                    Text = "Inner",
                                    Layout = new LayoutPosition
                                    {
                                        Left = 0.1, Top = 0.1,
                                        Width = 0.5, Height = 0.3
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        _configToEntitySystem.Execute(config);
        _entityManager.AddComponent(_configToEntitySystem.RootEntity, new ScreenRootComponent
        {
            Width = 1024,
            Height = 768
        });

        foreach (var entity in _entityManager.GetAllEntities())
        {
            if (!_entityManager.HasComponent<ControlStateComponent>(entity))
            {
                _entityManager.AddComponent(entity, new ControlStateComponent());
            }
        }

        _layoutSystem.Update();
        _styleSystem.Update();

        var rootHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(_configToEntitySystem.RootEntity);
        var viewEntity = rootHierarchy.Children[0];
        var viewHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(viewEntity);
        var panelEntity = viewHierarchy.Children[0];
        var panelHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(panelEntity);
        var innerBtnEntity = panelHierarchy.Children[0];

        var panelComputed = _entityManager.GetComponent<ComputedLayoutComponent>(panelEntity);
        Assert.AreEqual(1024, panelComputed.Width);
        Assert.AreEqual(768, panelComputed.Height);

        var btnComputed = _entityManager.GetComponent<ComputedLayoutComponent>(innerBtnEntity);
        Assert.AreEqual(102, btnComputed.Left);
        Assert.AreEqual(76, btnComputed.Top);
        Assert.AreEqual(512, btnComputed.Width);
        Assert.AreEqual(230, btnComputed.Height);
    }

    [TestMethod]
    public void FullPipeline_WithMultipleViews_ShouldProcessAll()
    {
        var config = new UIConfiguration
        {
            Form = new FormConfig { Title = "Multi View" },
            Views = new List<ViewConfig>
            {
                new()
                {
                    Name = "View1",
                    Controls = new List<ControlConfig>
                    {
                        new() { Type = "label", Name = "lbl1", Text = "View 1 Label" }
                    }
                },
                new()
                {
                    Name = "View2",
                    Controls = new List<ControlConfig>
                    {
                        new() { Type = "button", Name = "btn2", Text = "View 2 Button" }
                    }
                }
            }
        };

        _configToEntitySystem.Execute(config);

        var rootHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(_configToEntitySystem.RootEntity);
        Assert.AreEqual(2, rootHierarchy.Children.Count, "应有 2 个 View");

        var view1 = rootHierarchy.Children[0];
        var view1Type = _entityManager.GetComponent<ControlTypeComponent>(view1);
        Assert.AreEqual("View1", view1Type.Name);

        var view2 = rootHierarchy.Children[1];
        var view2Type = _entityManager.GetComponent<ControlTypeComponent>(view2);
        Assert.AreEqual("View2", view2Type.Name);
    }

    [TestMethod]
    public void FullPipeline_Reconfigure_ShouldReplaceAllEntities()
    {
        var config1 = new UIConfiguration
        {
            Form = new FormConfig { Title = "First" },
            Views = new List<ViewConfig>
            {
                new()
                {
                    Name = "View1",
                    Controls = new List<ControlConfig>
                    {
                        new() { Type = "button", Name = "btn1" }
                    }
                }
            }
        };

        var config2 = new UIConfiguration
        {
            Form = new FormConfig { Title = "Second" },
            Views = new List<ViewConfig>
            {
                new()
                {
                    Name = "View2",
                    Controls = new List<ControlConfig>
                    {
                        new() { Type = "label", Name = "lbl2" }
                    }
                }
            }
        };

        _configToEntitySystem.Execute(config1);
        var firstRoot = _configToEntitySystem.RootEntity;

        _configToEntitySystem.Execute(config2);
        var secondRoot = _configToEntitySystem.RootEntity;

        Assert.AreNotEqual(firstRoot, secondRoot, "重新配置应创建新的根 Entity");
        Assert.IsFalse(_entityManager.IsEntityValid(firstRoot), "旧的根 Entity 应已销毁");

        var rootType = _entityManager.GetComponent<ControlTypeComponent>(secondRoot);
        Assert.AreEqual("Second", rootType.Text);

        var rootHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(secondRoot);
        Assert.AreEqual(1, rootHierarchy.Children.Count);
        var viewEntity = rootHierarchy.Children[0];
        var viewType = _entityManager.GetComponent<ControlTypeComponent>(viewEntity);
        Assert.AreEqual("View2", viewType.Name);
    }
}
