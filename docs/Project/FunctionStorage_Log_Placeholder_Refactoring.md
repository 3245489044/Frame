# FunctionStorage 日志 PlaceholderFramework 改造文档

## 概述

将 `Share/FunctionStorage` 中的所有日志调用从传统的 `Logger` 模式改造为基于 `PlaceholderFramework` 的统一日志框架。

## 改造模式

参考 `WdUI` 的改造方式（`WdUILogPlaceholderHandler` + `wdui_log_messages.json`），为 FunctionStorage 创建了：

1. **`fs_log_messages.json`** — 日志消息定义文件（~102 条）
2. **`FsLogPlaceholderHandler.cs`** — 日志处理器（实现 `IPlaceholderHandler` + 静态 `Log` 方法）

## 改造文件清单

| # | 文件 | 日志条数 | 状态 |
|---|------|---------|------|
| 1 | `UIToolbox.cs` | 8 | ✅ |
| 2 | `WorkflowExecutionLayer.cs` | 18 | ✅ |
| 3 | `WorkflowCommandCenter.cs` | 14 | ✅ |
| 4 | `WorkflowLibrary.cs` | 12 | ✅ |
| 5 | `WorkflowHelpers.cs` | 2 | ✅ |
| 6 | `UIConnector.cs` | 7 | ✅ |
| 7 | `UIConnectorEventBinder.cs` | 6 | ✅ |
| 8 | `DefaultInputDispatcher.cs` | 8 | ✅ |
| 9 | `CodePathLibrary.cs` | 8 | ✅ |
| 10 | `MathLibrary.cs` | 4 | ✅ |
| 11 | `ModelLibrary.cs` | 6 | ✅ |
| 12 | `WorkflowIOGroup.cs` | 4 | ✅ |
| 13 | `WorkflowSkeletons.cs` | 5 | ✅ |
| **合计** | **13 个文件** | **~102** | **✅ 全部完成** |

## 改造内容

### 删除的内容
- 所有 `private static readonly Logger _log = new(...)` 字段声明
- 所有 `_log.Info(...)`, `_log.Debug(...)`, `_log.Warn(...)`, `_log.Error(...)` 调用
- 所有 `using Common.Diagnostics;` 导入（在已改造文件中移除）

### 新增的内容
- `using Common.PlaceholderFramework.Handlers;` 导入
- 所有日志调用替换为 `FsLogPlaceholderHandler.Log("消息键", new { 参数 })` 模式
- 每个文件添加 `★ 日志已改造为基于 PlaceholderFramework ★` 注释标记

### 日志消息命名规范

```
{ClassName}_{MethodName}_{描述}
```

例如：
- `WorkflowExecutionLayer_LoadWorkflowDefinition_file_not_found`
- `WorkflowExecutionLayer_ExecuteWorkflow_start`
- `UIConnector_GetWorkflowNames`

### 日志级别映射

| 原始级别 | Placeholder 消息键后缀约定 |
|---------|------------------------|
| `_log.Info(...)` | `_completed`, `_start`, `_loaded` 等 |
| `_log.Debug(...)` | `_cache_hit`, `_cache_miss`, `_skip` 等 |
| `_log.Warn(...)` | `_not_found`, `_empty`, `_warn` 等 |
| `_log.Error(...)` | `_failed`, `_error` 等 |

## 新增文件

### 1. `Share/FunctionStorage/fs_log_messages.json`

日志消息定义文件，包含所有 ~102 条日志消息的元数据（消息键、级别、模板、描述）。

### 2. `Share/PlaceholderFramework/Handlers/FsLogPlaceholderHandler.cs`

日志处理器实现：
- 实现 `IPlaceholderHandler` 接口
- 提供静态 `Log(string messageKey, object? data = null)` 方法
- 内部使用 `PlaceholderResolverSkeleton` 解析和输出日志

## 使用示例

### 改造前
```csharp
private static readonly Logger _log = new(moduleName: "WorkflowExec")
{
    EnabledCategories = LogCategory.FunctionStorage_Workflow
};

_log.Info(LogCategory.FunctionStorage_Workflow, "开始执行工作流: {0}", workflowName);
_log.Error(LogCategory.FunctionStorage_Workflow, "执行失败: {0}", ex.Message);
```

### 改造后
```csharp
FsLogPlaceholderHandler.Log("WorkflowExecutionLayer_ExecuteWorkflow_start",
    new { workflowName, stepCount = def.Steps.Count });
FsLogPlaceholderHandler.Log("WorkflowExecutionLayer_ExecuteWorkflow_failed",
    new { message = ex.Message });
```

## 验证方法

1. 编译项目：`dotnet build Share/Share.csproj`
2. 运行测试：`dotnet test CentralConsole/Share.Tsets/PlaceholderFramework.Tests/`
3. 检查日志输出是否正常显示在控制台或日志文件中
