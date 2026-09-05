# Mathematics 目录优化整理方案

## 优化优先级

| 优先级 | 问题 | 涉及文件 | 预计工作量 |
|--------|------|----------|-----------|
| P0 | AssignClusters() 双重计算 | ClusteringEngine.cs | 小 |
| P0 | 缓存键使用引用哈希 | MatrixCache.cs, ClusteringFlow.cs | 小 |
| P0 | SpectralAlgorithm 未真正实现谱聚类 | SpectralAlgorithm.cs | 中 |
| P1 | 欧几里得距离3处重复 | ClusteringEngine.cs + 所有算法文件 | 中 |
| P1 | 余弦相似度3处重复 | DistanceCalculator.cs, SimilarityCalculators.cs | 小 |
| P1 | 相似度矩阵未利用对称性 | ClusteringEngine.cs | 小 |
| P1 | 字节格式化重复 | MatrixAlgorithms.cs | 小 |
| P2 | 其余重复/冗余逻辑 | 多个文件 | 大 |

## 执行步骤

### Step 1: 修复 AssignClusters() 双重计算（P0）
- 移除第二次串行计算
- 在并行计算中使用 bool[] 跟踪变化

### Step 2: 修复缓存键使用引用哈希（P0）
- MatrixCacheManager.GenerateKey 改用内容哈希
- ClusteringFlow.ComputeCoreAsync 改用内容哈希

### Step 3: 修复 SpectralAlgorithm（P0）
- 移除冗余的矩阵计算
- 或者真正实现谱聚类

### Step 4: 统一欧几里得距离（P1）
- 移除 ClusteringEngine.EuclideanDistance()
- 所有算法改用 DistanceCalculatorFactory

### Step 5: 统一余弦相似度（P1）
- 移除 SimilarityCalculators.CosineSimilarity()
- 统一使用 IDistanceCalculator 体系

### Step 6: 修复相似度矩阵对称性（P1）
- ComputeSimilarityMatrix 利用对称性
- ComputeNegativeDistanceMatrix 利用对称性

### Step 7: 移除 ByteFormatter（P1）
- 统一使用 MemoryManager.FormatBytes()

### Step 8: 其他优化（P2）
- 移除冗余的扩展方法
- 合并重复的验证方法
- 其他清理
