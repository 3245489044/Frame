# BasicMethod.SortMent — 编排框架功能介绍

> **模块定位**：与图（`ICoreGraph`）、状态机（`IStateMachine`）、转换器（`ITransformer`）平级的第四大基础抽象  
> **数学基础**：范畴论中的态射（Morphism）概念  
> **设计哲学**：万物皆函数，一切皆可编排

---

## 1. 概述

`BasicMethod.SortMent` 是 WorkFlowPrj 四层架构中 **BasicMethod（基础工具库）** 层的重要组成部分。它提供了一个以**范畴论态射**为核心理念的通用编排框架，将"编排"这一基本操作抽象为统一的接口体系。

### 1.1 设计目标

- **统一抽象**：将一切"从 A 到 B 的编排操作"统一为 `ISortMent<TSource, TTarget>` 接口
- **态射复合**：支持编排态射的组合（`g ∘ f`），构建复杂编排链路
- **管线编排**：多步骤顺序执行，支持排序键（偏序 ≤）和全序（Order + SubOrder）
- **分类编排**：通过等价关系 ∼ 将输入划分到不同类别，每个类别对应一个处理分支
- **分组编排**：按等价关系 ∼ 将集合划分为等价类，每组应用相同的编排逻辑
- **注册表模式**：按键注册和查找编排态射，支持策略模式

### 1.2 与 Transformers 的关系

| 维度 | Transformers | SortMent |
|------|-------------|----------|
| **数学基础** | 函数 `f: A → B` | 范畴论态射 `f: A → B` |
| **核心语义** | 纯函数变换 | 编排态射（带编排语义） |
| **排序** | 无排序语义 | 偏序 ≤（Order）和全序（Order + SubOrder） |
| **分类** | 条件分支（if-then-else） | 等价关系 ∼ 的商集构造 |
| **分组** | GroupReducer（归约） | IGroupMent（等价类划分） |
| **管线** | Pipeline（步骤管线） | SortMentPipeline（编排管线） |
| **注册表** | 无 | ISortMentRegistry（策略注册表） |
| **层次** | 基础转换层 | Pipeline 的上层泛型，比 Pipeline 更基础通用 |

**核心区别**：
- `ITransformer` 是**纯函数变换** `f: A → B`，无编排语义
- `ISortMent` 是**编排态射** `f: (A, ≤, ∼) → (B, ≤, ∼)`，包含分类、排序、分组等编排语义

### 1.3 与 Pipeline 的关系

- **SortMent** 是 Pipeline 的上层泛型，比 Pipeline 更基础通用
- **Pipeline** 建立在 SortMent 之上，SortMent 提供最基础的编排抽象
- Pipeline 模块的 `IStagePipeline` 建立在此接口之上

### 1.4 适用场景

- 多步骤业务编排（工作流引擎）
- 管线化数据处理（ETL 管线）
- 策略模式（按键选择编排逻辑）
- 分类处理（按类型/状态分发到不同处理分支）
- 分组处理（按等价关系划分集合）
- 需要排序保证的编排链路

---

## 2. 核心接口体系

### 2.1 具名对象（INamed）

```csharp
public interface INamed
{
    string Name { get; }
    string Description { get; }
}
```

`INamed` 是 Name/Description 属性的统一抽象，用于泛型约束，避免 SortMentCollection 使用反射查找 Name。

### 2.2 编排态射（ISortMent）

```csharp
public interface ISortMent<in TSource, out TTarget> : INamed
{
    TTarget Morph(TSource source);
}
```

最核心的抽象。任何实现了此接口的类型都是一个"编排态射"，接受 `TSource` 返回 `TTarget`。

### 2.3 异步编排态射（IAsyncSortMent）

```csharp
public interface IAsyncSortMent<in TSource, TTarget> : INamed
{
    Task<TTarget> MorphAsync(TSource source, CancellationToken cancellationToken = default);
}
```

支持异步操作的编排态射，适用于 I/O 密集型或远程调用场景。

### 2.4 分类态射（IClassifyMent）

```csharp
public interface IClassifyMent<in TSource, TCategory, out TTarget> : ISortMent<TSource, TTarget>
{
    TCategory Classify(TSource source);
}
```

数学基础：商集构造 `S/∼ → T`。将输入划分到不同类别，每个类别对应一个处理分支。

### 2.5 可排序编排态射（ISortableMent）

```csharp
public interface ISortableMent<in TSource, out TTarget> : ISortMent<TSource, TTarget>
{
    int Order { get; }
}
```

数学基础：偏序集 `(S, ≤)` 上的保序函数。每个编排态射都有一个排序键 Order，用于确定执行顺序。

### 2.6 分组态射（IGroupMent）

```csharp
public interface IGroupMent<TSource, TKey, TTarget> : ISortMent<IEnumerable<TSource>, IEnumerable<TTarget>>
{
    Func<TSource, TKey> KeySelector { get; }
}
```

数学基础：等价关系 ∼ 将集合划分为等价类。将输入按指定键分组，每组应用相同的编排逻辑。

### 2.7 有序编排态射（IOrderedMent）

```csharp
public interface IOrderedMent<in TSource, out TTarget> : ISortableMent<TSource, TTarget>
{
    int SubOrder { get; }
}
```

数学基础：全序集 `(S, ≤)` 上的保序函数。在偏序基础上增加全序保证，确保所有态射有确定的执行顺序。

### 2.8 编排态射集合（ISortMentCollection）

```csharp
public interface ISortMentCollection<TSortMent> where TSortMent : INamed
{
    IReadOnlyList<TSortMent> Items { get; }
    int Count { get; }
    ISortMentCollection<TSortMent> Register(TSortMent item);
    ISortMentCollection<TSortMent> RegisterRange(IEnumerable<TSortMent> items);
    ISortMentCollection<TSortMent> InsertBefore(string name, TSortMent item);
    ISortMentCollection<TSortMent> InsertAfter(string name, TSortMent item);
    ISortMentCollection<TSortMent> Remove(string name);
    void Clear();
}
```

管理多个编排态射的注册、排序、动态调整。`TSortMent` 约束为 `INamed`，确保可按名称查找。

### 2.9 编排管线（ISortMentPipeline）

```csharp
public interface ISortMentPipeline<TContext> : ISortMentCollection<ISortableMent<TContext, TContext>>
{
    string Name { get; }
    string Description { get; }
    TContext Execute(TContext context);
    Task<TContext> ExecuteAsync(TContext context, CancellationToken cancellationToken = default);
}
```

多个编排态射按 Order 排序后依次执行，形成编排管线。Pipeline 模块的 `IStagePipeline` 建立在此接口之上。

### 2.10 编排态射注册表（ISortMentRegistry）

```csharp
public interface ISortMentRegistry<TKey, TSource, TTarget>
    where TKey : notnull
{
    void Register(TKey key, ISortMent<TSource, TTarget> sortMent);
    void RegisterOrUpdate(TKey key, ISortMent<TSource, TTarget> sortMent);
    ISortMent<TSource, TTarget> Get(TKey key);
    bool TryGet(TKey key, out ISortMent<TSource, TTarget>? sortMent);
    bool ContainsKey(TKey key);
    bool Remove(TKey key);
    void Clear();
    int Count { get; }
    IEnumerable<TKey> Keys { get; }
    TTarget Execute(TKey key, TSource source);
}
```

按键注册和查找编排态射，支持策略模式。

---

## 3. 态射复合（Morphism Composition）

### 3.1 Then() — 单步复合

通过扩展方法 `Then()` 实现两个编排态射的复合：

```csharp
// f: A → B, g: B → C => g ∘ f: A → C
ISortMent<string, int> composed = parser.Then(validator);
```

数学语义：`g ∘ f`，先执行 `f`，再将结果传给 `g`。

### 3.2 内部实现

`ComposedSortMent<TSource, TMiddle, TTarget>` 是 `Then()` 的内部实现：

```csharp
internal class ComposedSortMent<TSource, TMiddle, TTarget> : ISortMent<TSource, TTarget>
{
    // Name 格式为 "next ∘ first"，匹配数学 notation g ∘ f
    public string Name => $"{_next.Name} ∘ {_first.Name}";
    
    public TTarget Morph(TSource source)
    {
        var middle = _first.Morph(source);
        return _next.Morph(middle);
    }
}
```

### 3.3 Func → SortMent

通过 `ToSortMent()` 扩展方法，将任意 `Func<TSource, TTarget>` 委托包装为 `ISortMent`：

```csharp
var sortMent = new Func<string, int>(s => s.Length).ToSortMent("LengthCounter");
```

---

## 4. 管线编排（Pipeline）

`SortMentPipeline<TContext>` 继承 `SortMentCollection<ISortableMent<TContext, TContext>>`，复用集合操作，并将多个 `ISortableMent` 按 Order 排序后依次执行。

### 4.1 基本用法

```csharp
var pipeline = new SortMentPipeline<MyContext>("MyPipeline")
    .Register(new StepA())     // Order = 0
    .Register(new StepB())     // Order = 1
    .Register(new StepC());    // Order = 2

var result = pipeline.Execute(context);
// 执行顺序：StepA → StepB → StepC
```

### 4.2 排序保证

```csharp
// 通过 Order 控制执行顺序
pipeline.Register(new StepA { Order = 10 });  // 最后执行
pipeline.Register(new StepB { Order = 0 });   // 最先执行
pipeline.Register(new StepC { Order = 5 });   // 中间执行

// 执行顺序：StepB(0) → StepC(5) → StepA(10)
```

### 4.3 动态调整

```csharp
pipeline.Register(step1);                    // 末尾添加
pipeline.InsertBefore("StepB", step0);       // 在 StepB 之前插入
pipeline.InsertAfter("StepB", step2);        // 在 StepB 之后插入
pipeline.Remove("StepA");                    // 移除
pipeline.Clear();                            // 清空
```

### 4.4 便捷创建

```csharp
// 使用扩展方法创建
var pipeline = SortMentExtensions.CreatePipeline<MyContext>("MyPipeline");

// 从多个编排态射组合
var pipeline = SortMentExtensions.Compose("MyPipeline", step1, step2, step3);
```

---

## 5. 注册表模式（Registry）

`SortMentRegistry<TKey, TSource, TTarget>` 按键注册和查找编排态射，支持策略模式。

### 5.1 基本用法

```csharp
var registry = new SortMentRegistry<string, Order, ProcessResult>();

registry.Register("normal", new NormalOrderProcessor());
registry.Register("urgent", new UrgentOrderProcessor());
registry.Register("refund", new RefundOrderProcessor());

// 按策略执行
var result = registry.Execute(order.Type, order);
```

### 5.2 线程安全

`SortMentRegistry` 使用 `ConcurrentDictionary` 保证线程安全：

```csharp
registry.RegisterOrUpdate("key", newProcessor);  // 原子操作
registry.TryGet("key", out var processor);        // 安全读取
registry.Remove("key");                           // 安全移除
```

---

## 6. 抽象基类

### 6.1 SortMentBase

编排态射抽象基类，子类只需重写 `Morph` 方法：

```csharp
public class MySortMent : SortMentBase<string, int>
{
    public MySortMent() : base("MySortMent", "将字符串转换为整数") { }
    
    public override int Morph(string source) => int.Parse(source);
}
```

### 6.2 AsyncSortMentBase

异步编排态射抽象基类，子类只需重写 `MorphAsync` 方法：

```csharp
public class MyAsyncSortMent : AsyncSortMentBase<string, int>
{
    public MyAsyncSortMent() : base("MyAsyncSortMent", "异步转换") { }
    
    public override async Task<int> MorphAsync(string source, CancellationToken ct)
    {
        return await Task.Run(() => int.Parse(source), ct);
    }
}
```

### 6.3 SortableMentBase

可排序编排态射抽象基类，带 Order 排序键：

```csharp
public class StepA : SortableMentBase<MyContext, MyContext>
{
    public StepA() : base("StepA", order: 0, "第一步处理") { }
    
    public override MyContext Morph(MyContext source)
    {
        source.Data = "processed";
        return source;
    }
}
```

### 6.4 OrderedMentBase

有序编排态射抽象基类，带 Order + SubOrder 全序：

```csharp
public class DetailedStep : OrderedMentBase<MyContext, MyContext>
{
    public DetailedStep() : base("DetailedStep", order: 5, subOrder: 1, "详细处理步骤") { }
}
```

### 6.5 DelegateSortMent

将 Func 委托包装为 ISortMent：

```csharp
var sortMent = new DelegateSortMent<string, int>(
    "Parse",
    s => int.Parse(s),
    "将字符串解析为整数"
);
```

### 6.6 DelegateAsyncSortMent

将异步 Func 委托包装为 IAsyncSortMent：

```csharp
var asyncSortMent = new DelegateAsyncSortMent<string, int>(
    "AsyncParse",
    async (s, ct) => await Task.Run(() => int.Parse(s), ct)
);
```

---

## 7. 扩展方法速查

| 扩展方法 | 用途 | 定义位置 |
|---------|------|---------|
| `ToSortMent()` | Func 委托包装为 ISortMent | `SortMentExtensions` |
| `ToSortableMent()` | Func 委托包装为 ISortableMent（带排序键） | `SortMentExtensions` |
| `WithOrder()` | ISortMent 包装为 ISortableMent | `SortMentExtensions` |
| `Then()` | 态射复合 `g ∘ f` | `SortMentExtensions` |
| `GroupMorph()` | 集合分组编排 | `SortMentExtensions` |
| `CreatePipeline()` | 创建编排管线 | `SortMentExtensions` |
| `Compose()` | 多编排态射组合为管线 | `SortMentExtensions` |

---

## 8. 完整示例

### 8.1 订单处理管线

```csharp
// 定义处理步骤
public class ValidateOrder : SortableMentBase<OrderContext, OrderContext>
{
    public ValidateOrder() : base("ValidateOrder", order: 0, "验证订单") { }
    public override OrderContext Morph(OrderContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.Order.Id))
            throw new ArgumentException("订单ID不能为空");
        return ctx;
    }
}

public class CalculatePrice : SortableMentBase<OrderContext, OrderContext>
{
    public CalculatePrice() : base("CalculatePrice", order: 1, "计算价格") { }
    public override OrderContext Morph(OrderContext ctx)
    {
        ctx.TotalPrice = ctx.Order.Items.Sum(i => i.Price * i.Quantity);
        return ctx;
    }
}

public class ApplyDiscount : SortableMentBase<OrderContext, OrderContext>
{
    public ApplyDiscount() : base("ApplyDiscount", order: 2, "应用折扣") { }
    public override OrderContext Morph(OrderContext ctx)
    {
        if (ctx.TotalPrice > 1000)
            ctx.TotalPrice *= 0.9m; // 9折
        return ctx;
    }
}

// 构建管线
var pipeline = new SortMentPipeline<OrderContext>("OrderProcessing")
    .Register(new ValidateOrder())
    .Register(new CalculatePrice())
    .Register(new ApplyDiscount());

// 执行
var result = pipeline.Execute(new OrderContext { Order = order });
```

### 8.2 策略模式

```csharp
var registry = new SortMentRegistry<string, PaymentRequest, PaymentResult>();

registry.Register("alipay", new AlipayProcessor());
registry.Register("wechat", new WechatPayProcessor());
registry.Register("card", new CardPaymentProcessor());

// 按支付方式选择处理策略
var result = registry.Execute(request.PaymentMethod, request);
```

### 8.3 分类处理

```csharp
public class OrderClassifier : IClassifyMent<Order, OrderType, ProcessedOrder>
{
    public string Name => "OrderClassifier";
    public string Description => "按订单类型分类处理";
    
    public OrderType Classify(Order order) => order.Type;
    
    public ProcessedOrder Morph(Order order)
    {
        var type = Classify(order);
        return type switch
        {
            OrderType.Normal => ProcessNormal(order),
            OrderType.Urgent => ProcessUrgent(order),
            OrderType.Refund => ProcessRefund(order),
            _ => throw new NotSupportedException($"不支持的订单类型: {type}")
        };
    }
}
```

### 8.4 态射复合

```csharp
// 定义基础编排态射
ISortMent<string, string> trim = new DelegateSortMent<string, string>(
    "Trim", s => s.Trim());
ISortMent<string, int> parse = new DelegateSortMent<string, int>(
    "Parse", s => int.Parse(s));
ISortMent<int, bool> validate = new DelegateSortMent<int, bool>(
    "Validate", n => n > 0 && n < 100);

// 复合：Parse ∘ Trim, then Validate ∘ (Parse ∘ Trim)
var pipeline = trim.Then(parse).Then(validate);
// Name: "Validate ∘ Parse ∘ Trim"

var result = pipeline.Morph(" 42 "); // true
```

---

## 9. 文件清单

| 文件 | 职责 |
|------|------|
| `Interfaces.cs` | 核心接口（INamed、ISortMent、IAsyncSortMent、IClassifyMent、ISortableMent、IGroupMent、IOrderedMent、ISortMentCollection、ISortMentPipeline、ISortMentRegistry） |
| `SortMentBase.cs` | 抽象基类（SortMentBase、AsyncSortMentBase、SortableMentBase、OrderedMentBase、DelegateSortMent、DelegateSortableMent、DelegateAsyncSortMent） |
| `SortMentCollection.cs` | 编排态射集合与管线实现（SortMentCollection、SortMentPipeline） |
| `SortMentRegistry.cs` | 编排态射注册表实现（SortMentRegistry） |
| `SortMentExtensions.cs` | 扩展方法（Func 转换、态射复合、分组编排、管线创建） |

---

> **设计理念总结**：`BasicMethod.SortMent` 以范畴论态射为基石，将"编排"抽象为统一的接口体系，通过态射复合、管线编排、注册表模式三大机制，构建了一个灵活、可扩展、可组合的编排生态。它与图框架、状态机、转换器共同构成 WorkFlowPrj 的四大基础抽象，为上层应用提供了强大的编排能力。
