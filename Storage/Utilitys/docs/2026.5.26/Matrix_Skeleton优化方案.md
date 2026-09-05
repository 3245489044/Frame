# Matrix 目录 Skeleton 优化方案

## 已完成优化

### 1. ✅ LoopEngine.cs — 提取并行/顺序选择骨架
- 提取 `ShouldUseParallel(LoopShape, LoopConfig)` 方法 → 消除 4 处重复的条件判断
- 提取 `ReportSequentialProgress` 方法 → 消除顺序版本中重复的进度报告逻辑
- 新增 `ExecuteBlockedAsync` 分块循环骨架方法

### 2. ✅ MatrixEngine.cs — ComputeBlockedAsync 复用 LoopEngine
- `ComputeBlockedAsync` 改为委托给 `LoopEngine.ExecuteBlockedAsync`
- 消除了自实现的分块并行骨架代码（Parallel.ForEach、Interlocked.Increment、进度锁等）

### 3. ✅ MatrixFlow.cs — QuickComputeAsync 与 ComputeAsync 骨架合并
- 提取 `ComputeCoreAsync` 核心骨架方法，通过 `enableCaching` 和 `enableProgressReporting` 参数控制行为
- `ComputeAsync` 和 `QuickComputeAsync` 都委托给 `ComputeCoreAsync`
- 消除了输入验证、Stopwatch 计时、AlgorithmMetadata 创建、MatrixResult 包装的重复

### 4. ✅ MatrixFlow.cs — 静态工厂方法 async→sync 安全转换
- 提取 `RunSync` 安全同步执行骨架（使用 Task.Run 包装以避免死锁）
- `CreateMatrix` 改为使用 `RunSync` 替代 `.GetAwaiter().GetResult()`

### 5. ✅ MatrixAlgorithms.cs — 提取算法结果包装骨架
- 提取 `AlgorithmResultSkeleton.CreateResult` 方法
- `FunctionalMatrixAlgorithm.ComputeAsync` 和 `BlockMatrixAlgorithm.ComputeAsync` 复用

### 6. ✅ MatrixCache.cs — 简化 CacheCleanupSkeleton 参数
- 提取 `CacheSizeTracker` 类封装 `currentSize` 和 `sizeLock`
- `CacheCleanupSkeleton.CleanupExpired` 和 `EvictLru` 签名简化，不再需要 `ref long` 和 `object lock`

## 文件整合（按功能模块分类）

### 整合方案：11 个文件 → 5 个子目录

```
Mathematics/Matrix/
├── Models/                    ← 数据模型 + 配置
│   └── MatrixModels.cs        (MatrixShape, MatrixComputationConfig, CachePolicy, AlgorithmMetadata, MatrixResult)
│
├── Interfaces/                ← 接口定义
│   └── MatrixInterfaces.cs    (IMatrixAlgorithm<TInput>, IMatrixCalculator)
│
├── Core/                      ← 核心引擎 + 循环骨架
│   ├── LoopConfig.cs          (LoopShape, LoopConfig)
│   ├── LoopEngine.cs          (循环骨架引擎)
│   └── MatrixEngine.cs        (矩阵计算引擎)
│
├── Algorithms/                ← 算法实现 + 流程编排
│   ├── MatrixAlgorithms.cs    (FunctionalMatrixAlgorithm, BlockMatrixAlgorithm, MemoryCheckSkeleton, ByteFormatter)
│   ├── MatrixFlow.cs          (MatrixComputationFlow 端到端流程)
│   ├── MatrixDecomposition.cs (SVD、降维、转置、乘法)
│   └── MatrixExtensions.cs    (MatrixAlgorithmBuilder, MatrixExtensions)
│
└── Cache/                     ← 缓存管理 + 算法调度
    └── MatrixCache.cs         (CacheSizeTracker, CacheCleanupSkeleton, MatrixCacheManager, MatrixCalculator)
```

### 合并说明
- `MatrixCalculator.cs`（68行，调度器）→ 合并到 `Cache/MatrixCache.cs`，与缓存管理同属"基础设施"层
- 所有文件保持相同命名空间 `MathematicalAlgorithms`，无需修改 using 引用
