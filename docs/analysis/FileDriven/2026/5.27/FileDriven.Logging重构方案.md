# FileDriven.Logging 重构方案

## 一、现状分析

### 1.1 当前日志代码分布（6 个项目）

| 项目 | 文件 | 职责 |
|------|------|------|
| **BasicMethod.Logging** | `LogCore.cs`, `LogRepository.cs`, `LogModels.cs`, `LogController.cs` | 基础日志基础设施（Logger、LogEntry、LogRepository） |
| **Share.PlaceholderFramework** | `Handlers/FileDrivenLogPlaceholderHandler.cs`, `Core/PlaceholderLogHelper.cs`, `Core/LogMessageLoader.cs` | Placeholder 日志 Handler、消息模板加载 |
| **Share.Diagnostics** | `LoggingSkeletonWrapper.cs`, `LogPipelineSkeletons.cs`, `LogCategories.cs`, `CategoryResolver.cs`, `LogCategoryLoader.cs`, `LogCategoryRegistry.cs`, `LogQueryFS.cs` | 日志管线 Skeleton、日志包装器、分类管理 |
| **Share.UniversalReference** | `BatchIntegration/BatchManager.cs`, `BatchIntegration/BatchLoggerBridge.cs`, `BatchIntegration/BatchChangeTracker.cs` | 批次管理、批次上下文传递 |
| **FileDriven.Pipeline** | `Skeletons/FileDrivenPipeline.cs`, `Skeletons/FileDrivenPipelineSkeletons.cs`, `Skeletons/LogInjectionSkeletons.cs` | FileDriven 业务管线、日志注入 Skeleton |
| **FileDriven.Launcher** | `Program.cs`, `FrameworkInitializer.cs` | 启动入口、日志初始化、紧急转储 |

### 1.2 存在的问题

1. **跨项目依赖复杂**：`FileDriven.Pipeline` 引用了 5 个日志相关项目
2. **两条并行日志路径**：
   - 路径 A：`FileDrivenLogPlaceholderHandler.Log()` → 直接写 `LogRepository`
   - 路径 B：`LoggingSkeletonWrapper` → 写 `SkeletonContext` → `LogCollectSkeleton` → `LogRepository`
   - 两条路径最终都到 `LogRepository`，但时序不可控
3. **Category 解析重复**：`PlaceholderLogHelper.ResolveCategory()` 和 `CategoryResolver` 都做字符串→枚举映射
4. **初始化分散**：日志初始化在 `Program.Main()` 中分三步（LoggingSetup → FileDrivenLogPlaceholderHandler → FrameworkInitializer）
5. **紧急转储逻辑在 Launcher**：`Program.EmergencyFileDump()` 是日志降级逻辑，却放在启动器项目中

---

## 二、重构目标

### 2.1 核心目标

创建一个独立的 **`FileDriven.Logging`** 项目，集中管理 FileDriven 的所有日志运行处理逻辑。

### 2.2 具体目标

1. **统一日志入口**：所有日志通过 `FileDrivenLogManager` 统一管理
2. **消除双路径**：统一使用 `LoggingSkeletonWrapper` + `LogCollectSkeleton` 路径
3. **简化初始化**：从三步变为一步
4. **消除重复**：合并两个 Category 解析器
5. **职责清晰**：紧急转储逻辑从 Launcher 移到 Logging 项目
6. **易于测试**：日志逻辑集中后，可统一 mock LogRepository 进行测试

---

## 三、新项目结构

### 3.1 项目文件结构

```
FileDriven/FileDriven.Logging/
├── FileDriven.Logging.csproj
│
├── Core/
│   ├── FileDrivenLogManager.cs          ← 统一日志入口（核心）
│   ├── LogMessageLoader.cs              ← 从 Share.PlaceholderFramework 迁移
│   ├── PlaceholderLogHelper.cs          ← 从 Share.PlaceholderFramework 迁移
│   └── LogCategoryResolver.cs           ← 合并 PlaceholderLogHelper + CategoryResolver
│
├── Handlers/
│   └── FileDrivenLogPlaceholderHandler.cs  ← 从 Share.PlaceholderFramework 迁移
│
├── Pipeline/
│   ├── LogPipelineContext.cs            ← 从 Share.Diagnostics 迁移
│   ├── LogInitSkeleton.cs              ← 从 Share.Diagnostics 迁移
│   ├── LogCollectSkeleton.cs           ← 从 Share.Diagnostics 迁移
│   ├── LogFlushSkeleton.cs             ← 从 Share.Diagnostics 迁移
│   ├── LogEmergencyDumpSkeleton.cs     ← 从 Share.Diagnostics 迁移
│   ├── LogSummarySkeleton.cs           ← 从 Share.Diagnostics 迁移
│   ├── LogTryExecuteSkeleton.cs        ← 从 Share.Diagnostics 迁移
│   └── LogPipeline.cs                  ← 从 Share.Diagnostics 迁移（构建器）
│
├── Wrappers/
│   ├── LoggingSkeletonWrapper.cs       ← 从 Share.Diagnostics 迁移
│   ├── LogEntryCollector.cs            ← 从 Share.Diagnostics 迁移
│   ├── LifecycleLogSkeleton.cs         ← 从 Share.Diagnostics 迁移
│   ├── ExecutionStatsReportSkeleton.cs ← 从 Share.Diagnostics 迁移
│   ├── ExecutionStatsHelper.cs         ← 从 Share.Diagnostics 迁移
│   └── ChecklistLogger.cs             ← 从 Share.Diagnostics 迁移
│
├── Skeletons/
│   ├── FileDrivenPipeline.cs           ← 从 FileDriven.Pipeline 迁移（日志管线部分）
│   ├── FrameworkInitSkeleton.cs        ← 从 FileDriven.Pipeline 迁移
│   ├── CommandLoopSkeleton.cs          ← 从 FileDriven.Pipeline 迁移
│   ├── EventTriggerSkeleton.cs         ← 从 FileDriven.Pipeline 迁移
│   ├── ConfigLoadSkeleton.cs           ← 从 FileDriven.Pipeline 迁移
│   ├── CommandSetSkeleton.cs           ← 从 FileDriven.Pipeline 迁移
│   ├── CommandClickSkeleton.cs         ← 从 FileDriven.Pipeline 迁移
│   ├── CommandGetSkeleton.cs           ← 从 FileDriven.Pipeline 迁移
│   ├── RouterAdapterLogSkeleton.cs     ← 从 FileDriven.Pipeline 迁移
│   ├── RouterNormalizeLogSkeleton.cs   ← 从 FileDriven.Pipeline 迁移
│   ├── RouterLogCommandLogSkeleton.cs  ← 从 FileDriven.Pipeline 迁移
│   ├── ExecutionReportLogSkeleton.cs   ← 从 FileDriven.Pipeline 迁移
│   ├── ScriptConverterLogSkeleton.cs   ← 从 FileDriven.Pipeline 迁移
│   └── ValueExtractionLogSkeleton.cs   ← 从 FileDriven.Pipeline 迁移
│
├── Models/
│   ├── LogMessageDef.cs                ← 从 Share.PlaceholderFramework 迁移
│   ├── SkeletonExecutionStats.cs       ← 从 Share.Diagnostics 迁移
│   └── LifecycleLogInput.cs            ← 从 Share.Diagnostics 迁移
│
└── Emergency/
    └── EmergencyDumpHelper.cs          ← 从 FileDriven.Launcher.Program 迁移
```

### 3.2 项目依赖

```xml
<!-- FileDriven.Logging.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>FileDriven.Logging</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <!-- 基础日志基础设施 -->
    <ProjectReference Include="..\..\BasicMethod\Logging\BasicMethod.Logging.csproj" />
    
    <!-- 批次管理 -->
    <ProjectReference Include="..\..\Share\UniversalReference\UniversalWorkflowFramework.csproj" />
    
    <!-- Skeleton 框架 -->
    <ProjectReference Include="..\..\BasicMethod\BasicMethod.csproj" />
    
    <!-- 路径解析 -->
    <ProjectReference Include="..\..\BasicMethod\PathManager\PathManager.csproj" />
    
    <!-- FileDriven 核心 -->
    <ProjectReference Include="..\FileDrivenFramework.Core\FileDrivenFramework.Core.csproj" />
    
    <!-- CLI 路由（CommandLoopSkeleton 需要） -->
    <ProjectReference Include="..\FileDriven.CLI\FileDriven.CLI.csproj" />
  </ItemGroup>
</Project>
```

---

## 四、核心 API 设计

### 4.1 FileDrivenLogManager — 统一日志入口

```csharp
namespace FileDriven.Logging.Core;

/// <summary>
/// FileDriven 统一日志管理器
/// 集中管理日志系统的初始化、运行和关闭
/// </summary>
public static class FileDrivenLogManager
{
    /// <summary>
    /// 初始化日志系统（一步完成所有初始化）
    /// </summary>
    /// <param name="logMessagesPath">filedriven_log_messages.json 路径</param>
    /// <param name="enabledCategories">启用的日志分类</param>
    /// <param name="projectRoot">项目根目录（可选，自动检测）</param>
    public static void Initialize(
        string logMessagesPath,
        LogCategory enabledCategories = LogCategory.All,
        string? projectRoot = null)
    {
        // 1. 初始化 Logger 框架
        LoggingSetup.Initialize(enabledCategories);
        
        // 2. 初始化 FileDrivenLogPlaceholderHandler
        FileDrivenLogPlaceholderHandler.Initialize(logMessagesPath);
        
        // 3. 设置项目根目录
        ProjectRoot = projectRoot ?? ProjectPathManager.FindProjectRoot();
        
        // 4. 注册紧急转储回调
        _emergencyDumpCallback = EmergencyDumpHelper.EmergencyDump;
        
        IsInitialized = true;
    }
    
    /// <summary>
    /// 执行日志管线并关闭日志系统
    /// </summary>
    public static void Shutdown(string? batchId = null)
    {
        if (!IsInitialized) return;
        
        // 执行日志管线
        var targetBatchId = batchId ?? BatchLoggerBridge.CurrentBatchId;
        if (targetBatchId != null)
        {
            LogPipeline.ExecuteWithExceptionHandling(
                batchId: targetBatchId,
                batchName: "自动生成批次",
                source: "Launcher",
                projectRoot: ProjectRoot
            );
        }
        
        IsInitialized = false;
    }
    
    /// <summary>
    /// 记录日志（统一日志输出方法）
    /// </summary>
    public static void Log(string level, string category, string module, string message)
    {
        PlaceholderLogHelper.Log(level, category, module, message);
    }
    
    /// <summary>
    /// 记录日志（带消息 ID 的模板化方式）
    /// </summary>
    public static void LogMessage(string msgId, object? parameters = null)
    {
        FileDrivenLogPlaceholderHandler.Log(msgId, parameters);
    }
}
```

### 4.2 启动流程对比

#### 重构前（三步初始化）

```csharp
// Program.Main()
// 步骤 1：初始化 Logger 框架
LoggingSetup.Initialize(options.LogCategories);

// 步骤 2：初始化 FileDriven 日志 Placeholder Handler
var logMessagesPath = Path.Combine(
    PathManager.ProjectPathManager.FindProjectRoot(),
    "FileDriven",
    "filedriven_log_messages.json");
FileDrivenLogPlaceholderHandler.Initialize(logMessagesPath);

// 步骤 3：框架初始化（内部包含批次日志初始化）
FrameworkInitializer.Initialize();

// ... 执行 FileDrivenPipeline ...

// 步骤 4：手动执行日志管线
ExecuteLogPipeline();

// 步骤 5：关闭框架
FrameworkInitializer.Shutdown();
```

#### 重构后（一步初始化 + 自动关闭）

```csharp
// Program.Main()
// 一步初始化
FileDrivenLogManager.Initialize(
    logMessagesPath: Path.Combine(projectRoot, "FileDriven", "filedriven_log_messages.json"),
    enabledCategories: options.LogCategories);

// 框架初始化（不再包含日志初始化）
FrameworkInitializer.Initialize();

// ... 执行 FileDrivenPipeline ...

// 自动执行日志管线并关闭
FileDrivenLogManager.Shutdown(batchId);
```

---

## 五、消除双路径方案

### 5.1 当前双路径问题

```
路径 A（业务 Skeleton 内部）：
  ConfigLoadSkeleton.ExecuteCore()
    → FileDrivenLogPlaceholderHandler.Log("ConfigLoadSkeleton_ExecuteCore_loading", ...)
    → PlaceholderLogHelper.Log(...)
    → LogRepository.Write(entry)          ← 直接写入

路径 B（管线层面）：
  LoggingSkeletonWrapper.ExecuteCore()
    → LogEntryCollector.LogInfo(ctx, ...)
    → ctx.SetData(entries, "__LogEntries")  ← 暂存到 SkeletonContext
    → LogCollectSkeleton.ExecuteCore()
    → LogRepository.Write(entry)          ← 统一收集写入
```

### 5.2 重构后统一路径

```
统一路径（所有日志通过 LoggingSkeletonWrapper）：
  
  方案：废弃 FileDrivenLogPlaceholderHandler.Log() 的直接写入方式，
       改为通过 LogEntryCollector 写入 SkeletonContext。
  
  业务 Skeleton 内部不再调用 FileDrivenLogPlaceholderHandler.Log()，
  而是通过 LogEntryCollector.LogInfo/Warn/Error(ctx, ...) 写入 SkeletonContext。
  
  管线流程：
    LoggingSkeletonWrapper 自动记录开始/完成日志
      → LogEntryCollector.LogInfo(ctx, ...)
      → ctx.SetData(entries, "__LogEntries")
    
    业务 Skeleton 内部日志
      → LogEntryCollector.LogInfo(ctx, ...)
      → ctx.SetData(entries, "__LogEntries")
    
    LogCollectSkeleton 统一收集
      → LogRepository.Write(entry)
    
    LogFlushSkeleton 刷入 SQLite
      → BatchRouter.FlushToIsolatedStorage(batchId)
```

### 5.3 迁移步骤

1. 在 `LoggingSkeletonWrapper` 中添加 `LogEntryCollector` 的便捷访问属性
2. 修改所有业务 Skeleton，将 `FileDrivenLogPlaceholderHandler.Log()` 替换为 `LogEntryCollector.LogInfo/Warn/Error(ctx, ...)`
3. 保留 `FileDrivenLogPlaceholderHandler.Log()` 作为非 Skeleton 场景（如 FrameworkInitializer）的备用路径
4. 在 `LogCollectSkeleton` 中添加去重逻辑，防止同一日志被收集两次

---

## 六、迁移计划

### 6.1 阶段一：创建 FileDriven.Logging 项目

1. 创建 `FileDriven/FileDriven.Logging/` 目录和项目文件
2. 从 `Share.PlaceholderFramework` 迁移：
   - `Core/LogMessageLoader.cs` → `Core/LogMessageLoader.cs`
   - `Core/PlaceholderLogHelper.cs` → `Core/PlaceholderLogHelper.cs`
   - `Handlers/FileDrivenLogPlaceholderHandler.cs` → `Handlers/FileDrivenLogPlaceholderHandler.cs`
3. 从 `Share.Diagnostics` 迁移：
   - `LoggingSkeletonWrapper.cs` → `Wrappers/LoggingSkeletonWrapper.cs`
   - `LogPipelineSkeletons.cs` → 拆分为 `Pipeline/` 下的多个文件
4. 创建 `Core/FileDrivenLogManager.cs`
5. 创建 `Emergency/EmergencyDumpHelper.cs`

### 6.2 阶段二：迁移业务 Skeleton

1. 从 `FileDriven.Pipeline` 迁移业务 Skeleton 到 `FileDriven.Logging/Skeletons/`
2. 修改所有 Skeleton，将 `FileDrivenLogPlaceholderHandler.Log()` 替换为 `LogEntryCollector.LogInfo/Warn/Error(ctx, ...)`
3. 更新 `FileDrivenPipeline.cs` 引用新路径

### 6.3 阶段三：更新启动器

1. 修改 `FileDriven.Launcher/Program.cs`：
   - 移除 `LoggingSetup.Initialize()` 调用
   - 移除 `FileDrivenLogPlaceholderHandler.Initialize()` 调用
   - 移除 `ExecuteLogPipeline()` 方法
   - 移除 `EmergencyFileDump()` 方法
   - 替换为 `FileDrivenLogManager.Initialize()` 和 `FileDrivenLogManager.Shutdown()`
2. 修改 `FileDriven.Launcher/FrameworkInitializer.cs`：
   - 移除 `InitializeBatchLogging()` 方法
   - 移除 `LoadWduiLogConfigFromEnvironment()` 方法

### 6.4 阶段四：清理旧项目

1. 从 `Share.PlaceholderFramework` 移除已迁移的文件
2. 从 `Share.Diagnostics` 移除已迁移的文件
3. 从 `FileDriven.Pipeline` 移除已迁移的文件
4. 更新所有项目的 `.csproj` 引用关系

---

## 七、重构后的依赖关系图

### 重构前

```
FileDriven.Launcher
  ├── BasicMethod.Logging
  ├── Share.PlaceholderFramework
  ├── Share.Diagnostics
  ├── Share.UniversalReference
  ├── FileDriven.Pipeline
  │     ├── BasicMethod.Logging
  │     ├── Share.PlaceholderFramework
  │     ├── Share.Diagnostics
  │     ├── Share.UniversalReference
  │     └── ...
  └── ...
```

### 重构后

```
FileDriven.Launcher
  ├── FileDriven.Logging（唯一日志引用）
  │     ├── BasicMethod.Logging
  │     ├── Share.UniversalReference
  │     └── SkeletonFlow
  └── ...

FileDriven.Pipeline（仅保留业务逻辑，无日志）
  └── FileDriven.Logging（引用日志项目）
```

---

## 八、风险与注意事项

### 8.1 兼容性风险

- `FileDrivenLogPlaceholderHandler.Log()` 被大量非 Skeleton 代码调用（如 `FrameworkInitializer`），不能立即废弃
- 解决方案：保留静态 `Log()` 方法作为备用路径，但标记为 `[Obsolete]`

### 8.2 日志丢失风险

- 迁移过程中如果 `LogEntryCollector` 和 `LogCollectSkeleton` 配合不当，可能导致日志丢失
- 解决方案：在 `LogCollectSkeleton` 中添加日志计数校验，与 `LogEntryCollector.GetLogCount()` 对比

### 8.3 性能考虑

- `LogEntryCollector` 将日志暂存在 `SkeletonContext` 中，增加了内存占用
- 解决方案：在 `LogCollectSkeleton` 写入 `LogRepository` 后立即清空 `SkeletonContext` 中的日志集合

### 8.4 测试策略

- 为 `FileDrivenLogManager` 编写单元测试，验证初始化流程
- 为 `LogPipeline` 编写集成测试，验证完整管线流程
- 为 `LogCollectSkeleton` 编写测试，验证日志收集和去重

---

## 九、附录：文件迁移对照表

| 源文件 | 目标文件 | 操作 |
|--------|---------|------|
| `Share/PlaceholderFramework/Core/LogMessageLoader.cs` | `FileDriven.Logging/Core/LogMessageLoader.cs` | 复制 + 更新 namespace |
| `Share/PlaceholderFramework/Core/PlaceholderLogHelper.cs` | `FileDriven.Logging/Core/PlaceholderLogHelper.cs` | 复制 + 更新 namespace |
| `Share/PlaceholderFramework/Handlers/FileDrivenLogPlaceholderHandler.cs` | `FileDriven.Logging/Handlers/FileDrivenLogPlaceholderHandler.cs` | 复制 + 更新 namespace |
| `Share/Diagnostics/LoggingSkeletonWrapper.cs` | `FileDriven.Logging/Wrappers/LoggingSkeletonWrapper.cs` | 复制 + 更新 namespace |
| `Share/Diagnostics/LogPipelineSkeletons.cs` | `FileDriven.Logging/Pipeline/` (拆分为多个文件) | 拆分 + 更新 namespace |
| `Share/Diagnostics/LogCategories.cs` | `FileDriven.Logging/Core/LogCategoryResolver.cs` | 合并 |
| `Share/Diagnostics/CategoryResolver.cs` | `FileDriven.Logging/Core/LogCategoryResolver.cs` | 合并 |
| `FileDriven/FileDriven.Pipeline/Skeletons/FileDrivenPipeline.cs` | `FileDriven.Logging/Skeletons/FileDrivenPipeline.cs` | 复制 + 更新引用 |
| `FileDriven/FileDriven.Pipeline/Skeletons/FileDrivenPipelineSkeletons.cs` | `FileDriven.Logging/Skeletons/` (拆分为多个文件) | 拆分 + 更新日志路径 |
| `FileDriven/FileDriven.Pipeline/Skeletons/LogInjectionSkeletons.cs` | `FileDriven.Logging/Skeletons/` (拆分为多个文件) | 拆分 + 更新日志路径 |
| `FileDriven/FileDriven.Launcher/Program.cs` (部分) | `FileDriven.Logging/Emergency/EmergencyDumpHelper.cs` | 提取 |
| `FileDriven/FileDriven.Launcher/FrameworkInitializer.cs` (部分) | `FileDriven.Logging/Core/FileDrivenLogManager.cs` | 合并 |
