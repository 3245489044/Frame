
using ECS.Core;
using ECS.Systems;
using ECS.Tests.Mocks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TConfig.ContextFrame.Abstractions;
using TConfig.ECS;
using TConfig.ECS.Components;

namespace ECS.Tests.SystemTests;

[TestClass]
public class StyleSystemTests
{
    private IECSStorage _storage = null!;
    private IStorageRegistry _registry = null!;
    private EntityManager _entityManager = null!;
    private StyleSystem _styleSystem = null!;

    [TestInitialize]
    public void Setup()
    {
        _storage = new MockECSStorage();
        _registry = new MockStorageRegistry();
        _entityManager = new EntityManager(_storage, _registry);
        _styleSystem = new StyleSystem(_entityManager);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _entityManager.Dispose();
    }

    private Entity CreateControl()
    {
        var entity = _entityManager.CreateEntity();
        _entityManager.AddComponent(entity, new ControlTypeComponent { Type = "button", Name = "btn" });
        _entityManager.AddComponent(entity, new ControlStateComponent());
        return entity;
    }

    [TestMethod]
    public void Update_NoStyleComponents_ShouldApplyDefaultStyle()
    {
        var entity = CreateControl();

        _styleSystem.Update();

        Assert.IsTrue(_entityManager.HasComponent<MergedStyleComponent>(entity));
        var merged = _entityManager.GetComponent<MergedStyleComponent>(entity);
        Assert.AreEqual("Control", merged.BackColor);
        Assert.AreEqual("ControlText", merged.ForeColor);
        Assert.AreEqual("#CCCCCC", merged.BorderColor);
        Assert.AreEqual(1, merged.BorderWidth);
        Assert.AreEqual("Microsoft Sans Serif", merged.FontFamily);
        Assert.AreEqual(9f, merged.FontSize);
    }

    [TestMethod]
    public void Update_WithColorStyle_ShouldOverrideDefaults()
    {
        var entity = CreateControl();
        _entityManager.AddComponent(entity, new ColorStyleComponent
        {
            BackColor = "#FF0000",
            ForeColor = "#00FF00",
            BorderColor = "#0000FF",
            BorderWidth = 2
        });

        _styleSystem.Update();

        var merged = _entityManager.GetComponent<MergedStyleComponent>(entity);
        Assert.AreEqual("#FF0000", merged.BackColor);
        Assert.AreEqual("#00FF00", merged.ForeColor);
        Assert.AreEqual("#0000FF", merged.BorderColor);
        Assert.AreEqual(2, merged.BorderWidth);
    }

    [TestMethod]
    public void Update_WithFontStyle_ShouldOverrideDefaults()
    {
        var entity = CreateControl();
        _entityManager.AddComponent(entity, new FontStyleComponent
        {
            FontFamily = "Arial",
            FontSize = 14f,
            Bold = true,
            Italic = true,
            Underline = false
        });

        _styleSystem.Update();

        var merged = _entityManager.GetComponent<MergedStyleComponent>(entity);
        Assert.AreEqual("Arial", merged.FontFamily);
        Assert.AreEqual(14f, merged.FontSize);
        Assert.IsTrue(merged.Bold);
        Assert.IsTrue(merged.Italic);
        Assert.IsFalse(merged.Underline);
    }

    [TestMethod]
    public void Update_WithStyleRef_ShouldResolveAndApply()
    {
        var entity = CreateControl();
        _entityManager.AddComponent(entity, new StyleRefComponent
        {
            StyleRef = "PrimaryButton"
        });

        _styleSystem.StyleResolveRequested += (styleRef) =>
        {
            if (styleRef == "PrimaryButton")
            {
                return (
                    new ColorStyleComponent
                    {
                        BackColor = "#0078D4",
                        ForeColor = "#FFFFFF",
                        BorderColor = "#005A9E",
                        BorderWidth = 1
                    },
                    new FontStyleComponent
                    {
                        FontFamily = "Segoe UI",
                        FontSize = 10f,
                        Bold = false,
                        Italic = false
                    }
                );
            }
            return (null, null);
        };

        _styleSystem.Update();

        var merged = _entityManager.GetComponent<MergedStyleComponent>(entity);
        Assert.AreEqual("#0078D4", merged.BackColor);
        Assert.AreEqual("#FFFFFF", merged.ForeColor);
        Assert.AreEqual("#005A9E", merged.BorderColor);
        Assert.AreEqual("Segoe UI", merged.FontFamily);
        Assert.AreEqual(10f, merged.FontSize);
    }

    [TestMethod]
    public void Update_InlineStyleOverridesStyleRef()
    {
        var entity = CreateControl();
        _entityManager.AddComponent(entity, new StyleRefComponent { StyleRef = "BaseStyle" });
        _entityManager.AddComponent(entity, new ColorStyleComponent
        {
            BackColor = "#INLINE",
            ForeColor = "#INLINE"
        });

        _styleSystem.StyleResolveRequested += (styleRef) =>
        {
            return (
                new ColorStyleComponent
                {
                    BackColor = "#REF",
                    ForeColor = "#REF",
                    BorderColor = "#REF",
                    BorderWidth = 1
                },
                null
            );
        };

        _styleSystem.Update();

        var merged = _entityManager.GetComponent<MergedStyleComponent>(entity);
        Assert.AreEqual("#INLINE", merged.BackColor, "内联样式应覆盖 StyleRef");
        Assert.AreEqual("#INLINE", merged.ForeColor, "内联样式应覆盖 StyleRef");
        Assert.AreEqual("#REF", merged.BorderColor, "StyleRef 中未被内联覆盖的字段应保留");
    }

    [TestMethod]
    public void Update_AlreadyApplied_ShouldSkip()
    {
        var entity = CreateControl();
        var state = _entityManager.GetComponent<ControlStateComponent>(entity);
        state.IsStyleApplied = true;
        _entityManager.SetComponent(entity, state);

        _styleSystem.Update();

        Assert.IsFalse(_entityManager.HasComponent<MergedStyleComponent>(entity),
            "已应用样式的 Entity 不应重新合并");
    }

    [TestMethod]
    public void Update_ShouldSetIsStyleApplied()
    {
        var entity = CreateControl();

        _styleSystem.Update();

        var state = _entityManager.GetComponent<ControlStateComponent>(entity);
        Assert.IsTrue(state.IsStyleApplied);
    }
}
