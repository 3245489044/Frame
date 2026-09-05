# WorkFlowMethod → ExecutionFramework + Matrix ECS 迁移重构方案

> **版本**: v1.0  
> **日期**: 2026-07-28  
> **状态**: 规划完成  
> **涉及项目**: `CsharpPrj/Operation/WorkFlowMethod` → `CsharpPrj/Modules/ExecutionFramework`

---

## 一、背景与目标

### 1.1 现状

`WorkFlowMethod` 项目是 WorkFlow 体系的核心引擎层，包含工作流编排、函数注册、管线执行、运行时动态更新等全生命周期管理能力。当前该项目持有**3 套执行体系**：

| 体系 | 核心类 | 说明 |
|------|--------|------|
| **RefactoredExecution**（旧） | `RefactoredWorkflowExecutor` | 基于 `StateManager` + `WorkflowRuntimeContextManager`，已显式标记重构 |
| **WorkflowEngine**（中） | `WorkflowEngine` + `CentralPipeline` + `NodeExecutor` | 基于 `CentralPipeline` 管线阶段执行，使用 Polly 弹性策略 |
| **WorkflowMethodExecutionService**（新） | 包装 `WorkflowEngine` + `NodeExecutor` | 当前实际入口，实现 `IEventExecutionService` + `IFunctionExecutor` |

同时，`ExecutionFramework` 模块已经包含了目标框架：
- **`ExecutionEngine`** — 执行引擎（管理生命周期、会话、暂停/恢复/取消）
- **`StepExecutor`** — 步骤执行器（Function/Condition/Loop/Parallel 类型）
- **`MatrixEngine`** — ECS 运算矩阵引擎（离散事件调度）
- **`RouterSystem` / `SchedulerSystem` / `RetrySystem` / `MonitorSystem`** — ECS 系统链

### 1.2 目标

1. **消除 `WorkflowExecution.*` 旧命名空间** — 所有引用迁移到 `ExecutionFramework.*` / `TConfig.*`
2. **消除 3 套执行体系并存** — 统一使用 `ExecutionFramework` 的执行引擎
3. **引入 Matrix ECS 事件调度** — 增加异步事件驱动的工作流执行路径
4. **清理死代码** — 删除 TypeAliases 空壳、已废弃的管线路径、冗余包装层

### 1.3 影响范围

| 维度 | 数据 |
|------|------|
| 涉及文件 | 21 个（WorkFlowMethod 项目）|
| 受影响项目 | 仅 `WorkFlowMethod.csproj` |
| 依赖项目 | `ExecutionFramework.csproj`（已引用）|
| 新增文件 | 1~2 个（消息类型 + 可选接口）|

---

## 二、当前架构全景

### 2.1 WorkFlowMethod 执行调用链

```
调用方
  │
  ▼
WorkflowMethodExecutionService
  ├── ExecuteFunctionAsync(string, Dictionary)       ← 主入口
  │     │
  │     ▼
  │   WorkflowDefinitionEntry (工作流定义)
  │     │ 遍历 Nodes
  │     ▼
  │   NodeExecutor.ExecuteNodeAsync(StepDefinition)
  │     ├── Polly 弹性管道（重试 + 超时）
  │     ├── ExecuteFunctionNodeAsync → IFunctionExecutor
  │     ├── ExecuteConditionNodeAsync → 条件分支
  │     ├── ExecuteLoopNodeAsync → Foreach/Count 循环
  │     └── ExecuteParallelNodeAsync → Task.WhenAll
  │
  ├── WorkflowEngine (TConfig.IExecutionEngine)
  │     └── CentralPipeline
  │           ├── InitializationStage
  │           ├── ExecutionStage → IStepExecutor
  │           └── FinalizationStage
  │
  └── RefactoredWorkflowExecutor (已标记废弃)
```

### 2.2 ExecutionFramework 目标架构

```
ExecutionFramework
  ├── ExecutionEngine (IExecutionEngine)    ← 同步路径
  │     ├── IExecutionOrchestrator
  │     │     └── DefaultExecutionOrchestrator
  │     │           └── IStepExecutor
  │     │                 └── StepExecutor (Function/Condition/Loop/Parallel)
  │     ├── ISessionManager
  │     └── IExecutionLogger
  │
  └── MatrixEngine (ITaskExecutorPool)      ← 异步事件路径（新增）
        └── EcsWorld
              ├── SchedulerSystem: Pending → Running
              ├── RouterSystem: 路由到 Handler
              ├── RetrySystem: 重试/死信
              └── MonitorSystem: 指标采集
```

### 2.3 旧命名空间引用清单

| 旧命名空间 / 类型 | 被引用文件（WorkFlowMethod 内） |
|-------------------|--------------------------------|
| `WorkflowExecution.Abstractions.IWorkflowLogger` | `WorkflowLogger.cs`, `WorkflowLoggerAdapter.cs`, `NodeExecutor.cs`, `WorkflowEngine.cs`, `RefactoredExecution.cs` |
| `WorkflowExecution.Abstractions.INodeExecutor` | `NodeExecutor.cs`, `CentralPipeline.cs`, `WorkflowMethodExecutionService.cs` |
| `WorkflowExecution.Abstractions.IWorkflowEngine` | （仅 TypeAliases 空接口） |
| `WorkflowExecution.Engine.HotReloadService` | `HotReloadService.cs`（Operation 层） |
| `WorkflowExecution.Engine.ReloadResult` | `HotReloadService.cs`（Operation 层） |
| `WorkflowExecution.Session.DefaultSessionFactory` | `SessionFactory.cs`（Operation 层） |
| `WorkflowExecution.Session.ExecutionSession` | `ExecutionSession.cs`（Operation 层） |
| `WorkflowExecution.Logging.WorkflowLogger` | `DebugLog.cs` |
| `WorkflowExecution.Models.ExecutionStatus` | （仅 TypeAliases 空枚举） |
| `WorkflowExecution.Models.NodeStatus` | （仅 TypeAliases 空枚举） |

---

## 三、分阶段迁移方案

### 第一阶段：命名空间迁移（无行为变更）

#### 3.1.1 目标
消除所有 `WorkflowExecution.*` 旧命名空间引用，更新为对应的 `ExecutionFramework.*` / `TConfig.*`。

#### 3.1.2 变更清单

| 文件名 | 变更内容 |
|--------|----------|
| **TypeAliases.cs** | 保留 TODO 注释，但**在此之前所有调用方已更新完毕后删除此文件** |
| **Infrastructure/WorkflowLogger.cs** | `IWorkflowLogger` → `IExecutionLogger` |
| **Infrastructure/WorkflowLoggerAdapter.cs** | `IWorkflowLogger` → `IExecutionLogger` |
| **WorkflowExecution/Engine/WorkflowEngine.cs** | `IWorkflowLogger` → `IExecutionLogger` |
| **WorkflowExecution/Engine/NodeExecutor.cs** | `INodeExecutor` → `IStepExecutor`, `IWorkflowLogger` → `IExecutionLogger` |
| **WorkflowExecution/Execution/Session/ExecutionSession.cs** | `WorkflowExecution.Session.ExecutionSession` → `ExecutionFramework.Session.ExecutionSession` |
| **WorkflowExecution/Execution/Session/DebugLog.cs** | `WorkflowLogger` → `ExecutionLogger` |
| **WorkflowExecution/Execution/Engine/HotReloadService.cs** | `WorkflowExecution.Engine.HotReloadService/ReloadResult` → `ExecutionFramework.Engine.HotReloadService/ReloadResult` |
| **WorkflowExecution/Execution/Engine/SessionFactory.cs** | `WorkflowExecution.Session.DefaultSessionFactory` → `ExecutionFramework.Session.DefaultSessionFactory` |
| **WorkflowMethodExecutionService.cs** | `INodeExecutor` 接口注入移除具体类强制转换 |

#### 3.1.3 类型映射表

```
旧类型                             新类型
─────────────────────────────────────────────────
IWorkflowLogger                   IExecutionLogger (ExecutionFramework.Abstractions)
WorkflowLogger                    ExecutionLogger (ExecutionFramework.Logging)
INodeExecutor                     IStepExecutor (ExecutionFramework.Abstractions)
IWorkflowEngine                   IExecutionEngine (ExecutionFramework.Abstractions)
IWorkflowContext                  TConfig.IExecutionContext
WorkflowExecution.Session.ExecutionSession     ExecutionFramework.Session.ExecutionSession
WorkflowExecution.Session.DefaultSessionFactory ExecutionFramework.Session.DefaultSessionFactory
WorkflowExecution.Engine.HotReloadService      ExecutionFramework.Engine.HotReloadService
WorkflowExecution.Engine.ReloadResult          ExecutionFramework.Engine.ReloadResult
WorkflowExecution.Models.ExecutionStatus        TConfig.ExecutionStatus
WorkflowExecution.Models.NodeStatus             ExecutionFramework.Session.StepStatus
```

#### 3.1.4 预计变更

| 指标 | 数值 |
|------|------|
| 文件修改 | 9 个 |
| 代码行变更 | ~80 行（主要是 using + 类型名替换） |
| 行为变更 | 无 |
| 风险 | 低 |

---

### 第二阶段：执行路径统一

#### 3.2.1 目标
消除 `NodeExecutor.cs`（451行）中的自定义执行逻辑，统一委托给 `ExecutionFramework.Engine.StepExecutor`。

#### 3.2.2 当前 NodeExecutor 内部结构

```
NodeExecutor
  ├── Polly ResiliencePipeline (重试 + 超时)   ← 缓存
  ├── ExecuteFunctionNodeAsync                  ← 调用 IFunctionExecutor
  ├── ExecuteConditionNodeAsync                 ← 条件分支
  ├── ExecuteLoopNodeAsync                      ← Foreach / Count 循环
  └── ExecuteParallelNodeAsync                  ← Task.WhenAll
```

`StepExecutor` 中已有完全对应的实现：

```
StepExecutor
  ├── ExecuteFunctionStepAsync                  ← 调用 IFunctionExecutor
  ├── ExecuteConditionStepAsync                 ← 条件分支（TrueBranchCount 控制）
  ├── ExecuteLoopStepAsync                      ← 循环（MaxIterations 控制）
  └── ExecuteParallelStepAsync                  ← Task.WhenAll
```

#### 3.2.3 变更策略

**方案：包装 + 委托模式**

保持 `NodeExecutor` 作为 WorkFlowMethod 专有的包装层，内部委托给 `StepExecutor`，保留 Polly 弹性策略作为可选的额外层：

```csharp
// NodeExecutor.cs 改造后
public class NodeExecutor
{
    private readonly StepExecutor _stepExecutor;
    private readonly ResiliencePipelineCache _pipelineCache;

    public async Task<ExecutionResult> ExecuteNodeAsync(StepDefinition node, IExecutionContext ctx, CancellationToken ct)
    {
        // 可选：Polly 弹性管道包装
        if (NeedResilience(node))
        {
            var pipeline = _pipelineCache.GetOrBuild(node);
            return await pipeline.ExecuteAsync(ct2 => 
                new ValueTask<ExecutionResult>(_stepExecutor.ExecuteStepAsync(node, ctx, ct2)), ct);
        }
        return await _stepExecutor.ExecuteStepAsync(node, ctx, ct);
    }
}
```

#### 3.2.4 管线引用调整

`CentralPipeline.cs` 第 165 行调用 `nodeExecutor.ExecuteStepAsync(step, context, ct)`，其中 `INodeExecutor` 实际实现了 `IStepExecutor` 的显式接口。迁移后 `NodeExecutor` 的 `IStepExecutor` 显式实现直接委托给 `StepExecutor`：

```csharp
async Task<ExecutionResult> IStepExecutor.ExecuteStepAsync(StepDefinition step, IExecutionContext ctx, CancellationToken ct)
    => await _stepExecutor.ExecuteStepAsync(step, ctx, ct);
```

#### 3.2.5 预计变更

| 指标 | 数值 |
|------|------|
| 文件修改 | 3 个（NodeExecutor + WorkflowMethodExecutionService + CentralPipeline） |
| 代码行变更 | ~80 行 |
| 行为变更 | 执行栈变化但接口契约不变 |
| 风险 | 中 — 需要充分测试 Condition/Loop/Parallel 三种复合步骤类型 |

---

### 第三阶段：Matrix ECS 事件调度集成

#### 3.3.1 目标
为 `WorkflowMethodExecutionService` 增加异步事件驱动执行路径，将函数/工作流执行提交为 Matrix ECS Entity。

#### 3.3.2 新增类型

**① `WorkflowMethodTaskMessage` — 工作流方法任务消息**

```csharp
namespace WorkFlowMethod.Execution.Messages;

public class WorkflowMethodTaskMessage : TaskMessageBase
{
    public override string TaskType => "WorkflowMethod";
    public string FunctionName { get; init; }
    public string? WorkflowJson { get; init; }  // 序列化的工作流定义
    public Dictionary<string, object> Parameters { get; init; }
}
```

**② `IMatrixExecutionService` — 新增接口（可选）**

```csharp
public interface IMatrixExecutionService
{
    /// <summary>异步提交任务到 Matrix ECS</summary>
    Task<ITaskResponse> SubmitAsync(string functionName, Dictionary<string, object> parameters);
}
```

#### 3.3.3 集成方案

```
WorkflowMethodExecutionService
  ├── ExecuteFunctionAsync(string, Dictionary)  ← 同步路径（保持不变）
  │     └── (内部当前逻辑)
  │
  └── SubmitAsync(string, Dictionary)            ← 新增异步事件路径
        │
        ▼
  MatrixEngine.SubmitAsync
    │  创建 Entity (TaskArchetype.CreateRoutedTask)
    │  Payload: 序列化的 WorkflowMethodTaskMessage
    │  HandlerName: "WorkflowMethodHandler"
    │
    ▼
  EcsWorld.Update 循环
    ├── SchedulerSystem: Pending → Running (触发条件：依赖满足)
    ├── RouterSystem: 查找 Handler "WorkflowMethodHandler"
    │     └── async handler:
    │           ├── 反序列化 Payload → WorkflowMethodTaskMessage
    │           ├── 调用 FunctionExecutor.ExecuteAsync
    │           └── 写入 TaskResultComponent
    ├── RetrySystem: (若失败) Failed → Pending | Dead
    └── MonitorSystem: 采集指标
```

#### 3.3.4 注册 Handler

在 Composition Root 中注册：

```csharp
// MatrixEngine 初始化后
matrixEngine.RegisterHandler("WorkflowMethodHandler", async (entity, payload, em) =>
{
    var msg = JsonSerializer.Deserialize<WorkflowMethodTaskMessage>(payload.PayloadJson);
    var result = await executionService.ExecuteFunctionAsync(msg.FunctionName, msg.Parameters);
    em.AddComponent(entity, new TaskResultComponent { ResultValue = result.Value });
    return result.IsSuccess;
});
```

#### 3.3.5 预计变更

| 指标 | 数值 |
|------|------|
| 新增文件 | 1 个（WorkflowMethodTaskMessage）|
| 修改文件 | 2 个（WorkflowMethodExecutionService + Composition Root）|
| 行为变更 | 新增异步路径，同步路径保持 |
| 风险 | 低 |

---

### 第四阶段：清理工作

#### 3.4.1 删除清单

| 文件/代码 | 说明 |
|-----------|------|
| `TypeAliases.cs` | 第一阶段完成后所有旧命名空间引用已消除 |
| `WorkflowExecution/` 整个目录 | 代码迁移到 `ExecutionFramework` |
| `RefactoredExecution.cs` 中的旧代码 | 使用 `ExecutionFramework.Session` / `ExecutionFramework.Abstractions` |

#### 3.4.2 csproj 清理

检查 `WorkFlowMethod.csproj` 中可能因 ExecutionFramework 间接引用而不再需要的直接引用（不做硬性要求，仅当确认无外部引用时清理）：

```xml
<!-- 以下引用可能可通过 ExecutionFramework 间接获得 -->
<ProjectReference Include="..\..\Modules\EventFramework\EventFramework.csproj" />
<ProjectReference Include="..\..\Modules\Registry\Registry.csproj" />
```

#### 3.4.3 预计变更

| 指标 | 数值 |
|------|------|
| 删除文件 | ~5 个 |
| 代码行删除 | ~400 行 |

---

## 四、文件变更总表

| 阶段 | 文件 | 操作 | 说明 |
|------|------|------|------|
| 一 | `TypeAliases.cs` | 保留 → 最终删除 | 等所有调用方迁移完成 |
| 一 | `WorkflowLogger.cs` | 修改 | `IWorkflowLogger` → `IExecutionLogger` |
| 一 | `WorkflowLoggerAdapter.cs` | 修改 | `IWorkflowLogger` → `IExecutionLogger` |
| 一 | `WorkflowEngine.cs` | 修改 | `IWorkflowLogger` → `IExecutionLogger` |
| 一 | `NodeExecutor.cs` | 修改 | `INodeExecutor` → `IStepExecutor`, `IWorkflowLogger` → `IExecutionLogger` |
| 一 | `ExecutionSession.cs` | 修改 | 旧命名空间 → 新命名空间 |
| 一 | `DebugLog.cs` | 修改 | `WorkflowLogger` → `ExecutionLogger` |
| 一 | `HotReloadService.cs` | 修改 | 旧命名空间 → 新命名空间 |
| 一 | `SessionFactory.cs` | 修改 | 旧命名空间 → 新命名空间 |
| 一 | `WorkflowMethodExecutionService.cs` | 修改 | `INodeExecutor` 强制转换移除 |
| 二 | `NodeExecutor.cs` | 重构 | 委托给 `StepExecutor` |
| 二 | `WorkflowMethodExecutionService.cs` | 调整 | 适配新的 `NodeExecutor` |
| 二 | `CentralPipeline.cs` | 适配 | `INodeExecutor` → `IStepExecutor` |
| 三 | `WorkflowMethodTaskMessage.cs` | 新增 | Matrix ECS 消息类型 |
| 三 | `WorkflowMethodExecutionService.cs` | 增强 | 新增 `SubmitAsync` |
| 三 | Composition Root | 修改 | 注册 Matrix Handler |
| 四 | `TypeAliases.cs` | 删除 | 最终清理 |
| 四 | `WorkflowExecution/` 目录 | 删除 | 旧命名空间目录 |
| 四 | `RefactoredExecution.cs` | 部分清理 | 更新引用 |

---

## 五、风险与注意事项

### 5.1 风险矩阵

| 风险 | 等级 | 缓解措施 |
|------|------|----------|
| `IWorkflowLogger` 的 `LogError` 签名差异（多一个 `Exception?` 参数） | 低 | 编译时直接捕获，签名已兼容 |
| `IStepExecutor` 与 `NodeExecutor` 的 Exception 处理策略不同 | 中 | 第二阶段充分测试复合步骤 |
| Matrix ECS 路径引入异步延迟 | 低 | 同步路径保持，异步路径可选 |
| `StepExecutor` 缺少 `Polly` 弹性策略 | 中 | `NodeExecutor` 保留 Polly 包装层 |
| 第三方的依赖项目仍在引用旧命名空间 | 低 | 保留 `TypeAliases.cs` 直到确认零外部引用 |

### 5.2 测试要求

1. **第一阶段**：构建通过（0 错误 0 警告）即可
2. **第二阶段**：
   - 单函数执行（Function 类型）结果与迁移前一致
   - Condition/Loop/Parallel 复合步骤执行结果一致
   - Polly 重试 + 超时行为一致
3. **第三阶段**：
   - 同步 ExecuteFunctionAsync 无变化
   - 异步 SubmitAsync 正常完成并返回结果
   - SchedulerSystem 优先级调度正确
   - RetrySystem 重试/死信逻辑正确

---

## 六、实施时间线

| 阶段 | 预计工时 | 并行度 |
|------|---------|--------|
| 一：命名空间迁移 | 30 分钟 | 可并行 |
| 二：执行路径统一 | 2 小时 | 不可并行 |
| 三：Matrix ECS 集成 | 1.5 小时 | 不可并行 |
| 四：清理 | 30 分钟 | 可并行 |
| **总计** | **~5 小时** | |

---

## 七、参考资源

- `[CR: CsharpPrj/Modules/ExecutionFramework/]` — 目标框架源码
- `[CR: CsharpPrj/Operation/WorkFlowMethod/WorkflowExecution/]` — 待迁移源码
- `[CR: CsharpPrj/Modules/ExecutionFramework/ExecutionCenter/Matrix/]` — ECS 矩阵源码
- `[CR: docs/Refactoring/ExecutionCenter_ECS运算矩阵重构方案.md]` — ECS 矩阵方案
- `[CR: docs/analysis/CLI_GUI执行路径重构总结.md]` — 执行路径重构总结