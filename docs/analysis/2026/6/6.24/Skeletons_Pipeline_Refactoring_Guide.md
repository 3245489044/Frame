# Skeletons → Modules/Pipeline 重构迁移指南

## 概述

本文档提供将 `Operation/WorkFlowMethod/Skeletons/` 基于 `SkeletonFlow` 的架构优化重构为基于 `Modules/Pipeline` 的 `StagePipeline` 体系的具体操作指南。

## 架构概览

```
┌─────────────────────────────────────────────────────────────┐
│                    重构前（SkeletonFlow）                      │
│                                                             │
│  WorkflowCentralPipeline                                    │
│    ├── SkeletonRegistry (阶段注册)                            │
│    ├── SkeletonFactory (Skeleton 工厂)                       │
│    └── ISkeleton<WorkflowContext, WorkflowContext> (管线)     │
│          ├── GraphDefinitionSkeleton                         │
│          ├── NodeProcessSkeleton                             │
│          ├── MetricsCollectSkeleton                          │
│          └── ...                                             │
│                                                             │
│  每个 Skeleton 继承 SkeletonBase<WorkflowContext, WC>        │
│  手动 try-catch 异常处理                                     │
│  手动注册到 WorkflowCentralPipeline                          │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│                    重构后（Modules/Pipeline）                  │
│                                                             │
│  WorkflowStagePipeline (适配层)                              │
│    ├── StagePipeline<WorkflowPipelineContextAdapter>         │
│    │     ├── OrchestrationInitStage (PipelineStageBase)      │
│    │     ├── OrchestrationPauseStage (PipelineStageBase)     │
│    │     ├── SkeletonStageAdapter (旧 Skeleton 包装)          │
│    │     └── ...                                             │
│    ├── 自动异常处理 (PipelineStageBase 内置)                  │
│    ├── 动态阶段管理 (InsertBefore/After/Remove)              │
│    └── 双向兼容 (ToSkeleton / FromCentralPipeline)           │
│                                                             │
│  WorkflowPipelineContextAdapter (适配器)                     │
│    └── 包装 WorkflowContext → IPipelineContext               │
└─────────────────────────────────────────────────────────────┘
```

## 文件结构

```
Operation/WorkFlowMethod/Skeletons/
├── Adapters/                                    # 🆕 新增适配层
│   ├── WorkflowPipelineContextAdapter.cs        # WorkflowContext → IPipelineContext 适配器
│   ├── WorkflowStagePipeline.cs                 # StagePipeline 构建器 + SkeletonStageAdapter
│   ├── PipelineStageMapping.cs                  # 枚举冲突解决映射
│   ├── PipelineBuilderExtensions.cs             # PipelineBuilder 扩展方法
│   ├── OrchestrationStages.cs                   # PipelineStageBase 子类（迁移目标）
│   └── UsageExamples.cs                         # 使用示例
├── Orchestration/                               # 现有（保持不变）
├── WorkflowProcessor/                           # 现有（保持不变）
├── Loop/                                        # 现有（保持不变）
├── UpdateCheck/                                 # 现有（保持不变）
├── GraphDefinition/                             # 现有（保持不变）
├── NodeProcess/                                 # 现有（保持不变）
├── PostProcess/                                 # 现有（保持不变）
├── PipelineBuilder.cs                           # 现有（保持不变）
└── WorkflowContextExtensions.cs                 # 现有（保持不变）
```

## 迁移步骤

### 第一阶段：基础适配（无侵入）

**目标**：建立适配层，不修改任何现有代码。

1. ✅ 创建 `WorkflowPipelineContextAdapter` — 将 `WorkflowContext` 适配为 `IPipelineContext`
2. ✅ 创建 `PipelineStageMapping` — 解决 `PipelineStage` 枚举冲突
3. ✅ 创建 `WorkflowStagePipeline` — 基于 `StagePipeline` 的构建器
4. ✅ 创建 `SkeletonStageAdapter` — 将旧 `ISkeleton` 包装为 `IPipelineStage`

**验证方法**：
```csharp
// 旧体系正常运行
using var oldPipeline = new WorkflowCentralPipeline("Test");
PipelineBuilder.ConfigureDefaultPipeline(oldPipeline, graph);
var result = await oldPipeline.ExecuteAsync(context);

// 新体系可用
var stagePipeline = oldPipeline.ToStagePipeline();
await stagePipeline.ExecuteAsync(context);
```

### 第二阶段：Orchestration 迁移（高价值）

**目标**：将 12 个 Orchestration Skeleton 迁移为 `PipelineStageBase` 子类。

**迁移对照表**：

| 旧 Skeleton | 新 Stage | 改进 |
|------------|---------|------|
| `OrchestrationInitSkeleton` | `OrchestrationInitStage` | 自动异常处理，代码减少 60% |
| `OrchestrationPauseSkeleton` | `OrchestrationPauseStage` | 自动异常处理，代码减少 50% |
| `MetricsCollectSkeleton` | `OrchestrationMetricsStage` | 自动异常处理，代码减少 70% |
| `OrchestrationRecordSkeleton` | `OrchestrationRecordStage` | 自动异常处理，代码减少 60% |
| `OrchestrationStorageSkeleton` | `OrchestrationStorageStage` | 自动异常处理，代码减少 50% |
| `OrchestrationConfigSkeleton` | `OrchestrationConfigStage` | 自动异常处理，代码减少 50% |
| `OrchestrationBridgeSkeleton` | `OrchestrationBridgeStage` | 自动异常处理，代码减少 50% |
| `WorkflowChainUpdateSkeleton` | `OrchestrationChainUpdateStage` | 自动异常处理，代码减少 50% |
| `PostProcessSkeleton` | `OrchestrationPostProcessStage` | 自动异常处理，代码减少 60% |

**迁移模式**：
```csharp
// 旧方式
public class OldSkeleton : SkeletonBase<WorkflowContext, WorkflowContext>
{
    protected override async Task<WorkflowContext> ExecuteCoreAsync(
        WorkflowContext input, SkeletonContext context)
    {
        try
        {
            // 业务逻辑
            return input;
        }
        catch (Exception ex)
        {
            // 手动处理
            throw;
        }
    }
}

// 新方式
public class NewStage : PipelineStageBase<WorkflowPipelineContextAdapter>
{
    protected override Task ExecuteCoreAsync(
        WorkflowPipelineContextAdapter context,
        CancellationToken cancellationToken)
    {
        var wfContext = context.InnerContext;
        // 业务逻辑 — 无需 try-catch
        return Task.CompletedTask;
    }
}
```

### 第三阶段：PipelineBuilder 重构

**目标**：将 `PipelineBuilder` 的注册逻辑迁移到 `StagePipeline`。

**迁移对照**：
```csharp
// 旧方式
public static void ConfigureDefaultPipeline(WorkflowCentralPipeline pipeline, IGraph graph)
{
    var registry = pipeline.Registry;
    registry.Insert(PipelineStage.GraphDefinition, new GraphDefinitionSkeleton(graph));
    registry.Insert(PipelineStage.FunctionExec, new NodeProcessSkeleton());
    registry.Insert(PipelineStage.PostProcess, new MetricsCollectSkeleton());
    registry.Insert(PipelineStage.UpdateCheck, new GraphUpdateCheckSkeleton());
    pipeline.BuildPipeline();
}

// 新方式
var stagePipeline = new WorkflowStagePipeline("Default")
    .Register(new OrchestrationInitStage())
    .Register(new OrchestrationPauseStage())
    .Register(new OrchestrationConfigStage())
    .Register(new OrchestrationRecordStage())
    .Register(new OrchestrationMetricsStage())
    .Register(new OrchestrationStorageStage())
    .Register(new OrchestrationPostProcessStage());
await stagePipeline.ExecuteAsync(context);
```

### 第四阶段：组合子优化

**目标**：利用 `PipelineExtensions` 的 `ForEach/Parallel/Branch/Retry/TryCatch` 优化循环和分支逻辑。

**循环优化**：
```csharp
// 旧方式：OrchestrationLoopSkeleton + LoopEngine + 状态机
var loopEngine = new LoopEngine<WorkflowLoopContext, LoopState>(...);
var loopResult = await loopEngine.ExecuteAsync(loopContext);

// 新方式：ForEachPipeline
var forEachPipeline = new ForEachPipeline<WorkflowPipelineContextAdapter, string>(
    ctx => ctx.InnerContext.GraphSnapshot?.NodeKeys.ToList() ?? new List<string>(),
    new SkeletonStageAdapter("NodeProcess", new NodeProcessSkeleton()));
await forEachPipeline.ExecuteAsync(adapter);
```

**条件分支优化**：
```csharp
// 旧方式：DelegateSkeleton + Branch
var shouldUpdate = new DelegateSkeleton<WorkflowContext, bool>(...);
var branchPipeline = shouldUpdate.Branch(updateBranch, noopBranch);

// 新方式：PipelineBranch
var branch = new PipelineBranch<WorkflowPipelineContextAdapter>(
    ctx => ctx.InnerContext.Session?.Data.Get<bool>("NeedsGraphUpdate") ?? false,
    new OrchestrationChainUpdateStage(),
    new DelegateStage("NoOp", ctx => Task.CompletedTask));
```

## 验证清单

### 功能等价性验证
- [ ] 旧 `WorkflowCentralPipeline.ExecuteAsync` 结果与新 `WorkflowStagePipeline.ExecuteAsync` 结果一致
- [ ] 暂停/恢复功能在新旧体系中行为一致
- [ ] 指标收集数据一致
- [ ] 节点执行记录一致

### 性能验证
- [ ] 新体系执行时间不超过旧体系的 110%
- [ ] 内存占用不显著增加
- [ ] 并发执行正确性

### 兼容性验证
- [ ] 旧代码无需修改即可继续使用
- [ ] 新旧体系可以混合使用
- [ ] 动态阶段管理不影响已有功能

## 回滚方案

如果迁移过程中出现问题，可以随时回滚：

1. **删除适配层**：删除 `Adapters/` 目录下的所有文件
2. **恢复旧体系**：`PipelineBuilder.cs` 等原有文件保持不变
3. **验证**：旧体系代码无需任何修改即可继续使用

## 收益总结

| 指标 | 旧体系 | 新体系 | 改进 |
|------|-------|-------|------|
| 代码量 | 12 个 Skeleton × ~80 行 | 12 个 Stage × ~30 行 | **减少 60%** |
| 异常处理 | 手动 try-catch | 自动处理 | **消除重复代码** |
| 阶段管理 | 静态注册 | 动态 Insert/Remove | **新增能力** |
| 组合能力 | Then/Branch/Loop | + ForEach/Parallel/Retry/TryCatch | **翻倍** |
| 可测试性 | 需启动完整管道 | 独立测试每个 Stage | **大幅提升** |
| 迁移风险 | - | 适配器模式，零侵入 | **无风险** |
