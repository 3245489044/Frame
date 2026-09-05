# FileDriven.Execution 通用框架提取分析报告

> **分析日期**：2026-06-29  
> **分析范围**：`CsharpPrj/FileDriven/FileDriven.Execution/` 全部源码  
> **关联项目**：`CsharpPrj/Operation/BackEndDriven/`（已提取目标）  
> **文档目的**：识别 FileDriven.Execution 中可提取为通用框架的逻辑，评估已提取成果，规划未来提取路线

---

## 1. 概述

### 1.1 FileDriven.Execution 的架构定位

`FileDriven.Execution` 是 FileDriven 五层架构的执行层，封装了从参数解析到框架关闭的完整调用链：

```
Run(args)
├── [0] SetupGlobalExceptionHandling()
├── [1] ParseArguments(args) → CliOptions
├── [2] NamedPipeClient.Connect()（可选）
├── [3] SetupLogging(options)
├── [4] SetWorkingDirectory()
├── [5] InitializeFramework(cliMode)
├── [6] 模式分发（GUI / CLI）
├── [7] ShutdownFramework(cliMode)
├── [8] ShutdownLogging()
└── [9] NamedPipeClient.Dispose()
```

### 1.2 核心模块一览

| 模块 | 文件 | 行数 | 职责 |
|------|------|------|------|
| 执行门面 | `FileDrivenExecutor.cs` | 1920 | 五层架构统一入口，生命周期管理 |
| 管线入口 | `ExecutionPipelineEntry.cs` | 583 | 上下文创建/管线构建/执行/异步调度 |
| 管线工厂 | `PipelineFactory.cs` | 354 | CentralPipeline 构建（ScriptLoad + ForEach + 命令管线） |
| 管线上下文 | `FileDrivenPipelineContext.cs` | 582 | 贯穿管线的统一上下文 |
| 命令注册中心 | `CommandRegistry.cs` | 262 | 命令 handler 注册与执行 |
| 控件服务 | `ControlServices.cs` | 377 | 统一控件服务（查找/读写/注册/事件绑定） |
| 控件查找器 | `ControlFinder.cs` | 305 | 控件查找策略链 |
| 控件值读写器 | `ControlValueWriter.cs` | 595 | 控件值读写适配器体系 |
| 控件注册入口 | `ControlServiceCollection.cs` | 270 | 控件服务集中注册 |
| 执行结果 | `ExecutionResult.cs` | 89 | 统一执行结果模型 |

---

## 2. 各模块可通用性分析

### 2.1 管线执行引擎

#### 2.1.1 当前实现

`PipelineFactory.cs` 构建了两层管线：

```
CentralPipeline<FileDrivenPipelineContext>
├── ScriptLoadSkeleton（Initialization 阶段）
└── ForEachSkeleton（Execution 阶段）
    └── DelegatePipeline（命令管线）
        ├── ParseCommandStage
        ├── ExecuteCommandStage
        └── ResultCollectStage
```

#### 2.1.2 可通用部分

| 组件 | 通用性 | 说明 |
|------|--------|------|
| `CentralPipeline<TContext>` | ✅ 已通用 | 位于 `Share/CentralPipeline`，泛型设计 |
| `DelegatePipeline<TIn, TOut>` | ✅ 已通用 | 位于 `Modules.Pipeline`，委托驱动的管线组合 |
| `ForEachSkeleton<TContext>` | ⚠️ 部分通用 | 泛型设计，但硬编码了 `FileDrivenPipelineContext` 约束 |
| `SyncSkeletonBase<TIn, TOut>` | ✅ 已通用 | 位于 `SkeletonFlow` |
| `ParseCommandStage` | ❌ FileDriven 特有 | 依赖 `CommandParser` 和 `ParsedCommand` |
| `ExecuteCommandStage` | ❌ FileDriven 特有 | 依赖 `IEventBindingResolver` 和 `IFunctionExecutor` |
| `ResultCollectStage` | ⚠️ 部分通用 | 结果收集逻辑可抽象，但依赖 `CommandFileLineResult` |

#### 2.1.3 提取建议

**ForEachSkeleton 泛型约束放宽**：
```csharp
// 当前（硬编码约束）：
public class ForEachSkeleton<TContext> : SyncSkeletonBase<TContext, TContext>
    where TContext : FileDrivenPipelineContext

// 建议（接口约束）：
public class ForEachSkeleton<TContext> : SyncSkeletonBase<TContext, TContext>
    where TContext : class, IBackEndPipelineContext
```

**管线阶段接口化**：
```csharp
// 提取通用阶段接口
public interface IPipelineStage<TContext>
{
    string Name { get; }
    void Process(TContext context);
}
```

---

### 2.2 上下文模型

#### 2.2.1 当前实现

`FileDrivenPipelineContext` 继承自 `CommandContext`（EventTriggerFramework），实现 `IBackEndPipelineContext` 接口。

```
CommandContext（EventTriggerFramework）
└── FileDrivenPipelineContext（FileDriven.Execution）
    ├── BatchData（BatchContextData 组合模式）
    ├── 脚本加载相关（ScriptPath, CommandFilePath, CommandLines）
    ├── 命令执行相关（ParsedVerb, ParsedAction, ParsedTarget 等便捷属性）
    ├── 执行统计（CollectedLogCount, SkipCount, CommandResults）
    ├── 运行时依赖（BindingResolver, FunctionExecutor, TargetForm 等）
    └── IBackEndPipelineContext 显式接口实现
```

#### 2.2.2 可通用部分

| 组件 | 通用性 | 说明 |
|------|--------|------|
| `IBackEndPipelineContext` | ✅ **已提取** | 位于 `BackEndDriven.Abstractions` |
| `BatchContextData` | ✅ **已提取** | 位于 `BackEndDriven.Models`，组合模式共享 8 个字段 |
| `Reset()` 运行时依赖保护模式 | ⚠️ 部分通用 | 保存/恢复运行时依赖的模式可抽象 |
| 便捷属性映射模式 | ⚠️ 部分通用 | `ParsedVerb`/`ParsedAction` 等委托给 `ParsedCommand` |
| `SideloadFailed` 降级标志 | ❌ FileDriven 特有 | 依赖 sideload 机制 |

#### 2.2.3 提取建议

**运行时依赖保护模式通用化**：
```csharp
// 通用接口
public interface IRuntimeDependencyAware
{
    void SaveRuntimeDependencies();
    void RestoreRuntimeDependencies();
}

// 通用基类或扩展方法
public static class RuntimeDependencyExtensions
{
    public static void PreserveRuntimeDependencies<T>(
        this T context,
        Action<T> saveAction,
        Action<T> restoreAction)
        where T : IBackEndPipelineContext
    {
        saveAction(context);
        context.Reset();
        restoreAction(context);
    }
}
```

---

### 2.3 命令注册中心

#### 2.3.1 当前实现

`CommandRegistry` 实现了双接口：
- `FileDriven.Abstractions.ICommandRegistry`
- `BackEndDriven.Abstractions.ICommandRegistry`

内部采用 Facade 模式委托给 `NonEventCommandCenter.Registry`。

#### 2.3.2 可通用部分

| 组件 | 通用性 | 说明 |
|------|--------|------|
| `ICommandRegistry` 接口 | ✅ **已提取** | 位于 `BackEndDriven.Abstractions` |
| 同步/异步 handler 注册模式 | ✅ 通用 | 可提取为通用注册中心基类 |
| `CommandExecutionResult` | ✅ **已提取** | 位于 `BackEndDriven.Models` |
| `ExecutionContext` | ⚠️ 部分通用 | 实现了 `IExecutionContext`，但字段较简单 |

#### 2.3.3 提取建议

**通用命令注册中心基类**：
```csharp
// 可提取到 BackEndDriven
public abstract class BaseCommandRegistry<TExecutionContext>
    where TExecutionContext : class, IExecutionContext
{
    protected Dictionary<string, Action<TExecutionContext>> SyncHandlers = new();
    protected Dictionary<string, Func<TExecutionContext, Task>> AsyncHandlers = new();

    public void Register(string name, Action<TExecutionContext> handler, ...)
    public void RegisterAsync(string name, Func<TExecutionContext, Task> handler, ...)
    public bool TryGetHandler(string name, out Action<TExecutionContext>? handler)
    public bool TryGetAsyncHandler(string name, out Func<TExecutionContext, Task>? handler)
}
```

---

### 2.4 控件服务体系

#### 2.4.1 当前实现

控件服务体系由四个文件组成，采用 **Composite 组合模式**：

```
ControlServiceCollection.RegisterAll()
├── IControlFinder（ControlRegistryAwareFinder）
│   ├── 策略1: IControlRegistry 查找
│   └── 策略2: WinFormsControlFinder 递归查找
├── IControlValueWriter（CompositeValueWriter）
│   ├── WinFormsControlValueWriter（Label/TextBox/ListView/RichTextBox）
│   └── 可扩展：WdUI 适配器等
└── IControlRegistry（可选，GUI 模式）
```

`UnifiedControlService` 整合了五大接口：
- `IControlFinder` — 控件查找
- `IControlValueAccessor` — 值读写
- `IControlEventBinder` — 事件绑定
- `IControlInvoker` — UI 线程调度
- `IControlRegistry` — 控件注册

#### 2.4.2 可通用部分

| 组件 | 通用性 | 说明 |
|------|--------|------|
| `CompositeValueWriter` 模式 | ✅ **通用** | Composite 组合模式，可提取为通用工具 |
| `CompositeControlFinder` 模式 | ✅ **通用** | 策略链模式，可提取为通用工具 |
| `IControlValueWriter` 接口 | ⚠️ 部分通用 | 接口定义通用，但 WinForms 实现是特化 |
| `IControlFinder` 接口 | ⚠️ 部分通用 | 接口定义通用，但 WinForms 实现是特化 |
| `WinFormsControlValueWriter` | ❌ WinForms 特有 | 适配器注册表模式可通用，但控件类型是 WinForms |
| `ControlRegistryAwareFinder` | ❌ WinForms 特有 | 依赖 WinForms Control 类型 |
| `UnifiedControlService` | ❌ FileDriven 特有 | 整合了 MetaCommand 接口 |

#### 2.4.3 提取建议

**Composite 模式工具提取**：
```csharp
// 可提取到 BackEndDriven 或 Share
public class CompositeService<TService> where TService : class
{
    private readonly List<TService> _services = new();

    public void Add(TService service) { _services.Add(service); }
    public bool Remove(TService service) { return _services.Remove(service); }
    public void Clear() { _services.Clear(); }
    public int Count => _services.Count;
    public IReadOnlyList<TService> Services => _services.AsReadOnly();
}
```

**适配器注册表模式提取**：
```csharp
// 通用适配器注册表（不依赖 WinForms）
public class AdapterRegistry<TKey, TWriter, TReader>
    where TKey : class
{
    private readonly Dictionary<Type, (TWriter Writer, TReader Reader)> _adapters = new();

    public void Register<T>(TWriter writer, TReader reader) where T : TKey { ... }
    public bool TryGetAdapter(Type type, out (TWriter, TReader) adapter) { ... }
}
```

---

### 2.5 执行池生命周期

#### 2.5.1 当前实现

`ExecutionPoolLifecycle` 管理 `ExecutionCenter` 任务执行池的生命周期。

#### 2.5.2 可通用部分

| 组件 | 通用性 | 说明 |
|------|--------|------|
| `ExecutionPoolLifecycle` | ✅ **已提取** | 位于 `BackEndDriven.Execution` |
| `IExecutionPoolManager` | ✅ **已提取** | 位于 `BackEndDriven.Abstractions` |
| 引用计数模式 | ✅ **通用** | 防止重复创建/释放 |
| 泛型消息处理器注册 | ✅ **通用** | `RegisterHandler<TMessage>` |

#### 2.5.3 提取建议

**生命周期管理器模式文档化**：
```csharp
// 通用生命周期管理器模式
public interface ILifecycleManager
{
    void Initialize();
    void Shutdown();
    bool IsInitialized { get; }
}

// 引用计数实现可提取为通用基类
public abstract class RefCountLifecycleManager : ILifecycleManager
{
    private int _refCount;
    protected abstract void OnInitialize();
    protected abstract void OnShutdown();

    public void Initialize()
    {
        if (_refCount++ > 0) return;
        OnInitialize();
    }

    public void Shutdown()
    {
        if (--_refCount > 0) return;
        OnShutdown();
    }
}
```

---

### 2.6 结果模型

#### 2.6.1 当前实现

`ExecutionResult` 实现了 `IExecutionResult` 接口，支持从 `(FileDrivenPipelineContext, TimeSpan)` 隐式转换。

`CommandExecutionResult` 实现了双接口：
- `CommandFramework.Abstractions.ICommandExecutionResult`
- `BackEndDriven.Abstractions.ICommandExecutionResult`

#### 2.6.2 可通用部分

| 组件 | 通用性 | 说明 |
|------|--------|------|
| `CommandExecutionResult` | ✅ **已提取** | 位于 `BackEndDriven.Models` |
| `ICommandExecutionResult` | ✅ **已提取** | 位于 `BackEndDriven.Abstractions` |
| `IExecutionResult` | ✅ **已提取** | 位于 `BackEndDriven.Abstractions` |
| 隐式转换模式 | ✅ **通用** | `implicit operator` 从元组转换 |
| 静态工厂方法模式 | ✅ **通用** | `EmptySuccess()` / `Failure()` |

#### 2.6.3 提取建议

**通用结果模型基类**：
```csharp
// 可提取到 BackEndDriven
public abstract class BaseExecutionResult : IExecutionResult
{
    public bool Success { get; set; }
    public int TotalCommands { get; set; }
    public int SuccessCount { get; set; }
    public int FailCount { get; set; }
    public TimeSpan ExecutionTime { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Mode { get; set; }
    public string? BatchId { get; set; }

    public static T EmptySuccess<T>(string mode = "CLI") where T : BaseExecutionResult, new() { ... }
    public static T Failure<T>(string error, string mode = "CLI") where T : BaseExecutionResult, new() { ... }
}
```

---

### 2.7 工具类

#### 2.7.1 当前实现

`BatchIdGenerator` 提供统一的 BatchId 生成逻辑。

#### 2.7.2 可通用部分

| 组件 | 通用性 | 说明 |
|------|--------|------|
| `BatchIdGenerator` | ✅ **已提取** | 位于 `BackEndDriven.Utilities` |
| 线程安全序列号 | ✅ **通用** | `Interlocked.Increment` |
| `GetOrGenerate` 模式 | ✅ **通用** | 空值回退生成 |

---

### 2.8 生命周期管理（FileDrivenExecutor）

#### 2.8.1 当前实现

`FileDrivenExecutor` 的 `InitializeFramework` / `ShutdownFramework` 实现了步骤化的框架生命周期管理：

```
InitializeFramework:
  AutoInitializeLogging → JsonConverterRegistrar → WdUI Init →
  ProjectPathManager → LoadPathAliases → 函数注册 → CommandManager →
  RegisterComponents → TemplateEngine → BatchLogging → ComponentValidation

ShutdownFramework:
  ExecutionPool.Dispose → FileDrivenLifecycle.Shutdown →
  FunctionStorageLifecycle.Shutdown → CommandManager.Shutdown →
  FileDrivenWdUIBridge.Shutdown
```

#### 2.8.2 可通用部分

| 组件 | 通用性 | 说明 |
|------|--------|------|
| 细粒度 try/finally 资源跟踪 | ✅ **通用** | 每个步骤跟踪初始化状态，精确清理 |
| 步骤化初始化模式 | ✅ **通用** | 可提取为 `ILifecycleStep` 接口 |
| 组件注册模式 | ⚠️ 部分通用 | `ComponentRegistry` 是通用机制 |
| 函数注册 | ❌ FileDriven 特有 | `MathLibrary`/`UIToolbox` 等 |

#### 2.8.3 提取建议

**步骤化生命周期框架**：
```csharp
// 可提取到 BackEndDriven
public interface ILifecycleStep
{
    string Name { get; }
    int Order { get; }
    void Initialize();
    void Shutdown();
}

public class LifecycleManager
{
    private readonly List<ILifecycleStep> _steps = new();
    private readonly HashSet<string> _initializedSteps = new();

    public void AddStep(ILifecycleStep step) { _steps.Add(step); }
    public void InitializeAll() { /* 按 Order 排序执行 */ }
    public void ShutdownAll() { /* 逆序释放已初始化的步骤 */ }
}
```

---

## 3. 已提取到 BackEndDriven 的成果清单

### 3.1 接口层（`BackEndDriven.Abstractions`）

| 接口 | 源文件 | 说明 |
|------|--------|------|
| `IBackEndPipelineContext` | 新增 | 通用管线上下文接口，FileDrivenPipelineContext 实现 |
| `ICommandRegistry` | 新增 | 通用命令注册接口，CommandRegistry 实现 |
| `IExecutionResult` | 新增 | 通用执行结果接口，ExecutionResult 实现 |
| `ICommandExecutionResult` | 新增 | 通用命令执行结果接口，CommandExecutionResult 实现 |
| `IExecutionPoolManager` | 新增 | 执行池管理器接口，ExecutionPoolLifecycle 实现 |

### 3.2 模型层（`BackEndDriven.Models`）

| 模型 | 源文件 | 说明 |
|------|--------|------|
| `BatchContextData` | 新增 | 批次上下文共享数据，组合模式 |
| `CommandFileLineResult` | 新增 | 命令文件行执行结果 |
| `CommandExecutionResult` | 新增 | 命令执行结果（含静态工厂方法） |
| `ExecutionResult` | 新增 | 批量执行结果 |
| `CommandLineResult` | 新增 | 通用命令行结果 |

### 3.3 工具层（`BackEndDriven.Utilities`）

| 工具 | 源文件 | 说明 |
|------|--------|------|
| `BatchIdGenerator` | 新增 | 批次 ID 生成器（线程安全） |

### 3.4 执行层（`BackEndDriven.Execution`）

| 组件 | 源文件 | 说明 |
|------|--------|------|
| `ExecutionPoolLifecycle` | 新增 | 执行池生命周期管理器（引用计数） |

---

## 4. 未来提取路线图

### Phase 1：管线上下文接口标准化（优先级：高）

**目标**：使 `ForEachSkeleton` 等通用骨架不再依赖 `FileDrivenPipelineContext`

**具体任务**：
1. 将 `ForEachSkeleton<TContext>` 的泛型约束从 `FileDrivenPipelineContext` 改为 `IBackEndPipelineContext`
2. 提取 `IPipelineStage<TContext>` 通用阶段接口
3. 将 `Reset()` 运行时依赖保护模式抽象为通用扩展方法

**影响范围**：
- `PipelineFactory.cs` — ForEachSkeleton 约束修改
- `FileDrivenPipelineContext.cs` — Reset 模式抽象
- 新增 `BackEndDriven.Pipeline` 命名空间

### Phase 2：命令注册中心接口下沉（优先级：中）

**目标**：使 `BaseCommandRegistry<TExecutionContext>` 成为通用基类

**具体任务**：
1. 提取 `BaseCommandRegistry<TExecutionContext>` 到 `BackEndDriven`
2. 将 `CommandRegistry` 改为继承基类
3. 统一 `IExecutionContext` 接口定义

**影响范围**：
- `CommandRegistry.cs` — 改为继承基类
- 新增 `BackEndDriven.Registry` 命名空间

### Phase 3：控件服务接口抽象化（优先级：低）

**目标**：将 Composite 组合模式和适配器注册表模式提取为通用工具

**具体任务**：
1. 提取 `CompositeService<TService>` 通用组合容器
2. 提取 `AdapterRegistry<TKey, TWriter, TReader>` 通用适配器注册表
3. 将 `IControlFinder` / `IControlValueWriter` 接口定义下沉到 `BackEndDriven.Abstractions`

**影响范围**：
- `ControlFinder.cs` — CompositeControlFinder 改为使用通用组合容器
- `ControlValueWriter.cs` — CompositeValueWriter 改为使用通用组合容器
- 新增 `BackEndDriven.Composition` 命名空间

### Phase 4：生命周期管理器模式推广（优先级：低）

**目标**：将步骤化生命周期管理提取为通用框架

**具体任务**：
1. 提取 `ILifecycleStep` 接口和 `LifecycleManager` 类
2. 将 `FileDrivenExecutor` 的 InitializeFramework/ShutdownFramework 改为使用 LifecycleManager
3. 提取 `RefCountLifecycleManager` 通用基类

**影响范围**：
- `FileDrivenExecutor.cs` — 生命周期管理重构
- `ExecutionPoolLifecycle.cs` — 改为继承 RefCountLifecycleManager
- 新增 `BackEndDriven.Lifecycle` 命名空间

---

## 5. 架构建议

### 5.1 新模块复用 BackEndDriven 的路径

```
新模块（如 ForntEndDriven）
    │
    ├── 管线执行 → 引用 BackEndDriven.Pipeline
    │   ├── IBackEndPipelineContext（上下文接口）
    │   ├── ForEachSkeleton（通用遍历骨架）
    │   └── IPipelineStage（通用阶段接口）
    │
    ├── 命令注册 → 引用 BackEndDriven.Registry
    │   ├── BaseCommandRegistry（通用注册中心基类）
    │   └── ICommandRegistry（注册接口）
    │
    ├── 执行池 → 引用 BackEndDriven.Execution
    │   ├── ExecutionPoolLifecycle（生命周期管理器）
    │   └── IExecutionPoolManager（管理器接口）
    │
    ├── 结果模型 → 引用 BackEndDriven.Models
    │   ├── ExecutionResult（批量结果）
    │   ├── CommandExecutionResult（单命令结果）
    │   └── BatchContextData（批次上下文）
    │
    └── 工具类 → 引用 BackEndDriven.Utilities
        └── BatchIdGenerator（批次 ID 生成器）
```

### 5.2 关键设计原则

1. **接口优先**：所有通用功能先定义接口（`BackEndDriven.Abstractions`），再提供默认实现
2. **组合优于继承**：使用 Composite 模式组合多个实现，避免注册覆盖
3. **依赖注入**：通过 `ComponentRegistry` 运行时获取依赖，避免编译时循环引用
4. **泛型约束最小化**：通用骨架的泛型约束使用接口而非具体类
5. **逐步迁移**：每个 Phase 独立可交付，不影响现有 FileDriven 功能

### 5.3 已消除的重复代码统计

| 重复模式 | 消除方式 | 涉及文件数 |
|----------|----------|-----------|
| BatchId 生成逻辑 | 提取到 `BatchIdGenerator` | 3 → 1 |
| 执行池生命周期管理 | 提取到 `ExecutionPoolLifecycle` | 2 → 1 |
| 命令执行结果模型 | 提取到 `CommandExecutionResult` | 3 → 1 |
| 管线上下文接口 | 提取到 `IBackEndPipelineContext` | 2 → 1 |
| 命令注册接口 | 提取到 `ICommandRegistry` | 2 → 1 |
| 执行结果接口 | 提取到 `IExecutionResult` | 2 → 1 |
| 控件查找策略链 | Composite 模式统一 | 3 → 1 |
| 控件值读写策略链 | Composite 模式统一 | 3 → 1 |

---

## 附录 A：文件依赖关系图

```
FileDriven.Execution
├── FileDrivenExecutor.cs
│   ├── ExecutionPipelineEntry.cs
│   │   ├── PipelineFactory.cs
│   │   │   ├── CentralPipeline<T>（Share）
│   │   │   ├── ForEachSkeleton<T>（本模块）
│   │   │   ├── DelegatePipeline（Modules.Pipeline）
│   │   │   └── SyncSkeletonBase（SkeletonFlow）
│   │   ├── FileDrivenPipelineContext.cs
│   │   │   ├── CommandContext（EventTriggerFramework）
│   │   │   ├── IBackEndPipelineContext（BackEndDriven.Abstractions）← 已提取
│   │   │   └── BatchContextData（BackEndDriven.Models）← 已提取
│   │   └── PipelineEntry（FileDriven.Pipeline）
│   ├── CommandRegistry.cs
│   │   ├── ICommandRegistry（BackEndDriven.Abstractions）← 已提取
│   │   └── CommandExecutionResult（本模块 → 已提取到 BackEndDriven）
│   ├── ControlServices.cs / ControlFinder.cs / ControlValueWriter.cs / ControlServiceCollection.cs
│   │   ├── IControlFinder / IControlValueWriter（CommandFramework.Abstractions）
│   │   └── ComponentRegistry（FrameworkAbstractions）
│   └── ExecutionResult.cs
│       └── IExecutionResult（BackEndDriven.Abstractions）← 已提取
│
BackEndDriven（已提取目标）
├── Abstractions/
│   ├── IBackEndPipelineContext.cs
│   ├── ICommandRegistry.cs
│   ├── IExecutionResult.cs
│   ├── ICommandExecutionResult.cs
│   └── IExecutionPoolManager.cs
├── Models/
│   ├── BatchContextData.cs
│   ├── CommandFileLineResult.cs
│   ├── CommandExecutionResult.cs
│   ├── ExecutionResult.cs
│   └── CommandLineResult.cs
├── Utilities/
│   └── BatchIdGenerator.cs
└── Execution/
    └── ExecutionPoolLifecycle.cs
```

---

## 附录 B：术语对照

| 术语 | 说明 |
|------|------|
| CentralPipeline | 通用管线执行引擎，支持多阶段注册 |
| SkeletonFlow | 骨架流框架，提供 SyncSkeletonBase 等基类 |
| ForEachSkeleton | 遍历骨架，对集合中的每个元素执行子管线 |
| DelegatePipeline | 委托驱动的管线组合，通过 lambda 串联阶段 |
| Composite 模式 | 组合多个实现，按优先级依次尝试 |
| ComponentRegistry | 运行时组件注册中心，支持按接口/名称解析 |
| IBackEndPipelineContext | 通用管线上下文接口，解耦上下文继承体系 |
| BatchContextData | 批次上下文共享数据，组合模式消除字段重复 |
