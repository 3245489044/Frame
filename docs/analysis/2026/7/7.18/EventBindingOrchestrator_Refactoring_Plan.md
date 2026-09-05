# EventBindingOrchestrator 重构方案文档

> **重构目标**：消除两个平行的事件触发服务体系 + 两个平行的事件触发结果模型
> **合并方向**：以 `Models.EventTriggerResult` 为基础整合结果模型，以 `Services.EventTriggerService` 为基础整合服务体系
> **日期**：2026-07-18（更新）
> **作者**：Cline 代码分析

---

## 目录

1. [背景：问题概述](#1-背景问题概述)
2. [接口继承体系全景](#2-接口继承体系全景)
3. [整合一：事件触发结果模型](#3-整合一事件触发结果模型)
4. [整合二：事件触发服务体系](#4-整合二事件触发服务体系)
5. [整合后的完整体系](#5-整合后的完整体系)
6. [执行计划](#6-执行计划)
7. [附录：文件清单与调用链](#7-附录文件清单与调用链)

---

## 1. 背景：问题概述

### 1.1 两个平行的事件结果模型

| 类 | 文件路径 | 工厂方法 | 使用方 |
|---|---|---|---|
| `EventBindingResult` | `Services/EventBindingResult.cs` | `Success()` / `Failure()` | `EventTriggerService`（8处）、`EventBindingOrchestrator`（3处） |
| `EventTriggerResult` | `Models/EventTriggerResult.cs` | `SuccessResult()` / `FailureResult()` | 仅 `EventTriggerServiceBase`（6处） |

两者功能高度重叠：都是"事件触发结果"的数据模型，都提供成功/失败静态工厂方法。但 `EventTriggerResult` 继承自 `TConfig.EventResult` 并额外实现 3 个接口，`EventBindingResult` 是独立类直接实现 2 个接口。

### 1.2 两个平行的事件触发服务体系

| 类 | 文件路径 | 实现接口 | 使用方 |
|---|---|---|---|
| `EventTriggerService` | `Services/EventTriggerService.cs` | `TConfig.IEventTriggerService` | `EventBindingOrchestrator`（间接） |
| `EventTriggerServiceBase`（抽象） | `Services/EventTriggerServiceBase.cs` | `Abstractions.IEventTriggerService` | 无子类（仅自身） |

两者都叫"事件触发服务"，都实现了 `TriggerEventAsync` 方法，都包含"查找绑定 → 构建参数 → 执行函数 → 写入输出"的管线流程。但两者使用不同的接口体系、不同的依赖注入方式，且 `EventTriggerService` 并未继承 `EventTriggerServiceBase`。

### 1.3 核心问题

1. **代码重复**：两个结果类、两个服务类，功能高度重叠
2. **接口分裂**：`EventTriggerService` 实现 `TConfig.IEventTriggerService`，`EventTriggerServiceBase` 实现 `Abstractions.IEventTriggerService`，两者互不关联
3. **依赖混乱**：`EventTriggerServiceBase` 依赖 `CommandExecutionResult<object>`（CommandFramework 类型），而 `EventTriggerService` 依赖 `IFunctionExecutor`（TConfig 类型）
4. **抽象基类无子类**：`EventTriggerServiceBase` 声明为 `abstract`，但搜索确认无任何子类继承它，抽象方法从未被实现

---

## 2. 接口继承体系全景

### 2.1 事件触发结果接口体系

```
┌─────────────────────────────────────────────────────────────┐
│                    TConfig.IEventTriggerResult               │
│  (TConfig/Abstractions/IEventTriggerResult.cs)               │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  bool IsSuccess                                      │   │
│  │  string? ErrorMessage                                │   │
│  │  object? Value                                       │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
                              │ 继承
                              ▼
┌─────────────────────────────────────────────────────────────┐
│              Modules.IEventTriggerResult                     │
│  (Modules/EventFramework/Services/IEventTriggerService.cs)   │
│  空接口，仅用于标记继承关系                                   │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│          Abstractions.IEventTriggerResult                    │
│  (Share/EventBindingOrchestrator/Abstractions/               │
│   IEventTriggerResult.cs)                                    │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  bool IsSuccess                                      │   │
│  │  object? Value                                       │   │
│  │  string? ErrorMessage                                │   │
│  │  TimeSpan ExecutionTime                              │   │
│  │  string? Message                                     │   │
│  │  string? ControlName                                 │   │
│  │  string? EventType                                   │   │
│  │  string? FunctionName                                │   │
│  │  string? OutputTarget                                │   │
│  │  bool IsWorkflowSwitchEvent                          │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

### 2.2 事件触发结果实现类（当前）

```
┌─────────────────────────────────────────────────────────────┐
│                    TConfig.EventResult                       │
│  (TConfig/Models/EventResult.cs)                             │
│  实现: TConfig.IEventTriggerResult                           │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  IsSuccess, ErrorMessage, Value                      │   │
│  │  ControlName, EventType, Message                     │   │
│  │  IsWorkflowSwitchEvent, FunctionName                 │   │
│  │  OutputTarget, ExecutionTime, Status                 │   │
│  │  + SuccessResult() / FailureResult()                 │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
              │ 继承
              ▼
┌─────────────────────────────────────────────────────────────┐
│              EventTriggerResult  ★ 保留并扩展                │
│  (Models/EventTriggerResult.cs)                              │
│  继承: TConfig.EventResult                                   │
│  实现: Modules.IEventTriggerResult                           │
│         Abstractions.IEventTriggerResult                     │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  + EventName 属性                                    │   │
│  │  + 显式接口实现映射（4个属性）                        │   │
│  │  + SuccessResult() / FailureResult() 工厂方法         │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│              EventBindingResult  ★ 待删除                    │
│  (Services/EventBindingResult.cs)                            │
│  实现: Modules.IEventTriggerResult                           │
│         TConfig.IEventTriggerResult                          │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  IsSuccess, Value, ErrorMessage                      │   │
│  │  ExecutionTime, EventName                            │   │
│  │  FunctionName, OutputTarget                          │   │
│  │  + Success() / Failure()                             │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

### 2.3 事件触发服务接口体系

```
┌─────────────────────────────────────────────────────────────┐
│              TConfig.IEventTriggerService                    │
│  (TConfig/Abstractions/IEventTriggerService.cs)              │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  Task<IEventTriggerResult> TriggerEventAsync(        │   │
│  │      string eventName, object? sender, object? args) │   │
│  │  Task<IEventTriggerResult> TriggerAndExecuteAsync(   │   │
│  │      string eventName, object? sender, object? args) │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│          Abstractions.IEventTriggerService                   │
│  (Share/EventBindingOrchestrator/Abstractions/               │
│   IEventTriggerService.cs)                                   │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  Task<IEventTriggerResult> TriggerEventAsync(        │   │
│  │      string controlName, string eventType,           │   │
│  │      string value)                                   │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│          Modules.IEventTriggerService                        │
│  (Modules/EventFramework/Services/IEventTriggerService.cs)   │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  Task<IEventTriggerResult> TriggerEventAsync(        │   │
│  │      IEventSourceNode source, string eventName,      │   │
│  │      object? eventArgs)                              │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

### 2.4 事件触发服务实现类（当前）

```
┌─────────────────────────────────────────────────────────────┐
│              EventTriggerService  ★ 保留并扩展               │
│  (Services/EventTriggerService.cs)                           │
│  实现: TConfig.IEventTriggerService                          │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  依赖: IEventBindingResolver                         │   │
│  │        IFunctionExecutor                             │   │
│  │        IParameterBuilder<string>                     │   │
│  │        IValueWriter                                  │   │
│  │                                                      │   │
│  │  TriggerEventAsync(IEventSourceNode, string, obj?)   │   │
│  │    → 查找绑定 → 构建参数 → 执行函数 → 写入输出       │   │
│  │                                                      │   │
│  │  TriggerEventAsync(string, object?, object?)         │   │
│  │    → 字符串重载，通过名称查找绑定                     │   │
│  │                                                      │   │
│  │  TriggerAndExecuteAsync(string, object?, object?)    │   │
│  │    → 委托给 TriggerEventAsync（完全相同）            │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│           EventTriggerServiceBase (abstract)  ★ 待删除      │
│  (Services/EventTriggerServiceBase.cs)                       │
│  实现: Abstractions.IEventTriggerService                     │
│  ┌──────────────────────────────────────────────────────┐   │
│   │  依赖: IEventBindingResolver                         │   │
│  │        IStateStore (可选)                            │   │
│  │                                                      │   │
│  │  TriggerEventAsync(string, string, string)           │   │
│  │    → 委托给带 directInputValues 的重载               │   │
│  │                                                      │   │
│  │  TriggerEventAsync(string, string, string,           │   │
│  │                      Dictionary?)                    │   │
│  │    → 查找绑定 → 提取输入值 → 执行函数 → 写入输出     │   │
│  │                                                      │   │
│  │  ExecuteMultiFunctionBindingAsync()                  │   │
│  │    → 多函数绑定支持（循环执行）                       │   │
│  │                                                      │   │
│  │  ExtractInputValuesAsync() (virtual)                 │   │
│  │    → 从 StateStore 提取输入值                        │   │
│  │                                                      │   │
│  │  ExecuteFunctionAsync() (abstract)                   │   │
│  │  WriteOutputAsync() (abstract)                       │   │
│  │                                                      │   │
│  │  ExportDiagnosticSnapshot() (static)                 │   │
│  │    → 导出 DiagnosticContext 快照                     │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

---

## 3. 整合一：事件触发结果模型

### 3.1 整合目标

以 `EventTriggerResult`（`Models/EventTriggerResult.cs`）为基础，整合 `EventBindingResult`（`Services/EventBindingResult.cs`）的能力，删除 `EventBindingResult`。

**选择 `EventTriggerResult` 为基础的原因**：
1. 继承自 `TConfig.EventResult`，已具备 ControlName/EventType/Message/IsWorkflowSwitchEvent 等字段
2. 已实现全部 3 个结果接口（`Modules.IEventTriggerResult` + `Abstractions.IEventTriggerResult` + 通过基类实现 `TConfig.IEventTriggerResult`）
3. 工厂方法 `SuccessResult`/`FailureResult` 已包含 controlName/eventType 参数，语义更完整

### 3.2 整合策略：扩展工厂方法参数

`EventBindingResult` 的 `Success/Failure` 使用 `eventName` 参数，`EventTriggerResult` 的 `SuccessResult/FailureResult` 使用 `controlName/eventType` 参数。整合策略是**扩展 `EventTriggerResult` 的工厂方法**，使其同时兼容两种调用风格：

- **保留 `controlName`/`eventType`** 作为主要标识（`EventTriggerResult` 原有语义）
- **新增 `eventName` 可选参数**（兼容 `EventBindingResult` 调用方）
- 当 `eventName` 未提供时，使用 `eventType` 作为回退值

### 3.3 整合后的 `EventTriggerResult` 设计

```csharp
// Models/EventTriggerResult.cs
public class EventTriggerResult : TConfig.EventResult,
    Modules.EventFramework.Services.IEventTriggerResult,
    Share.EventBindingOrchestrator.Abstractions.IEventTriggerResult
{
    // ── 基类 TConfig.EventResult 已提供的属性 ──
    // IsSuccess, ErrorMessage, Value
    // ControlName, EventType, Message, IsWorkflowSwitchEvent
    // FunctionName, OutputTarget, ExecutionTime, Status

    // ── EventTriggerResult 独有属性 ──
    public string? EventName { get; set; }

    // ── 显式接口实现（Abstractions.IEventTriggerResult）──
    // ControlName, EventType, Message, IsWorkflowSwitchEvent

    // ── 工厂方法（扩展参数，兼容 EventBindingResult 调用方）──
    public new static EventTriggerResult SuccessResult(
        object? value,
        string? controlName = null,     // 改为可选
        string? eventType = null,       // 改为可选
        string? functionName = null,
        string? outputTarget = null,
        TimeSpan? executionTime = null,
        string? eventName = null)       // 新增：兼容 EventBindingResult.EventName
    {
        return new EventTriggerResult
        {
            Status = ExecutionStatus.Completed,
            IsSuccess = true,
            Value = value,
            ControlName = controlName,
            EventType = eventType,
            FunctionName = functionName,
            OutputTarget = outputTarget,
            ExecutionTime = executionTime ?? TimeSpan.Zero,
            EventName = eventName ?? eventType  // eventName 回退到 eventType
        };
    }

    public new static EventTriggerResult FailureResult(
        string errorMessage,
        string? controlName = null,     // 改为可选
        string? eventType = null,       // 改为可选
        string? functionName = null,
        TimeSpan? executionTime = null,
        string? eventName = null)       // 新增：兼容 EventBindingResult.EventName
    {
        return new EventTriggerResult
        {
            Status = ExecutionStatus.Failed,
            IsSuccess = false,
            ErrorMessage = errorMessage,
            ControlName = controlName,
            EventType = eventType,
            FunctionName = functionName,
            ExecutionTime = executionTime ?? TimeSpan.Zero,
            EventName = eventName ?? eventType  // eventName 回退到 eventType
        };
    }
}
```

### 3.4 调用方替换映射

#### EventTriggerService.cs（8处替换）

| 原调用 | 替换为 |
|---|---|
| `EventBindingResult.Failure($"事件源 '{...}' 未找到事件绑定", eventName, executionTime: sw.Elapsed)` | `EventTriggerResult.FailureResult($"事件源 '{...}' 未找到事件绑定", eventName: eventName, executionTime: sw.Elapsed)` |
| `EventBindingResult.Failure($"函数 '{...}' 执行失败: {result.ErrorMessage}", eventName, binding.FunctionName, sw.Elapsed)` | `EventTriggerResult.FailureResult($"函数 '{...}' 执行失败: {result.ErrorMessage}", eventName: eventName, functionName: binding.FunctionName, executionTime: sw.Elapsed)` |
| `EventBindingResult.Success(result.Value, eventName, binding.FunctionName, binding.Output, sw.Elapsed)` | `EventTriggerResult.SuccessResult(result.Value, eventName: eventName, functionName: binding.FunctionName, outputTarget: binding.Output, executionTime: sw.Elapsed)` |
| `EventBindingResult.Failure($"事件触发异常: {ex.Message}", eventName, executionTime: sw.Elapsed)` | `EventTriggerResult.FailureResult($"事件触发异常: {ex.Message}", eventName: eventName, executionTime: sw.Elapsed)` |
| `EventBindingResult.Failure($"事件源 '{...}' 未找到事件绑定", eventName: eventName)` | `EventTriggerResult.FailureResult($"事件源 '{...}' 未找到事件绑定", eventName: eventName)` |
| `EventBindingResult.Failure($"函数 '{...}' 执行失败: {result.ErrorMessage}", eventName: eventName, functionName: binding.FunctionName)` | `EventTriggerResult.FailureResult($"函数 '{...}' 执行失败: {result.ErrorMessage}", eventName: eventName, functionName: binding.FunctionName)` |
| `EventBindingResult.Success(result.Value, eventName: eventName, functionName: binding.FunctionName, outputTarget: binding.Output)` | `EventTriggerResult.SuccessResult(result.Value, eventName: eventName, functionName: binding.FunctionName, outputTarget: binding.Output)` |
| `EventBindingResult.Failure($"事件触发异常: {ex.Message}", eventName: eventName)` | `EventTriggerResult.FailureResult($"事件触发异常: {ex.Message}", eventName: eventName)` |

#### EventBindingOrchestrator.cs（3处替换）

| 原调用 | 替换为 |
|---|---|
| `EventBindingResult.Failure($"事件触发失败: {...}", eventName: eventType)` | `EventTriggerResult.FailureResult($"事件触发失败: {...}", eventName: eventType)` |
| `EventBindingResult.Failure($"控件 '{...}' 未找到（...）", eventName: eventType)` | `EventTriggerResult.FailureResult($"控件 '{...}' 未找到（...）", eventName: eventType)` |
| `EventBindingResult.Success(value: value, eventName: eventType)` | `EventTriggerResult.SuccessResult(value: value, eventName: eventType)` |

#### EventTriggerServiceBase.cs（6处，无需修改工厂方法名，但需适配参数签名变化）

`EventTriggerServiceBase` 已使用 `Models.EventTriggerResult.SuccessResult/FailureResult`，工厂方法名不变。但由于 `controlName`/`eventType` 改为可选参数，原有调用方式（位置参数）仍然兼容，**无需修改**。

### 3.5 受影响文件

| 文件 | 变更类型 | 具体变更 |
|---|---|---|
| `Models/EventTriggerResult.cs` | **修改** | 工厂方法参数扩展（controlName/eventType 改为可选，新增 eventName 参数） |
| `Services/EventTriggerService.cs` | **修改** | 将 `EventBindingResult.Success/Failure` 替换为 `EventTriggerResult.SuccessResult/FailureResult`，调整参数映射 |
| `EventBindingOrchestrator.cs` | **修改** | 将 `EventBindingResult.Success/Failure` 替换为 `EventTriggerResult.SuccessResult/FailureResult`，调整参数映射 |
| `Services/EventBindingResult.cs` | **删除** | 整个文件删除 |

---

## 4. 整合二：事件触发服务体系

### 4.1 整合目标

以 `EventTriggerService`（`Services/EventTriggerService.cs`）为基础，整合 `EventTriggerServiceBase`（`Services/EventTriggerServiceBase.cs`）的能力，删除 `EventTriggerServiceBase`。

**选择 `EventTriggerService` 为基础的原因**：
1. 基于 `IEventSourceNode` 的通用设计，零 WinForms 依赖
2. 已实现 `TConfig.IEventTriggerService`（含字符串重载）
3. 使用 `IFunctionExecutor`（TConfig 类型）而非 `CommandExecutionResult<object>`（CommandFramework 类型），依赖更轻量
4. 已有 `IParameterBuilder<string>` 参数构建机制，比 `IStateStore` 更灵活

### 4.2 两个服务的管线流程对比

```
EventTriggerService.TriggerEventAsync(IEventSourceNode, string, object?)
  │
  ├─ 1. 查找绑定：_resolver.FindEventBinding(name, sourceType)
  ├─ 2. 构建参数：_parameterBuilder.Build(source, inputs, resolver)
  │     └─ 回退：parameters["value"] = eventArgs
  ├─ 3. 执行函数：_executor.ExecuteFunctionAsync(functionName, parameters)
  ├─ 4. 写入输出：_valueWriter.WriteAsync(source, output, value)
  └─ 5. 返回结果：EventBindingResult.Success/Failure

EventTriggerServiceBase.TriggerEventAsync(string, string, string, Dictionary?)
  │
  ├─ 1. 查找绑定：BindingResolver.FindEventBinding(controlName, eventType)
  ├─ 2. 提取输入值：directInputValues ?? ExtractInputValuesAsync(inputs)
  │     └─ ExtractInputValuesAsync: StateStore?.Get(input)
  ├─ 3. 执行函数：ExecuteFunctionAsync(functionName, inputValues) [abstract]
  │     └─ 多函数分支：ExecuteMultiFunctionBindingAsync()
  ├─ 4. 写入输出：WriteOutputAsync(output, value) [abstract]
  ├─ 5. 导出诊断快照：ExportDiagnosticSnapshot()
  └─ 6. 返回结果：EventTriggerResult.SuccessResult/FailureResult
```

### 4.3 整合策略

`EventTriggerServiceBase` 中有 4 类能力需要整合到 `EventTriggerService`：

| 能力 | 整合方式 | 说明 |
|---|---|---|
| `Abstractions.IEventTriggerService` 接口实现 | **新增接口声明 + 实现方法** | `TriggerEventAsync(string, string, string)` 方法 |
| `ExecuteMultiFunctionBindingAsync` | **迁移为私有方法** | 多函数绑定循环执行逻辑 |
| `ExportDiagnosticSnapshot` | **迁移为私有静态方法** | DiagnosticContext 快照导出 |
| `IStateStore` 依赖 + `ExtractInputValuesAsync` | **新增可选依赖 + 统一参数构建策略** | 作为 `IParameterBuilder<string>` 的回退方案 |

### 4.4 整合后的 `EventTriggerService` 设计

```csharp
// Services/EventTriggerService.cs
public class EventTriggerService
    : TConfig.IEventTriggerService,
      Share.EventBindingOrchestrator.Abstractions.IEventTriggerService  // ★ 新增接口
{
    // ── 已有依赖 ──
    private readonly IEventBindingResolver _resolver;
    private readonly ExecutorIFunctionExecutor _executor;
    private readonly IParameterBuilder<string>? _parameterBuilder;  // ★ 改为可选
    private readonly IValueWriter? _valueWriter;                    // ★ 改为可选

    // ── 新增依赖（从 EventTriggerServiceBase 整合）──
    private readonly CommandFramework.Abstractions.IStateStore? _stateStore;

    // ── 构造函数（扩展）──
    public EventTriggerService(
        IEventBindingResolver resolver,
        ExecutorIFunctionExecutor executor,
        IParameterBuilder<string>? parameterBuilder = null,  // ★ 改为可选
        IValueWriter? valueWriter = null,                    // ★ 改为可选
        CommandFramework.Abstractions.IStateStore? stateStore = null)  // ★ 新增
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _parameterBuilder = parameterBuilder;
        _valueWriter = valueWriter;
        _stateStore = stateStore;
    }

    // ── 已有方法（不变）──
    public async Task<IEventTriggerResult> TriggerEventAsync(
        IEventSourceNode source, string eventName, object? eventArgs) { ... }

    public async Task<IEventTriggerResult> TriggerEventAsync(
        string eventName, object? sender, object? args) { ... }

    public async Task<IEventTriggerResult> TriggerAndExecuteAsync(
        string eventName, object? sender, object? args) { ... }

    // ── ★ 新增方法：实现 Abstractions.IEventTriggerService ──
    public async Task<IEventTriggerResult> TriggerEventAsync(
        string controlName, string eventType, string value) { ... }

    // ── ★ 新增私有方法：从 EventTriggerServiceBase 迁移 ──
    private async Task<IEventTriggerResult> ExecuteMultiFunctionBindingAsync(
        string controlName, string eventType, string value,
        IEventBindingInfo binding,
        Dictionary<string, string?> inputValues,
        Stopwatch sw) { ... }

    private static void ExportDiagnosticSnapshot(
        string controlName, string eventType, string? functionName) { ... }

    // ── ★ 新增私有方法：统一参数构建 ──
    private Dictionary<string, object?> BuildParameters(
        IEventSourceNode source, IEventBindingInfo binding, object? eventArgs) { ... }
}
```

### 4.5 关键设计决策详解

#### 4.5.1 决策一：统一参数构建策略

`EventTriggerService` 使用 `IParameterBuilder<string>` 构建参数，`EventTriggerServiceBase` 使用 `IStateStore` 提取输入值。整合后采用**策略模式**：

```
参数构建策略：
  if (_parameterBuilder != null)  → 使用 IParameterBuilder<string>（优先）
  else if (_stateStore != null)   → 使用 IStateStore（回退）
  else                            → 使用 eventArgs 作为回退值
```

**为什么 `IParameterBuilder<string>` 优先？**
- 更灵活：支持从事件源节点解析输入值（`ResolveInputValue`）
- 更通用：不依赖 `IStateStore` 的键值对存储
- 更轻量：`IParameterBuilder<string>` 是 TConfig 类型，`IStateStore` 是 CommandFramework 类型

**为什么保留 `IStateStore` 回退？**
- `EventTriggerServiceBase` 的调用方可能已依赖 `IStateStore`
- 提供向后兼容性，无需修改现有 DI 注册

```csharp
private Dictionary<string, object?> BuildParameters(
    IEventSourceNode source, IEventBindingInfo binding, object? eventArgs)
{
    // 策略 1：使用 IParameterBuilder<string>（优先）
    if (_parameterBuilder != null)
    {
        var parameters = _parameterBuilder.Build(
            source, Array.Empty<string>(),
            (sender, sourceName) => ResolveInputValue(source, sourceName));
        if (parameters.Count == 0 && eventArgs != null)
            parameters["value"] = eventArgs;
        return parameters;
    }

    // 策略 2：使用 IStateStore（回退）
    var fallback = new Dictionary<string, object?>();
    if (_stateStore != null)
    {
        foreach (var input in binding.Inputs ?? Array.Empty<string>())
            fallback[input] = _stateStore.Get(input);
    }
    if (fallback.Count == 0 && eventArgs != null)
        fallback["value"] = eventArgs;
    return fallback;
}
```

#### 4.5.2 决策二：多函数绑定支持

`EventTriggerServiceBase` 的 `ExecuteMultiFunctionBindingAsync` 方法直接迁移到 `EventTriggerService`，在 `TriggerEventAsync(IEventSourceNode, ...)` 中增加多函数分支：

```csharp
// 在 TriggerEventAsync(IEventSourceNode, string, object?) 中
if (binding.FunctionNames.Count > 1)
{
    return await ExecuteMultiFunctionBindingAsync(
        sourceName, eventName, eventArgs, binding, parameters, sw);
}
```

**迁移要点**：
- 方法签名中的 `Dictionary<string, string?>` 改为 `Dictionary<string, object?>`（与 `EventTriggerService` 的参数类型一致）
- 方法体中的 `CommandExecutionResult<object>` 改为 `IFunctionExecutionResult`（与 `EventTriggerService` 的执行器返回类型一致）
- 结果创建使用 `EventTriggerResult.SuccessResult/FailureResult`（已统一）

#### 4.5.3 决策三：诊断快照导出

`EventTriggerServiceBase.ExportDiagnosticSnapshot` 作为静态方法直接迁移到 `EventTriggerService`，在 `TriggerEventAsync` 执行完成后调用：

```csharp
// 在 TriggerEventAsync(IEventSourceNode, string, object?) 执行完成后
ExportDiagnosticSnapshot(sourceName, eventName, binding.FunctionName);
```

#### 4.5.4 决策四：构造函数参数改为可选

当前 `EventTriggerService` 构造函数中 `_parameterBuilder` 和 `_valueWriter` 是**必填**的（`throw` if null）。整合后改为**可选**，以兼容 `EventTriggerServiceBase` 的调用方（它们可能不注入这些依赖）。

**影响分析**：
- 当 `_parameterBuilder` 为 null 时，`BuildParameters` 回退到 `IStateStore` 或 `eventArgs`
- 当 `_valueWriter` 为 null 时，`TriggerEventAsync` 跳过写入输出步骤
- 现有调用方（已注入 `_parameterBuilder` 和 `_valueWriter`）不受影响

#### 4.5.5 决策五：`TriggerEventAsync(string, string, string)` 实现

新增的 `controlName` 重载方法实现 `Abstractions.IEventTriggerService` 接口：

```csharp
public async Task<IEventTriggerResult> TriggerEventAsync(
    string controlName, string eventType, string value)
{
    var sw = Stopwatch.StartNew();
    try
    {
        // 1. 查找事件绑定
        var binding = _resolver.FindEventBinding(controlName, eventType);
        if (binding == null)
        {
            sw.Stop();
            return EventTriggerResult.FailureResult(
                $"控件 '{controlName}' 未找到事件绑定 (eventType={eventType})",
                controlName, eventType, executionTime: sw.Elapsed);
        }

        // 2. 构建参数（使用统一参数构建策略）
        var parameters = BuildParameters(controlName, binding, value);

        // 3. 多函数绑定分支
        if (binding.FunctionNames.Count > 1)
        {
            return await ExecuteMultiFunctionBindingAsync(
                controlName, eventType, value, binding, parameters, sw);
        }

        // 4. 执行绑定的函数（单函数）
        var functionResult = await _executor.ExecuteFunctionAsync(binding.FunctionName, parameters);
        if (!functionResult.IsSuccess)
        {
            sw.Stop();
            return EventTriggerResult.FailureResult(
                $"函数 '{binding.FunctionName}' 执行失败: {functionResult.ErrorMessage}",
                controlName, eventType, binding.FunctionName, sw.Elapsed);
        }

        // 5. 写入输出
        if (!string.IsNullOrEmpty(binding.Output) && _valueWriter != null)
        {
            await _valueWriter.WriteAsync(null!, binding.Output, functionResult.Value);
        }

        sw.Stop();
        ExportDiagnosticSnapshot(controlName, eventType, binding.FunctionName);

        return EventTriggerResult.SuccessResult(
            functionResult.Value, controlName, eventType,
            binding.FunctionName, binding.Output, sw.Elapsed);
    }
    catch (Exception ex)
    {
        sw.Stop();
        return EventTriggerResult.FailureResult(
            $"事件触发异常: {ex.Message}", controlName, eventType, executionTime: sw.Elapsed);
    }
}
```

### 4.6 受影响文件

| 文件 | 变更类型 | 具体变更 |
|---|---|---|
| `Services/EventTriggerService.cs` | **修改** | 添加 `IStateStore?` 依赖；添加 `Abstractions.IEventTriggerService` 接口实现；添加多函数绑定支持；添加诊断快照导出；统一参数构建策略；构造函数参数改为可选 |
| `Services/EventTriggerServiceBase.cs` | **删除** | 整个文件删除 |
| `Abstractions/IEventTriggerService.cs` | **保留** | 接口仍被 `EventTriggerService` 实现，保留以保持接口体系完整 |

---

## 5. 整合后的完整体系

### 5.1 事件触发结果体系（整合后）

```
┌─────────────────────────────────────────────────────────────┐
│                    TConfig.IEventTriggerResult               │
│  (TConfig/Abstractions/IEventTriggerResult.cs)               │
│  IsSuccess, ErrorMessage, Value                             │
└─────────────────────────────────────────────────────────────┘
              │ 继承
              ▼
┌─────────────────────────────────────────────────────────────┐
│              Modules.IEventTriggerResult                     │
│  (Modules/EventFramework/Services/IEventTriggerService.cs)   │
│  空接口                                                      │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│          Abstractions.IEventTriggerResult                    │
│  (Share/EventBindingOrchestrator/Abstractions/               │
│   IEventTriggerResult.cs)                                    │
│  IsSuccess, Value, ErrorMessage, ExecutionTime,             │
│  Message, ControlName, EventType, FunctionName,             │
│  OutputTarget, IsWorkflowSwitchEvent                        │
└─────────────────────────────────────────────────────────────┘
              │ 全部实现
              ▼
┌─────────────────────────────────────────────────────────────┐
│              EventTriggerResult  ★ 统一结果类                 │
│  (Models/EventTriggerResult.cs)                              │
│  继承: TConfig.EventResult                                   │
│  实现: TConfig.IEventTriggerResult（通过基类）               │
│        Modules.IEventTriggerResult                           │
│        Abstractions.IEventTriggerResult                      │
│                                                              │
│  IsSuccess, Value, ErrorMessage, ExecutionTime,             │
│  EventName, FunctionName, OutputTarget,                     │
│  ControlName, EventType, Message, IsWorkflowSwitchEvent     │
│                                                              │
│  + SuccessResult() / FailureResult() 工厂方法                │
└─────────────────────────────────────────────────────────────┘
```

### 5.2 事件触发服务体系（整合后）

```
┌─────────────────────────────────────────────────────────────┐
│              TConfig.IEventTriggerService                    │
│  TriggerEventAsync(string, object?, object?)                │
│  TriggerAndExecuteAsync(string, object?, object?)           │
└─────────────────────────────────────────────────────────────┘
              │ 实现
              ▼
┌─────────────────────────────────────────────────────────────┐
│          Abstractions.IEventTriggerService                   │
│  TriggerEventAsync(string, string, string)                  │
└─────────────────────────────────────────────────────────────┘
              │ 实现
              ▼
┌─────────────────────────────────────────────────────────────┐
│              EventTriggerService  ★ 统一服务类                │
│  (Services/EventTriggerService.cs)                           │
│  实现: TConfig.IEventTriggerService                          │
│        Abstractions.IEventTriggerService                     │
│                                                              │
│  依赖: IEventBindingResolver                                │
│        IFunctionExecutor                                    │
│        IParameterBuilder<string> (可选)                     │
│        IValueWriter (可选)                                  │
│        IStateStore (可选)                                   │
│                                                              │
│  方法:                                                      │
│  ├─ TriggerEventAsync(IEventSourceNode, string, object?)    │
│  │   → 查找绑定 → 构建参数 → 执行函数 → 写入输出           │
│  │   → 支持多函数绑定分支                                   │
│  │   → 导出诊断快照                                         │
│  ├─ TriggerEventAsync(string, object?, object?)             │
│  │   → 字符串重载，通过名称查找绑定                         │
│  ├─ TriggerAndExecuteAsync(string, object?, object?)        │
│  │   → 委托给 TriggerEventAsync                             │
│  ├─ TriggerEventAsync(string, string, string)               │
│  │   → controlName 重载，实现 Abstractions 接口             │
│  └─ (private) ExecuteMultiFunctionBindingAsync()            │
│      → 多函数绑定循环执行                                   │
└─────────────────────────────────────────────────────────────┘
```

### 5.3 文件结构变化

```
CsharpPrj/Share/EventBindingOrchestrator/
├── Abstractions/
│   ├── IEventTriggerResult.cs          ← 保留（接口仍被实现）
│   └── IEventTriggerService.cs         ← 保留（接口仍被实现）
├── Models/
│   └── EventTriggerResult.cs           ← 修改（扩展工厂方法参数）★
├── Services/
│   ├── EventBindingResult.cs           ← 删除 ★
│   ├── EventTriggerService.cs          ← 修改（整合能力）★
│   └── EventTriggerServiceBase.cs      ← 删除 ★
└── ...其他文件不变
```

---

## 6. 执行计划

### 6.1 步骤一：扩展 EventTriggerResult 工厂方法

**文件**：`Models/EventTriggerResult.cs`

**变更**：
1. `SuccessResult()` 方法：`controlName`/`eventType` 改为可选参数，新增 `eventName` 可选参数
2. `FailureResult()` 方法：`controlName`/`eventType` 改为可选参数，新增 `eventName` 可选参数
3. 当 `eventName` 未提供时，使用 `eventType` 作为回退值

### 6.2 步骤二：修改 EventTriggerService 引用

**文件**：`Services/EventTriggerService.cs`

**变更**：
1. 将所有 `EventBindingResult.Success(...)` 替换为 `EventTriggerResult.SuccessResult(...)`
2. 将所有 `EventBindingResult.Failure(...)` 替换为 `EventTriggerResult.FailureResult(...)`
3. 调整参数映射以匹配新的工厂方法签名（详见 3.4 节替换映射表）

### 6.3 步骤三：修改 EventBindingOrchestrator 引用

**文件**：`EventBindingOrchestrator.cs`

**变更**：
1. 将所有 `EventBindingResult.Success(...)` 替换为 `EventTriggerResult.SuccessResult(...)`
2. 将所有 `EventBindingResult.Failure(...)` 替换为 `EventTriggerResult.FailureResult(...)`
3. 调整参数映射以匹配新的工厂方法签名（详见 3.4 节替换映射表）

### 6.4 步骤四：扩展 EventTriggerService

**文件**：`Services/EventTriggerService.cs`

**变更**：
1. 添加 `IStateStore?` 可选依赖
2. 添加 `Abstractions.IEventTriggerService` 接口声明
3. 实现 `TriggerEventAsync(string controlName, string eventType, string value)` 方法
4. 迁移 `ExecuteMultiFunctionBindingAsync` 方法（从 `EventTriggerServiceBase`）
5. 迁移 `ExportDiagnosticSnapshot` 方法（从 `EventTriggerServiceBase`）
6. 统一参数构建策略（`IParameterBuilder<string>` 优先，`IStateStore` 回退）
7. 在 `TriggerEventAsync(IEventSourceNode, ...)` 中增加多函数绑定分支
8. 构造函数中 `_parameterBuilder`/`_valueWriter` 改为可选

### 6.5 步骤五：删除废弃文件

**删除**：
1. `Services/EventBindingResult.cs`
2. `Services/EventTriggerServiceBase.cs`

### 6.6 步骤六：编译验证

```bash
dotnet build "d:\Project\Program\WorkFlowPrj\CsharpPrj\MainProj.sln"
```

---

## 7. 附录：文件清单与调用链

### 7.1 涉及的所有文件

| # | 文件路径 | 角色 | 行数 |
|---|---|---|---|
| 1 | `CsharpPrj/Share/EventBindingOrchestrator/Models/EventTriggerResult.cs` | 保留并扩展 | 70 |
| 2 | `CsharpPrj/Share/EventBindingOrchestrator/Services/EventTriggerService.cs` | 保留并扩展 | 234 |
| 3 | `CsharpPrj/Share/EventBindingOrchestrator/Services/EventBindingResult.cs` | 删除 | 82 |
| 4 | `CsharpPrj/Share/EventBindingOrchestrator/Services/EventTriggerServiceBase.cs` | 删除 | 243 |
| 5 | `CsharpPrj/Share/EventBindingOrchestrator/EventBindingOrchestrator.cs` | 修改引用 | 815 |
| 6 | `CsharpPrj/Share/EventBindingOrchestrator/Abstractions/IEventTriggerResult.cs` | 保留 | 27 |
| 7 | `CsharpPrj/Share/EventBindingOrchestrator/Abstractions/IEventTriggerService.cs` | 保留 | 19 |

### 7.2 接口定义文件（供参考，不修改）

| # | 文件路径 | 说明 |
|---|---|---|
| 8 | `CsharpPrj/Plate/TConfig/Abstractions/IEventTriggerResult.cs` | 基础结果接口 |
| 9 | `CsharpPrj/Plate/TConfig/Abstractions/IEventTriggerService.cs` | 服务接口（字符串重载） |
| 10 | `CsharpPrj/Plate/TConfig/Models/EventResult.cs` | 基础结果实现 |
| 11 | `CsharpPrj/Modules/EventFramework/Services/IEventTriggerService.cs` | Modules 服务接口 + 结果接口 |

### 7.3 调用链关系图（整合前 → 整合后）

```
整合前：
EventBindingOrchestrator
  └─ TriggerEventAsync(controlName, eventType, value)
       ├─ EventBindingResult.Success/Failure  ← 使用 EventBindingResult
       └─ (管线执行，不直接调用 EventTriggerService)

EventTriggerService
  ├─ TriggerEventAsync(IEventSourceNode, ...)
  │    └─ EventBindingResult.Success/Failure  ← 使用 EventBindingResult
  ├─ TriggerEventAsync(string, object?, object?)
  │    └─ EventBindingResult.Success/Failure  ← 使用 EventBindingResult
  └─ TriggerAndExecuteAsync(string, object?, object?)
       └─ EventBindingResult.Success/Failure  ← 使用 EventBindingResult

EventTriggerServiceBase (abstract, 无子类)
  └─ TriggerEventAsync(string, string, string)
       └─ EventTriggerResult.SuccessResult/FailureResult  ← 使用 EventTriggerResult

整合后：
EventBindingOrchestrator
  └─ TriggerEventAsync(controlName, eventType, value)
       └─ EventTriggerResult.SuccessResult/FailureResult  ← 统一使用 EventTriggerResult

EventTriggerService  ★ 统一服务
  ├─ TriggerEventAsync(IEventSourceNode, ...)
  │    └─ EventTriggerResult.SuccessResult/FailureResult  ← 统一使用 EventTriggerResult
  ├─ TriggerEventAsync(string, object?, object?)
  │    └─ EventTriggerResult.SuccessResult/FailureResult  ← 统一使用 EventTriggerResult
  ├─ TriggerAndExecuteAsync(string, object?, object?)
  │    └─ EventTriggerResult.SuccessResult/FailureResult  ← 统一使用 EventTriggerResult
  └─ TriggerEventAsync(string, string, string)  ★ 新增
       └─ EventTriggerResult.SuccessResult/FailureResult  ← 统一使用 EventTriggerResult
```

### 7.4 整合前后对比总结

| 维度 | 整合前 | 整合后 | 变化 |
|---|---|---|---|
| 结果模型类 | 2 个（EventBindingResult + EventTriggerResult） | 1 个（EventTriggerResult） | -1 |
| 服务类 | 2 个（EventTriggerService + EventTriggerServiceBase） | 1 个（EventTriggerService） | -1 |
| 接口实现 | 分散在 2 个服务类 | 集中在 1 个服务类 | 统一 |
| 工厂方法 | 2 套（Success/Failure + SuccessResult/FailureResult） | 1 套（SuccessResult/FailureResult） | 统一 |
| 参数构建 | 2 套（IParameterBuilder + IStateStore） | 1 套（策略模式，IParameterBuilder 优先） | 统一 |
| 多函数绑定 | 仅 EventTriggerServiceBase 支持 | EventTriggerService 支持 | 能力保留 |
| 诊断快照 | 仅 EventTriggerServiceBase 支持 | EventTriggerService 支持 | 能力保留 |
| 文件总数 | 7 个（含接口） | 5 个（含接口） | -2 |
| 代码行数 | ~1490 行 | ~1100 行（预估） | -390 行 |
