# Entity 驱动执行整合方案（方向修正版）

> **定位**：函数调用链重构的实施细节文档（基于 Entity 为基底的架构方向）
> **关联文档**：`call_chain_refactoring_plan.md`（旧方案，方向修正说明见 §0）
> **日期**：2026-08-01
> **状态**：规划中（已核对现有代码，可落地实施）
> **状态图例**：⬜ = 未开始，🔄 = 部分完成/进行中，✅ = 已完成

---

## §0 方向修正说明

### 0.1 旧方案偏差

`call_chain_refactoring_plan.md`（R1）提出"`FunctionCallFacade` 唯一入口，废弃 EcsGate/Matrix 执行链，Task.Run 直达 RGE"。此方向经评审被判定为**反向架构**：

- 函数执行被抽出 ECS，变成"门面 Task.Run → RGE 直调"的服务调用
- `ExecutionRequestComponent` / `FunctionExecutionSystem` / `Matrix` / `ExecuteFunction` Handler / `ExecutionResultComponent` 本已是完整的 **Entity 流转链**，不该被废弃
- UI 的 ECS World 与 RGE 形成两套平行引擎，与"Entity 为一切执行的基础"相悖

### 0.2 修正方向（本方案）

**以 Entity 为骨架，门面为 API 壳，RGE 为 Handler 实现——三者整合为一条引擎：**

```
所有调用源 → FunctionCallFacade（统一 API + 审计）
   → 落 ExecutionRequestComponent Entity（进入 ECS World）
   → FunctionExecutionSystem（消费请求 → 提交 Matrix）
   → MatrixEngine.Router → "ExecuteFunction" Handler（执行实现 = RGE）
   → 写 ExecutionResultComponent Entity + ExecutionResultReadyEvent（结果回传）
```

- **Entity** 承载命令/请求/结果，全程在 ECS World 中流转（可查询、可审计、可扩展）
- **FunctionCallFacade** 作为唯一调用 API/审计壳（保留 `FunctionCallRequest`/`ExecutionMode`/超时/日志），**内部改走落 Entity 而非 Task.Run 直调**
- **RGE（WorkflowMethodExecutionService）** 是 `ExecuteFunction` Handler 内部的执行实现，不另起第二条引擎

### 0.3 与旧方案的关系

| 旧方案（废除） | 本方案（采用） |
|---------------|---------------|
| 门面 Task.Run 直达 RGE，绕开 ECS | 门面落 Entity，走 ECS 链，Handler 内部用 RGE |
| 废弃 FunctionExecutionSystem / Matrix / Handler | 保留为 Entity 执行链主体 |
| 删除 ExecutionResultReadyEvent 再渲染 | 保留为结果事件源（再渲染 + Sync 等待共用）|
| 函数执行与 UI ECS World 无关 | 命令/请求/结果全程 Entity 化 |

---

## §1 目标架构

### 1.1 组件职责

| 组件 | 职责 | 归属 |
|------|------|------|
| `FunctionCallFacade` | 唯一调用 API：请求校验、模式解析、审计、超时、结果适配 | `FrontEndDriven.Gate` |
| `FunctionCallRequest` | 统一请求模型（函数名/参数/来源/模式/超时） | `FrontEndDriven.Gate` |
| `EcsInputPort.EnqueueExecution` | 唯一的"入队点"：创建 `ExecutionRequestComponent` Entity | `FrontEndDriven.Ports` |
| `FunctionExecutionSystem` | ECS 消费端：扫描 `ExecutionRequestComponent` → 提交 Matrix | `FrontEndDriven.ECS.Systems` |
| `MatrixEngine` | 调度：`SubmitAsync` → 创建 Task Entity → Router 路由 | `ExecutionCenter.Matrix` |
| `AsyncHandlers["ExecuteFunction"]` | 执行实现：调 `IEventExecutionService`（= RGE）→ 写结果 Entity/事件 | `RenderPipeline.PipelineCompositionFactory` |
| `ExecutionResultComponent` | 结果状态 Entity（UI 可查询） | `TConfig.ECS.Components` |
| `ExecutionResultReadyEvent` | 结果就绪事件（TCS 等待 + 再渲染共用） | `FrontEndDriven.Gate` |
| `WorkflowMethodExecutionService` | RGE 执行引擎（Handler 内部实现） | `WorkFlowMethod.Usage` |

### 1.2 调用链全景（整合后）

```
【所有调用源：按钮点击 / InitCommands / exec / 命令解释器 / 脚本】
        │
        ▼
FunctionCallFacade.ExecuteAsync(FunctionCallRequest)      ── API/审计壳
        │  ① Fail-Fast 校验  ② 模式解析(Auto/Background/Sync/FireAndForget)
        ▼
EcsInputPort.EnqueueExecution(functionName, params)       ── 落 Entity
        │  → ExecutionRequestComponent Entity (@World)
        ▼
FunctionExecutionSystem.Update()（Idle 驱动）            ── ECS 消费端
        │  → ExecuteFunctionMessage → MatrixEngine.SubmitAsync
        ▼
RouterSystem → AsyncHandlers["ExecuteFunction"]          ── 执行实现
        │  内部调 IEventExecutionService（= RGE）
        ▼
写入 ExecutionResultComponent Entity                     ── 结果状态
   + store.Set("exec_result_{id}", ExecutionResultReadyEvent)  ── 结果事件
        │
        ├─ ExecutionLoopManager.ExecutionResultRerenderReactor → UI 再渲染
        └─ FunctionCallFacade 内部 Sync 等待桥接（TCS）→ 返回 FunctionCallResult
```

---

## §2 现状核对（基于 2026-08-01 代码事实）

### 2.1 已有且保留

| 组件 | 现状 | 保留依据 |
|------|------|---------|
| `EcsInputPort.EnqueueExecution` | `Ports.cs:925`，创建 `ExecutionRequestComponent` | 唯一入队点 |
| `FunctionExecutionSystem.Update` | `FunctionExecutionSystem.cs:68`，扫描并提交 Matrix | ECS 消费端 |
| `AsyncHandlers["ExecuteFunction"]` | `PipelineCompositionFactory.cs:123`，调 `execService` + 写结果 Entity/事件 | 执行实现 |
| `ExecutionResultComponent` | Handler 写入，UI 状态查询 | 结果状态 |
| `ExecutionResultReadyEvent` + `store.Set("exec_result_{id}")` | `PipelineCompositionFactory.cs:145` | 结果事件源 |
| `ExecutionLoopManager.ExecutionResultRerenderReactor` | 消费事件 → 再渲染 | 结果驱动 UI |
| `ExecutionRequestComponent` | `BasicMethod/ECS/Components` | Entity 请求承载体 |

### 2.2 已在前轮被改动（需调整）

| 文件 | 前轮改动 | 本方案调整 |
|------|---------|-----------|
| `FileDriven.Execution/Services/CommandExecutionService.cs` | 🔄 改为门面直调 `ExecuteSyncRequest`（RGE 直达） | **撤回** → 改为"EnqueueExecution + Sync 等待桥接"（§阶段 1） |
| `EcsGate.cs` | ✅ 删除 `EnqueueFunctionOperation` 分支 + `DoEnqueueFunc` | **保留删除**（该 ECS 操作 record 冗余；入队直接走 `EcsInputPort.EnqueueExecution`，无需经 EcsGate 中转）|

### 2.3 缺口

| 缺口 | 说明 | 补法 |
|------|------|------|
| Sync 等待结果 | Enqueue 后无 TCS 回传（`ExecutionResultReadyEvent` 仅被再渲染消费）| §阶段 1 新建桥接 |
| 门面落 Entity | 门面 Sync/Background 路径走 Task.Run 直调，不落 Entity | §阶段 2 改造 |
| `CommandExecutionService` 方向错误 | 已改为门面直调 | §阶段 0 撤回 |

---

## §3 分阶段实施细节

### 阶段 0：撤回门面直改，恢复 ECS 入队语义

**目标**：`CommandExecutionService.ExecuteSingle` 不再直调门面 RGE，改回"入队 + 等待"。

**文件**：`FileDriven/FileDriven.Execution/Services/CommandExecutionService.cs`

**现状**（需撤回）：
```csharp
var result = FunctionCallFacade.ExecuteSyncRequest(new FunctionCallRequest { ... });
return (result.IsSuccess, result.ErrorMessage, result.Output);
```

**目标**：
```csharp
var gate = AppServiceProvider.Resolve<EcsGate>();
if (gate?.Composition?.InputPort == null) return (false, "EcsGate/InputPort 未注册", null);

// ① 提交命令请求到门面（门面内部落 Entity）
var facade = AppServiceProvider.Resolve<...>();  // 或 FunctionCallFacade（静态）
var result = facade.ExecuteSyncWait(new FunctionCallRequest {
    FunctionName = commandText,
    Parameters = { ["value"]=commandText, ["command"]=commandText },
    Source = FunctionCallSource.CommandExecutor,
    Mode = ExecutionMode.Sync,
    TimeoutMs = 30_000
});
return (result.IsSuccess, result.ErrorMessage, result.Output);
```

> **说明**：本阶段依赖阶段 1 的"Sync 等待桥接"；若桥接未就绪，先落地阶段 1，再撤回此处。

**验收标准**：`CommandExecutionService.ExecuteSingle("函数名")` 返回真实执行结果而非恒 true；命令文本不再走 RGE 直调。

---

### 阶段 1：Sync 等待桥接（核心新增）

**目标**：Enqueue 后能同步/异步等待该 Entity 的 `ExecutionResultReadyEvent`，返回真实结果。

**方案 A（推荐）**：`EcsGate` 提供 `ExecuteFunctionAndWaitAsync`

**文件**：`CsharpPrj/Operation/FrontEndDriven/Gate/EcsGate.cs`

```csharp
/// <summary>
/// 执行函数并等待结果（Sync 桥接，Entity 为基）
/// <para>Enqueue → ExecutionRequestComponent Entity → ECS 链 → ExecuteFunction Handler
/// → ExecutionResultReadyEvent → TCS 完成。</para>
/// </summary>
public async Task<FunctionExecutionResult> ExecuteFunctionAndWaitAsync(
    string functionName,
    Dictionary<string, object?>? parameters = null,
    int timeoutMs = 30_000,
    CancellationToken ct = default)
{
    // ① 入队（落 Entity）
    var requestEntity = _composition.InputPort?.EnqueueExecution(functionName, parameters);
    if (requestEntity == null)
        return FunctionExecutionResult.Failure("InputPort 未初始化");

    // ② 构建 TCS，订阅 exec_result_{SourceEntityId}
    var tcs = new TaskCompletionSource<ExecutionResultReadyEvent>(
        TaskCreationOptions.RunContinuationsAsynchronously);

    // ★ 接入点需确认：IComponentDataStore 的订阅 API（Set 事件 / TryGet 轮询兜底）
    //   ExecutionLoopManager 已用 IEcsComponentReactor<ExecutionResultReadyEvent> 消费，
    //   桥接可注册独立 Reactor 或直接查 store（failure 路径用 Task.Delay 兜底）。
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(timeoutMs);

    try
    {
        var done = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token))
            .ConfigureAwait(false);
        if (done != tcs.Task)
            return FunctionExecutionResult.Timeout;   // 超时（复用现有静态）
        var evt = await tcs.Task.ConfigureAwait(false);
        return evt.ToResult();
    }
    catch (OperationCanceledException)
    {
        return FunctionExecutionResult.Timeout;
    }
}
```

**接入点待确认**（实施时核实）：
- `IComponentDataStore`（`pipelineCompositionFactory.cs:142` 的 `em.GetComponentDataStore()`）的订阅 API：确认是否有 `Set` 回调/事件，若无则需在 `ExecutionLoopManager.ExecutionResultRerenderReactor.React` 处转发（或注册第二个 Reactor）。
- `FunctionExecutionResult` 复用现有 record（`EcsOperation.cs`），避免新增结果类型。

**验收标准**：`EcsGate.ExecuteFunctionAndWaitAsync("函数名", params, 5_000)` 能返回真实 `IsSuccess/Output/ErrorMessage`；超时返回 `FunctionExecutionResult.Timeout`。

---

### 阶段 2：门面整合（落 Entity）

**目标**：`FunctionCallFacade` 从"Task.Run 直达 RGE"改为"落 Entity 走 ECS 链"。

**文件**：`CsharpPrj/Operation/FrontEndDriven/Gate/FunctionCallFacade.cs`

**改造点**：
1. `ExecuteSync` / `ExecuteAsyncInternal`：由 `ResolveExecutionService()` + `Task.Run(execService.ExecuteFunctionAsync)` 改为：
   ```csharp
   var gate = AppServiceProvider.Resolve<EcsGate>();
   var fnResult = await gate.ExecuteFunctionAndWaitAsync(request.FunctionName, request.Parameters, timeoutMs);
   // 适配为 FunctionCallResult（保留 ConcludeSuccess/ConcludeOutput/AdaptResult 现有逻辑）
   ```
2. **保留门面级审计**（`ExecuteWithAudit` 的 Entry/Exit 日志继续在门面层），避免双审计（`EcsGate.AuditLogInterceptor` 只记 ECS 操作，不记函数执行）。
3. `ExecuteFireAndForget`：标记 `[Obsolete]` 并注释"需结果消费通道"前置契约；当前无通道时降级为 Background（落 Entity + 等待）——语义见 §阶段 3.3。
4. 依赖注入问题：门面当前是 `static class`，`EcsGate` 是 DI 实例。需将门面改为**实例类 + DI 注册**（或保留静态但内部 `AppServiceProvider.Resolve<EcsGate>()`）。**倾向后者（改动最小，门面仍是静态工具壳）**。

**风险**：
- `EcsGate` 与 `FunctionCallFacade` 若互相解析会形成环。缓解：门面只单向依赖 `EcsGate`，`EcsGate` 不反向依赖门面。

**验收标准**：门面 `ExecuteAsync(FunctionCallRequest)` 走 ECS 链；`ExecutionResultComponent` Entity 被创建（UI 可查询）；门面返回真实结果。

---

### 阶段 3：结果链强化与语义澄清

#### 3.1 结果 Entity 完备（E2 修复）

`AsyncHandlers["ExecuteFunction"]`（`PipelineCompositionFactory.cs:123`）已写 `ExecutionResultComponent`。增强：
- 同步写 `RequestTimestamp`/`CompletedTimestamp`（已有）
- 增加 `bool 契约` 一致性：`Success` 取 `result.IsSuccess`；输出 `Result = result.Value?.ToString()`；`ErrorMessage` 透传（已有）——**无需改动，确认即可**。

#### 3.2 再渲染保留（B3 可观测性）

`ExecutionLoopManager.ExecutionResultRerenderReactor` 保留，作为"结果 → UI 再渲染"事件源。门面 B3 日志（`ButtonClick_FunctionResult`）保留，**双通道不冲突**（事件驱动渲染，日志供诊断）。

#### 3.3 FireAndForget 语义强化（R3/E1）

`FunctionCallFacade.ExecuteFireAndForget`：
- 标记 `[Obsolete("需结果消费通道；当前无通道时降级 Background")]`
- 实际执行：降级为 `ExecuteAsyncInternal`（落 Entity + 等待），消除"虚假成功"。
- 若未来需要真 fire-and-forget，先提供结果消费通道（事件订阅器）再恢复。

**验收**：`FireAndForget` 调用方收到真实结果；不再有 `IsSuccess=true` 虚假成功。

---

### 阶段 4：参数 / 错误 / 冗余收敛（沿用分析报告 D2-D5/E3）

#### 4.1 MetaCommand 参数转义修复（D3/E3）

**文件**：`FileDriven/FileDriven.Utilities/ScriptRunner/Core/Models/MetaCommandExtensions.cs`

`ParseKeyValuePairs`（L85 `part.IndexOf('=')`）修复 `=` 误拆：
```csharp
// 引号整体包裹 → 整段为值（无 key），放入 "value"
var trimmed = part.Trim();
if (trimmed.StartsWith('"') || trimmed.StartsWith('\''))
{
    result["value"] = trimmed.Trim('"', '\'');
    continue;
}
var eqIndex = trimmed.IndexOf('=');
// ...（现有逻辑）
```
支持 `"a=b.json"` 整体值、`key="val with = sign"` 引号包裹值。

#### 4.2 依赖解析重复收敛（D4）

`FunctionCallFacade.ResolveExecutionService` 已统一；`UIToolbox` 的 `LoadConfigToScope` 与 `LoadConfigToTarget` 的 `Resolve<ILoadConfigPort>() + null + 日志 + catch` 模式提取私有辅助 `TryResolveLoadConfigPort(out port)`。`MetaCommandExtensions.ParseKeyValuePairs` 与 `SandboxGate.ParseKeyValueParameters` 格式不同（`--key=value`），**不合并**，仅注释互引。

#### 4.3 作用域销毁三段式收敛（D5）

**文件**：`CsharpPrj/Operation/FrontEndDriven/Gate/EcsGate.cs`

提取私有方法：
```csharp
private void DestroyScope(string scopeName)
{
    var platform = _eventPlatform;
    platform?.Publish(PlatformEvent.Destroying(scopeName));
    _composition.DestroyScopeEntities(scopeName);
    platform?.Publish(PlatformEvent.Destroyed(scopeName));
}
```
`DoLoadConfig` / `DoClearScope` 复用（消除重复）。

#### 4.4 EventBindingInitializer 参数简化（D2）

`EventBindingInitializer.cs:127` 的 `parameters?.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value)` 在门面接受 `Dictionary<string, object?>` 时直接传参，无需逐项转换——确认 `FunctionCallRequest.Parameters` 类型后简化。

#### 4.5 统一错误策略（E5）

`EcsGate.ExecuteAsync` 顶层 `catch → OnError → throw` 保留（ECS 框架语义）；函数调用失败统一以 `FunctionExecutionResult` 返回（`ExecuteFunctionAndWaitAsync` 内部 catch 转 Failure）。调用方约定：**函数失败=结果对象，不抛异常**。

---

### 阶段 5：编译验证 + 冒烟

| 步骤 | 命令/动作 | 预期 |
|------|----------|------|
| 1 | `dotnet build CsharpPrj/MainProj.sln` | 0 错误 |
| 2 | `dotnet build FileDriven/FileDriven.sln` | 0 错误 |
| 3 | 冒烟：`CommandExecutionService.ExecuteSingle("...")` 返回真实结果 | 结果回传 |
| 4 | 冒烟：按钮点击 → `ExecutionResultComponent` Entity 创建 | Entity 化 |
| 5 | 冒烟：`ExecuteFunctionAndWaitAsync` 超时路径（5s） | `FunctionExecutionResult.Timeout` |

---

## §4 保留 / 删除清单

### 保留（Entity 链主体）

| 组件 | 理由 |
|------|------|
| `EcsInputPort.EnqueueExecution` | 唯一入队点 |
| `ExecutionRequestComponent` / `FunctionExecutionSystem` | ECS 消费端 |
| `MatrixEngine` / `RouterSystem` / `AsyncHandlers["ExecuteFunction"]` | 执行调度与实现 |
| `ExecutionResultComponent` / `ExecutionResultReadyEvent` | 结果 Entity/事件 |
| `ExecutionLoopManager.ExecutionResultRerenderReactor` | 结果驱动再渲染 |
| `FunctionCallFacade`（改内部落 Entity） | 统一 API/审计壳 |
| `WorkflowMethodExecutionService`（RGE） | Handler 内部执行实现 |
| `SubmitToMatrixOperation` / `MatrixEngine.SubmitAsync`（显式 Matrix 编排） | 非函数执行旁路，保留 |

### 删除（已完成 / 冗余）

| 组件 | 依据 |
|------|------|
| `EnqueueFunctionOperation` record（`EcsOperation.cs`）| ✅ 已删（无构造调用方；入队直接走 `EcsInputPort.EnqueueExecution`）|
| `EcsGate.DoEnqueueFunc` / Dispatch 分支 | ✅ 已删 |
| `CommandExecutionService` 的门面 RGE 直调 | 🔄 阶段 0 撤回为 ECS 入队 + 等待 |
| `FunctionCallFacade` 的 Task.Run 直达路径 | 🔄 阶段 2 改为落 Entity |

---

## §5 风险与回滚

| 风险 | 缓解 | 回滚 |
|------|------|------|
| `IComponentDataStore` 无同步订阅 API，Sync 等待难实现 | 复用 `ExecutionResultRerenderReactor` 转发 TCS；或 `Task.Delay` 轮询 `exec_result_{id}` 兜底 | 阶段 1 暂缓，先用门面直调过渡 |
| 门面 static ↔ `EcsGate` 实例依赖环 | 门面单向依赖 `EcsGate`（`AppServiceProvider.Resolve`），`EcsGate` 不反依赖门面 | 撤回阶段 2，门面保留直调，ECS 链仅命令走 |
| 退役 RGE 直调后同步场景变慢（多一层 ECS 调度）| 仅命令/按钮走 ECS 链；高频内部调用可保留门面直调（`mode=Direct` 扩展）| 恢复 Task.Run 直达分支 |
| `ExecuteFunctionOperation`（Obsolete 备用）仍被引用 | 已有 `[Obsolete]` 告警；确认零调用后删除 | 保留 record |

---

## §6 实施批次

| 批次 | 内容 | 依赖 | 估时 |
|------|------|------|------|
| P1 | 阶段 1（Sync 桥接）→ 阶段 0（撤回门面直改）| 无 | 中 |
| P2 | 阶段 2（门面落 Entity）+ 阶段 3.3（FireAndForget 降级）| P1 | 中 |
| P3 | 阶段 4（参数/错误/销毁收敛）| 无（可并行 P1 前独立完成）| 中 |
| P4 | 阶段 5（编译 + 冒烟）| P1-P3 | 小 |

> **建议**：先 P1（打通"入队 → ECS 链 → 结果 TCS"闭环），再 P2（门面整合），P3 可提前做（无依赖）。