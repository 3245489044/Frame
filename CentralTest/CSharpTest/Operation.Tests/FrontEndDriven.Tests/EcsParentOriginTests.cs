#nullable enable

using System.Drawing;
using ECS.Core;
using FrontEndDriven.ControlRender.Layout;
using FrontEndDriven.ECS.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using TConfig.ECS;
using TConfig.ECS.Components;

namespace FrontEndDriven.Tests;

/// <summary>
/// ECS 父原点解析（单一权威）单测：物理 Bounds 的父基准唯一来源。
/// 契约：无 ECS 父（scope 根）→ HostBasisComponent（缺省 (0,0)）；有 ECS 父 → 父实体 ComputedLayout。
/// 与物理父链无关 → root 未挂宿主时坐标仍正确（首渲即正确，消除多重偏移）。
/// </summary>
[TestClass]
public class EcsParentOriginTests
{
    private static EntityManager CreateEntityManager()
    {
        var registry = new StorageRegistry();
        var storage = new ECSStorage(new StorageCell("EcsParentOriginTests"), registry);
        return new EntityManager(storage, registry);
    }

    [TestMethod]
    public void ScopeRoot_WithHostBasis_ReturnsBasisOrigin()
    {
        using var em = CreateEntityManager();
        var root = em.CreateEntity();
        em.AddComponent(root, new HostBasisComponent { Left = 1042, Top = 43, Width = 342, Height = 817 });

        var origin = EcsParentOrigin.Resolve(em, root);

        Assert.AreEqual(new Point(1042, 43), origin, "scope 根（无 ECS 父）原点应为宿主基准 HostBasisComponent");
    }

    [TestMethod]
    public void Child_WithEcsParentComputedLayout_ReturnsParentOrigin()
    {
        using var em = CreateEntityManager();
        var parent = em.CreateEntity();
        em.AddComponent(parent, new ComputedLayoutComponent { Left = 1042, Top = 43, Width = 340, Height = 815 });

        var child = em.CreateEntity();
        em.AddComponent(child, new ParentEntityComponent { ParentEntity = parent });

        var origin = EcsParentOrigin.Resolve(em, child);

        Assert.AreEqual(new Point(1042, 43), origin, "子实体原点应为父实体 ComputedLayout（全局 form 相对）");
    }

    [TestMethod]
    public void NoParent_NoHostBasis_ReturnsZero()
    {
        using var em = CreateEntityManager();
        var entity = em.CreateEntity();

        var origin = EcsParentOrigin.Resolve(em, entity);

        Assert.AreEqual(Point.Empty, origin, "既无 ECS 父也无宿主基准 → 原点 (0,0)");
    }

    [TestMethod]
    public void InvalidParent_ReturnsZeroOrHostBasis()
    {
        using var em = CreateEntityManager();
        var child = em.CreateEntity();
        em.AddComponent(child, new ParentEntityComponent { ParentEntity = Entity.Invalid });

        var origin = EcsParentOrigin.Resolve(em, child);

        Assert.AreEqual(Point.Empty, origin, "父实体无效 → 不解析父 ComputedLayout，回退 (0,0)");
    }
}
