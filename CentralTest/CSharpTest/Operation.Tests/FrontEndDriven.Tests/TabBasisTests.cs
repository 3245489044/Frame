#nullable enable

using System.Drawing;
using System.Windows.Forms;
using ECS.Bridge;
using ECS.Core;
using FrontEndDriven.ControlRender.Layout;
using FrontEndDriven.ECS.Components;
using FrontEndDriven.ECS.Systems;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using TConfig.ECS;
using TConfig.ECS.Components;

namespace FrontEndDriven.Tests;

/// <summary>
/// TabPage 统一同步基准确认测试（重构：托管几何缓存旁路 → 父可达成显示区同步推导）。
/// TabPage 基准 = 父 tabcontrol 的同步 ContainerSizeComponent 扣页签条+边框 inset，
/// 无中间缓存组件、无二次异步排空。覆盖：扣 inset、拉伸跟随同步父区、生命周期接线。
/// </summary>
[TestClass]
public class TabBasisTests
{
    private static EntityManager CreateEntityManager(out ControlPool pool)
    {
        var registry = new StorageRegistry();
        var storage = new ECSStorage(new StorageCell("TabBasisTests"), registry);
        var em = new EntityManager(storage, registry);
        pool = new ControlPool(em, new StorageCell("TabBasisTests-pool"), registry);
        return em;
    }

    /// <summary>构造 tabcontrol 实体：容器 + 同步父区 ContainerSize（布局引擎先父后子写入）。</summary>
    private static Entity CreateTabControl(EntityManager em, int parentW, int parentH)
    {
        var entity = em.CreateEntity();
        em.AddComponent(entity, new ControlTypeComponent { Type = "tabcontrol" });
        em.AddComponent(entity, new ContainerSizeComponent { Left = 0, Top = 0, Width = parentW, Height = parentH });
        return entity;
    }

    /// <summary>构造 tabpage 实体：父=tabcontrol，比例 width:1 height:1（填满所属 TabControl）。</summary>
    private static Entity CreateTabPage(EntityManager em, Entity parent)
    {
        var entity = em.CreateEntity();
        em.AddComponent(entity, new ControlTypeComponent { Type = "tabpage" });
        em.AddComponent(entity, new ParentEntityComponent { ParentEntity = parent });
        em.AddComponent(entity, new ProportionalLayoutComponent { Left = 0, Top = 0, Width = 1, Height = 1 });
        em.AddComponent(entity, new PendingLayoutComponent());
        return entity;
    }

    [TestMethod]
    public void Layout_TabPage_UsesSynchronousParentBasisMinusInset()
    {
        using var em = CreateEntityManager(out var pool);
        // 父 tabcontrol 同步区 484×733（不再依赖 ManagedGeometry 缓存）
        var tabControl = CreateTabControl(em, 484, 733);

        var tc = new TabControl { Name = "tc", Size = new Size(484, 733) };
        tc.CreateControl();
        pool.Register(tabControl, tc.Name, tc);

        var tabPage = CreateTabPage(em, tabControl);
        var layout = new LayoutSystem(em, new ContainerBasisResolver(em, pool));
        layout.Update();

        var size = em.GetComponent<ContainerSizeComponent>(tabPage);
        var dr = tc.DisplayRectangle;
        Assert.AreEqual(484 - dr.Left - Math.Max(0, tc.ClientSize.Width - dr.Right), size.Width,
            "TabPage 基准宽度 = 父同步区 − 页签条左右 inset");
        Assert.AreEqual(733 - dr.Top - Math.Max(0, tc.ClientSize.Height - dr.Bottom), size.Height,
            "TabPage 基准高度 = 父同步区 − 页签条上下 inset");
    }

    [TestMethod]
    public void Layout_TabPage_FollowsSynchronousParentBasis_OnStretch()
    {
        using var em = CreateEntityManager(out var pool);
        var tabControl = CreateTabControl(em, 484, 733);
        var tabPage = CreateTabPage(em, tabControl);

        var layout = new LayoutSystem(em, new ContainerBasisResolver(em, pool));
        layout.Update();
        Assert.AreEqual(733, em.GetComponent<ContainerSizeComponent>(tabPage).Height,
            "初始布局应收敛到同步父区（无物理 inset 回退父区高）");

        // 拉伸：同步父区更新 → RecalculateAll 重算 → TabPage 跟随（替代旧缓存命中测试）
        em.AddOrSetComponent(tabControl, new ContainerSizeComponent { Left = 0, Top = 0, Width = 600, Height = 800 });
        layout.RecalculateAll();
        var size = em.GetComponent<ContainerSizeComponent>(tabPage);
        Assert.AreEqual(600, size.Width,  "拉伸后同步父区更新 → TabPage 布局应跟随");
        Assert.AreEqual(800, size.Height, "拉伸后同步父区更新 → TabPage 布局应跟随");
    }

    [TestMethod]
    public void TabContentReflowSystem_AttachDetach_NoThrow()
    {
        using var em = CreateEntityManager(out var pool);
        var tabEntity = em.CreateEntity();
        em.AddComponent(tabEntity, new TabContainerComponent { SelectedIndex = 0 });
        em.AddComponent(tabEntity, new ScopeComponent { Scope = "Main" });
        var tabControl = new TabControl { Name = "tc" };
        pool.Register(tabEntity, tabControl.Name, tabControl);

        var trigger = new TabContentReflowSystem(em, pool);
        trigger.Attach("Main");
        trigger.Attach("Main"); // 幂等（同实体不重复订阅）
        trigger.Detach("Main");
        trigger.Detach("Main"); // 幂等退订
        trigger.Attach("");
        trigger.Attach(null!);
        trigger.Detach(null!);
    }
}