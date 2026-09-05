# Transformer 框架设计方案

> 基于 Mathematics 项目的 Skeleton 模式，构建 AI 大模型的 Transformer 架构

---

## 1. 概述

### 1.1 目标

在 C# 中实现一个完整的 **Transformer 推理框架**，基于 Mathematics 项目现有的 Skeleton 模式（IterationSkeleton、LoopEngine、TransformFlow），构建从底层张量运算到顶层 Transformer 模型的完整层次结构。

### 1.2 设计原则

1. **Skeleton 驱动**：每个核心组件都提取为 Skeleton 模式，定义不变的流程骨架，可变部分由子类/委托注入
2. **层次分离**：Tensor（数据层）→ Skeleton（算法层）→ Layer（组件层）→ Model（模型层）
3. **复用 Mathematics**：充分利用现有的 Matrix、Vectors、LoopEngine、IterationSkeleton 等基础设施
4. **纯推理**：当前聚焦前向传播（推理），不涉及反向传播训练

### 1.3 与现有 Mathematics 的关系

```
Transformer 框架
  │
  ├── Tensor (3D 张量) ────── 扩展 Matrix (2D)
  ├── AttentionSkeleton ───── 复用 Matrix 乘法 + LoopEngine
  ├── FeedForwardSkeleton ─── 复用 Matrix 乘法 + 激活函数
  ├── NormalizationSkeleton ─ 复用 Vectors 统计运算
  ├── PositionalEncoding ──── 复用 Vectors Sin/Cos
  ├── IterationSkeleton ───── 复用训练/推理迭代
  └── TransformFlow ───────── 复用流程骨架
```

---

## 2. 项目结构

```
Mathematics/
  Transformers/                          ← Transformer 框架根目录
    Core/                                ← 核心数据结构和基础运算
      Tensor.cs                          ← 3D 张量（扩展 Matrix<double>）
      TensorShape.cs                     ← 张量形状 (Batch, SeqLen, Dim)
      TensorOperations.cs                ← 张量运算（切片、转置、拼接）
      ActivationFunctions.cs             ← 激活函数（ReLU, GELU, Softmax, Swish）
      LayerNorm.cs                       ← Layer Normalization
      PositionalEncoding.cs              ← 位置编码（Sinusoidal + 可学习）
    
    Skeletons/                           ← Skeleton 层（算法骨架）
      AttentionSkeleton.cs               ← Attention 计算骨架
      FeedForwardSkeleton.cs             ← 前馈网络骨架
      NormalizationSkeleton.cs           ← 归一化骨架
      ResidualSkeleton.cs                ← 残差连接骨架
    
    Layers/                              ← 层组件
      MultiHeadAttentionLayer.cs         ← 多头注意力层
      FeedForwardLayer.cs                ← 前馈层
      TransformerBlock.cs                ← Transformer Block（组合层）
      EncoderLayer.cs                    ← Encoder 层
      DecoderLayer.cs                    ← Decoder 层（含 Cross-Attention）
    
    Models/                              ← 完整模型
      TransformerConfig.cs               ← Transformer 超参数配置
      TransformerModel.cs                ← 完整 Encoder-Decoder 模型
      EncoderModel.cs                    ← 仅 Encoder（BERT 风格）
      DecoderModel.cs                    ← 仅 Decoder（GPT 风格）
    
    Interfaces/                          ← 接口定义
      IAttention.cs                      ← Attention 接口
      ITransformerLayer.cs               ← 层接口
      ITransformerModel.cs               ← 模型接口
```

---

## 3. 核心组件设计

### 3.1 Tensor（张量）

Transformer 处理的是 3D 张量 `[Batch, SeqLen, Dim]`，需要在 Matrix（2D）基础上扩展。

```csharp
/// <summary>
/// 3D 张量，扩展 Matrix<double>
/// 形状: [BatchSize, SeqLength, Dimension]
/// </summary>
public class Tensor
{
    private readonly double[,,] _data;
    
    public int BatchSize { get; }
    public int SeqLength { get; }
    public int Dimension { get; }
    
    // 核心运算
    public Tensor MatMul(Tensor other);           // 批量矩阵乘法
    public Tensor Transpose(int dim1, int dim2);  // 维度转置
    public Tensor Slice(int batch, int start, int length); // 切片
    public Tensor Reshape(int b, int s, int d);   // 重塑
    
    // 与 Matrix 互转
    public Matrix<double> ToMatrix();             // 降维到 2D
    public static Tensor FromMatrix(Matrix<double> matrix, int batch);
}
```

**Skeleton 模式**：Tensor 运算委托给 LoopEngine 执行并行循环

```csharp
// Tensor 矩阵乘法骨架
public Tensor MatMul(Tensor other)
{
    // 使用 LoopEngine 执行批量矩阵乘法
    var result = new Tensor(BatchSize, SeqLength, other.Dimension);
    _loopEngine.ExecuteInPlaceAsync(
        (A: this, B: other, C: result),
        (ctx, b, s) => {
            for (int d = 0; d < Dimension; d++)
                ctx.C[b, s, d] = ctx.A[b, s, d] * ctx.B[b, s, d];
        },
        new LoopShape(BatchSize, SeqLength));
    return result;
}
```

### 3.2 AttentionSkeleton（注意力骨架）

Attention 的核心计算流程是固定的：

```
QK^T → Scale → Mask(可选) → Softmax → V
```

```csharp
/// <summary>
/// Attention 计算骨架
/// 
/// 定义不变的 Attention 计算流程：
///   Step 1: QK^T 矩阵乘法
///   Step 2: Scale (除以 sqrt(d_k))
///   Step 3: Mask (可选，Decoder 用)
///   Step 4: Softmax
///   Step 5: × V 矩阵乘法
/// 
/// 使用方式：
///   var output = await AttentionSkeleton.ComputeAsync(
///       Q, K, V, mask, config, progress, ct);
/// </summary>
public static class AttentionSkeleton
{
    public static async Task<Tensor> ComputeAsync(
        Tensor Q, Tensor K, Tensor V,
        Tensor mask = null,
        AttentionConfig config = null,
        IProgress<ComputationProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        // Step 1: QK^T
        var scores = await Q.MatMulAsync(K.Transpose(-2, -1));
        
        // Step 2: Scale
        scores = scores.Divide(Math.Sqrt(K.Dimension));
        
        // Step 3: Mask
        if (mask != null)
            scores = scores.Add(mask);
        
        // Step 4: Softmax (沿最后一个维度)
        scores = scores.Softmax(dim: -1);
        
        // Step 5: × V
        return await scores.MatMulAsync(V);
    }
}
```

### 3.3 FeedForwardSkeleton（前馈骨架）

FFN 的计算流程：

```
Input → Linear(W1) → Activation → Linear(W2) → Dropout(可选)
```

```csharp
/// <summary>
/// 前馈网络骨架
/// 
/// 定义不变的 FFN 计算流程：
///   Step 1: Linear (xW1^T + b1)
///   Step 2: Activation (ReLU/GELU/Swish)
///   Step 3: Linear (xW2^T + b2)
///   Step 4: Dropout (可选)
/// </summary>
public static class FeedForwardSkeleton
{
    public static async Task<Tensor> ComputeAsync(
        Tensor input,
        Tensor weight1, Tensor bias1,
        Tensor weight2, Tensor bias2,
        ActivationType activation = ActivationType.GELU,
        double dropoutRate = 0.0,
        IProgress<ComputationProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Linear 1
        var hidden = await LinearAsync(input, weight1, bias1);
        
        // Step 2: Activation
        hidden = ActivationFunctions.Apply(hidden, activation);
        
        // Step 3: Linear 2
        var output = await LinearAsync(hidden, weight2, bias2);
        
        // Step 4: Dropout
        if (dropoutRate > 0)
            output = output.Dropout(dropoutRate);
        
        return output;
    }
    
    private static async Task<Tensor> LinearAsync(
        Tensor input, Tensor weight, Tensor bias)
    {
        // xW^T + b
        return (await input.MatMulAsync(weight.Transpose())).Add(bias);
    }
}
```

### 3.4 NormalizationSkeleton（归一化骨架）

LayerNorm 的计算流程：

```
Input → Mean → Var → Normalize → Scale(γ) → Shift(β)
```

```csharp
/// <summary>
/// 归一化骨架
/// 
/// 定义不变的 LayerNorm 计算流程：
///   Step 1: 计算均值 (沿最后一个维度)
///   Step 2: 计算方差
///   Step 3: 归一化 (x - μ) / sqrt(σ² + ε)
///   Step 4: Scale (× γ)
///   Step 5: Shift (+ β)
/// </summary>
public static class NormalizationSkeleton
{
    public static Tensor LayerNorm(
        Tensor input,
        double[] gamma,
        double[] beta,
        double eps = 1e-5)
    {
        // Step 1-2: Mean + Var
        var mean = input.Mean(dim: -1, keepDim: true);
        var var = input.Var(dim: -1, keepDim: true);
        
        // Step 3: Normalize
        var normalized = input.Subtract(mean).Divide(var.Add(eps).Sqrt());
        
        // Step 4-5: Scale + Shift
        return normalized.Multiply(gamma).Add(beta);
    }
}
```

### 3.5 ResidualSkeleton（残差连接骨架）

```csharp
/// <summary>
/// 残差连接骨架
/// 
/// 定义不变的残差连接流程：
///   Step 1: 子层计算 (Attention/FFN)
///   Step 2: 残差连接 (x + Sublayer(x))
///   Step 3: LayerNorm
/// </summary>
public static class ResidualSkeleton
{
    public static Tensor Apply(
        Tensor input,
        Func<Tensor, Tensor> sublayer,
        double[] gamma, double[] beta,
        bool preNorm = false)  // Pre-LN vs Post-LN
    {
        if (preNorm)
        {
            // Pre-LN: x + Sublayer(LayerNorm(x))
            var normalized = NormalizationSkeleton.LayerNorm(input, gamma, beta);
            return input.Add(sublayer(normalized));
        }
        else
        {
            // Post-LN: LayerNorm(x + Sublayer(x))
            var output = input.Add(sublayer(input));
            return NormalizationSkeleton.LayerNorm(output, gamma, beta);
        }
    }
}
```

---

## 4. 层组件设计

### 4.1 MultiHeadAttentionLayer

```csharp
/// <summary>
/// 多头注意力层
/// 
/// 组合 AttentionSkeleton + 多头拆分/合并：
///   Step 1: QKV 线性投影 (Wq, Wk, Wv)
///   Step 2: 拆分为 num_heads 个头
///   Step 3: AttentionSkeleton (每个头独立计算)
///   Step 4: 合并所有头
///   Step 5: 输出线性投影 (Wo)
///   Step 6: Dropout
/// </summary>
public class MultiHeadAttentionLayer : ITransformerLayer
{
    private readonly int _numHeads;
    private readonly int _headDim;
    
    // 可学习参数
    private readonly Tensor _wq, _wk, _wv, _wo;
    
    public async Task<Tensor> ForwardAsync(
        Tensor input,
        IProgress<ComputationProgress> progress = null)
    {
        // QKV 投影
        var Q = await LinearAsync(input, _wq);
        var K = await LinearAsync(input, _wk);
        var V = await LinearAsync(input, _wv);
        
        // 拆分为多头
        var (qHeads, kHeads, vHeads) = SplitHeads(Q, K, V);
        
        // 每个头独立 Attention
        var headOutputs = new List<Tensor>();
        for (int h = 0; h < _numHeads; h++)
        {
            var headOut = await AttentionSkeleton.ComputeAsync(
                qHeads[h], kHeads[h], vHeads[h]);
            headOutputs.Add(headOut);
        }
        
        // 合并头
        var concat = ConcatHeads(headOutputs);
        
        // 输出投影
        return await LinearAsync(concat, _wo);
    }
}
```

### 4.2 TransformerBlock

```csharp
/// <summary>
/// Transformer Block
/// 
/// 组合 Attention + FFN + Residual + LayerNorm：
///   Step 1: MultiHeadAttention (带残差连接)
///   Step 2: FeedForward (带残差连接)
/// </summary>
public class TransformerBlock : ITransformerLayer
{
    private readonly MultiHeadAttentionLayer _attention;
    private readonly FeedForwardLayer _ffn;
    private readonly double[] _gamma1, _beta1;  // Attention LayerNorm
    private readonly double[] _gamma2, _beta2;  // FFN LayerNorm
    
    public async Task<Tensor> ForwardAsync(
        Tensor input,
        IProgress<ComputationProgress> progress = null)
    {
        // Step 1: Attention + Residual
        var attnOutput = ResidualSkeleton.Apply(
            input,
            (x) => _attention.ForwardAsync(x).Result,
            _gamma1, _beta1,
            preNorm: true);
        
        // Step 2: FFN + Residual
        var output = ResidualSkeleton.Apply(
            attnOutput,
            (x) => _ffn.ForwardAsync(x).Result,
            _gamma2, _beta2,
            preNorm: true);
        
        return output;
    }
}
```

---

## 5. 完整模型设计

### 5.1 TransformerConfig

```csharp
/// <summary>
/// Transformer 超参数配置
/// </summary>
public record TransformerConfig
{
    // 模型维度
    public int VocabSize { get; init; } = 50257;      // 词表大小
    public int HiddenSize { get; init; } = 768;        // 隐藏层维度 (d_model)
    public int NumHeads { get; init; } = 12;           // 注意力头数
    public int NumLayers { get; init; } = 12;          // Transformer 层数
    public int FFNHiddenSize { get; init; } = 3072;    // FFN 中间层维度
    public int MaxSeqLength { get; init; } = 512;      // 最大序列长度
    
    // 配置
    public double DropoutRate { get; init; } = 0.1;
    public double LayerNormEps { get; init; } = 1e-5;
    public ActivationType Activation { get; init; } = ActivationType.GELU;
    public bool PreNorm { get; init; } = true;         // Pre-LN vs Post-LN
    
    // 衍生属性
    public int HeadDim => HiddenSize / NumHeads;
    
    public static TransformerConfig Default => new();
    public static TransformerConfig Small => new() { HiddenSize = 512, NumHeads = 8, NumLayers = 6 };
    public static TransformerConfig Base => new();  // BERT-base 规模
    public static TransformerConfig Large => new() { HiddenSize = 1024, NumHeads = 16, NumLayers = 24 };
}
```

### 5.2 TransformerModel

```csharp
/// <summary>
/// 完整 Transformer 模型
/// 
/// 基于 Skeleton 模式构建的 Encoder-Decoder 架构：
/// 
/// Encoder:
///   Embedding → PositionalEncoding → [TransformerBlock × N]
/// 
/// Decoder:
///   Embedding → PositionalEncoding → 
///   [MaskedAttention → CrossAttention → FFN] × N →
///   Linear → Softmax
/// </summary>
public class TransformerModel : ITransformerModel
{
    private readonly TransformerConfig _config;
    private readonly List<TransformerBlock> _encoderLayers;
    private readonly List<DecoderBlock> _decoderLayers;
    private readonly Tensor _embedding;          // 词嵌入
    private readonly PositionalEncoding _posEnc; // 位置编码
    
    public async Task<Tensor> ForwardAsync(
        Tensor inputIds,           // [Batch, SeqLen]
        Tensor decoderInputIds,    // [Batch, SeqLen]
        IProgress<ComputationProgress> progress = null)
    {
        // === Encoder ===
        // Step 1: Token Embedding
        var encoderEmbed = Embed(inputIds);
        
        // Step 2: Positional Encoding
        encoderEmbed = _posEnc.Add(encoderEmbed);
        
        // Step 3: N × TransformerBlock
        var encoderOutput = encoderEmbed;
        for (int i = 0; i < _encoderLayers.Count; i++)
        {
            encoderOutput = await _encoderLayers[i].ForwardAsync(
                encoderOutput, progress);
        }
        
        // === Decoder ===
        // Step 1: Token Embedding
        var decoderEmbed = Embed(decoderInputIds);
        
        // Step 2: Positional Encoding
        decoderEmbed = _posEnc.Add(decoderEmbed);
        
        // Step 3: N × DecoderBlock (含 Cross-Attention)
        var decoderOutput = decoderEmbed;
        for (int i = 0; i < _decoderLayers.Count; i++)
        {
            decoderOutput = await _decoderLayers[i].ForwardAsync(
                decoderOutput, encoderOutput, progress);
        }
        
        // Step 4: LM Head (Linear + Softmax)
        var logits = await LinearAsync(decoderOutput, _lmHead);
        return logits.Softmax(dim: -1);
    }
}
```

---

## 6. 与现有 Skeleton 的对应关系

| Transformer 组件 | Skeleton 模式 | 复用的 Mathematics 模块 |
|-----------------|--------------|----------------------|
| Tensor 运算 | LoopEngine（并行循环） | MatrixEngine, LoopEngine |
| Attention 流程 | AttentionSkeleton（固定流程） | Matrix 乘法 |
| FFN 流程 | FeedForwardSkeleton（固定流程） | Matrix 乘法 |
| LayerNorm | NormalizationSkeleton（固定流程） | Vectors 统计运算 |
| 残差连接 | ResidualSkeleton（固定流程） | Tensor 加法 |
| 位置编码 | 直接实现 | Vectors Sin/Cos |
| 激活函数 | 直接实现 | Core/MathUtility |
| 模型推理 | TransformFlow（端到端流程） | TransformFlow 骨架 |
| 训练迭代 | IterationSkeleton（迭代模式） | IterationSkeleton |

---

## 7. 实施路线图

### Phase 1：基础组件
- [ ] Tensor 类（3D 张量 + 基本运算）
- [ ] TensorShape（形状管理 + 广播规则）
- [ ] ActivationFunctions（ReLU, GELU, Softmax, Swish）
- [ ] 单元测试

### Phase 2：Skeleton 层
- [ ] AttentionSkeleton（QKV 计算骨架）
- [ ] FeedForwardSkeleton（FFN 计算骨架）
- [ ] NormalizationSkeleton（LayerNorm 骨架）
- [ ] ResidualSkeleton（残差连接骨架）
- [ ] PositionalEncoding（位置编码）
- [ ] 单元测试

### Phase 3：层组件
- [ ] MultiHeadAttentionLayer
- [ ] FeedForwardLayer
- [ ] TransformerBlock
- [ ] EncoderLayer / DecoderLayer
- [ ] 集成测试

### Phase 4：完整模型
- [ ] TransformerConfig（超参数配置）
- [ ] TransformerModel（Encoder-Decoder）
- [ ] EncoderModel（BERT 风格）
- [ ] DecoderModel（GPT 风格）
- [ ] 端到端推理测试

---

## 8. 使用示例

```csharp
// 1. 创建配置
var config = TransformerConfig.Small;  // d_model=512, heads=8, layers=6

// 2. 构建模型
var model = new TransformerModel(config);

// 3. 准备输入
var inputIds = new int[,] { { 101, 205, 310, 102, 0, 0 } };  // [1, 6]
var inputTensor = Tensor.FromArray(inputIds);

// 4. 推理
var output = await model.ForwardAsync(inputTensor);

// 5. 获取结果
var predictions = output.ArgMax(dim: -1);
Console.WriteLine($"Predicted tokens: {predictions}");
```

---

## 9. 注意事项

1. **性能**：纯 C# CPU 实现，适合小规模推理和原型验证，大规模推理建议对接 ONNX Runtime
2. **内存**：3D 张量运算内存消耗大，建议使用 TransformCacheManager 做结果缓存
3. **精度**：使用 double 精度，后续可考虑 float/half 优化
4. **扩展性**：Skeleton 模式设计便于后续添加新组件（如 MoE、FlashAttention）
