# domainListView.selectedindexchanged 事件绑定查找失败分析

> **分析日期**：2026-06-17  
> **相关日志时间**：02:54:21 ~ 02:54:22  
> **涉及组件**：FileDriven.Pipeline / EventBindingResolver / ExecuteCommandStage

---

## 1. 错误现象

```
[ERROR] [FileDriven] [ERROR] domainListView.selectedindexchanged: No selectedindexchanged binding found for 'domainListView'.
```

该错误在 `MathFunctionBrowser.json` 配置加载完成后，用户点击左侧"数学域"列表（`domainListView`）时触发。错误连续出现 3 次，表明每次用户切换选择域时都会触发此错误。

---

## 2. 预期行为

当用户点击 `domainListView` 时，应触发 `SelectedIndexChanged` 事件，查找配置中定义的事件绑定：

```json
{
  "type": "listview", "name": "domainListView",
  "events": {
    "selectedIndexChanged": "Math_GetFunctionsInDomain",
    "bindings": { "inputs": ["domainListView"], "output": "funcListView" }
  }
}
```

然后执行 `Math_GetFunctionsInDomain` 函数，将结果写入 `funcListView`。

---

## 3. 完整事件链路追踪

### 3.1 事件触发链路（正常）

```
用户点击 domainListView
  → WinForms SelectedIndexChanged 事件触发
    → WinFormsEventBinderAdapter.BindSingleEvent 捕获
      → _eventPipelineExecutor.Execute("domainListView", "selectedindexchanged", ...)
        → 管线创建 CommandContext
          → SharedData["_originalEventType"] = "selectedindexchanged"
            → ExecuteCommandStage.HandleClick 执行
              → 从 SharedData 读取 eventType = "selectedindexchanged"
                → 调用 FindBinding("domainListView", "selectedindexchanged")
```

### 3.2 绑定查找链路（失败）

```
FindBinding("domainListView", "selectedindexchanged")
  → _bindingFinder("domainListView", "selectedindexchanged")     [PipelineEntry.FindBinding]
    → EventBindingResolver.FindBinding("domainListView", "selectedindexchanged")
      → ResolveEventType("domainListView", "selectedindexchanged")
        → action != "set", 直接返回 "selectedindexchanged"
      → FindEventBinding("domainListView", "selectedindexchanged")
        → FindControlByName("domainListView")
          → 遍历 _loadedConfigs 快照
            → 对每个配置调用 ControlTreeWalker.FindControl(config, "domainListView")
              → 遍历 config.Views → view.Controls → 递归查找
                → **返回 null** ← 失败点
        → 返回 null
      → 返回 null
    → 返回 null
  → binding == null
→ 输出错误: "No selectedindexchanged binding found for 'domainListView'."
```

---

## 4. 根本原因分析

### 4.1 核心问题：`_loadedConfigs` 字典实例不同步

`EventBindingResolver` 和 `PipelineEntry` 之间的 `_loadedConfigs` 字典**可能不是同一个实例**。

#### 配置注册路径

```
MainForm.LoadSideContainerAsync
  → JsonConfigService.LoadConfigAsync (反序列化得到 UIConfiguration)
    → MainForm.RegisterSideContainerControls (注册控件到 ControlRegistry)
      → ConfigRegistrar.RegisterLoadedConfig(config)    [IConfigLoader 接口]
        → PipelineEntry._loadedConfigs.TryAdd(title, new LoadedConfigInfo(uiConfig))
```

#### 绑定查找路径

```
ExecuteCommandStage.HandleClick
  → _bindingFinder (Func<string, string, BindingResult?>)
    → PipelineEntry.FindBinding
      → EventBindingResolver.FindBinding
        → 遍历 EventBindingResolver._loadedConfigs
```

**关键问题**：如果 `EventBindingResolver` 的 `_loadedConfigs` 是一个独立的空字典实例（而非与 `PipelineEntry` 共享的同一个字典），则所有绑定查找都会失败。

### 4.2 `EnsureSharedLoadedConfigs` 的类型转换问题

在 `PipelineEntry.cs`（第 370-389 行）中，`EnsureSharedLoadedConfigs` 方法试图确保 `EventBindingResolver` 与 `PipelineEntry` 共享同一个 `_loadedConfigs` 实例：

```csharp
public IEventBindingResolver EnsureSharedLoadedConfigs(IEventBindingResolver bindingResolver)
{
    if (bindingResolver is not EventBindingResolver resolver)
        return bindingResolver;

    // 通过反射检查内部字典是否已共享
    var dictField = typeof(EventBindingResolver).GetField("_loadedConfigs",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    if (dictField != null)
    {
        var internalDict = dictField.GetValue(resolver);
        if (ReferenceEquals(internalDict, _loadedConfigs))
            return resolver;  // 已共享，直接返回
    }

    // ★ 问题代码：类型转换会抛出 InvalidCastException
    return new EventBindingResolver(
        (IDictionary<string, object>)_loadedConfigs,   // ← InvalidCastException!
        _loadedConfigsLock);
}
```

**问题**：`_loadedConfigs` 的类型是 `ConcurrentDictionary<string, LoadedConfigInfo>`，而 `EventBindingResolver` 的第二个构造函数接受 `IDictionary<string, object>`。

在 C# 中，`ConcurrentDictionary<string, LoadedConfigInfo>` **不能直接转型**为 `IDictionary<string, object>`，即使 `LoadedConfigInfo` 是引用类型（泛型接口不支持协变）。这会在运行时抛出 `InvalidCastException`。

如果这个异常被上层的 `try-catch` 静默吞掉，或者 `EnsureSharedLoadedConfigs` 根本没有被调用，那么 `EventBindingResolver` 就会使用一个**空的 `_loadedConfigs` 字典**，导致所有控件查找失败。

### 4.3 次要可能原因

如果 `_loadedConfigs` 共享没有问题，则需检查以下路径：

| 检查点 | 状态 | 说明 |
|--------|------|------|
| `ControlTreeWalker.FindControl` 遍历逻辑 | ✅ 正常 | `domainListView` 在 `groupbox.controls[0]`，遍历路径正确 |
| `EventConfigConverter` 反序列化 | ✅ 正常 | `"selectedIndexChanged"` → `.ToLowerInvariant()` → `"selectedindexchanged"` → 匹配 `case "selectedindexchanged"` |
| `EventConfig.SelectedIndexChanged` 属性 | ✅ 正常 | 类型为 `List<string>?`，JSON 中定义为单字符串 |
| `RegisterLoadedConfig` 注册键 | ✅ 正常 | 键为 `uiConfig.Form.Title` = `"数学函数计算器"` |

---

## 5. 配置结构验证

### 5.1 domainListView 在配置树中的位置

```
UIConfiguration.Views[0] (MainView, Panel)
  └─ Controls[2] (mainPanel, Panel)
       └─ Controls[0] (domainGroup, GroupBox)
            └─ Controls[0] (domainListView, ListView)  ← 目标控件
                 └─ Events.SelectedIndexChanged = ["Math_GetFunctionsInDomain"]
                 └─ Events.Bindings.Inputs = ["domainListView"]
                 └─ Events.Bindings.Output = "funcListView"
```

### 5.2 ControlTreeWalker 遍历路径

```
FindControl(config, "domainListView")
  → FindControlInList(view.Controls, "domainListView")
    → 遍历: titleLabel, searchPanel, mainPanel, statusLabel
      → 匹配 mainPanel.Name == "mainPanel"? No
        → 递归 FindControlInList(mainPanel.Controls, "domainListView")
          → 遍历: domainGroup, funcGroup, detailGroup, resultGroup, actionBar
            → 匹配 domainGroup.Name == "domainListView"? No
              → 递归 FindControlInList(domainGroup.Controls, "domainListView")
                → 遍历: domainListView
                  → 匹配 domainListView.Name == "domainListView"? **Yes** ✓
```

遍历路径正确，理论上应该能找到。

---

## 6. 修复建议

### 6.1 方案一：修复 `EnsureSharedLoadedConfigs` 的类型转换（推荐）

```csharp
public IEventBindingResolver EnsureSharedLoadedConfigs(IEventBindingResolver bindingResolver)
{
    if (bindingResolver is not EventBindingResolver resolver)
        return bindingResolver;

    var dictField = typeof(EventBindingResolver).GetField("_loadedConfigs",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    if (dictField != null)
    {
        var internalDict = dictField.GetValue(resolver);
        if (ReferenceEquals(internalDict, _loadedConfigs))
            return resolver;
    }

    // ★ 修复：使用正确的类型转换
    var objectDict = new ConcurrentDictionary<string, object>(
        StringComparer.OrdinalIgnoreCase);
    foreach (var kvp in _loadedConfigs)
    {
        objectDict.TryAdd(kvp.Key, kvp.Value);
    }

    return new EventBindingResolver(objectDict, _loadedConfigsLock);
}
```

### 6.2 方案二：确保 `EventBindingResolver` 直接引用 `PipelineEntry._loadedConfigs`

在创建 `EventBindingResolver` 时，直接传入 `PipelineEntry._loadedConfigs` 的引用，而不是创建一个新的字典实例。

### 6.3 方案三：添加调试日志

在 `EventBindingResolver.FindEventBinding` 中添加日志输出，确认 `_loadedConfigs` 的内容：

```csharp
System.Diagnostics.Debug.WriteLine(
    $"[EventBindingResolver] FindEventBinding: controlName={controlName}, " +
    $"eventType={eventType}, loadedConfigsCount={_loadedConfigs.Count}");

foreach (var kvp in GetSnapshot())
{
    System.Diagnostics.Debug.WriteLine(
        $"[EventBindingResolver]   config key: {kvp.Key}");
}
```

---

## 7. 相关代码文件

| 文件 | 作用 |
|------|------|
| `FileDriven/FileDriven.Pipeline/PipelineEntry.cs` | PipelineEntry 入口，包含 `_loadedConfigs` 和 `EnsureSharedLoadedConfigs` |
| `FileDriven/FileDriven.Pipeline/Core/EventCore.cs` | `EventBindingResolver` 实现，包含 `FindEventBinding` 和 `FindControlByName` |
| `FileDriven/FileDriven.Pipeline/Core/Skeletons/ControlTreeWalker.cs` | 控件树遍历工具，`FindControl` 实现 |
| `Share/CommandFramework/Pipeline/Stages/ExecuteCommandStage.cs` | `ExecuteCommandStage.HandleClick`，错误输出的位置 |
| `Share/UniversalMiddleware/ConfigurationModels/Models/EventConfig.cs` | `EventConfig` 模型和 `EventConfigConverter` |
| `Share/UniversalMiddleware/ConfigurationModels/Models/UIConfiguration.cs` | `UIConfiguration` 模型 |
| `Share/UniversalMiddleware/ConfigurationModels/Models/ControlConfig.cs` | `ControlConfig` 模型 |
| `FileDriven/DrivenConfig/JsonConfig/MathFunctionBrowser.json` | 触发错误的配置文件 |
| `FileDriven/FileDriven.UI/Core/MainForm.cs` | `RegisterLoadedConfig` 的调用方 |

---

## 8. 验证步骤

修复后，通过以下步骤验证：

1. 启动应用，加载 `MathFunctionBrowser.json`
2. 点击左侧 `domainListView` 中的任意数学域（如 "Math"）
3. 观察日志，确认不再出现 `No selectedindexchanged binding found` 错误
4. 确认右侧 `funcListView` 正确显示该域下的函数列表
