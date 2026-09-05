# FileDriven.Business 逻辑整合方案

> **目标**：消除重复逻辑、修复错误逻辑、清理冗余逻辑，统一代码架构
> **范围**：`FileDriven/FileDriven.Business/` 全部 14 个源文件
> **日期**：2026-06-16（v2 — 修正分析错误并完善方案）
> **前置分析**：`FileDriven.Business_综合分析报告.md`

---

## 目录

1. [整合原则](#1-整合原则)
2. [整合全景图](#2-整合全景图)
3. [P0 整合：线程安全与死锁修复（WorkflowExecutionLayer）](#3-p0-整合线程安全与死锁修复)
4. [P1 整合：控件查找与结果写入统一（WinFormsEventBinder）](#4-p1-整合控件查找与结果写入统一)
5. [P2 整合：注册模式统一（所有 Library 类）](#5-p2-整合注册模式统一)
6. [P3 整合：模型层精简（ModelLibrary / WorkflowIOGroup）](#6-p3-整合模型层精简)
7. [P4 整合：日志与异常处理规范化](#7-p4-整合日志与异常处理规范化)
8. [整合路线图](#8-整合路线图)

---

## 1. 整合原则

### 1.1 核心原则

```
单一职责  →  每个类/方法只做一件事
统一入口  →  同类操作只有一个入口点
消除重复  →  相同逻辑只实现一次
最小依赖  →  减少不必要的中间层委托
```

### 1.2 整合优先级

| 优先级 | 分类 | 标准 |
|--------|------|------|
| **P0** | 功能正确性 | 存在 Bug 或功能缺陷，必须立即修复 |
| **P1** | 代码重复 | 相同逻辑在 3 处以上重复，或重复代码 > 30 行 |
| **P2** | 设计冗余 | 无参重载、纯委托包装、可合并的方法 |
| **P3** | 可维护性 | 命名不一致、废弃方法残留、条件编译 |
| **P4** | 性能优化 | 日志噪音、不必要的锁开销 |

### 1.3 整合方式标记

| 标记 | 含义 |
|------|------|
| 🔴 **合并** | 将多套逻辑合并为一套 |
| 🟢 **删除** | 删除冗余/废弃代码 |
| 🔵 **提取** | 提取公共方法/基类 |
| 🟡 **重写** | 重写有 Bug 的实现 |
| ⚪ **保留** | 保持现状，仅添加注释说明 |

---

## 2. 整合全景图

### 2.1 当前重复逻辑分布

```
WinFormsEventBinder.cs
├── FindControl(controlName)          ← 合并版（推荐）
├── FindControlValue(controlName)     ← 纯委托包装 → 🟢 删除
├── FindControlByName(controlName)    ← 纯委托包装 → 🟢 删除
├── WriteResultToControl(string, string?) ← 重复遍历 → 🔴 合并
├── WriteResultToControl(Control, string)  ← 核心实现（保留）
└── FindControlRecursive(parent, name)     ← 核心实现（保留）

WorkflowExecutionLayer.cs
├── EnsureCachePopulated()            ← 锁粒度不合理 → 🟡 重写
├── GetWorkflow()                     ← 委托给 EnsureCachePopulated
├── GetAllWorkflowNames()             ← 委托给 EnsureCachePopulated
├── ExecuteWorkflowAsync()            ← 异步核心
└── ExecuteWorkflow(同步)             ← AsyncHelper.RunSync → 🟡 重写

ModelLibrary.cs
├── GetWorkflowDefinition()           ← 纯委托 → 🟢 删除
├── GetWorkflowSteps()                ← 纯委托 → 🟢 删除
├── GetWorkflowDetails()              ← 纯委托 → 🟢 删除
└── GetOutputMapping()                ← 纯委托 → 🟢 删除

WorkflowCommandCenter.cs
├── HandleWorkflowSwitch()            ← 保留（编排逻辑）
├── ExecuteWorkflow()                 ← 精简委托
├── ExecuteWorkflowAsync()            ← Task.Run 包装
├── WorkflowSwitchResult              ← 可提取基类
└── WorkflowExecResult                ← 可提取基类

所有 Library 类
├── RegisterAll()                     ← 无参重载 → 🟢 删除
└── RegisterAll(IFunctionRegistry)    ← 保留
```

### 2.2 整合后架构

```
整合前：                         整合后：
                                
WinFormsEventBinder              WinFormsEventBinder
├── FindControl                  ├── FindControl (唯一入口)
├── FindControlValue    🟢       ├── WriteResultToControl(Control, string)
├── FindControlByName   🟢       └── FindControlRecursive
├── WriteResultToControl(string) 🔴
├── WriteResultToControl(Control)
└── FindControlRecursive

WorkflowExecutionLayer           WorkflowExecutionLayer
├── EnsureCachePopulated 🟡      ├── EnsureCachePopulated (线程安全)
├── GetWorkflow                  ├── GetWorkflow
├── GetAllWorkflowNames          ├── GetAllWorkflowNames
├── ExecuteWorkflowAsync         ├── ExecuteWorkflowAsync
└── ExecuteWorkflow(同步) 🟡     └── ExecuteWorkflow(同步)

ModelLibrary 🟢                  [已删除，调用方直接使用 WorkflowExecutionLayer]

WorkflowCommandCenter            WorkflowCommandCenter
├── HandleWorkflowSwitch         ├── HandleWorkflowSwitch
├── ExecuteWorkflow              ├── ExecuteWorkflow
├── ExecuteWorkflowAsync         ├── ExecuteWorkflowAsync
├── WorkflowSwitchResult         └── WorkflowResultBase 🔵
└── WorkflowExecResult    🔵     

MathLibrary                      MathLibrary
├── RegisterAll()       🟢       └── RegisterAll(IFunctionRegistry)
└── RegisterAll(IFunctionRegistry)

BuiltInUnitsLibrary              BuiltInUnitsLibrary
├── RegisterAll()       🟢       └── RegisterAll(IFunctionRegistry)
└── RegisterAll(IFunctionRegistry)

... (其他 Library 类同理)
```

---

## 3. P0 整合：线程安全与死锁修复

### 3.1 问题描述

`WorkflowExecutionLayer.cs` 的 `EnsureCachePopulated()` 方法存在锁粒度不合理的问题，且 `ExecuteWorkflow(同步)` 方法存在死锁风险。

### 3.2 整合方案

#### 3.2.1 缓存填充锁粒度修复

**原代码分析**（L103-136）：

```csharp
private static void EnsureCachePopulated()
{
    lock (_cacheLock)
    {
        if (_workflowCache.Count > 0)
            return; // 缓存已填充
    }
    // ★ 锁已释放！此时另一个线程可能正在进入并重复加载
    var configPath = ProjectDirectories.FileDriven_WorkFlowConfig;
    // ... 文件扫描和加载 ...
    foreach (var file in Directory.GetFiles(configPath, "*.json"))
    {
        var def = LoadWorkflowDefinition(file);
        if (def != null)
        {
            lock (_cacheLock)
            {
                if (!_workflowCache.ContainsKey(def.WorkflowName))
                {
                    _workflowCache[def.WorkflowName] = def;
                    count++;
                }
            }
        }
    }
}
```

**问题**：
1. 检查缓存后释放锁，另一个线程可能同时进入并重复加载
2. 两个线程可能同时加载不同版本的同名工作流，后加载的覆盖先加载的
3. 文件扫描和反序列化在锁外执行，浪费 CPU

**修复方案**：将整个加载过程放在锁内，使用双重检查锁定。

```csharp
// 🟡 重写：EnsureCachePopulated
private static void EnsureCachePopulated()
{
    // 快速路径：无锁检查（读操作不需要锁）
    if (_workflowCache.Count > 0)
        return;

    lock (_cacheLock)
    {
        // 双重检查锁定：防止两个线程同时进入
        if (_workflowCache.Count > 0)
            return;

        var configPath = ProjectDirectories.FileDriven_WorkFlowConfig;
        if (!Directory.Exists(configPath))
        {
            FsLog.Log("WorkflowExecutionLayer_cache_dir_not_found", new { configPath });
            return;
        }

        int count = 0;
        foreach (var file in Directory.GetFiles(configPath, "*.json"))
        {
            var def = LoadWorkflowDefinition(file);
            if (def != null && !_workflowCache.ContainsKey(def.WorkflowName))
            {
                _workflowCache[def.WorkflowName] = def;
                count++;
            }
        }

        FsLog.Log("WorkflowExecutionLayer_cache_populated", new { count });
    }
}
```

**变更说明**：
- 文件扫描和反序列化现在在锁内执行，消除了竞态条件
- 双重检查锁定确保只加载一次
- 快速路径使用无锁检查（`Dictionary.Count` 是原子操作）

#### 3.2.2 同步执行死锁风险修复

**原代码分析**（L364-369）：

```csharp
public static string ExecuteWorkflow(WorkflowDefinition def, string input)
{
    return AsyncHelper.RunSync(() => ExecuteWorkflowAsync(def, input));
}
```

**问题**：`AsyncHelper.RunSync` 在 UI 线程上可能导致死锁。`ExecuteWorkflowAsync` 内部调用 `FunctionStorageRegistry.ExecuteAsync`，如果底层执行器使用了 `.Result` 或 `.Wait()`，在已有 `SynchronizationContext`（如 UI 线程）上会导致死锁。

**修复方案**：由于工作流执行本质上是 CPU 密集型操作（函数调用链），没有真正的异步 I/O，改为纯同步实现。

```csharp
// 🟡 重写：ExecuteWorkflow(同步)
public static string ExecuteWorkflow(WorkflowDefinition def, string input)
{
    if (def == null)
        throw new ArgumentNullException(nameof(def));

    if (def.Steps == null || def.Steps.Count == 0)
    {
        FsLog.Log("WorkflowExecutionLayer_ExecuteWorkflow_no_steps",
            new { workflowName = def.WorkflowName });
        return "{}";
    }

    FsLog.Log("WorkflowExecutionLayer_ExecuteWorkflow_start",
        new { workflowName = def.WorkflowName, stepCount = def.Steps.Count });

    // ★ 纯同步执行，不依赖 AsyncHelper.RunSync
    return ExecuteWorkflowSync(def, input);
}

// ★ 新增：纯同步执行核心
//   与 ExecuteWorkflowAsync 共享相同的步骤执行逻辑，
//   区别在于使用 .GetAwaiter().GetResult() 同步等待 FunctionStorageRegistry.ExecuteAsync
//   （因为 ExecuteAsync 本质上是同步执行的异步包装，不会导致死锁）
private static string ExecuteWorkflowSync(WorkflowDefinition def, string input)
{
    var context = new Dictionary<string, object>();
    var failedSteps = new List<Dictionary<string, object>>();

    // 解析输入（与异步版本相同）
    if (!string.IsNullOrEmpty(input))
    {
        try
        {
            var inputObj = JsonSerializer.Deserialize<Dictionary<string, object>>(input);
            if (inputObj != null)
            {
                foreach (var kvp in inputObj)
                {
                    if (kvp.Value is JsonElement jsonElement)
                        context[kvp.Key] = ConvertJsonElementToPrimitive(jsonElement);
                    else
                        context[kvp.Key] = kvp.Value;
                }
            }
        }
        catch
        {
            context["input"] = input;
        }
    }

    // 逐步执行（与异步版本相同，但使用同步等待）
    foreach (var step in def.Steps)
    {
        var stepArgs = new List<object>();
        foreach (var key in step.InputKeys)
        {
            if (context.TryGetValue(key, out var val))
                stepArgs.Add(val);
            else
                stepArgs.Add(key);
        }

        // ★ 同步等待：因为 ExecuteAsync 内部没有真正的异步 I/O
        var execResult = FunctionStorageRegistry.ExecuteAsync(
            step.FuncName, stepArgs.ToArray())
            .GetAwaiter().GetResult();

        if (!execResult.IsSuccess)
        {
            failedSteps.Add(new Dictionary<string, object>
            {
                ["stepName"] = step.StepName,
                ["funcName"] = step.FuncName,
                ["error"] = execResult.ErrorMessage ?? "未知错误"
            });
            continue;
        }

        if (!string.IsNullOrEmpty(step.OutputKey) && execResult.Value != null)
        {
            context[step.OutputKey] = execResult.Value;
        }
    }

    // 构建输出（与异步版本相同）
    if (failedSteps.Count > 0)
    {
        context["_failedSteps"] = failedSteps;
        context["_hasErrors"] = true;
    }

    if (def.OutputMapping != null && def.OutputMapping.Count > 0)
    {
        var output = new Dictionary<string, object>();
        foreach (var kvp in def.OutputMapping)
        {
            if (context.TryGetValue(kvp.Key, out var val))
                output[kvp.Value] = val;
        }
        if (failedSteps.Count > 0)
        {
            output["_failedSteps"] = failedSteps;
            output["_hasErrors"] = true;
        }
        return JsonSerializer.Serialize(output);
    }

    return JsonSerializer.Serialize(context);
}
```

**重要说明**：
- `ExecuteWorkflowAsync` 保留作为异步入口，供需要 `await` 的场景使用
- `ExecuteWorkflow(同步)` 改为纯同步实现，消除死锁风险
- 同步版本和异步版本共享 `ConvertJsonElementToPrimitive` 等辅助方法
- 如果未来 `FunctionStorageRegistry.ExecuteAsync` 引入真正的异步 I/O，需要重新评估此方案

#### 3.2.3 缓存检查去重

**目标**：消除 `GetWorkflow` 和 `GetAllWorkflowNames` 中重复的 `EnsureCachePopulated` 调用。

```csharp
// 方案：在 FileDrivenLifecycle.Initialize() 中预填充缓存
public void Initialize()
{
    if (_initialized) return;

    // 0. 预填充工作流缓存（确保后续 GetWorkflow/GetAllWorkflowNames 无需再检查）
    WorkflowExecutionLayer.EnsureCachePopulated();

    // 1. 设置 DefaultInputDispatcher 的委托
    DefaultInputDispatcher.GetWorkflowDelegate = WorkflowExecutionLayer.GetWorkflow;

    // 2. 注册所有 FileDriven 层的函数
    MathLibrary.RegisterAll(_registry);
    // ...
}
```

这样 `GetWorkflow` 和 `GetAllWorkflowNames` 可以去掉 `EnsureCachePopulated()` 调用，直接读取缓存。

### 3.3 涉及文件

| 文件 | 修改类型 | 说明 |
|------|---------|------|
| `Workflow/WorkflowExecutionLayer.cs` | 🟡 重写 | 锁范围修复、同步执行死锁修复 |
| `FileDrivenLifecycle.cs` | 🔵 提取 | 预填充缓存 |

---

## 4. P1 整合：控件查找与结果写入统一

### 4.1 问题描述

`WinFormsEventBinder.cs` 中存在 **4 套控件查找逻辑** 和 **2 套结果写入逻辑**，功能重叠但实现分散。

### 4.2 整合方案

#### 4.2.1 控件查找统一

**目标**：只保留 `FindControl(controlName)` 作为唯一入口，删除纯委托包装。

```csharp
// 🟢 删除：FindControlValue — 调用方改为 FindControl(name).value
private static string? FindControlValue(string controlName)
{
    return FindControl(controlName).value;
}

// 🟢 删除：FindControlByName — 调用方改为 FindControl(name).control
private static Control? FindControlByName(string controlName)
{
    return FindControl(controlName).control;
}
```

**调用方修改**：

| 原调用 | 改为 |
|--------|------|
| `FindControlValue(name)` | `FindControl(name).value` |
| `FindControlByName(name)` | `FindControl(name).control` |

#### 4.2.2 结果写入统一

**目标**：`WriteResultToControl(string, string?)` 不再独立遍历控件，改为委托给 `FindControl`。

```csharp
// 🟡 重写：WriteResultToControl(string, string?)
private static void WriteResultToControl(string controlName, string? value)
{
    if (string.IsNullOrEmpty(value))
        return;

    var (control, _) = FindControl(controlName);
    if (control != null)
    {
        if (control.InvokeRequired)
            control.Invoke((Action)(() => WriteResultToControl(control, value)));
        else
            WriteResultToControl(control, value);
    }
}
```

#### 4.2.3 FireAndForgetTriggerAsync 写入路径统一

**目标**：消除 `IControlValueWriter` 和 `WriteResultToControl` 两条写入路径的不一致。

**原代码分析**（L408-449）：

```csharp
// 阶段3：写回结果
_outputTargetCache.TryGetValue(bindingKey, out var outputTarget);
if (string.IsNullOrEmpty(outputTarget))
{
    // 日志：未找到输出目标
}
else if (result.Value == null)
{
    // 日志：结果值为 null
}
else
{
    var (outputControl, _) = FindControl(outputTarget);
    if (outputControl != null)
    {
        var writer = ComponentRegistry.Instance.Resolve<IControlValueWriter>();
        if (writer != null)
        {
            writer.WriteValue(outputControl, result.Value);
        }
        else
        {
            var resultStr = result.Value.ToString();
            if (!string.IsNullOrEmpty(resultStr))
                WriteResultToControl(outputControl, resultStr);
        }
    }
    else
    {
        // 日志：未找到输出控件
    }
}
```

**问题**：
1. `IControlValueWriter` 路径能识别 `ListData` 等 `IControlValue` 类型，直接写入 `ListView.Items`
2. 回退路径只能处理字符串，通过 `SplitEscaped` 拆分后填充
3. 两条路径行为不一致：如果 `IControlValueWriter` 存在但写入失败，不会回退到字符串路径

**修复方案**：提取 `TryWriteWithValueWriter` 方法，统一写入逻辑。

```csharp
// 🟡 重写：FireAndForgetTriggerAsync 阶段3
var outputTarget = GetOutputTarget(bindingKey);
if (outputTarget == null) return;

var (outputControl, _) = FindControl(outputTarget);
if (outputControl == null)
{
    Log("WinFormsEventBinder_FireAndForgetTriggerAsync_control_not_found",
        new { command = $"{controlName}.{eventType}", outputTarget = outputTarget },
        moduleName: "FileDriven", keyPrefix: "filedriven:");
    return;
}

// ★ 统一写入：IControlValueWriter 优先，失败回退到字符串写入
if (!TryWriteWithValueWriter(outputControl, result.Value))
{
    var resultStr = result.Value?.ToString();
    if (!string.IsNullOrEmpty(resultStr))
        WriteResultToControl(outputControl, resultStr);
}

// ★ 新增：提取 TryWriteWithValueWriter 方法
private static bool TryWriteWithValueWriter(Control control, object? value)
{
    if (value == null) return false;

    try
    {
        var writer = ComponentRegistry.Instance.Resolve<IControlValueWriter>();
        if (writer != null)
        {
            writer.WriteValue(control, value);
            return true;
        }
    }
    catch (Exception ex)
    {
        Log("WinFormsEventBinder_TryWriteWithValueWriter_failed",
            new { message = $"IControlValueWriter 写入失败: {ex.Message}" },
            moduleName: "FileDriven", keyPrefix: "filedriven:");
    }
    return false;
}
```

### 4.3 涉及文件

| 文件 | 修改类型 | 说明 |
|------|---------|------|
| `UI/WinFormsEventBinder.cs` | 🟡 重写 | 控件查找统一、结果写入统一 |

---

## 5. P2 整合：注册模式统一

### 5.1 问题描述

5 个 Library 类都定义了无参 `RegisterAll()` 重载，仅委托给有参版本。如果所有调用方已迁移，这些重载可以删除。

### 5.2 整合方案

#### 5.2.1 删除无参重载

**目标**：删除所有 Library 类中的无参 `RegisterAll()` 方法。

```csharp
// 🟢 删除：MathLibrary.cs 中的无参重载
public static void RegisterAll()
{
    RegisterAll(FunctionStorageRegistry.Registry);
}

// 🟢 删除：BuiltInUnitsLibrary.cs 中的无参重载
public static void RegisterAll()
{
    RegisterAll(FunctionStorageRegistry.Registry);
}

// 🟢 删除：MathBrowserFunctions.cs 中的无参重载
public static void RegisterAll()
{
    RegisterAll(FunctionStorageRegistry.Registry);
}

// 🟢 删除：UIToolbox.cs 中的无参重载
public static void RegisterAll()
{
    RegisterAll(FunctionStorageRegistry.Registry);
}

// 🟢 删除：WorkflowLibrary.cs 中的无参重载
public static void RegisterAll()
{
    RegisterAll(FunctionStorageRegistry.Registry);
}
```

#### 5.2.2 调用方检查

删除前需确认以下调用方已迁移：

| 调用方 | 当前调用 | 改为 |
|--------|---------|------|
| `FileDrivenLifecycle.Initialize()` | `MathLibrary.RegisterAll(_registry)` | ✅ 已使用有参版本 |
| `FileDrivenLifecycle.Initialize()` | `Libraries.BuiltInUnitsLibrary.RegisterAll(_registry)` | ✅ 已使用有参版本 |
| `FileDrivenLifecycle.Initialize()` | `Registrations.MathBrowserFunctions.RegisterAll(_registry)` | ✅ 已使用有参版本 |
| `FileDrivenLifecycle.Initialize()` | `UIToolbox.RegisterAll(_registry)` | ✅ 已使用有参版本 |
| `FileDrivenLifecycle.Initialize()` | `WorkflowLibrary.RegisterAll(_registry)` | ✅ 已使用有参版本 |
| `WorkflowLibrary.ReloadAll()` | `RegisterAll()` | 改为 `RegisterAll(FunctionStorageRegistry.Registry)` |

### 5.3 涉及文件

| 文件 | 修改类型 | 说明 |
|------|---------|------|
| `Registrations/MathLibrary.cs` | 🟢 删除 | 删除无参重载 |
| `Registrations/BuiltInUnitsLibrary.cs` | 🟢 删除 | 删除无参重载 |
| `Registrations/MathBrowserFunctions.cs` | 🟢 删除 | 删除无参重载 |
| `Registrations/UIToolbox.cs` | 🟢 删除 | 删除无参重载 |
| `Registrations/WorkflowLibrary.cs` | 🟢 删除 | 删除无参重载 + 修改 ReloadAll |

---

## 6. P3 整合：模型层精简

### 6.1 问题描述

`ModelLibrary.cs` 的 4 个方法都是纯委托转发，没有额外逻辑。`WorkflowIORegistry.cs` 的 2 个方法已废弃但仍被调用。

### 6.2 整合方案

#### 6.2.1 删除 ModelLibrary

**目标**：删除 `ModelLibrary.cs`，调用方直接使用 `WorkflowExecutionLayer` 和 `LoadWorkflowDetailsSkeleton`。

```csharp
// 🟢 删除：ModelLibrary.cs 全部内容

// 调用方修改：
// ModelLibrary.GetWorkflowDefinition(name)  → WorkflowExecutionLayer.GetWorkflow(name)
// ModelLibrary.GetWorkflowSteps(name)       → WorkflowExecutionLayer.GetWorkflow(name)?.Steps
// ModelLibrary.GetWorkflowDetails(name)     → LoadWorkflowDetailsSkeleton.GetWorkflowDetails(name)
// ModelLibrary.GetOutputMapping(name)       → WorkflowExecutionLayer.GetWorkflow(name)?.OutputMapping
```

#### 6.2.2 清理废弃方法调用

**目标**：`WorkflowLibrary.RegisterIOGroup` 不再调用 `[Obsolete]` 方法。

```csharp
// 🟡 重写：WorkflowLibrary.RegisterIOGroup
private static void RegisterIOGroup(WorkflowDefinition def)
{
    if (def == null) return;

    if (def.IoMapping != null)
    {
        // 直接使用配置中的 ioMapping
        var ioGroup = new WorkflowIOGroup(def.WorkflowName, fieldMappings: def.IoMapping);
        WorkflowIORegistry.Register(def.WorkflowName, ioGroup);
        return;
    }

    // ★ 不再调用 [Obsolete] 方法，直接构造 FieldMappingCollection
    var mappings = new FieldMappingCollection();
    if (def.DefaultInput != null)
    {
        foreach (var kvp in def.DefaultInput)
        {
            mappings.InputToControl[kvp.Key] = kvp.Key;
        }
    }
    if (def.OutputMapping != null)
    {
        foreach (var kvp in def.OutputMapping)
        {
            mappings.OutputToControl[kvp.Key] = kvp.Value;
        }
    }

    var group = new WorkflowIOGroup(def.WorkflowName, fieldMappings: mappings);
    WorkflowIORegistry.Register(def.WorkflowName, group);
}
```

#### 6.2.3 删除废弃方法

```csharp
// 🟢 删除：WorkflowIORegistry.cs 中的 [Obsolete] 方法
[Obsolete("请直接使用 new FieldMappingCollection { InputToControl = dict } 构造")]
public static FieldMappingCollection InferMappingsFromDefaultInput(...) { ... }

[Obsolete("请直接使用 new FieldMappingCollection { OutputToControl = dict } 构造")]
public static FieldMappingCollection InferMappingsFromOutputMapping(...) { ... }
```

### 6.3 涉及文件

| 文件 | 修改类型 | 说明 |
|------|---------|------|
| `Registrations/ModelLibrary.cs` | 🟢 删除 | 整个文件删除 |
| `Registrations/WorkflowLibrary.cs` | 🟡 重写 | RegisterIOGroup 内联废弃方法逻辑 |
| `Registrations/WorkflowIOGroup.cs` | 🟢 删除 | 删除 [Obsolete] 方法 |

---

## 7. P4 整合：日志与异常处理规范化

### 7.1 问题描述

- 日志参数类型不一致（匿名对象 vs 无参数）
- `DynamicInvoke` 抛出的 `TargetInvocationException` 包装了真实异常
- 方法内日志过多（如 `HandleWorkflowSwitch` 6 条日志）

### 7.2 整合方案

#### 7.2.1 日志参数规范化

**目标**：统一 `FsLog.Log` 的调用签名。

```csharp
// 规范：所有日志调用使用统一签名
FsLog.Log(string messageKey, object? parameters = null);

// 无参数时显式传递 null（而非不传）
FsLog.Log("MathLibrary_RegisterAll_start", null);
FsLog.Log("UIToolbox_RegisterAll_start", null);
FsLog.Log("WorkflowLibrary_RegisterAll_start", null);
```

#### 7.2.2 异常信息修复

**目标**：`MathBrowserFunctions.Math_Execute` 中正确提取内部异常消息。

**原代码分析**（L110-113）：

```csharp
catch (Exception ex)
{
    return $"错误: {ex.Message}";
}
```

**问题**：`DynamicInvoke` 包装的异常是 `TargetInvocationException`，`ex.Message` 是"调用的目标发生了异常"，而不是真正的业务异常消息。需要 `ex.InnerException?.Message` 才能获取真实错误。

```csharp
// 🟡 修复：Math_Execute 的异常处理
catch (Exception ex)
{
    // ★ 修复：提取 InnerException 的真实消息
    //   DynamicInvoke 将业务异常包装为 TargetInvocationException，
    //   直接使用 ex.Message 只能得到"调用的目标发生了异常"
    var realMessage = ex is TargetInvocationException tie && tie.InnerException != null
        ? tie.InnerException.Message
        : ex.Message;
    return $"错误: {realMessage}";
}
```

#### 7.2.3 日志降级

**目标**：将方法内部的详细日志降级，减少生产环境噪音。

```csharp
// 当前：HandleWorkflowSwitch 中 6 条日志
// 优化：合并为 2 条（入口 + 出口），中间步骤使用 Debug 级别

public static WorkflowSwitchResult HandleWorkflowSwitch(string workflowName)
{
    // 入口日志（保留）
    FsLog.Log("WorkflowCommandCenter_HandleWorkflowSwitch", new { workflowName });

    // ... 中间逻辑 ...

    // ★ 中间步骤改为 Debug 日志（如果 FsLog 支持级别）
    // FsLog.Debug("WorkflowCommandCenter_HandleWorkflowSwitch_event1", ...);
    // FsLog.Debug("WorkflowCommandCenter_HandleWorkflowSwitch_details_loaded", ...);

    // 出口日志（保留）
    FsLog.Log("WorkflowCommandCenter_HandleWorkflowSwitch_completed",
        new { workflowName, isSuccess = result.IsSuccess });
}
```

### 7.3 涉及文件

| 文件 | 修改类型 | 说明 |
|------|---------|------|
| `Registrations/MathBrowserFunctions.cs` | 🟡 重写 | 异常信息提取修复 |
| `Workflow/WorkflowCommandCenter.cs` | 🔵 提取 | 日志降级 |
| 所有文件 | 🔵 提取 | 日志参数规范化 |

---

## 8. 整合路线图

### 8.1 执行顺序

```
Phase 1 — P0 修复（功能正确性）
├── 1.1 线程安全修复 → WorkflowExecutionLayer.EnsureCachePopulated
├── 1.2 死锁风险修复 → WorkflowExecutionLayer.ExecuteWorkflow(同步)
├── 1.3 异常信息修复 → MathBrowserFunctions.Math_Execute
└── 1.4 缓存预填充 → FileDrivenLifecycle

Phase 2 — P1 去重（代码重复）
├── 2.1 控件查找统一 → WinFormsEventBinder
├── 2.2 结果写入路径统一 → WinFormsEventBinder
└── 2.3 废弃方法清理 → WorkflowIORegistry + WorkflowLibrary

Phase 3 — P2 精简（设计冗余）
├── 3.1 删除无参重载 → 所有 Library 类
├── 3.2 删除 ModelLibrary → 调用方直接使用 WorkflowExecutionLayer
└── 3.3 结果类型提取基类 → WorkflowCommandCenter

Phase 4 — P3 规范化（可维护性）
├── 4.1 日志参数规范化 → 所有文件
├── 4.2 日志降级 → WorkflowCommandCenter
└── 4.3 条件编译清理 → WorkflowLibrary
```

### 8.2 影响范围矩阵

| 整合项 | 影响文件数 | 风险等级 | 回滚难度 | 说明 |
|--------|-----------|---------|---------|------|
| 线程安全修复 | 1 | 🟢 低 | 🟢 低 | 仅修改锁范围，行为不变 |
| 死锁风险修复 | 1 | 🟡 中 | 🟡 中 | 需验证同步路径与异步路径输出一致 |
| 异常信息修复 | 1 | 🟢 低 | 🟢 低 | 仅修改 catch 块中的消息提取 |
| 缓存预填充 | 1 | 🟢 低 | 🟢 低 | 初始化时多一次调用，无副作用 |
| 控件查找统一 | 1 | 🟢 低 | 🟢 低 | 纯删除包装方法，编译时检查 |
| 结果写入统一 | 1 | 🟡 中 | 🟡 中 | 需验证 IControlValueWriter 和回退路径 |
| 删除无参重载 | 5+1 | 🟢 低 | 🟢 低 | 编译时检查，调用方已全部使用有参版本 |
| 删除 ModelLibrary | 1+N | 🟡 中 | 🟡 中 | 需查找并修改所有调用方 |
| 废弃方法清理 | 2 | 🟢 低 | 🟢 低 | 内联后删除，无行为变化 |
| 日志规范化 | 全部 | 🟢 低 | 🟢 低 | 仅修改参数传递方式 |

### 8.3 验证策略

```
Phase 1 验证：
├── 多线程并发调用 GetWorkflow + GetAllWorkflowNames → 缓存一致，无重复加载
├── UI 线程调用 ExecuteWorkflow → 无死锁，结果与异步版本一致
├── Math_Execute 执行失败函数 → 返回真实业务错误消息（非 TargetInvocationException）
└── 初始化后缓存已填充 → GetWorkflow 直接命中缓存

Phase 2 验证：
├── 控件事件触发 → 正确查找控件并写入结果
├── 事件触发后结果写入 → IControlValueWriter 路径和回退路径行为一致
└── 无编译警告（[Obsolete] 调用已清理）

Phase 3 验证：
├── 项目编译通过（无参重载删除后）
├── 调用方已全部迁移到有参版本
└── ModelLibrary 删除后调用方正常工作

Phase 4 验证：
├── 日志格式一致（所有调用使用统一签名）
├── 日志量减少（降级后）
└── 条件编译代码已清理
```

---

## 附录：文件修改清单

| # | 文件路径 | Phase | 修改类型 | 修改内容 |
|---|---------|-------|---------|---------|
| 1 | `Workflow/WorkflowExecutionLayer.cs` | 1 | 🟡 重写 | 锁范围修复（双重检查锁定）、同步执行死锁修复（纯同步实现） |
| 2 | `Registrations/MathBrowserFunctions.cs` | 1 | 🟡 重写 | 异常 InnerException 提取（TargetInvocationException 解包） |
| 3 | `FileDrivenLifecycle.cs` | 1 | 🔵 提取 | 预填充缓存（Initialize 中调用 EnsureCachePopulated） |
| 4 | `UI/WinFormsEventBinder.cs` | 2 | 🟡 重写 | 删除 FindControlValue/FindControlByName，重写 WriteResultToControl(string)，提取 TryWriteWithValueWriter |
| 5 | `Registrations/WorkflowLibrary.cs` | 2 | 🟡 重写 | RegisterIOGroup 内联废弃方法逻辑 |
| 6 | `Registrations/WorkflowIOGroup.cs` | 2 | 🟢 删除 | 删除 [Obsolete] 方法（InferMappingsFromDefaultInput / InferMappingsFromOutputMapping） |
| 7 | `Registrations/MathLibrary.cs` | 3 | 🟢 删除 | 删除无参 RegisterAll() |
| 8 | `Registrations/BuiltInUnitsLibrary.cs` | 3 | 🟢 删除 | 删除无参 RegisterAll() |
| 9 | `Registrations/MathBrowserFunctions.cs` | 3 | 🟢 删除 | 删除无参 RegisterAll() |
| 10 | `Registrations/UIToolbox.cs` | 3 | 🟢 删除 | 删除无参 RegisterAll() |
| 11 | `Registrations/WorkflowLibrary.cs` | 3 | 🟢 删除 | 删除无参 RegisterAll() + ReloadAll 修改 |
| 12 | `Registrations/ModelLibrary.cs` | 3 | 🟢 删除 | 整个文件删除 |
| 13 | `Workflow/WorkflowCommandCenter.cs` | 3 | 🔵 提取 | 提取 WorkflowResultBase 基类 |
| 14 | 所有文件 | 4 | 🔵 提取 | 日志参数规范化 |
| 15 | `Workflow/WorkflowCommandCenter.cs` | 4 | 🔵 提取 | 日志降级 |
| 16 | `Registrations/WorkflowLibrary.cs` | 4 | 🟢 删除 | 清理 #if ENABLE_WORKFLOW_METHOD |
