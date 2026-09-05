# BackEndDriven 冗余逻辑清理报告

> **分析日期**：2026-07-02
> **根因定位**：`MathFunctions.json` 按钮触发无响应
> **清理范围**：BackEndDriven 层重复实现 Share 层框架的冗余代码

---

## 一、根因分析：按钮触发无响应

### 事件链路

```
按钮 Click
  → EventBindingTransformers 事件绑定
    → IEventExecutionService.ExecuteFunctionAsync("MathFunctions_Add", parameters)
      → EventExecutionService.ExecuteFunctionAsync()
        → IFunctionExecutionService.ExecuteFunctionAsync()  // ← 空实现！
          → (无操作，返回默认 ExecutionResult)
```

### 根因

`FunctionExecutionService` 的 `ExecuteFunctionAsync` 方法**没有实际执行逻辑**——它只是一个空壳，返回默认的 `ExecutionResult`。当 JSON 配置中 `eventType: "function"` 时，事件链路走到这个空实现就结束了，按钮自然无响应。

### 修复方案

`EventExecutionService` 改为直接依赖 Share 层的 `Modules.Registry.IFunctionRegistry`，通过 `TryGetDelegate` 获取委托后 `DynamicInvoke` 执行，消除 `IFunctionExecutionService` 这个空包装层。

---

## 二、冗余逻辑清单

### 第一批已删除文件（5 个）

| 文件 | 冗余原因 | Share 层替代 |
|------|---------|-------------|
| `Services/FunctionExecutionService.cs` | 空壳实现，`ExecuteFunctionAsync` 无实际逻辑 | `Modules.Registry.IFunctionRegistry.TryGetDelegate` + `DynamicInvoke` |
| `Pipeline/PipelineEntry.cs` | 重复注册 sleep/echo/exit 内置命令 | `Share.CommandFramework.Libraries.NonEventCommands` + `NonEventRegistry` |
| `Pipeline/Stages/ParseCommandStage.cs` | 泛型管线阶段，与 Share 层 `CommandFramework.Pipeline.Stages` 重复 | `Share.CommandFramework.Pipeline.Stages` |
| `Pipeline/Stages/ExecuteCommandStage.cs` | 同上 | `Share.CommandFramework.Pipeline.Stages` |
| `Pipeline/Stages/ResultCollectStage.cs` | 同上 | `Share.CommandFramework.Pipeline.Stages` |

### 第二批已删除文件（8 个）

| 文件 | 冗余原因 | Share 层替代 |
|------|---------|-------------|
| `Registry/BaseCommandRegistry.cs` | 用 `ConcurrentDictionary` 重复实现完整"注册→查找→执行"逻辑 | `CommandFramework.Registration.HandlerRegistry`（继承 `MapRegistryBase`） |
| `Registry/NonEventCommandRegistrar.cs` | 对 `INonEventCommandRegistry` 的薄包装，延迟注册逻辑 | `INonEventCommandRegistry` 直接使用 |
| `Registry/CommandValidator.cs` | 命令注册验证器，继承 `ValidatorBase`，仅提供 `TResult` 类型 | 合并到通用验证器 |
| `Lifecycle/ComponentValidator.cs` | 组件注册验证器，同上 | 合并到通用验证器 |
| `Abstractions/IEventBindingLookup.cs` | 已标记 `[Obsolete]` 但未删除，功能已迁移到 `EventTriggerFramework` | `EventTriggerFramework.Abstractions.IEventBindingInfo` |
| `Abstractions/EventBindingInfoAdapter.cs` | 适配已废弃的 `IEventBindingInfo` 接口 | 随接口删除 |
| `Abstractions/ControlTypeEventMapper.cs` | 包装 `EventTriggerFramework.EventTypeMapper`，增加 `InferEventType` | 合并到 `EventTypeMapper` |
| `Execution/EventBindingService.cs` | 实现已废弃的 `IEventBindingLookup`，通过反射遍历配置模型 | `EventTriggerFramework.Transformers.EventBinding.EventBindingContext` |

### 已改造文件（2 个）

| 文件 | 改造内容 |
|------|---------|
| `Execution/EventExecutionService.cs` | 构造函数参数从 `IFunctionExecutionService?` 改为 `IFunctionRegistry?`；`ExecuteFunctionAsync` 直接使用 `IFunctionRegistry.TryGetDelegate` + `DynamicInvoke`；`HasFunction` 改为 `IFunctionRegistry.Contains` |
| `BackEndDrivenBuilder.cs` | 移除 `Pipeline.PipelineEntry.RegisterBuiltInCommands(registry)` 调用；移除 `using BackEndDriven.Pipeline`；**移除 5 个死字段**（`_eventBindingLookup`/`_eventHub`/`_eventTypeMapper`/`_eventExecutionService`/`_eventBindingPipeline`）及对应 With* 方法 |

### 已新增引用

| 项目 | 引用 |
|------|------|
| `BackEndDriven.csproj` | 新增 `Modules.Registry\Registry.csproj` ProjectReference |

### 已删除空目录

| 目录 | 说明 |
|------|------|
| `Registry/` | 所有文件已删除，目录已清理 |

---

## 三、清理原则

1. **Share 层为唯一实现**：所有通用框架逻辑（管线、注册表、命令执行）以 Share 层为准
2. **BackEndDriven 层只做编排**：负责将 Share 层组件组合成具体业务流程
3. **不重复注册**：内置命令（sleep/echo/exit）由 Share 层 `NonEventRegistry` 统一注册
4. **不重复实现**：泛型管线阶段由 Share 层 `CommandFramework.Pipeline.Stages` 提供
5. **不死代码**：Builder 中未在 Build* 方法中使用的字段和 With* 方法应及时清理
6. **已废弃即删除**：标记 `[Obsolete]` 的接口和类应及时删除，避免误导后续开发者

---

## 四、BackEndDriven 当前文件结构

```
BackEndDriven/
├── Abstractions/
│   ├── IBindingFinderStrategy.cs    # 控件查找策略接口（GUI 模式）
│   ├── IBackEndPipelineContext.cs    # 通用管线上下文接口
│   └── ValidatorBase.cs             # 通用验证器基类（待迁移到 Share 层）
├── Execution/
│   ├── EventExecutionService.cs     # 通用事件执行服务（实现 Share.IEventExecutionService）
│   ├── ExecutionEngine.cs           # 通用执行引擎
│   ├── FrameworkExecutor.cs         # 通用执行门面（9 步骨架）
│   └── ModeDispatcher.cs            # 通用模式分发器
├── Lifecycle/
│   ├── DelegateLifecycleStep.cs     # 委托生命周期步骤
│   ├── FrameworkInitializer.cs      # 框架初始化器
│   ├── ILifecycleStep.cs            # 生命周期步骤接口
│   └── LifecycleManager.cs          # 生命周期管理器
├── Models/
│   ├── ExecutionResult.cs           # 执行结果模型
│   └── CommandLineResult.cs         # 命令行结果模型
├── Utilities/
│   └── BatchIdGenerator.cs          # 批次 ID 生成器
├── BackEndDrivenBuilder.cs          # 统一构建入口（Facade）
└── BackEndDriven.csproj             # 项目文件
```
| 项目 | 引用 |
|------|------|
| `BackEndDriven.csproj` | 新增 `Modules.Registry\Registry.csproj` ProjectReference |

### 已删除空目录

| 目录 | 说明 |
|------|------|
| `Registry/` | 所有文件已删除，目录已清理 |

---

## 三、清理原则

1. **Share 层为唯一实现**：所有通用框架逻辑（管线、注册表、命令执行）以 Share 层为准
2. **BackEndDriven 层只做编排**：负责将 Share 层组件组合成具体业务流程
3. **不重复注册**：内置命令（sleep/echo/exit）由 Share 层 `NonEventRegistry` 统一注册
4. **不重复实现**：泛型管线阶段由 Share 层 `CommandFramework.Pipeline.Stages` 提供
5. **不死代码**：Builder 中未在 Build* 方法中使用的字段和 With* 方法应及时清理
6. **已废弃即删除**：标记 `[Obsolete]` 的接口和类应及时删除，避免误导后续开发者

---

## 四、BackEndDriven 当前文件结构

```
BackEndDriven/
├── Abstractions/
│   ├── IBindingFinderStrategy.cs    # 控件查找策略接口（GUI 模式）
│   ├── IBackEndPipelineContext.cs    # 通用管线上下文接口
│   └── ValidatorBase.cs             # 通用验证器基类（待迁移到 Share 层）
├── Execution/
│   ├── EventExecutionService.cs     # 通用事件执行服务（实现 Share.IEventExecutionService）
│   ├── ExecutionEngine.cs           # 通用执行引擎
│   ├── FrameworkExecutor.cs         # 通用执行门面（9 步骨架）
│   └── ModeDispatcher.cs            # 通用模式分发器
├── Lifecycle/
│   ├── DelegateLifecycleStep.cs     # 委托生命周期步骤
│   ├── FrameworkInitializer.cs      # 框架初始化器
│   ├── ILifecycleStep.cs            # 生命周期步骤接口
│   └── LifecycleManager.cs          # 生命周期管理器
├── Models/
│   ├── ExecutionResult.cs           # 执行结果模型
│   └── CommandLineResult.cs         # 命令行结果模型
├── Utilities/
│   └── BatchIdGenerator.cs          # 批次 ID 生成器
├── BackEndDrivenBuilder.cs          # 统一构建入口（Facade）
