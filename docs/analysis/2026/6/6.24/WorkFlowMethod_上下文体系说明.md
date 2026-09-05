# WorkFlowMethod 上下文体系说明

> 本文档介绍 WorkFlowMethod 模块中上下文（Context）相关类的设计、职责和关系。
> 编写日期：2026-06-22

---

## 1. 核心上下文类

### 1.1 PipelineContext（统一状态容器）

**文件：** `Operation/WorkFlowMethod/CentralPipeline/WorkflowContext.cs`
**命名空间：** `WorkFlowMethod.CentralPipeline`
**类名：** `PipelineContext`（注意：文件名是 `WorkflowContext.cs`，但类名是 `PipelineContext`）

**职责：**
- 承载所有管道级状态：GraphSnapshot、ExecutionSession、DataContext、DebugLog
- 实现 `IWorkflowContext` 接口，与 `Share/WorkflowExecution` 统一
- 替代原有的 `ExecutionContext`、`SkeletonContext`、`GraphUnit.dic` 等多个分散的状态载体

**核心属性：**
| 属性 | 类型 | 说明 |
|------|------|------|
| `Session` | `ExecutionSession` | 执行会话，管理节点状态、数据上下文、日志 |
| `GraphSnapshot` | `GraphSnapshot` | 图快照，记录当前执行到的图位置 |
| `CurrentNodeKey` | `string` | 当前正在执行的节点键 |
| `Data` | `DataContext` | 数据上下文，存储管道级共享数据 |
| `Log` | `DebugLog` | 调试日志 |
| `CancellationToken` | `CancellationToken` | 取消令牌 |
| `PipelineName` | `string` | 管道名称 |
| `Status` | `PipelineStatus` | 管道状态（Idle/Running/Completed/Failed/Cancelled） |

**核心方法：**
| 方法 | 说明 |
|------|------|
| `SetCurrentNode(string nodeKey)` | 设置当前节点，更新 GraphSnapshot 位置 |
| `GetNodeState(string nodeKey)` | 获取指定节点的执行状态 |
| `SetNodeState(string nodeKey, NodeStatus status)` | 设置节点执行状态 |
| `GetNodeResult(string nodeKey)` | 获取节点执行结果 |
| `SetNodeResult(string nodeKey, object result)` | 设置节点执行结果 |
| `GetData<T>(string key)` | 获取共享数据 |
| `SetData(string key, object value)` | 设置共享数据 |
| `Reset()` | 重置上下文到初始状态 |
| `Dispose()` | 释放资源 |

---

### 1.2 ExecutionSession（执行会话）

**文件：** `Operation/WorkFlowMethod/Execution/Session/ExecutionSession.cs`
**命名空间：** `WorkFlowMethod.Execution.Session`

**职责：**
- 管理单个工作流执行的生命周期
- 维护节点状态映射（`Dictionary<string, NodeState>`）
- 提供数据上下文（`DataContext`）和日志（`DebugLog`）

**核心属性：**
| 属性 | 说明 |
|------|------|
| `SessionId` | 会话唯一标识 |
| `Status` | 会话状态（Running/Completed/Failed/Cancelled） |
| `Data` | 数据上下文 |
| `Log` | 调试日志 |
| `StartTime` | 开始时间 |
| `EndTime` | 结束时间 |

---

### 1.3 WorkflowPipelineContextAdapter（适配器）

**文件：** `Operation/WorkFlowMethod/WorkFlowPipeline/Adapters/WorkflowPipelineContextAdapter.cs`
**命名空间：** `WorkFlowMethod.Skeletons.Adapters`

**职责：**
- 将 `PipelineContext` 适配为 `IStageContext` 接口
- 使 `PipelineContext` 可用于 `Modules/Pipeline` 体系的 Stage 管线
- 采用适配器模式，不修改 `PipelineContext` 现有代码

**核心属性：**
| 属性 | 说明 |
|------|------|
| `InnerContext` | 内部持有的 `PipelineContext` 实例 |
| `StageName` | 当前 Stage 名称 |
| `StageData` | Stage 级数据存储 |
| `CancellationToken` | 取消令牌 |

---

## 2. 上下文关系图

```
PipelineContext (CentralPipeline)
  ├── Session ──→ ExecutionSession (Execution/Session)
  │     ├── NodeStates: Dictionary<string, NodeState>
  │     ├── Data: DataContext
  │     └── Log: DebugLog
  ├── GraphSnapshot (GraphFramework/Snapshots)
  ├── Data: DataContext
  ├── Log: DebugLog
  └── CurrentNodeKey: string

WorkflowPipelineContextAdapter (Adapters)
  └── wraps ──→ PipelineContext
        └── implements ──→ IStageContext (Modules/Pipeline)
```

---

## 3. 上下文流转流程

```
PipelineBuilder.Build()
  │
  ▼
WorkflowStagePipeline (StagePipeline)
  │
  ├── Stage 1: NodeGraphStage ──→ 构建图结构
  │     └── 使用 PipelineContext.GraphSnapshot
  │
  ├── Stage 2: NodeExecutionStage ──→ 执行节点
  │     └── 使用 PipelineContext.Session
  │     └── 使用 PipelineContext.CurrentNodeKey
  │
  ├── Stage 3: NodeLoopStage ──→ 循环遍历节点
  │     └── 使用 PipelineContext.GetNodeState()
  │
  ├── Stage 4: NodeErrorHandlerStage ──→ 错误处理
  │     └── 使用 PipelineContext.Session.GetNodeState()
  │
  └── Stage 5: NodeMetricsStage ──→ 指标收集
        └── 使用 PipelineContext.Session.Log
```

每个 Stage 通过 `WorkflowPipelineContextAdapter` 访问 `PipelineContext`：

```csharp
// Stage 内部
protected override async Task ExecuteCoreAsync(
    WorkflowPipelineContextAdapter context,
    CancellationToken cancellationToken)
{
    var wfContext = context.InnerContext;  // 获取 PipelineContext
    var session = wfContext.Session;       // 获取 ExecutionSession
    // ...
}
```

---

## 4. 命名空间与别名说明

由于 `WorkFlowMethod.CentralPipeline.PipelineContext` 与 `Modules.Pipeline.PipelineContext` 存在命名冲突，在同时引用这两个命名空间的文件中，使用 using 别名解决：

```csharp
using Modules.Pipeline;                                    // Modules.Pipeline.PipelineContext
using WorkFlowMethod.CentralPipeline;                      // WorkFlowMethod.CentralPipeline.PipelineContext
using WfPipelineContext = WorkFlowMethod.CentralPipeline.PipelineContext;  // 别名
```

**涉及别名的文件（13 个）：**
- `NodeProcess/Execution/NodeErrorHandlerStage.cs`
- `NodeProcess/Execution/NodeLoopStage.cs`
- `NodeProcess/Lifecycle/NodeMetricsStage.cs`
- `WorkFlowPipeline/Adapters/WorkflowPipelineContextAdapter.cs`
- `WorkFlowPipeline/Adapters/WorkflowStagePipeline.cs`
- `WorkFlowPipeline/Adapters/OrchestrationStageBridge.cs`
- `WorkFlowPipeline/Adapters/PipelineBuilderExtensions.cs`
- `Orchestration/State/PipelineContextAdapter.cs`
- `Orchestration/State/StageBridge.cs`
- `Orchestration/State/Stages/AdapterStage.cs`
- `Orchestration/State/Stages/LoopStage.cs`
- `Orchestration/State/Stages/ExecutionStage.cs`
- `Orchestration/State/Stages/ErrorHandlingStage.cs`

---

## 5. 旧体系与新体系对比

| 方面 | 旧体系（已废弃） | 新体系（当前） |
|------|-----------------|---------------|
| 上下文类 | `ExecutionContext`、`SkeletonContext`、`GraphUnit.dic` | `PipelineContext`（统一容器） |
| 执行会话 | 分散在各处 | `ExecutionSession`（集中管理） |
| 适配方式 | 直接操作上下文 | `WorkflowPipelineContextAdapter`（适配器模式） |
| 管线构建 | `PipelineBuilder` 静态方法 | `WorkflowStagePipeline`（StagePipeline） |
| 状态管理 | 手动管理 | `IWorkflowContext` 接口统一 |
| 废弃代码 | `Infrastructure/` 目录 | 待清理 |

---

## 6. 常见问题

### Q: 为什么文件名是 `WorkflowContext.cs` 但类名是 `PipelineContext`？
A: 历史原因。该类最初命名为 `WorkflowContext`，重构后更名为 `PipelineContext`，但文件名未同步更新。计划在后续清理中重命名文件。

### Q: 为什么需要 `WfPipelineContext` 别名？
A: 因为 `Modules.Pipeline` 和 `WorkFlowMethod.CentralPipeline` 两个命名空间都定义了 `PipelineContext` 类。在同时引用这两个命名空间的文件中，必须使用别名来消除歧义。

### Q: `PipelineContext` 和 `WorkflowPipelineContextAdapter` 的关系？
A: `PipelineContext` 是核心状态容器，`WorkflowPipelineContextAdapter` 是将其适配到 `IStageContext` 接口的包装器。Stage 管线通过适配器访问 `PipelineContext`。
