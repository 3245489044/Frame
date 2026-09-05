
using System.Windows.Forms;
using FrontEndDriven.WinForms.Adapter;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrontEndDriven.Tests;

/// <summary>
/// Tab 容器语义单测：挂载端口把 TabControl→TabPage 确定性落入 TabPages 集合（非通用 Controls.Add）。
/// 选中态（ApplyTabSelection）逻辑极简且强耦合 GUI 句柄，由 GUI 回归（agent_ui_sideload_diag）覆盖。
/// </summary>
[TestClass]
public class TabContainerTests
{
    [TestMethod]
    public void Adapter_AddControl_TabPage_LandsInTabPagesCollection()
    {
        var adapter = new WinFormsControlAdapter();
        var tc = new TabControl { Name = "tc" };
        var page = new TabPage { Name = "tabRuntime" };

        adapter.AddControl(tc, page);

        Assert.IsTrue(tc.TabPages.Contains(page), "TabPage 应确定性进入 TabControl.TabPages 集合");
        Assert.AreEqual(0, tc.TabPages.IndexOf(page));
    }

    [TestMethod]
    public void Adapter_AddControl_SameTabPage_Twice_IsIdempotent()
    {
        var adapter = new WinFormsControlAdapter();
        var tc = new TabControl { Name = "tc" };
        var page = new TabPage { Name = "tabRuntime" };

        adapter.AddControl(tc, page);
        adapter.AddControl(tc, page);

        Assert.AreEqual(1, tc.TabPages.Count, "重复挂载同一 TabPage 应幂等（不重复入页）");
    }

    [TestMethod]
    public void Adapter_AddControl_MultipleTabPages_OrderPreserved()
    {
        var adapter = new WinFormsControlAdapter();
        var tc = new TabControl { Name = "tc" };
        var page1 = new TabPage { Name = "tabRuntime" };
        var page2 = new TabPage { Name = "tabPersist" };

        adapter.AddControl(tc, page1);
        adapter.AddControl(tc, page2);

        Assert.AreEqual(2, tc.TabPages.Count);
        Assert.AreEqual(0, tc.TabPages.IndexOf(page1));
        Assert.AreEqual(1, tc.TabPages.IndexOf(page2));
    }

    [TestMethod]
    public void Adapter_AddControl_PlainControl_StillUsesControlsCollection()
    {
        var adapter = new WinFormsControlAdapter();
        var parent = new Panel();
        var child = new Button();

        adapter.AddControl(parent, child);

        Assert.IsTrue(parent.Controls.Contains(child), "非 Tab 容器仍走通用 Controls.Add");
    }

    [TestMethod]
    public void Adapter_AddControl_TabPage_WithoutTabControlParent_ReturnsFalse()
    {
        var adapter = new WinFormsControlAdapter();
        var parent = new Panel();
        var page = new TabPage { Name = "misnested" };

        // M1 挂载成败契约：TabPage 挂到非 TabControl 父（配置误嵌套）→ 返回 false 且不入宿主，
        // 使挂载失败沿调用链上抛（而非静默 orphaning → 统计层假绿）。
        bool ok = adapter.AddControl(parent, page);

        Assert.IsFalse(ok, "TabPage 挂到非 TabControl 父应返回 false（不再静默 orphaning）");
        Assert.IsFalse(parent.Controls.Contains(page), "TabPage 不应被挂到 Panel.Controls");
    }

    [TestMethod]
    public void Adapter_Contains_RespectsHostCollection()
    {
        var adapter = new WinFormsControlAdapter();

        // TabControl → TabPages 集合判定（物理核验平台适配）
        var tc = new TabControl { Name = "tc" };
        var page = new TabPage { Name = "p" };
        adapter.AddControl(tc, page);
        Assert.IsTrue(adapter.Contains(tc, page), "TabPage 应被判定为已入 TabControl.TabPages");

        // 普通控件 → Controls 集合判定
        var panel = new Panel { Name = "panel" };
        var btn = new Button { Name = "btn" };
        adapter.AddControl(panel, btn);
        Assert.IsTrue(adapter.Contains(panel, btn), "普通控件应判定为已入 Panel.Controls");
        Assert.IsFalse(adapter.Contains(panel, page), "TabPage 未入 Panel 不应被判定为已挂载");
    }
}
