# FrontEndDriven 冗余逻辑分析报告

> 分析范围：`D:\Project\Program\WorkFlowPrj\CsharpPrj\Operation\FrontEndDriven`
> 分析方法：全量文件枚举（约 110 个 .cs）+ 关键文件逐读 + 调用点交叉验证
> 说明：该目录经历过多次收敛重构（注释中大量 R1~R6 / D1~D3 / M1~M6 / P2 / S3 等标记），部分冗余已被注释标注为「已收敛」；本文仅列出**当前仍存在的重复或死代码**。

---

## 1. 查找体系双轨并存（最明显）

| 组件 | 位置 | 说明 |
|---|---|---|
| `ControlFinder`（静态类） | `WinForms\Finder\ControlFinder.cs` | 基于 `TreeWalker`/`ControlTraverser` 的物理树遍历 |
| `ControlRegistryAwareFinder`（实例类，`IControlFinder`） | `WinForms\Finder\ControlRegistryAwareFinder.cs` | 基于 `ControlLookupRegistry`（ControlPool → EntityTree → Registry → TreeWalk）的查找 |

**重复点**
- 两者大量方法同名同语义：`FindByName`、`FindRecursive`、`FindAcrossForms`、`FindInForm`、`FindForm`、`IsDescendantOf`、`GetDepth`。
- `ControlRegistryAwareFinder` 在未命中时回退 `TreeWalkSource`，本质又走回 `ControlFinder`/`ControlTraverser` 的深搜路径。
- 更底层还有 `ControlIndex`（`TreeIndexWrapper`）、`WinForms\Container\ControlIndexRegistry`、`ContainerRegistry`，同做「按名查控件/按实体查控件」，职责边界模糊。

**建议**：以 `ControlRegistryAwareFinder` 为唯一查找门面（或明确二选一），静态 `ControlFinder` 降级为内部工具；`ControlIndex`/`ControlIndexRegistry` 与 `ControlRegistry` 合并为单一注册表。

> **现状(2026-08)**：`ControlRegistryAwareFinder` 已是唯一 DI 门面（`ControlServiceCollection` 注册为 `IControlFinder`，被 CommandFramework / BackEndDriven `UiCommandSystems` 消费），内部走 `ControlLookupRegistry` 多级源；`ControlFinder` 已降级为其内部树工具 + `LayoutInspectSystem` 只读枚举（深搜已收敛到 `ControlTraverser`）。**误诊点**：`ControlIndexRegistry`（普通控件 name→Control 索引）与 `ContainerRegistry`（容器 scope↔hostKey↔MountState 挂载状态注册表）域不同，**勿合并**。

---

## 2. 属性应用路径重复（两个 ApplyProperties 并存）

| 实现 | 位置 |
|---|---|
| `PropertyApplier.ApplyProperties(Control, Dictionary, IWinFormsControlAdapter)` | `Shared\Utils\PropertyApplier.cs` |
| `WinFormsUIAdapter.ApplyPropertyChanges / ApplyCanonical` | `WinForms\Adapter\WinFormsUIAdapter.cs` |
| `WinFormApplier.ApplyPropertiesFromDict → PropertyApplier.ApplyProperties` | `WinForms\Applier\WinFormApplier.cs` |

**重复点**
- `WinFormsUIAdapter.ApplyCanonical` 逐节点解析 EntityId → `ControlMap` 查控件 → `ApplyPropertyChanges` → `PropertyApplier.ApplyProperties`；这与 `WinFormApplier.Apply`（快照 → 差异 → `PropertyApplier`）是**两套平行的「目标表单 → 物理控件属性投影」管线**。
- `WinFormsUIAdapter` 依赖的 `IControlMap`/`ControlMapAdapter` 只是把 `ControlPool.GetAllEntities()/GetControl()/GetEntity(name)` 薄包装了一遍，属于重复适配层。
- 触发后果：同一份 UI 状态存在 `WinFormApplier`（快照差异）与 `UIReconciler`（CanonicalForm 差异）两条 diff 实现（见第 3 条）。

**建议**：确认 `UIReconciler`/`WinFormsUIAdapter.ApplyCanonical` 是否仍为活跃路径；若是，将其收敛到 `WinFormApplier` 单一投影内核；`ControlMapAdapter` 可删，直接注入 `ControlPool`。

> **现状(2026-08)已处理**：`UIReconciler`（无实例化）、`WinFormsUIAdapter.ApplyCanonical/ApplyPropertyChanges/ApplyPropertyChangesPublic`（死写路径）、`ControlMapAdapter`/`IControlMap`/`ControlMapEntry`、`IPropertyApplyOutput.ApplyProperties/ApplyValueChange` 全部删除；`WinFormsUIAdapter` 精简为只读辅助（`GetSize`/`ToCanonical`）并直接注入 `ControlPool`。物理属性写入唯一走 `WinFormApplier` 快照内核。

---

## 3. 差异比较逻辑三份实现

| 实现 | 位置 |
|---|---|
| `PropertyComparer.GetChangedProperties/Equal` | `Shared\Utils\PropertyComparer.cs` |
| `UIReconciler.ComputeChanges`（节点增删 + 属性 diff） | `Initial\RenderPipeline\UIReconciler.cs` |
| `WinFormApplier.ApplyProperties`（AppliedSnapshot 差异） | `WinForms\Applier\WinFormApplier.cs` |
| `RenderAuditPort.CollectDiffStats`（审计侧 diff） | `Diagnostics\RenderAuditPort.cs` |

**重复点**：同是「当前态 vs 目标态」的属性差集计算，四处各写一遍（其中 UIReconciler/RenderAuditPort 已复用 PropertyComparer，但节点级 diff 骨架仍各自实现）。

**建议**：节点级 diff 抽为单一 `SnapshotDiffer`，`UIReconciler`、`WinFormApplier`、`RenderAuditPort` 共用。

> **现状(2026-08)已处理**：`UIReconciler.ComputeChanges` 与 `RenderAuditPort.CollectDiffStats`（均无调用方）已删除；现仅剩 `WinFormApplier` 快照差异 + 共享 `PropertyComparer`。`SnapshotDiffer` 待出现 ≥2 个活的节点级 diff 消费方再抽取。

---

## 4. 控件值读写三套通道

| 实现 | 位置 | 职责 |
|---|---|---|
| `WinFormsControlValueWriter`（`IControlValueAccessor`） | `WinForms\ValueSync\WinFormsControlValueWriter.cs` | 物理控件 get/set（Text/Checked/ListView/ComboBox…） |
| `CompositeValueWriter`（聚合器） | `ControlInteraction\CompositeValueWriter.cs` | 多 writer 分派 |
| `EcsStateAccessor`（`IEcsStateAccessor`） | `ControlInteraction\State\EcsStateAccessor.cs` | ECS 真源读写 + 契约投影 |
| `ControlValueBridge`（`IControlService`） | PipelineCompositionFactory 装配 | 值语义契约（ListView 匹配选中等） |

**重复点**
- `EcsStateAccessor.SetValue` 中：契约未覆盖类型时走 `_valueAccessor.SetValue`（即 `WinFormsControlValueWriter`），契约覆盖时走 `_valueService.SetValue`——两套物理写实现并存，且 `WinFormsControlValueWriter` 与 `ControlValueBridge` 对 ListView/ComboBox 等控件存在明显的同类适配代码（`RegisterListViewAdapter/RegisterComboBoxAdapter` vs 契约匹配选中）。
- 读路径同样三分支：ECS 状态 → 契约读 → `ControlInfo.Text`，而物理读实现（`WinFormsControlValueWriter.GetValue`）只在契约未覆盖时被间接使用，边界难以保证一致。

**建议**：明确 `IControlService`（契约）为唯一值语义实现，`WinFormsControlValueWriter` 的适配条目收敛进契约注册表，删除双写路径。

> **现状(2026-08)待办（Phase B）**：未执行。契约注册表（`BuiltInValueContracts`）已覆盖 textbox/richtextbox/maskedtextbox/label/linklabel/button/domainupdown/datetimepicker/ListView/ComboBox/TreeView/DataGridView/CheckBox 等，但尚未覆盖 `NumericUpDown/PictureBox/ProgressBar/TrackBar`（当前由 `WinFormsControlValueWriter` 兜底）——直接删除 `EcsStateAccessor` 的 `_valueAccessor` 兜底分支会破坏这些类型写入。且 CentralTest 测试工程因 NU1015（版本属性定义在 `CsharpPrj/generated` 下，还原链不通）无法运行，行为级改动无法回归。**前置条件**：① 为缺失类型补契约；② 恢复测试工程还原；再做单通道收敛。

---

## 5. 控件枚举/过滤逻辑残留多处手写副本

`EntityQueryService.QueryControls` 注释声明「消除各处手写 HasComponent\<SplitterPanelComponent\> + ScopeComponent 匹配重复」，但以下位置仍各自手写同一过滤：

- `ControlRender\Commands\LayoutInspectSystem.cs:82`（`world.HasComponent<SplitterPanelComponent>(e)`）
- `ControlRender\Diagnostics\LayoutConvergenceReport.cs:43`（同款过滤 + scope 匹配）
- `ControlRender\Systems\LayoutSystem.cs:179,190`（SplitterPanel 判定）
- `Validation\Validators\ComponentCompletenessValidator.cs:30`
- `Initial\RenderPipeline\RenderPipelineStages.cs`（可见性检查遍历）

**建议**：这些消费方统一改走 `EntityQueryService.QueryControls(scope)`，删除各自手写枚举。

---

## 6. 事件相关三个 Helper 职责重叠

| 文件 | 内容 |
|---|---|
| `Shared\EventHelper.cs` | 标准事件名表 + PascalCase 映射 + `BuildEvent`/`GetEventFunctions` |
| `Shared\EventSubscriptionHelper.cs` | `TrySubscribe`：事件名 → 控件物理事件订阅 switch（Click/CheckedChanged/SelectedIndexChanged/ValueChanged…） |
| `Shared\EventBindingFormatter.cs` | EventConfig → 绑定字符串（复用 EventHelper） |

**重复点**
- `EventHelper.StandardEventEntries`（约 50 个标准事件名）与 `EventSubscriptionHelper.TrySubscribe` 内手写 switch 的事件名集合（约 10 个）**各自维护**：新增事件名需两处同步，已出现 `afterSelect`/`selectionChanged` 仅在订阅侧存在、`splittermoved` 仅在标准表存在的不一致。
- 事件名 → 控件事件订阅的分派逻辑历史上从 `EventBindingSystem` 与 `InputCapturePort` 两处收敛到了 `EventSubscriptionHelper`（注释 D6），但分派表仍未与标准事件表统一。

**建议**：以 `EventHelper.StandardEventEntries` 为唯一事件名权威，`TrySubscribe` 改为按表驱动（或至少让未支持事件快速失败时记录日志），删除两边独立字符串集合。

---

## 7. 布局/样式校验与报告重复

| 实现 | 位置 |
|---|---|
| `LayoutValidator` | `Validation\Validators\LayoutValidator.cs` |
| `LayoutConvergenceReport` | `ControlRender\Diagnostics\LayoutConvergenceReport.cs` |
| `LayoutInspectSystem`（positions/report/json-report/overlaps） | `ControlRender\Commands\LayoutInspectSystem.cs`（401 行） |

**重复点**
- `LayoutConvergenceReport`（缺失布局/0 尺寸）与 `LayoutValidator`（ComputedLayout 非负、越界）都在遍历 `ProportionalLayoutComponent` 实体并检查 `ComputedLayoutComponent`，产出同类「布局问题清单」。
- `LayoutInspectSystem` 的 `positions/report` 子命令也做「ECS ComputedLayout 遍历 + 物理对照 + mismatch 汇总」，与上述两者高度重叠（且各自手写 SplitterPanel 过滤，见第 5 条）。

**建议**：布局检查收敛为单一「布局断言服务」：校验、收敛报告、inspect 子命令共用同一枚举与问题模型，仅输出形态不同。

---

## 8. 纯死代码（全仓无调用方）

| 文件/类型 | 证据 |
|---|---|
| `Shared\Utils\PathManager.cs`（287 行，约 40 个方法） | 本目录内 `PathManager` 仅自身定义处 1 次命中，无任何调用方 |
| `Shared\Abstractions\CommonAbstractions.cs`（`IPathResolver`） | 引用数 0 |
| `ComponentDataIntegration\ComponentDataIntegration.cs`（`EntityManagerEventSourceAdapter`） | 除自身命名空间外 0 引用（文件外命中为目录同名） |
| `Gate\EcsOperation.cs` 中 `LoadAndRenderOperation` | 文件注释自述「全仓无构造调用方（死分支），随 EcsGate.DispatchAsync 精简一并删除」 |

**建议**：确认无外部工程引用后删除；`EcsOperation` 若仅剩一个实现，可将抽象层一并简化。

---

## 9. 次要：日志与状态碎片

- `WinForms\Diagnostics\UILogger.cs` 与 `Shared\Diagnostics\FrontEndDrivenLog.cs` 并存，前者仅 1 处引用，建议并入统一日志门面。
- `Shared\ConfigLoadInput.cs`（`LoadedConfigs` 字典）与 `Shared\LoadedConfigInfo.cs` 仅被 `ConfigurationLifecycleManager` 少量引用，疑似早期配置缓存残留，可评估合并进 `PathManager` 之外的单一配置服务。
- `WinForms\Container\ContainerControl.cs` 与 `ControlLookup\Container\EcsConfigContainer.cs` 同为「容器控件」概念，`ContainerRegistry`/`ControlIndexRegistry` 双注册表并存（见第 1 条）。

---

## 结论（优先级排序）

1. **高**：查找体系（ControlFinder / ControlRegistryAwareFinder / ControlIndex / 双 Registry）合一； — **现状：已收敛**（唯一 DI 门面 + 内部工具；ControlIndexRegistry/ContainerRegistry 域不同不合并）
2. **高**：属性投影双管线（WinFormsUIAdapter.ApplyCanonical vs WinFormApplier）收敛，连带删除 `ControlMapAdapter`； — **现状：已完成**（死写路径全删，唯一 WinFormApplier 快照内核）
3. **高**：值读写双实现（WinFormsControlValueWriter vs IControlService 契约）统一； — **现状：待办 Phase B**（需先补 NumericUpDown/PictureBox/ProgressBar/TrackBar 契约 + 恢复测试工程）
4. **中**：控件枚举过滤手写副本统一到 `EntityQueryService.QueryControls`； — **未处理**
5. **中**：事件名表与订阅分派表合一；
6. **中**：布局校验/收敛报告/inspect 三合一；
7. **低**：删除死代码（PathManager、CommonAbstractions、ComponentDataIntegration、EcsOperation 死分支）；
8. **低**：日志门面与配置缓存碎片整理。

> 注意：部分「重复」在历史上是有意保留的双轨（如审计侧只读对照），删除前需按注释中 R/D/M 编号对应的收敛决议逐一确认，避免破坏已对齐的语义。
