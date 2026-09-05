# DiagnosticSnapshot 日志可读性修复方案

> 基于 Plate 层 `JsonSerializerFactory` 的统一修复方案

## 一、问题根因

### 1.1 双重序列化架构缺陷

```
Record(data) → EventEntry(JsonOptions.Compact) → DataJson(已转义字符串)
  → TextFormatter → snapshot(含转义序列的字符串)
    → EventTriggerLog.Log("DiagnosticSnapshot", new { snapshot })
      → StringUtils.ConvertParameters → 再次序列化 → 双重转义
```

### 1.2 JSON 序列化选项碎片化

整个框架中存在 **4 个独立的 JSON 选项定义点**，各自为政：

| 位置 | 选项名 | 编码器 | WriteIndented | 问题 |
|------|--------|--------|---------------|------|
| `Modules/DiagnosticContext/Formatting/JsonOptions.cs:11` | `Compact` | **默认编码器** ❌ | false | 换行符→`\\n`，中文→`\\uXXXX` |
| `Modules/DiagnosticContext/Formatting/JsonOptions.cs:21` | `CompactRelaxed` | `UnsafeRelaxedJsonEscaping` ✅ | false | 正确但未被使用 |
| `Modules/Parsing/Parsing/StringUtils.cs:137` | 内联匿名 | `UnsafeRelaxedJsonEscaping` ✅ | false | 正确，但独立于 JsonOptions |
| `Operation/BackEndDriven/Diagnostics/LogBase.cs:54` | `LogExport` | `BasicLatin` ✅ | true | 正确，但缩进格式 |
| `Share/EventBindingOrchestrator/Contexts/EventBindingDiagnosticContext.cs:165` | 内联匿名 | **默认编码器** ❌ | false | 降级路径未修复 |

### 1.3 项目依赖约束

| 项目 | 能否引用 Plate.JsonMethod | 原因 |
|------|--------------------------|------|
| `Modules/DiagnosticContext` | ❌ | 只引用 BasicMethod |
| `Modules/Parsing` | ❌ | 只引用 BasicMethod |
| `Share/EventBindingOrchestrator` | ❌ | 未引用 Plate |
| `Operation/BackEndDriven` | ✅ | 已引用 Plate.JsonMethod |

## 二、修复方案

### 2.1 Plate 层增强：新增 `LogCompact` 上下文

**文件：** `CsharpPrj/Plate/JsonMethod/Serialization/JsonSerializerFactory.cs`

在 `JsonContext` 枚举和 `Contexts` 静态类中新增 `LogCompact`：

```csharp
// JsonContext 枚举新增
/// <summary>日志紧凑导出（无缩进 + BasicLatin 编码器）</summary>
LogCompact
```

```csharp
// Contexts 静态类新增
/// <summary>
/// 日志紧凑导出（无缩进 + BasicLatin 编码器）
/// <para>用于 EventEntry 内部序列化，避免换行符和中文被转义。</para>
/// </summary>
public static JsonSerializerOptions LogCompact { get; } = CreateLogCompactOptions();
```

```csharp
// 新增工厂方法
/// <summary>
/// 创建 LogCompact 专用选项
/// <para>无缩进 + BasicLatin 编码器，用于 EventEntry 内部存储。</para>
/// </summary>
private static JsonSerializerOptions CreateLogCompactOptions()
{
    var options = CreateBaseOptions();
    options.WriteIndented = false;
    // BasicLatin 编码器允许换行符原样输出
    options.Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin);
    return options;
}
```

### 2.2 Modules 层：删除冗余 `JsonOptions.cs`，改用 Plate 工厂

**文件：** `CsharpPrj/Modules/DiagnosticContext/Formatting/JsonOptions.cs`

由于 `Modules/DiagnosticContext` 不能直接引用 Plate，需要将 `JsonOptions.cs` 精简为仅保留 Plate 工厂的**委托引用**。但考虑到项目依赖约束，更实际的方案是：

**方案：** 删除 `Compact` 选项（有缺陷），只保留 `CompactRelaxed` 作为 Modules 层的本地选项。同时添加注释说明应迁移到 Plate 层。

```csharp
internal static class JsonOptions
{
    /// <summary>
    /// 紧凑格式 + 宽松转义（日志存储标准）
    /// <para>★ 2026-07-12 修复：删除 Compact（无 Encoder），统一使用 CompactRelaxed</para>
    /// <para>TODO: 后续迁移到 Plate.JsonMethod.Serialization.JsonSerializerFactory.Contexts.LogCompact</para>
    /// </summary>
    public static readonly JsonSerializerOptions CompactRelaxed = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>缩进格式 + 宽松转义（日志展示标准）</summary>
    public static readonly JsonSerializerOptions UnsafeRelaxed = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>缩进格式 + 宽松转义 + 自定义转换器</summary>
    public static JsonSerializerOptions UnsafeRelaxedWithConverter<TConverter>(TConverter converter) where TConverter : JsonConverter
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        options.Converters.Add(converter);
        return options;
    }
}
```

### 2.3 EventEntry：使用 `CompactRelaxed` 替代 `Compact`

**文件：** `CsharpPrj/Modules/DiagnosticContext/Core/EventEntry.cs:45`

```csharp
// 修改前：
DataJson = data != null ? JsonSerializer.Serialize(data, JsonOptions.Compact) : null;

// 修改后：
DataJson = data != null ? JsonSerializer.Serialize(data, JsonOptions.CompactRelaxed) : null;
```

### 2.4 EventBindingDiagnosticContext：修复降级路径

**文件：** `CsharpPrj/Share/EventBindingOrchestrator/Contexts/EventBindingDiagnosticContext.cs:164-165`

```csharp
// 修改前：
var dataJson = data != null
    ? JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false })
    : "";

// 修改后：
var dataJson = data != null
    ? JsonSerializer.Serialize(data, Modules.DiagnosticContext.Formatting.JsonOptions.CompactRelaxed)
    : "";
```

### 2.5 StringUtils：添加注释说明应迁移到 Plate

**文件：** `CsharpPrj/Modules/Parsing/Parsing/StringUtils.cs:134-140`

```csharp
// 当前代码（已使用 UnsafeRelaxedJsonEscaping，正确）：
// ★ 2026-07-12 修复：使用 UnsafeRelaxedJsonEscaping 编码器
// TODO: 后续迁移到 Plate.JsonMethod.Serialization.JsonSerializerFactory.Contexts.LogCompact
var json = System.Text.Json.JsonSerializer.Serialize(parameters,
    new System.Text.Json.JsonSerializerOptions
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    });
```

### 2.6 LogBase：降级路径使用 `LogCompact` 替代 `LogExport`

**文件：** `CsharpPrj/Operation/BackEndDriven/Diagnostics/LogBase.cs:54`

```csharp
// 修改前（LogExport 带缩进，日志行内显示不美观）：
var dataStr = data != null ? $" {JsonSerializer.Serialize(data, Plate.JsonMethod.Serialization.JsonSerializerFactory.Contexts.LogExport)}" : "";

// 修改后（LogCompact 无缩进，适合行内显示）：
var dataStr = data != null ? $" {JsonSerializer.Serialize(data, Plate.JsonMethod.Serialization.JsonSerializerFactory.Contexts.LogCompact)}" : "";
```

## 三、修复清单

| # | 文件 | 修改内容 | 优先级 |
|---|------|---------|--------|
| 1 | `Plate/JsonMethod/Serialization/JsonSerializerFactory.cs` | 新增 `LogCompact` 上下文 | **高** |
| 2 | `Modules/DiagnosticContext/Formatting/JsonOptions.cs` | 删除 `Compact`，保留 `CompactRelaxed` | **高** |
| 3 | `Modules/DiagnosticContext/Core/EventEntry.cs` | `Compact` → `CompactRelaxed` | **高** |
| 4 | `Share/EventBindingOrchestrator/Contexts/EventBindingDiagnosticContext.cs` | 降级路径使用 `CompactRelaxed` | **中** |
| 5 | `Modules/Parsing/Parsing/StringUtils.cs` | 添加 TODO 注释 | **低** |
| 6 | `Operation/BackEndDriven/Diagnostics/LogBase.cs` | `LogExport` → `LogCompact` | **中** |

## 四、修复后数据流

```
Record(data) → EventEntry(CompactRelaxed / UnsafeRelaxedJsonEscaping)
  → DataJson(换行符和中文原样保留)
    → TextFormatter → snapshot(可读文本)
      → EventTriggerLog.Log("DiagnosticSnapshot", new { snapshot })
        → StringUtils.ConvertParameters(UnsafeRelaxedJsonEscaping)
          → 单层转义，snapshot 中的换行符保持原样 ✅
```
