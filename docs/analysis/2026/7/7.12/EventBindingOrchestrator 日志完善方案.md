## EventBindingOrchestrator 日志完善方案

### 一、现状分析

基于 `Architecture.md` 的架构规范，我对 `EventBindingOrchestrator` 目录下所有文件进行了全面审查，以下是当前日志使用情况的评估：

#### ✅ 已正确使用的模式

| 模式 | 使用位置 | 说明 |
|------|----------|------|
| `EventBindingDiagnosticContext.Record` | EventBindingPipelineRunner.cs | pipeline.execute_with_walk.start/complete |
| `EventBindingDiagnosticContext.Record` | GenericEventBindingPipeline.cs | pipeline.walk_and_bind.visiting/executing/map_result |
| `EventBindingDiagnosticContext.Record` | EventBinderTransformer.cs | eventbinder.transform.* / eventbinder.function.* / eventbinder.workflow.* / eventbinder.try_bind_event.* |
| `EventBindingDiagnosticContext.Record` | ControlFinderTransformer.cs | controlfinder.config_validation_warning |
| `EventBindingDiagnosticContext.Record` | StringStageCoordinator.cs | stagecoordinator.wait_start/wait_result/wait_timeout/wait_exception/stage_completed |
| `EventBindingDiagnosticContext.Record` | DiagnosticTransformer.cs | transformer.{name}.enter/exit/exception |
| `EventBindingDiagnosticContext.Record` | EventBindingDiagnosticContext.cs | eventbinder.scope.began/scope.restored |
| `EventBindingDiagnosticContext.BeginScope` | EventBindingPipelineRunner.Execute | 正确创建诊断作用域 |
| `EventBindingDiagnosticContext.GetSnapshotString` | EventBindingPipelineRunner.Execute | 正确获取快照 |

#### ❌ 存在的问题

| # | 问题 | 严重程度 | 说明 |
|---|------|----------|------|
| 1 | **EventTriggerLog 位于 Share 层** | 🔴 高 | 违反 Architecture.md §4.2 禁止事项 #1，Share 层不应使用需要消息字典注册的日志系统 |
| 2 | **EventBindingOrchestrator.cs 使用 ILogger** | 🟡 中 | ILogger 是通用框架日志，与架构的插桩体系不统一，缺少 EventBindingDiagnosticContext.Record 插桩 |
| 3 | **事件名魔术字符串散落各处** | 🟡 中 | 所有 Record 调用直接写字符串，无集中管理，易拼写错误 |
| 4 | **DiagnosticTransformer 未被管线使用** | 🟡 中 | 已实现但未在 EventBindingPipelineFactory.CreateGenericPipeline 中启用 |
| 5 | **Debug.WriteLine 散落** | 🟢 低 | EventBindingPipelineRunner.Execute 中直接 Debug.WriteLine 输出快照，应由 Operation 层决定输出方式 |
| 6 | **事件命名规范未完全覆盖** | 🟢 低 | 代码中部分事件名未在 Architecture.md §3.1 中列出 |

---

### 二、完善方案（7 步）

#### Step 1：创建 EventNames.cs 常量类
**目标**：集中管理所有事件名字符串常量，消除魔术字符串

**新建文件**：`CsharpPrj/Share/EventBindingOrchestrator/Diagnostics/EventNames.cs`

按领域分组定义常量：
```csharp
public static class EventNames
{
    // Pipeline 领域
    public const string PipelineExecuteWithWalkStart = "pipeline.execute_with_walk.start";
    public const string PipelineExecuteWithWalkComplete = "pipeline.execute_with_walk.complete";
    public const string PipelineWalkAndBindVisiting = "pipeline.walk_and_bind.visiting";
    public const string PipelineWalkAndBindExecuting = "pipeline.walk_and_bind.executing";
    public const string PipelineWalkAndBindMapResult = "pipeline.walk_and_bind.map_result";

    // EventBinder 领域
    public const string EventBinderTransformEntry = "eventbinder.transform.entry";
    // ... 所有事件名
}
```

**影响文件**：EventBindingPipelineRunner.cs, GenericEventBindingPipeline.cs, EventBinderTransformer.cs, ControlFinderTransformer.cs, StringStageCoordinator.cs, DiagnosticTransformer.cs, EventBindingDiagnosticContext.cs

---

#### Step 2：废弃 EventTriggerLog（Share 层定位）
**目标**：消除违反架构规范的日志入口

**修改文件**：`CsharpPrj/Share/EventBindingOrchestrator/Diagnostics/EventTriggerLog.cs`

添加 `[Obsolete]` 属性，引导迁移到 `EventBindingDiagnosticContext.Record`：
```csharp
[Obsolete("Share 层请使用 EventBindingDiagnosticContext.Record 替代 EventTriggerLog.Log。" +
    "EventTriggerLog 依赖 LogPlaceholderHandlerBase 的消息字典注册，属于 Operation 层控制范畴。")]
public static void Log(string messageId, object? data = null)
```

**影响**：编译时产生警告，引导开发者正确使用

---

#### Step 3：启用 DiagnosticTransformer 装饰器
**目标**：自动记录每个 Transformer 的 enter/exit/exception，减少手动 Record 调用

**修改文件**：`CsharpPrj/Share/EventBindingOrchestrator/Transformers/EventBindingPipelineFactory.cs`

在 `CreateGenericPipeline` 中为每个 Transformer 包装 `DiagnosticTransformer`：
```csharp
var transformers = new List<ITransformer<EventBindingContext, EventBindingContext>>
{
    new DiagnosticTransformer(new ControlFinderTransformer()),
    new DiagnosticTransformer(new BindingMetadataTransformer(validator)),
    new DiagnosticTransformer(new EventBinderTransformer(...))
};
```

**影响**：每个 Transformer 执行时自动记录 `transformer.{name}.enter/exit/exception`，无需手动 Record

---

#### Step 4：统一 EventBindingOrchestrator.cs 的日志方式
**目标**：在保留 ILogger 框架级日志的同时，增加 EventBindingDiagnosticContext.Record 插桩

**修改文件**：`CsharpPrj/Share/EventBindingOrchestrator/EventBindingOrchestrator.cs`

在 `LoadAndBindDeclarations` 方法中增加插桩事件：
```csharp
// 保留 ILogger 用于框架级统计日志
_logger.LogInformation("开始事件绑定（新管线）: Declarations={Count}, Target={Target}", ...);

// 新增：EventBindingDiagnosticContext.Record 插桩
EventBindingDiagnosticContext.Record(EventNames.OrchestratorLoadAndBindStart, new
{
    declarationCount = declarations.Count,
    targetType = targetObject.GetType().Name
});
```

**影响**：ILogger 和 Record 并存，ILogger 负责框架级日志，Record 负责插桩事件

---

#### Step 5：统一降级 Debug.WriteLine
**目标**：确保所有降级路径统一通过 EventBindingDiagnosticContext.Record 的降级机制

**修改文件**：`CsharpPrj/Share/EventBindingOrchestrator/Transformers/EventBindingPipelineRunner.cs`

将快照输出从 Debug.WriteLine 改为通过 EventBindingDiagnosticContext.Record 记录：
```csharp
// 原代码（违反"Share 层不负责输出"原则）
System.Diagnostics.Debug.WriteLine($"[DiagnosticSnapshot] eventbinding.pipeline.complete");
System.Diagnostics.Debug.WriteLine(pipelineSnapshot);

// 改为：通过 Record 记录快照内容
EventBindingDiagnosticContext.Record(EventNames.PipelineSnapshot, new
{
    snapshot = pipelineSnapshot,
    snapshotLength = pipelineSnapshot.Length
});
```

**影响**：符合"Share 层只负责记录，不负责输出"的架构原则

---

#### Step 6：补充 Architecture.md 事件命名规范
**目标**：将代码中已使用但未在 Architecture.md 中列出的事件名补充到规范文档中

**需要补充的事件名**（代码中已使用但 Architecture.md §3.1 未列出）：
- `eventbinder.try_bind_event.success_via_eventbus`
- `eventbinder.try_bind_event.reflection_fallback`
- `eventbinder.try_bind_event.success_via_reflection`
- `eventbinder.try_bind_event.event_not_found`
- `eventbinder.try_bind_event.reflection_failed`
- `eventbinder.bound_via_eventbus`
- `eventbinder.bound_from_declaration`
- `pipeline.snapshot`

**修改文件**：`CsharpPrj/Architecture.md`

---

#### Step 7：完善 Implementation.md 文档
**目标**：同步更新实现参考文档，反映最新的日志使用方式

**修改文件**：`CsharpPrj/Implementation.md`

- 更新 §1.3 Share 层插桩代码的示例，使用 EventNames 常量
- 更新 §7.2 Share 层插桩示例
- 更新 §8 迁移指南，反映 EventTriggerLog 已废弃

---

### 三、影响范围汇总

| 步骤 | 新增文件 | 修改文件 | 影响 |
|------|----------|----------|------|
| Step 1 | EventNames.cs (1) | 7 个文件 | 替换所有魔术字符串 |
| Step 2 | - | EventTriggerLog.cs | 添加 Obsolete 属性 |
| Step 3 | - | EventBindingPipelineFactory.cs | 包装 DiagnosticTransformer |
| Step 4 | - | EventBindingOrchestrator.cs | 增加 Record 插桩 |
| Step 5 | - | EventBindingPipelineRunner.cs | 替换 Debug.WriteLine |
| Step 6 | - | Architecture.md | 补充事件名列表 |
| Step 7 | - | Implementation.md | 同步更新示例 |

---

### 四、需要您确认的问题

1. **EventTriggerLog 废弃**：是否同意将 Share 层的 EventTriggerLog 标记为 `[Obsolete]`？这会产生编译警告，引导开发者迁移到 `EventBindingDiagnosticContext.Record`。

2. **ILogger 保留**：EventBindingOrchestrator.cs 中的 `ILogger<EventBindingOrchestrator>` 是否保留用于框架级统计日志（如绑定次数、成功/失败计数），同时增加 `EventBindingDiagnosticContext.Record` 用于插桩事件？

3. **DiagnosticTransformer 默认启用**：是否同意在 `CreateGenericPipeline` 中默认启用 `DiagnosticTransformer` 装饰器？这会为每个 Transformer 自动记录 enter/exit/exception，但会增加 Record 调用量（每个控件绑定会产生 3 个 Transformer × 2~3 个事件 = 6~9 条额外记录）。

4. **执行顺序**：您希望按什么顺序执行这 7 步？建议按 Step 1 → Step 3 → Step 4 → Step 5 → Step 2 → Step 6 → Step 7 的顺序，先做正向建设（常量类、装饰器），再做废弃标记和文档更新。

如果您同意这个方案，请切换到 **ACT MODE**，我将开始逐步实施。