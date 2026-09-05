# FileDrivenExecutor → BackEndDriven 迁移方案

> **目标**：将 FileDrivenExecutor.cs（1963行）中的通用逻辑逐步迁移到 BackEndDriven 通用框架，使其他模块也能复用。

---

## 一、当前架构分析

### 1.1 FileDrivenExecutor 职责分布

| 职责 | 行数 | 占比 | 说明 |
|------|------|------|------|
| 9 步执行骨架 | ~150 | 8% | Run() 的 try/finally 流程控制 |
| 步骤实现（Impl 方法） | ~400 | 20% | 各步骤的具体实现 |
| 非事件命令注册 | ~300 | 15% | sideload/sleep/close/exit/echo/exec |
| GUI 模式启动 | ~140 | 7% | MainForm 创建、配置加载、消息循环 |
| CLI 模式启动 | ~50 | 3% | 管线创建、命令执行、结果收集 |
| 框架初始化 | ~250 | 13% | JsonConverter、WdUI、FunctionStorage、模板引擎等 |
| 辅助方法 | ~200 | 10% | CreateExecutionEntry、CreateCommandExecutor 等 |
| 其他（字段/属性/构造函数/Dispose） | ~470 | 24% | 字段声明、属性、构造函数、日志等 |

### 1.2 三层分类

```
第一层：纯通用逻辑（10 项）→ 直接迁移到 BackEndDriven
  └─ 不依赖任何 FileDriven 特有类型

第二层：轻依赖逻辑（5 项）→ 通过接口抽象后迁移
  └─ 依赖可通过接口抽象的 FileDriven 类型

第三层：FileDriven 特有逻辑（18 项）→ 保留在 FileDrivenExecutor
  └─ 强依赖 FileDriven 特有类型，不应迁移
```

---

## 二、通用框架设计（BackEndDriven）

### 2.1 已构建的通用组件 ✅

| 组件 | 文件 | 设计模式 | 说明 |
|------|------|---------|------|
| `FrameworkExecutor` | `Execution/FrameworkExecutor.cs` | Template Method + Strategy | 9 步执行骨架，通过委托注入具体实现 |
| `ModeDispatcher` | `Execution/ModeDispatcher.cs` | Strategy | CLI/GUI/Pipe 三种模式分发 |
| `FrameworkInitializer` | `Lifecycle/FrameworkInitializer.cs` | Composite | 步骤化初始化/清理，封装 LifecycleManager |
| `FileDrivenExecutorAdapter` | `Execution/FileDrivenExecutorAdapter.cs` | Adapter | 展示如何继承 FrameworkExecutor |
| `PipelineContext<TContext>` | `Pipeline/PipelineContext.cs` | Generic | 通用管线上下文基类 |
| `IBackEndPipelineContext` | `Abstractions/IBackEndPipelineContext.cs` | Interface | 管线上下文接口契约 |
| `CommandExecutor` | `Executor/CommandExecutor.cs` | Facade | 单条命令执行器 |
| `BatchCommandExecutor` | `Executor/BatchCommandExecutor.cs` | Composite | 批量命令执行器 |
| `IExecutionPoolManager` | `Abstractions/IExecutionPoolManager.cs` | Interface | 执行池管理器接口 |

### 2.2 待构建的通用组件 🔧

| 组件 | 建议位置 | 设计模式 | 来源（FileDrivenExecutor 行号） |
|------|---------|---------|-------------------------------|
| `GlobalExceptionHandler` | `Execution/GlobalExceptionHandler.cs` | Singleton | L399-406：SetupGlobalExceptionHandlingImpl |
| `PipeConnector` | `Execution/PipeConnector.cs` | Strategy | L427-443：ConnectPipeImpl |
| `WorkingDirectoryManager` | `Execution/WorkingDirectoryManager.cs` | Static | L474-486：SetWorkingDirectoryImpl |
| `LoggingInitializer` | `Lifecycle/LoggingInitializer.cs` | Strategy | L1721-1763：AutoInitializeLogging |
| `ComponentValidator` | `Lifecycle/ComponentValidator.cs` | Strategy | L777-802：PerformComponentValidation |
| `CommandValidator` | `Registry/CommandValidator.cs` | Strategy | L817-843：PerformNonEventCommandValidation |
| `CommandExecutorFactory` | `Executor/CommandExecutorFactory.cs` | Factory | L1641-1688：CreateCommandExecutor |

---

## 三、迁移步骤

### 3.1 第一优先级：纯通用逻辑（无依赖）

这些逻辑不依赖任何 FileDriven 特有类型，可直接迁移。

#### 步骤 1：迁移 `SetupGlobalExceptionHandlingImpl` → `GlobalExceptionHandler`

```csharp
// BackEndDriven/Execution/GlobalExceptionHandler.cs
public static class GlobalExceptionHandler
{
    public static void Configure(
        Action<Exception>? onThreadException = null,
        Action<Exception>? onAppDomainException = null)
    {
        // 从 FileDrivenExecutor.L399-406 提取
        Application.ThreadException += (sender, e) =>
            onThreadException?.Invoke(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            onAppDomainException?.Invoke(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
    }
}
```

#### 步骤 2：迁移 `ConnectPipeImpl` → `PipeConnector`

```csharp
// BackEndDriven/Execution/PipeConnector.cs
public class PipeConnector : IDisposable
{
    public void Connect(string? pipeName) { ... }
    public void Disconnect() { ... }
    public void Dispose() { ... }
}
```

#### 步骤 3：迁移 `SetWorkingDirectoryImpl` → `WorkingDirectoryManager`

```csharp
// BackEndDriven/Execution/WorkingDirectoryManager.cs
public static class WorkingDirectoryManager
{
    public static bool SetToProjectRoot(Func<string?> projectRootFinder) { ... }
}
```

#### 步骤 4：迁移 `ShutdownLoggingImpl` → `FrameworkExecutor` 内置

`FrameworkExecutor` 已包含 `ShutdownLogging` 委托，无需额外组件。

#### 步骤 5：迁移 `DisconnectPipeImpl` → `PipeConnector`

合并到 PipeConnector 中。

### 3.2 第二优先级：轻依赖逻辑（需接口抽象）

这些逻辑依赖可通过接口抽象的 FileDriven 类型。

#### 步骤 6：迁移 `SetupLoggingImpl` → 定义 `ILoggingInitializer` 接口

```csharp
// BackEndDriven/Abstractions/ILoggingInitializer.cs
public interface ILoggingInitializer
{
    void Initialize(object options);
    void Shutdown();
}
```

FileDriven 实现：
```csharp
public class FileDrivenLoggingInitializer : ILoggingInitializer
{
    public void Initialize(object options)
    {
        var cliOptions = (CliOptions)options;
        FileDrivenLogManager.Initialize(cliOptions);
    }
    public void Shutdown() => FileDrivenLogManager.Shutdown();
}
```

#### 步骤 7：迁移 `ParseArgumentsImpl` → 定义 `IArgumentParser` 接口

```csharp
// BackEndDriven/Abstractions/IArgumentParser.cs
public interface IArgumentParser
{
    object Parse(string[] args);
}
```

#### 步骤 8：迁移 `PerformComponentValidation` → `ComponentValidator`

```csharp
// BackEndDriven/Lifecycle/ComponentValidator.cs
public class ComponentValidator
{
    public ValidationReport ExecuteAndReport(
        Action<string>? logCallback = null,
        params Type[] contextTypes) { ... }
}
```

#### 步骤 9：迁移 `PerformNonEventCommandValidation` → `CommandValidator`

```csharp
// BackEndDriven/Registry/CommandValidator.cs
public class CommandValidator
{
    public ValidationResult ValidateAll() { ... }
}
```

#### 步骤 10：迁移 `CreateCommandExecutor` → `CommandExecutorFactory`

```csharp
// BackEndDriven/Executor/CommandExecutorFactory.cs
public static class CommandExecutorFactory
{
    public static Func<string, Task<OperationResult<object>>> Create(
        ICommandParser parser,
        INonEventCommandLookup registry) { ... }
}
```

### 3.3 第三优先级：FileDriven 特有逻辑（不应迁移）

| 逻辑 | 保留理由 |
|------|---------|
| `InitializeFrameworkImpl` | 强依赖 FileDrivenWdUIBridge、FunctionStorageLifecycle 等 |
| `RegisterComponents` | 注册 IEventBindingService、IControlService 等 FileDriven 特有服务 |
| `InitializeTemplateEngine` | 强依赖 TemplateConfiguration、JsonPipelineRegistry |
| `LoadPathAliasesFromEnvironment` | 强依赖 PathAliasLoader |
| `InitializeBatchLogging` | 强依赖 BatchManager、BatchLoggerBridge |
| `RegisterNonEventCommands` | 注册 sideload/sleep/close/exit/echo/exec 命令 |
| `InitializeExecutionCenter` | 强依赖 TaskExecutorPool、FileDrivenPipelineMessage |
| `InjectEventBinderToRenderPipeline` | 强依赖 WdUIUsage、FileDrivenWdUIBridge |
| `StartGuiMode` | 强依赖 MainForm、WinFormsEventBinderAdapter |
| `StartCliMode` | 强依赖 ExecutionPipelineEntry、BatchLoggerBridge |
| `ShutdownFrameworkImpl` | 强依赖 _executionPool、_fileDrivenLifecycle |
| `CreateExecutionEntry` | 强依赖 PipelineEntry（FileDriven.Pipeline） |
| `ResolveCommandFile` | 强依赖 PathResolutionService、ScriptConverter |
| `GetOutputTargetFromParameters` | 强依赖 IExecutionContext（CommandFramework） |
| `ConvertToPrinterResults` | 强依赖 FileDrivenFramework.Core.CommandFileLineResult |
| `SetWinFormsEventBinder` | 强依赖 DefaultEventBindingService |
| `ProcessJsonWithTemplate` | 强依赖 JsonTemplateProcessor |
| `Demo` | 演示方法，强依赖 FileDriven 完整流程 |

---

## 四、应用方案

### 4.1 FileDrivenExecutor 继承 FrameworkExecutor

```
FileDrivenExecutor : FrameworkExecutor
  ├─ 继承 9 步骨架（Run() 方法）
  ├─ 通过委托注入 FileDriven 特有实现
  ├─ 保留 FileDriven 特有逻辑（第三类）
  └─ 使用 BackEndDriven 通用组件（第一、二类）
```

### 4.2 委托映射表

| FrameworkExecutor 委托 | FileDrivenExecutor 实现 | 状态 |
|----------------------|------------------------|------|
| `SetupGlobalExceptionHandling` | `SetupGlobalExceptionHandlingImpl()` | ✅ 已映射 |
| `ParseArguments` | `ParseArgumentsImpl()` | ✅ 已映射 |
| `ConnectPipe` | `ConnectPipeImpl()` | ✅ 已映射 |
| `SetupLogging` | `SetupLoggingImpl()` | ✅ 已映射 |
| `SetWorkingDirectory` | `SetWorkingDirectoryImpl()` | ✅ 已映射 |
| `InitializeFramework` | `InitializeFrameworkImpl()` | ✅ 已映射 |
| `ExecuteMode` | `StartCliMode()` / `StartGuiMode()` | ✅ 已映射 |
| `ShutdownFramework` | `ShutdownFrameworkImpl()` | ✅ 已映射 |
| `ShutdownLogging` | `ShutdownLoggingImpl()` | ✅ 已映射 |
| `DisconnectPipe` | `DisconnectPipeImpl()` | ✅ 已映射 |

### 4.3 其他模块复用示例

```csharp
// 其他模块继承 FrameworkExecutor
public class MyExecutor : FrameworkExecutor
{
    public MyExecutor()
    {
        ParseArguments = args => MyOptions.Parse(args);
        SetupLogging = options => MyLogger.Initialize((MyOptions)options);
        ExecuteMode = options => RunMyLogic((MyOptions)options);
        // ... 只需配置需要的步骤
    }
}
```

---

## 五、迁移路线图

```
Phase 1（当前）✅
  └─ FrameworkExecutor + ModeDispatcher + FrameworkInitializer + FileDrivenExecutorAdapter
  └─ FileDrivenExecutor 继承 FrameworkExecutor

Phase 2（本次）
  └─ GlobalExceptionHandler → BackEndDriven
  └─ PipeConnector → BackEndDriven
  └─ WorkingDirectoryManager → BackEndDriven
  └─ ILoggingInitializer 接口 → BackEndDriven
  └─ IArgumentParser 接口 → BackEndDriven
  └─ ComponentValidator → BackEndDriven
  └─ CommandValidator → BackEndDriven
  └─ CommandExecutorFactory → BackEndDriven

Phase 3（未来）
  └─ 其他模块（如 Plate/Modules）复用 FrameworkExecutor
  └─ 更多通用逻辑提取
```

---

## 六、设计原则

1. **渐进式迁移**：每次迁移一个组件，保持构建通过
2. **接口优先**：先定义接口契约，再实现具体逻辑
3. **向后兼容**：迁移后的组件不破坏现有 FileDrivenExecutor 的行为
4. **最少依赖**：BackEndDriven 层不引入 WinForms/FileDriven 依赖
5. **可测试性**：接口化使单元测试可以 mock 依赖
