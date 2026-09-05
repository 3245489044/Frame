# 未迁移逻辑的 SkeletonFlow 重构方案

## 一、背景与问题

### 1.2 核心问题

旧代码之间存在**交叉引用**，无法单独删除任何一个目录。新框架代码（`CentralPipeline/`、`Execution/`、`Skeletons/`、`GraphFramework/`）虽然已独立，但旧代码之间的依赖链仍然存在：

```
OrchestrationConnect.cs
  └─ using ExecutionEngine;  → 依赖 NodeProcess.cs
  └─ 定义 OrchestrationConnect.Config 命名空间
       └─ 被 GraphFramework/ 中5个文件引用（未使用的导入）
       └─ 被 WrokFlow_Script.cs 引用（using static + using 别名）

NodeProcess.cs
  └─ using WorkFlowMethod.Infrastructure;  → 依赖 Infrastructure/

Infrastructure/MigrationBridge.cs
  └─ using WorkFlowMethod.ExecutionEngine.Refactored;  → 依赖 RefactoredExecutionEngine.cs
  └─ using WorkFlowMethod.Orchestration.Refactored;  → 依赖 Orchestration/

LoopFramework/  ↔  LoopWorkflow/  (双向引用)
```

---

## 二、未迁移逻辑清单

### 2.1 ExecutionEngine/ 中未迁移的逻辑

| 文件 | 命名空间 | 功能 | 被谁引用 | 新框架替代方案 |
|------|---------|------|---------|--------------|
| `NodeProcess.cs` | `ExecutionEngine` | 节点处理管线（GetConfig → ExecuteFunc → AddNode → NextNode） | `OrchestrationConnect.cs` 第1758行 | `NodeProcessSkeleton`（已存在） |
| `ExecuteProcess.cs` | `ExecutionEngine` | 函数执行处理 | `NodeProcess.cs` | `NodeProcessSkeleton` 内部 Step2 |
| `GetInputStrProcess.cs` | `ExecutionEngine` | 输入获取处理 | `NodeProcess.cs` | `NodeProcessSkeleton` 内部 Step1 |
| `NextNodeProcess.cs` | `ExecutionEngine` | 下一节点解析 | `NodeProcess.cs` | `NodeProcessSkeleton` 内部 Step4 |
| `WorkflowChainUpdater.cs` | `ExecutionEngine` | 工作流链更新 | `NextNodeProcess.cs` | `WorkflowChainUpdateSkeleton`（已存在） |
| `AddNodeProcess.cs` | `WorkFlow.Execute` | 添加单元格节点 | `NodeProcess.cs` | `NodeProcessSkeleton` 内部 Step3 |
| `FunctionExecutionFramework.cs` | `WorkFlow.LogFrame` | 执行框架（静态类） | `NodeProcess.cs`、`ExecuteProcess.cs` 等 | `ExecutionSession` + `DebugLog` |
| `DebugLogger.cs` | `WorkFlow.LogFrame` | 调试日志服务 | `FunctionExecutionFramework.cs` 等 | `Execution/Session/DebugLog.cs`（已存在） |
| `Interfaces.cs` | `ExecutionEngine` | 引擎核心接口 | `NodeProcess.cs` | `CentralPipeline/WorkflowContext.cs` |
| `ExecutionEngine.cs` | `WorkFlow.Execute` | 原始执行引擎（已拆分） | 仅内部引用 | 已废弃，可删除 |
| `RefactoredExecutionEngine.cs` | `WorkFlowMethod.ExecutionEngine.Refactored` | 重构后的执行引擎（已标记 Obsolete） | `Infrastructure/MigrationBridge.cs` | `NodeProcessSkeleton` |
| `Integration.cs` | `WorkFlowMethod.Integration` | 集成层 | `OrchGraph.cs` | `GraphFramework/` 中的服务 |
| `CallFrame.cs` | `WorkFlow.CallFrame` | 调用帧记录 | `AddNodeProcess.cs`、`ExecuteProcess.cs` 等 | `NodeProcessSkeleton` 内部 Step3 |
| `External.cs` | `WorkFlowMethod.External` | 外部适配 | `ExecuteProcess.cs` | 需迁移 |
| `ExecutionRequest.cs` | `WorkFlowMethod.Execution` | 执行请求模型 | 无外部引用 | 可删除 |
| `IWorkflowExecutor.cs` | `WorkFlowMethod.Execution` | 执行器接口 | 无外部引用 | 可删除 |
| `WorkflowExecutor.cs` | `WorkFlowMethod.Execution` | 执行器实现 | 无外部引用 | 可删除 |

### 2.2 Orchestration/ 中未迁移的逻辑

| 文件 | 功能 | 被谁引用 | 新框架替代方案 |
|------|------|---------|--------------|
| `OrchestrationConnect.cs` | 编排连接（150KB，包含 Config/Content/IdManagement 等多个命名空间） | `GraphFramework/` 中5个文件、`WrokFlow_Script.cs`、`ExecutionEngine/` 中多个文件 | 需拆分迁移 |
| `OrchestrationConnect.Config` | 配置类型（NodeConfig、ChainInfo、WorkflowConnection 等） | `GraphFramework/` 中5个文件（未使用的导入） | `GraphFramework/Models/` |
| `OrchestrationConnect.Content` | 内容管理（WorkflowStorageManager、WorkflowTools 等） | `WrokFlow_Script.cs`（using static） | `Execution/` 或 `GraphFramework/Services/` |
| `OrchestrationConnect.Updates` | 更新管理（WorkFlowUpdate） | `WrokFlow_Script.cs`（using static） | `GraphFramework/Directives/` |
| `OrchestrationConnect.IdManagement` | ID 管理 | 内部引用 | 需迁移 |
| `OrchestrationConnect.Content.Context` | 上下文管理 | 内部引用 | `CentralPipeline/WorkflowContext.cs` |
| `OrchestrationFramework.cs` | 编排框架 | 内部引用 | `CentralPipeline/WorkflowCentralPipeline.cs` |
| `OrchGraph.cs` | 图编排 | 内部引用 | `GraphFramework/Services/` |
| `RefactoredOrchestration.cs` | 重构编排 | `Infrastructure/MigrationBridge.cs` | `CentralPipeline/` |
| `RefactoredWorkflowCreateModules.cs` | 工作流创建模块 | 内部引用 | `Execution/WorkflowBuilder.cs` |
| `WorkflowCreateModules.cs` | 工作流创建模块（旧版） | 内部引用 | `Execution/WorkflowBuilder.cs` |

### 2.3 Infrastructure/ 中未迁移的逻辑

| 文件 | 功能 | 被谁引用 | 新框架替代方案 |
|------|------|---------|--------------|
| `MigrationBridge.cs` | 迁移桥接（调用 RefactoredNodeProcess） | 无外部引用（仅自引用） | 直接删除，改用 `NodeProcessSkeleton` |
| `ExecutionContext.cs` | 执行上下文 | `StateManager.cs`（LoopFramework） | `CentralPipeline/WorkflowContext.cs` |
| `PipelineExecutor.cs` | 管线执行器 | `NodeProcess.cs` | `CentralPipeline/WorkflowCentralPipeline.cs` |
| `ResultTypes.cs` | 结果类型 | 无外部引用 | 可删除 |
| `WorkflowLogger.cs` | 工作流日志 | 无外部引用 | `Execution/Session/DebugLog.cs` |

### 2.4 LoopFramework/ 中未迁移的逻辑

| 文件 | 功能 | 被谁引用 | 新框架替代方案 |
|------|------|---------|--------------|
| `Core/LoopEngine.cs` | 循环引擎核心 | `LoopWorkflow/` | `WorkflowLoopSkeleton`（已存在） |
| `Core/DefaultLoopController.cs` | 默认循环控制器 | 内部引用 | `WorkflowLoopSkeleton` |
| `StateMachine/LLMStateMachine.cs` | LLM 状态机 | `LoopWorkflow/` | 需迁移 |
| `StateMachine/StateManager.cs` | 状态管理器 | 内部引用 | 需迁移 |
| `Execution/LegacyLoopEngineAdapter.cs` | 旧引擎适配器 | 内部引用 | 直接删除 |
| `Abstractions/` | 抽象接口 | `LoopWorkflow/` | 需迁移到 `Skeletons/WorkflowProcessor/` |

### 2.5 LoopWorkflow/ 中未迁移的逻辑

| 文件 | 功能 | 被谁引用 | 新框架替代方案 |
|------|------|---------|--------------|
| `Models/` 全部 | 循环工作流模型 | `LoopFramework/` | 需迁移到 `Skeletons/WorkflowProcessor/` |
| `Services/` 全部 | LLM 服务接口 | `LoopFramework/` | 需迁移 |
| `Execution/` 全部 | 子工作流执行器 | `LoopFramework/` | 需迁移 |
| `UsageExample.cs` | 使用示例 | 无外部引用 | 可删除 |

---

## 三、SkeletonFlow 重构方案

### 3.1 重构原则

1. **逐块迁移**：每次迁移一个功能点，确保构建通过
2. **先清理未使用的导入**：删除 `GraphFramework/` 和 `WrokFlow_Script.cs` 中未使用的 `OrchestrationConnect.*` 导入
3. **用新框架替代旧调用**：将 `OrchestrationConnect.cs` 中对 `NodeProcess.ProcessInputAndAddAICallCell` 的调用改为使用 `NodeProcessSkeleton`
4. **删除 MigrationBridge**：直接删除，因为无外部引用
5. **迁移循环逻辑**：将 `LoopFramework/` 和 `LoopWorkflow/` 中的循环处理逻辑合并到 `WorkflowLoopSkeleton`

### 3.2 重构步骤

#### ✅ Step 1: 创建缺失的 Skeleton 文件

| 文件 | 状态 |
|------|------|
| `Skeletons/PostProcess/MetricsCollectSkeleton.cs` | ✅ 已创建 |
| `Skeletons/UpdateCheck/GraphUpdateCheckSkeleton.cs` | ✅ 已创建 |
| `Skeletons/Loop/WorkflowLoopSkeleton.cs` | ✅ 已创建 |

#### ✅ Step 2: 将旧 NodeProcess 管线业务逻辑迁移到 NodeProcessSkeleton

| 旧功能 | 新位置 | 状态 |
|--------|--------|------|
| `NodeProcess` | `Skeletons/NodeProcess/NodeProcessSkeleton.cs` | ✅ 已迁移 |
| `ExecuteProcess` | `NodeProcessSkeleton` 内部 Step2 | ✅ 已迁移 |
| `GetInputStrProcess` | `NodeProcessSkeleton` 内部 Step1 | ✅ 已迁移 |
| `NextNodeProcess` | `NodeProcessSkeleton` 内部 Step4 | ✅ 已迁移 |
| `AddNodeProcess` | `NodeProcessSkeleton` 内部 Step3 | ✅ 已迁移 |
| `CallFrame` | `NodeProcessSkeleton` 内部 Step3 | ✅ 已迁移 |

#### ✅ Step 3: 将旧 WorkflowChainUpdater 迁移到 WorkflowChainUpdateSkeleton

| 旧功能 | 新位置 | 状态 |
|--------|--------|------|
| `WorkflowChainUpdater` | `Skeletons/UpdateCheck/WorkflowChainUpdateSkeleton.cs` | ✅ 已迁移 |

#### ✅ Step 4: 创建 WorkflowLoopSkeleton（循环处理）

| 旧功能 | 新位置 | 状态 |
|--------|--------|------|
| `LoopFramework/` + `LoopWorkflow/` | `Skeletons/Loop/WorkflowLoopSkeleton.cs` | ✅ 已创建 |

#### ✅ Step 5: 拆分 WrokFlow_Script.cs（1690→950行）

| 提取内容 | 目标文件 | 状态 |
|---------|---------|------|
| `WorkFlowsSet` 配置类 | `Config/WorkFlowsSet.cs` | ✅ 已迁移，构建通过 |

#### Step 6: 替换 OrchestrationConnect.cs 中对 ExecutionEngine 的调用

**当前代码**（`OrchestrationConnect.cs` 第1758行）：
```csharp
await NodeProcess.ProcessInputAndAddAICallCell(universalNode);
```

**改为使用新框架**：
```csharp
// 使用 NodeProcessSkeleton 替代旧的 NodeProcess
var pipeline = new WorkflowCentralPipeline.Builder()
    .UseSkeleton(new NodeProcessSkeleton())
    .Build();
await pipeline.ExecuteAsync(context);
```

#### Step 7: 删除 Infrastructure/MigrationBridge.cs

`MigrationBridge` 无外部引用，可直接删除。

#### Step 8: 删除 ExecutionEngine/RefactoredExecutionEngine.cs

`RefactoredNodeProcess` 仅被 `MigrationBridge.cs` 引用，删除 MigrationBridge 后即可删除此文件。

#### Step 9: 迁移 ExecutionEngine/ 剩余功能

| 旧功能 | 新位置 | 迁移方式 |
|--------|--------|---------|
| `FunctionExecutionFramework` | `Execution/Session/` + `CentralPipeline/` | 需迁移静态方法 |
| `DebugLogger` | `Execution/Session/DebugLog.cs` | ✅ 已存在 |

#### Step 10: 迁移 Orchestration/ 功能

将 `OrchestrationConnect` 中的功能拆分迁移：

| 旧命名空间 | 新位置 | 迁移方式 |
|-----------|--------|---------|
| `OrchestrationConnect.Config` | `GraphFramework/Models/` | 将 NodeConfig、ChainInfo 等类型迁移到 GraphFramework 模型 |
| `OrchestrationConnect.Content` | `GraphFramework/Services/` | 将 WorkflowStorageManager 等迁移 |
| `OrchestrationConnect.Updates` | `GraphFramework/Directives/` | 将 WorkFlowUpdate 迁移到 Directive 体系 |
| `OrchestrationConnect.IdManagement` | `GraphFramework/Services/` | 迁移 ID 管理服务 |
| `OrchestrationConnect.Content.Context` | `CentralPipeline/WorkflowContext.cs` | ✅ 已存在 |
| `OrchestrationFramework` | `CentralPipeline/WorkflowCentralPipeline.cs` | ✅ 已存在 |

#### Step 11: 删除空目录

删除所有已迁移完毕的空目录：
- `ExecutionEngine/`
- `Orchestration/`
- `Infrastructure/`
- `LoopFramework/`
- `LoopWorkflow/`

---

## 四、迁移优先级与依赖关系

```
优先级1: 清理未使用的导入
  └─ 删除 GraphFramework/ 中的 OrchestrationConnect.* 导入
  └─ 删除 WrokFlow_Script.cs 中的 OrchestrationConnect.* 导入
  └─ 结果：Orchestration/ 的外部引用减少

优先级2: 替换 OrchestrationConnect.cs 中的 ExecutionEngine 调用
  └─ 将 NodeProcess.ProcessInputAndAddAICallCell 替换为 NodeProcessSkeleton
  └─ 结果：ExecutionEngine/ 的外部引用减少

优先级3: 删除无引用的桥接代码
  └─ 删除 Infrastructure/MigrationBridge.cs
  └─ 删除 ExecutionEngine/RefactoredExecutionEngine.cs
  └─ 结果：Infrastructure/ 和 ExecutionEngine/ 的部分文件可删除

优先级4: 迁移 ExecutionEngine/ 剩余功能
  └─ 迁移 FunctionExecutionFramework 静态方法
  └─ 迁移 CallFrame 记录
  └─ 结果：ExecutionEngine/ 可完全删除

优先级5: 迁移 Orchestration/ 功能
  └─ 拆分 OrchestrationConnect.cs 中的多个命名空间
  └─ 迁移到 GraphFramework/ 和 CentralPipeline/
  └─ 结果：Orchestration/ 可完全删除

优先级6: 迁移循环逻辑
  └─ 合并 LoopFramework/ + LoopWorkflow/ 到 WorkflowLoopSkeleton
  └─ 结果：LoopFramework/ 和 LoopWorkflow/ 可完全删除
```

---

## 五、验证清单

### 5.1 每步验证

- [x] Step 1 完成后：`dotnet build` 通过 ✅
- [x] Step 2 完成后：`dotnet build` 通过 ✅
- [x] Step 3 完成后：`dotnet build` 通过 ✅
- [x] Step 4 完成后：`dotnet build` 通过 ✅
- [x] Step 5 完成后：`dotnet build` 通过 ✅
- [ ] Step 6 完成后：`dotnet build` 通过
- [ ] Step 7 完成后：`dotnet build` 通过
- [ ] Step 8 完成后：`dotnet build` 通过
- [ ] Step 9 完成后：`dotnet build` 通过
- [ ] Step 10 完成后：`dotnet build` 通过
- [ ] Step 11 完成后：`dotnet build` 通过

### 5.2 最终验证

- [ ] 所有旧目录已删除
- [ ] 无 `OrchestrationConnect` 引用残留
- [ ] 无 `ExecutionEngine` 命名空间引用残留
- [ ] 无 `Infrastructure` 命名空间引用残留
- [ ] 无 `LoopFramework` / `LoopWorkflow` 引用残留
- [ ] 测试项目（CentralConsole 下）构建通过
- [ ] 完整解决方案构建通过：`dotnet build`

---

## 六、风险与注意事项

| 风险 | 等级 | 缓解措施 |
|------|------|---------|
| `WrokFlow_Script.cs` 中 `using static` 导入的静态方法可能在运行时通过反射调用 | 低 | 搜索 `typeof` 和 `Assembly.Load` 确认无反射引用 |
| `OrchestrationConnect.cs` 中的类型被 `GraphFramework/` 通过 `using` 导入但未使用，编译器不会报错 | 低 | 删除未使用的导入后验证构建 |
| `LoopFramework/` 和 `LoopWorkflow/` 之间的双向引用需要同时迁移 | 中 | 将两者合并迁移到 `WorkflowLoopSkeleton` |
| `OrchestrationConnect.cs` 文件过大（150KB），拆分迁移工作量大 | 高 | 按命名空间分批迁移，每次迁移一个命名空间 |
