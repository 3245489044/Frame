# UniversalJsonMiddleware Pipeline 优化分析 & 下一步规划

## 一、当前架构概览

```
UniversalJsonMiddleware
├── Abstractions.cs          — IJsonStage, IJsonPipeline[Obsolete], JsonPipelineContext, 布局模型等
├── Core.cs                  — JsonPipeline, JsonPipelineBuilder, 内置 Stage, MiddlewarePipeline
├── Infrastructure.cs        — Mediator, EventBus, CacheManager, StateManager, LayoutCalculator 等
├── Container.cs             — IConfigContainer, ContainerManager
├── Serialization.cs         — UnifiedJsonSerializer, JsonPathEngine, Validator, Merger
├── Utilities.cs             — LockHelper, Guard, ViewManager, OverlapColorMapper
└── Stages/
    └── VariableResolveStage.cs — 变量替换 Stage
```

### 1.1 管线使用方式

**方式 A：`JsonPipeline.Create<TInput, TOutput>()`** — 创建空管线（透传）

```csharp
// Core.cs 第21-32行
public static IPipeline<TInput, TOutput> Create<TInput, TOutput>(...)
{
    return new DelegatePipeline<TInput, TOutput>(name,
        sync: input => throw new NotSupportedException("Use ProcessAsync for async JSON pipeline"),
        async: async input => { return (TOutput)(object)input!; });
}
```

**方式 B：`IJsonStage.ToPipeline()`** — 将单个 Stage 适配为 IPipeline

```csharp
// Core.cs 第37-47行
public static IPipeline<TInput, TOutput> ToPipeline<TInput, TOutput>(
    this IJsonStage<TInput, TOutput> stage, ...)
{
    var ctx = new JsonPipelineContext { Config = config ?? new JsonPipelineConfig() };
    return new DelegatePipeline<TInput, TOutput>(stage.Name,
        async: async input => { return await stage.ProcessAsync(input, ctx); });
}
```

**方式 C：`JsonPipelineBuilder.Build()`** — 组合多个 Stage（**存在反射问题**）

```csharp
// Core.cs 第113-155行
return new DelegatePipeline<TInput, TOutput>(_name,
    asyncWithCtx: async (input, ctx) =>
    {
        object current = input!;
        foreach (var stage in stages)
        {
            // ★ 通过反射调用 ProcessAsync ★
            var method = stage.GetType().GetMethod("ProcessAsync");
            var task = (Task)method.Invoke(stage, new[] { current, jsonCtx })!;
            await task;
            current = task.GetType().GetProperty("Result")?.GetValue(task)!;
        }
        return (TOutput)current;
    });
```

### 1.2 与 BasicMethod.Pipeline 的关系

| 组件 | UniversalJsonMiddleware | BasicMethod.Pipeline |
|------|------------------------|---------------------|
| `IPipeline<TInput, TOutput>` | ✅ 使用 `DelegatePipeline` | ✅ 提供 |
| `PipelineContext` | ❌ 自建 `JsonPipelineContext`（未继承） | ✅ 提供基类 |
| `IStage<TTarget,TConfig,TContext>` | ❌ 未使用 | ✅ 提供（共享配置模式） |
| `StagePipeline` | ❌ 未使用 | ✅ 提供 |
| `Then/Parallel/Loop/Branch` | ❌ 未使用 | ✅ 提供 |

## 二、关键问题分析

### 问题 1：`JsonPipelineBuilder.Build()` 使用反射调用 ProcessAsync 🔴 高

```csharp
// Core.cs 第143-151行
var method = stage.GetType().GetMethod("ProcessAsync");
var task = (Task)method.Invoke(stage, new[] { current, jsonCtx })!;
await task;
current = task.GetType().GetProperty("Result")?.GetValue(task)!;
```

**风险**：
- **类型不安全**：`IJsonStage<TInput, TOutput>` 有泛型参数，`ProcessAsync` 签名是 `Task<TOutput> ProcessAsync(TInput input, JsonPipelineContext context)`，反射调用时 `current` 是 `object`，类型不匹配在运行时才暴露
- **性能损失**：每次 stage 执行都涉及 `GetMethod` + `Invoke` + `GetProperty("Result")`
- **`Result` 属性风险**：如果 stage 返回非泛型 `Task`（无 `Result` 属性），会静默返回 `null`
- **`GetMethod("ProcessAsync")` 歧义**：如果 stage 有多个重载的 `ProcessAsync`，`GetMethod` 可能返回错误的重载

### 问题 2：`JsonPipelineContext` 未继承 `BasicMethod.Pipeline.PipelineContext` 🟡 中

```csharp
// Abstractions.cs 第119-133行
public class JsonPipelineContext
{
    public ConcurrentDictionary<string, object?> Items { get; } = new();
    public CancellationToken CancellationToken { get; set; }
    public IServiceProvider? Services { get; set; }
    public JsonPipelineConfig Config { get; set; } = new();
}
```

**问题**：
- `BasicMethod.Pipeline.PipelineContext` 已有 `Items`（`IDictionary<string, object?>`）、`CancellationToken`、`Services`、`Id`、`CreatedAt`、`Dispose()` 等
- `JsonPipelineContext` 重复实现了相同的功能
- `JsonPipelineBuilder.Build()` 中需要手动复制 `Items`（Core.cs 第139-140行）

### 问题 3：`IJsonStage` 与 `IPipeline` 泛型不兼容 🟡 中

```csharp
// IJsonStage 有额外的 JsonPipelineContext 参数
public interface IJsonStage<TInput, TOutput>
{
    Task<TOutput> ProcessAsync(TInput input, JsonPipelineContext context);
}

// IPipeline 的 ExecuteAsync 没有额外的 context 参数（context 是可选参数）
public interface IPipeline<in TInput, TOutput>
{
    Task<TOutput> ExecuteAsync(TInput input, PipelineContext? context = null);
}
```

**问题**：`ToPipeline()` 扩展方法通过闭包捕获 `ctx` 来适配，但 `ctx` 在创建时固定，无法在每次执行时动态传入新的上下文。

### 问题 4：`MiddlewarePipeline` 完全自建 🟢 低

`MiddlewarePipeline`（Core.cs 第258-306行）使用 ASP.NET Core 风格的中间件模式（洋葱模型），与 `BasicMethod.Pipeline` 的链式传参模式不同，属于不同的设计模式，**不需要迁移**。

## 三、优化方案

### 方案 A：消除反射 — 使用 `IJsonStage<object, object>` 统一泛型（推荐）

**思路**：`JsonPipelineBuilder.Build()` 内部将所有 stage 统一为 `IJsonStage<object, object>`，避免反射。

```csharp
// 修改后的 JsonPipelineBuilder.Build()
public IPipeline<TInput, TOutput> Build()
{
    if (_stageFactories.Count == 0)
        return JsonPipeline.Create<TInput, TOutput>(_name, _config);

    var stages = _stageFactories.Select(f => f(_config)).ToList();

    return new DelegatePipeline<TInput, TOutput>(_name,
        asyncWithCtx: async (input, ctx) =>
        {
            object current = input!;
            var jsonCtx = new JsonPipelineContext
            {
                Config = _config,
                CancellationToken = ctx.CancellationToken,
                Services = ctx.Services
            };
            foreach (var kvp in ctx.Items)
                jsonCtx.Items[kvp.Key] = kvp.Value;

            foreach (var stage in stages)
            {
                // ★ 方案 A：通过 IJsonStage<object, object> 接口调用，消除反射 ★
                if (stage is IJsonStage<object, object> objStage)
                {
                    current = await objStage.ProcessAsync(current, jsonCtx);
                }
                else
                {
                    // ★ 回退：通过动态类型转换（比反射安全）
                    current = await InvokeStageAsync(stage, current, jsonCtx);
                }
            }

            return (TOutput)current;
        });
}

// ★ 新增：类型安全的动态调用（使用 dynamic 或表达式树）
private static async Task<object> InvokeStageAsync<TInput, TOutput>(
    IJsonStage<TInput, TOutput> stage, object input, JsonPipelineContext ctx)
{
    return await stage.ProcessAsync((TInput)input, ctx)!;
}
```

**优点**：
- 消除了反射调用
- 类型安全（编译时检查）
- 性能提升

**缺点**：
- 需要 stage 实现 `IJsonStage<object, object>` 或通过动态转换

### 方案 B：使用 `Then()` 扩展方法组合 Stage

**思路**：`JsonPipelineBuilder.Build()` 使用 `BasicMethod.Pipeline.Then()` 扩展方法组合多个 stage。

```csharp
// 修改后的 JsonPipelineBuilder.Build()
public IPipeline<TInput, TOutput> Build()
{
    if (_stageFactories.Count == 0)
        return JsonPipeline.Create<TInput, TOutput>(_name, _config);

    var stages = _stageFactories.Select(f => f(_config)).ToList();

    // ★ 方案 B：使用 Then() 扩展方法组合 ★
    IPipeline<object, object> pipeline = new DelegatePipeline<object, object>(
        stages[0].Name,
        async: async input =>
        {
            var jsonCtx = new JsonPipelineContext { Config = _config };
            return await InvokeStageAsync(stages[0], input, jsonCtx);
        });

    for (int i = 1; i < stages.Count; i++)
    {
        var stage = stages[i];
        var next = new DelegatePipeline<object, object>(
            stage.Name,
            async: async input =>
            {
                var jsonCtx = new JsonPipelineContext { Config = _config };
                return await InvokeStageAsync(stage, input, jsonCtx);
            });
        pipeline = pipeline.Then(next);
    }

    // 适配泛型
    return new DelegatePipeline<TInput, TOutput>(_name,
        asyncWithCtx: async (input, ctx) =>
        {
            var result = await pipeline.ExecuteAsync(input!, ctx);
            return (TOutput)result!;
        });
}
```

**优点**：
- 完全利用 `BasicMethod.Pipeline` 的 `Then()` 扩展
- 类型安全
- 支持 `CancellationToken` 传播

**缺点**：
- 每个 stage 都需要包装为 `DelegatePipeline`
- 上下文传递需要额外处理

### 方案 C：`JsonPipelineContext` 继承 `BasicMethod.Pipeline.PipelineContext`

```csharp
// 修改后的 JsonPipelineContext
public class JsonPipelineContext : BasicMethod.Pipeline.PipelineContext
{
    public JsonPipelineConfig Config { get; set; } = new();

    public JsonPipelineContext(
        CancellationToken externalToken = default,
        IServiceProvider? services = null)
        : base(externalToken, services)
    {
    }

    // ★ 新增：从基类 PipelineContext 复制数据 ★
    public static JsonPipelineContext FromBase(PipelineContext baseCtx, JsonPipelineConfig config)
    {
        var jsonCtx = new JsonPipelineContext(baseCtx.CancellationToken, baseCtx.Services)
        {
            Config = config
        };
        foreach (var kvp in baseCtx.Items)
            jsonCtx.Items[kvp.Key] = kvp.Value;
        return jsonCtx;
    }
}
```

**优点**：
- 消除重复代码
- 自动获得 `Id`、`CreatedAt`、`Dispose()`、`GetItem<T>()`、`SetItem<T>()` 等方法
- 消除 `JsonPipelineBuilder.Build()` 中手动复制 `Items` 的代码

## 四、推荐实施计划

### 阶段 1：消除反射（方案 A + 方案 C）

| 步骤 | 文件 | 修改内容 |
|------|------|---------|
| 1.1 | `Abstractions.cs` | `JsonPipelineContext` 继承 `BasicMethod.Pipeline.PipelineContext` |
| 1.2 | `Core.cs` | `JsonPipelineBuilder.Build()` 消除反射，使用接口调用 |
| 1.3 | `Core.cs` | 更新 `ToPipeline()` 扩展方法使用新的 `JsonPipelineContext` |
| 1.4 | 全局 | 搜索所有 `new JsonPipelineContext` 调用，更新构造函数 |

### 阶段 2：清理废弃接口

| 步骤 | 文件 | 修改内容 |
|------|------|---------|
| 2.1 | `Abstractions.cs` | 移除 `IJsonPipeline<TInput, TOutput>`（已标记 `[Obsolete]`） |
| 2.2 | 全局 | 确保无代码引用 `IJsonPipeline` |

### 阶段 3：测试验证

| 步骤 | 内容 |
|------|------|
| 3.1 | 构建验证 |
| 3.2 | 单元测试：`JsonPipelineBuilder.Build()` 组合多个 stage |
| 3.3 | 单元测试：`JsonPipelineContext` 继承基类后的功能 |
| 3.4 | 集成测试：`VariableResolveStage` 在管线中的执行 |

## 五、影响范围

| 文件 | 影响程度 | 说明 |
|------|---------|------|
| `Abstractions.cs` | 🔴 高 | `JsonPipelineContext` 继承关系变更 |
| `Core.cs` | 🔴 高 | `JsonPipelineBuilder.Build()` 反射消除 |
| `Infrastructure.cs` | 🟡 中 | 使用 `JsonPipelineContext` 的代码需更新构造函数 |
| `Stages/VariableResolveStage.cs` | 🟢 低 | 无影响（仅使用 `JsonPipelineContext` 的 `Items` 属性） |
| `Serialization.cs` | 🟢 低 | 无影响 |
| `Utilities.cs` | 🟢 低 | 无影响 |
| `Container.cs` | 🟢 低 | 无影响 |

## 六、总结

UniversalJsonMiddleware 已正确使用 `BasicMethod.Pipeline` 的 `DelegatePipeline` 和 `IPipeline` 接口，但存在两个主要问题：

1. **`JsonPipelineBuilder.Build()` 使用反射调用 `ProcessAsync`** — 这是最严重的问题，应优先修复
2. **`JsonPipelineContext` 未继承基类** — 导致代码重复和手动复制 `Items`

推荐优先实施 **方案 A（消除反射）+ 方案 C（继承 PipelineContext）**，这两个修改相互独立，可以并行进行。
