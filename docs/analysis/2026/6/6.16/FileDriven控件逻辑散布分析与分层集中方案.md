# FileDriven 控件逻辑散布分析与分层集中方案

> **版本**：v2.3  
> **日期**：2026-06-15  
> **范围**：FileDriven / Share / Operation(WdUI) 三层架构  
> **涉及文件**：约 22 个文件 → 集中后 16 个文件  
> **重构状态**：P1 ✅ P2 ✅ P3 ✅ P4 ✅ P5 ✅ P6 ✅ P7 ✅ P8 ✅ P9 ✅ P10 ✅ P11 ✅ P12 ✅ P13 ✅（全部完成）
> **跨框架修复**：P14 ✅（ExecuteCommandStage 跨框架逻辑错误修复）


---

## 目录

1. [现状：控件逻辑散布全景](#1-现状控件逻辑散布全景)
2. [核心问题清单](#2-核心问题清单)
3. [分层集中方案](#3-分层集中方案)
4. [跨层调用链统一](#4-跨层调用链统一)
5. [实施路线图](#5-实施路线图)
6. [最终架构图](#6-最终架构图)
7. [附录：文件清单](#7-附录文件清单)

---

## 1. 现状：控件逻辑散布全景

控件逻辑散布在 **3 层架构** 的 **约 22 个文件** 中，按功能分为 **5 个维度**：控件查找、值读写、注册中心、事件注册、结果写入。

### 1.1 控件查找（Control Finding）— 9 个文件

#### Share 层（2 个同名接口并存 ⚠️）

| 文件 | 角色 | 方法数 |
|------|------|--------|
| `Share\FrameworkAbstractions\Controls\IControlFinder.cs` | **旧接口** | 2 方法（FindControl / FindControl\<T\>） |
| `Share\CommandFramework\Abstractions\IControlFinder.cs` | **新接口** | 10 方法（FindByName / FindByType / Find / FindInForm / FindForm / IsDescendantOf / GetDepth 等） |

> **问题**：两个接口同名（`IControlFinder`）不同命名空间，实现类需要选择实现哪一个，容易混淆。

#### 实现类（4 个，分属不同层）

| 文件 | 实现的接口 | 设计模式 | 所在层 |
|------|-----------|---------|--------|
| `Share\FrameworkAbstractions\Controls\WinFormsControlFinder.cs` | 旧接口 | 直接实现 | Share |
| `FileDriven\FileDriven.Pipeline\Core\WinFormsControlFinder.cs` | 新接口 | 直接实现 | FileDriven.Pipeline |
| `FileDriven\FileDriven.Execution\Command\ControlRegistryAwareFinder.cs` | 新接口 | **装饰器**（包装 WinFormsControlFinder） | FileDriven.Execution |
| `Operation\WdUI\Adapters\WdUIControlFinderAdapter.cs` | 新接口 | **适配器**（桥接 WdUI 底层引擎） | Operation |

> **问题**：两个 `WinFormsControlFinder` 功能高度重叠，位于不同项目。`ControlRegistryAwareFinder` 是装饰器，不是独立实现。

#### 辅助类（3 个）

| 文件 | 角色 |
|------|------|
| `FileDriven\FileDriven.UI\Utils\ControlHelper.cs` | 静态辅助方法（FindControlByName / FindControl\<T\>）— **[Obsolete]** |
| `FileDriven\FileDriven.Execution\FileDrivenExecutor.cs` | 私有 `FindControlByName`（已清理，委托给 WinFormsControlFinder） |
| `Operation\WdUI\Framework\Shared\ControlFinder.cs` | WdUI 底层查找引擎 |

### 1.2 控件值读写（Control Value Read/Write）— 6 个文件

| 文件 | 角色 |
|------|------|
| `Share\CommandFramework\Abstractions\IControlValueWriter.cs` | 接口（WriteValue / ReadValue / SupportsType / RegisterAdapter） |
| `FileDriven\FileDriven.Execution\Command\WinFormsControlValueWriter.cs` | 实现（预注册 Label / TextBox / ListView / RichTextBox 适配器，内部处理 Invoke） |
| `FileDriven\FileDriven.UI\Abstractions\UIAbstractions.cs` | `IControlValueAccessor` 接口（GetValue / SetValue） |
| `FileDriven\FileDriven.Execution\FileDrivenExecutor.cs` | 私有 `WriteValueToControl`（已清理，委托给 WinFormsControlValueWriter） |
| `FileDriven\FileDriven.UI\UICommands.cs` | `ControlValueHelper`（本地实现，委托给 BasicMethod） |
| `Operation\WdUI\Adapters\WdUIControlValueWriterAdapter.cs` | WdUI 适配器（基于 ControlValueHelper） |

### 1.3 控件注册中心（Control Registry）— 3 个文件

| 文件 | 角色 |
|------|------|
| `FileDriven\FileDriven.UI\Abstractions\IControlRegistry.cs` | 接口 + 线程安全默认实现 |
| `FileDriven\FileDriven.UI\Core\MainForm.cs` | `ControlRegistry` 属性 + `RegisterSideContainerControls` 方法 |
| `FileDriven\FileDriven.Execution\FileDrivenExecutor.cs` | 将 ControlRegistry 注册到 `ComponentRegistry` |

### 1.4 事件注册与触发（Event Registration & Triggering）— 4 个文件

| 文件 | 角色 |
|------|------|
| `FileDriven\FileDriven.UI\Utils\WinFormsEventRegistrar.cs` | **代码驱动** — 静态方法注册事件处理器 |
| `FileDriven\FileDriven.UI\Event\EventTriggerService.cs` | **配置驱动** — 从 JSON events 自动绑定并触发 |
| `FileDriven\FileDriven.Pipeline\PipelineEntry.cs` | `FindBinding` 方法 |
| `Share\CommandFramework\Pipeline\Stages\ExecuteCommandStage.cs` | `FindBinding` + `WriteResult` |

### 1.5 结果写入（Result Writing）— 3 条路径

| 文件 | 命令类型 | 实际查找链 |
|------|---------|-----------|
| `Share\CommandFramework\Pipeline\Stages\ExecuteCommandStage.cs` | **click 命令** | `_controlFinder`（构造函数注入）→ `_controlValueWriter` → 回退 `SharedData["ControlFinder"]` → 回退 `SharedData["ControlValueWriter"]` |
| `FileDriven\FileDriven.Execution\FileDrivenExecutor.cs` | **exec 命令** | `ComponentRegistry.Resolve<IControlFinder>()` → `IControlFinder.FindByName()` → **回退** `WriteResultToControl()` → `ComponentRegistry.Resolve<IControlRegistry>()` → `IControlRegistry.Find()` → **回退** `TargetForm.Controls.Find()` |
| `FileDriven\FileDriven.UI\Event\EventTriggerService.cs` | **事件触发** | `ComponentRegistry.Resolve<IControlFinder>()` → `IControlFinder.FindByName()` → **回退** `ControlHelper.FindControlByName` → `ComponentRegistry.Resolve<IControlValueWriter>()` → `IControlValueWriter.ReadValue()` → **回退** `ControlValueHelper.GetValueStatic` |

> **问题**：三条路径的查找链**不完全一致**，同一控件在不同命令下可能走不同的查找逻辑。exec 命令实际有 4 级回退，click 命令有 3 级回退，事件触发已部分迁移到 IControlFinder。

---

## 2. 核心问题清单

### 🔴 问题 1：两个 IControlFinder 接口并存

```
Share\FrameworkAbstractions\Controls\IControlFinder（旧，2 方法）
Share\CommandFramework\Abstractions\IControlFinder（新，10 方法）
```

同名不同命名空间，实现类需要选择实现哪一个。

### 🔴 问题 2：两个 WinFormsControlFinder 实现

```
Share\FrameworkAbstractions\Controls\WinFormsControlFinder（旧接口实现）
FileDriven\FileDriven.Pipeline\Core\WinFormsControlFinder（新接口实现）
```

功能重叠，位于不同项目。

### 🔴 问题 3：FileDrivenExecutor 中 3 个残留私有方法（已清理 ✅）

| 方法 | 状态 |
|------|------|
| `FindControlByName` | ✅ 已清理，委托给 WinFormsControlFinder |
| `WriteValueToControl` | ✅ 已清理，委托给 WinFormsControlValueWriter |
| `WriteResultToControl` | ✅ 已清理，委托给 IControlFinder + IControlValueWriter |

### 🟡 问题 4：三层散布导致查找优先级链不统一

click / exec / 事件触发 三条路径的查找链不完全一致，exec 有 4 级回退最深。

### 🟡 问题 5：WdUI 层有独立的控件查找体系

通过适配器桥接，但增加了整体复杂度。

### 🟡 问题 6：ControlHelper 作为"统一入口"未被统一使用（已标记 Obsolete ✅）

- `EventTriggerService` 已迁移到 `IControlFinder` + `IControlValueWriter`
- `ExecuteCommandStage` 使用 `IControlFinder` 接口
- `FileDrivenExecutor` 使用 `WinFormsControlFinder` 实例

---

## 3. 分层集中方案

### 3.1 Share 层集中

#### 步骤 1：废弃旧 IControlFinder 接口

**操作**：将旧接口标记为 `[Obsolete]`，所有引用改为新接口。

```csharp
// Share\FrameworkAbstractions\Controls\IControlFinder.cs
[Obsolete("请使用 CommandFramework.Abstractions.IControlFinder")]
public interface IControlFinder { ... }
```

**旧接口 → 新接口映射说明**：

| 旧接口方法 | 新接口等价方法 | 说明 |
|-----------|--------------|------|
| `FindControl(string name)` | `FindByName(string name)` | 跨所有 Form 按名称查找 |
| `FindControl<T>(string name)` | 组合：`FindByName(name) as T` | 新接口没有直接的"按名称+类型"方法，需要调用者自行转换 |

> **注意**：新旧接口方法签名不同，不是简单的命名空间替换。需要逐个评估每个调用点的映射方式。

#### 步骤 2：废弃 Share 层旧 WinFormsControlFinder

**操作**：标记为 `[Obsolete]`，功能已由 `FileDriven.Pipeline.Core.WinFormsControlFinder` 替代。

```csharp
// Share\FrameworkAbstractions\Controls\WinFormsControlFinder.cs
[Obsolete("请使用 FileDriven.Pipeline.Core.WinFormsControlFinder")]
public class WinFormsControlFinder : IControlFinder { ... }
```

#### 步骤 3：简化 ExecuteCommandStage.WriteResult

**操作**：删除 `SharedData` 回退路径，仅保留 `IControlFinder` + `IControlValueWriter` 路径。

```csharp
// 修改前：三级回退（实际代码）
var finder = _controlFinder;
if (finder == null && context.SharedData.TryGetValue("ControlFinder", out var finderObj))
    finder = finderObj as IControlFinder;
var writer = _controlValueWriter;
if (writer == null && context.SharedData.TryGetValue("ControlValueWriter", out var writerObj))
    writer = writerObj as IControlValueWriter;

// 修改后：仅构造函数注入
var finder = _controlFinder;
var writer = _controlValueWriter;
if (finder == null || writer == null)
{
    // 跳过写入（日志记录）
    return;
}
```

> **注意**：此步骤不依赖 P1（ControlServiceCollection），因为 ExecuteCommandStage 通过构造函数注入获取 IControlFinder，而非 ComponentRegistry。

---

### 3.2 FileDriven 层集中

#### 步骤 4：清理 FileDrivenExecutor 中 3 个残留方法（已完成 ✅）

**操作**：将方法体替换为委托调用，保留方法签名以兼容可能的内部调用。

```csharp
// FindControlByName — 委托给 WinFormsControlFinder，保留日志包装
private static Control? FindControlByName(Control parent, string name, int depth = 0)
{
    if (depth == 0)
    {
        FileDrivenLogPlaceholderHandler.Log("Executor_FindControlByName_searching",
            new { controlName = name, parentName = parent.Name, parentType = parent.GetType().Name, childCount = parent.Controls.Count });
    }

    var finder = new WinFormsControlFinder();
    var found = finder.FindByName(parent, name) as Control;

    if (found != null && depth == 0)
    {
        FileDrivenLogPlaceholderHandler.Log("Executor_FindControlByName_found",
            new { controlName = name, controlType = found.GetType().Name });
    }

    return found;
}

// WriteValueToControl — 委托给 WinFormsControlValueWriter（内部已处理 Invoke 线程安全）
private static void WriteValueToControl(Control control, string controlName, string value)
{
    var writer = new WinFormsControlValueWriter();
    var success = writer.WriteValue(control, value);
    if (!success)
    {
        FileDrivenLogPlaceholderHandler.Log("Executor_WriteResultToControl_unsupported_type",
            new { controlName, controlType = control.GetType().Name });
    }
}

// WriteResultToControl — 委托给 IControlFinder + IControlValueWriter（通过 ComponentRegistry）
private static void WriteResultToControl(IExecutionContext context, string controlName, string value)
{
    var finder = ComponentRegistry.Instance.Resolve<IControlFinder>();
    var writer = ComponentRegistry.Instance.Resolve<IControlValueWriter>();
    if (finder != null && writer != null &&
        context.SharedData?.TryGetValue("TargetForm", out var formObj) == true && formObj != null)
    {
        var control = finder.FindByName(formObj, controlName);
        if (control != null)
        {
            writer.WriteValue(control, value);
            return;
        }
    }
    // 日志记录失败（不抛出异常，保持与原始行为一致）
    FileDrivenLogPlaceholderHandler.Log("Executor_WriteResultToControl_failed",
        new { controlName, hasFinder = finder != null, hasWriter = writer != null });
}
```

> **注意**：这 3 个方法都是 `private static`，外部无法通过反射调用（`BindingFlags.NonPublic | BindingFlags.Static` 在正常使用中不会出现），因此不存在反射兼容风险。

#### 步骤 5：标记 ControlHelper 为 Obsolete（已完成 ✅）

```csharp
// FileDriven\FileDriven.UI\Utils\ControlHelper.cs
[Obsolete("请通过 ComponentRegistry.Resolve<IControlFinder>() 获取控件查找器")]
public static class ControlHelper { ... }
```

> **注意**：标记为 Obsolete 后，需要同步修改 EventTriggerService 中对 ControlHelper 的调用，改为通过 ComponentRegistry 获取 IControlFinder。

#### 步骤 6：新增 ControlServiceCollection — 控件服务集中注册入口（已完成 ✅）

**新建文件**：`FileDriven\FileDriven.Execution\Command\ControlServiceCollection.cs`

```csharp
// ============================================================
// ControlServiceCollection.cs — 控件服务集中注册入口
//
// 统一注册所有控件相关服务到 ComponentRegistry：
//   1. IControlFinder（ControlRegistryAwareFinder 装饰器）
//   2. IControlValueWriter（WinFormsControlValueWriter）
//   3. IControlRegistry（可选，由 MainForm 提供）
//   4. IControlService（UnifiedControlService — 统一控件服务）
//
// ★ 替代 FileDrivenExecutor.StartGuiMode 中散落的注册代码
// ★ 替代 FileDrivenExecutor.RegisterComponents 中的控件服务注册
// ============================================================

using CommandFramework.Abstractions;
using FileDriven.Pipeline.Core;
using FileDriven.UI.Abstractions;
using FrameworkAbstractions.ComponentRegistry;
using FrameworkAbstractions.ControlService;

namespace FileDriven.Execution.Command;

/// <summary>
/// 控件服务集中注册入口
/// <para>统一注册所有控件相关服务到 ComponentRegistry。</para>
/// <para>调用方：FileDrivenExecutor.InitializeFramework / StartGuiMode</para>
/// </summary>
public static class ControlServiceCollection
{
    private static bool _registered;

    /// <summary>
    /// 注册所有控件服务
    /// <para>幂等：多次调用仅第一次生效，后续调用跳过。</para>
    /// </summary>
    /// <param name="controlRegistry">控件注册中心（可选，GUI 模式由 MainForm 提供）</param>
    public static void RegisterAll(IControlRegistry? controlRegistry = null)
    {
        // ★ 幂等性检查：确保只注册一次
        if (_registered)
        {
            // 如果传入了新的 controlRegistry 且与已注册的不同，更新注册
            if (controlRegistry != null)
            {
                var registry = ComponentRegistry.Instance;
                registry.Update<IControlRegistry>(
                    controlRegistry,
                    serviceName: "控件注册中心",
                    criticality: CriticalityLevel.High,
                    description: "sideload 加载控件时注册，exec 命令优先从此查找");
            }
            return;
        }

        var reg = ComponentRegistry.Instance;

        // 1. 注册 IControlFinder（ControlRegistryAwareFinder 装饰器）
        //    查找优先级：IControlRegistry → WinFormsControlFinder 递归查找
        var innerFinder = new WinFormsControlFinder();
        var finder = new ControlRegistryAwareFinder(innerFinder, controlRegistry);
        reg.Register<IControlFinder>(
            finder,
            serviceName: "控件查找器（感知注册中心）",
            criticality: CriticalityLevel.High,
            description: "优先从 IControlRegistry 查找，回退到 WinFormsControlFinder 递归查找");

        // 2. 注册 IControlValueWriter（WinFormsControlValueWriter）
        //    支持 Label / TextBox / ListView / RichTextBox，可通过 RegisterAdapter 扩展
        var writer = new WinFormsControlValueWriter();
        reg.Register<IControlValueWriter>(
            writer,
            serviceName: "控件值读写器",
            criticality: CriticalityLevel.High,
            description: "WinForms 控件值读写，支持 Label/TextBox/ListView/RichTextBox");

        // 3. 注册 IControlRegistry（如果传入）
        //    GUI 模式下由 MainForm 提供，CLI 模式下为 null
        if (controlRegistry != null)
        {
            reg.Register<IControlRegistry>(
                controlRegistry,
                serviceName: "控件注册中心",
                criticality: CriticalityLevel.High,
                description: "sideload 加载控件时注册，exec 命令优先从此查找");
        }

        // 4. 注册 IControlService（统一控件服务）
        //    整合查找/读写/注册三大操作，所有消费方通过此接口访问
        var unifiedService = new UnifiedControlService();
        reg.Register<IControlService>(
            unifiedService,
            serviceName: "统一控件服务",
            criticality: CriticalityLevel.High,
            description: "整合控件查找/值读写/注册的统一操作入口，替代 FileDrivenExecutor 旧方法");

        _registered = true;
    }

    /// <summary>
    /// 重置注册状态（主要用于测试）
    /// </summary>
    public static void Reset()
    {
        _registered = false;
    }
}
```

**修改 FileDrivenExecutor.StartGuiMode**：

```csharp
// 修改前：散落的注册代码
ComponentRegistry.Instance.Register<IControlRegistry>(
    mainForm.ControlRegistry, ...);

// 修改后：统一调用
// 注意：RegisterAll 内部会创建新的 WinFormsControlFinder 和 ControlRegistryAwareFinder，
// 如果之前已通过其他方式注册了 IControlFinder，此处会覆盖注册。
ControlServiceCollection.RegisterAll(mainForm.ControlRegistry);
```

> **注意**：`RegisterAll` 应在 `StartGuiMode` 中**只调用一次**，避免重复注册导致 ComponentRegistry 中出现多个同类型实例。

#### 步骤 6b：新增 IControlService 接口 + UnifiedControlService 实现（已完成 ✅）

**新建文件**：`Share\FrameworkAbstractions\ControlService\IControlService.cs`

```csharp
// ============================================================
// IControlService.cs — 统一控件服务接口
//
// 整合控件查找、值读写、注册三大操作，提供统一的对外契约。
// 所有控件操作消费方通过此接口访问，不再直接操作具体实现。
//
// 设计目标：
//   1. 消除 FileDriven.Execution / FileDriven.UI / Share.CommandFramework
//      三处分散的控件操作逻辑
//   2. 通过 ComponentRegistry 运行时解析，避免项目引用限制
//   3. 提供带日志的完整操作（替代 FileDrivenExecutor 旧方法）
// ============================================================

using System.Windows.Forms;

namespace FrameworkAbstractions.ControlService;

public interface IControlService
{
    // 控件查找
    Control? FindControl(string name);
    Control? FindControl(Control container, string name);
    T? FindControl<T>(Control container, string name) where T : Control;

    // 控件值读写
    string? GetValue(Control control);
    void SetValue(Control control, string? value);

    // 控件注册
    void RegisterControl(string name, Control control);
    void UnregisterControl(string name);
    Control? FindRegistered(string name);
    void ClearRegistry();

    // 组合操作（带日志）
    bool TrySetControlValue(string controlName, string? value, Control? container = null);
    string? TryGetControlValue(string controlName, Control? container = null);
}
```

**新建文件**：`FileDriven\FileDriven.Execution\Command\UnifiedControlService.cs`

```csharp
// ============================================================
// UnifiedControlService.cs — 统一控件服务实现
//
// 实现 IControlService 接口，整合控件查找、值读写、注册三大操作。
// 内部委托给 IControlFinder、IControlValueWriter、IControlRegistry，
// 所有依赖通过 ComponentRegistry 运行时获取。
//
// ★ 设计目标：
//   1. 一处实现，处处使用 — 所有控件操作集中在此
//   2. 运行时解耦 — 通过 ComponentRegistry 获取依赖，避免编译时循环引用
//   3. 日志集成 — 带诊断日志的组合操作，替代 FileDrivenExecutor 旧方法
// ============================================================

using System.Windows.Forms;
using CommandFramework.Abstractions;
using FileDriven.UI.Abstractions;
using FrameworkAbstractions.ComponentRegistry;
using FrameworkAbstractions.ControlService;
using Common.PlaceholderFramework.Integrated.Handlers;

namespace FileDriven.Execution.Command;

public class UnifiedControlService : IControlService
{
    private IControlFinder? Finder => ComponentRegistry.Instance.Resolve<IControlFinder>();
    private IControlValueWriter? Writer => ComponentRegistry.Instance.Resolve<IControlValueWriter>();
    private IControlRegistry? Registry => ComponentRegistry.Instance.Resolve<IControlRegistry>();

    // 控件查找
    public Control? FindControl(string name) { ... }
    public Control? FindControl(Control container, string name) { ... }
    public T? FindControl<T>(Control container, string name) where T : Control { ... }

    // 控件值读写
    public string? GetValue(Control control) { ... }
    public void SetValue(Control control, string? value) { ... }

    // 控件注册
    public void RegisterControl(string name, Control control) { ... }
    public void UnregisterControl(string name) { ... }
    public Control? FindRegistered(string name) { ... }
    public void ClearRegistry() { ... }

    // 组合操作（带日志）
    public bool TrySetControlValue(string controlName, string? value, Control? container = null) { ... }
    public string? TryGetControlValue(string controlName, Control? container = null) { ... }
}
```

#### 步骤 6c：ControlRegistryAwareFinder 运行时动态获取 IControlRegistry（已完成 ✅）

**修改**：`FileDriven\FileDriven.Execution\Command\ControlRegistryAwareFinder.cs`

```csharp
// ★ 重构：运行时从 ComponentRegistry 动态获取 IControlRegistry，
//   支持 MainForm 在初始化后注册 IControlRegistry 的场景。

private IControlRegistry? GetRegistry()
{
    if (_initialRegistry != null)
        return _initialRegistry;
    return ComponentRegistry.Instance.Resolve<IControlRegistry>();
}
```

所有 `FindByName` / `FindInForm` 方法改为调用 `GetRegistry()` 而非直接使用 `_controlRegistry` 字段。

#### 步骤 6d：ControlServiceFacade 标记为 [Obsolete]（已完成 ✅）

**修改**：`FileDriven\FileDriven.Execution\Command\ControlServiceFacade.cs`

```csharp
[Obsolete("请使用 IControlService（UnifiedControlService）替代。通过 ComponentRegistry.Instance.Resolve<IControlService>() 获取。")]
public static class ControlServiceFacade { ... }
```

#### 步骤 6e：MainForm.ControlRegistry 注册到 ComponentRegistry（已完成 ✅）

**修改**：`FileDriven\FileDriven.UI\Core\MainForm.cs`

在 `OnRootContainerLoaded` 中添加：

```csharp
// ★ 将 ControlRegistry 注册到 ComponentRegistry
//   使 ControlRegistryAwareFinder 能通过 ComponentRegistry 获取到 MainForm 的控件注册中心
//   exec 命令的 WriteResultToControl 优先从 ControlRegistry 查找 sideload 控件
ComponentRegistry.Instance.Register<IControlRegistry>(
    ControlRegistry,
    serviceName: "控件注册中心（MainForm）",
    criticality: FrameworkAbstractions.ComponentRegistry.CriticalityLevel.High,
    description: "MainForm 的控件注册中心，sideload 加载控件时注册，exec 命令优先从此查找");
```

---

### 3.3 Operation/WdUI 层集中

#### 步骤 7：新增 WdUIControlServiceRegistration

**新建文件**：`Operation\WdUI\Adapters\WdUIControlServiceRegistration.cs`

```csharp
// ============================================================
// WdUIControlServiceRegistration.cs — WdUI 控件服务注册入口
//
// 集中注册 WdUI 层的控件服务到 ComponentRegistry：
//   1. IControlFinder（WdUIControlFinderAdapter）
//   2. IControlValueWriter（WdUIControlValueWriterAdapter）
//
// ★ 当需要 WdUI 原生控件查找能力时使用
// ============================================================

using CommandFramework.Abstractions;
using FrameworkAbstractions.ComponentRegistry;

namespace WdUI.Adapters;

/// <summary>
/// WdUI 控件服务注册入口
/// <para>集中注册 WdUI 层的控件服务到 ComponentRegistry。</para>
/// </summary>
public static class WdUIControlServiceRegistration
{
    /// <summary>
    /// 注册 WdUI 控件服务
    /// </summary>
    public static void RegisterAll()
    {
        var registry = ComponentRegistry.Instance;

        registry.Register<IControlFinder>(
            new WdUIControlFinderAdapter(),
            serviceName: "WdUI 控件查找器适配器",
            criticality: CriticalityLevel.Medium,
            description: "基于 WdUI.Framework.Shared.ControlFinder 的控件查找器");

        registry.Register<IControlValueWriter>(
            new WdUIControlValueWriterAdapter(),
            serviceName: "WdUI 控件值读写器适配器",
            criticality: CriticalityLevel.Medium,
            description: "基于 ControlValueHelper 的控件值读写器");
    }

    /// <summary>
    /// 注销 WdUI 控件服务
    /// </summary>
    public static void UnregisterAll()
    {
        var registry = ComponentRegistry.Instance;
        registry.Unregister<IControlFinder>();
        registry.Unregister<IControlValueWriter>();
    }
}
```

---

## 4. 跨层调用链统一

### 4.1 当前的实际调用链

```
click 命令：ExecuteCommandStage
         → _controlFinder（构造函数注入）
         → _controlValueWriter（构造函数注入）
         → 回退 SharedData["ControlFinder"]（as IControlFinder）
         → 回退 SharedData["ControlValueWriter"]（as IControlValueWriter）

exec 命令：FileDrivenExecutor.exec handler
         → ComponentRegistry.Resolve<IControlFinder>()
         → IControlFinder.FindByName()
         → 回退 WriteResultToControl（私有方法）
           → ComponentRegistry.Resolve<IControlRegistry>()
           → IControlRegistry.Find()
           → 回退 TargetForm.Controls.Find()
           → 回退 WriteValueToControl（私有方法）

事件触发：EventTriggerService.WriteOutputAsync
         → ComponentRegistry.Resolve<IControlFinder>()（已迁移 ✅）
         → IControlFinder.FindByName()
         → 回退 ControlHelper.FindControlByName（静态方法）
         → ComponentRegistry.Resolve<IControlValueWriter>()（已迁移 ✅）
         → IControlValueWriter.ReadValue()
         → 回退 ControlValueHelper.GetValueStatic（静态方法）
```

### 4.2 统一后的目标调用链

```
所有命令/事件：
         → ComponentRegistry.Resolve<IControlFinder>()
         → ControlRegistryAwareFinder.FindByName()
             ├── 1. IControlRegistry.Find(name)        ← sideload 注册的控件
             └── 2. WinFormsControlFinder.FindByName()  ← 传统控件树
         → ComponentRegistry.Resolve<IControlValueWriter>()
         → WinFormsControlValueWriter.WriteValue()
             ├── Label → Text 属性
             ├── TextBox → Text 属性
             ├── ListView → Items 集合
             ├── RichTextBox → Text 属性
             └── RegisterAdapter 扩展
```

> **说明**：上图是**统一后的目标状态**。当前 exec 命令已部分走此路径（先尝试 IControlFinder），click 命令通过构造函数注入走类似路径，事件触发已迁移到 IControlFinder + IControlValueWriter。

### 4.3 统一后的服务注册时机

```
InitializeFramework（CLI 模式）
  └── ControlServiceCollection.RegisterAll(controlRegistry: null)
      ├── IControlFinder（ControlRegistryAwareFinder，无注册中心）
      ├── IControlValueWriter（WinFormsControlValueWriter）
      └── IControlService（UnifiedControlService）

StartGuiMode（GUI 模式）
  ├── MainForm 创建（ControlRegistry 初始化）
  │   └── OnRootContainerLoaded → ComponentRegistry.Register<IControlRegistry>()
  └── ControlServiceCollection.RegisterAll(mainForm.ControlRegistry)  ← 只调用一次
      ├── IControlFinder（ControlRegistryAwareFinder，带注册中心）
      ├── IControlValueWriter（WinFormsControlValueWriter）
      ├── IControlRegistry（MainForm.ControlRegistry）
      └── IControlService（UnifiedControlService）
```

> **注意**：`RegisterAll` 应在 GUI 模式启动时**只调用一次**。如果 CLI 模式已调用过 `RegisterAll(null)`，GUI 模式再次调用会覆盖之前的注册。

---

## 5. 实施路线图

### 5.1 阶段划分

| 阶段 | 内容 | 涉及文件 | 风险 | 依赖 | 状态 |
|------|------|---------|------|------|------|
| **P1** | 新增 `ControlServiceCollection`，统一注册入口 | +1 文件 | 🟢 低 | 无 | ✅ 已完成 |
| **P2** | 清理 `FileDrivenExecutor` 中 3 个残留方法 | 1 文件 | 🟢 低 | P1 | ✅ 已完成 |
| **P3** | 废弃旧 `IControlFinder` + 旧 `WinFormsControlFinder` | 2 文件 | 🟡 中 | 无 | ✅ 已完成 |
| **P4** | 简化 `ExecuteCommandStage.WriteResult` 回退链 | 1 文件 | 🟢 低 | 无 | ✅ 已完成 |
| **P5** | 标记 `ControlHelper` 为 Obsolete + 迁移 EventTriggerService | 2 文件 | 🟡 中 | P1 | ✅ 已完成 |
| **P6** | 新增 `WdUIControlServiceRegistration` | +1 文件 | 🟢 低 | 无 | ✅ 已完成 |
| **P7** | 新增 `IControlService` + `UnifiedControlService` | +2 文件 | 🟢 低 | P1 | ✅ 已完成 |
| **P8** | `ControlRegistryAwareFinder` 运行时动态获取 IControlRegistry | 1 文件 | 🟢 低 | P1 | ✅ 已完成 |
| **P9** | `MainForm.ControlRegistry` 注册到 ComponentRegistry | 1 文件 | 🟢 低 | 无 | ✅ 已完成 |
| **P10** | `ControlServiceFacade` 标记为 [Obsolete] | 1 文件 | 🟢 低 | P7 | ✅ 已完成 |
| **P11** | `UICommandManager` 改用 IControlFinder + IControlValueWriter | 1 文件 | 🟡 中 | P1 | ✅ 已完成 |
| **P12** | **控件逻辑文件集中化** — 5 个文件合并为 1 个 `ControlServices.cs` | 5→1 文件 | 🟢 低 | P7 | ✅ 已完成 |
| **P13** | **FileDriven.UI 层文件集中化** — 4 个文件合并为 2 个（`UIControlAbstractions.cs` + `UIControlUtils.cs`） | 4→2 文件 | 🟢 低 | 无 | ✅ 已完成 |

### 5.2 详细步骤

#### P1：新增 ControlServiceCollection（已完成 ✅）

1. 创建 `FileDriven\FileDriven.Execution\Command\ControlServiceCollection.cs`
2. 实现 `RegisterAll(IControlRegistry?)` 和 `UnregisterAll()` 方法
3. 在 `FileDrivenExecutor.InitializeFramework` 中调用 `ControlServiceCollection.RegisterAll()`
4. 在 `FileDrivenExecutor.StartGuiMode` 中更新注册（传入 `mainForm.ControlRegistry`，确保只调用一次）
5. 删除 `StartGuiMode` 中散落的 `ComponentRegistry.Instance.Register<IControlRegistry>(...)` 代码

#### P2：清理 FileDrivenExecutor 残留方法（已完成 ✅）

1. 搜索 `WriteResultToControl` 的所有调用点，确认是否可替换为 `IControlFinder` + `IControlValueWriter`
2. 搜索 `WriteValueToControl` 的所有调用点，确认是否可替换为 `WinFormsControlValueWriter`
3. 搜索 `FindControlByName` 的所有调用点，确认是否可替换为 `WinFormsControlFinder`
4. 将方法体替换为委托调用（保留方法签名）
5. 运行现有测试，确保无回归

#### P3：废弃旧接口（已完成 ✅）

1. 在旧 `IControlFinder` 接口上添加 `[Obsolete]` 特性
2. 在旧 `WinFormsControlFinder` 类上添加 `[Obsolete]` 特性
3. 搜索所有引用旧接口的地方，逐个评估调用点如何映射到新接口（注意方法签名不同）
4. 编译确认无警告

#### P4：简化 ExecuteCommandStage.WriteResult（已完成 ✅）

1. 删除 `SharedData["ControlFinder"]` 和 `SharedData["ControlValueWriter"]` 回退路径
2. 当 `_controlFinder` 或 `_controlValueWriter` 为 null 时，跳过写入（日志记录）
3. 运行 click 命令测试，确保无回归

#### P5：标记 ControlHelper 为 Obsolete + 迁移 EventTriggerService（已完成 ✅）

1. 在 `ControlHelper` 类上添加 `[Obsolete]` 特性
2. 修改 `EventTriggerService` 中对 `ControlHelper` 的调用，改为通过 `ComponentRegistry.Resolve<IControlFinder>()`
3. 运行事件触发测试，确保无回归

#### P6：新增 WdUIControlServiceRegistration（已完成 ✅）

1. 创建 `Operation\WdUI\Adapters\WdUIControlServiceRegistration.cs`
2. 实现 `RegisterAll()` 和 `UnregisterAll()` 方法

#### P7：新增 IControlService + UnifiedControlService（已完成 ✅）

1. 创建 `Share\FrameworkAbstractions\ControlService\IControlService.cs`
2. 创建 `FileDriven\FileDriven.Execution\Command\UnifiedControlService.cs`
3. 在 `ControlServiceCollection.RegisterAll` 中注册 `IControlService`

#### P8：ControlRegistryAwareFinder 运行时动态获取（已完成 ✅）

1. 添加 `GetRegistry()` 方法，运行时从 `ComponentRegistry` 获取 `IControlRegistry`
2. 所有 `FindByName` / `FindInForm` 方法改为调用 `GetRegistry()`

#### P9：MainForm.ControlRegistry 注册到 ComponentRegistry（已完成 ✅）

1. 在 `MainForm.OnRootContainerLoaded` 中添加 `ComponentRegistry.Instance.Register<IControlRegistry>()`
2. 添加 `using FrameworkAbstractions.ComponentRegistry;`
3. 使 `ControlRegistryAwareFinder` 能通过 `ComponentRegistry` 获取到 MainForm 的控件注册中心

#### P10：ControlServiceFacade 标记为 [Obsolete]（已完成 ✅）

1. 在 `ControlServiceFacade` 类上添加 `[Obsolete]` 特性
2. 提示使用 `IControlService（UnifiedControlService）` 替代

#### P11：UICommandManager 改用 IControlFinder + IControlValueWriter（已完成 ✅）

1. `SetControlValue` 优先通过 `IControlFinder` + `IControlValueWriter`，回退到 `ControlHelper` + `ControlValueHelper`
2. `GetControlValue` 优先通过 `IControlFinder` + `IControlValueWriter`，回退到 `ControlHelper` + `ControlValueHelper`
3. 提取 `LogControlValue` 辅助方法消除重复日志代码

### 5.3 风险评估

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| P2 实施后 exec 命令的 WriteResultToControl 依赖 P1 的注册 | exec 命令结果写入失败 | 确保 P1 先完成，或 P2 中保留回退到原始方法的路径 |
| P3 中旧接口调用点映射复杂 | 编译错误或行为不一致 | 逐个评估每个调用点，编写单元测试验证 |
| P5 中 EventTriggerService 迁移遗漏 | 事件触发功能失效 | 搜索所有 ControlHelper 引用，确保全部迁移 |
| `RegisterAll` 被多次调用导致重复注册 | ComponentRegistry 中多个同类型实例 | 添加幂等性检查，或确保只调用一次 |

---

## 6. 最终架构图

### 6.1 集中后文件结构（目标状态）

```
┌─────────────────────────────────────────────────────────────────┐
│                      Share 层                                    │
│                                                                  │
│  CommandFramework.Abstractions                                   │
│  ├── IControlFinder.cs          ← 唯一接口（10 方法）           │
│  └── IControlValueWriter.cs     ← 唯一接口                      │
│                                                                  │
│  CommandFramework.Pipeline.Stages                                │
│  └── ExecuteCommandStage.cs     ← 仅 IControlFinder + IControlValueWriter │
│                                                                  │
│  FrameworkAbstractions.Controls                                 │
│  ├── IControlFinder.cs          ← [Obsolete] 待废弃             │
│  └── WinFormsControlFinder.cs   ← [Obsolete] 待废弃             │
│                                                                  │
│  FrameworkAbstractions.ControlService                            │
│  └── IControlService.cs         ← ★ 新增：统一控件服务接口      │
├─────────────────────────────────────────────────────────────────┤
│                      FileDriven 层                                │
│                                                                  │
│  FileDriven.Execution.Command  ← ★ 控件逻辑集中目录（1 个文件）│
│  └── ControlServices.cs            ← ★ 唯一文件（~975行）      │
│      ├── ControlServiceCollection    ← 统一注册入口             │
│      ├── UnifiedControlService       ← 统一控件服务实现         │
│      ├── ControlRegistryAwareFinder  ← 装饰器：IControlRegistry 优先 │
│      ├── WinFormsControlValueWriter  ← 值读写实现               │
│      └── ControlServiceFacade        ← [Obsolete] 向后兼容      │
│                                                                  │
│  FileDriven.Pipeline.Core                                        │
│  └── WinFormsControlFinder.cs   ← IControlFinder 实现           │
│                                                                  │
│  FileDriven.UI.Abstractions                                      │
│  └── UIControlAbstractions.cs   ← ★ 合并文件（IControlRegistry + IControlValueAccessor + IEventBindingService）│
│                                                                  │
│  FileDriven.UI.Core                                              │
│  └── MainForm.cs                ← ControlRegistry 属性          │
│                                                                  │
│  FileDriven.UI.Utils                                             │
│  └── UIControlUtils.cs          ← ★ 合并文件（ControlHelper + WinFormsEventRegistrar）│
│                                                                  │
│  FileDriven.UI.Event                                             │
│  └── EventTriggerService.cs     ← 配置驱动事件触发（已迁移到 IControlFinder）│
│                                                                  │
│  FileDriven.Execution                                            │
│  └── FileDrivenExecutor.cs      ← 3 个残留方法已清理            │
├─────────────────────────────────────────────────────────────────┤
│                      Operation/WdUI 层                            │
│                                                                  │
│  WdUI.Adapters                                                   │
│  ├── WdUIControlFinderAdapter.cs         ← 适配器               │
│  ├── WdUIControlValueWriterAdapter.cs    ← 适配器               │
│  └── WdUIControlServiceRegistration.cs   ← ★ 新增：WdUI 注册入口 │
│                                                                  │
│  WdUI.Framework.Shared                                           │
│  ├── ControlFinder.cs            ← WdUI 底层查找引擎            │
│  └── ControlIndex.cs             ← WdUI 控件索引                │
└─────────────────────────────────────────────────────────────────┘
```

### 6.2 集中后调用链（目标状态）

```
┌─────────────┐     ┌──────────────────────────────┐
│ click 命令  │────→│ ExecuteCommandStage          │
└─────────────┘     │  _controlFinder (IControlFinder) │
                    │  _controlValueWriter (IControlValueWriter) │
┌─────────────┐     └──────────┬───────────────────┘
│ exec 命令   │────→ ComponentRegistry             │
└─────────────┘     │  .Resolve<IControlFinder>()   │
                    │  .Resolve<IControlValueWriter>()│
┌─────────────┐     └──────────┬───────────────────┘
│ 事件触发    │────→           │
└─────────────┘     ┌──────────▼───────────────────┐
                    │ ControlRegistryAwareFinder    │
                    │  1. IControlRegistry.Find()  │
                    │  2. WinFormsControlFinder    │
                    └──────────┬───────────────────┘
                               │
                    ┌──────────▼───────────────────┐
                    │ WinFormsControlValueWriter    │
                    │  Label → Text                │
                    │  TextBox → Text              │
                    │  ListView → Items            │
                    │  RichTextBox → Text          │
                    └──────────────────────────────┘
```

> **说明**：上图是**目标状态**。当前 exec 命令已部分走 ComponentRegistry 路径，click 命令通过构造函数注入走类似路径，事件触发已迁移到 IControlFinder + IControlValueWriter。

---

## 7. 附录：文件清单

### 7.1 当前涉及的全部文件（18 个）

| # | 文件路径 | 角色 | 所在层 | 处理方式 |
|---|---------|------|--------|---------|
| 1 | `Share\FrameworkAbstractions\Controls\IControlFinder.cs` | 旧接口 | Share | [Obsolete] |
| 2 | `Share\FrameworkAbstractions\Controls\WinFormsControlFinder.cs` | 旧实现 | Share | [Obsolete] |
| 3 | `Share\CommandFramework\Abstractions\IControlFinder.cs` | 新接口 | Share | 保留 |
| 4 | `Share\CommandFramework\Abstractions\IControlValueWriter.cs` | 接口 | Share | 保留 |
| 5 | `Share\CommandFramework\Pipeline\Stages\ExecuteCommandStage.cs` | 结果写入 | Share | 简化回退链 |
| 6 | `Share\FrameworkAbstractions\ControlService\IControlService.cs` | ★ 新增：统一控件服务接口 | Share | 新增 |
| 7 | `FileDriven\FileDriven.Pipeline\Core\WinFormsControlFinder.cs` | 新实现 | FileDriven | 保留 |
| 8 | `FileDriven\FileDriven.Execution\Command\ControlServices.cs` | ★ **唯一控件逻辑文件**（~975行，合并 5 个文件） | FileDriven | **P12 集中化** |
| 9 | `FileDriven\FileDriven.Execution\FileDrivenExecutor.cs` | 残留方法 | FileDriven | 已清理 |
| 10 | `FileDriven\FileDriven.UI\Abstractions\UIControlAbstractions.cs` | ★ **合并文件**（IControlRegistry + IControlValueAccessor + IEventBindingService） | FileDriven | **P13 集中化** |
| 11 | `FileDriven\FileDriven.UI\Core\MainForm.cs` | ControlRegistry | FileDriven | 保留（注册到 ComponentRegistry） |
| 12 | `FileDriven\FileDriven.UI\Utils\UIControlUtils.cs` | ★ **合并文件**（ControlHelper + WinFormsEventRegistrar） | FileDriven | **P13 集中化** |
| 13 | `FileDriven\FileDriven.UI\Event\EventTriggerService.cs` | 事件触发 | FileDriven | 已迁移到 IControlFinder |
| 14 | `FileDriven\FileDriven.Pipeline\PipelineEntry.cs` | FindBinding | FileDriven | 保留 |
| 15 | `Operation\WdUI\Adapters\WdUIControlFinderAdapter.cs` | 适配器 | Operation | 保留 |
| 16 | `Operation\WdUI\Adapters\WdUIControlValueWriterAdapter.cs` | 适配器 | Operation | 保留 |
| 17 | `Operation\WdUI\Adapters\WdUIControlServiceRegistration.cs` | ★ 新增：WdUI 注册入口 | Operation | 新增 |
| 18 | `Operation\WdUI\Framework\Shared\ControlFinder.cs` | 底层引擎 | Operation | 保留 |


### 7.3 新增文件（4 个）

| # | 文件路径 | 角色 | 所在层 |
|---|---------|------|--------|
| N1 | `Share\FrameworkAbstractions\ControlService\IControlService.cs` | 统一控件服务接口 | Share |
| N2 | `FileDriven\FileDriven.Execution\Command\ControlServices.cs` | ★ **唯一控件逻辑文件**（合并 5 个文件） | FileDriven |
| N3 | `Operation\WdUI\Adapters\WdUIControlServiceRegistration.cs` | WdUI 控件服务注册入口 | Operation |


### 7.4 P14：跨框架逻辑错误修复记录

#### 问题 1：ExecuteCommandStage.HandleSet 使用 WinFormsEventTrigger（WdUI 特有）

**文件**：`Share\CommandFramework\Pipeline\Stages\ExecuteCommandStage.cs`

**现象**：`set` 命令使用 `_winFormsEventTrigger.SetControlTextAndTriggerEvent()` 设置控件值，但 `IWinFormsEventTrigger` 是 WdUI 特有的实现，不适用于 FileDriven 场景。当 FileDriven 单独运行时（无 WdUI），`_winFormsEventTrigger` 为 null，`set` 命令静默失败。

**修复**：改用 `IControlFinder` + `IControlValueWriter`（Composite 模式），确保 `set` 命令在两个框架下都能正常工作。

```csharp
// 修复前：使用 WinFormsEventTrigger（WdUI 特有）
var trigger = ResolveDependency(context, _winFormsEventTrigger, "WinFormsEventTrigger");
trigger ??= new NullWinFormsEventTrigger();
trigger.SetControlTextAndTriggerEvent(formObj, target, value ?? "");

// 修复后：使用 IControlFinder + IControlValueWriter（Composite 模式）
var finder = _controlFinder;
var writer = _controlValueWriter;
if (finder != null && writer != null)
{
    var control = FindControl(formObj, target, finder);
    if (control != null)
    {
        SetControlText(control, value ?? "", writer);
    }
}
```

#### 问题 2：ExecuteCommandStage.WriteResult 有 SharedData 回退路径

**文件**：`Share\CommandFramework\Pipeline\Stages\ExecuteCommandStage.cs`

**现象**：`WriteResult` 方法有 `SharedData["ControlFinder"]` 和 `SharedData["ControlValueWriter"]` 回退路径，这是旧架构的遗留代码。当 `_controlFinder` 或 `_controlValueWriter` 为 null 时，会尝试从 SharedData 获取，但 SharedData 中的实例可能来自不同框架，导致类型不匹配或行为不一致。

**修复**：移除 SharedData 回退路径，因为 `IControlFinder` 和 `IControlValueWriter` 现在通过 Composite 模式注册，所有框架的适配器共存于 Composite 中。

```csharp
// 修复前：三级回退
var finder = _controlFinder;
if (finder == null && context.SharedData.TryGetValue("ControlFinder", out var finderObj))
    finder = finderObj as IControlFinder;
var writer = _controlValueWriter;
if (writer == null && context.SharedData.TryGetValue("ControlValueWriter", out var writerObj))
    writer = writerObj as IControlValueWriter;

// 修复后：仅构造函数注入（Composite 模式）
var finder = _controlFinder;
var writer = _controlValueWriter;
if (finder != null && writer != null)
{
    var control = FindControl(formObj, outputTarget, finder);
    if (control != null)
    {
        SetControlText(control, value, writer);
    }
}
```

#### 问题 3：WdUI 适配器覆盖 FileDriven 注册

**文件**：`Operation\WdUI\UI\WdUI.Core.cs` → `FileDriven\FileDriven.Execution\Command\ControlServices.cs`

**现象**：WdUI 初始化时直接设置 `ControlFinder`/`ControlValueWriter` 覆盖 FileDriven 已注册的 `IControlFinder`/`IControlValueWriter`，导致 FileDriven 的控件查找/写入逻辑被 WdUI 适配器完全替代。

**修复**：使用 `CompositeControlFinder` 和 `CompositeValueWriter`，支持多个 `IControlFinder`/`IControlValueWriter` 按优先级依次查找/读写。WdUI 通过反射调用 `AddFinder`/`AddWriter` 将适配器添加到 Composite 中，而非覆盖注册。

```csharp
// 修复前：直接覆盖注册
options.EnableControlFinder = true;
options.ControlFinder = new WdUIControlFinderAdapter();

// 修复后：添加到 Composite（不覆盖）
if (!TryAddToCompositeFinder())
{
    // FileDriven 未初始化，直接设置（向后兼容）
    options.EnableControlFinder = true;
    options.ControlFinder = new WdUIControlFinderAdapter();
}
```

#### 问题 4：FileDrivenPipelineContext.Reset() 中 SharedData["WinFormsEventTrigger"] 死代码

**文件**：`FileDriven\FileDriven.Execution\Command\FileDrivenPipelineContext.cs`

**现象**：`Reset()` 方法中仍然写入 `SharedData["WinFormsEventTrigger"]`，但 `ExecuteCommandStage.HandleSet` 已改为使用 `IControlFinder` + `IControlValueWriter`，不再从 SharedData 读取 `WinFormsEventTrigger`。此代码为死代码。

**状态**：⚠️ **待清理** — 虽然不影响功能，但建议在下次重构时移除。

#### 问题 5：ExecutionPipelineEntry.RunPipelineAsync 中 SharedData 传递

**文件**：`FileDriven\FileDriven.Execution\Command\ExecutionPipelineEntry.cs`

**现象**：ExecutionCenter 调度路径中仍然通过 `SharedData` 传递 GUI 上下文（`TargetForm`、`UICommandManager`、`WinFormsEventBinder`）。

**状态**：✅ **必要保留** — ExecutionCenter 的消息传递机制需要序列化上下文，这是合理的架构设计。`TargetForm` 的 SharedData 传递是必要的，因为 `CommandStageBase.ExecuteForMode()` 需要从 SharedData 获取 `TargetForm`。


