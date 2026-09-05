# EventTriggerFramework 重构方案

## 一、重构目标

将 `FileDriven` 项目中的事件触发逻辑统一迁移到 `Share/EventTriggerFramework`，消除重复代码，建立统一的通用事件触发框架。

### 核心原则

1. **EventTriggerFramework 作为唯一通用框架** — 所有通用逻辑集中在 `Share/EventTriggerFramework`
2. **FileDriven 只保留 CLI 特有逻辑** — 不包含与 EventTriggerFramework 重复的代码
3. **接口统一** — 所有接口定义在 `EventTriggerFramework.Abstractions`，通过 `FileDrivenFramework.Abstractions` 的 global using 别名引用

---

## 二、重构完成项（6 项）

### ✅ 1. 基类切换：EventTriggerService

| 项目 | 重构前 | 重构后 |
|------|--------|--------|
| 基类 | `FileDrivenFramework.Core.EventTriggerServiceBase` | `EventTriggerFramework.Core.EventTriggerServiceBase` |
| 构造函数参数 | `(IEventBindingResolver)` | `(IEventBindingResolver, IValueExtractor, IStateStore?)` |
| `ExecuteFunctionAsync` 签名 | `(string, IReadOnlyList<object?>)` | `(string, Dictionary<string, string?>)` |
| `WriteOutputAsync` 签名 | `(string, string)` | `(string, object?)` |

**涉及文件：**
- `FileDriven/FileDriven.CLI/Services/EventTriggerService.cs` — 基类切换 + 签名适配
- `Share/EventTriggerFramework/Core/EventTriggerServiceBase.cs` — 通用基类（保持不变）

### ✅ 2. 基类切换：EventBindingResolver

| 项目 | 重构前 | 重构后 |
|------|--------|--------|
| 基类 | `FileDrivenFramework.Core.EventBindingResolverBase` | `EventTriggerFramework.Core.EventBindingResolverBase` |
| 抽象方法 | `FindControlByName` + `GetEventFunction` + `GetBindingInfo` + `GetControlTypeFromConfig` | `FindBindingInConfig` + `GetControlTypeFromConfig` |

**涉及文件：**
- `FileDriven/FileDriven.CLI/Services/EventBindingResolver.cs` — 基类切换 + 方法适配
- `Share/EventTriggerFramework/Core/EventBindingResolverBase.cs` — 通用基类（保持不变）

### ✅ 3. 基类切换：VirtualControlStore

| 项目 | 重构前 | 重构后 |
|------|--------|--------|
| 基类 | `IStateStore`（直接实现接口） | `EventTriggerFramework.Core.InMemoryStateStore` |
| 复用方法 | 无 | `Set`/`Get`/`Has`/`Clear`/`GetAllKeys`/`RemoveByPrefix` |
| CLI 特有方法 | `SetSelectedItem`/`GetSelectedItem`/`GetAllKeysList` | 保留 |

**涉及文件：**
- `FileDriven/FileDriven.CLI/Services/VirtualControlStore.cs` — 基类切换
- `Share/EventTriggerFramework/Core/InMemoryStateStore.cs` — 通用基类（保持不变）

### ✅ 4. 删除重复代码

| 删除的类型 | 所在文件 | 替代 |
|-----------|---------|------|
| `InMemoryStateStore` | `FileDrivenFramework.Core/CommonUtilities.cs` | `EventTriggerFramework.Core.InMemoryStateStore` |
| `DefaultValueExtractor` | `FileDrivenFramework.Core/CommonUtilities.cs` | `EventTriggerFramework.Core.DefaultValueExtractor` |
| `EventTriggerResultImpl` | `FileDrivenFramework.Core/CommonUtilities.cs` | `EventTriggerFramework.Core.EventTriggerResult` |

### ✅ 5. EventBinding.cs 死代码清理 + 文件拆分

清理了 `Share/EventTriggerFramework/EventBinding.cs` 中未被外部引用的类型，并将保留的类型按职责拆分到独立文件：

| 拆分目标 | 提取的类型 | 说明 |
|---------|-----------|------|
| `Models/EventBindingInput.cs` | `EventBindingInput` | 输入模型 |
| `Models/EventBindingOutput.cs` | `EventBindingOutput` | 输出模型 |
| `Models/EventBindingInfo.cs` | `EventBindingInfo` | 绑定信息模型 |
| `Models/ControlValueResult.cs` | `ControlValueResult` | 控件值结果模型 |
| `Abstractions/IEventNameResolver.cs` | `IEventNameResolver` | 事件名称解析器接口 |
| `Abstractions/IEventBinder.cs` | `IEventBinder` | 事件绑定器接口 |
| `Abstractions/IControlValueAccessor.cs` | `IControlValueAccessor` | 控件值访问器接口 |
| `Abstractions/IEventBindingPipeline.cs` | `IEventBindingPipeline` | 事件绑定管道接口 |
| `Core/EventBindingExecutor.cs` | `EventBindingExecutor` | 事件绑定执行器 |
| `Core/ControlValueAccessorBase.cs` | `ControlValueAccessorBase<TTarget>` | 控件值访问器基类 |

原 `EventBinding.cs` 清空为占位文件（保留命名空间和注释），确保外部引用不中断。

### ✅ 6. EventPipeline.cs 拆分 + 简化

**拆分 IEventTypeResolver：**
将 `EventPipeline.cs` 中内嵌的 `IEventTypeResolver` 接口和 `DefaultEventTypeResolver` 实现提取到独立文件：
- `Abstractions/IEventTypeResolver.cs` — 接口 + 默认实现

**简化 ToEventBindingInput 冗余实现：**
`ToEventBindingInput(string)` 扩展方法不再重复实现解析逻辑，直接委托给 `ToEventBindingInput(MetaCmd)` 重载：

```csharp
// 重构前：重复实现 MetaCmd 解析逻辑
public static EventBindingInput ToEventBindingInput(this string eventName)
{
    var metaCmd = new MetaCmd(eventName);
    return new EventBindingInput(metaCmd);
}

// 重构后：直接委托
public static EventBindingInput ToEventBindingInput(this string eventName)
    => eventName.ToMetaCmd().ToEventBindingInput();
```

### ✅ 7. 测试文件修复

删除了 `EventBindingCoreTests.cs` 中引用已删除类型（`LoadedConfigInfo`、`ValueExtractionHelper`）的测试用例。

### ✅ 8. 构建验证

`EventTriggerFramework.csproj` 构建成功，**0 错误 0 警告**。所有依赖项目（FileDriven.CLI 等）引用正常，无编译错误。


---

## 三、重构不完全项（保持现状）

### 🔴 1. 两个 EventTriggerServiceBase 签名不同

| 维度 | FileDriven 旧版（已删除） | EventTriggerFramework 版 |
|------|--------------------------|------------------------|
| `ExecuteFunctionAsync` | `(string, IReadOnlyList<object?>)` | `(string, Dictionary<string, string?>)` |
| `WriteOutputAsync` | `(string, string)` | `(string, object?)` |
| `TriggerEventAsync` | `virtual` | 非 virtual |

**建议：** 保持现状。两个基类的抽象方法签名不同，属于不同的抽象层次。

### 🔴 2. 两个 EventBindingResolverBase 设计不同

| 维度 | FileDriven 旧版（已删除） | EventTriggerFramework 版 |
|------|--------------------------|------------------------|
| 查找策略 | 先找控件对象，再从对象提取 | 直接在配置中查找绑定信息 |
| 父控件递归 | 无 | 有 `FindBindingInParent` |

**建议：** 保持现状。FileDriven 版本更灵活（支持扩展方法如 `FindClickBinding`），EventTriggerFramework 版本更简洁。

### 🔴 3. 两个 VirtualControlStore 各有扩展

| 维度 | FileDriven.CLI 版 | EventTriggerFramework 版 |
|------|-------------------|--------------------------|
| 基类 | `InMemoryStateStore` | `InMemoryStateStore` |
| CLI 特有 | `SetSelectedItem`/`GetSelectedItem`/`GetAllKeysList` | 无 |
| 通用扩展 | 无 | `RemoveByPrefix` |

**建议：** 保持现状。FileDriven.CLI 版本继承 EventTriggerFramework 版本并添加 CLI 特有方法。

---

## 四、发现的潜在逻辑缺陷

### ⚠️ TriggerComboBoxEventAsync 的多函数执行逻辑缺陷

**位置：** `EventTriggerService.cs` 第 138-144 行

**问题：** 当有多个函数名（逗号分隔）时，只有第一个成功且 Value 为 null 的函数的结果会被记录。

**建议修复：**
```csharp
result.IsSuccess = true;
foreach (var fn in functionNames)
{
    var execResult = await ExecuteFunctionAsync(fn, new List<object?> { value });
    if (!execResult.IsSuccess)
    {
        result.IsSuccess = false;
        result.ErrorMessage = execResult.ErrorMessage;
        break;
    }
    result.Value ??= execResult.Value;
    result.ExecutionTime += execResult.ExecutionTime;
}
```

---

## 五、文件变更清单

| 文件 | 变更类型 | 说明 |
|------|---------|------|
| `Share/EventTriggerFramework/EventBinding.cs` | 修改 | 清理死代码，清空为占位文件 |
| `Share/EventTriggerFramework/Models/EventBindingInput.cs` | **新增** | 从 EventBinding.cs 拆分 |
| `Share/EventTriggerFramework/Models/EventBindingOutput.cs` | **新增** | 从 EventBinding.cs 拆分 |
| `Share/EventTriggerFramework/Models/EventBindingInfo.cs` | **新增** | 从 EventBinding.cs 拆分 |
| `Share/EventTriggerFramework/Models/ControlValueResult.cs` | **新增** | 从 EventBinding.cs 拆分 |
| `Share/EventTriggerFramework/Abstractions/IEventNameResolver.cs` | **新增** | 从 EventBinding.cs 拆分 |
| `Share/EventTriggerFramework/Abstractions/IEventBinder.cs` | **新增** | 从 EventBinding.cs 拆分 |
| `Share/EventTriggerFramework/Abstractions/IControlValueAccessor.cs` | **新增** | 从 EventBinding.cs 拆分 |
| `Share/EventTriggerFramework/Abstractions/IEventBindingPipeline.cs` | **新增** | 从 EventBinding.cs 拆分 |
| `Share/EventTriggerFramework/Abstractions/IEventTypeResolver.cs` | **新增** | 从 EventPipeline.cs 拆分 |
| `Share/EventTriggerFramework/Core/EventBindingExecutor.cs` | **新增** | 从 EventBinding.cs 拆分 |
| `Share/EventTriggerFramework/Core/ControlValueAccessorBase.cs` | **新增** | 从 EventBinding.cs 拆分 |
| `Share/EventTriggerFramework/EventPipeline.cs` | 修改 | 简化 ToEventBindingInput(string) 冗余实现 |
| `FileDriven/FileDrivenFramework.Core/CommonUtilities.cs` | 修改 | 删除 InMemoryStateStore、DefaultValueExtractor、EventTriggerResultImpl |
| `FileDriven/FileDriven.CLI/Services/EventTriggerService.cs` | 修改 | 基类切换 + 签名适配 |
| `FileDriven/FileDriven.CLI/Services/EventBindingResolver.cs` | 修改 | 基类切换 + 方法适配 |
| `FileDriven/FileDriven.CLI/Services/VirtualControlStore.cs` | 修改 | 基类切换为 InMemoryStateStore |
| `CentralConsole/Share.Tsets/EventTriggerFramework.Tests/EventBindingCoreTests.cs` | 修改 | 删除引用已删除类型的测试用例 |


---

## 六、架构图

```
┌──────────────────────────────────────────────────────────────────┐
│                    Share/EventTriggerFramework                     │
│  ┌───────────────────────────────────────────────────────────┐   │
│  │  Abstractions/                                             │   │
│  │    IEventBindingResolver  IEventTriggerService             │   │
│  │    IStateStore  IValueExtractor  IFunctionRegistry         │   │
│  │    ICommandExecutionEngine  IEnvironmentAdapter            │   │
│  │    IEventNameResolver  IEventBinder  IControlValueAccessor │   │
│  │    IEventBindingPipeline  IEventTypeResolver               │   │
│  └───────────────────────────────────────────────────────────┘   │
│  ┌───────────────────────────────────────────────────────────┐   │
│  │  Core/                                                     │   │
│  │    EventTriggerServiceBase  EventBindingResolverBase       │   │
│  │    InMemoryStateStore  DefaultValueExtractor               │   │
│  │    FunctionRegistry  CommandFileExecutor                   │   │
│  │    EventBindingExecutor  ControlValueAccessorBase          │   │
│  │    EventTriggerResult                                      │   │
│  └───────────────────────────────────────────────────────────┘   │
│  ┌───────────────────────────────────────────────────────────┐   │
│  │  Models/                                                   │   │
│  │    EventBindingInput  EventBindingOutput                   │   │
│  │    EventBindingInfo  ControlValueResult                    │   │
│  └───────────────────────────────────────────────────────────┘   │
│  ┌───────────────────────────────────────────────────────────┐   │
│  │  EventBinding.cs (占位)  EventBus.cs  EventPipeline.cs    │   │
│  │  EventPipelineRegistry  WinFormsEventBinding.cs           │   │
│  └───────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────┘
                              ▲
                              │ 继承
┌──────────────────────────────────────────────────────────────────┐
│                    FileDriven/FileDriven.CLI                      │
│  ┌───────────────────────────────────────────────────────────┐   │
│  │  Services/                                                 │   │
│  │    EventTriggerService  : EventTriggerServiceBase          │   │
│  │    EventBindingResolver : EventBindingResolverBase         │   │
│  │    VirtualControlStore  : InMemoryStateStore               │   │
│  │    ValueExtractionHelper                                   │   │
│  └───────────────────────────────────────────────────────────┘   │
│  ┌───────────────────────────────────────────────────────────┐   │
│  │  Adapters/                                                 │   │
│  │    FunctionExecutionFrameworkFunctionExecutor              │   │
│  │    BackendCommandAdapter                                   │   │
│  └───────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────┘
```


---

## 七、后续建议

| 优先级 | 事项 | 说明 |
|--------|------|------|
| P0 | 修复 `TriggerComboBoxEventAsync` 逻辑缺陷 | 多函数执行时，所有函数都必须成功 |
| P1 | 统一 `EventTriggerResult` | 将 `EventTriggerServiceBase` 中的私有 `EventTriggerResult` 改为引用 `EventTriggerFramework.Core.EventTriggerResult` |
| P2 | 文档化两个基类的行为差异 | 避免未来误切换 |
