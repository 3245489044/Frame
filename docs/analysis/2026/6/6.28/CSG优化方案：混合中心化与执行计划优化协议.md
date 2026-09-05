# CSG 优化方案：混合中心化与执行计划优化协议

> **基于《从契约到编译：MCP协议演进的工程逻辑》的分析与实施路线图**
> 版本: v3.0 | 状态: 定稿 | 最后更新: 2026-06-25

---

## 1. 动机与问题

CSG（Configuration-StateMachine-Graph）是本项目的统一中间表示（IR）标准，天然面向图/状态机场景。当变换场景从单步调用扩展至多步骤依赖链时，纯运行时解释模式存在性能浪费——这与数据库从"逐条执行"到"查询计划优化"的演进逻辑一致。

当前架构存在三个问题：

| # | 问题 | 表现 |
|---|------|------|
| 1 | **纯运行时解释，无执行计划优化** | 无依赖步骤只能顺序执行；无预分析、无优化 |
| 2 | **中心化与去中心化边界模糊** | `TransformEngine` 既做核心变换又做适配器调度；验证规则全部集中在 `ValidationEngine` |
| 3 | **重复变换无缓存** | 相同变换链每次执行都重新解析步骤依赖 |

---

## 2. 设计理念：执行计划优化

**核心思路**：借鉴数据库查询优化器（Query Optimizer）的设计模式，将"编译"的刚性管道重构为"优化规则"的灵活组合。

### 2.1 为什么不是"编译"

| 维度 | 编译方案 | 计划优化方案（本方案） |
|------|---------|---------------------|
| **概念** | 编译器 + Pass 管道 | 优化器 + 可插拔规则 |
| **重量** | 重，4 Pass 固定管道 | 轻，规则可独立注册 |
| **扩展性** | 新增 Pass 需改管道 | 新增规则只需实现接口 |
| **运行时** | 编译时/运行时严格分离 | 计划阶段/执行阶段，可动态 replan |
| **失败模式** | 编译失败→不执行 | 优化失败→回退解释执行 |
| **心智模型** | C# Roslyn 编译器 | PostgreSQL 查询优化器 |
| **协议层适配** | 概念过重，不适合协议层 | 轻量通用，天然适合协议层 |

### 2.2 优化管道

```
TransformBatch 定义（步骤列表）
       │
       ▼
计划优化器（PlanOptimizer）— 规则管道
  ├─ 规则 1: 依赖分析 — 构建步骤依赖图 + 无环检测 + 拓扑排序
  ├─ 规则 2: 并行标记 — 标记无依赖步骤为并行组
  ├─ 规则 3: 串行合并 — 合并纯函数链式步骤（可选）
  ├─ 规则 4: 死代码消除 — 移除不可达步骤（可选）
  └─ 规则 N: 可扩展...
       │
       ▼
执行计划（ExecutionPlan）→ 按并行组执行
```

### 2.3 核心优势

- **0 规则 = 纯解释执行** — 完全向后兼容，现有 TransformBatch 无需改动
- **渐进增强** — 从 0 规则到 N 规则，每一步都是安全的增量
- **可回退** — 任何规则失败都不影响执行，只是优化效果打折扣
- **可插拔** — 规则是策略模式，可按场景组合

---

## 3. 优化方向一：混合中心化

### 3.1 三层职责

```
编排层 (Orchestration Layer) — 可选中心化
  TransformBatch → PlanOptimizer → ExecutionPlan

核心层 (Core Layer) — 严格中心化
  CanonicalForm（不可变 IR 契约）
  基础变换操作（C↔IR, S↔IR, G↔IR）
  核心验证规则（数学上不可变）

领域层 (Domain Layer) — 完全去中心化
  适配器（Adapter）— 各领域独立注册
  领域验证规则 — 适配器携带自己的验证逻辑
  领域优化策略 — 适配器声明自己的优化提示
```

### 3.2 核心层：不可变契约

六种基础变换操作（LTS 理论的标准投影，不可自定义）：
- `ConfigToCanonical` / `CanonicalToConfig`
- `StateMachineToCanonical` / `CanonicalToStateMachine`
- `GraphToCanonical` / `CanonicalToGraph`

### 3.3 领域层：适配器自治

```csharp
public class AdapterCapabilities
{
    public List<DomainValidationRule> CustomValidationRules { get; init; } = new();
    public DomainOptimizationHints? OptimizationHints { get; init; }
}

public record DomainOptimizationHints
{
    public bool IsIdempotent { get; init; }      // 是否幂等（可缓存）
    public bool IsPureFunction { get; init; }     // 是否纯函数（无副作用，可合并/预执行）
    public int SuggestedParallelism { get; init; } = 1;
}
```

### 3.4 编排层：优化模式可选

```csharp
public class TransformBatch
{
    private PlanOptimizer? _optimizer;

    public TransformBatch WithOptimizer(PlanOptimizer optimizer)
    {
        _optimizer = optimizer;
        return this;
    }

    public Dictionary<string, object?> Execute()
    {
        if (_optimizer != null)
        {
            var plan = _optimizer.Optimize(this);
            return plan.Execute(_engine);  // 优化模式
        }
        return ExecuteInterpreted();  // 解释模式（保持向后兼容）
    }
}
```

---

## 4. 优化方向二：执行计划优化协议

### 4.1 核心接口

```csharp
// 执行计划 — 优化后的执行方案
public class ExecutionPlan
{
    public List<PlanStep> Steps { get; } = new();
    public List<HashSet<int>> ParallelGroups { get; } = new();
    public ValidationResult PreValidation { get; set; } = new();
    public bool HasErrors => !PreValidation.IsValid;

    public Dictionary<string, object?> Execute(ITransformEngine engine)
    {
        if (HasErrors) throw new InvalidOperationException("计划验证失败");
        var results = new Dictionary<string, object?>();
        foreach (var group in ParallelGroups)
        {
            if (group.Count == 1)
            {
                var step = Steps[group.First()];
                results[step.StepName] = ExecuteStep(engine, step, results);
            }
            else
            {
                Parallel.ForEach(group, stepIndex =>
                {
                    var step = Steps[stepIndex];
                    var result = ExecuteStep(engine, step, results);
                    lock (results) { results[step.StepName] = result; }
                });
            }
        }
        return results;
    }
}

// 优化规则接口 — 每个规则是一个独立的策略
public interface IOptimizationRule
{
    string Name { get; }
    int Order { get; }  // 执行顺序，小值优先
    ExecutionPlan Apply(TransformBatch batch, ExecutionPlan currentPlan);
}

// 计划优化器 — 规则管道的宿主
public class PlanOptimizer
{
    private readonly List<IOptimizationRule> _rules = new();
    private readonly ITransformEngine _engine;

    public PlanOptimizer(ITransformEngine engine)
    {
        _engine = engine;
    }

    public PlanOptimizer WithRule(IOptimizationRule rule)
    {
        _rules.Add(rule);
        _rules.Sort((a, b) => a.Order.CompareTo(b.Order));
        return this;
    }

    public ExecutionPlan Optimize(TransformBatch batch)
    {
        // 从基础计划开始（纯步骤列表，无优化）
        var plan = CreateBasePlan(batch);

        // 依次应用每条规则
        foreach (var rule in _rules)
        {
            try
            {
                plan = rule.Apply(batch, plan);
            }
            catch (Exception ex)
            {
                // 规则失败不影响执行，只是优化效果打折扣
                // 记录日志后继续下一条规则
                continue;
            }
        }

        return plan;
    }
}
```

### 4.2 内置优化规则

| 规则 | 顺序 | 说明 | 默认启用 |
|------|------|------|---------|
| `DependencyAnalysisRule` | 0 | 构建依赖图 + 无环检测 + 拓扑排序 | ✅ |
| `ParallelMarkRule` | 1 | 标记无依赖步骤为并行组 | ✅ |
| `SequentialMergeRule` | 2 | 合并纯函数链式步骤 | ❌ 可选 |
| `DeadCodeEliminationRule` | 3 | 移除不可达步骤 | ❌ 可选 |
| `ConstantFoldRule` | 4 | 预执行常量输入步骤 | ❌ 可选 |

#### DependencyAnalysisRule

```csharp
public class DependencyAnalysisRule : IOptimizationRule
{
    public string Name => "DependencyAnalysis";
    public int Order => 0;

    public ExecutionPlan Apply(TransformBatch batch, ExecutionPlan plan)
    {
        // 1. 构建依赖图（邻接表）
        var dependencyGraph = BuildDependencyGraph(batch);

        // 2. 环检测（DFS + 三色标记法）
        var cycle = DetectCycle(dependencyGraph);
        if (cycle != null)
            plan.PreValidation.AddError(CsgErrorCode.CyclicDependency,
                "DependencyCycle", $"检测到循环依赖: {string.Join(" → ", cycle)}");

        // 3. 拓扑排序（Kahn 算法）
        var sortedIndices = TopologicalSort(dependencyGraph);

        // 4. 按拓扑序重排步骤
        plan.Steps = sortedIndices.Select(i => plan.Steps[i]).ToList();

        return plan;
    }
}
```

#### ParallelMarkRule

```csharp
public class ParallelMarkRule : IOptimizationRule
{
    public string Name => "ParallelMark";
    public int Order => 1;

    public ExecutionPlan Apply(TransformBatch batch, ExecutionPlan plan)
    {
        // 基于拓扑排序的层级分配
        // 同一层级的无依赖步骤标记为同一并行组
        var levels = AssignLevels(plan.Steps);
        plan.ParallelGroups = levels;
        return plan;
    }
}
```

### 4.3 计划缓存

```csharp
public class PlanCache
{
    private readonly Dictionary<string, ExecutionPlan> _cache = new();
    private readonly PlanOptimizer _optimizer;

    public PlanCache(PlanOptimizer optimizer)
    {
        _optimizer = optimizer;
    }

    public ExecutionPlan GetOrOptimize(TransformBatch batch)
    {
        var hash = ComputeHash(batch);
        if (_cache.TryGetValue(hash, out var cached)) return cached;
        var plan = _optimizer.Optimize(batch);
        _cache[hash] = plan;
        return plan;
    }

    private string ComputeHash(TransformBatch batch)
    {
        // 对 batch 的步骤列表做组合哈希
        // 步骤名 + 操作名 + 输入类型的组合
    }
}
```

### 4.4 增量优化（替代增量编译）

```csharp
public class IncrementalOptimizer
{
    private readonly PlanCache _cache;
    private readonly PlanOptimizer _optimizer;

    public IncrementalOptimizer(PlanOptimizer optimizer, PlanCache cache)
    {
        _optimizer = optimizer;
        _cache = cache;
    }

    public ExecutionPlan Reoptimize(TransformBatch batch, HashSet<string> changedStepNames)
    {
        var fullPlan = _cache.GetOrOptimize(batch);
        var affectedSteps = FindAffectedSteps(fullPlan, changedStepNames);

        if (affectedSteps.Count == fullPlan.Steps.Count)
            return _optimizer.Optimize(batch);  // 影响太大，全量重优化

        return IncrementalOptimize(fullPlan, affectedSteps);
    }

    private HashSet<int> FindAffectedSteps(ExecutionPlan plan, HashSet<string> changedNames)
    {
        // BFS 遍历依赖图，标记所有受影响的步骤
        // 包括直接变更的步骤 + 所有下游依赖步骤
    }

    private ExecutionPlan IncrementalOptimize(ExecutionPlan existing, HashSet<int> affectedIndices)
    {
        // 保留未受影响的步骤
        // 只重新优化受影响的步骤子图
        // 合并为新 ExecutionPlan
    }
}
```

### 4.5 优化效果

| 场景 | 当前（解释型） | 优化后（计划优化型） |
|------|---------------|-------------------|
| 3 步变换链（无分支） | 顺序执行，3 次往返 | 串行合并为 2 步 |
| 2 步无依赖变换 | 顺序执行 | 自动并行执行 |
| 重复变换模式 | 每次都重新解析 | 缓存执行计划 |
| 类型不匹配 | 运行时才报错 | 计划时提前报错 |
| 死代码路径 | 运行时发现 | 计划时消除 |
| 局部变更 | 全量重新执行 | 增量优化 |

---

## 5. 实施路线图

| 阶段 | 内容 | 预估 |
|------|------|------|
| **一：计划优化基础** | `ExecutionPlan` + `PlanOptimizer` + `DependencyAnalysisRule` + `ParallelMarkRule` + `WithOptimizer()` | 1-2 周 |
| **二：高级优化规则** | `SequentialMergeRule` / `DeadCodeEliminationRule` / `ConstantFoldRule` + `PlanCache` | 2-3 周 |
| **三：静态校验增强** | 计划时类型检查 + 状态可达性分析 + `IncrementalOptimizer` | 2-3 周 |
| **四：混合中心化重构** | 分离 `TransformEngine` 职责 + 增强 `AdapterCapabilities` + `DomainValidationRule` | 2-3 周 |
| **五：DSL 上游对接（远期）** | CSG DSL 语法定义 + DSL→`ExecutionPlan` 转换器 | 远期 |

---

## 6. 使用示例

```csharp
// 方式一：通过 TransformBatch 使用（推荐）
var optimizer = new PlanOptimizer(engine)
    .WithRule(new DependencyAnalysisRule())
    .WithRule(new ParallelMarkRule());

var batch = new TransformBatch(engine)
    .WithOptimizer(optimizer)
    .AddStep("step1", "ConfigToCanonical", config)
    .AddStepFromPrevious("step2", "CanonicalToStateMachine", "step1");
var results = batch.Execute();

// 方式二：直接使用优化器
var plan = optimizer.Optimize(batch);
var results2 = plan.Execute(engine);

// 方式三：带缓存
var cache = new PlanCache(optimizer);
var plan2 = cache.GetOrOptimize(batch);
var results3 = plan2.Execute(engine);

// 方式四：增量优化（局部变更时）
var incremental = new IncrementalOptimizer(optimizer, cache);
var plan3 = incremental.Reoptimize(batch, new HashSet<string> { "step1" });
var results4 = plan3.Execute(engine);
```

---

## 7. 与现有架构的集成

```
TransformBatch (现有)
  │
  ├─ Execute() — 解释执行（现有，不变）
  │
  └─ WithOptimizer() → Execute() — 优化执行（新增）
       │
       ▼
  PlanOptimizer (新增)
       │
       ├─ DependencyAnalysisRule (新增)
       ├─ ParallelMarkRule (新增)
       ├─ SequentialMergeRule (新增，可选)
       └─ ...
       │
       ▼
  ExecutionPlan (新增)
       │
       └─ Execute(ITransformEngine) — 调用 TransformEngine.CallOperation()
                                              │
                                              ▼
                                        TransformEngine (现有，不变)
```

所有新增代码位于 `CSG.Unified.Core/` 和 `CSG.Unified.Abstractions/` 目录，不破坏现有接口。
