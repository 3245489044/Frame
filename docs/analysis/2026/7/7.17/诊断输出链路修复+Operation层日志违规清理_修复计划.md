# 诊断输出链路修复 + Operation 层日志违规清理 — 修复文档

> **基于分析文档**：`docs/analysis/诊断输出链路修复+Operation层日志违规清理.md`
> **状态**：待执行
> **优先级**：高（诊断链路断裂 + 日志违规）
> **最后更新**：2026-07-16

---

## 一、问题概述

### 1.1 诊断输出链路断裂

`EventBindingDiagnosticContext.GetSnapshotString()` 和 `CaptureSnapshot()` 在无活跃 `DiagnosticContext` 时返回空值，导致：

- `RenderPipelineOrchestrator.OutputDiagnosticSnapshot()` 抛出 `InvalidOperationException`
- `WinFormsRenderPipeline.ExecuteBindEventsStage()` 抛出 `InvalidOperationException`
- 诊断快照完全不可用

**根因**：`DiagnosticsScopeStage` 中 `BeginScope()` 返回空作用域（`EventBindingDiagnosticContext.BeginScope()` 的 `Scope` 类 `Dispose()` 为空实现），且 `DiagnosticContext.Current` 为 null 时 `Record()` 静默跳过。

### 1.2 Operation 层日志违规

Operation 层代码直接调用 `EventBindingDiagnosticContext.Record()`，违反了**分层架构规范**：

- Operation 层应使用自己的日志通道（`WorkFlowMethodLog` / `IocLog` / `WdUILog`）
- `EventBindingDiagnosticContext` 属于 Share 层，不应被 Operation 层直接调用
- 违规调用点：
  - `EventBindingIoCInitializer.cs` — `RecordDiagnostic` 委托中调用 `EventBindingDiagnosticContext.Record()`
  - `WorkflowMethodExecutionService.cs` — 直接调用 `EventBindingDiagnosticContext.Record()`

---

## 二、框架层面根本问题分析

### 2.1 诊断上下文作用域生命周期断裂

**问题描述**：

`DiagnosticsScopeStage` 创建诊断作用域，但管线执行完毕后没有任何代码释放该作用域。`EventBindingDiagnosticContext.BeginScope()` 返回的 `Scope` 是空实现（`Dispose()` 什么都不做），且没有通过 `DiagnosticContext.Activate()` 将上下文挂载到 `AsyncLocal`。

**影响**：
- `DiagnosticContext.Current` 始终为 null
- `EventBindingDiagnosticContext.Record()` 调用无效果（`context?.Record(eventName, data)` 因 `context` 为 null 而跳过）
- `GetSnapshotString()` 返回 `string.Empty`
- `CaptureSnapshot()` 返回 `new object()`

**修复方向**：
- `EventBindingDiagnosticContext.BeginScope()` 应创建真正的 `DiagnosticContext` 实例并调用 `Activate()`
- `Scope.Dispose()` 应调用 `DiagnosticContext.Dispose()` 恢复 `AsyncLocal`
- `EventBindingOrchestrator.ExecuteCore()` 中管线执行完毕后应释放作用域

### 2.2 日志通道选择错误

**问题描述**：

Operation 层代码使用 `EventBindingDiagnosticContext.Record()` 记录日志，但该方法是 Share 层的诊断上下文机制，不应被 Operation 层直接调用。

**违规点**：

| 文件 | 违规代码 | 应使用的日志通道 |
|------|---------|----------------|
| `EventBindingIoCInitializer.cs` | `EventBindingDiagnosticContext.Record("Preresolve_...", data)` | `IocLog.Log(...)` |
| `WorkflowMethodExecutionService.cs` | `EventBindingDiagnosticContext.Record("event.execution.start", ...)` | `WorkFlowMethodLog.Log(...)` |

**修复方向**：
- 将 `EventBindingDiagnosticContext.Record()` 替换为对应模块的 `Log.Log()`

### 2.3 诊断快照消费方缺乏防御

**问题描述**：

`RenderPipelineOrchestrator.OutputDiagnosticSnapshot()` 和 `WdUI.RenderPipeline.ExecuteBindEventsStage()` 在获取到空快照时抛出 `InvalidOperationException`，但异常信息不够明确，且没有提供降级路径。

**修复方向**：
- 快照为空时抛出明确的 `InvalidOperationException`，包含诊断链路断裂的根因提示
- 异常信息应指导开发者检查 `DiagnosticsScopeStage` 是否正确注册

---

## 三、修复步骤

### Step 1：修复 EventBindingDiagnosticContext.cs — 诊断上下文 stub 增强

**目标**：`GetSnapshotString()` 和 `CaptureSnapshot()` 在无活跃 `DiagnosticContext` 时返回非空值，避免调用方抛出异常。

**文件**：`CsharpPrj/Share/EventBindingOrchestrator/Diagnostics/EventBindingDiagnosticContext.cs`

**修改内容**：

| 方法 | 当前行为 | 修复后行为 |
|------|---------|-----------|
| `GetSnapshotString()` | `DiagnosticContext.Current?.ToJson() ?? string.Empty` | 返回 `"{}"` 而非 `string.Empty` |
| `CaptureSnapshot()` | `context?.Freeze() ?? new object()` | 返回 `new object()`（不变，但调用方需处理） |

**变更**：

```csharp
// GetSnapshotString — 返回 "{}" 而非空字符串，避免调用方抛出异常
public static string GetSnapshotString()
{
    var context = DiagnosticContext.Current;
    return context?.ToJson() ?? "{}";
}
```

---

### Step 2：修复 EventBindingIoCInitializer.cs — RecordDiagnostic 改为 IocLog.Log

**目标**：消除 Operation 层对 `EventBindingDiagnosticContext.Record()` 的违规调用。

**文件**：`CsharpPrj/Operation/BackEndDriven/IoC/EventBindingIoCInitializer.cs`

**修改内容**：

```csharp
// 修改前（第 70-71 行）：
ioc.RecordDiagnostic = (key, data) =>
    EventBindingDiagnosticContext.Record("Preresolve_" + key.Replace(".", "_"), data);

// 修改后：
ioc.RecordDiagnostic = (key, data) =>
    IocLog.Log("Preresolve_" + key.Replace(".", "_"), data);
```

---

### Step 3：修复 WorkflowMethodExecutionService.cs — EventBindingDiagnosticContext.Record 改为 WorkFlowMethodLog.Log

**目标**：消除 WorkFlowMethod 项目中对 `EventBindingDiagnosticContext.Record()` 的违规调用。

**文件**：`CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodExecutionService.cs`

**修改内容**：

搜索文件中所有 `EventBindingDiagnosticContext.Record(` 调用，替换为 `WorkFlowMethodLog.Log(`。

典型替换模式：

```csharp
// 修改前：
EventBindingDiagnosticContext.Record("event.execution.start", new { ... });

// 修改后：
WorkFlowMethodLog.Log("event.execution.start", new { ... });
```

---

### Step 4：构建验证

**目标**：确认 Step 1-3 的修改编译通过，0 错误。

```bash
dotnet build "d:\Project\Program\WorkFlowPrj\CsharpPrj\MainProj.sln" 2>&1 | findstr /C:"error"
```

---

### Step 5：完善快照处理逻辑 — 抛出异常而非静默跳过

**目标**：当诊断快照为空时，抛出明确的 `InvalidOperationException`，帮助快速定位诊断链路断裂问题。

**文件 1**：`CsharpPrj/Operation/BackEndDriven/Pipeline/RenderPipelineOrchestrator.cs`

**修改 `OutputDiagnosticSnapshot()` 方法**（第 221-232 行）：

```csharp
protected void OutputDiagnosticSnapshot()
{
    var snapshot = EventBindingDiagnosticContext.GetSnapshotString();
    if (string.IsNullOrWhiteSpace(snapshot) || snapshot == "{}")
    {
        throw new InvalidOperationException(
            "Diagnostic snapshot is empty: DiagnosticContext.Current is null. " +
            "The event binding pipeline did not create a DiagnosticContext scope. " +
            "Ensure BeginScope() is called before Record() in the binding pipeline.");
    }
    BackEndLog.Log("RenderPipelineOrchestrator_DiagnosticSnapshot", new { snapshot });
}
```

**文件 2**：`CsharpPrj/Operation/WdUI/WdUI.RenderPipeline.cs`

**修改 `ExecuteBindEventsStage()` 方法**（第 352-361 行）：

```csharp
// ★ 2026-07-16 修复：快照为空时抛出异常，避免静默失败
var snapshot = EventBindingDiagnosticContext.GetSnapshotString();
if (string.IsNullOrWhiteSpace(snapshot) || snapshot == "{}")
{
    throw new InvalidOperationException(
        "Diagnostic snapshot is empty: DiagnosticContext.Current is null. " +
        "The event binding pipeline did not create a DiagnosticContext scope. " +
        "Ensure BeginScope() is called before Record() in the binding pipeline.");
}
WdUILog.Log("wdui:DiagnosticSnapshot", new { snapshot });
```

---

### Step 6：构建验证

**目标**：确认 Step 5 的修改编译通过，0 错误。

```bash
dotnet build "d:\Project\Program\WorkFlowPrj\CsharpPrj\MainProj.sln" 2>&1 | findstr /C:"error"
```

---

### Step 7：功能验证

**目标**：手动 GUI 测试，确认诊断快照正常输出。

1. 启动 GUI 应用
2. 触发事件绑定流程
3. 检查控制台输出，确认诊断快照正常显示
4. 检查 `WdUILog` / `BackEndLog` 输出，确认无异常抛出

---

## 四、涉及文件清单

| # | 文件路径 | 修改类型 | 说明 |
|---|---------|---------|------|
| 1 | `CsharpPrj/Share/EventBindingOrchestrator/Diagnostics/EventBindingDiagnosticContext.cs` | 修改 | `GetSnapshotString()` 返回 `"{}"` 而非 `string.Empty` |
| 2 | `CsharpPrj/Operation/BackEndDriven/IoC/EventBindingIoCInitializer.cs` | 修改 | `RecordDiagnostic` 改为 `IocLog.Log` |
| 3 | `CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodExecutionService.cs` | 修改 | `EventBindingDiagnosticContext.Record` 改为 `WorkFlowMethodLog.Log` |
| 4 | `CsharpPrj/Operation/BackEndDriven/Pipeline/RenderPipelineOrchestrator.cs` | 修改 | 快照为空时抛出异常 |
| 5 | `CsharpPrj/Operation/WdUI/WdUI.RenderPipeline.cs` | 修改 | 快照为空时抛出异常 |

---

## 五、框架层面待办事项（后续迭代）

以下问题属于框架层面，当前修复文档暂不涉及，留待后续迭代：

| # | 问题 | 说明 | 优先级 |
|---|------|------|--------|
| 1 | `EventBindingDiagnosticContext.BeginScope()` 不创建 `DiagnosticContext` 作用域 | 当前返回空 `Scope`，`DiagnosticContext.Current` 始终为 null | 低 |
| 2 | `DiagnosticsScopeStage` 创建的作用域未被释放 | 管线执行完毕后没有 `Dispose()` 调用 | 低 |
| 3 | `EventBindingDiagnosticContext.Record()` 在管线执行完毕后仍被调用 | `ExecuteWithWalk()` 中管线执行完毕后仍有 `Record()` 调用 | 低 |

> **说明**：上述框架问题当前被 `GetSnapshotString()` 返回 `"{}"` 的降级策略掩盖，不影响核心业务逻辑。待诊断链路需要真正输出数据时再行修复。

---

## 六、回退方案

如果修复后出现异常，可临时回退：

1. **Step 1 回退**：将 `GetSnapshotString()` 恢复为返回 `string.Empty`
2. **Step 5 回退**：将快照检查改为 `if` 日志记录而非 `throw`
3. **完整回退**：`git checkout -- <file>` 逐个文件回退
