# FileDriven.Business 通用框架提取分析报告

> 分析日期：2026-06-16
> 分析范围：FileDriven.Business 全部 13 个源文件
> 分析目标：识别哪些逻辑应作为通用框架提取到 Share/Operation 层

---

## 一、当前架构概览

FileDriven.Business 位于四层架构的最上层（FileDriven 层），职责是：
1. **注册**：将函数注册到 FunctionStorageRegistry
2. **UI 绑定**：将 WinForms 控件事件绑定到注册的函数
3. **工作流执行**：加载和执行 JSON 驱动的工作流

### 文件清单

| 目录 | 文件 | 行数 | 职责 |
|------|------|------|------|
| Registrations/ | MathLibrary.cs | 43 | 注册基础数学函数 |
| Registrations/ | BuiltInUnitsLibrary.cs | 108 | 注册类型转换函数 |
| Registrations/ | MathBrowserFunctions.cs | 160 | 注册数学浏览器辅助函数 |
| Registrations/ | MathUtils.cs | 102 | 数学参数解析/格式化工具 |
| Registrations/ | UIToolbox.cs | 138 | 注册 UI 操作函数 |
| Registrations/ | WorkflowLibrary.cs | 213 | 注册工作流为函数 |
| Registrations/ | WorkflowIOGroup.cs | 115 | 工作流 IO 组管理 |
| Registrations/ | ModelLibrary.cs | 59 | 工作流模型查询 |
| UI/ | WinFormsEventBinder.cs | 670 | WinForms 事件绑定 |
| UI/ | UIConnector.cs | 56 | UI 层工作流桥接 |
| UI/ | UIConnectorEventBinder.cs | 78 | UI 事件绑定辅助 |
| Workflow/ | WorkflowExecutionLayer.cs | 424 | 工作流加载/执行 |
| Workflow/ | WorkflowCommandCenter.cs | 213 | 工作流命令中心 |
| Workflow/ | WorkflowSkeletons.cs | 102 | 工作流详情骨架 |
| 根目录 | FileDrivenLifecycle.cs | 76 | 生命周期管理 |

---

## 二、逐文件分析：哪些逻辑应提取为通用框架

### 2.1 MathUtils.cs — 参数解析/类型转换/结果格式化

**当前状态**：`FileDriven.Business\Registrations\MathUtils.cs`

包含三个通用工具方法：
- `ParseMathParameters(string, IReadOnlyList<IParameterDescriptor>)` — 参数字符串解析
- `ConvertToType(string, Type)` — 字符串到目标类型转换
- `FormatMathResult(object?)` — 结果格式化

**分析**：这些方法**不依赖** FileDriven 层的任何类型，完全是通用工具逻辑。

**建议**：**提取到 Share/CommandFramework/Execution/**
- 理由：`ConvertToType` 与 `Share/CommandFramework/Execution/TypeConversionHelper.cs` 中的逻辑重复
- `FormatMathResult` 与 `Share/CommandFramework/Execution/ResultFormatter.cs` 中的逻辑重复
- 这些是命令框架执行管线的基础能力，不应放在 FileDriven.Business

**但注意**：代码注释已说明 `MathUtils` 与 `MathWrapper` 中的实现逻辑相同但参数类型不同，**不建议强行合并**，而是：
1. 将 `ConvertToType` 和 `FormatMathResult` 提取到 `Share/CommandFramework/Execution/` 作为公共工具
2. `MathUtils` 改为委托调用公共工具，保持向后兼容

### 2.2 WorkflowIOGroup.cs — IO 组注册表

**当前状态**：`FileDriven.Business\Registrations\WorkflowIOGroup.cs`

包含：
- `WorkflowIOGroup` — IO 组模型类
- `WorkflowIORegistry` — 静态字典注册表

**分析**：
- `WorkflowIOGroup` 是纯数据模型，**不依赖** FileDriven 层
- `WorkflowIORegistry` 是简单的 `Dictionary<string, T>` 线程安全包装

**建议**：**提取到 Share/Registry/**
- `WorkflowIORegistry` 的模式（线程安全字典 + 注册/获取）与 `Share/Registry/` 中的其他 Registry 模式重复
- 可提取为泛型 `RegistryBase<T>` 基类，消除多个 Registry 类的重复代码

### 2.3 WinFormsEventBinder.cs — 事件绑定核心

**当前状态**：`FileDriven.Business\UI\WinFormsEventBinder.cs`（670 行，最大文件）

包含：
- `BindControlEvents(Control, string, Delegate, ...)` — 绑定单个事件
- `BindControlEvents(Control, EventConfig)` — 从配置绑定事件
- `FireAndForgetTriggerAsync(...)` — 事件触发执行管线
- `FindControl(string)` — 跨 Form 递归查找控件
- `WriteResultToControl(Control, string)` — 写回结果到控件
- `SplitEscaped` — 转义逗号分割

**分析**：
- **事件绑定逻辑**（`BindSingleEvent`、`BindAndSaveEvent`）与 `Operation/EventTriggerFramework/WinForms/WinFormsEventBinder.cs` 中的 `WinFormsEventBinder` 高度重叠
- **控件查找逻辑**（`FindControl`、`FindControlRecursive`）与 `Share/FrameworkAbstractions/Controls/IControlFinder.cs` 接口定义的能力重复
- **结果写入逻辑**（`WriteResultToControl`）与 `Operation/WdUI/Adapters/WdUIControlValueWriterAdapter.cs` 中的 `IControlValueWriter` 实现重复

**建议**：
1. **控件查找** → 已有 `IControlFinder` 接口在 `Share/CommandFramework/Abstractions/`，应实现该接口而非自写查找逻辑
2. **结果写入** → 已有 `IControlValueWriter` 接口，应通过 DI 获取而非自写 `WriteResultToControl`
3. **事件绑定** → `Operation/EventTriggerFramework/WinForms/WinFormsEventBinder.cs` 已提供通用实现，FileDriven 版本应委托给它

**关键发现**：`WinFormsEventBinder.cs` 中的 `FindControl`、`WriteResultToControl`、`SplitEscaped` 三个方法在 `Operation/EventTriggerFramework/WinForms/WinFormsEventBinder.cs` 和 `Operation/WdUI/Adapters/` 中都有类似实现，存在**三处重复**。

### 2.4 WorkflowExecutionLayer.cs — 工作流加载/执行

**当前状态**：`FileDriven.Business\Workflow\WorkflowExecutionLayer.cs`（424 行）

包含：
- `EnsureCachePopulated()` — 双重检查锁定的缓存填充
- `LoadWorkflowDefinition(string)` — JSON 文件加载
- `GetWorkflow(string)` / `GetAllWorkflowNames()` — 缓存查询
- `ExecuteStepsCore(...)` — 步骤执行核心
- `BuildWorkflowOutput(...)` — 输出构建
- `ConvertJsonElementToPrimitive(JsonElement)` — JSON 类型转换

**分析**：
- **缓存模式**（双重检查锁定 + 快照）是通用模式，与 `Operation/FunctionStorage/Framework/FunctionStorageRegistry.cs` 中的缓存模式重复
- **JSON 文件加载**是通用能力，与 `FileDriven/FileDriven.JsonRefer/` 中的 JSON 加载逻辑重复
- **步骤执行核心**是工作流引擎的核心，但已注释说明"已重构：执行引擎统一基于 WorkFlowEngine"

**建议**：
1. **缓存模式** → 提取为 `CacheBase<TKey, TValue>` 泛型基类到 `Share/`
2. **JSON 文件加载** → 已有 `FileDriven/FileDriven.Utilities/` 中的通用 JSON 加载工具
3. **ConvertJsonElementToPrimitive** → 这是通用 JSON 工具方法，应提取到 `Share/`

### 2.5 WorkflowCommandCenter.cs — 命令中心

**当前状态**：`FileDriven.Business\Workflow\WorkflowCommandCenter.cs`

包含：
- `HandleWorkflowSwitch(string)` — 工作流切换双事件
- `ExecuteWorkflow(string, string)` — 执行工作流（带异常包装）
- `WorkflowSwitchResult` / `WorkflowExecResult` — 结果类型

**分析**：
- `WorkflowSwitchResult` 和 `WorkflowExecResult` 是纯数据模型
- `HandleWorkflowSwitch` 是 FileDriven 特有的业务逻辑（双事件序列）
- `ExecuteWorkflow` 是简单的 try-catch 包装

**建议**：
- **结果类型** → 可提取到 `Share/Registry/` 或 `Share/Models/`
- **业务逻辑** → 保持原位，这是 FileDriven 层的特有职责

### 2.6 各 Library 注册类

**当前状态**：`MathLibrary.cs`、`BuiltInUnitsLibrary.cs`、`MathBrowserFunctions.cs`、`UIToolbox.cs`、`WorkflowLibrary.cs`、`ModelLibrary.cs`

**分析**：这些类本质上是**函数注册脚本**，将具体函数注册到 `IFunctionRegistry`。它们：
- 依赖 `IFunctionRegistry` 接口（在 `Share/Registry/`）
- 依赖 `FsLog` 日志（在 `Common/`）
- 注册的函数是 FileDriven 层特有的业务逻辑

**建议**：**保持原位**。这些是 FileDriven 层的业务注册代码，提取到下层会导致下层依赖上层的不合理架构。

---

## 三、总结：应提取 vs 不应提取

### ✅ 应提取为通用框架的逻辑

| 逻辑 | 当前位置 | 目标位置 | 优先级 | 理由 |
|------|---------|---------|--------|------|
| `ConvertToType(string, Type)` | MathUtils.cs | Share/CommandFramework/Execution/ | P1 | 与 TypeConversionHelper 重复 |
| `FormatMathResult(object?)` | MathUtils.cs | Share/CommandFramework/Execution/ | P1 | 与 ResultFormatter 重复 |
| `WorkflowIORegistry` 模式 | WorkflowIOGroup.cs | Share/Registry/ 泛型基类 | P2 | 多个 Registry 重复相同模式 |
| `WorkflowSwitchResult` / `WorkflowExecResult` | WorkflowCommandCenter.cs | Share/Models/ | P2 | 纯数据模型，不依赖 FileDriven |
| `ConvertJsonElementToPrimitive` | WorkflowExecutionLayer.cs | Share/ 通用 JSON 工具 | P1 | 通用 JSON 处理能力 |
| 双重检查锁定缓存模式 | WorkflowExecutionLayer.cs | Share/ CacheBase<T> | P2 | 与 FunctionStorageRegistry 缓存模式重复 |

### ❌ 不应提取的逻辑

| 逻辑 | 理由 |
|------|------|
| 各 Library 的 RegisterAll 方法 | FileDriven 层特有的业务注册，提取会导致下层依赖上层 |
| HandleWorkflowSwitch 双事件 | FileDriven 特有的业务编排逻辑 |
| WinFormsEventBinder 的事件绑定 | 应**委托**给 EventTriggerFramework 而非提取，已有通用实现 |
| WorkflowExecutionLayer 的工作流执行 | 已重构为基于 WorkFlowEngine，保持原位 |

### ⚠️ 应消除重复而非提取的逻辑

| 重复点 | 涉及文件 | 建议方案 |
|--------|---------|---------|
| 控件查找 (FindControl) | WinFormsEventBinder.cs + EventTriggerFramework + WdUI | 实现 IControlFinder 接口，三处统一使用 |
| 控件值写入 (WriteResultToControl) | WinFormsEventBinder.cs + WdUIControlValueWriterAdapter | 通过 DI 获取 IControlValueWriter，消除自写逻辑 |
| 事件绑定 (BindSingleEvent) | WinFormsEventBinder.cs + EventTriggerFramework.WinFormsEventBinder | 委托给 EventTriggerFramework 的通用实现 |

---

## 四、建议的提取顺序

1. **P1（立即）**：`ConvertToType` + `FormatMathResult` → 合并到 Share/CommandFramework/Execution/
2. **P1（立即）**：`ConvertJsonElementToPrimitive` → 提取到 Share/ 通用 JSON 工具
3. **P1（立即）**：WinFormsEventBinder 中的 `FindControl` → 实现 IControlFinder 接口
4. **P2（短期）**：`WorkflowIORegistry` → 提取泛型 RegistryBase<T>
5. **P2（短期）**：`WorkflowSwitchResult` / `WorkflowExecResult` → 提取到 Share/Models/
6. **P2（短期）**：双重检查锁定缓存模式 → 提取 CacheBase<TKey, TValue>
