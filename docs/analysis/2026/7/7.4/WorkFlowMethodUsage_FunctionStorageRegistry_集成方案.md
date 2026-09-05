# WorkFlowMethodUsage 与 FunctionStorageRegistry 集成方案

> **框架反转设计**：WorkFlowMethodUsage 在上层定义注册表抽象，FunctionStorageRegistry 在下层提供实现，通过初始化注入完成解耦。

---

## 1. 背景与问题

### 1.1 当前架构

```
ParameterExecutionWorkflow (BackEndDriven)
    ↓ 依赖
IParameterExecutionWorkflow (Share/CommandFramework)
    ↓ 实现
ParameterExecutionWorkflow (BackEndDriven.Invoke)
    ↓ 使用
IFunctionExecutor (TConfig)  ← 背后是 FunctionStorageRegistry 的注册表
```

当前 `ParameterExecutionWorkflow` 直接使用 `IFunctionExecutor` 执行函数，而 `IFunctionExecutor` 背后是 `FunctionStorageRegistry` 管理的注册表。

### 1.2 目标

将 `ParameterExecutionWorkflow` 接入 `WorkFlowMethodUsage`，同时：

1. **复用 `FunctionStorageRegistry` 的注册表** — 所有已注册函数无需重新注册
2. **框架反转** — `WorkFlowMethodUsage` 不直接依赖 `FunctionStorageRegistry`，而是由下层注入注册表实例
3. **动态注册** — 后续可通过 `FunctionStorageRegistry` 动态注册函数，`WorkFlowMethodUsage` 自动感知

---

## 2. 架构分层

```
┌─────────────────────────────────────────────────────────┐
│                    应用层 (Application)                    │
│  ParameterExecutionWorkflow → WorkFlowMethodUsage        │
│  (BackEndDriven.Invoke)     (WorkFlowMethod.Usage)       │
└───────────────────────┬─────────────────────────────────┘
                        │ 使用
                        ▼
┌─────────────────────────────────────────────────────────┐
│                   WorkFlowMethodUsage                     │
│  定义：IWorkflowRegistry 接口（继承 IFunctionRegistry）    │
│  持有：DefaultWorkflowRegistry 实例（包装 TrConfigRegistry）│
│  提供：ExecuteFunctionAsync / CallBridgedFunctionAsync    │
└───────────────────────┬─────────────────────────────────┘
                        │ 初始化时注入
                        ▼
┌─────────────────────────────────────────────────────────┐
│                  FunctionStorageRegistry                  │
│  底层：Modules.Registry.FunctionRegistry                 │
│  提供：Register / ExecuteAsync / 所有 Library 注册       │
│  职责：系统唯一的注册表中心                                │
└─────────────────────────────────────────────────────────┘
```

### 2.1 分层原则

| 层 | 模块 | 职责 | 依赖方向 |
|----|------|------|---------|
| **上层** | `WorkFlowMethodUsage` | 定义注册表抽象，提供统一执行入口 | 定义接口，不依赖下层 |
| **下层** | `FunctionStorageRegistry` | 实现注册表，管理所有函数注册 | 实现接口，被上层使用 |
| **胶水层** | 初始化代码 | 将下层实例注入上层 | 依赖上下两层 |

### 2.2 框架反转的核心

```
❌ 错误（上层依赖下层）：
   WorkFlowMethodUsage → 直接引用 FunctionStorageRegistry

✅ 正确（依赖倒置）：
   WorkFlowMethodUsage 定义 IWorkflowRegistry 接口
   FunctionStorageRegistry 实现该接口（通过 DefaultWorkflowRegistry 包装）
   初始化时：WorkFlowMethodUsage.Initialize(workflowRegistry: 包装后的实例)
```

---

## 3. 框架反转设计

### 3.1 WorkFlowMethodUsage 的初始化入口

`WorkFlowMethodUsage.Initialize` 已经支持接收外部注册表实例：

```csharp
// WorkflowMethodUseage.cs 第 137-167 行
public static void Initialize(
    IWorkflowRegistry? workflowRegistry = null,
    BmIFunctionRegistry? basicRegistry = null)
{
    // 1. 如果传入了 workflowRegistry，直接使用
    if (workflowRegistry != null)
        _workflowRegistry = workflowRegistry;
    // 2. 如果传入了 basicRegistry（即 TrConfigRegistry），包装为 DefaultWorkflowRegistry
    else if (basicRegistry != null)
        _workflowRegistry = new DefaultWorkflowRegistry(basicRegistry);
    // 3. 兜底：使用 Fef.Registry（内部新建 TrConfigRegistry）
    else
        _workflowRegistry = Fef.Registry;
}
```

**框架反转的体现**：
- `WorkFlowMethodUsage` 不知道也不关心传入的注册表具体来自哪里
- 它只依赖 `IWorkflowRegistry` 接口（自身定义）和 `BmIFunctionRegistry` 接口（TConfig 定义）
- 下层（`FunctionStorageRegistry`）负责提供符合接口的实例

### 3.2 初始化流程

```
应用启动
    │
    ├── 1. FunctionStorageLifecycle.Initialize()
    │       └── 创建 FunctionRegistry + FunctionExecutor
    │       └── 注册所有内置函数（MathLibrary 等）
    │       └── 同步到 FunctionStorageRegistry 静态门面
    │
    ├── 2. 包装为 DefaultWorkflowRegistry
    │       └── new DefaultWorkflowRegistry(FunctionStorageRegistry.Registry)
    │       └── 此时 DefaultWorkflowRegistry 内部持有的是同一个 TrConfigRegistry
    │
    ├── 3. WorkFlowMethodUsage.Initialize(basicRegistry: FunctionStorageRegistry.Registry)
    │       └── _workflowRegistry = new DefaultWorkflowRegistry(basicRegistry)
    │       └── _functionExecutor = new FunctionExecutor(_workflowRegistry)
    │       └── 此时 WorkFlowMethodUsage 内部使用的注册表 = FunctionStorageRegistry 的注册表
    │
    └── 4. 后续调用 WorkFlowMethodUsage.ExecuteFunctionAsync("FuncName", params)
            └── 委托给 _functionExecutor.ExecuteAsync
            └── 在 FunctionStorageRegistry 的注册表中查找并执行
```

### 3.3 代码示例

```csharp
// ============================================================
// 应用启动时的初始化代码（胶水层）
// ============================================================

// Step 1: 初始化 FunctionStorage（注册所有内置函数）
var storageLifecycle = new FunctionStorageLifecycle();
storageLifecycle.Initialize();

// Step 2: 初始化 WorkFlowMethodUsage，复用 FunctionStorageRegistry 的注册表
WorkFlowMethodUsage.Initialize(
    basicRegistry: FunctionStorageRegistry.Registry  // ★ 框架反转：下层注入到上层
);

// 此时 WorkFlowMethodUsage 已就绪，可以执行任何已注册的函数
```

---

## 4. 动态注册方案

### 4.1 注册路径

由于 `WorkFlowMethodUsage` 内部持有的是 `FunctionStorageRegistry` 的同一个注册表实例，动态注册只有**一条路径**：

```csharp
// ★ 唯一注册路径：通过 FunctionStorageRegistry
FunctionStorageRegistry.Register("MyDynamicFunc", (string input) =>
{
    return $"Processed: {input}";
});

// WorkFlowMethodUsage 自动感知（因为是同一个注册表）
var result = await WorkFlowMethodUsage.ExecuteFunctionAsync("MyDynamicFunc", parameters);
```

### 4.2 框架反转的体现

```
注册行为发生在下层（FunctionStorageRegistry），
执行行为发生在上层（WorkFlowMethodUsage），
上层不需要知道函数是怎么注册的，只需要知道去哪里执行。
```

### 4.3 多种注册方式

```csharp
// 方式 1：注册委托（最常用）
FunctionStorageRegistry.Register("Calculate", (int a, int b) => a + b);

// 方式 2：注册 IFunctionUnit
FunctionStorageRegistry.Register(new MyFunctionUnit());

// 方式 3：批量注册（通过 Library Registrar）
MathFunctionRegistrar.RegisterAll(FunctionStorageRegistry.Registry);

// 方式 4：注销
FunctionStorageRegistry.Unregister("OldFunction");
```

### 4.4 动态注册的典型场景

```csharp
// 场景 1：启动时从配置文件加载
void LoadFunctionsFromConfig(string configPath)
{
    var config = JsonSerializer.Deserialize<FunctionConfig[]>(File.ReadAllText(configPath));
    foreach (var func in config)
    {
        var delegateFunc = BuildDelegateFromConfig(func);
        FunctionStorageRegistry.Register(func.Name, delegateFunc);
    }
}

// 场景 2：运行时按需注册（插件式）
void RegisterPluginFunction(string name, Delegate function)
{
    FunctionStorageRegistry.Register(name, function);
}

// 场景 3：热替换
void UpdateFunction(string name, Delegate newFunction)
{
    FunctionStorageRegistry.Unregister(name);
    FunctionStorageRegistry.Register(name, newFunction);
}
```

---

## 5. 接入 ParameterExecutionWorkflow

### 5.1 新建 WorkflowMethodExecutionWorkflow

```csharp
// ============================================================
// WorkflowMethodExecutionWorkflow.cs — 基于 WorkFlowMethod 的参数执行工作流
//
// 实现 IParameterExecutionWorkflow 接口，
// 内部委托给 WorkFlowMethodUsage 执行函数。
//
// ★ 框架反转：Share 层只依赖 IParameterExecutionWorkflow 接口，
//   BackEndDriven 通过 DI 注入提供此实现。
//   内部使用 WorkFlowMethodUsage 执行函数，
//   WorkFlowMethodUsage 的注册表由 FunctionStorageRegistry 在初始化时注入。
// ============================================================

using CommandFramework.Abstractions;
using CommandFramework.Parsing;
using FrameworkAbstractions.Controls;
using ParameterResolver.Abstractions;
using ParameterResolver.Sources;
using IFunctionExecutor = TConfig.IFunctionExecutor;
using IControlFinder = FrameworkAbstractions.Controls.IControlFinder;
using IControlValueWriter = CommandFramework.Abstractions.IControlValueWriter;

namespace BackEndDriven.Invoke;

/// <summary>
/// 基于 WorkFlowMethod 的参数执行工作流
/// <para>
/// 与 ParameterExecutionWorkflow 功能相同，但函数执行委托给 WorkFlowMethodUsage。
/// 控件读写逻辑（ReadControlValuesAsync / WriteResult）保持原样复用。
/// </para>
/// </summary>
public class WorkflowMethodExecutionWorkflow : IParameterExecutionWorkflow
{
    /// <summary>
    /// 执行参数执行工作流
    /// <para>流程：ReadControlValues → ExecuteFunction（通过 WorkFlowMethodUsage）→ WriteResult</para>
    /// </summary>
    public async Task<ParameterExecutionResult> ExecuteAsync(
        ParameterExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 步骤 1：从控件读取值 → 构建参数（前端逻辑，不变）
            var parameters = await ReadControlValuesAsync(request, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // 步骤 2：通过 WorkFlowMethodUsage 执行函数
            // ★ WorkFlowMethodUsage 内部注册表 = FunctionStorageRegistry 的注册表
            //   （在应用启动初始化时注入）
            WorkFlowMethodUsage.EnsureInitialized();
            var result = await WorkFlowMethodUsage.ExecuteFunctionAsync(
                request.FunctionName, parameters);

            if (!result.IsSuccess)
            {
                return ParameterExecutionResult.Fail(
                    $"Function '{request.FunctionName}' execution failed: {result.ErrorMessage}");
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 步骤 3：写回结果（前端逻辑，不变）
            if (!string.IsNullOrEmpty(request.OutputTarget) && result.Value != null)
            {
                WriteResult(request, result.Value.ToString());
            }

            var executionTime = DateTime.UtcNow - startTime;
            return ParameterExecutionResult.Ok(result.Value, executionTime);
        }
        catch (OperationCanceledException)
        {
            return ParameterExecutionResult.Fail("Parameter execution was cancelled.");
        }
        catch (Exception ex)
        {
            return ParameterExecutionResult.Fail(
                $"Parameter execution error: {ex.Message}");
        }
    }

    // ================================================================
    // 以下方法从 ParameterExecutionWorkflow 原样复用
    // ================================================================

    private Task<object?[]?> ReadControlValuesAsync(
        ParameterExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.InputControls))
            return Task.FromResult<object?[]?>(null);

        IParameterSource source;
        if (request.IsGuiMode && request.TargetForm != null)
            source = new GuiControlSource(request.TargetForm);
        else if (request.VirtualStore != null)
            source = new VirtualStoreSource(new Dictionary<string, string?>(request.VirtualStore));
        else
            source = new VirtualStoreSource(new Dictionary<string, string?>());

        var controlNames = StringUtils.SplitEscapedToArray(request.InputControls);
        var rawValues = new string?[controlNames.Length];
        for (int i = 0; i < controlNames.Length; i++)
            rawValues[i] = source.ReadValue(controlNames[i]);

        return Task.FromResult<object?[]?>(rawValues);
    }

    private void WriteResult(ParameterExecutionRequest request, string? value)
    {
        if (request.IsGuiMode && request.TargetForm != null)
        {
            var finder = request.ControlFinder;
            var writer = request.ControlValueWriter;
            if (finder == null || writer == null) return;

            var control = FindControl(request.TargetForm, request.OutputTarget!, finder);
            if (control != null)
                SetControlText(control, value, writer);
        }
        else if (request.VirtualStore != null && request.OutputTarget != null)
        {
            request.VirtualStore[request.OutputTarget] = value;
        }
    }

    private static object? FindControl(object form, string controlName, IControlFinder finder)
    {
        try { return finder.FindControl(controlName); }
        catch { return null; }
    }

    private static void SetControlText(object control, string? value, IControlValueWriter writer)
    {
        try { writer.WriteValue(control, value); }
        catch { }
    }
}
```

### 5.2 DI 注入切换

```csharp
// 在 DI 容器中注册时，将实现从 ParameterExecutionWorkflow 切换为 WorkflowMethodExecutionWorkflow

// 旧注册（直接使用 IFunctionExecutor）
services.AddSingleton<IParameterExecutionWorkflow>(sp =>
    new ParameterExecutionWorkflow(functionExecutor));

// 新注册（使用 WorkFlowMethodUsage）
services.AddSingleton<IParameterExecutionWorkflow>(sp =>
    new WorkflowMethodExecutionWorkflow());
```

### 5.3 ExecuteCommandStage 的适配

`ExecuteCommandStage` 已经支持 `IParameterExecutionWorkflow` 的可选注入（第 42 行、第 76-91 行），无需修改：

```csharp
// ExecuteCommandStage.cs 第 262-267 行
// ★ 框架反转：当注入 IParameterExecutionWorkflow 时，委托给工作流实现
if (_parameterExecutionWorkflow != null)
{
    await HandleClickWithWorkflow(context, binding.Value, cancellationToken);
    return;
}
```

只需在构造 `ExecuteCommandStage` 时传入新的 `WorkflowMethodExecutionWorkflow` 实例即可。

---

## 6. 完整初始化流程示例

```csharp
// ============================================================
// Program.cs / Startup.cs — 应用启动入口
// ============================================================

void ConfigureApplication()
{
    // ---- Step 1: 初始化 FunctionStorage ----
    var storageLifecycle = new FunctionStorageLifecycle();
    storageLifecycle.Initialize();
    // 此时 FunctionStorageRegistry.Registry 已就绪
    // 所有内置函数（MathLibrary 等）已注册

    // ---- Step 2: 初始化 WorkFlowMethodUsage ----
    // ★ 框架反转：将 FunctionStorageRegistry 的注册表注入到 WorkFlowMethodUsage
    WorkFlowMethodUsage.Initialize(
        basicRegistry: FunctionStorageRegistry.Registry
    );
    // 此时 WorkFlowMethodUsage 内部注册表 = FunctionStorageRegistry 的注册表
    // 两者是同一个 TrConfigRegistry 实例

    // ---- Step 3: 注册 WorkflowMethodExecutionWorkflow ----
    // 在 DI 容器中注册
    services.AddSingleton<IParameterExecutionWorkflow>(
        new WorkflowMethodExecutionWorkflow()
    );

    // ---- Step 4: 配置 ExecuteCommandStage ----
    services.AddSingleton<ExecuteCommandStage>(sp =>
        new ExecuteCommandStage(
            parameterExecutionWorkflow: sp.GetRequiredService<IParameterExecutionWorkflow>(),
            // ... 其他依赖
        )
    );
}
```

---

## 7. 架构优势总结
****
| 维度 | 旧方案 | 新方案 |
|------|--------|--------|
| **函数执行** | `IFunctionExecutor` 直接执行 | `WorkFlowMethodUsage.ExecuteFunctionAsync` |
| **注册表管理** | `FunctionStorageRegistry` 独立管理 | 同一实例，WorkFlowMethodUsage 复用 |
| **框架反转** | 无 | WorkFlowMethodUsage 定义接口，下层注入 |
| **动态注册** | `FunctionStorageRegistry.Register` | 同一路径，WorkFlowMethodUsage 自动感知 |
| **控件读写** | 内联在 ParameterExecutionWorkflow | 保持原样，不变 |
| **扩展性** | 需修改 ParameterExecutionWorkflow | 新建类，不修改原有代码 |

### 7.1 框架反转的核心价值

```
传统依赖：上层 → 直接调用 → 下层
框架反转：上层 → 定义接口 ← 下层实现
                ↓
          初始化时注入
```

- **WorkFlowMethodUsage** 不需要知道 `FunctionStorageRegistry` 的存在
- **FunctionStorageRegistry** 不需要知道 `WorkFlowMethodUsage` 的存在
- 两者通过**接口**（`IWorkflowRegistry` / `BmIFunctionRegistry`）解耦
- 胶水层（初始化代码）负责将两者连接起来
