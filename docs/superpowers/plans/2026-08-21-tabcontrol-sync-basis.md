# TabControl 同步父基准统一建模 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 TabPage 的布局基准从「缓存组件 + 外部事件回写 + 异步重排」旁路，收敛到布局引擎统一同步的「父容器可达成显示区」模型，消灭 TabPage 比外部容器慢一拍的结构缺陷。

**Architecture:** 布局引擎在自顶向下同一趟计算内，经一个统一解析器 `ContainerBasisResolver.TryResolveChildArea(parentEntity)` 推导每个父容器提供给子控件的可达成显示区：普通容器 = 父自身 `ContainerSizeComponent`（同步下推，零额外跳）；TabControl = 父 `ContainerSizeComponent`（同步父链区域）扣减页签条+边框 inset。inset 在 reflow 执行时刻（UI 线程单消费者）从物理 `TabControl.DisplayRectangle` 的**偏移量**同步读取——offset 是相对稳定的物理属性（页签高/边框），不依赖会被「读旧尺寸」污染的缓存快照。真源就绪信号仍是 TabControl 自身 `Resize`，但它只负责触发一次该 scope 的 reflow，不再写缓存；基准在 reflow 内同步推导。这样 TabPage 与外部走同一条逐层下推链路，天然同步、零额外跳。

**Tech Stack:** C# / .NET WinForms / ECS（EntityManager、ControlPool）/ FrontEndDriven.ControlRender。

**关键术语（重构后状态映射）**
- 主链路（同步）：`Host Resize | TabControl.Resize → RequestReflow(scope) → LayoutScheduler.Drain（UI 线程单消费者）→ Reflow → ResolveBasis → RecalculateScope → 逐层下推 TryComputeEntityLayout（先父后子）→ Project`。
- 旁路（被消灭）：~~`TabControl.Resize → 写 ManagedGeometryComponent 缓存 → 再 RequestReflow 第二次异步排空`~~。

---

### Task 0：基线

**Files:**
- Test: `CentralTest/CSharpTest/Operation.Tests/FrontEndDriven.Tests/ManagedGeometryTests.cs`

- [ ] **Step 1: 确认当前测试通过（进入重构前的绿色基线）**

Run: `dotnet test CentralTest/CSharpTest/Operation.Tests/FrontEndDriven.Tests/FrontEndDriven.Tests.csproj --filter "FullyQualifiedName~FrontEndDriven.Tests.ManagedGeometryTests"`
Expected: 5 个测试全部 PASS（`Capture_TabControl_WithHandle_...` / `Capture_TabControl_MissingPhysicalControl_...` / `Layout_TabPage_UsesManagedGeometryAsBasis` / `Attach_Detach_...` / `Layout_TabPage_FollowsManagedGeometryUpdate_OnStretch`）。

- [ ] **Step 2: 全量 FrontEndDriven.Tests 跑一遍，记录绿色集合**

Run: `dotnet test CentralTest/CSharpTest/Operation.Tests/FrontEndDriven.Tests/FrontEndDriven.Tests.csproj`
Expected: 全部 PASS（若有既有失绿，记录到会话日志，重构完成后必须覆盖它们）。

---

### Task 1：新增统一同步解析器 `ContainerBasisResolver`

**Files:**
- Create: `CsharpPrj/Operation/FrontEndDriven/ControlRender/Layout/ContainerBasisResolver.cs`
- Test: `CentralTest/CSharpTest/Operation.Tests/FrontEndDriven.Tests/TabBasisResolverTests.cs`

- [ ] **Step 1: 先写失败测试（普通容器直通 + TabControl 扣 inset）**

```csharp
#nullable enable

using System.Drawing;
using System.Windows.Forms;
using ECS.Bridge;
using ECS.Core;
using FrontEndDriven.ControlRender.Layout;
using FrontEndDriven.ECS.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
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
        // 父链同步区域：484×733（与物理 TabControl 同宽高超尺寸无关——尺寸取父区，仅扣 inset）
        em.AddComponent(tabEntity, new ContainerSizeComponent { Left = 10, Top = 20, Width = 484, Height = 733 });

        var tc = new TabControl { Name = "tc", Size = new Size(484, 733) };
        tc.CreateControl();
        pool.Register(tabEntity, tc.Name, tc);

        var resolver = new ContainerBasisResolver(em, pool);
        Assert.IsTrue(resolver.TryResolveChildArea(tabEntity, out var l, out var t, out var w, out var h),
            "TabControl 应解析出扣 inset 后的可达成显示区");

        var dr = tc.DisplayRectangle;
        Assert.AreEqual(10 + dr.Left, l, "左 origin = 父区 Left + DisplayRectangle.Left");
        Assert.AreEqual(20 + dr.Top,  t, "上 origin = 父区 Top + DisplayRectangle.Top");
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
        // 不 Register 物理控件 → inset 回退 0，尺寸=父区（Resize 触发 reflow 后自愈）
        var resolver = new ContainerBasisResolver(em, pool);

        Assert.IsTrue(resolver.TryResolveChildArea(tabEntity, out _, out _, out var w, out var h));
        Assert.AreEqual(484, w); Assert.AreEqual(733, h);
    }
}
```

- [ ] **Step 2: 运行，确认失败（类型不存在）**

Run: `dotnet test CentralTest/CSharpTest/Operation.Tests/FrontEndDriven.Tests/FrontEndDriven.Tests.csproj --filter "FullyQualifiedName~TabBasisResolverTests"`
Expected: FAIL，`ContainerBasisResolver` 未定义。

- [ ] **Step 3: 最小实现**

Create `CsharpPrj/Operation/FrontEndDriven/ControlRender/Layout/ContainerBasisResolver.cs`:

```csharp
#nullable enable

using System;
using System.Windows.Forms;
using ECS.Bridge;
using ECS.Core;
using FrontEndDriven.ECS.Components;
using TConfig.ECS.Components;

namespace FrontEndDriven.ControlRender.Layout;

/// <summary>
/// 统一同步「父容器可达成显示区」解析器（重构：托管几何缓存旁路 → 主链路同步推导）。
/// 布局引擎在自顶向下同一趟计算内，为每个父容器解析提供给子控件的可达成显示区：
///   普通容器 → 父自身 ContainerSizeComponent（同步下推，零额外跳）；
///   TabControl → 父 ContainerSizeComponent（同步父链区域）扣减页签条+边框 inset。
/// inset 在 reflow 执行时刻从物理 TabControl.DisplayRectangle 的偏移量同步读取——offset 是
/// 相对稳定物理属性（页签高/边框），不依赖会被「读旧尺寸」污染的缓存快照；
/// 尺寸永远取同步父区，杜绝把滞后尺寸当基准。真源 signals 仍是 Resize，但只触发 reflow。
/// </summary>
public sealed class ContainerBasisResolver
{
    private readonly EntityManager _entityManager;
    private readonly ControlPool _controlPool;

    public ContainerBasisResolver(EntityManager entityManager, ControlPool controlPool)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _controlPool = controlPool ?? throw new ArgumentNullException(nameof(controlPool));
    }

    public static bool IsTabControlContainer(EntityManager em, Entity entity)
        => em.TryGetComponent<ControlTypeComponent>(entity, out var type)
           && string.Equals(type.Type, "tabcontrol", StringComparison.OrdinalIgnoreCase);

    /// <summary>解析父容器提供给子控件的可达成显示区（全局 form 相对坐标）。</summary>
    /// <returns>父容器才有可达成显示区；父未就绪（无有效 ContainerSize）返回 false。</returns>
    public bool TryResolveChildArea(Entity parentEntity, out int left, out int top, out int width, out int height)
    {
        left = 0; top = 0; width = 0; height = 0;

        if (!_entityManager.TryGetComponent<ContainerSizeComponent>(parentEntity, out var parent) || !parent.IsValid)
            return false;

        left = parent.Left;
        top = parent.Top;
        width = parent.Width;
        height = parent.Height;

        if (!IsTabControlContainer(_entityManager, parentEntity))
            return true;

        // TabControl：可达成显示区 = 父区扣减页签条+边框 inset。只读偏移量（相对稳定），尺寸取同步父区。
        // 物理控件缺失/句柄未就绪 → inset=0 回退父区；TabControl.Resize 触发 reflow 后自愈。
        if (_controlPool.GetControl(parentEntity) is not TabControl tc || !tc.IsHandleCreated)
            return true;

        var dr = tc.DisplayRectangle;
        int clientW = tc.ClientSize.Width;
        int clientH = tc.ClientSize.Height;

        int leftInset   = Math.Max(0, dr.Left);
        int topInset    = Math.Max(0, dr.Top);
        int rightInset  = Math.Max(0, clientW - dr.Right);
        int bottomInset = Math.Max(0, clientH - dr.Bottom);

        left = parent.Left + leftInset;
        top  = parent.Top + topInset;
        width  = Math.Max(0, parent.Width  - leftInset - rightInset);
        height = Math.Max(0, parent.Height - topInset  - bottomInset);
        return true;
    }
}
```

- [ ] **Step 4: 运行，确认通过**

Run: `dotnet test CentralTest/CSharpTest/Operation.Tests/FrontEndDriven.Tests/FrontEndDriven.Tests.csproj --filter "FullyQualifiedName~TabBasisResolverTests"`
Expected: 3 个测试全部 PASS。

- [ ] **Step 5: Commit**

```bash
git add CsharpPrj/Operation/FrontEndDriven/ControlRender/Layout/ContainerBasisResolver.cs \
        CentralTest/CSharpTest/Operation.Tests/FrontEndDriven.Tests/TabBasisResolverTests.cs
git commit -m "feat: 新增统一同步父容器可达成显示区解析器 ContainerBasisResolver"
```

---

### Task 2：LayoutSystem 收敛到同步基准确认（移除托管几何缓存分支）

**Files:**
- Modify: `CsharpPrj/Operation/FrontEndDriven/ControlRender/Systems/LayoutSystem.cs`
- Test: `CentralTest/CSharpTest/Operation.Tests/FrontEndDriven.Tests/ManagedGeometryTests.cs`（本任务改造的测试先不改，仅确认被 Task 4 接管前保存语义；此处新增复位测试在 Task 4 落地）

- [ ] **Step 1: 先写失败测试：TabPage 布局基准 = 父 tabcontrol 同步区扣 inset**

在 `ManagedGeometryTests.cs` 追加（临时保留，Task 4 会整体改写）：

```csharp
[TestMethod]
public void Layout_TabPage_UsesSynchronousParentBasisMinusInset()
{
    using var em = CreateEntityManager(out var pool);
    // 父 tabcontrol 同步区 484×733（不再依赖 ManagedGeometry 缓存）
    var tabControl = em.CreateEntity();
    em.AddComponent(tabControl, new ControlTypeComponent { Type = "tabcontrol" });
    em.AddComponent(tabControl, new ContainerSizeComponent { Left = 0, Top = 0, Width = 484, Height = 733 });

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
```

- [ ] **Step 2: 运行，确认失败**

Run: `dotnet test CentralTest/CSharpTest/Operation.Tests/FrontEndDriven.Tests/FrontEndDriven.Tests.csproj --filter "FullyQualifiedName~Layout_TabPage_UsesSynchronousParentBasisMinusInset"`
Expected: FAIL（编译错误：`LayoutSystem` 无带 resolver 的构造）。

- [ ] **Step 3: 改造 `LayoutSystem`**

修改构造签名（保留无 resolver 旧构造，测试态/无 ControlPool 场景回退）：

```csharp
private readonly ContainerBasisResolver? _basisResolver;

public LayoutSystem(EntityManager entityManager)
    : this(entityManager, null) { }

public LayoutSystem(EntityManager entityManager, ContainerBasisResolver? basisResolver)
{
    _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
    _basisResolver = basisResolver;
}
```

重写 `TryGetParentContainerSize` 的非 root 分支：删掉 `ManagedGeometryComponent` 缓存读取（原 L336-351 块），改为优先走统一解析器（普通容器直通、TabControl 自动扣 inset），保留 `HostBasisComponent` 优先（scope root）、外部容器父链兜底：

```csharp
var current = entity;
int depth = 0;
while (depth < MaxTraversalDepth)
{
    if (!_entityManager.TryGetComponent<ParentEntityComponent>(current, out var parentComp))
        break;

    var parentEntity = parentComp.ParentEntity;
    if (!parentEntity.IsValid)
        break;

    // 统一同步基准确认：父已结算（含 TabControl 的 ContainerSize，LayoutSystem 先父后子写入）时，
    // 经容器可达成显示区解析器得到子基准——普通容器直通、TabControl 自动扣页签条+边框 inset。
    // 完全取代旧「ManagedGeometryComponent 缓存 + Resize 事件回写」旁路：基准在布局计算内同步推导。
    if (_basisResolver != null && _basisResolver.TryResolveChildArea(parentEntity, out int cl, out int ct, out int cw, out int ch))
    {
        parentLeft = cl;
        parentTop = ct;
        width = cw;
        height = ch;
        return true;
    }

    if (_entityManager.TryGetComponent<HostBasisComponent>(parentEntity, out var basis) && basis.IsValid)
    {
        parentLeft = basis.Left;
        parentTop = basis.Top;
        width = basis.Width;
        height = basis.Height;
        return true;
    }

    if (_entityManager.TryGetComponent<ContainerSizeComponent>(parentEntity, out var containerSize) && containerSize.IsValid)
    {
        parentLeft = containerSize.Left;
        parentTop = containerSize.Top;
        width = containerSize.Width;
        height = containerSize.Height;
        return true;
    }

    if (IsContainerEntity(parentEntity) || IsSplitterPanelEntity(parentEntity))
        return false;

    current = parentEntity;
    depth++;
}

return false;
```

> 说明：`_basisResolver` 为 null（旧测试直构）时，解析器分支被跳过，行为与旧「ContainerSize/HostBasis 兜底」一致；只有注入 resolver 的路径含 TabControl inset 语义。`ContainerBasisResolver.TryResolveChildArea` 对普通容器返回父 `ContainerSizeComponent`，与下方兜底分支等价——可在后续任务把下方 ContainerSize 兜底也统一收口，但本任务保持最小改动。

- [ ] **Step 4: 运行，确认通过**

Run: `dotnet test CentralTest/CSharpTest/Operation.Tests/FrontEndDriven.Tests/FrontEndDriven.Tests.csproj --filter "FullyQualifiedName~Layout_TabPage_UsesSynchronousParentBasisMinusInset"`
Expected: PASS。

- [ ] **Step 5: 运行既有单测，确认无回归**

Run: `dotnet test CentralTest/CSharpTest/Operation.Tests/FrontEndDriven.Tests/FrontEndDriven.Tests.csproj --filter "FullyQualifiedName~ManagedGeometryTests|FullyQualifiedName~LayoutSystemTests"`
Expected: PASS（旧 ManagedGeometryTests 未注入 resolver，仍走旧 ContainerSize 兜底，2 个几何断言在无 inset 时等价——`CreateTabControl` 设 476×705 托管几何，但新路径若无 resolver 会落到父 ContainerSize 484×733，故 **两个旧断言会在 Task 2 末尾阶段性失绿，属预期**，Task 4 整体改写测试语义）。

- [ ] **Step 6: Commit**

```bash
git add CsharpPrj/Operation/FrontEndDriven/ControlRender/Systems/LayoutSystem.cs \
        CentralTest/CSharpTest/Operation.Tests/FrontEndDriven.Tests/ManagedGeometryTests.cs
git commit -m "refactor: LayoutSystem 基准确认收敛到统一可达成显示区解析器（移除托管几何缓存分支）"
```

---

### Task 3：`TabControl.Resize` 触发收敛为「仅重排」，不再写缓存

**Files:**
- Rename+Modify: `CsharpPrj/Operation/FrontEndDriven/ControlRender/Systems/ManagedGeometryCaptureSystem.cs` → `TabContentReflowSystem.cs`
- Modify: `CsharpPrj/Operation/FrontEndDriven/ControlRender/Layout/LayoutSubsystem.cs`

- [ ] **Step 1: 先写失败测试（重命名后类型存在）**

在 `ManagedGeometryTests.cs` 追加：

```csharp
[TestMethod]
public void Attach_Detach_AfterRepurpose_NoThrow()
{
    using var em = CreateEntityManager(out var pool);
    var tabEntity = em.CreateEntity();
    em.AddComponent(tabEntity, new TabContainerComponent { SelectedIndex = 0 });
    em.AddComponent(tabEntity, new ScopeComponent { Scope = "Main" });
    var tabControl = new TabControl { Name = "tc" };
    pool.Register(tabEntity, tabControl.Name, tabControl);

    var trigger = new FrontEndDriven.ECS.Systems.TabContentReflowSystem(em, pool);
    trigger.Attach("Main");
    trigger.Attach("Main"); // 幂等
    trigger.Detach("Main");
    trigger.Detach("Main"); // 幂等退订
    trigger.Attach("");
    trigger.Attach(null!);
    trigger.Detach(null!);
}
```

- [ ] **Step 2: 运行，确认失败（类型不存在）**

Run: `dotnet test CentralTest/CSharpTest/Operation.Tests/FrontEndDriven.Tests/FrontEndDriven.Tests.csproj --filter "FullyQualifiedName~Attach_Detach_AfterRepurpose"`
Expected: FAIL（`TabContentReflowSystem` 未定义）。

- [ ] **Step 3: 重排系统实现（删除全部缓存写入与 ManagedGeometry 依赖）**

将 `ManagedGeometryCaptureSystem.cs` 重命名为 `TabContentReflowSystem.cs`，类更名并清空缓存逻辑：

```csharp
#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using ECS.Bridge;
using ECS.Core;
using FrameworkAbstractions.ComponentRegistry;
using FrontEndDriven.ControlRender.Layout;
using FrontEndDriven.ECS.Components;

namespace FrontEndDriven.ECS.Systems;

/// <summary>
/// Tab 内容重排触发系统（重构后角色）：TabControl 被 WinForms 布局引擎更新尺寸后其自身
/// Resize 触发——这是「Tab 可达成显示区就绪」的真源信号。本系统只把该信号转译成一次该
/// scope 的 reflow 调度（RequestReflow），不再向任何缓存组件写几何。
/// 基准由 ContainerBasisResolver 在 reflow 内从物理 DisplayRectangle 偏移量同步推导。
/// 幂等：同实体不重复订阅。销毁路径 Detach 单一收口。
/// </summary>
public sealed class TabContentReflowSystem
{
    private readonly EntityManager _entityManager;
    private readonly ControlPool _controlPool;

    private readonly ConcurrentDictionary<string, List<AttachedTab>> _attached = new(StringComparer.OrdinalIgnoreCase);

    private sealed record AttachedTab(Entity Entity, TabControl Control, EventHandler Handler);

    public TabContentReflowSystem(EntityManager entityManager, ControlPool controlPool)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _controlPool = controlPool ?? throw new ArgumentNullException(nameof(controlPool));
    }

    public void Attach(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope)) return;

        var list = _attached.GetOrAdd(scope, _ => new List<AttachedTab>());
        foreach (var entity in _entityManager.GetEntities<TabContainerComponent>())
        {
            if (!LayoutQuery.IsInScope(_entityManager, entity, scope)) continue;
            if (list.Any(t => t.Entity == entity)) continue;
            if (_controlPool.GetControl(entity) is not TabControl tc || tc.IsDisposed) continue;

            var handler = new EventHandler((_, _) => OnTabContentChanged(scope, entity, tc));
            tc.Resize += handler;
            list.Add(new AttachedTab(entity, tc, handler));
        }
    }

    public void Detach(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope)) return;
        if (!_attached.TryRemove(scope, out var list)) return;
        foreach (var tab in list)
        {
            try { tab.Control.Resize -= tab.Handler; } catch { /* 控件已释放，退订可忽略 */ }
        }
    }

    private void OnTabContentChanged(string scope, Entity entity, TabControl tc)
    {
        if (tc.IsDisposed || tc.Disposing) { Detach(scope); return; }

        // 真源已就绪（TabControl 布局完成）→ 触发该 scope 一次 reflow；
        // 基准由 ContainerBasisResolver 在 reflow 内同步读取 DisplayRectangle 偏移量，无需写缓存。
        AppServiceProvider.Resolve<LayoutSubsystem>()?.RequestReflow(scope);
    }
}
```

> 注：本 Task 仅处理 `Resize` 主就绪信号。页签增删/`SelectedIndexChanged` 改变页签条高度（multiline/换行）时 inset 变化，作为后续增强在 Task 6 附带开启（`tc.SelectedIndexChanged +=` 同一 handler），不阻塞本次主链路收敛。

- [ ] **Step 4: 同步更新 `LayoutSubsystem` 引用**

Modify `CsharpPrj/Operation/FrontEndDriven/ControlRender/Layout/LayoutSubsystem.cs`:
- 把两处 `AppServiceProvider.Resolve<ManagedGeometryCaptureSystem>()?.Attach/Detach` 换成 `TabContentReflowSystem`。
- 同步更新方法上面的文档注释（不再提「托管几何真源回写」，改为「Tab 可达成区就绪触发重排」）。

- [ ] **Step 5: 更新装配注册（PipelineCompositionFactory）**

Modify `CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/PipelineCompositionFactory.cs`:
- 用 `TabContentReflowSystem` 替换 `ManagedGeometryCaptureSystem` 注册（L194），并把 `ContainerBasisResolver` 注入 `LayoutSystem`：

```csharp
var basisResolver = new ContainerBasisResolver(entityManager, controlPool);
var layoutSystem = new LayoutSystem(entityManager, basisResolver);
...
AppServiceProvider.Register(new FrontEndDriven.ECS.Systems.TabContentReflowSystem(entityManager, controlPool));
```

- [ ] **Step 6: 运行，确认失败测试+旧生命周期测试通过**

Run: `dotnet test CentralTest/CSharpTest/Operation.Tests/FrontEndDriven.Tests/FrontEndDriven.Tests.csproj --filter "FullyQualifiedName~Attach_Detach_AfterRepurpose|FullyQualifiedName~TabContentReflowSystem"`
Expected: PASS。同时确认 `ManagedGeometryCaptureSystem` 已在编译面消失（无旧引用残留）。

- [ ] **Step 7: Commit**

```bash
git add CsharpPrj/Operation/FrontEndDriven/ControlRender/Systems/TabContentReflowSystem.cs \
        CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/PipelineCompositionFactory.cs \
        CsharpPrj/Operation/FrontEndDriven/ControlRender/Layout/LayoutSubsystem.cs
git rm CsharpPrj/Operation/FrontEndDriven/ControlRender/Systems/ManagedGeometryCaptureSystem.cs
git commit -m "refactor: TabControl.Resize 触发收敛为仅重排调度，删除托管几何缓存写回"
```

---

### Task 4：删除 `ManagedGeometryComponent` 并改写审计/挂载/测试

**Files:**
- Delete: `CsharpPrj/Operation/FrontEndDriven/ControlRender/Components/ManagedGeometryComponent.cs`
- Modify: `CsharpPrj/Operation/FrontEndDriven/Ports.cs`
- Modify: `CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderPipelineStages.cs`
- Modify: `CentralTest/CSharpTest/Operation.Tests/FrontEndDriven.Tests/ManagedGeometryTests.cs`

- [ ] **Step 1: 审计改为直接比较 ECS 同步基准确认（删托管几何预期分支）**

Modify `Ports.cs`：
- 删除 `TryGetManagedExpected` 方法（原 L862-888）。
- 在 `SyncActualContainerSizes` 循环内删除 `managedExpected` 变量及其三个日志分支（L757-769、L781-797、L832-845），`expectedWidth/Height` 恒取 `current`（ECS `ContainerSizeComponent`）。重构后 TabPage 的 ECS `ContainerSizeComponent` 已是「同步父区扣 inset」的可达成值 = 物理 DisplayRectangle 区域，与 TabPage 物理 Bounds 一致，审计直接匹配，不再需要特别分支。

- [ ] **Step 2: 挂载点移除兜底捕获**

Modify `RenderPipelineStages.cs`：把 `AppServiceProvider.Resolve<ManagedGeometryCaptureSystem>()?.Capture(ctx.Scope);` 前一行连同调用删除（域名：托管几何已由布局同步推导，挂载后既有 `ReflowNow(ctx.Scope)` 读 inset 覆盖），保留 `layoutSubsystem?.ReflowNow(ctx.Scope);`。

- [ ] **Step 3: 整体改写测试为「同步基准确认」语义**

将 `ManagedGeometryTests.cs` 改写为 `TabBasisTests.cs`（删除旧文件，保留 Task 2/3 已加的断言，套上图斑语义）：
- `Layout_TabPage_UsesSynchronousParentBasisMinusInset`（同步父区扣 inset，无缓存）。
- `Layout_TabPage_FollowsSynchronousParentBasis_OnStretch`：更新 tabcontrol `ContainerSizeComponent` 到新同步区 → `layout.RecalculateAll()` → 断言 TabPage 跟随（**用同步父区替换旧「改 ManagedGeometry」测试**）。
- `Attach_Detach_AfterRepurpose_NoThrow`（保留）。
- 删除引用 `ManagedGeometryComponent` 的所有用例。

```csharp
[TestMethod]
public void Layout_TabPage_FollowsSynchronousParentBasis_OnStretch()
{
    using var em = CreateEntityManager(out var pool);
    var tabControl = em.CreateEntity();
    em.AddComponent(tabControl, new ControlTypeComponent { Type = "tabcontrol" });
    em.AddComponent(tabControl, new ContainerSizeComponent { Left = 0, Top = 0, Width = 484, Height = 733 });
    var tabPage = CreateTabPage(em, tabControl);

    var layout = new LayoutSystem(em, new ContainerBasisResolver(em, pool));
    layout.Update();
    Assert.AreEqual(733, em.GetComponent<ContainerSizeComponent>(tabPage).Height,
        "初始布局应收敛到同步父区扣 inset（无 inset 时≈父区高）");

    // 拉伸：同步父区更新 → reflow 重算 → TabPage 跟随（替代旧缓存命中测试）
    em.AddOrSetComponent(tabControl, new ContainerSizeComponent { Left = 0, Top = 0, Width = 600, Height = 800 });
    layout.RecalculateAll();
    var size = em.GetComponent<ContainerSizeComponent>(tabPage);
    Assert.AreEqual(600, size.Width,  "拉伸后同步父区更新 → TabPage 布局应跟随");
    Assert.AreEqual(800, size.Height, "拉伸后同步父区更新 → TabPage 布局应跟随");
}
```

> 注：两个拉伸用例断言取「无物理 inset」时的父区等价（测试未 `pool.Register` 物理 TabControl → resolver 回退 inset=0），数值为 476/705 附近的抽象——保留断言同步父区即跟随的核心契约；物理 inset 精确一致性由 GUI 回归覆盖。

- [ ] **Step 4: 删除组件并清理引用**

Delete `CsharpPrj/Operation/FrontEndDriven/ControlRender/Components/ManagedGeometryComponent.cs`。更新 `ManagedGeometryTests.cs` → 重命名为 `TabBasisTests.cs`（用 Write 重建同目录文件）。全仓 Grep `ManagedGeometryComponent|ManagedGeometryCaptureSystem` 确认无残留引用。

- [ ] **Step 5: 全量测试**

Run: `dotnet test CentralTest/CSharpTest/Operation.Tests/FrontEndDriven.Tests/FrontEndDriven.Tests.csproj`
Expected: 全部 PASS（含改名后 `TabBasisTests`、`TabBasisResolverTests`、`LayoutSystemTests`）。

- [ ] **Step 6: Compress/Commit**

```bash
git add CsharpPrj/Operation/FrontEndDriven/Ports.cs \
        CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderPipelineStages.cs \
        CentralTest/CSharpTest/Operation.Tests/FrontEndDriven.Tests/TabBasisTests.cs
git rm CsharpPrj/Operation/FrontEndDriven/ControlRender/Components/ManagedGeometryComponent.cs \
        CentralTest/CSharpTest/Operation.Tests/FrontEndDriven.Tests/ManagedGeometryTests.cs
git commit -m "refactor: 删除托管几何缓存组件，审计/挂载/测试收敛到同步基准确认"
```

---

### Task 5：端到端构建 + GUI 回归

**Files:**
- Build: 整个 UI 宿主工程（含 FrontEndDriven 依赖）

- [ ] **Step 1: 增量编译整套 CsharpPrj**

Run: `dotnet build CsharpPrj/FrontEndDriven.csproj`（确切 csproj 路径按仓库结构定位；若多层，`dotnet build CsharpPrj/Operation/FrontEndDriven/*.csproj`）
Expected: 0 error（确认 `using FrontEndDriven.ECS.Systems` 等无脏引用）。

- [ ] **Step 2: GUI 回归——尺寸变化、拉伸、Tab 聚焦不闪跳**

用既有 GUI 自动化（`agent_ui_sideload_diag` / 引擎脚本吞吐 EngineViewer.json）加载含 tabcontrol 的配置，验证：
1. 首渲：TabPage 及子控件无裁剪（不再越界）。
2. 拉伸宿主：TabPage 一次收敛到新尺寸（无「先旧后新两拍」；日志无 `ManagedGeometry_captured`，仅 `LayoutSubsystem_reflow` 单次 + TabControl.Resize 各自触发）。
3. 切 Tab / 增删页：页签条高度变化后 inset 更新、内容跟随（若已开启 `SelectedIndexChanged`）。
验证手段：`FrontEndDrivenLog` 观察 `LayoutSubsystem_reflow` 与 `SyncActualContainerSizes` 日志、`LayoutConvergenceReport.Report` 无反复 Pending。

- [ ] **Step 3: 回归旧布局（GroupBox/Panel/SplitContainer）**

拉伸/加载带 splitter、dock=Fill、比例布局的外部容器，断言布局与重构前一致（无回归）。

- [ ] **Step 4: Commit**

```bash
git add CsharpPrj/
git commit -m "chore: 端到端构建与 GUI 回归通过"
```

---

### Task 6：文档对齐 + inset 就绪信号增强（可选，单独 commit）

**Files:**
- Modify: `docs/analysis/2026/8/8.21/TabPage托管几何_审计误报与子控件裁剪修复方案.md`（或新增 `docs/superpowers/plans/2026-08-21-tabcontrol-sync-basis-refactor-result.md`）
- Modify: `CsharpPrj/Operation/FrontEndDriven/ControlRender/Systems/TabContentReflowSystem.cs`

- [ ] **Step 1: 开启页签条 inset 变化就绪信号**

Modify `TabContentReflowSystem.Attach`：为同一个 `tc` 追加 `tc.SelectedIndexChanged += handler;`（与 Resize 共用 `OnTabContentChanged`），`Detach` 对应退订。使页签条高度随选中页/增删变化（multiline、tab 换行）引起的 inset 更新也能驱动重排。

- [ ] **Step 2: 更新分析文档**

在既有修复方案文档末尾追加「重构收敛」段落：说明从「缓存组件 + 事件回写 + 二次异步排空」收敛为「统一同步父可达成显示区」；`ManagedGeometryComponent`/`ManagedGeometryCaptureSystem` 已删除；TabPage 基准 = 父区同一趟计算扣 inset。

- [ ] **Step 3: 跑相关单测**

Run: `dotnet test CentralTest/CSharpTest/Operation.Tests/FrontEndDriven.Tests/FrontEndDriven.Tests.csproj --filter "FullyQualifiedName~TabBasis|FullyQualifiedName~TabContentReflowSystem"`
Expected: PASS。

- [ ] **Step 4: Commit**

```bash
git add docs/ CsharpPrj/Operation/FrontEndDriven/ControlRender/Systems/TabContentReflowSystem.cs
git commit -m "feat: TabContentReflowSystem 增加 SelectedIndexChanged 就绪信号，更新分析文档"
```

---

## Self-Review

**1. Spec 覆盖（对照用户根因结论）**
- “托管几何采用缓存组件+事件回写+异步重排旁路”→ Task 3 删除缓存写回、Task 4 删除组件；✓
- “外部同步逐层计算 vs TabPage 慢一拍”→ Task 2 把基准确认收进 `TryGetParentContainerSize` 同步主干；✓
- “把 TabControl 显示矩形纳入统一父基准（客户区−页签条）”→ Task 1 `ContainerBasisResolver` 普通容器=客户区、TabControl=扣 inset；✓
- “消灭外部事件回写这一拍”→ Task 3 `Resize` 仅触发重排，基准在 reflow 内读偏移量推导；✓
- 审计/挂载/测试旧缓存语义 → Task 4 收敛；✓
- 附带的页签增删 inset 变化 → Task 6（增强项）。

**2. 占位符扫描**：所有代码步骤均给出可编译实现与测试体；命令均带可运行项目路径与 filter；无 TBD，无“类似 Task N”。

**3. 类型一致性**
- `ContainerBasisResolver.TryResolveChildArea(Entity,int,int,int,int)` 全计划同签名（Task 1 定义、Task 2/4 引用）。
- `LayoutSystem(EntityManager, ContainerBasisResolver?)` 构造：Task 2 定义，Task 2 测试、Task 4 测试、Factory 注入一致。
- `TabContentReflowSystem.Attach/Detach(string)`：Task 3 定义并替换 LayoutSubsystem/Factory 引用，Task 4 测试沿用。
- 删除名 `ManagedGeometryComponent`/`ManagedGeometryCaptureSystem`：Task 4 全仓 Grep 确认无残留。
- `ContainerSizeComponent` 字段命名 `Width/Height/Left/Top` 与现有实现一致。

---

## 执行交接

计划已保存至 `docs/superpowers/plans/2026-08-21-tabcontrol-sync-basis.md`。两种执行方式：

1. **Subagent-Driven（推荐）** —— 每个 Task 派发独立子代理，任务间我做两段式审查，快速迭代。
2. **Inline 执行** —— 本会话内用 executing-plans 批量执行，带检查点审查。

**选哪个？**