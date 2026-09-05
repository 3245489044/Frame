#nullable enable

using System.Drawing;
using System.Windows.Forms;
using ECS.Bridge;
using ECS.Core;
using FrontEndDriven.ControlRender.Layout;
using FrontEndDriven.ECS.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using TConfig.ECS;
using TConfig.ECS.Components;

namespace FrontEndDriven.Tests;

[TestClass]
public class TabBasisResolverTests
{
    private static EntityManager CreateEntityManager(out ControlPool pool)
    {
        var registry = new StorageRegistry();
        var storage = new ECSStorage(new StorageCell("TabBasisResolverTests"), registry);
        var em = new EntityManager(storage, registry);
        pool = new ControlPool(em, new StorageCell("TabBasisResolverTests-pool"), registry);
        return em;
    }

    private static Entity CreateContainer(EntityManager em, string type, int w, int h, int left = 10, int top = 20)
    {
        var e = em.CreateEntity();
        em.AddComponent(e, new ControlTypeComponent { Type = type });
        em.AddComponent(e, new ContainerSizeComponent { Left = left, Top = top, Width = w, Height = h });
        return e;
    }

    [TestMethod]
    public void NormalContainer_ChildArea_EqualsParentContainerSize_Passthrough()
    {
        using var em = CreateEntityManager(out var pool);
        var parent = CreateContainer(em, "panel", 500, 400, left: 10, top: 20);
        var resolver = new ContainerBasisResolver(em, pool);

        Assert.IsTrue(resolver.TryResolveChildArea(parent, out var l, out var t, out var w, out var h),
            "普通容器应解析出可达成显示区");
        Assert.AreEqual(10, l); Assert.AreEqual(20, t);
        Assert.AreEqual(500, w); Assert.AreEqual(400, h);
    }

    [TestMethod]
    public void TabControl_ChildArea_SubtractsDisplayRectangleInsets_FromSynchronousParentSize()
    {
        using var em = CreateEntityManager(out var pool);
        var tabEntity = em.CreateEntity();
        em.AddComponent(tabEntity, new ControlTypeComponent { Type = "tabcontrol" });
        em.AddComponent(tabEntity, new ContainerSizeComponent { Left = 10, Top = 20, Width = 484, Height = 733 });

        var tc = new TabControl { Name = "tc", Size = new Size(484, 733) };
        tc.CreateControl();
        pool.Register(tabEntity, tc.Name, tc);

        var resolver = new ContainerBasisResolver(em, pool);
        Assert.IsTrue(resolver.TryResolveChildArea(tabEntity, out var l, out var t, out var w, out var h),
            "TabControl 应解析出扣 inset 后的可达成显示区");

        var dr = tc.DisplayRectangle;
        Assert.AreEqual(10 + dr.Left, l, "左 origin = 父区 Left + DisplayRectangle.Left");
        Assert.AreEqual(20 + dr.Top, t, "上 origin = 父区 Top + DisplayRectangle.Top");
        Assert.AreEqual(484, w + dr.Left + Math.Max(0, tc.ClientSize.Width - dr.Right),
            "宽度 = 父区宽 − (左inset + 右inset)");
        Assert.AreEqual(733, h + dr.Top + Math.Max(0, tc.ClientSize.Height - dr.Bottom),
            "高度 = 父区高 − (上inset + 下inset)");
    }

    [TestMethod]
    public void TabControl_PhysicalUnready_FallsBackToParentSize()
    {
        using var em = CreateEntityManager(out var pool);
        var tabEntity = CreateContainer(em, "tabcontrol", 484, 733);
        var resolver = new ContainerBasisResolver(em, pool);

        Assert.IsTrue(resolver.TryResolveChildArea(tabEntity, out _, out _, out var w, out var h));
        Assert.AreEqual(484, w); Assert.AreEqual(733, h);
    }
}