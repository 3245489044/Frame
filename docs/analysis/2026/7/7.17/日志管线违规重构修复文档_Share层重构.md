# Share 层日志管线重构 — 插桩化改造

> **版本：** v1.0
> **日期：** 2026-07-16
> **依据：** [`CsharpPrj/Architecture.md`](../CsharpPrj/Architecture.md) §4.2 禁止事项第 6 条
>
> **核心原则：** Share 层只做**字符串插桩**，不关心日志输出。每个 Share 子项目在自己的命名空间内创建 `Record` 插桩类，通过 `Modules.DiagnosticContext.Core.DiagnosticContext.Current?.Record()` 记录结构化事件。

---

## 1. 设计原则

### 1.1 Share 层的日志职责

| 职责 | 不负责 |
|------|--------|
| 记录结构化插桩事件（字符串 + 数据） | 日志如何输出、持久化 |
| 通过 `DiagnosticContext` 框架记录 | 降级策略（由调用方决定） |
| 定义事件名常量 | 日志级别、过滤、路由 |

### 1.2 插桩类的设计规范

每个 Share 子项目应创建一个 `XxxDiagnosticContext` 静态类，遵循以下规范：

```csharp
namespace Xxx.Diagnostics;

/// <summary>
/// Xxx 模块诊断插桩上下文
/// <para>只负责记录结构化插桩事件，不关心日志输出方式。</para>
/// <para>无活跃 DiagnosticContext 时不执行任何操作（由调用方决定降级策略）。</para>
/// </summary>
public static class XxxDiagnosticContext
{
    /// <summary>记录诊断事件</summary>
    public static void Record(string eventName, object? data = null)
    {
        var context = Modules.DiagnosticContext.Core.DiagnosticContext.Current;
        context?.Record(eventName, data);
    }
}
```

**关键约束：**
- ❌ 不包含 `Debug.WriteLine` / `Console.WriteLine` / `Trace.WriteLine`
- ❌ 不包含 `#if DEBUG` 条件编译
- ❌ 不引用 `LogPlaceholderHandlerBase` / `ModuleLog` 等日志输出 API
- ✅ 只通过 `DiagnosticContext.Current?.Record()` 记录事件
- ✅ 方法签名统一为 `Record(string eventName, object? data = null)`

---

## 2. 需要新建的插桩类

| # | 子项目 | 新建文件 | 命名空间 | 对应违规文件 |
|---|--------|---------|----------|-------------|
| 1 | **GraphFramework** | `Share/GraphFramework/Diagnostics/GraphFrameworkDiagnosticContext.cs` | `GraphFramework.Diagnostics` | `DecoratorChain.cs` |
| 2 | **CommandFramework** | `Share/CommandFramework/Diagnostics/CommandFrameworkDiagnosticContext.cs` | `CommandFramework.Diagnostics` | `Registries.cs`、`CommandEngine.cs`、`ClickCommandHandler.cs` |
| 3 | **PlaceholderFramework** | `Share/PlaceholderFramework/Diagnostics/PlaceholderFrameworkDiagnosticContext.cs` | `PlaceholderFramework.Diagnostics` | `HandlersService.cs`、`PlaceholderResolverSkeleton.cs`、`JsonPipelineRegistry.cs` |
| 4 | **StateManagement** | `Share/StateManagement/Diagnostics/StateManagementDiagnosticContext.cs` | `StateManagement.Diagnostics` | `ExecutionStage.cs` |
| 5 | **UniversalMiddleware** | `Share/UniversalMiddleware/Diagnostics/UniversalMiddlewareDiagnosticContext.cs` | `UniversalMiddleware.Diagnostics` | `ContainerEventManager.cs` |
| 6 | **FrameworkAbstractions** | `Share/FrameworkAbstractions/Diagnostics/FrameworkAbstractionsDiagnosticContext.cs` | `FrameworkAbstractions.Diagnostics` | `DefaultErrorReporter.cs` |

> **EventBindingOrchestrator** 已有 `EventBindingDiagnosticContext.cs`，只需清理其中的 `Debug.WriteLine` 降级逻辑。

---

## 3. 修复步骤

### 步骤 1：清理 EventBindingDiagnosticContext.cs（P0）

**文件：** `Share/EventBindingOrchestrator/Diagnostics/EventBindingDiagnosticContext.cs`

**操作：** 移除所有 `Debug.WriteLine` 和 `#if DEBUG` 降级逻辑，只保留纯插桩实现。

```csharp
// ✅ 正确实现 — 纯插桩，无降级
public static class EventBindingDiagnosticContext
{
    public static IDisposable BeginScope()
    {
        var context = DiagnosticContext.Current;
        return context?.BeginScope() ?? new Scope();
    }

    public static void Record(string eventName, object? data = null)
    {
        var context = DiagnosticContext.Current;
        context?.Record(eventName, data);
    }

    // CaptureSnapshot / RestoreScope / GetSnapshotString 保持原有实现
    // Scope 类保持最小实现，不包含 Debug.WriteLine
}
```

---

### 步骤 2：新建 6 个插桩类

为每个子项目创建 `Diagnostics` 目录（如不存在）和插桩类文件。

#### 2.1 GraphFrameworkDiagnosticContext.cs

```csharp
// ============================================================
// GraphFrameworkDiagnosticContext.cs — GraphFramework 诊断插桩上下文
//
// ★ 2026-07-16 新增：纯插桩实现，通过 DiagnosticContext 记录结构化事件。
// ============================================================

using Modules.DiagnosticContext.Core;

namespace GraphFramework.Diagnostics;

/// <summary>
/// GraphFramework 诊断插桩上下文
/// <para>只负责记录结构化插桩事件，不关心日志输出方式。</para>
/// </summary>
public static class GraphFrameworkDiagnosticContext
{
    /// <summary>记录诊断事件</summary>
    public static void Record(string eventName, object? data = null)
    {
        var context = DiagnosticContext.Current;
        context?.Record(eventName, data);
    }
}
```

#### 2.2 CommandFrameworkDiagnosticContext.cs

```csharp
// ============================================================
// CommandFrameworkDiagnosticContext.cs — CommandFramework 诊断插桩上下文
//
// ★ 2026-07-16 新增：纯插桩实现，通过 DiagnosticContext 记录结构化事件。
// ============================================================

using Modules.DiagnosticContext.Core;

namespace CommandFramework.Diagnostics;

/// <summary>
/// CommandFramework 诊断插桩上下文
/// <para>只负责记录结构化插桩事件，不关心日志输出方式。</para>
/// </summary>
public static class CommandFrameworkDiagnosticContext
{
    /// <summary>记录诊断事件</summary>
    public static void Record(string eventName, object? data = null)
    {
        var context = DiagnosticContext.Current;
        context?.Record(eventName, data);
    }
}
```

#### 2.3 PlaceholderFrameworkDiagnosticContext.cs

```csharp
// ============================================================
// PlaceholderFrameworkDiagnosticContext.cs — PlaceholderFramework 诊断插桩上下文
//
// ★ 2026-07-16 新增：纯插桩实现，通过 DiagnosticContext 记录结构化事件。
// ============================================================

using Modules.DiagnosticContext.Core;

namespace PlaceholderFramework.Diagnostics;

/// <summary>
/// PlaceholderFramework 诊断插桩上下文
/// <para>只负责记录结构化插桩事件，不关心日志输出方式。</para>
/// </summary>
public static class PlaceholderFrameworkDiagnosticContext
{
    /// <summary>记录诊断事件</summary>
    public static void Record(string eventName, object? data = null)
    {
        var context = DiagnosticContext.Current;
        context?.Record(eventName, data);
    }
}
```

#### 2.4 StateManagementDiagnosticContext.cs

```csharp
// ============================================================
// StateManagementDiagnosticContext.cs — StateManagement 诊断插桩上下文
//
// ★ 2026-07-16 新增：纯插桩实现，通过 DiagnosticContext 记录结构化事件。
// ============================================================

using Modules.DiagnosticContext.Core;

namespace StateManagement.Diagnostics;

/// <summary>
/// StateManagement 诊断插桩上下文
/// <para>只负责记录结构化插桩事件，不关心日志输出方式。</para>
/// </summary>
public static class StateManagementDiagnosticContext
{
    /// <summary>记录诊断事件</summary>
    public static void Record(string eventName, object? data = null)
    {
        var context = DiagnosticContext.Current;
        context?.Record(eventName, data);
    }
}
```

#### 2.5 UniversalMiddlewareDiagnosticContext.cs

```csharp
// ============================================================
// UniversalMiddlewareDiagnosticContext.cs — UniversalMiddleware 诊断插桩上下文
//
// ★ 2026-07-16 新增：纯插桩实现，通过 DiagnosticContext 记录结构化事件。
// ============================================================

using Modules.DiagnosticContext.Core;

namespace UniversalMiddleware.Diagnostics;

/// <summary>
/// UniversalMiddleware 诊断插桩上下文
/// <para>只负责记录结构化插桩事件，不关心日志输出方式。</para>
/// </summary>
public static class UniversalMiddlewareDiagnosticContext
{
    /// <summary>记录诊断事件</summary>
    public static void Record(string eventName, object? data = null)
    {
        var context = DiagnosticContext.Current;
        context?.Record(eventName, data);
    }
}
```

#### 2.6 FrameworkAbstractionsDiagnosticContext.cs

```csharp
// ============================================================
// FrameworkAbstractionsDiagnosticContext.cs — FrameworkAbstractions 诊断插桩上下文
//
// ★ 2026-07-16 新增：纯插桩实现，通过 DiagnosticContext 记录结构化事件。
// ============================================================

using Modules.DiagnosticContext.Core;

namespace FrameworkAbstractions.Diagnostics;

/// <summary>
/// FrameworkAbstractions 诊断插桩上下文
/// <para>只负责记录结构化插桩事件，不关心日志输出方式。</para>
/// </summary>
public static class FrameworkAbstractionsDiagnosticContext
{
    /// <summary>记录诊断事件</summary>
    public static void Record(string eventName, object? data = null)
    {
        var context = DiagnosticContext.Current;
        context?.Record(eventName, data);
    }
}
```

---

### 步骤 3：替换 Share 层各文件的 Debug.WriteLine

#### 3.1 DecoratorChain.cs

**文件：** `Share/GraphFramework/Pipeline/DecoratorChain.cs`

| 原代码 | 替换为 |
|--------|--------|
| `Debug.WriteLine($"[Pipeline] Executing {name}...")` | `GraphFrameworkDiagnosticContext.Record("pipeline.step.executing", new { stepName = name })` |
| `Debug.WriteLine($"[Pipeline] {name} failed: ...")` | `GraphFrameworkDiagnosticContext.Record("pipeline.step.failed", new { stepName = name, error = exception.Message })` |
| `Debug.WriteLine($"[Pipeline] {name} completed.")` | `GraphFrameworkDiagnosticContext.Record("pipeline.step.completed", new { stepName = name })` |
| `Debug.WriteLine($"[Pipeline] {name} took ...ms")` | `GraphFrameworkDiagnosticContext.Record("pipeline.step.timing", new { stepName = name, elapsedMs })` |
| `Debug.WriteLine($"[Pipeline] {name} failed (attempt ...)")` | `GraphFrameworkDiagnosticContext.Record("pipeline.step.retry", new { stepName = name, attempt, maxRetries })` |

#### 3.2 Registries.cs

**文件：** `Share/CommandFramework/Registries.cs`

| 原代码 | 替换为 |
|--------|--------|
| `Debug.WriteLine($"[CommandRegistry] 解析命令 '{name}' 失败: {ex.Message}")` | `CommandFrameworkDiagnosticContext.Record("command.resolve.failed", new { commandName = name, error = ex.Message })` |

#### 3.3 CommandEngine.cs

**文件：** `Share/CommandFramework/Execution/CommandEngine.cs`

| 原代码 | 替换为 |
|--------|--------|
| `Debug.WriteLine($"[CommandEngine] Undo failed for '{name}': {ex.Message}")` | `CommandFrameworkDiagnosticContext.Record("command.undo.failed", new { commandName = name, error = ex.Message })` |
| `Debug.WriteLine($"[CommandEngine] Redo failed for '{name}': {ex.Message}")` | `CommandFrameworkDiagnosticContext.Record("command.redo.failed", new { commandName = name, error = ex.Message })` |

#### 3.4 ClickCommandHandler.cs

**文件：** `Share/CommandFramework/Invoke/Handlers/ClickCommandHandler.cs`

| 原代码 | 替换为 |
|--------|--------|
| `Debug.WriteLine($"[ClickCommandHandler] Binding finder returned null for ...")` | `CommandFrameworkDiagnosticContext.Record("binding.find.null", new { controlName, eventType })` |

#### 3.5 WinFormsThreadDispatcher.cs

**文件：** `Share/EventBindingOrchestrator/Controls/WinFormsThreadDispatcher.cs`

| 原代码 | 替换为 |
|--------|--------|
| `Debug.WriteLine(...)` | `EventBindingDiagnosticContext.Record("thread.dispatch", new { ... })` |

#### 3.6 DefaultErrorReporter.cs

**文件：** `Share/FrameworkAbstractions/ErrorHandling/DefaultErrorReporter.cs`

| 原代码 | 替换为 |
|--------|--------|
| `Trace.WriteLine(...)` | `FrameworkAbstractionsDiagnosticContext.Record("error.report", new { ... })` |

#### 3.7 JsonPipelineRegistry.cs

**文件：** `Share/PlaceholderFramework/TemplateExpansion/Core/JsonPipelineRegistry.cs`

| 原代码 | 替换为 |
|--------|--------|
| `Debug.WriteLine(...)` | `PlaceholderFrameworkDiagnosticContext.Record("jsonpipeline.register", new { ... })` |

#### 3.8 HandlersService.cs

**文件：** `Share/PlaceholderFramework/Handlers/HandlersService.cs`

| 原代码（7处） | 替换为 |
|--------|--------|
| `Debug.WriteLine(...)` | `PlaceholderFrameworkDiagnosticContext.Record("handler.xxx", new { ... })` |

#### 3.9 PlaceholderResolverSkeleton.cs

**文件：** `Share/PlaceholderFramework/Core/PlaceholderResolverSkeleton.cs`

| 原代码（3处） | 替换为 |
|--------|--------|
| `Debug.WriteLine(...)` | `PlaceholderFrameworkDiagnosticContext.Record("resolver.xxx", new { ... })` |

#### 3.10 ExecutionStage.cs

**文件：** `Share/StateManagement/Stages/ExecutionStage.cs`

| 原代码 | 替换为 |
|--------|--------|
| `Debug.WriteLine(...)` | `StateManagementDiagnosticContext.Record("stage.execute", new { ... })` |

#### 3.11 ContainerEventManager.cs

**文件：** `Share/UniversalMiddleware/Container/ContainerEventManager.cs`

| 原代码 | 替换为 |
|--------|--------|
| `Debug.WriteLine(...)` | `UniversalMiddlewareDiagnosticContext.Record("container.event", new { ... })` |

---

### 步骤 4：保留的文件（无需修改）

以下文件的 `Console.WriteLine` / `Trace.WriteLine` 属于设计如此，**保留**：

| 文件 | 原因 |
|------|------|
| `Share/CommandFramework/Pipeline/Stages/LoggingStage.cs` | Console.WriteLine 默认委托，设计如此 |
| `Share/LayoutAnalysis/UIOverlapDetector/Reporter/ConsoleOverlapReporter.cs` | 工具类，设计如此 |
| `Share/LayoutAnalysis/UIOverlapDetector/Monitor/OverlapMonitor.cs` | 工具类，设计如此 |
| `Share/UniversalMiddleware/Infrastructure.cs` | 有注释说明已知问题 |

---

## 4. 执行顺序

```
步骤 1：清理 EventBindingDiagnosticContext.cs（移除 Debug 降级）
    ↓
步骤 2：新建 6 个插桩类
    ↓
步骤 3：替换 12 个文件的 Debug.WriteLine → Record 调用
    ↓
步骤 4：dotnet build 验证编译通过
```

---

## 5. 验证清单

- [ ] `EventBindingDiagnosticContext.cs` 无 `Debug.WriteLine` / `#if DEBUG` 残留
- [ ] 6 个新插桩类创建完成，命名空间正确
- [ ] 12 个违规文件的 `Debug.WriteLine` 已替换为对应的 `Record` 调用
- [ ] `dotnet build CsharpPrj/MainProj.sln` 编译通过
- [ ] 保留的文件未误修改

---

## 6. 附录：事件命名规范

参考 [`Architecture.md §3.1`](../CsharpPrj/Architecture.md#31-事件命名规范)，事件名格式为 `领域.组件.动作[.状态]`：

| 领域 | 示例 |
|------|------|
| pipeline | `pipeline.step.executing`、`pipeline.step.failed`、`pipeline.step.completed` |
| command | `command.resolve.failed`、`command.undo.failed`、`command.redo.failed` |
| binding | `binding.find.null` |
| handler | `handler.xxx`（按具体 handler 命名） |
| resolver | `resolver.xxx`（按具体 resolver 命名） |
| stage | `stage.execute` |
| container | `container.event` |
| error | `error.report` |
| thread | `thread.dispatch` |
| jsonpipeline | `jsonpipeline.register` |
