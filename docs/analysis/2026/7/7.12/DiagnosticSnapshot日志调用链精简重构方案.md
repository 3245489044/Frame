# DiagnosticSnapshot 日志调用链精简重构方案

> **问题**：DiagnosticSnapshot 日志中的 snapshot 字段出现三重转义（`\\r\\n`、`\\\\\\u0022`），可读性差。
> **根因**：日志调用链中多次序列化导致字符串被反复转义。
> **方案**：新增字符串直传重载，跳过 `ConvertParameters` + `FormatTemplate` 中间层。

---

## 一、问题分析

### 1.1 当前调用链（7 层）

```
DefaultEventBindingService.BindEvents()
  → EventTriggerLog.Log("DiagnosticSnapshot", new { snapshot = "文本含\r\n和\"" })
    → LogBase.Log("EventTrigger", "eventtrigger:", "DiagnosticSnapshot", data)
      → LogPlaceholderHandlerBase.Log("DiagnosticSnapshot", "EventTrigger", "eventtrigger:", data)
        → StringUtils.ConvertParameters(data)              ← ★ 序列化→反序列化，导致第一次转义
          → JsonSerializer.Serialize(data)                  // \r\n → \\r\\n
          → JsonSerializer.Deserialize<Dictionary>(json)    // 值已转义
        → StringUtils.FormatTemplate(template, paramDict)   // 填充模板
        → LogRepository.Write(message)                      // 输出
      → 降级路径（LogPlaceholderHandlerBase 未初始化时）：
        → JsonSerializer.Serialize(data, LogCompact)        ← ★ 二次序列化，导致第二次转义
          // \\r\\n → \\\\r\\\\n
```

### 1.2 三重转义过程

| 阶段 | snapshot 中的内容 | 说明 |
|------|------------------|------|
| 原始 | `\r\n  pipeline.walk...` | 含换行符和引号 |
| ① ConvertParameters 序列化 | `\\r\\n  pipeline.walk...` | `\r\n` → `\\r\\n` |
| ② 降级路径再次序列化 | `\\\\r\\\\n  pipeline...` | `\\r\\n` → `\\\\r\\\\n` |
| ③ 日志输出格式化 | `\\\\r\\\\n  pipeline...` | 最终显示 |

### 1.3 根因

1. **`StringUtils.ConvertParameters`** 做了 `Serialize → Deserialize` 的多余操作，导致字符串值被转义
2. **`LogBase` 降级路径** 再次对 data 进行 `Serialize`，导致二次转义
3. **`DiagnosticSnapshot` 的 snapshot 是已格式化文本**，但被当作 JSON 字符串值处理

---

## 二、重构方案

### 2.1 核心思路

**新增字符串直传重载，跳过 `ConvertParameters` + `FormatTemplate` 中间层。**

对于 `DiagnosticSnapshot` 这类不需要模板填充的日志，直接传递字符串消息，避免经过序列化/反序列化环节。

### 2.2 改动详情

#### 2.2.1 `LogBase.cs` — 新增字符串重载

**文件**：`CsharpPrj/Operation/BackEndDriven/Diagnostics/LogBase.cs`

```csharp
/// <summary>
/// 输出日志（直接传字符串消息，跳过 ConvertParameters + FormatTemplate）
/// <para>★ 2026-07-12 新增：用于 DiagnosticSnapshot 等不需要模板填充的日志，避免序列化转义。</para>
/// </summary>
internal static void Log(string moduleName, string keyPrefix, string messageId, string message)
{
    bool logged = LogPlaceholderHandlerBase.Log(messageId, moduleName, keyPrefix, message);
    if (!logged)
    {
        PlaceholderLogHelper.Log("DEBUG", messageId, moduleName,
            $"[{keyPrefix}{messageId}] {message}");
    }
}
```

#### 2.2.2 `EventTriggerLog.cs` — 新增字符串重载

**文件**：`CsharpPrj/Operation/BackEndDriven/Diagnostics/EventTriggerLog.cs`

```csharp
/// <summary>
/// 输出日志（直接传字符串消息）
/// <para>★ 2026-07-12 新增：委托给 LogBase.Log(string) 重载。</para>
/// </summary>
public static void Log(string messageId, string message)
{
    LogBase.Log(ModuleName, KeyPrefix, messageId, message);
}
```

#### 2.2.3 `DefaultEventBindingService.cs` — 改用字符串传参

**文件**：`CsharpPrj/Operation/BackEndDriven/EventTriggerFramework/DefaultEventBindingService.cs`

```csharp
// ★ 2026-07-12 重构：改用字符串直传，避免匿名对象序列化导致的转义问题
EventTriggerLog.Log("DiagnosticSnapshot",
    $"targetName={target.GetType().Name}, " +
    $"boundCount={pipelineResult.BoundCount}, " +
    $"skippedCount={pipelineResult.SkippedCount}, " +
    $"snapshotTruncated={diagSnapshot.Length > 500}, " +
    $"snapshotOriginalLength={diagSnapshot.Length}, " +
    $"snapshot={Environment.NewLine}{truncatedSnapshot}");
```

#### 2.2.4 `ControlValueAccessorBase.cs` — 同上

**文件**：`CsharpPrj/Operation/BackEndDriven/EventTriggerFramework/ControlValueAccessorBase.cs`

```csharp
// ★ 2026-07-12 重构：改用字符串直传
EventTriggerLog.Log("DiagnosticSnapshot",
    $"controlName={controlName}, " +
    $"eventType={eventType}, " +
    $"functionName={functionName}");
```

### 2.3 精简后的调用链（4 层）

```
DefaultEventBindingService.BindEvents()
  → EventTriggerLog.Log("DiagnosticSnapshot", "snapshot=...\r\n...")
    → LogBase.Log("EventTrigger", "eventtrigger:", "DiagnosticSnapshot", message)
      → LogPlaceholderHandlerBase.Log("DiagnosticSnapshot", "EventTrigger", "eventtrigger:", message)
        → LogRepository.Write(level, category, module, message)
          → Console.WriteLine(formatted)
      → 降级路径：
        → PlaceholderLogHelper.Log("DEBUG", ...)
          → LogRepository.Write(...)
```

**关键变化**：
- 消除了 `StringUtils.ConvertParameters` 的序列化/反序列化
- 消除了降级路径的二次序列化
- snapshot 中的换行符保持原样，不会被转义

---

## 三、影响范围

### 3.1 修改文件清单

| 文件 | 改动类型 | 风险 |
|------|---------|------|
| `Operation/BackEndDriven/Diagnostics/LogBase.cs` | 新增重载 | 低 |
| `Operation/BackEndDriven/Diagnostics/EventTriggerLog.cs` | 新增重载 | 低 |
| `Operation/BackEndDriven/EventTriggerFramework/DefaultEventBindingService.cs` | 修改调用 | 低 |
| `Operation/BackEndDriven/EventTriggerFramework/ControlValueAccessorBase.cs` | 修改调用 | 低 |

### 3.2 兼容性

- 原有 `Log(object? data)` 重载保持不变，不影响其他调用方
- 新增的 `Log(string message)` 重载是独立路径，不冲突
- `BackEndLog` 等其他日志入口不受影响

---

## 四、验证方法

1. 编译通过：`dotnet build CsharpPrj/MainProj.sln`
2. 运行 UI 启动，观察 DiagnosticSnapshot 日志：
   - 预期：snapshot 中的换行符显示为实际换行，而非 `\\r\\n`
   - 预期：snapshot 中的引号显示为实际引号，而非 `\\u0022`
3. 检查其他使用 `EventTriggerLog.Log` 的地方，确保不受影响

---

## 五、后续优化方向

1. **废弃 `StringUtils.ConvertParameters`**：如果所有调用方都改用字符串直传，可以删除此方法
2. **统一日志序列化策略**：所有日志的结构化数据统一使用 `Plate.JsonMethod.Serialization.JsonSerializerFactory.Contexts.LogCompact`
3. **进一步精简调用链**：考虑让 `EventTriggerLog.Log` 直接调用 `LogRepository.Write`，跳过 `LogPlaceholderHandlerBase`
