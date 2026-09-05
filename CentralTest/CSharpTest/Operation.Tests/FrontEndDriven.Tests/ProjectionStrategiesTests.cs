
using System.Collections.Generic;
using System.Windows.Forms;
using FrontEndDriven.Protocol;
using FrontEndDriven.WinForms.Applier;
using FrontEndDriven.WinForms.Applier.Projection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrontEndDriven.Tests;

/// <summary>
/// R3·投影策略注册表单测：策略解析（Dock/DropDownList → 专属策略；普通 → PixelProjector 兜底）
/// 与归一行为（Dock 移除坐标键 / DropDownList 移除 height / 有效尺寸保留坐标 / 无效尺寸移除坐标）。
/// </summary>
[TestClass]
public class ProjectionStrategiesTests
{
    private static Dictionary<string, object?> Props(int left = 10, int top = 20, int width = 100, int height = 30)
        => new()
        {
            [UIProtocol.Prop_Left] = left,
            [UIProtocol.Prop_Top] = top,
            [UIProtocol.Prop_Width] = width,
            [UIProtocol.Prop_Height] = height,
            [UIProtocol.Prop_Text] = "label",
            [UIProtocol.Prop_Visible] = true,
        };

    [TestMethod]
    public void Resolve_OrdinaryControl_ReturnsPixelProjector()
    {
        var registry = ControlProjectionStrategyRegistry.CreateDefault();
        var desc = new ControlDescriptor { Type = "Button" };

        var strategy = registry.Resolve(desc, new Button());

        Assert.IsInstanceOfType<PixelProjector>(strategy);
    }

    [TestMethod]
    public void Resolve_DockIntent_ReturnsDockProjector()
    {
        var registry = ControlProjectionStrategyRegistry.CreateDefault();
        var desc = new ControlDescriptor { Type = "Panel", HasDock = true, Dock = "Fill" };

        var strategy = registry.Resolve(desc, new Panel());

        Assert.IsInstanceOfType<DockProjector>(strategy);
    }

    [TestMethod]
    public void Resolve_PhysicalDock_ReturnsDockProjector()
    {
        var registry = ControlProjectionStrategyRegistry.CreateDefault();
        var panel = new Panel { Dock = DockStyle.Fill };

        var strategy = registry.Resolve(new ControlDescriptor(), panel);

        Assert.IsInstanceOfType<DockProjector>(strategy);
    }

    [TestMethod]
    public void Resolve_DropDownListComboBox_ReturnsPlatformConstrainedProjector()
    {
        var registry = ControlProjectionStrategyRegistry.CreateDefault();
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };

        var strategy = registry.Resolve(new ControlDescriptor(), combo);

        Assert.IsInstanceOfType<PlatformConstrainedProjector>(strategy);
    }

    [TestMethod]
    public void DockProjector_Normalize_RemovesCoordinateKeys()
    {
        var strategy = new DockProjector();

        var normalized = strategy.Normalize(Props(), new Panel());

        Assert.IsFalse(normalized.ContainsKey(UIProtocol.Prop_Left));
        Assert.IsFalse(normalized.ContainsKey(UIProtocol.Prop_Top));
        Assert.IsFalse(normalized.ContainsKey(UIProtocol.Prop_Width));
        Assert.IsFalse(normalized.ContainsKey(UIProtocol.Prop_Height));
        Assert.AreEqual("label", normalized[UIProtocol.Prop_Text]);
    }

    [TestMethod]
    public void PlatformConstrainedProjector_Normalize_RemovesOnlyHeight()
    {
        var strategy = new PlatformConstrainedProjector();
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };

        var normalized = strategy.Normalize(Props(), combo);

        Assert.IsFalse(normalized.ContainsKey(UIProtocol.Prop_Height), "平台强制高度应移除");
        Assert.AreEqual(10, normalized[UIProtocol.Prop_Left], "Left 仍参与快照比较");
        Assert.AreEqual(20, normalized[UIProtocol.Prop_Top]);
        Assert.AreEqual(100, normalized[UIProtocol.Prop_Width]);
    }

    [TestMethod]
    public void PixelProjector_Normalize_KeepsCoordinates_ForValidSize()
    {
        var strategy = PixelProjector.Instance;

        var normalized = strategy.Normalize(Props(width: 100, height: 30), new Button());

        Assert.AreEqual(10, normalized[UIProtocol.Prop_Left]);
        Assert.AreEqual(20, normalized[UIProtocol.Prop_Top]);
        Assert.AreEqual(100, normalized[UIProtocol.Prop_Width]);
        Assert.AreEqual(30, normalized[UIProtocol.Prop_Height]);
    }

    [TestMethod]
    public void PixelProjector_Normalize_RemovesCoordinates_ForInvalidSize()
    {
        var strategy = PixelProjector.Instance;

        // root/非布局控件尺寸无效（Width/Height ≤ 0）→ 坐标不参与快照比较
        var normalized = strategy.Normalize(Props(width: 0, height: 0), new Button());

        Assert.IsFalse(normalized.ContainsKey(UIProtocol.Prop_Left));
        Assert.IsFalse(normalized.ContainsKey(UIProtocol.Prop_Width));
        Assert.AreEqual("label", normalized[UIProtocol.Prop_Text]);
    }

    [TestMethod]
    public void Resolve_TabPage_ReturnsTabPageProjector()
    {
        var registry = ControlProjectionStrategyRegistry.CreateDefault();
        var desc = new ControlDescriptor { Type = "tabpage" };

        var strategy = registry.Resolve(desc, new TabPage());

        Assert.IsInstanceOfType<TabPageProjector>(strategy);
    }

    [TestMethod]
    public void Resolve_PhysicalTabPage_ReturnsTabPageProjector()
    {
        var registry = ControlProjectionStrategyRegistry.CreateDefault();

        var strategy = registry.Resolve(new ControlDescriptor(), new TabPage());

        Assert.IsInstanceOfType<TabPageProjector>(strategy);
    }

    [TestMethod]
    public void TabPageProjector_Normalize_RemovesCoordinateAndVisibleKeys()
    {
        var strategy = new TabPageProjector();

        var normalized = strategy.Normalize(Props(left: 6, top: 69, width: 607, height: 733), new TabPage());

        // TabPage 位置/尺寸/可见性由所属 TabControl 托管 → 均不参与快照比较
        Assert.IsFalse(normalized.ContainsKey(UIProtocol.Prop_Left));
        Assert.IsFalse(normalized.ContainsKey(UIProtocol.Prop_Top));
        Assert.IsFalse(normalized.ContainsKey(UIProtocol.Prop_Width));
        Assert.IsFalse(normalized.ContainsKey(UIProtocol.Prop_Height));
        Assert.IsFalse(normalized.ContainsKey(UIProtocol.Prop_Visible));
        Assert.AreEqual("label", normalized[UIProtocol.Prop_Text]);
    }
}
