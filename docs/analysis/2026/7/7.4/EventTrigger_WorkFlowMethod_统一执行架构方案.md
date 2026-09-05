# EventTrigger 与 WorkFlowMethod 统一执行架构方案

> **基于**：`MathFunctions_DataFlow_Analysis.md` 数据流分析  
> **问题**：EventTrigger 事件绑定直接调用与 WorkFlowMethod 工作流管道两套执行路径并存  
> **目标**：废弃直接调用路径，全部统一走 WorkFlowMethod 工作流管道  
> **日期**：2026-07-04

---

## 目录

1. [现状分析](#1-现状分析)
2. [问题定义](#2-问题定义)
3. [目标架构](#3-目标架构)
4. [详细设计](#4-详细设计)
5. [实施步骤](#5-实施步骤)
6. [风险与对策](#6-风险与对策)
7. [附录](#7-附录)

---

## 1. 现状分析

### 1.1 当前两套执行路径

```
用户点击按钮
       │
       ▼
EventBinderTransformer.CreateEventHandler()
       │
       ├──────────────────────────────────────────────┐
       │                                              │
       ▼                                              ▼
┌──────────────────┐                    ┌──────────────────────────┐
│  路径 A：直接调用  │                    │  路径 B：工作流管道（未启用）│
│                  │                    │                          │
│ IEventExecution- │                    │ WorkFlowMethodUsage.     │
│ Service          │                    │ CallBridgedFunction()    │
│       │          │                    │       │                  │
│       ▼          │                    │       ▼                  │
│ IFunctionRegistry│                    │ IWorkflowRegistry        │
│ .TryGetDelegate  │                    │ .HasFunction()?          │
│       │          │                    │       │                  │
│       ▼          │                    │  Yes  ▼  No             │
│ DynamicInvoke    │                    │ ┌────────┐ ┌──────────┐  │
│                  │                    │ │工作流  │ │回退到普通 │  │
│                  │                    │ │执行    │ │函数执行   │  │
└──────────────────┘                    └──────────────────────────┘
```

### 1.2 路径 A：直接调用（当前实际路径）

**调用链**：

```
MathFunctions.json 配置
  → EventBinderTransformer.CreateEventHandler()          // EventBindingTransformers.cs:389
    → AppServiceProvider.Resolve<IEventExecutionService>() // 运行时解析
      → EventExecutionService.ExecuteFunctionAsync()      // EventExecutionService.cs:67
        → IFunctionRegistry.TryGetDelegate("Add")         // 查找委托
          → Delegate.DynamicInvoke(parameters)            // 直接执行
```

**关键代码**（EventBindingTransformers.cs 第 426-434 行）：

```csharp
var resolvedService = FrameworkAbstractions.ComponentRegistry.AppServiceProvider
    .Resolve<IEventExecutionService>();
var execResult = await resolvedService.ExecuteFunctionAsync(functionName, parameters);
```

**关键代码**（EventExecutionService.cs 第 67-132 行）：

```csharp
public async Task<ExecutionResult> ExecuteFunctionAsync(string functionName, object?[]? parameters)
{
    if (!_functionRegistry.TryGetDelegate(functionName, out var del))
        return ExecutionResult.Fail($"函数 '{functionName}' 未注册");
    
    var result = del.DynamicInvoke(convertedParams);
    // ...
}
```

### 1.3 路径 B：工作流管道（已存在但未启用）

**调用链**：

```
WorkFlowMethodUsage.CallBridgedFunctionAsync("Add", context)  // WorkflowMethodUseage.cs:321
  → IWorkflowRegistry.HasFunction("Add")?                     // 检查是否工作流函数
    ├─ Yes → IWorkflowRegistry.Call("Add", context)           // 走工作流管道
    │         → PipelineExecutor / WorkflowEngine
    │           → ExecutionOrchestrator.OrchestrateAsync()     // ExecutionOrchestrator.cs:46
    │             → CentralPipeline.ExecuteAsync()             // 管线执行
    │               → NodeExecutor.ExecuteNodeAsync()          // NodeExecutor.cs:35
    │                 → FunctionExecutor.ExecuteAsync()        // 最终执行
    └─ No  → 回退到 FunctionExecutor.ExecuteAsync()            // 普通函数执行
```

### 1.4 两套路径对比

| 维度 | 路径 A：直接调用 | 路径 B：工作流管道 |
|------|-----------------|-------------------|
| **入口** | `IEventExecutionService` | `WorkFlowMethodUsage` |
| **注册表** | `IFunctionRegistry` | `IWorkflowRegistry` |
| **执行方式** | `DynamicInvoke` | `NodeExecutor` → `FunctionExecutor` |
| **参数处理** | 手动类型转换（string→double） | 通过 `NodeDefinition.Parameters` |
| **弹性策略** | 无 | Polly 重试 + 超时 |
| **节点类型** | 仅函数调用 | Function / Condition / Loop / Parallel |
| **会话管理** | 无 | 暂停 / 恢复 / 取消 |
| **日志追踪** | 简单 Debug 日志 | `IWorkflowLogger` 结构化日志 |
| **代码行数** | ~140 行（EventExecutionService） | ~378 行（NodeExecutor）+ 管线系统 |

---

## 2. 问题定义

### 2.1 核心问题

**两套执行路径并存导致架构复杂度翻倍**：

1. **两套注册表**：`IFunctionRegistry`（直接调用用）和 `IWorkflowRegistry`（工作流用），函数需要分别在两处注册
2. **两套状态管理**：直接调用无状态管理，工作流有完整的会话管理，行为不一致
3. **两套参数处理**：直接调用在 `EventExecutionService` 中手动类型转换，工作流在 `NodeExecutor` 中通过 `FunctionExecutor` 处理
4. **维护成本**：修改执行逻辑需要同时修改两套代码

### 2.2 根因分析

`EventBinderTransformer.CreateEventHandler()` 在运行时通过 `AppServiceProvider.Resolve<IEventExecutionService>()` 解析执行服务，而 `IEventExecutionService` 的实现 `EventExecutionService` 直接通过 `IFunctionRegistry.TryGetDelegate` → `DynamicInvoke` 执行，**完全没有经过 WorkFlowMethod 管道**。

### 2.3 为什么可以统一

工作流引擎的 `NodeExecutor.ExecuteFunctionNodeAsync()` 本质上也是调用 `FunctionExecutor.ExecuteAsync()`，与直接调用的底层执行路径一致。**单步执行走工作流没有额外的性能开销**——只是多了 `NodeDefinition` 和 `IWorkflowContext` 两个轻量级包装对象。

---

## 3. 目标架构

### 3.1 统一后的执行路径

```
JSON 配置 { "functionName": "Add" }
  → EventBinderTransformer.CreateEventHandler()
    → WorkFlowMethodUsage.CallBridgedFunctionAsync("Add", context)
      → IWorkflowRegistry.HasFunction("Add")?
        ├─ Yes → IWorkflowRegistry.Call("Add", context)
        │         → PipelineExecutor / WorkflowEngine
        │           → NodeExecutor.ExecuteFunctionNodeAsync()
        │             → FunctionExecutor.ExecuteAsync()
        └─ No  → FunctionExecutor.ExecuteAsync()  // 自动包装为单步执行
```

### 3.2 架构变化

```
                    ┌───────────────┐
                    │  JSON 配置     │
                    └───────┬───────┘
                            │
                    ┌───────▼───────┐
                    │ EventBinder-  │
                    │ Transformer   │
                    └───────┬───────┘
                            │
              ┌─────────────┴─────────────┐
              │                           │
              ▼                           ▼
    ┌─────────────────┐       ┌──────────────────────┐
    │ 当前：直接调用    │       │ 目标：统一工作流管道   │
    │                 │       │                      │
    │ IEventExecution │       │ WorkFlowMethodUsage  │
    │ Service         │       │ .CallBridgedFunction │
    │       │         │       │          │           │
    │       ▼         │       │          ▼           │
    │ DynamicInvoke   │       │ IWorkflowRegistry    │
    │                 │       │ / FunctionExecutor   │
    └─────────────────┘       └──────────────────────┘
              │                           │
              └───────────┬───────────────┘
                          │
                          ▼
              ┌──────────────────────┐
              │  统一底层执行          │
              │  FunctionExecutor    │
              │  .ExecuteAsync()     │
              └──────────────────────┘
```

### 3.3 组件职责变化

| 组件 | 当前职责 | 目标职责 |
|------|---------|---------|
| `EventExecutionService` | 事件执行入口（直接调用） | **废弃** |
| `EventBinderTransformer` | 创建事件处理器，调用 `IEventExecutionService` | 创建事件处理器，调用 `WorkFlowMethodUsage` |
| `WorkFlowMethodUsage` | 工作流统一入口（可选） | **唯一执行入口** |
| `FunctionStorageRegistry` | 函数注册 + 执行门面 | 仅保留注册职责，执行委托给 `WorkFlowMethodUsage` |
| `IFunctionRegistry` | 函数注册表（直接调用用） | 作为 `IWorkflowRegistry` 的底层存储 |
| `IWorkflowRegistry` | 工作流函数注册表 | **统一注册表**，包装 `IFunctionRegistry` |

---

## 4. 详细设计

### 4.1 修改 EventBinderTransformer.CreateEventHandler()

**文件**：`CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingTransformers.cs`

**当前代码**（第 426-434 行）：

```csharp
var resolvedService = FrameworkAbstractions.ComponentRegistry.AppServiceProvider
    .Resolve<IEventExecutionService>();
if (resolvedService == null)
{
    EventTriggerLog.Log("EventBinder_ExecutionServiceNull", ...);
    return;
}
var execResult = await resolvedService.ExecuteFunctionAsync(functionName, parameters);
```

**目标代码**：

```csharp
// 统一走 WorkFlowMethod 工作流管道
// WorkFlowMethodUsage.CallBridgedFunctionAsync 自动处理：
//   1. 工作流函数 → 走工作流管道
//   2. 普通函数 → 自动包装为单步执行
var wfContext = new WfContext(
    parameters: BuildParametersDictionary(ctrl, valueAccessor),
    data: new Dictionary<string, object>(),
    executionId: Guid.NewGuid().ToString(),
    nodeKey: functionName,
    workflowId: null
);

var result = await WorkFlowMethodUsage.CallBridgedFunctionAsync(functionName, wfContext);

// 将结果写入 output 控件
WriteOutputToControl(ctrl, result, valueAccessor);
```

**关键变化**：
- 不再通过 `AppServiceProvider` 解析 `IEventExecutionService`
- 直接调用 `WorkFlowMethodUsage.CallBridgedFunctionAsync()`
- 参数从 `object?[]` 改为 `WfContext.Parameters`（命名参数字典）
- 返回值从 `ExecutionResult` 改为 `object`（工作流返回值）

### 4.2 废弃 EventExecutionService

**文件**：`CsharpPrj/Operation/BackEndDriven/Execution/EventExecutionService.cs`

**操作**：删除整个文件（~143 行）

**影响分析**：
- `BackEndDrivenBuilder.RegisterEventExecutionService()` 中创建 `EventExecutionService` 的代码需要删除
- `IEventExecutionService` 接口本身保留（可能被其他组件引用），但不再有实现类
- 如果 `IEventExecutionService` 无其他实现，接口本身也应废弃

**文件**：`CsharpPrj/Operation/BackEndDriven/BackEndDrivenBuilder.cs`

**当前代码**：

```csharp
public static void RegisterEventExecutionService()
{
    var functionRegistry = FrameworkAbstractions.ComponentRegistry.AppServiceProvider
        .Resolve<IFunctionRegistry>();
    if (functionRegistry != null)
    {
        var eventExecutionService = new BackEndDriven.Execution.EventExecutionService(functionRegistry);
        FrameworkAbstractions.ComponentRegistry.AppServiceProvider
            .Register<Share.EventBindingOrchestrator.Abstractions.IEventExecutionService>(eventExecutionService);
    }
}
```

**目标代码**：删除此方法及其所有调用点。

### 4.3 统一注册表

**问题**：当前 `FunctionStorageLifecycle.Initialize()` 将函数注册到 `FunctionRegistry`（`IFunctionRegistry`），但 `WorkFlowMethodUsage` 检查的是 `IWorkflowRegistry`。如果函数只注册到 `IFunctionRegistry` 而未注册到 `IWorkflowRegistry`，则 `CallBridgedFunction` 会走回退路径（普通函数执行），而不是工作流管道。

**解决方案**：让 `IWorkflowRegistry` 包装 `IFunctionRegistry`，使所有已注册函数自动对工作流可见。

**文件**：`CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodUseage.cs`

在 `Initialize()` 中建立包装关系：

```csharp
public static void Initialize(
    IWorkflowRegistry? workflowRegistry = null,
    BmIFunctionRegistry? basicRegistry = null)
{
    lock (_initLock)
    {
        if (_initialized) return;

        _basicRegistry = basicRegistry;

        if (workflowRegistry != null)
        {
            _workflowRegistry = workflowRegistry;
        }
        else if (basicRegistry != null)
        {
            // ★ 关键修改：创建包装器，使 IFunctionRegistry 中的函数对 IWorkflowRegistry 可见
            _workflowRegistry = new WorkflowRegistryWrapper(basicRegistry);
        }
        else
        {
            _workflowRegistry = Fef.Registry;
        }

        _pipelineExecutor = new PipelineExecutor(_workflowRegistry);
        _functionExecutor = new FunctionExecutor(_workflowRegistry);
        _initialized = true;
    }
}
```

**新增包装器** `WorkflowRegistryWrapper`：

```csharp
/// <summary>
/// IWorkflowRegistry 包装器 — 将 IFunctionRegistry 包装为 IWorkflowRegistry，
/// 使所有已注册函数对工作流可见，无需重复注册。
/// </summary>
internal class WorkflowRegistryWrapper : IWorkflowRegistry
{
    private readonly BmIFunctionRegistry _inner;

    public WorkflowRegistryWrapper(BmIFunctionRegistry inner)
    {
        _inner = inner;
    }

    public bool HasFunction(string name) => _inner.Contains(name);

    public object Call(string name, WfContext context)
    {
        // 从 WfContext.Parameters 提取参数
        var parameters = context.Parameters.Count > 0
            ? context.Parameters.Values.ToArray()
            : null;
        
        // 通过 FunctionExecutor 执行
        var result = Task.Run(async () =>
            await _functionExecutor!.ExecuteAsync(name, parameters, ...))
            .GetAwaiter().GetResult();

        return result.Value!;
    }

    // ... 其他接口实现
}
```

### 4.4 修改参数传递方式

**当前**：`EventBinderTransformer.BuildParametersFromBindings()` 返回 `object?[]`（位置参数数组）

**目标**：改为 `Dictionary<string, object>`（命名参数字典），与 `WfContext.Parameters` 兼容

**文件**：`CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingTransformers.cs`

```csharp
private static Dictionary<string, object> BuildParametersFromBindings(
    Control ctrl, IControlValueAccessor? valueAccessor)
{
    var parameters = new Dictionary<string, object>();
    
    if (ctrl.Tag is Dictionary<string, object> tag &&
        tag.TryGetValue("inputs", out var inputsObj) &&
        inputsObj is List<string> inputNames)
    {
        var form = ctrl.FindForm();
        foreach (var inputName in inputNames)
        {
            object? inputValue = null;
            if (form != null)
            {
                var inputControl = WinFormsControlService.Instance.FindByName(form, inputName);
                if (inputControl != null)
                {
                    inputValue = valueAccessor != null
                        ? valueAccessor.GetValue(inputControl)
                        : (inputControl is Control ic ? ic.Text : null);
                }
            }
            parameters[inputName] = inputValue?.ToString() ?? string.Empty;
        }
    }
    
    return parameters;
}
```

### 4.5 修改 WriteOutputToControl

**当前**：从 `Control.Tag` 读取 `output` 绑定信息，将结果写入指定控件

**目标**：从 `WorkFlowMethodUsage.CallBridgedFunctionAsync()` 的返回值中提取结果并写入

```csharp
private static void WriteOutputToControl(Control ctrl, object? resultValue, 
    IControlValueAccessor? valueAccessor)
{
    if (ctrl.Tag is Dictionary<string, object> tag &&
        tag.TryGetValue("output", out var outputObj) &&
        outputObj is string outputName &&
        !string.IsNullOrWhiteSpace(outputName))
    {
        var form = ctrl.FindForm();
        if (form != null)
        {
            var outputControl = WinFormsControlService.Instance.FindByName(form, outputName);
            if (outputControl != null)
            {
                var resultText = resultValue?.ToString() ?? string.Empty;
                if (valueAccessor != null)
                    valueAccessor.SetValue(outputControl, resultText);
                else if (outputControl is Control oc)
                    oc.Text = resultText;
            }
        }
    }
}
```

### 4.6 修改 FunctionStorageLifecycle

**文件**：`CsharpPrj/Operation/FunctionStorage/Framework/FunctionStorageRegistry.cs`

**当前**：`FunctionStorageLifecycle.Initialize()` 只注册到 `FunctionRegistry`

**目标**：注册时同时确保函数对 `IWorkflowRegistry` 可见

```csharp
public void Initialize()
{
    if (_initialized) return;

    // 1. 同步到静态门面
    FunctionStorageRegistry.Initialize(_registry, _executor);

    // 2. 注册 FunctionStorage 自身内部的函数
    Registrations.MathFunctionRegistrar.RegisterAll(_registry);

    // ★ 3. 确保 WorkFlowMethod 使用同一注册表
    //    如果 WorkFlowMethodUsage 尚未初始化，设置默认注册表提供者
    if (!WorkFlowMethodUsage.IsInitialized)
    {
        WorkFlowMethodUsage.DefaultBasicRegistryProvider = () => _registry;
    }

    _initialized = true;
}
```

---

## 5. 实施步骤

### 5.1 实施顺序

```
阶段一：准备（P0）
  ├─ 5.1.1 确认 EventTriggerFramework 对 WorkFlowMethod 的引用可行性
  ├─ 5.1.2 创建 WorkflowRegistryWrapper 包装器
  └─ 5.1.3 修改 FunctionStorageLifecycle 确保注册表共享

阶段二：修改 EventBinderTransformer（P1）
  ├─ 5.2.1 修改 BuildParametersFromBindings() 返回 Dictionary
  ├─ 5.2.2 修改 CreateEventHandler() 调用 WorkFlowMethodUsage
  └─ 5.2.3 修改 WriteOutputToControl() 适配新返回值

阶段三：废弃旧代码（P2）
  ├─ 5.3.1 删除 EventExecutionService.cs
  ├─ 5.3.2 删除 RegisterEventExecutionService() 及相关调用
  └─ 5.3.3 确认 IEventExecutionService 接口无其他引用

阶段四：测试验证（P3）
  ├─ 5.4.1 测试单步函数（Add/Subtract/Multiply/Divide）
  ├─ 5.4.2 测试 ConfigSelector 加载
  ├─ 5.4.3 测试复杂工作流（多步编排）
  └─ 5.4.4 回归测试所有 JSON 配置
```

### 5.2 详细步骤

#### 5.2.1 确认引用可行性

检查 `EventTriggerFramework.csproj` 是否已引用 `WorkFlowMethod` 项目：

```bash
# 检查 EventTriggerFramework 的项目引用
grep -i "WorkFlowMethod" CsharpPrj/Operation/EventTriggerFramework/EventTriggerFramework.csproj
```

如果不存在引用，需要添加：

```xml
<ProjectReference Include="..\WorkFlowMethod\WorkFlowMethod.csproj" />
```

同时检查是否存在循环依赖：
- `EventTriggerFramework` → `WorkFlowMethod` → `BackEndDriven` → `EventTriggerFramework`?

如果存在循环依赖，需要通过接口抽象层（Share 层）解耦。

#### 5.2.2 修改 EventBinderTransformer

**文件**：`CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingTransformers.cs`

修改范围：
- `CreateEventHandler()` 方法（第 389-463 行）
- `BuildParametersFromBindings()` 方法（第 471-508 行）
- `WriteOutputToControl()` 方法（第 514-540 行）

#### 5.2.3 废弃 EventExecutionService

**文件**：
- 删除 `CsharpPrj/Operation/BackEndDriven/Execution/EventExecutionService.cs`
- 修改 `CsharpPrj/Operation/BackEndDriven/BackEndDrivenBuilder.cs`，删除 `RegisterEventExecutionService()` 方法及其调用

#### 5.2.4 测试验证

```bash
# 构建项目
dotnet build CsharpPrj/MainProj.sln

# 运行测试
dotnet test CsharpPrj/MainProj.sln
```

---

## 6. 风险与对策

### 6.1 循环依赖风险

| 风险 | 概率 | 影响 | 对策 |
|------|------|------|------|
| EventTriggerFramework ↔ WorkFlowMethod 循环引用 | 中 | 高 | 通过 Share 层接口解耦，或使用 `WorkFlowMethodUsage` 静态类（无项目引用） |

**检测方法**：

```bash
# 检查 EventTriggerFramework.csproj 的 ProjectReference
cat CsharpPrj/Operation/EventTriggerFramework/EventTriggerFramework.csproj | grep ProjectReference

# 检查 WorkFlowMethod.csproj 的 ProjectReference
cat CsharpPrj/Operation/WorkFlowMethod/WorkFlowMethod.csproj | grep ProjectReference
```

**解决方案**：如果存在循环依赖，将 `WorkFlowMethodUsage` 的调用改为通过 `AppServiceProvider` 解析（与当前 `IEventExecutionService` 类似），但解析的是 `IWorkflowExecutionService` 接口：

```csharp
// 定义在 Share 层
public interface IWorkflowExecutionService
{
    Task<object> CallBridgedFunctionAsync(string name, IDictionary<string, object> parameters);
}

// 实现注册在 WorkFlowMethod 层
// 调用时通过 AppServiceProvider 解析
```

### 6.2 向后兼容风险

| 风险 | 概率 | 影响 | 对策 |
|------|------|------|------|
| 现有 JSON 配置不兼容 | 低 | 高 | `CallBridgedFunctionAsync` 已内置回退逻辑，无配置修改需求 |
| 参数传递方式变化 | 中 | 中 | 位置参数 → 命名参数，需要修改 `BuildParametersFromBindings` |
| 返回值类型变化 | 中 | 中 | `ExecutionResult` → `object`，需要修改 `WriteOutputToControl` |

### 6.3 性能风险

| 风险 | 概率 | 影响 | 对策 |
|------|------|------|------|
| 单步执行创建额外对象 | 低 | 低 | `WfContext` 和 `NodeDefinition` 是轻量级对象，GC 友好 |
| 工作流管线额外开销 | 低 | 低 | `CallBridgedFunction` 回退路径直接调用 `FunctionExecutor`，无管线开销 |

---

## 7. 附录

### 7.1 关键文件映射表

| 文件路径 | 职责 | 操作 |
|---------|------|------|
| `Operation/EventTriggerFramework/EventBinding/EventBindingTransformers.cs` | 事件绑定转换器（含 CreateEventHandler） | **修改** |
| `Operation/EventTriggerFramework/EventBinding/EventBindingPipeline.cs` | 事件绑定管线 | 无需修改 |
| `Operation/BackEndDriven/Execution/EventExecutionService.cs` | 通用事件执行服务 | **废弃** |
| `Operation/BackEndDriven/BackEndDrivenBuilder.cs` | BackEndDriven 构建器 | **修改** |
| `Operation/WorkFlowMethod/WorkflowMethodUseage.cs` | WorkFlowMethod 统一入口 | **修改**（增加包装器） |
| `Operation/FunctionStorage/Framework/FunctionStorageRegistry.cs` | 函数注册入口 | **修改**（确保注册表共享） |
| `Modules/Registry/06_ConcreteRegistries.cs` | FunctionRegistry 实现 | 无需修改 |
| `Plate/TConfig/TrConfig/IFunctionRegistry.cs` | 函数注册表接口 | 无需修改 |

### 7.2 接口依赖关系

```
IEventExecutionService (Share 层)
  ← EventExecutionService (BackEndDriven)  ← 废弃
  ← EventBinderTransformer (EventTriggerFramework)  ← 改为调用 WorkFlowMethodUsage

WorkFlowMethodUsage (WorkFlowMethod 层)
  ← IWorkflowRegistry (WorkFlowMethod 内部)
    ← WorkflowRegistryWrapper (新增) 包装 IFunctionRegistry
      ← IFunctionRegistry (TConfig/Share 层)
        ← FunctionRegistry (Modules.Registry)
          ← FunctionStorageRegistry (FunctionStorage)
```

### 7.3 代码修改量估算

| 文件 | 修改类型 | 估算行数 |
|------|---------|---------|
| `EventBindingTransformers.cs` | 修改 | ~50 行 |
| `EventExecutionService.cs` | 删除 | ~143 行 |
| `BackEndDrivenBuilder.cs` | 修改 | ~20 行 |
| `WorkflowMethodUseage.cs` | 新增包装器 | ~80 行 |
| `FunctionStorageRegistry.cs` | 修改 | ~10 行 |
| **合计** | | **~303 行** |

### 7.4 参考文档

- [MathFunctions.json 数据使用路径分析](./MathFunctions_DataFlow_Analysis.md)
- [WorkFlowMethodUsage_FunctionStorageRegistry_集成方案](./WorkFlowMethodUsage_FunctionStorageRegistry_集成方案.md)
- [CommandFramework_WorkflowExecution_外壳与内核关系.md](./CommandFramework_WorkflowExecution_外壳与内核关系.md)
- [WorkFlowMethod_定位与价值分析](../KnowLedge/WorkFlowMethod_定位与价值分析.md)
