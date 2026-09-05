# EventTriggerFramework WinForms IoC 解耦方案

> **日期**：2026-07-05  
> **作者**：Cline  
> **状态**：方案设计完成，待实施  
> **关联任务**：将 EventTriggerFramework 中所有 WinForms 依赖集中解耦到 WdUI

---

## 一、背景与目标

### 1.1 当前问题

`EventTriggerFramework` 是一个事件触发中间层框架，但其 `WinForms/` 子目录中的 10 个文件全部依赖 `System.Windows.Forms`，且核心框架文件 `EventBindingTransformers.cs` 也直接引用了 `System.Windows.Forms.Control` 类型。

这导致：
- **EventTriggerFramework 无法在非 WinForms 环境中编译**（如 Linux、macOS）
- **职责不清**：WinForms 具体实现与框架抽象混在同一个项目中
- **扩展困难**：新增 UI 平台（WPF、Avalonia 等）需要修改核心框架代码

### 1.2 目标架构

```
┌─────────────────────────────────┐     ┌──────────────────────────────┐
│   EventTriggerFramework (核心)   │     │   WdUI (WinForms 具体实现)    │
│                                 │     │                              │
│   Abstractions/                 │     │   WinForms/                   │
│     ├── IControlAdapter  ← 新增  │◄────┤     ├── WinFormsEventBinder   │
│     ├── IWinFormsSubsystem ← 新增 │◄────┤     ├── WinFormsControlValue  │
│     ├── IEventBinder (已有)      │◄────┤     ├── WinFormsEventBus      │
│     └── IEventNameResolver (已有) │◄────┤     ├── WinFormsInitializer   │
│                                 │     │     ├── ReflectionEventBinder  │
│   EventBinding/                 │     │     └── ... (全部 10 个文件)    │
│     └── Transformers (无 WF 依赖) │     │                              │
│                                 │     │   UI/                         │
│   FrameworkContext.cs           │     │     └── (已有 WinForms 代码)   │
│     └── IWinFormsSubsystem?     │     │                              │
└─────────────────────────────────┘     └──────────────────────────────┘
```

### 1.3 核心原则

> **控制反转（IoC）**：框架不应依赖具体实现，具体实现应依赖框架定义的接口。

---

## 二、当前强依赖分析

### 2.1 依赖全景图

```
EventTriggerFramework/
│
├── FrameworkContext.cs
│   ├── WinFormsAccessor          (object)       ← 已解耦为 object
│   ├── WinFormsNameResolver      (object)       ← 已解耦为 object
│   ├── WinFormsReflectionBinder  (object)       ← 已解耦为 object
│   ├── WinFormsEventBus          (object)       ← 已解耦为 object
│   └── WinFormsEventBinder       (object)       ← 已解耦为 object
│
├── EventTriggerFrameworkInitializer.cs
│   └── InitializeWinForms()
│       └── WinForms.WinFormsInitializer.Initialize(ctx)  ← 已委托
│
├── EventBinding/
│   └── EventBindingTransformers.cs          ★ 核心强依赖
│       ├── using System.Windows.Forms;
│       ├── control as System.Windows.Forms.Control  (第243行)
│       ├── var ctrl = control as System.Windows.Forms.Control  (第254行)
│       ├── ctrl.FindForm()  (第473、543行)
│       ├── ctrl.Tag  (第459、538行)
│       └── WinFormsControlService.Instance.FindByName(form, ...)  (第484、546行)
│
└── WinForms/  (10 个文件，全部依赖 System.Windows.Forms)
    ├── WinFormsEventBinder.cs
    ├── WinFormsControlValueAccessor.cs
    ├── WinFormsEventBus.cs
    ├── WinFormsEventNameResolver.cs
    ├── ReflectionEventBinder.cs
    ├── WinFormsControlFinderTransformer.cs
    ├── WinFormsInitializer.cs
    ├── DefaultEventBindingServiceWinForms.cs
    ├── EventBindingContextExtensions.cs
    └── FrameworkContextWinFormsExtensions.cs
```

### 2.2 4 个强依赖点

| 编号 | 依赖点 | 位置 | 当前代码 | 风险等级 |
|------|--------|------|---------|---------|
| **D1** | `EventBindingTransformers.cs` 直接使用 `Control` 类型 | 第 243、254、348、376、455、535、570 行 | `control as System.Windows.Forms.Control` | 🔴 最高 |
| **D2** | `EventBindingTransformers.cs` 调用 `Control` 实例方法 | 第 473、543 行 | `ctrl.FindForm()` | 🔴 最高 |
| **D3** | `EventBindingTransformers.cs` 访问 `Control.Tag` 属性 | 第 459、538 行 | `ctrl.Tag as Dictionary<...>` | 🔴 最高 |
| **D4** | `EventBindingTransformers.cs` 调用 WinForms 静态服务 | 第 484、546 行 | `WinFormsControlService.Instance.FindByName(form, ...)` | 🔴 最高 |

### 2.3 WinForms/ 目录文件依赖矩阵

| 文件 | 依赖 System.Windows.Forms | 依赖 ETF 接口/类型 | 迁移难度 |
|------|--------------------------|-------------------|---------|
| `WinFormsEventBinder.cs` | ✅ | `IEventBinder`（ETF.Abstractions） | ⭐⭐⭐ |
| `WinFormsControlValueAccessor.cs` | ✅ | 无（只依赖 TConfig） | ⭐ |
| `WinFormsEventBus.cs` | ✅ | 继承 `DefaultEventBusCore`（Share 层） | ⭐⭐ |
| `WinFormsEventNameResolver.cs` | ❌ | `IEventNameResolver`（ETF 内部） | ⭐⭐⭐ |
| `ReflectionEventBinder.cs` | ❌ | 无（纯泛型工具） | ⭐ |
| `WinFormsControlFinderTransformer.cs` | ✅ | `ITransformer`（Modules.Transformers） | ⭐⭐⭐ |
| `WinFormsInitializer.cs` | ✅ | `FrameworkContext`（ETF 核心） | ⭐⭐⭐⭐ |
| `DefaultEventBindingServiceWinForms.cs` | ✅ | 无（纯工具方法） | ⭐ |
| `EventBindingContextExtensions.cs` | ✅ | `EventBindingContext`（ETF 模型） | ⭐⭐ |
| `FrameworkContextWinFormsExtensions.cs` | ✅ | `FrameworkContext`（ETF 核心） | ⭐⭐ |

---

## 三、IoC 倒置方案

### 3.1 新增接口定义

#### 接口 1：`IControlAdapter` — 控件操作抽象

```csharp
// 位置：EventTriggerFramework/Abstractions/IControlAdapter.cs
// 目的：消除 EventBindingTransformers.cs 中对 System.Windows.Forms.Control 的直接依赖

namespace EventTriggerFramework.Abstractions
{
    /// <summary>
    /// 控件适配器接口 — 抽象所有 UI 平台控件的通用操作
    /// </summary>
    public interface IControlAdapter
    {
        /// <summary>获取控件 Tag/附加数据</summary>
        object? GetTag(object control);

        /// <summary>设置控件 Tag/附加数据</summary>
        void SetTag(object control, object? tag);

        /// <summary>查找控件所属的顶级窗口/容器</summary>
        object? FindForm(object control);

        /// <summary>在指定容器中按名称查找子控件</summary>
        object? FindControlByName(object root, string name);

        /// <summary>获取控件文本</summary>
        string? GetText(object control);

        /// <summary>设置控件文本</summary>
        void SetText(object control, string? text);

        /// <summary>通过 IControlValueAccessor 获取控件值</summary>
        object? GetValue(object control, IControlValueAccessor? accessor);

        /// <summary>通过 IControlValueAccessor 设置控件值</summary>
        void SetValue(object control, object? value, IControlValueAccessor? accessor);
    }
}
```

#### 接口 2：`IWinFormsSubsystem` — WinForms 子系统抽象

```csharp
// 位置：EventTriggerFramework/Abstractions/IWinFormsSubsystem.cs
// 目的：替代 FrameworkContext 中 5 个分散的 object 属性

namespace EventTriggerFramework.Abstractions
{
    /// <summary>
    /// WinForms 子系统接口 — 统一管理 WinForms 相关组件的生命周期
    /// </summary>
    public interface IWinFormsSubsystem : IDisposable
    {
        /// <summary>控件适配器</summary>
        IControlAdapter ControlAdapter { get; }

        /// <summary>控件值读写器</summary>
        object WinFormsAccessor { get; }

        /// <summary>事件名称解析器</summary>
        object WinFormsNameResolver { get; }

        /// <summary>反射事件绑定器</summary>
        object WinFormsReflectionBinder { get; }

        /// <summary>事件总线</summary>
        object WinFormsEventBus { get; }

        /// <summary>事件绑定器</summary>
        object WinFormsEventBinder { get; }

        /// <summary>初始化子系统</summary>
        void Initialize(FrameworkContext ctx);
    }
}
```

### 3.2 核心文件修改方案

#### 修改 1：`EventBindingTransformers.cs` — 消除 Control 依赖

**修改策略**：将所有 `System.Windows.Forms.Control` 操作替换为 `IControlAdapter` 调用。

| 原代码 | 替换为 |
|--------|--------|
| `control as System.Windows.Forms.Control` | `_controlAdapter` 不转换，直接使用 `object` |
| `ctrl.FindForm()` | `_controlAdapter.FindForm(ctrl)` |
| `ctrl.Tag as Dictionary<string, object>` | `_controlAdapter.GetTag(ctrl) as Dictionary<string, object>` |
| `ctrl.Tag = tag` | `_controlAdapter.SetTag(ctrl, tag)` |
| `WinFormsControlService.Instance.FindByName(form, name)` | `_controlAdapter.FindControlByName(form, name)` |
| `ctrl.Text` | `_controlAdapter.GetText(ctrl)` |
| `oc.Text = resultText` | `_controlAdapter.SetText(oc, resultText)` |

**注入方式**：通过 `EventBindingContext` 传递 `IControlAdapter` 实例，或通过 `AppServiceProvider` 解析。

```csharp
// EventBindingContext 中新增属性
public IControlAdapter? ControlAdapter { get; set; }
```

#### 修改 2：`FrameworkContext.cs` — 合并 WinForms 属性

**修改策略**：将 5 个分散的 `object` 属性合并为 1 个 `IWinFormsSubsystem?` 属性。

```csharp
// 修改前
public object? WinFormsAccessor { get; internal set; }
public object? WinFormsNameResolver { get; internal set; }
public object? WinFormsReflectionBinder { get; internal set; }
public object? WinFormsEventBus { get; internal set; }
public object? WinFormsEventBinder { get; internal set; }

// 修改后
public IWinFormsSubsystem? WinFormsSubsystem { get; internal set; }
```

**Dispose 方法简化**：
```csharp
// 修改前
WinFormsEventBus?.Dispose();

// 修改后
WinFormsSubsystem?.Dispose();
```

#### 修改 3：`EventTriggerFrameworkInitializer.cs` — 通过 DI 解析

**修改策略**：`InitializeWinForms` 改为通过 `AppServiceProvider` 解析 `IWinFormsSubsystem`。

```csharp
private static void InitializeWinForms(FrameworkContext ctx)
{
    var subsystem = AppServiceProvider.Resolve<IWinFormsSubsystem>();
    if (subsystem != null)
    {
        subsystem.Initialize(ctx);
        ctx.WinFormsSubsystem = subsystem;
    }
}
```

### 3.3 WinForms/ 目录迁移方案

#### 可无依赖迁移的文件（3 个）

| 文件 | 目标位置 | 改动 |
|------|---------|------|
| `WinFormsControlValueAccessor.cs` | `WdUI/WinForms/` | 仅改命名空间 `WdUI.WinForms` |
| `ReflectionEventBinder.cs` | `WdUI/WinForms/` | 仅改命名空间 `WdUI.WinForms` |
| `DefaultEventBindingServiceWinForms.cs` | `WdUI/WinForms/` | 仅改命名空间 `WdUI.WinForms` |

#### 需接口适配后迁移的文件（5 个）

| 文件 | 依赖的 ETF 接口 | 迁移方式 |
|------|----------------|---------|
| `WinFormsEventBinder.cs` | `IEventBinder` | 接口保留在 ETF，实现移到 WdUI |
| `WinFormsEventNameResolver.cs` | `IEventNameResolver` | 接口保留在 ETF，实现移到 WdUI |
| `WinFormsEventBus.cs` | 继承 `DefaultEventBusCore` | 直接迁移（Share 层引用不变） |
| `WinFormsControlFinderTransformer.cs` | `ITransformer` | 接口保留在 Modules，实现移到 WdUI |
| `WinFormsInitializer.cs` | 改为实现 `IWinFormsSubsystem` | 改为 `WdUIWinFormsSubsystem` |

#### 需重写的扩展方法文件（2 个）

| 文件 | 问题 | 解决方案 |
|------|------|---------|
| `EventBindingContextExtensions.cs` | 强依赖 `EventBindingContext` | 移到 WdUI，改为 WdUI 命名空间 |
| `FrameworkContextWinFormsExtensions.cs` | 强依赖 `FrameworkContext` | 移到 WdUI，通过 `WinFormsSubsystem` 属性访问 |

---

## 四、实施步骤

### 第 1 步：在 ETF.Abstractions 中定义接口

**文件**：
- `CsharpPrj/Operation/EventTriggerFramework/Abstractions/IControlAdapter.cs`（新建）
- `CsharpPrj/Operation/EventTriggerFramework/Abstractions/IWinFormsSubsystem.cs`（新建）

**内容**：按 3.1 节定义两个接口。

### 第 2 步：修改 `EventBindingTransformers.cs`

**文件**：`CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingTransformers.cs`

**改动**：
1. 移除 `using System.Windows.Forms`
2. 移除 `using Modules.Common.WinForms`
3. 所有 `Control` 类型操作替换为 `IControlAdapter` 调用
4. 构造函数增加 `IControlAdapter?` 参数
5. `EventBindingContext` 增加 `ControlAdapter` 属性

### 第 3 步：修改 `EventBindingContext`（EventBindingModels.cs）

**文件**：`CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingModels.cs`

**改动**：
1. 移除 `using System.Windows.Forms`
2. `ControlBindingInfo.Control` 类型保持 `object`
3. 新增 `IControlAdapter? ControlAdapter` 属性

### 第 4 步：修改 `FrameworkContext.cs`

**文件**：`CsharpPrj/Operation/EventTriggerFramework/FrameworkContext.cs`

**改动**：
1. 5 个 `object` 属性 → 1 个 `IWinFormsSubsystem?` 属性
2. `Dispose()` 中 `WinFormsEventBus?.Dispose()` → `WinFormsSubsystem?.Dispose()`
3. `ClearReferences()` 中清理 5 个属性 → 清理 1 个属性

### 第 5 步：修改 `EventTriggerFrameworkInitializer.cs`

**文件**：`CsharpPrj/Operation/EventTriggerFramework/EventTriggerFrameworkInitializer.cs`

**改动**：
1. `InitializeWinForms()` 改为通过 `AppServiceProvider.Resolve<IWinFormsSubsystem>()` 解析
2. 移除对 `WinForms.WinFormsInitializer` 的直接调用

### 第 6 步：在 WdUI 中实现 `IControlAdapter`

**文件**：`CsharpPrj/Operation/WdUI/WinForms/WinFormsControlAdapter.cs`（新建）

**内容**：实现 `IControlAdapter` 接口，内部调用 `System.Windows.Forms.Control` 的对应方法。

### 第 7 步：在 WdUI 中实现 `IWinFormsSubsystem`

**文件**：`CsharpPrj/Operation/WdUI/WinForms/WdUIWinFormsSubsystem.cs`（新建）

**内容**：实现 `IWinFormsSubsystem` 接口，合并原 `WinFormsInitializer` 的逻辑。

### 第 8 步：迁移 WinForms/ 目录文件

**操作**：将 `EventTriggerFramework/WinForms/` 下的 10 个文件移动到 `WdUI/WinForms/`。

**命名空间调整**：
- `EventTriggerFramework.WinForms` → `WdUI.WinForms`
- `EventTriggerFramework` → `WdUI`（扩展方法类）
- `EventTriggerFramework.Transformers.EventBinding` → `WdUI.WinForms`（EventBindingContextExtensions）

### 第 9 步：删除 ETF 的 WinForms/ 目录

**操作**：删除 `CsharpPrj/Operation/EventTriggerFramework/WinForms/` 目录。

### 第 10 步：编译验证

**操作**：
1. `dotnet build CsharpPrj/MainProj.sln` 验证无编译错误
2. 检查无循环依赖
3. 运行现有测试确保功能正常

---

## 五、风险与注意事项

### 5.1 循环依赖风险

```
EventTriggerFramework ──→ WdUI（通过 ProjectReference）
WdUI ──→ EventTriggerFramework（通过 ProjectReference）
```

**解决方案**：接口定义在 `EventTriggerFramework.Abstractions` 命名空间中，WdUI 引用 ETF 获取接口，ETF 不引用 WdUI。通过 `AppServiceProvider` 或 DI 容器实现运行时解析。

### 5.2 向后兼容

- `FrameworkContext` 的 WinForms 属性改为 `IWinFormsSubsystem` 后，现有通过 `ctx.WinFormsAccessor` 等属性访问的代码需要更新
- 提供过渡期兼容属性（标记为 `[Obsolete]`）：
  ```csharp
  [Obsolete("请使用 WinFormsSubsystem.WinFormsAccessor")]
  public object? WinFormsAccessor => WinFormsSubsystem?.WinFormsAccessor;
  ```

### 5.3 测试策略

1. 单元测试：`IControlAdapter` 可 mock，`EventBindingTransformers` 的测试无需真实 WinForms 环境
2. 集成测试：WdUI 的 WinForms 实现需要真实的 WinForms 运行时

---

## 六、附录

### 6.1 文件变更清单

| 操作 | 文件路径 | 说明 |
|------|---------|------|
| 🆕 新建 | `EventTriggerFramework/Abstractions/IControlAdapter.cs` | 控件适配器接口 |
| 🆕 新建 | `EventTriggerFramework/Abstractions/IWinFormsSubsystem.cs` | WinForms 子系统接口 |
| ✏️ 修改 | `EventTriggerFramework/EventBinding/EventBindingTransformers.cs` | 消除 Control 依赖 |
| ✏️ 修改 | `EventTriggerFramework/EventBinding/EventBindingModels.cs` | 移除 WinForms using |
| ✏️ 修改 | `EventTriggerFramework/FrameworkContext.cs` | 合并 WinForms 属性 |
| ✏️ 修改 | `EventTriggerFramework/EventTriggerFrameworkInitializer.cs` | DI 解析 WinForms 子系统 |
| 🆕 新建 | `WdUI/WinForms/WinFormsControlAdapter.cs` | IControlAdapter 实现 |
| 🆕 新建 | `WdUI/WinForms/WdUIWinFormsSubsystem.cs` | IWinFormsSubsystem 实现 |
| ➡️ 迁移 | `ETF/WinForms/*.cs` → `WdUI/WinForms/*.cs` | 10 个文件整体迁移 |
| 🗑️ 删除 | `EventTriggerFramework/WinForms/` | 清理空目录 |

### 6.2 接口定义参考

#### `IControlAdapter` 完整定义

```csharp
namespace EventTriggerFramework.Abstractions
{
    public interface IControlAdapter
    {
        object? GetTag(object control);
        void SetTag(object control, object? tag);
        object? FindForm(object control);
        object? FindControlByName(object root, string name);
        string? GetText(object control);
        void SetText(object control, string? text);
        object? GetValue(object control, IControlValueAccessor? accessor);
        void SetValue(object control, object? value, IControlValueAccessor? accessor);
    }
}
```

#### `IWinFormsSubsystem` 完整定义

```csharp
namespace EventTriggerFramework.Abstractions
{
    public interface IWinFormsSubsystem : IDisposable
    {
        IControlAdapter ControlAdapter { get; }
        object WinFormsAccessor { get; }
        object WinFormsNameResolver { get; }
        object WinFormsReflectionBinder { get; }
        object WinFormsEventBus { get; }
        object WinFormsEventBinder { get; }
        void Initialize(FrameworkContext ctx);
    }
}
```

### 6.3 `EventBindingTransformers.cs` 关键修改对照

| 行号 | 原代码 | 新代码 |
|------|--------|--------|
| 18 | `using System.Windows.Forms;` | （删除） |
| 21 | `using Modules.Common.WinForms;` | （删除） |
| 147 | `if (control is not Control winFormsControl)` | `if (_controlAdapter == null) return;` |
| 148-167 | `winFormsControl.Tag` 操作 | `_controlAdapter.GetTag/SetTag` |
| 243 | `control as System.Windows.Forms.Control` | `input.CurrentControl`（直接使用 object） |
| 254 | `var ctrl = control as System.Windows.Forms.Control` | `var ctrl = input.CurrentControl` |
| 348 | `Control ctrl` 参数类型 | `object ctrl` |
| 376 | `Control control` 参数类型 | `object control` |
| 455 | `Control ctrl` 参数类型 | `object ctrl` |
| 459 | `ctrl.Tag is not Dictionary...` | `_controlAdapter.GetTag(ctrl) is not Dictionary...` |
| 473 | `var form = ctrl.FindForm()` | `var form = _controlAdapter.FindForm(ctrl)` |
| 484 | `WinFormsControlService.Instance.FindByName(form, ...)` | `_controlAdapter.FindControlByName(form, ...)` |
| 535 | `Control ctrl` 参数类型 | `object ctrl` |
| 538 | `ctrl.Tag is Dictionary...` | `_controlAdapter.GetTag(ctrl) is Dictionary...` |
| 543 | `var form = ctrl.FindForm()` | `var form = _controlAdapter.FindForm(ctrl)` |
| 546 | `WinFormsControlService.Instance.FindByName(form, ...)` | `_controlAdapter.FindControlByName(form, ...)` |
| 570 | `object control` + `Control ctrl` | `object control`（直接使用） |
| 579 | `if (control is Control ctrl)` | `if (_controlAdapter != null)` |
| 581 | `return ctrl.Text` | `return _controlAdapter.GetText(control)` |
