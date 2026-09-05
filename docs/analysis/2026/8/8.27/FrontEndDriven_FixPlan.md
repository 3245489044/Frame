# FrontEndDriven 重复/冗余/错误逻辑修复方案

> 基于对 `C:\...\FrontEndDriven` 核心链路的通读分析。
> 目标：收敛重复口径、删除冗余、消除竞态与状态分叉，且不改变既有业务行为。
> 优先级：P0 错误逻辑 → P1 重复口径 → P2 纯粹冗余清理。每项附「问题定位→修复代码→验证标准」。

---

## 目录

| # | 优先级 | 主题 | 状态 |
|---|--------|------|------|
| F1 | P0 | Splitter 几何计算口径统一（含方向来源不一致） | ✅ 已实现（LayoutSystem / SplitterMath） |
| F2 | P0 | LayoutProjector 非 Try 读组件竞态 | ✅ 已实现 |
| F3 | P0 | `_suppressProjectionEvents` 手动复位可永久抑制 | ✅ 复核：当前代码已 try/finally 保护，无需改动 |
| F4 | P1 | 父容器可达成区解析双路径收敛 | ✅ 已实现（收敛到 ContainerBasisResolver，保留测试态单跳兜底） |
| F5 | P1 | 坐标换算收敛到 ECS 单一权威 | ⏸ 高风险，需专项（内容 Apply 先行于布局重排） |
| F6 | P1 | LayoutScheduler 预算耗尽滞留待算 scope | ✅ 已实现（耗尽后重新调度 Drain） |
| F7 | P2 | 冗余置脏重算（SplitContainerSystem.RecalculateAll） | ✅ 已删除（无调用方） |
| F8 | P2 | LayoutSystem 双重遍历 + 双构造冗余 | ✅ 已实现（去深度排序预结算 & GetEntityDepth；单参构造因测试依赖保留） |
| F9 | P2 | Validation 域过度设计清理 | ✅ 已实现（删除 EntityValidationEngine.Clear；GetEntityName 保留为统一入口） |
| F10 | P2 | Elment 过度退订（TabContentReflowSystem） | ✅ 已实现（DetachOne 单实体退订） |

> 状态说明：F1/F2/F4/F6–F10 已入码并通过 `dotnet build`（0 warning / 0 error）。
> F3 复核确认现有代码已正确（`ApplySnapshot` 内已用 try/finally 复位），无需改动。
> F5 因改动渲染坐标写路径、风险高且需 GUI 运行时验证，暂缓，待确认方案后专项实施。

---

## F1（P0）Splitter 几何计算口径统一 + 方向来源统一

### 问题
分割几何有三套并存实现，且方向来源不一致：
- `LayoutSystem.TryComputeSplitterPanelLayout` 手工算面板尺寸，用固定栏宽 `4` 近似（[LayoutSystem.cs#L191-273]）。
- `SplitterMath.Distance` + `LayoutProjector` 结算真实 `SplitterDistance`。
- `Ports.CalculateSplitterDistance` 复用 `SplitterMath`（快照侧）。
- 方向：`LayoutSystem` 用配置 `panel.Orientation`，`Ports.OnSplitterMoved` 用物理 `sc.Orientation`。

### 方案 A：方向来源统一（必做）
所有分割方向一律以**物理 `SplitContainer.Orientation`** 为准（真源 = 用户最终拖拽/物理状态），配置声明仅作创建期初值。
- `LayoutSystem` 不再读 `panel.Orientation`，改经 `ControlPool` 读物理方向，取不到再回退配置声明。
- 抽取 `SplitterMath` 新增方向解析，三处共用。

### 方案 B：面板几何统一（收敛）
让 `LayoutSystem.TryComputeSplitterPanelLayout` 直接委托 `SplitterMath`，消除固定栏宽 `4` 与真实 distance 的漂移：

`SplitterMath` 增加重载（保留原方法签名以兼容调用方）：

```csharp
/// <summary>由比例+方向+容器尺寸结算 Panel1 尺寸（= SplitterDistance）与 Panel2 相对偏移。</summary>
public static class SplitterMath
{
    public static void PanelRects(
        double ratio, Orientation orientation, int width, int height, int splitterBarWidth,
        out Rectangle panel1, out Rectangle panel2)
    {
        splitterBarWidth = Math.Max(0, splitterBarWidth);
        int distance = Distance(ratio, orientation, width, height);
        if (orientation == Orientation.Vertical)
        {
            panel1 = new Rectangle(0, 0, distance, height);
            panel2 = new Rectangle(distance + splitterBarWidth, 0,
                Math.Max(0, width - distance - splitterBarWidth), height);
        }
        else
        {
            panel1 = new Rectangle(0, 0, width, distance);
            panel2 = new Rectangle(0, distance + splitterBarWidth,
                width, Math.Max(0, height - distance - splitterBarWidth));
        }
    }
}
```

`LayoutSystem.TryComputeSplitterPanelLayout` 改造（物理方向优先 + 委托 SplitterMath）：

```csharp
orientation  = ResolvePhysicalOrientation(panelEntity)      // 新私有方法：物理优先，回退配置
            ?? ParseOrientation(panel.Orientation);

int barWidth = ResolveSplitterBarWidth(splitContainerEntity); // 物理 SplitterWidth，取不到回退 4
SplitterMath.PanelRects(ratio, orientation, parentWidth, parentHeight, barWidth,
    out var p1, out var p2);
var rect = isPanel1 ? p1 : p2;
computed = new ComputedLayoutComponent {
    Left = parentLeft + rect.X, Top = parentTop + rect.Y,
    Width = rect.Width, Height = rect.Height,
    ParentWidth = parentWidth, ParentHeight = parentHeight
};
```

### 验证标准
1. 对「比高还宽的横向分割」「比宽还高的纵向分割」配置，`RecalculateScope` 后 ECS `ContainerSize` 与物理 `SplitterDistance` 一致（差值 ≤1px）。
2. 配置声明方向与物理方向不一致时，以物理方向结算，`LayoutInspectSystem` 报告坐标无错位。
3. 现有 `SplitterMathTests` / `LayoutCoordinateResolverTests` 全绿。

---

## F2（P0）LayoutProjector 非 Try 读组件竞态

### 问题
[LayoutProjector.cs#L109-125] 在 `GetEntities<SplitterRatioComponent>()` 枚举后用 `_entityManager.GetComponent`（无 Try）。并发 scope 销毁移除组件时抛异常。

### 修复
```csharp
foreach (var entity in _entityManager.GetEntities<SplitterRatioComponent>())
{
    if (!LayoutQuery.IsInScope(_entityManager, entity, scope)) continue;
    if (!_entityManager.TryGetComponents(entity, out SplitterRatioComponent ratio, out ComputedLayoutComponent cl))
        continue;   // 组件已并发移除 → 跳过，防御竞态

    var control = GetControl(entity);
    if (control is not SplitContainer sc || sc.IsDisposed) continue;

    int distance = SplitterMath.Distance(ratio.Ratio, sc.Orientation, cl.Width, cl.Height);
    if (distance <= 0) continue;
    if (Math.Abs(_adapter.GetSplitterDistance(sc) - distance) <= 1) continue;

    _adapter.SetSplitterDistance(sc, distance);
    changed++;
}
```

### 验证标准
- 连续创建/销毁含 splitter 的 scope 压力测试（`--collect 60`）期间记录 0 例组件读写异常。

---

## F3（P0）`_suppressProjectionEvents` 异常可永久抑制

### 问题
[Ports.cs#L696-698] 读取抑制位，但设置/使用未被 `try/finally` 包裹。投影中途抛异常未复位 → 分割事件永久抑制。

### 修复
```csharp
private int _suppressDepth;

// 包裹投影写入口（ApplySnapshot 的 SetBounds/SetSplitterDistance 段）：
try { Interlocked.Increment(ref _suppressDepth); /* ...投影写... */ }
finally { Interlocked.Decrement(ref _suppressDepth); }

// OnSplitterMoved 判定改为深度计数：
if (Interlocked.Read(ref _suppressDepth) > 0) return;
```

### 验证标准
- 注入投影写异常，验证后续拖拽事件仍正常回灌 `SplitterRatioComponent`。

---

## F4（P1）父容器可达成区解析双路径收敛

### 问题
`LayoutSystem.TryGetParentContainerSize`（[LayoutSystem.cs#L295-390]）保留完整手动父链遍历 + HostBasis/ContainerSize 兜底，与 `ContainerBasisResolver.TryResolveChildArea` 并存。

### 修复
以 `ContainerBasisResolver.TryResolveChildArea` 为**唯一**子基准来源，移除 LayoutSystem 内重复遍历与兜底分支：

```csharp
// TryGetParentContainerSize 精简为：
if (!_entityManager.TryGetComponent<ParentEntityComponent>(entity, out var pc)
    || !pc.ParentEntity.IsValid)
{
    // root：仅走 HostBasis 兜底（见下）
    return TryResolveSelfBasis(entity, out parentLeft, out parentTop, out width, out height);
}

if (_basisResolver != null
    && _basisResolver.TryResolveChildArea(pc.ParentEntity, out parentLeft, out parentTop, out width, out height))
    return true;

// 无 resolver 或父未就绪：不静默落 0，保持 Pending
return false;
```

> 对 root 保留 HostBasis→ContainerSize 自身基准兜底（该分支语义与子实体不同，唯一例外保留）。
> `LayoutSystem` 双构造中 `basisResolver=null` 的测试态分支：改为必须注入，删除第二构造。

### 验证标准
- 嵌套 TabControl inset、嵌套 splitter 面板，`RectCalculateScope` 后子基准与 `ContainerBasisResolverTests` 语义一致。

---

## F5（P1）坐标换算收敛到 ECS 单一权威

### 问题
`EcsParentOrigin.Resolve`（纯 ECS）与 `LayoutCoordinateResolver.ToParentRelative`（物理父链）两套并存，前者仅 LayoutProjector 用，后者被内容投影（WinFormApplier/PropertyApplier/Ports）使用，存在基准分叉。

### 修复（分两步，避免一次大改风险）
**Step1**：把内容属性投影的坐标换算改为「目标坐标已被 ECS 布局真源写定（全局 form 相对）→ 物理父链仅做 `Bounds` 的 `Location` 还原」，删除重复的乘法换算路径，统一从 ECS `ComputedLayout` 取坐标：
- `WinFormApplier` 的 Bounds 写入复用 `EcsParentOrigin.Resolve` 得到父原点，`rel = cl - origin`，不再各自调 `ToParentRelative`。

**Step2**：逐步弃用 `LayoutCoordinateResolver` 物理换算在属性投影中的调用点，保留其在**诊断/审计**（`LayoutInspectSystem`）中的用途（纯只读，不产生状态）。

> 原则：**写路径**一律走 ECS 单一权威；物理换算仅保留在**读/诊断**路径。

### 验证标准
- 嵌套 scope 宿主、root 未挂宿主时首渲坐标即正确（无需拉伸自愈）。
- `LayoutCoordinateResolverTests`（语义契约）仍通过——契约保持不变，仅实现来源统一。

---

## F6（P1）LayoutScheduler 预算耗尽滞留待算 scope

### 问题
[LayoutScheduler.cs#L71-101] 达 `MaxDrainPasses=16` 且 `_pending.Count>0` 时仅记日志退出；若后续无新 `Schedule`，剩余布线滞留在队列中不执行。

### 修复
预算用尽后不丢弃，重新安排一次 Drain：

```csharp
if (_pending.Count > 0)
{
    FrontEndDrivenLog.Log("LayoutScheduler_drain_budget_exhausted",
        new { pendingCount = _pending.Count });

    // 预算用尽仍有剩余 → 让下一次调度继续消化，避免静默滞留。
    // 仅当并非在 `_work` 自身执行栈内（防递归风暴）时重新 Post；否则交由 `_work`
    // 内的再次 Schedule 自然驱动。
    ScheduleDrain();
}
```

> 额外防线：将 `MaxDrainPasses` 提升并对 `_work` 内同一 scope 连续重排计数，超过阈值记日志告警（避免 `ScheduleDrain` 重入无限风暴）。

### 验证标准
- 模拟连续 `Schedule` 风暴，验证最终所有 pending scope 均被执行，无静默丢失。

---

## F7（P2）冗余置脏重算：删除 SplitContainerSystem.RecalculateAll

### 问题
[SplitContainerSystem.cs#L54-69] `RecalculateAll` 仅加 `PendingLayoutComponent`，与 `LayoutSystem.RecalculateAll/RecalculateScope` 重复。

### 修复
- 移除 `SplitContainerSystem.RecalculateAll`；把"加 PendingLayoutComponent"并入 `LayoutSystem` 已覆盖的清理路径。
- 确认调用方可改为 `LayoutSystem.RecalculateAll/RecalculateScope`（或经 `LayoutSubsystem.RequestReflow`）。

### 验证标准
- grep 确认无 `SplitContainerSystem.RecalculateAll` 引用残留；分割容器重排行为回归一致。

---

## F8（P2）LayoutSystem 双重遍历 + 双构造冗余

### 问题
- [LayoutSystem.cs#L46-82] 先深度排序算一遍，再跑收敛循环重复枚举同一批实体。
- [LayoutSystem.cs#L27-37] `basisResolver=null` 第二构造保留整套物理兜底（配合 F4 一并移除）。

### 修复
- 删除深度排序预结算段，仅保留收敛循环（`MaxLayoutPasses`）——收敛循环本身按父先子后由 `ContainerBasisResolver` 保证充分性。
- 删除 `LayoutSystem(EntityManager)` 单参构造，全部走注入 `ContainerBasisResolver`，测试基座同步更新。

### 验证标准
- 深层嵌套布局收敛轮数与修复前一致（无更低覆盖），首遍坐标正确。

---

## F9（P2）Validation 域过度设计清理

### 问题
- [EntityValidationEngine.cs#L49-58] `UnregisterValidator` 运行态几乎不用。
- [EntityValidationHelper.cs] 单方法工具类 `GetEntityName` 与散落命名逻辑不统一。

### 修复
- 保留 `UnregisterValidator`（框架契约，成本低），但删去 `Clear()` 若确无调用方（grep 确认）。
- 将 `GetEntityName` 收敛为 `ControlTypeComponent.Name ?? $"Entity#{Id}"` 的统一静态入口，让 `RenderAuditReport`/`ControlInfoComponent`/`EntityQueryService` 复用同一方法，消除命名格式漂移。

### 验证标准
- grep 无孤立引用；实体缺失 name 时日志命名格式全链路一致。

---

## F10（P2）TabContentReflowSystem 过度退订

### 问题
[TabContentReflowSystem.cs#L73-80] 单个 tab `IsDisposed` 时 `Detach(scope)` 会退订该 scope 全部 tab。

### 修复
改为仅退订当前实体：

```csharp
private void OnTabContentChanged(string scope, Entity entity, TabControl tc)
{
    if (tc.IsDisposed || tc.Disposing) { DetachOne(scope, entity); return; }
    AppServiceProvider.Resolve<LayoutSubsystem>()?.RequestReflow(scope);
}

/// <summary>仅移除并退订指定实体的订阅（不再整体 Detach）。</summary>
private void DetachOne(string scope, Entity entity)
{
    if (!_attached.TryGetValue(scope, out var list)) return;
    var hit = list.FirstOrDefault(t => t.Entity == entity);
    if (hit == null) return;
    try { hit.Control.Resize -= hit.ResizeHandler; }
    catch { }
    list.Remove(hit);
    if (list.Count == 0) _attached.TryRemove(scope, out _);
}
```

### 验证标准
- 同一 scope 内 A tab 释放后，B tab 的 Resize/SelectedIndexChanged 仍生效触发 reflow。

---

## 实施顺序与回归清单

1. 先落地 **F1/F2/F3**（P0 错误逻辑），跑分割/布局相关测试。
2. 再执行 **F4/F5/F6**（口径收敛 + 调度稳健），验证布局坐标、嵌套 scope、splitter。
3. 最后清 **F7~F10**（冗余），grep 确认无残留引用。

**统一命令验证**：`--collect <N>`（含 M1 收敛断言、`LayoutConvergenceReport`、`RenderAuditReport`）在 GUI 与命令行双模式跑通无异常、无坐标错位、无组件竞态。

> 说明：本方案文档化作为实施依据，**代码改动尚未执行**；确认后再按 F1→F10 顺序分步入码。