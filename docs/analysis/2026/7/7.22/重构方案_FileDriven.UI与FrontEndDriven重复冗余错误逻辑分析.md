# 重构方案：FileDriven.UI 与 FrontEndDriven 重复/冗余/错误逻辑分析

> **版本**：v1.0  
> **日期**：2026-07-22  
> **范围**：`FileDriven/FileDriven.UI/` ↔ `CsharpPrj/Operation/FrontEndDriven/`  
> **状态**：待实施

---

## 目录

1. [背景与目标](#1-背景与目标)
2. [问题总览](#2-问题总览)
3. [详细分析](#3-详细分析)
   - [3.1 重复逻辑](#31-重复逻辑)
   - [3.2 冗余逻辑](#32-冗余逻辑)
   - [3.3 错误逻辑](#33-错误逻辑)
4. [重构方案](#4-重构方案)
5. [实施路线图](#5-实施路线图)
6. [风险与注意事项](#6-风险与注意事项)

---

## 1. 背景与目标

### 1.1 背景

`FileDriven.UI`（位于 `FileDriven/FileDriven.UI/`）是 FileDriven 体系下的 WinForms UI 层，负责配置驱动的界面渲染。`FrontEndDriven`（位于 `CsharpPrj/Operation/FrontEndDriven/`）是从 `FileDriven.UI` 迁移和重构后的新一代前端驱动模块，采用 ECS 架构。

在迁移过程中，部分代码采用了"保留旧文件 + 委托转发"的策略，导致：
- 旧文件保留为纯委托转发层，增加维护负担
- 部分功能在两处有独立实现，逻辑重复
- 部分方法存在实现与签名/注释不符的错误
- 异常处理链中存在静默吞异常的问题

### 1.2 目标

1. **消除重复**：删除所有纯委托转发类，统一调用路径
2. **消除冗余**：合并功能重叠的组件，简化架构
3. **修复错误**：修复实现与签名不符、异常被吞、死方法等问题
4. **统一日志**：合并三套并行日志系统

---

## 2. 问题总览

| 编号 | 类别 | 严重程度 | 文件 | 摘要 |
|------|------|----------|------|------|
| D1 | 重复 | 中 | `WinFormsControlMetadataStore.cs`（两处） | 完全重复的委托转发层 |
| D2 | 重复 | 中 | `UILogger.cs`（两处） | 完全重复的委托转发层 |
| D3 | 重复 | 中 | `UICommandManager.cs` / `ControlFinder.cs` | 两套控件查找逻辑 |
| D4 | 重复 | 中 | `EcsPipelineOrchestrator.cs` / `EcsRenderPipeline.cs` | 薄包装层委托 |
| D5 | 重复 | 中 | `UILog` / `FrontEndDrivenLog` / `WdUILog` | 三套并行日志系统 |
| R1 | 冗余 | 低 | `FileDrivenWdUIBridge.cs` | 纯透传桥接层 |
| R2 | 冗余 | 低 | `UnifiedControlService.cs` | 双重依赖解析路径 |
| R3 | 冗余 | 中 | `MainForm.cs` — `ForceAttachEventLoggers` | 与事件绑定管线功能重叠 |
| R4 | 冗余 | 中 | `MainForm.cs` | 15 个注入属性，职责过重 |
| R5 | 冗余 | 中 | `EcsConfigContainer.cs` | 4 个加载方法功能重叠 |
| E1 | 错误 | **高** | `EcsConfigContainer.cs` — `LoadFromFile` | `Task.Run` + `GetAwaiter().GetResult()` 死锁风险 |
| E2 | 错误 | **高** | `EcsConfigContainer.cs` — 加载链 | 异常被两层 catch 静默吞掉 |
| E3 | 错误 | **高** | `MainForm.cs` — `BuildFunctionStatusDescription` | 方法名与实现不符 |
| E4 | 错误 | **高** | `EcsConfigContainer.cs` — 无参构造 | 创建"半初始化"对象 |
| E5 | 错误 | 中 | `EcsConfigContainer.cs` — 固定返回值 | `UseEventTriggerFramework`/`EventScope` 返回固定值 |
| E6 | 错误 | **高** | `EcsRenderPipeline.cs` — `ExecuteBindEventsStageAsync` | 忽略 config 参数 |

---

## 3. 详细分析

### 3.1 重复逻辑

#### D1. WinFormsControlMetadataStore — 完全重复的委托转发层

**涉及文件**：
- `FileDriven/FileDriven.UI/WinForms/WinFormsControlMetadataStore.cs`
- `CsharpPrj/Operation/FrontEndDriven/Shared/Utils/WinFormsControlMetadataStore.cs`

**现状**：
FileDriven.UI 版本是一个纯委托转发类，内部创建 FrontEndDriven 版本的实例并委托所有方法：

```csharp
// FileDriven.UI 版本（纯委托）
public class WinFormsControlMetadataStore : IControlMetadataStore
{
    private readonly FrontEndDriven.WinForms.Utils.WinFormsControlMetadataStore _inner = new();
    
    public Dictionary<string, object>? GetMetadata(object control) 
        => _inner.GetMetadata(control);
    
    public void SetMetadata(object control, Dictionary<string, object> metadata) 
        => _inner.SetMetadata(control, metadata);
}
```

**影响**：
- 任何修改需要同步两个文件
- 调用方多一层间接调用
- 增加代码体积和编译时间

**方案**：删除 FileDriven.UI 版本，所有调用方直接引用 FrontEndDriven 版本。

---

#### D2. UILogger — 委托转发层重复

**涉及文件**：
- `FileDriven/FileDriven.UI/Diagnostics/UILogger.cs`
- `CsharpPrj/Operation/FrontEndDriven/Shared/Diagnostics/UILogger.cs`

**现状**：
FileDriven.UI 的 `UILogger` 是一个纯委托转发类，所有 3 个方法（`InvokeOnUI`、`LogMessage`、`AppendTextSafe`）都委托给 FrontEndDriven 版本。FrontEndDriven 版本才是真正的实现。

**方案**：删除 FileDriven.UI 版本，调用方直接引用 FrontEndDriven 版本。

---

#### D3. UICommandManager.FindControlByName — 与 ControlFinder 重复的控件查找逻辑

**涉及文件**：
- `FileDriven/FileDriven.UI/WinForms/CommandLayer.cs` — `UICommandManager`
- `CsharpPrj/Operation/FrontEndDriven/ControlLookup/Finder/ControlFinder.cs`

**现状**：
`UICommandManager` 类（已标记 `[Obsolete]`）中实现了自己的递归控件查找逻辑：

```csharp
// UICommandManager 中的查找逻辑（已废弃）
private Control? FindControlByName(Control parent, string name)
{
    if (parent.Name == name) return parent;
    foreach (Control child in parent.Controls)
    {
        var result = FindControlByName(child, name);
        if (result != null) return result;
    }
    return null;
}
```

而 FrontEndDriven 的 `IControlFinder` 接口和 `ControlFinder` 实现提供了相同的功能。

**方案**：
1. 确认 `UICommandManager` 是否仍有调用方
2. 若无调用方，删除整个 `UICommandManager` 类
3. 若有调用方，迁移到 `IControlFinder` 接口

---

#### D4. EcsPipelineOrchestrator — 与 EcsRenderPipeline 的委托包装层

**涉及文件**：
- `FileDriven/FileDriven.UI/ECS/EcsPipelineOrchestrator.cs`
- `CsharpPrj/Operation/FrontEndDriven/RenderPipeline/EcsRenderPipeline.cs`

**现状**：
`EcsPipelineOrchestrator` 是一个薄包装层，内部创建 `EcsRenderPipeline` 实例，所有方法都委托给 `EcsRenderPipeline`：

```csharp
public class EcsPipelineOrchestrator
{
    private readonly EcsRenderPipeline _pipeline;
    
    public Task ExecutePipelineAsync(...) => _pipeline.ExecutePipelineAsync(...);
    public void OnWindowResized(...) => _pipeline.OnWindowResized(...);
    public void Clear() => _pipeline.Clear();
    public void Dispose() => _pipeline.Dispose();
}
```

注释也明确标注"★ 2026-07-21 重构：委托到 EcsRenderPipeline"。

**方案**：删除 `EcsPipelineOrchestrator`，调用方直接使用 `EcsRenderPipeline`。

---

#### D5. 日志系统三套并行

**涉及文件**：
- `FileDriven/FileDriven.UI/Diagnostics/UILog.cs` — 前缀 `ui:`
- `CsharpPrj/Operation/FrontEndDriven/Shared/Diagnostics/FrontEndDrivenLog.cs` — 前缀 `fde:`
- `CsharpPrj/Operation/FrontEndDriven/Shared/Diagnostics/WdUILog.cs` — 前缀 `wdui:`

**现状**：
三个日志类都封装 `LogPlaceholderHandlerBase.Log`，结构完全相同（`Initialize` + `Log` 重载），只是 module 名和 keyPrefix 不同：

| 日志类 | Module 名 | 前缀 | 消息 JSON 文件 |
|--------|-----------|------|----------------|
| `UILog` | `FileDriven.UI` | `ui:` | `ui_log_messages.json` |
| `FrontEndDrivenLog` | `FrontEndDriven` | `fde:` | `fde_log_messages.json` |
| `WdUILog` | `WdUI` | `wdui:` | `wdui_log_messages.json` |

此外，`WdUILog` 位于 `WdUI` 命名空间但物理位置在 FrontEndDriven 项目中，命名空间与项目不匹配。

**方案**：
1. 合并为统一的日志入口类 `UnifiedUILog`
2. 通过枚举参数区分模块来源
3. 或保留三个类但提取公共基类消除代码重复

---

### 3.2 冗余逻辑

#### R1. FileDrivenWdUIBridge — 纯透传桥接层

**涉及文件**：
- `FileDriven/FileDriven.UI/WinForms/Services/FileDrivenWdUIBridge.cs`

**现状**：
`FileDrivenWdUIBridge` 的大部分方法都是纯透传：

| 方法 | 委托目标 |
|------|----------|
| `FindAndBindContainers` | `ContainerFinder.FindAndBindMainContainers` |
| `FindConfigSelectorControls` | `ConfigContainerExtensions.FindConfigSelectorControls` |
| `ClearSideContainer` | `sideContainer.ClearContainerContent()` |

只有 `CreateContainer`/`CreateSubContainer`/`Shutdown` 有实际的容器管理逻辑（`_containers` 字典维护）。

**方案**：
1. 保留 `CreateContainer`/`CreateSubContainer`/`Shutdown` 等有实际逻辑的方法
2. 纯透传方法由调用方直接调用目标方法
3. 或标记纯透传方法为 `[Obsolete]` 逐步淘汰

---

#### R2. UnifiedControlService 的双重依赖解析路径

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlLookup/UnifiedControlService.cs`

**现状**：
`UnifiedControlService` 的无参构造函数实现了两套依赖解析路径：

```csharp
public UnifiedControlService()
{
    // 路径 1：优先从 IStorageRegistry 解析
    if (_storageRegistry != null)
    {
        _controlFinder = ResolveDependency<IControlFinder>(_storageRegistry);
        // ...
    }
    else
    {
        // 路径 2：fallback 到 AppServiceProvider
        _controlFinder = ResolveDependency<IControlFinder>(AppServiceProvider.Instance);
        // ...
    }
}
```

虽然已提取 `ResolveDependency` 辅助方法消除代码重复，但运行时两套路径的存在意味着依赖注入方式不统一。

**方案**：
1. 废弃无参构造函数
2. 强制使用构造函数注入
3. 或统一为单一解析路径

---

#### R3. ForceAttachEventLoggers — 与事件绑定管线功能重叠

**涉及文件**：
- `FileDriven/FileDriven.UI/Core/MainForm.cs`

**现状**：
`ForceAttachEventLoggers` 是一个完全独立于事件绑定管线的日志挂载机制，为每个有 events 配置的控件挂载 WinForms 原生事件 handler 输出日志。注释明确说明"完全绕过现有事件绑定管线"。

但 FrontEndDriven 的 `EventBindingSystem` 已经通过 `EcsRenderPipeline` 执行事件绑定，`ForceAttachEventLoggers` 相当于重复遍历控件配置树并做类似的事情。

**方案**：
1. 将强制日志功能合并到事件绑定管线中
2. 作为事件绑定的一个诊断选项（`DiagnosticMode.ForceLogging`）
3. 删除 `ForceAttachEventLoggers` 独立实现

---

#### R4. MainForm 中大量注入属性 — 职责过重

**涉及文件**：
- `FileDriven/FileDriven.UI/Core/MainForm.cs`

**现状**：
`MainForm` 类有 15 个注入属性：

```
EventRegistrar, EventBinder, EventHub, ConfigRegistrar, 
CommandExecutor, PipelineInstance, FunctionExecutor, 
EventExecutionService, StepCoordinator, StageCoordinator, 
ControlRegistry, ConfigSelectorController, CommandHandler, ...
```

加上 `ConfigSelectorController` 和 `CommandHandler` 两个子组件，`MainForm` 承担了过多的职责。

**方案**：
1. 将初始化编排逻辑提取到独立的 `MainFormInitializer` 类
2. `MainForm` 只保留 Form 生命周期管理和事件转发
3. 使用 `IMainFormOrchestrator` 接口抽象编排逻辑

---

#### R5. EcsConfigContainer 的 LoadFromFile/LoadFromFileAsync — 与 LoadConfigAsync 功能重叠

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlLookup/Container/EcsConfigContainer.cs`

**现状**：
`EcsConfigContainer` 有 4 个加载配置的方法：

| 方法 | 状态 |
|------|------|
| `LoadConfigAsync(UIConfiguration config)` | ✅ 正常工作 |
| `LoadConfigAsync(string configPath)` | ❌ 直接抛出异常 |
| `LoadFromFile(string configPath, bool strictMode)` | ❌ 委托给 `LoadConfigAsync(string)` 必然失败 |
| `LoadFromFileAsync(string configPath, bool strictMode)` | ❌ 委托给 `LoadConfigAsync(string)` 必然失败 |

**方案**：
1. 删除 `LoadConfigAsync(string)` 方法
2. 修复 `LoadFromFile`/`LoadFromFileAsync` 的实现（从文件读取配置后调用 `LoadConfigAsync(UIConfiguration)`）
3. 或一并删除，调用方统一使用 `LoadConfigAsync(UIConfiguration)`

---

### 3.3 错误逻辑

#### E1. LoadFromFile 的 Task.Run + GetAwaiter().GetResult() 死锁风险

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlLookup/Container/EcsConfigContainer.cs` — 第 239 行

**现状**：
```csharp
public bool LoadFromFile(string configPath, bool strictMode)
{
    try
    {
        var config = Task.Run(() => LoadFromFileAsync(configPath, strictMode))
                         .GetAwaiter().GetResult();
        // ...
    }
    catch when (!strictMode) { return false; }
}
```

**问题**：
1. `LoadFromFileAsync` 内部调用 `LoadConfigAsync(string)` 直接抛出异常，所以这段代码永远执行不到实际逻辑
2. `Task.Run` + `GetAwaiter().GetResult()` 在 UI 线程上调用时有死锁风险
3. 异常被 `catch when (!strictMode)` 静默吞掉

**方案**：
1. 删除此同步方法
2. 调用方统一使用异步版本 `LoadFromFileAsync`

---

#### E2. LoadConfigAsync(string) 抛出异常但被 LoadFromFileAsync 静默吞掉

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlLookup/Container/EcsConfigContainer.cs`

**现状**：
调用链：
```
LoadFromFile(configPath)
  → Task.Run(() => LoadFromFileAsync(...))
    → await LoadConfigAsync(string)
      → throw new InvalidOperationException("请使用 LoadConfigAsync(UIConfiguration)")
    → catch when (!strictMode) { /* 静默吞掉 */ return null; }
  → catch when (!strictMode) { /* 静默吞掉 */ return false; }
```

**问题**：
- 异常被两层 catch 静默吞掉
- 用户完全不知道加载失败的原因
- 违反零容错原则

**方案**：
1. 删除 `LoadConfigAsync(string)` 方法
2. 修复 `LoadFromFileAsync` 的实现
3. 异常不应被静默吞掉，至少应记录日志

---

#### E3. BuildFunctionStatusDescription 方法名与实现不符

**涉及文件**：
- `FileDriven/FileDriven.UI/Core/MainForm.cs` — 第 949-966 行

**现状**：
```csharp
private static string BuildFunctionStatusDescription(IReadOnlyList<string>? functionNames)
{
    if (functionNames == null || functionNames.Count == 0)
        return "(无函数)";
    var parts = new List<string>();
    foreach (var fn in functionNames)
    {
        if (string.IsNullOrWhiteSpace(fn)) continue;
        // 通过反射检查 Registry 中是否注册了该函数
        // 这里无法直接访问 Registry 实例，返回函数名作为标识
        parts.Add(fn);
    }
    return parts.Count > 0 ? string.Join(", ", parts) : "(无函数)";
}
```

**问题**：
- 注释说"通过反射检查 Registry 中是否注册了该函数"
- 但实际代码只是收集函数名，完全没有反射检查逻辑
- 方法名和注释都承诺了"检查函数注册状态"，但实现只是返回函数名列表
- 日志中 `funcStatus` 字段永远只显示函数名，不反映真实的注册状态

**方案**：
1. 修复实现（通过 `AppServiceProvider` 获取 `IRegistry` 检查）
2. 或重命名方法为 `BuildFunctionNameDescription`
3. 或删除注释中关于反射检查的描述

---

#### E4. EcsConfigContainer 无参构造函数导致 InitializeRenderPipeline 必然失败

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlLookup/Container/EcsConfigContainer.cs`

**现状**：
```csharp
public EcsConfigContainer() // 供 WinForms 设计器和简单工厂使用
{
    _ecsStorage = null;
    _storageRegistry = null;
}
```

当 `RenderConfigAsync` 调用 `InitializeRenderPipeline()` 时：
```csharp
if (ecsStorage == null)
    throw new InvalidOperationException("IECSStorage 不可用，请确保通过构造函数注入");
```

**问题**：
- 无参构造函数创建的实例实际上不可用
- 属于"半初始化"对象
- 违反 Fail-Fast 原则

**方案**：
1. 无参构造函数创建默认的 `IECSStorage`/`IStorageRegistry` 实例
2. 或标记为 `[Obsolete]` 并添加明确警告
3. 或在调用时抛出更明确的异常信息

---

#### E5. EcsConfigContainer 的 UseEventTriggerFramework 和 EventScope 属性返回固定值

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlLookup/Container/EcsConfigContainer.cs` — 第 110-116 行

**现状**：
```csharp
public bool UseEventTriggerFramework => false;
public EventScope? EventScope => null;
```

**问题**：
- 两个属性返回固定值
- 注释说"ContainerEventManager 已移除 UseEventTriggerFramework 属性"
- 但属性本身仍然保留在接口中
- 如果调用方依赖这些属性做条件判断，会得到错误的结果

**方案**：
1. 从接口中移除这些属性
2. 或根据实际运行时状态返回正确值

---

#### E6. EcsRenderPipeline 的 ExecuteBindEventsStageAsync 忽略 config 参数

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/RenderPipeline/EcsRenderPipeline.cs` — 第 373-378 行

**现状**：
```csharp
public Task ExecuteBindEventsStageAsync(object target, object config)
{
    ThrowIfDisposed();
    _eventBindingSystem?.Update();
    return Task.CompletedTask;
}
```

**问题**：
- `config` 参数被完全忽略
- `_eventBindingSystem.Update()` 不接收任何配置数据
- 但调用方（MainForm 第 696 行）传入 `config` 参数期望事件绑定系统使用配置中的事件绑定信息
- 事件绑定系统可能无法正确读取配置中的事件绑定定义

**方案**：
1. 将 `config` 参数传递给 `_eventBindingSystem.Update(config)`
2. 或明确说明参数被忽略的原因
3. 或在接口层面移除不必要的参数

---

## 4. 重构方案

### 4.1 方案优先级

| 优先级 | 编号 | 方案 | 工作量 | 风险 |
|--------|------|------|--------|------|
| P0 | E2, E4 | 修复 EcsConfigContainer 加载链 | 2h | 高 — 影响配置加载核心流程 |
| P0 | E6 | 修复 ExecuteBindEventsStageAsync 参数传递 | 1h | 高 — 影响事件绑定 |
| P1 | D1, D2 | 删除纯委托转发类 | 1h | 低 — 纯删除操作 |
| P1 | D4 | 删除 EcsPipelineOrchestrator | 0.5h | 低 — 纯删除操作 |
| P1 | E3 | 修复 BuildFunctionStatusDescription | 0.5h | 低 — 不影响运行时 |
| P2 | D5 | 合并三套日志系统 | 3h | 中 — 影响日志输出 |
| P2 | R5 | 清理 EcsConfigContainer 加载方法 | 1h | 中 — 需确认调用方 |
| P2 | E1 | 删除有问题的同步方法 | 0.5h | 低 — 方法已不可用 |
| P3 | D3 | 清理废弃的 UICommandManager | 1h | 低 — 需确认无调用方 |
| P3 | R1 | 简化 FileDrivenWdUIBridge | 1h | 低 — 纯重构 |
| P3 | R3 | 合并 ForceAttachEventLoggers | 2h | 中 — 需验证日志完整性 |
| P4 | R2 | 统一依赖解析路径 | 1h | 低 — 向后兼容 |
| P4 | R4 | 拆分 MainForm 职责 | 4h | 中 — 影响范围大 |
| P4 | E5 | 清理接口属性 | 0.5h | 低 — 需确认接口调用方 |

### 4.2 详细重构步骤

#### P0：修复核心错误

**步骤 1：修复 EcsConfigContainer 加载链（E2, E4, E1）**

```csharp
// 1. 删除 LoadConfigAsync(string) 方法
// 2. 修复 LoadFromFileAsync 实现
public async Task<UIConfiguration?> LoadFromFileAsync(string configPath, bool strictMode)
{
    try
    {
        var json = await File.ReadAllTextAsync(configPath);
        var config = JsonSerializer.Deserialize<UIConfiguration>(json, _jsonOptions);
        if (config == null)
            throw new InvalidOperationException($"配置反序列化失败: {configPath}");
        await LoadConfigAsync(config);
        return config;
    }
    catch (Exception ex)
    {
        FrontEndDrivenLog.Log("EcsConfigContainer_LoadFromFileAsync_error",
            new { path = configPath, error = ex.Message });
        if (strictMode) throw;
        return null;
    }
}

// 3. 删除 LoadFromFile 同步方法（调用方使用异步版本）
// 4. 无参构造函数创建默认存储实例
public EcsConfigContainer() : this(new DefaultECSStorage(), new DefaultStorageRegistry()) { }
```

**步骤 2：修复 ExecuteBindEventsStageAsync（E6）**

```csharp
public Task ExecuteBindEventsStageAsync(object target, object config)
{
    ThrowIfDisposed();
    if (config is UIConfiguration uiConfig)
    {
        _eventBindingSystem?.Update(uiConfig);
    }
    else
    {
        _eventBindingSystem?.Update();
    }
    return Task.CompletedTask;
}
```

#### P1：删除重复逻辑

**步骤 3：删除纯委托转发类（D1, D2, D4）**

| 删除文件 | 调用方迁移 |
|----------|-----------|
| `FileDriven.UI/WinForms/WinFormsControlMetadataStore.cs` | 引用 `FrontEndDriven.WinForms.Utils.WinFormsControlMetadataStore` |
| `FileDriven.UI/Diagnostics/UILogger.cs` | 引用 `FrontEndDriven.WinForms.Utils.UILogger` |
| `FileDriven.UI/ECS/EcsPipelineOrchestrator.cs` | 引用 `FrontEndDriven.RenderPipeline.EcsRenderPipeline` |

**步骤 4：修复 BuildFunctionStatusDescription（E3）**

```csharp
private static string BuildFunctionStatusDescription(IReadOnlyList<string>? functionNames)
{
    if (functionNames == null || functionNames.Count == 0)
        return "(无函数)";
    
    var registry = AppServiceProvider.Instance?.GetService<IRegistry>();
    var parts = new List<string>();
    
    foreach (var fn in functionNames)
    {
        if (string.IsNullOrWhiteSpace(fn)) continue;
        var isRegistered = registry?.IsRegistered(fn) ?? false;
        parts.Add(isRegistered ? fn : $"{fn}(未注册)");
    }
    
    return parts.Count > 0 ? string.Join(", ", parts) : "(无函数)";
}
```

#### P2：合并与清理

**步骤 5：合并三套日志系统（D5）**

方案 A（推荐）— 统一入口：
```csharp
public static class UnifiedUILog
{
    public enum Module { FileDrivenUI, FrontEndDriven, WdUI }
    
    private static readonly Dictionary<Module, (string Name, string Prefix)> _moduleMap = new()
    {
        [Module.FileDrivenUI] = ("FileDriven.UI", "ui:"),
        [Module.FrontEndDriven] = ("FrontEndDriven", "fde:"),
        [Module.WdUI] = ("WdUI", "wdui:"),
    };
    
    public static void Log(Module module, string msgId, object? parameters = null)
    {
        var (name, prefix) = _moduleMap[module];
        LogPlaceholderHandlerBase.Log(msgId, name, prefix, parameters);
    }
}
```

方案 B — 保留三个类但提取公共基类：
```csharp
public abstract class ModuleLogBase
{
    protected abstract string ModuleName { get; }
    protected abstract string KeyPrefix { get; }
    
    public void Log(string msgId, object? parameters = null)
        => LogPlaceholderHandlerBase.Log(msgId, ModuleName, KeyPrefix, parameters);
}
```

**步骤 6：清理 EcsConfigContainer 加载方法（R5）**

- 保留 `LoadConfigAsync(UIConfiguration config)` 作为唯一加载入口
- 删除 `LoadConfigAsync(string configPath)`
- 删除 `LoadFromFile(string configPath, bool strictMode)`
- 修复 `LoadFromFileAsync` 实现

#### P3：逐步清理

**步骤 7：清理废弃代码（D3, R1, R3）**

- 确认 `UICommandManager` 无调用方后删除
- 简化 `FileDrivenWdUIBridge`，纯透传方法由调用方直接调用
- 将 `ForceAttachEventLoggers` 合并到事件绑定管线

#### P4：架构优化

**步骤 8：拆分 MainForm 职责（R4）**

```csharp
// 提取 MainFormInitializer
public class MainFormInitializer
{
    private readonly MainForm _form;
    private readonly IServiceProvider _services;
    
    public async Task InitializeAsync()
    {
        InitializeContainers();
        InitializePipeline();
        BindEvents();
        await LoadDefaultConfigAsync();
    }
}
```

**步骤 9：统一依赖解析路径（R2）**

- 废弃 `UnifiedControlService` 的无参构造函数
- 强制使用构造函数注入

**步骤 10：清理接口属性（E5）**

- 从接口中移除 `UseEventTriggerFramework` 和 `EventScope`
- 或根据实际运行时状态返回正确值

---

## 5. 实施路线图

### 阶段 1：紧急修复（P0）— 1 天

```
Day 1:
  ├── 上午: 修复 EcsConfigContainer 加载链（E2, E4, E1）
  │   ├── 删除 LoadConfigAsync(string)
  │   ├── 修复 LoadFromFileAsync 实现
  │   ├── 删除 LoadFromFile 同步方法
  │   └── 修复无参构造函数
  └── 下午: 修复 ExecuteBindEventsStageAsync（E6）
      ├── 传递 config 参数到 EventBindingSystem
      └── 编写单元测试验证
```

### 阶段 2：删除重复（P1）— 0.5 天

```
Day 2:
  ├── 删除 WinFormsControlMetadataStore（D1）
  ├── 删除 UILogger（D2）
  ├── 删除 EcsPipelineOrchestrator（D4）
  └── 修复 BuildFunctionStatusDescription（E3）
```

### 阶段 3：合并清理（P2）— 1 天

```
Day 3:
  ├── 合并三套日志系统（D5）
  ├── 清理 EcsConfigContainer 加载方法（R5）
  └── 删除有问题的同步方法（E1）
```

### 阶段 4：逐步清理（P3）— 1 天

```
Day 4:
  ├── 清理废弃的 UICommandManager（D3）
  ├── 简化 FileDrivenWdUIBridge（R1）
  └── 合并 ForceAttachEventLoggers（R3）
```

### 阶段 5：架构优化（P4）— 1 天

```
Day 5:
  ├── 拆分 MainForm 职责（R4）
  ├── 统一依赖解析路径（R2）
  └── 清理接口属性（E5）
```

### 总工作量估计

| 阶段 | 工作量 | 风险 |
|------|--------|------|
| 阶段 1：紧急修复 | 1 天 | 高 — 影响核心流程 |
| 阶段 2：删除重复 | 0.5 天 | 低 |
| 阶段 3：合并清理 | 1 天 | 中 |
| 阶段 4：逐步清理 | 1 天 | 低 |
| 阶段 5：架构优化 | 1 天 | 中 |
| **合计** | **4.5 天** | |

---

## 6. 风险与注意事项

### 6.1 高风险项

1. **EcsConfigContainer 加载链修复**（E2, E4, E1）
   - 影响配置加载核心流程
   - 需要确保所有调用方已迁移到正确的 API
   - 建议先编写集成测试验证

2. **ExecuteBindEventsStageAsync 修复**（E6）
   - 影响事件绑定功能
   - 需要验证 `EventBindingSystem.Update(config)` 的正确行为
   - 建议先编写单元测试

### 6.2 注意事项

1. **删除文件前确认无调用方**
   - 使用 `find_references` 或 `search_code` 确认
   - 特别注意间接引用（反射、动态加载）

2. **日志系统合并的兼容性**
   - 确保旧的日志消息 ID 仍然可用
   - 确保日志文件路径兼容

3. **MainForm 拆分的影响范围**
   - `MainForm` 被多个外部类引用
   - 拆分后需要更新所有引用点

4. **向后兼容**
   - 删除的类如果被外部项目引用，需要保留 `[Obsolete]` 转发类
   - 建议在删除前先标记为 `[Obsolete]` 一个版本周期

---

## 附录 A：文件清单

### A.1 待删除文件

| 文件 | 原因 | 替代 |
|------|------|------|
| `FileDriven.UI/WinForms/WinFormsControlMetadataStore.cs` | D1 — 重复 | `FrontEndDriven.WinForms.Utils.WinFormsControlMetadataStore` |
| `FileDriven.UI/Diagnostics/UILogger.cs` | D2 — 重复 | `FrontEndDriven.WinForms.Utils.UILogger` |
| `FileDriven.UI/ECS/EcsPipelineOrchestrator.cs` | D4 — 重复 | `FrontEndDriven.RenderPipeline.EcsRenderPipeline` |
| `FileDriven.UI/WinForms/CommandLayer.cs`（部分） | D3 — 废弃 | `IControlFinder` |

### A.2 待修改文件

| 文件 | 修改内容 |
|------|----------|
| `FrontEndDriven/ControlLookup/Container/EcsConfigContainer.cs` | 修复加载链、无参构造函数、接口属性 |
| `FrontEndDriven/RenderPipeline/EcsRenderPipeline.cs` | 修复参数传递 |
| `FileDriven.UI/Core/MainForm.cs` | 修复 BuildFunctionStatusDescription、拆分职责 |
| `FrontEndDriven/Shared/Diagnostics/UILogger.cs` | 合并日志系统 |
| `FrontEndDriven/Shared/Diagnostics/FrontEndDrivenLog.cs` | 合并日志系统 |
| `FrontEndDriven/Shared/Diagnostics/WdUILog.cs` | 合并日志系统 |
| `FileDriven.UI/Diagnostics/UILog.cs` | 合并日志系统 |
| `FrontEndDriven/ControlLookup/UnifiedControlService.cs` | 统一依赖解析路径 |
| `FileDriven.UI/WinForms/Services/FileDrivenWdUIBridge.cs` | 简化桥接层 |

---

## 附录 B：调用关系图

```
MainForm
  ├── EcsPipelineOrchestrator (D4) ──→ EcsRenderPipeline (保留)
  ├── UILogger (D2) ──→ FrontEndDriven.WinForms.Utils.UILogger (保留)
  ├── WinFormsControlMetadataStore (D1) ──→ FrontEndDriven.WinForms.Utils.WinFormsControlMetadataStore (保留)
  ├── UILog (D5) ──→ 合并到 UnifiedUILog
  ├── ConfigSelectorController
  │   └── EcsConfigContainer (E1, E2, E4, E5)
  │       ├── LoadFromFile (E1) → 删除
  │       ├── LoadFromFileAsync (E2) → 修复
  │       └── LoadConfigAsync(string) (E2) → 删除
 