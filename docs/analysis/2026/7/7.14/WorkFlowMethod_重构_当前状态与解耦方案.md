# WorkFlowMethod 重构 — 当前状态与解耦方案

> 编写日期：2026-07-14
> 目标：记录重构当前状态，分析重复定义问题，提出基于 IoC 的解耦方案

---

## 一、已完成的工作

### 1.1 目录创建

```
CsharpPrj/Operation/WorkFlowMethod/
├── EventBinding/          ← 新建
│   ├── EventBindingStageBase.cs
│   └── WinFormsValueAccessor.cs
└── Execution/             ← 新建
    └── WorkflowMethodExecutionWorkflow.cs
```

### 1.2 文件迁移

| 文件 | 源位置 | 目标位置 | 状态 |
|------|--------|----------|------|
| `WorkflowMethodExecutionWorkflow.cs` | `BackEndDriven/WorkFlowMethodBridge/` | `WorkFlowMethod/Execution/` | ✅ 已迁移，命名空间改为 `WorkFlowMethod.Execution` |
| `EventBindingStageBase.cs` | `Share/EventBindingOrchestrator/Invoke/Pipeline/` | `WorkFlowMethod/EventBinding/` | ✅ 已迁移，命名空间仍为 `Share.EventBindingOrchestrator.Stages` |
| `WinFormsValueAccessor.cs` | `Share/EventBindingOrchestrator/Invoke/` | `WorkFlowMethod/EventBinding/` | ✅ 已迁移，命名空间仍为 `Share.EventBindingOrchestrator.Adapters` |
| `DefaultParameterBuilder.cs` | `Share/EventBindingOrchestrator/Services/` | 已删除 | ✅ 已删除（标记 Obsolete） |

### 1.3 项目引用更新

`WorkFlowMethod.csproj` 新增引用：
- `CommandFramework.csproj` — 使用 `IParameterExecutionWorkflow` 接口
- `FrameworkAbstractions.csproj` — 使用 `IControlFinder` 接口
- `Threading.csproj` — 使用 `ReentryGuard`

### 1.4 旧源文件清理

- `BackEndDriven/WorkFlowMethodBridge/WorkflowMethodExecutionWorkflow.cs` — ✅ 已删除
- `Share/EventBindingOrchestrator/Invoke/Pipeline/EventBindingStageBase.cs` — ✅ 已删除
- `Share/EventBindingOrchestrator/Invoke/WinFormsValueAccessor.cs` — ✅ 已删除
- `Share/EventBindingOrchestrator/Services/DefaultParameterBuilder.cs` — ✅ 已删除

---

## 二、当前问题：重复定义

### 2.1 问题描述

`EventBindingOrchestrator.csproj` 通过 `<Compile Include>` 链接了 `WorkFlowMethod/EventBinding/` 下的两个文件：

```xml
<!-- EventBindingOrchestrator.csproj -->
<Compile Include="..\..\Operation\WorkFlowMethod\EventBinding\EventBindingStageBase.cs"
         Link="Stages\EventBindingStageBase.cs" />
<Compile Include="..\..\Operation\WorkFlowMethod\EventBinding\WinFormsValueAccessor.cs"
         Link="Adapters\WinFormsValueAccessor.cs" />
```

由于这些文件物理上在 `WorkFlowMethod/` 项目目录下，SDK 风格的 `.csproj` 默认会包含它们，导致：

| 类型 | 被谁编译 | 问题 |
|------|----------|------|
| `EventBindingStageBase` | `EventBindingOrchestrator` + `WorkFlowMethod` | ❌ 重复定义 |
| `WinFormsValueAccessor` | `EventBindingOrchestrator` + `WorkFlowMethod` | ❌ 重复定义 |

### 2.2 编译错误

上次构建报错：

```
error CS0246: 未能找到类型或命名空间名"IValueAccessor"
  → WinFormsValueAccessor.cs 在 WorkFlowMethod 项目中编译时，
    EventBindingOrchestrator 的 global using 不生效

error CS0246: 未能找到类型或命名空间名"WorkFlowMethod"
  → BackEndDrivenBuilder.cs 引用了 WorkFlowMethod.Execution，
    但 BackEndDriven 项目未引用 WorkFlowMethod
```

---

## 三、架构约束

### 3.1 依赖方向

```
WorkFlowMethod ──→ EventBindingOrchestrator  (允许引用)
EventBindingOrchestrator ──→ WorkFlowMethod  (禁止引用)
```

`EventBindingOrchestrator` 是 Share（共享）层，`WorkFlowMethod` 是 Operation（操作）层。依赖方向必须从 Operation 指向 Share，不能反向。

### 3.2 迁移规划回顾

根据 `WorkFlowMethod_重构规划.md`：

| 逻辑模块 | 目标归属 |
|----------|----------|
| 事件绑定与参数构建 | → `WorkFlowMethod` |
| 工作流执行引擎 | → `WorkFlowMethod` |
| 控件值读写 | → `WorkFlowMethod` |

### 3.3 矛盾点

`EventBindingStageBase` 被规划迁入 `WorkFlowMethod`，但 `ConfigBindingStage` 和 `DeclarationBindingStage` 继承自它且留在 `EventBindingOrchestrator`。

由于 `EventBindingOrchestrator` 禁止引用 `WorkFlowMethod`，派生类无法访问基类，因此**不能通过继承复用**，必须通过 IoC 解耦。

---

## 四、解耦方案

### 4.1 核心思路

将 `EventBindingStageBase` 的 protected 成员提取为接口，定义在 `EventBindingOrchestrator.Abstractions` 中，由 `WorkFlowMethod` 实现，通过构造函数注入到 `ConfigBindingStage` / `DeclarationBindingStage`。

### 4.2 需要解耦的成员

| 基类成员 | 访问方 | 解耦方式 |
|----------|--------|----------|
| `_normalizer` (IEventNameNormalizer) | ConfigBindingStage, DeclarationBindingStage | 已有接口，直接注入 |
| `_onEventResult` (Action) | ConfigBindingStage, DeclarationBindingStage | 改为构造函数参数注入 |
| `CreateEventHandler(...)` | ConfigBindingStage, DeclarationBindingStage | 提取为 `IEventHandlerFactory` 接口 |
| `TryBindEvent(...)` | ConfigBindingStage, DeclarationBindingStage | 已有 `IEventBinderStrategy`，或提取 `IEventBinder` |
| `RecordBoundControl(...)` | ConfigBindingStage, DeclarationBindingStage | 提取为 `IEventBindingRecorder` 接口 |
| `_executionEngine` | 仅在基类内部使用 | 基类内部保留 |
| `_parameterBuilder` / `_outputControlFinder` / `_outputWriter` | 仅在基类内部使用 | 基类内部保留 |

### 4.3 新增接口定义（在 `EventBindingOrchestrator.Abstractions` 中）

```csharp
// IEventHandlerFactory.cs
public interface IEventHandlerFactory
{
    EventHandler CreateEventHandler(
        object control, string eventName, string functionName,
        IReadOnlyList<ParameterBinding>? parameterBindings = null,
        string? output = null,
        Action<string, string, object?>? onResult = null,
        EventBindingContext? bindingContext = null);
}

// IEventBinder.cs（或复用已有的 IEventBinderStrategy）
public interface IEventBinder
{
    bool TryBindEvent(
        IEventBus eventBus, object control,
        string normalizedEventName, Delegate handler,
        ErrorCollector errors);
}

// IEventBindingRecorder.cs
public interface IEventBindingRecorder
{
    void RecordBoundControl(
        EventBindingContext input, object control,
        int boundEventCount, string diagnosticKey,
        EventConfig? eventsConfig = null,
        string? eventName = null,
        string? functionName = null);
}
```

### 4.4 ConfigBindingStage / DeclarationBindingStage 改造

```csharp
// 不再继承 EventBindingStageBase
public class ConfigBindingStage : IPipelineStage
{
    private readonly IEventHandlerFactory _handlerFactory;
    private readonly IEventBinder _eventBinder;
    private readonly IEventBindingRecorder _recorder;
    private readonly IEventNameNormalizer _normalizer;
    private readonly Action<string, string, object?>? _onEventResult;

    public ConfigBindingStage(
        IEventHandlerFactory handlerFactory,
        IEventBinder eventBinder,
        IEventBindingRecorder recorder,
        IEventNameNormalizer? normalizer = null,
        Action<string, string, object?>? onEventResult = null)
    {
        _handlerFactory = handlerFactory;
        _eventBinder = eventBinder;
        _recorder = recorder;
        _normalizer = normalizer ?? new DefaultEventNameNormalizer();
        _onEventResult = onEventResult;
    }
    // ...
}
```

### 4.5 EventBindingStageBase 改造（在 WorkFlowMethod 中）

```csharp
namespace WorkFlowMethod.EventBinding;

// 实现 EventBindingOrchestrator.Abstractions 中的接口
public class EventBindingStageBase :
    IEventHandlerFactory,
    IEventBinder,
    IEventBindingRecorder
{
    // 原有逻辑保持不变，仅实现接口方法
    public EventHandler CreateEventHandler(...) { ... }
    public bool TryBindEvent(...) { ... }
    public void RecordBoundControl(...) { ... }
}
```

### 4.6 WinFormsValueAccessor 处理

`WinFormsValueAccessor` 不涉及继承关系，是纯实现类。直接：
- 命名空间改为 `WorkFlowMethod.EventBinding`
- 移除 `EventBindingOrchestrator.csproj` 中的 `<Compile Include>` 链接
- `EventBindingOrchestrator` 通过 `IControlValueAccessor` 接口使用，不依赖具体实现

---

## 五、实施步骤

| 步骤 | 内容 | 涉及文件 |
|------|------|----------|
| 1 | 在 `EventBindingOrchestrator.Abstractions` 中定义 `IEventHandlerFactory`、`IEventBinder`、`IEventBindingRecorder` 接口 | 新建 3 个接口文件 |
| 2 | 改造 `ConfigBindingStage`：不再继承 `EventBindingStageBase`，改为构造函数注入接口 | `ConfigBindingStage.cs` |
| 3 | 改造 `DeclarationBindingStage`：同上 | `DeclarationBindingStage.cs` |
| 4 | 改造 `EventBindingStageBase`：实现上述接口，命名空间改为 `WorkFlowMethod.EventBinding` | `EventBindingStageBase.cs` |
| 5 | 改造 `WinFormsValueAccessor`：命名空间改为 `WorkFlowMethod.EventBinding` | `WinFormsValueAccessor.cs` |
| 6 | 移除 `EventBindingOrchestrator.csproj` 中的 `<Compile Include>` 链接 | `EventBindingOrchestrator.csproj` |
| 7 | `BackEndDriven.csproj` 添加对 `WorkFlowMethod` 的引用 | `BackEndDriven.csproj` |
| 8 | 构建验证 | 全解决方案构建 |

---

## 六、迁移后项目结构（目标）

```
CsharpPrj/Operation/WorkFlowMethod/
├── EventBinding/
│   ├── EventBindingStageBase.cs       ← 实现 IEventHandlerFactory / IEventBinder / IEventBindingRecorder
│   └── WinFormsValueAccessor.cs       ← 命名空间 WorkFlowMethod.EventBinding
├── Execution/
│   └── WorkflowMethodExecutionWorkflow.cs
├── Parameters/
├── Output/
└── Diagnostics/

CsharpPrj/Share/EventBindingOrchestrator/
├── Abstractions/
│   ├── IEventHandlerFactory.cs        ← 新增接口
│   ├── IEventBinder.cs                ← 新增接口
│   └── IEventBindingRecorder.cs       ← 新增接口
├── Stages/
│   ├── ConfigBindingStage.cs          ← 不再继承 EventBindingStageBase
│   └── DeclarationBindingStage.cs     ← 不再继承 EventBindingStageBase
└── ...
```

---

## 七、注意事项

1. **向后兼容**：`EventBindingStageOptions` 中的委托属性（`ParameterBuilder`、`OutputControlFinder`、`OutputWriter`）保留，由 `WorkFlowMethod` 在注册管线时注入
2. **测试**：`ConfigBindingStage` 和 `DeclarationBindingStage` 的单元测试需要更新构造函数签名
3. **DI 容器**：确保 IoC 容器正确注册新接口及其实现
