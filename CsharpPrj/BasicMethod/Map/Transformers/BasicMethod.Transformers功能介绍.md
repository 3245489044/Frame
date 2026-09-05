# BasicMethod.Transformers — 转换器框架功能介绍

> **模块定位**：与图（`ICoreGraph`）、状态机（`IStateMachine`）平级的第三大基础抽象  
> **数学基础**：函数 `f: A → B`  
> **设计哲学**：万物皆函数，一切皆可转换

---

## 1. 概述

`BasicMethod.Transformers` 是 WorkFlowPrj 四层架构中 **BasicMethod（基础工具库）** 层的重要组成部分。它提供了一个以**函数式编程**为核心理念的通用转换器框架，将"转换"这一基本操作抽象为统一的接口体系。

### 1.1 设计目标

- **统一抽象**：将一切"从 A 到 B"的操作统一为 `ITransformer<TInput, TOutput>` 接口
- **函数复合**：支持转换器的组合（`g ∘ f`），构建复杂转换链路
- **管线编排**：多步骤顺序执行，支持失败策略、执行追踪
- **横切关注点**：通过装饰器模式实现缓存、重试、超时、日志等能力
- **控制反转**：通过生命周期、元数据、装饰器接口，让外部框架自动发现和管理转换器

### 1.2 适用场景

- 数据清洗与转换（ETL）
- 集合操作（映射、过滤、聚合、去重）
- 业务规则验证
- 管线化数据处理
- 需要缓存/重试/超时的远程调用包装

---

## 2. 核心接口体系

### 2.1 同步转换器

```csharp
public interface ITransformer<in TInput, out TOutput>
{
    string Name { get; }
    TOutput Transform(TInput input);
}
```

最核心的抽象。任何实现了此接口的类型都是一个"转换器"，接受 `TInput` 返回 `TOutput`。

### 2.2 异步转换器

```csharp
public interface IAsyncTransformer<in TInput, TOutput>
{
    string Name { get; }
    Task<TOutput> TransformAsync(TInput input, CancellationToken cancellationToken = default);
}
```

支持异步操作的转换器，适用于 I/O 密集型或远程调用场景。

### 2.3 异步适配器

`AsyncTransformerAdapter<TInput, TOutput>` 将同步转换器适配为异步转换器，实现同步到异步的无缝桥接：

```csharp
var syncTransformer = new MyTransformer();
IAsyncTransformer<string, int> asyncTransformer = new AsyncTransformerAdapter<string, int>(syncTransformer);
```

### 2.4 函数复合

`ComposedAsyncTransformer<TInput, TMiddle, TOutput>` 实现异步转换器的函数复合（`f₁ ∘ f₂`），名称以数学符号 `∘` 标识：

```
Name = "FirstTransformer ∘ SecondTransformer"
```

---

## 3. 函数复合（Function Composition）

### 3.1 Then() — 单步复合

通过扩展方法 `Then()` 实现两个转换器的函数复合：

```csharp
// f: A → B, g: B → C => g ∘ f: A → C
ITransformer<string, int> transformer = parser.Then(validator);
```

数学语义：`g ∘ f`，先执行 `f`，再将结果传给 `g`。

### 3.2 内部实现

`ComposedTransformer<TInput, TMiddle, TOutput>` 是 `Then()` 的内部实现：

```csharp
internal class ComposedTransformer<TInput, TMiddle, TOutput> : ITransformer<TInput, TOutput>
{
    public string Name => $"{_first.Name} ∘ {_second.Name}";

    public TOutput Transform(TInput input)
        => _second.Transform(_first.Transform(input));
}
```

### 3.3 Func → Transformer

通过 `ToTransformer()` 扩展方法，将任意 `Func<TInput, TOutput>` 委托包装为 `ITransformer`：

```csharp
var transformer = new Func<string, int>(s => s.Length).ToTransformer("LengthCounter");
```

---

## 4. 管线编排（Pipeline）

`Pipeline<TInput, TOutput>` 将多个 `ITransformer` 组合为顺序执行的管线，扩展了 `Then()` 的单步复合能力为多步骤编排。

### 4.1 核心概念

| 概念 | 说明 |
|------|------|
| **Pipeline** | 多步骤顺序执行管线，本身也是 `ITransformer` |
| **IPipelineStep** | 管线步骤接口，返回 `PipelineStepResult`（含成功/失败/耗时/计数） |
| **PipelineResult** | 管线执行结果，包含总步骤数、成功/失败数、每步详情 |
| **PipelineConfig** | 管线配置（失败时停止、详细日志、缓存开关） |

### 4.2 基本用法

```csharp
var pipeline = new Pipeline<string, PipelineResult>("MyPipeline")
    .AddStep(step1)
    .AddStep(step2)
    .AddStep(transformer3);  // ITransformer 自动适配为步骤

var result = pipeline.Transform(input);
// result.SuccessfulSteps, result.FailedSteps, result.StepDetails...
```

### 4.3 步骤管理

```csharp
pipeline.AddStep(step);           // 末尾添加
pipeline.InsertStep(index, step); // 指定位置插入
pipeline.RemoveStep(step);        // 移除
pipeline.ClearSteps();            // 清空
```

### 4.4 失败策略

```csharp
var config = new PipelineConfig
{
    StopOnFailure = true,   // 失败时停止后续步骤（默认）
    VerboseLogging = true,  // 启用详细日志
    EnableCaching = false   // 启用缓存
};
var pipeline = new Pipeline<string, PipelineResult>("SafePipeline", config);
```

失败时抛出 `PipelineExecutionException`，其中包含失败前的部分执行结果。

### 4.5 便捷创建

```csharp
// 单转换器转管线
var pipeline = transformer.ToPipeline();

// 多转换器组合为管线
var pipeline = PipelineExtensions.Compose("MyPipeline", t1, t2, t3);
```

---

## 5. 装饰器模式（Decorator）

装饰器模式为转换器叠加横切关注点，实现**控制反转**——外部框架通过 `ITransformDecorator` 自动发现和应用装饰器。

### 5.1 装饰器接口

```csharp
public interface ITransformDecorator
{
    string DecoratorType { get; }       // 如 "retry", "cache", "timeout", "log"
    string? Description { get; }
    ITransformer<TInput, TOutput> Apply<TInput, TOutput>(ITransformer<TInput, TOutput> inner);
    bool CanHandle(object? configValue); // 判断是否能处理指定配置值
}
```

### 5.2 内置装饰器

| 装饰器 | 类型标识 | 功能 |
|--------|---------|------|
| **LogDecorator** | `"log"` | 转换前后通过 `TransformContext` 记录日志 |
| **TimeoutDecorator** | `"timeout"` | 设置转换超时时间，超时抛出 `TimeoutException` |
| **RetryDecorator** | `"retry"` | 失败时自动重试，支持指数退避 |

### 5.3 装饰器链

`DecoratorChain` 将多个装饰器组合应用：

```csharp
var chain = new DecoratorChain()
    .Add(new LogDecorator())
    .Add(new RetryDecorator(3))
    .Add(new TimeoutDecorator(TimeSpan.FromSeconds(10)));

var wrapped = chain.Apply(originalTransformer);
// 装饰顺序（从外到内）：Log → Retry → Timeout → original
```

### 5.4 扩展方法

```csharp
// 应用装饰器链
transformer.Decorate(chain);

// 应用单个装饰器
transformer.Decorate(new LogDecorator());
```

### 5.5 IoC 自动发现

框架通过 `ITransformDecorator.CanHandle(configValue)` 判断是否需要应用装饰器。例如，JSON 配置中 `"retry": 3` 会自动触发 `RetryDecorator` 的应用。

---

## 6. 内置转换器

### 6.1 缓存转换器（CacheTransformer）

包装任意 `ITransformer`，添加缓存层。使用 `ConcurrentDictionary<TInput, Lazy<TOutput>>` 保证线程安全，每个键只计算一次。

```csharp
var cached = new CacheTransformer<string, int>(expensiveTransformer);
var result1 = cached.Transform("key"); // 执行计算
var result2 = cached.Transform("key"); // 命中缓存

cached.ClearCache();     // 清空缓存
cached.RemoveCache("key"); // 移除指定缓存
```

**线程安全设计**：`GetOrAdd` + `Lazy<TOutput>(ExecutionAndPublication)` 保证多个线程同时请求同一 key 时，只有一个执行计算。

### 6.2 条件转换器（ConditionalTransformer）

根据谓词选择不同的转换分支，数学基础：`if-then-else`。

```csharp
var conditional = new ConditionalTransformer<string, string>(
    "IfEmpty",
    input => string.IsNullOrEmpty(input),
    new DefaultValueTransformer("N/A"),
    new TrimTransformer()
);
```

### 6.3 容错转换器（TryTransformer）

捕获内部转换器的异常，以 `Result<TOutput>` 形式返回而非抛出。

```csharp
var safeTransformer = new TryTransformer<string, int>(riskyTransformer);
Result<int> result = safeTransformer.Transform(input);

if (result.IsSuccess)
    UseValue(result.Value!);
else
    LogError(result.ErrorMessage);
```

### 6.4 分组归约器（GroupReducer）

按匹配键分组，每组通过归约策略归约为一个元素。数学基础：等价关系将集合划分为等价类，从每个类中选代表元。

```csharp
// 按属性去重，保留第一个
var reducer = new GroupReducer<Person>(
    "DeduplicateByName",
    new KeySelectorMatch<Person, string>(p => p.Name),
    new KeepFirstReduce<Person>()
);

var unique = reducer.Transform(people);
```

**匹配策略**：
- `IdentityMatch<T>` — 按元素本身匹配
- `KeySelectorMatch<T, TKey>` — 按指定键选择器匹配

**归约策略**：
- `KeepFirstReduce<T>` — 保留第一个（默认）
- `KeepLastReduce<T>` — 保留最后一个
- `FuncReduce<T>` — 自定义归约函数

### 6.5 集合操作

| 转换器 | 数学基础 | 说明 |
|--------|---------|------|
| **Aggregator<TIn, TOut>** | `f: T[] → R` | 将集合聚合为单个值 |
| **Filter<T>** | `p: T → bool` | 根据谓词保留/移除元素 |
| **Mapper<TIn, TOut>** | `f: T → U` | 对集合中每个元素映射 |

```csharp
var sumAggregator = new Aggregator<int, int>("Sum", nums => nums.Sum());
var evenFilter = new Filter<int>("Even", n => n % 2 == 0);
var toStringMapper = new Mapper<int, string>("ToString", n => n.ToString());
```

### 6.6 验证器（Validator）

数学基础：谓词函数 `p: T → bool`，返回验证结果。

```csharp
var validator = new Validator<string>(
    "NotEmpty",
    s => !string.IsNullOrEmpty(s),
    "字符串不能为空"
);

ValidationResult result = validator.Transform(input);
if (!result.IsValid)
    Console.WriteLine(result.Error);
```

### 6.7 复合匹配键（MatchKey）

为多维匹配提供便捷构建方式，统一返回 `ValueTuple` 保证 `Dictionary` 中哈希和相等性行为一致。

```csharp
// 2-5 维匹配键
var key = MatchKey.Of(person.Name, person.Age, person.City);
```

---

## 7. 上下文传播（TransformContext）

`TransformContext` 提供转换器之间的共享状态容器，支持 **AsyncLocal 隐式传播**，实现 IoC 所需的上下文注入。

### 7.1 核心特性

- **AsyncLocal 隐式传播**：在 `async/await` 调用链中自动传播
- **作用域嵌套**：支持父子作用域，子作用域可递归查找父作用域的数据
- **共享数据**：任意类型的键值对存储
- **日志注入**：通过 `Logger` 委托记录日志
- **取消令牌**：支持异步操作的取消

### 7.2 基本用法

```csharp
// 外部框架设置上下文
using (TransformContext.BeginScope())
{
    TransformContext.Current!.Set("userId", "123");
    TransformContext.Current!.Logger = msg => Console.WriteLine(msg);
    
    var result = transformer.Transform(input);
}

// 转换器内部读取
public class MyTransformer : ITransformer<string, string>
{
    public string Name => "MyTransformer";
    
    public string Transform(string input)
    {
        var userId = TransformContext.Current?.Get<string>("userId");
        TransformContext.Current?.Log($"处理用户 {userId} 的输入");
        // ...
    }
}
```

### 7.3 作用域嵌套

```csharp
using (TransformContext.BeginScope(ctx => ctx.Set("level", "outer")))
{
    // 当前作用域
    using (TransformContext.BeginScope(ctx => ctx.Set("level", "inner")))
    {
        var level = TransformContext.Current?.Get<string>("level"); // "inner"
        var outer = TransformContext.Current?.Get<string>("outer"); // 递归查找父作用域
    }
    // 自动恢复父作用域
}
```

### 7.4 快照

```csharp
var snapshot = TransformContext.Current?.Snapshot(); // 浅拷贝当前上下文
```

---

## 8. 生命周期与元数据

### 8.1 生命周期接口（ITransformLifecycle）

提供转换前/后/错误/释放等钩子，支持同步和异步双版本，避免死锁风险。

| 钩子 | 同步版本 | 异步版本 | 触发时机 |
|------|---------|---------|---------|
| 转换前 | `OnBefore<T>` | `OnBeforeAsync<T>` | 转换执行前 |
| 转换成功 | `OnAfter<TIn,TOut>` | `OnAfterAsync<TIn,TOut>` | 转换成功后 |
| 转换失败 | `OnError<T>` | `OnErrorAsync<T>` | 转换异常时 |
| 释放资源 | `OnDispose()` | — | 作用域结束时 |

```csharp
// 通过扩展方法注入生命周期
transformer.WithLifecycle(
    onBefore: msg => Console.WriteLine($"开始: {msg}"),
    onAfter: msg => Console.WriteLine($"完成: {msg}"),
    onError: msg => Console.WriteLine($"失败: {msg}")
);
```

### 8.2 元数据接口（ITransformMetadata）

描述转换器的类型信息、版本、参数等，让外部框架能"理解"转换器，无需硬编码具体类型。

```csharp
public interface ITransformMetadata
{
    string Name { get; }
    Type InputType { get; }
    Type OutputType { get; }
    string? Description { get; }
    string Version { get; }
    IReadOnlyDictionary<string, string> Properties { get; }
}
```

```csharp
// 通过构建器创建元数据
var metadata = new TransformMetadataBuilder("MyTransformer", typeof(string), typeof(int))
    .WithDescription("将字符串转换为整数")
    .WithVersion("2.0.0")
    .WithProperty("author", "WorkFlowPrj")
    .Build();

// 附加到转换器
transformer.WithMetadata(metadata);

// 便捷方式：从转换器推断类型
var metadata = TransformMetadataBuilder.FromTransformer(transformer, "描述信息");
```

---

## 9. 扩展方法速查

| 扩展方法 | 用途 | 定义位置 |
|---------|------|---------|
| `Then()` | 函数复合 `g ∘ f` | `TransformerExtensions` |
| `ToTransformer()` | Func 委托包装为 ITransformer | `TransformerExtensions` |
| `WithLifecycle()` | 注入生命周期钩子 | `TransformerExtensions` |
| `WithMetadata()` | 附加元数据 | `TransformerExtensions` |
| `Decorate()` | 应用装饰器/装饰器链 | `TransformerExtensions` |
| `GroupReduce()` | 分组归约扩展 | `TransformerExtensions` |
| `DistinctBy()` | 按键去重 | `TransformerExtensions` |
| `ReduceBy()` | 按键归约 | `TransformerExtensions` |
| `ToPipeline()` | 转换器转管线 | `PipelineExtensions` |
| `Compose()` | 多转换器组合为管线 | `PipelineExtensions` |

---

## 10. 完整示例

### 10.1 数据清洗管线

```csharp
// 构建数据清洗管线
var pipeline = new Pipeline<string, PipelineResult>("DataCleaning")
    .AddStep(new Filter<string>("RemoveEmpty", s => !string.IsNullOrEmpty(s)))
    .AddStep(new Mapper<string, string>("Trim", s => s.Trim()))
    .AddStep(new Validator<string>("MaxLength", s => s.Length <= 100, "超过最大长度"));

// 应用装饰器
var decorated = pipeline.Decorate(new DecoratorChain()
    .Add(new LogDecorator())
    .Add(new TimeoutDecorator(TimeSpan.FromSeconds(30))));

// 执行
var result = decorated.Transform(inputData);
Console.WriteLine(result); // Pipeline: 3/3 steps succeeded, 0 failed.
```

### 10.2 带缓存和重试的远程调用

```csharp
var remoteCall = new FuncTransformer<string, string>("RemoteApi", CallRemoteApi)
    .Decorate(new RetryDecorator(3, TimeSpan.FromMilliseconds(200)))
    .WithLifecycle(onBefore: msg => Log($"调用开始: {msg}"));

var cachedCall = new CacheTransformer<string, string>(remoteCall);

// 第一次调用：执行远程请求
var result1 = cachedCall.Transform("request-data");

// 第二次调用：命中缓存
var result2 = cachedCall.Transform("request-data");
```

### 10.3 分组归约去重

```csharp
var people = new List<Person> { ... };

// 按 Name 去重，保留第一个
var unique = people.GroupReduce(
    new KeySelectorMatch<Person, string>(p => p.Name),
    new KeepFirstReduce<Person>()
);

// 或使用扩展方法
var unique = people.DistinctBy(p => p.Name);

// 按 Name 分组，合并年龄
var merged = people.ReduceBy(
    p => p.Name,
    (existing, newItem) => new Person(existing.Name, existing.Age + newItem.Age)
);
```

---

## 11. 文件清单

| 文件 | 职责 |
|------|------|
| `Interfaces.cs` | 核心接口（ITransformer、IAsyncTransformer、适配器、复合） |
| `TransformContext.cs` | 转换上下文（AsyncLocal 隐式传播、作用域嵌套） |
| `Pipeline.cs` | 多步骤管线编排（步骤管理、执行追踪、失败策略） |
| `Decorator.cs` | 装饰器接口与内置实现（日志、超时、重试、装饰器链） |
| `Strategies.cs` | 匹配策略与归约策略（IMatchStrategy、IReduceStrategy） |
| `GroupReducer.cs` | 分组归约器（等价类划分、策略模式） |
| `Collections.cs` | 集合操作（聚合器、过滤器、映射器） |
| `CacheTransformer.cs` | 缓存转换器（ConcurrentDictionary + Lazy 线程安全） |
| `ConditionalTransformer.cs` | 条件转换器（if-then-else 分支） |
| `TryTransformer.cs` | 容错转换器（异常转 Result） |
| `Validator.cs` | 验证器（谓词验证） |
| `MatchKey.cs` | 复合匹配键构建器（多维键 ValueTuple） |
| `Lifecycle.cs` | 生命周期接口与包装器（OnBefore/OnAfter/OnError） |
| `Metadata.cs` | 元数据接口与构建器（类型、版本、属性） |
| `Results.cs` | 结果类型（Result<T> 类型安全的操作结果） |
| `TransformerExtensions.cs` | 扩展方法（复合、装饰、生命周期、元数据、归约） |

---

> **设计理念总结**：`BasicMethod.Transformers` 以函数式编程为基石，将"转换"抽象为统一的接口体系，通过函数复合、管线编排、装饰器模式三大机制，构建了一个灵活、可扩展、可组合的转换器生态。它与图框架、状态机共同构成 WorkFlowPrj 的三大基础抽象，为上层应用提供了强大的数据转换能力。
