# RoslynMethod 基于 SkeletonFlow 重构方案

> 文档版本：v1.0
> 更新日期：2026-05-25
> 目标：将 RoslynMethod 项目全面重构为 SkeletonFlow 架构风格

---

## 目录

- [RoslynMethod 基于 SkeletonFlow 重构方案](#roslynmethod-基于-skeletonflow-重构方案)
  - [目录](#目录)
  - [一、概述](#一概述)
    - [1.1 重构目标](#11-重构目标)
    - [1.2 重构原则](#12-重构原则)
  - [二、当前架构分析](#二当前架构分析)
    - [2.1 项目结构总览](#21-项目结构总览)
    - [2.2 当前架构问题](#22-当前架构问题)
  - [三、SkeletonFlow 架构回顾](#三skeletonflow-架构回顾)
    - [3.1 四层架构](#31-四层架构)
    - [3.2 骨架通用设计模式](#32-骨架通用设计模式)
    - [3.3 命名规范](#33-命名规范)
  - [四、重构目标架构](#四重构目标架构)
    - [4.1 目标目录结构](#41-目标目录结构)
  - [五、模块级重构方案](#五模块级重构方案)
    - [5.1 RoslynBasic.cs 拆分](#51-roslynbasiccs-拆分)
      - [5.1.1 AnalysisSkeleton - 代码分析骨架](#511-analysisskeleton---代码分析骨架)
      - [5.1.2 QuerySkeleton - 语法查询骨架](#512-queryskeleton---语法查询骨架)
      - [5.1.3 CodeTransformSkeleton - 代码变换骨架](#513-codetransformskeleton---代码变换骨架)
      - [5.1.4 工具类拆分清单](#514-工具类拆分清单)
    - [5.2 MethodMergeEngine 重构](#52-methodmergeengine-重构)
      - [5.2.1 MethodMergeSkeleton](#521-methodmergeskeleton)
      - [5.2.2 合并策略独立文件](#522-合并策略独立文件)
    - [5.3 SyntaxHierarchy 重构](#53-syntaxhierarchy-重构)
      - [5.3.1 HierarchyAnalysisSkeleton](#531-hierarchyanalysisskeleton)
    - [5.4 RefactoringFramework 重构](#54-refactoringframework-重构)
      - [5.4.1 RefactoringSkeleton](#541-refactoringskeleton)
    - [5.5 CodeRemovalService 重构](#55-coderemovalservice-重构)
      - [5.5.1 CodeRemovalSkeleton](#551-coderemovalskeleton)
    - [5.6 StringCodeGeneration 重构](#56-stringcodegeneration-重构)
      - [5.6.1 CodeGenerationSkeleton](#561-codegenerationskeleton)
    - [5.7 StaticStore 重构](#57-staticstore-重构)
      - [5.7.1 StaticStoreSkeleton](#571-staticstoreskeleton)
    - [5.8 StorageFramework 重构](#58-storageframework-重构)
      - [5.8.1 CacheSkeleton](#581-cacheskeleton)
    - [5.9 Clustering.SkeletonFlow 优化](#59-clusteringskeletonflow-优化)
      - [5.9.1 ClusteringSkeleton](#591-clusteringskeleton)
  - [六、文件目录结构规划](#六文件目录结构规划)
    - [6.1 重构前后对比](#61-重构前后对比)
    - [6.2 文件命名规范](#62-文件命名规范)
  - [七、重构优先级与实施路线图](#七重构优先级与实施路线图)
    - [7.1 优先级矩阵](#71-优先级矩阵)
    - [7.2 分阶段实施计划](#72-分阶段实施计划)
      - [第一阶段（P0）- Core 模块拆分](#第一阶段p0--core-模块拆分)
      - [第二阶段（P1）- Merge + Hierarchy 模块重构](#第二阶段p1--merge--hierarchy-模块重构)
      - [第三阶段（P2）- Refactoring + CodeRemoval + Clustering 重构](#第三阶段p2--refactoring--coderemoval--clustering-重构)
      - [第四阶段（P3）- Generation + Storage + Analysis 重构](#第四阶段p3--generation--storage--analysis-重构)
    - [7.3 总体时间线](#73-总体时间线)
  - [八、Skeleton 通用模板](#八skeleton-通用模板)
    - [8.1 标准骨架模板](#81-标准骨架模板)
    - [8.2 进度报告规范](#82-进度报告规范)
    - [8.3 异常处理规范](#83-异常处理规范)
  - [九、兼容性与测试策略](#九兼容性与测试策略)
    - [9.1 向后兼容策略](#91-向后兼容策略)
    - [9.2 测试策略](#92-测试策略)
    - [9.3 测试用例模板](#93-测试用例模板)
  - [附录](#附录)
    - [A. 参考文档](#a-参考文档)
    - [B. 术语表](#b-术语表)
    - [C. 变更日志](#c-变更日志)

---

## 一、概述

### 1.1 重构目标

将 RoslynMethod 项目从当前的**工具类集合**架构，重构为基于 **SkeletonFlow 骨架模式**的层次化架构，实现：

- **消除重复循环**：统一并行/顺序执行策略
- **流程复用**：将固定的分析/重构流程封装为骨架
- **关注点分离**：骨架层控制流程，业务层只关注具体逻辑
- **统一进度报告**：所有骨架支持 `IProgress<ComputationProgress>`
- **统一取消支持**：所有骨架提供异步版本支持 `CancellationToken`

### 1.2 重构原则

1. **渐进式重构**：按优先级分批实施，每批保持向后兼容
2. **不改变外部 API**：重构后保持原有功能接口，新增骨架 API 作为补充
3. **单一职责**：每个文件/类只负责一个关注点
4. **层次清晰**：严格遵循 SkeletonFlow 的四层架构

---

## 二、当前架构分析

### 2.1 项目结构总览

```
RoslynMethod/
├── RoslynMethod.Analysis/          # 代码分析模块
│   └── Basic.cs                    # 624行 - 元素提取、变量分析、引用分析
├── RoslynMethod.Clustering/        # 聚类分析模块
│   ├── SkeletonFlow.cs             # 254行 - 聚类骨架流程（已部分SkeletonFlow化）
│   ├── Analysis/                   # 聚类分析器
│   ├── Distance/                   # 距离计算
│   ├── FeatureExtraction/          # 特征提取
│   └── Models/                     # 数据模型
├── RoslynMethod.CodeRemoval/       # 代码移除模块
│   └── CodeRemovalService.cs       # 234行 - 代码移除服务
├── RoslynMethod.Core/              # 核心模块
│   ├── RoslynBasic.cs              # 1127行 - 超大文件，职责混杂
│   └── CompilationReferenceHelper.cs # 207行 - 编译引用管理
├── RoslynMethod.Generation/        # 代码生成模块
│   └── StringCodeGeneration.cs     # 429行 - 代码生成器
├── RoslynMethod.Hierarchy/         # 层级分析模块
│   └── SyntaxHierarchy.cs          # 638行 - 函数层级分析
├── RoslynMethod.Merge/             # 方法合并模块
│   └── MethodMergeEngine.cs        # 854行 - 方法合并引擎
├── RoslynMethod.Refactoring/       # 重构模块
│   ├── RefactoringFramework.cs     # 271行 - 重构框架
│   └── StaticStore.cs              # 531行 - 静态存储
└── RoslynMethod.Storage/           # 存储模块
    └── StorageFramework.cs         # 327行 - 缓存与内存管理
```

### 2.2 当前架构问题

| 问题 | 严重程度 | 说明 |
|------|---------|------|
| **超大文件** | 🔴 P0 | `RoslynBasic.cs` 1127行，包含15+个类/工具模块 |
| **职责混杂** | 🔴 P0 | 一个文件同时包含模型定义、查询工具、操作工具、内部实现 |
| **缺少骨架抽象** | 🟡 P1 | 固定流程（如合并、层级分析）未抽象为骨架 |
| **缺少进度报告** | 🟡 P1 | 大部分操作不支持进度报告 |
| **缺少取消支持** | 🟡 P1 | 大部分操作不支持 CancellationToken |
| **重复循环逻辑** | 🟡 P1 | 多处存在手动 Parallel.ForEach 循环 |
| **命名不统一** | 🟢 P3 | 部分类名未遵循 Skeleton 命名规范 |

---

## 三、SkeletonFlow 架构回顾

### 3.1 四层架构

```
┌─────────────────────────────────────────────────────────────┐
│                    业务层 (Business Layer)                    │
│  RoslynMethod 各模块的业务逻辑                                │
│  关注：API 设计、数据组织、业务逻辑组合                      │
├─────────────────────────────────────────────────────────────┤
│                    骨架层 (Skeleton Layer)                    │
│  AnalysisSkeleton, MergeSkeleton, HierarchySkeleton, ...     │
│  关注：固定计算流程的定义和复用                              │
├─────────────────────────────────────────────────────────────┤
│                    循环引擎层 (Loop Engine Layer)             │
│  LoopEngine, LoopConfig, LoopShape                          │
│  关注：高性能并行循环的执行、进度报告、取消支持              │
├─────────────────────────────────────────────────────────────┤
│                    工具层 (Utility Layer)                     │
│  SyntaxAccessor, SyntaxLocator, Guard, ...                  │
│  关注：纯工具函数，无流程依赖                                │
└─────────────────────────────────────────────────────────────┘
```

### 3.2 骨架通用设计模式

```csharp
public static class XxxSkeleton
{
    // 1. 复用 LoopEngine（可选）
    private static readonly LoopEngine _loopEngine = new();

    // 2. 同步计算入口
    public static TResult Compute(
        TInput input,
        /* 其他参数 */,
        IProgress<ComputationProgress> progress = null)
    {
        // 参数验证
        if (input == null) throw new ArgumentNullException(nameof(input));

        // 分步计算，每步报告进度
        progress?.Report(new ComputationProgress(0, "Step 1/3 - ..."));
        var result = Step1(input);

        progress?.Report(new ComputationProgress(33, "Step 2/3 - ..."));
        result = Step2(result);

        progress?.Report(new ComputationProgress(66, "Step 3/3 - ..."));
        result = Step3(result);

        progress?.Report(new ComputationProgress(100, "Complete"));
        return result;
    }

    // 3. 异步计算入口（支持取消）
    public static Task<TResult> ComputeAsync(
        TInput input,
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

### 3.3 命名规范

| 元素 | 命名规则 | 示例 |
|------|---------|------|
| 骨架类 | `XxxSkeleton` | `AnalysisSkeleton` |
| 同步方法 | `Compute` / `Apply` | `Compute(code)` |
| 异步方法 | `ComputeAsync` / `ApplyAsync` | `ComputeAsync(code, ct)` |
| 循环引擎实例 | `_loopEngine` | `private static readonly LoopEngine _loopEngine = new();` |
| 进度参数 | `progress` | `IProgress<ComputationProgress> progress = null` |
| 取消令牌 | `cancellationToken` | `CancellationToken cancellationToken = default` |

---

## 四、重构目标架构

### 4.1 目标目录结构

```
RoslynMethod/
├── RoslynMethod.Core/                    # 核心模块（重构重点）
│   ├── Skeletons/
│   │   ├── AnalysisSkeleton.cs           # 代码分析骨架（原 RoslynAnalyzer）
│   │   ├── QuerySkeleton.cs              # 语法查询骨架（原 TypeQueryTool + MemberQueryTool）
│   │   └── CodeTransformSkeleton.cs      # 代码变换骨架（原 SyntaxCreationTool + StatementTransformTool）
│   ├── Models/
│   │   ├── EnhancedCodeElement.cs        # 代码元素模型
│   │   ├── VariableUsageInfo.cs          # 变量使用信息
│   │   ├── AnalysisContext.cs            # 分析上下文
│   │   └── ElementType.cs               # 元素类型枚举
│   └── Utilities/
│       ├── SyntaxAccessor.cs            # 语法访问工具
│       ├── SyntaxLocator.cs             # 语法定位工具
│       ├── SyntaxMatcher.cs             # 语法匹配工具
│       ├── SyntaxParser.cs              # 语法解析工具
│       ├── IdentifierFactory.cs         # 标识符工厂
│       ├── AccessibilityConverter.cs    # 可访问性转换
│       ├── StatementFactory.cs          # 语句工厂
│       ├── DelegateFactory.cs           # 委托工厂
│       ├── StatementTransformer.cs      # 语句变换器
│       ├── SymbolAnalyzer.cs            # 符号分析工具
│       ├── TypeQueryExecutor.cs         # 类型查询执行器
│       ├── MemberQueryExecutor.cs       # 成员查询执行器
│       └── Guard.cs                     # 参数验证工具
│
├── RoslynMethod.Analysis/               # 代码分析模块
│   ├── Skeletons/
│   │   └── ElementExtractionSkeleton.cs # 元素提取骨架
│   └── Analyzers/
│       ├── SyntaxElementExtractor.cs    # 语法元素提取器
│       ├── VariableAnalyzer.cs          # 变量分析器
│       └── ReferenceAnalyzer.cs         # 引用分析器
│
├── RoslynMethod.Clustering/             # 聚类分析模块
│   ├── Skeletons/
│   │   └── ClusteringSkeleton.cs        # 聚类分析骨架（原 SkeletonFlow.cs 优化）
│   ├── Analysis/
│   │   └── ClusterAnalyzer.cs           # 聚类分析器
│   ├── Distance/
│   │   └── DistanceCalculators.cs       # 距离计算器
│   ├── FeatureExtraction/
│   │   ├── CodeElementFeatureExtractor.cs
│   │   └── DistanceMatrixBuilder.cs
│   └── Models/
│       ├── AdvancedClusteringConfig.cs
│       ├── Cluster.cs
│       └── AnalysisResult.cs
│
├── RoslynMethod.CodeRemoval/            # 代码移除模块
│   ├── Skeletons/
│   │   └── CodeRemovalSkeleton.cs       # 代码移除骨架
│   └── Services/
│       └── CodeRemovalService.cs        # 底层移除服务
│
├── RoslynMethod.Generation/             # 代码生成模块
│   ├── Skeletons/
│   │   └── CodeGenerationSkeleton.cs    # 代码生成骨架
│   ├── Generators/
│   │   ├── RoslynGeneratorBase.cs
│   │   ├── StaticStringClassGenerator.cs
│   │   └── TemplateGenerator.cs
│   └── Models/
│       ├── CodeGenerationContext.cs
│       └── CodeGenerationResult.cs
│
├── RoslynMethod.Hierarchy/              # 层级分析模块
│   ├── Skeletons/
│   │   └── HierarchyAnalysisSkeleton.cs # 层级分析骨架
│   ├── Models/
│   │   ├── FunctionHierarchyInfo.cs
│   │   ├── CallGraphData.cs
│   │   └── LevelStatistics.cs
│   └── Visualization/
│       └── FunctionGraphVisualizer.cs
│
├── RoslynMethod.Merge/                  # 方法合并模块
│   ├── Skeletons/
│   │   └── MethodMergeSkeleton.cs       # 方法合并骨架
│   ├── Strategies/
│   │   ├── ConservativeMergeStrategy.cs
│   │   ├── AggressiveMergeStrategy.cs
│   │   ├── IntelligentMergeStrategy.cs
│   │   └── ThreeWayMergeStrategy.cs
│   └── Models/
│       ├── MethodMergeDetails.cs
│       └── StatementMerge.cs
│
├── RoslynMethod.Refactoring/            # 重构模块
│   ├── Skeletons/
│   │   └── RefactoringSkeleton.cs       # 代码重构骨架
│   ├── Rules/
│   │   ├── UseVarKeywordRule.cs
│   │   ├── SimplifyLinqRule.cs
│   │   └── AddNullChecksRule.cs
│   ├── Stores/
│   │   ├── StaticFieldStore.cs
│   │   ├── StaticTupleStore.cs
│   │   └── StringTupleStore.cs
│   └── Core/
│       ├── CodeRefactoringContext.cs
│       └── RoslynCacheManager.cs
│
└── RoslynMethod.Storage/                # 存储模块
    ├── Skeletons/
    │   └── CacheSkeleton.cs             # 缓存骨架
    └── Caches/
        ├── LRUCache.cs
        └── MemoryManager.cs
```

---

## 五、模块级重构方案

### 5.1 RoslynBasic.cs 拆分

**当前状态**：1127 行，包含 15+ 个类/工具模块，职责混杂。

**重构策略**：拆分为 3 个骨架 + 12 个工具类。

#### 5.1.1 AnalysisSkeleton - 代码分析骨架

```csharp
/// <summary>
/// 代码分析骨架 - 封装 Roslyn 代码分析的完整流程
/// </summary>
public static class AnalysisSkeleton
{
    private static readonly LoopEngine _loopEngine = new();

    /// <summary>
    /// 分析代码，提取增强的代码元素信息
    /// </summary>
    public static AnalysisResult Analyze(
        string code,
        IProgress<ComputationProgress> progress = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code cannot be null or empty", nameof(code));

        progress?.Report(new ComputationProgress(0, "Analysis: Step 1/4 - Parsing syntax tree..."));

        // Step 1: 解析语法树
        var (syntaxTree, semanticModel) = SyntaxParser.ParseCode(code);
        var root = syntaxTree.GetRoot();

        progress?.Report(new ComputationProgress(25, "Analysis: Step 2/4 - Extracting elements..."));

        // Step 2: 提取代码元素
        var elements = SyntaxElementExtractor.Extract(root, semanticModel);

        progress?.Report(new ComputationProgress(50, "Analysis: Step 3/4 - Analyzing variables..."));

        // Step 3: 变量分析
        var variableMap = VariableAnalyzer.AnalyzeVariables(root);
        VariableAnalyzer.UpdateElementVariables(elements, variableMap);
        VariableAnalyzer.CalculateVariableMetrics(elements);

        progress?.Report(new ComputationProgress(75, "Analysis: Step 4/4 - Analyzing references..."));

        // Step 4: 引用分析
        ReferenceAnalyzer.Analyze(elements, root);

        progress?.Report(new ComputationProgress(100, "Analysis: Complete"));

        return new AnalysisResult
        {
            Elements = elements,
            SyntaxTree = syntaxTree,
            SemanticModel = semanticModel
        };
    }

    /// <summary>
    /// 异步分析（支持取消）
    /// </summary>
    public static Task<AnalysisResult> AnalyzeAsync(
        string code,
        IProgress<ComputationProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Analyze(code, progress);
        }, cancellationToken);
    }
}
```

#### 5.1.2 QuerySkeleton - 语法查询骨架

```csharp
/// <summary>
/// 语法查询骨架 - 封装类型/成员查询的固定流程
/// </summary>
public static class QuerySkeleton
{
    /// <summary>
    /// 查询指定类型的语法节点
    /// </summary>
    public static QueryResult<T> Query<T>(
        SyntaxNode root,
        QueryCriteria criteria = null,
        IProgress<ComputationProgress> progress = null) where T : SyntaxNode
    {
        if (root == null)
            throw new ArgumentNullException(nameof(root));

        progress?.Report(new ComputationProgress(0, "Query: Searching..."));

        var result = criteria switch
        {
            { TypeName: not null } when typeof(T) == typeof(ClassDeclarationSyntax)
                => TypeQueryExecutor.FindClasses(root, criteria.TypeName, criteria.IncludeNested),
            { MemberName: not null } when typeof(T) == typeof(MethodDeclarationSyntax)
                => MemberQueryExecutor.FindMethods(root, criteria.TypeName, criteria.MemberName),
            // ... 其他类型
            _ => root.DescendantNodes().OfType<T>()
        };

        progress?.Report(new ComputationProgress(100, "Query: Complete"));

        return new QueryResult<T>
        {
            Items = result.Cast<T>().ToList(),
            TotalCount = result.Count()
        };
    }
}
```

#### 5.1.3 CodeTransformSkeleton - 代码变换骨架

```csharp
/// <summary>
/// 代码变换骨架 - 封装代码变换的固定流程
/// </summary>
public static class CodeTransformSkeleton
{
    /// <summary>
    /// 将方法变换为委托形式
    /// </summary>
    public static TransformResult TransformToDelegate(
        MethodDeclarationSyntax method,
        IProgress<ComputationProgress> progress = null)
    {
        if (method == null)
            throw new ArgumentNullException(nameof(method));

        progress?.Report(new ComputationProgress(0, "Transform: Creating delegate field..."));

        // Step 1: 创建委托字段
        var delegateField = DelegateFactory.CreateDelegateField(method);

        progress?.Report(new ComputationProgress(50, "Transform: Creating lambda body..."));

        // Step 2: 创建 Lambda 表达式体
        var isVoidMethod = method.ReturnType.IsVoid;
        var lambdaBody = DelegateFactory.CreateLambdaBody(method, isVoidMethod);

        progress?.Report(new ComputationProgress(100, "Transform: Complete"));

        return new TransformResult
        {
            DelegateField = delegateField,
            LambdaBody = lambdaBody
        };
    }
}
```

#### 5.1.4 工具类拆分清单

| 原位置 | 目标文件 | 职责 |
|--------|---------|------|
| `RoslynBasic.cs:26-40` | `Models/ElementType.cs` | 元素类型枚举 |
| `RoslynBasic.cs:45-80` | `Models/EnhancedCodeElement.cs` | 增强代码元素类 |
| `RoslynBasic.cs:85-113` | `Models/VariableUsageInfo.cs` | 变量使用信息 |
| `RoslynBasic.cs:118-124` | `Models/AccessibilityLevel.cs` | 可访问性级别枚举 |
| `RoslynBasic.cs:129-144` | `Models/LocationInfo.cs` | 位置信息类 |
| `RoslynBasic.cs:149-160` | `Utilities/CodeCompilation.cs` | 编译工厂 |
| `RoslynBasic.cs:167-189` | `Skeletons/AnalysisSkeleton.cs` | 分析骨架 |
| `RoslynBasic.cs:193-292` | `Models/AnalysisContext.cs` | 分析上下文 |
| `RoslynBasic.cs:297-340` | `Skeletons/QuerySkeleton.cs` | 查询骨架（类型查询部分） |
| `RoslynBasic.cs:343-387` | `Skeletons/QuerySkeleton.cs` | 查询骨架（成员查询部分） |
| `RoslynBasic.cs:390-411` | `Utilities/SyntaxInfoTool.cs` | 语法信息提取工具 |
| `RoslynBasic.cs:414-432` | `Skeletons/CodeTransformSkeleton.cs` | 代码变换骨架 |
| `RoslynBasic.cs:435-461` | `Utilities/SyntaxTreeHelper.cs` | 语法树辅助工具 |
| `RoslynBasic.cs:464-497` | `Utilities/TypeQueryExecutor.cs` | 类型查询执行器 |
| `RoslynBasic.cs:499-579` | `Utilities/MemberQueryExecutor.cs` | 成员查询执行器 |
| `RoslynBasic.cs:581-697` | `Utilities/SymbolAnalyzer.cs` | 符号分析工具 |
| `RoslynBasic.cs:699-774` | `Utilities/SyntaxAccessor.cs` | 语法访问工具 |
| `RoslynBasic.cs:776-787` | `Utilities/SyntaxLocator.cs` | 语法定位工具 |
| `RoslynBasic.cs:789-803` | `Utilities/SyntaxMatcher.cs` | 语法匹配工具 |
| `RoslynBasic.cs:805-818` | `Utilities/SyntaxParser.cs` | 语法解析工具 |
| `RoslynBasic.cs:820-824` | `Utilities/IdentifierFactory.cs` | 标识符工厂 |
| `RoslynBasic.cs:826-835` | `Utilities/AccessibilityConverter.cs` | 可访问性转换 |
| `RoslynBasic.cs:837-882` | `Utilities/StatementFactory.cs` | 语句工厂 |
| `RoslynBasic.cs:884-933` | `Utilities/DelegateFactory.cs` | 委托工厂 |
| `RoslynBasic.cs:935-965` | `Utilities/StatementTransformer.cs` | 语句变换器 |
| `RoslynBasic.cs:967-1000` | `Utilities/Guard.cs` | 参数验证工具 |

---

### 5.2 MethodMergeEngine 重构

**当前状态**：854 行，包含多种合并策略，缺少骨架抽象。

**重构策略**：提取 `MethodMergeSkeleton`，合并策略通过委托注入。

#### 5.2.1 MethodMergeSkeleton

```csharp
/// <summary>
/// 方法合并骨架 - 封装方法合并的固定流程
/// </summary>
public static class MethodMergeSkeleton
{
    /// <summary>
    /// 合并两个语法树中的方法
    /// </summary>
    public static EnhancedMergeResult Merge(
        SyntaxTree originalTree,
        SyntaxTree modifiedTree,
        MergeStrategy strategy = MergeStrategy.Intelligent,
        IProgress<ComputationProgress> progress = null)
    {
        if (originalTree == null) throw new ArgumentNullException(nameof(originalTree));
        if (modifiedTree == null) throw new ArgumentNullException(nameof(modifiedTree));

        progress?.Report(new ComputationProgress(0, "Merge: Step 1/4 - Analyzing methods..."));

        // Step 1: 分析方法分组
        var originalMethods = GroupMethodsByClass(originalTree.GetRoot());
        var modifiedMethods = GroupMethodsByClass(modifiedTree.GetRoot());

        progress?.Report(new ComputationProgress(25, "Merge: Step 2/4 - Matching methods..."));

        // Step 2: 方法匹配
        var matchedPairs = MatchMethods(originalMethods, modifiedMethods);

        progress?.Report(new ComputationProgress(50, "Merge: Step 3/4 - Merging methods..."));

        // Step 3: 执行合并（策略通过委托注入）
        var mergeStrategy = CreateMergeStrategy(strategy);
        var mergeResults = matchedPairs.Select(pair =>
            mergeStrategy(pair.Original, pair.Modified)).ToList();

        progress?.Report(new ComputationProgress(75, "Merge: Step 4/4 - Building result..."));

        // Step 4: 构建最终代码
        var result = BuildFinalCode(originalTree, modifiedTree, mergeResults);

        progress?.Report(new ComputationProgress(100, "Merge: Complete"));

        return result;
    }

    /// <summary>
    /// 异步合并（支持取消）
    /// </summary>
    public static Task<EnhancedMergeResult> MergeAsync(
        SyntaxTree originalTree,
        SyntaxTree modifiedTree,
        MergeStrategy strategy = MergeStrategy.Intelligent,
        IProgress<ComputationProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Merge(originalTree, modifiedTree, strategy, progress);
        }, cancellationToken);
    }

    // 合并策略委托
    private delegate (List<StatementSyntax> merged, List<string> conflicts) MergeStrategyDelegate(
        List<StatementSyntax> original, List<StatementSyntax> modified);

    private static MergeStrategyDelegate CreateMergeStrategy(MergeStrategy strategy) => strategy switch
    {
        MergeStrategy.Conservative => EnhancedMethodMergeEngine.MergeConservative,
        MergeStrategy.Aggressive => EnhancedMethodMergeEngine.MergeAggressive,
        MergeStrategy.Intelligent => EnhancedMethodMergeEngine.MergeWithIntelligentAnalysis,
        MergeStrategy.ThreeWay => EnhancedMethodMergeEngine.MergeThreeWay,
        _ => EnhancedMethodMergeEngine.MergeWithIntelligentAnalysis
    };
}
```

#### 5.2.2 合并策略独立文件

每个合并策略提取为独立文件：

| 文件 | 内容 |
|------|------|
| `Strategies/ConservativeMergeStrategy.cs` | 保守合并策略实现 |
| `Strategies/AggressiveMergeStrategy.cs` | 激进合并策略实现 |
| `Strategies/IntelligentMergeStrategy.cs` | 智能合并策略实现 |
| `Strategies/ThreeWayMergeStrategy.cs` | 三路合并策略实现 |

---

### 5.3 SyntaxHierarchy 重构

**当前状态**：638 行，函数层级分析流程固定，可提取骨架。

#### 5.3.1 HierarchyAnalysisSkeleton

```csharp
/// <summary>
/// 层级分析骨架 - 封装函数调用层级分析的固定流程
/// </summary>
public static class HierarchyAnalysisSkeleton
{
    /// <summary>
    /// 分析函数层级关系
    /// </summary>
    public static List<FunctionHierarchyInfo> Analyze(
        AnalysisContext context,
        string className,
        IProgress<ComputationProgress> progress = null)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (string.IsNullOrWhiteSpace(className))
            throw new ArgumentException("Class name cannot be null or empty", nameof(className));

        progress?.Report(new ComputationProgress(0, "Hierarchy: Step 1/4 - Retrieving methods..."));

        // Step 1: 获取方法列表
        var methods = MemberQueryTool.GetMethods(context, className).ToList();
        if (methods.Count == 0)
            throw new ArgumentException($"Class '{className}' has no methods");

        progress?.Report(new ComputationProgress(25, "Hierarchy: Step 2/4 - Building call graph..."));

        // Step 2: 构建调用关系图
        var classNode = TypeQueryTool.GetClasses(context, className).FirstOrDefault();
        if (classNode == null)
            throw new ArgumentException($"Class '{className}' not found");
        var callGraph = BuildCallGraph(context, classNode, methods);

        progress?.Report(new ComputationProgress(50, "Hierarchy: Step 3/4 - Calculating levels..."));

        // Step 3: 计算层级
        var hierarchy = CalculateFunctionHierarchy(context, methods, callGraph);

        progress?.Report(new ComputationProgress(75, "Hierarchy: Step 4/4 - Analyzing statistics..."));

        // Step 4: 统计信息
        var statistics = FunctionHierarchyAnalyzer.GetLevelStatistics(hierarchy);

        progress?.Report(new ComputationProgress(100, "Hierarchy: Complete"));

        return hierarchy;
    }

    /// <summary>
    /// 异步分析（支持取消）
    /// </summary>
    public static Task<List<FunctionHierarchyInfo>> AnalyzeAsync(
        AnalysisContext context,
        string className,
        IProgress<ComputationProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Analyze(context, className, progress);
        }, cancellationToken);
    }
}
```

---

### 5.4 RefactoringFramework 重构

**当前状态**：271 行，重构规则应用流程固定。

#### 5.4.1 RefactoringSkeleton

```csharp
/// <summary>
/// 代码重构骨架 - 封装重构规则应用的固定流程
/// </summary>
public static class RefactoringSkeleton
{
    /// <summary>
    /// 执行代码重构
    /// </summary>
    public static RefactoringResult Refactor(
        string sourceCode,
        List<RefactoringRule> rules,
        IProgress<ComputationProgress> progress = null)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            throw new ArgumentException("Source code cannot be null or empty", nameof(sourceCode));
        if (rules == null || rules.Count == 0)
            return RefactoringResult.SuccessResult(sourceCode, new List<string>());

        progress?.Report(new ComputationProgress(0, "Refactor: Step 1/3 - Parsing code..."));

        // Step 1: 解析代码
        var (syntaxTree, compilation, semanticModel) = _cacheManager.GetOrCreateAnalysis(sourceCode);

        progress?.Report(new ComputationProgress(30, "Refactor: Step 2/3 - Applying rules..."));

        // Step 2: 应用规则
        var applicableRules = rules
            .Where(r => r.ShouldApply(syntaxTree.GetRoot(), semanticModel))
            .OrderBy(r => r.Priority)
            .ToList();

        var transformedRoot = syntaxTree.GetRoot();
        foreach (var rule in applicableRules)
        {
            transformedRoot = rule.Transform(transformedRoot, semanticModel);
        }

        progress?.Report(new ComputationProgress(80, "Refactor: Step 3/3 - Formatting result..."));

        // Step 3: 格式化结果
        var result = RefactoringResult.SuccessResult(
            transformedRoot.ToFullString(),
            applicableRules.Select(r => r.Name).ToList());

        progress?.Report(new ComputationProgress(100, "Refactor: Complete"));

        return result;
    }

    /// <summary>
    /// 异步重构（支持取消）
    /// </summary>
    public static Task<RefactoringResult> RefactorAsync(
        string sourceCode,
        List<RefactoringRule> rules,
        IProgress<ComputationProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Refactor(sourceCode, rules, progress);
        }, cancellationToken);
    }
}
```

---

### 5.5 CodeRemovalService 重构

**当前状态**：234 行，移除操作流程固定。

#### 5.5.1 CodeRemovalSkeleton

```csharp
/// <summary>
/// 代码移除骨架 - 封装代码移除的固定流程
/// </summary>
public static class CodeRemovalSkeleton
{
    /// <summary>
    /// 从代码中移除指定元素
    /// </summary>
    public static string Remove(
        string code,
        RemovalCriteria criteria,
        IProgress<ComputationProgress> progress = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code cannot be null or empty", nameof(code));
        if (criteria == null)
            throw new ArgumentNullException(nameof(criteria));

        progress?.Report(new ComputationProgress(0, "Removal: Step 1/3 - Parsing code..."));

        // Step 1: 解析代码
        var context = AnalysisContext.Create(code);

        progress?.Report(new ComputationProgress(30, "Removal: Step 2/3 - Finding targets..."));

        // Step 2: 查找目标节点
        var nodesToRemove = FindTargetNodes(context, criteria);

        if (!nodesToRemove.Any())
            return code;

        progress?.Report(new ComputationProgress(60, "Removal: Step 3/3 - Removing nodes..."));

        // Step 3: 执行移除
        var newRoot = CodeRemovalService.RemoveNodes(context.Root, nodesToRemove);
        var result = newRoot.NormalizeWhitespace().ToFullString();

        progress?.Report(new ComputationProgress(100, "Removal: Complete"));

        return result;
    }

    /// <summary>
    /// 异步移除（支持取消）
    /// </summary>
    public static Task<string> RemoveAsync(
        string code,
        RemovalCriteria criteria,
        IProgress<ComputationProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Remove(code, criteria, progress);
        }, cancellationToken);
    }
}
```

---

### 5.6 StringCodeGeneration 重构

**当前状态**：429 行，代码生成流程固定。

#### 5.6.1 CodeGenerationSkeleton

```csharp
/// <summary>
/// 代码生成骨架 - 封装代码生成的固定流程
/// </summary>
public static class CodeGenerationSkeleton
{
    /// <summary>
    /// 生成代码
    /// </summary>
    public static CodeGenerationResult Generate<TModel>(
        TModel model,
        ICodeGenerator<TModel> generator,
        CodeGenerationContext context,
        IProgress<ComputationProgress> progress = null)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));
        if (generator == null) throw new ArgumentNullException(nameof(generator));

        progress?.Report(new ComputationProgress(0, "Generation: Starting..."));

        // Step 1: 执行生成
        var result = generator.GenerateAsync(model, context)
            .GetAwaiter().GetResult();

        progress?.Report(new ComputationProgress(50, "Generation: Validating..."));

        // Step 2: 验证结果
        if (result.Success && result.SyntaxTree != null)
        {
            var diagnostics = result.SyntaxTree.GetRoot().GetDiagnostics();
            foreach (var diag in diagnostics)
                result.Diagnostics.Add(diag);
        }

        progress?.Report(new ComputationProgress(100, "Generation: Complete"));

        return result;
    }

    /// <summary>
    /// 异步生成（支持取消）
    /// </summary>
    public static async Task<CodeGenerationResult> GenerateAsync<TModel>(
        TModel model,
        ICodeGenerator<TModel> generator,
        CodeGenerationContext context,
        IProgress<ComputationProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));
        if (generator == null) throw new ArgumentNullException(nameof(generator));

        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report(new ComputationProgress(0, "Generation: Starting..."));

        var result = await generator.GenerateAsync(model, context);

        progress?.Report(new ComputationProgress(100, "Generation: Complete"));

        return result;
    }
}
```

---

### 5.7 StaticStore 重构

**当前状态**：531 行，静态字段/元组存储操作可提取骨架。

#### 5.7.1 StaticStoreSkeleton

```csharp
/// <summary>
/// 静态存储骨架 - 封装静态字段读写的固定流程
/// </summary>
public static class StaticStoreSkeleton
{
    /// <summary>
    /// 从代码中读取静态字段值
    /// </summary>
    public static T Read<T>(
        string code,
        string className,
        string fieldName,
        IProgress<ComputationProgress> progress = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code cannot be null or empty", nameof(code));

        progress?.Report(new ComputationProgress(0, "Store: Step 1/3 - Parsing code..."));

        // Step 1: 解析代码
        var root = CSharpSyntaxTree.ParseText(code).GetRoot();

        progress?.Report(new ComputationProgress(33, "Store: Step 2/3 - Finding field..."));

        // Step 2: 查找字段
        var field = root.DescendantNodes()
            .OfType<FieldDeclarationSyntax>()
            .FirstOrDefault(f =>
                f.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                f.Declaration.Variables.Any(v => v.Identifier.Text == fieldName));

        if (field == null)
            throw new InvalidOperationException($"Static field '{fieldName}' not found in class '{className}'");

        progress?.Report(new ComputationProgress(66, "Store: Step 3/3 - Extracting value..."));

        // Step 3: 提取值
        var variable = field.Declaration.Variables.First();
        var value = variable.Initializer?.Value?.ToString();

        progress?.Report(new ComputationProgress(100, "Store: Complete"));

        return (T)Convert.ChangeType(value, typeof(T));
    }

    /// <summary>
    /// 异步读取（支持取消）
    /// </summary>
    public static Task<T> ReadAsync<T>(
        string code,
        string className,
        string fieldName,
        IProgress<ComputationProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Read<T>(code, className, fieldName, progress);
        }, cancellationToken);
    }
}
```

---

### 5.8 StorageFramework 重构

**当前状态**：327 行，LRU 缓存 + 内存管理。

#### 5.8.1 CacheSkeleton

```csharp
/// <summary>
/// 缓存骨架 - 封装缓存操作的固定流程
/// </summary>
public static class CacheSkeleton
{
    /// <summary>
    /// 使用 LRU 缓存获取或创建对象
    /// </summary>
    public static T GetOrCreate<T>(
        string key,
        Func<T> factory,
        CacheConfig config = null,
        IProgress<ComputationProgress> progress = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));

        config ??= CacheConfig.Default;

        progress?.Report(new ComputationProgress(0, "Cache: Checking..."));

        // Step 1: 检查缓存
        if (LRUCache.TryGet(key, out T cached))
        {
            progress?.Report(new ComputationProgress(100, "Cache: Hit"));
            return cached;
        }

        progress?.Report(new ComputationProgress(50, "Cache: Creating..."));

        // Step 2: 创建对象
        var result = factory();

        // Step 3: 存入缓存
        LRUCache.Set(key, result, config.SlidingExpiration);

        progress?.Report(new ComputationProgress(100, "Cache: Miss - Created"));

        return result;
    }

    /// <summary>
    /// 异步获取或创建（支持取消）
    /// </summary>
    public static async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<Task<T>> factoryAsync,
        CacheConfig config = null,
        IProgress<ComputationProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));
        if (factoryAsync == null)
            throw new ArgumentNullException(nameof(factoryAsync));

        cancellationToken.ThrowIfCancellationRequested();

        config ??= CacheConfig.Default;

        progress?.Report(new ComputationProgress(0, "Cache: Checking..."));

        if (LRUCache.TryGet(key, out T cached))
        {
            progress?.Report(new ComputationProgress(100, "Cache: Hit"));
            return cached;
        }

        progress?.Report(new ComputationProgress(50, "Cache: Creating..."));

        var result = await factoryAsync();

        LRUCache.Set(key, result, config.SlidingExpiration);

        progress?.Report(new ComputationProgress(100, "Cache: Miss - Created"));

        return result;
    }
}
```

---

### 5.9 Clustering.SkeletonFlow 优化

**当前状态**：`SkeletonFlow.cs` 254 行，已部分 SkeletonFlow 化，但命名和风格与 Agent 项目不一致。

**优化方向**：

| 当前 | 目标 |
|------|------|
| `class SkeletonFlow` | `static class ClusteringSkeleton` |
| `AnalyzeAsync` 实例方法 | `ComputeAsync` 静态方法 |
| `QuickAnalyzeAsync` 实例方法 | `QuickComputeAsync` 静态方法 |
| 内部步骤方法 | 保持内部实现，通过骨架编排 |

#### 5.9.1 ClusteringSkeleton

```csharp
/// <summary>
/// 聚类分析骨架 - 封装代码元素聚类分析的完整流程
/// </summary>
public static class ClusteringSkeleton
{
    /// <summary>
    /// 执行完整的聚类分析
    /// </summary>
    public static async Task<AnalysisResult> ComputeAsync(
        string code,
        AdvancedClusteringConfig config = null,
        IProgress<ComputationProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code cannot be null or empty", nameof(code));

        config ??= new AdvancedClusteringConfig();

        progress?.Report(new ComputationProgress(0, "Clustering: Step 1/5 - Extracting features..."));

        // Step 1: 特征提取
        var extractor = new CodeElementFeatureExtractor();
        var elements = extractor.ExtractFeatures(code);

        progress?.Report(new ComputationProgress(20, "Clustering: Step 2/5 - Building distance matrix..."));

        // Step 2: 距离矩阵
        var matrixBuilder = new DistanceMatrixBuilder();
        var distanceMatrix = matrixBuilder.BuildDistanceMatrix(elements);

        progress?.Report(new ComputationProgress(40, "Clustering: Step 3/5 - Running clustering algorithm..."));

        // Step 3: 执行聚类
        var algorithm = CreateAlgorithm(config);
        var clusters = await algorithm.AnalyzeAsync(elements, distanceMatrix, config, progress, cancellationToken);

        progress?.Report(new ComputationProgress(70, "Clustering: Step 4/5 - Post-processing..."));

        // Step 4: 后处理
        var analyzer = new ClusterAnalyzer();
        analyzer.AnalyzeClusters(clusters);

        progress?.Report(new ComputationProgress(90, "Clustering: Step 5/5 - Aggregating results..."));

        // Step 5: 聚合结果
        var result = AggregateResults(elements, clusters, config);

        progress?.Report(new ComputationProgress(100, "Clustering: Complete"));

        return result;
    }

    /// <summary>
    /// 快速聚类分析（简化流程）
    /// </summary>
    public static async Task<AnalysisResult> QuickComputeAsync(
        string code,
        AdvancedClusteringConfig config = null,
        IProgress<ComputationProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        // 快速版本：跳过距离矩阵构建，使用默认配置
        config ??= new AdvancedClusteringConfig();
        config.EnableParallel = false;
        config.MaxDegreeOfParallelism = 1;

        return await ComputeAsync(code, config, progress, cancellationToken);
    }
}
```

---

## 六、文件目录结构规划

### 6.1 重构前后对比

| 模块 | 重构前文件数 | 重构后文件数 | 变化 |
|------|-------------|-------------|------|
| RoslynMethod.Core | 2 | 18 | +16（拆分 RoslynBasic.cs） |
| RoslynMethod.Analysis | 1 | 4 | +3（提取骨架+分析器） |
| RoslynMethod.Clustering | 7 | 7 | 0（优化 SkeletonFlow） |
| RoslynMethod.CodeRemoval | 1 | 2 | +1（添加骨架） |
| RoslynMethod.Generation | 1 | 5 | +4（提取骨架+模型） |
| RoslynMethod.Hierarchy | 1 | 4 | +3（提取骨架+模型） |
| RoslynMethod.Merge | 1 | 7 | +6（提取骨架+策略） |
| RoslynMethod.Refactoring | 2 | 9 | +7（提取骨架+规则+存储） |
| RoslynMethod.Storage | 1 | 3 | +2（提取骨架+缓存） |
| **总计** | **17** | **59** | **+42** |

### 6.2 文件命名规范

```
RoslynMethod.{Module}/
├── Skeletons/
│   └── {Module}Skeleton.cs          # 骨架类
├── Models/
│   └── {ModelName}.cs               # 数据模型
├── Utilities/                        # Core 模块专用
│   └── {UtilityName}.cs             # 工具类
├── Strategies/                       # Merge 模块专用
│   └── {StrategyName}Strategy.cs    # 策略实现
├── Rules/                            # Refactoring 模块专用
│   └── {RuleName}Rule.cs            # 规则实现
├── Stores/                           # Refactoring 模块专用
│   └── {StoreName}Store.cs          # 存储实现
├── Caches/                           # Storage 模块专用
│   └── {CacheName}.cs               # 缓存实现
├── Generators/                       # Generation 模块专用
│   └── {GeneratorName}.cs           # 生成器实现
├── Analyzers/                        # Analysis 模块专用
│   └── {AnalyzerName}.cs            # 分析器实现
└── Services/                         # CodeRemoval 模块专用
    └── {ServiceName}.cs             # 服务实现
```

---

## 七、重构优先级与实施路线图

### 7.1 优先级矩阵

| 优先级 | 模块 | 工作量 | 影响范围 | 依赖 |
|--------|------|--------|---------|------|
| **P0** | RoslynMethod.Core | ⭐⭐⭐⭐⭐ | 所有模块 | 无 |
| **P1** | RoslynMethod.Merge | ⭐⭐⭐⭐ | Merge 模块 | Core |
| **P1** | RoslynMethod.Hierarchy | ⭐⭐⭐ | Hierarchy 模块 | Core |
| **P2** | RoslynMethod.Refactoring | ⭐⭐⭐ | Refactoring 模块 | Core |
| **P2** | RoslynMethod.CodeRemoval | ⭐⭐ | CodeRemoval 模块 | Core |
| **P2** | RoslynMethod.Clustering | ⭐⭐ | Clustering 模块 | 无 |
| **P3** | RoslynMethod.Generation | ⭐⭐⭐ | Generation 模块 | Core |
| **P3** | RoslynMethod.Storage | ⭐⭐ | Storage 模块 | 无 |
| **P3** | RoslynMethod.Analysis | ⭐⭐ | Analysis 模块 | Core |

### 7.2 分阶段实施计划

#### 第一阶段（P0）- Core 模块拆分

**目标**：将 `RoslynBasic.cs` 拆分为骨架 + 模型 + 工具类

**步骤**：
1. 创建 `Models/` 目录，提取数据模型（ElementType, EnhancedCodeElement, VariableUsageInfo, AccessibilityLevel, LocationInfo, AnalysisContext）
2. 创建 `Utilities/` 目录，提取工具类（SyntaxAccessor, SyntaxLocator, SyntaxMatcher, SyntaxParser, IdentifierFactory, AccessibilityConverter, StatementFactory, DelegateFactory, StatementTransformer, SymbolAnalyzer, TypeQueryExecutor, MemberQueryExecutor, Guard）
3. 创建 `Skeletons/` 目录，创建骨架类（AnalysisSkeleton, QuerySkeleton, CodeTransformSkeleton）
4. 保留 `RoslynBasic.cs` 作为向后兼容的包装（委托给新文件）

**预计耗时**：2-3 天

#### 第二阶段（P1）- Merge + Hierarchy 模块重构

**目标**：为 MethodMergeEngine 和 SyntaxHierarchy 添加骨架层

**步骤**：
1. 创建 `RoslynMethod.Merge/Skeletons/MethodMergeSkeleton.cs`
2. 将合并策略提取为独立文件
3. 创建 `RoslynMethod.Hierarchy/Skeletons/HierarchyAnalysisSkeleton.cs`
4. 创建 `RoslynMethod.Hierarchy/Models/` 数据模型

**预计耗时**：1-2 天

#### 第三阶段（P2）- Refactoring + CodeRemoval + Clustering 重构

**目标**：为 RefactoringFramework、CodeRemovalService 添加骨架，优化 Clustering.SkeletonFlow

**步骤**：
1. 创建 `RoslynMethod.Refactoring/Skeletons/RefactoringSkeleton.cs`
2. 将重构规则提取为独立文件
3. 创建 `RoslynMethod.CodeRemoval/Skeletons/CodeRemovalSkeleton.cs`
4. 优化 `RoslynMethod.Clustering/SkeletonFlow.cs` → `ClusteringSkeleton.cs`

**预计耗时**：1-2 天

#### 第四阶段（P3）- Generation + Storage + Analysis 重构

**目标**：为 StringCodeGeneration、StorageFramework、Analysis.Basic 添加骨架

**步骤**：
1. 创建 `RoslynMethod.Generation/Skeletons/CodeGenerationSkeleton.cs`
2. 创建 `RoslynMethod.Storage/Skeletons/CacheSkeleton.cs`
3. 创建 `RoslynMethod.Analysis/Skeletons/ElementExtractionSkeleton.cs`

**预计耗时**：1 天

### 7.3 总体时间线

```
Week 1: 第一阶段（P0）- Core 模块拆分
Week 2: 第二阶段（P1）- Merge + Hierarchy 重构
Week 3: 第三阶段（P2）- Refactoring + CodeRemoval + Clustering
Week 4: 第四阶段（P3）- Generation + Storage + Analysis
         + 集成测试 + 文档更新
```

---

## 八、Skeleton 通用模板

### 8.1 标准骨架模板

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RoslynMethod.{Module}.Skeletons
{
    /// <summary>
    /// {功能描述}骨架 - 封装{功能}的固定流程
    /// </summary>
    public static class {Name}Skeleton
    {
        // 可选：复用 LoopEngine
        // private static readonly LoopEngine _loopEngine = new();

        /// <summary>
        /// 同步执行{功能}
        /// </summary>
        public static {ResultType} Compute(
            {InputType} input,
            /* 其他参数 */,
            IProgress<ComputationProgress> progress = null)
        {
            // 1. 参数验证
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            // 2. 分步执行
            progress?.Report(new ComputationProgress(0, "Step 1/{n} - ..."));
            var step1Result = Step1(input);

            progress?.Report(new ComputationProgress(33, "Step 2/{n} - ..."));
            var step2Result = Step2(step1Result);

            progress?.Report(new ComputationProgress(66, "Step 3/{n} - ..."));
            var finalResult = Step3(step2Result);

            progress?.Report(new ComputationProgress(100, "Complete"));
            return finalResult;
        }

        /// <summary>
        /// 异步执行{功能}（支持取消）
        /// </summary>
        public static Task<{ResultType}> ComputeAsync(
            {InputType} input,
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

        // 内部步骤方法
        private static {Step1Result} Step1({InputType} input) { /* ... */ }
        private static {Step2Result} Step2({Step1Result} input) { /* ... */ }
        private static {ResultType} Step3({Step2Result} input) { /* ... */ }
    }
}
```

### 8.2 进度报告规范

| 进度值 | 阶段 | 消息格式 |
|--------|------|---------|
| 0% | 开始 | `"{Module}: Starting..."` |
| 25% | 步骤1 | `"{Module}: Step 1/{n} - {description}"` |
| 50% | 步骤2 | `"{Module}: Step 2/{n} - {description}"` |
| 75% | 步骤3 | `"{Module}: Step 3/{n} - {description}"` |
| 100% | 完成 | `"{Module}: Complete"` |

### 8.3 异常处理规范

```csharp
// 参数验证异常
if (input == null) throw new ArgumentNullException(nameof(input));
if (string.IsNullOrWhiteSpace(code))
    throw new ArgumentException("Code cannot be null or empty", nameof(code));

// 业务逻辑异常
if (methods.Count == 0)
    throw new ArgumentException($"Class '{className}' has no methods");

// 取消异常（异步版本）
cancellationToken.ThrowIfCancellationRequested();
```

---

## 九、兼容性与测试策略

### 9.1 向后兼容策略

1. **保留原始 API**：重构后，原始类和方法保留为 `[Obsolete]` 包装，委托给新的骨架实现
2. **逐步弃用**：标记旧 API 为 `[Obsolete("Use XxxSkeleton.Compute instead")]`
3. **命名空间兼容**：新类放在新命名空间，旧命名空间保留引用

```csharp
// 旧 API 保留为 Obsolete 包装
[Obsolete("Use AnalysisSkeleton.Analyze instead")]
public static List<EnhancedCodeElement> AnalyzeCode(string code)
{
    return AnalysisSkeleton.Analyze(code).Elements;
}
```

### 9.2 测试策略

| 测试类型 | 覆盖范围 | 方法 |
|---------|---------|------|
| **单元测试** | 每个工具类 | 独立测试工具函数 |
| **骨架测试** | 每个骨架 | 测试完整流程 + 进度报告 + 取消 |
| **集成测试** | 模块间调用 | 测试骨架调用工具类的正确性 |
| **回归测试** | 旧 API | 确保旧 API 输出与重构前一致 |

### 9.3 测试用例模板

```csharp
[TestClass]
public class {Name}SkeletonTests
{
    [TestMethod]
    public void Compute_ValidInput_ReturnsExpectedResult()
    {
        // Arrange
        var input = ...;

        // Act
        var result = {Name}Skeleton.Compute(input);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void Compute_WithProgress_ReportsCorrectly()
    {
        // Arrange
        var progress = new Progress<ComputationProgress>();
        var reports = new List<ComputationProgress>();
        progress.ProgressChanged += (_, p) => reports.Add(p);

        // Act
        {Name}Skeleton.Compute(input, progress);

        // Assert
        Assert.IsTrue(reports.Count >= 3);
        Assert.AreEqual(100, reports.Last().Percentage);
    }

    [TestMethod]
    [ExpectedException(typeof(OperationCanceledException))]
    public async Task ComputeAsync_WithCancellation_Throws()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        await {Name}Skeleton.ComputeAsync(input, cancellationToken: cts.Token);
    }
}
```

---

## 附录

### A. 参考文档

- [SkeletonFlow 使用文档](./SkeletonFlow使用文档.md) - SkeletonFlow 架构详细说明
- [Agent_SkeletonFlow重构分析](./Agent_SkeletonFlow重构分析.md) - Agent 项目 SkeletonFlow 重构经验
- [RoslynMethod重复逻辑与冗余逻辑分析报告](./RoslynMethod重复逻辑与冗余逻辑分析报告.md) - 当前代码问题分析

### B. 术语表

| 术语 | 说明 |
|------|------|
| **Skeleton** | 骨架类，封装固定计算流程 |
| **LoopEngine** | 循环引擎，提供高性能并行循环执行 |
| **ComputationProgress** | 进度报告数据结构 |
| **CancellationToken** | .NET 取消令牌 |
| **关注点分离** | 骨架层控制流程，业务层关注逻辑 |
| **渐进式重构** | 按优先级分批实施，每批保持向后兼容 |

### C. 变更日志

| 版本 | 日期 | 变更内容 |
|------|------|---------|
| v1.0 | 2026-05-25 | 初始版本，完成全部模块重构方案设计 |
