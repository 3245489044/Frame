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
    BlockPolicy Policy => BlockPolicy.Default;   // 条件/重试/超时/收敛/兜底
    Task<BlockResult> ExecuteAsync(BlockContext context, CancellationToken ct = default);
}
```

### BlockPolicy — 唯一策略面

```csharp
new BlockPolicy
{
    IsFatal = true,                       // 失败是否短路
    CanExecute = ctx => ...,              // 条件（异常 → Failed 诊断）
    MaxRetries = 2,                       // 总尝试 = MaxRetries + 1
    RetryDelay = TimeSpan.FromMilliseconds(100),
    RetryBackoff = BlockRetryBackoff.Exponential,
    Timeout = TimeSpan.FromSeconds(5),    // 超时 → Failed(fatal)
    Guarded = true,                       // 失败收敛（原 Circuit Guard）
    FallbackAsync = (ctx, last, ct) => ... // 重试耗尽后的兜底
};
```

### BlockContext — 唯一阶段账本

```csharp
public class BlockContext
{
    public IDictionary<string, object?> Data { get; }              // 块间数据
    public ConcurrentDictionary<string, BlockDiagnostic> StageResults { get; } // 唯一账本
    public IReadOnlyList<BlockDiagnostic> Diagnostics { get; }
    public CancellationToken CancellationToken { get; set; }

    public T? Get<T>(string key);
    public void Set<T>(string key, T value);
    public bool IsSuccess(string block);
    public BlockContext CreateChild();                             // 批处理：独立账本
    public void Cancel(string? reason = null);                     // 幂等
}
```

### BlockPipeline — 装配 + 执行

```csharp
var pipeline = new BlockPipeline("MyPipeline")
    .Add(new DelegateBlock("Step1", _ => BlockResult.Ok()))
    .Add(new DelegateBlock("Step2", _ => BlockResult.Ok()));

BlockRunResult result = await pipeline.RunAsync();
```

### ExecutionPipeline — 旧签名兼容壳

```csharp
[Obsolete] public class ExecutionPipeline : IBlockPipeline
{
    public bool FailFast { get; set; }    // false = 非致命失败继续（旧默认）
    Task<PipelineResult> RunAsync(...);   // 内部委托 BlockRunner
}
```


## 使用示例

### 基本用法

```csharp
var pipeline = new ExecutionPipeline("MyPipeline");

pipeline.AddBlock(new DelegateBlock("Step1", ctx =>
{
    Console.WriteLine("Step 1");
    return BlockResult.Ok();
}));

pipeline.AddBlock(new DelegateBlock("Step2", ctx =>
{
    var data = ctx.Get<string>("key");
    Console.WriteLine($"Step 2: {data}");
    return BlockResult.Ok();
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
        return BlockResult.Ok("执行完成");
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
        return BlockResult.Ok();
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
        return BlockResult.Ok();
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
    new DelegateBlock("TrueBranch", ctx => BlockResult.Ok()),
    new DelegateBlock("FalseBranch", ctx => BlockResult.Ok())));

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
| `Interfaces.cs` | 核心接口（IBlock、IBlockPipeline、IBlockDiagnosticProvider、IBlockCondition、IBlockRetry）；`IBlock.Policy` 默认实现 |
| `BlockOutcome.cs` | 四态结论（Success/Skipped/Failed/Aborted）与整条管线结论 |
| `BlockResult.cs` | 单块结果（含 Attempts/Converged） |
| `BlockDiagnostic.cs` | 阶段账本条目（兼容旧构造 + 新字段） |
| `BlockPolicy.cs` | 唯一策略面（条件/致命性/重试/退避/超时/收敛/兜底） |
| `BlockContext.cs` | 唯一阶段账本（Data/StageResults/Diagnostics/Cancel/CreateChild） |
| `BlockRunner.cs` | **全仓唯一执行循环**（条件/重试/超时/兜底/记账） |
| `BlockPipeline.cs` | 新内核管线（装配 fail-fast + RunAsync → BlockRunResult） |
| `BlockRunResult.cs` | 唯一结果类型 + `PipelineResult` 兼容壳 |
| `ExecutionPipeline.cs` | 旧签名兼容壳（内部委托 BlockRunner，保留 FailFast 语义） |
| `BlockBase.cs` | 抽象基类（BlockBase、DelegateBlock、ConditionalBlockBase、RetryBlockBase、DiagnosticBlockBase） |
| `BuiltInBlocks.cs` | 内置块（DiagnosticBlock、ConditionalBlock、TimeoutBlock、LogBlock、SequenceBlock、DelayBlock） |
| `BlockExtensions.cs` | 扩展方法（旧/新结果诊断输出） |
| `BasicMethod.Block功能介绍.md` | 功能介绍文档 |
