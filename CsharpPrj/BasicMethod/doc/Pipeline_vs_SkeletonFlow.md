# Pipeline 与 SkeletonFlow 对比分析文档

> **版本**: v1.0  
> **最后更新**: 2026-06-04  
> **适用范围**: `BasicMethod/Pipeline/` 目录 与 `BasicMethod/FrameMethod/SkeletonFlow.cs`

---

## 目录

1. [概述](#1-概述)
2. [核心接口对比](#2-核心接口对比)
3. [抽象基类实现对比](#3-抽象基类实现对比)
4. [委托实现类对比](#4-委托实现类对比)
5. [上下文设计对比](#5-上下文设计对比)
6. [组合扩展方法对比](#6-组合扩展方法对比)
7. [注册表/工厂模式对比](#7-注册表工厂模式对比)
8. [Pipeline 独有功能详解](#8-pipeline-独有功能详解)
9. [SkeletonFlow 独有功能详解](#9-skeletonflow-独有功能详解)
10. [使用场景指南](#10-使用场景指南)
11. [整合建议](#11-整合建议)
12. [附录：核心类型速查](#12-附录核心类型速查)

---

## 1. 概述

### 1.1 Pipeline（管线体系）

| 项目 | 内容 |
|------|------|
| **位置** | `BasicMethod/Pipeline/` 目录 |
| **文件数** | 6 个文件 |
| **代码量** | ~4600+ 行 |
| **命名空间** | `BasicMethod.Pipeline` |
| **设计哲学** | 管线/管道模式（Pipeline Pattern），强调**流程编排与执行管理** |
| **核心接口** | `IPipeline<TIn, TOut>` |

**文件清单：**

| 文件名 | 行数 | 核心职责 |
|--------|------|---------|
| `Core.cs` | 1262 | 核心接口、抽象基类、PipelineContext、管线编排器（Pipeline/Pipeline\<T\>）、PipelineResult、PipelineErrorHandler |
| `Abstractions.cs` | 323 | 抽象接口：生命周期（IAsyncLifecycle）、绑定模式（IBindingMode）、命令（ICommandSource/Target）、通用阶段编排器（IStagePipeline\<TContext\>） |
| `Models.cs` | 501 | 枚举（PipelineStage/PipelineStatus）、ErrorCollector、Guard、Session/SessionManager |
| `Registry.cs` | 438 | 策略模式（IStrategy/StrategyRegistry）、执行策略（SequentialStrategy/ParallelStrategy/ConditionalStrategy）、PipelineStageRegistry |
| `Handlers.cs` | 1296 | 扩展方法（Then/Branch/Loop/Parallel/Compose/Switch/TryCatch/WithTimeout/WithCache/WithRetry/WithLogging等）、MiddlewarePipeline、StructureHandler |
| `Combinators.cs` | 866 | 类实现组合子：ForEachPipeline、ParallelPipeline、PipelineSwitch、PipelineBranch、PipelineTryCatch、PipelineRetry、ConditionalPipeline、PipelineAdapter |

### 1.2 SkeletonFlow（骨架流体系）

| 项目 | 内容 |
|------|------|
| **位置** | `BasicMethod/FrameMethod/SkeletonFlow.cs` |
| **文件数** | 1 个文件 |
| **代码量** | 908 行 |
| **命名空间** | `SkeletonFlow` |
| **设计哲学** | 骨架/骨骼模式（Skeleton Pattern），强调**数据变换操作与配置驱动组合** |
| **核心接口** | `ISkeleton<TInput, TOutput>` |

**Region 分区：**

| Region | 行号 | 核心职责 |
|--------|------|---------|
| Core | 15-245 | 核心接口、抽象基类、委托实现、SyncSkeletonBase |
| BuiltIn | 247-411 | 内置数据操作骨架工厂（Filter/Map/Reduce/Skip/Take/Distinct/GroupBy/OrderBy/First/Single/ToList/ToArray/ParallelForEach） |
| Composition | 413-508 | 组合扩展方法（Then/Branch/Loop/Parallel/ForEach/SelectMany） |
| Dynamic | 511-700 | SkeletonConfig、ISkeletonRegistry/DefaultSkeletonRegistry（含JSON解析、构造函数注入）、IConfigParser/JsonConfigParser |
| Adapters | 702-709 | IExecutionAdapter\<TEnvironment\> 环境适配器接口 |
| Engine | 711-735 | SkeletonEngine（配置驱动执行引擎） |
| Composite Skeletons | 737-896 | SequentialSkeleton（自动类型链匹配）、ParallelForEachSkeleton |
| Unit | 898-908 | Unit 函数式占位类型 |

---

## 2. 核心接口对比

### 2.1 IPipeline\<TIn, TOut\> vs ISkeleton\<TInput, TOutput\>

```csharp
// Pipeline 核心接口
public interface IPipeline<in TInput, TOutput>
{
    string Name { get; }
    TOutput Execute(TInput input, PipelineContext? context = null);
    Task<TOutput> ExecuteAsync(TInput input, PipelineContext? context = null);
}

// SkeletonFlow 核心接口
public interface ISkeleton<in TInput, TOutput> : ISkeleton  // ← 多继承了一个标记接口 ISkeleton
{
    string Name { get; }
    string Description { get; }                              // ← 多了一个 Description 属性
    TOutput Execute(TInput input, SkeletonContext context = null);
    Task<TOutput> ExecuteAsync(TInput input, SkeletonContext context = null);
}
```

**差异分析：**
- `ISkeleton` 多继承了一个空的标记接口 `ISkeleton`，可用于类型收窄（如 `ICompositeSkeleton` 的 `SetChildren(ISkeleton[])` 接受任意骨架）
- `ISkeleton` 多了 `Description` 属性，Pipeline 的 `Description` 在 `PipelineBase` 中才有
- 方法签名完全一致，只是上下文类型不同（`PipelineContext` vs `SkeletonContext`）

### 2.2 接口体系复杂度

**Pipeline 有 3 套接口体系：**

```csharp
// 体系 A：链式传参（数据流模式）
IPipeline<TIn, TOut>

// 体系 B：共享配置（所有阶段共享 target + config + context）
IStage<TTarget, TConfig, TContext> where TContext : PipelineContext
{
    string Name { get; }
    void Execute(TTarget target, TConfig config, TContext context);
}

// 体系 C：编排器模式
IPipelineStage<TInput, TOutput>  // 泛型阶段，带 Order/IsFatal
{
    string Name { get; }
    int Order { get; }
    bool IsFatal { get; }
    TOutput? Process(TInput input, ErrorCollector errors, PipelineContext context);
}

IPipelineStage  // 非泛型版本
{
    string Name { get; }
    int Order { get; }
    bool IsFatal { get; }
    object? Process(object input, ErrorCollector errors, PipelineContext context);
}

IPipelineStage<TContext>  // 通用阶段接口（带 CanExecute 条件执行）
{
    string Name { get; }
    string Description { get; }
    bool CanExecute(TContext context);
    Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default);
}

IStagePipeline<TContext>  // 通用阶段编排器接口
{
    string Name { get; }
    string Description { get; }
    IStagePipeline<TContext> Register(IPipelineStage<TContext> stage);
    IStagePipeline<TContext> InsertBefore(string stageName, IPipelineStage<TContext> stage);
    IStagePipeline<TContext> InsertAfter(string stageName, IPipelineStage<TContext> stage);
    IStagePipeline<TContext> Remove(string stageName);
    Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default);
    Task ExecuteForEachAsync(...);
}
```

**SkeletonFlow 只有 1 套接口体系：**

```csharp
ISkeleton                         // 标记接口
ISkeleton<TInput, TOutput>        // 核心接口
ICompositeSkeleton : ISkeleton    // 复合骨架接口（用于嵌套组合）
{
    void SetChildren(IEnumerable<ISkeleton> children);
    IEnumerable<ISkeleton> GetChildren();
}
ISkeletonRegistry                 // 注册表接口
IConfigParser                     // 配置解析器接口
IExecutionAdapter<TEnvironment>   // 环境适配器接口
```

**结论：** Pipeline 的接口体系远更复杂，覆盖了 3 种不同的使用模式；SkeletonFlow 只有 1 种核心模式，通过 `ICompositeSkeleton` 实现嵌套组合。

---

## 3. 抽象基类实现对比

### 3.1 PipelineBase\<TIn, TOut\> vs SkeletonBase\<TInput, TOutput\>

**相同点：** 都采用模板方法模式，同步/异步双实现，自动管理上下文生命周期。

**不同点：**

| 维度 | PipelineBase | SkeletonBase |
|------|-------------|-------------|
| Description | `string?`（可空） | `string`（不可空，默认 null） |
| 构造函数 | `name` 和 `description` 都是可选参数 | `name` 和 `description` 都是可选参数 |
| Execute 中取消检查 | 有 `ctx.CancellationToken.ThrowIfCancellationRequested()` | 有 `ctx.CancellationToken.ThrowIfCancellationRequested()` |
| 异步 ConfigureAwait | 使用 `ConfigureAwait(false)` | 使用 `ConfigureAwait(false)` |
| **子类扩展点** | 无额外基类 | 有 `SyncSkeletonBase`（为纯同步骨架提供默认异步实现） |

**SyncSkeletonBase 是 SkeletonFlow 独有的：**

```csharp
// SkeletonFlow 独有：为纯同步骨架省去编写异步方法的麻烦
public abstract class SyncSkeletonBase<TInput, TOutput> : SkeletonBase<TInput, TOutput>
{
    protected override Task<TOutput> ExecuteCoreAsync(TInput input, SkeletonContext context)
    {
        return Task.FromResult(ExecuteCore(input, context));
    }
}
```

### 3.2 DelegatePipeline vs DelegateSkeleton

**相同点：** 都提供无上下文和有上下文两种构造函数重载。

**不同点：**

| 维度 | DelegatePipeline | DelegateSkeleton |
|------|-----------------|-----------------|
| name 参数 | `string?`（可空） | `string`（不可空） |
| 异步回退策略 | `Task.Run(() => ExecuteCore(...))` 将同步委托包装到新线程 | `Task.FromResult(ExecuteCore(...))` 直接返回同步结果 |
| 错误消息语言 | 中文 | 中文 |

**关键差异——异步回退策略：**

```csharp
// Pipeline：同步委托在异步调用时，通过 Task.Run 放到线程池执行
protected override Task<TOutput> ExecuteCoreAsync(TInput input, PipelineContext ctx)
{
    if (_async != null) return _async(input, ctx);
    return Task.Run(() => ExecuteCore(input, ctx), ctx.CancellationToken);  // ← 新线程
}

// SkeletonFlow：同步委托在异步调用时，直接同步执行后包装为 Task
protected override Task<TOutput> ExecuteCoreAsync(TInput input, SkeletonContext ctx) =>
    _async != null ? _async(input, ctx) :
    Task.FromResult(ExecuteCore(input, ctx));  // ← 当前线程同步执行
```

**这意味着：** 如果只提供了同步委托却调用 `ExecuteAsync`，Pipeline 会在线程池执行，SkeletonFlow 会在当前线程同步执行。

---

## 4. 委托实现类对比

### 4.1 构造函数重载

**DelegatePipeline 的构造函数：**

```csharp
// 重载1：无上下文版本（自动包装）
public DelegatePipeline(
    string? name = null,
    Func<TInput, TOutput>? sync = null,          // ← 无上下文
    Func<TInput, Task<TOutput>>? async = null,    // ← 无上下文
    string? description = null)

// 重载2：有上下文版本
public DelegatePipeline(
    string? name,
    Func<TInput, PipelineContext, TOutput>? syncWithCtx = null,       // ← 有上下文
    Func<TInput, PipelineContext, Task<TOutput>>? asyncWithCtx = null, // ← 有上下文
    string? description = null)
```

**DelegateSkeleton 的构造函数：**

```csharp
// 重载1：无上下文版本（自动包装）
public DelegateSkeleton(string name,
    Func<TInput, TOutput> sync = null,            // ← 无上下文
    Func<TInput, Task<TOutput>> async = null,      // ← 无上下文
    string description = null)

// 重载2：有上下文版本
public DelegateSkeleton(string name,
    Func<TInput, SkeletonContext, TOutput> syncWithCtx = null,       // ← 有上下文
    Func<TInput, SkeletonContext, Task<TOutput>> asyncWithCtx = null, // ← 有上下文
    string description = null)
```

**差异：** 结构完全一致，只是上下文类型不同。

---

## 5. 上下文设计对比

### 5.1 PipelineContext vs SkeletonContext

这是**差异最大**的部分之一。

| 维度 | PipelineContext | SkeletonContext |
|------|----------------|-----------------|
| **外部依赖** | 无（仅依赖 `BasicMethod.Common`） | **依赖 `Graphkey.Core.IContext`** |
| **底层存储** | 自包含：`ConcurrentDictionary<string, object?>` | 委托给 `IContext`：通过 `IContext.GetData/SetData` 存取 |
| **唯一标识** | 有 `Id`（GUID） | 无 |
| **创建时间** | 有 `CreatedAt` | 无 |
| **取消令牌来源** | 自己的 `CancellationTokenSource` | 从 `IContext.CancellationToken` 链接而来 |
| **取消令牌设置** | 只读（通过构造函数传入外部 token） | **可读写**（有 `set` 访问器，可动态替换） |
| **取消原因** | 有 `CancelReason` 属性 | 无 |
| **阶段状态跟踪** | 内置 `StageSuccess`/`StageErrors` 字典 | 无 |
| **Reset 方法** | 有（清空所有状态） | 无 |
| **服务注册** | 通过 `IServiceProvider` 属性（外部注入） | 通过 `RegisterService<T>()` 方法（内部字典） |
| **服务解析** | `Services?.GetService(type)` | `ResolveService<T>()` / `ResolveRequiredService<T>()` |
| **数据访问方式** | 索引器 `this[string key]` + `GetItem<T>()`/`SetItem<T>()` | `SetItem(key, value)`/`RemoveItem(key)` |
| **泛型数据存取** | `GetItem<T>(string key) where T : class` | 无泛型版本，直接操作 `object` |
| **批量设置** | 有 `SetRange(IEnumerable<KeyValuePair>)` | 无 |
| **Placeholder** | 无 | 有 `GetPlaceholder`/`SetPlaceholder` |
| **IContext 代理方法** | 无 | 有 `GetData<T>`/`TryGetData<T>`/`GetAllData<T>`/`SetData<T>`/`RemoveData<T>` |
| **Dispose** | 释放自己的 `_cts` | 释放所有 `_allCts` + 释放 `_context`（如果自己创建） |

### 5.2 关键差异详解

**（1）底层存储机制不同：**

```csharp
// PipelineContext：自包含存储
private readonly ConcurrentDictionary<string, object?> _items = new();
public object? this[string key]
{
    get => _items.TryGetValue(key, out var value) ? value : null;
    set => _items[key] = value;
}

// SkeletonContext：委托给 IContext 存储
private readonly IContext _context;
// Items 是独立于 IContext 的额外字典
private readonly ConcurrentDictionary<string, object> _items;
// 但也可以通过 IContext 存取数据
public T GetData<T>(string key = null) where T : class => _context.GetData<T>(key);
public void SetData<T>(T data, string key = null) where T : class => _context.SetData(data, key);
```

**（2）取消令牌机制不同：**

```csharp
// PipelineContext：只读，通过构造函数传入外部 token 链接
public CancellationToken CancellationToken => _cts.Token;
public PipelineContext(CancellationToken externalToken = default, ...)
{
    _cts = externalToken == default
        ? new CancellationTokenSource()
        : CancellationTokenSource.CreateLinkedTokenSource(externalToken);
}

// SkeletonContext：可读写，可动态替换
public CancellationToken CancellationToken
{
    get => _linkedCts.Token;
    set  // ← 可动态设置！会创建新的链接 token
    {
        lock (_lock)
        {
            var newCts = CancellationTokenSource.CreateLinkedTokenSource(_context.CancellationToken, value);
            _allCts.Add(newCts);
            var oldCts = Interlocked.Exchange(ref _linkedCts, newCts);
            oldCts?.Dispose();
        }
    }
}
```

**（3）服务容器实现不同：**

```csharp
// PipelineContext：通过 IServiceProvider 接口（外部 DI 容器）
public IServiceProvider? Services { get; set; }
// 使用方式：Services?.GetService(typeof(T))

// SkeletonContext：内部字典（自建简易 DI）
private readonly Dictionary<Type, object> _services = new();
public void RegisterService<T>(T service) where T : class { _services[typeof(T)] = service; }
public T? ResolveService<T>() where T : class { ... }
```

---

## 6. 组合扩展方法对比

### 6.1 Then（串联）

```csharp
// Pipeline 的 Then
public static IPipeline<TInput, TOutput> Then<TInput, TIntermediate, TOutput>(
    this IPipeline<TInput, TIntermediate> first,
    IPipeline<TIntermediate, TOutput> next)
{
    return new DelegatePipeline<TInput, TOutput>($"{first.Name}+{next.Name}",
        syncWithCtx: (i, ctx) => next.Execute(first.Execute(i, ctx), ctx),
        asyncWithCtx: async (i, ctx) =>
            await next.ExecuteAsync(
                await first.ExecuteAsync(i, ctx).ConfigureAwait(false), ctx)
            .ConfigureAwait(false));
}

// SkeletonFlow 的 Then
public static ISkeleton<TInput, TOutput> Then<TInput, TIntermediate, TOutput>(
    this ISkeleton<TInput, TIntermediate> first,
    ISkeleton<TIntermediate, TOutput> next) =>
    new DelegateSkeleton<TInput, TOutput>($"{first.Name}+{next.Name}",
        (i, ctx) => next.Execute(first.Execute(i, ctx), ctx),
        async (i, ctx) => await next.ExecuteAsync(
            await first.ExecuteAsync(i, ctx).ConfigureAwait(false), ctx).ConfigureAwait(false),
        $"Sequential {first.Description} & {next.Description}");
```

**差异：**
- Pipeline 的 `asyncWithCtx` 参数使用命名参数，SkeletonFlow 使用位置参数
- SkeletonFlow 多传了一个 `description` 参数（`$"Sequential {first.Description} & {next.Description}"`）
- **核心逻辑完全一致**：`next.Execute(first.Execute(input))`

### 6.2 Branch（条件分支）

```csharp
// Pipeline 的 Branch（基于 IPipeline<bool> 条件）
public static IPipeline<TInput, TOutput> Branch<TInput, TOutput>(
    this IPipeline<TInput, bool> condition,
    IPipeline<TInput, TOutput> trueBranch,
    IPipeline<TInput, TOutput> falseBranch)
{
    return new DelegatePipeline<TInput, TOutput>($"Branch({condition.Name})",
        syncWithCtx: (i, ctx) =>
            condition.Execute(i, ctx) ? trueBranch.Execute(i, ctx) : falseBranch.Execute(i, ctx),
        asyncWithCtx: async (i, ctx) =>
            await condition.ExecuteAsync(i, ctx).ConfigureAwait(false)
                ? await trueBranch.ExecuteAsync(i, ctx).ConfigureAwait(false)
                : await falseBranch.ExecuteAsync(i, ctx).ConfigureAwait(false));
}

// Pipeline 的 Branch（基于 Func<TInput, bool> 谓词）
public static IPipeline<TInput, TOutput> Branch<TInput, TOutput>(
    this Func<TInput, bool> predicate,
    IPipeline<TInput, TOutput> trueBranch,
    IPipeline<TInput, TOutput> falseBranch)
{
    var cond = new DelegatePipeline<TInput, bool>("Cond", sync: predicate);
    return cond.Branch(trueBranch, falseBranch);
}

// SkeletonFlow 的 Branch（基于 Func<TInput, bool> 谓词）
public static ISkeleton<TInput, TOutput> Branch<TInput, TOutput>(
    this Func<TInput, bool> cond,
    ISkeleton<TInput, TOutput> trueBranch,
    ISkeleton<TInput, TOutput> falseBranch) =>
    cond == null ? throw new ArgumentNullException(nameof(cond)) :  // ← 多了 null 检查
    new DelegateSkeleton<TInput, bool>("BranchCond", (i, _) => cond(i), (i, _) => Task.FromResult(cond(i)))
        .Branch(trueBranch, falseBranch);

// SkeletonFlow 的 Branch（基于 ISkeleton<bool> 条件）
public static ISkeleton<TInput, TOutput> Branch<TInput, TOutput>(
    this ISkeleton<TInput, bool> condSkel,
    ISkeleton<TInput, TOutput> trueBranch,
    ISkeleton<TInput, TOutput> falseBranch) =>
    new DelegateSkeleton<TInput, TOutput>("Branch",
        (i, ctx) => condSkel.Execute(i, ctx) ? trueBranch.Execute(i, ctx) : falseBranch.Execute(i, ctx),
        async (i, ctx) => await condSkel.ExecuteAsync(i, ctx).ConfigureAwait(false)
            ? await trueBranch.ExecuteAsync(i, ctx) : await falseBranch.ExecuteAsync(i, ctx));
```

**差异：**
- SkeletonFlow 的谓词重载多了 `ArgumentNullException` 检查
- SkeletonFlow 的谓词重载创建的条件骨架名为 `"BranchCond"`，Pipeline 为 `"Cond"`
- **核心逻辑完全一致**

### 6.3 Loop（循环）

```csharp
// Pipeline 的 Loop
public static IPipeline<TInput, TInput> Loop<TInput>(
    this IPipeline<TInput, bool> condition,
    IPipeline<TInput, TInput> body)
{
    return new DelegatePipeline<TInput, TInput>($"Loop({condition.Name})",
        syncWithCtx: (i, ctx) =>
        {
            var cur = i;
            while (condition.Execute(cur, ctx)) cur = body.Execute(cur, ctx);
            return cur;
        },
        asyncWithCtx: async (i, ctx) =>
        {
            var cur = i;
            while (await condition.ExecuteAsync(cur, ctx).ConfigureAwait(false))
                cur = await body.ExecuteAsync(cur, ctx).ConfigureAwait(false);
            return cur;
        });
}

// SkeletonFlow 的 Loop
public static ISkeleton<TInput, TInput> Loop<TInput>(
    this ISkeleton<TInput, bool> condSkel,
    ISkeleton<TInput, TInput> body) =>
    new DelegateSkeleton<TInput, TInput>("Loop",
        (i, ctx) =>
        {
            var cur = i;
            while (condSkel.Execute(cur, ctx)) cur = body.Execute(cur, ctx);
            return cur;
        },
        async (i, ctx) =>
        {
            var cur = i;
            while (await condSkel.ExecuteAsync(cur, ctx).ConfigureAwait(false))
                cur = await body.ExecuteAsync(cur, ctx).ConfigureAwait(false);
            return cur;
        });
```

**差异：** 核心逻辑完全一致，只是命名参数风格不同。

### 6.4 Parallel（并行）

```csharp
// Pipeline 的 Parallel
public static IPipeline<TInput, (TOutput1, TOutput2)> Parallel<TInput, TOutput1, TOutput2>(
    this IPipeline<TInput, TOutput1> first,
    IPipeline<TInput, TOutput2> second)
{
    return new DelegatePipeline<TInput, (TOutput1, TOutput2)>(
        $"Parallel({first.Name},{second.Name})",
        syncWithCtx: (i, ctx) =>
        {
            TOutput1 r1 = default!;
            TOutput2 r2 = default!;
            System.Threading.Tasks.Parallel.Invoke(
                new ParallelOptions { CancellationToken = ctx.CancellationToken },
                () => r1 = first.Execute(i, ctx),
                () => r2 = second.Execute(i, ctx));
            return (r1, r2);
        },
        asyncWithCtx: async (i, ctx) =>
        {
            var t1 = first.ExecuteAsync(i, ctx);
            var t2 = second.ExecuteAsync(i, ctx);
            await Task.WhenAll(t1, t2).ConfigureAwait(false);
            return (t1.Result, t2.Result);  // ← 使用 .Result 获取结果
        });
}

// SkeletonFlow 的 Parallel
public static ISkeleton<TInput, (TOutput1, TOutput2)> Parallel<TInput, TOutput1, TOutput2>(
    this ISkeleton<TInput, TOutput1> s1,
    ISkeleton<TInput, TOutput2> s2) =>
    new DelegateSkeleton<TInput, (TOutput1, TOutput2)>("Parallel",
        syncWithCtx: (i, ctx) =>
        {
            TOutput1 r1 = default!;
            TOutput2 r2 = default!;
            System.Threading.Tasks.Parallel.Invoke(
                new ParallelOptions { CancellationToken = ctx.CancellationToken },
                () => r1 = s1.Execute(i, ctx),
                () => r2 = s2.Execute(i, ctx));
            return (r1, r2);
        },
        asyncWithCtx: async (i, ctx) =>
        {
            var t1 = s1.ExecuteAsync(i, ctx);
            var t2 = s2.ExecuteAsync(i, ctx);
            await Task.WhenAll(t1, t2).ConfigureAwait(false);
            return (t1.Result, t2.Result);  // ← 同样使用 .Result
        });
```

**差异：** 核心逻辑完全一致，包括同步使用 `Parallel.Invoke`、异步使用 `Task.WhenAll` + `.Result` 的写法。

### 6.5 ForEach（集合遍历）

```csharp
// Pipeline 的 ForEach
public static IPipeline<IEnumerable<TInput>, IEnumerable<TOutput>> ForEach<TInput, TOutput>(
    this IPipeline<TInput, TOutput> element)
{
    return new DelegatePipeline<IEnumerable<TInput>, IEnumerable<TOutput>>(
        $"ForEach({element.Name})",
        syncWithCtx: (items, ctx) => items.Select(i => element.Execute(i, ctx)).ToList(),
        asyncWithCtx: async (items, ctx) =>
        {
            var results = new List<TOutput>();
            foreach (var item in items)
                results.Add(await element.ExecuteAsync(item, ctx).ConfigureAwait(false));
            return results;
        });
}

// SkeletonFlow 的 ForEach
public static ISkeleton<IEnumerable<TInput>, IEnumerable<TOutput>> ForEach<TInput, TOutput>(
    this ISkeleton<TInput, TOutput> element) =>
    new DelegateSkeleton<IEnumerable<TInput>, IEnumerable<TOutput>>($"ForEach({element.Name})",
        (src, ctx) => src.Select(i => element.Execute(i, ctx)).ToList(),
        async (src, ctx) =>
        {
            var r = new List<TOutput>();
            foreach (var i in src) r.Add(await element.ExecuteAsync(i, ctx).ConfigureAwait(false));
            return r;
        });
```

**差异：** 核心逻辑完全一致。

### 6.6 扩展方法总体对比

| 扩展方法 | Pipeline | SkeletonFlow | 实现是否一致 |
|---------|:--------:|:------------:|:----------:|
| `Then()` | ✅ | ✅ | **核心逻辑一致**，SkeletonFlow 多传 description |
| `Branch()`（谓词） | ✅ | ✅ | **核心逻辑一致**，SkeletonFlow 多了 null 检查 |
| `Branch()`（管线/骨架） | ✅ | ✅ | **核心逻辑一致** |
| `Loop()`（谓词） | ✅ | ✅ | **核心逻辑一致** |
| `Loop()`（管线/骨架） | ✅ | ✅ | **核心逻辑一致** |
| `Parallel()` | ✅ | ✅ | **核心逻辑完全一致**（含 `.Result` 的写法） |
| `ForEach()` | ✅ | ✅ | **核心逻辑完全一致** |
| `SelectMany()` | ❌ | ✅ | SkeletonFlow 独有 |
| `Switch()` | ✅ | ❌ | Pipeline 独有 |
| `Compose()` | ✅ | ❌ | Pipeline 独有 |
| `TryCatch()` | ✅ | ❌ | Pipeline 独有 |
| `WithTimeout()` | ✅ | ❌ | Pipeline 独有 |
| `WithCache()` | ✅ | ❌ | Pipeline 独有 |
| `WithRetry()` | ✅ | ❌ | Pipeline 独有 |
| `WithLogging()` | ✅ | ❌ | Pipeline 独有 |
| `WithLifecycle()` | ✅ | ❌ | Pipeline 独有 |
| `Tap()` | ✅ | ❌ | Pipeline 独有 |
| `ParallelExec()` | ✅ | ❌ | Pipeline 独有 |
| `ParallelAll()` | ✅ | ❌ | Pipeline 独有 |
| `ForEachParallel()` | ✅ | ❌ | Pipeline 独有 |

---

## 7. 注册表/工厂模式对比

### 7.1 PipelineStageRegistry vs DefaultSkeletonRegistry

| 维度 | PipelineStageRegistry | DefaultSkeletonRegistry |
|------|----------------------|------------------------|
| **注册键类型** | `PipelineStage` 枚举（固定9个阶段） | `string` 类型名称 |
| **注册值类型** | `IPipeline<object, object>` | `Type` 或 `Func<object[], ISkeleton>` 工厂 |
| **缓存机制** | 有 `_cachedPipelines` 缓存 | 有 `_typeCache` 缓存 |
| **构造函数注入** | 无 | **有**（通过 `SelectBestConstructor` 自动匹配） |
| **JSON 参数解析** | 无 | **有**（支持 `JsonElement` 反序列化） |
| **服务引用** | 无 | **有**（`$service:TypeName` 语法） |
| **复合骨架支持** | 无 | **有**（`ICompositeSkeleton.SetChildren`） |
| **执行策略** | 有（`ExecuteAll<T>` 支持策略参数） | 无 |

**DefaultSkeletonRegistry 的独有能力——构造函数自动匹配：**

```csharp
// SkeletonFlow 独有：自动选择最佳构造函数
private ConstructorInfo SelectBestConstructor(Type type, IDictionary<string, object> parameters, IServiceProvider services)
{
    var constructors = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length);
    // 尝试每个构造函数，找到参数都能满足的那个
    foreach (var ctor in constructors)
        if (TryBuildConstructorArgs(ctor, parameters, services, out _))
            candidates.Add(ctor);
    return candidates[0];
}
```

**DefaultSkeletonRegistry 的独有能力——JSON 值解析：**

```csharp
// SkeletonFlow 独有：支持从 JsonElement 反序列化参数
private object ResolveValue(object val, Type target, IServiceProvider services) => val switch
{
    string s when s.StartsWith("$service:") => ResolveService(s[9..], services),
    System.Text.Json.JsonElement je => target != null
        ? System.Text.Json.JsonSerializer.Deserialize(je, target, JsonOptions)
        : ConvertJsonElementToObject(je),
    _ => val
};
```

---

## 8. Pipeline 独有功能详解

### 8.1 错误收集体系（ErrorCollector）

Pipeline 有完整的错误收集体系，SkeletonFlow 完全没有：

```csharp
// 错误级别
public enum ErrorLevel { Info, Warning, Error, Fatal }

// 错误条目
public class ErrorEntry
{
    public string Code { get; }
    public string Message { get; }
    public ErrorLevel Level { get; }
    public DateTime Timestamp { get; }
    public Exception? Exception { get; }
}

// 线程安全的错误收集器
public class ErrorCollector
{
    public void Add(string code, string message, ErrorLevel level, Exception? exception = null);
    public void Merge(ErrorCollector other);           // 合并另一个收集器
    public string GetErrorReport();                    // 生成格式化报告
    public bool HasFatalErrors { get; }
    public bool HasRealErrors { get; }
    public bool HasWarnings { get; }
}
```

### 8.2 管线执行结果封装（PipelineResult）

```csharp
public class PipelineResult
{
    public PipelineResultStatus Status { get; }  // Success/Failed/Aborted/PartialSuccess
    public object? Data { get; }
    public ErrorCollector Errors { get; }
    public PipelineContext Context { get; }
    public bool IsSuccess => Status == Success || Status == PartialSuccess;
    public bool IsAborted => Status == Aborted;
    public bool HasErrors => Errors.HasErrors;
    public bool HasFatalErrors => Errors.HasFatalErrors;
    
    // 工厂方法
    public static PipelineResult Successful(object? data = null);
    public static PipelineResult Failed(ErrorCollector errors);
    public static PipelineResult Aborted(ErrorCollector errors);
    public static PipelineResult CompletedWithWarnings(object? data, ErrorCollector errors, PipelineContext context);
    
    // 类型转换
    public PipelineResult<T> ToGeneric<T>() where T : class;
}
```

### 8.3 中间件模式（MiddlewarePipeline）

```csharp
// Pipeline 独有：类似 ASP.NET Core 的中间件模式
public class MiddlewarePipeline<TRequest, TResponse>
{
    public MiddlewarePipeline<TRequest, TResponse> Use(MiddlewareDelegate<TRequest, TResponse> middleware);
    public MiddlewarePipeline<TRequest, TResponse> SetCoreHandler(RequestDelegate<TRequest, TResponse> handler);
    public RequestDelegate<TRequest, TResponse> Build();  // 从后往前构建中间件链
    public Task<TResponse> ExecuteAsync(TRequest request);
    public IPipeline<TRequest, TResponse> ToPipeline(string? name = null);
}
```

### 8.4 策略模式（StrategyRegistry）

```csharp
// Pipeline 独有：通用策略注册表
public class StrategyRegistry<TKey, TInput, TOutput> where TKey : notnull
{
    public void Register(TKey key, IStrategy<TInput, TOutput> strategy);
    public void RegisterOrUpdate(TKey key, IStrategy<TInput, TOutput> strategy);
    public IStrategy<TInput, TOutput> Get(TKey key);
    public bool TryGet(TKey key, out IStrategy<TInput, TOutput>? strategy);
    public TOutput Execute(TKey key, TInput input);
    public bool TryExecute(TKey key, TInput input, out TOutput? result);
}
```

### 8.5 执行策略（IExecutionStrategy）

```csharp
// Pipeline 独有：三种执行策略
public interface IExecutionStrategy
{
    string Name { get; }
    T Execute<T>(IReadOnlyList<IPipeline<T, T>> stages, T input);
    Task<T> ExecuteAsync<T>(IReadOnlyList<IPipeline<T, T>> stages, T input);
}

// 顺序执行
public class SequentialStrategy : IExecutionStrategy { ... }

// 并行执行（需要 MergeFunction 合并结果）
public class ParallelStrategy : IExecutionStrategy
{
    public Func<IReadOnlyList<object>, object>? MergeFunction { get; set; }
}

// 条件执行
public class ConditionalStrategy : IExecutionStrategy { ... }
```

### 8.6 会话管理（Session/SessionManager）

```csharp
// Pipeline 独有：管理多个目标的处理会话
public class Session<TTarget, TConfig, TContext> where TContext : PipelineContext
{
    public string Id { get; }
    public TTarget Target { get; }
    public TConfig Config { get; }
    public TContext Context { get; }
    public DateTime CreatedAt { get; }
    public DateTime? CompletedAt { get; }
    public SessionStatus Status { get; }  // Pending/Running/Completed/Failed
    public ErrorCollector Errors { get; }
}

public class SessionManager
{
    public Session<TTarget, TConfig, TContext> CreateSession<TTarget, TConfig, TContext>(...);
    public Session<TTarget, TConfig, TContext>? GetSession<TTarget, TConfig, TContext>(string id);
    public void CompleteSession(string id);
    public void FailSession(string id, ErrorCollector errors);
    public IEnumerable<Session<...>> GetActiveSessions();
}
```

### 8.7 守卫/重入保护（Guard）

```csharp
// Pipeline 独有：防止重复执行
public struct Guard
{
    public enum GuardMode { Throw, Skip }
    
    public static GuardMode Mode { get; set; }
    public static bool IsExecuting { get; }
    public static IDisposable Enter();  // 进入执行区域，退出时自动释放
}
```

### 8.8 管线阶段枚举（PipelineStage）

```csharp
// Pipeline 独有：9 个预定义阶段
public enum PipelineStage
{
    Initialize,   // 初始化
    Load,         // 加载
    Validate,     // 验证
    Process,      // 处理
    Map,          // 映射
    Transform,    // 转换
    Save,         // 保存
    Cleanup,      // 清理
    Complete      // 完成
}
```

### 8.9 管线状态枚举（PipelineStatus）

```csharp
// Pipeline 独有：5 种状态
public enum PipelineStatus
{
    Idle,       // 空闲
    Running,    // 运行中
    Completed,  // 已完成
    Failed,     // 失败
    Cancelled   // 已取消
}
```

### 8.10 类实现组合子（Combinators.cs）

Pipeline 有 8 个类实现组合子，SkeletonFlow 只有 2 个：

| Pipeline 组合子 | 说明 |
|----------------|------|
| `ForEachPipeline<TContext, TItem>` | 集合遍历（带上下文） |
| `ParallelPipeline<TContext>` | 并行执行多个阶段 |
| `PipelineSwitch<TContext, TKey>` | 多路分支（类似 switch-case） |
| `PipelineBranch<TContext>` | 双路分支（if-else） |
| `PipelineTryCatch<TContext>` | 异常捕获 |
| `PipelineRetry<TContext>` | 重试机制 |
| `ConditionalPipeline<TContext>` | 条件执行 |
| `PipelineAdapter<TContext, TIn, TOut>` | 类型适配 |

### 8.11 扩展方法装饰器（WithXxx）

Pipeline 有 7 个装饰器扩展方法，SkeletonFlow 完全没有：

| 装饰器 | 说明 |
|--------|------|
| `WithTimeout(TimeSpan)` | 超时控制 |
| `WithCache(TimeSpan)` | 结果缓存 |
| `WithRetry(int, int)` | 失败重试 |
| `WithLogging()` | 日志记录 |
| `WithLifecycle()` | 生命周期管理 |
| `Tap(Action)` | 副作用（不改变数据） |
| `TryCatch(Func<Exception, TOutput>)` | 异常处理 |

---

## 9. SkeletonFlow 独有功能详解

### 9.1 内置数据操作骨架（BuiltInSkeletons）

这是 SkeletonFlow 最核心的特色功能，Pipeline 完全没有：

```csharp
public static class BuiltInSkeletons
{
    // 过滤：保留满足条件的元素
    public static ISkeleton<IEnumerable<T>, IEnumerable<T>> Filter<T>(
        string name, Func<T, bool> predicate);

    // 映射：将每个元素转换为新类型
    public static ISkeleton<IEnumerable<T>, IEnumerable<TResult>> Map<T, TResult>(
        string name, Func<T, TResult> selector);

    // 归约：将集合聚合为单个值
    public static ISkeleton<IEnumerable<T>, TAccumulate> Reduce<T, TAccumulate>(
        string name, TAccumulate seed, Func<TAccumulate, T, TAccumulate> func);

    // 跳过前 N 个元素
    public static ISkeleton<IEnumerable<T>, IEnumerable<T>> Skip<T>(
        string name, int count);

    // 取前 N 个元素
    public static ISkeleton<IEnumerable<T>, IEnumerable<T>> Take<T>(
        string name, int count);

    // 去重
    public static ISkeleton<IEnumerable<T>, IEnumerable<T>> Distinct<T>(
        string name);

    // 分组
    public static ISkeleton<IEnumerable<T>, IEnumerable<IGrouping<TKey, T>>> GroupBy<T, TKey>(
        string name, Func<T, TKey> keySelector);

    // 排序
    public static ISkeleton<IEnumerable<T>, IEnumerable<T>> OrderBy<T, TKey>(
        string name, Func<T, TKey> keySelector, bool ascending = true);

    // 取第一个元素
    public static ISkeleton<IEnumerable<T>, T> First<T>(
        string name);

    // 取唯一元素
    public static ISkeleton<IEnumerable<T>, T> Single<T>(
        string name);

    // 转 List
    public static ISkeleton<IEnumerable<T>, List<T>> ToList<T>(
        string name);

    // 转 Array
    public static ISkeleton<IEnumerable<T>, T[]> ToArray<T>(
        string name);

    // 并行遍历
    public static ISkeleton<IEnumerable<T>, IEnumerable<T>> ParallelForEach<T>(
        string name, Action<T> action, int maxDegreeOfParallelism = -1);
}
```

### 9.2 JSON 配置驱动（SkeletonConfig + JsonConfigParser）

```csharp
// SkeletonFlow 独有：配置模型
public class SkeletonConfig
{
    public string Name { get; set; }
    public string Type { get; set; }           // 骨架类型名称
    public Dictionary<string, object> Parameters { get; set; }
    public List<SkeletonConfig> Children { get; set; }  // 子骨架（用于复合骨架）
}

// 配置解析器接口
public interface IConfigParser
{
    SkeletonConfig Parse(string json);
    SkeletonConfig Parse(JsonElement element);
}

// JSON 配置解析器
public class JsonConfigParser : IConfigParser
{
    public SkeletonConfig Parse(string json);
    public SkeletonConfig Parse(JsonElement element);
}
```

### 9.3 动态类型解析（DefaultSkeletonRegistry）

```csharp
// SkeletonFlow 独有：按名称解析骨架类型
public class DefaultSkeletonRegistry : ISkeletonRegistry
{
    // 注册类型
    public void Register<T>() where T : ISkeleton;
    public void Register(string name, Type type);
    public void Register(string name, Func<object[], ISkeleton> factory);
    
    // 解析骨架（支持构造函数参数注入）
    public ISkeleton Resolve(string name, IDictionary<string, object> parameters = null,
        IServiceProvider services = null);
    
    // 从配置创建骨架
    public ISkeleton CreateFromConfig(SkeletonConfig config, IServiceProvider services = null);
    
    // 自动选择最佳构造函数
    private ConstructorInfo SelectBestConstructor(Type type, ...);
    
    // JSON 值解析（支持 $service: 语法）
    private object ResolveValue(object val, Type target, IServiceProvider services);
}
```

### 9.4 环境适配器（IExecutionAdapter\<TEnvironment\>）

```csharp
// SkeletonFlow 独有：从不同环境提取输入并应用输出
public interface IExecutionAdapter<TEnvironment>
{
    TInput ExtractInput<TInput>(TEnvironment environment);
    void ApplyOutput<TOutput>(TEnvironment environment, TOutput output);
}
```

### 9.5 骨架引擎（SkeletonEngine）

```csharp
// SkeletonFlow 独有：配置驱动的执行引擎
public class SkeletonEngine
{
    public SkeletonEngine(ISkeletonRegistry registry, IConfigParser parser = null);
    
    // 从 JSON 配置加载并执行
    public TOutput Execute<TOutput>(string jsonConfig, TInput input,
        IServiceProvider services = null);
    
    // 从 SkeletonConfig 执行
    public TOutput Execute<TOutput>(SkeletonConfig config, TInput input,
        IServiceProvider services = null);
}
```

### 9.6 复合骨架（SequentialSkeleton / ParallelForEachSkeleton）

```csharp
// SkeletonFlow 独有：自动类型链匹配的顺序执行
public class SequentialSkeleton<TInput, TOutput> : SkeletonBase<TInput, TOutput>, ICompositeSkeleton
{
    public void SetChildren(IEnumerable<ISkeleton> children);
    // 自动匹配前一个骨架的输出类型与后一个骨架的输入类型
    protected override TOutput ExecuteCore(TInput input, SkeletonContext context);
}

// SkeletonFlow 独有：并行遍历
public class ParallelForEachSkeleton<TInput, TOutput> : SkeletonBase<TInput, TOutput>, ICompositeSkeleton
{
    public void SetChildren(IEnumerable<ISkeleton> children);
    // 对每个输入元素并行执行所有子骨架
    protected override TOutput ExecuteCore(TInput input, SkeletonContext context);
}
```

### 9.7 SelectMany 扩展方法

```csharp
// SkeletonFlow 独有：扁平化映射
public static ISkeleton<IEnumerable<TInput>, IEnumerable<TOutput>> SelectMany<TInput, TOutput>(
    this ISkeleton<TInput, IEnumerable<TOutput>> element) =>
    new DelegateSkeleton<IEnumerable<TInput>, IEnumerable<TOutput>>($"SelectMany({element.Name})",
        (src, ctx) => src.SelectMany(i => element.Execute(i, ctx)).ToList(),
        async (src, ctx) =>
        {
            var r = new List<TOutput>();
            foreach (var i in src) r.AddRange(await element.ExecuteAsync(i, ctx).ConfigureAwait(false));
            return r;
        });
```

### 9.8 Placeholder 机制

```csharp
// SkeletonFlow 独有：占位符
public class Placeholder { ... }

// SkeletonContext 中的占位符存取
public Placeholder GetPlaceholder(string key);
public void SetPlaceholder(string key, Placeholder placeholder);
```

### 9.9 Unit 类型

```csharp
// SkeletonFlow 独有：函数式编程占位类型（类似 void 的类型安全版本）
public struct Unit { ... }
```

---

## 10. 使用场景指南

### 10.1 什么时候用 Pipeline？

| 场景 | 说明 |
|------|------|
| **复杂多阶段处理流程** | 需要明确的阶段划分（初始化→加载→验证→处理→保存→清理） |
| **需要企业级错误管理** | 错误分级、错误收集、错误报告、致命错误中止 |
| **需要重试/超时/缓存** | 网络请求、文件操作等需要容错机制的场景 |
| **需要中间件模式** | 请求/响应处理链，如 HTTP 中间件风格 |
| **需要会话管理** | 管理多个目标的处理会话生命周期 |
| **需要策略模式** | 根据运行时条件选择不同的处理策略 |
| **需要三体系架构** | 同时需要链式传参、共享配置、编排器三种模式 |
| **需要类实现组合子** | ForEachPipeline、PipelineSwitch、PipelineTryCatch 等类实现 |

### 10.2 什么时候用 SkeletonFlow？

| 场景 | 说明 |
|------|------|
| **数据变换管道** | 对集合进行 Filter → Map → Reduce 等操作 |
| **需要 JSON 配置驱动** | 通过配置文件动态组装处理流程 |
| **需要动态类型解析** | 运行时根据类型名称创建处理单元 |
| **需要环境适配** | 从不同环境（GUI/CLI/Test）提取输入并应用输出 |
| **轻量级场景** | 不需要错误收集、重试等企业级特性 |
| **快速原型开发** | 利用内置骨架快速搭建数据处理流程 |
| **需要 Placeholder 机制** | 在骨架间传递占位符数据 |

### 10.3 什么时候两者配合使用？

```
SkeletonFlow 提供"做什么"（数据变换操作）
         +
Pipeline 提供"怎么管理执行"（错误处理、重试、超时）
         =
完整的处理框架
```

典型架构：

```csharp
// 1. 用 SkeletonFlow 定义数据变换
var filter = BuiltInSkeletons.Filter("成年人", (Person p) => p.Age >= 18);
var map = BuiltInSkeletons.Map("提取姓名", (Person p) => p.Name);

// 2. 用 Pipeline 包装并添加管理能力
var pipeline = new DelegatePipeline<List<Person>, List<string>>(
    "处理流程",
    sync: (input, ctx) => map.Execute(filter.Execute(input, ctx), ctx))
    .WithRetry(3, 100)
    .WithLogging()
    .WithTimeout(TimeSpan.FromSeconds(30));

// 3. 执行
var result = pipeline.Execute(people);
```

---

## 11. 整合建议

### 11.1 建议方案：适度整合，保留特色

```
BasicMethod/Pipeline/          ← 保留，作为基础设施层
  Core.cs                      ← 精简：移除与 SkeletonFlow 重复的抽象基类
  Handlers.cs                  ← 保留：错误处理、重试、缓存、超时等
  Registry.cs                  ← 保留：策略注册表
  Models.cs                    ← 保留：ErrorCollector、Session 等
  Combinators.cs               ← 保留：类实现组合子

BasicMethod/FrameMethod/
  SkeletonFlow.cs              ← 精简：移除与 Pipeline 重复的抽象，改为基于 IPipeline 实现
  SkeletonFlow.BuiltIn.cs      ← 保留：Filter/Map/Reduce 等内置骨架
  SkeletonFlow.Config.cs       ← 保留：JSON 配置解析 + 动态类型解析
  SkeletonFlow.Engine.cs       ← 保留：SkeletonEngine
```

### 11.2 具体整合点

| 整合内容 | 当前状态 | 目标 |
|---------|---------|------|
| `SkeletonBase<TInput,TOutput>` | 独立实现 | 改为继承 `PipelineBase<TInput,TOutput>` |
| `SkeletonContext` | 独立实现 | 改为继承或包装 `PipelineContext` |
| `DelegateSkeleton` | 独立实现 | 改为包装 `DelegatePipeline` |
| `Then`/`Branch`/`Loop`/`Parallel`/`ForEach` | 两套重复实现 | 统一到 Pipeline，SkeletonFlow 直接复用 |

### 11.3 保留独立的特色功能

| 功能 | 说明 |
|------|------|
| `BuiltInSkeletons` | Filter/Map/Reduce 等数据操作（SkeletonFlow 独有） |
| `SkeletonConfig` + `JsonConfigParser` | 配置驱动能力（SkeletonFlow 独有） |
| `DefaultSkeletonRegistry` | 动态类型解析（SkeletonFlow 独有） |
| `SkeletonEngine` | 引擎驱动执行（SkeletonFlow 独有） |
| `IExecutionAdapter<TEnvironment>` | 环境适配（SkeletonFlow 独有） |
| `Placeholder` | 占位符机制（SkeletonFlow 独有） |

---

## 12. 附录：核心类型速查

### 12.1 Pipeline 核心类型

```csharp
// ===== 核心接口 =====
IPipeline<TIn, TOut>              // 管线核心接口
IStage<TTarget, TConfig, TContext> // 阶段接口（体系B）
IPipelineStage<TInput, TOutput>   // 管线阶段接口（体系C）
IPipelineStage<TContext>           // 通用管线阶段接口
IStagePipeline<TContext>           // 通用阶段编排器接口

// ===== 核心类 =====
PipelineContext                    // 管线上下文
PipelineBase<TIn, TOut>           // 管线抽象基类
DelegatePipeline<TIn, TOut>       // 委托管线
Pipeline                          // 管线编排器（非泛型）
Pipeline<TInput, TOutput>         // 管线编排器（泛型）
StageCollection<TStage>           // 阶段集合管理基类

// ===== 结果与错误 =====
PipelineResult                    // 管线执行结果
PipelineResult<T>                 // 管线执行结果（泛型）
ErrorCollector                    // 错误收集器
ErrorEntry                        // 错误条目
ErrorLevel                        // 错误级别枚举
Guard                             // 守卫标志

// ===== 策略 =====
IStrategy<TIn, TOut>              // 策略接口
StrategyRegistry<TKey, TIn, TOut> // 策略注册表
IExecutionStrategy                // 执行策略接口
SequentialStrategy                // 顺序执行策略
ParallelStrategy                  // 并行执行策略
ConditionalStrategy               // 条件执行策略

// ===== 模型 =====
PipelineStage                     // 管线阶段枚举（9个阶段）
PipelineStatus                    // 管线状态枚举（5种状态）
Session<TTarget, TConfig, TContext> // 会话
SessionManager                    // 会话管理器

// ===== 中间件 =====
MiddlewarePipeline<TRequest, TResponse> // 中间件管线
MiddlewareDelegate<TRequest, TResponse> // 中间件委托
RequestDelegate<TRequest, TResponse>    // 请求处理委托

// ===== 组合子（类实现） =====
ForEachPipeline<TContext, TItem>  // 集合遍历管线
ParallelPipeline<TContext>        // 并行执行管线
PipelineSwitch<TContext, TKey>    // 多路分支管线
PipelineBranch<TContext>          // 双路分支管线
PipelineTryCatch<TContext>        // 异常处理管线
PipelineRetry<TContext>           // 重试管线
ConditionalPipeline<TContext>     // 条件执行管线
PipelineAdapter<TContext, TIn, TOut> // 适配管线
```

### 12.2 SkeletonFlow 核心类型

```csharp
// ===== 核心接口 =====
ISkeleton                         // 骨架标记接口
ISkeleton<TInput, TOutput>        // 骨架核心接口
ICompositeSkeleton                 // 复合骨架接口
ISkeletonRegistry                  // 骨架注册表接口
IConfigParser                      // 配置解析器接口
IExecutionAdapter<TEnvironment>    // 环境适配器接口

// ===== 核心类 =====
SkeletonContext                    // 骨架上下文
SkeletonBase<TInput, TOutput>     // 骨架抽象基类
SyncSkeletonBase<TInput, TOutput> // 同步骨架基类
DelegateSkeleton<TInput, TOutput> // 委托骨架
DefaultSkeletonRegistry            // 默认骨架注册表
SkeletonEngine                     // 骨架引擎

// ===== 配置 =====
SkeletonConfig                     // 骨架配置
JsonConfigParser                   // JSON 配置解析器

// ===== 内置骨架 =====
BuiltInSkeletons                   // 内置骨架工厂类
  - Filter<T>()                   // 过滤
  - Map<T, TResult>()             // 映射
  - Reduce<T, TAccumulate>()      // 归约
  - Skip<T>()                     // 跳过
  - Take<T>()                     // 取前N个
  - Distinct<T>()                 // 去重
  - GroupBy<T, TKey>()            // 分组
  - OrderBy<T, TKey>()            // 排序
  - First<T>()                    // 取第一个
  - Single<T>()                   // 取唯一
  - ToList<T>()                   // 转列表
  - ToArray<T>()                  // 转数组
  - ParallelForEach<T>()          // 并行遍历

// ===== 复合骨架 =====
SequentialSkeleton<TInput, TOutput>   // 顺序复合骨架
ParallelForEachSkeleton<TInput, TOutput> // 并行遍历复合骨架

// ===== 工具 =====
Unit                              // 函数式占位类型
Placeholder                       // 占位符
```

---

> **文档维护**: 本文档由代码分析生成，基于实际代码实现进行对比。如需更新请同步修改对应代码文件。


