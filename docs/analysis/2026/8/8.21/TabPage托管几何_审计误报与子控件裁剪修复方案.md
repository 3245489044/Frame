# TabPage 尺寸审计误报与子控件裁剪 —「托管几何」修复方案

> 日期：2026-08-21 · 状态：已实施（详见「九、重构收敛：从托管几何缓存到统一同步父基准确认」）
> 触发：`AgentRuntimeUI.json` 渲染后连续输出 `SyncActualContainerSizes: 容器尺寸不一致` —— 6 个 TabPage（pageResult/pageRuntime/pageTasks/pageDoc/pageDb/pageConversation）全部报 ECS 484×733 vs 物理 476×705
> 关联：`Ports.cs` / `LayoutSystem.cs` / `TabPageProjector.cs` / `ControlProjectionStrategyRegistry.cs` / `RenderPipelineStages.cs` / `LayoutSubsystem.cs` / `PipelineStateComponents.cs` / `LayoutCoordinateResolver.cs` / `AgentRuntimeUI.json`

---

## 一、结论先行

这是**框架缺陷，不是配置错误**。

- 配置侧：6 个 tabpage 均为 `left:0 top:0 width:1 height:1`（填满所属 TabControl），完全一致且符合语义；
- 6 个 TabPage 偏移完全相同（-8 宽 / -28 高），证明是系统性平台开销，绝非逐条配置笔误；
- 根因：**ECS 布局引擎把 TabPage 当作普通容器，按其父 TabControl 的整个客户区（484×733）结算尺寸，未建模 TabControl「页签条 + 边框」的显示矩形（DisplayRectangle ≈ 476×705）开销**。

框架内部存在自相矛盾：投影层 [TabPageProjector](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/FrontEndDriven/WinForms/Applier/Projection/TabPageProjector.cs) 已声明「TabPage 坐标由宿主托管、不写像素」，审计层却仍按普通容器逐像素比对 → **必然误报**。

**比日志更严重的隐藏后果**：TabPage 内子控件按不可达的 484×733 布局，在物理 476×705 视口内**底部/右侧越界被裁剪**（如 `resultGroup` 底部约 13px）。只修审计不修布局会遗留真实渲染缺陷。

---

## 二、现象与证据

### 2.1 日志现象

```
SyncActualContainerSizes: 容器尺寸不一致 — entityId=60, controlName=pageResult, controlType=TabPage,
  ecsWidth=484, ecsHeight=733, actualWidth=476, actualHeight=705
  note=ExternalConsistency: ECS 布局为唯一真源，WinForms 实际尺寸仅审计不回写
…（pageRuntime/pageTasks/pageDoc/pageDb/pageConversation 完全同构）
```

6 条告警偏移恒定 `-8 / -28`，且 `note` 自述「仅审计不回写」——审计自身也确认物理值不回写，属于纯告警。

### 2.2 证据链

| # | 环节 | 现状 | 文件 |
|---|------|------|------|
| 1 | 配置 | tabpage `width:1 height:1` 填满 TabControl | `AgentRuntimeUI.json:286-294` |
| 2 | 布局层 | TabPage `ContainerSizeComponent` = 父 TabControl 客户区 484×733，未扣页签条+边框 | `LayoutSystem.cs:125-145` |
| 3 | 物理层 | TabPage 被 WinForms 托管在 `DisplayRectangle` = 476×705 | 平台行为 |
| 4 | 投影层 | 剥离 Width/Height/Left/Top，声明坐标由宿主托管、不写像素 | `TabPageProjector.cs:18-27` |
| 5 | 审计层 | 拿物理尺寸比对不可达的 ECS 值 → 恒 mismatch，计数入 `MismatchCount` | `Ports.cs:725-801` |

### 2.3 子控件裁剪（隐藏后果）

以 `pageResult` 内 `resultGroup`（[AgentRuntimeUI.json:296-305](file:///d:/Project/Program/WorkFlowPrj/FileDriven/DrivenConfig/JsonConfig/AgentRuntimeUI.json#L296-L305)，`top:0.02 height:0.96`）为例：

- ECS 布局基准：TabPage = 484×733
- groupbox 逻辑底边 = 0.02×733 + 0.96×733 ≈ **718**；右边缘 = (0.01+0.98)×484 ≈ **479**
- 物理 TabPage 视口：476×705 → 底部超 **13px**、右侧超 **3px**
- 经 [LayoutCoordinateResolver.ToParentRelative](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Share/LayoutAnalysis/UIInspector/LayoutCoordinateResolver.cs#L61-L72) 换算为 TabPage 内相对坐标后，越界部分被物理裁剪

> 说明：日志仅对「容器实体」告警，groupbox 自身 ECS 值与投影写入一致故不告警，但越界裁剪是布局基准错误导致的真实物理后果，审计发现不了——这是本方案必须同时修复布局的核心理由。

---

## 三、结构性根因（框架级，非单点）

### D1 布局模型缺「显示矩形」概念
`LayoutSystem` 对容器尺寸一律按「父客户区全尺寸」结算，没有区分「普通容器（客户区=可达成尺寸）」与「Tab-managed 容器（可达成尺寸=父 TabControl 的 DisplayRectangle）」。

### D2 审计预期与投影语义自相矛盾
投影层（TabPageProjector）剥离坐标键 = 「坐标由宿主托管，不比像素」；审计层（SyncActualContainerSizes）却仍比对 ECS 全客户区值。**同一控件在两条链路上的「尺寸真源」认知不一致**，是本次告警的直接来源。

### D3 物理托管真源没有进入 ECS
WinForms 的 `DisplayRectangle`（页签条+边框由主题/字体/DPI 决定，无法用常量建模）是 TabPage 尺寸的唯一物理真源，但框架从未把它捕获进 ECS 布局真源，导致 ECS 与物理结构性不可达。

### D4 修复只覆盖单点
现有 ComboBox DropDownList 豁免是「平台约束」特判；若继续对 TabPage 加第二个特判，是打补丁而非根治——同类托管容器（未来 TableLayoutPanel 单元格等）会再次踩坑。

---

## 四、修复设计：托管几何（ManagedGeometry）回写闭环

### 4.1 核心思想

把「平台托管可达成尺寸」提升为一等概念：挂载后把物理 `TabControl.DisplayRectangle` **回写进 ECS**（`ManagedGeometryComponent`），作为 TabPage 及其子树的**布局基准与审计预期**。一个机制同时消除：

1. 审计误报（ECS 真源收敛为物理可达成值 → ECS==物理）；
2. 子控件裁剪（TabPage 及子树按可达成尺寸 476×705 布局）。

与既有先例同一模式，属第三个收敛实例：

| 先例 | 机制 | 文件 |
|------|------|------|
| SplitterRatioComponent | 物理拖拽比例 → ECS 真源回写 | `SplitterRatioComponent.cs` |
| HostBasisComponent | 物理宿主客户区 → ECS 布局基准 | `HostBasisComponent.cs` |
| **ManagedGeometryComponent（本方案）** | **物理 DisplayRectangle → ECS 布局基准 + 审计预期** | 新增 |

### 4.2 分层改动点

#### ① 新组件 `ManagedGeometryComponent`
位置：`CsharpPrj/Operation/FrontEndDriven/ControlRender/Components/`（与现有组件同层）

```csharp
/// <summary>
/// 托管几何组件（真源：物理宿主托管的可达成尺寸）。
/// 挂在 tabcontrol 实体上，记录其物理 DisplayRectangle.Size——
/// TabPage 的布局基准与审计预期均取本组件，而非父客户区全尺寸。
/// </summary>
public struct ManagedGeometryComponent : IComponent
{
    public int Width;
    public int Height;
    /// <summary>已捕获（物理句柄就绪）；false 时布局/审计回退旧逻辑。</summary>
    public bool Valid;
}
```

#### ② 捕获器 `ManagedGeometryCaptureSystem`（物理→ECS 回写）
- **挂载点**：`RenderMountStage`（[RenderPipelineStages.cs:305-350](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderPipelineStages.cs#L305-L350)）内 `AttachRootToHost` 之后、`ReflowNow`（L342）之前；
- **遍历对象**：含 [TabContainerComponent](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/FrontEndDriven/ControlRender/Components/PipelineStateComponents.cs#L52-L55) 的实体（ECS 侧 TabControl 语义标记，避免字符串比对）；
- **捕获逻辑**：
  - `_controlPool` 取物理 TabControl；
  - `IsHandleCreated` 就绪 → 读 `DisplayRectangle.Size` → 写 `ManagedGeometryComponent`；
  - 未就绪 → 置 `Valid=false`，登记待捕获，由审计期兜底再捕（见 4.3）；
- **幂等**：已 `Valid` 且尺寸未变不重写（对齐 SplitterRatio 0.01 容差防循环）；
- **日志**：`ManagedGeometry_captured`（含 ecsWidth/ecsHeight → managedWidth/managedHeight）；
- **触发重排**：捕获后调用 `LayoutSubsystem.RecalculateScope(scope)`，使 TabPage 及子树按新基准重算。

#### ③ 布局基准（`LayoutSystem.TryGetParentContainerSize`）
[LayoutSystem.cs:285-368](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/FrontEndDriven/ControlRender/Systems/LayoutSystem.cs#L285-L368) 取父基准时新增分支：

- TabPage 的父实体（tabcontrol）有 `ManagedGeometryComponent.Valid` → 用 `Width/Height`（476×705）；
- 否则回退现有 `HostBasisComponent` / `ContainerSizeComponent` 逻辑（无物理句柄/未捕获时不改变现状）。

效果：TabPage `ContainerSizeComponent` 收敛为物理可达成值，其子控件按 476×705 布局 → 不再越界裁剪。

#### ④ 审计预期（`Ports.SyncActualContainerSizes`）
[Ports.cs:757-775](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/FrontEndDriven/Ports.cs#L757-L775) 对 Tab-managed 容器：

- **期望值来源切换**：TabPage 的期望尺寸取父 tabcontrol 的 `ManagedGeometryComponent`（而非自身 `ContainerSizeComponent`）；
- physical==managed → `SyncActualContainerSizes_managed_ok`（Debug 常态），**不计数**；
- physical≠managed → 仍计 mismatch（真实缺陷，如显示矩形被外部程序改动）；
- `Valid=false`（句柄未就绪）→ 回退现有逻辑，不误报不崩溃；
- ComboBox DropDownList 豁免**保留**（不同根因：字体约束，非托管矩形，勿合并）。

#### ⑤ 投影层（`TabPageProjector`）—— 保持不变
仍剥离坐标键。托管几何只作为「布局基准 + 审计预期」，**不改「不写像素 Bounds」的投影语义**，避免与 TabControl 托管冲突（TabPage 物理尺寸仍由 TabControl 裁决）。

### 4.3 兜底路径（句柄未就绪）

- 首渲 `DisplayRectangle` 依赖句柄与选中页，`IsHandleCreated=false` 时无法读取；
- 兜底：`RunMismatchAudit`（审计入口，管线完成后执行）前再次尝试捕获；捕获成功 → 写 `ManagedGeometryComponent` + `RecalculateScope` 重排一次；
- 仍失败 → `Valid=false`，布局/审计维持旧逻辑（回归安全）。

### 4.4 数据流总览

```
挂载(AttachRootToHost) → 捕获 DisplayRectangle → ManagedGeometryComponent
  → RecalculateScope 重排 → TabPage(476×705) 及子树按可达成尺寸布局 → 物理投影
  → 审计：TabPage 预期取 ManagedGeometry → ECS==物理 → 零误报
宿主 Resize → Reflow → 基准更新 → 重排 → 审计持续通过（无回跳）
```

---

## 五、实施步骤（逐步，每步含验证）

| 步骤 | 内容 | 验证 |
|------|------|------|
| 1 | 新增 `ManagedGeometryComponent` | 编译通过 |
| 2 | 新增捕获器并在 `RenderMountStage` 挂接（含幂等 + 待捕获登记） | 日志出现 `ManagedGeometry_captured` |
| 3 | `LayoutSystem.TryGetParentContainerSize` 加托管基准分支 | TabPage `ContainerSizeComponent` 收敛为 476×705 |
| 4 | `SyncActualContainerSizes` 审计预期取托管几何 | 不再输出 TabPage 的 `mismatch` |
| 5 | 审计期兜底捕获 + 一次重排 | 句柄延迟就绪场景不误报 |
| 6 | 回归：非 Tab 容器审计行为不变 | 无新增/消失的 mismatch |

---

## 六、验证标准（验收）

1. **编译 + 单测回归**：`LayoutCoordinateResolver` / `LayoutConvergenceReport` 等既有测试不回归；
2. **告警归零**：运行 `AgentRuntimeUI`，不再出现 TabPage 的 `SyncActualContainerSizes_mismatch`；新增 `ManagedGeometry_captured(476×705)` 与 `SyncActualContainerSizes_managed_ok`；
3. **MismatchCount 归 0**（或仅剩真实缺陷）；
4. **裁剪消除**：`resultGroup`（height 0.96）底部与 TabPage 底边距正常，不再被裁 13px；右边缘不裁；
5. **Resize 跟随**：宿主拉伸后 TabPage 及子控件随 DisplayRectangle 重排，审计持续通过、无尺寸回跳；
6. **无头/句柄未就绪**：回退旧逻辑，不崩溃不误报。

---

## 七、风险与回退

- **DisplayRectangle 时序**：依赖句柄与选中页 → 已设计兜底（审计期再捕 + 一次重排）；
- **额外重排成本**：挂载后重排与既有 `ReflowNow` 同级，成本可接受；
- **通用性**：未来其它托管容器（TableLayoutPanel 单元格等）→ 扩展「托管几何来源」注册即可，审计/布局自动遵循，无需再改审计或布局核心；
- **回退**：`ManagedGeometryComponent.Valid=false` 时全链路回退现状，方案可灰度/单作用域试点。

---

## 八、结论

本次告警是**框架对 Tab-managed 容器缺少「显示矩形/托管几何」建模**的结构性缺陷，非配置错误。采用「托管几何物理→ECS 回写」框架级修复，一个机制同时根治审计误报与子控件裁剪两类问题，并保持 ECS 布局唯一真源、投影不写像素、审计真实校验的既有架构原则。

---

## 九、重构收敛：从「托管几何缓存」到「统一同步父基准」

> 本节记录实施阶段的关键设计转变。原方案（四至七章）采用「物理 DisplayRectangle → 缓存组件回写」的异步路径，与外部容器「自上而下同步计算」构成**双轨基准**（TabPage 慢一帧）。实施中收敛为 **统一同步父基准模型**，消除了异步旁路。

### 9.1 收敛动机：双轨基准缺陷

- **外部容器**：Resize → reflow → `LayoutSystem` 同步自上而下推算出子区域；
- **原方案 TabPage**：物理 → `ManagedGeometryCaptureSystem` 写缓存 → 异步 `RecalculateScope` → 子控件更新。

两条链路用不同时机计算同一「父基准」，TabPage 总是慢一帧，且引入一写多读的缓存状态。

### 9.2 统一模型（已落地）

| # | 文件 | 角色 |
|---|------|------|
| 1 | `ControlRender/Layout/ContainerBasisResolver.cs`（新增） | 在 reflow 的**同步**通道内解析父容器可达成显示区：普通容器取 `ContainerSizeComponent`；`TabControl` 进一步扣 `DisplayRectangle` 的 left/top/right/bottom inset |
| 2 | `ControlRender/Systems/LayoutSystem.cs`（改） | `TryGetParentContainerSize` 优先走解析器派生基准，**移除对 `ManagedGeometryComponent` 的依赖** |
| 3 | `ControlRender/Systems/TabContentReflowSystem.cs`（新增，替换 `ManagedGeometryCaptureSystem`） | 订阅 `TabControl.Resize` + `SelectedIndexChanged`，仅转译为一次 `RequestReflow`，**不向任何几何缓存组件写入** |
| 4 | `Ports.cs`（改） | 审计预期取 `HostBasisComponent`（同步基准），移除托管几何审计分支 |
| 5 | `PipelineStages` / `PipelineCompositionFactory` / `LayoutSubsystem`（改） | 装配解析器与重排系统，移除 `ManagedGeometryCaptureSystem.Capture` 调用 |
| 6 | `ManagedGeometryComponent` / `ManagedGeometryCaptureSystem`（删除） | 异步缓存组件与捕获器整体移除 |

### 9.3 数据流（收敛后）

```
TabControl Resize / SelectedIndexChanged
  → TabContentReflowSystem → RequestReflow(scope)
  → LayoutSystem.TryGetParentContainerSize
  → ContainerBasisResolver.TryResolveChildArea  ← 同步读 DisplayRectangle inset
  → TabPage 以可达成尺寸布局 → 审计取同步基准 → 零误报
```

基准来源不再依赖「异步缓存 + 事件回写」，而是 reflow 内**同步推导**，与外部容器收敛到同一父基准模型，慢一帧缺陷消失。

### 9.4 验证

- 新增单测：`TabBasisResolverTests`（ContainerBasisResolver 的普通容器 / TabControl inset 分支）、`TabBasisTests`（TabPage 布局基准 + TabContentReflowSystem 生命周期）；
- `FrontEndDriven.Tests` 全部通过，无回归；
- 审计 `mismatch` 与子控件裁剪由同步基准根治，无缓存回跳。
