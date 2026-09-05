# BackEndDriven 与 CommandFramework 整合方案

> 分析日期：2026-07-05
> 分析范围：`BackEndDriven` → `CommandFramework` → `WorkFlowMethod` → `FileDriven`
> 目标：整合两条执行路径，将路径 B（CommandFramework）改造为完整工作流

---

## 1. 现状分析

### 1.1 两条并行的执行路径

```
路径 A（FileDriven 管线 — 脚本驱动）：
  ExecutionPipelineEntry
    → FileDrivenPipelineRunner
      → CentralPipeline
        → PipelineFactory.CreateFileDrivenPipeline
          → ForEachSkeleton
            → BuildCommandPipeline
              → ExecuteCommandStage(FileDriven版)
                → IExecutionCell.ExecuteAsync
                  → Container 内部处理 UI 控件

路径 B（CommandFramework — 手动点击/命令驱动）：
  PipelineEntryInstance
    → ExecuteCommandStage(CommandFramework版)
      → ClickCommandHandler
        → IParameterExecutionWorkflow
          → WorkflowMethodExecutionWorkflow
            → GuiControlSource（反射读取控件 Text）
            → WorkFlowMethodUsage.ExecuteAsync（只是函数调用，不是工作流）
```

### 1.2 核心差异

| 维度 | 路径 A（FileDriven 管线） | 路径 B（CommandFramework） |
|------|--------------------------|--------------------------|
| **执行单元** | `IExecutionCell`（Container 弹性执行单元） | `IParameterExecutionWorkflow`（参数执行工作流） |
| **上下文** | `FileDrivenPipelineContext`（含 TargetForm、FunctionExecutor、BindingResolver） | `CommandContext`（含 SharedData） |
| **命令分发** | 内部 switch + HandleNonEventCommand | 策略字典 `_eventHandlers` |
| **UI 控件** | Container 内部处理，通过 `FileDrivenPipelineContext.TargetForm` 传递 | `GuiControlSource` 反射 + `IControlFinder`/`IControlValueAccessor` |
| **工作流调用** | 不经过 WorkFlowMethod | 经过 `WorkFlowMethodUsage.ExecuteAsync`（但只是函数调用） |
| **弹性策略** | Container 自带（重试/熔断/超时/日志） | 无 |

### 1.3 路径 B 的 4 个核心问题

#### 问题 1：两条路径的 UI 控件逻辑完全割裂

同样的 UI 控件操作，在脚本执行和手动点击两条路径下走的是完全不同的代码逻辑。

#### 问题 2：GuiControlSource 控件值读取方式落后

`GuiControlSource.cs`（位于 `ParameterResolver.Sources`）使用纯反射查找控件并读取 Text 属性：
- ❌ 只支持 `Text` 属性
- ❌ 不支持 `ComboBox.SelectedItem`、`CheckBox.Checked`、`DataGridView` 等复杂控件
- ❌ 没有使用 `IControlValueAccessor`（`TConfig.Controls` 的统一接口）
- ❌ 与 `FrontEndDriven` 项目中的 `WinFormsControlValueWriter`、`UnifiedControlService` 等实现没有关联

#### 问题 3：IControlFinder/IControlValueAccessor 传递链脆弱

```
CommandFrameworkOptions
  → ExecuteCommandStage 构造函数（nullable）
    → ClickCommandHandler.HandleClick（nullable）
      → ParameterExecutionRequest.ControlFinder（nullable）
        → WorkflowMethodExecutionWorkflow（nullable）
```

每个环节都是 `nullable`，任何一个环节为 null 都会导致 UI 控件操作失败。而且 `WorkflowMethodExecutionWorkflow` 构造函数中的 `_controlFinder` 和 `_controlValueWriter` **实际上没有被使用**——`WriteToTarget` 方法优先使用 `request.ControlFinder` 和 `request.ControlValueWriter`。

#### 问题 4：WorkflowMethodExecutionWorkflow 跳过了完整工作流引擎路径

`WorkflowMethodExecutionWorkflow.ExecuteAsync()` 直接调用：
```csharp
WorkFlowMethodUsage.ExecuteAsync(functionName, wfContext.Parameters)
```

而 `WorkflowMethodExecutionService`（`IEventExecutionService` 实现）走的是：
```csharp
NodeExecutor.ExecuteNodeAsync(node, wfContext)
  → Polly 弹性策略（重试 + 超时）
  → ExecuteFunctionNodeAsync（创建 TConfig.ExecutionContext）
  → FunctionExecutor.ExecuteAsync（参数转换 + DynamicInvoke）
```

**差异**：
- ❌ 缺少 Polly 弹性策略（重试、超时、熔断）
- ❌ 缺少 `WorkflowContext` 生命周期管理
- ❌ 参数只传了 `Dictionary`，丢失了完整的上下文信息
- ❌ 返回值处理简单（JSON 序列化），不支持复杂类型传递

---

## 2. 整合方案

### 2.1 核心思路

```
统一命令执行阶段（合并两个 ExecuteCommandStage）
  │
  ├─ 事件命令（click/set）→ IExecutionCell（Container 弹性执行）
  │                            │
  │                            └─ 内部委托给 IParameterExecutionWorkflow
  │                                  │
  │                                  └─ WorkflowMethodExecutionWorkflow（重构版）
  │                                        │
  │                                        └─ WorkflowMethodExecutionService（NodeExecutor 完整路径）
  │                                              │
  │                                              └─ WorkFlowMethod 工作流引擎
  │                                                    ├─ Pipeline（管线编排）
  │                                                    ├─ NodeExecutor（弹性策略）
  │                                                    └─ WorkflowContext（生命周期）
  │
  └─ 非事件命令 → INonEventCommandLookup
```

### 2.2 架构图

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        统一命令执行阶段                                    │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │                    ExecuteCommandStage（统一版）                    │   │
│  │                                                                    │   │
│  │  事件命令（click/set）                   非事件命令（echo/sleep）      │   │
│  │        │                                        │                  │   │
│  │        ▼                                        ▼                  │   │
│  │  IExecutionCell                          INonEventCommandLookup    │   │
│  │  （Container 弹性执行）                    （非事件命令查找器）        │   │
│  │        │                                                        │   │
│  └────────┼────────────────────────────────────────────────────────┘   │
└───────────┼────────────────────────────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────────────────────────────────────────┐
│              Container 弹性策略链（重试/熔断/超时/日志）                    │
│                                                                          │
│  IExecutionCell.ExecuteAsync(request)                                    │
│    ├─ Polly RetryPolicy（重试 3 次，指数退避）                             │
│    ├─ Polly TimeoutPolicy（30 秒超时）                                    │
│    ├─ Polly CircuitBreaker（5 次失败后熔断 30 秒）                        │
│    └─ 日志记录（开始/完成/异常）                                          │
│                                                                          │
│  事件命令 → IParameterExecutionWorkflow.ExecuteAsync(request)            │
└──────────────────────────┬──────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────────────┐
│              WorkflowMethodExecutionWorkflow（重构版）                    │
│                                                                          │
│  1. IControlValueAccessor.ReadValue("input1")  ← 统一控件接口            │
│  2. WorkflowMethodExecutionService.ExecuteFunctionAsync("SomeFunc", ..)  │
│  3. IControlValueAccessor.WriteValue("output1", result)                  │
└──────────────────────────┬──────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────────────┐
│              WorkflowMethodExecutionService（NodeExecutor 路径）          │
│                                                                          │
│  NodeExecutor.ExecuteNodeAsync(node, wfContext)                          │
│    ├─ 构建 PipelineContext（IWorkflowContext）                            │
│    ├─ 构建 NodeDefinition（Function 类型节点）                             │
│    ├─ Polly 弹性策略（重试 + 超时）                                       │
│    ├─ ExecuteFunctionNodeAsync                                           │
│    │     ├─ 创建 TConfig.ExecutionContext                                │
│    │     └─ FunctionExecutor.ExecuteAsync                                │
│    │           ├─ ParameterTransformer.FillFromDict                      │
│    │           └─ DynamicInvoke                                          │
│    └─ 返回 ExecutionResult                                               │
└─────────────────────────────────────────────────────────────────────────┘
```

### 2.3 整合后的完整调用链

```
用户点击 UI 按钮
  │
  ▼
MainForm 事件 → IEventPipelineExecutor.Execute
  │
  ▼
PipelineEntryInstance.RunCommandAsync("do click Button1")
  │
  ▼
ParseCommandStage → { Action="click", Target="Button1" }
  │
  ▼
ExecuteCommandStage（统一版）
  │
  ├─ 事件命令 → IExecutionCell.ExecuteAsync(request)
  │                │
  │                └─ Container 弹性策略链（重试/熔断/超时/日志）
  │                      │
  │                      └─ IParameterExecutionWorkflow.ExecuteAsync(request)
  │                            │
  │                            └─ WorkflowMethodExecutionWorkflow（重构版）
  │                                  │
  │                                  1. IControlValueAccessor.ReadValue("input1")
  │                                  2. WorkflowMethodExecutionService.ExecuteFunctionAsync("SomeFunc", params)
  │                                     │
  │                                     └─ NodeExecutor.ExecuteNodeAsync(node, wfContext)
  │                                           │
  │                                           ├─ Polly 重试策略
  │                                           ├─ Polly 超时策略
  │                                           ├─ ExecuteFunctionNodeAsync
  │                                           │     ├─ 创建 TConfig.ExecutionContext
  │                                           │     └─ FunctionExecutor.ExecuteAsync
  │                                           │           ├─ ParameterTransformer.FillFromDict
  │                                           │           └─ DynamicInvoke
  │                                           └─ 返回 ExecutionResult
  │                                  3. IControlValueAccessor.WriteValue("output1", result)
  │
  └─ 非事件命令 → INonEventCommandLookup
```

---

## 3. 实施步骤

> **状态标记**：✅ 已完成 | 📋 待实施

### 步骤 1：合并两个 ExecuteCommandStage（📋 待实施）

**目标**：让 CommandFramework 版的 `ExecuteCommandStage` 也使用 `IExecutionCell`，与 FileDriven 版共享同一套执行路径。

**改动文件**：`CsharpPrj/Share/CommandFramework/Invoke/ExecuteCommandStage.cs`

```csharp
// 改造后的 CommandFramework.ExecuteCommandStage
public class ExecuteCommandStage : CommandStageBase
{
    private readonly IExecutionCell _executionCell;  // ★ 新增：Container 执行单元

    protected override async Task ExecuteCoreAsync(CommandContext context, CancellationToken ct)
    {
        // 事件命令 → 通过 IExecutionCell 执行
        // 非事件命令 → 通过 INonEventCommandLookup 执行
        var request = CreateExecutionRequest(context);
        var result = await _executionCell.ExecuteAsync(request, ct);
        // ...
    }
}
```

**关键设计**：
- `IExecutionCell` 成为唯一执行入口，事件命令和非事件命令统一经过 Container
- `CommandContext` 中的信息（TargetForm、FunctionExecutor 等）通过 `ExecutionRequest` 传递给 Container
- FileDriven 版的 `ExecuteCommandStage` 保持不变，但内部逻辑与 CommandFramework 版共享 `IExecutionCell`

### 步骤 2：在 CommandFramework 中新建 ContainerExecutionCell（📋 待实施）

**目标**：在 `CommandFramework` 项目中新建 `ContainerExecutionCell`，实现 `IExecutionCell` 接口，对于事件命令（click/set）委托给 `IParameterExecutionWorkflow`。

**设计原则**：`BasicMethod` 只放最基础通用的逻辑，`ContainerExecutionCell` 作为命令框架的执行单元，放在 `CommandFramework` 项目中。

**新建文件**：`CsharpPrj/Share/CommandFramework/Invoke/ContainerExecutionCell.cs`

```csharp
// CommandFramework 内部的事件命令路由
public class ContainerExecutionCell : IExecutionCell
{
    private readonly IParameterExecutionWorkflow? _parameterExecutionWorkflow;
    private readonly INonEventCommandLookup? _nonEventCommandLookup;

    public async Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken ct)
    {
        // 判断是否为事件命令
        if (IsEventCommand(request) && _parameterExecutionWorkflow != null)
        {
            // 委托给 IParameterExecutionWorkflow
            var paramRequest = CreateParameterExecutionRequest(request);
            var result = await _parameterExecutionWorkflow.ExecuteAsync(paramRequest, ct);
            return ConvertToExecutionResult(result);
        }
        else if (_nonEventCommandLookup != null)
        {
            // 非事件命令：委托给 INonEventCommandLookup
            return await _nonEventCommandLookup.ExecuteAsync(request, ct);
        }

        return ExecutionResult.Fail(request.CommandText, "No handler configured");
    }
}
```

### 步骤 3：重构 WorkflowMethodExecutionWorkflow — 核心：参数转换层（✅ 已完成）

**目标**：改为使用 `WorkflowMethodExecutionService` 的 `NodeExecutor` 路径，获得 Polly 弹性策略和 WorkflowContext 生命周期。**核心在于构建调用参数与工作流之间的转换层**。

#### 3.1 参数转换现状分析

当前 `WorkflowMethodExecutionWorkflow` 的参数流存在 **3 个断裂点**：

##### 断裂点 1：控件名 → 函数参数名的映射

```
InputControls = "txtName,cmbGender,chkActive"
                  │
                  ▼
parameters["txtName"] = "张三"
parameters["cmbGender"] = "男"
parameters["chkActive"] = "True"
                  │
                  ▼
函数签名：SomeFunction(string name, bool isActive)
                  │
                  ▼
ArgumentBuilder 按参数名匹配：
  "txtName"   → name?    ❌ 不匹配（控件名 ≠ 参数名）
  "cmbGender" → gender?  ❌ 不匹配
  "chkActive" → isActive? ❌ 不匹配
```

**当前没有控件名到参数名的映射机制**，完全依赖 `ArgumentBuilder` 的模糊匹配（按位置或名称近似匹配），这是不稳定的。

##### 断裂点 2：控件值类型 → 函数参数类型的转换

```
控件读取的值都是 string：
  "txtName"     = "张三"        → string ✓
  "cmbGender"   = "男"          → string ✓
  "chkActive"   = "True"        → bool   ❌ 需要 "True" → true
  "nudAge"      = "25"          → int    ❌ 需要 "25" → 25
```

`GuiControlSource` 只返回 `string?`，没有类型转换。虽然 `WorkflowMethodExecutionService` 中有 `ConvertToTargetType`，但 `WorkFlowMethodUsage.ExecuteAsync` 内部没有调用这个转换——它直接传 `Dictionary<string, object>` 给 `FunctionExecutor`，由 `ArgumentBuilder` 内部处理类型转换。

##### 断裂点 3：返回值 → 控件值的反向转换

```
函数返回：object?（可能是 int、bool、复杂对象）
                  │
                  ▼
result is string s ? s : JsonSerializer.Serialize(result)
                  │
                  ▼
控件写入：string? → IControlValueAccessor.WriteValue(control, value)
```

复杂对象被 JSON 序列化后写入控件，但控件可能只接受特定格式（如 `DateTimePicker` 需要日期格式）。

#### 3.2 参数转换层设计

```
ParameterExecutionRequest
  │
  ▼
┌─────────────────────────────────────────────────────────────┐
│               ParameterConversionLayer（新增）               │
│                                                             │
│  输入：InputControls = "txtName,cmbGender,chkActive"        │
│        VirtualStore / TargetForm（控件值来源）                │
│        FunctionName = "SomeFunction"（用于获取参数签名）      │
│                                                             │
│  转换过程：                                                   │
│    1. 从 WorkFlowRegistry 获取函数参数签名                    │
│       SomeFunction(string name, bool isActive)               │
│                                                             │
│    2. 建立控件名 → 参数名的映射（通过 BindingResult）          │
│       txtName    → name       (来自 binding.Parameter)       │
│       chkActive  → isActive   (来自 binding.Parameter)       │
│                                                             │
│    3. 读取控件值 + 类型转换                                   │
│       txtName    = "张三"     → "张三"     (string)          │
│       chkActive  = "True"    → true       (bool)            │
│                                                             │
│  输出：Dictionary<string, object>                            │
│        { "name": "张三", "isActive": true }                  │
│                                                             │
│  同时输出：反向映射表（用于写回）                               │
│        { "name" → "txtName", "isActive" → "chkActive" }     │
│                                                             │
└─────────────────────────────────────────────────────────────┘
          │
          ▼
WorkflowMethodExecutionService.ExecuteFunctionAsync(functionName, convertedParams)
          │
          ▼
NodeExecutor.ExecuteNodeAsync(node, wfContext)
  → ExecuteFunctionNodeAsync
    → FunctionExecutor.ExecuteAsync
      → ParameterTransformer.FillFromDict（按参数名精确匹配）
```

#### 3.3 关键数据结构

```csharp
/// <summary>
/// 参数转换结果
/// </summary>
public class ParameterConversionResult
{
    /// <summary>转换后的参数字典（键=函数参数名，值=转换后的值）</summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>反向映射表（键=函数参数名，值=控件名），用于写回</summary>
    public Dictionary<string, string> ParameterToControlMap { get; set; } = new();

    /// <summary>原始控件值（键=控件名，值=原始字符串值），用于调试</summary>
    public Dictionary<string, string?> RawControlValues { get; set; } = new();
}
```

#### 3.4 类型转换器

```csharp
/// <summary>
/// 类型转换器：string → 目标类型
/// </summary>
public static class TypeConverter
{
    public static object ConvertTo(string? value, Type targetType)
    {
        if (value == null)
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null!;

        if (targetType == typeof(string)) return value;
        if (targetType == typeof(int)) return int.Parse(value);
        if (targetType == typeof(long)) return long.Parse(value);
        if (targetType == typeof(double)) return double.Parse(value);
        if (targetType == typeof(decimal)) return decimal.Parse(value);
        if (targetType == typeof(bool)) return bool.Parse(value);
        if (targetType == typeof(DateTime)) return DateTime.Parse(value);
        if (targetType.IsEnum) return Enum.Parse(targetType, value);

        return Convert.ChangeType(value, targetType);
    }

    public static string FormatValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string s => s,
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
            bool b => b.ToString(),
            _ => System.Text.Json.JsonSerializer.Serialize(value)
        };
    }
}
```

#### 3.5 改造后的 WorkflowMethodExecutionWorkflow

**改动文件**：`CsharpPrj/Operation/BackEndDriven/Invoke/WorkflowMethodExecutionWorkflow.cs`

```csharp
public class WorkflowMethodExecutionWorkflow : IParameterExecutionWorkflow
{
    private readonly IEventExecutionService _executionService;
    private readonly IControlValueAccessor _controlValueAccessor;
    private readonly IControlFinder _controlFinder;

    public async Task<ParameterExecutionResult> ExecuteAsync(
        ParameterExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        // 步骤 1：参数转换（控件值 → 函数参数）
        var conversionResult = await ConvertControlValuesToParametersAsync(request);

        // 步骤 2：通过 WorkflowMethodExecutionService 执行（NodeExecutor 完整路径）
        var execResult = await _executionService.ExecuteFunctionAsync(
            request.FunctionName, conversionResult.Parameters);

        if (!execResult.Success)
            return ParameterExecutionResult.Fail(execResult.Error ?? "Unknown error");

        // 步骤 3：结果写回（函数返回值 + 传出参数 → 控件）
        await WriteResultsBackAsync(request, conversionResult, execResult);

        return ParameterExecutionResult.Ok(execResult.Value, executionTime);
    }

    private async Task<ParameterConversionResult> ConvertControlValuesToParametersAsync(
        ParameterExecutionRequest request)
    {
        var result = new ParameterConversionResult();

        // 1. 获取函数参数签名
        var registry = WorkFlowMethodUsage.WorkFlowRegistry;
        if (!registry.TryGetConfig(request.FunctionName, out var config))
            throw new InvalidOperationException($"Function '{request.FunctionName}' not found");

        // 2. 解析控件列表
        var controlNames = StringUtils.SplitEscapedToArray(request.InputControls ?? "");

        // 3. 逐控件读取值
        foreach (var ctrl in controlNames)
        {
            string? rawValue;
            if (request.IsGuiMode && request.TargetForm != null)
            {
                var control = _controlFinder.FindControl(request.TargetForm, ctrl);
                rawValue = control != null ? _controlValueAccessor.ReadValue(control) : null;
            }
            else
            {
                request.VirtualStore?.TryGetValue(ctrl, out rawValue);
            }
            result.RawControlValues[ctrl] = rawValue ?? string.Empty;
        }

        // 4. 建立控件名 → 参数名的映射
        //    通过函数参数签名匹配：控件名与参数名相同（或通过 BindingResult 映射）
        var paramConfigs = config.Parameters;
        foreach (var param in paramConfigs)
        {
            // 查找控件名：优先使用与参数名同名的控件
            var matchedControl = controlNames.FirstOrDefault(c =>
                c.Equals(param.Name, StringComparison.OrdinalIgnoreCase));

            if (matchedControl != null && result.RawControlValues.TryGetValue(matchedControl, out var rawVal))
            {
                // 类型转换：string → 目标类型
                var convertedValue = TypeConverter.ConvertTo(rawVal, param.Type);
                result.Parameters[param.Name] = convertedValue;
                result.ParameterToControlMap[param.Name] = matchedControl;
            }
        }

        return result;
    }

    private async Task WriteResultsBackAsync(
        ParameterExecutionRequest request,
        ParameterConversionResult conversion,
        ExecutionResult execResult)
    {
        if (string.IsNullOrEmpty(request.OutputTarget))
            return;

        // 写回函数返回值
        if (execResult.Value != null)
        {
            var valueStr = TypeConverter.FormatValue(execResult.Value);
            WriteToTarget(request, request.OutputTarget, valueStr);
        }

        // 写回传出参数（通过反向映射表）
        // 注意：这里需要 WorkflowMethodExecutionService 支持传出参数
        // 当前 NodeExecutor 不支持传出参数，这是后续需要增强的点
    }
}
```

#### 3.6 参数转换层的 4 个职责总结

| 职责 | 当前状态 | 改造后 |
|------|---------|--------|
| **控件名 → 参数名映射** | 无映射，靠 ArgumentBuilder 模糊匹配 | 通过函数签名 + BindingResult 精确映射 |
| **string → 目标类型转换** | 无转换，string 直接传入 | `TypeConverter.ConvertTo(value, paramType)` |
| **返回值 → 控件值格式化** | JSON 序列化 | `TypeConverter.FormatValue(value)` |
| **传出参数反向映射** | 无 | 通过 `ParameterToControlMap` 反向查找 |

这个转换层是 `WorkflowMethodExecutionWorkflow` 重构的核心——它解决了"UI 控件的字符串世界"与"工作流函数的类型化参数世界"之间的鸿沟。

### 步骤 4：统一 UI 控件操作接口（📋 待实施）

**目标**：统一使用 `IControlValueAccessor`（`TConfig.Controls` 接口），废弃 `GuiControlSource`。

**改动文件**：
- `CsharpPrj/Share/ParameterResolver/Sources/GuiControlSource.cs` — 标记废弃
- `CsharpPrj/Operation/FrontEndDriven/` — 确保 `WinFormsControlValueWriter` 实现 `IControlValueAccessor`

```csharp
// 统一控件操作接口
public interface IControlValueAccessor
{
    string? ReadValue(object control);
    void WriteValue(object control, string? value);
}

// WinForms 实现（复用 FrontEndDriven 的 WinFormsControlValueWriter）
public class WinFormsControlValueWriter : IControlValueAccessor
{
    public string? ReadValue(object control)
    {
        return control switch
        {
            TextBox tb => tb.Text,
            ComboBox cb => cb.SelectedItem?.ToString(),
            CheckBox chk => chk.Checked.ToString(),
            NumericUpDown nud => nud.Value.ToString(),
            Label lbl => lbl.Text,
            DataGridView dgv => dgv.CurrentCell?.Value?.ToString(),
            _ => control.GetType().GetProperty("Text")?.GetValue(control)?.ToString()
        };
    }

    public void WriteValue(object control, string? value)
    {
        if (value == null) return;
        switch (control)
        {
            case TextBox tb: tb.Text = value; break;
            case ComboBox cb: cb.SelectedItem = value; break;
            case CheckBox chk: chk.Checked = bool.TryParse(value, out var b) && b; break;
            case NumericUpDown nud: nud.Value = decimal.TryParse(value, out var d) ? d : 0; break;
            case Label lbl: lbl.Text = value; break;
            default:
                control.GetType().GetProperty("Text")?.SetValue(control, value);
                break;
        }
    }
}
```

### 步骤 5：简化 ClickCommandHandler（📋 待实施）

**目标**：`ClickCommandHandler` 不再需要 `IControlFinder`/`IControlValueAccessor` 参数，这些已由 `WorkflowMethodExecutionWorkflow` 内部处理。

**改动文件**：`CsharpPrj/Share/CommandFramework/Invoke/Handlers/ClickCommandHandler.cs`

```csharp
// 简化后的 ClickCommandHandler
public static async Task HandleClick(
    CommandContext context,
    string? target,
    string stageName,
    Func<string, string, BindingResult?>? bindingFinder,
    IParameterExecutionWorkflow? parameterExecutionWorkflow,
    CancellationToken cancellationToken,
    ILogger? logger = null)
{
    // 不再需要 controlFinder 和 controlValueWriter 参数
    // 这些已由 WorkflowMethodExecutionWorkflow 内部通过 IControlValueAccessor 处理
    // ...
}
```

### 步骤 6：更新 BackEndDrivenBuilder（✅ 已完成）

**目标**：注入 `IExecutionCell` + `IEventExecutionService` + `IControlValueAccessor`。

**改动文件**：`CsharpPrj/Operation/BackEndDriven/BackEndDrivenBuilder.cs`

```csharp
public class BackEndDrivenBuilder
{
    public BackEndDrivenBuilder WithWorkflowMethodExecution()
    {
        // 注入 IEventExecutionService（WorkflowMethodExecutionService）
        options.EventExecutionService = new WorkflowMethodExecutionService();

        // 注入 IParameterExecutionWorkflow（重构版）
        options.ParameterExecutionWorkflow = new WorkflowMethodExecutionWorkflow(
            executionService: options.EventExecutionService,
            controlValueAccessor: new WinFormsControlValueWriter());

        // 注入 IExecutionCell（Container 弹性执行单元）
        options.ExecutionCell = new ContainerExecutionCell(
            parameterExecutionWorkflow: options.ParameterExecutionWorkflow);

        return this;
    }
}
```

---

## 4. 文件改动清单

| 文件 | 改动 | 工作量 | 优先级 | 状态 |
|------|------|--------|--------|------|
| `Share/CommandFramework/Invoke/ExecuteCommandStage.cs` | 新增 `IExecutionCell` 依赖，事件命令走 Container | 中 | P0 | 📋 |
| `Share/CommandFramework/Invoke/ContainerExecutionCell.cs` | **新建**：实现 `IExecutionCell`，事件命令委托给 `IParameterExecutionWorkflow` | 中 | P0 | 📋 |
| `BackEndDriven/Invoke/WorkflowMethodExecutionWorkflow.cs` | 改用 `IEventExecutionService`（NodeExecutor 路径），废弃 `GuiControlSource` | 中 | P0 | ✅ |
| `BackEndDriven/BackEndDrivenBuilder.cs` | 注入 `IExecutionCell` + `IEventExecutionService` + `IControlValueAccessor` | 小 | P0 | ✅ |
| `Share/CommandFramework/Invoke/Handlers/ClickCommandHandler.cs` | 简化参数，移除 `IControlFinder`/`IControlValueAccessor` | 小 | P1 | 📋 |
| `ParameterResolver/Sources/GuiControlSource.cs` | 标记废弃，统一使用 `IControlValueAccessor` | 小 | P1 | 📋 |
| `FrontEndDriven` 的 `WinFormsControlValueWriter` | 确保实现 `IControlValueAccessor` 接口 | 小 | P1 | ✅ |

---

## 5. 收益

1. **路径 B 真正使用 WorkFlowMethod 工作流引擎** — 获得 Pipeline 编排、NodeExecutor 弹性策略、WorkflowContext 生命周期
2. **两条路径共享同一套执行单元** — `IExecutionCell` 成为唯一执行入口
3. **UI 控件操作统一** — 废弃反射的 `GuiControlSource`，使用 `IControlValueAccessor` 适配器体系
4. **消除 nullable 传递链** — `IControlFinder`/`IControlValueAccessor` 不再需要跨多层传递
5. **弹性策略全覆盖** — 路径 B 也获得 Container 的重试/熔断/超时/日志能力

---

## 6. 风险与注意事项

1. **向后兼容**：`IExecutionCell` 接口的变更需要确保 FileDriven 管线不受影响
2. **性能影响**：路径 B 增加 Container 弹性策略链，会引入少量性能开销（毫秒级）
3. **依赖注入**：需要确保 `IEventExecutionService` 和 `IControlValueAccessor` 在正确的生命周期内可用
4. **测试覆盖**：需要为重构后的 `WorkflowMethodExecutionWorkflow` 编写单元测试，覆盖 GUI 模式和 CLI 模式
