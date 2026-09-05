# DiagnosticSnapshot 日志格式修复方案

## 问题描述

`DiagnosticSnapshot` 的日志输出可读性差，因为 `snapshot` 字段的值是 `TextFormatter` 输出的纯文本（包含换行符），被 `JsonSerializer.Serialize` 序列化时，换行符被转义为 `\r\n`，双引号被转义为 `\u0022`。

### 当前输出（可读性差）

```json
[eventtrigger:DiagnosticSnapshot] {
  "snapshot": "\r\n...trolName\\u0022:\\u0022styleLabel\\u0022,..."
}
```

### 期望输出

```json
[eventtrigger:DiagnosticSnapshot] {
  "snapshot": "EventBindingOrchestrator\n  eventbinder.scope.began  { ... }\n  eventbinder.function.success  { ... }"
}
```

## 根因分析

### 序列化链路

```
DefaultEventBindingService.cs
  → EventTriggerLog.Log("DiagnosticSnapshot", data)
    → LogBase.Log()
      → LogPlaceholderHandlerBase.Log()    // 查找 filedriven_log_messages.json 模板
        → 降级路径: JsonSerializer.Serialize(data, JsonSerializerFactory.Contexts.LogExport)
```

当 `DiagnosticSnapshot` 消息 ID 在 `filedriven_log_messages.json` 中**未定义**时，`LogPlaceholderHandlerBase.Log` 返回 `false`，走降级路径：

```csharp
// LogBase.cs 第 50 行
var dataStr = data != null
    ? $" {JsonSerializer.Serialize(data, JsonSerializerFactory.Contexts.LogExport)}"
    : "";
```

`JsonSerializerFactory.Contexts.LogExport` 当前使用 `_prettyOptions`（美化输出），其 `Encoder` 虽然已设置为 `UnsafeRelaxedJsonEscaping`（解决 `\u0022` 问题），但换行符（`\n`、`\r`）仍然会被转义。

### 编码器行为对比

| 编码器 | 双引号 `"` | 换行符 `\n` | `<` `>` `&` |
|--------|-----------|------------|------------|
| 默认 `JavaScriptEncoder.Default` | `\u0022` | `\n` 保留 | `\u003C` `\u003E` `\u0026` |
| `UnsafeRelaxedJsonEscaping` | 保留 `"` | `\n` 保留 | 保留 |
| `JavaScriptEncoder.Create(UnicodeRanges.BasicLatin)` | 保留 `"` | **保留 `\n`** | 保留 |

`UnsafeRelaxedJsonEscaping` 实际上**已经保留了换行符**，但 `snapshot` 字符串中的 `\r\n` 是字面换行符，在 JSON 序列化时会被转义为 `\\r\\n`。

## 修复方案

### 方案：为 `LogExport` 单独创建编码器

在 `JsonSerializerFactory` 中为 `LogExport` 创建独立的 `JsonSerializerOptions` 实例，使用 `JavaScriptEncoder.Create(UnicodeRanges.BasicLatin)` 允许换行符不被转义。

### 修改文件

**仅修改：** `CsharpPrj/BasicMethod/JsonProcess/Serialization/JsonSerializerFactory.cs`

### 修改内容

```csharp
// 新增：LogExport 专用编码器，允许换行符
private static readonly JavaScriptEncoder _logExportEncoder =
    JavaScriptEncoder.Create(UnicodeRanges.BasicLatin);

// 修改：LogExport 不再共享 _prettyOptions，使用独立实例
public static JsonSerializerOptions LogExport { get; } = CreateLogExportOptions();

private static JsonSerializerOptions CreateLogExportOptions()
{
    var options = CreateBaseOptions();
    options.WriteIndented = true;
    options.Encoder = _logExportEncoder;
    return options;
}
```

### 影响范围

所有通过 `JsonSerializerFactory.Contexts.LogExport` 序列化的日志输出都会受益：

| 文件 | 行号 | 用途 |
|------|------|------|
| `CsharpPrj/Operation/BackEndDriven/Diagnostics/LogBase.cs` | 第 50 行 | `LogBase.Log` 降级路径 |
| `CsharpPrj/Share/EventBindingOrchestrator/Contexts/EventBindingDiagnosticContext.cs` | 第 164 行 | `EventBindingDiagnosticContext.Record` 降级路径 |

其他上下文（`UIConfig`、`Compact`、`Default` 等）保持不变，不受影响。

### 不修改的文件

| 文件 | 原因 |
|------|------|
| `CsharpPrj/Share/PlaceholderFramework/Core/JsonService.cs` | 旧版副本，引用 `GetOptions` 方法，不直接使用 `LogExport` |
| `CsharpPrj/Share/UniversalMiddleware/Serialization/JsonSerializerFactory.cs` | 旧版副本，只使用 `DefaultOptions` |
| `CsharpPrj/Share/UniversalMiddleware/Serialization.cs` | 使用 `DefaultOptions` |
| `CsharpPrj/Share/UniversalMiddleware/Infrastructure.cs` | 使用 `DefaultOptions` |
| `CsharpPrj/BasicMethod/JsonProcess/Serialization/JsonSerializationService.cs` | 使用 `GetOptions(context)` 通用方法 |

## 验证方法

1. 构建：`dotnet build CsharpPrj/MainProj.sln`
2. 运行时观察 `DiagnosticSnapshot` 日志输出，确认换行符不再被转义
