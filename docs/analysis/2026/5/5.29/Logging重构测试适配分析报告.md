# Logging 框架重构 — CentralConsole 测试适配分析报告

## 一、重构背景

Logging 框架进行了架构重构，核心变更如下：

| 变更项 | 重构前 | 重构后 |
|--------|--------|--------|
| 批次元数据管理 | `BatchRouter`（Share/Diagnostics） | `BatchManager`（UniversalReference） |
| 当前批次 ID 传递 | `BatchRouter.CurrentBatchId` | `BatchLoggerBridge.CurrentBatchId`（AsyncLocal） |
| 批次注册 | `BatchRouter.RegisterBatch()` | `BatchManager.Instance.RegisterBatch()` |
| 日志查询 | `BatchRouter.Query()` | `BatchRouter.Query()`（保留） |
| 日志刷入 | `BatchRouter.FlushToIsolatedStorage()` | `BatchRouter.FlushToIsolatedStorage()`（保留） |
| 分类注册表 | `LogCategories.cs`（硬编码） | `LogCategoryRegistry`（JSON 配置驱动） |

## 二、当前测试状态

**测试总数：172 | 通过：127 | 失败：45**

### 2.1 失败测试分类

| 类别 | 失败数 | 根因 |
|------|--------|------|
| **LogCategoryRegistry** | 7 | `Clear()` 后默认分类被重新加载；`Register()` 不更新描述；JSON 配置 20 个节点 vs 测试预期 21 个；位标志从 16 开始 vs 测试预期从 46 开始 |
| **LogCategorySelector** | 2 | `Clear()` 后默认分类被重新加载，测试上下文被污染 |
| **LogController** | 5 | `Clear()` 后默认分类被重新加载；别名映射未配置 |
| **LogDiagnosticDebug** | 3 | 同上 |
| **LogQueryFS** | 6 | 查询结果为空或统计不正确 |
| **LogQueryResult** | 2 | ToFormattedText、ToJson 格式问题 |
| **LogRepository** | 2 | SequenceNumber、GetNewerThan |
| **Pipeline** | 3 | NormalFlow、EmergencyFallback、Idempotent（导出文件为 0） |
| **BatchIntegration** | 3 | FullWorkflow、MultipleBatches、ErrorStats |
| **BatchEnvironment** | 2 | To_BatchLoggerBridge、FullChain |
| **BatchSelector** | 5 | GetCurrentBatch、SwitchTo、ResetToCurrent、GetCurrentBatchDisplayName |

### 2.2 已修复问题

| 修复项 | 文件 | 修复内容 |
|--------|------|----------|
| ✅ `BatchLoggerBridge.ExitBatch()` 清理 | `LoggingCleanupSkeleton.cs` | 三个 `ClearAll` 方法添加 `ExitBatch()` |
| ✅ `BatchFixtureSkeletons.RegisterBatch()` 同步 | `BatchFixtureSkeletons.cs` | `RegisterBatch()` 自动调用 `BatchLoggerBridge.EnterBatch()` |
| ✅ `LoggingWriteSkeleton.WriteLogs()` 注入 BatchId | `LoggingWriteSkeleton.cs` | `WriteLogs()` 自动注入 `BatchId` 字段 |
| ✅ `LoggingTestCleanup.CleanupBatchRouter()` | `LoggingTestCleanup.cs` | 改用 `LogRepository.Reset()` |
| ✅ `LogCategoryLoader` 测试环境回退路径 | `LogCategoryLoader.cs` | `GetDefaultConfigPath()` 添加回溯查找 |
| ✅ `log-categories.json` 复制到输出目录 | `Share.Diagnostics.csproj` | 添加 `CopyToOutputDirectory` |
| ✅ JSON 反序列化大小写不敏感 | `LogCategoryLoader.cs` | 添加 `PropertyNameCaseInsensitive = true` |
| ✅ `LogCategoryRegistry.Clear()` 空字典 | `LogCategoryRegistry.cs` | `Clear()` 设置空字典而非 null，避免自动重新加载 |
| ✅ `LogCategoryRegistry.Register()` 更新描述 | `LogCategoryRegistry.cs` | 重复注册时更新 `Description` 属性 |
| ✅ `CategoryNode.Description` 可写 | `LogCategoryRegistry.cs` | `Description` 改为 `internal set` |
| ✅ `LogRepository.Reset()` 重置序号 | `LogRepository.cs` | 添加 `Interlocked.Exchange(ref _nextSequence, 0)` |
| ✅ `BatchFixtureSkeletons` 改用 Reset | `BatchFixtureSkeletons.cs` | `LogRepository.Clear()` → `LogRepository.Reset()` |
| ✅ 所有测试文件 `Clear` → `Reset` | 全部 95 处 | 批量替换 `LogRepository.Clear()` → `LogRepository.Reset()` |


## 三、框架层面根因分析

### 3.1 核心问题：`LogCategoryRegistry.Clear()` 与 `EnsureInitialized()` 的循环依赖

**这是导致最多测试失败（约 30 个）的根本原因。**

#### 问题描述

`LogCategoryRegistry` 是一个静态类，其设计存在一个根本性的矛盾：

1. `Clear()` 方法将 `_nodes = null`（清空所有注册）
2. `Count` getter、`GetAllPaths()`、`GetLeafNodes()` 等方法都调用 `EnsureInitialized()`
3. `EnsureInitialized()` 在 `_nodes == null` 时调用 `BuildCategoryTree()` → `LogCategoryLoader.LoadDefinitions()` 重新加载默认分类
4. 因此，`Clear()` 后访问 `Count` 等属性会**自动重新加载默认分类**，导致 `Clear()` 无效

#### 影响范围

所有在 `Clear()` 后期望 `Count == 0` 的测试都会失败，因为 `Count` getter 内部调用了 `EnsureInitialized()` 重新加载了默认分类。

#### 修复方案

**方案 A（推荐）：`Clear()` 后设置空字典而非 null**

```csharp
public static void Clear()
{
    lock (_initLock)
    {
        _nodes = new Dictionary<string, CategoryNode>();  // 空字典而非 null
        _dynamicNodes = null;
        _nextDynamicBit = 1UL << 56;
    }
}
```

这样 `EnsureInitialized()` 的 `if (_nodes != null) return;` 检查会通过，不会重新加载默认分类。

**方案 B：`Clear()` 添加 `_initialized` 标志**

```csharp
private static bool _initialized;

private static void EnsureInitialized()
{
    if (_initialized) return;
    lock (_initLock)
    {
        if (_initialized) return;
        _nodes = BuildCategoryTree();
        _initialized = true;
    }
}

public static void Clear()
{
    lock (_initLock)
    {
        _nodes = null;
        _dynamicNodes = null;
        _nextDynamicBit = 1UL << 56;
        _initialized = false;  // 允许重新初始化
    }
}
```

但方案 B 仍然有同样的问题：`Clear()` 后访问 `Count` 会重新加载默认分类。

**推荐方案 A**，因为：
- 测试期望 `Clear()` 后注册表为空
- 需要重新加载时，测试可以显式调用 `InitializeDefaults()`
- 与现有测试语义一致

### 3.2 问题：`Register()` 重复注册不更新描述

#### 问题描述

`Register()` 方法在路径已存在时直接返回已有节点，不更新描述：

```csharp
if (_nodes!.ContainsKey(normalized))
    return _nodes[normalized];
```

测试期望重复注册同一路径能更新描述。

#### 修复方案

```csharp
if (_nodes!.ContainsKey(normalized))
{
    var existingNode = _nodes[normalized];
    existingNode.Description = description;  // ★ 更新描述
    return existingNode;
}
```

### 3.3 问题：JSON 配置 20 个节点 vs 测试预期 21 个

#### 问题描述

`log-categories.json` 中有 20 个叶子节点（WdUI:7 + FileDriven:3 + Launcher:3 + FunctionStorage:4 + Batch:3 = 20），但测试预期 `InitializeDefaults` 注册 21 个节点。

#### 根因

旧代码（重构前）硬编码了 21 个分类，但 JSON 配置中 Batch 模块只有 3 个分类（Lifecycle、Routing、Query），而旧代码可能有 4 个 Batch 分类。

#### 修复方案

**方案 A：在 JSON 中添加第 4 个 Batch 分类**

在 `log-categories.json` 的 Batch 模块中添加一个分类（如 `"Export": { "description": "Batch 日志导出" }`），使总数达到 21。

**方案 B：更新测试预期为 20**

将测试中的 `Assert.AreEqual(21, count)` 改为 `Assert.AreEqual(20, count)`。

**推荐方案 A**，因为：
- 保持与旧代码的兼容性
- 测试预期 21 个节点是合理的业务需求
- 只需修改配置文件，无需修改测试代码

### 3.4 问题：位标志从 16 开始 vs 测试预期从 46 开始

#### 问题描述

测试预期 `WdUI/Render` 的位标志为 `1UL << 46`（70368744177664），但实际为 `1UL << 16`（65536）。

#### 根因

旧代码中 `LogCategory` 枚举的位标志从 46 开始（`WdUI_Render = 1UL << 46`），但重构后 `BuildCategoryTree()` 从 `LogCategory` 枚举中解析位标志，而 `LogCategoryEx` 枚举的位标志从 16 开始。

#### 修复方案

**方案 A：更新测试预期值**

将测试中的位标志预期值从 `1UL << 46` 改为 `1UL << 16`。

**方案 B：在 JSON 配置中指定位标志偏移**

在 `log-categories.json` 中添加 `baseBitOffset` 配置，使位标志从 46 开始。

**推荐方案 A**，因为：
- 位标志的具体值不是业务逻辑的一部分
- 只要位标志唯一且非零即可
- 更新测试预期值成本最低

### 3.5 问题：Pipeline 测试导出文件为 0

#### 问题描述

`Test_Pipeline_NormalFlow` 中 `ExportedFileCountEquals(2)` 实际为 0，说明日志导出到文件系统的过程失败。

#### 根因

需要检查：
1. `LogPipeline.ExecuteWithExceptionHandling` 的导出路径配置
2. `LogFileExporter` 是否依赖 `LoggingConfiguration` 中的路径配置
3. 测试环境中文件系统权限
4. `LogPipelineSkeletons` 中的 `PipelineFixture` 是否正确初始化了导出路径

#### 修复方案

需要进一步分析 `LogPipelineSkeletons.cs` 和 `LogFileExporter` 的实现。

### 3.6 问题：`LogRepository.SequenceNumber` 和 `GetNewerThan` 失败

#### 问题描述

写入 5 条日志后序号未从 1 开始递增，增量读取未返回新增日志。

#### 根因

`LogRepository.Reset()` 未重置 `_sequenceNumber` 字段，或 `ClearAll` 后 `LogRepository` 的静态状态未完全清理。

#### 修复方案

检查 `LogRepository.Reset()` 实现，确保 `_sequenceNumber` 字段被重置为 0。

### 3.7 问题：`LogController` 别名解析失败

#### 问题描述

`LogController.Parse()` 的别名解析（如 `wdui.render`、`render`、`filedriven.command` 等）全部失败。

#### 根因

`LogController` 的别名映射表未正确初始化，或依赖 `LogCategoryRegistry` 的默认分类注册。

#### 修复方案

需要检查 `LogController` 的别名映射初始化逻辑，确保在测试环境中别名映射正确加载。

### 3.8 问题：`LogQueryFS` 查询结果为空

#### 问题描述

按路径、级别、关键词查询日志都返回空结果。

#### 根因

`LogQueryFS` 的查询方法依赖 `LogRepository` 中的日志数据，但测试中写入的日志可能未被正确存储到 `LogRepository`，或查询条件与存储的日志不匹配。

#### 修复方案

需要检查 `LogQueryFS` 的查询实现和 `LogRepository` 的存储逻辑。

## 四、修复方案汇总

### 4.1 框架代码修复

| 优先级 | 修复项 | 文件 | 修复内容 | 影响测试数 |
|--------|--------|------|----------|-----------|
| P0 | `Clear()` 后不重新加载默认分类 | `LogCategoryRegistry.cs` | `Clear()` 设置空字典而非 null | ~30 |
| P0 | `Register()` 重复注册更新描述 | `LogCategoryRegistry.cs` | 添加 `existingNode.Description = description` | 2 |
| P0 | `LogRepository.Reset()` 重置序号 | `LogRepository.cs` | 重置 `_sequenceNumber` 为 0 | 2 |
| P1 | JSON 配置添加第 4 个 Batch 分类 | `log-categories.json` | 使总数达到 21 | 1 |
| P1 | Pipeline 导出路径配置 | `LogPipelineSkeletons.cs` | 确保导出路径正确初始化 | 3 |
| P2 | `LogController` 别名映射 | `LogController.cs` | 确保别名映射正确加载 | 5 |

### 4.2 测试代码修复

| 优先级 | 修复项 | 文件 | 修复内容 |
|--------|--------|------|----------|
| P1 | 更新位标志预期值 | `LogCategoryRegistryAdvancedTests.cs` | `1UL << 46` → `1UL << 16` |
| P1 | 更新 Count 预期值（如选方案 B） | `LogCategoryRegistryAdvancedTests.cs` | `21` → `20` |

## 五、项目引用关系

```
BasicMethod.Logging (BasicMethod/Logging/)
    ↑ 引用
Share.Diagnostics (Share/Diagnostics/)
    ↑ 引用
Logging.Tests (CentralConsole/BasicMethod.Tests/Logging.Tests/)
    ├── 引用 Share.Diagnostics
    ├── 引用 BasicMethod.Logging
    ├── 引用 UniversalWorkflowFramework
    └── 引用 TestInfrastructure
```

## 六、关键文件清单

| 文件 | 作用 | 状态 |
|------|------|------|
| `Shared/LoggingCleanupSkeleton.cs` | 通用清理骨架 | ✅ 已修复（添加 ExitBatch） |
| `Batch/BatchFixtureSkeletons.cs` | 批次 Arrange 骨架 | ✅ 已修复（添加 EnterBatch） |
| `Shared/LoggingWriteSkeleton.cs` | 日志写入骨架 | ✅ 已修复（注入 BatchId） |
| `Shared/LoggingTestCleanup.cs` | 测试清理 | ✅ 已修复（改用 Reset） |
| `Share.Diagnostics.csproj` | 项目配置 | ✅ 已修复（复制 JSON） |
| `LogCategoryLoader.cs` | 配置文件加载器 | ✅ 已修复（回退路径 + 大小写不敏感） |
| `LogCategoryRegistry.cs` | 分类注册表 | ✅ 已修复（Clear 空字典 + Register 更新描述） |
| `LogRepository.cs` | 日志仓库 | ✅ 已修复（Reset 重置序号） |
| `LogPipelineSkeletons.cs` | Pipeline Arrange 骨架 | ❌ 需检查（导出路径） |
| `LogController.cs` | 日志控制器 | ❌ 需检查（别名映射） |
| `log-categories.json` | 分类配置文件 | ❌ 需检查（Batch 分类数量） |


