# MetaCommand Adapters.cs 通用化改造 - 实施完成

## 改动清单

### 1. `Adapters.cs` — 彻底重写，移除所有 GUI/CLI 概念

| 旧类/概念 | 新类/概念 | 说明 |
|---|---|---|
| `RunModeResolver` + `ShouldRunGuiMode()` | **已删除** | 运行模式判断是应用层职责，不应在基础类库中 |
| `ParsedCommandArguments.ControlName` | `ParsedCommandArguments.Identifier` | 通用化命名，不暗示 GUI 控件 |
| `CommandArgumentParser.ExtractControlName()` | `CommandArgumentParser.ExtractIdentifier()` | 同上 |
| `IEnvironmentAdapter` | **已移至 `Abstractions.cs` 并重命名为 `ICommandExecutor`** | 通用命令执行器接口 |
| `BaseAdapter` | `BaseCommandExecutor` | 通用执行器基类 |
| `ConsoleAdapter` | `NullCommandExecutor` | 空对象模式，不依赖 `System.Console` |
| `IConfigLifecycleManager` | `IResourceLifecycleManager` | 通用资源生命周期管理 |
| `ConfigLifecycleManager` | `DefaultResourceLifecycleManager` | 通用实现 |
| `ConfigLifecycleManager` (适配器类) | `ResourceCleaner` | 通用资源清理器 |

### 2. `Abstractions.cs` — 接口重命名

| 旧接口 | 新接口 |
|---|---|
| `IEnvironmentAdapter` | `ICommandExecutor` |
| `ICommandRouter.RegisterAdapter()` | `ICommandRouter.RegisterExecutor()` |
| `ICommandRouter.SetActiveAdapter()` | `ICommandRouter.SetActiveExecutor()` |
| `ICommandRouter.GetActiveAdapterId()` | `ICommandRouter.GetActiveExecutorId()` |
| `ICommandRouter.GetRegisteredAdapters()` | `ICommandRouter.GetRegisteredExecutors()` |
| `ICommandRouter.TryGetAdapter()` | `ICommandRouter.TryGetExecutor()` |
| `ICommandRouter.UnregisterAdapter()` | `ICommandRouter.UnregisterExecutor()` |

### 3. `Execution.cs` — 适配接口变更

- `CommandRouter` 内部字段 `_adapters` → `_executors`
- `CommandRouter` 内部字段 `_activeAdapterId` → `_activeExecutorId`
- `CommandRouter` 内部字段 `_adapterLock` → `_executorLock`
- 所有方法实现同步更新

### 4. `Extensions.cs` — 适配接口变更

- `AddConsoleAdapter()` → `AddNullExecutor()`
- `RegisterAndActivateAdapter()` → `RegisterAndActivateExecutor()`
- `IsAdapterRegistered()` → `IsExecutorRegistered()`
- `AdapterCount()` → `ExecutorCount()`

### 5. `UICommands.cs` — 适配属性名变更

- `parsed.ControlName` → `parsed.Identifier`

### 6. `CliOptions.cs` — 移除对 `RunModeResolver` 的依赖

- `ShouldRunGuiMode()` 改为内联实现，不再依赖 `MetaCommand.RunModeResolver`

## 编译状态

✅ **MetaCommand 项目编译成功** — 0 个警告，0 个错误
