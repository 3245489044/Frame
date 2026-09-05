# 诊断输出链路修复 + Operation 层日志违规清理

> **背景：** 预解析重构中新增的诊断记录（`eventbinder.preresolve.*`）在运行时日志中不可见。
> 排查发现两个问题：诊断输出链路断裂、Operation 层违规调用字符串框架。
>
> **关联文档：** [事件绑定预解析重构.md](./事件绑定预解析重构.md)

---

## 1. 问题一：诊断输出链路断裂

### 1.1 根因

`EventBindingDiagnosticContext.GetSnapshotString()` 和 `CaptureSnapshot()` 是 **stub 实现**，永远返回空值/空对象。

```csharp
// EventBindingDiagnosticContext.cs — 当前 stub 实现
public static string GetSnapshotString() => string.Empty;   // ← 永远返回空
public static object CaptureSnapshot() => new object();      // ← 永远返回空对象
```

### 1.2 当前诊断链路

```
Share 层:
  EventBindingDiagnosticContext.Record(key, data)
    → DiagnosticContext.Current?.Record(key, data)    ← 写入 AsyncLocal 上下文
      → ScopeNode.AddEntry(entry)                     ← 存储在内存中（有效）

Operation 层:
  OutputDiagnosticSnapshot()
    → EventBindingDiagnosticContext.GetSnapshotString()  ← return string.Empty!!!
    → if (!string.IsNullOrWhiteSpace(snapshot))          ← 永远为 false
    → BackEndLog.Log(...)                                ← 永远不执行
```

**结果：** 所有诊断数据都只存在内存中，从未被输出到 ILogger。

### 1.3 修复方案

让 `GetSnapshotString()` 和 `CaptureSnapshot()` 真正从 `DiagnosticContext.Current` 导出数据：

```csharp
// EventBindingDiagnosticContext.cs — 修复后
public static string GetSnapshotString()
{
    var context = DiagnosticContext.Current;
    if (context == null) return string.Empty;
    return context.ToJson();
}

public static object CaptureSnapshot()
{
    var context = DiagnosticContext.Current;
    if (context == null) return new object();
    return context.Freeze();
}
```

### 1.4 修复后链路

```
Share 层:
  EventBindingDiagnosticContext.Record(key, data)
    → DiagnosticContext.Current?.Record(key, data)
      → ScopeNode.AddEntry(entry)

Operation 层:
  OutputDiagnosticSnapshot()
    → EventBindingDiagnosticContext.GetSnapshotString()
      → DiagnosticContext.Current.ToJson()              ← 真正导出 JSON
    → if (!string.IsNullOrWhiteSpace(snapshot))          ← true
    → BackEndLog.Log("RenderPipelineOrchestrator_DiagnosticSnapshot", ...)  ← 输出到 ILogger
```

---

## 2. 问题二：Operation 层违规调用字符串框架

### 2.1 架构原则

```
Share 层: EventBindingDiagnosticContext.Record(key, data)
  → DiagnosticContext.Current?.Record(key, data)  ← 字符串框架基础设施，正确

Operation 层: IocLog.Log / WorkFlowMethodLog.Log
  → LogBase.Log → ILogger  ← 直接打印输出，正确
```

**Share 层的 `Record` 是字符串框架基础设施**——它把诊断事件记录到 `DiagnosticContext.Current` 这个字符串收集器中，这是正确的设计。

**违规的是 Operation 层**——Operation 层不应该调用 `EventBindingDiagnosticContext.Record`（字符串框架），而应该直接通过 `IocLog.Log` / `WorkFlowMethodLog.Log` 打印日志。

### 2.2 违规点

#### 违规点 1：`EventBindingIoCInitializer.cs`

```csharp
// 当前代码（第 70-71 行）— ❌ Operation 层调用字符串框架
ioc.RecordDiagnostic = (key, data) =>
    EventBindingDiagnosticContext.Record(key, data);

// 应改为 — ✅ 直接打印到 ILogger
ioc.RecordDiagnostic = (key, data) =>
    IocLog.Log("Preresolve_" + key.Replace(".", "_"), data);
```

#### 违规点 2：`WorkflowMethodExecutionService.cs`

```csharp
// 当前代码 — ❌ Operation 层调用字符串框架
EventBindingDiagnosticContext.Record("eventbinder.workflow.auto_wrapped_from_registry", new { ... });

// 应改为 — ✅ 直接打印到 ILogger
WorkFlowMethodLog.Log("Workflow_AutoWrappedFromRegistry", new { ... });
```

### 2.3 影响范围

`EventBindingIoC.cs` 中新增的 `RecordDiagnostic?.Invoke(...)` 调用本身没问题——`RecordDiagnostic` 委托由 Operation 层注入，只要注入的实现改为 `IocLog.Log`，这些调用就会直接输出到 ILogger 管道，不再依赖 `DiagnosticContext.Current`。

---

## 3. 改动清单

| # | 文件 | 改动 | 说明 |
|---|------|------|------|
| 1 | `CsharpPrj/Share/EventBindingOrchestrator/Diagnostics/EventBindingDiagnosticContext.cs` | `GetSnapshotString()` → 调用 `DiagnosticContext.Current?.ToJson()`；`CaptureSnapshot()` → 调用 `DiagnosticContext.Current?.Freeze()` | 修复诊断输出链路断裂 |
| 2 | `CsharpPrj/Operation/BackEndDriven/IoC/EventBindingIoCInitializer.cs` | `RecordDiagnostic` 委托从 `EventBindingDiagnosticContext.Record` 改为 `IocLog.Log` | 修复 Operation 层违规调用 |
| 3 | `CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodExecutionService.cs` | `EventBindingDiagnosticContext.Record` 改为 `WorkFlowMethodLog.Log` | 修复 Operation 层违规调用 |

---

## 4. 验证标准

### 4.1 构建验证

```bash
dotnet build "d:\Project\Program\WorkFlowPrj\CsharpPrj\MainProj.sln" --no-restore 2>&1
```

### 4.2 功能验证

1. 启动 GUI 模式，加载 MathFunctions.unified.json
2. 点击 Add/Subtract/Multiply/Divide 按钮
3. 验证结果正确显示在 resultTextBox 中
4. 验证日志中无异常

### 4.3 诊断日志验证

- 日志中出现 `Preresolve_*` 条目（来自 `RecordDiagnostic` 委托）
- 日志中出现 `RenderPipelineOrchestrator_DiagnosticSnapshot` 条目（来自 `OutputDiagnosticSnapshot`）
- 诊断快照中包含预解析相关事件（`eventbinder.preresolve.*`）

---

## 5. 执行顺序

| Step | 改动 | 依赖 |
|------|------|------|
| 1 | `EventBindingDiagnosticContext.cs` — 修复 `GetSnapshotString` / `CaptureSnapshot` | 无 |
| 2 | `EventBindingIoCInitializer.cs` — `RecordDiagnostic` 改为 `IocLog.Log` | 无 |
| 3 | `WorkflowMethodExecutionService.cs` — `EventBindingDiagnosticContext.Record` 改为 `WorkFlowMethodLog.Log` | 无 |
| 4 | 构建验证 | Step 1, 2, 3 |
| 5 | 运行测试 | Step 4 |
