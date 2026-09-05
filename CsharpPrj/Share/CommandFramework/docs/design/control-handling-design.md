# CommandFramework — 通用控件处理逻辑设计方案

> 版本：v1.0
> 日期：2026-06-15
> 状态：待实施

---

## 一、背景与动机

### 1.1 当前问题

在 WorkFlowPrj 项目中，控件查找和控件值读写逻辑**分散在 5 处**，存在严重的重复和功能不一致：

| # | 位置 | 实现方式 | 功能覆盖度 |
|---|------|---------|:---------:|
| 1 | `FileDrivenExecutor.FindControlByName` | 手写递归 `parent.Controls` | ⭐⭐⭐ |
| 2 | `ControlFinder` (Operation/WdUI) | 基于 `TreeWalker<Control>` 框架 | ⭐⭐⭐⭐⭐ |
| 3 | `WinFormsControlFinder` (Share/FrameworkAbstractions) | 基于 `Controls.Find(name, true)` | ⭐⭐ |
| 4 | `CommandStageBase.FindControlReflection` (CommandFramework) | 反射调用 `Controls.Find` | ⭐ |
| 5 | `WinFormsEventTrigger.FindControl` (CommandFramework) | 手写递归 `parent.Controls` | ⭐⭐ |

### 1.2 根本原因

- 缺乏**统一的控件查找接口**，各模块各自实现
- 缺乏**统一的控件值写入接口**，各模块各自处理控件类型分支
- `CommandFramework` 作为命令执行框架，其 `CommandStageBase` 和 `WinFormsEventTrigger` 中的控件查找逻辑过于简陋（不支持 SplitContainer/TabControl 等特殊容器）
- WdUI 的 `ControlFinder` 功能最完善，但位于 `Operation` 层，无法被 `Share` 层的 `CommandFramework` 引用

### 1.3 设计目标

| 目标 | 说明 |
|------|------|
| **统一接口** | 定义 `IControlFinder` 和 `IControlValueWriter` 接口，作为控件处理的统一契约 |
| **功能完备** | 新实现必须覆盖现有 5 处逻辑的**所有功能** |
| **可扩展** | 支持注册式适配器，便于扩展新的容器类型和控件类型 |
| **可诊断** | 内置诊断日志，便于排查控件查找失败问题 |
| **零侵入** | 现有代码逐步迁移，不破坏已有功能 |

---

## 二、现有功能覆盖分析

### 2.1 控件查找功能矩阵

| 功能 | FileDrivenExecutor | ControlFinder (WdUI) | WinFormsControlFinder | CommandStageBase | WinFormsEventTrigger |
|------|:---:|:---:|:---:|:---:|:---:|
| 按名称递归查找 | ✅ | ✅ | ✅ | ✅ | ✅ |
| 按类型查找 | ❌ | ✅ | ❌ | ❌ | ❌ |
| 按谓词查找 | ❌ | ✅ | ❌ | ❌ | ❌ |
| 跨 Form 查找 | ❌ | ✅ | ✅ | ❌ | ❌ |
| SplitContainer 支持 | ✅ | ✅ (TreeWalker) | ❌ | ❌ | ❌ |
| TabControl 支持 | ✅ | ✅ (TreeWalker) | ❌ | ❌ | ❌ |
| 可配置递归深度 | ❌ | ✅ (RecursionPolicy) | ❌ | ❌ | ❌ |
| O(1) ControlIndex | ❌ | ✅ | ❌ | ❌ | ❌ |
| 查找父 Form | ❌ | ✅ (FindForm) | ❌ | ❌ | ❌ |
| 查找 ConfigContainer 祖先 | ❌ | ✅ | ❌ | ❌ | ❌ |
| 诊断日志 | ✅ | ❌ | ❌ | ❌ | ❌ |

### 2.2 控件值写入功能矩阵

| 控件类型 | FileDrivenExecutor.WriteResultToControl | CommandStageBase.SetControlTextReflection | WinFormsEventTrigger.SetControlTextAndTriggerEvent |
|----------|:---:|:---:|:---:|
| Label | ✅ | ✅ (反射) | ✅ |
| TextBox | ✅ | ✅ (反射) | ✅ |
| RichTextBox | ✅ | ✅ (反射) | ❌ |
| ListView | ✅ | ❌ | ❌ |
| ComboBox | ✅ | ✅ (反射) | ✅ |
| CheckBox | ✅ | ✅ (反射) | ✅ |
| RadioButton | ✅ | ✅ (反射) | ✅ |
| DateTimePicker | ✅ | ✅ (反射) | ✅ |
| NumericUpDown | ✅ | ✅ (反射) | ✅ |
| 诊断日志 | ✅ | ❌ | ❌ |

---

## 三、接口定义

### 3.1 `IControlFinder` — 通用控件查找器

```csharp
namespace CommandFramework.Abstractions;

/// <summary>
/// 通用控件查找器接口
/// 提供按名称、类型、谓词查找控件的统一契约
/// 支持跨 Form 查找、可配置递归策略、诊断日志
/// </summary>
public interface IControlFinder
{
    // ================================================================
    // 按名称查找
    // ================================================================

    /// <summary>
    /// 在指定根控件下按名称递归查找控件
    /// </summary>
    /// <param name="root">根控件（Form 或容器控件）</param>
    /// <param name="name">控件名称</param>
    /// <returns>找到的控件，未找到返回 null</returns>
    object? FindByName(object root, string name);

    /// <summary>
    /// 跨所有打开的 Form 按名称查找控件
    /// </summary>
    /// <param name="name">控件名称</param>
    /// <returns>找到的控件，未找到返回 null</returns>
    object? FindByName(string name);

    // ================================================================
    // 按类型查找
    // ================================================================

    /// <summary>
    /// 在指定根控件下按类型查找第一个匹配的控件
    /// </summary>
    /// <typeparam name="T">控件类型</typeparam>
    /// <param name="root">根控件</param>
    /// <returns>找到的控件，未找到返回 null</returns>
    T? FindByType<T>(object root) where T : class;

    /// <summary>
    /// 在指定根控件下按类型查找所有匹配的控件
    /// </summary>
    /// <typeparam name="T">控件类型</typeparam>
    /// <param name="root">根控件</param>
    /// <returns>匹配的控件列表</returns>
    List<T> FindAllByType<T>(object root) where T : class;

    // ================================================================
    // 按谓词查找
    // ================================================================

    /// <summary>
    /// 在指定根控件下按谓词查找第一个匹配的控件
    /// </summary>
    /// <param name="root">根控件</param>
    /// <param name="predicate">匹配谓词</param>
    /// <returns>找到的控件，未找到返回 null</returns>
    object? Find(object root, Func<object, bool> predicate);

    /// <summary>
    /// 在指定根控件下按谓词查找所有匹配的控件
    /// </summary>
    /// <param name="root">根控件</param>
    /// <param name="predicate">匹配谓词</param>
    /// <returns>匹配的控件列表</returns>
    List<object> FindAll(object root, Func<object, bool> predicate);

    // ================================================================
    // 跨 Form 查找
    // ================================================================

    /// <summary>
    /// 在指定名称的 Form 中查找控件
    /// </summary>
    /// <param name="formName">Form 名称</param>
    /// <param name="controlName">控件名称</param>
    /// <returns>找到的控件，未找到返回 null</returns>
    object? FindInForm(string formName, string controlName);

    // ================================================================
    // 工具方法
    // ================================================================

    /// <summary>
    /// 查找控件所属的顶级 Form
    /// </summary>
    /// <param name="control">控件</param>
    /// <returns>所属 Form，未找到返回 null</returns>
    object? FindForm(object control);

    /// <summary>
    /// 判断控件是否是指定祖先的后代
    /// </summary>
    /// <param name="control">控件</param>
    /// <param name="ancestor">祖先控件</param>
    /// <returns>如果是后代返回 true</returns>
    bool IsDescendantOf(object control, object ancestor);

    /// <summary>
    /// 获取控件在控件树中的深度
    /// </summary>
    /// <param name="control">控件</param>
    /// <returns>深度（根为 0）</returns>
    int GetDepth(object control);
}
```

### 3.2 `IControlValueWriter` — 通用控件值写入器

```csharp
namespace CommandFramework.Abstractions;

/// <summary>
/// 通用控件值写入器接口
/// 统一写入不同类型控件的值，支持注册式适配器
/// </summary>
public interface IControlValueWriter
{
    /// <summary>
    /// 写入控件值
    /// </summary>
    /// <param name="control">目标控件</param>
    /// <param name="value">要写入的值</param>
    /// <returns>是否成功写入</returns>
    bool WriteValue(object control, string? value);

    /// <summary>
    /// 读取控件值
    /// </summary>
    /// <param name="control">目标控件</param>
    /// <returns>读取的值，失败返回 null</returns>
    string? ReadValue(object control);

    /// <summary>
    /// 判断是否支持指定的控件类型
    /// </summary>
    /// <param name="control">目标控件</param>
    /// <returns>如果支持返回 true</returns>
    bool SupportsType(object control);

    /// <summary>
    /// 注册控件适配器
    /// </summary>
    /// <typeparam name="TControl">控件类型</typeparam>
    /// <param name="writer">写入委托 (control, value) → bool</param>
    /// <param name="reader">读取委托 (control) → string?</param>
    void RegisterAdapter<TControl>(
        Func<TControl, string?, bool> writer,
        Func<TControl, string?> reader) where TControl : class;
}
```

### 3.3 `IControlFinderOptions` — 控件查找器配置

```csharp
namespace CommandFramework.Abstractions;

/// <summary>
/// 控件查找器配置选项
/// </summary>
public class ControlFinderOptions
{
    /// <summary>最大递归深度（默认 10，-1 表示无限制）</summary>
    public int MaxDepth { get; set; } = 10;

    /// <summary>是否启用诊断日志（默认 false）</summary>
    public bool EnableDiagnostics { get; set; } = false;

    /// <summary>诊断日志输出回调</summary>
    public Action<string>? DiagnosticOutput { get; set; }

    /// <summary>是否跨 Application.OpenForms 查找（默认 true）</summary>
    public bool SearchOpenForms { get; set; } = true;

    /// <summary>是否启用 ControlIndex 缓存（默认 false）</summary>
    public bool EnableControlIndex { get; set; } = false;
}
```

---

## 四、实现方案

### 4.1 新增文件清单

| 文件 | 类型 | 说明 |
|------|------|------|
| `Abstractions/IControlFinder.cs` | 新增 | 控件查找器接口 |
| `Abstractions/IControlValueWriter.cs` | 新增 | 控件值读写器接口 |
| `Abstractions/ControlFinderOptions.cs` | 新增 | 控件查找器配置选项 |
| `Execution/WinFormsControlFinder.cs` | 新增 | WinForms 控件查找器实现（增强版） |
| `Execution/WinFormsControlValueWriter.cs` | 新增 | WinForms 控件值读写器实现 |

### 4.2 `WinFormsControlFinder` 设计

**核心设计原则：** 整合 WdUI 中 `ControlFinder` 的 TreeWalker 能力 + 现有 `WinFormsControlFinder` 的跨 Form 能力 + `FileDrivenExecutor.FindControlByName` 的特殊容器支持 + 诊断日志。

**关键实现细节：**

```csharp
namespace CommandFramework.Execution;

/// <summary>
/// WinForms 控件查找器（增强版）
/// 整合 TreeWalker 框架，支持所有容器类型和可配置递归策略
/// </summary>
public class WinFormsControlFinder : IControlFinder, IDisposable
{
    private readonly ControlFinderOptions _options;
    private readonly ConcurrentDictionary<string, Control>? _controlIndex;

    public WinFormsControlFinder(ControlFinderOptions? options = null)
    {
        _options = options ?? new ControlFinderOptions();
        if (_options.EnableControlIndex)
            _controlIndex = new ConcurrentDictionary<string, Control>(StringComparer.OrdinalIgnoreCase);
    }

    // ---- 核心递归遍历 ----

    /// <summary>
    /// 递归遍历控件树，支持 SplitContainer / TabControl / Panel 等所有容器类型
    /// </summary>
    private Control? FindControlRecursive(Control parent, string name, int depth)
    {
        if (depth > _options.MaxDepth && _options.MaxDepth != -1)
            return null;

        // 1. 检查当前控件
        if (string.Equals(parent.Name, name, StringComparison.OrdinalIgnoreCase))
            return parent;

        // 2. 检查 Controls 集合
        foreach (Control child in parent.Controls)
        {
            var found = FindControlRecursive(child, name, depth + 1);
            if (found != null) return found;
        }

        // 3. 特殊容器处理
        if (parent is SplitContainer splitter)
        {
            var found = FindControlRecursive(splitter.Panel1, name, depth + 1);
            if (found == null)
                found = FindControlRecursive(splitter.Panel2, name, depth + 1);
            return found;
        }

        if (parent is TabControl tabControl)
        {
            foreach (TabPage page in tabControl.TabPages)
            {
                var found = FindControlRecursive(page, name, depth + 1);
                if (found != null) return found;
            }
        }

        // 其他容器类型（GroupBox, Panel, FlowLayoutPanel, TableLayoutPanel 等）
        // 已通过 parent.Controls 覆盖

        return null;
    }

    // ---- 跨 Form 查找 ----

    private IEnumerable<Form> GetOpenForms()
    {
        return Application.OpenForms.Cast<Form>();
    }

    // ---- 诊断日志 ----

    private void LogDiagnostic(string message)
    {
        if (_options.EnableDiagnostics)
            _options.DiagnosticOutput?.Invoke(message);
    }

    public void Dispose()
    {
        _controlIndex?.Clear();
    }
}
```

### 4.3 `WinFormsControlValueWriter` 设计

```csharp
namespace CommandFramework.Execution;

/// <summary>
/// WinForms 控件值读写器
/// 支持 Label、TextBox、ListView、RichTextBox、ComboBox、CheckBox、
/// RadioButton、DateTimePicker、NumericUpDown 等控件类型
/// </summary>
public class WinFormsControlValueWriter : IControlValueWriter
{
    private readonly Dictionary<Type, (Func<object, string?, bool> Writer, Func<object, string?> Reader)> _adapters;

    public WinFormsControlValueWriter()
    {
        _adapters = new Dictionary<Type, (Func<object, string?, bool>, Func<object, string?>)>();
        RegisterDefaultAdapters();
    }

    private void RegisterDefaultAdapters()
    {
        // Label
        RegisterAdapter<Label>(
            (ctrl, val) => { ctrl.Text = val ?? ""; return true; },
            ctrl => ctrl.Text);

        // TextBox
        RegisterAdapter<TextBox>(
            (ctrl, val) => { ctrl.Text = val ?? ""; return true; },
            ctrl => ctrl.Text);

        // RichTextBox
        RegisterAdapter<RichTextBox>(
            (ctrl, val) => { ctrl.Text = val ?? ""; return true; },
            ctrl => ctrl.Text);

        // ComboBox
        RegisterAdapter<ComboBox>(
            (ctrl, val) => { ctrl.SelectedItem = val; return true; },
            ctrl => ctrl.SelectedItem?.ToString());

        // CheckBox
        RegisterAdapter<CheckBox>(
            (ctrl, val) => { ctrl.Checked = bool.TryParse(val, out var b) && b; return true; },
            ctrl => ctrl.Checked.ToString());

        // RadioButton
        RegisterAdapter<RadioButton>(
            (ctrl, val) => { ctrl.Checked = bool.TryParse(val, out var b) && b; return true; },
            ctrl => ctrl.Checked.ToString());

        // DateTimePicker
        RegisterAdapter<DateTimePicker>(
            (ctrl, val) =>
            {
                if (DateTime.TryParse(val, out var dt))
                { ctrl.Value = dt; return true; }
                return false;
            },
            ctrl => ctrl.Value.ToString("yyyy-MM-dd HH:mm:ss"));

        // NumericUpDown
        RegisterAdapter<NumericUpDown>(
            (ctrl, val) =>
            {
                if (decimal.TryParse(val, out var d))
                { ctrl.Value = d; return true; }
                return false;
            },
            ctrl => ctrl.Value.ToString());

        // ListView
        RegisterAdapter<ListView>(
            (ctrl, val) =>
            {
                if (val != null)
                {
                    ctrl.Items.Add(val);
                    return true;
                }
                return false;
            },
            ctrl =>
            {
                if (ctrl.SelectedItems.Count > 0)
                    return ctrl.SelectedItems[0].Text;
                return null;
            });
    }

    public bool WriteValue(object control, string? value)
    {
        foreach (var kvp in _adapters)
        {
            if (kvp.Key.IsInstanceOfType(control))
                return kvp.Value.Writer(control, value);
        }
        return false;
    }

    public string? ReadValue(object control)
    {
        foreach (var kvp in _adapters)
        {
            if (kvp.Key.IsInstanceOfType(control))
                return kvp.Value.Reader(control);
        }
        return null;
    }

    public bool SupportsType(object control)
    {
        return _adapters.Keys.Any(t => t.IsInstanceOfType(control));
    }

    public void RegisterAdapter<TControl>(
        Func<TControl, string?, bool> writer,
        Func<TControl, string?> reader) where TControl : class
    {
        _adapters[typeof(TControl)] = (
            (obj, val) => writer((TControl)obj, val),
            (obj) => reader((TControl)obj));
    }
}
```

---

## 五、修改现有文件

### 5.1 修改 `CommandStageBase`（`Pipeline/PipelineStageBase.cs`）

- 将 `FindControlReflection` 改为委托给 `IControlFinder`
- 将 `SetControlTextReflection` / `GetControlTextReflection` 改为委托给 `IControlValueWriter`
- 新增 `IControlFinder` 和 `IControlValueWriter` 的依赖解析

### 5.2 修改 `ExecuteCommandStage`（`Pipeline/Stages/ExecuteCommandStage.cs`）

- `WriteResult` 方法改为委托给 `IControlFinder` + `IControlValueWriter`
- `HandleSet` 方法改为委托给 `IControlFinder` + `IWinFormsEventTrigger`

### 5.3 修改 `WinFormsEventTrigger`（`Execution/WinFormsEventTrigger.cs`）

- `FindControl` 方法改为委托给 `IControlFinder`

### 5.4 修改 `CommandFrameworkHost`（`CommandFrameworkHost.cs`）

- 新增 `IControlFinder` 和 `IControlValueWriter` 的初始化和属性暴露

### 5.5 修改 `CommandFrameworkOptions`（`CommandFrameworkOptions.cs`）

- 新增 `EnableControlFinder` 和 `EnableControlValueWriter` 开关
- 新增 `ControlFinder` 和 `ControlValueWriter` 注入属性

---

## 六、后续应用替换（第二阶段）

待 CommandFramework 的通用逻辑完善后，逐步替换现有 5 处分散逻辑：

| 现有逻辑 | 替换方式 | 优先级 |
|----------|---------|:------:|
| `FileDrivenExecutor.FindControlByName` | 委托给 `IControlFinder` | 高 |
| `FileDrivenExecutor.WriteResultToControl` | 委托给 `IControlValueWriter` | 高 |
| `CommandStageBase.FindControlReflection` | 委托给 `IControlFinder` | 高 |
| `CommandStageBase.SetControlTextReflection` | 委托给 `IControlValueWriter` | 高 |
| `WinFormsEventTrigger.FindControl` | 委托给 `IControlFinder` | 中 |
| `WinFormsControlFinder` (FrameworkAbstractions) | 标记为过时，建议使用 CommandFramework 实现 | 低 |
| `ControlFinder` (WdUI) | 保留为 WdUI 框架内的便捷封装，内部委托给 CommandFramework 实现 | 低 |

---

## 七、与现有架构的关系

### 7.1 职责边界

```
CommandFramework（通用命令执行框架）
├── ICommandParser              ← 命令解析
├── ICommandEngine              ← 命令执行
├── IControlFinder              ← 控件查找（新增）
├── IControlValueWriter         ← 控件值写入（新增）
├── IControlValueAccessor       ← 控件值读写（已有，适配器模式）
├── IStateStore                 ← 状态存储
├── IFunctionRegistry           ← 函数执行
└── IEnvironmentAdapter         ← 环境适配
```

### 7.2 `IControlValueWriter` vs `IControlValueAccessor`

| 维度 | `IControlValueWriter` | `IControlValueAccessor` |
|------|----------------------|------------------------|
| 定位 | 简单值读写（string 级别） | 通用值读写（object 级别） |
| 返回值 | `string?` | `object?` / `TResult?` |
| 适配器注册 | `RegisterAdapter<T>(writer, reader)` | `RegisterAdapter<T>(getter, setter)` |
| 适用场景 | 控件值写入/读取（WriteResultStage） | 通用值读写（适配器模式） |
| 关系 | 可作为 `IControlValueAccessor` 的简化封装 | 更通用的接口 |

### 7.3 与 Share/LayoutAnalysis 的关系

`Share/LayoutAnalysis` 提供控件树遍历能力（`UIInspector` / `ControlTreeWalker`），主要用于**布局分析和调试**。`IControlFinder` 专注于**运行时控件查找**，两者职责不同：

- `LayoutAnalysis` → 分析控件树结构、收集布局数据
- `IControlFinder` → 按名称/类型/谓词查找控件，支持命令执行

---

## 八、实施步骤

### 第一阶段：完善 CommandFramework（当前阶段）

| 步骤 | 内容 | 涉及文件 |
|------|------|---------|
| 1 | 定义 `IControlFinder` 接口 | `Abstractions/IControlFinder.cs` |
| 2 | 定义 `IControlValueWriter` 接口 | `Abstractions/IControlValueWriter.cs` |
| 3 | 定义 `ControlFinderOptions` 配置类 | `Abstractions/ControlFinderOptions.cs` |
| 4 | 实现 `WinFormsControlFinder` | `Execution/WinFormsControlFinder.cs` |
| 5 | 实现 `WinFormsControlValueWriter` | `Execution/WinFormsControlValueWriter.cs` |
| 6 | 修改 `CommandFrameworkOptions` | 新增 EnableControlFinder / EnableControlValueWriter |
| 7 | 修改 `CommandFrameworkHost` | 新增 IControlFinder / IControlValueWriter 初始化 |
| 8 | 修改 `CommandStageBase` | 委托给 IControlFinder / IControlValueWriter |
| 9 | 修改 `ExecuteCommandStage` | 委托给 IControlFinder / IControlValueWriter |
| 10 | 修改 `WinFormsEventTrigger` | 委托给 IControlFinder |

### 第二阶段：应用替换（后续阶段）

| 步骤 | 内容 | 涉及文件 |
|------|------|---------|
| 11 | 替换 `FileDrivenExecutor.FindControlByName` | `FileDriven/FileDriven.Execution/FileDrivenExecutor.cs` |
| 12 | 替换 `FileDrivenExecutor.WriteResultToControl` | `FileDriven/FileDriven.Execution/FileDrivenExecutor.cs` |
| 13 | 标记 `FrameworkAbstractions.WinFormsControlFinder` 为过时 | `Share/FrameworkAbstractions/Controls/WinFormsControlFinder.cs` |
| 14 | 更新 WdUI `ControlFinder` 内部委托 | `Operation/WdUI/ControlFinder.cs` |

---

## 九、附录

### 9.1 术语表

| 术语 | 说明 |
|------|------|
| 控件查找 | 在 WinForms 控件树中按名称/类型/谓词查找控件 |
| 控件值写入 | 设置控件的值（如 TextBox.Text、CheckBox.Checked） |
| 控件值读取 | 读取控件的值 |
| 特殊容器 | SplitContainer、TabControl 等非标准容器（其子控件不在 `parent.Controls` 中） |
| ControlIndex | 控件索引缓存，实现 O(1) 控件查找 |

### 9.2 参考文档

| 文档 | 说明 |
|------|------|
| `FileDriven/docs/控件查找失败分析报告.md` | 控件查找失败问题分析 |
| `Share/CommandFramework/README.md` | CommandFramework 使用指南 |
| `Share/CommandFramework/doc/CommandFramework_设计文档.md` | CommandFramework 设计文档 |
