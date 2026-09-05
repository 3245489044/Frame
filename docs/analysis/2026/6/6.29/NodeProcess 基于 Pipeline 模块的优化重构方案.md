```markdown
# NodeProcess 基于 Pipeline 模块的优化重构方案

> **分析日期**：2026-06-28  
> **涉及模块**：`NodeProcess`、`Modules.Pipeline`、`Modules.CentralPipeline`  
> **当前状态**：方案设计阶段（待实施）

---

## 1. 核心问题：体系错配

### 1.1 两个 Pipeline 体系

| 体系 | 代表类型 | 核心接口 | 适用场景 |
|------|---------|---------|---------|
| **链式传参 (A)** | `IPipeline<TIn, TOut>`、`CentralPipeline<TContext>` | `Execute(TIn) → TOut` | 数据流变换 |
| **上下文模式 (B)** | `IPipelineStage<TContext>`、`StagePipeline<TContext>` | `ExecuteAsync(TContext)` | 阶段编排 |

### 1.2 当前架构问题
```

NodeProcessPipelineBuilder └─ PipelineBuilder ← 体系 A（链式传参） └─ WrapStageAsPipeline() ← 适配器（体系 B → 体系 A） └─ GetAwaiter().GetResult() ← ⚠️ 同步阻塞异步，可能死锁

````javascript

所有 Stage（`NodeExecutionStage`、`NodeLoopStage`、`NodeErrorHandlerStage` 等）都继承自 `PipelineStageBase<WfPipelineContext>`（体系 B），但 Builder 却通过 `WrapStageAsPipeline` 强行适配到 `CentralPipeline`（体系 A）。

### 1.3 具体问题清单

| # | 问题 | 位置 | 影响 |
|---|------|------|------|
| 1 | `GetAwaiter().GetResult()` 同步阻塞异步 | `WrapStageAsPipeline` | 可能死锁 |
| 2 | `NodeLoopStage` 自实现循环 | `NodeLoopStage.cs` | 与 `PipelineUsage.Loop` 重复 |
| 3 | `NodeErrorHandlerStage` 自实现错误处理 | `NodeErrorHandlerStage.cs` | 与 `PipelineTryCatch`/`PipelineRetry` 重复 |
| 4 | `NodeExecutionStage` 过于臃肿 | `NodeExecutionStage.cs` (~482行) | 混合执行状态管理 + 节点协调 + 参数管理 |

---

## 2. 架构关键发现

### 2.1 `StagePipeline<TContext>` 不是 `IPipeline<TContext, TContext>`

```csharp
// StagePipeline 继承 PipelineStageBase，不是 PipelineBase
public class StagePipeline<TContext> : PipelineStageBase<TContext>
    where TContext : IPipelineContext
{
    public void Register(IPipelineStage<TContext> stage) { ... }
}

// PipelineUsage.Loop/TryCatch/Retry 需要 IPipeline<TContext, TContext>
public static IPipeline<TContext, TContext> Loop<TContext>(
    Func<TContext, bool> predicate,
    IPipeline<TContext, TContext> body) { ... }
````

__结论__：`PipelineUsage.Loop`/`PipelineTryCatch`/`PipelineRetry` 不能直接接收 `IPipelineStage<TContext>`，需要适配器。

### 2.2 需要双向适配器

```javascript
IPipelineStage<TContext>  ──[StageToPipelineAdapter]──→  IPipeline<TContext, TContext>
IPipeline<TContext, TContext>  ──[PipelineToStageAdapter]──→  IPipelineStage<TContext>
```

| 适配器 | 方向 | 用途 | |--------|------|------| | `StageToPipelineAdapter` | Stage → Pipeline | 将 Stage 包装后传给 `PipelineUsage.Loop`/`TryCatch`/`Retry` | | `PipelineToStageAdapter` | Pipeline → Stage | 将组合后的 Pipeline 注册到 `StagePipeline` |

---

## 3. 重构方案

### 3.1 总体架构变化

```javascript
重构前：
  NodeProcessPipelineBuilder
    └─ PipelineBuilder<WfPipelineContext>  (体系 A)
         └─ WrapStageAsPipeline() 适配器
              └─ CentralPipeline<WfPipelineContext>

重构后：
  NodeProcessPipelineBuilder
    └─ StagePipeline<WfPipelineContext>  (体系 B)
         └─ 直接注册 IPipelineStage
         └─ PipelineToStageAdapter(PipelineUsage.Loop(...))
         └─ PipelineToStageAdapter(PipelineTryCatch(...))
```

### 3.2 新增：两个适配器类

#### `StageToPipelineAdapter<TContext>` — Stage → Pipeline

```csharp
/// <summary>
/// 将 IPipelineStage<TContext> 适配为 IPipeline<TContext, TContext>
/// 用于将 Stage 包装后传给 PipelineUsage.Loop / PipelineTryCatch / PipelineRetry
/// </summary>
public class StageToPipelineAdapter<TContext> : PipelineBase<TContext, TContext>
    where TContext : class, IPipelineContext
{
    private readonly IPipelineStage<TContext> _stage;

    public StageToPipelineAdapter(IPipelineStage<TContext> stage, string? name = null)
        : base(name ?? $"StageAdapter-{stage.GetType().Name}",
               $"Adapter: {stage.GetType().Name} → IPipeline")
    {
        _stage = stage ?? throw new ArgumentNullException(nameof(stage));
    }

    protected override TContext ExecuteCore(TContext input, PipelineContext context)
    {
        // 同步执行：通过异步包装
        _stage.ExecuteAsync(input, context.CancellationToken)
              .GetAwaiter().GetResult();
        return input;
    }

    protected override async Task<TContext> ExecuteCoreAsync(TContext input, PipelineContext context)
    {
        await _stage.ExecuteAsync(input, context.CancellationToken)
                    .ConfigureAwait(false);
        return input;
    }
}
```

#### `PipelineToStageAdapter<TContext>` — Pipeline → Stage

```csharp
/// <summary>
/// 将 IPipeline<TContext, TContext> 适配为 IPipelineStage<TContext>
/// 用于将组合后的 Pipeline（Loop/TryCatch/Retry）注册到 StagePipeline
/// </summary>
public class PipelineToStageAdapter<TContext> : PipelineStageBase<TContext>
    where TContext : class, IPipelineContext
{
    private readonly IPipeline<TContext, TContext> _pipeline;

    public PipelineToStageAdapter(IPipeline<TContext, TContext> pipeline, string? name = null)
        : base(name ?? $"StageAdapter-{pipeline.Name}")
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    protected override async Task ExecuteAsync(TContext context, CancellationToken cancellationToken)
    {
        var pipelineContext = new PipelineContext(cancellationToken);
        await _pipeline.ExecuteAsync(context, pipelineContext)
                       .ConfigureAwait(false);
    }
}
```

### 3.3 重写：`NodeProcessPipelineBuilder`

```csharp
public class NodeProcessPipelineBuilder : IDisposable
{
    private readonly StagePipeline<WfPipelineContext> _pipeline;
    private bool _disposed;

    public NodeProcessPipelineBuilder(string? name = null)
    {
        _pipeline = new StagePipeline<WfPipelineContext>(name ?? "NodeProcessPipeline");
    }

    /// <summary>直接注册 Stage（无需适配）</summary>
    public NodeProcessPipelineBuilder AddStage(IPipelineStage<WfPipelineContext> stage)
    {
        _pipeline.Register(stage);
        return this;
    }

    /// <summary>注册循环（使用 PipelineUsage.Loop）</summary>
    public NodeProcessPipelineBuilder AddLoop(
        Func<WfPipelineContext, bool> shouldContinue,
        IPipelineStage<WfPipelineContext> bodyStage,
        string? name = null)
    {
        var bodyPipeline = new StageToPipelineAdapter<WfPipelineContext>(bodyStage);
        var loopPipeline = PipelineUsage.Loop(shouldContinue, bodyPipeline, name);
        _pipeline.Register(new PipelineToStageAdapter<WfPipelineContext>(loopPipeline));
        return this;
    }

    /// <summary>注册 TryCatch</summary>
    public NodeProcessPipelineBuilder AddTryCatch(
        IPipelineStage<WfPipelineContext> innerStage,
        IPipelineStage<WfPipelineContext>? fallbackStage = null,
        Func<Exception, bool>? exceptionFilter = null,
        Action<Exception>? onError = null,
        string? name = null)
    {
        var innerPipeline = new StageToPipelineAdapter<WfPipelineContext>(innerStage);
        IPipeline<WfPipelineContext, WfPipelineContext>? fallbackPipeline = null;
        if (fallbackStage != null)
            fallbackPipeline = new StageToPipelineAdapter<WfPipelineContext>(fallbackStage);

        var tryCatch = new PipelineTryCatch<WfPipelineContext>(
            innerPipeline, fallbackPipeline, exceptionFilter, onError, name);
        _pipeline.Register(new PipelineToStageAdapter<WfPipelineContext>(tryCatch));
        return this;
    }

    /// <summary>注册 Retry</summary>
    public NodeProcessPipelineBuilder AddRetry(
        IPipelineStage<WfPipelineContext> innerStage,
        int maxRetries = 3,
        int delayMs = 100,
        Func<Exception, int, bool>? shouldRetry = null,
        Action<Exception, int>? onRetry = null,
        string? name = null)
    {
        var innerPipeline = new StageToPipelineAdapter<WfPipelineContext>(innerStage);
        var retry = new PipelineRetry<WfPipelineContext>(
            innerPipeline, maxRetries, delayMs, shouldRetry, onRetry, name);
        _pipeline.Register(new PipelineToStageAdapter<WfPipelineContext>(retry));
        return this;
    }

    /// <summary>构建管线</summary>
    public StagePipeline<WfPipelineContext> Build() => _pipeline;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        (_pipeline as IDisposable)?.Dispose();
    }
}
```

### 3.4 删除：`NodeLoopStage.cs`

__当前__：`NodeLoopStage` 内部手动创建 `SkeletonExtensions.Loop`，与 `PipelineUsage.Loop` 功能重复。

__替代方案__：在 Builder 中直接使用 `PipelineUsage.Loop`：

```csharp
// 重构前（NodeLoopStage 内部）
var loopSkeleton = SkeletonExtensions.Loop(
    shouldContinue: ctx => ctx.GetNodeKey() != null && ctx.ExecutionState == ExecutionState.Running,
    body: stepSkeleton);

// 重构后（Builder 中）
builder.AddLoop(
    shouldContinue: ctx => ctx.GetNodeKey() != null && ctx.ExecutionState == ExecutionState.Running,
    bodyStage: new NodeExecutionStepSkeleton(nodeProcessor));
```

### 3.5 删除：`NodeErrorHandlerStage.cs`

__当前__：`NodeErrorHandlerStage` 自实现了错误处理器注册和重试策略。

__替代方案__：在 Builder 中使用 `PipelineTryCatch`/`PipelineRetry`：

```csharp
// 重构前（NodeErrorHandlerStage 内部）
var handler = new NodeErrorHandlerStage(coordinator, errorHandlers, retryPolicy);

// 重构后（Builder 中）
builder.AddRetry(
    innerStage: executionStage,
    maxRetries: 3,
    delayMs: 100,
    onRetry: (ex, attempt) => logger?.LogWarning($"重试 #{attempt}: {ex.Message}"));
```

### 3.6 重构：`NodeExecutionStage` 职责拆分

__当前问题__：`NodeExecutionStage` (~482行) 混合了：

1. __执行状态管理__（`ExecutionState`、`IsRunning`、`IsCompleted` 等）
2. __节点协调__（`ExecuteNode`、`GetNextNode` 等）
3. __参数管理__（`ResolveParameters`、`MergeParameters` 等）

__推荐方案__：提取接口注入（改动最小，风险最低）

```csharp
/// <summary>节点执行协调器接口</summary>
public interface INodeExecutionCoordinator
{
    Task<bool> ExecuteNodeAsync(WfPipelineContext context);
    string? GetNextNodeKey(WfPipelineContext context);
}

/// <summary>参数解析器接口</summary>
public interface IParameterResolver
{
    void ResolveParameters(WfPipelineContext context);
    void MergeParameters(WfPipelineContext context, object? input);
}
```

__可选方案__：拆分为三个独立 Stage（改动更大，但更干净）

| 新 Stage | 职责 | 说明 | |----------|------|------| | `ExecutionStateStage` | 执行状态管理 | 管理 ExecutionState 生命周期 | | `NodeExecutionStage` | 节点协调（精简） | 仅保留 ExecuteNode 核心逻辑 | | `ParameterResolutionStage` | 参数管理 | 参数解析、合并、缓存 |

### 3.7 可能删除：`Models/ErrorHandling.cs`

确认 `IErrorHandler`、`RetryPolicy` 等类型在 `NodeErrorHandlerStage` 之外无其他引用后删除。

---

## 4. 文件变更清单

| 文件 | 操作 | 说明 | |------|------|------| | `NodeProcess/Abstractions/StageToPipelineAdapter.cs` | __新增__ | Stage → Pipeline 适配器 | | `NodeProcess/Abstractions/PipelineToStageAdapter.cs` | __新增__ | Pipeline → Stage 适配器 | | `NodeProcess/Abstractions/INodeExecutionCoordinator.cs` | __新增__ | 节点执行协调器接口 | | `NodeProcess/Abstractions/IParameterResolver.cs` | __新增__ | 参数解析器接口 | | `NodeProcess/NodeProcessPipelineBuilder.cs` | __重写__ | 改用 StagePipeline，删除适配层 | | `NodeProcess/Execution/NodeLoopStage.cs` | __删除__ | 被 `PipelineUsage.Loop` 替代 | | `NodeProcess/Execution/NodeErrorHandlerStage.cs` | __删除__ | 被 `PipelineTryCatch`/`PipelineRetry` 替代 | | `NodeProcess/Execution/NodeExecutionStage.cs` | __重构__ | 提取接口，拆分职责 | | `NodeProcess/Execution/NodeExecutionStepSkeleton.cs` | __保留__ | 作为 Loop 的循环体 | | `NodeProcess/Execution/NodeMetricsStage.cs` | __保留__ | 保持现状 | | `NodeProcess/NodeProcessSkeleton.cs` | __微调__ | 更新 `Build()` 调用签名 | | `NodeProcess/Models/ErrorHandling.cs` | __可能删除__ | 确认无其他引用后删除 |

---

## 5. 预期收益

| 指标 | 当前 | 优化后 | |------|------|--------| | 适配层 | `WrapStageAsPipeline` 手动适配 | `StageToPipelineAdapter` + `PipelineToStageAdapter` 通用适配器 | | 循环实现 | `NodeLoopStage` + `SkeletonExtensions.Loop` | `PipelineUsage.Loop` 统一 | | 错误处理 | `NodeErrorHandlerStage` 自实现 | `PipelineTryCatch`/`PipelineRetry` | | 编排器 | `CentralPipeline`（链式传参） | `StagePipeline`（上下文模式） | | 同步阻塞 | `GetAwaiter().GetResult()` | 原生异步 | | 代码行数 | ~1200+ | ~900+ |

---

## 6. 实施顺序

| 步骤 | 内容 | 风险 | |------|------|------| | 1 | 创建 `StageToPipelineAdapter` + `PipelineToStageAdapter` | 低 | | 2 | 创建 `INodeExecutionCoordinator` + `IParameterResolver` 接口 | 低 | | 3 | 重写 `NodeProcessPipelineBuilder` | 中（需确保所有方法签名兼容） | | 4 | 删除 `NodeLoopStage`，迁移循环逻辑到 Builder | 中（需确保 `ShouldContinue` 条件完整迁移） | | 5 | 删除 `NodeErrorHandlerStage`，迁移错误处理到 Builder | 中（需确保所有错误处理器正确注册） | | 6 | 重构 `NodeExecutionStage`（提取接口注入） | 低-中 | | 7 | 删除 `Models/ErrorHandling.cs`（确认无引用后） | 低 | | 8 | 微调 `NodeProcessSkeleton.cs`（更新 `Build()` 调用） | 低 | | 9 | 编译验证 | — |

---

## 7. 风险与注意事项

### 7.1 调用方兼容性

- `Build()` 返回类型从 `CentralPipeline<WfPipelineContext>` 变为 `StagePipeline<WfPipelineContext>`
- 需要搜索所有调用方并更新

### 7.2 `NodeLoopStage.ShouldContinue` 逻辑迁移

- 当前条件：`ctx.GetNodeKey() != null && ctx.ExecutionState == ExecutionState.Running`
- 需要确保迁移为 `Func<WfPipelineContext, bool>` 谓词后逻辑一致

### 7.3 `NodeErrorHandlerStage.IErrorHandler` 注册迁移

- 当前支持多个 `IErrorHandler` 注册
- 需要迁移为 `PipelineTryCatch` 的 `onError` 回调或 `exceptionFilter`
- 如果多个错误处理器有优先级顺序，可能需要额外处理

### 7.4 适配器性能开销

- 每个适配器增加一层方法调用间接性
- 对于高频调用的 Stage，考虑缓存适配器实例

---

## 8. 附录：关键类型关系图

```javascript
重构前：
  CentralPipeline<WfPipelineContext>  (体系 A: IPipeline<TIn, TOut>)
    └─ PipelineBuilder<WfPipelineContext>
         └─ WrapStageAsPipeline(NodeExecutionStage)    ← 适配
         └─ WrapStageAsPipeline(NodeLoopStage)          ← 适配
         └─ WrapStageAsPipeline(NodeErrorHandlerStage)  ← 适配
         └─ WrapStageAsPipeline(NodeMetricsStage)       ← 适配

重构后：
  StagePipeline<WfPipelineContext>  (体系 B: IPipelineStage<TContext>)
    └─ NodeExecutionStage (直接注册)
    └─ PipelineToStageAdapter(PipelineUsage.Loop(...))  ← 适配
    └─ PipelineToStageAdapter(PipelineTryCatch(...))    ← 适配
    └─ NodeMetricsStage (直接注册)
```
