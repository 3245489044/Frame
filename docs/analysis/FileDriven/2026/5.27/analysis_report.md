# FileDriven.Logging 综合分析报告

## 一、合并操作总结

已将 18 个分散的源文件合并为 4 个核心文件：

| 合并前文件数 | 合并后文件 | 包含内容 |
|---|---|---|
| 18 个 | `Skeletons.cs` | 所有 Skeleton 定义（A/B/C/D 四类） |
| 6 个 | `LogPipeline.cs` | 日志管线上下文、异常、各步骤 Skeleton、管线构建器 |
| 2 个 | `Wrappers.cs` | LogEntryCollector、LoggingSkeletonWrapper、LoggingSkeletonWrapper |
| 1 个 | `Models.cs` | LogMessageDef、SkeletonExecutionStats、LifecycleLogInput |

### 合并的 18 个源文件

1. `FileDrivenPipeline.cs`
2. `FrameworkInitAdapterSkeleton.cs`
3. `CommandLoopSkeleton.cs`
4. `CliModeRouterSkeleton.cs`
5. `ConfigLoadSkeleton.cs`
6. `CommandSetSkeleton.cs`
7. `CommandClickSkeleton.cs`
8. `CommandGetSkeleton.cs`
9. `EventTriggerSkeleton.cs`
10. `FrameworkInitSkeleton.cs`
11. `RouterAdapterLogSkeleton.cs`
12. `RouterNormalizeLogSkeleton.cs`
13. `RouterLogCommandLogSkeleton.cs`
14. `ExecutionReportLogSkeleton.cs`
15. `ScriptConverterLogSkeleton.cs`
16. `ValueExtractionLogSkeleton.cs`
17. `LogInjectionSkeletons.cs`
18. `FileDrivenLoggingSkeletons.cs`

### 合并的 6 个管线文件

1. `LogPipeline.cs`
2. `LogPipelineContext.cs`
3. `LogPipelineException.cs`
4. `LogInitSkeleton.cs`
5. `LogCollectSkeleton.cs`
6. `LogFlushSkeleton.cs`
7. `LogEmergencyDumpSkeleton.cs`
8. `LogSummarySkeleton.cs`
9. `LogTryExecuteSkeleton.cs`

---

## 二、文件分类整理方案

### 建议目录结构

```
FileDriven.Logging/
├── FileDriven.Logging.csproj
├── FileDrivenLogManager.cs          # 日志管理器（入口）
│
├── Models/                          # 数据模型
│   ├── LogMessageDef.cs
│   ├── SkeletonExecutionStats.cs
│   └── LifecycleLogInput.cs
│
├── Core/                            # 核心工具
│   ├── LogMessageLoader.cs
│   ├── PlaceholderTemplateFormatter.cs
│   ├── LogCategoryResolver.cs
│   └── PlaceholderLogHelper.cs
│
├── Pipeline/                        # 日志管线
│   ├── LogPipeline.cs               # 管线构建器 + 上下文 + 异常 + 各步骤 Skeleton
│   └── LogPipelineContext.cs        # （可选拆分）
│
├── Skeletons/                       # 业务 Skeleton
│   ├── Orchestration/               # A. 业务编排
│   │   ├── CliModeRouterSkeleton.cs
│   │   ├── CommandLoopSkeleton.cs
│   │   ├── FrameworkInitAdapterSkeleton.cs
│   │   └── FileDrivenPipeline.cs
│   ├── Commands/                    # B. 具体命令
│   │   ├── FrameworkInitSkeleton.cs
│   │   ├── ConfigLoadSkeleton.cs
│   │   ├── EventTriggerSkeleton.cs
│   │   ├── CommandSetSkeleton.cs
│   │   ├── CommandClickSkeleton.cs
│   │   └── CommandGetSkeleton.cs
│   ├── Diagnostics/                 # C. 诊断日志
│   │   ├── RouterAdapterLogSkeleton.cs
│   │   ├── RouterNormalizeLogSkeleton.cs
│   │   ├── RouterLogCommandLogSkeleton.cs
│   │   ├── ExecutionReportLogSkeleton.cs
│   │   ├── ScriptConverterLogSkeleton.cs
│   │   └── ValueExtractionLogSkeleton.cs
│   └── Factories/                   # D. 静态工厂
│       ├── LogInjectionSkeletons.cs
│       └── FileDrivenLoggingSkeletons.cs
│
├── Wrappers/                        # 包装器
│   ├── LogEntryCollector.cs
│   ├── LoggingSkeletonWrapper.cs
│   └── LoggingSkeletonWrapper.cs
│
├── Handlers/                        # Placeholder Handler
│   └── FileDrivenLogPlaceholderHandler.cs
│
└── Emergency/                       # 紧急处理
    └── EmergencyDumpHelper.cs
```

### 分类原则

| 分类 | 职责 | 依赖方向 |
|---|---|---|
| **Models** | 纯数据模型，无业务逻辑 | 无依赖 |
| **Core** | 工具类，无 Skeleton 依赖 | 依赖 Models |
| **Pipeline** | 日志管线编排 | 依赖 Core、Wrappers |
| **Skeletons/Orchestration** | 业务流程编排 | 依赖 Pipeline、Skeletons/Commands |
| **Skeletons/Commands** | 具体命令执行 | 依赖 Core、CLI 服务 |
| **Skeletons/Diagnostics** | 日志注入（纯副作用） | 依赖 Wrappers |
| **Skeletons/Factories** | 静态工厂方法 | 依赖 Pipeline |
| **Wrappers** | Skeleton 包装器 | 依赖 Models、Core |
| **Handlers** | Placeholder 处理 | 依赖 Core |
| **Emergency** | 紧急降级处理 | 依赖 Core |

---

## 三、已修复的问题

### 3.1 命名空间歧义（已修复）

| 问题 | 修复方式 |
|---|---|
| `LogPipelineContext` 在 `Common.Diagnostics` 和 `FileDriven.Logging.Pipeline` 中重复 | 添加 `using PipelineContext = FileDriven.Logging.Pipeline.LogPipelineContext;` 别名 |
| `Flow<,>` 未找到 | 添加 `using SkeletonFlow.App;` |
| `EventTriggerService` 未找到 | 添加 `using FileDriven.CLI.Services;` |
| `VirtualControlStore` 未找到 | 添加 `using FileDriven.CLI.Services;` |
| `LoadedConfigInfo` 未找到 | 添加 `using FileDriven.CLI.Adapters;` |
| `Placeholder` 未找到 | 添加 `using Graphkey.Core;` |
| `PlaceholderFramework` 命名空间错误 | 修正为 `using Common.PlaceholderFramework.Handlers;` |

### 3.2 重复定义（已合并）

| 重复项 | 处理方式 |
|---|---|
| `FileDrivenLoggingSkeletons` 与 `LogInjectionSkeletons` | 合并到 `LogInjectionSkeletons`，删除重复 |
| 多个文件中的 `LogPipelineContext` 定义 | 统一到 `LogPipeline.cs` |
| 多个文件中的 `LogPipelineException` 定义 | 统一到 `LogPipeline.cs` |

---

## 四、遗留问题（需手动修复）

### 4.1 命名空间歧义（需添加别名）

以下类型在 `Common.Diagnostics` 和 `FileDriven.Logging` 中重复定义，需要添加 using 别名：

```csharp
// 在 LogPipeline.cs 中
using LogEntryCollectorAlias = FileDriven.Logging.Wrappers.LogEntryCollector;

// 在 Skeletons.cs 中
using LogEntryCollectorAlias = FileDriven.Logging.Wrappers.LogEntryCollector;
using LogPipelineAlias = FileDriven.Logging.Pipeline.LogPipeline;

// 在 Wrappers.cs 中
using SkeletonExecutionStatsAlias = FileDriven.Logging.Models.SkeletonExecutionStats;

// 在 FileDrivenLogManager.cs 中
using LogPipelineAlias = FileDriven.Logging.Pipeline.LogPipeline;
```

### 4.2 LogCategory 缺少成员

`Skeletons.cs` 中使用了 `LogCategory.Lifecycle`、`LogCategory.Command`、`LogCategory.Config`、`LogCategory.Report`，但这些成员在 `Common.Diagnostics.LogCategory` 枚举中不存在。需要：

1. 检查 `Common.Diagnostics.LogCategory` 的实际定义
2. 使用 `LogCategoryEx` 扩展类中的对应值（如 `(LogCategory)LogCategoryEx.Launcher_Lifecycle`）

### 4.3 Wrappers.cs 中的 out 参数问题

`Wrappers.cs` 第 64 行存在 `TryGetValue` 调用参数顺序错误，需要修正。

### 4.4 Flow.Start 重载问题

`LogPipeline.cs` 中 `Flow.Start(skeleton)` 没有匹配的重载，需要检查 `SkeletonFlow.App.Flow` 的 API 签名。

### 4.5 BatchRouter 未找到

`EmergencyDumpHelper.cs` 中引用了 `BatchRouter`，但该类型不在 `UniversalWorkflowFramework.BatchIntegration` 命名空间中，需要查找实际位置。

### 4.6 LogRepository.Initialize 未找到

`FileDrivenLogManager.cs` 中调用了 `LogRepository.Initialize()`，但该方法不存在。

### 4.7 WdUIComposer 未找到

`Skeletons.cs` 中 `ConfigLoadSkeleton` 引用了 `WdUIComposer`，但该类型未导入。

---

## 五、重构建议

### 5.1 短期（立即执行）

1. 添加所有缺失的 using 别名
2. 修正 `LogCategory` 枚举值引用
3. 修正 `Wrappers.cs` 的 out 参数问题

### 5.2 中期（1-2 周）

1. 将 `Skeletons.cs` 按分类拆分为子目录文件（Orchestration/Commands/Diagnostics/Factories）
2. 统一 `LogEntryCollector` 的使用方式，消除与 `Common.Diagnostics` 的歧义
3. 将 `LogPipeline.cs` 中的 `LogPipelineContext` 和 `LogPipelineException` 拆分为独立文件

### 5.3 长期（1 个月）

1. 评估 `Common.Diagnostics` 中的重复类型是否可以统一
2. 考虑将 `FileDriven.Logging` 中的 Skeleton 定义迁移到 `FileDriven.CLI` 项目（因为它们依赖 CLI 类型）
3. 建立命名规范：所有 `FileDriven.Logging` 中的类型不应与 `Common.Diagnostics` 中的类型重名
