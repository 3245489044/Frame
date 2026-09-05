# EventTriggerFramework → EventBindingOrchestrator 迁移重构方案

> **更新日期**：2026-07-17  
> **状态**：实施中（第 2 步修正中）  
> **目标**：将 EventTriggerFramework 剩余逻辑迁移到 Share.EventBindingOrchestrator，消除 PipelineMode 条件分支

---

## 一、总体策略

### 1.1 迁移方式

**Move（移动）**：将 EventTriggerFramework 中的文件直接移动到 Share.EventBindingOrchestrator 对应目录，而非 Copy + Delete。

### 1.2 命名空间映射

| 旧命名空间 | 新命名空间 |
|-----------|-----------|
| `EventTriggerFramework.Core` | `Share.EventBindingOrchestrator.EventBus` |
| `EventTriggerFramework.Mapping` | `Share.EventBindingOrchestrator.Mapping` |
| `EventTriggerFramework.Abstractions` | `Share.EventBindingOrchestrator.Abstractions` |
| `EventTriggerFramework.Stores` | `Share.EventBindingOrchestrator.Stores` |
| `EventTriggerFramework.Services` | `Share.EventBindingOrchestrator.Services` |
| `EventTriggerFramework.Models` | `Share.EventBindingOrchestrator.Models` |

### 1.3 管线策略

**只保留一条统一管线**，不拆分为多条管线，不通过 PipelineMode 枚举做条件分支。

管线步骤固定为 7 步：
```
ValidateInput → DiagnosticsScope → EmptyViewGuard → ControlFinder → BindingMetadata → EventBinder → StatisticsCollect
```

每个步骤内部通过 `ctx` 的实际状态（如 `ctx.CurrentControlConfig` / `ctx.CurrentDeclaration` 是否为 null）决定是否执行，而非通过 `ctx.Mode` 枚举。

---

## 二、实施步骤

### 第 1 步：迁移 EventTriggerFramework 剩余逻辑到 Share 层 ✅ 已完成

| 子步骤 | 文件 | 目标位置 | 状态 |
|--------|------|---------|------|
| 1.1 | `EventEngine.cs` | `EventBus/` | ✅ |
| 1.2 | `EventTypeMapper.cs` | `Mapping/` | ✅ |
| 1.3 | `IControlFinder.cs` | `Abstractions/` | ✅ |
| 1.4 | `IControlMetadataStore.cs` | `Abstractions/` | ✅ |
| 1.5 | `DictionaryControlMetadataStore.cs` | `Stores/`（已不存在，跳过） | ✅ |
| 1.6 | `ControlValueAccessorBase.cs` | `Models/` + `Services/` | ✅ |
| 1.7 | `FrameworkContext.cs` | 根目录 | ✅ |
| 1.8 | `ICommandExecutionResult.cs` | `Abstractions/` | ✅ |
| 1.9 | `WinFormsControlMetadataStore.cs` | `FileDriven.UI/WinForms/` | ✅ |
| 1.10 | 更新 `EventBindingOrchestrator.csproj` | — | ✅ |

### 第 2 步：重构管线 — 消除 PipelineMode 🔄 修正中

| 子步骤 | 内容 | 状态 |
|--------|------|------|
| 2.1 | 删除 `PipelineMode` 枚举 + `Mode` 属性 | ✅ 已完成 |
| 2.2 | 重写 `CreateFullUnifiedPipeline` — 去掉 Mode 条件分支 | 🔄 修正中 |
| 2.3 | 更新 `EventBindingOrchestrator.cs` — 只保留 `_unifiedPipeline` | 🔄 修正中 |

### 第 3 步：迁移 EventTriggerFrameworkInitializer

| 子步骤 | 内容 | 状态 |
|--------|------|------|
| 3.1 | 移动 `EventTriggerFrameworkInitializer.cs` | ⏳ |
| 3.2 | 更新命名空间和引用 | ⏳ |
| 3.3 | 更新 `EventBindingOrchestrator.csproj` | ⏳ |

### 第 4 步：删除 EventTriggerFramework 项目

| 子步骤 | 内容 | 状态 |
|--------|------|------|
| 4.1 | 从 `.sln` 移除 EventTriggerFramework 项目引用 | ⏳ |
| 4.2 | 删除 `EventTriggerFramework` 目录 | ⏳ |
| 4.3 | 删除 `eventtrigger_log_messages.json`、`README.md`、`task_progress.md` | ⏳ |

---

## 三、当前状态

### 3.1 已完成的变更

**EventBindingContext.cs**（`CsharpPrj/Plate/TConfig/Contexts/`）：
- 已删除 `PipelineMode` 枚举定义
- 已删除 `Mode` 属性
- 已更新类注释说明删除原因

**EventBindingPipelineFactory.cs**（`CsharpPrj/Share/EventBindingOrchestrator/Transformers/`）：
- 当前状态：已拆分为 3 个工厂方法（`CreateOrchestratorPipeline` / `CreateEventTriggerPipeline` / `CreateDeclarationPipeline`）
- **待修正**：合并回 1 个工厂方法 `CreateFullUnifiedPipeline`，去掉 Mode 条件分支

**EventBindingOrchestrator.cs**（`CsharpPrj/Share/EventBindingOrchestrator/`）：
- 当前状态：已拆分为 3 个管线字段（`_orchestratorPipeline` / `_eventTriggerPipeline` / `_declarationPipeline`）
- **待修正**：合并回 1 个字段 `_unifiedPipeline`

---

## 四、具体重构步骤（详细）

### 4.1 EventBindingPipelineFactory.cs 修正步骤

#### 步骤 2.2.1：合并工厂方法

将 3 个工厂方法合并为 1 个 `CreateFullUnifiedPipeline`：

```csharp
public static StaticPipeline<EventBindingContext> CreateFullUnifiedPipeline(
    ILogger? logger = null,
    ErrorCollector? globalStats = null,
    IFunctionResolver? functionResolver = null,
    IParameterBinder? parameterBinder = null,
    IEventBinder? eventBinder = null,
    Action<string, string, object?>? onEventResult = null)
{
    return new StaticPipeline<EventBindingContext>(new[]
    {
        new Step<EventBindingContext>("ValidateInput", ValidateInputStep, IsFatal: true),
        new Step<EventBindingContext>("DiagnosticsScope", DiagnosticsScopeStep, IsFatal: false),
        new Step<EventBindingContext>("EmptyViewGuard", ctx => EmptyViewGuardStep(ctx, logger), IsFatal: false),
        new Step<EventBindingContext>("ControlFinder", ControlFinderStep, IsFatal: false),
        new Step<EventBindingContext>("BindingMetadata", BindingMetadataStep, IsFatal: false),
        new Step<EventBindingContext>("EventBinder", ctx => EventBinderStep(ctx, functionResolver, parameterBinder, eventBinder, onEventResult, null), IsFatal: false),
        new Step<EventBindingContext>("StatisticsCollect", ctx => StatisticsCollectStep(ctx, globalStats), IsFatal: false),
    });
}
```

**变更内容**：
- 删除 `CreateOrchestratorPipeline` 方法
- 删除 `CreateEventTriggerPipeline` 方法
- 删除 `CreateDeclarationPipeline` 方法
- 重命名 `CreateOrchestratorPipeline` → `CreateFullUnifiedPipeline`（保留其 7 步管线定义）
- 更新类注释：删除"拆分为 3 个独立管线方法"的描述

#### 步骤 2.2.2：验证步骤内部无 Mode 依赖

检查所有内联步骤，确认它们不依赖 `ctx.Mode`：

| 步骤 | 依赖 ctx.Mode？ | 依据 |
|------|----------------|------|
| `ValidateInputStep` | 否 | 只验证 ctx 输入参数，不检查 Mode |
| `DiagnosticsScopeStep` | 否 | 只记录诊断事件，不检查 Mode |
| `EmptyViewGuardStep` | 否 | 只检查 ctx.Config?.Views，不检查 Mode |
| `ControlFinderStep` | 否 | 通过 `ctx.CurrentControlConfig?.Name ?? ctx.CurrentDeclaration?.SourceName` 获取控件名 |
| `BindingMetadataStep` | 否 | 通过 `ctx.CurrentDeclaration != null` / `ctx.CurrentControlConfig` 判断路径 |
| `EventBinderStep` | 否 | 通过 `ctx.CurrentControlConfig != null` / `ctx.CurrentDeclaration != null` 分发 |
| `StatisticsCollectStep` | 否 | 只收集累计统计，不检查 Mode |

结论：所有步骤均不依赖 `ctx.Mode`，合并后无需修改步骤内部逻辑。

### 4.2 EventBindingOrchestrator.cs 修正步骤

#### 步骤 2.3.1：合并管线字段

将 3 个管线字段合并为 1 个：

```csharp
// 删除：
private readonly StaticPipeline<EventBindingContext> _orchestratorPipeline;
private readonly StaticPipeline<EventBindingContext> _eventTriggerPipeline;
private readonly StaticPipeline<EventBindingContext> _declarationPipeline;

// 保留：
private readonly StaticPipeline<EventBindingContext> _unifiedPipeline;
```

#### 步骤 2.3.2：更新构造函数

```csharp
// 删除 3 条管线的创建：
_orchestratorPipeline = EventBindingPipelineFactory.CreateOrchestratorPipeline(...);
_eventTriggerPipeline = EventBindingPipelineFactory.CreateEventTriggerPipeline();
_declarationPipeline = EventBindingPipelineFactory.CreateDeclarationPipeline(...);

// 改为 1 条统一管线：
_unifiedPipeline = EventBindingPipelineFactory.CreateFullUnifiedPipeline(
    logger, _globalStats,
    functionResolver, parameterBinder, eventBinder,
    options?.OnEventResult);
```

#### 步骤 2.3.3：更新 ExecuteCore 方法

```csharp
// 删除：
// Mode = PipelineMode.Orchestrator

// 保留：
var context = new EventBindingContext
{
    TargetObject = targetObject,
    // Mode 属性已删除，不再设置
};
```

#### 步骤 2.3.4：更新 TriggerEventAsync 方法

```csharp
// 删除：
// Mode = PipelineMode.EventTrigger

// 保留：
var context = new EventBindingContext
{
    TargetObject = _options.SearchRoot,
    SearchRoot = _options.SearchRoot,
    // Mode 属性已删除，不再设置
    CurrentDeclaration = new EventBindingDeclaration<IReadOnlyList<ParameterMapping>>
    {
        SourceName = controlName,
        EventType = eventType
    }
};
```

#### 步骤 2.3.5：更新 LoadAndBindDeclarations 方法

```csharp
// 删除注释中的 Mode 描述，管线调用不变：
// ★ 2026-07-17 扁平化：使用 _unifiedPipeline 替代 _genericPipeline.Execute
//   Mode=Declaration 时执行 ControlFinder → BindingMetadata → EventBinder
_unifiedPipeline.Execute(declContext);
```

#### 步骤 2.3.6：更新类注释和字段注释

- 删除 `_orchestratorPipeline` / `_eventTriggerPipeline` / `_declarationPipeline` 的字段注释
- 恢复 `_unifiedPipeline` 的字段注释
- 更新构造函数注释：删除"拆分为 3 条独立管线"的描述

### 4.3 验证清单

修正完成后，验证以下要点：

| 检查项 | 预期结果 |
|--------|---------|
| `PipelineMode` 枚举 | 不存在（已在 2.1 删除） |
| `EventBindingContext.Mode` 属性 | 不存在（已在 2.1 删除） |
| `EventBindingPipelineFactory` 中的工厂方法 | 只有 `CreateFullUnifiedPipeline` 一个 |
| `EventBindingOrchestrator` 中的管线字段 | 只有 `_unifiedPipeline` 一个 |
| `ExecuteCore` 中设置 Mode 的代码 | 已删除 |
| `TriggerEventAsync` 中设置 Mode 的代码 | 已删除 |
| 所有引用 `_orchestratorPipeline` 的地方 | 已改为 `_unifiedPipeline` |
| 所有引用 `_eventTriggerPipeline` 的地方 | 已改为 `_unifiedPipeline` |
| 所有引用 `_declarationPipeline` 的地方 | 已改为 `_unifiedPipeline` |
| 编译通过 | `dotnet build` 无错误 |

---

## 五、风险与注意事项

1. **ValidateInput 步骤**：在 Declaration 场景下 `ctx.Config` 可能为 null，`EventBindingContextValidator` 需要能处理 `Config == null` 的情况
2. **EmptyViewGuard 步骤**：在 Declaration 场景下 `ctx.Config` 可能为 null，需要做 null 检查（已有 `ctx.Config?.Views` 安全访问）
3. **StatisticsCollect 步骤**：在 Declaration 场景下 `globalStats` 可能为 null，已有 null 检查
4. **ControlFinder 步骤**：在 EventTrigger 场景下 `ctx.CurrentControlConfig` 为 null，但 `ctx.CurrentDeclaration` 不为 null，内部已有空安全判断
5. **EventBinder 步骤**：在 EventTrigger 场景下 `ctx.CurrentControlConfig` 和 `ctx.CurrentDeclaration` 都可能为 null，内部已有空安全判断
6. **ControlLookupStep / EventTriggerStep**：这两个步骤在统一管线中始终存在，但在 ExecuteWithWalk 场景下 `ctx.CurrentDeclaration` 为 null，`GetControlName` 会返回 null，`ControlLookupStep` 会安全跳过；`EventTriggerStep` 中 `ctx.CurrentControl` 为 null，也会安全跳过
