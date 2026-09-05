# WdUI 通用逻辑提取方案

> **目标**：识别 WdUI 中可提取为通用逻辑的模块，逐步迁移到 `BasicMethod` 共享库中，减少重复代码，提高复用性。

---

## 一、提取原则

| 优先级 | 条件 | 示例 |
|--------|------|------|
| 🟢 **高** | 无 WinForms 依赖 / 纯算法 | `AsyncUiHelper.FireAndForget`、`ColorExtensions` |
| 🟡 **中** | 有 WinForms 依赖但通用性强 | `AsyncFormBase`、`ControlExtensions` |
| 🔵 **低** | 有 WinForms 依赖且通用性一般 | `WdUIEvents` 门面封装 |
| ⚪ **不提取** | 强依赖 WdUI 业务上下文 | `AsyncLoadManager`、`DataBindingManager` |

---

## 二、候选提取模块

### P1 🟢 `AsyncUiHelper` → `BasicMethod.Common`

**当前位置**：`WdUI.UI.AsyncUiHelper`（81 行）

**内容**：

| 方法 | WinForms 依赖 | 可提取性 |
|------|--------------|---------|
| `FireAndForget(Func<Task>, Action<Exception>?)` | ❌ 无 | ✅ 完全通用 |
| `RunOnUiThread(Control, Func<Task>)` | ⚠️ `Control.InvokeRequired` / `BeginInvoke` | ✅ 可改为 `ISynchronizeInvoke` 接口 |
| `ExecuteOnUiThread(Control, Action)` | ⚠️ `Control.Invoke` | ✅ 可改为 `ISynchronizeInvoke` 接口 |

**提取方案**：

```csharp
// === BasicMethod/Common/Async/AsyncHelper.cs ===
namespace BasicMethod.Common.Async
{
    public static class AsyncHelper
    {
        // 完全通用
        public static async void FireAndForget(
            Func<Task> asyncAction,
            Action<Exception>? onError = null)
        { ... }

        // 依赖 ISynchronizeInvoke（Control 实现了此接口）
        public static Task RunOnUiThread(
            ISynchronizeInvoke syncObject,
            Func<Task> asyncAction)
        { ... }

        public static void ExecuteOnUiThread(
            ISynchronizeInvoke syncObject,
            Action action)
        { ... }
    }
}
```

**WdUI 端改造**：
```csharp
// WdUI.UI.AsyncUiHelper → 改为委托
using AsyncHelper = BasicMethod.Common.Async.AsyncHelper;
```

---

### P2 🟢 `ColorExtensions` → `BasicMethod.Common`

**当前位置**：`WdUI.UI.Utilities.ColorExtensions`（37 行）

**内容**：

| 方法 | 依赖 | 可提取性 |
|------|------|---------|
| `FromHex(string)` | `System.Drawing.ColorTranslator` | ✅ 完全通用 |
| `ToHex(Color)` | `System.Drawing.Color` | ✅ 完全通用 |
| `Darken(Color, double)` | `System.Drawing.Color` | ✅ 完全通用 |
| `Lighten(Color, double)` | `System.Drawing.Color` | ✅ 完全通用 |

**提取方案**：

```csharp
// === BasicMethod/Common/Drawing/ColorExtensions.cs ===
namespace BasicMethod.Common.Drawing
{
    public static class ColorExtensions
    {
        public static Color FromHex(string hex) { ... }
        public static string ToHex(this Color color) { ... }
        public static Color Darken(this Color color, double percent) { ... }
        public static Color Lighten(this Color color, double percent) { ... }
    }
}
```

---

### P3 🟡 `AsyncFormBase` → `BasicMethod.Common` (WinForms 部分)

**当前位置**：`WdUI.UI.AsyncFormBase`（136 行）

**内容**：异步 Form 基类，提供：
- `OnLoad` 事件触发异步初始化
- `FormClosingToken` 取消令牌
- `OnInitializeAsync()` 虚方法
- `OnInitialized()` / `OnInitializeFailed()` 回调

**提取方案**：

```csharp
// === BasicMethod/Common/WinForms/AsyncFormBase.cs ===
namespace BasicMethod.Common.WinForms
{
    [SupportedOSPlatform("windows")]
    public abstract class AsyncFormBase : Form
    {
        // 完整保留现有实现
    }
}
```

**WdUI 端改造**：
```csharp
// WdUI.UI.AsyncFormBase → 改为继承
public abstract class AsyncFormBase : BasicMethod.Common.WinForms.AsyncFormBase
{
    // WdUI 特定的扩展（如有）
}
```

---

### P4 🟡 `ControlExtensions`（部分）→ `BasicMethod.Common`

**当前位置**：`WdUI.UI.Utilities.ControlExtensions`（部分方法）

**可提取方法**：

| 方法 | 说明 |
|------|------|
| `EnableDoubleBuffering(Control)` | 反射设置 DoubleBuffered |
| `SuspendAllLayout(Control)` | 递归挂起布局 |
| `ResumeAllLayout(Control, bool)` | 递归恢复布局 |
| `SetBoundsRelative(Control, Rectangle)` | 相对父容器设置位置 |

**提取方案**：

```csharp
// === BasicMethod/Common/WinForms/ControlExtensions.cs ===
namespace BasicMethod.Common.WinForms
{
    public static class ControlExtensions
    {
        public static void EnableDoubleBuffering(this Control control) { ... }
        public static void SuspendAllLayout(this Control control) { ... }
        public static void ResumeAllLayout(this Control control, bool performLayout = true) { ... }
        public static void SetBoundsRelative(this Control control, Rectangle relativeBounds) { ... }
    }
}
```

---

### P5 🔵 `WdUIEvents` → 通用事件总线门面

**当前位置**：`WdUI.UI.WdUIEvents`（40 行）

**内容**：对 `IEventBus` 的静态门面封装

**分析**：`IEventBus` 已在 `UniversalJsonMiddleware.Abstractions` 中定义。门面模式本身是应用层模式，提取价值有限。但如果多个项目都需要类似的类型安全封装，可以提取。

---

## 三、已提取的通用框架模块（Framework/ 目录）

> 以下模块已从 `UI/WdUI.*.cs` 中提取为独立的通用框架，位于 `Framework/` 目录下。
> 这些框架模块**不修改已有代码**，仅提供更清晰的抽象层供新代码使用。

### ✅ F1 `FrameworkModule` — 框架生命周期管理

| 项目 | 说明 |
|------|------|
| **源文件** | `UI/WdUI.Framework.cs` → `Framework/FrameworkModule.cs` |
| **命名空间** | `WdUI.Framework` |
| **内容** | 框架状态枚举、生命周期管理、日志抽象、模块注册与发现 |
| **与原代码关系** | 原 `WdUI.UI.Framework.FrameworkModule` 保留不变，新 `WdUI.Framework.FrameworkModule` 为独立实现 |

### ✅ F2 `LayoutFramework` — 布局管理框架

| 项目 | 说明 |
|------|------|
| **源文件** | `UI/WdUI.Layout.cs` → `Framework/LayoutFramework.cs` |
| **命名空间** | `WdUI.Framework.Layout` |
| **内容** | `ILayoutEngine` 布局引擎接口、`LayoutContext` 布局上下文、`LayoutPipeline` 布局管线、`LayoutStage` 布局阶段基类 |
| **与原代码关系** | 原 `WdUI.UI.Layout` 中的逻辑保持不变，新框架提供更清晰的接口抽象 |

### ✅ F3 `ControlFactoryFramework` — 控件工厂框架

| 项目 | 说明 |
|------|------|
| **源文件** | `UI/WdUI.Framework.cs`(ControlFactory) → `Framework/ControlFactoryFramework.cs` |
| **命名空间** | `WdUI.Framework.Controls` |
| **内容** | `IControlFactory` 接口、`ControlCreationContext` 控件创建上下文、`ControlBuilder` 控件构建器 |
| **与原代码关系** | 原 `WdUI.UI.Framework.ControlFactory` 保留不变，新框架提供 Builder 模式 |

### ✅ F4 `ViewManagerFramework` — 视图管理框架

| 项目 | 说明 |
|------|------|
| **源文件** | `UI/WdUI.ViewManager.cs` → `Framework/ViewManagerFramework.cs` |
| **命名空间** | `WdUI.Framework.Views` |
| **内容** | `IViewManager` 接口、`ViewNavigationEventArgs` 导航事件参数、`ViewManagerBase` 视图管理器基类 |
| **与原代码关系** | 原 `WdUI.UI.ViewManager` 保留不变，新框架提供基类 |

### ✅ F5 `ConfigurationFramework` — 配置管理框架

| 项目 | 说明 |
|------|------|
| **源文件** | `UI/WdUI.Configuration.cs` → `Framework/ConfigurationFramework.cs` |
| **命名空间** | `WdUI.Framework.Configuration` |
| **内容** | `IConfigurationService` 配置服务接口、`ConfigurationLoadContext` 配置加载上下文、`ConfigurationWatcher` 配置监视器 |
| **与原代码关系** | 原 `WdUI.UI.Configuration` 保留不变，新框架提供更丰富的配置管理能力 |

### ✅ F6 `EventFramework` — 事件管理框架

| 项目 | 说明 |
|------|------|
| **源文件** | `UI/WdUI.Events.cs` → `Framework/EventFramework.cs` |
| **命名空间** | `WdUI.Framework.Events` |
| **内容** | `IEventBinder` 事件绑定器接口、`EventManager` 事件管理器、`EventRouter` 事件路由 |
| **与原代码关系** | 原 `WdUI.UI.Events` 保留不变，新框架提供完整的事件管理抽象 |

---

## 五、不建议提取的模块

| 模块 | 原因 |
|------|------|
| `AsyncLoadManager` | 强依赖 `ConfigContainer`，业务逻辑太具体 |
| `DataBindingManager` | 强依赖 WinForms `Control`，且实现较粗糙 |
| `UILayoutManager` | 强依赖 WinForms + UniversalLayoutMath |
| `ControlFactory` | 强依赖 WinForms + 配置模型 |
| `ConfigPathManager` | 已委托到 `PathManager.ProjectDirectories` |
| `LayoutApplier` / `LayoutMediator` | 强依赖 WinForms + UniversalLayoutMath |
| `OverlapDetection` | 仅定义了一个结果类，逻辑在外部 |

---

## 六、实施计划

```
迭代 1 (P1) ─── AsyncHelper 提取 ─── 2-3 小时
  1. 在 BasicMethod.Common 中创建 Async/AsyncHelper.cs
  2. 迁移 FireAndForget（完全通用）
  3. 迁移 RunOnUiThread/ExecuteOnUiThread（改为 ISynchronizeInvoke）
  4. WdUI 端改为 using 别名委托
  5. 编译验证

迭代 2 (P2) ─── ColorExtensions 提取 ─── 1 小时
  1. 在 BasicMethod.Common 中创建 Drawing/ColorExtensions.cs
  2. 迁移所有方法
  3. WdUI 端改为 using 别名委托
  4. 编译验证

迭代 3 (P3) ─── AsyncFormBase 提取 ─── 1-2 小时
  1. 在 BasicMethod.Common 中创建 WinForms/AsyncFormBase.cs
  2. 迁移基类
  3. WdUI 端改为继承共享基类
  4. 编译验证

迭代 4 (P4) ─── ControlExtensions 提取 ─── 1 小时
  1. 在 BasicMethod.Common 中创建 WinForms/ControlExtensions.cs
  2. 迁移 EnableDoubleBuffering/SuspendAllLayout/ResumeAllLayout
  3. WdUI 端改为 using 别名委托
  4. 编译验证
```

---

## 七、依赖关系图

```
WdUI (WinForms UI 框架)
  ├── AsyncHelper ← BasicMethod.Common (无依赖)
  ├── ColorExtensions ← BasicMethod.Common (仅 System.Drawing)
  ├── AsyncFormBase ← BasicMethod.Common.WinForms (依赖 WinForms)
  ├── ControlExtensions ← BasicMethod.Common.WinForms (依赖 WinForms)
  ├── ConfigurationModels ← BasicMethod.ConfigurationModels (已完成 ✅)
  ├── Pipeline ← BasicMethod.Pipeline (已完成 ✅)
  ├── PathManager ← BasicMethod.PathManager (已完成 ✅)
  ├── UniversalLayoutMath ← BasicMethod.UniversalLayout (外部引用)
  └── UniversalJsonMiddleware ← BasicMethod.UniversalJsonMiddleware (外部引用)
```

---

## 八、收益评估

| 模块 | 消除代码行数 | 复用项目数 | 维护成本降低 |
|------|------------|-----------|------------|
| AsyncHelper | ~80 行 | 所有项目 | 🔴 高（异步安全是通用需求） |
| ColorExtensions | ~40 行 | 所有项目 | 🟡 中 |
| AsyncFormBase | ~140 行 | WinForms 项目 | 🔴 高（每个 WinForms 项目都需要） |
| ControlExtensions | ~40 行 | WinForms 项目 | 🟡 中 |
| **合计** | **~300 行** | | |

---

> **文档版本**：v1.0
> **创建日期**：2026-05-20
> **关联文档**：[重复逻辑分析与重构方案.md](./重复逻辑分析与重构方案.md)
