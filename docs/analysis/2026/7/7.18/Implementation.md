# C# 项目日志实现参考

> **版本：** v1.0
> **维护者：** WorkFlowPrj Team
> **更新日期：** 2026-07-11
>
> **说明：** 本文档是 [`Architecture.md`](Architecture.md) 的配套实现参考，包含所有日志相关的代码实现、接口契约、数据流定义和文件索引。架构设计请参阅 `Architecture.md`。

---

## 目录

- [C# 项目日志实现参考](#c-项目日志实现参考)
  - [目录](#目录)
  - [1. 实现源码](#1-实现源码)
    - [1.1 Operation 层日志入口](#11-operation-层日志入口)
      - [BackEndLog](#backendlog)
      - [EventTriggerLog](#eventtriggerlog)
      - [WdUILog](#wduilog)
      - [WorkFlowMethodLog](#workflowmethodlog)
    - [1.2 三级降级策略](#12-三级降级策略)
    - [1.3 Share 层插桩代码](#13-share-层插桩代码)
      - [核心 API](#核心-api)
      - [降级兼容机制](#降级兼容机制)
  - [2. 接口契约](#2-接口契约)
    - [2.1 Operation 层日志入口接口](#21-operation-层日志入口接口)
    - [2.2 Share 层插桩接口](#22-share-层插桩接口)
    - [2.3 降级契约](#23-降级契约)
  - [3. 数据流](#3-数据流)
    - [3.1 编译时依赖](#31-编译时依赖)
    - [3.2 运行时数据流](#32-运行时数据流)
    - [3.3 跨线程数据流](#33-跨线程数据流)
  - [4. 日志管线基础设施](#4-日志管线基础设施)
    - [4.1 管线编排](#41-管线编排)
    - [4.2 组件清单](#42-组件清单)
    - [4.3 LogEntry 结构](#43-logentry-结构)
  - [5. Modules.DiagnosticContext 插桩框架](#5-modulesdiagnosticcontext-插桩框架)
    - [5.1 核心类型](#51-核心类型)
    - [5.2 导出格式](#52-导出格式)
  - [6. 消息注册文件格式](#6-消息注册文件格式)
  - [7. 代码示例](#7-代码示例)
    - [7.1 Operation 层输出日志](#71-operation-层输出日志)
    - [7.2 Share 层插桩](#72-share-层插桩)
    - [7.3 跨线程场景](#73-跨线程场景)
    - [7.4 Operation 层消费诊断快照](#74-operation-层消费诊断快照)
    - [7.5 日志管线构建](#75-日志管线构建)
  - [8. 迁移指南](#8-迁移指南)
    - [8.1 EventTriggerLog → EventBindingDiagnosticContext](#81-eventtriggerlog--eventbindingdiagnosticcontext)
    - [8.2 迁移步骤](#82-迁移步骤)
    - [8.3 代码审查清单](#83-代码审查清单)
  - [附录](#附录)
    - [A. 文件索引](#a-文件索引)
    - [B. 修订历史](#b-修订历史)

---

## 1. 实现源码

### 1.1 Operation 层日志入口

#### BackEndLog

**文件：** `CsharpPrj/Operation/BackEndDriven/Diagnostics/BackEndLog.cs`

```csharp
namespace BackEndDriven.Diagnostics;

public static class BackEndLog
{
    private const string ModuleName = "BackEndDriven";
    private const string KeyPrefix = "backend:";

    public static void Log(string messageId, object? data = null)
        => LogBase.Log(ModuleName, KeyPrefix, messageId, data);
}
```

#### EventTriggerLog

**文件：** `CsharpPrj/Operation/BackEndDriven/Diagnostics/EventTriggerLog.cs`

```csharp
namespace EventTriggerFramework.Diagnostics;

public static class EventTriggerLog
{
    private const string ModuleName = "EventTrigger";
    private const string KeyPrefix = "eventtrigger:";

    public static void Log(string messageId, object? data = null)
        => LogBase.Log(ModuleName, KeyPrefix, messageId, data);
}
```

#### WdUILog

**文件：** `CsharpPrj/Operation/WdUI/Diagnostics/WdUILog.cs`

```csharp
namespace WdUI.Diagnostics;

public static class WdUILog
{
    public static void Log(string messageId, object? data = null)
    {
        var cleanMsgId = messageId.StartsWith("wdui:") ? messageId[5..] : messageId;
        LogPlaceholderHandlerBase.Log(cleanMsgId, "WdUI", "wdui:", data);
    }
}
```

#### WorkFlowMethodLog

**文件：** `CsharpPrj/Operation/WorkFlowMethod/Diagnostics/WorkFlowMethodLog.cs`

```csharp
namespace WorkFlowMethod.Diagnostics;

public static class WorkFlowMethodLog
{
    private const string ModuleName = "WorkFlowMethod";
    private const string KeyPrefix = "workflowmethod:";

    public static void Initialize(string jsonFilePath)
        => LogPlaceholderHandlerBase.Initialize(jsonFilePath, KeyPrefix);

    public static void Log(string messageId, object? data = null)
        => LogPlaceholderHandlerBase.Log(messageId, ModuleName, KeyPrefix, data);
}
```

### 1.2 三级降级策略

**文件：** `CsharpPrj/Operation/BackEndDriven/Diagnostics/LogBase.cs`

```
第一级：LogPlaceholderHandlerBase.Log
  → 标准日志通道，需要消息字典注册
  → 成功时直接输出结构化日志

第二级：PlaceholderLogHelper.Log
  → LogRepository 降级
  → 当消息字典未注册时使用

第三级：Debug.WriteLine（仅首次）
  → 最终保底输出到 VS 输出窗口
  → 使用静态标记 _placeholderInitialized 确保仅首次触发
```

```csharp
internal static void Log(string moduleName, string keyPrefix, string messageId, object? data = null)
{
    bool logged = LogPlaceholderHandlerBase.Log(messageId, moduleName, keyPrefix, data);
    if (!logged)
    {
        var dataStr = data != null ? $" {JsonSerializer.Serialize(data, ...)}" : "";
        PlaceholderLogHelper.Log("DEBUG", messageId, moduleName,
            $"[{keyPrefix}{messageId}]{dataStr}");

        if (!_placeholderInitialized)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[{moduleName}] [{keyPrefix}{messageId}]{dataStr}");
            _placeholderInitialized = true;
        }
    }
}
```

### 1.3 Share 层插桩代码

**文件：** `CsharpPrj/Share/EventBindingOrchestrator/Contexts/EventBindingDiagnosticContext.cs`

#### 核心 API

| 方法 | 签名 | 说明 |
|------|------|------|
| `Current` | `static DiagnosticContext?` | 获取当前异步流上的诊断上下文 |
| `BeginScope()` | `static IDisposable` | 开始一个新的诊断作用域 |
| `Record` | `static void(string, object?)` | 记录一个插桩事件 |
| `CaptureSnapshot()` | `static DiagnosticContext?` | 捕获当前上下文快照（用于跨线程） |
| `RestoreScope(snapshot)` | `static IDisposable?(DiagnosticContext?)` | 恢复上下文作用域 |
| `GetSnapshotString()` | `static string?` | 获取诊断快照字符串 |

#### 降级兼容机制

```csharp
public static void Record(string eventName, object? data = null)
{
    var ctx = Current;
    if (ctx != null)
    {
        ctx.Record(eventName, data);
        return;
    }

    // 降级：Debug.WriteLine
    var dataJson = data != null
        ? JsonSerializer.Serialize(data, JsonSerializerFactory.Contexts.LogExport)
        : "";
    System.Diagnostics.Debug.WriteLine($"[EventBinding] {eventName} {dataJson}");
}
```

---

## 2. 接口契约

### 2.1 Operation 层日志入口接口

```csharp
// 所有 Operation 层日志入口遵循的统一签名
public static void Log(string messageId, object? data = null);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `messageId` | `string` | 日志消息 ID（不含 KeyPrefix） |
| `data` | `object?` | 结构化日志数据（匿名对象或 null） |

### 2.2 Share 层插桩接口

```csharp
// Share 层插桩的统一入口
public static void Record(string eventName, object? data = null);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `eventName` | `string` | 事件名称（点分隔命名空间风格） |
| `data` | `object?` | 结构化事件数据（匿名对象或 null） |

### 2.3 降级契约

| 条件 | 行为 |
|------|------|
| Operation 层：消息字典已注册 | `LogPlaceholderHandlerBase.Log` 输出结构化日志 |
| Operation 层：消息字典未注册 | `PlaceholderLogHelper.Log` → `Debug.WriteLine` |
| Share 层：有活跃 DiagnosticContext | `DiagnosticContext.Record` 记录到作用域树 |
| Share 层：无活跃 DiagnosticContext | `Debug.WriteLine` 直接输出 |

---

## 3. 数据流

### 3.1 编译时依赖

```
Operation ──▶ Share ──▶ Share.Diagnostics ──▶ Modules.DiagnosticContext ──▶ BasicMethod
                              │
                              └──▶ Modules.Pipeline
```

### 3.2 运行时数据流

```
┌─────────────────────────────────────────────────────────────────────┐
│  Operation 层（日志消费者）                                           │
│                                                                      │
│  ① EventBindingDiagnosticContext.BeginScope()                        │
│     → DiagnosticManager.BeginScope("EventBindingOrchestrator")       │
│     → AsyncContext<DiagnosticContext>.CreateScope(diagCtx)           │
│                                                                      │
│  ② 执行 Share 层代码                                                 │
│     → Share 层调用 Record("event.name", data)                        │
│     → DiagnosticContext.Record → ScopeNode.Record → EventEntry       │
│                                                                      │
│  ③ EventBindingDiagnosticContext.GetSnapshotString()                 │
│     → DiagnosticManager.GetSnapshot()                                │
│     → 返回所有活跃上下文的文本快照                                     │
│                                                                      │
│  ④ BackEndLog.Log("MessageId", data)                                 │
│     → LogBase.Log → LogPlaceholderHandlerBase.Log                    │
│     → LogRepository.Write → SqliteIsolatedStorage                    │
└─────────────────────────────────────────────────────────────────────┘
```

### 3.3 跨线程数据流

```
主线程                             子线程（Task.Run）
  │                                   │
  ├─ BeginScope()                     │
  ├─ CaptureSnapshot() ──── snapshot ─┤
  │                                   ├─ RestoreScope(snapshot)
  │                                   ├─ Record("event.name", data)
  │                                   └─ Dispose()
  ├─ Record("event.name", data)       │
  └─ Dispose()                        │
```

---

## 4. 日志管线基础设施

### 4.1 管线编排

日志管线通过 `LogPipelineAdapter` 注册到 `CentralPipeline` 的各阶段：

| 阶段 | Skeleton | 说明 |
|------|----------|------|
| `Initialization` | `LogInitSkeleton` | 注册批次，设置日志分类 |
| `Execution` | `LoggingSkeletonWrapper(business)` | 包装业务 Skeleton，自动记录开始/结束 |
| `PostProcessing` | `LogTryExecuteSkeleton(LogFlush, LogEmergencyDump)` | 刷入 SQLite，异常时紧急转储 |
| `Finalization` | `LogSummarySkeleton` | 输出日志摘要 |

**管线构建示例：**

```csharp
// 默认管线：LogInit → LogFlush → LogSummary
var pipeline = LogPipelineAdapter.CreateDefaultPipeline();
pipeline.Build();

// 带业务 Skeleton 的管线
var flow = LogPipelineAdapter.CreateWithBusinessSkeleton(businessSkeleton);
```

### 4.2 组件清单

| 组件 | 命名空间 | 说明 |
|------|----------|------|
| `LogRepository` | `Common.Diagnostics` | 内存日志仓库，提供 `Write` 方法 |
| `SqliteIsolatedStorage` | `Common.Diagnostics` | SQLite 持久化存储实现 |
| `BatchManager` | `UniversalWorkflowFramework.BatchIntegration` | 批次管理，注册/完成批次 |
| `BatchRouter` | `UniversalWorkflowFramework.BatchIntegration` | 批次路由，刷入隔离存储 |
| `LogEntryBuilder` | `Common.Diagnostics` | 便捷日志 API（Info/Warn/Error/Debug） |

### 4.3 LogEntry 结构

| 字段 | 类型 | 说明 |
|------|------|------|
| `Timestamp` | `DateTime` | 时间戳 |
| `Level` | `string` | 日志级别（INFO/WARN/ERROR/DEBUG） |
| `Category` | `LogCategory` | 日志分类（位标志） |
| `Module` | `string` | 模块名称 |
| `Message` | `string` | 日志消息 |
| `SourceFile` | `string?` | 源文件路径 |
| `SourceLine` | `int?` | 源文件行号 |
| `SourceMember` | `string?` | 调用成员名称 |
| `Exception` | `Exception?` | 异常信息（结构化 JSON 序列化） |
| `Extensions` | `Dictionary` | 扩展属性（如 BatchId） |

---

## 5. Modules.DiagnosticContext 插桩框架

### 5.1 核心类型

| 类型 | 说明 |
|------|------|
| `IDiagnosticContext` | 诊断上下文抽象接口（Record / SetState / GetState / CreateScope） |
| `DiagnosticContext` | AsyncLocal 自动传播实现，管理 ScopeStack |
| `DiagnosticManager` | 全局入口（BeginScope / GetSnapshot / RecordToAll） |
| `ScopeNode` | 嵌套作用域树节点（事件列表 + 状态字典 + 子作用域） |
| `EventEntry` | 结构化事件条目（事件名 + 相对时间戳 + 数据 JSON） |

### 5.2 导出格式

| 方法 | 格式 | 用途 |
|------|------|------|
| `ToString()` | 文本快照 | 完整事件列表 + 状态 + 子作用域 |
| `ToSummary()` | 摘要 | 仅统计信息（事件数、状态数、作用域数） |
| `ToJson()` | JSON | 结构化导出，适合程序化处理 |

---

## 6. 消息注册文件格式

Operation 层的每条日志消息需要在对应的 JSON 文件中注册：

```json
{
  "version": "1.0",
  "description": "模块描述",
  "messages": {
    "MessageId_xxx": {
      "level": "Info|Debug|Warning|Error",
      "category": "模块_分类",
      "template": "[模块名] 描述文本: {参数名}"
    }
  }
}
```

**消息注册文件位置：**

| 文件 | 路径 |
|------|------|
| BackEndDriven | `CsharpPrj/Operation/BackEndDriven/backend_log_messages.json` |
| WdUI | `CsharpPrj/Operation/WdUI/wdui_log_messages.json` |
| WorkFlowMethod | `CsharpPrj/Operation/WorkFlowMethod/workflowmethod_log_messages.json` |

---

## 7. 代码示例

### 7.1 Operation 层输出日志

```csharp
// BackEndDriven 模块
BackEndLog.Log("ExecutionEngine_StartFile", new { commandFile = "test.txt" });

// EventTrigger 模块
EventTriggerLog.Log("EventTrigger_Invoke_start", new { eventName = "Click" });

// WdUI 模块
WdUILog.Log("ConfigContainer_RenderConfigAsync_ApplyLayout_done", new { targetName = "panel1" });

// WorkFlowMethod 模块
WorkFlowMethodLog.Log("WorkflowMethodExecution_Execute_start", new { Function = "Math.Add", ParamCount = 2 });
```

### 7.2 Share 层插桩

```csharp
using Share.EventBindingOrchestrator.Diagnostics;

// 创建作用域
using var diagScope = EventBindingDiagnosticContext.BeginScope();

// 记录插桩事件
EventBindingDiagnosticContext.Record("pipeline.execute_with_walk.start", new
{
    controlName = "button1",
    configIsNull = config == null
});

// 循环中记录
for (int i = 0; i < items.Count; i++)
{
    EventBindingDiagnosticContext.Record("pipeline.walk_and_bind.visiting", new
    {
        index = i,
        controlName = items[i].Name
    });
}
```

### 7.3 跨线程场景

```csharp
// 主线程捕获快照
var snapshot = EventBindingDiagnosticContext.CaptureSnapshot();

Task.Run(() =>
{
    // 子线程恢复上下文
    using var restored = EventBindingDiagnosticContext.RestoreScope(snapshot);
    EventBindingDiagnosticContext.Record("eventbinder.function.success", new
    {
        functionName = "Math.Add",
        result = 42
    });
});
```

### 7.4 Operation 层消费诊断快照

```csharp
// Share 层代码执行完毕后，获取诊断快照
var pipelineSnapshot = EventBindingDiagnosticContext.GetSnapshotString();
if (pipelineSnapshot != null)
{
    // Operation 层决定如何输出
    BackEndLog.Log("Pipeline_Snapshot", new { snapshot = pipelineSnapshot });
}
```

### 7.5 日志管线构建

```csharp
// 默认管线：LogInit → LogFlush → LogSummary
var pipeline = LogPipelineAdapter.CreateDefaultPipeline();
pipeline.Build();

// 带业务 Skeleton 的管线
var flow = LogPipelineAdapter.CreateWithBusinessSkeleton(businessSkeleton);

// 便捷日志 API
LogEntryBuilder.Info("ModuleName", "消息内容", "Category");
LogEntryBuilder.Warn("ModuleName", "警告消息", "Category");
LogEntryBuilder.Error("ModuleName", "错误消息", "Category");
LogEntryBuilder.Debug("ModuleName", "调试消息", "Category");
```

---

## 8. 迁移指南

### 8.1 EventTriggerLog → EventBindingDiagnosticContext

| 对比维度 | 原写法（❌ 禁止） | 新写法（✅ 推荐） |
|----------|-------------------|-------------------|
| 调用方式 | `EventTriggerLog.Log("[DEBUG] msg", data)` | `EventBindingDiagnosticContext.Record("event_name", data)` |
| 标识格式 | `[ClassName.DEBUG] description` | `namespace.event_name`（点分隔） |
| 注册要求 | 需要 `filedriven_log_messages.json` 注册 | 无需注册，自动通过 DiagnosticContext 收集 |
| 输出控制 | 由 EventTriggerLog 内部决定 | 由 Operation 层决定 |

### 8.2 迁移步骤

```
Step 1: 定位所有 EventTriggerLog.Log 调用（Share 层代码中）
    ↓
Step 2: 替换为 EventBindingDiagnosticContext.Record
    ↓
Step 3: 将消息 ID 从 "[ClassName.DEBUG] description" 改为点分隔命名空间风格
    ↓
Step 4: 保留结构化数据参数不变
    ↓
Step 5: 如果调用处没有活跃的 DiagnosticContext，在入口处添加 BeginScope()
```

### 8.3 代码审查清单

- [ ] Share 层代码中是否使用了 `EventTriggerLog.Log`？
- [ ] Share 层代码中是否使用了 `WdUILog.Log` 或 `UILog.Log`？
- [ ] Share 层代码中是否使用了任何需要消息字典注册的日志系统？
- [ ] Operation 层代码是否通过 `EventBindingDiagnosticContext` 获取诊断快照？
- [ ] 事件名是否使用点分隔命名空间风格？
- [ ] 消息 ID 是否已在对应的 JSON 文件中注册？

---

## 附录

### A. 文件索引

| 文件 | 说明 |
|------|------|
| `CsharpPrj/Share/ShareDefinition.md` | Share 层日志使用规范（原始定义） |
| `CsharpPrj/Share/EventBindingOrchestrator/Contexts/EventBindingDiagnosticContext.cs` | EventBindingDiagnosticContext 实现 |
| `CsharpPrj/Share/Diagnostics/Pipeline/LogPipeline.cs` | 日志管线定义 |
| `CsharpPrj/Share/Diagnostics/Pipeline/LogPipelineAdapter.cs` | 日志管线 CentralPipeline 适配器 |
| `CsharpPrj/Share/Diagnostics/Pipeline/LogPipelineContext.cs` | 日志管线上下文 |
| `CsharpPrj/Share/Diagnostics/Storage/LogStorage.cs` | SQLite 持久化存储 |
| `CsharpPrj/Share/Diagnostics/Wrappers/Wrappers.cs` | 日志包装器（LogEntryBuilder、LoggingSkeletonWrapper） |
| `CsharpPrj/Modules/DiagnosticContext/Core/DiagnosticContext.cs` | DiagnosticContext 核心实现 |
| `CsharpPrj/Modules/DiagnosticContext/Core/DiagnosticManager.cs` | 诊断上下文管理器 |
| `CsharpPrj/Modules/DiagnosticContext/Core/ScopeNode.cs` | 嵌套作用域节点 |
| `CsharpPrj/Modules/DiagnosticContext/Core/EventEntry.cs` | 事件条目模型 |
| `CsharpPrj/Modules/DiagnosticContext/Abstractions/IDiagnosticContext.cs` | 诊断上下文抽象接口 |
| `CsharpPrj/Operation/BackEndDriven/Diagnostics/LogBase.cs` | 日志公共基类（三级降级） |
| `CsharpPrj/Operation/BackEndDriven/Diagnostics/BackEndLog.cs` | BackEndDriven 日志入口 |
| `CsharpPrj/Operation/BackEndDriven/Diagnostics/EventTriggerLog.cs` | EventTrigger 日志入口 |
| `CsharpPrj/Operation/WdUI/Diagnostics/WdUILog.cs` | WdUI 日志入口 |
| `CsharpPrj/Operation/WorkFlowMethod/Diagnostics/WorkFlowMethodLog.cs` | WorkFlowMethod 日志入口 |
| `CsharpPrj/Operation/BackEndDriven/backend_log_messages.json` | BackEndDriven 消息注册文件 |
| `CsharpPrj/Operation/WdUI/wdui_log_messages.json` | WdUI 消息注册文件 |
| `CsharpPrj/Operation/WorkFlowMethod/workflowmethod_log_messages.json` | WorkFlowMethod 消息注册文件 |

### B. 修订历史

| 版本 | 日期 | 修订内容 | 修订人 |
|------|------|----------|--------|
| v1.0 | 2026-07-11 | 初始版本，从 Architecture.md 分离代码和文件逻辑 | WorkFlowPrj Team |
