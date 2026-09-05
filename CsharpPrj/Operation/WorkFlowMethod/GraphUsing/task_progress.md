# GraphFramework 分类整合方案B - 执行进度

## 执行步骤

- [ ] 步骤1: Directives/ 合并 - 将5个小文件合并到 GraphUpdateDirective.cs
  - ✅ WorkFlowUpdate.cs 已删除（无外部引用，功能已迁移到 GraphUpdateSkeleton）
- [ ] 步骤2: KeyGeneration/ 合并 - KeyManager.cs 合并到 KeyProcessor.cs
  - ✅ KeyManager 代码已合并到 KeyProcessor.cs（步骤标记未更新）
- [ ] 步骤3: Pipeline/ 合并 - GraphPipelineStep.cs + GraphWorkflowConnector.cs 合并到 GraphPipelineSteps.cs
  - ⚠️ 有外部引用（WorkflowCreateModules.cs、NodeManager.cs），暂不能删除
- [ ] 步骤4: Services/ 合并 - GraphUpdateService.cs 合并到 DirectiveApplier.cs
  - ⚠️ 有外部引用（WorkflowChainUpdateSkeleton.cs），暂不能删除
- [ ] 步骤5: SkeletonBridge/ 合并 - 7个文件按功能合并为2个文件
  - ⚠️ 各文件功能独立，合并需谨慎
- [ ] 步骤6: 更新外部引用
- [ ] 步骤7: 删除已合并的源文件

## 已完成的清理

### GraphFramework/ 目录
- ✅ 删除 `Directives/WorkFlowUpdate.cs`（旧版兼容层，功能已迁移）
- ✅ 删除 `Updates/GraphTransformers.cs`（DynamicUpdateTransformer 等类功能重复，无外部引用）
- ✅ 删除 `Updates/` 空目录
- ✅ 更新 `GraphUpdateSkeleton.cs` 注释（移除对已删除文件的引用）

### ExecutionEngine/ 目录
- ✅ 删除 `WorkflowChainUpdater.cs`（空壳文件，仅注释引用）
- ✅ 删除 `RefactoredExecutionEngine.cs`（RefactoredNodeProcess 废弃重复实现）
- ✅ 删除 `NextNodeProcess.cs`（空壳文件，引用已更新）
- ✅ 更新 `MigrationBridge.cs`（引用从 RefactoredNodeProcess 改为 NodeProcess）
- ✅ 删除 `NodeProcess.cs` 中的 `ExecutionPipeline<T>`（废弃内部类）
