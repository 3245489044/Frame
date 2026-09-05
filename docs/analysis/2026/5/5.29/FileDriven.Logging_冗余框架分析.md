# FileDriven.Logging 冗余框架分析

> 分析日期：2026-05-27
> 分析目标：FileDriven.Logging 与 Share.PlaceholderFramework 之间的功能重叠

---

## 1. 背景

项目中存在两套并行的日志框架：

| 框架 | 位置 | 核心机制 |
|------|------|---------|
| **旧框架** | `FileDriven/FileDriven.Logging/` | `LogEntryBuilder` + `LogEntryCollector` + `LogCollectSkeleton` |
| **新框架** | `Share/PlaceholderFramework/` | `PlaceholderLogHelper` + `LogMessageLoader` + `FileDrivenLogPlaceholderHandler` |

两套框架都向 `LogRepository` 写入日志，但采用了完全不同的实现路径。

---

## 2. 架构对比

### 2.1 旧框架（FileDriven.Logging）

```
Skeletons.cs 中的硬编码日志调用
    │
    ▼
LogEntryBuilder.Info/Warn/Error(ctx, LogCategory.xxx, "Module", "message")
    │
    ▼
LogEntryCollector.AddLog(ctx, new LogEntry { ... })
    │  将 LogEntry 暂存到 SkeletonContext
    ▼
LogCollectSkeleton.ExecuteCore()
    │  从 SkeletonContext 取出所有 LogEntry
    ▼
LogRepository.Write(entry)
```

**特点：**
- 日志消息**硬编码**在 C# 代码中
- 使用旧的 `LogCategory.Lifecycle/Command/Config` 枚举
- 采用"先收集后写入"模式（`LogEntryCollector` → `LogCollectSkeleton`）
- 需要 `SkeletonContext` 作为上下文

### 2.2 新框架（PlaceholderFramework）

```
filedriven_log_messages.json（消息定义）
    │
    ▼
LogMessageLoader.Load()  →  Dictionary<string, LogMessageDef>
    │
    ▼
FileDrivenLogPlaceholderHandler.Log("msgId", new { ... })
    │
    ▼
PlaceholderTemplateFormatter.Format(template, params)
    │
    ▼
PlaceholderLogHelper.Log(level, category, module, message)
    │
    ▼
LogCategoryResolver.Resolve(category)  →  LogCategory 枚举
    │
    ▼
LogRepository.Write(logEntry)
```

**特点：**
- 日志消息定义在 `filedriven_log_messages.json` 中集中管理
- 使用新的 `LogCategoryEx` 枚举（通过 `LogCategoryResolver` 解析）
- 直接写入 `LogRepository`，无需中间收集步骤
- 支持静态调用（无需 `SkeletonContext`）

---

## 3. 功能重叠分析

### 3.1 LogEntryBuilder vs PlaceholderLogHelper

| 功能 | LogEntryBuilder | PlaceholderLogHelper |
|------|----------------|---------------------|
| 创建 LogEntry | ✅ 直接 new LogEntry | ✅ 直接 new LogEntry |
| 日志级别 | Info/Warn/Error/Debug | LogInfo/LogWarn/LogError/LogDebug |
| 分类支持 | LogCategory 枚举 | 字符串分类（自动解析为枚举） |
| 输出目标 | LogEntryCollector（暂存） | LogRepository（直接写入） |
| 是否需要 SkeletonContext | ✅ 是 | ❌ 否 |

**结论：** `LogEntryBuilder` 的功能完全被 `PlaceholderLogHelper` 覆盖，且 `PlaceholderLogHelper` 更灵活（支持字符串分类、无需 SkeletonContext）。

### 3.2 LogEntryCollector + LogCollectSkeleton vs 直接写入

旧框架的"先收集后写入"模式增加了不必要的复杂性：

```
LogEntryCollector.AddLog()  →  SkeletonContext 暂存  →  LogCollectSkeleton 统一写入
```

而新框架直接：
```
PlaceholderLogHelper.Log()  →  LogRepository.Write()
```

**结论：** "先收集后写入"模式在 Skeleton 管线中有其历史原因（延迟写入），但 `PlaceholderLogHelper` 直接写入 `LogRepository` 的方式更简洁，且 `LogRepository` 本身是线程安全的。

### 3.3 硬编码消息 vs JSON 配置消息

旧框架在 `Skeletons.cs` 中硬编码了约 **50+ 条日志消息**，例如：

```csharp
LogEntryBuilder.Info(ctx, LogCategory.Config, "ConfigLoad", $"Loading configuration: {resolvedPath}");
LogEntryBuilder.Info(ctx, LogCategory.Command, "CommandSet", $"Setting value for: {input.Target}");
LogEntryBuilder.Error(ctx, LogCategory.Command, "CommandClick", $"Click failed for {input.Target}: ...");
```

而 `filedriven_log_messages.json` 已经定义了所有这些消息的模板：

```json
"ConfigLoadSkeleton_ExecuteCore_loading": {
    "level": "Info",
    "category": "FileDriven_Command",
    "template": "Loading config: {configPath}"
}
```

**结论：** 硬编码消息与 JSON 配置消息之间存在**双重维护**问题。修改日志格式需要同时修改代码和 JSON 文件。

### 3.4 LogCategory 旧枚举 vs LogCategoryEx 新枚举

| 旧枚举值 | 新枚举值 |
|---------|---------|
| `LogCategory.Lifecycle` | `LogCategoryEx.Launcher_Lifecycle` |
| `LogCategory.Command` | `LogCategoryEx.FileDriven_Command` |
| `LogCategory.Config` | `LogCategoryEx.FileDriven_General` |

旧枚举 `LogCategory` 是 `Common.Diagnostics` 中的通用枚举，而 `LogCategoryEx` 是扩展枚举，提供了更细粒度的分类。

---

## 4. 冗余总结

| 组件 | 状态 | 说明 |
|------|------|------|
| `LogEntryBuilder` | **冗余** | 功能被 `PlaceholderLogHelper` 完全覆盖 |
| `LogEntryCollector` | **冗余** | "先收集后写入"模式不再需要 |
| `LogCollectSkeleton` | **冗余** | 收集逻辑不再需要 |
| `LoggingSkeletonWrapper` | **可保留** | 自动记录 Skeleton 开始/结束日志的功能仍有价值 |
| `Skeletons.cs` 中的硬编码日志 | **需重构** | 应改为通过 `FileDrivenLogPlaceholderHandler.Log()` 输出 |
| `LogCategory.Lifecycle/Command/Config` | **需移除** | 应统一使用 `LogCategoryEx` 枚举 |

---

## 5. 重构方案

### 5.1 目标

消除 `FileDriven.Logging` 中的冗余日志框架，所有日志统一走 `PlaceholderFramework` 的 JSON 配置路径。

### 5.2 具体步骤

#### 步骤 1：改造 LogEntryBuilder

`LogEntryBuilder` 保留作为便捷 API，但内部改为调用 `FileDrivenLogPlaceholderHandler.Log()`：

```csharp
public static class LogEntryBuilder
{
    public static void Info(SkeletonContext ctx, string module, string message)
    {
        // 不再通过 LogEntryCollector 暂存，直接写入 LogRepository
        PlaceholderLogHelper.LogInfo("FileDriven_General", module, message);
    }
    
    // 其他方法类似...
}
```

#### 步骤 2：移除 LogEntryCollector 和 LogCollectSkeleton

- `LogEntryCollector` — 完全移除
- `LogCollectSkeleton` — 完全移除
- `LogPipeline.CreateWithLogCollect()` — 简化为直接管线

#### 步骤 3：重构 Skeletons.cs

将所有硬编码日志调用改为通过 `FileDrivenLogPlaceholderHandler.Log()`：

```csharp
// 旧代码
LogEntryBuilder.Info(ctx, LogCategory.Config, "ConfigLoad", 
    $"Loading configuration: {resolvedPath}");

// 新代码
FileDrivenLogPlaceholderHandler.Log("ConfigLoadSkeleton_ExecuteCore_loading", 
    new { configPath = resolvedPath });
```

#### 步骤 4：移除 LogCategory 旧枚举引用

所有 `LogCategory.Lifecycle` → 通过 `LogCategoryResolver.Resolve("Launcher_Lifecycle")` 解析
所有 `LogCategory.Command` → 通过 `LogCategoryResolver.Resolve("FileDriven_Command")` 解析
所有 `LogCategory.Config` → 通过 `LogCategoryResolver.Resolve("FileDriven_General")` 解析

### 5.3 保留的组件

- **`LoggingSkeletonWrapper`** — 自动记录 Skeleton 开始/结束日志的功能仍有价值
- **`LogPipeline`** — 管线编排逻辑不变，只是内部日志输出方式改变
- **`EmergencyDumpHelper`** — 紧急转储功能独立于日志框架

---

## 6. 影响范围

| 文件 | 影响 |
|------|------|
| `FileDriven.Logging/Wrappers.cs` | `LogEntryBuilder` 需改造，`LogEntryCollector` 可移除 |
| `FileDriven.Logging/Skeletons.cs` | 约 50+ 处硬编码日志需替换 |
| `FileDriven.Logging/LogPipeline.cs` | `LogCollectSkeleton` 相关逻辑需调整 |
| `FileDriven.Logging/Models.cs` | 可能移除 `LogMessageDef`（已由 PlaceholderFramework 提供） |
| `Share/PlaceholderFramework/Core/LogMessageLoader.cs` | 无需修改 |
| `Share/PlaceholderFramework/Core/PlaceholderLogHelper.cs` | 无需修改 |
| `Share/PlaceholderFramework/Core/LogCategoryResolver.cs` | 无需修改 |
| `Share/PlaceholderFramework/Handlers/FileDrivenLogPlaceholderHandler.cs` | 无需修改 |
| `filedriven_log_messages.json` | 无需修改（消息定义已完整） |

---

## 7. 总结

`FileDriven.Logging` 中的 `LogEntryBuilder` + `LogEntryCollector` + `LogCollectSkeleton` 机制与 `Share.PlaceholderFramework` 中的 `PlaceholderLogHelper` + `LogMessageLoader` + `FileDrivenLogPlaceholderHandler` 机制存在**严重的功能重叠**。

旧框架的"先收集后写入"模式、硬编码日志消息、旧枚举分类，都已被新框架的 JSON 配置驱动、直接写入、自动分类解析的模式所覆盖。

建议将 `FileDriven.Logging` 中的日志输出统一迁移到 `PlaceholderFramework` 体系，消除冗余，降低维护成本。
