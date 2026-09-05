# SkeletonFlow 框架使用文档

## 概述

SkeletonFlow 是一个基于**骨架模式（Skeleton Pattern）**的高性能计算框架，旨在消除重复的循环逻辑，统一并行计算策略，并提供可复用的计算流程模板。

框架核心思想：**将不变的计算流程抽象为骨架（Skeleton），将可变的计算逻辑通过委托（Delegate）注入**。

### 核心优势

- **消除重复循环**：所有并行循环统一使用 `LoopEngine` 骨架
- **统一并行策略**：自动选择并行/顺序执行，支持进度报告和取消
- **流程复用**：Attention、FeedForward、Normalization 等固定流程封装为骨架
- **关注点分离**：骨架层负责流程控制，业务层只关注计算逻辑

---

## 架构层次

```
┌─────────────────────────────────────────────────────────────┐
│                    业务层 (Business Layer)                    │
│  Tensor, TransformerModel, MultiHeadAttentionLayer, ...     │
│  关注：API 设计、数据组织、业务逻辑组合                      │
├─────────────────────────────────────────────────────────────┤
│                    骨架层 (Skeleton Layer)                    │
│  AttentionSkeleton, FeedForwardSkeleton,                    │
│  NormalizationSkeleton, ResidualSkeleton, EmbeddingSkeleton │
│  关注：固定计算流程的定义和复用                              │
├─────────────────────────────────────────────────────────────┤
│                    循环引擎层 (Loop Engine Layer)             │
│  LoopEngine, LoopConfig, LoopShape                          │
│  关注：高性能并行循环的执行、进度报告、取消支持              │
├─────────────────────────────────────────────────────────────┤
│                    数学工具层 (Math Utility Layer)            │
│  SoftmaxCalculator, ReduceOperations, ActivationFunctions,  │
│  BroadcastHelper, DimensionHelper                           │
│  关注：纯数学计算，无循环依赖                                │
└─────────────────────────────────────────────────────────────┘
```

---

## 一、LoopEngine — 通用循环骨架

`LoopEngine` 是所有骨架的基石，提供高性能的双层循环执行能力。

### 1.1 核心类型

```csharp
// 循环形状定义
public record LoopShape(int OuterCount, int InnerCount);

// 循环执行配置
public record LoopConfig
{
    public bool EnableParallel { get; init; } = true;
    public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;
    public bool EnableProgressReporting { get; init; } = true;
    public int ParallelThreshold { get; init; } = 100;  // 超过此行数启用并行
    public int ProgressReportInterval { get; init; } = 100;

    public static LoopConfig Default => new();
    public static LoopConfig Sequential => new() { EnableParallel = false };
}
```

### 1.2 四种执行模式

| 方法 | 返回值 | 同步/异步 | 适用场景 |
|------|--------|-----------|---------|
| `ExecuteAsync` | `TOutput[][]` | 异步 | 元素变换、转置、降维 |
| `ExecuteInPlaceAsync` | 无（就地修改） | 异步 | 写入 Matrix/Array 的场景 |
| `ExecuteSync` | `TOutput[][]` | 同步 | 无 async 开销的同步场景 |
| `ExecuteInPlaceSync` | 无（就地修改） | 同步 | 同步就地修改 |
| `ExecuteBlockedAsync` | 无（就地修改） | 异步 | 大矩阵分块并行计算 |

### 1.3 基本用法示例

```csharp
var engine = new LoopEngine();

// 示例 1: 元素变换（有返回值）
var result = await engine.ExecuteAsync(
    input,                          // 输入数据
    (x, i, j) => x.U[i][j] * x.S[j], // 变换函数
    new LoopShape(rows, cols));     // 循环形状

// 示例 2: 就地修改（无返回值）
engine.ExecuteInPlaceSync(
    (Src: source, Dst: result, Dim: dim),
    (ctx, b, s) => {
        for (int d = 0; d < ctx.Dim; d++)
            ctx.Dst[b, s, d] = ctx.Src[b, s, d] * 2;
    },
    new LoopShape(batchSize, seqLen));

// 示例 3: 分块并行计算（大矩阵）
await engine.ExecuteBlockedAsync(
    (Input: data, Matrix: matrix),
    (ctx, i, j) => ctx.Matrix[i, j] = ctx.Input[i, j],
    new LoopShape(rows, cols),
    blockSize: 256,                 // 分块大小
    progress: progress,             // 进度报告
    cancellationToken: cts.Token);  // 取消令牌
```

### 1.4 自动并行策略

`LoopEngine` 根据数据量自动选择执行策略：

```csharp
// 当 OuterCount > ParallelThreshold(100) 时自动启用 Parallel.For
// 可通过 LoopConfig 控制
var config = new LoopConfig
{
    EnableParallel = true,
    MaxDegreeOfParallelism = 4,     // 限制并行度
    ParallelThreshold = 50          // 调整并行阈值
};

engine.ExecuteInPlaceSync(input, action, shape, config);
```

---

## 二、计算骨架（Computation Skeletons）

计算骨架封装了深度学习模型中**不变的计算流程**，是 SkeletonFlow 的核心复用单元。

### 2.1 AttentionSkeleton

封装 Scaled Dot-Product Attention 的完整计算流程：

```
Step 1: QK^T 矩阵乘法
Step 2: Scale (除以 sqrt(d_k))
Step 3: Mask (可选，Decoder 用)
Step 4: Softmax
Step 5: × V 矩阵乘法
```

```csharp
// 同步计算
var output = AttentionSkeleton.Compute(Q, K, V, mask, progress);

// 异步计算（支持取消）
var cts = new CancellationTokenSource();
var outputAsync = await AttentionSkeleton.ComputeAsync(
    Q, K, V, mask, progress, cts.Token);
```

**参数说明：**
- `Q, K, V`：`Tensor` 类型，形状 `[Batch, SeqLen, Dim]`
- `mask`：可选掩码张量，用于因果掩码或填充掩码
- `progress`：`IProgress<ComputationProgress>` 进度报告

### 2.2 FeedForwardSkeleton

封装前馈网络（FFN）的完整计算流程：

```
Step 1: Linear (xW1^T + b1)
Step 2: Activation (ReLU/GELU/Swish)
Step 3: Linear (xW2^T + b2)
Step 4: Dropout (可选)
```

```csharp
var output = FeedForwardSkeleton.Compute(
    input,          // [Batch, SeqLen, Dim]
    weight1, bias1, // 第一层 [Dim, HiddenDim] + [HiddenDim]
    weight2, bias2, // 第二层 [HiddenDim, Dim] + [Dim]
    ActivationType.GELU,
    dropoutRate: 0.1,
    progress: progress);

// 异步版本
var outputAsync = await FeedForwardSkeleton.ComputeAsync(
    input, weight1, bias1, weight2, bias2,
    ActivationType.GELU, cancellationToken: cts.Token);
```

### 2.3 NormalizationSkeleton

封装 Layer Normalization 和 RMS Normalization：

```
LayerNorm:
  Step 1: 计算均值 mean = sum(x) / dim
  Step 2: 计算方差 var = sum((x - mean)^2) / dim
  Step 3: 归一化 y = (x - mean) / sqrt(var + eps) * gamma + beta

RMSNorm:
  Step 1: 计算 RMS = sqrt(mean(x^2) + eps)
  Step 2: 归一化 y = (x / RMS) * gamma
```

```csharp
// Layer Normalization
var output = NormalizationSkeleton.LayerNorm(
    input, gamma, beta, eps: 1e-5);

// RMS Normalization
var rmsOutput = NormalizationSkeleton.RMSNorm(
    input, gamma, eps: 1e-5);

// 异步版本
var outputAsync = await NormalizationSkeleton.LayerNormAsync(
    input, gamma, beta, cancellationToken: cts.Token);
```

### 2.4 ResidualSkeleton

封装残差连接，支持 Pre-LN 和 Post-LN 两种模式：

```
Pre-LN (GPT/BERT 现代实现):
  x + Sublayer(LayerNorm(x))

Post-LN (原始 Transformer):
  LayerNorm(x + Sublayer(x))
```

```csharp
var output = ResidualSkeleton.Apply(
    input,
    sublayer: (x) => attentionLayer.Forward(x),  // 子层函数
    gamma, beta,
    preNorm: true,   // 使用 Pre-LN
    eps: 1e-5);

// 异步版本
var outputAsync = await ResidualSkeleton.ApplyAsync(
    input, sublayer, gamma, beta,
    cancellationToken: cts.Token);
```

### 2.5 EmbeddingSkeleton

封装 Token Embedding + Position Embedding 查找：

```csharp
var result = EmbeddingSkeleton.Lookup(
    tokenIds,           // int[,] 形状 [Batch, SeqLen]
    tokenEmbedding,     // Tensor [1, VocabSize, ModelDim]
    positionEmbedding,  // Tensor [1, MaxSeqLen, ModelDim]
    vocabSize);         // 词表大小
```

---

## 三、骨架的通用设计模式

所有骨架遵循统一的设计模式，包含以下要素：

### 3.1 骨架结构模板

```csharp
public static class XxxSkeleton
{
    // 1. 复用 LoopEngine（可选，部分骨架可能不需要）
    private static readonly LoopEngine _loopEngine = new();

    // 2. 同步计算入口
    public static Tensor Compute(
        Tensor input,
        /* 其他参数 */,
        IProgress<ComputationProgress> progress = null)
    {
        // 2.1 参数验证
        if (input == null) throw new ArgumentNullException(nameof(input));

        // 2.2 分步计算，每步报告进度
        progress?.Report(new ComputationProgress(0, "Step 1/3 - ..."));

        // Step 1: ...
        var result = Step1(input);

        progress?.Report(new ComputationProgress(33, "Step 2/3 - ..."));

        // Step 2: ...
        result = Step2(result);

        progress?.Report(new ComputationProgress(66, "Step 3/3 - ..."));

        // Step 3: ...
        result = Step3(result);

        progress?.Report(new ComputationProgress(100, "Complete"));

        return result;
    }

    // 3. 异步计算入口（支持取消）
    public static Task<Tensor> ComputeAsync(
        Tensor input,
        /* 其他参数 */,
        IProgress<ComputationProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Compute(input, /* ... */, progress);
        }, cancellationToken);
    }
}
```

### 3.2 使用 LoopEngine 的骨架内部实现

对于需要高性能并行循环的骨架，内部使用 `LoopEngine.ExecuteInPlaceSync`：

```csharp
public static Tensor LayerNorm(Tensor input, double[] gamma, double[] beta, double eps)
{
    int dim = input.Dimension;
    var result = new Tensor(input.Shape);

    _loopEngine.ExecuteInPlaceSync(
        // 上下文：将所需数据打包为元组
        (Src: input, Dst: result, Gamma: gamma, Beta: beta, Dim: dim, Eps: eps),
        // 操作函数：对每个 (batch, seq) 位置执行
        (ctx, b, s) => {
            // 计算均值
            double mean = 0;
            for (int d = 0; d < ctx.Dim; d++)
                mean += ctx.Src[b, s, d];
            mean /= ctx.Dim;

            // 计算方差
            double variance = 0;
            for (int d = 0; d < ctx.Dim; d++)
            {
                double diff = ctx.Src[b, s, d] - mean;
                variance += diff * diff;
            }
            variance /= ctx.Dim;

            // 归一化
            double invStd = 1.0 / Math.Sqrt(variance + ctx.Eps);
            for (int d = 0; d < ctx.Dim; d++)
            {
                double normalized = (ctx.Src[b, s, d] - mean) * invStd;
                ctx.Dst[b, s, d] = normalized * ctx.Gamma[d] + ctx.Beta[d];
            }
        },
        new LoopShape(input.BatchSize, input.SeqLength));

    return result;
}
```

**关键点：**
- 上下文（Context）使用 C# 元组语法 `(Src: input, Dst: result, ...)` 打包
- 操作函数接收 `(context, outerIdx, innerIdx)` 参数
- `LoopShape(outerCount, innerCount)` 定义循环范围
- 框架自动决定并行/顺序执行

---

## 四、进度报告与取消

### 4.1 ComputationProgress

```csharp
public class ComputationProgress
{
    public double Percentage { get; }      // 进度百分比 (0-100)
    public string Status { get; }          // 当前状态描述
    public int CurrentChunk { get; }       // 当前处理的数据块索引
    public int TotalChunks { get; }        // 总数据块数
    public int ProcessedRows { get; }      // 已处理行数
    public int TotalRows { get; }          // 总行数
}
```

### 4.2 使用方式

```csharp
// 创建进度报告器
var progress = new Progress<ComputationProgress>(p =>
{
    Console.WriteLine($"[{p.Percentage:F1}%] {p.Status}");
});

// 在骨架中报告进度
progress?.Report(new ComputationProgress(0, "Starting..."));
// ... 计算步骤 ...
progress?.Report(new ComputationProgress(50, "Halfway..."));
// ... 计算步骤 ...
progress?.Report(new ComputationProgress(100, "Complete"));

// 支持取消
var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromSeconds(30)); // 30秒超时

var result = await skeleton.ComputeAsync(input, progress, cts.Token);
```

---

## 五、组合骨架构建复杂计算

骨架可以灵活组合，构建更复杂的计算流程。

### 5.1 Transformer Block 示例

```csharp
public class TransformerBlock
{
    public Tensor Forward(Tensor input)
    {
        // 1. Attention + 残差连接
        var attnOutput = ResidualSkeleton.Apply(
            input,
            sublayer: (x) => _attention.Forward(x),
            _gammaAttn, _betaAttn,
            preNorm: true);

        // 2. FFN + 残差连接
        var output = ResidualSkeleton.Apply(
            attnOutput,
            sublayer: (x) => _ffn.Forward(x),
            _gammaFfn, _betaFfn,
            preNorm: true);

        return output;
    }
}
```

### 5.2 完整 Transformer 模型示例

```csharp
public class TransformerModel
{
    public Tensor Forward(int[,] tokenIds)
    {
        // 1. Embedding 查找
        var x = EmbeddingSkeleton.Lookup(
            tokenIds, _tokenEmbedding, _positionEmbedding, _vocabSize);

        // 2. 堆叠 Transformer Block
        foreach (var block in _blocks)
            x = block.Forward(x);

        // 3. 输出投影
        return x.MatMul(_outputProjection);
    }
}
```

---

## 六、创建自定义骨架

### 6.1 步骤指南

1. **识别不变流程**：分析计算中哪些步骤是固定的
2. **定义骨架类**：创建 `static class`，方法名使用 `Compute`/`Apply`
3. **参数验证**：在入口方法进行参数验证
4. **分步计算**：将流程拆分为清晰的步骤，每步报告进度
5. **提供异步版本**：添加 `ComputeAsync` 方法支持取消
6. **使用 LoopEngine**：对于需要并行循环的内部逻辑，委托给 LoopEngine

### 6.2 完整示例

```csharp
/// <summary>
/// 自定义归一化骨架
/// </summary>
public static class MyNormalizationSkeleton
{
    private static readonly LoopEngine _loopEngine = new();

    /// <summary>
    /// 执行自定义归一化
    /// </summary>
    public static Tensor Compute(
        Tensor input,
        double alpha,
        IProgress<ComputationProgress> progress = null)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));

        progress?.Report(new ComputationProgress(0, "MyNorm: Step 1/2 - Compute Scale"));

        // Step 1: 计算缩放因子
        var scale = ComputeScale(input, alpha);

        progress?.Report(new ComputationProgress(50, "MyNorm: Step 2/2 - Apply Scale"));

        // Step 2: 应用缩放
        var result = ApplyScale(input, scale);

        progress?.Report(new ComputationProgress(100, "MyNorm: Complete"));

        return result;
    }

    private static Tensor ComputeScale(Tensor input, double alpha)
    {
        var result = new Tensor(input.Shape);
        _loopEngine.ExecuteInPlaceSync(
            (Src: input, Dst: result, Alpha: alpha, Dim: input.Dimension),
            (ctx, b, s) => {
                double sum = 0;
                for (int d = 0; d < ctx.Dim; d++)
                    sum += Math.Abs(ctx.Src[b, s, d]);
                double scale = Math.Log(sum + ctx.Alpha);
                for (int d = 0; d < ctx.Dim; d++)
                    ctx.Dst[b, s, d] = scale;
            },
            new LoopShape(input.BatchSize, input.SeqLength));
        return result;
    }

    private static Tensor ApplyScale(Tensor input, Tensor scale)
    {
        return input.Divide(scale);
    }

    /// <summary>
    /// 异步版本（支持取消）
    /// </summary>
    public static Task<Tensor> ComputeAsync(
        Tensor input,
        double alpha,
        IProgress<ComputationProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Compute(input, alpha, progress);
        }, cancellationToken);
    }
}
```

---

## 七、最佳实践

### 7.1 何时使用骨架

| 场景 | 推荐做法 |
|------|---------|
| 固定计算流程（Attention、FFN） | 封装为计算骨架 |
| 需要并行循环的逐元素运算 | 使用 LoopEngine |
| 纯数学计算（无循环依赖） | 提取为静态工具类 |
| 简单的一维数组操作 | 直接编写循环 |

### 7.2 性能建议

- **LoopEngine 自动处理并行**：无需手动判断是否使用 `Parallel.For`
- **分块计算**：大矩阵（>10000 行）使用 `ExecuteBlockedAsync`
- **避免过度抽象**：简单循环（如 GetRow/SetRow）直接编写
- **上下文打包**：使用元组语法打包上下文，避免创建额外类

### 7.3 命名规范

| 元素 | 命名规则 | 示例 |
|------|---------|------|
| 骨架类 | `XxxSkeleton` | `AttentionSkeleton` |
| 同步方法 | `Compute` / `Apply` | `Compute(Q, K, V)` |
| 异步方法 | `ComputeAsync` / `ApplyAsync` | `ComputeAsync(Q, K, V)` |
| 循环引擎实例 | `_loopEngine` | `private static readonly LoopEngine _loopEngine = new();` |
| 进度参数 | `progress` | `IProgress<ComputationProgress> progress = null` |
| 取消令牌 | `cancellationToken` | `CancellationToken cancellationToken = default` |

---

## 八、文件索引

### Mathematics 项目（基础骨架层）

| 文件 | 说明 |
|------|------|
| `Mathematics/Matrix/Core/LoopEngine.cs` | 通用循环骨架引擎 |
| `Mathematics/Matrix/Core/LoopConfig.cs` | 循环配置 + LoopShape |
| `Mathematics/Matrix/Core/MatrixEngine.cs` | Matrix<double> 包装层 |
| `Mathematics/Core/ComputationProgress.cs` | 进度信息类 |
| `Mathematics/Core/SoftmaxCalculator.cs` | Softmax 纯数学实现 |
| `Mathematics/Core/ReduceOperations.cs` | 归约操作纯数学实现 |
| `Mathematics/Core/ActivationFunctions.cs` | 激活函数纯数学实现 |
| `Mathematics/Core/NormalizationOperations.cs` | 归一化纯数学实现 |
| `Mathematics/Core/BroadcastHelper.cs` | 广播辅助工具 |
| `Mathematics/Core/DimensionHelper.cs` | 维度验证工具 |

### Agent 项目（业务骨架层）

| 文件 | 说明 |
|------|------|
| `Agent/Transformers/Skeletons/AttentionSkeleton.cs` | Attention 计算骨架 |
| `Agent/Transformers/Skeletons/FeedForwardSkeleton.cs` | FFN 计算骨架 |
| `Agent/Transformers/Skeletons/NormalizationSkeleton.cs` | 归一化骨架 |
| `Agent/Transformers/Skeletons/ResidualSkeleton.cs` | 残差连接骨架 |
| `Agent/Transformers/Skeletons/EmbeddingSkeleton.cs` | Embedding 查找骨架 |
| `Agent/Transformers/Core/Tensor.cs` | 3D 张量实现 |
| `Agent/Transformers/Core/TensorShape.cs` | 张量形状定义 |
| `Agent/Transformers/Core/TensorOperations.cs` | 张量操作工具 |
| `Agent/Transformers/Core/ActivationFunctions.cs` | 激活函数（Tensor 版本） |
| `Agent/Transformers/Layers/MultiHeadAttentionLayer.cs` | 多头注意力层 |
| `Agent/Transformers/Layers/FeedForwardLayer.cs` | 前馈网络层 |
| `Agent/Transformers/Layers/TransformerBlock.cs` | Transformer Block |
| `Agent/Transformers/Models/TransformerModel.cs` | 完整 Transformer 模型 |

---

## 九、快速参考

### 骨架 API 速查表

```csharp
// === Attention ===
AttentionSkeleton.Compute(Q, K, V, mask?, progress?)
AttentionSkeleton.ComputeAsync(Q, K, V, mask?, progress?, cancellationToken?)

// === FeedForward ===
FeedForwardSkeleton.Compute(input, w1, b1, w2, b2, activation, dropoutRate?, progress?)
FeedForwardSkeleton.ComputeAsync(input, w1, b1, w2, b2, activation, dropoutRate?, progress?, cancellationToken?)

// === Normalization ===
NormalizationSkeleton.LayerNorm(input, gamma, beta, eps?)
NormalizationSkeleton.RMSNorm(input, gamma, eps?)
NormalizationSkeleton.LayerNormAsync(input, gamma, beta, eps?, cancellationToken?)
NormalizationSkeleton.RMSNormAsync(input, gamma, eps?, cancellationToken?)

// === Residual ===
ResidualSkeleton.Apply(input, sublayer, gamma, beta, preNorm?, eps?)
ResidualSkeleton.ApplyAsync(input, sublayer, gamma, beta, preNorm?, eps?, cancellationToken?)

// === Embedding ===
EmbeddingSkeleton.Lookup(tokenIds, tokenEmbedding, positionEmbedding, vocabSize)

// === LoopEngine ===
engine.ExecuteAsync<TIn, TOut>(input, transform, shape, config?, progress?, ct?)
engine.ExecuteInPlaceAsync<TIn>(input, action, shape, config?, progress?, ct?)
engine.ExecuteSync<TIn, TOut>(input, transform, shape, config?)
engine.ExecuteInPlaceSync<TIn>(input, action, shape, config?)
engine.ExecuteBlockedAsync<TIn>(input, action, shape, blockSize, config?, progress?, ct?)
```
