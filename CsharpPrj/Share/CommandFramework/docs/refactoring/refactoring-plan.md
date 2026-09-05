# CommandFramework 重构方案

## 一、背景与目标

### 1.1 问题描述

`CommandFramework` 和 `EventTriggerFramework` 两个项目之间存在大量**重复的接口和实现类**，导致：

- **命名冲突**：两个项目定义了同名的接口（如 `ICommandExecutionResult`、`IFunctionRegistry`、`IValueExtractor` 等），导致 `EventTriggerFramework` 编译时出现 `CS0104` 歧义引用错误
- **代码冗余**：`FunctionRegistry`、`FunctionExecutor`、`InMemoryStateStore`、`DefaultValueExtractor` 等实现类在两个项目中完全重复
- **维护困难**：修改一个项目中的接口需要同步修改另一个项目，容易遗漏
- **依赖混乱**：`EventTriggerFramework` 已引用 `CommandFramework`，但仍在自身项目中重复定义相同的类型

### 1.2 重构目标

- **彻底移除重复类型**：`EventTriggerFramework` 完全基于 `CommandFramework` 的原始逻辑实现
- **不拆分文件**：保持现有文件结构不变，只修改文件内容
- **保持功能完整**：确保重构后所有功能正常运行
- **消除命名冲突**：确保整个解决方案编译通过

---

## 二、重复类型清单

### 2.1 接口重复（`ICommandExecutionResult.cs`）

| 重复接口 | EventTriggerFramework 位置 | CommandFramework 位置 |
|---------|--------------------------|---------------------|
| `ICommandExecutionResult` | `EventTriggerFramework.Abstractions` | `CommandFramework.Abstractions` |
| `ICommandBatchResult` | `EventTriggerFramework.Abstractions` | `CommandFramework.Abstractions` |
| `IControlValueAccessor` | `EventTriggerFramework` | `CommandFramework.Abstractions` |
| `IEnvironmentAdapter` | `EventTriggerFramework.Abstractions` | `CommandFramework.Abstractions` |
| `IParameterDescriptor` | `EventTriggerFramework.Abstractions` | `CommandFramework.Abstractions` |
| `IFunctionDescriptor` | `EventTriggerFramework.Abstractions` | `CommandFramework.Abstractions` |
| `IFunctionExecutionResult` | `EventTriggerFramework.Abstractions` | `CommandFramework.Abstractions` |
| `IFunctionRegistry` | `EventTriggerFramework.Abstractions` | `CommandFramework.Abstractions` |
| `IFunctionExecutor` | `EventTriggerFramework.Abstractions` | `CommandFramework.Abstractions` |
| `IStateStore` | `EventTriggerFramework.Abstractions` | `CommandFramework.Abstractions` |
| `IValueExtractor` | `EventTriggerFramework.Abstractions` | `CommandFramework.Abstractions` |

### 2.2 接口重复（`ICommandPipeline.cs`）

| 重复接口 | EventTriggerFramework 位置 | CommandFramework 位置 |
|---------|--------------------------|---------------------|
| `Result<T>` | `EventTriggerFramework.Abstractions` | `CommandFramework.Abstractions` |
| `Result` | `EventTriggerFramework.Abstractions` | `CommandFramework.Abstractions` |
| `BatchResult<T>` | `EventTriggerFramework.Abstractions` | `CommandFramework.Abstractions` |
| `ICommandPipeline<TContext>` | `EventTriggerFramework.Abstractions` | `CommandFramework.Abstractions` |
| `IPipelineContext` | `EventTriggerFramework.Abstractions` | `CommandFramework.Abstractions` |
| `IPipelineStage<TContext>` | `EventTriggerFramework.Abstractions` | `CommandFramework.Abstractions` |

### 2.3 实现类重复（`ControlValueAccessorBase.cs`）

| 重复类 | EventTriggerFramework 位置 | CommandFramework 位置 |
|-------|--------------------------|---------------------|
| `InMemoryStateStore` | `EventTriggerFramework.Core` | `CommandFramework.Execution` |
| `ParameterDescriptor` | `EventTriggerFramework.Core` | `CommandFramework.Execution` |
| `FunctionDescriptor` | `EventTriggerFramework.Core` | `CommandFramework.Execution` |
| `FunctionRegistry` | `EventTriggerFramework.Core` | `CommandFramework.Execution` |
| `FunctionExecutor` | `EventTriggerFramework.Core` | `CommandFramework.Execution` |
| `DefaultValueExtractor` | `EventTriggerFramework.Core` | `CommandFramework.Execution` |
| `CommandExecutionResult` | `EventTriggerFramework.Core` | `CommandFramework.Execution` |
| `CommandBatchResult` | `EventTriggerFramework.Core` | `CommandFramework.Execution` |
| `ControlValueAccessorBase<TTarget>` | `EventTriggerFramework` | `CommandFramework.Execution` |

### 2.4 基类重复（`EnvironmentAdapterBase.cs`）

| 重复类 | EventTriggerFramework 位置 | CommandFramework 位置 |
|-------|--------------------------|---------------------|
| `EnvironmentAdapterBase` | `EventTriggerFramework.Adapters` | `CommandFramework.Execution` |

---

## 三、EventTriggerFramework 特有类型（需保留）

以下类型是 `EventTriggerFramework` 特有的，**不应移除**：

### 3.1 WinForms 相关

| 类型 | 文件 | 说明 |
|-----|------|------|
| `IWinFormsEventBinder` | `ICommandExecutionResult.cs` | WinForms 事件绑定器接口 |
| `WinFormsControlValueAccessor` | `WinForms/WinFormsControlValueAccessor.cs` | WinForms 控件值读写器 |
| `WinFormsEventBinder` | `WinForms/WinFormsEventBinder.cs` | WinForms 事件绑定器 |
| `WinFormsEventBus` | `WinForms/WinFormsEventBus.cs` | WinForms 事件总线 |
| `WinFormsEventNameResolver` | `WinForms/WinFormsEventNameResolver.cs` | WinForms 事件名称解析器 |
| `ReflectionEventBinder<Control>` | `WinForms/ReflectionEventBinder.cs` | 反射事件绑定器 |

### 3.2 事件绑定相关

| 类型 | 文件 | 说明 |
|-----|------|------|
| `IEventBindingService` | `ICommandExecutionResult.cs` | 事件绑定服务接口 |
| `IEventBinder` | `ICommandExecutionResult.cs` | 通用事件绑定器接口 |
| `IEventBindingInfo` | `ICommandExecutionResult.cs` | 事件绑定信息接口 |
| `IEventBindingResolver` | `ICommandExecutionResult.cs` | 事件绑定解析器接口 |
| `IEventNameResolver` | `ICommandExecutionResult.cs` | 事件名称解析器接口 |
| `IEventPipeline` | `ICommandExecutionResult.cs` | 事件管线接口 |
| `IEventTriggerResult` | `ICommandExecutionResult.cs` | 事件触发结果接口 |
| `IEventTriggerService` | `ICommandExecutionResult.cs` | 事件触发服务接口 |
| `IEventTypeResolver` | `ICommandExecutionResult.cs` | 事件类型解析器接口 |
| `DefaultEventTypeResolver` | `ICommandExecutionResult.cs` | 默认事件类型解析器 |
| `EventBindingInfo` | `EventBindingInfo.cs` | 事件绑定信息实现 |
| `DefaultEventBindingService` | `DefaultEventBindingService.cs` | 默认事件绑定服务 |

### 3.3 事件总线相关

| 类型 | 文件 | 说明 |
|-----|------|------|
| `DefaultEventBusCore` | `EventBus.cs` | 事件总线核心 |
| `EnhancedEventBusDecorator` | `EnhancedEventBusCore.cs` | 增强事件总线装饰器 |
| `EventBusFacade` | `EventBus.cs` | 事件总线门面 |
| `EventEngine` | `EventBus.cs` | 事件引擎 |

### 3.4 管线相关

| 类型 | 文件 | 说明 |
|-----|------|------|
| `CommandPipeline` | `Pipeline/CommandPipeline.cs` | 命令管线实现 |
| `EventBindingStage` | `Pipeline/EventBindingStage.cs` | 事件绑定阶段基类 |
| `ParseCommandStage` | `Pipeline/EventBindingStage.cs` | 命令解析阶段 |
| `WriteResultStage` | `Pipeline/EventBindingStage.cs` | 结果写入阶段 |
| `ExtractValuesStage` | `Pipeline/EventBindingStage.cs` | 值提取阶段 |
| `ExecuteFunctionStage` | `Pipeline/EventBindingStage.cs` | 函数执行阶段 |
| `FindBindingStage` | `Pipeline/EventBindingStage.cs` | 绑定查找阶段 |

### 3.5 其他特有类型

| 类型 | 文件 | 说明 |
|-----|------|------|
| `NonEventCommandHandler` | `ICommand.cs` | 非事件命令处理器（继承自 CommandFramework） |
| `EventTriggerResult` | `ControlValueAccessorBase.cs` | 事件触发结果实现 |
| `EventTriggerServiceBase` | `ControlValueAccessorBase.cs` | 事件触发服务基类 |
| `EventBindingExecutor` | `ControlValueAccessorBase.cs` | 事件绑定执行器 |
| `SimpleBindingResolver` | `EventTriggerFrameworkInitializer.cs` | 简单绑定解析器 |
| `SimpleEventTriggerService` | `EventTriggerFrameworkInitializer.cs` | 简单事件触发服务 |
| `SimpleEventPipeline` | `EventTriggerFrameworkInitializer.cs` | 简单事件管线 |

---

## 四、重构实施步骤

### 步骤 1：删除 `EventTriggerFramework\ICommandPipeline.cs`

**原因**：该文件中的所有类型（`Result<T>`、`Result`、`BatchResult<T>`、`ICommandPipeline<TContext>`、`IPipelineContext`、`IPipelineStage<TContext>`）在 `CommandFramework.Abstractions` 中已有完全相同的定义。

**影响**：
- `EventTriggerFrameworkInitializer.cs` 中使用了 `using Result = EventTriggerFramework.Abstractions.Result;` — 需要改为 `using CmdResult = CommandFramework.Abstractions.Result;`
- `EventBindingStage.cs` 中使用了 `ICommandPipeline<TContext>` — 需要改为 `CommandFramework.Abstractions.ICommandPipeline<TContext>`

### 步骤 2：删除 `EventTriggerFramework\EnvironmentAdapterBase.cs`

**原因**：该文件中的 `EnvironmentAdapterBase` 在 `CommandFramework.Execution` 中已有完全相同的定义。

**影响**：
- 无直接引用（`EventTriggerFramework` 中无其他类继承此基类）

### 步骤 3：重写 `EventTriggerFramework\ICommandExecutionResult.cs`

**操作**：移除所有与 `CommandFramework.Abstractions` 重复的接口定义，只保留 `EventTriggerFramework` 特有的接口。

**需保留的接口**：
- `IWinFormsEventBinder`
- `IEventBindingService`
- `IEventBinder`
- `IEventBindingInfo`
- `IEventBindingResolver`
- `IEventNameResolver`
- `IEventPipeline`
- `IEventTriggerResult`
- `IEventTriggerService`
- `IEventTypeResolver`
- `DefaultEventTypeResolver`

**需移除的接口**：
- `ICommandExecutionResult` → 改用 `CommandFramework.Abstractions.ICommandExecutionResult`
- `ICommandBatchResult` → 改用 `CommandFramework.Abstractions.ICommandBatchResult`
- `IControlValueAccessor` → 改用 `CommandFramework.Abstractions.IControlValueAccessor`
- `IEnvironmentAdapter` → 改用 `CommandFramework.Abstractions.IEnvironmentAdapter`
- `IParameterDescriptor` → 改用 `CommandFramework.Abstractions.IParameterDescriptor`
- `IFunctionDescriptor` → 改用 `CommandFramework.Abstractions.IFunctionDescriptor`
- `IFunctionExecutionResult` → 改用 `CommandFramework.Abstractions.IFunctionExecutionResult`
- `IFunctionRegistry` → 改用 `CommandFramework.Abstractions.IFunctionRegistry`
- `IFunctionExecutor` → 改用 `CommandFramework.Abstractions.IFunctionExecutor`
- `IStateStore` → 改用 `CommandFramework.Abstractions.IStateStore`
- `IValueExtractor` → 改用 `CommandFramework.Abstractions.IValueExtractor`

### 步骤 4：重写 `EventTriggerFramework\ControlValueAccessorBase.cs`

**操作**：移除所有与 `CommandFramework.Execution` 重复的实现类，只保留 `EventTriggerFramework` 特有的类。

**需保留的类**：
- `EventTriggerResult`
- `EventTriggerServiceBase`
- `EventBindingExecutor`

**需移除的类**：
- `InMemoryStateStore` → 改用 `CommandFramework.Execution.InMemoryStateStore`
- `ParameterDescriptor` → 改用 `CommandFramework.Execution.ParameterDescriptor`
- `FunctionDescriptor` → 改用 `CommandFramework.Execution.FunctionDescriptor`
- `FunctionRegistry` → 改用 `CommandFramework.Execution.FunctionRegistry`
- `FunctionExecutor` → 改用 `CommandFramework.Execution.FunctionExecutor`
- `DefaultValueExtractor` → 改用 `CommandFramework.Execution.DefaultValueExtractor`
- `CommandExecutionResult` → 改用 `CommandFramework.Execution.CommandExecutionResult`
- `CommandBatchResult` → 改用 `CommandFramework.Execution.CommandBatchResult`
- `ControlValueAccessorBase<TTarget>` → 改用 `CommandFramework.Execution.ControlValueAccessorBase<TTarget>`

### 步骤 5：修复 `EventTriggerFrameworkInitializer.cs`

**操作**：
- 移除 `using EventTriggerFramework.Core;`（因为 `FunctionRegistry`、`FunctionExecutor`、`InMemoryStateStore`、`DefaultValueExtractor` 已迁移到 `CommandFramework.Execution`）
- 添加 `using CommandFramework.Execution;`（使用 CommandFramework 中的实现类）
- 将 `FunctionRegistry` 改为 `CommandFramework.Execution.FunctionRegistry`
- 将 `FunctionExecutor` 改为 `CommandFramework.Execution.FunctionExecutor`
- 将 `InMemoryStateStore` 改为 `CommandFramework.Execution.InMemoryStateStore`
- 将 `DefaultValueExtractor` 改为 `CommandFramework.Execution.DefaultValueExtractor`
- 将 `CommandEngine` 改为 `CommandFramework.Execution.CommandEngine`
- 将 `Result` 别名改为指向 `CommandFramework.Abstractions.Result`

### 步骤 6：修复 `EventBindingStage.cs`

**操作**：
- 移除 `using EventTriggerFramework.Abstractions;`（因为 `ICommandExecutionResult`、`IFunctionRegistry` 等已迁移到 `CommandFramework.Abstractions`）
- 添加 `using CommandFramework.Abstractions;`（使用 CommandFramework 中的接口）
- 保留 `EventTriggerFramework.Abstractions` 中特有的接口引用（如 `IEventBindingResolver`、`IEventPipeline` 等）

### 步骤 7：修复其他引用文件

**需检查的文件**：
- `Pipeline/CommandPipeline.cs` — 检查是否引用了重复类型
- `EventBus.cs` — 检查是否引用了重复类型
- `EnhancedEventBusCore.cs` — 检查是否引用了重复类型
- `DefaultEventBindingService.cs` — 检查是否引用了重复类型
- `EventBindingInfo.cs` — 检查是否引用了重复类型
- `WinForms/*.cs` — 检查是否引用了重复类型

---

## 五、依赖关系图

```
重构前：
  EventTriggerFramework
    ├── EventTriggerFramework.Abstractions (重复接口)
    ├── EventTriggerFramework.Core (重复实现)
    ├── EventTriggerFramework.Adapters (重复基类)
    └── CommandFramework (引用)
          ├── CommandFramework.Abstractions
          └── CommandFramework.Execution

重构后：
  EventTriggerFramework
    ├── EventTriggerFramework.Abstractions (仅特有接口)
    ├── CommandFramework.Abstractions (引用)
    ├── CommandFramework.Execution (引用)
    └── CommandFramework (引用)
          ├── CommandFramework.Abstractions
          └── CommandFramework.Execution
```

---

## 六、风险与注意事项

### 6.1 命名空间冲突风险

重构后，`EventTriggerFramework` 中的代码需要同时使用 `EventTriggerFramework.Abstractions` 和 `CommandFramework.Abstractions` 两个命名空间。如果两个命名空间中有同名类型，仍会出现歧义。

**解决方案**：确保 `EventTriggerFramework.Abstractions` 中不再包含与 `CommandFramework.Abstractions` 同名的类型。

### 6.2 `Result<T>` 类型冲突

`EventTriggerFramework` 中大量使用了 `Result<T>` 类型。重构后需要统一使用 `CommandFramework.Abstractions.Result<T>`。

**解决方案**：在 `EventTriggerFrameworkInitializer.cs` 中使用 `using CmdResult = CommandFramework.Abstractions.Result;` 别名。

### 6.3 测试项目影响

`CentralConsole/Operation.Tests/EventTriggerFramework.Tests/NonEventCommandHandlerTests.cs` 可能引用了 `EventTriggerFramework.Core` 中的类型。

**解决方案**：检查测试项目中的引用，更新为 `CommandFramework.Execution` 中的对应类型。

### 6.4 外部项目引用

其他项目（如 `FileDriven`、`WdUI`、`WorkFlowMethod`）可能通过 `EventTriggerFramework` 间接使用了重复类型。

**解决方案**：重构后重新编译整个解决方案，修复所有编译错误。

---

## 七、验证清单

- [ ] `CommandFramework.csproj` 单独构建成功
- [ ] `EventTriggerFramework.csproj` 单独构建成功
- [ ] 整个解决方案 `WorkFlowPrj.sln` 构建成功
- [ ] 无 `CS0104` 歧义引用错误
- [ ] 无 `CS0246` 类型未找到错误
- [ ] 所有测试项目编译通过
- [ ] 功能测试通过
