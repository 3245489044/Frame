# WorkFlowMethod 代码整理任务清单

## 第一阶段：P0 级修复

### 步骤 1：修复 KeyProcessor 异常类型擦除（P1）
- [ ] 修改 `NodeProcess/KeyMapping/KeyProcessor.cs` 中的异常处理，保留原始异常类型

### 步骤 2：修复 WorkflowUseage.CallBridgedFunction 同步包装异步（P2）
- [ ] 标记 `CallBridgedFunction` 为 `[Obsolete]`，推荐使用 `CallBridgedFunctionAsync`

### 步骤 3：修复 WorkflowBuilder 中内联的 ExpandoObject 转换（P2）
- [ ] 将 `WorkflowBuilder.cs:108-121` 的内联转换替换为 `ExpandoObjectHelper` 调用

## 第二阶段：命名空间统一

### 步骤 4：统一 WorkFlow → WorkFlowMethod.Execution（4 个文件）
- [ ] `Config/WorkFlowsSet.cs` → `WorkFlowMethod.Config`
- [ ] `Execution/Builders/WorkflowFactory.cs` → `WorkFlowMethod.Execution.Builders`
- [ ] `Execution/WorkflowBuilder.cs` → `WorkFlowMethod.Execution`
- [ ] `WorkflowUseage.cs` → `WorkFlowMethod`

### 步骤 5：统一 Orchestration.Content → 正确目录命名空间（5 个文件）
- [ ] `Execution/Builders/NodeIdentityGenerator.cs` → `WorkFlowMethod.Execution.Builders`
- [ ] `Execution/Builders/WorkflowTools.cs` → `WorkFlowMethod.Execution.Builders`
- [ ] `Execution/Chain/WorkflowMediator.cs` → `WorkFlowMethod.Execution.Chain`
- [ ] `Execution/Chain/WorkflowRegistrar.cs` → `WorkFlowMethod.Execution.Chain`
- [ ] `Execution/Serialization/WorkflowDeserializer.cs` → `WorkFlowMethod.Execution.Serialization`

### 步骤 6：统一其他命名空间不匹配（5 个文件）
- [ ] `Execution/RefactoredExecution.cs` → `WorkFlowMethod.Execution`
- [ ] `Orchestration/Validation.cs` → `WorkFlowMethod.Orchestration`
- [ ] `Orchestration/State/Identity.cs` → `WorkFlowMethod.Orchestration.State`
- [ ] `WorkFlowPipeline/PipelineStages.cs` → `WorkFlowMethod.WorkFlowPipeline`
- [ ] `Execution/Data/DataContext.cs` → `WorkFlowMethod.Execution.Data`
- [ ] `Infrastructure/DefaultBridgeContext.cs` → `WorkFlowMethod.Infrastructure`
- [ ] `Infrastructure/WorkflowConstants.cs` → `WorkFlowMethod.Infrastructure`

## 第三阶段：清理冗余代码

### 步骤 7：删除纯委托/空壳类
- [ ] 评估 `WorkflowLoggerAdapter` 是否可删除
- [ ] 评估 `DefaultBridgeContext` 空壳实现
- [ ] 删除 `ExpandoObjectHelper.CreateExpandoFromDictionary`（已标记删除）

### 步骤 8：清理 [Obsolete] 标记
- [ ] `Orchestration/State/Identity.cs:140` — 确认引用后处理

## 第四阶段：架构整理

### 步骤 9：统一 Context 命名空间（去掉 Orchestration 前缀）
- [ ] 所有 `WorkFlowMethod.Orchestration.Context.*` → `WorkFlowMethod.Context.*`
- [ ] 更新所有引用

### 步骤 10：编译验证
- [ ] 每次修改后编译验证
