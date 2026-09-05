# SplitContainer Entity 树重构：JSON 只在一处解析为 Entity 实例

> **日期**：2026-07-22
> **作者**：Cline
> **状态**：待实施

---

## 1. 问题描述

### 1.1 错误日志

```
[ERROR] [MainForm] 初始化失败:
  errorMessage=Root container must contain a 'contentContainer'
  → 根容器类型: EcsConfigContainer
  → 根容器名称: rootContainer
  → 可用子容器: []
  → 来源: ContentContainer
```

### 1.2 调用链

```
MainForm.OnRootContainerLoaded
  → FileDrivenWdUIBridge.FindAndBindContainers
    → ContainerFinder.FindAndBindMainContainers
      → ContainerFinder.FindSubContainer(rootContainer, "contentContainer", "ContentContainer")
        → rootContainer.FindSubContainer("contentContainer")
          → EcsContainer.FindControlByName("contentContainer")
            → FindRecursive(this, "contentContainer")  // 遍历 WinForms 控件树
              → 找不到！抛出 InvalidOperationException
```

---

## 2. 当前流程分析

### 2.1 JSON 字符串在流程中的传递路径

```
MainLayout.json (文件)
  ↓ File.ReadAllTextAsync
JSON 字符串
  ↓ JsonTemplateProcessor.Process (模板展开)
预处理后的 JSON 字符串
  ↓ JsonSerializer.Deserialize<UIConfiguration>
UIConfiguration 对象
  ↓
ConfigLoader.LoadConfigAsync(container, configPath)
  → JsonConfigService.LoadConfigAsync(configPath)  ← 读取文件 → 预处理 → 反序列化
  → container.LoadConfigAsync(config)              ← 传入 UIConfiguration 对象
    → EcsConfigContainer.RenderConfigAsync(config)
      → _renderPipeline.ExecuteCreateStageAsync(this, config)  ← 传入 UIConfiguration
        → LoadConfiguration(uiConfig)  ← 再次解析 UIConfiguration
          → ConfigToEntitySystem.Execute(config)
            → ConfigToEntityConverter.Convert(config)  ← 遍历 UIConfiguration 创建 Entity 树
```

### 2.2 核心问题

**`UIConfiguration` 对象在流程中作为"配置载体"传递，但 `ConfigToEntityConverter.Convert` 又再次遍历 `UIConfiguration.Views[].Controls[].Panel1Controls[]/Panel2Controls[]` 来创建 Entity 树。**

这意味着 JSON 的结构信息在流程中经历了三次映射：

| 步骤 | 输入 | 输出 | 信息损失 |
|------|------|------|---------|
| 1. JSON → UIConfiguration | JSON 字符串 | UIConfiguration 对象 | 无 |
| 2. UIConfiguration → Entity 树 | UIConfiguration | Entity 树 | **Panel1/Panel2 归属信息丢失** |
| 3. Entity 树 → WinForms 控件树 | Entity 树 | WinForms 控件 | 子控件被添加到 SplitContainer.Controls 而非 Panel1.Controls |

**步骤 2 中，`Panel1Controls`/`Panel2Controls` 的子控件 Entity 被直接挂到 `splitContainer` Entity 下：**

```csharp
// ConfigToEntityConverter.ConvertControl 中：
if (control.Panel1Controls is { Count: > 0 })
{
    foreach (var child in control.Panel1Controls)
    {
        var childEntity = ConvertControl(child, entity, childAncestor);
        AddChild(entity, childEntity);  // entity = splitContainer Entity
    }
}
```

Entity 树结构（当前，错误）：
```
splitContainer Entity (type: "splitcontainer")
  └── contentContainer Entity (type: "container")  ← 父 Entity 是 splitContainer
```

**步骤 3 中，`ControlCreateSystem` 阶段 2 的 `FindParent` 返回 `SplitContainer` 实例：**

```csharp
var parent = _strategy?.FindParent(entity);  // → ControlPool.GetControl(splitContainer Entity)
parent.Controls.Add(control);  // → splitContainer.Controls.Add(contentContainer)
```

**`contentContainer` 被直接添加到 `SplitContainer.Controls`，而不是 `SplitContainer.Panel1.Controls`。**

WinForms 控件树（实际）：
```
SplitContainer
  ├── SplitterPanel (Panel1)
  └── SplitterPanel (Panel2)
  └── EcsConfigContainer (contentContainer)  ← 被直接添加到了 SplitContainer.Controls
```

`EcsContainer.FindRecursive` 遍历 `parent.Controls`，对于 `SplitContainer`，其 `Controls` 集合只包含 `SplitterPanel` 对象，不包含直接添加的子控件，因此找不到 `contentContainer`。

---

## 3. 修复方案

### 3.1 核心设计原则

**JSON 只在一处解析为 Entity 实例，后续传递 Entity 实例而不是 `UIConfiguration` 对象。**

具体来说：
1. **`JsonConfigService.LoadConfigAsync`** 只负责读取文件、预处理、反序列化为 `UIConfiguration`
2. **`ConfigToEntityConverter.Convert`** 将 `UIConfiguration` 转换为 Entity 树（这是唯一一次解析 JSON 结构）
3. **后续所有操作**（控件创建、布局、事件绑定）都基于 Entity 树，不再需要 `UIConfiguration`

### 3.2 具体修改：在 Entity 树中为 Panel1/Panel2 创建中间 Entity

**在 `ConfigToEntityConverter.ConvertControl` 中，当处理 `Panel1Controls`/`Panel2Controls` 时，为每个 Panel 创建一个中间 Entity（类型为 `"splitterpanel"`），然后将子控件 Entity 的父 Entity 设置为中间 Entity。**

Entity 树结构（修复后）：
```
splitContainer Entity (type: "splitcontainer")
  ├── Panel1 Entity (type: "splitterpanel", name: "contentSplitter_Panel1")
  │   └── contentContainer Entity (type: "container")
  └── Panel2 Entity (type: "splitterpanel", name: "contentSplitter_Panel2")
      └── sideContainer Entity (type: "container")
```

WinForms 控件树（修复后）：
```
SplitContainer
  ├── SplitterPanel (Panel1)
  │   └── EcsConfigContainer (contentContainer)  ← 正确位置
  └── SplitterPanel (Panel2)
      └── EcsConfigContainer (sideContainer)  ← 正确位置
```

### 3.3 修改点

#### 3.3.1 `ConfigToEntityConverter.ConvertControl` — 为 Panel1/Panel2 创建中间 Entity

```csharp
// 处理 SplitContainer Panel1 的子控件
if (control.Panel1Controls is { Count: > 0 })
{
    // ★ 2026-07-22 修复：创建 Panel1 中间 Entity，保留 Panel1 归属信息
    var panel1Entity = CreateSplitterPanelEntity(entity, $"{control.Name}_Panel1", "Panel1");
    foreach (var child in control.Panel1Controls)
    {
        var childAncestor = isContainer ? entity : ancestor;
        var childEntity = ConvertControl(child, panel1Entity, childAncestor);
        AddChild(panel1Entity, childEntity);
    }
}

// 处理 SplitContainer Panel2 的子控件
if (control.Panel2Controls is { Count: > 0 })
{
    // ★ 2026-07-22 修复：创建 Panel2 中间 Entity，保留 Panel2 归属信息
    var panel2Entity = CreateSplitterPanelEntity(entity, $"{control.Name}_Panel2", "Panel2");
    foreach (var child in control.Panel2Controls)
    {
        var childAncestor = isContainer ? entity : ancestor;
        var childEntity = ConvertControl(child, panel2Entity, childAncestor);
        AddChild(panel2Entity, childEntity);
    }
}
```

新增 `CreateSplitterPanelEntity` 方法：

```csharp
/// <summary>
/// 创建 SplitterPanel 中间 Entity
/// <para>★ 2026-07-22 新增：为 SplitContainer 的 Panel1/Panel2 创建中间 Entity，</para>
/// <para>   保证 Entity 树与 WinForms 控件树结构一致。</para>
/// <para>   JSON 只在一处解析为 Entity 实例，后续传递 Entity 实例而不是 UIConfiguration。</para>
/// </summary>
private Entity CreateSplitterPanelEntity(Entity parentEntity, string name, string panelSide)
{
    var panelEntity = _controlFactory.CreateFromConfig(new ControlConfig
    {
        Type = "splitterpanel",
        Name = name,
        Text = name
    }, parentEntity);

    _entityManager.AddComponent(panelEntity, new SplitterPanelComponent
    {
        PanelSide = panelSide  // "Panel1" 或 "Panel2"
    });

    _entityManager.AddComponent(panelEntity, new ContainerAncestorComponent
    {
        AncestorEntity = parentEntity,  // 指向 splitContainer Entity
        IsContainer = false
    });

    AddChild(parentEntity, panelEntity);
    return panelEntity;
}
```

#### 3.3.2 新增 `SplitterPanelComponent`

```csharp
/// <summary>
/// SplitterPanel 组件 — 标记 Entity 为 SplitContainer 的 Panel1/Panel2
/// <para>★ 2026-07-22 新增：用于 ControlCreateSystem 阶段 1 中从 SplitContainer 获取对应的 SplitterPanel 实例。</para>
/// </summary>
public class SplitterPanelComponent : IComponent
{
    /// <summary>Panel 侧："Panel1" 或 "Panel2"</summary>
    public string PanelSide { get; set; } = "Panel1";
}
```

#### 3.3.3 `ControlCreateSystem` 阶段 1 — 处理 `splitterpanel` 类型

在 `ControlCreateSystem.Update()` 的阶段 1 中，当遇到 `"splitterpanel"` 类型时，从父 `SplitContainer` 获取对应的 `SplitterPanel` 实例：

```csharp
// 优先使用策略接口创建控件
var control = _strategy?.CreateControl(typeComp.Type);
if (control == null)
{
    // ★ 2026-07-22 新增：处理 SplitterPanel 类型
    if (string.Equals(typeComp.Type, "splitterpanel", StringComparison.OrdinalIgnoreCase))
    {
        control = CreateSplitterPanelControl(entity);
        if (control == null)
        {
            RemoveEntityFromTree(entity);
            continue;
        }
    }
    else
    {
        // 回退到事件触发（兼容旧逻辑）
        ControlCreated?.Invoke(entity, typeComp.Type, typeComp.Name);
        if (ControlCreated == null)
        {
            RemoveEntityFromTree(entity);
            continue;
        }
        // 事件触发成功，标记为已创建
        var state = _entityManager.GetComponent<ControlStateComponent>(entity);
        state.IsCreated = true;
        state.LastUpdated = DateTime.UtcNow;
        _entityManager.SetComponent(entity, state);
        continue;
    }
}
```

新增 `CreateSplitterPanelControl` 方法：

```csharp
/// <summary>
/// 创建 SplitterPanel 控件
/// <para>★ 2026-07-22 新增：从父 SplitContainer 获取对应的 SplitterPanel 实例。</para>
/// <para>   SplitterPanel 是 SplitContainer 的内置控件，不需要通过 ControlStrategyRegistry 创建。</para>
/// </summary>
private Control? CreateSplitterPanelControl(Entity entity)
{
    if (!_entityManager.TryGetComponent<ControlHierarchyComponent>(entity, out var hierarchy))
        return null;

    var parentEntity = hierarchy.Parent;
    if (!parentEntity.IsValid)
        return null;

    // 获取父 SplitContainer 控件
    var parentControl = _controlPool.GetControl(parentEntity);
    if (parentControl is not SplitContainer splitter)
        return null;

    // 获取 Panel 侧
    if (!_entityManager.TryGetComponent<SplitterPanelComponent>(entity, out var panelComp))
        return null;

    var panel = panelComp.PanelSide == "Panel2" ? splitter.Panel2 : splitter.Panel1;

    // 注册到 ControlPool（Entity ↔ SplitterPanel 映射）
    _controlPool.Register(entity, panel.Name, panel);

    return panel;
}
```

#### 3.3.4 `ControlCreateSystem` 阶段 2 — 跳过 SplitterPanel 的父子关系建立

`SplitterPanel` 是 `SplitContainer` 的内置控件，不需要通过 `parent.Controls.Add(control)` 建立父子关系：

```csharp
// 阶段 2：建立父子关系
foreach (var (entity, control) in created)
{
    // ★ 2026-07-22 新增：跳过 SplitterPanel，其父子关系由 SplitContainer 自身管理
    if (_entityManager.TryGetComponent<ControlTypeComponent>(entity, out var typeComp) &&
        string.Equals(typeComp.Type, "splitterpanel", StringComparison.OrdinalIgnoreCase))
    {
        // 标记为已创建
        var state = _entityManager.GetComponent<ControlStateComponent>(entity);
        state.IsCreated = true;
        state.LastUpdated = DateTime.UtcNow;
        _entityManager.SetComponent(entity, state);
        continue;
    }

    // 查找父控件
    var parent = _strategy?.FindParent(entity);
    if (parent != null)
    {
        parent.Controls.Add(control);
    }
    // ...
}
```

#### 3.3.5 `EcsControlCreationStrategy.FindParent` — 处理 SplitterPanel 父 Entity

当子控件的父 Entity 是 `SplitterPanel` Entity 时，`FindParent` 应返回 `SplitterPanel` 对应的控件（已在阶段 1 注册到 `ControlPool`）：

```csharp
public Control? FindParent(Entity entity)
{
    if (!_entityManager.TryGetComponent<ControlHierarchyComponent>(entity, out var hierarchy))
        return null;

    var parentEntity = hierarchy.Parent;
    if (!parentEntity.IsValid)
        return null;

    // ★ 2026-07-22 新增：如果父 Entity 是 SplitterPanel，直接返回其对应的 Control
    if (_entityManager.TryGetComponent<ControlTypeComponent>(parentEntity, out var parentTypeComp) &&
        string.Equals(parentTypeComp.Type, "splitterpanel", StringComparison.OrdinalIgnoreCase))
    {
        return _controlPool.GetControl(parentEntity);
    }

    // 原有逻辑：通过 ContainerAncestorComponent 查找父容器
    // ...
}
```

---

## 4. 影响范围

| 文件 | 修改类型 | 说明 |
|------|---------|------|
| `CsharpPrj/BasicMethod/ECS/Conversion/ConfigToEntityConverter.cs` | 修改 | 为 Panel1/Panel2 创建中间 Entity |
| `CsharpPrj/BasicMethod/ECS/Components/SplitterPanelComponent.cs` | **新增** | SplitterPanel 组件 |
| `CsharpPrj/Operation/FrontEndDriven/ECS/Systems/ControlCreateSystem.cs` | 修改 | 阶段 1 处理 splitterpanel 类型，阶段 2 跳过 splitterpanel |
| `CsharpPrj/Operation/FrontEndDriven/ECS/Strategies/EcsControlCreationStrategy.cs` | 修改 | FindParent 处理 SplitterPanel 父 Entity |

---

## 5. 验证方法

1. 启动应用，观察 `MainForm.OnRootContainerLoaded` 是否正常执行
2. 验证 `contentContainer` 和 `sideContainer` 能被 `ContainerFinder.FindAndBindMainContainers` 找到
3. 验证 `contentContainer` 在 WinForms 控件树中的位置：
   - `rootContainer.Controls[0]` → Panel "MainView"
   - `Panel.Controls[0]` → SplitContainer "mainSplitter"
   - `SplitContainer.Panel1.Controls[0]` → Panel "mainArea"
   - `Panel.Controls[0]` → SplitContainer "contentSplitter"
   - `SplitContainer.Panel1.Controls[0]` → EcsConfigContainer "contentContainer"
4. 验证 `EcsContainer.FindRecursive` 能正确遍历到 `contentContainer`
5. 验证 `ConfigSelector` 能正常加载到 `contentContainer` 中

---

## 6. 回退方案

如果修复后出现问题，可以回退到修改 `EcsContainer.FindRecursive` 的方案（治标不治本）：

```csharp
private static Control? FindRecursive(Control parent, string name)
{
    foreach (Control c in parent.Controls)
    {
        if (c.Name == name) return c;
        // 处理 SplitContainer
        if (c is SplitContainer splitter)
        {
            var found = FindRecursive(splitter.Panel1, name);
            if (found != null) return found;
            found = FindRecursive(splitter.Panel2, name);
            if (found != null) return found;
        }
        else
        {
            var found = FindRecursive(c, name);
            if (found != null) return found;
        }
    }
    return null;
}
```
