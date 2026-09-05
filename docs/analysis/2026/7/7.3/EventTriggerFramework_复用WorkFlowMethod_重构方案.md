# EventTriggerFramework 复用 WorkFlowMethod 重构方案

> **目标**：EventTriggerFramework 项目保留，在其现有基础上复用 WorkFlowMethod 的管线能力，使 UI 事件触发后的执行路径走 WorkFlowMethod，而非直接调用 BackEndDriven.EventExecutionService。

---

## 1. 背景

### 1.1 当前架构

```
UI点击 → EventBinderTransformer.CreateEventHandler
  → AppServiceProvider.Resolve<IEventExecutionService>()
  → BackEndDriven.EventExecutionService.ExecuteFunctionAsync()
  → IFunctionRegistry.TryGetDelegate → DynamicInvoke
```

### 1.2 目标架构

```
UI点击 → EventBinderTransformer.CreateEventHandler
  → ParameterResolver.ExecuteFunction (委托)
  → WorkFlowMethod 的 NodeProcess 管线
  → 结果写回 WorkflowRuntimeContext
```

### 1.3 核心原则

- **EventTriggerFramework 项目保留**，不删除、不迁移
- **WinForms 特有逻辑**（控件查找、事件绑定、值访问器）保留在 EventTriggerFramework 中
- **仅事件触发后的函数执行路径**改为走 WorkFlowMethod
- **通过静态委托解耦**，EventTriggerFramework 不直接引用 WorkFlowMethod 的具体类型

---

## 2. 职责边界

### 2.1 EventTriggerFramework 保留的职责

| 模块 | 文件 | 保留原因 |
|------|------|---------|
| 事件绑定管线 | `EventBinding/EventBindingTransformers.cs` | WinForms 控件事件绑定逻辑 |
| 管线组合 | `EventBinding/EventBindingPipeline.cs` | 管线编排 |
| 上下文模型 | `EventBinding/EventBindingModels.cs` | 绑定状态承载 |
| 绑定工具 | `EventBinding/EventBindingHelper.cs` | OutputMapping 解析 |
| 外观层 | `DefaultEventBindingService.cs` | 对外接口 |
| 框架初始化 | `EventTriggerFrameworkInitializer.cs` | 生命周期管理 |
| WinForms 特有 | `WinForms/` 目录 | 控件查找/值访问/事件绑定 |
| 事件总线 | `EventBus/` 目录 | 独立功能 |
| 接口定义 | `Abstractions/` 目录 | 被 FileDriven 引用 |

### 2.2 WorkFlowMethod 接管的部分

| 功能 | 当前实现 | 改为 |
|------|---------|------|
| 函数执行 | `BackEndDriven.EventExecutionService.ExecuteFunctionAsync()` | `WorkFlowMethod.NodeProcess` 管线 |
| 参数解析 | `BuildParametersFromBindings` 中直接读控件 | 通过 `ParameterResolver.ResolveParameter` 委托从 `WorkflowRuntimeContext` 读取 |
| 结果回写 | `WriteOutputToControl` 中直接写控件 | 通过 `ParameterResolver.SetResult` 委托写回 `WorkflowRuntimeContext` |

### 2.3 BackEndDriven 保留的职责（不受影响）

| 模块 | 保留原因 |
|------|---------|
| `CommandExecutionService` | 命令系统，WorkFlowMethod 不处理 |
| `ExecutionEngine` | CLI 执行引擎，与 UI 事件触发无关 |
| `BackEndDrivenBuilder` | 构建入口，与 UI 事件触发无关 |
| `FrameworkInitializer` | 生命周期管理，与 UI 事件触发无关 |
| `Pipeline/` 目录 | 管线基础设施 |
| `Utilities/` 目录 | 工具类 |

---

## 3. 已完成的改造

### 3.1 `ParameterResolver` 静态委托类

**文件**：`CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingTransformers.cs`

在 `EventTriggerFramework.Transformers.EventBinding` 命名空间中新增：

```csharp
public static class ParameterResolver
{
    /// <summary>按 ParameterKey 解析参数值的委托</summary>
    public static Func<string, object?>? ResolveParameter { get; set; }

    /// <summary>按 ResultKey 设置结果值的委托</summary>
    public static Action<string, object?>? SetResult { get; set; }

    /// <summary>★ 新增：按函数名执行函数的委托</summary>
    public static Func<string, object?[], Task<object?>>? ExecuteFunction { get; set; }
}
```

**设计要点**：
1. 使用静态委托而非接口，避免引入额外类型
2. 默认实现返回 null（向后兼容），调用方需检查 null
3. 线程安全：委托本身是只读的，注入后不再修改
4. 由 `WorkFlowMethod` 在初始化时注入具体实现

### 3.2 `BindingMetadataTransformer` 虚方法化

**文件**：`CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingTransformers.cs`

- `Transform` 方法改为 `virtual`，允许子类重写
- `SaveControlParameters` 改为 `protected virtual`，允许子类自定义存储方式
- `BuildParametersFromBindings` 改为 `protected static`，允许子类访问
- `IsWorkflowParameterKey` 改为 `protected static`，允许子类访问

### 3.3 `WorkflowBindingMetadataTransformer`

**文件**：`CsharpPrj/Operation/WorkFlowMethod/NodeProcess/KeyMapping/WorkflowBindingMetadataTransformer.cs`

继承 `BindingMetadataTransformer`，重写 `SaveControlParameters`：
- 额外保存 `dataBinding` 信息到 `Control.Tag`
- 构建 `dataBindingMap`（parameterKey → controlName 映射）

### 3.4 `WorkflowParameterResolverInitializer`

**文件**：`CsharpPrj/Operation/WorkFlowMethod/NodeProcess/KeyMapping/WorkflowParameterResolverInitializer.cs`

注入 `ParameterResolver` 的三个委托：
- `ResolveParameter` → `WorkflowRuntimeContext.Current?.GetParameter(key)`
- `SetResult` → `WorkflowRuntimeContext.Current?.SetResult(key, value)`
- `ExecuteFunction` → `WorkflowUseage.ExecuteFunction(functionName, parameters)`

### 3.5 项目引用

- `WorkFlowMethod.csproj` 已添加对 `EventTriggerFramework` 的项目引用
- 构建验证：0 错误 0 警告

---

## 4. 待完成的改造

### 4.1 `EventBinderTransformer.CreateEventHandler` — 执行路径切换

**文件**：`CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingTransformers.cs`

**位置**：第 464-471 行

**当前代码**：
```csharp
var resolvedService = FrameworkAbstractions.ComponentRegistry.AppServiceProvider
    .Resolve<IEventExecutionService>();
if (resolvedService == null)
{
    EventTriggerLog.Log("EventBinder_ExecutionServiceNull", ...);
    return;
}
var execResult = await resolvedService.ExecuteFunctionAsync(functionName, parameters);
```

**改为**：
```csharp
// ★ 优先通过 ParameterResolver.ExecuteFunction 委托走 WorkFlowMethod 管线
if (ParameterResolver.ExecuteFunction != null)
{
    var resultValue = await ParameterResolver.ExecuteFunction(functionName, parameters);
    // 处理结果...
}
else
{
    // 回退：通过 AppServiceProvider 解析 IEventExecutionService（向后兼容）
    var resolvedService = FrameworkAbstractions.ComponentRegistry.AppServiceProvider
        .Resolve<IEventExecutionService>();
    // ...
}
```

### 4.2 `EventTriggerFrameworkInitializer` — 初始化路径调整

**文件**：`CsharpPrj/Operation/EventTriggerFramework/EventTriggerFrameworkInitializer.cs`

**当前**：独立初始化 EventBus、Commands、Pipeline、Core、WinForms

**改为**：
- 保留 `InitializeEventBus`（EventBus 是 EventTriggerFramework 特有功能）
- 保留 `InitializeWinForms`（WinForms 特有）
- 移除 `InitializeCommands`（命令系统由 WorkFlowMethod 管理）
- 移除 `InitializePipeline`（管线由 WorkFlowMethod 管理）
- 移除 `InitializeCore` 中的 `StateStore`/`ValueExtractor`（由 WorkFlowMethod 管理）
- 新增：初始化完成后调用 `WorkflowParameterResolverInitializer.EnsureInitialized()` 注入委托

### 4.3 `DefaultEventBindingService` — 清理

**文件**：`CsharpPrj/Operation/EventTriggerFramework/DefaultEventBindingService.cs`

- 移除 `IEventExecutionService` 相关的注释和逻辑
- 构造函数中移除 `IEventExecutionService` 参数（已标记 `[Obsolete]`）

### 4.4 `EventBindingPipeline` — 清理

**文件**：`CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingPipeline.cs`

- 移除 `[Obsolete]` 的 `IEventExecutionService` 构造函数重载

---

## 5. 数据流对比

### 5.1 改前

```
┌─────────────────────────────────────────────────────────────────────┐
│ UI 点击按钮                                                          │
│   ↓                                                                  │
│ EventBinderTransformer.CreateEventHandler()                          │
│   ↓                                                                  │
│ BuildParametersFromBindings()                                        │
│   ├── 从 Control.Tag 读取 inputs = ["valueATextBox", "valueBTextBox"]│
│   ├── FindByName("valueATextBox") → 从 WinForms 控件树查找           │
│   ├── GetValue(TextBox) → "10" (string)                              │
│   └── 返回 ["10", "20"]                                              │
│   ↓                                                                  │
│ AppServiceProvider.Resolve<IEventExecutionService>()                  │
│   ↓                                                                  │
│ BackEndDriven.EventExecutionService.ExecuteFunctionAsync("Add",...)   │
│   ├── IFunctionRegistry.TryGetDelegate("Add") → 获取委托              │
│   ├── TypeConverter.ConvertArguments → 类型转换                       │
│   ├── DynamicInvoke(10.0, 20.0) → 30.0                               │
│   └── 返回 ExecutionResult { Value = 30.0 }                          │
│   ↓                                                                  │
│ WriteOutputToControl()                                               │
│   ├── 从 Tag 读取 output = "resultTextBox"                            │
│   ├── FindByName("resultTextBox") → 从 WinForms 控件树查找            │
│   └── SetValue(TextBox, "30")                                        │
└─────────────────────────────────────────────────────────────────────┘
```

### 5.2 改后

```
┌─────────────────────────────────────────────────────────────────────┐
│ UI 点击按钮                                                          │
│   ↓                                                                  │
│ EventBinderTransformer.CreateEventHandler()                          │
│   ↓                                                                  │
│ BuildParametersFromBindings()                                        │
│   ├── 从 Control.Tag 读取 inputs = ["MathInputA", "MathInputB"]      │
│   ├── IsWorkflowParameterKey("MathInputA") → true (在 dataBindingMap)│
│   ├── ParameterResolver.ResolveParameter("MathInputA")               │
│   │     → WorkflowRuntimeContext.GetParameter("MathInputA") → "10"   │
│   ├── IsWorkflowParameterKey("MathInputB") → true                    │
│   ├── ParameterResolver.ResolveParameter("MathInputB")               │
│   │     → WorkflowRuntimeContext.GetParameter("MathInputB") → "20"   │
│   └── 返回 ["10", "20"]                                              │
│   ↓                                                                  │
│ ParameterResolver.ExecuteFunction("Add", ["10", "20"])               │
│   ↓                                                                  │
│ WorkFlowMethod.NodeProcess 管线                                       │
│   ├── 函数查找 → 类型转换 → 执行 → 结果返回                          │
│   └── 返回 30.0                                                      │
│   ↓                                                                  │
│ WriteOutputToControl()                                               │
│   ├── 从 Tag 读取 output = "MathResult"                               │
│   ├── IsWorkflowParameterKey("MathResult") → true (在 dataBindingMap)│
│   ├── ParameterResolver.SetResult("MathResult", "30")                │
│   │     → WorkflowRuntimeContext.SetResult("MathResult", "30")       │
│   └── 同时更新 UI: resultTextBox.Text = "30"                         │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 6. 向后兼容策略

| 条件 | 行为 | 兼容性 |
|------|------|--------|
| `ParameterResolver.ExecuteFunction` 未注入 | 回退为 `AppServiceProvider.Resolve<IEventExecutionService>()` | ✅ 完全兼容 |
| `ParameterResolver.ResolveParameter` 未注入 | `BuildParametersFromBindings` 回退为从 WinForms 控件树读取 | ✅ 完全兼容 |
| `ParameterResolver.SetResult` 未注入 | `WriteOutputToControl` 回退为直接写 WinForms 控件 | ✅ 完全兼容 |
| 无 `dataBinding` 配置 | `IsWorkflowParameterKey` 返回 false，走旧路径 | ✅ 完全兼容 |
| 无 `WorkflowRuntimeContext` | `ParameterResolver` 委托返回 null，走旧路径 | ✅ 自动降级 |

---

## 7. 改造清单

| # | 文件 | 改动内容 | 状态 |
|:-:|------|---------|:---:|
| 1 | `EventBindingTransformers.cs` | 新增 `ParameterResolver.ExecuteFunction` 委托 | ✅ 已完成 |
| 2 | `EventBindingTransformers.cs` | `BindingMetadataTransformer.Transform` 改为 `virtual` | ✅ 已完成 |
| 3 | `EventBindingTransformers.cs` | `SaveControlParameters` 改为 `protected virtual` | ✅ 已完成 |
| 4 | `EventBindingTransformers.cs` | `BuildParametersFromBindings` 改为 `protected static` | ✅ 已完成 |
| 5 | `EventBindingTransformers.cs` | `IsWorkflowParameterKey` 改为 `protected static` | ✅ 已完成 |
| 6 | `WorkflowBindingMetadataTransformer.cs` | 新建，继承 `BindingMetadataTransformer` | ✅ 已完成 |
| 7 | `WorkflowParameterResolverInitializer.cs` | 新建，注入三个委托 | ✅ 已完成 |
| 8 | `WorkFlowMethod.csproj` | 添加对 `EventTriggerFramework` 的引用 | ✅ 已完成 |
| 9 | `EventBindingTransformers.cs` | `CreateEventHandler` 中切换执行路径 | ⏳ 待完成 |
| 10 | `EventTriggerFrameworkInitializer.cs` | 调整初始化路径 | ⏳ 待完成 |
| 11 | `DefaultEventBindingService.cs` | 清理 `IEventExecutionService` 相关逻辑 | ⏳ 待完成 |
| 12 | `EventBindingPipeline.cs` | 移除 `[Obsolete]` 构造函数重载 | ⏳ 待完成 |

---

## 8. 关键设计决策

### 8.1 为什么用静态委托而非接口？

- **避免引入额外类型**：EventTriggerFramework 不需要新增接口定义
- **零侵入**：EventTriggerFramework 的现有代码无需修改接口签名
- **延迟绑定**：WorkFlowMethod 在初始化时注入，时序问题自然解决
- **向后兼容**：默认实现返回 null，旧代码不受影响

### 8.2 为什么 BackEndDriven 不受影响？

- `EventExecutionService` 仍然存在，作为 `IEventExecutionService` 接口的实现
- 只是 EventTriggerFramework 不再直接调用它
- 其他模块（如 CLI 模式）仍然可以通过 `AppServiceProvider` 使用它
- 当 `ParameterResolver.ExecuteFunction` 未注入时，EventTriggerFramework 自动回退到 `IEventExecutionService`

### 8.3 为什么 EventTriggerFrameworkInitializer 需要调整？

- 当前 `EventTriggerFrameworkInitializer` 独立初始化 Commands/Pipeline/Core
- 这些功能在 WorkFlowMethod 接管后由 WorkFlowMethod 统一管理
- 保留 EventBus 和 WinForms 初始化，因为它们是 EventTriggerFramework 特有的
- 调整后 EventTriggerFramework 成为 WorkFlowMethod 的轻量前端层

---

## 9. 预期效果

1. **UI 成为工作流的前端视图层**：按钮点击直接读写 `WorkflowRuntimeContext`
2. **消除参数映射重复**：UI 管线复用 WorkFlowMethod 的 `KeyProcessor` 和 `TypeConverter`
3. **工作流可视化调试**：通过 UI 手动输入参数、触发函数、查看结果
4. **渐进式迁移**：旧配置无需修改，新功能使用 `dataBinding` 声明
5. **统一数据流**：所有数据传递经过 `WorkflowRuntimeContext`，便于追踪和审计
6. **零新语法**：`inputs` 直接使用工作流的 `ParameterKeys` 语法，不创造新的包装格式
