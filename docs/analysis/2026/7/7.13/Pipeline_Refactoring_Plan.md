# Pipeline 模块修复方案文档

> 生成日期：2026-07-13
> 基于对 `CsharpPrj/Modules/Pipeline` 目录的全面代码审查

---

## 第一阶段：Bug 修复（高优先级）

### 修复 1：PipelineStageBase 异常静默吞掉

**问题**：`PipelineStageBase.ExecuteAsync` 捕获异常后只设置失败状态，不重新抛出，导致 `StagePipeline` 无法感知错误详情。

**修复**：在 `catch` 块中添加 `throw` 重新抛出异常。

**文件**：`Core.cs` — `PipelineStageBase.ExecuteAsync`

---

### 修复 2：PipelineRetry 死代码

**问题**：`ExecuteCore` 和 `ExecuteCoreAsync` 中循环后的 `throw` 语句永远无法到达。

**修复**：将循环逻辑改为 `while(true)` + 条件判断，删除不可达代码。

**文件**：`Combinators.cs` — `PipelineRetry.ExecuteCore` / `ExecuteCoreAsync`

---

### 修复 3：PipelineHelper.WaitForStagesAsync 静默失败

**问题**：前置阶段等待失败时只留空注释，无任何处理。

**修复**：等待失败时抛出 `InvalidOperationException`。

**文件**：`Initialization/PipelineHelper.cs` — `WaitForStagesAsync`

---

### 修复 4：PipelineContext.Cancel() 取消状态不一致

**问题**：释放旧 CTS 后赋值新 token，可能导致状态不一致。

**修复**：如果已有 `_cancelCts` 且未取消，直接复用；否则创建新的 CTS 并取消。

**文件**：`Core.cs` — `PipelineContext.Cancel()`

---

### 修复 5：Pipeline<TInput,TOutput>.ExecuteStages 类型转换风险

**问题**：`ExecuteStages` 中使用 `object? current = input` 做链式传递，然后强制 `(TInput)current!`。但 `current` 被赋值为 `result`（类型 `TOutput?`）后，下一个 stage 的输入类型仍然是 `TInput`，当 `TInput != TOutput` 时抛出 `InvalidCastException`。

**根因**：代码逻辑与文档注释矛盾。文档说"所有 stage 都接收原始 input"，但代码却做了链式传递。

**修复**：删除 `current` 变量，所有 stage 直接使用原始 `input` 参数，与文档注释保持一致。

**文件**：`Core.cs` — `Pipeline<TInput, TOutput>.ExecuteStages()`

---

## 第二阶段：冗余清理（中优先级）

### 清理 1：删除所有已废弃代码

删除以下 11 个 `[Obsolete]` 标记的类/方法：

| 文件 | 删除内容 | 替代方案 |
|------|---------|---------|
| `Core.cs` | `PipelineCore` 类 | `PipelineErrorHandler` + `Pipeline` |
| `Handlers.cs` | `StructureHandler` 类 | `StagePipeline<TContext>` |
| `Handlers.cs` | `MiddlewarePipeline<TRequest,TResponse>` 类 | `GenericMiddlewarePipeline<TData,TContext>` |
| `Handlers.cs` | `ReferenceTrackingMiddleware` 类 | `GenericMiddlewarePipeline` |
| `Handlers.cs` | `OperationResult` 类 | `PipelineResult` |
| `Registry.cs` | `SequentialStrategy` 类 | `Then` / `WithStages` |
| `Registry.cs` | `ParallelStrategy` 类 | `ParallelAll` / `ParallelPipeline` |
| `Registry.cs` | `ConditionalStrategy` 类 | `ConditionalPipeline` / `Branch` |
| `Registry.cs` | `StrategyRegistry<TKey,TOutput>` 类 | `StrategyRegistry<TKey,Unit,TOutput>` |
| `Registry.cs` | `Unit` 结构体 | `SkeletonFlow.Unit` |
| `PipelineUsage.cs` | `CreateLegacyMiddlewarePipeline()` 方法 | `CreateMiddlewarePipeline()` |

---

### 清理 2：精简 PipelineUsage 外观类

**问题**：766 行的外观类几乎全是简单委托。

**修复**：删除纯委托方法，仅保留有实际价值的工厂方法。

---

### 清理 3：删除 CentralPipeline 目录中的重复 PipelineStage 枚举

**问题**：`CentralPipeline/PipelineStage.cs` 中的枚举只是 `BP.PipelineStage` 的别名映射。

**修复**：删除 `CentralPipeline/PipelineStage.cs`，所有引用改为直接使用 `BP.PipelineStage`。

---

## 第三阶段：架构重构（低优先级，可选）

### 重构 1：统一重试/异常/条件逻辑

**方案**：选择类版本作为标准实现，删除对应的委托版本。

### 重构 2：消除同步/异步对称代码

**方案**：为 `PipelineBase` 添加 `IsAsyncPreferred` 属性，默认同步委托给异步。

### 重构 3：统一编排体系

**方案**：将体系 A/C 和 CentralPipeline 合并为一套核心编排器。

---

## 执行计划

| 阶段 | 任务 | 预估工作量 | 风险 |
|------|------|-----------|------|
| 一 | Bug 修复（4 项） | 小（2-3 小时） | 低 |
| 二 | 冗余清理（3 项） | 中（4-6 小时） | 中 |
| 三 | 架构重构（3 项） | 大（2-3 天） | 高 |
