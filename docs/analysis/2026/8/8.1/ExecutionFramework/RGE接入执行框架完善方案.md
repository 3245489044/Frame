# RGE 接入执行框架完善方案

> **在现有 ExecutionFramework 基础上消费 RGE 模型并补强执行调度**
> 版本: v1.0-draft | 状态: 草案 | 最后更新: 2026-08-01

---

## 目录

1. [背景与定位](#1-背景与定位)
2. [现状盘点](#2-现状盘点)
3. [问题分析](#3-问题分析)
4. [与 RGE 的关系](#4-与-rge-的关系)
5. [演进点 1：引用解析](#5-演进点-1引用解析)
6. [演进点 2：IoC 策略注入](#6-演进点-2ioc-策略注入)
7. [演进点 3：失败收集语义](#7-演进点-3失败收集语义)
8. [演进点 4：门面统一调度](#8-演进点-4门面统一调度)
9. [现有引擎收敛策略](#9-现有引擎收敛策略)
10. [演进路线图](#10-演进路线图)

---

## 1. 背景与定位

### 1.1 为什么不需要重建

`ExecutionFramework`（`CsharpPrj/Modules/ExecutionFramework/`）已提供完整的通用执行框架：

| 能力域 | 现有组件 |
|--------|---------|
| 执行引擎 | `ExecutionEngine` / `IExecutionEngine` |
| 会话管理 | `SessionManager` / `ExecutionSession` / `IExecutionSession` |
| 步骤执行 | `StepExecutor` / `IStepExecutor`（含 Polly 弹性策略） |
| 管线编排 | `PipelineExecutor` / `CentralPipeline` / `IPipelineStage` |
| 参数处理 | `ParameterHelper` / `ParameterTransformer` / `IParameterProcessor` |
| 执行中心 | `ExecutionCenterBootstrapper` + Matrix 队列 |

因此本文档**不主张重建**，而是：**在现有 ExecutionFramework 基础上，消费 RGE 模型（`CsharpPrj/Protocol/RGE/`）并补强执行调度能力**。

### 1.2 本文档定位

- **上游标准**：`CsharpPrj/Protocol/RGE/RGE数据模型标准.md`（L0 模型标准）
- **本文档**：`ExecutionFramework` 的 L1-L3 实现演进方案
- **边界**：执行框架是 RGE 模型的**消费方**，不在框架内重新定义图模型

### 1.3 依赖方向

```
Protocol/RGE (L0 模型标准, 零依赖)
    ↓ 被消费
Modules/ExecutionFramework (L1-L3 实现演进, 依赖 RGE 模型)
    ↓ 供调度
Operation/WorkFlowMethod + FileDriven (现有引擎收敛为策略)
```

---

## 2. 现状盘点

### 2.1 ExecutionFramework 现有组件清单

```
ExecutionFramework/
├── Abstractions/
│   ├── IDataContext.cs
│   ├── IExecutionEngine.cs
│   ├── IExecutionLogger.cs
│   ├── IExecutionSession.cs
│   ├── IParameterProcessor.cs
│   └── IStepExecutor.cs
├── Engine/
│   ├── DefaultExecutionOrchestrator.cs
│   ├── ExecutionEngine.cs
│   ├── ExecutionOrchestrator.cs
│   ├── FallbackTypeConverter.cs
│   ├── FunctionExecutor.cs
│   ├── HotReloadService.cs
│   ├── ParameterHelper.cs
│   ├── ParameterTransformer.cs
│   ├── PipelineBuilder.cs
│   ├── PipelineExecutor.cs
│   ├── SessionManager.cs
│   ├── StepExecutor.cs
│   ├── TaskHelper.cs
│   └── TrConfigRegistry.cs
├── ExecutionCenter/
│   ├── ExecutionCenterBootstrapper.cs
│   ├── Core/
│   └── Matrix/
├── Session/
│   ├── DataContext.cs
│   ├── ExecutionSession.cs
│   ├── SessionFactory.cs
│   └── StepExecutionState.cs
└── docs/
    └── WorkflowExecution通用化执行框架方案.md（既有愿景文档）
```

### 2.2 已具备能力

| 能力 | 现状 | 说明 |
|------|------|------|
| 步骤类型支持 | ✅ 已有 | `StepExecutor` 支持 Function/Condition/Loop/Parallel 四类 |
| 弹性策略 | ✅ 已有 | `NodeExecutor` 包装 Polly 重试/超时 |
| 会话生命周期 | ✅ 已有 | `WorkflowEngine`（WorkFlowMethod 内）实现 Session 全生命周期 |
| 参数转换 | ✅ 已有 | `ParameterTransformer` / `TypeConverter` |
| 取消令牌 | ✅ 已有 | `CancellationTokenSourceManager` |

### 2.3 与 RGE 的差距（本文档要补的）

| 差距 | 现状 | RGE 要求 |
|------|------|---------|
| **引用图模型** | `StepDefinition.Parameters` 是字典，引用关系隐含 | 显式 `ParamRef` 路径 + `DependencyEdge` |
| **引用解析** | `StepExecutor.GetStepParameters` 直接把字典转数组，不解析引用 | `ReferenceResolver` 按路径解析 ParamRef |
| **策略注入** | `StepExecutor` 内 switch 硬编码 StepType | IoC 注入 `IIntentSystem` 策略 |
| **失败收集** | 失败即中止（工作流节点） | `_failedSteps` 收集 + 依赖标注 |
| **门面统一** | `FunctionCallFacade` + `WorkflowCommandCenter` 双入口 | 统一经 ExecutionFramework 调度 |

---

## 3. 问题分析

### 3.1 引用丢失（E1 — 高危）

现有 `StepExecutor.GetStepParameters`：

```csharp
private static object?[]? GetStepParameters(StepDefinition step)
{
    return step.Parameters is object?[] arr ? arr : new[] { step.Parameters };
}
```

当 `step.Parameters` 是 `Dictionary<string, object?>` 时，`is object?[]` 为 false，整个字典被包成**单元素数组**传入函数——函数收到的是字典而非命名参数。

### 3.2 参数名污染（E2 — 高危）

`WorkflowExecutionEngine.ExecuteStepsCore`（StateHoldDriven）：

```csharp
foreach (var key in step.InputKeys)
{
    if (context.TryGetValue(key, out var val))
        stepArgs.Add(val);
    else
        stepArgs.Add(key);    // ⚡ 键名作为值传入！
}
```

输入缺键时把键名字符串当作参数值传入，导致类型转换错误。

### 3.3 失败语义不一致（E3 — 中危）

- `WorkflowMethodExecutionService`：节点失败 → **立即中止**
- `WorkflowExecutionEngine`：节点失败 → **收集 `_failedSteps` 继续**

同一业务语义在不同路径下行为不同。

### 3.4 双入口分叉（E4 — 中危）

- `FunctionCallFacade.ExecuteAsync` → `WorkflowMethodExecutionService`（新）
- `WorkflowCommandCenter.ExecuteWorkflow` → `WorkflowExecutionLayer` → `WorkflowExecutionEngine`（旧）

调用方需自行决策走哪条路径，无法统一审计/路由。

---

## 4. 与 RGE 的关系

### 4.1 执行框架作为 RGE 消费方

执行框架**不定义图模型**，而是消费 `CsharpPrj/Protocol/RGE/` 的 ExecutionGraph：

```
RGE ExecutionGraph（模型层）
    ↓ 反序列化/构建
ExecutionFramework（消费层）
    ├── RgeGraphScheduler（新）— 图遍历调度
    ├── ReferenceResolverSystem（新）— 引用解析
    ├── IIntentSystem 策略注入（新）— 执行能力转包
    ├── StepExecutor（演进取）— 增加引用感知
    └── SessionManager（保留）— 生命周期
```

### 4.2 映射关系

| RGE 概念 | ExecutionFramework 组件 | 演进方式 |
|----------|------------------------|---------|
| `ExecutionGraph` | `RgeGraphScheduler`（新） | 新增：图遍历入口 |
| `Entity` | `StepDefinition` / `ExecutionPlan` | 适配：Entity 映射为 step |
| `ParamRef` | `StepDefinition.Parameters` | 增强：支持路径语义 |
| `System` 契约 | `IStepExecutor` / `IIntentSystem`（新） | 扩展：按 StepType 策略注入 |
| 失败收集 | `ExecutionResult` / `_failedSteps`（新） | 增强：统一收集语义 |

---

## 5. 演进点 1：引用解析

### 5.1 目标

在 `ExecutionFramework` 新增 `ReferenceResolverSystem`，使 `StepExecutor` 具备 RGE 引用的路径解析能力。

### 5.2 新增组件

```
ExecutionFramework/Reference/
├── ReferenceResolver.cs      // 路径 → 值（实现 RGE §5 ReferenceResolver 契约）
├── ReferencePathParser.cs    // 路径语法解析器
└── ReferenceScope.cs         // 引用作用域（父/子/外部）
```

### 5.3 实现要点

```csharp
public sealed class ReferenceResolver
{
    // 依据 RGE §5.1-5.2 作用域顺序解析
    public object? Resolve(
        IExecutionContext context,
        string path)
    {
        // 1. 自身 OutputKey 缓存
        // 2. 兄弟 Entity 输出
        // 3. 父图输入/输出
        // 4. 根输入对象
        // 5. 外部作用域（$env:/$const:）
    }
}
```

### 5.4 修复现有 Bug

- **修复 `GetStepParameters`**：`Dictionary<string, object?>` 不再被包成单元素数组，遍历为命名参数
- **修复 `stepArgs.Add(key)`**：缺键时不传参（不传键名字符串），符合 RGE §5.4 缺失引用处理

### 5.5 日志

- `ReferenceResolver_Resolve_Path` — 解析路径入口
- `ReferenceResolver_Resolve_Missing` — 缺失引用标记
- `ReferenceResolver_Resolve_Resolved` — 解析成功

---

## 6. 演进点 2：IoC 策略注入

### 6.1 目标

将 `StepExecutor` 内的 StepType switch 硬编码，改为 IoC 注入的 `IIntentSystem` 策略（对齐 RGE §7 转包边界）。

### 6.2 新增接口

```csharp
// ExecutionFramework/Abstractions/IIntentSystem.cs
public interface IIntentSystem
{
    string StepType { get; }          // 路由键（Function/Condition/Loop/Parallel）
    Task<ExecutionResult> ExecuteAsync(
        StepDefinition step,
        IExecutionContext context,
        CancellationToken cancellationToken);
}
```

### 6.3 策略注册

通过 DI 容器注册各 StepType 的实现：

```csharp
services.AddTransient<IIntentSystem, FunctionSystem>();
services.AddTransient<IIntentSystem, ConditionSystem>();
services.AddTransient<IIntentSystem, LoopSystem>();
services.AddTransient<IIntentSystem, ParallelSystem>();
```

`StepExecutor` 通过 `IEnumerable<IIntentSystem>` 注入，按 `step.StepType` 路由。

### 6.4 现状保留

- Polly 弹性包装保留在 `NodeExecutor`（WorkFlowMethod 内），作为可选外层
- `StepExecutor` 的既有四种步骤逻辑迁移为各 System 实现

### 6.5 收益

| 收益 | 说明 |
|------|------|
| 可替换 | 新 StepType（如 Agent/LLM）只需新增 IIntentSystem 实现 |
| 可测试 | 各策略独立单元测试 |
| 解耦 | `StepExecutor` 不再硬编码步骤逻辑 |

---

## 7. 演进点 3：失败收集语义

### 7.1 目标

统一失败行为为**收集模式**（借鉴旧 `WorkflowExecutionEngine` 优点）：

- 节点失败 → 记录到 `_failedSteps`
- 依赖失败的节点 → 标记依赖错误，不执行
- 无依赖的独立节点 → 继续执行
- 输出包含 `_hasErrors: true`

### 7.2 ExecutionResult 增强

```csharp
public sealed class ExecutionResult
{
    // 既有属性不变
    public List<StepFailureRecord> FailedSteps { get; } = new();

    public bool HasErrors => FailedSteps.Count > 0;
}

public sealed class StepFailureRecord
{
    public string StepName { get; set; }
    public string FuncName { get; set; }
    public string Error { get; set; }
}
```

### 7.3 调度器语义

```
RgeGraphScheduler.Execute(graph):
  foreach entity in topological_order(graph):
    if hasDependencyError(entity):
      markDependencyError(entity)      # 依赖失败 → 标记跳过
      continue
    result = dispatch(entity)          # 交给 IIntentSystem
    if !result.IsSuccess:
      failedSteps.Add(entity, result)  # 收集失败
    else:
      exposeOutput(entity, result)     # 暴露 OutputKey 引用
```

### 7.4 与旧引擎对比

| 行为 | 旧引擎 | 统一后 |
|------|--------|--------|
| 失败后继续 | ✅ 继续收集 | ✅ 继续（独立节点） |
| 依赖失败节点 | 仍执行（可能出错） | 标记跳过（更安全） |
| 输出 | `_failedSteps` + `_hasErrors` | 同左 + 依赖错误标记 |

---

## 8. 演进点 4：门面统一调度

### 8.1 目标

所有函数/工作流执行统一走 ExecutionFramework，消除 `FunctionCallFacade` 与 `WorkflowCommandCenter` 双入口分叉。

### 8.2 统一调度链

```
FunctionCallFacade.ExecuteAsync (统一入口, 保留)
    ↓ 仅做: Fail-Fast + Mode 解析 + 审计
ExecutionFramework.RgeGraphScheduler (统一调度)
    ├── ReferenceResolverSystem     # 引用解析
    ├── IIntentSystem 策略路由      # 执行能力转包
    └── SessionManager             # 生命周期
```

### 8.3 重定向

- `WorkflowCommandCenter.ExecuteWorkflow` → 委托 `FunctionCallFacade`（Sync 模式）
- `WorkflowCommandCenter.HandleWorkflowSwitch` → 保留（UI 编排职责，不涉执行）
- `WorkflowExecutionLayer` → 仅保留加载/缓存/详情（无执行）
- `EcsGate.ExecuteFunctionOperation` → 委托门面（现有已如此，保持）

### 8.4 伪异步清理

- `ExecuteSync` / `ExecuteAsyncInternal` 合并为真正异步路径
- `ExecuteSyncRequest` 用 `.GetAwaiter().GetResult()` 调用同一异步方法

---

## 9. 现有引擎收敛策略

### 9.1 收敛总览

| 现有引擎 | 收敛方向 | 优先级 |
|----------|---------|--------|
| `WorkflowExecutionEngine`（StateHoldDriven） | 废弃；`_failedSteps` 收集逻辑迁入调度器 | 高 |
| `WorkflowMethodExecutionService`（WorkFlowMethod） | 适配为执行框架入口适配器 | 高 |
| `WorkflowEngine`（WorkFlowMethod/WorkflowExecution/Engine） | 保留会话管理；迁入 ExecutionFramework | 中 |
| `NodeExecutor`（WorkFlowMethod） | Polly 弹性层保留为策略包装 | 中 |
| `FuncCallRun`（WorkFlowMethod） | 轻量场景保留；重型场景走调度器 | 低 |
| `RefactoredWorkflowExecutor`（WorkFlowMethod） | 适配为 L2 策略或废弃 | 低 |

### 9.2 分阶段

**阶段 A（保兼容）**：在 ExecutionFramework 增加 RGE 消费能力，旧引擎继续可用
**阶段 B（双轨）**：新调用走 ExecutionFramework，旧调用仍走旧引擎，观察行为一致
**阶段 C（收敛）**：确认一致后，旧引擎入口重定向到门面，逐步删除旧代码

### 9.3 依赖收敛

```
改造前:
  FunctionCallFacade → WorkflowMethodExecutionService → (WorkFlowMethod 内部多引擎)
  WorkflowCommandCenter → WorkflowExecutionLayer → WorkflowExecutionEngine

改造后:
  FunctionCallFacade → ExecutionFramework.RgeGraphScheduler
                        ├── ReferenceResolverSystem
                        ├── IIntentSystem(策略: 来自现有引擎适配)
                        └── SessionManager
```

---

## 10. 演进路线图

### 10.1 实施步骤

| 步骤 | 内容 | 风险 | 工作量 | 状态 |
|------|------|------|--------|------|
| 1 | 新增 `ReferenceResolverSystem` + 修复 `GetStepParameters`/`stepArgs.Add(key)` | 中 | 1.5d | ✅ 已完成 |
| 2 | 引入 `IIntentSystem` 接口 + 四类策略从 `StepExecutor` 提取 | 中 | 1.5d | ✅ 已完成 |
| 3 | `ExecutionResult.FailedSteps` 增强 + 调度器收集语义 | 中 | 1d | ✅ 已完成 |
| 4 | `RgeGraphScheduler` 图遍历 + 引用作用域 | 高 | 2d | ✅ 已完成 |
| 5 | 门面统一：`WorkflowCommandCenter.ExecuteWorkflow` 重定向 | 中 | 0.5d | ⬜ 待实施 |
| 6 | 旧引擎收敛：StateHoldDriven 废弃/适配 | 高 | 1.5d | ⬜ 待实施 |
| 7 | 编译验证 + 完整工作流回归测试 | - | 1d | 🔄 部分（编译已验证） |

### 10.2 验证标准

1. 现有工作流（MathOperationsWorkflow 等）在新调度器下结果一致
2. 缺键参数不再传入键名字符串（E2 修复可测）
3. 失败节点收集 `_failedSteps` 且独立节点继续执行
4. 所有执行入口统一经 FunctionCallFacade
5. 新 StepType 策略无需修改框架核心即可接入

### 10.3 后续演进

- **RGE GraphBuilder**：JSON 工作流文件 → ExecutionGraph（含嵌套子图）
- **CSG ↔ RGE 转换**：CSG CanonicalForm ↔ ExecutionGraph 双向映射
- **可视化调度**：基于 ExecutionGraph 的执行过程可视化
- **分布式调度**：ExtensionGraph 跨进程/机器执行

---

## 附录：与既有文档的关系

| 文档 | 关系 |
|------|------|
| `ExecutionFramework/docs/WorkflowExecution通用化执行框架方案.md`（v1.0, 2026-06-28） | 既有愿景：命名抽象+迁移。本文档在其基础上**增加 RGE 消费能力**，不推翻既有结论 |
| `Protocol/RGE/RGE数据模型标准.md`（v1.0-draft, 2026-08-01） | 上游 L0 模型标准，本文档消费其 ExecutionGraph |