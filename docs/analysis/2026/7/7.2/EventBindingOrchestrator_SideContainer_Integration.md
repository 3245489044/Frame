# EventBindingOrchestrator ContainerBinder 集成方案

> **目标**：在 `EventBindingOrchestrator` 中构建能够覆盖 SideContainer 事件绑定完整路径的通用逻辑框架，使外部调用方只需调用一个函数接口即可完成"配置加载→控件渲染→控件注册→会话创建→事件绑定"全流程。
>
> **命名说明**：原设计文档使用 `SideContainer` 前缀，实际实现统一使用 `ContainerBinder` 前缀，以体现通用容器绑定语义。

---

## 1. 现状分析

### 1.1 当前 SideContainer 事件绑定调用链

```
MainForm.LoadSideContainerAsync(configPath)
  │
  ├─ 1. ConfigLoader.LoadSideContainerAsync(sideContainer, configPath, ...)
  │      └─ JsonConfigService.LoadConfigAsync(resolvedPath)  → 反序列化 JSON
  │      └─ sideContainer.LoadConfigAsync(config)             → 渲染控件到容器
  │
  ├─ 2. SideContainerBinder.RegisterSideContainerControls(config, sideContainer, ControlRegistry)
  │      └─ 收集所有控件名称 → ConfigContainer.FindControl → 注册到 ControlRegistry
  │
  ├─ 3. 执行 InitCommands（如果有）
  │
  └─ 4. SideContainerBinder.BindSideContainerEventsAsync(sideContainer, config, configPath)
         │
         ├─ 4a. pipeline.RenderSessionOnly(sideContainer, config)
         │       └─ 创建 WdUI 会话（幂等性检查）
         │
         └─ 4b. await pipeline.BindEventsAsync(sideContainer)
                │
                ├─ WinFormsRenderPipeline.BindEventsAsync(object target)  [重写]
                │   └─ GetActiveSession(control) → 获取会话
                │   └─ ExecuteBindEventsStage(control, session)
                │
                └─ ExecuteBindEventsStage(Control target, IRenderSession session)
                    ├─ _eventBinder == null? → 跳过
                    ├─ config = session.Config as UIConfiguration → null? → 跳过
                    ├─ form = FindForm(target)
                    ├─ _eventBinder is DefaultEventBindingService defaultBinder?
                    │   ├─ YES → defaultBinder.BindEvents(form, config, searchRoot: target)
                    │   └─ NO → await _eventBinder.BindEventsAsync(form, config)  [无 searchRoot]
                    │
                    └─ 日志: ExecuteBindEventsStage_done
```

**问题**：调用方（`MainForm`）需要了解并协调 4 个独立步骤，每个步骤都有多个失败点。

### 1.2 EventBindingOrchestrator 现有能力

| 组件 | 能力 | 状态 |
|------|------|------|
| `IEventBindingStep` | 步骤化执行接口 | ✅ 已有 |
| `EventBindingOrchestratorContext` | 编排上下文（含 Target/SearchRoot/CustomControlFinder） | ✅ 已有 |
| `EventBindingOrchestrator.ExecutePipelineAsync()` | 按步骤执行管线，异常隔离 | ✅ 已有 |
| `BuildEventsStep` | 从模板 parameters 生成 events 配置 | ✅ 已有 |
| `DiagnosticStep` | 诊断输出 | ✅ 已有 |
| `DelegateStep` | 将委托包装为步骤 | ✅ 已有 |
| `BindEventsWithDiagnosticAsync()` | 包装事件绑定，增加诊断日志 | ✅ 已有 |
| `IEventBindingService` | 事件绑定服务接口 | ✅ 已有 |
| `IEventBindingContext` | 事件绑定上下文接口 | ✅ 已有 |
| `IEventExecutionService` | 事件执行服务接口 | ✅ 已有 |
| `EventTypeMapperService` | 事件类型映射服务 | ✅ 已有 |
| **ContainerBinder 完整流程封装** | 加载→渲染→注册→绑定 一站式接口 | ✅ **已实现** |

---

## 2. 设计目标

### 2.1 核心原则

1. **一站式接口**：外部调用方只需调用一个函数，传入容器实例 + `configPath`，即可完成全流程
2. **步骤化可观测**：内部按步骤执行，每步有独立日志和异常隔离
3. **零 WinForms 依赖**：核心框架代码不依赖 `System.Windows.Forms`，使用弱类型 `object` 解耦
4. **可扩展**：调用方可注入自定义步骤（如 InitCommands 执行）
5. **幂等安全**：避免 `LoadConfigAsync` 内部已绑定事件导致的重复绑定

### 2.2 目标调用方式

```csharp
// 调用方只需一行
var options = new ContainerBinderOptions
{
    ControlRegistry = _controlRegistry,
    RenderPipeline = _renderPipeline,
};
var result = await EventBindingOrchestrator.Instance
    .LoadAndBindContainerAsync(container, configPath, options);
```

---

## 3. 架构设计

### 3.1 新增组件

在 `EventBindingOrchestrator` 中新增以下组件：

```
Share.EventBindingOrchestrator/
├── Abstractions/
│   ├── IEventBindingService.cs          [已有]
│   ├── IEventBindingContext.cs          [已有]
│   ├── IEventExecutionService.cs        [已有]
│   └── IEventNameResolver.cs            [已有]
├── Services/
│   ├── EventTypeMapperService.cs        [已有]
│   └── EventBindingResolver.cs          [已有]
├── Models/
│   ├── BindingResult.cs                 [已有]
│   └── EventBindingInfo.cs              [已有]
├── EventBus/                            [已有]
├── Pipeline/
│   └── ContainerBinderPipeline.cs       [★ 已实现] 容器绑定管线（6 个步骤）
├── ContainerBinder/
│   ├── IContainerBinderContext.cs       [★ 已实现] 容器绑定上下文接口
│   └── ContainerBinderContext.cs        [★ 已实现] 容器绑定上下文实现
└── EventBindingOrchestrator.cs          [★ 已增强] 新增 LoadAndBindContainerAsync + ContainerBinderOptions
```

### 3.2 核心接口

```csharp
// ============================================================
// IContainerBinderContext.cs — 容器绑定上下文
// ============================================================
public interface IContainerBinderContext
{
    /// <summary>容器实例（WinForms 场景下为 ConfigContainer）</summary>
    object Container { get; }

    /// <summary>配置文件路径</summary>
    string ConfigPath { get; }

    /// <summary>加载后的 UIConfiguration</summary>
    UIConfiguration? Config { get; set; }

    /// <summary>控件注册表（用于注册容器中的控件，应为 IControlRegistry 类型）</summary>
    object? ControlRegistry { get; set; }

    /// <summary>渲染管线（用于创建会话和绑定事件，应为 IRenderPipeline 类型）</summary>
    object? RenderPipeline { get; set; }

    /// <summary>事件绑定服务（应为 IEventBindingService 类型）</summary>
    object? EventBindingService { get; set; }

    /// <summary>自定义数据存储（步骤间共享数据）</summary>
    Dictionary<string, object?> Items { get; }
}
```

### 3.3 步骤定义

容器绑定流程拆解为以下步骤：

| 步骤 | 名称 | 职责 | 可跳过 |
|------|------|------|--------|
| 1 | `ConfigLoad` | 加载 JSON 配置并反序列化为 UIConfiguration | ❌ |
| 2 | `ControlRender` | 将配置渲染到容器 | ❌ |
| 3 | `ControlRegister` | 收集控件名称并注册到 ControlRegistry | ✅ |
| 4 | `InitCommands` | 执行配置中的初始化命令 | ✅ |
| 5 | `SessionCreate` | 创建渲染会话 | ❌ |
| 6 | `ContainerEventBinding` | 执行事件绑定 | ❌ |
| 7 | `Diagnostic` | 输出绑定结果诊断 | ✅ |

### 3.4 步骤实现（实际代码）

所有步骤实现位于 `Pipeline/ContainerBinderPipeline.cs`，使用**弱类型 + 反射**方式调用外部组件，保持 Share 层零依赖。

```csharp
// ============================================================
// ContainerBinderPipeline.cs — 容器绑定管线
// ============================================================

/// <summary>
/// 步骤 1：配置加载 — 从 JSON 文件加载并反序列化为 UIConfiguration
/// 通过反射调用 ConfigLoader.LoadConfigAsync 完成加载。
/// </summary>
public class ConfigLoadStep : IEventBindingStep
{
    public string Name => "ConfigLoad";

    public async Task<bool> ExecuteAsync(EventBindingOrchestratorContext context)
    {
        var configPath = context.Items.GetValueOrDefault("ConfigPath") as string;
        var container = context.Items.GetValueOrDefault("Container");

        if (string.IsNullOrEmpty(configPath) || container == null)
            return false;

        // 通过反射调用 ConfigLoader.LoadConfigAsync(container, configPath)
        await InvokeConfigLoaderAsync(container, configPath);
        return true;
    }
}

/// <summary>
/// 步骤 2：控件渲染 — 将 UIConfiguration 渲染到容器
/// 通过反射调用容器的 LoadConfigAsync 方法。
/// </summary>
public class ControlRenderStep : IEventBindingStep
{
    public string Name => "ControlRender";

    public async Task<bool> ExecuteAsync(EventBindingOrchestratorContext context)
    {
        var container = context.Items.GetValueOrDefault("Container");
        var config = context.Config;

        if (container == null || config == null)
            return false;

        // 通过反射调用 container.LoadConfigAsync(config)
        await InvokeLoadConfigAsync(container, config);
        return true;
    }
}

/// <summary>
/// 步骤 3：控件注册 — 从 UIConfiguration 提取控件名称并注册到 ControlRegistry
/// 遍历 ControlConfig 树，通过容器的 FindControl 方法查找控件实例。
/// </summary>
public class ControlRegisterStep : IEventBindingStep
{
    public string Name => "ControlRegister";

    public Task<bool> ExecuteAsync(EventBindingOrchestratorContext context)
    {
        var container = context.Items.GetValueOrDefault("Container");
        var config = context.Config;
        var registry = context.Items.GetValueOrDefault("ControlRegistry");

        if (container == null || config == null || registry == null)
            return Task.FromResult(false);

        // 遍历 UIConfiguration.Views 中的 ControlConfig 树
        RegisterControlTree(config.Views, container, registry);
        return Task.FromResult(true);
    }
}

/// <summary>
/// 步骤 4：初始化命令执行 — 执行 UIConfiguration.InitCommands 列表
/// </summary>
public class InitCommandsStep : IEventBindingStep
{
    public string Name => "InitCommands";

    public async Task<bool> ExecuteAsync(EventBindingOrchestratorContext context)
    {
        var config = context.Config;
        if (config?.InitCommands == null || config.InitCommands.Count == 0)
            return true; // 无初始化命令，视为成功

        foreach (var cmd in config.InitCommands)
        {
            await ExecuteCommandAsync(cmd);
        }
        return true;
    }
}

/// <summary>
/// 步骤 5：会话创建 — 创建渲染会话
/// 调用 IRenderPipeline.RenderSessionOnly 创建轻量级渲染会话。
/// </summary>
public class SessionCreateStep : IEventBindingStep
{
    public string Name => "SessionCreate";

    public Task<bool> ExecuteAsync(EventBindingOrchestratorContext context)
    {
        var container = context.Items.GetValueOrDefault("Container");
        var config = context.Config;
        var pipeline = context.Items.GetValueOrDefault("RenderPipeline");

        if (container == null || config == null || pipeline == null)
            return Task.FromResult(false);

        // 通过反射调用 pipeline.RenderSessionOnly(container, config)
        InvokeRenderSessionOnly(pipeline, container, config);
        return Task.FromResult(true);
    }
}

/// <summary>
/// 步骤 6：容器事件绑定 — 执行事件绑定
/// 调用 IRenderPipeline.BindEventsAsync 执行事件绑定。
/// </summary>
public class ContainerEventBindingStep : IEventBindingStep
{
    public string Name => "ContainerEventBinding";

    public async Task<bool> ExecuteAsync(EventBindingOrchestratorContext context)
    {
        var container = context.Items.GetValueOrDefault("Container");
        var config = context.Config;
        var pipeline = context.Items.GetValueOrDefault("RenderPipeline");

        if (container == null || config == null || pipeline == null)
            return false;

        // 通过反射调用 pipeline.BindEventsAsync(container)
        await InvokeBindEventsAsync(pipeline, container);

        // 收集绑定结果
        context.Result = new BindingResult();
        return true;
    }
}
```

### 3.5 EventBindingOrchestrator 增强（实际代码）

```csharp
// 在 EventBindingOrchestrator.cs 中新增

/// <summary>
/// 容器绑定选项
/// </summary>
public class ContainerBinderOptions
{
    /// <summary>控件注册表（应为 IControlRegistry 类型）</summary>
    public object? ControlRegistry { get; set; }

    /// <summary>渲染管线（应为 IRenderPipeline 类型）</summary>
    public object? RenderPipeline { get; set; }

    /// <summary>事件绑定服务（应为 IEventBindingService 类型）</summary>
    public object? EventBindingService { get; set; }

    /// <summary>是否跳过 InitCommands 执行</summary>
    public bool SkipInitCommands { get; set; }

    /// <summary>自定义步骤（在诊断之前插入）</summary>
    public List<IEventBindingStep>? CustomSteps { get; set; }
}

/// <summary>
/// EventBindingOrchestrator 扩展 — 容器绑定一站式入口
/// 封装"配置加载→控件渲染→控件注册→初始化命令→会话创建→事件绑定→诊断"完整流程。
/// </summary>
public static class EventBindingOrchestratorContainerExtensions
{
    public static async Task<BindingResult> LoadAndBindContainerAsync(
        this EventBindingOrchestrator orchestrator,
        object container,
        string configPath,
        ContainerBinderOptions? options = null)
    {
        // 1. 创建编排上下文
        var context = new EventBindingOrchestratorContext
        {
            Target = container,
            Items =
            {
                ["Container"] = container,
                ["ConfigPath"] = configPath,
                ["ControlRegistry"] = options?.ControlRegistry,
                ["RenderPipeline"] = options?.RenderPipeline,
                ["EventBindingService"] = options?.EventBindingService,
            }
        };

        // 2. 构建步骤管线
        var steps = new List<IEventBindingStep>();

        // 核心步骤（不可跳过）
        steps.Add(new Pipeline.ConfigLoadStep());
        steps.Add(new Pipeline.ControlRenderStep());

        // 可选步骤：控件注册（需要 ControlRegistry）
        if (options?.ControlRegistry != null)
            steps.Add(new Pipeline.ControlRegisterStep());

        // 可选步骤：初始化命令
        if (options?.SkipInitCommands != true)
            steps.Add(new Pipeline.InitCommandsStep());

        // 会话创建和事件绑定
        steps.Add(new Pipeline.SessionCreateStep());
        steps.Add(new Pipeline.ContainerEventBindingStep());

        // 诊断步骤
        steps.Add(new DiagnosticStep());

        // 3. 调用方自定义步骤（在诊断之前插入）
        if (options?.CustomSteps != null)
        {
            var insertIndex = steps.Count - 1;
            steps.InsertRange(insertIndex, options.CustomSteps);
        }

        // 4. 执行管线
        var allSuccess = await orchestrator.ExecutePipelineAsync(context, steps);

        // 5. 返回结果
        return context.Result ?? new BindingResult
        {
            BoundCount = 0,
            SkippedCount = 1,
            SkippedControls = { new ControlBindingInfo
            {
                ControlName = container.GetType().Name,
                IsBound = false,
                SkipReason = allSuccess ? "No result collected" : "Pipeline execution failed"
            }}
        };
    }
}
```

---

## 4. 调用方集成

### 4.1 MainForm 中的调用

```csharp
// 当前（分散调用）
await ConfigLoader.LoadSideContainerAsync(_sideContainer, configPath);
SideContainerBinder.RegisterSideContainerControls(config, _sideContainer, _controlRegistry);
// ... InitCommands ...
await SideContainerBinder.BindSideContainerEventsAsync(_sideContainer, config, configPath);

// 目标（一站式调用）
var options = new ContainerBinderOptions
{
    ControlRegistry = _controlRegistry,
    RenderPipeline = _renderPipeline,
    EventBindingService = _eventBindingService,
};
var result = await EventBindingOrchestrator.Instance
    .LoadAndBindContainerAsync(_sideContainer, configPath, options);

if (!result.IsAllBound)
{
    // 处理绑定失败
    Log.Warn($"容器事件绑定部分失败: {result.GetDiagnosticSummary()}");
}
```

### 4.2 依赖注入

`EventBindingOrchestrator` 作为单例，其依赖通过 `ContainerBinderOptions` 传入，而非构造函数注入。这样设计的原因：

1. **保持单例模式**：`EventBindingOrchestrator.Instance` 是全局单例，不依赖 DI 容器
2. **调用方灵活**：不同调用方可以传入不同的 `RenderPipeline`/`ControlRegistry`
3. **测试友好**：单元测试时可以 mock 各个依赖

---

## 5. 与现有架构的关系

### 5.1 依赖关系

```
EventBindingOrchestrator (Share 层)
  │
  ├─ 依赖：BasicMethod, Pipeline, Parsing, PlaceholderFramework, 
  │        CommandFramework, ConfigurationModels, UniversalMiddleware
  │
  ├─ 不依赖：WinForms, WdUI, EventTriggerFramework, FrontEndDriven
  │
  └─ 调用方注入：RenderPipeline (WdUI), ControlRegistry (FrontEndDriven),
                 EventBindingService (EventTriggerFramework)
```

### 5.2 职责边界

| 职责 | 归属 |
|------|------|
| 步骤编排（顺序、异常隔离、日志） | `EventBindingOrchestrator` |
| 配置加载（JSON → UIConfiguration） | `ConfigLoadStep`（内部通过反射委托给 `ConfigLoader`） |
| 控件渲染（UIConfiguration → WinForms 控件） | `ControlRenderStep`（内部通过反射委托给容器的 `LoadConfigAsync`） |
| 控件注册（收集控件名称） | `ControlRegisterStep`（内部通过反射遍历 ControlConfig 树） |
| 会话创建（WdUI 会话管理） | `SessionCreateStep`（内部通过反射委托给 `IRenderPipeline.RenderSessionOnly`） |
| 事件绑定（WinForms 事件绑定） | `ContainerEventBindingStep`（内部通过反射委托给 `IRenderPipeline.BindEventsAsync`） |
| 诊断输出 | `DiagnosticStep`（内置） |

---

## 6. 实施记录

### 6.1 文件清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `EventBindingOrchestrator.cs` | ✅ 已增强 | 新增 `ContainerBinderOptions` 和 `EventBindingOrchestratorContainerExtensions` |
| `Pipeline/ContainerBinderPipeline.cs` | ✅ 已新建 | 6 个步骤实现类（ConfigLoadStep、ControlRenderStep、ControlRegisterStep、InitCommandsStep、SessionCreateStep、ContainerEventBindingStep） |
| `ContainerBinder/IContainerBinderContext.cs` | ✅ 已新建 | 容器绑定上下文接口 |
| `ContainerBinder/ContainerBinderContext.cs` | ✅ 已新建 | 容器绑定上下文实现 |
| `MainForm.cs` | ⏳ 待修改 | 替换原有 4 步调用为 `LoadAndBindContainerAsync` |

### 6.2 实施步骤

1. ✅ **新建 `Pipeline/ContainerBinderPipeline.cs`** — 6 个步骤实现（2026-07-01）
2. ✅ **增强 `EventBindingOrchestrator.cs`** — 新增 `ContainerBinderOptions` + 扩展方法（2026-07-01）
3. ✅ **编译验证** — 0 警告 0 错误（2026-07-01）
4. ⏳ **修改 `MainForm.cs`** — 替换调用
5. ⏳ **测试验证** — 运行 SideContainer 场景，确认事件正确触发

---

## 7. 风险与注意事项

### 7.1 幂等性风险

`LoadConfigAsync(config)` 内部可能已通过 `_orchestrator.RenderFullAsync` 完成了事件绑定。`SessionCreateStep` 中的 `RenderSessionOnly` 有幂等性检查（已有活跃会话且 config 相同则跳过），但 `ControlRenderStep` 中的 `LoadConfigAsync` 可能已经触发了事件绑定。

**解决方案**：在 `ControlRenderStep` 中，如果 `LoadConfigAsync` 内部已经绑定了事件，则后续的 `SessionCreateStep` 和 `ContainerEventBindingStep` 应该跳过。可以通过检查容器的状态来判断是否已绑定。

### 7.2 线程安全

`EventBindingOrchestrator` 是单例，`LoadAndBindContainerAsync` 可能被多个线程同时调用。需要确保：
- `EventBindingOrchestratorContext` 是每次调用新建的，不共享
- `ContainerBinderOptions` 是每次调用传入的，不共享

### 7.3 向后兼容

现有调用方式（`ConfigLoader.LoadSideContainerAsync` + `SideContainerBinder.BindSideContainerEventsAsync`）保持不变，新增的 `LoadAndBindContainerAsync` 是可选的一站式入口。

### 7.4 类型安全

由于使用弱类型 `object` 传递依赖，运行时可能抛出类型转换异常。调用方需确保传入正确的实例类型。建议在 `ContainerBinderOptions` 的 setter 中添加类型检查日志。
