# FrontEndDriven 渲染子系统 ECS 基准重构方案

> **日期：** 2026-08-06
> **范围：** FrontEndDriven 渲染子系统（ECS World / RenderPipeline / RenderPort）
> **关联故障：** `math_functions_test.json` 全命令 PASS 但四个运算按钮不可见（"控件在树但屏幕不可见"）
> **关联文档：** `布局基准宿主对齐_容器内控件裁剪修复方案.md`（临时补丁，本方案替代其根治方向）
> **方向裁定：** 以 ECS 为唯一真源进行优化重构；逐步把 WinForms 承担的功能全部收编进 ECS 系统；WinForms 降级为最薄渲染端口（RenderPort）

---

## 一、现状与根因：为什么补丁无效

### 1.1 现状架构

```
Config(JSON) → ConfigDrivenControlFactory → ECS 实体(组件集)
   → RenderStageRunner 三阶段:
       Load      : 建实体树（ControlType/ProportionalLayout/ContainerSize/ComputedLayout/Parent/EventBindings/Scope/ControlState）
       Creation  : EcsCapabilityApplier 建物理控件入 ControlPool + LayoutSystem 自算像素 + SplitContainer + Style
       Render    : Collect(CsgEntryPoint 全量快照) → Apply(WinFormApplier 全量 SetBounds/Add)
                   → Mount(HostMountService root 挂宿主, 0×0 Dock=Fill 兜底) → Bind(EventBindingSystem)
```

渲染入口多轨：`Execute` / `ExecuteRenderStage` / `RecalculateAndRender` / `FunctionExecutedRenderBridge` / `SetSplitterChangedCallback`。

### 1.2 五个结构缺陷（互为因果）

| # | 结构缺陷 | 代码证据 | 后果 |
|---|----------|----------|------|
| ① | **双世界同步**：ECS 实体世界与物理 WinForms 树是两个世界，靠快照搬运拼凑 | `CsgEntryPoint.CollectSnapshot`（ECS→Descriptor）→ `WinFormApplier`（Descriptor→物理 SetBounds）；平行结构：ECS、ControlPool、ContainerRegistry、物理树 | 任何一环脱节即静默漂移；`SyncActualContainerSizes`/`SyncControlInfoComponent`/`OnSplitterMoved` 回写制造双真源 |
| ② | **全量快照重放，无差异更新** | `UIReconciler`（差异更新器）为悬空死代码（docs 7.31 记录"移除悬空 new UIReconciler，类保留作审计"）；每次渲染全量 SetBounds/Add | 布局基准/挂载时相/宿主尺寸任何偏差被静默固化，无收敛校验 |
| ③ | **布局引擎缺位**：弃用 WinForms 原生 Dock/Anchor，自算像素 | `LayoutSystem` 把比例换算成像素；root 0×0 靠 Dock=Fill 兜底；`RecalculateAll` 全量重算 | 宿主/窗口变化不自动重排；比例布局在窄宿主（342px）必然溢出 |
| ④ | **渲染无结果契约**：只输出"控件对象+Bounds"，无可见矩形/尺寸>0/Visible 断言 | `validate-mount` 仅查 `FindForm()!=null`；关键诊断（`HostMountService_RootMountedVisual`/`WinFormApplier_ApplyResult_summary`/`SyncActualContainerSizes_mismatch`）全 Debug 被过滤 | **"控件在树但屏幕不可见"可永久存在且日志全绿**——即本次故障症状 |
| ⑤ | **多入口多轨**：渲染多入口 + 事件双体系（MainForm 硬编码 + 配置绑定）+ 命令 fire-and-forget | `RenderPipelineComposition` L132-210；docs L4/RD5；实测 `Command failed: Pending` | 竞态、命令挂起、重载不稳定 |

### 1.3 为什么所有补丁无效

修布局基准、修挂载时相、修裁剪——都只是双世界同步的某一个面。**"可见性"从来不在这个范式的任何一环里被定义、被校验**：命令链按名查找 + `PerformClick`（不查可见）、validate 仅查 `FindForm()!=null`、渲染链只搬运 Bounds。补丁修一个裂缝，物理树在另一个裂缝里继续漂移。根治必须改变范式本身。

## 二、目标架构（ECS 基准）

### 2.1 架构图

```
                 Config (JSON) —— 格式不变，新增可选 dock/anchor 意图
                        │ 解析
                        ▼
   ┌─────────────── ECS World（唯一真源）───────────────┐
   │ DataLayer   : Entity + 组件集（状态/布局/事件/生命周期） │
   │ LayoutSystem: 布局意图(proportional/dock/anchor/fixed)│
   │               → ComputedLayout（相对宿主基准）        │
   │ EventSystem : 事件声明 → ECS 事件总线 → 函数链         │
   │ StateSystem : Text/Visible/Enabled/Value 全在 ECS     │
   │ Lifecycle   : ScopeLifecycle 状态机（挂载/销毁/重载）   │
   │ Query       : 实体树查询（按 scope/name/type）        │
   └──────────────────────┬──────────────────────────────┘
                          │ 渲染请求（scope+version，单队列）
                          ▼
              ┌── RenderPort（WinForms 薄适配）──┐
              │ 投影：ECS→物理控件（建/删/改）      │
              │ 应用：ComputedLayout → Bounds/Dock │
              │ 采集：物理交互 → ECS 事件（单向）   │
              └───────────────────────────────────┘
```

### 2.2 铁律（真源方向）

1. **状态只写 ECS**：Text/Visible/Enabled/Value/Bounds 一律以 ECS 组件为唯一事实。
2. **物理树只读投影**：WinForms 控件是 ECS 的渲染输出，不反向持有状态。
3. **交互单向回灌**：物理 Click/TextChanged 等经 RenderPort 采集端口转为 ECS 事件，GUI 操作与脚本命令走同一条 ECS 事件链。
4. **渲染后必做结果契约**：可见性断言（Bounds ⊆ 宿主客户区 && 尺寸>0 && Visible），失败显式 Warning + 一次自愈重渲染。

### 2.3 WinForms 功能迁移映射表

| # | 现状（物理树/WinForms 承担） | 迁移目标（ECS 承载） | 主要落点 |
|---|---|---|---|
| 1 | 控件创建 `ControlStrategyRegistry` 工厂 + `WinFormsOutputPort.CreateControl` | ECS 类型注册表 + RenderPort 创建端口 | `ControlStrategyRegistry` → `EcsControlTypeRegistry`；`WinFormsOutputPort` 收编 |
| 2 | 控件查找 `ControlFinder/ControlTraverser`（遍历物理树） | ECS 实体树查询 | `ControlFinder` → `EntityQueryService`；`ControlPool` → 投影索引（仅 RenderPort 维护） |
| 3 | 布局 `LayoutSystem` 自算像素 + WinForms Dock=Fill 兜底 | ECS 布局意图 → ComputedLayout（相对宿主基准） | `LayoutSystem` 重构 + 新增 `HostBasisSystem` |
| 4 | 事件 `EventBindingSystem` 绑物理控件事件；click 走 `PerformClick` | ECS 事件总线 + 输入采集端口 | 新增 `EcsEventBus`；`EventBindingSystem` 重构为订阅注册 |
| 5 | 状态 Text/Visible/Enabled/Value 在物理控件 + 回写 | `ControlStateComponent` 单一真源 | **删除全部物理→ECS 回写**；新增 `StateSystem` |
| 6 | 挂载/宿主 `HostMountService` + `ContainerRegistry`（多套挂载状态口径） | ECS 挂载状态机 + 宿主绑定组件 | 新增 `MountStateMachine`；`ContainerRegistry` 降级为只读宿主寻址表 |
| 7 | 生命周期 `EcsGate`/`ContainerProjector`/scope 销毁（多套） | ECS `ScopeLifecycleSystem`（单一状态机） | 新增 `ScopeLifecycleSystem`；多套销毁收敛 |
| 8 | 渲染应用 `WinFormApplier`/`PropertyApplier` 全量 SetBounds | RenderPort 差异投影（复活 `UIReconciler`） | `WinFormApplier` → `RenderPort` 重构 |
| 9 | 命令 `ClickSetGetSystems` 直接摸物理控件 | ECS 命令系统（操作组件→投影同步） | `ClickSetGetSystems` 重构 |
| 10 | 值访问 `IControlValueAccessor` 读写物理控件 | ECS 状态读写 | `ControlValueAccessor` → `EcsStateAccessor` |
| 11 | 分割容器 `SplitContainerSystem` 直操作物理 SplitContainer | ECS 组件 → RenderPort 同步 | `SplitContainerSystem` 重构 |
| 12 | 诊断 `layout positions/report` 走物理树 + Debug 被过滤 | ECS 查询 + Info 常开 + 可见性契约断言 | `LayoutInspectSystem` 改 ECS 口径 |

---

## 三、分阶段重构（M1→M6）

> 每阶段退出判据 = 可编译 + 全脚本回归通过 + 对应新增断言通过。每阶段独立提交，可回退。

### M1 · 状态真源单向化（先止血，立即消除双真源）

- **目标**：ECS 成为状态唯一真源；物理→ECS 回写全部消除。
- **迁移内容**：
  1. 定义完整 ECS 状态组件集（`ControlStateComponent` 扩展：Text/Visible/Enabled/Value/Bounds 全量承载）；
  2. 删除 3 处物理→ECS 回写：`SyncControlInfoComponent` 写回、`SyncActualContainerSizes` 写回、`OnSplitterMoved` 写 ECS——全部降级为**只读审计**（比对不一致即记录架构缺陷）；
  3. 新增 `HostBasisComponent`：scope 布局基准 = 宿主客户区（0×0 回退父面板），由宿主决议器统一计算，替代 `AlignLayoutBasisToHost` 的"渲染前校准"补丁；
  4. 新增 `LayoutConvergenceReport`：每次布局计算后断言"全部实体已取得 ComputedLayout 且尺寸>0"，缺失/0 尺寸实体显式 Warning。
- **代码落点**：`Ports.cs`（Sync*）、`LayoutSystem.cs`、`ConfigDrivenControlFactory.cs`、`RenderStageRunner.cs`。
- **验证**：全脚本回归 + 新增"ECS 状态 vs 物理状态只读一致性审计"——立即拿到"按钮不可见"时的真实数值基线。

### M2 · 布局意图化（消灭自算像素范式）

- **目标**：布局由"自算像素"转为"意图声明 + 原生引擎接管"。
- **迁移内容**：
  1. `layout` 声明扩展四类意图：`proportional`（现有）/`dock`/`anchor`/`fixed`（JSON 兼容，现有配置按 proportional 处理）；
  2. `HostBasisSystem` 决议宿主基准 → 写 root 的 `HostBasisComponent`；
  3. `LayoutSystem` 把意图解析为 `ComputedLayout`（像素，相对宿主基准），带收敛断言；
  4. RenderPort 应用时**优先映射为原生 Dock/Anchor 交 WinForms 布局引擎接管**（`proportional` 才计算像素），宿主尺寸变化 → 增量重算受影响子树（不再 `RecalculateAll` 全量）。
- **代码落点**：`LayoutSystem.cs`（重构）、新增 `HostBasisSystem`、`RenderPort`（原 `WinFormApplier`）、`LayoutValidator.cs`。
- **验证**：Math/MainLayout/WorkflowRunner 各配置截图基线 + 布局收敛断言；`math_functions_test` 按钮可见性断言。

### M3 · 事件与命令收编（修复"命令链与渲染链解耦"根因）

- **目标**：GUI 操作与脚本命令走同一条 ECS 事件链；命令系统不再直接摸物理控件。
- **迁移内容**：
  1. 新增 `EcsEventBus`：实体事件队列 + 分发（事件名 → 函数绑定，参数绑定读取 ECS 状态）；
  2. RenderPort 输入采集端口：物理 `Click`/`TextChanged`/`DoubleClick` → `UIInteractionEvent{entity, eventName, payload}` 入 ECS 队列；
  3. `ClickSetGetSystems` 重构：`set` 写 ECS 状态（`EcsStateAccessor`）→ 投影同步；`click` 发 ECS 事件；`get` 读 ECS 状态；
  4. `EventBindingSystem` 从"绑定物理控件事件"改为"注册 ECS 事件订阅"。
- **代码落点**：新增 `EcsEventBus`/`EcsStateAccessor`/`InputCapturePort`；`ClickSetGetSystems.cs`、`EventBindingSystem.cs` 重构。
- **验证**：`math_functions_test` click 链 + 手动操作（GUI 人手点击）回归——两条路径结果一致。

### M4 · 生命周期与挂载收编

- **目标**：scope 生命周期与挂载状态唯一化；挂载时相从"事后兜底"转为"先就绪后投影"。
- **迁移内容**：
  1. `ScopeLifecycleSystem` 唯一状态机：Loading / Mounted / Unmounted / Destroying；
  2. 挂载前移：root 先挂进宿主 → 宿主尺寸就绪（自愈）→ 再投影子树——消灭"布局在挂载前按错误基准定死"；
  3. scope 销毁单出口：销毁 ECS 实体 + 卸载物理树 + 清投影索引 + 状态复位（替代 `EcsGate.DestroyScopeAsync`/`ContainerProjector`/`DestroyScopeEntities` 多套）；
  4. `ContainerRegistry` 降级为只读宿主寻址表；挂载状态移入 ECS。
- **代码落点**：新增 `ScopeLifecycleSystem`/`MountStateMachine`；`EcsGate.cs`、`HostMountService.cs`、`ContainerProjector.cs` 收编。
- **验证**：`mainlayout_run_ui`（Side 内容切换 MathFunctions↔WorkflowInputUI）+ 重复加载回归。

### M5 · 单入口 + 差异投影（消灭全量重放与多入口竞态）

- **目标**：渲染从"全量盲放"变为"增量 + 校验"。
- **迁移内容**：
  1. 统一 `RenderRequest{scope, version, reason}` 串行队列（单消费者），加载/函数执行/分割条/重算统一入队，过期请求丢弃；
  2. 复活 `UIReconciler` 做**差异投影**（只投影建/删/改属性），`RenderPipelineStages` 重写为"挂载先行 + 差异投影 + 结果契约"；
  3. 渲染后**可见性契约断言**：物理控件 Bounds ⊆ 宿主客户区 && 尺寸>0 && Visible，失败显式 Warning + 一次自愈重渲染。
- **代码落点**：新增 `RenderRequestQueue`；`RenderPipelineStages.cs`/`RenderStageRunner.cs`/`UIReconciler.cs` 重构；`RenderPipelineComposition.cs` 多入口收敛。
- **验证**：全部脚本回归 + 竞态压力（快速切换 Side 内容，断言无 `Command failed: Pending`、无孤儿控件）。

### M6 · 查找与诊断收编

- **目标**：控件查找、值访问、诊断全部以 ECS 为口径。
- **迁移内容**：
  1. `EntityQueryService`：按 scope/name/type 查询 ECS 实体（替代 `ControlFinder`/`ControlTraverser` 物理树遍历）；
  2. `ControlPool` 降级为投影索引（实体→物理控件反查缓存，仅 RenderPort 维护）；
  3. `LayoutInspectSystem` 改 ECS 口径：`layout positions/report` 直接读 ECS `ComputedLayout` + 物理投影校验，Info 常开；
  4. 新增"容器自检脚本"：load math → layout positions → 断言 4 按钮 Bounds ∈ sideContainer 客户区且非 0。
- **代码落点**：新增 `EntityQueryService`；`ControlFinder.cs`/`ControlPool.cs`/`LayoutInspectSystem.cs` 收编。
- **验证**：全部脚本 + 新自检脚本作为每次改动的回归判据。

---

## 四、关键设计决策

1. **真源方向铁律**：一切状态写 ECS；物理树只读投影；交互经采集端口单向回灌。现有回写代码（`Sync*`/`OnSplitterMoved`）一律删除或降级审计——双真源的根源，不删等于没重构。
2. **布局意图化**：ECS 表达"意图"，像素计算在 ECS 内完成并带收敛断言；RenderPort 应用时优先借力 WinForms 原生 Dock/Anchor（`proportional` 才计算像素）——不再"自己实现残缺布局引擎"。
3. **事件同链**：GUI 操作与脚本命令统一进 ECS 事件总线，"命令链与渲染链解耦"的架构裂缝随之消除。
4. **版本化渲染**：单队列 + 版本号 + 差异投影 + 结果契约——渲染从"全量盲放"变为"增量 + 校验"。
5. **兼容性**：Config JSON 格式不变（新增可选 dock/anchor 字段）；物理控件形态不变（WinForms 仍是最终呈现，仅不再反向持有状态）。

---

## 五、实施顺序、风险与兼容性

### 5.1 建议顺序

**M1（真源单向化）→ M3（事件/命令收编）→ M2（布局意图化）→ M4（生命周期）→ M5（单入口差异投影）→ M6（查找/诊断）**

- M1 先行：工作量小、立即消除双真源、产出"按钮不可见"的真实数值基线；
- M3 提前：直接修复"命令链与渲染链解耦"这一最痛缺陷，且不改变布局行为、风险低；
- M2 行为变化最大：需配置截图基线校准，放在命令链稳定之后；
- M4/M5 依赖 M2/M3 的稳定基础，最后收口入口与生命周期。

### 5.2 风险

| 风险 | 等级 | 缓解 |
|------|------|------|
| M2 改变现有配置渲染效果（proportional→原生布局映射） | 中 | 截图基线逐配置校准；映射器兼容比例语义 |
| M5 管线重写涉及渲染主路径 | 高 | 以 M2/M4 为前提，分步落地；差异投影逐实体灰度 |
| 事件采集端口遗漏某些控件事件（如 ListView 双击） | 中 | 事件白名单清单化，回归脚本覆盖 |
| 物理树交互与 ECS 状态竞态（双线程） | 中 | 采集端口统一 UI 线程入队；版本号去重 |

### 5.3 兼容性承诺

- 存量 JSON 配置零改动可运行（`dock`/`anchor` 为可选新增字段）；
- 命令脚本（`click`/`set`/`get`/`validate`）语义不变；
- 对外 API（`EcsGate.LoadConfigAndRenderAsync`/`LoadConfigToTarget` 函数注册）签名不变；
- 每阶段结束时旧代码（`Sync*` 回写、多入口、物理树查找）同步移除或标记 `[Obsolete]`，不留双轨。

