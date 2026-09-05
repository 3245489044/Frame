# WorkFlowMethod 与 CommandFramework 重构整合方案

> **版本**: v2.0  
> **日期**: 2026-07-03  
> **目标**: 消除 WorkFlowMethod（操作层）与 CommandFramework（共享层）之间的重复逻辑

---

## 1. 现状分析

### 1.1 重复逻辑总览

| 重复领域 | WorkFlowMethod | CommandFramework | 说明 |
|----------|---------------|------------------|------|
| **函数执行器** | `FunctionExecutionFramework/` | `Execution/FunctionExecution.cs` + `ArgumentBuilder.cs` | 两套函数查找→参数构建→DynamicInvoke 逻辑 |
| **管道阶段** | `WorkFlowPipeline/PipelineStages.cs` | `Pipeline/Stages/` | 两套管道阶段定义，但 CommandFramework 的阶段更通用 |

### 1.2 共同依赖

两者都依赖 `Modules/Pipeline`（管线基座）和 `Modules/Registry`（注册表基座）。

---

## 2. 重构目标

### 目标一：函数执行器 → Modules/ExecutionFramework

**`Modules/ExecutionFramework`**（模块层 Layer 2）作为统一的函数执行框架，WorkFlowMethod 和 CommandFramework 的函数执行逻辑都整理到这里。

**现有基础：**
- `Abstractions/` — `IExecutionContext`、`IExecutionEngine`、`IDataContext`、`IStepExecutor`、`IExecutionSession`
- `Engine/` — `ExecutionEngine`、`StepExecutor`、`DefaultExecutionOrchestrator`、`SessionManager`
- `Session/` — `DataContext`、`ExecutionSession`、`SessionFactory`
- `Models/` — `ExecutionPlan`、`ExecutionStatus`、`StepDefinition`

**需要扩展的内容：**

```
Modules/ExecutionFramework/
├── Abstractions/
│   ├── IFunctionExecutor.cs      ← 新增：统一函数执行接口
│   └── IFunctionRegistry.cs      ← 新增：统一函数注册接口
├── Engine/
│   ├── FunctionExecutor.cs       ← 新增：从 CommandFramework 迁移 + 合并 WorkFlowMethod 逻辑
│   └── ArgumentBuilder.cs        ← 新增：从 CommandFramework 迁移
└── Models/
    └── FunctionExecutionResult.cs ← 新增：从 CommandFramework 迁移 + 合并 WorkFlowMethod 逻辑
```

**迁移步骤：**

| # | 操作 | 来源 | 目标 |
|---|------|------|------|
| 1 | 新增 `IFunctionExecutor` 接口 | — | `ExecutionFramework/Abstractions/` |
| 2 | 新增 `IFunctionRegistry` 接口 | — | `ExecutionFramework/Abstractions/` |
| 3 | 迁移 `FunctionExecutor` | `CommandFramework/Execution/FunctionExecution.cs` | `ExecutionFramework/Engine/` |
| 4 | 迁移 `ArgumentBuilder` | `CommandFramework/Execution/ArgumentBuilder.cs` | `ExecutionFramework/Engine/` |
| 5 | 迁移 `CommandExecutionResult` | `CommandFramework/Execution/CommandResults.cs` | `ExecutionFramework/Models/` |
| 6 | WorkFlowMethod 的 `FunctionExecutionFramework` 适配到 `ExecutionFramework` 接口 | `WorkFlowMethod/FunctionExecutionFramework/` | — |
| 7 | CommandFramework 的 `FunctionExecutor` 改为委托 `ExecutionFramework` 实现 | `CommandFramework/Execution/FunctionExecution.cs` | — |
| 8 | 删除 WorkFlowMethod 中重复的注册器逻辑 | `WorkFlowMethod/FunctionExecutionFramework/Registrars/` | — |

---

### 目标二：管道逻辑 → Share/CommandFramework

**`Share/CommandFramework`**（共享层 Layer 3）作为通用的命令式执行管线，从 WorkFlowMethod 的 `WorkFlowPipeline` 中抽取通用管道逻辑来完善它。

**CommandFramework 现有管道阶段：**
- `Pipeline/Stages/ParseCommandStage.cs` — 命令解析
- `Pipeline/Stages/ExecuteCommandStage.cs` — 命令执行
- `Pipeline/Stages/ScriptLoadStage.cs` — 脚本加载
- `Pipeline/Stages/ResultCollectStage.cs` — 结果收集

**WorkFlowMethod 现有管道阶段：**
- `WorkFlowPipeline/PipelineStages.cs` — `GraphDefinition` → `FunctionExec` → `PostProcess` → `UpdateCheck`

**需要从 WorkFlowMethod 抽取到 CommandFramework 的通用逻辑：**

| WorkFlowMethod 中的通用逻辑 | 抽取到 CommandFramework 的位置 | 说明 |
|---------------------------|-------------------------------|------|
| 管道构建器（`PipelineBuilder.cs`） | `Pipeline/PipelineBuilder.cs` | 通用管道构建模式 |
| 管道入口（`PipelineEntry.cs`） | `Pipeline/PipelineEntry.cs` | 通用管道入口模式 |
| 阶段结果收集 | `Pipeline/Stages/ResultCollectStage.cs` | 已有，从 WorkFlowMethod 完善 |
| 错误处理/重试逻辑 | `Pipeline/Stages/RetryStage.cs` | 新增通用重试阶段 |
| 上下文初始化 | `Pipeline/Stages/ContextInitStage.cs` | 新增通用上下文初始化阶段 |

**迁移步骤：**

| # | 操作 | 来源 | 目标 |
|---|------|------|------|
| 1 | 抽取管道构建器通用模式 | `WorkFlowMethod/WorkFlowPipeline/PipelineBuilder.cs` | `CommandFramework/Pipeline/PipelineBuilder.cs` |
| 2 | 抽取管道入口通用模式 | `WorkFlowMethod/WorkFlowPipeline/PipelineEntry.cs` | `CommandFramework/Pipeline/PipelineEntry.cs` |
| 3 | 完善结果收集阶段 | WorkFlowMethod 的阶段结果处理逻辑 | `CommandFramework/Pipeline/Stages/ResultCollectStage.cs` |
| 4 | 新增通用重试阶段 | WorkFlowMethod 的错误处理逻辑 | `CommandFramework/Pipeline/Stages/RetryStage.cs` |
| 5 | 新增上下文初始化阶段 | WorkFlowMethod 的上下文初始化逻辑 | `CommandFramework/Pipeline/Stages/ContextInitStage.cs` |
| 6 | WorkFlowMethod 的 `WorkFlowPipeline` 改为复用 CommandFramework 的通用阶段 | — | — |

---

## 3. 保留在 WorkFlowMethod 的领域特有逻辑

以下 WorkFlowMethod 特有的功能**不参与重构**，保留在原模块：

```
WorkFlowMethod/
├── Orchestration/          ← 工作流编排
├── NodeProcess/            ← 节点处理
├── LoopFramework/          ← 循环框架
├── CallFrame/              ← 调用帧追踪
├── GraphUsing/             ← 图处理
├── CSG/                    ← CSG 协议
├── Config/                 ← 工作流配置
└── Infrastructure/
    ├── WorkflowConstants.cs
    ├── ExpandoObjectHelper.cs
    ├── ServiceDefaults.cs
    └── WorkflowLogger.cs
```

---

## 4. 依赖关系变化

```
重构前：
  WorkFlowMethod → UniversalWorkflowFramework → (内部 BmFunctionExecutor)
  WorkFlowMethod → Pipeline
  CommandFramework → Pipeline
  CommandFramework → Registry

重构后：
  WorkFlowMethod → ExecutionFramework (Modules) → Pipeline
  WorkFlowMethod → CommandFramework (Share) → Pipeline
  CommandFramework → ExecutionFramework (Modules) → Pipeline
  CommandFramework → Pipeline
  CommandFramework → Registry
```

---

## 5. 实施顺序

```
阶段 1：函数执行器 → Modules/ExecutionFramework
├── 新增 IFunctionExecutor、IFunctionRegistry 接口
├── 从 CommandFramework 迁移 FunctionExecutor、ArgumentBuilder、CommandExecutionResult
├── WorkFlowMethod 适配 ExecutionFramework 接口
├── CommandFramework 委托 ExecutionFramework 实现
└── 验证编译通过

阶段 2：管道逻辑 → Share/CommandFramework
├── 从 WorkFlowMethod 抽取管道构建器、管道入口通用模式
├── 完善 CommandFramework 的 ResultCollectStage
├── 新增 RetryStage、ContextInitStage
├── WorkFlowMethod 复用 CommandFramework 通用阶段
└── 验证编译通过

阶段 3：清理
├── 删除重复代码
├── 更新 .csproj 依赖
├── 更新 ModuleRule 属性
└── 更新知识图谱
```
