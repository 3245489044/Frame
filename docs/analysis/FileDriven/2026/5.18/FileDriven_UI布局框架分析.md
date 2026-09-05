# FileDriven UI 布局框架分析

## 问题概述

日志 `[11:32:37] [SUCCESS] Config loaded into side container: ...\WorkflowRunner.json` 显示配置加载成功，但 **sideContainer（作为 mainSplitter.Panel2 中的容器）没有可拖拽的边框**。

用户期望 sideContainer 本身有可拖拽的边框来调整其大小，但 sideContainer 是 `ConfigContainer`（继承 Panel），它本身没有"可拖拽的边框"这种概念。sideContainer 的大小是由 `mainSplitter` 的分割条控制的。

---

## 一、整体架构

### 1.1 启动流程

```
MainForm 构造函数
  └─ LoadMainLayout()
       ├─ 创建 _rootContainer (ConfigContainer, Dock=Fill)
       ├─ Controls.Add(_rootContainer)
       └─ _rootContainer.LoadConfigAsync(MainLayoutPath)
            └─ RenderConfigAsync(config)
                 ├─ Controls.Clear()
                 ├─ RenderPipeline.Render(this, config)  ← 6阶段管线
                 ├─ RenderPipeline.TrackResize(this)      ← 订阅Resize
                 ├─ ScanAndRegisterSubContainers()        ← 扫描子容器
                 ├─ LoadStaticSubContainersAsync()         ← 加载静态子容器
                 └─ BeginInvoke → RenderPipeline.Relayout(this)  ← 延迟重布局
```

### 1.2 渲染管线（6个阶段）

```
RenderPipeline._renderPipeline:
  阶段1: TrackResizeStage     → 订阅 Resize 事件
  阶段2: CreateLayoutPanelStage → 创建空的 LayoutPanel (Dock=Fill)
  阶段3: CacheConfigStage     → 缓存配置到 State
  阶段4: ExecuteStrategyStage → 执行 PanelStrategy
  阶段5: ProcessNestedStage   → 递归处理嵌套控件
  阶段6: ApplySplitterStage   → 应用 Splitter 距离

RelayoutPipeline._relayoutPipeline (跳过阶段1-2):
  阶段3: CacheConfigStage     → 重新缓存配置
  阶段4: ExecuteStrategyStage → 重新执行策略
  阶段5: ProcessNestedStage   → 重新处理嵌套
  阶段6: ApplySplitterStage   → 重新应用分割器
```

---

## 二、核心问题分析

### 2.1 问题1：CreateLayoutPanelStage 没有创建子控件

**`CreateLayoutPanelStage`（阶段2）** 的代码：

```csharp
// 只创建了一个空的 Panel，没有创建任何子控件！
var panel = new Panel
{
    Name = view.Name,
    Dock = DockStyle.Fill
};
target.Controls.Add(panel);
```

而 **`ControlFactory.CreateLayoutPanel`** 方法（负责创建 LayoutPanel 并添加子控件）**在正式的渲染流程中根本没有被调用**（只在测试文件 `WdUIComposerTestForm.cs` 中被调用过）。

**这意味着 MainLayout.json 中的控件（toolbarPanel、mainSplitter 等）在 RenderPipeline 的 6 个阶段中根本没有被创建！**

### 2.2 问题2：控件创建与布局分离的两条路径

框架中存在两条独立的路径：

| 路径 | 创建控件 | 布局控件 | 调用方 |
|------|---------|---------|-------|
| **旧路径** | `ControlFactory.CreateLayoutPanel` → `CreateControl` | `WdUIComposer.ApplyLayout` → `LayoutMediator` | `WdUIComposerTestForm`（测试用） |
| **新路径** | ❌ **没有创建控件** | `RenderPipeline` 6阶段 | `ConfigContainer.RenderConfigAsync` |

**新路径（RenderPipeline）只负责布局，不负责创建控件。** 但 `CreateLayoutPanelStage` 创建了一个空的 LayoutPanel，然后 `ExecuteStrategyStage` 中的 `PanelStrategy` 调用 `context.ApplyLayout(panel, config.Controls)` 尝试布局这些不存在的控件。

### 2.3 问题3：PanelStrategy 的 ApplyLayout 只设置位置不创建控件

`PanelStrategy.Execute` 中：

```csharp
// 检查是否有 layout 属性的子控件
var hasLayout = config.Controls.Any(c => c.Layout != null);
if (!hasLayout) return;

// 通过 LayoutMediator 应用比例布局（只设置位置和大小，不创建控件）
context.ApplyLayout(panel, config.Controls);

// 遍历子控件，尝试执行策略
foreach (var childConfig in config.Controls)
{
    var child = context.FindControl(childConfig.Name);  // 通过 Application.OpenForms 全局查找
    if (child != null)
    {
        context.TryExecuteStrategy(child, childConfig);
    }
}
```

`context.FindControl` 通过 `Application.OpenForms` 全局查找控件。如果控件不存在（没有被创建），则 `child` 为 null，策略不会执行。

### 2.4 问题4：ProcessNestedStage 的 FindChildControlByName 找不到控件

`ProcessNestedStage.ProcessNestedControls` 中：

```csharp
var child = FindChildControlByName(parent, controlConfig.Name);
if (child == null) continue;  // 找不到控件，跳过
```

`FindChildControlByName` 在 LayoutPanel 内部递归查找。如果控件没有被创建，则找不到。

### 2.5 问题5：ApplySplitterStage 的 SplitterDistance 条件不满足

`ApplySplitterStage` 中：

```csharp
var state = context.LayoutContext.State.GetOrCreateValue(splitter);
if (state.Config?.SplitterDistance > 0 && state.Config.SplitterDistance <= 1)
{
    // 设置 SplitterDistance
}
```

`state.Config` 是通过 `CacheConfigStage` 缓存的配置。但 `CacheConfigStage` 创建了一个 `wrapperConfig`，只设置了 `Type`、`Name`、`Controls`，**没有设置 `SplitterDistance`**。

所以 `state.Config?.SplitterDistance` 是 `null`（默认值），条件不满足，`SplitterDistance` 没有被应用。

---

## 三、为什么"右侧显示出来了"？

用户说"右侧显示出来了"，但根据上述分析，MainLayout.json 中的控件根本没有被创建。

**可能的解释：**

1. **sideContainer 是 ConfigContainer**，它在 `ControlFactory.CreateControl` 中创建（type: "container"），但 `CreateControl` 没有被调用。

2. **但 sideContainer 加载 WorkflowRunner.json 后显示出了内容**，这是因为 `LoadSideContainer` 方法直接调用了 `_sideContainer.LoadConfigAsync(clonedConfig)`，而 `_sideContainer` 是通过 `_rootContainer.FindSubContainer("sideContainer")` 获取的。

3. **`FindSubContainer` 在 `ScanAndRegisterSubContainers` 中注册**，而 `ScanAndRegisterSubContainers` 在 `RenderConfigAsync` 中调用。

4. **但 `ScanAndRegisterSubContainers` 扫描的是 `_rootContainer.Controls` 中的控件**，如果 LayoutPanel 中没有子控件，则找不到 `sideContainer`。

**所以"右侧显示出来了"可能是指 sideContainer 加载 WorkflowRunner.json 后显示出了内容，但 sideContainer 本身可能没有被正确创建和布局。**

---

## 四、根本原因总结

| 问题 | 原因 | 影响 |
|------|------|------|
| **控件未创建** | `CreateLayoutPanelStage` 只创建空 Panel，未调用 `ControlFactory.CreateLayoutPanel` | MainLayout.json 中的所有控件（toolbarPanel、mainSplitter、sideContainer 等）未被创建 |
| **布局找不到控件** | `PanelStrategy` 和 `ProcessNestedStage` 通过 `FindControl`/`FindChildControlByName` 查找控件，但控件不存在 | 布局策略无法执行 |
| **SplitterDistance 未应用** | `CacheConfigStage` 缓存的配置不包含 `SplitterDistance` | `ApplySplitterStage` 条件不满足，分割条位置未设置 |
| **两条路径分离** | 旧路径（`CreateLayoutPanel` + `ApplyLayout`）和新路径（`RenderPipeline` 6阶段）分离 | 控件创建和布局逻辑脱节 |

---

## 五、修复方案

### 方案1：让 CreateLayoutPanelStage 调用 ControlFactory.CreateLayoutPanel

修改 `CreateLayoutPanelStage`，让它调用 `ControlFactory.CreateLayoutPanel` 来创建 LayoutPanel 并添加子控件：

```csharp
public class CreateLayoutPanelStage : IPipelineStage
{
    public string Name => "CreateLayoutPanel";

    public void Execute(Control target, UIConfiguration config, PipelineContext context)
    {
        var pending = new List<(Panel panel, ViewConfig view)>();

        target.SuspendLayout();
        try
        {
            foreach (var view in config.Views)
            {
                // ★ 使用 ControlFactory.CreateLayoutPanel 创建 LayoutPanel 并添加子控件
                var panel = WdUIComposer.CreateLayoutPanel(view);
                target.Controls.Add(panel);
                pending.Add((panel, view));
            }
        }
        finally
        {
            target.ResumeLayout(true);
        }

        context.Items["__pendingPanels"] = pending;
    }
}
```

**优点：** 控件被正确创建，后续阶段可以正常查找和布局。
**缺点：** `ControlFactory.CreateLayoutPanel` 中已经设置了子控件的 `Dock` 和 `Tag`，后续的 `PanelStrategy` 可能会重复设置。

### 方案2：在 RenderPipeline.Render 之前创建控件

在 `ConfigContainer.RenderConfigAsync` 中，在调用 `RenderPipeline.Render` 之前，先调用 `ControlFactory.CreateLayoutPanel` 创建控件：

```csharp
private async Task RenderConfigAsync(UIConfiguration config)
{
    SuspendLayout();
    try
    {
        WdUIComposer.RenderPipeline.DisposeSession(this);
        Controls.Clear();
        _subContainers.Clear();
        _currentConfig = config;

        // ★ 先创建 LayoutPanel 并添加子控件
        foreach (var view in config.Views)
        {
            var panel = WdUIComposer.CreateLayoutPanel(view);
            Controls.Add(panel);
        }

        // ★ 再执行 RenderPipeline 进行布局
        WdUIComposer.RenderPipeline.Render(this, config);
    }
    finally
    {
        ResumeLayout(true);
    }
    // ...
}
```

**优点：** 控件在 RenderPipeline 执行前已被创建，后续阶段可以正常查找和布局。
**缺点：** `CreateLayoutPanelStage` 会再次创建空的 LayoutPanel，导致重复。

### 方案3：合并两条路径，统一使用 RenderPipeline

将 `ControlFactory.CreateLayoutPanel` 的逻辑合并到 `CreateLayoutPanelStage` 中，让 RenderPipeline 同时负责控件创建和布局：

```csharp
public class CreateLayoutPanelStage : IPipelineStage
{
    public string Name => "CreateLayoutPanel";

    public void Execute(Control target, UIConfiguration config, PipelineContext context)
    {
        var pending = new List<(Panel panel, ViewConfig view)>();

        target.SuspendLayout();
        try
        {
            foreach (var view in config.Views)
            {
                var panel = new Panel
                {
                    Name = view.Name,
                    Dock = DockStyle.Fill
                };

                // ★ 创建子控件
                if (view.Controls != null)
                {
                    foreach (var controlConfig in view.Controls)
                    {
                        var control = WdUIComposer.CreateControl(controlConfig);
                        panel.Controls.Add(control);
                    }
                }

                target.Controls.Add(panel);
                pending.Add((panel, view));
            }
        }
        finally
        {
            target.ResumeLayout(true);
        }

        context.Items["__pendingPanels"] = pending;
    }
}
```

**优点：** 统一了控件创建和布局的入口，逻辑清晰。
**缺点：** 需要确保 `WdUIComposer.CreateControl` 在 Pipeline 上下文中可用。

---

## 六、推荐修复方案

**推荐方案3**：合并两条路径，统一使用 RenderPipeline。

理由：
1. RenderPipeline 是框架的核心布局入口，应该同时负责控件创建和布局。
2. `ControlFactory.CreateLayoutPanel` 是旧路径的遗留代码，应该被整合到 RenderPipeline 中。
3. 统一入口后，后续的 Resize 重布局（Relayout）也能正确处理。

### 具体修改步骤

1. **修改 `CreateLayoutPanelStage`**：在创建 LayoutPanel 的同时，调用 `WdUIComposer.CreateControl` 创建子控件。
2. **确保 `CacheConfigStage` 缓存完整的配置**：包括 `SplitterDistance` 等属性。
3. **确保 `PanelStrategy` 和 `ProcessNestedStage` 能正确查找控件**：控件被创建后，`FindControl` 和 `FindChildControlByName` 能正常找到。
4. **确保 `ApplySplitterStage` 能正确应用 SplitterDistance**：`state.Config` 包含 `SplitterDistance`。

### 验证方法

1. 启动应用，检查 mainSplitter 的分割条是否显示。
2. 拖拽分割条，检查 sideContainer 的大小是否变化。
3. 加载 WorkflowRunner.json 到 sideContainer，检查内容是否正常显示。
4. 调整窗口大小，检查布局是否正确重排。
