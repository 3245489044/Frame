# AgentConfigUI.json 调用链 — 代码参考文档

> **版本：** v1.0
> **维护者：** WorkFlowPrj Team
> **更新日期：** 2026-07-11
> **目标：** 提供 `CallChain.md` 中引用的代码逻辑参考，包括关键数据结构定义、依赖关系图和配置索引。
> **关联文档：** `CallChain.md` — 6 条调用链的详细描述

---

## 目录

- [AgentConfigUI.json 调用链 — 代码参考文档](#agentconfiguijson-调用链--代码参考文档)
  - [目录](#目录)
  - [1. 关键数据结构](#1-关键数据结构)
    - [1.1 UIConfiguration](#11-uiconfiguration)
    - [1.2 EventBindingContext](#12-eventbindingcontext)
    - [1.3 EventBindingDeclaration](#13-eventbindingdeclaration)
    - [1.4 FileDrivenPipelineContext](#14-filedrivenpipelinecontext)
  - [2. 依赖关系图](#2-依赖关系图)
    - [2.1 项目依赖](#21-项目依赖)
    - [2.2 命名空间依赖](#22-命名空间依赖)
  - [3. 附录：AgentConfigUI.json 关键配置索引](#3-附录agentconfiguijson-关键配置索引)

---

## 1. 关键数据结构

### 1.1 UIConfiguration

```csharp
public class UIConfiguration
{
    string? Description;           // 配置描述
    string? InitCommandMode;       // InitCommands 执行模式 ("function" / "default")
    List<string>? InitCommands;    // 初始化命令列表
    FormConfig? Form;              // 窗口配置（标题、尺寸）
    List<ViewConfig>? Views;       // 视图列表
}

public class ViewConfig
{
    string? Name;                  // 视图名称
    string? Type;                  // 视图类型（Panel 等）
    List<ControlConfig>? Controls; // 控件列表
}

public class ControlConfig
{
    string? Name;                  // 控件名称
    string? Type;                  // 控件类型（button、textbox、treeview 等）
    string? Template;              // 模板名称
    string? Text;                  // 显示文本
    string? FunctionName;          // 绑定的函数名（简单绑定）
    List<ParameterBinding>? Parameters; // 参数绑定
    string? Output;                // 输出目标
    LayoutConfig? Layout;          // 布局配置
    EventConfig? Events;           // 事件配置
    List<ControlConfig>? Controls; // 子控件
}

public class EventConfig
{
    Dictionary<string, List<string>>? CustomEvents;  // 自定义事件
    BindingConfig? Bindings;                         // 绑定配置
}

public class BindingConfig
{
    List<ParameterBinding>? Parameters;              // 参数绑定
    string? Output;                                  // 输出目标
    Dictionary<string, string>? OutputMapping;       // 输出映射（按函数名）
    Dictionary<string, WorkflowDefinition>? Workflows; // 工作流定义
}
```

### 1.2 EventBindingContext

```csharp
public class EventBindingContext
{
    object TargetObject;                              // 目标对象（Form）
    UIConfiguration Config;                           // UI 配置
    bool IsScriptExecutionMode;                       // 脚本执行模式
    object? SearchRoot;                               // 控件查找根容器
    Func<string, object?>? CustomControlFinder;       // 自定义控件查找委托
    IEventBus? EventBus;                              // 事件总线

    // 当前处理状态
    ViewConfig? CurrentView;                          // 当前视图
    ControlConfig? CurrentControlConfig;              // 当前控件配置
    object? CurrentControl;                           // 当前真实控件
    EventBindingDeclaration? CurrentDeclaration;      // 当前声明
    List<ParameterBinding>? CurrentParameterBindings; // 当前参数绑定
    string? CurrentOutput;                            // 当前输出目标
    bool SkipCurrentControl;                          // 跳过当前控件

    // 工作流定义
    Dictionary<string, WorkflowDefinitionEntry>? WorkflowDefinitions;

    // 绑定结果
    List<ControlBindingInfo> BoundControls;           // 已绑定的控件列表
    int BoundCount;                                   // 绑定计数
    int SkippedCount;                                 // 跳过计数

    void ResetCurrent();                              // 重置当前状态
}
```

### 1.3 EventBindingDeclaration

```csharp
public class EventBindingDeclaration
{
    string ControlName;                               // 控件名称
    string EventName;                                 // 事件名称
    string FunctionName;                              // 函数名称
    List<ParameterBinding>? Parameters;               // 参数绑定
    string? OutputTarget;                             // 输出目标
    Dictionary<string, string>? OutputMapping;        // 输出映射
}
```

### 1.4 FileDrivenPipelineContext

```csharp
public class FileDrivenPipelineContext
{
    // 脚本加载
    string? ScriptPath;                               // 脚本路径
    string? ProjectRoot;                              // 项目根目录
    string[]? CommandLines;                           // 命令列表
    int TotalCommands;                                // 总命令数
    bool ScriptLoadSuccess;                           // 脚本加载成功
    string? ScriptLoadErrorMessage;                   // 加载错误信息

    // 命令解析
    bool ParseSuccess;                                // 解析成功
    ParsedCommand? ParsedCommand;                     // 解析后的命令
    string? ParsedAction;                             // 解析后的动作
    string? ParsedTarget;                             // 解析后的目标
    Dictionary<string, string>? ParsedParameters;     // 解析后的参数

    // 执行状态
    int CurrentIndex;                                 // 当前命令索引
    string? CurrentCommand;                           // 当前命令文本
    bool FunctionExecuteSuccess;                      // 函数执行成功
    string? FunctionResultValue;                      // 函数执行结果
    string? FunctionErrorMessage;                     // 函数错误信息

    // 绑定信息
    string? BoundFunctionName;                        // 绑定的函数名
    List<string>? BoundInputs;                        // 绑定的输入
    string? BoundOutputTarget;                        // 绑定的输出目标

    // 运行时依赖
    object? TargetForm;                               // 目标 Form
    IFunctionExecutor? FunctionExecutor;              // 函数执行器
    IEventBindingResolver? BindingResolver;           // 绑定解析器
    INonEventCommandLookup? NonEventCommandLookup;    // 非事件命令查找器
}
```

---

## 2. 依赖关系图

### 2.1 项目依赖

```
FileDriven.Launcher
  └─ FileDriven.Execution
       ├─ FileDriven.Pipeline
       │    ├─ FileDriven.Abstractions
       │    └─ FileDriven.Business
       ├─ FileDriven.UI
       │    ├─ WdUI (CsharpPrj/Operation/WdUI)
       │    │    ├─ UniversalMiddleware.Rendering
       │    │    └─ Share.EventBindingOrchestrator
       │    │         ├─ Modules.DiagnosticContext
       │    │         ├─ Modules.Transformers
       │    │         ├─ Modules.ConfigurationModels
       │    │         ├─ Modules.EventFramework
       │    │         ├─ Modules.Threading
       │    │         ├─ ExecutionFramework.Abstractions
       │    │         └─ TConfig
       │    └─ FrontEndDriven
       ├─ FileDriven.CLI
       ├─ FileDriven.JsonRefer
       ├─ Container.Abstractions
       ├─ BackEndDriven
       ├─ FunctionStorage
       └─ Modules.CentralPipeline
```

### 2.2 命名空间依赖

```
FileDriven.Launcher.Program
  → FileDriven.Execution.FileDrivenExecutor
    → FileDriven.Execution.Gui.Blocks.StartGuiModePipeline
      → FileDriven.UI.Core.MainForm
        → WdUI.Layout.WinFormsRenderPipeline
          → Share.EventBindingOrchestrator.Transformers.EventBindingPipelineRunner
            → Share.EventBindingOrchestrator.Transformers.GenericEventBindingPipeline
              → Share.EventBindingOrchestrator.Transformers.ControlFinderTransformer
              → Share.EventBindingOrchestrator.Transformers.BindingMetadataTransformer
              → Share.EventBindingOrchestrator.Parameters.EventBinderTransformer
                → Share.EventBindingOrchestrator.Parameters.ParameterBuilder
                → ExecutionFramework.Abstractions.IExecutionEngine
                  → TConfig (IFunctionExecutor, ExecutionResult)
```

---

## 3. 附录：AgentConfigUI.json 关键配置索引

| 配置项 | 值 | 说明 |
|--------|-----|------|
| `initCommandMode` | `"function"` | InitCommands 通过 FunctionExecutor 直接执行 |
| `initCommands` | `["InitWorkflowBrowser"]` | 初始化时执行 InitWorkflowBrowser 函数 |
| `form.title` | `"Agent 智能体控制面板"` | 窗口标题 |
| `form.width/height` | `900/750` | 窗口尺寸 |
| `views[0].name` | `"MainView"` | 主视图名称 |
| `executeButton.functionName` | `"ExecuteWorkflow"` | 执行按钮绑定的函数 |
| `executeButton.parameters` | `[workflowName, input, apiKey]` | 从三个控件读取参数 |
| `executeButton.output` | `"outputTextBox"` | 执行结果输出到 outputTextBox |
| `workflowTreeView.events.bindings.workflows` | `workflowTreeView_afterSelect` | 工作流定义（5 个节点） |
| `workflowTreeView.events.bindings.parameters` | `[workflowName]` | 从 workflowTreeView 读取选中项 |
| `workflowTreeView.events.bindings.output` | `"workflowDetailsPanel"` | 工作流详情输出到 detailsPanel |
| `inputTextBox.events.bindings.parameters` | `[inputText]` | 从自身读取输入文本 |
| `apiKeyTextBox.events.bindings.parameters` | `[apiKey]` | 从自身读取 API Key |
| `executeButton.events.bindings.parameters` | `[workflowName, input, apiKey]` | 从三个控件读取参数 |
| `executeButton.events.bindings.output` | `"outputTextBox"` | 执行结果输出到 outputTextBox |
| `executeButton.events.bindings.workflows` | `executeWorkflow` | 工作流定义（3 个节点） |
| `clearButton.functionName` | `"ClearOutput"` | 清除按钮绑定的函数 |
| `clearButton.output` | `"outputTextBox"` | 清除结果输出到 outputTextBox |
| `workflowTreeView_afterSelect.nodes` | `[5 个节点]` | 工作流节点：LoadWorkflowList → PopulateListView → GetFirstWorkflowName → LoadWorkflowDetails → LoadDefaultInput |
| `executeWorkflow.nodes` | `[3 个节点]` | 工作流节点：ValidateInput → ExecuteAgentWorkflow → DisplayResult |
