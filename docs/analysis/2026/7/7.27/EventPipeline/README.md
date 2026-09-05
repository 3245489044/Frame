# EventPipeline — UI 事件→管线执行完整链路

> 目标：将 UI 从点击到触发的所有逻辑集中在 `EventPipeline/` 一个文件夹中

## 调用链

```
用户点击 UI 控件
  ↓
WinForms 原生事件（.NET 运行时调度）
  ↓
[EventPipeline/WinFormsEventBinder.cs]
  WinFormsEventBinder.BindControlEvents() 中绑定的 EventHandler
  → 检查 IsScriptExecutionMode（脚本执行模式跳过）
  → _eventExecutionService.Execute(ctrl.Name, eventName, value)
    [依赖 BackEndDriven.Abstractions.IEventExecutionService]
  ↓
[BackEndDriven.Execution.EventExecutionService] (内部使用)
  EventExecutionService.Execute()
  ├─ BuildEventCommand() → 使用 IEventTypeMapper 映射命令动作
  └─ Execute() → 委托调用 _executePipeline(command, context)
       ↓
[EventPipeline/EventPipelineExecutor.cs]
  EventPipelineExecutor.ExecutePipelineInternal()
  ├─ CreateMinimalContext(command, targetForm)
  └─ GetOrCreateSingleCommandPipeline().Execute(context)
       ↓
[EventPipeline/EventBindingResolver.cs]
  EventBindingResolver.FindEventBinding(controlName, eventType)
  [继承 BackEndDriven.Execution.EventBindingService]
  → 强类型遍历 UIConfiguration.Views → ControlConfig
  → 从 ControlConfig.Events 提取函数名列表
  → 返回 IEventBindingInfo
       ↓
[Pipeline/ExecuteCommandStage.cs]
  ExecuteCommandStage 通过 bindingFinder 获取绑定信息
  → FunctionExecutor.Execute(functionName, args)
```

## 文件说明

| 文件 | 职责 | 与 BackEndDriven 关系 |
|------|------|----------------------|
| `EventBindingResolver.cs` | 事件绑定解析器 | **继承** `BackEndDriven.Execution.EventBindingService`，利用强类型 ControlConfig |
| `EventPipelineExecutor.cs` | 事件管线执行器 | **内部使用** `BackEndDriven.Execution.EventExecutionService`，将管线逻辑封装为委托 |
| `EventTriggerService.cs` | 事件触发服务 | **继承** `BackEndDriven.Execution.EventTriggerEngine`，实现三个抽象方法 |
| `WinFormsEventBinder.cs` | WinForms 事件绑定器 | **依赖** `BackEndDriven.Abstractions.IEventExecutionService` 接口 |
| `WinFormsEventRegistrar.cs` | 事件注册辅助方法 | 无变化（纯 WinForms 辅助） |
| `EventBindingInfo.cs` | 事件绑定信息 DTO | 无变化（实现 `EventTriggerFramework.Abstractions.IEventBindingInfo`） |
| `EventHub.cs` | 框架级生命周期事件中心 | 无变化（已使用 `EventBusFacade`） |
| `Models/LoadedConfigInfo.cs` | 已加载配置信息 DTO | 无变化 |

## 架构说明

EventPipeline 通过以下方式与 BackEndDriven 框架集成：

1. **继承**：`EventBindingResolver` 继承 `EventBindingService`，利用强类型重写遍历逻辑
2. **组合**：`EventPipelineExecutor` 内部使用 `EventExecutionService`，通过委托注入管线执行
3. **继承**：`EventTriggerService` 继承 `EventTriggerEngine`，实现 Template Method 抽象方法
4. **接口依赖**：`WinFormsEventBinder` 依赖 `IEventExecutionService`，与具体实现解耦
