# BackEndDriven 执行结果模型收敛与命令执行入口聚拢方案

> **日期**：2026-08-05
> **目标目录**：`CsharpPrj/Operation/BackEndDriven` + `CsharpPrj/Plate/TConfig`（最小跨模块边界）
> **关联**：上一轮分析（重复/冗余/错误逻辑）之 **1.6 三套执行结果模型** 与 **1.2 命令执行双入口** 两项的重构方案与实施记录。
> **原则对齐**：clinerules `Strategy.SingleImplementation`（单一实现）、`Strategy.RedundancyElimination`（无消费方冗余即清除）。

---

## 0. 现状与问题

### 0.1 执行结果模型三套并存于 BackEndDriven

| 类型 | 位置 | 字段要点 | 消费方 | 与 TConfig 既有抽象的关系 |
|------|------|----------|--------|---------------------------|
| `Models.ExecutionResult` + `IExecutionResult` | `Models/ExecutionResult.cs` | Success/TotalCommands/SuccessCount/FailCount/CommandResults/ExecutionTime/ErrorMessage/Mode/BatchId/FunctionResultValue | `FrameworkExecutor` | 命令**批次统计**，语义与 `TConfig.ExecutionResult`（通用单次）不同 |
| `Models.CommandLineResult` | 同上文件 | LineNumber/CommandText/Success/Message/Duration | `CommandPipelineContext`、`IBackEndPipelineContext` | 批次内**单命令记录**，TConfig 无对应 |
| `Models.EventTriggerResult` + `IEventTriggerResult` | `Models/EventTriggerResult.cs` | IsSuccess/Value/ErrorMessage/FunctionName/ExecutionTime | **全仓无消费（死代码）** | `TConfig.IEventTriggerResult`（IsSuccess/ErrorMessage/Value）已覆盖其语义 |
| `Gate.FunctionCallResult` | `Gate/FunctionCallResult.cs` | IsSuccess/FunctionName/Source/Mode/Resolved/IsWorkflow/NodeCount/Output/ErrorMessage/ElapsedMs | 仅 `FunctionCallFacade` | `TConfig.IEventPlatform.FunctionCallFacadeResult`（IsSuccess/Output/ErrorMessage/ElapsedMs）已覆盖其内核字段 |

### 0.2 全仓 `ExecutionResult` 命名冲突（根因）

全仓 10+ 个不同模块各自声明 `ExecutionResult`（仅名为 `ExecutionResult.cs` 的就有 3 处）：
`BackEndDriven.Models`、`TConfig`（`Models/ExecutionModels.cs`）、`Share.FrameworkAbstractions.Utilities`、
`UniversalWorkflowFramework.Core.Models`、`WorkFlowMethod.Execution.Messages`、`ExecutionFramework.Engine`、
`ExecutionCenter`、`Commands` 等。**同名异构**是模型重复的根源，收敛目标统一到 `TConfig` 权威。

### 0.3 命令执行双入口

`BackEndDriven` 内两处 `Resolve<IEventPlatform>() + InvokeFunction{Async}(...)` 重复拼装：

| 入口 | 位置 | 调用 |
|------|------|------|
| `ExecSystem.Execute` | `CommandSystems.cs:199-208` | `platform.InvokeFunction(target, parameters, "ExecCommand", 30_000)` |
| `CommandInputPort.ExecuteSingleWorkflowAsync` | `CommandInputPort.cs:117-126` | `platform.InvokeFunctionAsync(commandText, namedParams, "ExecCommand", 30_000)` |

两者：source=`"ExecCommand"`、timeout=`30_000`、FuncParam 拼装、`FunctionCallFacadeResult` 消费归一 —— 全部重复。
统一聚拢点为 `TConfig.IEventPlatform`（已是公共平台唯一函数调用入口，见既有 8.2 方案）。

---

## 1. 收敛目标

1. **删除死代码** `BackEndDriven.Models.EventTriggerResult` / `IEventTriggerResult`（用 TConfig 抽象替代）。
2. **`CommandLineResult` 收敛到 TConfig**：新增 `TConfig.CommandLineResult`，BackEndDriven 除本地副本并经 `using` 别名绑定。
3. **命令执行入口聚拢**：新增 BackEndDriven 统一执行门面，`ExecSystem` 与 `CommandInputPort` 复用，消除 Resolve+Invoke+归一三处重复。
4. **文档化（后续批次）**：`FunctionCallResult`/`FunctionCallFacadeResult`/`ExecutionResult` 全仓命名冲突治理（涉 FrontEndDriven/ExecutionFramework/UniversalReference，破坏面大，不在本轮改动跨模块消费方）。

---

## 2. 依赖与可行性确认（已核实）

- `IEventPlatform`（含 `FunctionCallFacadeResult`）位于 `Share/EventBindingOrchestrator/Abstractions`，是公共平台统一入口，已被 `ExecSystem`/`CommandInputPort` 使用 —— 聚拢落点确定。
- `IEventExecutionService`、`IEventTriggerResult`、`IFunctionExecutionResult`、`ParameterExecutionResult`、`ExecutionResult` 均在 `Plate/TConfig` 命名空间 —— TConfig 已是"执行结果 + 执行服务"的权威容器，符合"集中配置在 TConfig"。
- `TConfig.csproj` 已被 `BackEndDriven.csproj` 引用（`Plate\TConfig\TConfig.csproj`），新增 `TConfig.CommandLineResult` 无循环依赖风险。

### 依赖方向（收敛后）

```
BackEndDriven ──using──> TConfig(IEventExecutionService/IEventPlatform.FunctionCallFacadeResult/CommandLineResult/IEventTriggerResult)
        ▲
        │（反向：被消费）
FrontEndDriven ──IEventPlatform──> BackEndDriven.FunctionCallFacade（外部契约仅用 FunctionCallFacadeResult）
```

---

## 3. 实施步骤

### 3.1 ✅ 已实施：删除死代码 `Models/EventTriggerResult.cs`

- 全仓 `BackEndDriven.Models.EventTriggerResult/IEventTriggerResult` **无任何消费方**。
- 语义已由 `TConfig.IEventTriggerResult`（IsSuccess/ErrorMessage/Value）覆盖（TConfig 已有 `EventResult : IEventTriggerResult` 具体实现）。
- 处理：删除文件。**构建验证通过。**

### 3.2 ✅ 已实施：`CommandLineResult` 收敛到 TConfig

1. 新增 `Plate/TConfig/Models/CommandLineResult.cs`（`namespace TConfig`，字段：LineNumber/CommandText/Success/Message/Duration）。
2. `BackEndDriven/Models/ExecutionResult.cs`：删除本地 `CommandLineResult`，加别名 `using CommandLineResult = TConfig.CommandLineResult;`。
3. `CommandPipelineContext.cs`：别名改为 `using CommandLineResult = TConfig.CommandLineResult;`（line 13）。
4. `IBackEndPipelineContext.cs`：加别名 `using CommandLineResult = TConfig.CommandLineResult;`。
5. **构建验证通过**（`BackEndDriven`、`Operation` 聚合 0 错误）。

### 3.3 ✅ 已实施：命令执行入口聚拢

1. 新增 `CommandExecution/EventPlatformExecutor.cs`：封装 `Resolve<IEventPlatform>` + `InvokeFunction{Async}` + `ExecCommand`/`DefaultTimeoutMs` 常量 + `FunctionCallFacadeResult` 归一；
   提供 `EventPlatformInvocation` 统一返回（`Result`/`Error` + `PlatformAvailable`，避免 async/out 参数限制）。
2. `ExecSystem.Execute`：改经 `EventPlatformExecutor.Invoke`（同步）。
3. `CommandInputPort.ExecuteSingleWorkflowAsync`：改经 `EventPlatformExecutor.InvokeAsync`（异步）。
4. 移除两文件残留的 `using Share.EventBindingOrchestrator.Abstractions`（`IEventPlatform` 直接用途已聚拢到门面）。
5. **构建验证通过**；`FunctionCallFacade` 仍为执行内核（被 `EventPlatform` 反向调用，不归入命令层门面，避免循环依赖）。

### 3.4（本轮已实施）`FunctionCallResult` 收敛到 `FunctionCallFacadeResult`

**目标**：消除 BackEndDriven 内部执行结果与外部契约（`TConfig.IEventPlatform.FunctionCallFacadeResult`）的重复投影。

**实施**：
1. `FunctionCallFacade.ExecuteAsync` / `ExecuteSyncRequest` / `ExecuteCore` / `AdaptResult` 返回类型由 `BackEndDriven.Gate.FunctionCallResult` 改为 `TConfig.FunctionCallFacadeResult`（`Share.EventBindingOrchestrator.Abstractions`）。
2. 替换 `FunctionCallResult.Success/Failure` 静态工厂为 `new FunctionCallFacadeResult(success, output, error, elapsedMs)`；审计所需 FunctionName/Source/Mode 保留在 `BackEndLog` 副作用中（`FunctionCallFacade_summary`/`_timeout`/`_error` 已含）。
3. 删除死类型 `Gate/FunctionCallResult.cs`（其 `Resolved/IsWorkflow/NodeCount` 字段从未被赋值，为死字段）。
4. `FrontEndDriven/Lifecycle/EventPlatform.cs`（跨模块消费方）：去掉 `new FunctionCallFacadeResult(result.IsSuccess, result.Output, ...)` 重复投影，直接 `return result`。
5. **构建验证通过**（`BackEndDriven` + `Operation` 聚合含 FrontEndDriven，0 错误）。

### 3.5 `ExecutionResult` 全仓命名冲突治理 —— 分析结论（非盲改）

**调研结论**：全仓同名 `ExecutionResult` 的多个定义**分属不同职责对象**，并非全部可统一：

| 定义 | 职责 | 治理建议 |
|------|------|----------|
| `TConfig.ExecutionResult`（`ExecutionModels.cs`） | 通用单次执行结果（执行框架权威） | **保留为权威**；贯穿 TConfig/WorkFlowMethod/ExecutionFramework 执行链 |
| `BackEndDriven.Models.ExecutionResult`/`IExecutionResult` | 命令**批次统计**（TotalCommands/SuccessCount/FailCount/BatchId/Mode） | 职责不同，**保留**在 `BackEndDriven.Models`（命名空间已隔离），不并入 TConfig |
| `Share.FrameworkAbstractions.Utilities.ExecutionResult<TContext>` | 上下文化结果 | **保留**（职责不同，泛型上下文） |
| `UniversalWorkflowFramework.Core.Models.ExecutionResult` | UniversalReference 执行结果 | 跨模块，保留 |
| `ExecutionFramework.*` 各 ExecutionResult | 引擎执行结果 | 执行框架内部，保留 |

**结论**：此类"同名异构"**不应强行合并**（字段语义各不同，合并会破坏职责隔离）。治理方向改为「**命名空间职责声明 + 显式 using 别名**」：
- 保留各模块职责模型；依赖方需跨模块消费时用 `using XResult = 目标命名空间.XResult;` 显式绑定（本轮已在 BackEndDriven 用 `using CommandLineResult = TConfig.CommandLineResult;` 示范）。
- 未来如需彻底消歧，可在各模块 root namespace 层加 `global using` 别名，属横切演进，单列批次，不擅自推行。

> 本轮已落地的真重复：`FunctionCallResult`（与 `FunctionCallFacadeResult` 字段重复）已收敛删除；`EventTriggerResult`（死代码）已删；`CommandLineResult` 已归一 TConfig。

---

## 4. 验证策略（已执行）

- `dotnet build CsharpPrj/Operation/BackEndDriven/BackEndDriven.csproj -p:GenerateDocumentationFile=false` → **0 警告 0 错误**
- `dotnet build CsharpPrj/Operation/Operation.csproj ...`（含 FrontEndDriven/StateHoldDriven 下游）→ **0 警告 0 错误**
- grep 断言 `BackEndDriven.Models.CommandLineResult / EventTriggerResult / IEventTriggerResult` 不再出现（仅 TConfig 新文件注释提及来源）。

---

## 5. 风险与边界

| 风险 | 缓解 |
|------|------|
| `CommandLineResult` 移至 TConfig 影响 `CommandPipelineContext`/`IBackEndPipelineContext` 绑定 | 依 alias 绑定，BackEndDriven 内部无行为变化；构建验证 |
| `ExecSystem` 同步 / `CommandInputPort` 异步两个入口超时语义 | 统一 `EventPlatformExecutor` 保留 sync/async 两分发，source/timeout 收敛为常量，语义不变 |
| 跨模块 `ExecutionResult` 冲突 | 本轮不触碰 FrontEndDriven/ExecutionFramework 等消费方；仅文档化治理方案 |
