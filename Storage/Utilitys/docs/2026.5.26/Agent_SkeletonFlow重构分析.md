# Agent Skeleton Flow 重构分析

## 概述

本文档分析 `Agent/Transformers/Core/Tensor.cs` 中哪些逻辑可以基于 Skeleton 重构，以及哪些可以提取为通用逻辑。

## 重构目标

1. **消除重复循环模式**：Tensor 中大量方法使用相同的三重循环结构（for b, for s, for d），应提取为通用骨架
2. **复用 Mathematics 的 LoopEngine**：所有并行循环应统一使用 `LoopEngine` 骨架
3. **提取通用辅助逻辑**：广播索引计算、维度验证等应提取为独立工具类
4. **保持 API 兼容**：重构后 Tensor 的公开 API 不变

## 可基于 Skeleton 重构的逻辑

### 1. 广播逐元素运算 → BroadcastSkeleton

**涉及方法**：
- `Tensor.Add(Tensor)` → `BroadcastSkeleton.ElementWiseOp(this, other, (x,y) => x+y)`
- `Tensor.Subtract(Tensor)` → `BroadcastSkeleton.ElementWiseOp(this, other, (x,y) => x-y)`
- `Tensor.Multiply(Tensor)` → `BroadcastSkeleton.ElementWiseOp(this, other, (x,y) => x*y)`
- `Tensor.Divide(Tensor)` → `BroadcastSkeleton.ElementWiseOp(this, other, (x,y) => x/y)`

**重构收益**：消除 4 个方法中重复的广播索引计算和循环逻辑。

**文件**：`Agent/Transformers/Skeletons/BroadcastSkeleton.cs` ✅ 已创建

### 2. 一元/标量运算 → UnaryOpSkeleton

**涉及方法**：
- `Tensor.Sqrt()` → `UnaryOpSkeleton.Apply(this, Math.Sqrt)`
- `Tensor.Exp()` → `UnaryOpSkeleton.Apply(this, Math.Exp)`
- `Tensor.Multiply(double)` → `UnaryOpSkeleton.ApplyScalar(this, scalar, (x,s) => x*s)`
- `Tensor.Divide(double)` → `UnaryOpSkeleton.ApplyScalar(this, scalar, (x,s) => x/s)`
- `Tensor.Add(double)` → `UnaryOpSkeleton.ApplyScalar(this, scalar, (x,s) => x+s)`
- `ActivationFunctions.ReLU(Tensor)` → `UnaryOpSkeleton.Apply(input, x => Math.Max(0, x))`
- `ActivationFunctions.GELU(Tensor)` → `UnaryOpSkeleton.Apply(input, GELU)`
- `ActivationFunctions.Swish(Tensor)` → `UnaryOpSkeleton.Apply(input, Swish)`

**重构收益**：消除 8+ 个方法中重复的循环逻辑。

**文件**：`Agent/Transformers/Skeletons/UnaryOpSkeleton.cs` ✅ 已创建

### 3. 张量工厂方法 → TensorFactorySkeleton

**涉及方法**：
- `Tensor.Ones(TensorShape)` → `TensorFactorySkeleton.Ones(shape)`
- `Tensor.Fill(TensorShape, double)` → `TensorFactorySkeleton.Fill(shape, value)`
- `Tensor.Random(TensorShape, Random?)` → `TensorFactorySkeleton.Random(shape, rng)`

**重构收益**：消除工厂方法中重复的填充循环逻辑。

**文件**：`Agent/Transformers/Skeletons/TensorFactorySkeleton.cs` ✅ 已创建

### 4. 归约运算 → ReduceSkeleton

**涉及方法**：
- `Tensor.Mean(int dim)` → `ReduceSkeleton.Mean(this, dim)`
- `Tensor.Var(int dim)` → `ReduceSkeleton.Var(this, dim)`
- `Tensor.Sum(int dim)` → `ReduceSkeleton.Sum(this, dim)`
- `Tensor.Max(int dim)` → `ReduceSkeleton.Max(this, dim)`
- `Tensor.ArgMax(int dim)` → `ReduceSkeleton.ArgMax(this, dim)`

**重构收益**：消除 5 个方法中重复的维度验证、数据收集和循环逻辑。

**文件**：`Agent/Transformers/Skeletons/ReduceSkeleton.cs` ✅ 已创建

## 可提取为通用逻辑的部分

### 1. 广播索引计算 → BroadcastHelper

**提取内容**：
- `Math.Min(targetB, sourceShape.Batch - 1)` 等广播索引映射逻辑
- 在 `Tensor.Add/Subtract/Multiply/Divide` 和 `TensorOperations.ApplyMask` 中重复出现

**文件**：`Agent/Transformers/Core/BroadcastHelper.cs` ✅ 已创建

### 2. 维度验证和转换 → DimensionHelper

**提取内容**：
- `ValidateDim(int dim)`：验证维度值（0, 1, 2）
- `NormalizeDim(int dim)`：处理负索引（-1 → 2, -2 → 1, -3 → 0）
- `GetReducedShape(TensorShape, int, bool)`：计算归约后的形状
- `GetTransposedShape(TensorShape, int, int)`：计算转置后的形状
- 在 `Tensor.Reduce/Transpose/Softmax` 中分散出现

**文件**：`Agent/Transformers/Core/DimensionHelper.cs` ✅ 已创建

### 3. 矩阵运算中的广播 Batch 逻辑

**提取内容**：
- `Math.Max(BatchSize, other.BatchSize)` 和 `Math.Min(b, ctx.BatchA - 1)` 在 `MatMul` 中重复
- 可提取为 `MatMulHelper` 或合并到 `BroadcastHelper`

**状态**：已在 `MatMul` 中内联使用，暂未提取为独立类

## 重构后的文件结构

```
Agent/Transformers/
├── Core/
│   ├── Tensor.cs              # 重构后：委托给 Skeleton 层，使用 LoopEngine
│   ├── TensorShape.cs         # 不变
│   ├── TensorOperations.cs    # 重构后：使用 LoopEngine + BroadcastHelper
│   ├── ActivationFunctions.cs # 重构后：使用 UnaryOpSkeleton
│   ├── BroadcastHelper.cs     # [新增] 广播索引计算辅助类
│   └── DimensionHelper.cs     # [新增] 维度验证和转换辅助类
├── Skeletons/
│   ├── BroadcastSkeleton.cs   # [新增] 广播逐元素运算骨架
│   ├── UnaryOpSkeleton.cs     # [新增] 一元/标量运算骨架
│   ├── TensorFactorySkeleton.cs # [新增] 张量工厂骨架
│   ├── ReduceSkeleton.cs      # [新增] 归约运算骨架
│   ├── EmbeddingSkeleton.cs   # [新增] Embedding 查找骨架
│   ├── NormalizationSkeleton.cs # 不变
│   ├── AttentionSkeleton.cs   # 不变
│   └── FeedForwardSkeleton.cs # 不变
├── Models/
│   └── TransformerModel.cs    # 重构后：使用 EmbeddingSkeleton
```

## 额外发现的优化点

### 1. Tensor.cs 中仍可进一步优化的原始循环

在重构过程中，发现以下方法仍包含原始三重循环，已使用 LoopEngine 优化：

| 方法 | 原始循环 | 优化方式 |
|------|---------|---------|
| `Tensor(double[,])` 构造函数 | 双重循环 | LoopEngine.ExecuteInPlaceSync |
| `Tensor.FromMatrix()` | 双重循环 | LoopEngine.ExecuteInPlaceSync |
| `Tensor.ToMatrix()` | 三重循环 | LoopEngine.ExecuteInPlaceSync |
| `Tensor.Slice()` | 双重循环 | LoopEngine.ExecuteInPlaceSync |

**Reshape 方法** 由于需要展平再填充（非逐元素映射），保留原始循环更清晰。

### 2. TransformerModel.Embed 中的原始循环

`TransformerModel.Embed` 方法包含原始三重循环（batch × seqLen × modelDim），已提取为 `EmbeddingSkeleton` 骨架：

- **文件**：`Agent/Transformers/Skeletons/EmbeddingSkeleton.cs` ✅ 已创建
- **重构收益**：消除 Embedding 查找中的重复循环逻辑，统一使用 LoopEngine 并行执行

### 3. 仍保留原始循环的方法（无需优化）

以下方法保留原始循环，原因如下：

| 方法 | 原因 |
|------|------|
| `Tensor.Reshape()` | 需要展平再填充，非逐元素映射，使用 LoopEngine 反而增加复杂度 |
| `Tensor.GetRow()` | 仅返回一维数组，循环简单 |
| `Tensor.SetRow()` | 仅设置一维数组，循环简单 |
| `TransformerModel.SampleFromProbs()` | 累积概率采样，需要顺序执行，无法并行 |
| `TransformerModel.Generate()` | 自回归生成，每一步依赖上一步结果 |


## 复用层次

```
Tensor (API 层)
  ├── BroadcastSkeleton      → 广播逐元素运算
  │     └── LoopEngine       → 高性能并行循环
  ├── UnaryOpSkeleton        → 一元/标量运算
  │     └── LoopEngine       → 高性能并行循环
  ├── TensorFactorySkeleton  → 工厂方法
  │     └── LoopEngine       → 高性能并行循环
  ├── ReduceSkeleton         → 归约运算
  │     └── LoopEngine       → 高性能并行循环
  ├── MatMul (内联)          → 矩阵乘法
  │     └── LoopEngine       → 高性能并行循环
  ├── Softmax (内联)         → Softmax
  │     └── LoopEngine       → 高性能并行循环
  └── Transpose (内联)       → 转置
        └── LoopEngine       → 高性能并行循环

辅助类：
  ├── BroadcastHelper        → 广播索引计算
  └── DimensionHelper        → 维度验证和转换
```

## 重构收益

| 类别 | 消除重复方法数 | 减少代码行数（估计） |
|------|---------------|-------------------|
| 广播运算 | 4 | ~80 行 |
| 一元/标量运算 | 8+ | ~120 行 |
| 工厂方法 | 3 | ~60 行 |
| 归约运算 | 5 | ~150 行 |
| 辅助逻辑 | - | ~50 行 |
| **总计** | **20+** | **~460 行** |

## 构建状态

✅ **构建成功** - 0 Warnings, 0 Errors
