# Pipeline 代码分析报告

> 分析日期：2026-06-04
> 分析范围：Pipeline 目录下所有文件（含 CentralPipeline 子目录）
> 分析类型：重复逻辑、错误逻辑、冗余逻辑

---

## 目录

1. [重复逻辑（Duplicated Logic）](#1-重复逻辑duplicated-logic)
2. [错误逻辑（Buggy Logic）](#2-错误逻辑buggy-logic)
3. [冗余逻辑（Redundant Logic）](#3-冗余逻辑redundant-logic)
4. [修复建议优先级](#4-修复建议优先级)

---

## 1. 重复逻辑（Duplicated Logic）

### 1.1 `ConditionalPipeline<TContext>` 与 `ConditionalSkeleton<TContext>` 重复 ⚠️ 严重

| 文件 | 类 |
|------|------|
| `Combinators.cs` (L652-696) | `ConditionalPipeline<TContext>` — 基于 `IPipeline` 的条件执行 |
| `CentralPipeline/_Deprecated/ConditionalSkeleton.cs` (L20-64) | `ConditionalSkeleton<TContext>` — 基于 `ISkeleton` 的条件执行 |

**问题**：两者逻辑完全相同——条件满足时执行内部逻辑，不满足时跳过。只是分别包装了 `IPipeline` 和 `ISkeleton`。`ConditionalSkeleton` 放在 `_Deprecated` 目录中但**没有被标记为 `[Obsolete]`**，且 `ConditionalSkeletonExtensions` 扩展方法仍然可用，使用者可能误用。

### 1.2 `PipelineAdapter<TContext, TIn, TOut>` 与 `SkeletonAdapter<TContext, TIn, TOut>` 重复 ⚠️ 严重

| 文件 | 类 |
|------|------|
| `Combinators.cs` (L713-761) | `PipelineAdapter<TContext, TIn, TOut>` — 基于 `IPipeline` 的适配器 |
| `CentralPipeline/_Deprecated/SkeletonAdapter.cs` (L23-83) | `SkeletonAdapter<TContext, TIn, TOut>` — 基于 `ISkeleton` 的适配器 |

**问题**：两者功能完全相同——从 `TContext` 提取 `TIn`，执行业务逻辑，将 `TOut` 写回 `TContext`。`SkeletonAdapter` 放在 `_Deprecated` 目录但**未标记 `[Obsolete]`**，且 `SkeletonAdapterFactory` 和 `AsyncSkeletonAdapter` 仍然可用。

### 1.3 `PipelineBuilder<TContext>` 与 `PipelineBuilder`（Core.cs）重复

| 文件 | 类 |
|------|------|
| `Core.cs` (L479-515) | `PipelineBuilder` — 已标记 `[Obsolete]` |
| `CentralPipeline/PipelineBuilder.cs` (L19-226) | `PipelineBuilder<TContext>` — 功能完善的 Fluent API 构建器 |

**问题**：`Core.cs` 中的 `PipelineBuilder` 已被标记为废弃，但 `CentralPipeline` 中的 `PipelineBuilder<TContext>` 提供了完全不同的 Fluent API。两者名称相同但功能不同，容易混淆。

### 1.4 `PipelineStageRegistry` 重复定义

| 文件 | 类 |
|------|------|
| `Registry.cs` (L324-440) | `PipelineStageRegistry` — 基于 `PipelineStage` 枚举的注册表 |
| `CentralPipeline/PipelineStageRegistry.cs` (L20-309) | `PipelineStageRegistry<TContext>` — 泛型版本 |

**问题**：`Registry.cs` 中的 `PipelineStageRegistry` 使用 `PipelineStage` 枚举（9个阶段），而 `CentralPipeline` 中的 `PipelineStageRegistry<TContext>` 使用 `Share.CentralPipeline.PipelineStage` 枚举（5个阶段）。两者管理方式不同，但概念重复。

### 1.5 `PipelineStage` 枚举重复定义

| 文件 | 枚举 |
|------|------|
| `Models.cs` (L35-55) | `PipelineStage` — 9个阶段（Initialize~Complete） |
| `CentralPipeline/PipelineStage.cs` (L16-32) | `PipelineStage` — 5个阶段（Initialization~Finalization） |

**问题**：`CentralPipeline.PipelineStage` 是 `BasicMethod.Pipeline.PipelineStage` 的映射别名，但命名不同（`Initialization` vs `Initialize`），使用者需要在两个枚举间转换。

### 1.6 `ParallelPipeline<TContext>` 与 `ParallelStrategy` + `PipelineExtensions.Parallel` 三重重复 ⚠️ 严重

| 文件 | 类/方法 |
|------|------|
| `Combinators.cs` (L162-271) | `ParallelPipeline<TContext>` — 类实现 |
| `Handlers.cs` (L284-293) | `PipelineExtensions.Parallel` — 两个管线并行 |
| `Handlers.cs` (L348-389) | `PipelineExtensions.ParallelAll` — 多个管线并行 |
| `Registry.cs` (L224-261) | `ParallelStrategy` — 策略模式 |

**问题**：四种并行执行方式，每种都有同步/异步双实现，代码高度重复。`ParallelPipeline<TContext>` 和 `ParallelStrategy` 都支持多个管线并行，但 API 完全不同。

### 1.7 `PipelineTryCatch<TContext>` 与 `PipelineExtensions.TryCatch` 重复

| 文件 | 类/方法 |
|------|------|
| `Combinators.cs` (L422-526) | `PipelineTryCatch<TContext>` — 类实现（支持降级路径） |
| `Handlers.cs` (L467-496) | `PipelineExtensions.TryCatch` — 扩展方法（仅异常处理） |

**问题**：`PipelineTryCatch<TContext>` 支持降级路径（fallback），而 `TryCatch` 扩展方法仅支持异常处理和回调。功能重叠但能力不同，使用者需要了解两者差异。

### 1.8 `PipelineRetry<TContext>` 与 `PipelineExtensions.WithRetry` 重复

| 文件 | 类/方法 |
|------|------|
| `Combinators.cs` (L540-636) | `PipelineRetry<TContext>` — 类实现 |
| `Handlers.cs` (L614-655) | `PipelineExtensions.WithRetry` — 扩展方法 |

**问题**：两者重试逻辑完全相同，只是 API 风格不同（类 vs 扩展方法）。`PipelineRetry<TContext>` 多了 `onRetry` 回调。

---

## 2. 错误逻辑（Buggy Logic）

### 2.1 `Pipeline<TInput, TOutput>.ExecuteStages` 注释与实现矛盾 🚨 严重 Bug

**文件**：`Core.cs` (L964-968)

```csharp
/// <summary>
/// 执行泛型阶段列表的公共逻辑
/// <para>注意：使用链式传递，每个 stage 接收上一个 stage 的输出作为输入。
/// 第一个 stage 接收原始 input。</para>
/// </summary>
```

**问题**：注释声称"使用链式传递"，但实际代码（L982）是：
```csharp
result = stage.Process((TInput)current!, errors, context);
```
每次都强制将 `current` 转型为 `TInput`。如果某个 stage 的输出类型不是 `TInput`（这是完全可能的，因为 `IPipelineStage<TInput, TOutput>` 的 `Process` 返回 `TOutput?`），这里会抛出 `InvalidCastException`。**链式传递实际上被类型约束破坏了**。

### 2.2 `ForEachPipeline`（CentralPipeline 版本）缺少 `breakOnError` 参数

**文件**：`CentralPipeline/ForEachPipeline.cs` (L27-37)

```csharp
public ForEachPipeline(
    ...
    bool breakOnError = false,  // ← 这个参数被硬编码为 false！
    ...)
    : base(..., breakOnError: false, ...)  // ← 始终传 false
```

**问题**：`CentralPipeline.ForEachPipeline` 的构造函数没有暴露 `breakOnError` 参数，而是硬编码为 `false`。而基类 `BasicMethod.Pipeline.ForEachPipeline` 是支持这个参数的。这意味着 CentralPipeline 的使用者无法控制错误中断行为。

### 2.3 `PipelineContext.Cancel()` 方法每次创建新的 `CancellationTokenSource` 🚨 严重 Bug

**文件**：`Core.cs` (L168-175)

```csharp
public void Cancel(string? reason = null)
{
    CancelReason = reason;
    var cts = new CancellationTokenSource();
    cts.Cancel();
    CancellationToken = cts.Token;
}
```

**问题**：每次调用 `Cancel()` 都创建一个新的 `CancellationTokenSource` 并立即取消，然后赋值给 `CancellationToken` 属性。但：
1. 旧的 `CancellationTokenSource` 没有被释放（`Dispose`），造成资源泄漏
2. 如果外部代码持有旧的 `CancellationToken`，将无法感知取消
3. 多次调用 `Cancel()` 会产生多个未释放的 `CancellationTokenSource`

### 2.4 `PipelineContext.Reset()` 不重置 `CancelReason`

**文件**：`Core.cs` (L145-154)

```csharp
public void Reset()
{
    _stageSuccess.Clear();
    _stageErrors.Clear();
    foreach (var key in Items.Keys.ToList())
        RemoveItem(key);
    Mode = "Default";
    SourceFile = null;
    // 缺少：CancelReason = null;
}
```

**问题**：`Reset()` 清除了阶段状态、Items、Mode、SourceFile，但**没有重置 `CancelReason`**。如果之前调用过 `Cancel("some reason")`，重置后 `CancelReason` 仍然保留旧值。

### 2.5 `PipelineResult.ToGeneric<T>()` 可能丢失数据

**文件**：`Core.cs` (L405-407)

```csharp
public PipelineResult<T> ToGeneric<T>() where T : class
    => new(IsSuccess, IsAborted, Data as T, default, Errors);
```

**问题**：`RawData` 被设为 `default`（即 `null`），丢失了原始数据。且 `Status` 枚举（`Success`/`Failed`/`Aborted`/`PartialSuccess`）被简化为 `IsSuccess`/`IsAborted` 布尔值，丢失了 `PartialSuccess` 状态。

### 2.6 `PipelineResult<T>.ToNonGeneric()` 丢失 `PartialSuccess` 状态

**文件**：`Core.cs` (L465-467)

```csharp
public PipelineResult ToNonGeneric(PipelineContext context)
    => new(Data, Errors, context,
        IsSuccess ? PipelineResultStatus.Success : IsAborted ? PipelineResultStatus.Aborted : PipelineResultStatus.Failed);
```

**问题**：`CompletedWithWarnings` 状态（`IsSuccess = true`）被映射为 `PipelineResultStatus.Success`，丢失了"部分成功"的语义。

### 2.7 `ParallelPipeline.ExecuteCore` 同步版本不支持并行

**文件**：`Combinators.cs` (L193-209)

```csharp
protected override TContext ExecuteCore(TContext input, PipelineContext context)
{
    foreach (var pipeline in _pipelines)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        try { pipeline.Execute(input, context); }
        catch (Exception) { if (!_continueOnError) throw; }
    }
    return input;
}
```

**问题**：名为"并行管线"的同步版本实际上是**串行执行**。虽然注释说明了"并行执行仅在异步版本中生效"，但这违反了最小惊讶原则——使用者调用 `Execute` 期望并行，实际得到串行。

### 2.8 `StrategyRegistry<TKey, TOutput>` 未标记为 `[Obsolete]` 但引用了已废弃的 `Unit` 类型

**文件**：`Registry.cs` (L117-161)

```csharp
[Obsolete("请使用 SkeletonFlow.Unit 替代。")]
public readonly struct Unit { }
```

`StrategyRegistry<TKey, TOutput>` 使用了 `IStrategy<Unit, TOutput>`，但 `Unit` 已被标记为 `[Obsolete]`。这意味着使用 `StrategyRegistry<TKey, TOutput>` 会产生废弃警告。

---

## 3. 冗余逻辑（Redundant Logic）

### 3.1 `CentralPipeline` 子目录中的转发类型（Wrapper Classes）

**文件**：`CentralPipeline/PipelineTryCatch.cs`、`PipelineBranch.cs`、`ForEachPipeline.cs`、`ParallelPipeline.cs`

**问题**：这四个文件中的类（`PipelineTryCatch<TContext>`、`PipelineRetry<TContext>`、`PipelineSwitch<TContext, TKey>`、`PipelineBranch<TContext>`、`ForEachPipeline<TContext, TItem>`、`ParallelPipeline<TContext>`）都是**纯粹的转发类型**——它们继承自 `BasicMethod.Pipeline` 中对应的类，构造函数完全委托给基类，没有添加任何新逻辑。

这些类存在的唯一目的是为了在 `Share.CentralPipeline` 命名空间下提供类型别名，确保旧代码引用不中断。但这种方式增加了维护负担——每次基类构造函数签名变化，所有转发类都需要同步更新。

### 3.2 `SkeletonToPipelineAdapter` 放在 `_Deprecated` 目录但仍在被使用

**文件**：`CentralPipeline/_Deprecated/SkeletonToPipelineAdapter.cs`

**问题**：这个适配器被 `CentralPipeline<TContext>.Register()` 方法（`CentralPipeline.cs` L97-102）直接使用，但它却放在 `_Deprecated` 目录中。这会造成维护者的困惑——到底这个类是否应该被使用？

### 3.3 `SkeletonAdapter` 和 `AsyncSkeletonAdapter` 与 `PipelineAdapter` 功能重叠

**文件**：
- `CentralPipeline/_Deprecated/SkeletonAdapter.cs` — `SkeletonAdapter<TContext, TIn, TOut>` + `AsyncSkeletonAdapter<TContext, TIn, TOut>`
- `Combinators.cs` — `PipelineAdapter<TContext, TIn, TOut>`

**问题**：三个适配器类功能完全相同，只是分别适配 `ISkeleton` 和 `IPipeline`。`SkeletonAdapterFactory` 进一步增加了不必要的复杂度。

### 3.4 `PipelineBuilderExtensions` 中的 `RegisterAdapter` 和 `RegisterAsyncAdapter` 已废弃

**文件**：`CentralPipeline/PipelineBuilder.cs` (L236-266)

```csharp
[Obsolete("直接 Skeleton 化后不再需要适配器。...")]
public static PipelineBuilder<TContext> RegisterAdapter<TContext, TIn, TOut>(...)
```

**问题**：`RegisterAdapter` 已标记为 `[Obsolete]`，但 `RegisterAsyncAdapter` 没有标记。两者功能对应，应该同时废弃。

### 3.5 `IExecutionStrategy` 接口及其实现类过度设计

**文件**：`Registry.cs` (L170-315)

**问题**：`IExecutionStrategy` + `SequentialStrategy` + `ParallelStrategy` + `ConditionalStrategy` 这套策略模式实现，功能已被 `PipelineExtensions` 和 `Combinators` 中的方法完全覆盖。`SequentialStrategy` 已标记为 `[Obsolete]`，但 `ParallelStrategy` 和 `ConditionalStrategy` 没有。

### 3.6 `MiddlewarePipeline<TRequest, TResponse>` 与 `GenericMiddlewarePipeline<TData, TContext>` 重复

**文件**：`Handlers.cs` (L872-949) vs `Handlers.cs` (L1164-1232)

**问题**：`MiddlewarePipeline<TRequest, TResponse>` 已标记为 `[Obsolete]`，但代码仍然保留。`GenericMiddlewarePipeline<TData, TContext>` 是其替代品。废弃的代码应该被移除而不是保留在文件中。

### 3.7 `OperationResult` 已废弃但保留

**文件**：`Handlers.cs` (L1002-1010)

**问题**：`OperationResult` 已标记为 `[Obsolete]`，功能已被 `PipelineResult` 覆盖。类似的还有 `ReferenceTrackingMiddleware`（L1024-1077）。

### 3.8 `StructureHandler` 已废弃但保留

**文件**：`Handlers.cs` (L759-849)

**问题**：`StructureHandler` 已标记为 `[Obsolete]`，功能已被 `StagePipeline<TContext>` 覆盖。但代码仍然保留在文件中。

---

## 4. 修复建议优先级

| 优先级 | 问题 | 类型 | 影响范围 |
|--------|------|------|----------|
| 🔴 P0 | `PipelineContext.Cancel()` 资源泄漏 | Bug | 每次取消泄漏一个 `CancellationTokenSource` |
| 🔴 P0 | `Pipeline<TInput, TOutput>.ExecuteStages` 链式传递被类型约束破坏 | Bug | 泛型管线链式传递会抛出 `InvalidCastException` |
| 🔴 P0 | `PipelineContext.Reset()` 不重置 `CancelReason` | Bug | 状态重置不完整 |
| 🟠 P1 | `ConditionalPipeline` vs `ConditionalSkeleton` 重复 | 重复 | 两套条件执行机制 |
| 🟠 P1 | `PipelineAdapter` vs `SkeletonAdapter` 重复 | 重复 | 两套适配器机制 |
| 🟠 P1 | `ParallelPipeline` vs `ParallelStrategy` vs `ParallelExtensions` 三重重复 | 重复 | 四种并行方式 |
| 🟠 P1 | CentralPipeline 转发类型（6个文件） | 冗余 | 纯转发无新增逻辑 |
| 🟠 P1 | `ForEachPipeline` CentralPipeline 版本缺少 `breakOnError` 参数 | Bug | 功能受限 |
| 🟡 P2 | `PipelineResult` 互转丢失状态 | Bug | `PartialSuccess` 状态丢失 |
| 🟡 P2 | `ParallelPipeline.ExecuteCore` 同步版本串行执行 | 设计缺陷 | 名不副实 |
| 🟡 P2 | `IExecutionStrategy` 体系过度设计 | 冗余 | 被扩展方法覆盖 |
| 🟡 P2 | `SkeletonToPipelineAdapter` 在 `_Deprecated` 但被使用 | 不一致 | 维护困惑 |
| 🟢 P3 | 已废弃类未移除（`OperationResult`、`StructureHandler`、`MiddlewarePipeline` 等） | 冗余 | 代码臃肿 |
| 🟢 P3 | `StrategyRegistry<TKey, TOutput>` 引用已废弃的 `Unit` | 警告 | 编译警告 |

---

## 5. 修复记录

| 日期 | 问题 | 修复内容 | 状态 |
|------|------|----------|------|
| 2026-06-04 | 2.3 `PipelineContext.Cancel()` 资源泄漏 | 缓存 `CancellationTokenSource`，释放旧实例 | ✅ 已修复 |
| 2026-06-04 | 2.4 `PipelineContext.Reset()` 不重置 `CancelReason` | 添加 `CancelReason = null` | ✅ 已修复 |
| 2026-06-04 | 2.1 `Pipeline<TInput, TOutput>.ExecuteStages` 注释矛盾 | 修正注释，明确说明类型约束限制 | ✅ 已修复 |
| 2026-06-04 | 2.2 `ForEachPipeline` CentralPipeline 版本缺少 `breakOnError` | 暴露 `breakOnError` 参数 | ✅ 已修复 |
| 2026-06-04 | 2.5/2.6 `PipelineResult` 互转丢失状态 | 修复 `ToGeneric`/`ToNonGeneric` 保留 `PartialSuccess` | ✅ 已修复 |
| 2026-06-04 | 2.7 `ParallelPipeline.ExecuteCore` 同步版本串行 | 添加 XML 注释强调同步回退行为 | ✅ 已修复 |
| 2026-06-04 | 2.8 `StrategyRegistry<TKey, TOutput>` 引用已废弃 `Unit` | 标记为 `[Obsolete]` | ✅ 已修复 |
| 2026-06-04 | 1.1 `ConditionalSkeleton` 未标记 `[Obsolete]` | 添加 `[Obsolete]` 属性 | ✅ 已修复 |
| 2026-06-04 | 1.2 `SkeletonAdapter` 未标记 `[Obsolete]` | 添加 `[Obsolete]` 属性 | ✅ 已修复 |
| 2026-06-04 | 3.4 `RegisterAsyncAdapter` 未标记 `[Obsolete]` | 添加 `[Obsolete]` 属性 | ✅ 已修复 |
| 2026-06-04 | 3.5 `ParallelStrategy`/`ConditionalStrategy` 未标记 `[Obsolete]` | 添加 `[Obsolete]` 属性 | ✅ 已修复 |
| 2026-06-04 | 3.4 `RegisterAsyncAdapter` 未标记 `[Obsolete]` | 添加 `[Obsolete]` 属性 | ✅ 已修复 |
| 2026-06-04 | 1.1 `ConditionalSkeleton` 未标记 `[Obsolete]` | 添加 `[Obsolete]` 属性 | ✅ 已修复 |
| 2026-06-04 | 1.2 `SkeletonAdapter`/`AsyncSkeletonAdapter`/`SkeletonAdapterFactory` 未标记 `[Obsolete]` | 添加 `[Obsolete]` 属性 | ✅ 已修复 |
| 2026-06-04 | 1.1/1.2/3.4 废弃类删除 | 删除 `ConditionalSkeleton.cs`、`SkeletonAdapter.cs`、`RegisterAdapter`/`RegisterAsyncAdapter` 扩展方法 | ✅ 已删除 |
---

*本报告由代码分析工具自动生成 + 人工审核完成。*
