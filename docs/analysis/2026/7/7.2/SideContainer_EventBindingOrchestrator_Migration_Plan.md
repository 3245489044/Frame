# SideContainer 事件绑定 — 旧路径到 EventBindingOrchestrator 迁移方案

> **版本**: 2026-07-01  
> **目标**: 将 `MainForm.LoadSideContainerAsync` 的 5 个步骤逐步迁移到 `EventBindingOrchestrator` 管线，使其成为 SideContainer 事件绑定的唯一入口

---

## 1. 现状分析

### 1.1 当前调用链

```
MainForm.LoadSideContainerAsync(configPath)
  │
  ├─ 1. ConfigLoader.LoadSideContainerAsync(sideContainer, configPath, lock, log)
  │      └─ JsonConfigService.LoadConfigAsync(resolvedPath)  → 反序列化 JSON
  │      └─ sideContainer.LoadConfigAsync(config)             → 渲染控件到容器
  │      📁 FrontEndDriven.Loading (接口) → FileDriven (实现)
  │
  ├─ 2. ConfigRegistrar.RegisterLoadedConfig(config)
  │      └─ 注册到 PipelineEntry._loadedConfigs，使 EventBindingResolver 能查找
  │      📁 FrontEndDriven/WinForms/EventBinding/ConfigRegistrar.cs
  │
  ├─ 3. SideContainerBinder.RegisterSideContainerControls(config, sideContainer, ControlRegistry)
  │      └─ 收集所有控件名称 → ConfigContainer.FindControl → 注册到 ControlRegistry
  │      📁 FrontEndDriven/WinForms/EventBinding/SideContainerBinder.cs
  │
  ├─ 4. 执行 InitCommands（如果有）
  │      └─ CommandExecutor(cmd) 循环
  │      └─ 再次 RegisterSideContainerControls（InitCommands 后控件可能变化）
  │      📁 MainForm 中直接实现
  │
  └─ 5. SideContainerBinder.BindSideContainerEventsAsync(sideContainer, config, configPath)
         │
         ├─ 5a. pipeline.RenderSessionOnly(sideContainer, config)
         │       └─ WinFormsRenderPipeline.RenderSessionOnly
         │           └─ 创建 WdUI 会话（_sessions[target] = session，幂等性检查）
         │
         └─ 5b. await pipeline.BindEventsAsync(sideContainer)
                └─ WinFormsRenderPipeline.BindEventsAsync (override)
                    └─ ExecuteBindEventsStage
                        └─ DefaultEventBindingService.BindEvents(form, config, searchRoot: sideContainer)
                            └─ EventBindingPipelineRunner.Execute (Transformers 管线)
                                └─ 遍历 UIConfiguration.Views.Controls
                                └─ 将 JSON 事件定义绑定到 WinForms 控件原生事件
```

### 1.2 EventBindingOrchestrator 当前管线

```
EventBindingOrchestrator.LoadAndBindContainerAsync(config, options)
  │
  └─ ContainerBinderPipeline.ExecuteAsync(context)
       │
       ├─ 阶段1: InvokeConfigLoaderAsync
       │      └─ ControlTreeWalker.WalkAllViews(config, ...)
       │      └─ _configLoader.LoadAsync(config, registry)
       │
       ├─ 阶段2: BindEventsAsync
       │      └─ ControlTreeWalker.WalkAllViews(config, ...)
       │      └─ _eventBindingService.BindEventsAsync(config, options)
       │
       └─ 阶段3: ExecuteCommandsAsync
              └─ ControlTreeWalker.WalkAllViews(config, ...)
              └─ _commandExecutor.ExecuteAsync(commandName)
```

### 1.3 关键差异

| 维度 | 旧路径 (MainForm) | 新路径 (EventBindingOrchestrator) |
|------|------------------|----------------------------------|
| **入口** | `MainForm.LoadSideContainerAsync` | `EventBindingOrchestrator.LoadAndBindContainerAsync` |
| **配置加载** | 文件→模型→渲染控件 | 模型→注册到 ComponentRegistry |
| **控件注册** | `IControlRegistry` (WinForms 层) | `IComponentRegistry` (Share 层) |
| **事件绑定** | WinFormsRenderPipeline (WdUI 渲染管线) | `IEventBindingService.BindEventsAsync` (抽象接口) |
| **命令执行** | MainForm 循环 InitCommands | 管线阶段3 遍历控件 Items |
| **渲染会话** | `pipeline.RenderSessionOnly` | 缺失 |
| **配置注册** | `ConfigRegistrar.RegisterLoadedConfig` | 缺失 |

---

## 2. 迁移策略

### 2.1 总体原则

1. **增量迁移** — 每次迁移一个步骤，保持旧路径和新路径并行运行
2. **兼容性优先** — 迁移过程中不破坏现有功能
3. **先抽象后替换** — 先定义接口抽象，再替换具体实现
4. **错误隔离** — 每个步骤的异常不影响其他步骤

### 2.2 迁移路线图

```
阶段1 (P0) ─── 接口契约对齐 ─── 使 EventBindingOrchestrator 可编译、可运行
    │
阶段2 (P1) ─── 步骤5 迁移 ─── 将渲染管线事件绑定纳入 Orchestrator
    │
阶段3 (P2) ─── 步骤3+4 迁移 ─── 将控件注册和命令执行纳入 Orchestrator
    │
阶段4 (P3) ─── 步骤1+2 迁移 ─── 将配置加载和注册纳入 Orchestrator
    │
阶段5 (P4) ─── 旧路径清理 ─── 删除重复代码，统一入口
```

---

## 3. 阶段1 (P0)：接口契约对齐

### 3.1 问题清单

| # | 问题 | 文件 | 当前状态 | 目标 |
|---|------|------|---------|------|
| 1 | `EventBindingOrchestratorContext` 类缺失 | `ContainerBinderContext.cs:19` | 不存在 | 定义或替换为 `Dictionary<string, object?>` |
| 2 | `IConfigLoader.LoadAsync(UIConfiguration, IComponentRegistry)` 签名不存在 | `ContainerBinderPipeline.cs:120` | 接口无此签名 | 统一接口签名 |
| 3 | `IEventBindingService.BindEventsAsync(UIConfiguration, object?)` 签名不存在 | `ContainerBinderPipeline.cs:149` | 接口无此签名 | 统一接口签名 |
| 4 | `ICommandExecutor.Execute` 同步调用 | `ContainerBinderPipeline.cs:185` | 使用 `Execute` 而非 `ExecuteAsync` | 改为 `ExecuteAsync` |

### 3.2 修复方案

#### 3.2.1 EventBindingOrchestratorContext

**方案**: 将 `ContainerBinderContext` 的 `EventBindingOrchestratorContext` 依赖替换为 `Dictionary<string, object?>`

```csharp
// 修改前
public class ContainerBinderContext : IContainerBinderContext
{
    private readonly EventBindingOrchestratorContext _innerContext;  // 不存在

    public ContainerBinderContext(object container, string configPath, EventBindingOrchestratorContext innerContext)
    {
        ...
    }
}

// 修改后
public class ContainerBinderContext : IContainerBinderContext
{
    private readonly Dictionary<string, object?> _items = new();

    public ContainerBinderContext(object container, string configPath)
    {
        Container = container;
        ConfigPath = configPath;
    }

    public object? ControlRegistry
    {
        get => _items.GetValueOrDefault("ControlRegistry");
        set => _items["ControlRegistry"] = value;
    }
    // ... 其他属性同理
}
```

#### 3.2.2 IConfigLoader.LoadAsync 签名

```csharp
// 当前接口定义 (EventBindingOrchestrator 层)
public interface IConfigLoader
{
    Task LoadAsync(UIConfiguration config, IComponentRegistry registry);
}

// 管线调用方式 (ContainerBinderPipeline.cs:120)
await _configLoader.LoadAsync(config, registry);
// ✅ 签名已匹配，无需修改
```

#### 3.2.3 IEventBindingService.BindEventsAsync 签名

```csharp
// 当前接口定义 (EventBindingOrchestrator 层)
public interface IEventBindingService
{
    Task<BindingResult> BindEventsAsync(UIConfiguration config, object? options = null);
    // ...
}

// 管线调用方式 (ContainerBinderPipeline.cs:149)
await _eventBindingService.BindEventsAsync(config, context.Options);
// ✅ 签名已匹配，无需修改
```

#### 3.2.4 ICommandExecutor 同步→异步

```csharp
// 当前接口定义
public interface ICommandExecutor
{
    Task ExecuteAsync(string commandName);
}

// 管线调用方式 (ContainerBinderPipeline.cs:185)
await _commandExecutor.ExecuteAsync(commandName);
// ✅ 已使用 ExecuteAsync，无需修改
```

> **结论**: 经过代码审查，P0 的 4 个问题中：
> - `EventBindingOrchestratorContext` 确实不存在 → **需要修复**
> - 其余 3 个接口签名已匹配 → **无需修改**

### 3.3 修复清单

| 文件 | 修改内容 | 影响范围 |
|------|---------|---------|
| `ContainerBinder/ContainerBinderContext.cs` | 移除 `EventBindingOrchestratorContext` 依赖，改用 `Dictionary<string, object?>` | 仅此文件 |
| `ContainerBinder/IContainerBinderContext.cs` | 移除 `Items` 属性中的 `EventBindingOrchestratorContext` 引用 | 仅此文件 |

---

## 4. 阶段2 (P1)：步骤5 迁移 — 渲染管线事件绑定

### 4.1 目标

将 `SideContainerBinder.BindSideContainerEventsAsync` 的 3 参数重载（渲染管线事件绑定）纳入 `EventBindingOrchestrator` 管线。

### 4.2 当前状态

```csharp
// SideContainerBinder.cs — 旧路径
public static async Task BindSideContainerEventsAsync(
    ConfigContainer sideContainer,
    UIConfiguration config,
    string configPath)
{
    var pipeline = WdUIUsage.RenderPipeline;
    // 1. 创建轻量级渲染会话
    pipeline.RenderSessionOnly(sideContainer, config);
    // 2. 绑定事件
    await pipeline.BindEventsAsync(sideContainer);
}
```

### 4.3 迁移方案

#### 4.3.1 新增 IRenderPipeline 抽象

在 `EventBindingOrchestrator.Abstractions` 中新增接口：

```csharp
// Share/EventBindingOrchestrator/Abstractions/IRenderPipeline.cs
public interface IRenderPipeline
{
    /// <summary>创建轻量级渲染会话（使 GetActiveSession 能找到）</summary>
    void RenderSessionOnly(object container, UIConfiguration config);

    /// <summary>绑定事件</summary>
    Task BindEventsAsync(object container);
}
```

#### 4.3.2 修改 ContainerBinderPipeline

在管线中新增阶段 0（渲染会话创建）和阶段 2 增强（使用 IRenderPipeline）：

```csharp
// ContainerBinderPipeline.cs — 新增依赖
private readonly IRenderPipeline? _renderPipeline;

// 管线执行流程
public async Task ExecuteAsync(IContainerBinderContext context)
{
    // 阶段 0: 渲染会话（可选）
    if (_renderPipeline != null)
    {
        await InvokeRenderSessionAsync(context);
    }

    // 阶段 1: 配置加载
    await InvokeConfigLoaderAsync(context);

    // 阶段 2: 事件绑定（优先使用 IRenderPipeline，回退到 IEventBindingService）
    if (_renderPipeline != null)
    {
        await _renderPipeline.BindEventsAsync(context.Container);
    }
    else
    {
        await BindEventsAsync(context);
    }

    // 阶段 3: 命令执行
    await ExecuteCommandsAsync(context);
}
```

#### 4.3.3 WinFormsRenderPipelineAdapter

在 Operation 层实现 `IRenderPipeline`，适配 `WinFormsRenderPipeline`：

```csharp
// Operation/FrontEndDriven/WinForms/EventBinding/WinFormsRenderPipelineAdapter.cs
public class WinFormsRenderPipelineAdapter : IRenderPipeline
{
    private readonly WinFormsRenderPipeline _pipeline;

    public void RenderSessionOnly(object container, UIConfiguration config)
    {
        _pipeline.RenderSessionOnly((ConfigContainer)container, config);
    }

    public async Task BindEventsAsync(object container)
    {
        await _pipeline.BindEventsAsync((ConfigContainer)container);
    }
}
```

### 4.4 迁移后的调用链

```csharp
// MainForm.cs — 迁移后
public async Task LoadSideContainerAsync(string configPath)
{
    // ... 步骤1-4 保持不变 ...

    // 步骤5: 使用 EventBindingOrchestrator
    var orchestrator = ServiceLocator.Resolve<EventBindingOrchestrator>();
    var context = new ContainerBinderContext(sideContainer, configPath)
    {
        Config = config,
        ControlRegistry = controlRegistry,
        RenderPipeline = WdUIUsage.RenderPipeline,
        Services = serviceProvider
    };
    await orchestrator.LoadAndBindContainerAsync(config, context);
}
```

### 4.5 验证标准

- [ ] `IRenderPipeline` 接口定义完成
- [ ] `WinFormsRenderPipelineAdapter` 实现完成
- [ ] `ContainerBinderPipeline` 支持可选的渲染管线阶段
- [ ] 旧路径 3 参数 `BindSideContainerEventsAsync` 仍可正常工作
- [ ] 新路径通过 `EventBindingOrchestrator` 调用后，事件绑定结果一致

---

## 5. 阶段3 (P2)：步骤3+4 迁移 — 控件注册和命令执行

### 5.1 目标

将 `RegisterSideContainerControls` 和 InitCommands 执行纳入 `EventBindingOrchestrator` 管线。

### 5.2 当前状态

```csharp
// SideContainerBinder.cs — 控件注册
public static void RegisterSideContainerControls(
    UIConfiguration config,
    ConfigContainer sideContainer,
    IControlRegistry controlRegistry)
{
    // 收集控件名称 → ConfigContainer.FindControl → 注册到 ControlRegistry
}

// MainForm.cs — InitCommands 执行
foreach (var cmd in config.InitCommands)
{
    await CommandExecutor(cmd);
}
// 再次注册（InitCommands 后控件可能变化）
SideContainerBinder.RegisterSideContainerControls(config, sideContainer, controlRegistry);
```

### 5.3 迁移方案

#### 5.3.1 将控件注册纳入管线阶段1

修改 `IConfigLoader.LoadAsync` 的实现，使其同时完成控件注册：

```csharp
// 实现类示例
public class SideContainerConfigLoader : IConfigLoader
{
    public async Task LoadAsync(UIConfiguration config, IComponentRegistry registry)
    {
        // 1. 注册配置到 ComponentRegistry
        registry.Register(config);

        // 2. 同时注册控件到 ControlRegistry（通过上下文获取）
        var controlRegistry = _context.ControlRegistry as IControlRegistry;
        if (controlRegistry != null)
        {
            RegisterControls(config, controlRegistry);
        }
    }
}
```

#### 5.3.2 将 InitCommands 纳入管线阶段3

修改 `ContainerBinderPipeline.ExecuteCommandsAsync`，使其同时处理 InitCommands：

```csharp
private async Task ExecuteCommandsAsync(IContainerBinderContext context)
{
    var config = context.Config;
    if (config == null) return;

    // 1. 执行 InitCommands（来自配置顶层）
    if (config.InitCommands is { Count: > 0 })
    {
        foreach (var cmd in config.InitCommands)
        {
            await _commandExecutor.ExecuteAsync(cmd);
        }
    }

    // 2. 执行控件 Items 中的命令（原有逻辑）
    ControlTreeWalker.WalkAllViews(config, async ctrl =>
    {
        var commandName = TryGetParameterFromItems(ctrl, "command");
        if (!string.IsNullOrEmpty(commandName))
        {
            await _commandExecutor.ExecuteAsync(commandName);
        }
    });
}
```

### 5.4 迁移后的调用链

```csharp
// MainForm.cs — 迁移后
public async Task LoadSideContainerAsync(string configPath)
{
    // 步骤1: 配置加载（保持不变）
    var config = await ConfigLoader.LoadSideContainerAsync(...);

    // 步骤2-5: 全部由 EventBindingOrchestrator 完成
    var orchestrator = ServiceLocator.Resolve<EventBindingOrchestrator>();
    var context = new ContainerBinderContext(sideContainer, configPath)
    {
        Config = config,
        ControlRegistry = controlRegistry,
        RenderPipeline = WdUIUsage.RenderPipeline,
        Services = serviceProvider
    };
    await orchestrator.LoadAndBindContainerAsync(config, context);
}
```

### 5.5 验证标准

- [ ] InitCommands 在管线阶段3 中正确执行
- [ ] InitCommands 后的控件重新注册在管线中自动完成
- [ ] 控件 Items 中的命令执行不受影响
- [ ] 旧路径的 `RegisterSideContainerControls` 可安全删除

---

## 6. 阶段4 (P3)：步骤1+2 迁移 — 配置加载和注册

### 6.1 目标

将 `ConfigLoader.LoadSideContainerAsync` 和 `ConfigRegistrar.RegisterLoadedConfig` 纳入 `EventBindingOrchestrator` 管线。

### 6.2 当前状态

```csharp
// 步骤1: 配置加载
var config = await ConfigLoader.LoadSideContainerAsync(
    sideContainer, configPath, loadLock, logAction);

// 步骤2: 注册配置
ConfigRegistrar.RegisterLoadedConfig(config);
```

### 6.3 迁移方案

#### 6.3.1 扩展 IConfigLoader 接口

```csharp
// EventBindingOrchestrator 层 IConfigLoader
public interface IConfigLoader
{
    /// <summary>从文件加载配置并渲染到容器</summary>
    Task<UIConfiguration> LoadSideContainerAsync(
        object container, string configPath, object? loadLock, Action<string>? logAction);

    /// <summary>加载配置到组件注册中心</summary>
    Task LoadAsync(UIConfiguration config, IComponentRegistry registry);
}
```

#### 6.3.2 将配置注册纳入管线阶段1

```csharp
private async Task InvokeConfigLoaderAsync(IContainerBinderContext context)
{
    var config = context.Config;

    // 1. 如果配置尚未加载，先加载
    if (config == null)
    {
        config = await _configLoader.LoadSideContainerAsync(
            context.Container, context.ConfigPath, null, msg => _logger.LogDebug(msg));
        context.Config = config;
    }

    // 2. 注册配置到 PipelineEntry._loadedConfigs
    ConfigRegistrar.RegisterLoadedConfig(config);

    // 3. 注册到 ComponentRegistry
    var registry = ResolveComponentRegistry(context);
    if (registry != null)
    {
        await _configLoader.LoadAsync(config, registry);
    }
}
```

### 6.4 最终目标：单一入口

```csharp
// MainForm.cs — 最终状态
public async Task LoadSideContainerAsync(string configPath)
{
    var orchestrator = ServiceLocator.Resolve<EventBindingOrchestrator>();
    var context = new ContainerBinderContext(sideContainer, configPath)
    {
        ControlRegistry = controlRegistry,
        RenderPipeline = WdUIUsage.RenderPipeline,
        Services = serviceProvider,
        LoadLock = loadLock,
        LogAction = msg => logAction?.Invoke(msg)
    };
    await orchestrator.LoadAndBindContainerAsync(context);
}
```

---

## 7. 阶段5 (P4)：旧路径清理

### 7.1 删除清单

| 文件 | 删除内容 | 替代 |
|------|---------|------|
| `SideContainerBinder.cs` | `RegisterSideContainerControls` 方法 | `IConfigLoader.LoadAsync` 实现 |
| `SideContainerBinder.cs` | 3 参数 `BindSideContainerEventsAsync` | `EventBindingOrchestrator` 管线 |
| `ConfigRegistrar.cs` | `RegisterLoadedConfig` 方法 | 管线阶段1 内部处理 |
| `MainForm.cs` | InitCommands 循环 | 管线阶段3 内部处理 |
| `MainForm.cs` | 步骤1-5 的分散调用 | 单一 `orchestrator.LoadAndBindContainerAsync` |

### 7.2 保留清单

| 文件 | 保留原因 |
|------|---------|
| `SideContainerBinder.cs` 的 4 参数重载 | 作为旧路径到新路径的过渡桥梁 |
| `ConfigRegistrar.cs` 的其他方法 | 可能被其他调用方使用 |
| `IControlRegistry` 接口 | 仍被 WinForms 层使用 |

---

## 8. 风险与注意事项

### 8.1 风险

| 风险 | 等级 | 缓解措施 |
|------|------|---------|
| `EventBindingOrchestratorContext` 缺失导致编译失败 | 🔴 高 | 阶段1 立即修复 |
| `IRenderPipeline` 抽象可能遗漏 WinForms 特定逻辑 | 🟡 中 | 保留旧路径作为回退 |
| InitCommands 后的控件重新注册时序问题 | 🟡 中 | 在管线阶段3 中显式处理 |
| `IControlRegistry` 和 `IComponentRegistry` 数据不一致 | 🟢 低 | 通过适配器同步 |

### 8.2 注意事项

1. **每个阶段可独立发布** — 不要求所有阶段完成后才部署
2. **旧路径保留** — 每个阶段迁移后，旧路径代码保留至少一个发布周期
3. **日志对比** — 迁移期间开启详细日志，对比新旧路径的绑定结果
4. **回滚方案** — 如果新路径出现问题，切换回旧路径的开关

---

## 9. 附录

### 9.1 文件映射

```
旧路径文件                             新路径文件
─────────────────────────────────────────────────────
FrontEndDriven/Abstractions/IConfigLoader
  → Share/EventBindingOrchestrator/Abstractions/IConfigLoader

FrontEndDriven/WinForms/EventBinding/SideContainerBinder
  → Share/EventBindingOrchestrator/Pipeline/ContainerBinderPipeline
  → Share/EventBindingOrchestrator/Abstractions/IRenderPipeline (新增)

FrontEndDriven/WinForms/EventBinding/ConfigRegistrar
  → Share/EventBindingOrchestrator/Pipeline/ContainerBinderPipeline (阶段1)

FrontEndDriven/WinForms/Rendering/WinFormsRenderPipeline
  → Operation/FrontEndDriven/WinForms/EventBinding/WinFormsRenderPipelineAdapter (新增)

MainForm (InitCommands 循环)
  → Share/EventBindingOrchestrator/Pipeline/ContainerBinderPipeline (阶段3)
```

### 9.2 接口依赖图

```
EventBindingOrchestrator
  ├── IContainerBinderContext    ← ContainerBinderContext
  ├── ContainerBinderPipeline
  │     ├── IConfigLoader        ← SideContainerConfigLoader (新增)
  │     ├── IEventBindingService ← EventTriggerService (Operation 层)
  │     ├── ICommandExecutor     ← CommandExecutor (Operation 层)
  │     ├── IComponentRegistry   ← ComponentRegistry (FrameworkAbstractions)
  │     └── IRenderPipeline      ← WinFormsRenderPipelineAdapter (新增)
  └── IEventBindingResolver      ← EventBindingResolver
```

### 9.3 迁移检查清单

```
阶段1 (P0): 接口契约对齐
  [ ] ContainerBinderContext 移除 EventBindingOrchestratorContext 依赖
  [ ] 编译通过

阶段2 (P1): 步骤5 迁移
  [ ] IRenderPipeline 接口定义
  [ ] WinFormsRenderPipelineAdapter 实现
  [ ] ContainerBinderPipeline 支持可选渲染管线
  [ ] 新旧路径事件绑定结果一致

阶段3 (P2): 步骤3+4 迁移
  [ ] InitCommands 纳入管线阶段3
  [ ] 控件注册纳入管线阶段1
  [ ] InitCommands 后控件重新注册自动完成

阶段4 (P3): 步骤1+2 迁移
  [ ] IConfigLoader 扩展 LoadSideContainerAsync
  [ ] 配置注册纳入管线阶段1
  [ ] MainForm 调用简化为单一入口

阶段5 (P4): 旧路径清理
  [ ] RegisterSideContainerControls 删除
  [ ] 3 参数 BindSideContainerEventsAsync 删除
  [ ] ConfigRegistrar.RegisterLoadedConfig 删除
  [ ] MainForm InitCommands 循环删除
```
