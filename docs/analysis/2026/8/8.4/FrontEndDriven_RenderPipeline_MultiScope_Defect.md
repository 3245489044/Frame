# FrontEndDriven 渲染管线多作用域缺陷分析

> 状态：已确认（框架层整体缺陷） | 日期：2026-08-03 | 关联脚本：`FileDriven/DrivenConfig/Scripts/UI/mainlayout_run_ui.json`
> 修复状态：缺陷3（收集器无作用域感知）已于 2026-08-03 修复（见 §5.2 实施记录）

## 1. 背景与现象

在 `mainlayout_run_ui.json` 脚本运行中，双击 `configListView` 将 MathFunctions 配置加载到**侧边栏（sideContainer）**后，**内容区（contentContainer）的 ConfigSelector 界面消失**。随后点击 `Load Side` 按钮时抛出：

```
[ERROR] EventPlatform: 事件处理异常 — source=LoadSidePanel,
  error=Cannot access a disposed object. Object name: 'RenderPipelineComposition'.
```

现象定位为**框架级渲染管线缺陷**，而非单点配置问题。

## 2. 已有修复（已完成）

### 2.1 `UIToolbox.ResolveScopeFromContainer` 删除静默回退

- **文件**：`FileDriven/FileDriven.Business/Registrations/UI/UIToolbox.cs`
- **修复前**：未知/空 `targetContainer` 静默回退到 `"Content"`，导致侧边栏加载在参数缺失时误刷内容区。
- **修复后**：未知容器抛出 `InvalidOperationException`，明确暴露参数问题：

```csharp
throw new InvalidOperationException(
    $"未知的目标容器 '{targetContainer}'，无法解析作用域。合法值: '{UIControlNames.SideContainer}' / '{UIControlNames.ContentContainer}'");
```

验证：双击日志显示 `targetContainer=sideContainer` 已正确传递，Side 加载成功。

## 3. 框架层根因分析（证据链）

### 3.1 时间线（本次运行日志）

| 时间 | 事件 | 证据 |
|------|------|------|
| 22:57:50.779 | 双击 → LoadConfigToTarget 参数正确 | `configPath=...MathFunctions.json, targetContainer=sideContainer` |
| 22:57:50.877 | Side 作用域渲染启动 | `EcsGate: 审计 — LoadAndRender, scope=Side` |
| 22:57:50.893 | 管线执行 Side | `RenderPipelineComposition: scope=Side, rootEntityId=34, collectedCount=0` |
| 22:57:50.894 | **收集全量 MainLayout** | `CsgEntryPoint: scope=MainLayout, controlCount=48`（而非仅 Side 局部） |
| 22:57:50.939 | 事件绑定 6 实体 | `EventBindingSystem: boundCount=6` |
| 22:57:51.874 | **composition 已 Dispose** | `LoadSidePanel → Cannot access a disposed object. RenderPipelineComposition` |

### 3.2 缺陷 1：多作用域渲染缺乏隔离（核心）

- `RenderPipelineComposition.Clear()`（`RenderPipelineComposition.cs`）同时执行：
  ```csharp
  _controlPool.Clear();
  _entityManager.Clear();
  ```
  这是**全局清空**——`ControlPool` 与 `EntityManager` 是所有作用域（MainLayout / Content / Side）共享的单例池。任何 scope 级 `DestroyScopeAsync` / `Clear()` 都会**连带移除其他作用域的实体与控件**，包括内容区的 ConfigSelector。

- `EcsGate.DoLoadAndRender` 中 `DestroyScopeAsync(op.Scope)` → `DestroyScopeEntities(scopeName)` 仅按 `ScopeComponent.Scope == scopeName` 销毁实体，但**实体销毁后关联的 WinForms 控件清理由全局 `ControlPool` 承担**，无作用域边界保护。

### 3.3 缺陷 2：`FunctionExecutedRenderBridge` 全量重渲染重入

- `FunctionExecutedRenderBridge.OnFunctionExecutedAsync`（`Initial/RenderPipeline/FunctionExecutedRenderBridge.cs`）在**每次函数执行完成**后触发：

  ```csharp
  _dispatcher.Post(() => _composition.ExecuteRenderStage());
  ```

- `ExecuteRenderStage()`（`RenderStageRunner.ExecuteRenderStage`）执行 **Collect → Apply → Bind 全量区间**，且 `CollectSnapshot()` 无作用域过滤，收集的是**整个窗体快照**（证据：`scope=MainLayout, controlCount=48`）。

- 副作用链：
  1. `LoadConfigToTarget`（Side 加载）执行完成 → 发布 `FunctionExecuted`；
  2. Bridge 立即再触发一次全量 `ExecuteRenderStage`；
  3. 该重渲染与 `ContainerProjector.RenderAsync` 的 Side 渲染叠加，同一管线在毫秒级窗口内**多次全量快照收集与重放**；
  4. 重放时对非目标容器执行控件清理/重建（`WinFormsControlAdapter` 含 `ctrl.Controls.Clear()` 路径），内容区随之消失。

### 3.4 缺陷 3：收集器无作用域感知

- `RenderCollectStage` → `CsgEntryPoint.CollectSnapshot()`（`Ports.cs`）从 **ControlPool / WinForms 控件树全集**收集，无 scope 参数分发。
- 日志 `CsgEntryPoint: scope=MainLayout` 证实：即使渲染目标是 Side，收集仍是 MainLayout 全量。
- 结果：动态容器（EcsConfigContainer）内的控件与静态骨架（SplitContainer、Toolbar）在快照中**混为一棵重建树**，无法按作用域增量投影。

### 3.5 缺陷 4：共享 `RenderContext` 单例复用

- `RenderStageRunner._sharedContext`（`RenderStageRunner.cs`）为单例，`CreateSharedContext()` 每次 `Reset()` 后复用。
- `ExecuteRenderStage` 时 `PartLoad=false` → `RenderLoadStage.CanExecute=false`，**不重新加载配置**，`ctx.RootEntity` 为 default；但 Collect/Apply/Bind 仍以**上一轮残留上下文 + 全局快照**执行——作用域切换后上下文完整性无保证。

### 3.6 缺陷 5：composition Dispose 时序失控

- 日志证实 `LoadSidePanel` 事件处理时 `RenderPipelineComposition` 已 `Dispose`。
- 触发点候选：
  - `EcsGate.Dispose()`（`EcsGate.cs`）→ `_composition.Dispose()`；
  - `EcsContainerCore.Dispose()`（`EcsContainerCore.cs:136`）→ `_composition?.Dispose()`；
  - `EcsConfigContainer.Dispose()`（动态 Side 容器清除/重建时）→ `Controls.Clear()` + 生命周期迁移。
- 在 Side 动态容器加载/重建引发的 `Clear()`/`Dispose()` 链中，**全局 composition 被释放**，后续任何函数执行（LoadSidePanel）访问 composition 即抛 `Cannot access a disposed object`。

## 4. 影响范围

| 影响 | 说明 |
|------|------|
| 内容区丢失 | Side 加载后全量重渲染/重建，Content（ConfigSelector）控件被清空 |
| 二次交互崩溃 | composition Dispose 后，后续按钮事件（LoadSidePanel 等）抛对象已释放异常 |
| 多容器不可共存 | 所有 scope 共享 ControlPool/EntityManager，无隔离边界，无法并行独立投影 |
| 快照语义失真 | 收集器无 scope 过滤，动态容器与静态骨架混树重建 |

## 5. 修复方向建议（待实施）

### 5.1 作用域级隔离（治本）
- `ControlPool` / `EntityManager` 引入 scope 分区：按 `ScopeComponent` 建立控件归属索引，`Clear()`/销毁仅作用于目标 scope。
- `DestroyScopeEntities` 同步从 ControlPool 卸载关联控件，避免全局清空。

### 5.2 收集器作用域感知（治本）
- `CollectSnapshot(scope)`：按 scope 过滤控件树与关系，只收集目标宿主容器（`sideContainer` / `contentContainer`）子树。
- `RenderCollectStage` 传入 `ctx.Scope`，消除 `scope=MainLayout` 硬编码。

**实施记录（2026-08-03，已完成并编译验证）**：
| 文件 | 改动 |
|------|------|
| `Ports.cs` | `CollectSnapshot(string? scope=null)` 按 `ScopeComponent` 过滤；`EnumerateControls(scope)` 作用域感知枚举 |
| `RenderPipelineStages.cs` | `RenderCollectStage` 传 `ctx.Scope` |
| `RenderStageRunner.cs` | `ExecuteRenderStage(string? scope=null)`：显式 scope 局部渲染，无参保持全量兼容 |
| `RenderPipelineComposition.cs` | `ExecuteRenderStage(scope)` 透传 |
| `EcsGate.cs` | `DoLoadAndRender` 重渲染传 `op.Scope`（Side 加载后仅重渲染 Side） |
> 详细方案见 `docs/analysis/RefactorPlan_ScopeIsolation_InternalStorage.md`

### 5.3 消除全量重渲染重入（治本/缓解）
- `FunctionExecutedRenderBridge` 限制重渲染仅作用于**触发事件的 scope 宿主**，或引入与 `ContainerProjector.RenderAsync` 的去重/互斥（如渲染互斥锁、scope 级版本号）。
- 函数执行完成事件若已由渲染链自行触发渲染，Bridge 应跳过。

**实施记录（2026-08-03，已完成并编译验证）**：
| 文件 | 改动 |
|------|------|
| `EventPlatform.cs` | `FunctionExecutedPayload` 携带 `Scope`；`PlatformEvent.FunctionDone(scope)` 支持作用域；`InvokeFunctionAsync/InvokeFunction` 加 `scope` 参数（默认空=全量兼容） |
| `IEventPlatform.cs` | 接口签名同步（加 `scope=""` 可选参数） |
| `FunctionExecutedRenderBridge.cs` | `OnFunctionExecutedAsync` 读事件 `Scope`，非空时 `ExecuteRenderStage(scope)` 局部重渲染，空保持全量 |
> 效果：Side 作用域的函数执行触发重渲染时仅重渲染 Side，不再全量重放 MainLayout（内容区不再被误清）；配合 §5.2 收集器作用域过滤形成双保险

### 5.4 composition 生命周期保护（缓解）
- `EcsConfigContainer.Dispose()` / `EcsContainerCore.Dispose()` 不得释放**共享全局 composition**；仅解绑自身容器与订阅，composition 生命周期由 `EcsGate` 统一持有。
- 增加 `IScopeDestroyer` 对 composition 的引用计数/所有权声明。

### 5.5 渲染上下文按 scope 隔离（缓解）
- `_sharedContext` 改为「scope → RenderContext」字典，或每次执行新建上下文（放弃单例复用），避免跨作用域残留。

## 6. 待验证项

- 确切定位 `LoadSidePanel` 时报 disposed 的具体 Dispose 调用栈（候选：EcsConfigContainer.Dispose / EcsContainerCore.Dispose / EcsGate.Dispose）。
- 确认全量重渲染中 `WinFormsControlAdapter.Controls.Clear()` 的执行路径与触发条件。
- 验证 5.1~5.4 修复后，MainLayout + Content + Side 三作用域可共存且独立投影。

## 7. 附录：关键代码位置

| 组件 | 文件 |
|------|------|
| 全局清空/Dispose | `CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderPipelineComposition.cs`（`Clear`/`Dispose`） |
| 作用域销毁 | `CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderStageRunner.cs`（`DestroyScopeEntities`） |
| 全量重渲染重入 | `CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/FunctionExecutedRenderBridge.cs` |
| 收集器（无 scope） | `CsharpPrj/Operation/FrontEndDriven/Ports.cs`（`CsgEntryPoint.CollectSnapshot`） |
| 渲染上下文单例 | `CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderStageRunner.cs`（`_sharedContext`） |
| 容器 Dispose | `CsharpPrj/Operation/FrontEndDriven/ControlLookup/Container/EcsContainerCore.cs`、`EcsConfigContainer.cs` |
| 容器名→作用域映射 | `FileDriven/FileDriven.Business/Registrations/UI/UIToolbox.cs`（`ResolveScopeFromContainer`，已修复） |
| 作用域宿主绑定 | `CsharpPrj/Operation/FrontEndDriven/State/Projection/BootHostBindingRegistrar.cs` |

---

<!-- 本文档为框架层缺陷现状总结；修复实施后应回填验证结果并更新状态 -->
