# BasicMethod.Block — 执行块框架

## 概述

Block 是执行块框架，数学基础为**分段函数（Piecewise Function）**：

```
f(x) = { f₁(x)  if x ∈ D₁
       { f₂(x)  if x ∈ D₂
       { ...
       { fₙ(x)  if x ∈ Dₙ
```

每个块 = 一个分段，有独立的定义域 Dᵢ 和值域 Rᵢ。块之间通过 `BlockContext` 传递数据，每个块独立 try/catch 隔离。

## 与同级模块的关系

| 模块 | 数学基础 | 核心语义 |
|------|---------|---------|
| Transformers | 函数 `f: A → B` | 纯变换 |
| SortMent | 范畴论态射 `f: A → B` | 编排 |
| StateMachine | 自动机 `(S, Σ, δ, s₀, F)` | 状态转换 |
| **Block** | **分段函数 `f(x) = { f₁(x), ..., fₙ(x) }`** | **分块执行 + 隔离检测** |

## 核心类型

### IBlock — 执行块接口

```csharp
public interface IBlock
{
    string Name { get; }
    string Description { get; }
    Task<BlockResult> ExecuteAsync(BlockContext context, CancellationToken ct = default);
}
```

### BlockContext — 块上下文

```csharp
public class BlockContext
{
    public Dictionary<string, object?> Data { get; }      // 块间数据传递
    public List<BlockDiagnostic> Diagnostics { get; }     // 诊断记录
    public CancellationToken CancellationToken { get; set; }
    
    public T? Get<T>(string key);                         // 获取数据
    public void Set<T>(string key, T value);              // 设置数据
    public bool TryGet<T>(string key, out T? value);      // 尝试获取
}
```

### ExecutionPipeline — 执行管线

```csharp
public class ExecutionPipeline : IBlockPipeline
{
    public ExecutionPipeline AddBlock(IBlock block);      // 注册块
    public ExecutionPipeline AddBlock<T>() where T : IBlock, new();
    public ExecutionPipeline InsertBefore(string name, IBlock block);
    public ExecutionPipeline InsertAfter(string name, IBlock block);
    public ExecutionPipeline Remove(string name);
    
    public Task<PipelineResult> RunAsync(BlockContext? context = null, CancellationToken ct = default);
}
```

## 使用示例

### 基本用法

```csharp
var pipeline = new ExecutionPipeline("MyPipeline");

pipeline.AddBlock(new DelegateBlock("Step1", ctx =>
{
    Console.WriteLine("Step 1");
    return BlockResult.Success();
}));

pipeline.AddBlock(new DelegateBlock("Step2", ctx =>
{
    var data = ctx.Get<string>("key");
    Console.WriteLine($"Step 2: {data}");
    return BlockResult.Success();
}));

var result = await pipeline.RunAsync();
Console.WriteLine(result.ToDiagnosticString());
```

### 自定义块

```csharp
public class MyBlock : BlockBase
{
    public MyBlock() : base("MyBlock", "我的自定义块") { }
    
    public override async Task<BlockResult> ExecuteAsync(BlockContext ctx, CancellationToken ct)
    {
        // 业务逻辑
        ctx.Set("result", 42);
        return BlockResult.Success("执行完成");
    }
}
```

### 条件块

```csharp
public class ConditionalMyBlock : ConditionalBlockBase
{
    public ConditionalMyBlock() : base("ConditionalMyBlock") { }
    
    public override bool ShouldExecute(BlockContext ctx)
        => ctx.Get<bool>("featureEnabled");
    
    public override async Task<BlockResult> ExecuteAsync(BlockContext ctx, CancellationToken ct)
    {
        // 条件满足时执行的逻辑
        return BlockResult.Success();
    }
}
```

### 诊断块

```csharp
public class DiagnosticMyBlock : DiagnosticBlockBase
{
    public DiagnosticMyBlock() : base("DiagnosticMyBlock") { }
    
    public override bool PreDiagnostic(BlockContext ctx)
    {
        Console.WriteLine($"执行前检查: {Name}");
        return true; // 返回 false 跳过执行
    }
    
    public override async Task<BlockResult> ExecuteAsync(BlockContext ctx, CancellationToken ct)
    {
        return BlockResult.Success();
    }
    
    public override void PostDiagnostic(BlockContext ctx, BlockResult result)
    {
        Console.WriteLine($"执行后: {result.Success}");
    }
}
```

### 内置块

```csharp
// 诊断检查
pipeline.AddBlock(new DiagnosticBlock("CheckData", ctx => ctx.Get<int>("count") > 0));

// 条件分支
pipeline.AddBlock(new ConditionalBlock("Branch", 
    ctx => ctx.Get<bool>("flag"),
    new DelegateBlock("TrueBranch", ctx => BlockResult.Success()),
    new DelegateBlock("FalseBranch", ctx => BlockResult.Success())));

// 超时控制
pipeline.AddBlock(new TimeoutBlock("TimedOp", innerBlock, TimeSpan.FromSeconds(5)));

// 日志
pipeline.AddBlock(new LogBlock("LogStart", "开始执行"));

// 子序列
pipeline.AddBlock(new SequenceBlock("SubPipeline", subPipeline));

// 延迟
pipeline.AddBlock(new DelayBlock("Wait", TimeSpan.FromSeconds(1)));
```

### 诊断输出

```csharp
var result = await pipeline.RunAsync();
Console.WriteLine(result.ToDiagnosticString());

// 输出示例：
// ╔═══ 管线执行报告 ═══
// ║ 整体状态: ✓ 成功
// ║ 总耗时: 1234.5ms
// ║ 成功: 5 | 失败: 0 | 跳过: 1
// ╠═══ 块诊断详情 ═══
// ║ [✓] InitCommandFramework
// ║   状态: Success | 耗时: 10.2ms
// ║ [✓] CreateMainForm
// ║   状态: Success | 耗时: 50.1ms
// ║ [⊘] OptionalFeature
// ║   状态: Skipped | 耗时: 0.0ms
// ║   消息: 条件不满足，跳过执行
// ╚═══ 报告结束 ═══
```

## 文件清单

| 文件 | 职责 |
|------|------|
| `Interfaces.cs` | 核心接口（IBlock、IBlockPipeline、IBlockDiagnosticProvider、IBlockCondition、IBlockRetry） |
| `BlockContext.cs` | 块上下文与结果类型（BlockContext、BlockResult、BlockDiagnostic、PipelineResult、BlockStatus） |
| `BlockBase.cs` | 抽象基类（BlockBase、DelegateBlock、ConditionalBlockBase、RetryBlockBase、DiagnosticBlockBase） |
| `ExecutionPipeline.cs` | 执行管线实现 |
| `BuiltInBlocks.cs` | 内置块（DiagnosticBlock、ConditionalBlock、TimeoutBlock、LogBlock、SequenceBlock、DelayBlock） |
| `BlockExtensions.cs` | 扩展方法 |
| `BasicMethod.Block功能介绍.md` | 功能介绍文档 |
