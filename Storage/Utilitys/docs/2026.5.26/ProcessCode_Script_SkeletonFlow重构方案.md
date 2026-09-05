# ProcessCode_Script.cs SkeletonFlow 重构方案

## 概述

`ProcessCode_Script.cs` 是一个包含 3067 行代码的大型文件，包含了代码分析、优化评估、重构建议、报告生成等多个功能模块。按照 SkeletonFlow 架构，需要将其拆分为骨架层（Skeleton）和业务层（Business），骨架层负责流程控制，业务层只关注具体逻辑。

## 重构目标

1. **分离骨架层和业务层**：将流程控制逻辑提取到骨架层，业务逻辑保留在业务层
2. **消除重复代码**：消除多个分析器中的重复流程控制代码
3. **统一接口**：所有骨架层提供统一的同步/异步接口
4. **支持进度报告和取消**：骨架层支持 `IProgress<AnalysisProgress>` 和 `CancellationToken`

## 重构后的文件结构

### 骨架层（新建）

| 文件 | 位置 | 封装对象 |
|------|------|----------|
| `AnalysisSkeleton.cs` | `RoslynMethod.Analysis/Skeletons/` | `CodeAnalysisEngine.Analyze` |
| `OptimizationSkeleton.cs` | `RoslynMethod.Analysis/Skeletons/` | `EnhancedOptimizationAnalyzer.AnalyzeWithRecommendation` |
| `RefactoringSuggestionSkeleton.cs` | `RoslynMethod.Refactoring/Skeletons/` | `RefactoringSuggestionGenerator.GenerateSuggestions` |
| `PromptRecommenderSkeleton.cs` | `RoslynMethod.Analysis/Skeletons/` | `PromptRecommender.RecommendForCode` |
| `AnalysisReportSkeleton.cs` | `RoslynMethod.Analysis/Skeletons/` | `AnalysisReportGenerator.GenerateReport` |

### 模型层（已迁移）

| 文件 | 位置 | 内容 |
|------|------|------|
| `Enums.cs` | `RoslynMethod.Core/Models/` | 所有枚举类型（已扩展） |
| `AnalysisModels.cs` | `RoslynMethod.Core/Models/` | `AnalysisContext`, `EnhancedCodeElement`, `VariableUsageInfo`, `ElementDistance` |
| `AnalysisConfiguration.cs` | `RoslynMethod.Core/Models/` | `AnalysisConfiguration`, `AnalysisResult`, `CodeCluster`, `RefactoringSuggestion`, `RecommendedPrompt` |
| `CodeAnalysisConstants.cs` | `RoslynMethod.Core/Models/` | 所有常量配置 |

### 业务层（保留在 ProcessCode_Script.cs）

| 类 | 说明 |
|----|------|
| `CodeAnalysisEngine` | 代码分析引擎（业务逻辑） |
| `RefactoringSuggestionGenerator` | 重构建议生成器（业务逻辑） |
| `OptimizationAssessmentCalculator` | 优化评估计算器（业务逻辑） |
| `AnalysisReportGenerator` | 分析报告生成器（业务逻辑） |
| `PromptRecommender` | 提示推荐器（业务逻辑） |
| `PromptQualityAnalyzer` | 提示质量分析器（业务逻辑） |
| `PromptRecommenderConstants` | 提示推荐常量（业务逻辑） |

## 骨架层接口规范

每个骨架层都遵循以下接口规范：

```csharp
public class XxxSkeleton
{
    // 同步版本
    public ResultType MethodName(params, config = null);
    
    // 异步版本（支持进度报告和取消）
    public async Task<ResultType> MethodNameAsync(params, config = null, 
        IProgress<AnalysisProgress> progress = null, 
        CancellationToken cancellationToken = default);
}
```

## 重构步骤

### 步骤 1：迁移模型层 ✅
- [x] 扩展 `Enums.cs`：添加 `RefactoringType`, `ScoreCategory`, `RecommendationType`, `OptimizationAssessment`, `VariableScope`, `VariableLifetime`
- [x] 扩展 `AnalysisModels.cs`：添加 `ElementDistance` 类，扩展 `EnhancedCodeElement` 和 `VariableUsageInfo`
- [x] 创建 `AnalysisConfiguration.cs`：包含 `AnalysisConfiguration`, `AnalysisResult`, `CodeCluster`, `RefactoringSuggestion`, `RecommendedPrompt`, `RecommendationAnalysis`, `RecommendationResult`
- [x] 创建 `CodeAnalysisConstants.cs`：集中管理所有阈值和权重常量

### 步骤 2：创建骨架层 ✅
- [x] 创建 `AnalysisSkeleton.cs`：封装 `CodeAnalysisEngine.Analyze` 的流程
- [x] 创建 `OptimizationSkeleton.cs`：封装 `EnhancedOptimizationAnalyzer.AnalyzeWithRecommendation` 的流程
- [x] 创建 `RefactoringSuggestionSkeleton.cs`：封装 `RefactoringSuggestionGenerator.GenerateSuggestions` 的流程
- [x] 创建 `PromptRecommenderSkeleton.cs`：封装 `PromptRecommender.RecommendForCode` 的流程
- [x] 创建 `AnalysisReportSkeleton.cs`：封装 `AnalysisReportGenerator.GenerateReport` 的流程

### 步骤 3：清理旧文件 ✅
- [x] 删除 `RoslynMethod.Core/Skeletons/AnalysisSkeleton.cs`（旧版，已迁移到 Analysis 项目）

### 步骤 4：更新 ProcessCode_Script.cs（待完成）
- [ ] 删除已迁移到模型层的类定义（`AnalysisConfiguration`, `AnalysisResult`, `CodeCluster` 等）
- [ ] 删除已迁移到骨架层的类定义（`EnhancedOptimizationAnalyzer` 等）
- [ ] 保留业务逻辑类（`CodeAnalysisEngine`, `RefactoringSuggestionGenerator` 等）
- [ ] 更新命名空间引用

## 骨架层与业务层的职责划分

### 骨架层职责
- 流程控制（初始化、执行、异常处理）
- 进度报告（`IProgress<AnalysisProgress>`）
- 取消支持（`CancellationToken`）
- 同步/异步接口统一
- 参数验证和默认值处理

### 业务层职责
- 具体的分析逻辑
- 具体的计算逻辑
- 具体的生成逻辑
- 不关心流程控制

## 依赖关系

```
骨架层 → 业务层 → 模型层
```

- 骨架层引用业务层（`CodeAnalysisEngine`, `RefactoringSuggestionGenerator` 等）
- 业务层引用模型层（`EnhancedCodeElement`, `AnalysisResult` 等）
- 骨架层也引用模型层（用于参数和返回值）

## 注意事项

1. **向后兼容性**：骨架层提供与原始类相同的方法签名，确保调用方无需修改
2. **渐进式重构**：可以先创建骨架层，再逐步迁移业务逻辑
3. **测试覆盖**：骨架层的测试可以专注于流程控制，业务层的测试专注于具体逻辑
