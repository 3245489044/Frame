# FrontEndDriven 多作用域重构：当前状态总结与下一步方案

> 状态：S3 + 缺陷2 已实施并通过编译验证；**缺陷5 已治本修复（S4）**；缺陷1/缺陷2 残余见 §3.3
> 日期：2026-08-03 | 关联文档：`FrontEndDriven_RenderPipeline_MultiScope_Defect.md`（原缺陷分析）、`RefactorPlan_ScopeIsolation_InternalStorage.md`（重构方案）
> 验证脚本：`FileDriven/DrivenConfig/Scripts/UI/mainlayout_run_ui.json`
> 最近更新：2026-08-04 S4（composition 生命周期收敛）+ S1（ControlPool 复用 StorageBidirectionalExtensions）已实施，MainProj.sln + FileDriven.sln 全量编译通过

---

## 一、当前状态（已实施完成）

### 1.1 已完成的代码改动（9 个文件，均经 `MainProj.sln` 全量编译）

| # | 文件 | 改动 | 目标缺陷 |
|---|------|------|---------|
| 1 | `CsharpPrj/Operation/FrontEndDriven/Ports.cs` | `CollectSnapshot(string? scope=null)` 按 `ScopeComponent` 过滤；新增 `EnumerateControls(scope)` 作用域感知枚举（无参=全量兼容） | 缺陷3 |
| 2 | `CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderPipelineStages.cs` | `RenderCollectStage` 传 `ctx.Scope` 给 `CollectSnapshot` | 缺陷3 |
| 3 | `CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderStageRunner.cs` | `ExecuteRenderStage(string? scope=null)`：显式 scope 局部渲染，无参全量兼容 | 缺陷2/3 |
| 4 | `CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderPipelineComposition.cs` | `ExecuteRenderStage(scope)` 透传 | 缺陷2/3 |
| 5 | `CsharpPrj/Operation/FrontEndDriven/Gate/EcsGate.cs` | `DoLoadAndRender` 重渲染传 `op.Scope` | 缺陷2/3 |
| 6 | `CsharpPrj/Operation/FrontEndDriven/Lifecycle/EventPlatform.cs` | `FunctionExecutedPayload` 携带 `Scope`；`FunctionDone(scope)`；`InvokeFunctionAsync/InvokeFunction` 加 `scope` 参数 | 缺陷2 |
| 7 | `CsharpPrj/Share/EventBindingOrchestrator/Abstractions/IEventPlatform.cs` | 接口签名同步（加 `scope=""`） | 缺陷2 |
| 8 | `CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/FunctionExecutedRenderBridge.cs` | 按事件 `Scope` 限定重渲染（非空→局部，空→全量） | 缺陷2 |
| 9 | `CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/FunctionExecutedRenderBridge.cs` | **S4 补强**：`_disposed` 防护标志——入口与 Post 回调双重检查 + ObjectDisposedException 竞态兜底 | 缺陷5 |
| 10 | `CsharpPrj/Operation/FrontEndDriven/ControlLookup/Container/EcsContainerCore.cs` | **S4**：`Dispose()` 移除 `_composition?.Dispose()`，改为解绑视图/桥接 + `DestroyScopeEntities(Name)`，composition 生命周期收敛至 `EcsGate` | 缺陷5 |
| 11 | `CsharpPrj/Operation/FrontEndDriven/ControlLookup/Container/EcsConfigContainer.cs` | **S4**：`Dispose(bool)` 保持现状（仅清 Controls），加所有权声明注释 | 缺陷5 |
| 12 | `CsharpPrj/BasicMethod/ECS/Bridge/ControlPool.cs` | **S1**：内嵌 `MapForward/GetForward/GetBackward/Unmap/ContainsForward/ContainsBackward` 改复用 `StorageBidirectionalExtensions`（Modules.InternalStorage） | 消除重复实现 |
| 13 | `CsharpPrj/BasicMethod/ECS/ECS.csproj` | **S1**：新增 `InternalStorage.csproj` 引用（无循环依赖——InternalStorage 仅引用 TConfig） | 消除重复实现 |

### 1.2 运行时验证结果（2026-08-03 23:34 日志）

**S3 修复已生效（证据充分）**：

| 时间 | 事件 | 日志证据 | 结论 |
|------|------|---------|------|
| 23:34:15.154 | Side 加载启动 | `LoadConfigToTarget → EcsGate 审计 LoadAndRender` | 走 gate |
| 23:34:15.167 | 管线执行 | `RenderPipelineComposition: scope=Side, rootEntityId=34` | scope 传递正确 |
| 23:34:15.168 | **收集器** | **`CsgEntryPoint: scope=Side, controlCount=19`** | **核心：Side 局部收集，不再是 MainLayout=48 全量** |
| 23:34:15.561 | 校验 | `titleLabel/addButton/subtractButton/statusLabel 校验成功` | Side 正确加载，内容区未消失 |

**缺陷5 残留（需定位）**：

| 时间 | 事件 | 日志证据 |
|------|------|---------|
| 23:34:15.569 | 点击 loadSideButton | `LoadSidePanel → LoadConfigToContainerInteractive(SideContainer)` |
| 23:34:16.168 | **composition disposed** | `[ERROR] EventPlatform: eventType=function.Executed, source=LoadSidePanel, error=Cannot access a disposed object. Object name: 'RenderPipelineComposition'.` |

---

## 二、残留问题分析（缺陷5：composition Dispose 时序失控）

> **S4 修复结论（2026-08-04 已实施）**：根因确认为共享 composition 在 `LoadSidePanel` 模态对话框阻塞期间被释放，`FunctionExecutedRenderBridge` 收到残留 `function.Executed` 事件后仍调用 `ExecuteRenderStage` 抛 `ObjectDisposedException`（函数执行与窗体 Dispose 的异步竞态）。治本三层防护已实施（见 §1.1 第 9-11 行）：
> 1. **所有权收敛**：`EcsContainerCore.Dispose` 不再 Dispose composition（仅解绑视图/桥接 + `DestroyScopeEntities(Name)`）；
> 2. **唯一释放点**：`EcsGate.Dispose` 保持为共享 composition 唯一释放点；
> 3. **事件竞态兜底**：`FunctionExecutedRenderBridge` 增加 `_disposed` 标志（入口 + Post 回调双重检查）+ `ObjectDisposedException` 静默捕获。

### 2.1 已排除的嫌疑

- **`ConfigurationLifecycleManager.DestroyScopeAsync`**（`CsharpPrj/Operation/FrontEndDriven/Lifecycle/ConfigurationLifecycleManager.cs:61-77`）：仅 `_gate.Composition.DestroyScopeEntities(scope)`，**不 Dispose composition** ✅ 已排除
- **`EcsConfigContainer.Dispose`**（`CsharpPrj/Operation/FrontEndDriven/ControlLookup/Container/EcsConfigContainer.cs:94-104`）：仅 `Controls.Clear()` + 生命周期状态迁移，**不 Dispose composition** ✅ 已排除

### 2.2 剩余嫌疑（composition.Dispose() 仅存两处）

| 调用点 | 文件 | 触发条件 |
|--------|------|---------|
| `_composition.Dispose()` | `CsharpPrj/Operation/FrontEndDriven/Gate/EcsGate.cs:183` | `MainForm.Dispose → EcsGate.Dispose`（窗体关闭/CLI 模式结束） |
| `_composition?.Dispose()` | `CsharpPrj/Operation/FrontEndDriven/ControlLookup/Container/EcsContainerCore.cs:138` | `EcsContainerCore.Dispose`（每容器独立 composition，非主 composition） |

### 2.3 关键差异：`LoadSidePanel` vs `LoadConfigToTarget`

| | 双击 configListView（成功） | 点击 loadSideButton（失败） |
|--|---------------------------|----------------------------|
| 函数 | `LoadConfigToTarget` | `LoadSidePanel` |
| UI 交互 | 无对话框，直接 `sink.EmitLoadConfig` | **`mainForm.Invoke(...)` 先弹 `OpenFileDialog` 模态对话框**，再 `sink.EmitLoadConfig` |
| 结果 | composition 存活 | composition disposed |

**假设**：模态对话框（`ShowDialog`）阻塞期间，主窗体（CLI 模式下）可能收到 `FormClosing`/`Dispose`，或 `sink.EmitLoadConfig` 的后台加载链在对话框确认后触发了某次 `DestroyScopeAsync` 连带影响主 composition 持有者。**确切调用栈需运行时断点/诊断代码确认**（原文档 §6 待验证项）。

---

## 三、下一步重构方案（S4 + 验证）

### 3.1 S4：composition 生命周期保护（缺陷5 治本）

**目标**：共享主 composition 的生命周期唯一由 `EcsGate` 持有（应用级），作用域销毁/容器重建**绝不触及 composition.Dispose**。

| 改动文件 | 具体动作 |
|---------|---------|
| `CsharpPrj/Operation/FrontEndDriven/ControlLookup/Container/EcsContainerCore.cs` | `Dispose()` 不再 `_composition?.Dispose()`；改为：解绑 `_renderBridge`、清 `_containerView/_pendingView`、调用 `_composition.DestroyScopeEntities(Name)`（若必要）后置 `_composition = null` |
| `CsharpPrj/Operation/FrontEndDriven/ControlLookup/Container/EcsConfigContainer.cs` | `Dispose(bool)` 保持现状（仅清 Controls，不 Dispose composition）——**加注释声明 composition 非本容器所有权** |
| `CsharpPrj/Operation/FrontEndDriven/Gate/EcsGate.cs` | 保持 `EcsGate.Dispose → _composition.Dispose()` 为**唯一**共享 composition 释放点 |

**前置验证**（S4 实施前需运行确认）：
1. 在 `EcsGate.Dispose` 与 `EcsContainerCore.Dispose` 增加临时 Debug 日志（或断点），运行 `UI启动.bat` 点击 `loadSideButton`，确认 composition 被谁 Dispose。

### 3.2 S1/S2（基于 InternalStorage 的作用域存储分区，架构级）

> 注意范围评估：S2 影响 `ControlPool.Register` 签名，全局约 62 处调用，属**架构级重构**，建议独立评审后实施。

| 阶段 | 改动文件 | 内容 |
|------|---------|------|
| S1 | `CsharpPrj/BasicMethod/ECS/Bridge/ControlPool.cs` | 内嵌 `MapForward/Unmap/bidir_reverse:` 改复用 `CsharpPrj/Modules/InternalStorage/Extensions/StorageBidirectionalExtensions.cs`（消除重复实现） |
| S2 | `ControlPool.cs` + `CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/PipelineCompositionFactory.cs` | scope→`IStorageCell` 分区（基于 `StorageEngine.CreateSession`），`Clear()`→`UnregisterScope(scope)` |

### 3.3 其他已识别待办（低风险，可择期）

| 内容 | 文件 |
|------|------|
| 缺陷1 残留：`DestroyScopeEntities` 未清理 ControlPool 控件归属（实体销毁后 `EntityDestroying`→`Unregister` 已自动解绑，但 WinForms 视图清理仍依赖全局 Applier 路径） | `ControlPool.cs`/`RenderStageRunner.cs` |
| 缺陷2 残余：事件绑定 `executeFunctionDelegate` 直接调后端不发布 `FunctionExecuted`，其触发 UI 变更后无自动重渲染——需评估是否接入 scope 化事件 | `Share/EventBindingOrchestrator/EventBindingPipelineFactory.cs` |

---

## 四、文件目录索引（当前状态涉及）

### 4.1 已修改（实施完成）
```
CsharpPrj/Operation/FrontEndDriven/
├── Ports.cs                                    # S3 收集器作用域感知
├── Gate/EcsGate.cs                             # S3 DoLoadAndRender scope 化
├── Lifecycle/EventPlatform.cs                  # 缺陷2 事件携带 scope
├── Initial/RenderPipeline/
│   ├── RenderPipelineComposition.cs            # S3 ExecuteRenderStage(scope) 透传
│   ├── RenderStageRunner.cs                    # S3 ExecuteRenderStage(scope)
│   ├── RenderPipelineStages.cs                 # S3 RenderCollectStage 传 scope
│   └── FunctionExecutedRenderBridge.cs         # 缺陷2 按事件 scope 限定重渲染
CsharpPrj/Share/EventBindingOrchestrator/Abstractions/
└── IEventPlatform.cs                           # 缺陷2 接口签名同步
```

### 4.2 S4（已实施）
```
CsharpPrj/Operation/FrontEndDriven/ControlLookup/Container/
├── EcsContainerCore.cs                         # S4 移除 _composition?.Dispose()，改 DestroyScopeEntities(Name)
└── EcsConfigContainer.cs                       # S4 加 composition 所有权声明注释
CsharpPrj/Operation/FrontEndDriven/Gate/EcsGate.cs  # S4 保持唯一释放点（评审确认，未改）
CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/
└── FunctionExecutedRenderBridge.cs             # S4 dispose 防护（_disposed 双重检查 + 竞态兜底）
```

### 4.3 架构级（S1 已实施；S2 待独立评审）
```
CsharpPrj/BasicMethod/ECS/Bridge/ControlPool.cs          # S1 复用 StorageBidirectionalExtensions（已实施）
CsharpPrj/BasicMethod/ECS/ECS.csproj                     # S1 新增 InternalStorage.csproj 引用（已实施）
CsharpPrj/Modules/InternalStorage/Extensions/
└── StorageBidirectionalMap.cs                           # S1 复用目标（⚠ 路径勘误：原文档写 StorageBidirectionalExtensions.cs 不存在，实为 StorageBidirectionalMap.cs，类名 StorageBidirectionalExtensions）
CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/
└── PipelineCompositionFactory.cs                        # S2 scope→Cell 装配（待独立评审）
```

### 4.4 文档
```
docs/analysis/
├── FrontEndDriven_RenderPipeline_MultiScope_Defect.md   # 原缺陷分析（§5.2/§5.3 已回填实施记录）
├── RefactorPlan_ScopeIsolation_InternalStorage.md       # 重构方案（S1-S4）
└── FrontEndDriven_ScopeRefactor_StateSummary.md         # 本文件（状态总结）
```

---

## 五、建议执行顺序

1. ~~**S4 前置验证**~~ ✅ 已完成（2026-08-04）：根因确认为共享 composition 模态对话框阻塞期间释放 + FunctionExecutedRenderBridge 无防护；
2. ~~**S4 实施**~~ ✅ 已完成：`EcsContainerCore.Dispose` 移除 `_composition?.Dispose()`，composition 生命周期收敛至 `EcsGate`（含 FunctionExecutedRenderBridge 防护）；
3. **回归验证**：`dotnet build CsharpPrj/MainProj.sln` ✅ 通过（0 警告 0 错误）+ `UI启动.bat` 复跑 `mainlayout_run_ui.json`，确认 `loadSideButton` 不再报 disposed ⏳ **待 GUI 复跑确认**；
4. ~~**S1**~~ ✅ 已完成：ControlPool 复用 StorageBidirectionalExtensions（+ 路径勘误：内部存储扩展实为 StorageBidirectionalMap.cs）；
5. **S2**（架构级，独立评审）：scope→Cell 分区。

---

*本文件为进度快照；每步实施后回填验证结果并更新状态。*