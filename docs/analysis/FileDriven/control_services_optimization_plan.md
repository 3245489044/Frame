# ControlServices 及相关组件优化方案

> 基于 `domainListView` 21 个数学域不显示问题的分析，整理相关文件、根因和优化方案。

---

## 1. 问题概述

### 现象
- `MathFunctionBrowser.json` 加载后，`InitCommands` 执行 `exec Math_GetDomains output=domainListView` 成功
- 日志显示 `已设置控件值: domainListView = {"Items":[...],"Count":21}`
- 但 UI 上 `domainListView` 的 21 个数学域**完全不显示**
- `ControlRegistryAwareFinder` 输出大量 `Registry 未命中` 警告

### 根因
`WriteListData` 中 `BeginUpdate/EndUpdate` 包裹的 `Items.Clear()` + `Items.Add()` 逻辑本身正确，但存在两个潜在问题：
1. **`EndUpdate()` 后可能触发 `SelectedIndexChanged`**，导致 `Math_GetFunctionsInDomain` 被调用（传入空 domain），返回空结果写入 `funcListView`
2. **`RegisterSideContainerControls` 注册不完整** — 深层嵌套控件（如 `domainGroup` → `domainListView`）可能未被注册到 `ControlRegistry`，导致查找链每次都走回退路径

---

## 2. 相关文件清单

### 核心文件

| 文件 | 行数 | 职责 | 优化优先级 |
|------|------|------|-----------|
| `FileDriven/FileDriven.Execution/Command/ControlServices.cs` | 1339 | 控件查找、写入、服务编排（6 个类合并） | **P0** |
| `FileDriven/FileDriven.UI/Core/MainForm.cs` | 739 | UI 主窗体，含 `RegisterSideContainerControls`、`LoadSideContainerAsync` | **P0** |
| `FileDriven/FileDriven.UI/WinForms/Loading/ConfigLoader.cs` | 225 | JSON 配置加载器 | P1 |
| `FileDriven/FileDriven.UI/WinForms/Utils/ControlFinderService.cs` | 98 | 统一控件查找入口 | P1 |
| `FileDriven/FileDriven.UI/WinForms/Services/UIEventBinder.cs` | 201 | UI 事件绑定器 | P2 |
| `FileDriven/FileDriven.Execution/FileDrivenExecutor.cs` | 1879 | 命令执行器（含 ExecHandler） | P2 |
| `FileDriven/FileDriven.Business/Registrations/MathBrowserFunctions.cs` | 160 | 数学浏览器函数注册 | P3 |

### 配置文件

| 文件 | 职责 |
|------|------|
| `FileDriven/DrivenConfig/JsonConfig/MathFunctionBrowser.json` | 数学函数浏览器 UI 配置 |
| `FileDriven/DrivenConfig/JsonConfig/ConfigSelector.json` | 配置选择器 UI 配置 |

### 测试相关

| 文件 | 职责 |
|------|------|
| `CentralConsole/Operation.Tests/` | Operation 层测试 |
| `CentralConsole/Integration.Tests/` | 集成测试 |

---

## 3. 优化方案

### 3.1 P0 — 修复 `WriteListData` 的事件副作用

**文件：** `ControlServices.cs`（第 1086-1096 行）

**问题：** `BeginUpdate/EndUpdate` 抑制了重绘但**不抑制事件**。`Items.Clear()` 在无选中项时不会触发 `SelectedIndexChanged`，但 `EndUpdate()` 后如果选中状态变化，事件仍可能触发。

**优化：**
```csharp
private bool WriteListData(object control, ListData data)
{
    switch (control)
    {
        case ListView listView:
            // ★ 修复：保存当前选中项，写入后恢复
            var selectedIndices = listView.SelectedIndices.Cast<int>().ToList();
            var focusedItem = listView.FocusedItem;
            
            listView.BeginUpdate();
            listView.Items.Clear();
            foreach (var item in data.Items)
                listView.Items.Add(new ListViewItem(item));
            listView.EndUpdate();
            
            // ★ 修复：恢复选中状态，避免意外触发 SelectedIndexChanged
            if (selectedIndices.Count > 0)
            {
                foreach (var idx in selectedIndices.Where(i => i < listView.Items.Count))
                    listView.Items[idx].Selected = true;
            }
            return true;
        // ... ComboBox, ListBox 同理
    }
}
```

### 3.2 P0 — 优化 `RegisterSideContainerControls` 的注册可靠性

**文件：** `MainForm.cs`（第 575-633 行）

**问题：** `RegisterSingleControl` 通过 `ControlFinderService.FindControl<Control>(_sideContainer, controlName)` 查找控件，如果查找失败则不会注册。深层嵌套控件（如 `domainGroup` → `domainListView`）可能因查找链问题未被注册。

**优化方案 A（推荐）：** 直接从 `UIConfiguration.Controls` 树中提取控件名注册，不依赖运行时查找
```csharp
private void RegisterSideContainerControls(UIConfiguration config)
{
    if (config.Views == null) return;
    
    var allControlNames = new HashSet<string>();
    foreach (var view in config.Views)
    {
        if (view.Controls == null) continue;
        CollectControlNames(view.Controls, allControlNames);
    }
    
    // 批量注册：先收集所有名称，再统一查找注册
    foreach (var name in allControlNames)
    {
        var actualControl = ControlFinderService.FindControl<Control>(_sideContainer, name);
        if (actualControl != null)
        {
            ControlRegistry.Register(name, actualControl);
        }
        else
        {
            // ★ 修复：记录查找失败的控件名，便于诊断
            FileDrivenLogPlaceholderHandler.Log("MainForm_RegisterSideContainerControls_find_failed",
                new { controlName = name });
        }
    }
}

private void CollectControlNames(List<ControlConfig> controls, HashSet<string> names)
{
    foreach (var ctrl in controls)
    {
        if (!string.IsNullOrWhiteSpace(ctrl.Name))
            names.Add(ctrl.Name);
        if (ctrl.Controls != null && ctrl.Controls.Count > 0)
            CollectControlNames(ctrl.Controls, names);
    }
}
```

**优化方案 B（备选）：** 在 `InitCommands` 执行完成后，**再次执行注册**，确保所有控件都已渲染完成
```csharp
// 在 LoadSideContainerAsync 中 InitCommands 执行后
if (config.InitCommands?.Count > 0 && CommandExecutor != null)
{
    // ... 执行 InitCommands ...
    
    // ★ 修复：InitCommands 执行后再次注册控件
    //   某些控件可能在 InitCommands 执行后才完全初始化
    RegisterSideContainerControls(config);
}
```

### 3.3 P1 — 拆分 `ControlServices.cs`

**文件：** `ControlServices.cs`（1339 行）

**问题：** 6 个类集中在一个文件，不利于维护和测试。

**优化：** 按职责拆分为 3 个文件

| 新文件 | 包含类 | 预估行数 |
|--------|--------|---------|
| `ControlFinder.cs` | `CompositeControlFinder`、`ControlRegistryAwareFinder` | ~200 |
| `ControlValueWriter.cs` | `CompositeValueWriter`、`WinFormsControlValueWriter` | ~500 |
| `UnifiedControlService.cs` | `UnifiedControlService`、`ControlServiceCollection` | ~300 |

### 3.4 P1 — 补充 `ListView.View` 兼容性处理

**文件：** `ControlServices.cs` — `WriteListData`

**问题：** 当前没有处理 `View.Details` 模式下的 `Columns` 设置。如果 JSON 配置了 `view: "Details"` 但没有 `Columns`，数据写入后可能不显示。

**优化：**
```csharp
case ListView listView:
    listView.BeginUpdate();
    listView.Items.Clear();
    
    // ★ 修复：根据 View 模式决定添加方式
    if (listView.View == View.Details && listView.Columns.Count > 0)
    {
        foreach (var item in data.Items)
        {
            var lvi = new ListViewItem(item);
            // 如果数据包含子项（制表符分隔），自动填充 Columns
            var parts = item.Split('\t');
            if (parts.Length > 1)
            {
                lvi.Text = parts[0];
                for (int i = 1; i < parts.Length && i < listView.Columns.Count; i++)
                    lvi.SubItems.Add(parts[i]);
            }
            listView.Items.Add(lvi);
        }
    }
    else
    {
        foreach (var item in data.Items)
            listView.Items.Add(new ListViewItem(item));
    }
    
    listView.EndUpdate();
    return true;
```

### 3.5 P2 — 优化 `ControlFinderService` 的查找链

**文件：** `ControlFinderService.cs`

**问题：** `FindControl<T>(Control parent, string name)` 先查 `IControlRegistry`，再查 `IControlFinder`。但 `IControlFinder` 的实现（`CompositeControlFinder`）内部又包含 `ControlRegistryAwareFinder`（再次查 `IControlRegistry`），存在**重复查找**。

**优化：** 简化查找链，消除重复
```csharp
public static T? FindControl<T>(Control parent, string name) where T : Control
{
    if (parent == null || string.IsNullOrWhiteSpace(name))
        return null;

    // ★ 优化：直接通过 IControlFinder 查找
    //   IControlFinder 的实现（CompositeControlFinder）内部已包含
    //   ControlRegistryAwareFinder → WinFormsControlFinder 的完整回退链
    var finder = ComponentRegistry.Instance.Resolve<IControlFinder>();
    if (finder != null)
    {
        var found = finder.FindByName(parent, name);
        if (found is T result)
            return result;
    }

    return null;
}
```

### 3.6 P3 — 补充 `Math_GetDomains` 边界处理

**文件：** `MathBrowserFunctions.cs`

**问题：** `Math_GetDomains` 返回 `ListData`，但 `ExecHandler` 中 `result.Value` 的类型在运行时是 `object?`，`WriteValue(object, object?)` 通过 `is IControlValue` 判断。如果 `ListData` 的序列化/反序列化过程中类型信息丢失，会降级到 `string` 路径。

**优化：** 无需修改（当前 `ListData` 实现了 `IControlValue`，类型信息保持完整）。但建议增加单元测试验证。

---

## 4. 优化执行顺序

```
Phase 1 (P0): 修复 WriteListData 事件副作用 + 优化 RegisterSideContainerControls
    ↓
Phase 2 (P1): 拆分 ControlServices.cs + 补充 View 兼容性
    ↓
Phase 3 (P2): 优化 ControlFinderService 查找链
    ↓
Phase 4 (P3): 补充单元测试（WriteListData、Math_GetDomains、ControlRegistryAwareFinder）
```

---

## 5. 测试策略（优化后）

| 测试目标 | 类型 | 覆盖内容 |
|---------|------|---------|
| `WinFormsControlValueWriter.WriteListData` | 单元测试 | ListView/ComboBox/ListBox 写入、事件抑制、View 兼容性 |
| `Math_GetDomains` / `Math_GetFunctionsInDomain` | 单元测试 | 返回值结构、空输入、边界情况 |
| `ControlRegistryAwareFinder.FindByName` | 单元测试 | Registry 命中/未命中、回退链 |
| `RegisterSideContainerControls` | 集成测试 | 控件注册完整性、嵌套控件注册 |
| `LoadSideContainerAsync` | 集成测试 | 完整加载流程（可选，成本较高） |

---

## 6. 附录：关键代码位置速查

| 功能 | 文件 | 行号 |
|------|------|------|
| `WriteListData` | `ControlServices.cs` | 1086-1113 |
| `WriteControlValue` | `ControlServices.cs` | 1071-1081 |
| `WriteValue(object, object?)` | `ControlServices.cs` | 292-315 |
| `ControlRegistryAwareFinder.FindByName` | `ControlServices.cs` | 868-906 |
| `RegisterSideContainerControls` | `MainForm.cs` | 575-633 |
| `LoadSideContainerAsync` | `MainForm.cs` | 512-568 |
| `ExecHandler` | `FileDrivenExecutor.cs` | 1001-1123 |
| `Math_GetDomains` | `MathBrowserFunctions.cs` | 32-44 |
| `ControlFinderService.FindControl` | `ControlFinderService.cs` | 37-63 |
