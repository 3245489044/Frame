# CommandFramework 参数执行框架反转方案

> **文档日期**：2026-07-04  
> **核心目标**：将 `ExecuteCommandStage.HandleClick()` 中"从控件读值 → 参数构建 → 执行函数 → 写回结果"的内联实现，通过框架反转（IoC）改为接受 `BackEndDriven` 注入的工作流实现  
> **后续方向**：`BackEndDriven` 中的工作流实现可逐步替换为 WorkflowExecution 的工作流节点

---

## 一、现状分析

### 1.1 当前调用链

```
ExecuteCommandStage.HandleClick()  ← 在 Share/CommandFramework/Invoke/ExecuteCommandStage.cs
  │
  ├── ① 查找绑定（第 229-241 行）
  │     FindBinding(target, eventType) → BindingResult { FunctionName, Parameter, Target }
  │
  ├── ② 从控件读取值 + 参数构建（第 265-298 行）  ← 内联实现
  │     ├── 创建参数源（GuiControlSource / VirtualStoreSource）
  │     ├── 解析控件名 → 读取原始值 → string?[]
  │     └── 传入 FunctionExecutor.ExecuteAsync()
  │
  ├── ③ 执行函数（第 300 行）  ← 内联调用
  │     executor.ExecuteAsync(functionName, parameters)
  │
  └── ④ 写回结果（第 310-313 行）  ← 内联实现
        WriteResult(context, binding.Value.Target, result.Value?.ToString())
```

### 1.2 问题

步骤 ②③④ 是**参数执行**的核心逻辑，当前在 `Share/CommandFramework/Invoke/` 中内联实现。后续需要将这些逻辑改为使用工作流（WorkflowExecution），但 Share 层不应直接依赖 Operation 层的工作流实现。

---

## 二、框架反转方案

### 2.1 核心思路

**不迁移文件**，而是通过接口定义调用契约：

```
当前：Share 层内联实现参数执行逻辑
       ↓
目标：Share 层定义接口，BackEndDriven 提供实现
       ↓
未来：BackEndDriven 的实现改为工作流节点
```

### 2.2 架构变化

```
┌──────────────────────────────────────────────────────────────┐
│                    Share/CommandFramework（外壳）              │
│                                                              │
│  ExecuteCommandStage.HandleClick()                           │
│    ├── ① 查找绑定（保留）                                     │
│    └── ②③④ 委托给 IParameterExecutionWorkflow（新增接口）     │
│                                                              │
│  Abstractions/                                               │
│    ├── IParameterExecutionWorkflow  ← 新增反转接口            │
│    ├── ParameterExecutionRequest    ← 新增请求模型            │
│    └── ParameterExecutionResult     ← 新增结果模型            │
└──────────────────────────────────────────────────────────────┘
                            │ 依赖接口（不依赖实现）
                            ▼
┌──────────────────────────────────────────────────────────────┐
│              Operation/BackEndDriven（内核）                   │
│                                                              │
│  Invoke/                                                     │
│    └── ParameterExecutionWorkflow  ← 实现反转接口             │
│          ├── 当前：内联实现（从 ExecuteCommandStage 提取）     │
│          └── 未来：改为工作流节点                              │
│                                                              │
│  BackEndDrivenBuilder                                        │
│    └── WithParameterExecutionWorkflow()  ← 注册入口           │
└──────────────────────────────────────────────────────────────┘
```

---

## 三、接口定义

### 3.1 反转接口

在 `Share/CommandFramework/Abstractions/` 中新增：

```csharp
// ============================================================
// IParameterExecutionWorkflow.cs — 参数执行工作流接口
//
// ★ 框架反转接口：Share 层定义契约，BackEndDriven 提供实现
//   后续 BackEndDriven 的实现可替换为 WorkflowExecution 的工作流节点
// ============================================================

namespace CommandFramework.Abstractions;

/// <summary>
/// 参数执行工作流接口
/// <para>
/// 封装 click 命令中"从控件读值 → 参数构建 → 执行函数 → 写回结果"的完整流程。
/// Share 层只依赖此接口，不依赖具体实现。
/// BackEndDriven 提供实现，后续可替换为工作流节点。
/// </para>
/// </summary>
public interface IParameterExecutionWorkflow
{
    /// <summary>
    /// 执行参数执行工作流
    /// </summary>
    /// <param name="request">执行请求（包含绑定信息和上下文）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>执行结果</returns>
    Task<ParameterExecutionResult> ExecuteAsync(
        ParameterExecutionRequest request,
        CancellationToken cancellationToken = default);
}
```

### 3.2 请求模型

```csharp
// ============================================================
// ParameterExecutionRequest.cs — 参数执行请求
// ============================================================

namespace CommandFramework.Abstractions;

/// <summary>
/// 参数执行请求
/// <para>封装 ExecuteCommandStage.HandleClick 传递给工作流的所有上下文信息。</para>
/// </summary>
public class ParameterExecutionRequest
{
    /// <summary>要执行的函数名称（来自绑定）</summary>
    public required string FunctionName { get; init; }

    /// <summary>输入控件名（来自绑定，逗号分隔，支持转义）</summary>
    public string? InputControls { get; init; }

    /// <summary>输出目标控件名（来自绑定，用于写回结果）</summary>
    public string? OutputTarget { get; init; }

    /// <summary>是否为 GUI 模式</summary>
    public bool IsGuiMode { get; init; }

    /// <summary>目标窗体对象（GUI 模式）</summary>
    public object? TargetForm { get; init; }

    /// <summary>虚拟存储（CLI 模式）</summary>
    public Dictionary<string, string?>? VirtualStore { get; init; }

    /// <summary>控件查找器（GUI 模式）</summary>
    public IControlFinder? ControlFinder { get; init; }

    /// <summary>控件值读写器（GUI 模式）</summary>
    public IControlValueWriter? ControlValueWriter { get; init; }
}
```

### 3.3 结果模型

```csharp
// ============================================================
// ParameterExecutionResult.cs — 参数执行结果
// ============================================================

namespace CommandFramework.Abstractions;

/// <summary>
/// 参数执行结果
/// </summary>
public class ParameterExecutionResult
{
    /// <summary>是否执行成功</summary>
    public bool Success { get; init; }

    /// <summary>函数执行返回值</summary>
    public object? Value { get; init; }

    /// <summary>执行耗时</summary>
    public TimeSpan ExecutionTime { get; init; }

    /// <summary>错误信息（失败时）</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>创建成功结果</summary>
    public static ParameterExecutionResult Ok(object? value, TimeSpan executionTime)
        => new() { Success = true, Value = value, ExecutionTime = executionTime };

    /// <summary>创建失败结果</summary>
    public static ParameterExecutionResult Fail(string errorMessage)
        => new() { Success = false, ErrorMessage = errorMessage };
}
```

---

## 四、Share 层改动

### 4.1 ExecuteCommandStage 改动

修改 `HandleClick()` 方法，将步骤 ②③④ 委托给 `IParameterExecutionWorkflow`：

```csharp
// 在 ExecuteCommandStage 中新增字段
private readonly IParameterExecutionWorkflow? _parameterExecutionWorkflow;

// 在构造函数中新增参数
public ExecuteCommandStage(
    IFunctionExecutor? functionExecutor = null,
    Func<string, string, BindingResult?>? bindingFinder = null,
    INonEventCommandLookup? nonEventCommandLookup = null,
    IControlFinder? controlFinder = null,
    IControlValueWriter? controlValueWriter = null,
    IParameterExecutionWorkflow? parameterExecutionWorkflow = null)  // ← 新增
{
    // ... 原有初始化 ...
    _parameterExecutionWorkflow = parameterExecutionWorkflow;
}

// HandleClick 中步骤 ②③④ 替换为：
private async Task HandleClick(CommandContext context, string? target, CancellationToken cancellationToken)
{
    // ... ① 查找绑定（保持不变）...

    // ★ 框架反转：委托给 IParameterExecutionWorkflow
    if (_parameterExecutionWorkflow != null)
    {
        var request = new ParameterExecutionRequest
        {
            FunctionName = binding.Value.FunctionName,
            InputControls = binding.Value.Parameter,
            OutputTarget = binding.Value.Target,
            IsGuiMode = context.IsGuiMode,
            TargetForm = context.SharedData.TryGetValue("TargetForm", out var f) ? f : null,
            VirtualStore = context.VirtualStore,
            ControlFinder = _controlFinder,
            ControlValueWriter = _controlValueWriter
        };

        var result = await _parameterExecutionWorkflow.ExecuteAsync(request, cancellationToken);

        if (result.Success)
        {
            context.FunctionResultValue = result.Value;
            context.FunctionExecutionTime = result.ExecutionTime;
            context.SetStageResult(Name, true);
        }
        else
        {
            context.SetStageResult(Name, false, result.ErrorMessage);
        }
        return;
    }

    // ★ 回退路径：当未注入 IParameterExecutionWorkflow 时，使用原有内联逻辑
    // （保留原有代码作为回退，确保向后兼容）
    // ... 原有步骤 ②③④ 代码 ...
}
```

### 4.2 PipelineEntry 改动

在 `CreateDefaultPipeline()` 中透传 `IParameterExecutionWorkflow`：

```csharp
public static BP.IStagePipeline<CommandContext> CreateDefaultPipeline(
    // ... 原有参数 ...
    IParameterExecutionWorkflow? parameterExecutionWorkflow = null,  // ← 新增
    params BP.IPipelineStage<CommandContext>[] additionalStages)
{
    // ...
    var executeStage = new ExecuteCommandStage(
        functionExecutor: functionExecutor,
        bindingFinder: bindingFinder,
        nonEventCommandLookup: nonEventCommandLookup,
        controlFinder: controlFinder,
        controlValueWriter: controlValueWriter,
        parameterExecutionWorkflow: parameterExecutionWorkflow);  // ← 新增
    // ...
}
```

---

## 五、BackEndDriven 层改动

### 5.1 新增 Invoke 目录

```
CsharpPrj/Operation/BackEndDriven/
├── Invoke/                                    ← 新增目录
│   ├── ParameterExecutionWorkflow.cs          ← 工作流实现
│   └── README.md                              ← 说明文档
├── Execution/                                 ← 已有
├── Pipeline/                                  ← 已有
└── ...
```

### 5.2 ParameterExecutionWorkflow 实现

```csharp
// ============================================================
// ParameterExecutionWorkflow.cs — 参数执行工作流实现
//
// ★ 实现 IParameterExecutionWorkflow 接口
//   当前为内联实现（从 ExecuteCommandStage.HandleClick 提取），
//   后续可替换为 WorkflowExecution 的工作流节点。
//
// 当前流程（4 步）：
//   1. ReadControlValues — 从控件读取原始值
//   2. BuildArguments — 参数构建 + 类型转换
//   3. ExecuteFunction — 执行函数
//   4. WriteResult — 写回结果
// ============================================================

using CommandFramework.Abstractions;
using CommandFramework.Parameters;
using ParameterResolver.Sources;
using TConfig;

namespace BackEndDriven.Invoke;

/// <summary>
/// 参数执行工作流实现
/// <para>
/// 实现 <see cref="IParameterExecutionWorkflow"/> 接口，
/// 封装"从控件读值 → 参数构建 → 执行函数 → 写回结果"的完整流程。
/// </para>
/// <para>
/// ★ 当前为内联实现，后续可替换为 WorkflowExecution 的工作流节点：
///   Workflow.StartWith<ReadControlValue>()
///          .Then<BuildArguments>()
///          .Then<ExecuteFunction>()
///          .Then<WriteResult>()
/// </para>
/// </summary>
public class ParameterExecutionWorkflow : IParameterExecutionWorkflow
{
    private readonly IFunctionExecutor _functionExecutor;

    /// <summary>
    /// 创建参数执行工作流
    /// </summary>
    /// <param name="functionExecutor">函数执行器（必选）</param>
    public ParameterExecutionWorkflow(IFunctionExecutor functionExecutor)
    {
        _functionExecutor = functionExecutor ?? throw new ArgumentNullException(nameof(functionExecutor));
    }

    /// <summary>
    /// 执行参数执行工作流
    /// <para>流程：读值 → 构建参数 → 执行函数 → 写回结果</para>
    /// </summary>
    public async Task<ParameterExecutionResult> ExecuteAsync(
        ParameterExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 步骤 1：从控件读取原始值
            var rawValues = await ReadControlValuesAsync(request);

            // 步骤 2：执行函数（内部完成参数构建 + 类型转换）
            var result = await _functionExecutor.ExecuteAsync(
                request.FunctionName, rawValues);

            if (!result.IsSuccess)
            {
                return ParameterExecutionResult.Fail(
                    $"Function '{request.FunctionName}' execution failed: {result.ErrorMessage}");
            }

            // 步骤 3：写回结果
            if (!string.IsNullOrEmpty(request.OutputTarget) && result.Value != null)
            {
                WriteResult(request, request.OutputTarget, result.Value.ToString());
            }

            return ParameterExecutionResult.Ok(result.Value, result.ExecutionTime);
        }
        catch (Exception ex)
        {
            return ParameterExecutionResult.Fail(
                $"Parameter execution workflow error: {ex.Message}");
        }
    }

    /// <summary>
    /// 从控件读取原始值
    /// <para>GUI 模式：从 TargetForm 的真实控件读取</para>
    /// <para>CLI 模式：从 VirtualStore 读取</para>
    /// </summary>
    private static Task<string?[]> ReadControlValuesAsync(ParameterExecutionRequest request)
    {
        if (string.IsNullOrEmpty(request.InputControls))
            return Task.FromResult(Array.Empty<string?>());

        // 创建参数源
        IParameterSource source;
        if (request.IsGuiMode && request.TargetForm != null)
        {
            source = new GuiControlSource(request.TargetForm);
        }
        else if (!request.IsGuiMode && request.VirtualStore != null)
        {
            source = new VirtualStoreSource(
                new Dictionary<string, string?>(request.VirtualStore));
        }
        else
        {
            source = new VirtualStoreSource(new Dictionary<string, string?>());
        }

        // 解析控件名并读取值
        var controlNames = StringUtils.SplitEscapedToArray(request.InputControls);
        var rawValues = new string?[controlNames.Length];
        for (int i = 0; i < controlNames.Length; i++)
        {
            rawValues[i] = source.ReadValue(controlNames[i]);
        }

        return Task.FromResult(rawValues);
    }

    /// <summary>
    /// 写回结果到控件
    /// <para>GUI 模式：写入 TargetForm 的真实控件</para>
    /// <para>CLI 模式：写入 VirtualStore</para>
    /// </summary>
    private static void WriteResult(ParameterExecutionRequest request, string outputTarget, string? value)
    {
        if (request.IsGuiMode && request.TargetForm != null)
        {
            var finder = request.ControlFinder;
            var writer = request.ControlValueWriter;

            if (finder == null || writer == null)
                return;

            var control = finder.FindByName(request.TargetForm, outputTarget);
            if (control != null)
            {
                writer.WriteValue(control, value);
            }
        }
        else if (!request.IsGuiMode && request.VirtualStore != null)
        {
            request.VirtualStore[outputTarget] = value;
        }
    }
}
```

### 5.3 BackEndDrivenBuilder 扩展

```csharp
// 在 BackEndDrivenBuilder.cs 中新增

/// <summary>
/// 设置参数执行工作流实现
/// <para>注册到 AppServiceProvider，供 Share 层 ExecuteCommandStage 使用</para>
/// </summary>
public BackEndDrivenBuilder WithParameterExecutionWorkflow(
    IParameterExecutionWorkflow workflow)
{
    FrameworkAbstractions.ComponentRegistry.AppServiceProvider
        .Register<IParameterExecutionWorkflow>(workflow);
    return this;
}
```

---

## 六、注册与使用

### 6.1 CLI 模式注册

```csharp
// 在 CLI 启动入口中
var workflow = new ParameterExecutionWorkflow(functionExecutor);
builder.WithParameterExecutionWorkflow(workflow);

// 创建管线时传入
var pipeline = PipelineEntry.CreateDefaultPipeline(
    parser: parser,
    parameterExecutionWorkflow: workflow);
```

### 6.2 GUI 模式注册

```csharp
// 在 GUI 初始化中
var workflow = new ParameterExecutionWorkflow(functionExecutor);
builder.WithParameterExecutionWorkflow(workflow);
```

### 6.3 ExecuteCommandStage 获取工作流

```csharp
// 在 ExecuteCommandStage 构造函数或 ExecuteCoreAsync 中
// 从 SharedData 或 IServiceProvider 获取
_parameterExecutionWorkflow ??= ResolveDependency<IParameterExecutionWorkflow>(
    context, null, "__ParameterExecutionWorkflow");
```

---

## 七、后续工作流改造方向

### 7.1 当前实现 vs 工作流实现

```
当前（内联实现）：
  ParameterExecutionWorkflow.ExecuteAsync()
    ├── ReadControlValues()          ← 内联
    ├── _functionExecutor.Execute()  ← 内联调用
    └── WriteResult()                ← 内联

未来（工作流实现）：
  Workflow.StartWith<ReadControlValueNode>()
         .Then<BuildArgumentsNode>()
         .Then<ExecuteFunctionNode>()
         .Then<WriteResultNode>()
```

### 7.2 工作流节点定义

```csharp
// 未来在 WorkflowExecution 中定义的工作流节点

public class ReadControlValueNode : IWorkflowNode
{
    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext context)
    {
        var request = context.GetInput<ParameterExecutionRequest>();
        var values = await ReadControlValuesAsync(request);
        return NodeExecutionResult.Success(values);
    }
}

public class BuildArgumentsNode : IWorkflowNode
{
    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext context)
    {
        var rawValues = context.GetInput<string?[]>();
        var descriptor = context.GetInput<IFunctionDescriptor>();
        var args = ArgumentBuilder.BuildFromDescriptor(descriptor, rawValues);
        return NodeExecutionResult.Success(args);
    }
}

public class ExecuteFunctionNode : IWorkflowNode
{
    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext context)
    {
        var args = context.GetInput<object?[]>();
        var funcName = context.GetInput<string>();
        var result = await _functionExecutor.ExecuteAsync(funcName, args);
        return NodeExecutionResult.Success(result);
    }
}

public class WriteResultNode : IWorkflowNode
{
    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext context)
    {
        var result = context.GetInput<ExecutionResult>();
        var request = context.GetInput<ParameterExecutionRequest>();
        WriteResult(request, request.OutputTarget, result.Value?.ToString());
        return NodeExecutionResult.Success(result.Value);
    }
}
```

---

## 八、文件变更清单

### 8.1 新增文件

| 文件 | 说明 |
|------|------|
| `Share/CommandFramework/Abstractions/IParameterExecutionWorkflow.cs` | 反转接口 |
| `Share/CommandFramework/Abstractions/ParameterExecutionRequest.cs` | 请求模型 |
| `Share/CommandFramework/Abstractions/ParameterExecutionResult.cs` | 结果模型 |
| `Operation/BackEndDriven/Invoke/ParameterExecutionWorkflow.cs` | 工作流实现 |
| `Operation/BackEndDriven/Invoke/README.md` | 目录说明 |

### 8.2 修改文件

| 文件 | 改动 |
|------|------|
| `Share/CommandFramework/Invoke/ExecuteCommandStage.cs` | 新增 `_parameterExecutionWorkflow` 字段 + 构造函数参数；HandleClick 委托给工作流 |
| `Share/CommandFramework/Invoke/PipelineEntry.cs` | CreateDefaultPipeline 透传 `IParameterExecutionWorkflow` |
| `Operation/BackEndDriven/BackEndDrivenBuilder.cs` | 新增 `WithParameterExecutionWorkflow()` 方法 |

### 8.3 不修改的文件

| 文件 | 原因 |
|------|------|
| `Share/CommandFramework/Invoke/FunctionExecution.cs` | 函数执行器，仍被工作流实现使用 |
| `Share/CommandFramework/Invoke/ArgumentBuilder.cs` | 参数构建器，仍被工作流实现使用 |
| `Share/CommandFramework/Pipeline/PipelineStageBase.cs` | 阶段基类，与参数执行无关 |
| `Share/CommandFramework/Models/CommandContext.cs` | 上下文模型，与参数执行无关 |
| `Share/CommandFramework/Pipeline/Stages/ParseCommandStage.cs` | 命令解析阶段，与参数执行无关 |
| `Share/CommandFramework/Pipeline/Stages/ResultCollectStage.cs` | 结果收集阶段，与参数执行无关 |

---

## 九、回退兼容

### 9.1 向后兼容

`IParameterExecutionWorkflow` 为可选注入：

- **注入时**：HandleClick 委托给工作流实现
- **未注入时**：使用原有内联逻辑（保留原有代码作为回退路径）

确保现有功能不受影响。

### 9.2 渐进式迁移

```
阶段 1（当前）：定义接口 + 内联实现
  → ExecuteCommandStage 可选委托给 IParameterExecutionWorkflow
  → BackEndDriven 提供内联实现

阶段 2（后续）：替换为工作流节点
  → ParameterExecutionWorkflow 内部改为 WorkflowExecution 节点编排
  → 接口不变，调用方无感知

阶段 3（最终）：移除回退路径
  → 所有场景均使用工作流实现
  → 删除 ExecuteCommandStage 中的内联回退代码
```
