# FileDriven 日志体系重构分析

> 编写日期：2026-07-09
> 背景：运行时出现 `[WARN] Log message 'MainForm_OnRootContainerLoaded_controls_found' not found for keyPrefix 'filedriven:'`，暴露日志体系存在定义分散、重复、缺失的问题。

---

## 一、现状问题

### 1.1 三层日志体系混乱

```
Share 层 (CsharpPrj/Share/Diagnostics)
  ├── LogPipelineSkeletons.cs        ← [Obsolete] 旧版管线骨架
  ├── LoggingSkeletonWrapper.cs      ← [Obsolete] 旧版包装器
  └── LogCategoryRegistry.cs         ← 新版分类注册表

FileDriven.Logging 层 (FileDriven/FileDriven.Logging)
  ├── LogPipeline.cs                 ← 新版管线（含 LogPipelineContext + 6个Skeleton）
  ├── Wrappers.cs                    ← 新版包装器（LogEntryBuilder + LoggingSkeletonWrapper）
  ├── Models.cs                      ← 新版数据模型（SkeletonExecutionStats + LifecycleLogInput）
  ├── BatchContextData.cs            ← 批次上下文共享数据
  ├── EmergencyDumpHelper.cs         ← 紧急转储
  ├── Skeletons.cs                   ← FileDriven 业务 Skeleton（FrameworkInit、RouterLog 等）
  ├── FileDrivenLogManager.cs        ← 统一日志管理器
  └── Diagnostics/LoggingLog.cs      ← 模块日志入口

PlaceholderFramework 层 (CsharpPrj/Share/PlaceholderFramework)
  └── Handlers/HandlersService.cs    ← LogPlaceholderHandlerBase（JSON 消息加载 + 模板填充）
```

**问题**：Share 层旧版标记 `[Obsolete]` 但未删除，FileDriven.Logging 新版与 Share 层旧版功能重复。

### 1.2 FileDrivenLogPlaceholderHandler 定义位置不明

- `FileDrivenLogManager.cs` 第 57 行调用 `FileDrivenLogPlaceholderHandler.Initialize(jsonFilePath)`
- `LoggingInitializer.cs` 第 67 行同样调用 `FileDrivenLogPlaceholderHandler.Initialize(projectPath)`
- 但 `FileDrivenLogPlaceholderHandler` 类定义**不存在于任何独立文件中**
- `HandlersService.cs` 注释说已整合 11 个文件（含 `FileDrivenLogPlaceholderHandler.cs`），但整合后的类名为 `LogPlaceholderHandlerBase`，不是 `FileDrivenLogPlaceholderHandler`
- 推测：`FileDrivenLogPlaceholderHandler` 原本是一个继承 `LogPlaceholderHandlerBase` 的子类，整合时被删除，但调用方未更新

### 1.3 msgId 缺失（触发 WARN 的直接原因）

```
[WARN] Log message 'MainForm_OnRootContainerLoaded_controls_found' not found for keyPrefix 'filedriven:'
```

- `filedriven_log_messages.json` 中**没有定义** `MainForm_OnRootContainerLoaded_controls_found`
- 调用方（`MainForm.cs`）使用了 `FileDrivenLogManager.Log("MainForm_OnRootContainerLoaded_controls_found", ...)`
- `LogPlaceholderHandlerBase.Log()` 在 JSON 中找不到 msgId 时输出 WARN 降级日志

### 1.4 各模块独立日志系统膨胀

每个 FileDriven 子模块都有独立的 `XxxLog.cs` + `xxx_log_messages.json`：

| 模块 | Log 类 | JSON 文件 | KeyPrefix |
|------|--------|-----------|-----------|
| FileDriven.Execution | `ExecutionLog` | `execution_log_messages.json` | `exec:` |
| FileDriven.Abstractions | `AbstractionsLog` | `abstractions_log_messages.json` | `abst:` |
| FileDriven.Business | `BusinessLog` | `business_log_messages.json` | `busi:` |
| FileDriven.JsonRefer | `JsonReferLog` | `jsonrefer_log_messages.json` | `json:` |
| FileDriven.Logging | `LoggingLog` | `logging_log_messages.json` | `logg:` |
| FileDriven.UI | `UILog` | `ui_log_messages.json` | `ui:` |
| FileDriven.Utilities | `UtilitiesLog` | `utilities_log_messages.json` | `util:` |

每个 `XxxLog.cs` 都是对 `LogPlaceholderHandlerBase.Log()` 的简单封装，模式完全一致。

---

## 二、目标架构

### 2.1 分层职责

```
┌─────────────────────────────────────────────────────────┐
│  Share 层 (Common.Diagnostics)                          │
│  通用日志基础设施：LogPipeline、LoggingSkeletonWrapper、 │
│  SkeletonExecutionStats、EmergencyDumpHelper            │
│  不依赖 FileDriven 特有类型                             │
├─────────────────────────────────────────────────────────┤
│  FileDriven 层 (FileDriven.Logging → FileDriven.Execution) │
│  FileDriven 特有逻辑：FileDrivenLogManager、            │
│  FileDriven 业务 Skeleton（Skeletons.cs）               │
├─────────────────────────────────────────────────────────┤
│  各子模块 (FileDriven.XXX/Diagnostics)                  │
│  XxxLog.cs + xxx_log_messages.json                      │
│  保持现有模式不变                                       │
└─────────────────────────────────────────────────────────┘
```

### 2.2 具体迁移方案

#### 2.2.1 提升到 Share 层（替换旧版）

| 文件 | 当前位置 | 目标位置 | 说明 |
|------|---------|---------|------|
| `LogPipeline.cs` | `FileDriven.Logging.Pipeline` | `Common.Diagnostics` | 替换 `LogPipelineSkeletons.cs` |
| `Wrappers.cs` | `FileDriven.Logging.Wrappers` | `Common.Diagnostics` | 替换 `LoggingSkeletonWrapper.cs` |
| `Models.cs` | `FileDriven.Logging.Models` | `Common.Diagnostics` | 替换旧版重复定义 |
| `BatchContextData.cs` | `FileDriven.Logging` | `Common.Diagnostics` | 通用数据模型 |
| `EmergencyDumpHelper.cs` | `FileDriven.Logging.Emergency` | `Common.Diagnostics` | 通用紧急转储 |

**注意**：提升时需要：
- 将命名空间从 `FileDriven.Logging.*` 改为 `Common.Diagnostics`
- 移除对 FileDriven 特有类型的依赖（如 `FileDrivenLogManager`、`LoggingLog`）
- 将 `PlaceholderLogHelper.Log()` 调用改为 `LogRepository.Write()` 直接写入

#### 2.2.2 迁移到 FileDriven.Execution

| 文件 | 当前位置 | 目标位置 | 说明 |
|------|---------|---------|------|
| `FileDrivenLogManager.cs` | `FileDriven.Logging` | `FileDriven.Execution` | 日志管理器，依赖 Execution 特有类型 |
| `Skeletons.cs` | `FileDriven.Logging.Skeletons` | `FileDriven.Execution` | 业务 Skeleton，依赖 Execution 特有类型 |
| `Diagnostics/LoggingLog.cs` | `FileDriven.Logging.Diagnostics` | `FileDriven.Execution.Diagnostics` | 模块日志入口 |

#### 2.2.3 修复 FileDrivenLogPlaceholderHandler 引用

将 `FileDrivenLogManager.cs` 和 `LoggingInitializer.cs` 中的：
```csharp
FileDrivenLogPlaceholderHandler.Initialize(jsonFilePath);
```
改为：
```csharp
LogPlaceholderHandlerBase.Initialize(jsonFilePath, "filedriven:");
```

#### 2.2.4 删除旧版

删除 `CsharpPrj/Share/Diagnostics/` 中的：
- `LogPipelineSkeletons.cs`（被新版 LogPipeline.cs 替代）
- `LoggingSkeletonWrapper.cs`（被新版 Wrappers.cs 替代）

---

## 三、WARN 日志修复（紧急）

### 3.1 根因

`MainForm.cs` 中调用：
```csharp
FileDrivenLogManager.Log("MainForm_OnRootContainerLoaded_controls_found", new { ... });
```

但 `filedriven_log_messages.json` 中没有定义 `MainForm_OnRootContainerLoaded_controls_found`。

### 3.2 修复方案

在 `filedriven_log_messages.json` 中添加：

```json
"MainForm_OnRootContainerLoaded_controls_found": {
    "level": "Info",
    "category": "FileDriven_General",
    "template": "[MainForm] RootContainer 加载完成: controlCount={controlCount}, containerName={containerName}"
}
```

### 3.3 预防措施

- 所有 `FileDrivenLogManager.Log(msgId, ...)` 调用必须先在 `filedriven_log_messages.json` 中定义 msgId
- 新增 msgId 时遵循命名规范：`{ClassName}_{MethodName}_{description}`
- 运行时 WARN 日志表示 msgId 缺失，应视为 bug 修复

---

## 四、各模块 XxxLog 模式评估

### 4.1 现状

每个子模块的 `XxxLog.cs` 都是 45 行的模板代码：
```csharp
public static class XxxLog
{
    private const string ModuleName = "FileDriven.XXX";
    private const string KeyPrefix = "xxx:";

    public static void Initialize(string jsonFilePath)
        => LogPlaceholderHandlerBase.Initialize(jsonFilePath, KeyPrefix);

    public static bool Log(string msgId, object? parameters = null)
        => LogPlaceholderHandlerBase.Log(msgId, ModuleName, KeyPrefix, parameters);
}
```

### 4.2 建议

**保持现状，不做统一**。原因：
1. 每个模块的 JSON 文件独立，便于按模块管理
2. KeyPrefix 隔离避免 msgId 冲突
3. 模板代码已足够精简（45 行），统一带来的收益有限
4. 各模块初始化时机不同，独立控制更灵活

---

## 五、执行计划

### Phase 1：紧急修复（5 分钟）
- [ ] 在 `filedriven_log_messages.json` 中添加缺失的 msgId

### Phase 2：修复 FileDrivenLogPlaceholderHandler 引用（10 分钟）
- [ ] 将 `FileDrivenLogManager.cs` 中的调用改为 `LogPlaceholderHandlerBase.Initialize()`
- [ ] 将 `LoggingInitializer.cs` 中的调用改为 `LogPlaceholderHandlerBase.Initialize()`

### Phase 3：通用逻辑提升到 Share 层（30 分钟）
- [ ] 将 `LogPipeline.cs` 提升到 `Common.Diagnostics`，替换旧版
- [ ] 将 `Wrappers.cs` 提升到 `Common.Diagnostics`，替换旧版
- [ ] 将 `Models.cs` 提升到 `Common.Diagnostics`
- [ ] 将 `BatchContextData.cs` 提升到 `Common.Diagnostics`
- [ ] 将 `EmergencyDumpHelper.cs` 提升到 `Common.Diagnostics`
- [ ] 删除旧版 `LogPipelineSkeletons.cs` 和 `LoggingSkeletonWrapper.cs`

### Phase 4：FileDriven 特有逻辑迁移（15 分钟）
- [ ] 将 `FileDrivenLogManager.cs` 迁移到 `FileDriven.Execution`
- [ ] 将 `Skeletons.cs` 迁移到 `FileDriven.Execution`
- [ ] 将 `LoggingLog.cs` 迁移到 `FileDriven.Execution.Diagnostics`

### Phase 5：验证（10 分钟）
- [ ] 编译通过
- [ ] 运行时无 WARN 日志
- [ ] 日志管线正常执行
