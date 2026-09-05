# IParameterExecutionWorkflow 统一迁移重构文档

> **日期：** 2026-07-16
> **目标：** 消除 `TConfig.IParameterExecutionWorkflow` 与 `CommandFramework.Abstractions.IParameterExecutionWorkflow` 的重复定义，统一使用 `TConfig` 版本。
> **触发：** BackEndDriven.csproj 编译报 30 个错误，核心根因是类型不兼容。

---

## 一、问题分析

### 1.1 根因

`IParameterExecutionWorkflow` 在两个地方重复定义，且签名不同：

| 位置 | 签名 | 实现类 |
|------|------|--------|
| `TConfig.IParameterExecutionWorkflow` | `ExecuteAsync(object, Dictionary<string, object>)` → `Task` | `WorkflowMethodExecutionWorkflow` |
| `CommandFramework.Abstractions.IParameterExecutionWorkflow` | `ExecuteAsync(ParameterExecutionRequest, CancellationToken)` → `Task<ParameterExecutionResult>` | （无直接实现） |

`WorkflowMethodExecutionWorkflow` 实现的是 `TConfig` 版本，但 `CommandFrameworkOptions.ParameterExecutionWorkflow` 属性类型是 `CommandFramework.Abstractions` 版本，导致赋值时报 CS0266。

### 1.2 连锁错误

| 错误类别 | 数量 | 说明 |
|----------|------|------|
| A类：类型不兼容（CS0266） | 2 | BackEndDrivenBuilder.cs:73, EventTriggerFrameworkInitializer.cs:147 |
| B类：FrameworkContext 缺少属性（CS1061） | 12 | 基类改为 TConfig.FrameworkContext 后，CommandFrameworkHost/EventBusCore 等属性不存在 |
| C类：int→TimeSpan?（CS1503） | 1 | DedupWindow(int) 传给需要 TimeSpan? 的参数 |
| D类：IEventBindingInfo 不兼容（CS1503） | 1 | TConfig.IEventBindingInfo → EventTriggerFramework.Abstractions.IEventBindingInfo |
| E类：GetSnapshotString 缺失（CS0117） | 1 | EventBindingDiagnosticContext Stub 缺少此方法 |
| F类：UnifiedConfigLoader 多个错误 | 13 | ParamConfig 不存在、TrConfig 是静态类、Register 重载不匹配等 |

---

## 二、重构方案

### 2.1 核心决策

**统一使用 `TConfig.IParameterExecutionWorkflow`**，将 `ParameterExecutionRequest` 和 `ParameterExecutionResult` 也迁移到 `TConfig`。

**原因：**
- `TConfig` 是平台抽象层，契约类型应定义在此层
- `ParameterExecutionRequest` 已引用 `TConfig.Controls.IControlValueAccessor`
- `ParameterExecutionResult` 是纯数据类，零外部依赖
- 消除 `CommandFramework.Abstractions` 与 `TConfig` 之间的类型歧义

### 2.2 迁移路线图

```
迁移前：
  CommandFramework.Abstractions
    ├── IParameterExecutionWorkflow  ←── CommandFrameworkOptions 引用此版本
    ├── ParameterExecutionRequest
    └── ParameterExecutionResult
  
  TConfig
    └── IParameterExecutionWorkflow  ←── WorkflowMethodExecutionWorkflow 实现此版本

迁移后：
  TConfig
    ├── IParameterExecutionWorkflow  (签名统一)
    ├── ParameterExecutionRequest    (从 Abstractions 迁入)
    └── ParameterExecutionResult     (从 Abstractions 迁入)
  
  CommandFramework.Abstractions      (删除上述三个类型)
    └── (其他类型保持不变)
```

---

## 三、详细步骤

### 步骤 1：修改 `TConfig.IParameterExecutionWorkflow` 签名

**文件：** `CsharpPrj/Plate/TConfig/Contexts/FrameworkOptions.cs`

**变更：**
```csharp
// 修改前
public interface IParameterExecutionWorkflow
{
    Task ExecuteAsync(object sender, Dictionary<string, object> parameters);
}

// 修改后
public interface IParameterExecutionWorkflow
{
    Task<ParameterExecutionResult> ExecuteAsync(
        ParameterExecutionRequest request,
        CancellationToken cancellationToken = default);
}
```

### 步骤 2：新建 `ParameterExecutionRequest.cs`（迁移到 TConfig）

**文件：** `CsharpPrj/Plate/TConfig/Models/ParameterExecutionRequest.cs`

从 `CommandFramework.Abstractions` 迁移，将 `ControlFinder` 属性类型改为 `object?`（避免 TConfig 依赖 FrameworkAbstractions）：

```csharp
namespace TConfig;

public class ParameterExecutionRequest
{
    public required string FunctionName { get; init; }
    public string? InputControls { get; init; }
    public string? OutputTarget { get; init; }
    public bool IsGuiMode { get; init; }
    public object? TargetForm { get; init; }
    public Dictionary<string, string?>? VirtualStore { get; init; }
    
    // ★ 改为 object? 避免 TConfig 依赖 FrameworkAbstractions.Controls.IControlFinder
    //   WorkflowMethodExecutionWorkflow 内部已通过构造函数注入 _controlFinder
    public object? ControlFinder { get; init; }
    
    public TConfig.Controls.IControlValueAccessor? ControlValueWriter { get; init; }
    public Dictionary<string, System.Type>? ParameterTypes { get; init; }
}
```

### 步骤 3：新建 `ParameterExecutionResult.cs`（迁移到 TConfig）

**文件：** `CsharpPrj/Plate/TConfig/Models/ParameterExecutionResult.cs`

```csharp
namespace TConfig;

public class ParameterExecutionResult
{
    public bool Success { get; init; }
    public object? Value { get; init; }
    public TimeSpan ExecutionTime { get; init; }
    public string? ErrorMessage { get; init; }

    public static ParameterExecutionResult Ok(object? value, TimeSpan executionTime)
        => new() { Success = true, Value = value, ExecutionTime = executionTime };

    public static ParameterExecutionResult Fail(string errorMessage)
        => new() { Success = false, ErrorMessage = errorMessage };
}
```

### 步骤 4：删除 `CommandFramework.Abstractions` 中的旧定义

删除以下三个文件：
- `CsharpPrj/Share/CommandFramework/Abstractions/IParameterExecutionWorkflow.cs`
- `CsharpPrj/Share/CommandFramework/Abstractions/ParameterExecutionRequest.cs`
- `CsharpPrj/Share/CommandFramework/Abstractions/ParameterExecutionResult.cs`

### 步骤 5：修改 `CommandFramework/GlobalUsings.cs`

**文件：** `CsharpPrj/Share/CommandFramework/GlobalUsings.cs`

```csharp
// 修改前（第29行）
global using IParameterExecutionWorkflow = CommandFramework.Abstractions.IParameterExecutionWorkflow;

// 修改后
global using IParameterExecutionWorkflow = TConfig.IParameterExecutionWorkflow;
```

### 步骤 6：修改 `WorkflowMethodExecutionWorkflow`

**文件：** `CsharpPrj/Operation/WorkFlowMethod/Execution/WorkflowMethodExecutionWorkflow.cs`

**变更：**
1. 移除 `TConfig.IParameterExecutionWorkflow` 的显式接口实现（旧的 `ExecuteAsync(object, Dictionary)` 方法，第72-82行）
2. 将类声明从 `: TConfig.IParameterExecutionWorkflow` 改为 `: IParameterExecutionWorkflow`（通过 global using 解析）
3. 保留现有的 `ExecuteAsync(ParameterExecutionRequest, CancellationToken)` 方法作为接口实现
4. 移除 `LastResult` 属性（不再需要，因为接口直接返回结果）

### 步骤 7：修复 `BackEndDrivenBuilder.cs`

**文件：** `CsharpPrj/Operation/BackEndDriven/WorkFlowMethodBridge/BackEndDrivenBuilder.cs`

**变更：**
1. 第73行：`new WorkflowMethodExecutionWorkflow(...)` 自动兼容（类型已统一）
2. 第101行：`CommandFramework.Abstractions.IParameterExecutionWorkflow` → `IParameterExecutionWorkflow`
3. 第116行：`CommandFramework.Abstractions.IParameterExecutionWorkflow?` → `IParameterExecutionWorkflow?`
4. 移除第25行 `using CommandFramework.Abstractions;`（不再需要）

### 步骤 8：修复 `FrameworkContext.cs` + `EventTriggerFrameworkInitializer.cs`

#### 8a：`FrameworkContext.cs`

**文件：** `CsharpPrj/Operation/BackEndDriven/EventTriggerFramework/FrameworkContext.cs`

在 `EventTriggerFramework.FrameworkContext` 中添加缺失的属性（这些属性在 `TConfig.FrameworkContext` 基类中不存在）：

```csharp
// 在 FrameworkContext 类中添加：
public CommandFramework.CommandFrameworkHost? CommandFrameworkHost { get; internal set; }
public IEventBusCore? EventBusCore { get; internal set; }
public IEnhancedEventBus? EnhancedEventBus { get; internal set; }
public IDeduplicatingEventBus? DeduplicatingEventBus { get; internal set; }
public IEventQueue? EventQueue { get; internal set; }
public IEventScope? RootEventScope { get; internal set; }
public CommandParser? CommandParser { get; internal set; }
public CommandEngine? CommandEngine { get; internal set; }
public IStagePipeline<CommandContext>? Pipeline { get; internal set; }
public IPipelineInstance? PipelineInstance { get; internal set; }
public IStateStore? StateStore { get; internal set; }
public IValueExtractor? ValueExtractor { get; internal set; }
```

#### 8b：`EventTriggerFrameworkInitializer.cs`

**文件：** `CsharpPrj/Operation/BackEndDriven/EventTriggerFramework/EventTriggerFrameworkInitializer.cs`

**变更：**
1. 第147行：`options.ParameterExecutionWorkflow` 类型统一后自动修复
2. 第183行：`options.DedupWindow` → `TimeSpan.FromMilliseconds(options.DedupWindow)`
3. 第139行及后续：`ctx.CommandFrameworkHost` 等属性通过步骤8a添加后自动修复

### 步骤 9：修复 `ControlValueAccessorBase.cs`

**文件：** `CsharpPrj/Operation/BackEndDriven/EventTriggerFramework/ControlValueAccessorBase.cs`

**变更：**
1. 第154行：`ExecuteMultiFunctionBindingAsync` 的参数类型从 `EventTriggerFramework.Abstractions.IEventBindingInfo` 改为 `TConfig.IEventBindingInfo`
2. 第201行：方法签名中的 `IEventBindingInfo` 改为 `TConfig.IEventBindingInfo`

### 步骤 10：修复 `EventBindingDiagnosticContext.cs`

**文件：** `CsharpPrj/Share/EventBindingOrchestrator/Diagnostics/EventBindingDiagnosticContext.cs`

**变更：** 添加 `GetSnapshotString()` 方法：

```csharp
public static string GetSnapshotString() => string.Empty;
```

### 步骤 11：修复 `UnifiedConfigLoader.cs`

**文件：** `CsharpPrj/Operation/BackEndDriven/UnifiedConfig/UnifiedConfigLoader.cs`

**变更：**

#### 11a：创建 `ParamConfig` 类

在文件末尾或单独文件中添加：

```csharp
public class ParamConfig : IParamConfig
{
    public string Name { get; set; } = string.Empty;
    public Type Type { get; set; } = typeof(object);
    public bool IsOptional { get; set; }
    public object? DefaultValue { get; set; }
}
```

#### 11b：修复 `RegisterWorkflow` 方法

将 `new TrConfig { ... }` 改为使用 `ITrConfig` 的实现：

```csharp
private void RegisterWorkflow(UnifiedRegistrationEntry func)
{
    // TrConfig 是静态类，不能实例化
    // 改用匿名 ITrConfig 实现或使用 TrConfigRegistry 的 DelegateTrConfig
    var trConfig = new SimpleTrConfig
    {
        Name = func.Name,
        Description = func.Description,
        InputType = typeof(object),
        OutputType = typeof(object),
        IsAsync = false,
        Parameters = Array.Empty<IParamConfig>(),
        Delegate = null
    };

    if (_registry is ITrConfigRegistry trConfigRegistry)
    {
        trConfigRegistry.Register(func.Name, trConfig);
    }
}
```

需要创建 `SimpleTrConfig` 类实现 `ITrConfig` 接口。

#### 11c：修复 `RegisterDelegate` 方法

`IFunctionRegistry.Register` 没有接受 `IParamConfig[]` 的重载，需要调整：

```csharp
// 修改前
_registry.Register(func.Name, dlg, paramConfigs, func.Description);

// 修改后：IFunctionRegistry 只有 Register(string, Delegate) 和 Register(string, Delegate, string?) 重载
// 如果 _registry 同时也是 ITrConfigRegistry，使用 ITrConfigRegistry 的重载
if (_registry is ITrConfigRegistry trConfigRegistry)
{
    trConfigRegistry.Register(func.Name, dlg, paramConfigs, func.Description);
}
else
{
    _registry.Register(func.Name, dlg, func.Description);
}
```

---

## 四、影响范围

### 4.1 直接修改的文件（11个）

| # | 文件 | 操作类型 |
|---|------|----------|
| 1 | `CsharpPrj/Plate/TConfig/Contexts/FrameworkOptions.cs` | 修改接口签名 |
| 2 | `CsharpPrj/Plate/TConfig/Models/ParameterExecutionRequest.cs` | **新建** |
| 3 | `CsharpPrj/Plate/TConfig/Models/ParameterExecutionResult.cs` | **新建** |
| 4 | `CsharpPrj/Share/CommandFramework/Abstractions/IParameterExecutionWorkflow.cs` | **删除** |
| 5 | `CsharpPrj/Share/CommandFramework/Abstractions/ParameterExecutionRequest.cs` | **删除** |
| 6 | `CsharpPrj/Share/CommandFramework/Abstractions/ParameterExecutionResult.cs` | **删除** |
| 7 | `CsharpPrj/Share/CommandFramework/GlobalUsings.cs` | 修改 global using |
| 8 | `CsharpPrj/Operation/WorkFlowMethod/Execution/WorkflowMethodExecutionWorkflow.cs` | 修改 |
| 9 | `CsharpPrj/Operation/BackEndDriven/WorkFlowMethodBridge/BackEndDrivenBuilder.cs` | 修改 |
| 10 | `CsharpPrj/Operation/BackEndDriven/EventTriggerFramework/FrameworkContext.cs` | 添加属性 |
| 11 | `CsharpPrj/Operation/BackEndDriven/EventTriggerFramework/EventTriggerFrameworkInitializer.cs` | 修改 |
| 12 | `CsharpPrj/Operation/BackEndDriven/EventTriggerFramework/ControlValueAccessorBase.cs` | 修改 |
| 13 | `CsharpPrj/Share/EventBindingOrchestrator/Diagnostics/EventBindingDiagnosticContext.cs` | 添加方法 |
| 14 | `CsharpPrj/Operation/BackEndDriven/UnifiedConfig/UnifiedConfigLoader.cs` | 修改 + 新建类 |

### 4.2 间接影响（需验证编译）

- `CommandFramework/Invoke/Handlers/ClickCommandHandler.cs` — 使用 `IParameterExecutionWorkflow`，通过 global using 自动解析
- `CommandFramework/Invoke/ContainerExecutionCell.cs` — 同上
- `CommandFramework/Invoke/ExecuteCommandStage.cs` — 同上
- `CommandFramework/Pipeline/PipelineEntryOptions.cs` — 同上
- `CommandFramework/CommandFrameworkHost.cs` — 同上
- `CommandFramework/CommandFrameworkOptions.cs` — 同上

### 4.3 需要额外注意

`WorkflowMethodExecutionWorkflow.cs` 中 `request.ControlFinder` 的类型从 `FrameworkAbstractions.Controls.IControlFinder?` 变为 `object?`，第192行的空合并运算符 `??` 需要调整：

```csharp
// 修改前
var finder = request.ControlFinder ?? _controlFinder;

// 修改后（request.ControlFinder 现在是 object? 类型）
var finder = request.ControlFinder as FrameworkAbstractions.Controls.IControlFinder ?? _controlFinder;
```

---

## 五、验证方法

```bash
# 编译 BackEndDriven 项目
dotnet build "d:\Project\Program\WorkFlowPrj\CsharpPrj\Operation\BackEndDriven\BackEndDriven.csproj"

# 编译整个解决方案
dotnet build "d:\Project\Program\WorkFlowPrj\CsharpPrj\MainProj.sln"
```

预期结果：30 个错误全部消除，零错误零警告。

---

## 六、回退方案

如果重构后出现新的编译错误，按以下优先级排查：

1. **global using 冲突**：检查 `CommandFramework/GlobalUsings.cs` 中的别名是否正确
2. **类型引用歧义**：检查是否有文件同时引用了 `TConfig.IParameterExecutionWorkflow` 和 `CommandFramework.Abstractions.IParameterExecutionWorkflow`
3. **WorkflowMethodExecutionWorkflow 接口实现**：确认移除了旧的显式接口实现，新的 `ExecuteAsync` 方法签名与接口匹配
4. **ControlFinder 类型转换**：确认 `as` 转换正确
