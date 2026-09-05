# BackEndDriven 调用 WorkflowMethod 调用链分析

> 分析日期：2026-07-05
> 分析范围：`CsharpPrj/Operation/BackEndDriven/` → `CsharpPrj/Operation/WorkFlowMethod/` → `CsharpPrj/Modules/ExecutionFramework/`

---

## 1. 调用链总览

```
命令字符串（如 "do click Button1"）
  │
  ▼
PipelineEntryInstance.RunCommandAsync()          ← Share/CommandFramework/Pipeline/PipelineEntryInstance
  │
  ├─ ParseCommandStage                            ← 解析命令为 {Action, Verb, Target, ...}
  │
  └─ ExecuteCommandStage                          ← 统一命令执行阶段（调度器）
       │
       ├─ action == "set"    → SetCommandHandler.HandleSet()
       ├─ action == "click"  → ClickCommandHandler.HandleClick()  ★ WorkflowMethod 路径
       └─ 其他               → NonEventCommandHandler.HandleNonEvent()
```

---

## 2. click 命令的 WorkflowMethod 调用链（核心路径）

### 2.1 完整调用链

```
ExecuteCommandStage.ExecuteCoreAsync()                    ← BackEndDriven/Pipeline/ExecuteCommandStage.cs
  │  action == "click"
  ▼
ClickCommandHandler.HandleClick()                         ← BackEndDriven/Pipeline/Stages/Handlers/ClickCommandHandler.cs
  │
  ├─ 1. 查找绑定：bindingFinder(target, eventType)
  │     返回 BindingResult { FunctionName, Parameter, Target }
  │
  └─ 2. HandleClickWithWorkflow()
       │
       ▼
       parameterExecutionWorkflow.ExecuteAsync(request)    ← IParameterExecutionWorkflow 接口
       │
       │  ★ 实际注入的实现：
       │     WorkflowMethodExecutionWorkflow
       │     （BackEndDriven/Invoke/WorkflowMethodExecutionWorkflow.cs）
       │
       ▼
WorkflowMethodExecutionWorkflow.ExecuteAsync()            ← BackEndDriven/Invoke/WorkflowMethodExecutionWorkflow.cs
  │
  ├─ 步骤1: WorkFlowMethodUsage.EnsureInitialized()
  │         确保 WorkFlowMethodUsage 已初始化
  │
  ├─ 步骤2: BuildWorkflowContextAsync(request)
  │         │
  │         ├─ GUI 模式：从 TargetForm 的真实控件读取值
  │         ├─ CLI 模式：从 VirtualStore 读取值
  │         └─ 创建 WfContext(parameters, data, executionId, nodeKey, workflowId)
  │             其中 parameters = {"控件名1": "值1", "控件名2": "值2", ...}
  │
  ├─ 步骤3: WorkFlowMethodUsage.ExecuteAsync(functionName, wfContext.Parameters)
  │         │
  │         │  ★ 直接调用 WorkFlowMethodUsage 的统一入口
  │         │     （Operation/WorkFlowMethod/WorkflowMethodUseage.cs）
  │         │
  │         ▼
  │      WorkFlowMethodUsage.ExecuteAsync(string, Dictionary<string, object>, EXContext?)
  │         │
  │         └─ _functionExecutor!.ExecuteAsync(functionName, namedParameters, context)
  │              │
  │              │  _functionExecutor 是 FunctionExecutor 实例
  │              │  （Modules/ExecutionFramework/Engine/FunctionExecutor.cs）
  │              │
  │              ▼
  │           FunctionExecutor.ExecuteAsync(string, Dictionary<string, object>, EXContext?)
  │              │
  │              ├─ _registry.TryGetConfig(functionName, out config)
  │              │    从注册表查找函数配置
  │              │
  │              ├─ ParameterTransformer.ForConfig(config).Transform(args)
  │              │    │
  │              │    └─ FillFromDict()  ← 按参数名匹配 Dictionary 键
  │              │         │
  │              │         ├─ 优先按参数名匹配（_paramNames[i]）
  │              │         ├─ 回退按位置索引匹配（"0", "1", ...）
  │              │         └─ 未匹配时使用默认值（输出警告日志）
  │              │
  │              └─ InvokeDelegateAsync(config, args2, ct)
  │                   │
  │                   └─ config.Invoke(args)  ← 实际调用注册的函数
  │
  └─ 步骤4: WriteResult() / WriteBackParameters()
            │
            ├─ 写回函数返回值到 OutputTarget
            └─ 写回修改后的参数值到控件/VirtualStore
```

### 2.2 关键文件索引

| 步骤 | 文件 | 命名空间 |
|------|------|---------|
| 命令入口 | `Share/CommandFramework/Pipeline/PipelineEntryInstance.cs` | `CommandFramework.Pipeline` |
| 解析阶段 | `Share/CommandFramework/Pipeline/Stages/ParseCommandStage.cs` | `CommandFramework.Pipeline.Stages` |
| 执行阶段 | `BackEndDriven/Pipeline/ExecuteCommandStage.cs` | `BackEndDriven.Pipeline` |
| click 处理 | `BackEndDriven/Pipeline/Stages/Handlers/ClickCommandHandler.cs` | `BackEndDriven.Pipeline.Stages.Handlers` |
| 工作流实现 | `BackEndDriven/Invoke/WorkflowMethodExecutionWorkflow.cs` | `BackEndDriven.Invoke` |
| 统一入口 | `Operation/WorkFlowMethod/WorkflowMethodUseage.cs` | `WorkFlowMethod.Usage` |
| 函数执行器 | `Modules/ExecutionFramework/Engine/FunctionExecutor.cs` | `ExecutionFramework.Engine` |
| 参数构建 | `BackEndDriven/Invoke/ArgumentBuilder.cs` | `BackEndDriven.Invoke` |

---

## 3. 关键注入点

### 3.1 IParameterExecutionWorkflow 注入

```
CommandFrameworkOptions.ParameterExecutionWorkflow
  │
  ├─ 由 BackEndDrivenBuilder.WithWorkflowMethodExecution() 设置
  │
  └─ 注入到 CommandFrameworkHost
       │
       └─ 传递给 PipelineEntryInstance
            │
            └─ 传递给 ExecuteCommandStage
                 │
                 └─ 传递给 ClickCommandHandler
```

### 3.2 WorkFlowMethodUsage 初始化

```
WorkFlowMethodUsage.Initialize()
  │
  ├─ _workflowRegistry = 工作流函数注册表（IWorkflowRegistry）
  │     ├─ 默认：Fef.Registry（FunctionExecutionFramework 的默认注册表）
  │     └─ 外部注入：通过 DefaultBasicRegistryProvider 共享 FunctionStorage 的注册表
  │
  ├─ _pipelineExecutor = new PipelineExecutor(_workflowRegistry)
  │
  └─ _functionExecutor = new FunctionExecutor(_workflowRegistry)
       │
       └─ _registry = _workflowRegistry（ITrConfigRegistry 接口）
```

### 3.3 FunctionExecutor 参数转换

```
FunctionExecutor.ExecuteAsync(string, Dictionary<string, object>, EXContext?)
  │
  ├─ ParameterTransformer.ForConfig(config).Transform(args)
  │    │
  │    └─ ConditionalTransformer 按输入类型分派：
  │         ├─ null        → FillNull()
  │         ├─ object[]    → FillFromArray()
  │         ├─ List<object>→ FillFromList()
  │         ├─ Dictionary  → FillFromDict()  ★ WorkflowMethod 路径
  │         └─ 其他        → FillDefault()
  │
  └─ FillFromDict() 逻辑：
       ├─ 优先按参数名匹配：dict[_paramNames[i]]
       ├─ 回退按位置索引：dict[i.ToString()]
       └─ 未匹配时使用默认值（输出 Debug 警告日志）
```

---

## 4. 数据流

### 4.1 请求数据流

```
命令字符串 "do click Button1"
  │
  ▼
ParseCommandStage 解析
  │  { Action="click", Verb=null, Target="Button1" }
  ▼
ClickCommandHandler 查找绑定
  │  bindingFinder("Button1", "click")
  │  → BindingResult { FunctionName="SomeFunc", Parameter="input1", Target="output1" }
  ▼
ParameterExecutionRequest 构建
  │  {
  │    FunctionName = "SomeFunc",
  │    InputControls = "input1",
  │    OutputTarget = "output1",
  │    IsGuiMode = true/false,
  │    TargetForm = formObj,
  │    VirtualStore = store
  │  }
  ▼
WorkflowMethodExecutionWorkflow.BuildWorkflowContextAsync()
  │  Parameters = { "input1": "从控件读取的值" }
  │  Data = { "FunctionName": "SomeFunc", "IsGuiMode": true/false }
  ▼
WorkFlowMethodUsage.ExecuteAsync("SomeFunc", Parameters)
  │  Dictionary<string, object> → { "input1": "值" }
  ▼
FunctionExecutor.ExecuteAsync
  │  ParameterTransformer.FillFromDict → 按参数名匹配
  │  config.Invoke(args) → 执行注册的函数
  ▼
函数返回值
```

### 4.2 响应数据流

```
函数返回值 result
  │
  ▼
WorkflowMethodExecutionWorkflow
  │
  ├─ WriteResult(request, OutputTarget, resultStr)
  │    ├─ GUI 模式：写入 TargetForm 的真实控件
  │    └─ CLI 模式：写入 VirtualStore[OutputTarget]
  │
  └─ WriteBackParameters(request, wfContext)
       ├─ 遍历 InputControls 中的控件名
       ├─ 跳过 OutputTarget（避免重复写入）
       └─ 将 Parameters 中修改的值写回控件/VirtualStore
```

---

## 5. 架构图

```
┌─────────────────────────────────────────────────────────────────┐
│                     命令管线（Pipeline）                          │
│  ┌─────────────────┐    ┌──────────────────────────────────┐    │
│  │ ParseCommandStage│    │       ExecuteCommandStage        │    │
│  │  解析命令字符串   │ →  │  ┌──────────────────────────┐  │    │
│  └─────────────────┘    │  │ ClickCommandHandler        │  │    │
│                          │  │  ┌─────────────────────┐  │  │    │
│                          │  │  │ HandleClickWithWorkflow│  │    │
│                          │  │  └─────────┬───────────┘  │  │    │
│                          │  └────────────┼──────────────┘  │    │
│                          └───────────────┼──────────────────┘    │
└──────────────────────────────────────────┼──────────────────────┘
                                           │ IParameterExecutionWorkflow
                                           ▼
┌─────────────────────────────────────────────────────────────────┐
│              WorkflowMethodExecutionWorkflow                     │
│              (BackEndDriven/Invoke/)                             │
│                                                                  │
│  1. EnsureInitialized()                                          │
│  2. BuildWorkflowContextAsync() → WfContext                      │
│  3. WorkFlowMethodUsage.ExecuteAsync(functionName, params)       │
│  4. WriteResult / WriteBackParameters                            │
└──────────────────────────┬──────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│              WorkFlowMethodUsage (Operation/WorkFlowMethod/)     │
│                                                                  │
│  ExecuteAsync(string, Dictionary<string, object>, EXContext?)    │
└──────────────────────────┬──────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│              FunctionExecutor (Modules/ExecutionFramework/)      │
│                                                                  │
│  ExecuteAsync(string, Dictionary, EXContext?)                    │
│    ├─ TryGetConfig → 查找函数配置                                │
│    ├─ ParameterTransformer.FillFromDict → 按参数名匹配           │
│    └─ config.Invoke(args) → 执行注册的函数                       │
└─────────────────────────────────────────────────────────────────┘
```

---

## 6. 历史变更记录

| 日期 | 变更 | 说明 |
|------|------|------|
| 2026-07-04 | 新增 `WorkflowMethodExecutionWorkflow` | 基于 WorkflowMethodUseage 的新实现，替代 ParameterExecutionWorkflow |
| 2026-07-04 | 新增 `ArgumentBuilder` 快速路径3 | 支持 Dictionary 命名参数匹配 |
| 2026-07-04 | `FunctionExecutor` 新增 Dictionary 重载 | `ExecuteAsync(string, Dictionary<string, object>, EXContext?)` |
| 2026-07-04 | `ParameterTransformer` 新增 `FillFromDict` | 按参数名匹配 Dictionary 键 |
| 2026-07-05 | 消除 `IEventExecutionService` 依赖 | 直接调用 `WorkFlowMethodUsage.ExecuteAsync()` |
| 2026-07-05 | 修复复杂类型序列化 | 返回值使用 JSON 序列化替代 ToString() |
| 2026-07-05 | 修复参数传出逻辑 | 跳过 OutputTarget 避免重复写入 |
| 2026-07-05 | 移除死代码 | 删除 param0/param1 索引键（实际回退键是 "0"/"1"） |
| 2026-07-05 | 重构 ExecuteCommandStage | 命令处理逻辑拆分到 Handlers/ 目录 |
| 2026-07-05 | 新增 BackEndDrivenBuilder | 标准注入入口，消除胶水代码缺失问题 |

---

## 7. 缺陷分析 & 修复记录

### 7.1 原始缺陷：WorkflowMethodExecutionWorkflow 未被实际使用

**问题描述：** `WorkflowMethodExecutionWorkflow` 的代码实现正确，但从未被实际调用。

**根因分析：**

```
CommandFrameworkOptions.ParameterExecutionWorkflow = ?  ← 从未赋值
```

1. `CommandFrameworkOptions.ParameterExecutionWorkflow` 属性在整个 BackEndDriven 项目内没有任何代码给它赋值
2. 注释中提到的 `BackEndDrivenBuilder.WithWorkflowMethodExecution()` 方法不存在（只在注释中被提及）
3. `ExecuteCommandStage` 构造函数要求 `IParameterExecutionWorkflow` 必须注入，但没有任何代码创建 `WorkflowMethodExecutionWorkflow` 实例并传进去

**影响范围：** click 命令的 WorkflowMethod 执行路径完全不可用。

### 7.2 修复方案

**方案：** 在 BackEndDriven 项目内部创建 `BackEndDrivenBuilder` 类，作为标准注入入口。

**新增文件：** `BackEndDriven/BackEndDrivenBuilder.cs`

```csharp
// 调用方只需一行代码即可完成注入
var host = CommandFrameworkHost.Create(options =>
{
    new BackEndDrivenBuilder(options)
        .WithWorkflowMethodExecution()
        .Apply();
});
```

**修复后的注入链路：**

```
BackEndDrivenBuilder.WithWorkflowMethodExecution()
  │
  └─ new WorkflowMethodExecutionWorkflow()
       │
       └─ 赋值给 CommandFrameworkOptions.ParameterExecutionWorkflow
            │
            └─ CommandFrameworkHost 读取 options.ParameterExecutionWorkflow
                 │
                 └─ 传递给 PipelineEntryInstance
                      │
                      └─ 传递给 ExecuteCommandStage
                           │
                           └─ 传递给 ClickCommandHandler
                                │
                                └─ HandleClickWithWorkflow() 调用 workflow.ExecuteAsync()
```

### 7.3 缺陷清单

| 问题 | 严重程度 | 状态 | 说明 |
|------|---------|------|------|
| 无代码实例化 `WorkflowMethodExecutionWorkflow` | P0 致命 | ✅ 已修复 | BackEndDrivenBuilder 提供标准注入入口 |
| `ParameterExecutionWorkflow` 属性从未赋值 | P0 致命 | ✅ 已修复 | WithWorkflowMethodExecution() 完成赋值 |
| `BackEndDrivenBuilder` 不存在 | P1 严重 | ✅ 已修复 | 新建 BackEndDrivenBuilder.cs |
| 构造函数 nullable 参数实际必填 | P2 中等 | ⏳ 待修复 | ExecuteCommandStage 构造函数参数语义 |
| 依赖传递链路不完整 | P2 中等 | ⏳ 待修复 | controlFinder/controlValueWriter 传递 |
