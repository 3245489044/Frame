# LogicComposition 通用框架优化实施计划

> **目标**：将 WorkFlowMethod 中可复用的逻辑迁移到 LogicComposition 通用框架，消除重复、统一接口
> **分析日期**：2026-06-28
> **实施日期**：2026-06-28

---

## 一、优化项总览

| 优先级 | 优化项 | 文件 | 类型 | 工作量 | 状态 |
|--------|--------|------|------|--------|------|
| **P0** | 结果类型统一 | `ResultTypes.cs` + 各使用处 | 体系性 | 中 | 📋 待实施 |
| **P0** | 管线体系统一 | 5 套管线 → CentralPipeline | 体系性 | 大 | 📋 待实施 |
| **P1** | NodeConfigFactory 合并方向修复 | `NodeConfigFactory.cs` | 代码修复 | 小 | ✅ 已实施 |
| **P1** | DefaultDeduplicationProcessor 废弃 | `DeduplicationProcessor.cs` | 废弃标记 | 小 | ✅ 已实施 |
| **P1** | ModularValidator 迁移 | `Validation.cs` | 重构 | 中 | ✅ 已实施 |
| **P2** | ConfigDeduplicationProcessor 标识符逻辑迁移 | `ConfigDeduplicationProcessor.cs` | 重构 | 中 | 📋 待实施 |
| **P2** | WorkflowMediator ConnectChains 实现 | `WorkflowMediator.cs` | 功能补齐 | 小 | ✅ 已实施 |
| **P2** | WorkflowMediator ConnectChains 实现说明 | `WorkflowMediator.cs` | 接口适配 | 小 | ✅ 已实施（适配 ConnectRequest 实际签名） |
| **P2** | ChainLinkProcessor 接口对齐 | `ChainLinkProcessor.cs` | 接口标准化 | 小 | 📋 待实施 |

---

## 二、已实施优化详情

### 2.1 NodeConfigFactory 合并方向修复（P1）

**问题**：`Merge<T>()` 方法中使用了反转合并方向的 workaround：
```csharp
// ★ 修复：反转合并方向 — 模板(source)作为被覆盖方，已有配置(target)作为覆盖方
ObjectMerger.Merge(source, target, ...);  // 参数顺序反转
return source;  // 返回 source 而非 target
```

**修复**：改为标准用法，使用 `FillDefaults` 策略（仅合并目标对象中为 null 或默认值的属性）：
```csharp
// 标准用法：模板作为 source，已有配置作为 target
// FillDefaults 策略：仅合并 target 中为 null 或默认值的属性
ObjectMerger.Merge(target, source, MergeOptions.FillDefaults);
return target;
```

**变更文件**：`NodeProcess/NodeConfigFactory.cs`

---

### 2.2 WorkflowMediator ConnectChains 实现（P2）

**问题**：`ConnectChains<TNode>()` 方法抛出 `NotImplementedException`。

**修复**：委托给 `DefaultChainComposer.Connect()` 实现，使用 `ConnectionPoint.End()` 和 `ConnectionPoint.Start()` 连接两个链。

**变更文件**：`Execution/Chain/WorkflowMediator.cs`

---

### 2.3 DefaultDeduplicationProcessor 废弃标记（P1）

**问题**：`DefaultDeduplicationProcessor` 与已使用 LogicComposition 的 `ConfigDeduplicationProcessor` 功能重叠。

**修复**：添加 `[Obsolete]` 标记，指向 `ConfigDeduplicationProcessor`。

**变更文件**：`NodeProcess/DeduplicationProcessor.cs`

---

### 2.4 ModularValidator 迁移到 LogicComposition（P1）

**问题**：`ModularValidator` 是独立实现，与 LogicComposition 的 `DefaultCompositionValidator` 功能重叠。

**修复**：
- 保留 `ModularValidator` 作为领域层外观（Facade），内部委托给 `DefaultCompositionValidator`
- `WorkflowValidator`、`ConnectionValidator`、`ConfigValidator` 作为领域验证规则注册到 `DefaultCompositionValidator`
- 新增 `WorkflowValidationRule`、`ConnectionValidationRule`、`ConfigValidationRule` 三个验证规则类

**变更文件**：`Orchestration/Validation.cs`

---

## 三、待实施优化详情

### 3.1 结果类型统一（P0）

**目标**：以 `Result<T>` 为核心统一 7 种结果类型

**步骤**：
1. 为 `Result` 基类添加 `Duration` 属性
2. 删除 `StepResult`（替换为 `Result<string>`）
3. 删除 `RefactoredWorkflowExecutionResult`（替换为 `Result<ExecutionResultData>`）
4. 删除 `Result<T>.ToStepResult()` 方法
5. 统一 `IsSuccess` 命名（废弃 `Success` 属性）

**涉及文件**：
- `NodeProcess/Models/ResultTypes.cs`
- `Execution/RefactoredExecution.cs`
- `Execution/Engine/NodeExecutor.cs`
- `Execution/Session/ExecutionSession.cs`

---

### 3.2 管线体系统一（P0）

**目标**：5 套管线 → CentralPipeline + StagePipeline

**步骤**：
1. 删除 `Infrastructure/PipelineExecutor.cs`
2. 删除 `FuncCallRun` 中的管线执行逻辑（保留 `FuncCall` 模型类）
3. 为 `CentralPipeline/StagePipeline` 添加条件步骤支持
4. 创建 `BmPipelineToCentralAdapter`（桥接 BasicMethod 管线）

**涉及文件**：
- `Infrastructure/PipelineExecutor.cs`
- `Infrastructure/FuncCallRun.cs`
- `WorkFlowPipeline/PipelineBuilder.cs`
- `WorkFlowPipeline/PipelineContextAccessor.cs`

---

### 3.3 ConfigDeduplicationProcessor 标识符逻辑迁移（P2）

**目标**：将 `BuildIdentifierMap()`、`ResolveDuplicates()`、`ApplyRenames()` 迁移到 LogicComposition 的 `IDeduplicationStrategy` 接口

**步骤**：
1. 在 LogicComposition 中新增 `IDeduplicationStrategy` 接口
2. 将标识符映射构建、重复解析、重命名应用实现为策略
3. 在 `ConfigDeduplicationProcessor` 中注入策略

---

### 3.4 ChainLinkProcessor 接口对齐（P2）

**目标**：基于 LogicComposition 的 `IChainLinkHandler<TNode>` 接口重构

**步骤**：
1. 在 LogicComposition 中新增 `IChainLinkHandler<TNode>` 接口
2. 让 `ChainLinkProcessor` 实现该接口
3. 更新 `WorkflowMediator` 的依赖注入

---

## 四、实施顺序建议

```
Phase 1（当前）：P1 + P2 小改动
  ├── NodeConfigFactory 合并方向修复 ✅
  ├── DefaultDeduplicationProcessor 废弃 ✅
  ├── ModularValidator 迁移 ✅
  └── WorkflowMediator ConnectChains 实现 ✅

Phase 2（下一步）：P0 体系性优化
  ├── 结果类型统一
  └── 管线体系统一

Phase 3（后续）：P2 深入优化
  ├── ConfigDeduplicationProcessor 标识符逻辑迁移
  └── ChainLinkProcessor 接口对齐
```
