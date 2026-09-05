# WorkflowCentralPipeline 迁移方案 — 工作流图管线迁移到通用管线框架

## 1. 现状分析

### 1.1 WorkflowCentralPipeline（工作流图执行）

| 项目 | 内容 |
|------|------|
| 位置 | `Share/WorkFlowMethod/CentralPipeline/` |
| 上下文类型 | `WorkflowContext` |
| 阶段定义 | `GraphDefinition` → `FunctionExec` → `PostProcess` → `UpdateCheck` |
| 注册机制 | `SkeletonRegistry` 动态注册 + `SkeletonFactory` 工厂创建 |
| 构建方式 | `BuildPipeline()` 按阶段收集 Skeleton 并组合 |
| 用途 | 工作流图的节点执行 |

### 1.2 核心问题

1. **WorkflowCentralPipeline 与 FileDrivenPipeline 各自独立**：两者都基于 SkeletonFlow，但上下文类型不同，无法互通
2. **缺乏统一的管线构建器**：WorkflowCentralPipeline 有动态注册机制，但与其他管线模式不同
3. **框架复用性差**：两个管线体系各自实现了一套管线编排逻辑

---

## 2. 迁移目标

### 2.1 总体目标

**将 WorkflowCentralPipeline 从自定义管线体系迁移到通用管线框架（Share/CentralPipeline）。**

### 2.2 设计原则

1. **保持对外接口不变**：调用方无需感知内部变化
2. **渐进式迁移**：先构建适配层，再逐步替换内部实现
3. **兼容性优先**：保留 SkeletonRegistry 和 SkeletonFactory 的兼容层

---

## 3. 阶段映射

### 3.1 Workflow 阶段 → 通用阶段映射

```
WorkflowCentralPipeline 阶段 → 通用阶段映射：
  GraphDefinition  → PipelineStage.PreProcessing
  FunctionExec     → PipelineStage.Execution
  PostProcess      → PipelineStage.PostProcessing
  UpdateCheck      → PipelineStage.Finalization
```

### 3.2 通用阶段定义

通用阶段覆盖管线的完整生命周期：

```
PipelineStage 枚举：
  ├── Initialization    — 初始化阶段（日志系统、框架组件等）
  ├── PreProcessing     — 预处理阶段（脚本加载、配置加载等）
  ├── Execution         — 执行阶段（命令循环、函数执行等）
  ├── PostProcessing    — 后处理阶段（结果写入、日志记录等）
  └── Finalization      — 终结阶段（日志刷入、资源清理等）
```

---

## 4. 迁移步骤

### 4.1 重构 WorkflowCentralPipeline

```csharp
public class WorkflowCentralPipeline
{
    private readonly CentralPipeline<WorkflowContext> _innerPipeline;
    
    public WorkflowCentralPipeline(string? name = null)
    {
        _innerPipeline = new CentralPipeline<WorkflowContext>(name ?? "DefaultPipeline");
    }
    
    // 保持对外接口不变
    public PipelineExecutionResult Execute(WorkflowContext context)
    {
        var result = _innerPipeline.Execute(context);
        return new PipelineExecutionResult { ... };
    }
}
```

### 4.2 保留 SkeletonRegistry 兼容层

`SkeletonRegistry` 和 `SkeletonFactory` 保留，但内部委托给 `PipelineStageRegistry<WorkflowContext>`：

```csharp
public class SkeletonRegistry
{
    private readonly PipelineStageRegistry<WorkflowContext> _innerRegistry;
    
    public void Insert(PipelineStage stage, ISkeleton skeleton)
    {
        var mappedStage = MapToGenericStage(stage);
        _innerRegistry.Register(mappedStage, WrapSkeleton(skeleton));
    }
}
```

### 4.3 提供便捷入口

```csharp
public static class PipelineEntry
{
    public static CentralPipeline<WorkflowContext> CreateWorkflowPipeline()
    {
        return new PipelineBuilder<WorkflowContext>()
            .Register(PipelineStage.PreProcessing, new GraphDefinitionSkeleton())
            .Register(PipelineStage.Execution, new FunctionExecSkeleton())
            .Register(PipelineStage.PostProcessing, new PostProcessSkeleton())
            .Register(PipelineStage.Finalization, new UpdateCheckSkeleton())
            .Build();
    }
}
```

---

## 5. 架构对比

### 5.1 当前架构

```
┌─────────────────────────────────────────────────────────────┐
│  WorkflowCentralPipeline                                    │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  SkeletonRegistry (PipelineStage 枚举)                │   │
│  │  SkeletonFactory                                      │   │
│  │  BuildPipeline() → Flow<WorkflowContext>              │   │
│  └──────────────────────────────────────────────────────┘   │
│  上下文：WorkflowContext                                     │
├─────────────────────────────────────────────────────────────┤
│  SkeletonFlow 框架                                           │
│  SkeletonBase<TIn, TOut>, Flow<TIn, TOut>                   │
└─────────────────────────────────────────────────────────────┘
```

### 5.2 目标架构

```
┌─────────────────────────────────────────────────────────────┐
│  WorkFlowMethod.CentralPipeline (业务管线)                    │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  GraphDefinitionSkeleton, FunctionExecSkeleton, ...   │   │
│  │  SkeletonAdapter 适配到 WorkflowContext               │   │
│  └──────────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────────┤
│  Share/CentralPipeline (通用管线框架)                         │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  CentralPipeline<TContext>  — 泛型管线执行引擎         │   │
│  │  PipelineStageRegistry<TContext> — 按阶段注册表        │   │
│  │  PipelineBuilder<TContext>     — Fluent API 构建器     │   │
│  │  SkeletonAdapter<TContext,TIn,TOut> — 适配器           │   │
│  │  PipelineStage                — 通用阶段枚举           │   │
│  └──────────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────────┤
│  SkeletonFlow 框架                                           │
│  SkeletonBase<TIn, TOut>, Flow<TIn, TOut>                   │
└─────────────────────────────────────────────────────────────┘
```

---

## 6. 实施路线图

### 第一阶段：构建通用管线框架 ✅（已完成）

**目标**：在 `Share/CentralPipeline` 中创建通用管线框架，不修改任何现有代码。

```
Share/CentralPipeline/
  ├── Share.CentralPipeline.csproj       — 项目文件（引用 BasicMethod.Pipeline + BasicMethod）
  ├── PipelineStage.cs                   — 通用阶段枚举（转发到 BasicMethod.Pipeline）✅
  ├── PipelineStageRegistry.cs           — 按阶段注册表（基于 BP.PipelineStageRegistry）✅
  ├── CentralPipeline.cs                 — 通用管线执行引擎（基于 BP.IPipeline）✅
  ├── PipelineBuilder.cs                 — Fluent API 构建器 ✅
  ├── SkeletonAdapter.cs                 — 适配器（移除 new() 约束，修复死锁）✅
  └── SkeletonToPipelineAdapter.cs       — ISkeleton → IPipeline 桥接适配器 ✅
```

### 第一阶段重构：基础类型下沉到 BasicMethod.Pipeline ✅（已完成）

**目标**：将通用管线框架的基础类型下沉到 `BasicMethod.Pipeline`，实现分层解耦。

```
BasicMethod/Pipeline/
  ├── BasicMethod.Pipeline.csproj        — 独立项目文件（不依赖 SkeletonFlow）
  ├── 01_IPipeline.cs                    — IPipeline<TIn, TOut> 接口 ✅
  ├── 02_PipelineBase.cs                 — PipelineBase 抽象基类 ✅
  ├── 03_PipelineContext.cs              — PipelineContext 执行上下文 ✅
  ├── 04_PipelineExtensions.cs           — Then / Catch / Finally 扩展方法 ✅
  ├── 05_DelegatePipeline.cs             — 委托 Pipeline ✅
  ├── 06_CompositePipeline.cs            — 组合 Pipeline ✅
  ├── 07_LoggingPipeline.cs              — 日志 Pipeline ✅
  ├── 08_MetricsPipeline.cs              — 指标 Pipeline ✅
  ├── 09_RetryPipeline.cs                — 重试 Pipeline ✅
  ├── 10_TimeoutPipeline.cs              — 超时 Pipeline ✅
  ├── 11_CircuitBreakerPipeline.cs       — 熔断 Pipeline ✅
  ├── 12_PipelineBuilder.cs              — Pipeline 构建器 ✅
  ├── 13_PipelineStage.cs                — 通用阶段枚举 ✅
  ├── 14_PipelineStatus.cs               — 管线状态枚举 ✅
  └── 15_PipelineStageRegistry.cs        — 按阶段注册表（泛型）✅
```

### 架构分层（重构后）

```
┌─────────────────────────────────────────────────────────────┐
│  Share.CentralPipeline（SkeletonFlow 桥接层）                 │
│  CentralPipeline<TContext> — 基于 BP.IPipeline 的执行引擎    │
│  PipelineStageRegistry<TContext> — 基于 BP 注册表的 Skeleton │
│  SkeletonToPipelineAdapter — ISkeleton → IPipeline 桥接      │
│  SkeletonAdapter — 业务 Skeleton 适配器                      │
│  依赖：BasicMethod（间接引用 Pipeline.csproj + SkeletonFlow） │
├─────────────────────────────────────────────────────────────┤
│  BasicMethod（聚合项目）                                      │
│  引用：Pipeline/Pipeline.csproj（基础管线框架）                │
│  引用：Common、Logging、MetaCommand 等子项目                  │
│  包含：SkeletonFlow（FrameMethod/SkeletonFlow.cs）            │
├─────────────────────────────────────────────────────────────┤
│  BasicMethod.Pipeline (Pipeline/Pipeline.csproj)             │
│  IPipeline<TIn, TOut> — 管线接口                             │
│  PipelineBase — 抽象基类                                     │
│  PipelineContext — 执行上下文                                │
│  PipelineExtensions — Then/Catch/Finally                    │
│  PipelineStage — 通用阶段枚举                                │
│  PipelineStatus — 管线状态枚举                               │
│  PipelineStageRegistry<TContext> — 按阶段注册表               │
│  依赖：Common、UniversalLayoutMath                            │
└─────────────────────────────────────────────────────────────┘
```

引用链：
```
Share.CentralPipeline → BasicMethod → Pipeline/Pipeline.csproj (BasicMethod.Pipeline)
                                    → SkeletonFlow (FrameMethod)
```

### 第二阶段：迁移 WorkflowCentralPipeline（预计 2-3 天）

```
Share/WorkFlowMethod/CentralPipeline/
  ├── WorkflowCentralPipeline.cs     — 重构为内部使用 CentralPipeline<WorkflowContext>
  ├── SkeletonRegistry.cs            — 保留兼容层，内部委托给 PipelineStageRegistry
  ├── SkeletonFactory.cs             — 保留不变
  ├── PipelineStages.cs              — 保留阶段映射
  └── WorkflowContext.cs             — 保留不变
```

### 第三阶段：清理与优化（预计 1-2 天）

- 移除冗余代码
- 统一日志
- 编写集成测试
- 更新文档

---

## 7. 关键设计决策

### 7.1 泛型 vs 非泛型

**决策**：使用泛型 `CentralPipeline<TContext>`。

**理由**：
- `WorkflowContext` 和 `LogPipelineContext` 是完全不同的类型，无法统一
- 泛型提供了最大的灵活性，同时保持类型安全
- 使用者只需指定自己的上下文类型

### 7.2 适配器 vs 直接实现

**决策**：业务 Skeleton 保持独立的输入/输出类型，通过适配器接入通用管线。

**理由**：
- 业务 Skeleton 的输入/输出类型是业务特定的
- 适配器模式允许业务 Skeleton 独立于管线框架
- 业务 Skeleton 可以单独测试，无需创建 `TContext` 实例

### 7.3 动态注册 vs 静态编排

**决策**：通用管线框架同时支持动态注册和静态编排。

**理由**：
- WorkflowCentralPipeline 需要动态注册（运行时添加/移除 Skeleton）
- `PipelineBuilder` 提供 Fluent API 支持静态编排
- `PipelineStageRegistry` 提供动态注册支持

### 7.4 阶段映射

**决策**：通用阶段定义为 5 个阶段，业务管线通过映射适配。

**理由**：
- 5 个阶段覆盖了管线的完整生命周期
- 业务管线可以自由映射到通用阶段
- 阶段映射在适配器中完成，不影响业务逻辑
