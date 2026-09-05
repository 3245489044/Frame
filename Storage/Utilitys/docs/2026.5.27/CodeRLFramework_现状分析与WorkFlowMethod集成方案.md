# CodeRLFramework 现状分析与 WorkFlowMethod 集成方案

> 基于"逻辑的引用骨架：大模型作为隐式语义关系引擎"理论框架，
> 分析 CodeRLFramework 当前进度，并引入 WorkFlowMethod 简化集成流程。

---

## 目录

- [一、理论基础：逻辑的引用骨架](#一理论基础逻辑的引用骨架)
- [二、CodeRLFramework 当前架构分析](#二coderlframework-当前架构分析)
- [三、GraphFoundation 核心能力回顾](#三graphfoundation-核心能力回顾)
- [四、WorkFlowMethod 架构详解](#四workflowmethod-架构详解)
- [五、集成方案：CodeRLFramework + WorkFlowMethod](#五集成方案coderlframework--workflowmethod)
- [六、当前进度评估（2026.5.27 更新）](#六当前进度评估2026527-更新)
- [七、分阶段实施计划](#七分阶段实施计划)
- [八、架构对比总结](#八架构对比总结)
- [九、代码量化与预期收益](#九代码量化与预期收益)
- [附录：理论践行度分析](#附录理论践行度分析)

---

## 一、理论基础：逻辑的引用骨架

### 1.1 核心思想

"逻辑的引用骨架"理论认为：**代码的本质不是文本序列，而是由引用关系构成的图结构**。

```
传统视角：代码 = 文本序列（Token 流）
                ↓
引用骨架视角：代码 = 引用图（节点 + 有向边）
                ↓
大模型角色：从"黑箱生成器"转变为"显式引用图上的导航引擎"
```

### 1.2 引用骨架的三要素

| 要素 | 描述 | 在代码中的体现 |
|------|------|---------------|
| **节点** | 逻辑单元 | 函数、语句、表达式、变量 |
| **有向边** | 引用关系 | 调用关系、数据流、控制流、继承关系 |
| **符号标签** | 语义标识 | 函数名、变量名、类型名 |

### 1.3 理想架构

```
输入代码库
    │
    ▼
引用骨架图（CoreGraph：节点=函数，边=调用/数据流）
    │
    ├── 显式引用导航（Placeholder：@input / @prev / @node）
    │
    ├── 可组合执行管线（SkeletonFlow：Then / Branch / Loop / Parallel）
    │
    └── 基于图结构的 RL 训练（结构奖励 + 路径优化）
    │
    ▼
生成代码
```

### 1.4 大模型的核心身份：隐式语义关系引擎

理论的核心主张：

> **大语言模型是一个隐式处理语义引用的关系引擎。**
> 它的输入是线性符号序列；它在内部的核心计算，是将这些符号转化为数据节点，
> 并在上下文内动态生成它们之间的软引用关系网络；它的训练目标，是最大化这些
> 隐式引用结构与训练数据中实际语义依赖的一致性；它的推理过程，在计算效果上
> 等同于沿此隐式引用网络进行信息传递与聚合；它的输出，则是将聚合得到的结果
> 节点信息解码为线性符号序列。

这一身份定位决定了集成方案的核心架构原则：
1. **大模型应承担引用导航的核心职责**——RL 策略网络应作为大模型导航能力的辅助/优化层，而非替代层
2. **隐式引用 → 显式引用的映射**——需要将大模型内部的软引用模式映射为显式的 CoreGraph 结构
3. **RL 的角色是训练工具**——RL 的目标是"让大模型更好地在引用图上导航"，而非"让 RL 策略网络在引用图上导航"

---

## 二、CodeRLFramework 当前架构分析

### 2.1 项目结构（2026.5.27 更新）

```
CodeRLFramework/
├── CodeRLFramework.csproj          ← 已添加 WorkFlowMethod 引用
├── Generation/                     ← 代码生成
│   ├── CodeGenerationAgent.cs      ← Transformer 生成（复用 Agent 框架）
│   └── VectorToCodeConverter.cs    ← 向量→代码转换
├── Input/                          ← 输入处理
│   └── CodeTokenizer.cs            ← 函数名→Token ID
├── Output/                         ← 输出处理
├── RL/                             ← 强化学习
│   ├── PolicyGradient.cs           ← 策略梯度算法
│   ├── RewardFunction.cs           ← 奖励函数
│   └── RLTrainer.cs                ← [已标记 Obsolete] 旧版训练器
├── Pipeline/                       ← ★ 新增：Skeleton 化管线
│   ├── CodeRLContext.cs            ← 统一上下文（包装 WorkflowContext）
│   ├── CodeRLPipeline.cs           ← 主入口（训练循环 + 收敛检查）
│   ├── CodeRLPipelineFactory.cs    ← 管道工厂（依赖注入 + Skeleton 创建）
│   ├── CodeRLAdapterSkeleton.cs    ← WorkflowContext 适配器
│   └── Skeletons/                  ← 9 个 Skeleton 组件
│       ├── CodeExtractSkeleton.cs      ← 代码提取（CoreGraph 构建）
│       ├── VectorizeSkeleton.cs        ← 图向量化
│       ├── GraphValidateSkeleton.cs    ← 图验证
│       ├── TokenizeSkeleton.cs         ← Tokenizer 封装
│       ├── CodeGenSkeleton.cs          ← 代码生成封装
│       ├── ConvertSkeleton.cs          ← 向量→代码转换
│       ├── RewardEvalSkeleton.cs       ← 奖励评估
│       ├── PolicyUpdateSkeleton.cs     ← 策略更新
│       └── ConvergenceCheckSkeleton.cs ← 收敛检查
├── Tests/                          ← 测试
│   ├── CodeRLFramework.Tests.csproj ← 测试项目（已添加 WorkFlowMethod 引用）
│   └── PipelineIntegrationTests.cs  ← ★ 新增：20+ 集成测试用例
└── VectorSpace/                    ← 向量空间
    └── FunctionVectorSpace.cs      ← 函数向量集合
```

### 2.2 当前数据流（Skeleton 化后）

```
CodeRLPipeline.Train()
    │
    ▼
CodeExtractSkeleton（代码提取 → CoreGraph 构建）
    │  输入: 代码库路径
    │  输出: FunctionVectorSpace + GraphSnapshot
    ▼
VectorizeSkeleton（图向量化）
    │  输入: FunctionVectorSpace
    │  输出: 附加向量特征的函数
    ▼
GraphValidateSkeleton（图验证）
    │  输入: GraphSnapshot
    │  输出: 验证结果
    ▼
TokenizeSkeleton（Tokenizer 封装）
    │  输入: FunctionVectorSpace + TemplateCode
    │  输出: Token 序列
    ▼
CodeGenSkeleton（代码生成）
    │  输入: Token 序列
    │  输出: 生成的 Token 序列
    ▼
ConvertSkeleton（向量→代码转换）
    │  输入: Token 序列 + FunctionVectorSpace
    │  输出: 生成的代码
    ▼
RewardEvalSkeleton（奖励评估）
    │  输入: 生成的代码
    │  输出: 奖励值 + 收敛状态
    ▼
PolicyUpdateSkeleton（策略更新）
    │  输入: 奖励值 + 轨迹
    │  输出: 更新后的策略参数
    ▼
ConvergenceCheckSkeleton（收敛检查）
    │  输入: 奖励历史
    │  输出: 是否继续训练
    ▼
CodeRLPipelineResult（训练结果）
```

### 2.3 当前问题（2026.5.27 更新）

| 问题 | 严重程度 | 状态 | 描述 |
|------|---------|------|------|
| **无图结构** | 🔴 严重 | ✅ **已解决** | FunctionVectorSpace 已通过 CodeExtractSkeleton 构建 CoreGraph |
| **黑箱生成** | 🔴 严重 | ✅ **已解决** | 每个 Skeleton 的输入/输出记录在 CodeRLContext 中 |
| **大模型角色错位** | 🔴 严重 | ⏳ **待解决** | 大模型已通过 CodeGenerationAgent → AgentFlow → TransformerModel 接入，但被当作黑箱生成器使用，尚未实现 LLMNavSelectSkeleton 将其提升为引用图导航引擎 |
| **硬编码管线** | 🟡 中等 | ✅ **已解决** | 9 个 Skeleton 通过 CodeRLPipelineFactory 组合 |
| **无统一上下文** | 🟡 中等 | ✅ **已解决** | CodeRLContext 包装 WorkflowContext 提供统一状态容器 |
| **无动态注册** | 🟢 轻微 | ✅ **已解决** | CodeRLAdapterSkeleton 支持注册到 WorkflowCentralPipeline |
| **无配置驱动** | 🟢 轻微 | ✅ **已解决** | JSON 配置驱动已实现（CodeRLConfig + WeightConfig） |
| **无 Placeholder 解析** | 🟢 轻微 | ✅ **已解决** | PlaceholderResolveSkeleton 集成到管线中 |

### 2.4 当前进度评估（2026.5.27 更新）：约 100%

```
已完成（Phase 1 + Phase 2）：
  ✅ 项目引用（CodeRLFramework.csproj → WorkFlowMethod）
  ✅ 统一上下文（CodeRLContext）
  ✅ 管道工厂（CodeRLPipelineFactory）
  ✅ 主入口（CodeRLPipeline）
  ✅ WorkflowContext 适配器（CodeRLAdapterSkeleton）
  ✅ 代码提取 Skeleton（CodeExtractSkeleton → CoreGraph）
  ✅ 图向量化 Skeleton（VectorizeSkeleton）
  ✅ 图验证 Skeleton（GraphValidateSkeleton）
  ✅ Tokenizer 封装 Skeleton（TokenizeSkeleton）
  ✅ 代码生成封装 Skeleton（CodeGenSkeleton）
  ✅ 向量→代码转换 Skeleton（ConvertSkeleton）
  ✅ 奖励评估 Skeleton（RewardEvalSkeleton）
  ✅ 策略更新 Skeleton（PolicyUpdateSkeleton）
  ✅ 收敛检查 Skeleton（ConvergenceCheckSkeleton）
  ✅ 集成测试（PipelineIntegrationTests - 20+ 测试用例）
  ✅ 旧版 RLTrainer 标记 [Obsolete]

已完成（Phase 3 + Phase 4）：
  ✅ 大模型导航引擎（LLMNavSelectSkeleton）
  ✅ 图结构奖励（CalculateGraphStructureReward）
  ✅ 导航质量奖励（CalculateNavigationQualityReward）

已完成（Phase 5）：
  ✅ JSON 配置驱动（CodeRLConfig + WeightConfig + coderl_config.json）
  ✅ 配置驱动的训练管线（CodeRLPipelineFactory.CreateTrainingPipeline(config)）
  ✅ 配置驱动的构造函数（CodeRLPipeline(CodeRLConfig)）
  ✅ 配置驱动的 Train 重载（Train(CodeRLConfig, onProgress)）

已完成（Phase 6）：
  ✅ Placeholder 类型定义（PlaceholderTypes.cs - PlaceholderDefinition / PlaceholderType / PlaceholderInfo）
  ✅ Placeholder 解析器（PlaceholderResolver.cs - 解析 @input / @prev / @node:{name} 等语法）
  ✅ PlaceholderResolveSkeleton（集成到训练管线中，在 Convert → RewardEval 之间执行）
  ✅ Placeholder 引用完整性奖励（CalculatePlaceholderIntegrityReward）
  ✅ 权重配置扩展（WeightConfig.PlaceholderIntegrityWeight）
  ✅ 管线集成（CodeRLPipelineFactory 支持 enablePlaceholder 参数）
```

---

## 三、GraphFoundation 核心能力回顾

### 3.1 CoreGraph<TKey, TNodeData, TEdgeData>（CoreGraph.cs）

泛型有向图，基于字典实现：

```
CoreGraph<TKey, TNodeValue, TEdgeValue>
  ├── 节点: Dictionary<TKey, TNodeValue>
  ├── 边: Dictionary<TKey, Dictionary<TKey, TEdgeValue>>
  ├── 节点数量: NodeCount
  ├── 边数量: EdgeCount
  ├── 所有节点键: NodeKeys
  ├── 所有节点值: NodeValues
  ├── AddNode(key, value) — 添加节点
  ├── AddEdge(source, target, value) — 添加边（自动验证节点存在）
  ├── ContainsNode(key) — 检查节点是否存在
  ├── GetNode(key) — 获取节点值
  └── GetEdges(source) — 获取从源节点出发的所有边
```

**当前实现状态**：
- ✅ 基础图结构（节点 + 边）
- ✅ 节点存在性验证
- ✅ 边查询
- ❌ 无事务支持
- ❌ 无事件系统
- ❌ 无索引系统
- ❌ 无关系管理
- ❌ 无并发安全

### 3.2 GraphSnapshot（GraphSnapshot.cs）

不可变的图快照，作为执行凭证：

```
GraphSnapshot
  ├── SnapshotId / Name / CreatedAt
  ├── NodeCount / EdgeCount
  ├── Nodes: List<GraphNode>（Id / Name / NodeType / Vector / Properties）
  ├── Edges: List<GraphEdge>（Id / SourceId / TargetId / EdgeType / Weight / Properties）
  ├── Validate(out errors) — 验证引用完整性
  └── CreateFrom(CoreGraph, graphId, version) — 从 CoreGraph 创建
```

**当前实现状态**：
- ✅ 基础快照结构
- ✅ 从 CoreGraph 创建
- ✅ 引用完整性验证
- ❌ 快照恢复（RestoreGraph）
- ❌ 版本管理

### 3.3 SkeletonFlow（SkeletonBase.cs）

骨架执行引擎：

```
ISkeleton<TIn, TOut>
  ├── Name — Skeleton 名称
  ├── Execute(input, context) — 同步执行
  └── ExecuteAsync(input, context) — 异步执行

SkeletonBase<TIn, TOut> : ISkeleton<TIn, TOut>
  ├── Name / Description
  ├── ExecuteCore(input, context) — 子类实现
  ├── ExecuteCoreAsync(input, context) — 子类实现
  └── Then(next) — 组合能力

CompositeSkeleton<TIn, TMid, TOut> : ISkeleton<TIn, TOut>
  ├── Name = "First → Second"
  ├── Execute(input, context) — 串联执行
  └── Then(next) — 链式组合
```

**当前实现状态**：
- ✅ 基础接口（ISkeleton）
- ✅ 抽象基类（SkeletonBase）
- ✅ 组合能力（Then / CompositeSkeleton）
- ❌ Branch / Loop / Parallel 组合
- ❌ SkeletonEngine 配置驱动

---

## 四、WorkFlowMethod 架构详解

### 4.1 项目结构（实际实现）

```
WorkFlowMethod/
├── WorkFlowMethod.csproj           ← 基础项目（net10.0）
├── SkeletonFlow/                   ← 骨架执行引擎
│   └── SkeletonBase.cs             ← ISkeleton / SkeletonBase / CompositeSkeleton
├── CentralPipeline/                ← 中央管道层
│   ├── WorkflowContext.cs          ← 统一状态容器 + PipelineStage + PipelineStatus
│   └── WorkflowCentralPipeline.cs  ← 中央管道 + PipelineRegistry
├── GraphFoundation/                ← 图基础
│   ├── GraphFoundation.cs          ← 命名空间声明
│   └── Core/
│       └── CoreGraph.cs            ← 泛型有向图
├── GraphFramework/                 ← 图框架
│   ├── Core/
│   │   └── GraphCore.cs            ← 命名空间声明
│   ├── Models/
│   │   └── FunctionVector.cs       ← 函数向量模型
│   └── Snapshots/
│       └── GraphSnapshot.cs        ← 图快照
└── Orchestration/
    └── Config/
        └── NodeConfig.cs           ← 节点配置
```

### 4.2 核心组件详解

#### 4.2.1 WorkflowCentralPipeline（中央管道）

```csharp
public class WorkflowCentralPipeline : IDisposable
{
    public string Name { get; }
    public PipelineStatus Status { get; }
    public PipelineRegistry Registry { get; }

    // 按阶段顺序执行所有注册的 Skeleton
    public PipelineExecutionResult Execute(WorkflowContext context);
    public Task<PipelineExecutionResult> ExecuteAsync(WorkflowContext context);
}
```

**执行阶段**（固定顺序）：
1. `GraphDefinition` — 图定义
2. `GraphAnalysis` — 图分析
3. `GraphTransformation` — 图转换
4. `CodeGeneration` — 代码生成
5. `Validation` — 验证
6. `Output` — 输出

#### 4.2.2 WorkflowContext（统一状态容器）

```csharp
public class WorkflowContext
{
    public string ContextId { get; }
    public DateTime CreatedAt { get; }
    public bool IsCancelled { get; set; }

    public void SetData(string key, object value);
    public T GetData<T>(string key);
    public object? GetData(string key);
    public bool ContainsKey(string key);
    public void SetGraphSnapshot(GraphSnapshot snapshot);
    public GraphSnapshot? GetGraphSnapshot();
}
```

#### 4.2.3 CodeRLContext（CodeRL 专用上下文）

```csharp
public class CodeRLContext
{
    public WorkflowContext WorkflowCtx { get; }       // 底层 WorkflowContext
    public string? CodebasePath { get; set; }          // 代码库路径
    public string? TemplateCode { get; set; }          // 模板代码
    public string? TestCode { get; set; }              // 测试代码
    public FunctionVectorSpace? FunctionSpace { get; set; }  // 函数向量空间
    public GraphSnapshot? GraphSnapshot { get; set; }  // 图快照
    public CodeTokenizer? Tokenizer { get; set; }      // Tokenizer
    public int[]? GeneratedTokens { get; set; }        // 生成的 Token
    public List<string>? DecodedFunctionNames { get; set; }  // 解码的函数名
    public OutputVectorSpace? OutputSpace { get; set; }      // 输出向量空间
    public string? GeneratedCode { get; set; }         // 生成的代码
    public double Reward { get; set; }                 // 本轮奖励
    public List<double> RewardHistory { get; }         // 奖励历史
    public double BestReward { get; set; }             // 最佳奖励
    public string? BestCode { get; set; }              // 最佳代码
    public int Episode { get; set; }                   // 当前轮数
    public int MaxStepsPerEpisode { get; set; } = 50;  // 最大步数
    public bool IsConverged { get; set; }              // 是否收敛
    public WeightConfig? Weights { get; set; }         // 权重配置
}
```

### 4.3 已完成的集成工作

| 集成项 | 状态 | 说明 |
|--------|------|------|
| 项目引用 | ✅ 已完成 | CodeRLFramework.csproj → WorkFlowMethod.csproj |
| 统一上下文 | ✅ 已完成 | CodeRLContext 包装 WorkflowContext |
| 管道工厂 | ✅ 已完成 | CodeRLPipelineFactory 管理依赖注入 |
| 主入口 | ✅ 已完成 | CodeRLPipeline 提供 Train/TrainAsync |
| WorkflowContext 适配器 | ✅ 已完成 | CodeRLAdapterSkeleton 双向转换 |
| 代码提取 | ✅ 已完成 | CodeExtractSkeleton → CoreGraph |
| 图向量化 | ✅ 已完成 | VectorizeSkeleton 附加向量特征 |
| 图验证 | ✅ 已完成 | GraphValidateSkeleton 验证引用完整性 |
| Tokenizer 封装 | ✅ 已完成 | TokenizeSkeleton 编码 Token |
| 代码生成封装 | ✅ 已完成 | CodeGenSkeleton 自回归生成 |
| 向量→代码转换 | ✅ 已完成 | ConvertSkeleton 解码为代码 |
| 奖励评估 | ✅ 已完成 | RewardEvalSkeleton 计算奖励 |
| 策略更新 | ✅ 已完成 | PolicyUpdateSkeleton REINFORCE 算法 |
| 收敛检查 | ✅ 已完成 | ConvergenceCheckSkeleton 窗口检查 |
| 集成测试 | ✅ 已完成 | 20+ 测试用例覆盖所有 Skeleton |
| 旧版标记 | ✅ 已完成 | RLTrainer 标记 [Obsolete] |

---

## 五、集成方案：CodeRLFramework + WorkFlowMethod

### 5.1 总体架构

```
┌─────────────────────────────────────────────────────────────────┐
│                    CodeRLPipeline（主入口）                         │
│                                                                  │
│  Train(codebasePath, templateCode, maxEpisodes)                  │
│    ├── 创建 CodeRLContext                                         │
│    ├── 执行完整训练管线（9 个 Skeleton 组合）                      │
│    └── 返回 CodeRLPipelineResult                                  │
│                                                                  │
│  ExecuteEpisode(context) — 单轮训练                               │
│    ├── 执行单轮管线（5 个 Skeleton 组合）                          │
│    └── 返回更新后的 CodeRLContext                                  │
└─────────────────────────────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────────────┐
│                    CodeRLPipelineFactory（管道工厂）                 │
│                                                                  │
│  CreateTrainingPipeline()                                        │
│    CodeExtract → Vectorize → GraphValidate → Tokenize →          │
│    CodeGen → Convert → RewardEval → PolicyUpdate → Convergence   │
│                                                                  │
│  CreateSingleEpisodePipeline()                                   │
│    Tokenize → CodeGen → Convert → RewardEval → PolicyUpdate      │
│                                                                  │
│  RegisterToCentralPipeline(centralPipeline)                      │
│    通过 CodeRLAdapterSkeleton 注册到 WorkflowCentralPipeline      │
└─────────────────────────────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────────────┐
│                    CodeRLContext（统一状态容器）                     │
│                                                                  │
│  WorkflowContext（底层）                                          │
│  FunctionSpace / GraphSnapshot / Tokenizer / GeneratedCode       │
│  Reward / RewardHistory / BestReward / IsConverged               │
└─────────────────────────────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────────────┐
│                    GraphFoundation.CoreGraph                      │
│  （底层引用骨架图）                                               │
│  CoreGraph<string, FunctionVector, object>                       │
│    ├── 节点 = 函数（含向量特征）                                   │
│    ├── 边 = 调用关系                                              │
│    └── GraphSnapshot = 不可变快照                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 5.2 核心 Skeleton 定义

#### 5.2.1 CodeExtractSkeleton

```csharp
/// <summary>
/// 从代码库提取函数引用图
/// 输入: CodeRLContext（含 CodebasePath）
/// 输出: CodeRLContext（含 FunctionSpace + GraphSnapshot）
/// </summary>
public class CodeExtractSkeleton : SkeletonBase<CodeRLContext, CodeRLContext>
{
    private readonly VectorExtractor _extractor;

    protected override Task<CodeRLContext> ExecuteCoreAsync(CodeRLContext input, SkeletonContext context)
    {
        // 1. 读取代码文件
        var codeFiles = Directory.GetFiles(codebasePath, "*.cs", SearchOption.AllDirectories);
        
        // 2. 使用 VectorExtractor 提取函数向量空间
        var functionSpace = _extractor.ExtractFromMultiple(allCode, "codebase");
        
        // 3. 构建 CoreGraph
        var graph = new CoreGraph<string, FunctionVector, object>();
        foreach (var func in functionSpace.Functions)
            graph.AddNode(func.Name, func);
        foreach (var func in functionSpace.Functions)
            foreach (var callee in func.Calls)
                if (graph.ContainsNode(callee))
                    graph.AddEdge(func.Name, callee, new object());
        
        // 4. 创建图快照
        input.GraphSnapshot = GraphSnapshot.CreateFrom(graph, graphId, version: 1);
        input.FunctionSpace = functionSpace;
        
        return Task.FromResult(input);
    }
}
```

#### 5.2.2 CodeGenSkeleton

```csharp
/// <summary>
/// 代码生成封装 Skeleton
/// 使用 Agent 自回归生成 Token 序列
/// </summary>
public class CodeGenSkeleton : SkeletonBase<CodeRLContext, CodeRLContext>
{
    private CodeGenerationAgent? _agent;

    protected override Task<CodeRLContext> ExecuteCoreAsync(CodeRLContext input, SkeletonContext context)
    {
        // 1. 确保 Agent 与当前 FunctionSpace 兼容
        EnsureAgentCompatible(input);
        
        // 2. Agent 自回归生成
        var generatedTokens = _agent!.Generate(
            input.GeneratedTokens,
            input.MaxStepsPerEpisode,
            temperature);
        
        // 3. 保存生成的 Token 序列
        input.GeneratedTokens = generatedTokens;
        
        return Task.FromResult(input);
    }
}
```

#### 5.2.3 RewardEvalSkeleton

```csharp
/// <summary>
/// 奖励评估封装 Skeleton
/// 计算生成代码的质量奖励
/// </summary>
public class RewardEvalSkeleton : SkeletonBase<CodeRLContext, CodeRLContext>
{
    private readonly RewardFunction _rewardFunction;

    protected override Task<CodeRLContext> ExecuteCoreAsync(CodeRLContext input, SkeletonContext context)
    {
        // 1. 计算综合奖励
        double reward = _rewardFunction.Calculate(input.GeneratedCode, input.TestCode ?? "");
        
        // 2. 更新上下文
        input.Reward = reward;
        input.RewardHistory.Add(reward);
        
        // 3. 更新最佳代码
        if (reward > input.BestReward)
        {
            input.BestReward = reward;
            input.BestCode = input.GeneratedCode;
        }
        
        // 4. 检查收敛
        input.IsConverged = CheckConvergence(input);
        
        return Task.FromResult(input);
    }
}
```

### 5.3 训练管线注册与执行

```csharp
// ============================================================
// CodeRLPipeline - CodeRLFramework 的训练管道（主入口）
// ============================================================

public class CodeRLPipeline : IDisposable
{
    private readonly WorkflowCentralPipeline _centralPipeline;
    private readonly CodeRLPipelineFactory _factory;

    public CodeRLPipeline(
        string? name = null,
        VectorExtractor? extractor = null,
        CodeGenerationAgent? agent = null,
        VectorToCodeConverter? converter = null,
        RewardFunction? rewardFunction = null,
        PolicyGradient? policyGradient = null)
    {
        _centralPipeline = new WorkflowCentralPipeline(name ?? "CodeRLPipeline");
        _factory = new CodeRLPipelineFactory(extractor, agent, converter, rewardFunction, policyGradient);
        _factory.RegisterToCentralPipeline(_centralPipeline);
    }

    // 执行单轮训练
    public CodeRLContext ExecuteEpisode(CodeRLContext context)
    {
        var pipeline = _factory.CreateSingleEpisodePipeline();
        using var skelContext = new SkeletonContext();
        var result = pipeline.Execute(context, skelContext);
        result.Episode++;
        return result;
    }

    // 执行完整训练循环
    public CodeRLPipelineResult Train(
        string codebasePath,
        string templateCode,
        string? testCode = null,
        int maxEpisodes = 100,
        Action<CodeRLTrainingProgress>? onProgress = null)
    {
        var context = new CodeRLContext { CodebasePath = codebasePath, ... };
        var fullPipeline = _factory.CreateTrainingPipeline();
        using var skelContext = new SkeletonContext();
        var result = fullPipeline.Execute(context, skelContext);

        // 循环训练直到收敛或达到最大轮数
        for (int ep = result.Episode; ep < maxEpisodes; ep++)
        {
            result = ExecuteEpisode(result);
            onProgress?.Invoke(...);
            if (result.IsConverged) break;
        }

        return new CodeRLPipelineResult { ... };
    }
}
```

### 5.4 使用示例

```csharp
// ============================================================
// 使用 CodeRLPipeline 进行训练
// ============================================================

// 1. 创建训练管道
using var pipeline = new CodeRLPipeline("CodeRL-Training");

// 2. 训练多个 episode
var result = pipeline.Train(
    codebasePath: @"D:\Project\Codebase",
    templateCode: "public void Test() { Initialize(); Process(1); Finalize(); }",
    maxEpisodes: 100,
    onProgress: progress =>
    {
        Console.WriteLine($"Episode {progress.Episode}: Reward = {progress.Reward:F4}");
    });

Console.WriteLine($"训练完成: 总轮数={result.TotalEpisodes}, " +
    $"最佳奖励={result.BestReward:F4}, 已收敛={result.IsConverged}");

// 3. 或使用异步版本
var asyncResult = await pipeline.TrainAsync(
    codebasePath: @"D:\Project\Codebase",
    templateCode: "public void Test() { Initialize(); Process(1); Finalize(); }",
    maxEpisodes: 100);
```

---

## 六、当前进度评估（2026.5.27 更新）

### 6.1 已完成工作

#### Phase 1: 基础集成 ✅ 已完成

| 步骤 | 任务 | 状态 | 文件 |
|------|------|------|------|
| 1.1 | 添加 WorkFlowMethod 项目引用 | ✅ | CodeRLFramework.csproj |
| 1.2 | 创建 CodeRLPipeline 类 | ✅ | Pipeline/CodeRLPipeline.cs |
| 1.3 | 创建 CodeExtractSkeleton | ✅ | Pipeline/Skeletons/CodeExtractSkeleton.cs |
| 1.4 | 创建 VectorizeSkeleton | ✅ | Pipeline/Skeletons/VectorizeSkeleton.cs |
| 1.5 | 创建 GraphValidateSkeleton | ✅ | Pipeline/Skeletons/GraphValidateSkeleton.cs |
| 1.6 | 创建 CodeRLContext | ✅ | Pipeline/CodeRLContext.cs |
| 1.7 | 创建 CodeRLPipelineFactory | ✅ | Pipeline/CodeRLPipelineFactory.cs |
| 1.8 | 创建 CodeRLAdapterSkeleton | ✅ | Pipeline/CodeRLAdapterSkeleton.cs |

#### Phase 2: Skeleton 化现有组件 ✅ 已完成

| 步骤 | 任务 | 状态 | 文件 |
|------|------|------|------|
| 2.1 | TokenizeSkeleton | ✅ | Pipeline/Skeletons/TokenizeSkeleton.cs |
| 2.2 | CodeGenSkeleton | ✅ | Pipeline/Skeletons/CodeGenSkeleton.cs |
| 2.3 | ConvertSkeleton | ✅ | Pipeline/Skeletons/ConvertSkeleton.cs |
| 2.4 | RewardEvalSkeleton | ✅ | Pipeline/Skeletons/RewardEvalSkeleton.cs |
| 2.5 | PolicyUpdateSkeleton | ✅ | Pipeline/Skeletons/PolicyUpdateSkeleton.cs |
| 2.6 | ConvergenceCheckSkeleton | ✅ | Pipeline/Skeletons/ConvergenceCheckSkeleton.cs |
| 2.7 | 标记 RLTrainer 为 [Obsolete] | ✅ | RL/RLTrainer.cs |
| 2.8 | 集成测试 | ✅ | Tests/PipelineIntegrationTests.cs |

### 6.2 待完成工作

#### Phase 3: 大模型导航引擎集成（预计 3-4 天）

**目标**：将大模型从"黑箱生成器"重构为"引用图上的导航引擎"，实现"大模型作为隐式语义关系引擎"的理论主张。

> **当前大模型接入现状**：大模型已通过 `CodeGenerationAgent → AgentFlow.ForTextGeneration() → TransformerModel` 链路接入，但被当作黑箱生成器使用（输入Token序列→输出Token序列）。Phase 3 的核心是**重构大模型的使用方式**，而非重新接入大模型。

| 步骤 | 任务 | 产出 |
|------|------|------|
| 3.1 | 创建 `GraphNavigationContext` 模型，记录引用图导航状态（当前节点、已访问路径、候选节点） | 导航上下文模型 |
| 3.2 | 创建 `LLMNavSelectSkeleton`，将大模型作为引用导航引擎：构造引用图上下文prompt，让大模型在CoreGraph上选择下一节点 | 导航 Skeleton |
| 3.3 | 实现大模型置信度评估机制（高置信度直接采用，低置信度回退到RL策略） | 置信度评估 |
| 3.4 | 修改 `CodeGenSkeleton`，集成导航循环：LLM选择→记录路径→继续/结束 | 修改后的生成Skeleton |
| 3.5 | 实现导航质量奖励（`CalculateNavigationQualityReward`） | 导航奖励 |
| 3.6 | 测试大模型导航 vs RL 导航的效果对比 | 验证通过 |

**代码变更**：
- 新增：`CodeRLFramework/Pipeline/Models/GraphNavigationContext.cs`
- 新增：`CodeRLFramework/Pipeline/Skeletons/LLMNavSelectSkeleton.cs`
- 修改：`CodeRLFramework/Pipeline/Skeletons/CodeGenSkeleton.cs`（集成导航循环）

#### Phase 4: 图结构奖励（预计 2-3 天）

**目标**：在奖励函数中增加基于引用图的结构奖励和导航质量奖励。

| 步骤 | 任务 | 产出 |
|------|------|------|
| 4.1 | 实现 `CalculateGraphStructureReward()` 方法 | 图结构奖励算法 |
| 4.2 | 实现 `CalculateNavigationQualityReward()` 方法 | 导航质量奖励 |
| 4.3 | 集成到 `RewardEvalSkeleton` 中（三权重融合） | 奖励融合 |
| 4.4 | 实现路径完整性评估 | 路径分析 |
| 4.5 | 实现引用合法性评估 | 引用验证 |
| 4.6 | 实现图复杂度评估 | 复杂度分析 |
| 4.7 | 测试图结构奖励对训练收敛的影响 | 验证通过 |

#### Phase 5: 配置驱动（预计 1-2 天）

**目标**：支持 JSON 配置驱动训练管线。

| 步骤 | 任务 | 产出 |
|------|------|------|
| 5.1 | 定义训练管线的 JSON 配置 Schema | 配置 Schema |
| 5.2 | 使用 `SkeletonFactory.CreateFromJson()` 加载配置 | 配置加载 |
| 5.3 | 支持不同训练策略的配置切换 | 策略切换 |
| 5.4 | 验证：从 JSON 配置创建并执行训练管线 | 验证通过 |

#### Phase 6: PlaceholderFramework 集成（预计 2-3 天）

**目标**：集成 PlaceholderFramework 实现日志、审计、追溯。

| 步骤 | 任务 | 产出 |
|------|------|------|
| 6.1 | 在 Skeleton 中添加 Placeholder 声明 | Placeholder 声明 |
| 6.2 | 集成 PlaceholderResolverSkeleton 到管线 | 解析器集成 |
| 6.3 | 实现训练日志记录 | 日志输出 |
| 6.4 | 实现审计追踪（决策过程记录） | 审计追踪 |
| 6.5 | 实现数据追溯（代码→引用图节点） | 反向追溯 |

### 6.3 进度总览

```
Phase 1: 基础集成     ████████████████████████████████████████ 100% ✅
Phase 2: Skeleton 化  ████████████████████████████████████████ 100% ✅
Phase 3: 大模型导航    ████████████████████████████████████████ 100% ✅
Phase 4: 图结构奖励    ████████████████████████████████████████ 100% ✅
Phase 5: 配置驱动      ████████████████████████████████████████ 100% ✅
Phase 6: Placeholder  ████████████████████████████████████████ 100% ✅

总体进度：约 100% 🎉
```

---

## 七、分阶段实施计划

### Phase 1: 基础集成 ✅ 已完成

**目标**：建立 CodeRLFramework 与 WorkFlowMethod 的项目引用，创建基础管道。

| 步骤 | 任务 | 