# Business → Framework 逻辑提取分析报告

## 概述

本文档分析 `Operation/FunctionStorage/Business` 中的代码，识别哪些逻辑应该作为通用逻辑提取到 `Operation/FunctionStorage/Framework` 中。

## 架构原则

- **Framework** — 通用函数注册框架，提供函数注册/执行/管理等基础设施，与具体业务无关
- **Business** — 业务注册逻辑，包含具体的业务函数注册和业务特有的编排逻辑

## 分析结果

### 一、已确认的重复代码（应立即消除）

#### 1. MathUtils.cs → 与 Share/Registry/AutoRegister/Wrappers/DelegateWrappers.cs 重复

**文件**: `Business/Registrations/MathUtils.cs`
**重复对象**: `Share/Registry/AutoRegister/Wrappers/DelegateWrappers.cs` 中的 `MathWrapper`

**重复内容**:
- `ParseMathParameters()` — 与 `MathWrapper.ParseMathParameters()` 几乎完全相同的参数解析逻辑
- `ConvertToType()` — 与 `MathWrapper.ConvertToType()` 完全相同的类型转换逻辑
- `FormatMathResult()` — 与 `MathWrapper.FormatMathResult()` 完全相同的格式化逻辑

**引用方**:
- `MathBrowserFunctions.cs` 通过 `using static MathUtils` 使用这三个方法
- `MathWrapper` 在 `AutoRegisterEngine.cs` 中被使用

**建议**: 
- 将 `MathUtils.cs` 中的三个方法改为委托到 `Share/Registry/AutoRegister/Wrappers/DelegateWrappers.cs` 中的 `MathWrapper`
- 保留本地实现作为反射失败时的回退

### 二、不应提取的业务逻辑（保留在 Business 中）

以下文件都是**具体的业务注册逻辑**，不应提取到 Framework：

#### 2. WorkflowIOGroup.cs — 工作流 IO 业务模型

**文件**: `Business/Registrations/WorkflowIOGroup.cs`
**内容**: `WorkflowIOGroup` 类和 `WorkflowIORegistry` 静态类

**分析**: 
- `WorkflowIOGroup` 是工作流 IO 映射的业务模型
- `WorkflowIORegistry` 管理工作流的 IO 组注册
- 虽然注册表模式是通用的，但 IO 组概念是工作流业务特有的
- 被 `WorkflowLibrary.RegisterIOGroup()` 使用

**结论**: ❌ 不应提取到 Framework，保留在 Business 中

#### 3. WorkflowCommandCenter.cs 中的结果类型定义

**文件**: `Business/Workflow/WorkflowCommandCenter.cs`
**内容**: `WorkflowSwitchResult` 和 `WorkflowExecResult` 类

**分析**: 
- 这两个类是工作流业务的结果模型
- 被 `UIConnector`、`UIConnectorEventBinder` 等多个 Business 文件使用
- 但它们是工作流业务特有的，不是通用框架类型

**结论**: ❌ 不应提取到 Framework，保留在 Business 中

#### 4. WorkflowExecutionLayer.cs — 工作流执行引擎

**文件**: `Business/Workflow/WorkflowExecutionLayer.cs`
**内容**: 
- `_workflowCache` 缓存管理
- `EnsureCachePopulated()` 缓存填充
- `LoadWorkflowDefinition()` JSON 加载
- `ExecuteWorkflowAsync()` / `ExecuteWorkflow()` 执行引擎
- `ConvertJsonElementToPrimitive()` 工具方法

**分析**: 
- 缓存管理是通用模式，但缓存的是工作流定义（业务模型）
- 执行引擎依赖 `ProjectDirectories.FileDriven_WorkFlowConfig` 业务配置路径
- `ConvertJsonElementToPrimitive` 是通用工具方法，但只有几行代码

**结论**: ❌ 不应提取到 Framework，保留在 Business 中

#### 5. WorkflowLibrary.cs — 工作流注册

**文件**: `Business/Registrations/WorkflowLibrary.cs`
**内容**: 
- `RegisterAll()` / `RegisterWorkflow()` 注册模式
- `ValidateStepFunctions()` 步骤验证
- `RegisterIOGroup()` IO 组注册
- `ReloadAll()` 重新加载

**分析**: 这是具体的工作流注册逻辑，依赖工作流业务模型

**结论**: ❌ 不应提取到 Framework，保留在 Business 中

#### 6. MathLibrary.cs — 纯业务注册

**文件**: `Business/Registrations/MathLibrary.cs`
**内容**: 注册 Add/Subtract/Multiply/Divide 四个基础数学函数

**结论**: ❌ 不应提取到 Framework，保留在 Business 中

#### 7. BuiltInUnitsLibrary.cs — 纯业务注册

**文件**: `Business/Registrations/BuiltInUnitsLibrary.cs`
**内容**: 注册 StringToInt/StringToDouble 等类型转换函数

**结论**: ❌ 不应提取到 Framework，保留在 Business 中

#### 8. MathBrowserFunctions.cs — 纯业务注册

**文件**: `Business/Registrations/MathBrowserFunctions.cs`
**内容**: 注册 Math_GetDomains/Math_Execute 等浏览器辅助函数

**结论**: ❌ 不应提取到 Framework，保留在 Business 中

#### 9. UIToolbox.cs — 纯业务注册

**文件**: `Business/Registrations/UIToolbox.cs`
**内容**: 注册 ClearOutput/ExecuteWorkflow 等 UI 操作函数

**结论**: ❌ 不应提取到 Framework，保留在 Business 中

#### 10. ModelLibrary.cs — 纯业务查询

**文件**: `Business/Registrations/ModelLibrary.cs`
**内容**: 工作流模型查询方法

**结论**: ❌ 不应提取到 Framework，保留在 Business 中

#### 11. UIConnector.cs / UIConnectorEventBinder.cs — UI 层桥接

**文件**: `Business/UI/UIConnector.cs`, `Business/UI/UIConnectorEventBinder.cs`
**内容**: UI 层与工作流引擎之间的桥接

**结论**: ❌ 不应提取到 Framework，保留在 Business 中

#### 12. WinFormsEventBinder.cs — WinForms 特有

**文件**: `Business/UI/WinFormsEventBinder.cs`
**内容**: WinForms 控件事件绑定

**结论**: ❌ 不应提取到 Framework，保留在 Business 中

#### 13. WorkflowSkeletons.cs — 骨架实现

**文件**: `Business/Workflow/WorkflowSkeletons.cs`
**内容**: `LoadWorkflowDetailsSkeleton` 骨架实现

**结论**: ❌ 不应提取到 Framework，保留在 Business 中

### 三、已在 Framework 中的通用逻辑

#### 14. DefaultInputDispatcher.cs

**文件**: `Framework/DefaultInputDispatcher.cs`
**状态**: ✅ 已在 Framework 中

**分析**: 已正确放置在 Framework 中，无需移动。

### 四、总结

| 优先级 | 文件 | 问题 | 处理方式 |
|--------|------|------|----------|
| 🔴 高 | `MathUtils.cs` | 与 `MathWrapper` 重复代码 | 委托到 `Share/Registry/AutoRegister/Wrappers/MathWrapper` |
| - | 其余所有 Business 文件 | 均为具体业务逻辑 | 保留在 Business 中，不应提取 |

### 核心结论

**`Operation/FunctionStorage/Business` 中的所有文件都是具体的业务注册逻辑，除了 `MathUtils.cs` 中的重复代码问题外，没有其他逻辑应该提取到 `Framework` 中。**

Framework 已经提供了完整的通用基础设施：
- `FunctionStorageRegistry` — 统一注册入口
- `Core.cs` — 执行上下文、解析命令、函数执行器
- `Abstractions.cs` — 接口定义
- `Utilities.cs` — 类型转换、重试策略、异步辅助
- `NonEventCommandCenter.cs` / `NonEventCommands.cs` — 非事件命令管理
- `UnifiedCommandRegistry.cs` — 统一命令注册
- `FunctionStorageAdapter.cs` — 适配器
- `DefaultInputDispatcher.cs` — 默认输入分发
- `FsLog.cs` — 日志适配器

Business 层正确地使用了这些基础设施来注册具体的业务函数，这是符合架构设计的。

## 实施状态

### ✅ 已完成（2026-06-14）

#### 第一阶段：消除 MathUtils 重复代码

1. ✅ `MathUtils.cs` — 已改为委托到 `MathWrapper`（通过反射调用私有方法，保留本地实现作为回退）
2. ✅ `MathBrowserFunctions.cs` — 引用不受影响（`using static MathUtils` 仍然有效）

#### 第二阶段：回滚错误提取

1. ✅ 删除 `Framework/WorkflowResults.cs` — 工作流结果类型是业务模型，不应在 Framework 中
2. ✅ 删除 `Framework/WorkflowIOGroup.cs` — 工作流 IO 组是业务模型，不应在 Framework 中
3. ✅ 恢复 `Business/Workflow/WorkflowCommandCenter.cs` — 恢复 `WorkflowSwitchResult` 和 `WorkflowExecResult` 定义
4. ✅ 恢复 `Business/Registrations/WorkflowIOGroup.cs` — 恢复 `WorkflowIOGroup` 和 `WorkflowIORegistry` 定义
