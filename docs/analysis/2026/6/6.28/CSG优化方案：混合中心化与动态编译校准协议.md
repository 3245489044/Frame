# CSG 优化方案：混合中心化与动态编译校准协议

> **基于《从契约到编译：MCP协议演进的工程逻辑》的分析与实施路线图**
> 版本: v2.0 | 状态: 定稿 | 最后更新: 2026-06-25

---

## 1. 动机与问题

CSG（Configuration-StateMachine-Graph）是本项目的统一中间表示（IR）标准，天然面向图/状态机场景。当变换场景从单步调用扩展至多步骤依赖链时，纯运行时解释模式必然引入编译层——这与 MCP 协议从"解释型"向"编译型"演进的工程逻辑一致。

当前架构存在三个问题：

| # | 问题 | 表现 |
|---|------|------|
| 1 | **纯运行时解释，无编译优化** | 无依赖步骤只能顺序执行；无预分析、无优化 |
| 2 | **中心化与去中心化边界模糊** | `TransformEngine` 既做核心变换又做适配器调度；验证规则全部集中在 `ValidationEngine` |
| 3 | **重复变换无缓存** | 相同变换链每次执行都重新解析步骤依赖 |

---

## 2. 优化方向一：混合中心化

### 2.1 三层职责

```
编排层 (Orchestration Layer) — 可选中心化
  TransformBatch → TransformCompiler → Plan

核心层 (Core Layer) — 严格中心化
  CanonicalForm（不可变 IR 契约）
  基础变换操作（C↔IR, S↔IR, G↔IR）
  核心验证规则（数学上不可变）

领域层 (Domain Layer) — 完全去中心化
  适配器（Adapter）— 各领域独立注册
  领域验证规则 — 适配器携带自己的验证逻辑
  领域优化策略 — 适配器声明自己的优化提示
```

### 2.2 核心层：不可变契约

六种基础变换操作（LTS 理论的标准投影，不可自定义）：
- `ConfigToCanonical` / `CanonicalToConfig`
- `StateMachineToCanonical` / `CanonicalToStateMachine`
- `GraphToCanonical` / `CanonicalToGraph`

### 2.3 领域层：适配器自治

```csharp
public class AdapterCapabilities
{
    public List<DomainValidationRule> CustomValidationRules { get; init; } = new();
    public DomainOptimizationHints? OptimizationHints { get; init; }
}

public record DomainOptimizationHints
{
    public bool IsIdempotent { get; init; }      // 是否幂等（可缓存）
    public bool IsPureFunction { get; init; }     // 是否纯函数（无副作用）
    public int SuggestedParallelism { get; init; } = 1;
}
```

### 2.4 编排层：编译模式可选

```csharp
public class TransformBatch
{
    private TransformCompiler? _compiler;

    public TransformBatch WithCompilation(TransformCompiler compiler)
    {
        _compiler = compiler;
        return this;
    }

    public Dictionary<string, object?> Execute()
    {
        if (_compiler != null)
            return _compiler.Compile(this).Execute(_engine);  // 编译模式
        return ExecuteInterpreted();  // 解释模式（保持向后兼容）
    }
}
```

---

## 3. 优化方向二：动态编译校准协议

### 3.1 设计理念

**运行时触发编译，编译过程做静态分析。** 无状态→解释执行；有状态/有图→必须编译。

### 3.2 编译管道

```
TransformBatch 定义
       │
       ▼
TransformCompiler 编译管道
  Pass 1: 静态分析 — 构建步骤依赖图 + 无环检测 + 拓扑排序
  Pass 2: 静态校验 — 类型一致性检查 + 状态可达性分析
  Pass 3: 优化通道 — 串行合并 → 并行标记 → 死代码消除 → 常量折叠
  Pass 4: 代码生成 — 生成 CompiledPlan（编译后的执行计划）
       │
       ▼
CompiledPlan.Execute() — 执行编译后的计划
```

### 3.3 核心实现

```csharp
// TransformCompiler
public class TransformCompiler
{
    private readonly ITransformEngine _engine;

    public CompiledPlan Compile(TransformBatch batch)
    {
        var plan = new CompiledPlan();
        AnalyzeStepDependencies(batch, plan);  // Pass 1
        StaticValidate(batch, plan);           // Pass 2
        Optimize(plan);                        // Pass 3
        GenerateExecutionPlan(batch, plan);    // Pass 4
        return plan;
    }
}

// CompiledPlan
public class CompiledPlan
{
    public List<CompiledStep> Steps { get; } = new();
    public List<HashSet<int>> ParallelGroups { get; } = new();
    public ValidationResult PreValidation { get; set; } = new();
    public bool HasErrors => !PreValidation.IsValid;

    public Dictionary<string, object?> Execute(ITransformEngine engine)
    {
        if (HasErrors) throw new InvalidOperationException("编译时验证失败");
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

// PlanCache
public class PlanCache
{
    private readonly Dictionary<string, CompiledPlan> _cache = new();
    private readonly TransformCompiler _compiler;

    public CompiledPlan GetOrCompile(TransformBatch batch)
    {
        var hash = ComputeHash(batch);
        if (_cache.TryGetValue(hash, out var cached)) return cached;
        var plan = _compiler.Compile(batch);
        _cache[hash] = plan;
        return plan;
    }
}

// IncrementalCompiler
public class IncrementalCompiler
{
    public CompiledPlan Recompile(TransformBatch batch, HashSet<string> changedStepNames)
    {
        var fullPlan = _cache.GetOrCompile(batch);
        var affectedSteps = FindAffectedSteps(fullPlan, changedStepNames);
        if (affectedSteps.Count == fullPlan.Steps.Count)
            return _compiler.Compile(batch);  // 影响太大，全量重编译
        return IncrementalCompile(fullPlan, affectedSteps);
    }
}
```

### 3.4 优化效果

| 场景 | 当前（解释型） | 优化后（编译型） |
|------|---------------|-----------------|
| 3 步变换链（无分支） | 顺序执行，3 次往返 | 串行合并为 2 步 |
| 2 步无依赖变换 | 顺序执行 | 自动并行执行 |
| 重复变换模式 | 每次都重新解析 | 缓存编译计划 |
| 类型不匹配 | 运行时才报错 | 编译时提前报错 |
| 死代码路径 | 运行时发现 | 编译时消除 |
| 局部变更 | 全量重新执行 | 增量编译 |

---

## 4. 实施路线图

| 阶段 | 内容 | 预估 |
|------|------|------|
| **一：编译管道基础** | TransformCompiler Pass 1 + CompiledPlan + WithCompilation() | 1-2 周 |
| **二：优化通道** | 串行合并/并行标记/死代码消除/常量折叠 + PlanCache | 2-3 周 |
| **三：静态校验增强** | 编译时类型检查 + 状态可达性分析 + 增量编译 | 2-3 周 |
| **四：混合中心化重构** | 分离 TransformEngine 职责 + 增强 AdapterCapabilities | 2-3 周 |
| **五：DSL 上游对接（远期）** | CSG DSL 语法定义 + DSL→CompiledPlan 编译器 | 远期 |

---

## 5. 使用示例

```csharp
// 编译模式（推荐）
var compiler = new TransformCompiler(engine);
var batch = new TransformBatch(engine)
    .WithCompilation(compiler)
    .AddStep("step1", "ConfigToCanonical", config)
    .AddStepFromPrevious("step2", "CanonicalToStateMachine", "step1");
var results = batch.Execute();

// 带缓存
var cache = new PlanCache(compiler);
var plan = cache.GetOrCompile(batch);
var results2 = plan.Execute(engine);
```
