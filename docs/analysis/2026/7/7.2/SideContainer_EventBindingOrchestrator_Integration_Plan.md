# SideContainer 事件绑定调用链接入 EventBinding 框架 — 重构方案

> **文档状态**：方案设计  
> **创建日期**：2026-07-01  
> **涉及项目**：FrontEndDriven / Share.EventBindingOrchestrator / WdUI / EventTriggerFramework

---

## 1. 背景与目标

### 1.1 当前问题

`SideContainerBinder.BindSideContainerEventsAsync` 只完成了"配置绑定"阶段（JSON 事件 → 控件原生事件），没有接入 EventBindingOrchestrator 的"运行时触发"阶段（用户操作 → 执行函数）。

这意味着 sideload 加载的配置中，控件的事件定义虽然已绑定到 WinForms 原生事件，但 EventBindingOrchestrator 的运行时触发管线没有被调用，无法响应运行时事件触发。

### 1.2 目标

在 SideContainer 事件绑定调用链中追加 EventBindingOrchestrator 的运行时触发注册，使 sideload 配置的控件事件能完整走通"配置绑定 + 运行时触发"双阶段管线。

---

## 2. 当前架构全景

### 2.1 MainForm.LoadSideContainerAsync 完整调用链（当前状态）

```
MainForm.LoadSideContainerAsync(configPath)
  │
  ├─ 1. ConfigLoader.LoadSideContainerAsync(sideContainer, configPath, lock, log)
  │      └─ JsonConfigService.LoadConfigAsync(resolvedPath)  → 反序列化 JSON
  │      └─ sideContainer.LoadConfigAsync(config)             → 渲染控件到容器
  │
  ├─ 2. ConfigRegistrar.RegisterLoadedConfig(config)
  │      └─ 注册到 PipelineEntry._loadedConfigs，使 EventBindingResolver 能查找
  │
  ├─ 3. SideContainerBinder.RegisterSideContainerControls(config, sideContainer, ControlRegistry)
  │      └─ 收集所有控件名称 → ConfigContainer.FindControl → 注册到 ControlRegistry
  │
  ├─ 4. 执行 InitCommands（如果有）
  │      └─ CommandExecutor(cmd) 循环
  │      └─ 再次 RegisterSideContainerControls（InitCommands 后控件可能变化）
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

**问题**：调用方（`MainForm`）需要了解并协调 5 个独立步骤，每个步骤都有多个失败点。  
**核心缺失**：步骤 5 只完成了"配置绑定"阶段（JSON 事件 → 控件原生事件），没有接入 EventBindingOrchestrator 的"运行时触发"阶段。

### 2.2 运行时触发阶段（缺失）

```
EventBindingOrchestrator.LoadAndBindContainerAsync
  (Share.EventBindingOrchestrator)
  │
  └─ ContainerBinderPipeline.ExecuteAsync(context)
      ├─ InvokeConfigLoaderAsync → IConfigLoader.LoadAsync
      │   └─ 加载配置到 ComponentRegistry
      ├─ BindEventsAsync → IEventBindingService.TriggerEventAsync
      │   └─ 注册运行时事件触发（用户操作 → 执行函数）
      └─ ExecuteCommandsAsync → ICommandExecutor.Execute
          └─ 执行绑定命令
```

**接口**：`Share.EventBindingOrchestrator.Abstractions.IEventBindingService`  
**方法**：`TriggerEventAsync(string controlName, string eventType, string value)`  
**职责**：用户操作 → 执行对应函数  
**时机**：运行时按需触发

### 2.3 两个 IEventBindingService 的关系

| 维度 | UniversalMiddleware 接口 | EventBindingOrchestrator 接口 |
|------|------------------------|------------------------------|
| **命名空间** | `UniversalMiddleware.Abstractions` | `Share.EventBindingOrchestrator.Abstractions` |
| **定义位置** | `Abstractions.cs:490` | `IEventBindingService.cs:21` |
| **核心方法** | `BindEventsAsync(object, object)` | `TriggerEventAsync(string, string, string)` |
| **职责** | 配置绑定（JSON → 原生事件） | 运行时触发（用户操作 → 函数） |
| **实现** | `DefaultEventBindingService` | `EventTriggerService` |
| **调用方** | `WinFormsRenderPipeline.ExecuteBindEventsStage` | `ContainerBinderPipeline.BindEventsAsync` |

**两者职责不同，不是重复，而是互补关系。**


---

## 3. 重构方案

### 3.1 修改文件

| 文件 | 修改类型 | 说明 |
|------|---------|------|
| `CsharpPrj/Operation/FrontEndDriven/WinForms/EventBinding/SideContainerBinder.cs` | **修改** | 追加 `RegisterRuntimeEventTriggeringAsync` 方法 |
| `CsharpPrj/Share/EventBindingOrchestrator/EventBindingOrchestrator.cs` | 无需修改 | 已提供 `LoadAndBindContainerAsync` 入口 |
| `CsharpPrj/Share/EventBindingOrchestrator/Pipeline/ContainerBinderPipeline.cs` | 无需修改 | 管线阶段已完备 |

### 3.2 修改后的 SideContainerBinder.cs

```csharp
// ============================================================
// SideContainerBinder.cs — 侧边容器控件注册与事件绑定
//
// ★ 从 FileDrivenWdUIBridge 提取
// ★ 职责：sideload 配置加载后的控件注册 + 渲染管线事件绑定
//         + EventBindingOrchestrator 运行时触发注册
// ★ 使 FileDrivenWdUIBridge 专注于容器管理/生命周期职责
// ============================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using Common.PlaceholderFramework.Core;
using FrontEndDriven.Abstractions;
using Modules.ConfigurationModels;
using WdUI;
using WdUI.UI.Controls;

namespace FrontEndDriven.WinForms.EventBinding;

/// <summary>
/// 侧边容器控件注册与事件绑定
/// <para>从 FileDrivenWdUIBridge 提取的独立职责：</para>
/// <para>1. <see cref="RegisterSideContainerControls"/> — 从 UIConfiguration 提取控件名称并注册到 ControlRegistry</para>
/// <para>2. <see cref="BindSideContainerEventsAsync"/> — 通过渲染管线执行 sideload 配置的事件绑定</para>
/// <para>3. <see cref="RegisterRuntimeEventTriggeringAsync"/> — 通过 EventBindingOrchestrator 注册运行时触发</para>
/// </summary>
public static class SideContainerBinder
{
    // ================================================================
    // 控件注册（不变）
    // ================================================================

    /// <summary>
    /// 从加载的 UIConfiguration 中提取控件名称并注册到 ControlRegistry
    /// <para>sideload 加载的控件在 ConfigContainer 中动态渲染，递归查找可能失败。</para>
    /// <para>通过此方法主动注册，使 WriteResultToControl 能直接找到控件。</para>
    /// </summary>
    public static void RegisterSideContainerControls(
        UIConfiguration config,
        ConfigContainer sideContainer,
        IControlRegistry controlRegistry)
    {
        if (config.Views == null) return;

        var allControlNames = new HashSet<string>();
        foreach (var view in config.Views)
        {
            if (view.Controls == null) continue;
            CollectControlNames(view.Controls, allControlNames);
        }

        foreach (var name in allControlNames)
        {
            RegisterSingleControl(name, sideContainer, controlRegistry);
        }
    }

    private static void CollectControlNames(List<ControlConfig> controls, HashSet<string> names)
    {
        foreach (var ctrl in controls)
        {
            if (!string.IsNullOrWhiteSpace(ctrl.Name))
                names.Add(ctrl.Name);
            if (ctrl.Controls != null && ctrl.Controls.Count > 0)
                CollectControlNames(ctrl.Controls, names);
        }
    }

    private static void RegisterSingleControl(string controlName, ConfigContainer sideContainer, IControlRegistry controlRegistry)
    {
        var actualControl = sideContainer.FindControl<Control>(controlName);
        if (actualControl != null)
        {
            controlRegistry.Register(controlName, actualControl);
        }
        else
        {
            PlaceholderLogHelper.LogWarn("FrontEndDriven", "SideContainerBinder",
                $"控件查找失败: {controlName}");
        }
    }

    // ================================================================
    // 渲染管线事件绑定（修改：追加运行时触发注册）
    // ================================================================

    /// <summary>
    /// 通过渲染管线执行 sideload 配置的事件绑定
    /// <para>封装 RenderSessionOnly + BindEventsAsync + RegisterRuntimeEventTriggeringAsync 调用，</para>
    /// <para>使 MainForm.LoadSideContainerAsync 只需一行调用。</para>
    /// </summary>
    /// <param name="sideContainer">侧边容器</param>
    /// <param name="config">已加载的 UIConfiguration</param>
    /// <param name="configPath">配置路径（仅用于日志）</param>
    /// <param name="orchestrator">EventBindingOrchestrator 实例（由 MainForm 注入）</param>
    public static async Task BindSideContainerEventsAsync(
        ConfigContainer sideContainer,
        UIConfiguration config,
        string configPath,
        EventBindingOrchestrator.EventBindingOrchestrator orchestrator)
    {
        var pipeline = WdUIUsage.RenderPipeline;
        if (pipeline.EventBinder == null)
        {
            PlaceholderLogHelper.LogWarn("FrontEndDriven", "SideContainerBinder",
                $"事件绑定跳过: {configPath}, 原因: RenderPipeline.EventBinder 未注入");
            return;
        }

        try
        {
            PlaceholderLogHelper.LogInfo("FrontEndDriven", "SideContainerBinder",
                $"开始事件绑定: {configPath}, 视图数: {config.Views?.Count ?? 0}");

            // 阶段 1: 创建轻量级渲染会话（使 GetActiveSession 能找到）
            pipeline.RenderSessionOnly(sideContainer, config);

            // 阶段 2: 配置绑定（JSON 事件 → 控件原生事件）
            await pipeline.BindEventsAsync(sideContainer);

            // ★ 阶段 3: 运行时触发注册（EventBindingOrchestrator 管线）
            await RegisterRuntimeEventTriggeringAsync(config, configPath, orchestrator);
        }
        catch (Exception ex)
        {
            PlaceholderLogHelper.LogError("FrontEndDriven", "SideContainerBinder",
                $"事件绑定失败: {configPath}, 错误: {ex.Message}");
        }
    }


    // ================================================================
    // ★ 新增：运行时触发注册
    // ================================================================

    /// <summary>
    /// 注册运行时事件触发 — 通过 EventBindingOrchestrator 管线
    /// <para>使 EventBindingOrchestrator 能响应 sideload 配置中的控件事件。</para>
    /// <para>调用 ContainerBinderPipeline 的三阶段管线：</para>
    /// <para>1. 配置加载 — 加载 UI 配置到 ComponentRegistry</para>
    /// <para>2. 事件绑定 — 注册运行时事件触发（用户操作 → 执行函数）</para>
    /// <para>3. 命令执行 — 执行绑定命令</para>
    /// </summary>
    /// <param name="config">UI 配置</param>
    /// <param name="configPath">配置路径（仅用于日志）</param>
    /// <param name="orchestrator">EventBindingOrchestrator 实例（由调用方注入）</param>
    private static async Task RegisterRuntimeEventTriggeringAsync(
        UIConfiguration config,
        string configPath,
        EventBindingOrchestrator.EventBindingOrchestrator orchestrator)
    {
        if (orchestrator == null)
        {
            PlaceholderLogHelper.LogWarn("FrontEndDriven", "SideContainerBinder",
                $"运行时触发注册跳过: {configPath}, 原因: EventBindingOrchestrator 为 null");
            return;
        }

        var options = new EventBindingOrchestrator.ContainerBinderOptions
        {
            EnableDebugLogging = false,
            SkipAlreadyBound = true,
            EnableDeduplication = true
        };

        await orchestrator.LoadAndBindContainerAsync(config, options);
    }

}
```

### 3.3 调用链全景（重构后）

```
MainForm.LoadSideContainerAsync(configPath)
  │
  ├─ 1. ConfigLoader.LoadSideContainerAsync(sideContainer, configPath, lock, log)
  │      └─ JsonConfigService.LoadConfigAsync(resolvedPath)  → 反序列化 JSON
  │      └─ sideContainer.LoadConfigAsync(config)             → 渲染控件到容器
  │
  ├─ 2. ConfigRegistrar.RegisterLoadedConfig(config)
  │
  ├─ 3. SideContainerBinder.RegisterSideContainerControls(config, sideContainer, ControlRegistry)
  │
  ├─ 4. 执行 InitCommands（如果有）
  │      └─ 再次 RegisterSideContainerControls
  │
  └─ 5. SideContainerBinder.BindSideContainerEventsAsync(sideContainer, config, configPath, orchestrator)
         │
         ├─ 阶段 1: pipeline.RenderSessionOnly(sideContainer, config)
         │   └─ WinFormsRenderPipeline.RenderSessionOnly
         │       └─ 创建 WdUI 会话（_sessions[target] = session）
         │
         ├─ 阶段 2: await pipeline.BindEventsAsync(sideContainer)
         │   └─ WinFormsRenderPipeline.BindEventsAsync (override)
         │       └─ ExecuteBindEventsStage
         │           └─ DefaultEventBindingService.BindEvents(form, config, searchRoot: sideContainer)
         │               └─ EventBindingPipelineRunner.Execute (Transformers 管线)
         │                   └─ 遍历控件树，绑定 JSON 事件到 WinForms 原生事件
         │
         └─ 阶段 3: await RegisterRuntimeEventTriggeringAsync(config, configPath, orchestrator)
             └─ EventBindingOrchestrator.LoadAndBindContainerAsync(config, options)
                 └─ ContainerBinderPipeline.ExecuteAsync(context)
                     ├─ InvokeConfigLoaderAsync → IConfigLoader.LoadAsync
                     │   └─ 加载配置到 ComponentRegistry
                     ├─ BindEventsAsync → IEventBindingService.TriggerEventAsync
                     │   └─ 注册运行时事件触发（用户操作 → 执行函数）
                     └─ ExecuteCommandsAsync → ICommandExecutor.Execute
                         └─ 执行绑定命令
```


---

## 4. 关键设计决策

### 4.1 为什么阶段 2 和阶段 3 不冲突？

| 维度 | 阶段 2（配置绑定） | 阶段 3（运行时触发） |
|------|-------------------|-------------------|
| **接口** | `UniversalMiddleware.Abstractions.IEventBindingService` | `Share.EventBindingOrchestrator.Abstractions.IEventBindingService` |
| **核心方法** | `BindEventsAsync(object, object)` | `TriggerEventAsync(string, string, string)` |
| **实现类** | `DefaultEventBindingService` | `EventTriggerService` |
| **职责** | JSON 事件 → 控件原生事件 | 用户操作 → 执行函数 |
| **时机** | 渲染时一次性绑定 | 运行时按需触发 |
| **遍历方式** | Transformers 管线（EventBindingPipelineRunner） | ControlTreeWalker + IEventBindingService |
| **依赖** | WinForms 控件原生事件系统 | ComponentRegistry + ICommandExecutor |

### 4.2 幂等性保证

- **阶段 2**：`WinFormsRenderPipeline.BindEventsAsync` 已有 `_boundTargets` 字典做幂等性检查（`ReferenceEquals(boundConfig, config)`）
- **阶段 3**：`EventBindingOrchestrator.LoadAndBindContainerAsync` 内部有 `SkipAlreadyBound` 选项（默认 `true`）
- 两次调用 `BindSideContainerEventsAsync` 不会导致重复绑定

### 4.3 错误隔离

阶段 3 的异常不应影响阶段 1/2 的结果。当前设计通过独立的 try-catch 保证：

```csharp
try
{
    // 阶段 1 + 2
    pipeline.RenderSessionOnly(sideContainer, config);
    await pipeline.BindEventsAsync(sideContainer);
}
catch (Exception ex)
{
    // 阶段 1/2 失败，记录日志
    PlaceholderLogHelper.LogError(...);
    return;  // 不继续阶段 3
}

// 阶段 3 独立执行（orchestrator 由调用方注入）
await RegisterRuntimeEventTriggeringAsync(config, configPath, orchestrator);
```


### 4.4 依赖注入要求

`EventBindingOrchestrator` 通过构造函数注入到 `MainForm`（或 `FileDrivenWdUIBridge`），再由 `MainForm` 传递给 `SideContainerBinder.BindSideContainerEventsAsync`。

**在 `MainForm` 中注入：**

```csharp
public partial class MainForm : Form
{
    private readonly EventBindingOrchestrator.EventBindingOrchestrator _orchestrator;

    public MainForm(
        ILogger<MainForm> logger,
        /* 其他依赖 */,
        EventBindingOrchestrator.EventBindingOrchestrator orchestrator)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        // ...
    }

    private async Task LoadSideContainerAsync(string configPath)
    {
        // ... 步骤 1-4 ...

        // 步骤 5: 传入 orchestrator
        await SideContainerBinder.BindSideContainerEventsAsync(
            sideContainer, config, configPath, _orchestrator);
    }
}
```

**`EventBindingOrchestrator` 自身的依赖注册**（在 DI 容器中）：

```csharp
// 在 DI 容器注册（如 Startup.cs 或 Program.cs）
services.AddSingleton<IContainerBinderContext, ContainerBinderContext>();
services.AddSingleton<ContainerBinderPipeline>();
services.AddSingleton<EventBindingOrchestrator.EventBindingOrchestrator>();
```

> **注意**：`EventBindingOrchestrator` 的构造函数需要 `ILogger<EventBindingOrchestrator>`、`ContainerBinderPipeline`、`IContainerBinderContext` 三个参数，这些应由 DI 容器自动解析。


---

## 5. 风险与注意事项

### 5.1 循环依赖风险

`SideContainerBinder` 在 `FrontEndDriven` 层，`EventBindingOrchestrator` 在 `Share` 层。`FrontEndDriven` 已引用 `Share` 项目，所以单向依赖是安全的。

### 5.2 性能影响

阶段 3 的 `ContainerBinderPipeline.ExecuteAsync` 会遍历控件树三次（三个阶段各一次）。如果性能敏感，可考虑：

- **短期**：接受三次遍历（sideload 场景非高频操作）
- **长期**：将阶段 3 的遍历合并到阶段 2 中，但会增加耦合度

### 5.3 配置加载重复

`ContainerBinderPipeline.InvokeConfigLoaderAsync` 会再次调用 `IConfigLoader.LoadAsync`。如果配置已加载，`IConfigLoader` 实现应做幂等性检查。

### 5.4 配置变更后的重新绑定

如果 sideload 配置变更后需要重新绑定，调用方应先调用 `DisposeSession` 清理旧会话，再调用 `BindSideContainerEventsAsync`：

```csharp
// 重新绑定流程
pipeline.DisposeSession(sideContainer);
await SideContainerBinder.BindSideContainerEventsAsync(sideContainer, newConfig, configPath);
```

---

## 6. 测试要点

| 测试场景 | 预期结果 |
|---------|---------|
| sideload 配置加载后事件绑定 | 控件原生事件被触发，对应函数被执行 |
| 两次调用 BindSideContainerEventsAsync | 不会重复绑定（幂等性） |
| EventBindingOrchestrator 为 null（未注入） | 阶段 3 跳过，阶段 1/2 正常执行 |
| 阶段 1/2 失败 | 阶段 3 不执行，错误日志记录 |
| 配置变更后重新绑定 | 旧事件解绑，新事件绑定 |


---

## 7. 附录：相关文件路径

| 文件 | 路径 |
|------|------|
| SideContainerBinder | `CsharpPrj/Operation/FrontEndDriven/WinForms/EventBinding/SideContainerBinder.cs` |
| EventBindingOrchestrator | `CsharpPrj/Share/EventBindingOrchestrator/EventBindingOrchestrator.cs` |
| ContainerBinderPipeline | `CsharpPrj/Share/EventBindingOrchestrator/Pipeline/ContainerBinderPipeline.cs` |
| WinFormsRenderPipeline | `CsharpPrj/Operation/WdUI/WdUI.RenderPipeline.cs` |
| DefaultEventBindingService | `CsharpPrj/Operation/EventTriggerFramework/DefaultEventBindingService.cs` |
| IEventBindingService (Share) | `CsharpPrj/Share/EventBindingOrchestrator/Abstractions/IEventBindingService.cs` |
| IEventBindingService (UM) | `CsharpPrj/Share/UniversalMiddleware/Abstractions.cs` (line 490) |
| IConfigLoader (FrontEndDriven) | `CsharpPrj/Operation/FrontEndDriven/Abstractions/IConfigLoader.cs` |
| ConfigLoader (static) | `CsharpPrj/Operation/FrontEndDriven/Loading/ConfigLoader.cs` |
| UIEventBinder | `CsharpPrj/Operation/FrontEndDriven/WinForms/EventBinding/UIEventBinder.cs` |
| WdUIContainerManager | `CsharpPrj/Operation/FrontEndDriven/Implementation/WdUIContainerManager.cs` |

---

## 8. 附录：实际代码覆盖分析（2026-07-01）

> 以下为对 `CsharpPrj/Share/EventBindingOrchestrator` 目录及 `SideContainerBinder.cs` 实际代码的逐节点对比分析结果。

### 8.1 覆盖度总览

| 维度 | 数值 |
|------|------|
| 调用链总节点数 | 18 |
| 完全覆盖 | 10 (55.6%) |
| 部分覆盖 | 1 (5.6%) |
| 未覆盖/编译错误 | 7 (38.9%) |

### 8.2 已覆盖节点

| 调用链节点 | 实际代码位置 | 状态 |
|---|---|---|
| `ConfigLoader.LoadSideContainerAsync` | `FrontEndDriven.Loading` 层 | ✅ |
| `ConfigRegistrar.RegisterLoadedConfig` | `FrontEndDriven` 层 | ✅ |
| `SideContainerBinder.RegisterSideContainerControls` | `SideContainerBinder.cs:41` | ✅ |
| 执行 InitCommands | `MainForm` 中实现 | ✅ |
| 阶段1: `pipeline.RenderSessionOnly` | `SideContainerBinder.cs:129` | ✅ |
| 阶段2: `pipeline.BindEventsAsync` | `SideContainerBinder.cs:132` | ✅ |
| `EventBindingOrchestrator` 类 | `EventBindingOrchestrator.cs:33` | ✅ |
| `LoadAndBindContainerAsync` | `EventBindingOrchestrator.cs:79` | ✅ |
| `ContainerBinderOptions` 类 | `EventBindingOrchestrator.cs:207` | ✅ |
| `ContainerBinderPipeline` 骨架 | `ContainerBinderPipeline.cs:31` | ✅ |
| `ControlTreeWalker` | `Utils/ControlTreeWalker.cs` | ✅ |
| `EventBindingResolver` | `Services/EventBindingResolver.cs` | ✅ |
| `EventTypeMapperService` | `Services/EventTypeMapperService.cs` | ✅ |
| `BindingResult` 模型 | `Models/BindingResult.cs` | ✅ |

### 8.3 未覆盖/编译错误节点

| # | 问题 | 位置 | 严重性 | 说明 |
|---|---|---|---|---|
| 1 | **SideContainerBinder 未按计划修改** | `SideContainerBinder.cs:110` | 🔴 高 | 计划要求4参数签名(含 `orchestrator`)，实际只有3参数；`RegisterRuntimeEventTriggeringAsync` 完全缺失 |
| 2 | **IComponentRegistry 接口不存在** | `ContainerBinderPipeline.cs:37` | 🔴 编译错误 | 管线依赖 `IComponentRegistry` 接口，但代码库中只有具体类 `FrameworkAbstractions.ComponentRegistry.ComponentRegistry`，无此接口定义 |
| 3 | **IConfigLoader 签名不匹配** | `ContainerBinderPipeline.cs:120` | 🔴 编译错误 | 管线调用 `_configLoader.LoadAsync(config, registry)`，但所有 IConfigLoader 实现均无此签名 |
| 4 | **IEventBindingService 签名不匹配** | `ContainerBinderPipeline.cs:149` | 🔴 编译错误 | 管线调用 `_eventBindingService.BindEventsAsync(config, context.Options)`，但 Share 层接口定义的是 `TriggerEventAsync(string, string, string)` |
| 5 | **EventBindingOrchestratorContext 类缺失** | `ContainerBinderContext.cs:19` | 🔴 编译错误 | `ContainerBinderContext` 构造函数依赖此类型，但代码库中不存在 |
| 6 | **阶段3 完全缺失** | `SideContainerBinder.cs` | 🔴 高 | `RegisterRuntimeEventTriggeringAsync` 方法不存在，EventBindingOrchestrator 管线未被调用 |
| 7 | **ICommandExecutor 同步调用** | `ContainerBinderPipeline.cs:185` | 🟡 中 | 管线使用同步 `Execute(commandName)` 而非异步 `ExecuteAsync(commandName)` |

### 8.4 修复优先级

#### P0 — 编译错误（必须修复）
1. **定义 `IComponentRegistry` 接口**（或改为直接使用 `ComponentRegistry` 具体类）
2. **统一 `IConfigLoader` 接口签名** — 添加 `LoadAsync(UIConfiguration, ComponentRegistry)` 方法
3. **统一 `IEventBindingService` 接口签名** — 添加 `BindEventsAsync` 方法或修改调用方式
4. **定义 `EventBindingOrchestratorContext` 类**

#### P1 — 功能缺失
5. **修改 `SideContainerBinder.BindSideContainerEventsAsync` 签名** — 添加 `orchestrator` 参数
6. **实现 `RegisterRuntimeEventTriggeringAsync` 方法** — 调用 `EventBindingOrchestrator.LoadAndBindContainerAsync`

#### P2 — 代码质量
7. **将 `ICommandExecutor.Execute` 改为 `ExecuteAsync`**
8. **添加编译验证到 CI 流程**

### 8.5 关键结论

1. **EventBindingOrchestrator 核心管线骨架已搭建**，但存在 4 处编译错误（接口不匹配/缺失），导致代码无法编译通过
2. **SideContainerBinder 的修改尚未执行**，计划文档中定义的阶段3（`RegisterRuntimeEventTriggeringAsync` → `EventBindingOrchestrator`）完全未实现
3. **接口契约不一致**是最大问题：`ContainerBinderPipeline` 依赖的接口（`IComponentRegistry`、`IConfigLoader`、`IEventBindingService`）与代码库中实际定义的接口签名不匹配
4. **EventBindingOrchestrator 辅助基础设施完整**：`ControlTreeWalker`、`EventBindingResolver`、`EventTypeMapperService`、`BindingResult`、EventBus 等均已实现且可编译
