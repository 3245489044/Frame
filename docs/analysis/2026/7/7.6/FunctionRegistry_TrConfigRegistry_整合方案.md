# FunctionRegistry 与 TrConfigRegistry 整合方案

> **核心思想**：所有函数以 ITrConfig（工作流配置）为基准注册到 FunctionRegistry，Delegate 只是 ITrConfig 的一种内部实现形式。

---

## 一、现状：两套注册体系

| | FileDriven FunctionRegistry | WorkFlowMethod TrConfigRegistry |
|---|---|---|
| **注册方式** | `registry.Register(name, Func<Dict, object>)` | `registry.Register(name, ITrConfig)` |
| **函数体** | 纯业务 Delegate | `ControlExecutorBase`（含 `c.Invoke`） |
| **执行方式** | 直接调用 delegate | `config.Invoke(args)` |
| **谁在用** | FileDriven 调度引擎 | WinForms 事件触发 |
| **存储模型** | `ConcurrentDictionary<string, Delegate>` | `ConcurrentDictionary<string, ITrConfig>` |

### 接口继承现状（已部分整合）

```
ITrConfigRegistry
  Register(string, ITrConfig)
  Register(string, Delegate)
  TryGetConfig / Contains / GetRegisteredNames
      ↑
IFunctionRegistry : ITrConfigRegistry
  Register(string, IFunctionUnit)
  TryGetDescriptor / TryGetUnit / TryGetDelegate
      ↑
IWorkflowRegistry : ITrConfigRegistry
  Call / CallAsync / HasFunction
```

`FunctionRegistry` 已实现 `IFunctionRegistry`，且有 `ITrConfigRegistry` 的显式接口实现。

---

## 二、问题分析

### 2.1 注册表实例不统一

当前存在两个可能不同的实例：

| 实例 | 类型 | 创建者 |
|------|------|--------|
| `FunctionStorageRegistry.Registry` | `Modules.Registry.FunctionRegistry` | `FunctionStorageLifecycle` |
| `WorkFlowMethodUsage.WorkFlowRegistry` | `DefaultWorkflowRegistry`（包装 TrConfigRegistry） | `WorkFlowMethodUsage` |

`WorkflowMethodExecutionService` 中已经在做"双表查找"：
```csharp
var registry = WorkFlowMethodUsage.WorkFlowRegistry;
if (!registry.TryGetConfig(functionName, out var config))
{
    var basicRegistry = WorkFlowMethodUsage.BasicRegistry;  // 回退查找
    ...
}
```

### 2.2 ITrConfig 注册时丢失语义

当前 `FunctionRegistry` 的 `ITrConfigRegistry.Register(string, ITrConfig)` 实现：

```csharp
void ITrConfigRegistry.Register(string name, ITrConfig config)
{
    if (config is FunctionDescriptor fd)
        Register(name, fd.Delegate, fd.Description);
    else
        Register(name, new Func<object?[], object?>(args => config.Invoke(args)), config.Description);
}
```

**ITrConfig 被包装为 Delegate 后，原始 ITrConfig 引用丢失**，事件触发时无法判断应该走工作流引擎还是直接调用。

### 2.3 ControlExecutorBase 的 InvokeIfRequired 可能死锁

```csharp
ControlService.InvokeIfRequired(control, () => ControlService.SetValue(control, value));
```

同步 `Invoke` 在工作线程中调用时，如果 UI 线程正在等待工作线程完成，会导致死锁。

---

## 三、整合方案

### 3.1 核心设计：统一以 ITrConfig 为存储基准

```
FileDriven 注册函数
  → Delegate 包装为 ITrConfig（TrConfig.FromDelegate）
  → 注册到 FunctionRegistry

WinForms 配置中的工作流函数
  → 本身就是 ITrConfig
  → 注册到 FunctionRegistry（同一份）

事件触发时
  → FunctionRegistry.TryGetConfig(name) → 拿到 ITrConfig
  → ITrConfig.Invoke(args) → 执行工作流
```

### 3.2 FunctionRegistry 改造

```csharp
public class FunctionRegistry : MetaMapRegistryBase<string, Delegate, FunctionDescriptor>,
    IFunctionRegistry
{
    // ★ 新增：ITrConfig 主存储（统一存储模型）
    private readonly ConcurrentDictionary<string, ITrConfig> _configs;
    
    // ★ 保留：IFunctionUnit 存储（兼容已有逻辑）
    private readonly Dictionary<string, IFunctionUnit> _units;

    public FunctionRegistry(IEqualityComparer<string>? comparer = null)
        : base(comparer ?? StringComparer.OrdinalIgnoreCase)
    {
        _configs = new ConcurrentDictionary<string, ITrConfig>(comparer ?? StringComparer.OrdinalIgnoreCase);
        _units = new Dictionary<string, IFunctionUnit>(comparer ?? StringComparer.OrdinalIgnoreCase);
    }

    // ─── Delegate 注册：自动包装为 ITrConfig ───

    public void Register(string name, Delegate function, string? description = null)
    {
        var config = TrConfig.FromDelegate(function, description);
        config.Name = name;
        _configs[name] = config;
        
        // 同步到基类（兼容已有逻辑）
        var descriptor = FunctionDescriptor.FromDelegate(function, description);
        descriptor.Name = name;
        descriptor.Delegate = function;
        Register(name, function, descriptor);
    }

    // ─── ITrConfig 注册：直接存储 ───

    public void Register(string name, ITrConfig config)
    {
        _configs[name] = config;
        
        // 同步到基类（兼容已有逻辑）
        if (config is FunctionDescriptor fd && fd.Delegate != null)
        {
            Register(name, fd.Delegate, fd.Description);
        }
        else
        {
            Register(name, new Func<object?[], object?>(args => config.Invoke(args)), config.Description);
        }
    }

    // ─── IFunctionUnit 注册 ───

    public void Register(string name, IFunctionUnit unit, string? description = null)
    {
        _units[name] = unit;
        
        // 包装为 ITrConfig
        var config = TrConfig.FromFunctionUnit(unit, description);
        config.Name = name;
        _configs[name] = config;
        
        // 同步到基类
        Register(name, new Func<object?, Task<ExecutionResult>>(input => unit.ExecuteAsync(input)), ...);
    }

    // ─── 统一查询入口 ───

    /// <summary>获取 ITrConfig（统一查询入口）</summary>
    public bool TryGetConfig(string name, out ITrConfig config)
    {
        return _configs.TryGetValue(name, out config);
    }

    // ─── ITrConfigRegistry 显式接口实现 ───

    void ITrConfigRegistry.Register(string name, ITrConfig config)
    {
        Register(name, config);  // 委托到上面的 public 方法
    }

    bool ITrConfigRegistry.TryGetConfig(string name, out ITrConfig config)
    {
        return TryGetConfig(name, out config);
    }
}
```

### 3.3 事件触发统一走工作流路径

```csharp
// EventBindingTransformers.cs - CreateEventHandler
private static EventHandler CreateEventHandler(
    object control, string eventName, string functionName,
    IWinFormsEventBinder? binder,
    IControlValueAccessor? valueAccessor = null)
{
    return (sender, args) =>
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (sender == null) return;
                // ... 脚本模式检查 ...

                var parameters = BuildParametersFromBindings(sender, valueAccessor);

                // ★ 统一通过 IEventExecutionService 执行工作流
                var executionService = AppServiceProvider.Resolve<IEventExecutionService>();
                if (executionService != null)
                {
                    var result = await executionService.ExecuteFunctionAsync(functionName, parameters);
                    if (result.Success)
                        WriteOutputToControl(sender, result.Value, valueAccessor);
                }
            }
            catch (Exception ex)
            {
                EventTriggerLog.Log("EventBinder_ExecuteException", new { error = ex.Message });
            }
        });
    };
}
```

### 3.4 统一注册表实例

```csharp
// FunctionStorageLifecycle.Initialize() 中
public void Initialize()
{
    if (_initialized) return;
    
    FunctionStorageRegistry.Initialize(_registry, _executor);
    Registrations.MathFunctionRegistrar.RegisterAll(_registry);
    
    // ★ 注册 IFunctionRegistry 到 AppServiceProvider
    AppServiceProvider.Register<IFunctionRegistry>(_registry);
    
    // ★ 确保 WorkFlowMethod 使用同一注册表
    WorkFlowMethodUsage.DefaultBasicRegistryProvider = () => _registry;
    
    _initialized = true;
}
```

### 3.5 ControlExecutorBase 的 InvokeIfRequired 改为 BeginInvoke

```csharp
// ControlExecutorBase.cs
// 新增辅助方法
private void InvokeOrBeginOnControl(object control, Action action)
{
    if (control is Control c && c.InvokeRequired)
        c.BeginInvoke(action);  // 异步投递，不阻塞工作线程
    else
        action();
}

// 所有 InvokeIfRequired 调用改为 InvokeOrBeginOnControl
protected virtual Task<OperationResult<object>> DoSetAsync(...)
{
    var value = parameters?.GetValueOrDefault("value")?.ToString();
    InvokeOrBeginOnControl(control, () => ControlService.SetValue(control, value));
    return Task.FromResult(OperationResult<object>.SuccessResult(value, "已设置"));
}
```

---

## 四、整合后的架构

```
                    ┌──────────────────────────────────┐
                    │      FunctionRegistry             │
                    │  (Modules.Registry.FunctionRegistry) │
                    │                                    │
                    │  _configs: ITrConfig[]  ← 统一存储  │
                    │  _units: IFunctionUnit[]  ← 兼容    │
                    │  (base) Delegate[]  ← 兼容          │
                    └──────────┬───────────────────────┘
                               │
              ┌────────────────┼────────────────┐
              ▼                ▼                ▼
    ┌─────────────────┐ ┌──────────────┐ ┌──────────────┐
    │ FileDriven 调度  │ │ WinForms 事件 │ │ WorkFlowMethod│
    │ (注册 Delegate)  │ │ (触发工作流)  │ │ (工作流编排)   │
    │ → 包装为 ITrConfig│ │ → 查 ITrConfig│ │ → 查 ITrConfig│
    └─────────────────┘ └──────────────┘ └──────────────┘
```

### 注册流程

```
FileDriven:
  FunctionRegistry.Register("add", (a, b) => a + b)
    → TrConfig.FromDelegate → _configs["add"] = ITrConfig
    → base.Register("add", delegate)

WinForms 配置:
  FunctionRegistry.Register("calcWorkflow", workflowConfig)
    → _configs["calcWorkflow"] = ITrConfig
    → base.Register("calcWorkflow", wrappedDelegate)

WorkFlowMethod:
  FunctionRegistry.Register("wf_node1", unit)
    → TrConfig.FromFunctionUnit → _configs["wf_node1"] = ITrConfig
    → _units["wf_node1"] = unit
    → base.Register("wf_node1", wrappedDelegate)
```

### 执行流程

```
事件触发 → CreateEventHandler
  → IEventExecutionService.ExecuteFunctionAsync("calcWorkflow", params)
    → WorkflowMethodExecutionService
      → FunctionRegistry.TryGetConfig("calcWorkflow") → ITrConfig
      → NodeExecutor.ExecuteNodeAsync → 执行工作流
      → 返回 ExecutionResult
  → WriteOutputToControl(sender, result)
```

---

## 五、改动清单

| # | 文件 | 改动内容 | 类型 |
|---|------|---------|------|
| 1 | `CsharpPrj/Modules/Registry/06_ConcreteRegistries.cs` | `FunctionRegistry` 新增 `_configs` 字典，统一以 ITrConfig 为存储基准 | 核心改动 |
| 2 | `CsharpPrj/Plate/TConfig/TrConfig/IFunctionRegistry.cs` | 可选：新增 `TryGetConfig` 方法声明 | 接口扩展 |
| 3 | `CsharpPrj/Operation/FunctionStorage/Framework/FunctionStorageLifecycle.cs` | `Initialize()` 中注册 `IFunctionRegistry` 到 `AppServiceProvider` | 初始化 |
| 4 | `CsharpPrj/Operation/BackEndDriven/EventTriggerFramework/EventBinding/EventBindingTransformers.cs` | `CreateEventHandler` 统一走 `IEventExecutionService` 路径 | 事件触发 |
| 5 | `CsharpPrj/Modules/MetaCommand/ControlExecutorBase.cs` | `InvokeIfRequired` → `BeginInvoke` | 防死锁 |
| 6 | `CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodUseage.cs` | 确保 `_workflowRegistry` 和 `_basicRegistry` 共享同一实例 | 加固 |

---

## 六、风险与注意事项

1. **向后兼容**：`FunctionRegistry` 基类 `MetaMapRegistryBase<string, Delegate, FunctionDescriptor>` 的 Delegate 存储保留，所有通过 `TryGetDelegate` 查询的代码不受影响。

2. **IFunctionUnit 注册**：`IFunctionUnit` 注册时同步包装为 ITrConfig，确保工作流引擎能通过 `TryGetConfig` 找到。

3. **AppServiceProvider 注册时机**：`IFunctionRegistry` 必须在事件绑定之前完成注册，否则事件触发时解析为 null。

4. **ControlExecutorBase 语义变化**：`BeginInvoke` 不等待 UI 操作完成，如果调用方依赖操作完成后的状态，需要调整。
