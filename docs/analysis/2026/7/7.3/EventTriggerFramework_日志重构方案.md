# EventTriggerFramework 日志重构方案

> **日期**：2026-07-03
> **背景**：MathFunctions.json 按钮操作无效问题排查过程中发现运行时日志不输出
> **原则**：Operation 层不反向引用 FileDriven，参考 WdUI 日志模式

---

## 1. 问题背景

### 1.1 现象

在 `MathFunctions.json` 配置加载后，点击按钮（Add/Subtract/Multiply/Divide）没有任何日志输出，用户无法判断：
- 按钮点击是否触发了事件？
- 函数是否执行成功？
- 错误原因是什么？

### 1.2 根因

`EventBindingTransformers.cs` 中的 `CreateEventHandler` 方法使用 `ctx?.Log(...)` 输出运行时日志，其中 `ctx` 是 `TransformContext?` 类型，来自绑定阶段的 `TransformContext.Current`。

```csharp
// 绑定阶段 — TransformContext.Current 为 null
var ctx = TransformContext.Current;  // ← null
ctx?.Log($"[EventBinder] 绑定控件 ...");  // ← 不输出

// CreateEventHandler 中捕获的 ctx（运行时阶段）
return async (sender, args) =>
{
    ctx?.Log($"[EventBinder] 触发事件 ...");  // ← ctx 为 null，不输出
};
```

**原因链：**
1. `EventBindingPipelineRunner.Execute()` **没有调用 `TransformContext.BeginScope()`**，所以 `TransformContext.Current` 在绑定阶段就是 null
2. `CreateEventHandler` 捕获的 `ctx` 参数是 null
3. 运行时 handler 执行时，`ctx?.Log(...)` 什么都不做
4. 异常 catch 块中的 `ctx?.Log(...)` 同样不输出

### 1.3 影响范围

| 位置 | 方法 | 日志内容 | 当前状态 |
|------|------|---------|---------|
| 绑定阶段 | `EventBinderTransformer.Transform()` | 绑定控件、绑定事件、绑定失败 | ❌ 不输出 |
| 运行时 | `CreateEventHandler` handler | 触发事件、执行成功/失败/异常 | ❌ 不输出 |
| 守卫 | `ScriptModeGuardTransformer.Transform()` | 脚本模式跳过 | ❌ 不输出 |

---

## 2. 参考方案：WdUI 日志模式

WdUI（Operation 层）已经成功实现了独立于 `TransformContext` 的日志系统：

### 2.1 架构

```
WdUI/Diagnostics/WdUILog.cs          ← 统一日志入口
WdUI/wdui_log_messages.json          ← 日志消息定义
Share/PlaceholderFramework/          ← 底层日志基础设施
  ├── LogPlaceholderHandlerBase.cs   ← 模板化日志处理器
  ├── PlaceholderLogHelper.cs        ← 降级输出辅助类
  └── LogRepository.cs               ← 日志存储
```

### 2.2 WdUILog 统一入口

```csharp
// WdUI/Diagnostics/WdUILog.cs
public static class WdUILog
{
    public static void Log(string messageId, object? data = null)
    {
        // 优先：通过 PlaceholderHandler 输出（带模板格式化）
        bool logged = LogPlaceholderHandlerBase.Log(messageId, "WdUI", "wdui:", data);
        if (!logged)
        {
            // 降级：直接输出到 LogRepository
            var dataStr = data != null ? $" {JsonSerializer.Serialize(data)}" : "";
            PlaceholderLogHelper.Log("DEBUG", "WdUI_Render", "WdUI", $"[wdui:{messageId}]{dataStr}");
        }
    }
}
```

### 2.3 日志消息定义

```json
// wdui_log_messages.json
{
  "messages": {
    "RenderPipeline_ExecuteCreateStage_done": {
      "level": "Debug",
      "category": "WdUI_Render",
      "template": "[RenderPipeline] CreateStage done: {targetName}"
    }
  }
}
```

### 2.4 调用方式

```csharp
WdUILog.Log("RenderPipeline_ExecuteCreateStage_done",
    new { targetName = target.Name });
```

### 2.5 关键设计

| 特性 | 说明 |
|------|------|
| **不依赖 TransformContext** | 使用独立的 `LogPlaceholderHandlerBase.Log` 静态方法 |
| **不依赖 FileDriven** | 使用 Share 层的 PlaceholderFramework |
| **降级机制** | PlaceholderHandler 未初始化时降级到 `PlaceholderLogHelper` |
| **结构化数据** | 支持匿名对象作为结构化日志数据 |
| **消息 ID 驱动** | 日志格式在 `wdui_log_messages.json` 中定义，代码中只传 ID |

---

## 3. 重构方案

### 3.1 核心变更

为 EventTriggerFramework 创建独立的日志系统，参考 WdUI 模式：

```
EventTriggerFramework/
  ├── Diagnostics/
  │   └── EventTriggerLog.cs          ← 新建：统一日志入口
  ├── eventtrigger_log_messages.json   ← 新建：日志消息定义
  └── EventBinding/
      └── EventBindingTransformers.cs  ← 修改：替换 ctx?.Log()
```

### 3.2 新建文件：`EventTriggerLog.cs`

```csharp
// CsharpPrj/Operation/EventTriggerFramework/Diagnostics/EventTriggerLog.cs
#nullable enable
using System.Text.Json;
using Common.PlaceholderFramework.Core;
using Common.PlaceholderFramework.Integrated.Handlers;

namespace EventTriggerFramework.Diagnostics
{
    /// <summary>
    /// EventTriggerFramework 统一日志入口
    /// <para>封装 LogPlaceholderHandlerBase.Log，提供更简洁的调用签名</para>
    /// <para>自动填充 module="EventTrigger" 和 category="eventtrigger:" 参数</para>
    /// <para>★ 不依赖 TransformContext，运行时 handler 中也可正常输出 ★</para>
    /// <para>★ 不依赖 FileDriven，Operation 层独立日志通道 ★</para>
    /// </summary>
    public static class EventTriggerLog
    {
        private const string ModuleName = "EventTrigger";
        private const string KeyPrefix = "eventtrigger:";

        /// <summary>
        /// 输出日志（带结构化数据）
        /// </summary>
        /// <param name="messageId">日志消息 ID（不含 "eventtrigger:" 前缀）</param>
        /// <param name="data">结构化日志数据（匿名对象）</param>
        public static void Log(string messageId, object? data = null)
        {
            bool logged = LogPlaceholderHandlerBase.Log(messageId, ModuleName, KeyPrefix, data);
            if (!logged)
            {
                // 降级：当 eventtrigger: 日志加载器未初始化时，
                // 直接输出到 LogRepository，确保日志能被捕获
                var dataStr = data != null ? $" {JsonSerializer.Serialize(data)}" : "";
                PlaceholderLogHelper.Log("DEBUG", "EventTrigger_General", ModuleName,
                    $"[eventtrigger:{messageId}]{dataStr}");
            }
        }
    }
}
```

### 3.3 新建文件：`eventtrigger_log_messages.json`

```json
{
  "version": "1.0",
  "description": "EventTriggerFramework 日志消息定义 - 使用 ClassName_MethodName_description 命名",
  "messages": {
    "ScriptModeGuard_Active": {
      "level": "Debug",
      "category": "EventTrigger_General",
      "template": "[ScriptModeGuard] 脚本执行模式激活，跳过事件绑定 (控件: '{controlName}')"
    },
    "EventBinder_BindingControl": {
      "level": "Debug",
      "category": "EventTrigger_Event",
      "template": "[EventBinder] 绑定控件 '{controlName}' 的事件"
    },
    "EventBinder_BoundEvent": {
      "level": "Debug",
      "category": "EventTrigger_Event",
      "template": "[EventBinder]   绑定事件 '{eventName}' → '{functionName}'"
    },
    "EventBinder_BoundCustomEvent": {
      "level": "Debug",
      "category": "EventTrigger_Event",
      "template": "[EventBinder]   绑定自定义事件 '{eventName}' → '{functionName}'"
    },
    "EventBinder_BindingFailed": {
      "level": "Error",
      "category": "EventTrigger_Event",
      "template": "[EventBinder] 绑定控件 '{controlName}' 失败: {error}"
    },
    "EventBinder_TriggerEvent": {
      "level": "Debug",
      "category": "EventTrigger_Event",
      "template": "[EventBinder] 触发事件 '{eventName}' → '{functionName}' (控件: '{controlName}')"
    },
    "EventBinder_ExecuteSuccess": {
      "level": "Info",
      "category": "EventTrigger_Event",
      "template": "[EventBinder] 执行成功 (控件: '{controlName}', 事件: '{eventName}', 函数: '{functionName}')"
    },
    "EventBinder_ExecuteFailure": {
      "level": "Error",
      "category": "EventTrigger_Event",
      "template": "[EventBinder] 执行失败: {error} (控件: '{controlName}', 事件: '{eventName}', 函数: '{functionName}')"
    },
    "EventBinder_ExecuteException": {
      "level": "Error",
      "category": "EventTrigger_Event",
      "template": "[EventBinder] 执行异常: {error} (控件: '{controlName}', 事件: '{eventName}')"
    },
    "EventBinder_ScriptModeWarning": {
      "level": "Warning",
      "category": "EventTrigger_Event",
      "template": "[EventBinder] 警告: 脚本执行模式下触发运行时事件 '{eventName}' (控件: '{controlName}', 函数: '{functionName}')"
    },
    "EventBinder_ExecutionServiceNull": {
      "level": "Warning",
      "category": "EventTrigger_Event",
      "template": "[EventBinder] 警告: eventExecutionService 为 null，无法执行函数 '{functionName}' (控件: '{controlName}', 事件: '{eventName}')"
    }
  }
}
```

### 3.4 修改文件：`EventBindingTransformers.cs`

#### a) 添加 using 导入

```csharp
using EventTriggerFramework.Diagnostics;
```

#### b) 替换 ScriptModeGuardTransformer 中的日志

| 行号 | 原代码 | 替换为 |
|------|--------|--------|
| 55 | `ctx?.Log("[ScriptModeGuard] 脚本执行模式激活，跳过事件绑定");` | `EventTriggerLog.Log("ScriptModeGuard_Active", new { controlName = input.CurrentControlConfig?.Name ?? "unknown" });` |

#### c) 替换 EventBinderTransformer.Transform() 中的日志

| 行号 | 原代码 | 替换为 |
|------|--------|--------|
| 272 | `ctx?.Log($"[EventBinder] 绑定控件 '{config.Name}' 的事件");` | `EventTriggerLog.Log("EventBinder_BindingControl", new { controlName = config.Name });` |
| 288 | `ctx?.Log($"[EventBinder]   绑定事件 '{eventName}' → '{funcNames[0]}'");` | `EventTriggerLog.Log("EventBinder_BoundEvent", new { eventName, functionName = funcNames[0], controlName = config.Name });` |
| 302 | `ctx?.Log($"[EventBinder]   绑定自定义事件 '{kvp.Key}' → '{kvp.Value}'");` | `EventTriggerLog.Log("EventBinder_BoundCustomEvent", new { eventName = kvp.Key, functionName = kvp.Value, controlName = config.Name });` |
| 309 | `ctx?.Log($"[EventBinder] 绑定控件 '{config.Name}' 失败: {ex.Message}");` | `EventTriggerLog.Log("EventBinder_BindingFailed", new { controlName = config.Name, error = ex.Message });` |

#### d) 替换 CreateEventHandler 中的日志

| 行号 | 原代码 | 替换为 |
|------|--------|--------|
| 426 | `ctx?.Log($"[EventBinder] 警告: 脚本执行模式下触发运行时事件 '{eventName}' → '{functionName}' (控件: '{ctrl.Name}')");` | `EventTriggerLog.Log("EventBinder_ScriptModeWarning", new { eventName, functionName, controlName = ctrl.Name });` |
| 430 | `ctx?.Log($"[EventBinder] 触发事件 '{eventName}' → '{functionName}' (控件: '{ctrl.Name}')");` | `EventTriggerLog.Log("EventBinder_TriggerEvent", new { eventName, functionName, controlName = ctrl.Name });` |
| 442 | `ctx?.Log($"[EventBinder] 警告: eventExecutionService 为 null，无法执行函数 '{functionName}' (控件: '{ctrl.Name}', 事件: '{eventName}')");` | `EventTriggerLog.Log("EventBinder_ExecutionServiceNull", new { functionName, controlName = ctrl.Name, eventName });` |
| 451 | `ctx?.Log($"[EventBinder] 执行失败: {execResult.Error} (控件: '{ctrl.Name}', 事件: '{eventName}', 函数: '{functionName}')");` | `EventTriggerLog.Log("EventBinder_ExecuteFailure", new { error = execResult.Error, controlName = ctrl.Name, eventName, functionName });` |
| 460 | `ctx?.Log($"[EventBinder] 执行成功 (控件: '{ctrl.Name}', 事件: '{eventName}', 函数: '{functionName}')");` | `EventTriggerLog.Log("EventBinder_ExecuteSuccess", new { controlName = ctrl.Name, eventName, functionName });` |
| 468 | `ctx?.Log($"[EventBinder] 执行异常: {ex.Message} (控件: '{ctrlName ?? "unknown"}', 事件: '{eventName}')");` | `EventTriggerLog.Log("EventBinder_ExecuteException", new { error = ex.Message, controlName = ctrlName ?? "unknown", eventName });` |

### 3.5 不需要修改的文件

| 文件 | 原因 |
|------|------|
| `WinFormsEventBinder.cs` | 已使用 `FileDrivenLogPlaceholderHandler.Log`（FileDriven 层） |
| `EventBindingPipeline.cs` | 无日志调用 |
| `EventBindingModels.cs` | 纯模型定义 |
| `ControlTreeWalkerTransformer.cs` | 绑定阶段日志，影响较小 |

---

## 4. 预期效果

### 4.1 绑定阶段日志

```
[DEBUG] [EventTrigger] [EventBinder] 绑定控件 'addButton' 的事件
[DEBUG] [EventTrigger] [EventBinder]   绑定事件 'Click' → 'add'
[DEBUG] [EventTrigger] [EventBinder] 绑定控件 'subtractButton' 的事件
[DEBUG] [EventTrigger] [EventBinder]   绑定事件 'Click' → 'subtract'
```

### 4.2 运行时日志（用户点击按钮时）

```
[DEBUG] [EventTrigger] [EventBinder] 触发事件 'Click' → 'add' (控件: 'addButton')
[INFO]  [EventTrigger] [EventBinder] 执行成功 (控件: 'addButton', 事件: 'Click', 函数: 'add')
```

### 4.3 错误日志

```
[ERROR] [EventTrigger] [EventBinder] 执行失败: 输入值无效 (控件: 'addButton', 事件: 'Click', 函数: 'add')
```

---

## 5. 架构原则

### 5.1 分层日志架构

```
┌─────────────────────────────────────────────────────────────┐
│                    FileDriven (应用层)                        │
│  FileDrivenLogPlaceholderHandler.Log()                      │
│  filedriven_log_messages.json                               │
├─────────────────────────────────────────────────────────────┤
│                    Operation (操作层)                         │
│  ┌─────────────────────┐  ┌─────────────────────────────┐   │
│  │ WdUI                │  │ EventTriggerFramework        │   │
│  │ WdUILog.Log()       │  │ EventTriggerLog.Log()       │   │
│  │ wdui_log_messages   │  │ eventtrigger_log_messages   │   │
│  └─────────┬───────────┘  └──────────────┬──────────────┘   │
├────────────┼──────────────────────────────┼─────────────────┤
│            Share (共享层)                  │                 │
│  PlaceholderLogHelper.Log() ←─────────────┘                 │
│  LogRepository.Write()                                      │
│  LogCategoryResolver.Resolve()                              │
└─────────────────────────────────────────────────────────────┘
```

### 5.2 关键约束

| 约束 | 说明 |
|------|------|
| **Operation 层不反向引用 FileDriven** | 每个 Operation 模块有自己的日志入口和消息定义文件 |
| **共享基础设施在 Share 层** | `PlaceholderLogHelper`、`LogRepository` 等底层设施在 Share 层 |
| **日志消息定义文件在模块根目录** | 如 `wdui_log_messages.json`、`eventtrigger_log_messages.json` |
| **统一日志入口在 Diagnostics 子目录** | 如 `WdUI/Diagnostics/WdUILog.cs`、`EventTriggerFramework/Diagnostics/EventTriggerLog.cs` |

---

## 6. 实施步骤

1. ✅ 创建 `EventTriggerFramework/Diagnostics/EventTriggerLog.cs`
2. ✅ 创建 `EventTriggerFramework/eventtrigger_log_messages.json`
3. ✅ 修改 `EventBindingTransformers.cs`：替换所有 `ctx?.Log(...)` 为 `EventTriggerLog.Log(...)`
4. ✅ 编译验证
5. ✅ 运行测试验证日志输出

---

## 7. 回退方案

如果 `EventTriggerLog.Log()` 返回 false（消息 ID 未找到），自动降级到 `PlaceholderLogHelper.Log` 直接输出，确保日志不会丢失：

```csharp
bool logged = LogPlaceholderHandlerBase.Log(messageId, ModuleName, KeyPrefix, data);
if (!logged)
{
    var dataStr = data != null ? $" {JsonSerializer.Serialize(data)}" : "";
    PlaceholderLogHelper.Log("DEBUG", "EventTrigger_General", ModuleName,
        $"[eventtrigger:{messageId}]{dataStr}");
}
```
