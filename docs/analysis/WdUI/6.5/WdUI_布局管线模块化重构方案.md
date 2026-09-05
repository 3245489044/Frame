# WdUI 布局管线模块化重构方案

> **问题背景**：SplitContainer 支持方案的实现过程中，发现当前架构存在"布局管线的时序依赖"问题——每个新容器类型都需要手动在 `ApplyNestedLayoutsRecursive` 中添加特殊处理，且必须知道正确的时序（先设 SplitterDistance 再布局子控件）。这种扁平的、手动的、时序敏感的架构难以扩展和维护。

---

## 目录

- [WdUI 布局管线模块化重构方案](#wdui-布局管线模块化重构方案)
  - [目录](#目录)
  - [一、当前架构的问题分析](#一当前架构的问题分析)
    - [1.1 当前渲染管线](#11-当前渲染管线)
    - [1.2 核心问题](#12-核心问题)
    - [1.3 新增容器类型的成本](#13-新增容器类型的成本)
  - [二、核心思路：布局管线模式](#二核心思路布局管线模式)
    - [管线阶段说明](#管线阶段说明)
  - [三、模块划分](#三模块划分)
    - [模块 1：布局策略注册表（LayoutStrategyRegistry）](#模块-1布局策略注册表layoutstrategyregistry)
      - [接口定义](#接口定义)
      - [注册表实现](#注册表实现)
      - [内置策略](#内置策略)
        - [SplitContainerStrategy](#splitcontainerstrategy)
        - [PanelStrategy](#panelstrategy)
        - [GroupBoxStrategy](#groupboxstrategy)
    - [模块 2：布局上下文（ILayoutContext）](#模块-2布局上下文ilayoutcontext)
    - [模块 3：渲染管线（RenderPipeline）](#模块-3渲染管线renderpipeline)
  - [四、分阶段实施计划](#四分阶段实施计划)
    - [第一阶段：提取布局策略](#第一阶段提取布局策略)
    - [第二阶段：重构渲染管线](#第二阶段重构渲染管线)
    - [第三阶段：统一状态管理](#第三阶段统一状态管理)
    - [第四阶段：Resize 事件统一管理](#第四阶段resize-事件统一管理)
  - [五、架构对比](#五架构对比)
    - [当前架构](#当前架构)
    - [重构后架构](#重构后架构)
    - [文件结构变化](#文件结构变化)
  - [六、关键收益](#六关键收益)
  - [七、后续扩展](#七后续扩展)
    - [7.1 新增容器类型示例](#71-新增容器类型示例)
      - [TabControl](#tabcontrol)
      - [FlowLayoutPanel](#flowlayoutpanel)
    - [7.2 自定义容器类型](#72-自定义容器类型)
    - [7.3 未来可能的优化方向](#73-未来可能的优化方向)

---

## 一、当前架构的问题分析

### 1.1 当前渲染管线

```
RenderFromJson()
  ├─ ApplySmartLayout(panel, wrapperConfig)     // 阶段1：布局第一层控件
  ├─ ApplyNestedLayoutsRecursive(panel, ...)     // 阶段2：递归布局嵌套控件
  │   └─ 遇到 SplitContainer → 先设 SplitterDistance → 再布局 Panel1/Panel2
  └─ ApplySplitterDistances(target)              // 阶段3：再次设置 SplitterDistance
```

### 1.2 核心问题

| 问题 | 描述 | 影响 |
|------|------|------|
| **时序敏感** | 必须先设 SplitterDistance 再布局子控件，顺序错误导致布局异常 | 新增容器类型容易踩坑 |
| **手动递归** | `ApplyNestedLayoutsRecursive` 中硬编码了每种容器类型的处理逻辑 | 每新增一种容器类型都要修改该方法 |
| **逻辑分散** | SplitContainer 的特殊处理散布在 3 个方法中（CreateControl、ApplyNestedLayoutsRecursive、ApplySplitterDistances） | 关注点不分离，修改一处容易遗漏其他处 |
| **状态分散** | 运行时状态分散在多个 static 字段中（`_splitterRatios`、`_panelConfigCache` 等） | 状态管理混乱，难以追踪 |
| **Resize 事件管理分散** | ConfigContainer 和 LayoutMediator 各自管理 Resize 事件 | 重复订阅、弱引用事件等问题 |

### 1.3 新增容器类型的成本

当前要新增一种容器类型（如 TabControl），需要修改：

| 文件 | 修改内容 | 风险 |
|------|---------|------|
| `WdUI.Framework.cs` | `CreateControl` 中添加控件创建逻辑 | 低 |
| `WdUIComposer.cs` | `ApplyNestedLayoutsRecursive` 中添加特殊判断 | **高**（时序依赖） |
| `WdUIComposer.cs` | 可能需要添加新的 ApplyXxx 方法 | 中 |
| `ConfigContainer.cs` | 可能需要修改 `OnContainerResize` | 中 |

**目标：从修改 3-4 个文件降低到只需新增 1 个策略类。**

---

## 二、核心思路：布局管线模式

将当前的**手动递归遍历**改为**声明式管线**，每个容器类型注册自己的"布局策略"：

```
┌─────────────────────────────────────────────────────────┐
│                    LayoutPipeline                        │
│                                                         │
│  Stage 1: Measure  ── 计算所有控件的期望大小              │
│       │                                                  │
│  Stage 2: Arrange  ── 按策略排列控件（比例/堆叠/网格）     │
│       │                                                  │
│  Stage 3: PostProcess ── 容器后处理（SplitterDistance等） │
│       │                                                  │
│  Stage 4: Recursive ── 递归处理嵌套容器                   │
└─────────────────────────────────────────────────────────┘
```

### 管线阶段说明

| 阶段 | 名称 | 说明 | 示例 |
|------|------|------|------|
| 1 | Measure | 计算所有控件的期望大小（比例→像素转换） | `LayoutMediator.Calculate()` |
| 2 | Arrange | 按策略排列控件位置 | `ApplySmartLayout()` |
| 3 | PostProcess | 容器后处理，设置容器特有的属性 | `SplitterDistance` 设置 |
| 4 | Recursive | 递归处理嵌套容器 | 遍历 Panel1/Panel2 的子控件 |

---

## 三、模块划分

### 模块 1：布局策略注册表（LayoutStrategyRegistry）

#### 接口定义

```csharp
/// <summary>
/// 布局策略接口
/// 每个容器类型注册自己的布局策略，实现关注点分离
/// </summary>
public interface ILayoutStrategy
{
    /// <summary>
    /// 容器类型标识（小写）
    /// 例如："splitcontainer"、"groupbox"、"panel"、"tabcontrol"
    /// </summary>
    string ContainerType { get; }

    /// <summary>
    /// 执行顺序（越小越先执行）
    /// </summary>
    int StageOrder { get; }

    /// <summary>
    /// 执行布局策略
    /// </summary>
    /// <param name="control">已创建的控件实例</param>
    /// <param name="config">控件的配置</param>
    /// <param name="context">布局上下文</param>
    void Execute(Control control, ControlConfig config, ILayoutContext context);
}
```

#### 注册表实现

```csharp
/// <summary>
/// 布局策略注册表
/// 管理所有容器类型的布局策略
/// </summary>
public class LayoutStrategyRegistry
{
    private readonly Dictionary<string, ILayoutStrategy> _strategies = new();

    /// <summary>
    /// 注册策略
    /// </summary>
    public void RegisterStrategy(ILayoutStrategy strategy)
    {
        _strategies[strategy.ContainerType] = strategy;
    }

    /// <summary>
    /// 获取策略
    /// </summary>
    public ILayoutStrategy? GetStrategy(string containerType)
    {
        return _strategies.TryGetValue(containerType, out var strategy) 
            ? strategy : null;
    }

    /// <summary>
    /// 执行策略（如果存在）
    /// </summary>
    public bool TryExecute(Control control, ControlConfig config, ILayoutContext context)
    {
        var type = config.Type?.ToLowerInvariant();
        if (type == null) return false;

        var strategy = GetStrategy(type);
        if (strategy == null) return false;

        strategy.Execute(control, config, context);
        return true;
    }
}
```

#### 内置策略

##### SplitContainerStrategy

```csharp
/// <summary>
/// SplitContainer 布局策略
/// 处理 SplitterDistance 设置、Panel1/Panel2 子控件布局、SplitterMoved 事件订阅
/// </summary>
public class SplitContainerStrategy : ILayoutStrategy
{
    public string ContainerType => "splitcontainer";
    public int StageOrder => 10; // 在 Arrange 之后执行

    public void Execute(Control control, ControlConfig config, ILayoutContext context)
    {
        if (control is not SplitContainer splitter) return;

        // Stage 3: PostProcess - 设置 SplitterDistance
        if (config.SplitterDistance > 0 && config.SplitterDistance <= 1)
        {
            int splitterWidth = splitter.Orientation == Orientation.Vertical
                ? splitter.Width : splitter.Height;
            splitter.SplitterDistance = (int)(splitterWidth * config.SplitterDistance);
        }

        // Stage 4: Recursive - 布局 Panel1 子控件
        if (config.Panel1Controls != null && config.Panel1Controls.Count > 0)
        {
            ApplySplitterPanelLayout(splitter.Panel1, config.Panel1Controls, context);
        }

        // Stage 4: Recursive - 布局 Panel2 子控件
        if (config.Panel2Controls != null && config.Panel2Controls.Count > 0)
        {
            ApplySplitterPanelLayout(splitter.Panel2, config.Panel2Controls, context);
        }

        // 订阅 SplitterMoved 事件，记录用户调整后的比例
        splitter.SplitterMoved += (sender, e) =>
        {
            if (sender is SplitContainer s)
            {
                double ratio = s.Orientation == Orientation.Vertical
                    ? (double)s.SplitterDistance / s.Width
                    : (double)s.SplitterDistance / s.Height;
                context.State.GetOrCreateValue(s).Ratio = ratio;
            }
        };
    }

    private void ApplySplitterPanelLayout(Control panel, List<ControlConfig> controls, ILayoutContext context)
    {
        var hasLayout = controls.Any(c => c.Layout != null);
        if (!hasLayout) return;

        context.ApplyLayout(panel, controls);

        // 递归处理嵌套容器
        foreach (var childConfig in controls)
        {
            var child = context.FindControl(childConfig.Name);
            if (child != null)
            {
                context.TryExecuteStrategy(child, childConfig);
            }
        }
    }
}
```

##### PanelStrategy

```csharp
/// <summary>
/// 普通 Panel 布局策略
/// 布局内部 Controls → 递归
/// </summary>
public class PanelStrategy : ILayoutStrategy
{
    public string ContainerType => "panel";
    public int StageOrder => 20;

    public void Execute(Control control, ControlConfig config, ILayoutContext context)
    {
        if (config.Controls == null || config.Controls.Count == 0) return;

        var hasLayout = config.Controls.Any(c => c.Layout != null);
        if (!hasLayout) return;

        context.ApplyLayout(control, config.Controls);

        // 递归处理嵌套容器
        foreach (var childConfig in config.Controls)
        {
            var child = context.FindControl(childConfig.Name);
            if (child != null)
            {
                context.TryExecuteStrategy(child, childConfig);
            }
        }
    }
}
```

##### GroupBoxStrategy

```csharp
/// <summary>
/// GroupBox 布局策略
/// 与 Panel 类似，但 GroupBox 有标题区域需要额外考虑
/// </summary>
public class GroupBoxStrategy : ILayoutStrategy
{
    public string ContainerType => "groupbox";
    public int StageOrder => 20;

    public void Execute(Control control, ControlConfig config, ILayoutContext context)
    {
        // 与 PanelStrategy 逻辑相同，但需要考虑 GroupBox 的标题区域偏移
        // GroupBox 的 ClientRectangle 已经排除了标题区域
        if (config.Controls == null || config.Controls.Count == 0) return;

        var hasLayout = config.Controls.Any(c => c.Layout != null);
        if (!hasLayout) return;

        context.ApplyLayout(control, config.Controls);

        foreach (var childConfig in config.Controls)
        {
            var child = context.FindControl(childConfig.Name);
            if (child != null)
            {
                context.TryExecuteStrategy(child, childConfig);
            }
        }
    }
}
```

### 模块 2：布局上下文（ILayoutContext）

```csharp
/// <summary>
/// 布局上下文接口
/// 提供布局过程中需要的所有服务和状态
/// </summary>
public interface ILayoutContext
{
    /// <summary>
    /// 运行时状态存储（替代 Control.Tag 和 static 字段）
    /// 使用 ConditionalWeakTable 避免内存泄漏
    /// </summary>
    ConditionalWeakTable<Control, LayoutState> State { get; }

    /// <summary>
    /// 在指定父容器中查找控件
    /// </summary>
    Control? FindControl(string name);

    /// <summary>
    /// 应用比例布局到父容器
    /// </summary>
    void ApplyLayout(Control parent, List<ControlConfig> configs);

    /// <summary>
    /// 尝试执行控件的布局策略
    /// </summary>
    bool TryExecuteStrategy(Control control, ControlConfig config);

    /// <summary>
    /// 订阅 Resize 事件（自动去重）
    /// </summary>
    void SubscribeResize(Control control, Action<Control> callback);

    /// <summary>
    /// 获取 SplitContainer 的保存比例
    /// </summary>
    double? GetSplitterRatio(SplitContainer splitter);
}

/// <summary>
/// 布局运行时状态
/// 存储每个控件在布局过程中的运行时数据
/// </summary>
public class LayoutState
{
    /// <summary>SplitContainer 的用户调整比例</summary>
    public double? SplitterRatio { get; set; }

    /// <summary>控件的布局配置缓存</summary>
    public ControlConfig? Config { get; set; }

    /// <summary>是否已订阅 Resize 事件</summary>
    public bool IsResizeSubscribed { get; set; }
}
```

### 模块 3：渲染管线（RenderPipeline）

```csharp
/// <summary>
/// 渲染管线
/// 封装完整的布局渲染流程，提供统一的 Render 和 Relayout 入口
/// </summary>
public class RenderPipeline
{
    private readonly LayoutStrategyRegistry _registry;
    private readonly ILayoutContext _context;

    public RenderPipeline(LayoutStrategyRegistry registry, ILayoutContext context)
    {
        _registry = registry;
        _context = context;
    }

    /// <summary>
    /// 渲染入口
    /// </summary>
    public void Render(Control target, UIConfiguration config)
    {
        var pending = new List<(Panel panel, ViewConfig view)>();

        target.SuspendLayout();
        try
        {
            foreach (var view in config.Views)
            {
                var panel = CreateLayoutPanel(view);
                target.Controls.Add(panel);
                pending.Add((panel, view));
            }
        }
        finally
        {
            target.ResumeLayout(true);
        }

        foreach (var (panel, view) in pending)
        {
            var wrapperConfig = new ControlConfig
            {
                Type = "panel",
                Name = view.Name,
                Controls = view.Controls
            };

            // 缓存配置
            _context.State.GetOrCreateValue(panel).Config = wrapperConfig;

            // 执行布局策略
            _context.TryExecuteStrategy(panel, wrapperConfig);

            // 递归处理嵌套控件
            ProcessNestedControls(panel, view.Controls);
        }

        // 后处理：应用 SplitContainer 的初始分割位置
        ApplySplitterDistances(target);
    }

    /// <summary>
    /// 重新布局入口（Resize 时调用）
    /// </summary>
    public void Relayout(Control target)
    {
        // 先恢复 SplitContainer 的分割比例
        RestoreSplitterDistances(target);

        // 重新应用布局
        var allConfigs = CollectLayoutConfigs(target);
        if (allConfigs.Count > 0)
        {
            _context.ApplyLayout(target, allConfigs);
        }
    }

    private void ProcessNestedControls(Control parent, List<ControlConfig> controls)
    {
        foreach (var controlConfig in controls)
        {
            var child = _context.FindControl(controlConfig.Name);
            if (child == null) continue;

            // 尝试执行策略
            if (!_context.TryExecuteStrategy(child, controlConfig))
            {
                // 没有注册策略的控件类型，如果有子控件则递归处理
                if (controlConfig.Controls != null && controlConfig.Controls.Count > 0)
                {
                    ProcessNestedControls(child, controlConfig.Controls);
                }
            }
        }
    }

    private void RestoreSplitterDistances(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            if (control is SplitContainer splitter)
            {
                var ratio = _context.GetSplitterRatio(splitter);
                if (ratio.HasValue)
                {
                    int splitterWidth = splitter.Orientation == Orientation.Vertical
                        ? splitter.Width : splitter.Height;
                    splitter.SplitterDistance = (int)(splitterWidth * ratio.Value);
                }

                // 递归处理 SplitContainer 内部的 Panel
                RestoreSplitterDistances(splitter.Panel1);
                RestoreSplitterDistances(splitter.Panel2);
            }
            else if (control.HasChildren)
            {
                RestoreSplitterDistances(control);
            }
        }
    }

    private void ApplySplitterDistances(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            if (control is SplitContainer splitter)
            {
                var state = _context.State.GetOrCreateValue(splitter);
                if (state.Config?.SplitterDistance > 0 && state.Config.SplitterDistance <= 1)
                {
                    int splitterWidth = splitter.Orientation == Orientation.Vertical
                        ? splitter.Width : splitter.Height;
                    splitter.SplitterDistance = (int)(splitterWidth * state.Config.SplitterDistance);
                }

                ApplySplitterDistances(splitter.Panel1);
                ApplySplitterDistances(splitter.Panel2);
            }
            else if (control.HasChildren)
            {
                ApplySplitterDistances(control);
            }
        }
    }

    private List<ControlConfig> CollectLayoutConfigs(Control parent)
    {
        var configs = new List<ControlConfig>();
        foreach (Control control in parent.Controls)
        {
            var state = _context.State.GetOrCreateValue(control);
            if (state.Config?.Layout != null)
            {
                configs.Add(state.Config);
            }

            if (control.HasChildren)
            {
                configs.AddRange(CollectLayoutConfigs(control));
            }
        }
        return configs;
    }

    private static Panel CreateLayoutPanel(ViewConfig view)
    {
        return new Panel
        {
            Name = view.Name,
            Dock = DockStyle.Fill
        };
    }
}
```

---

## 四、分阶段实施计划

### 第一阶段：提取布局策略

**目标**：创建策略注册表和内置策略，消除 `ApplyNestedLayoutsRecursive` 中的特殊判断。

| 步骤 | 内容 | 涉及文件 | 预计工时 |
|------|------|---------|---------|
| 1.1 | 创建 `ILayoutStrategy` 接口和 `LayoutStrategyRegistry` | 新建 `WdUI/Layout/Strategies/ILayoutStrategy.cs` | 1h |
| 1.2 | 创建 `SplitContainerStrategy` | 新建 `WdUI/Layout/Strategies/SplitContainerStrategy.cs` | 2h |
| 1.3 | 创建 `PanelStrategy` | 新建 `WdUI/Layout/Strategies/PanelStrategy.cs` | 0.5h |
| 1.4 | 创建 `GroupBoxStrategy` | 新建 `WdUI/Layout/Strategies/GroupBoxStrategy.cs` | 0.5h |
| 1.5 | 创建 `ILayoutContext` 接口和 `LayoutContext` 实现 | 新建 `WdUI/Layout/ILayoutContext.cs` | 1h |
| 1.6 | 创建 `LayoutState` 类 | 新建 `WdUI/Layout/LayoutState.cs` | 0.5h |
| 1.7 | 单元测试：验证策略注册和执行 | 新建测试 | 1h |

**完成标志**：`ApplyNestedLayoutsRecursive` 中的 `if (childControl is SplitContainer)` 特殊判断消失，变成统一的 `context.TryExecuteStrategy(child, childConfig)` 调用。

### 第二阶段：重构渲染管线

**目标**：创建 `RenderPipeline` 类，将 `WdUIComposer` 的渲染逻辑委托给管线。

| 步骤 | 内容 | 涉及文件 | 预计工时 |
|------|------|---------|---------|
| 2.1 | 创建 `RenderPipeline` 类 | 新建 `WdUI/Layout/RenderPipeline.cs` | 3h |
| 2.2 | 将 `WdUIComposer.RenderFromJson` 委托给 `RenderPipeline.Render` | 修改 `WdUIComposer.cs` | 1h |
| 2.3 | 将 `ConfigContainer.OnContainerResize` 委托给 `RenderPipeline.Relayout` | 修改 `ConfigContainer.cs` | 1h |
| 2.4 | 删除 `ApplyNestedLayoutsRecursive`、`ApplySplitterPanelLayout` 等手动方法 | 清理 `WdUIComposer.cs` | 0.5h |
| 2.5 | 构建验证 | `dotnet build` | 0.5h |

**完成标志**：`WdUIComposer.cs` 减少 200+ 行手动递归代码，`ConfigContainer.cs` 的 `OnContainerResize` 简化为委托调用。

### 第三阶段：统一状态管理

**目标**：将分散的运行时状态集中到 `ILayoutContext.State` 中管理。

| 步骤 | 内容 | 涉及文件 | 预计工时 |
|------|------|---------|---------|
| 3.1 | 将 `ControlFactory._splitterRatios` 迁移到 `ILayoutContext.State` | 修改 `WdUI.Framework.cs` | 1h |
| 3.2 | 将 `WdUIComposer._panelConfigCache` 迁移到 `ILayoutContext.State` | 修改 `WdUIComposer.cs` | 0.5h |
| 3.3 | 统一 `ApplySplitterDistances` 和 `RestoreSplitterDistances` 为策略内部逻辑 | 清理 | 1h |
| 3.4 | 构建验证 | `dotnet build` | 0.5h |

**完成标志**：不再有分散的 static 字段管理运行时状态，所有状态通过 `ILayoutContext.State` 访问。

### 第四阶段：Resize 事件统一管理

**目标**：在 `RenderPipeline` 中统一管理 Resize 事件订阅，消除重复订阅和弱引用事件。

| 步骤 | 内容 | 涉及文件 | 预计工时 |
|------|------|---------|---------|
| 4.1 | 在 `RenderPipeline` 中实现 Resize 事件统一管理 | 修改 `RenderPipeline.cs` | 1h |
| 4.2 | 消除 `ConfigContainer.EnsureLayoutSubscribed` 的重复订阅检查 | 修改 `ConfigContainer.cs` | 0.5h |
| 4.3 | 消除 `LayoutMediator.ApplyAndTrackResize` 的弱引用事件 | 修改 `LayoutApplier.cs` | 0.5h |
| 4.4 | 集成测试：验证 Resize 时布局正确恢复 | 手动测试 | 1h |

**完成标志**：Resize 事件订阅统一在 `RenderPipeline` 中管理，`ConfigContainer` 不再需要手动管理 Resize 事件。

---

## 五、架构对比

### 当前架构

```
WdUIComposer (2000+ 行，包含所有逻辑)
  ├─ RenderFromJson()              // 渲染入口
  ├─ ApplySmartLayout()            // 布局计算
  ├─ ApplyNestedLayoutsRecursive() // 手动递归 + 特殊判断
  ├─ ApplySplitterPanelLayout()    // SplitContainer 特殊处理
  ├─ ApplySplitterDistances()      // SplitterDistance 设置
  ├─ RestoreSplitterDistances()    // Resize 恢复
  ├─ OnPanelResize()               // Resize 事件
  └─ ... 其他 20+ 方法

ConfigContainer (400+ 行)
  ├─ RenderConfigAsync()           // 渲染入口
  ├─ OnContainerResize()           // Resize 事件 + 手动调用 RestoreSplitterDistances
  └─ CollectLayoutConfigs()        // 手动收集 layout 配置

WdUI.Framework.cs (500+ 行)
  ├─ ControlFactory.CreateControl() // 控件创建 + SplitContainer 特殊处理
  ├─ OnSplitterMoved()              // SplitterMoved 事件处理
  └─ GetSplitterRatio()             // 获取保存的比例
```

### 重构后架构

```
WdUIComposer (精简为 500+ 行，只保留对外 API)
  └─ 委托给 RenderPipeline

RenderPipeline (新建，300+ 行)
  ├─ Render()                       // 渲染入口
  ├─ Relayout()                     // Resize 入口
  ├─ ProcessNestedControls()        // 统一递归处理
  ├─ RestoreSplitterDistances()     // 恢复分割比例
  ├─ ApplySplitterDistances()       // 应用分割比例
  └─ CollectLayoutConfigs()         // 收集布局配置

LayoutStrategyRegistry (新建，100+ 行)
  ├─ RegisterStrategy()
  ├─ GetStrategy()
  └─ TryExecute()

SplitContainerStrategy (新建，80+ 行)
  ├─ Execute()                      // 先设 SplitterDistance → 布局子控件 → 订阅事件
  └─ ApplySplitterPanelLayout()     // Panel1/Panel2 布局

PanelStrategy (新建，30+ 行)
  └─ Execute()                      // 布局子控件 → 递归

GroupBoxStrategy (新建，30+ 行)
  └─ Execute()                      // 布局子控件 → 递归（考虑标题偏移）

ILayoutContext (新建，50+ 行)
  ├─ State                          // 运行时状态
  ├─ FindControl()                  // 控件查找
  ├─ ApplyLayout()                  // 布局计算
  ├─ TryExecuteStrategy()           // 策略执行
  ├─ SubscribeResize()              // Resize 订阅
  └─ GetSplitterRatio()             // 获取比例

ConfigContainer (精简为 200+ 行)
  └─ 委托给 RenderPipeline.Relayout()

WdUI.Framework.cs (精简为 400+ 行)
  └─ ControlFactory.CreateControl()  // 只保留控件创建逻辑
```

### 文件结构变化

```
WdUI/
├── UI/
│   ├── WdUIComposer.cs              ← 精简
│   ├── ConfigContainer.cs           ← 精简
│   ├── WdUI.Framework.cs            ← 精简
│   ├── WdUI.Configuration.cs        ← 不变
│   ├── LayoutApplier.cs             ← 不变
│   ├── Managers.cs                  ← 不变
│   └── ...
├── Layout/                          ← 新增目录
│   ├── RenderPipeline.cs            ← 新增
│   ├── ILayoutContext.cs            ← 新增
│   ├── LayoutState.cs               ← 新增
│   ├── LayoutStrategyRegistry.cs    ← 新增
│   └── Strategies/                  ← 新增目录
│       ├── ILayoutStrategy.cs       ← 新增
│       ├── SplitContainerStrategy.cs ← 新增
│       ├── PanelStrategy.cs         ← 新增
│       └── GroupBoxStrategy.cs      ← 新增
└── docs/
    └── WdUI_布局管线模块化重构方案.md  ← 本文档
```

---

## 六、关键收益

| 指标 | 当前 | 重构后 | 提升 |
|------|------|--------|------|
| WdUIComposer 行数 | ~2000 | ~500 | **-75%** |
| 新增容器类型的成本 | 修改 3-4 个文件 | 新增 1 个 Strategy 类 | **从小时级到分钟级** |
| SplitContainer 特殊判断 | 散布在 3 个方法中 | 集中在 1 个 Strategy 中 | **关注点分离** |
| 时序依赖风险 | 手动保证 | 管线自动保证 | **消除人为错误** |
| Resize 事件管理 | 散布在 2 个类中 | 统一在 Pipeline 中 | **单一职责** |
| 运行时状态 | 3 个 static 字段 | 1 个 ILayoutContext | **集中管理** |
| 代码可测试性 | 低（静态方法多） | 高（接口注入） | **大幅提升** |
| 可扩展性 | 低（硬编码） | 高（策略注册） | **大幅提升** |

---

## 七、后续扩展

### 7.1 新增容器类型示例

#### TabControl

```csharp
public class TabControlStrategy : ILayoutStrategy
{
    public string ContainerType => "tabcontrol";
    public int StageOrder => 20;

    public void Execute(Control control, ControlConfig config, ILayoutContext context)
    {
        if (control is not TabControl tabControl) return;

        // 布局每个 TabPage
        foreach (TabPage tabPage in tabControl.TabPages)
        {
            var tabConfig = config.Controls?.FirstOrDefault(c => c.Name == tabPage.Name);
            if (tabConfig?.Controls != null)
            {
                context.ApplyLayout(tabPage, tabConfig.Controls);
                
                foreach (var childConfig in tabConfig.Controls)
                {
                    var child = context.FindControl(childConfig.Name);
                    if (child != null)
                    {
                        context.TryExecuteStrategy(child, childConfig);
                    }
                }
            }
        }
    }
}

// 注册
registry.RegisterStrategy(new TabControlStrategy());
```

#### FlowLayoutPanel

```csharp
public class FlowLayoutStrategy : ILayoutStrategy
{
    public string ContainerType => "flowlayoutpanel";
    public int StageOrder => 20;

    public void Execute(Control control, ControlConfig config, ILayoutContext context)
    {
        // FlowLayoutPanel 自动布局，不需要手动设置位置
        // 但需要递归处理嵌套容器
        if (config.Controls == null) return;

        foreach (var childConfig in config.Controls)
        {
            var child = context.FindControl(childConfig.Name);
            if (child != null)
            {
                context.TryExecuteStrategy(child, childConfig);
            }
        }
    }
}
```

### 7.2 自定义容器类型

外部使用者可以通过注册自定义策略来支持自己的容器类型：

```csharp
// 在应用启动时注册
var registry = new LayoutStrategyRegistry();
registry.RegisterStrategy(new MyCustomContainerStrategy());

var pipeline = new RenderPipeline(registry, context);
pipeline.Render(target, config);
```

### 7.3 未来可能的优化方向

| 方向 | 说明 | 优先级 |
|------|------|--------|
| **异步渲染** | 支持异步加载和渲染，避免 UI 线程阻塞 | 中 |
| **布局预览** | 在渲染前预览布局效果，支持设计时调整 | 低 |
| **布局动画** | 支持 Resize 时的平滑过渡动画 | 低 |
| **布局快照** | 支持保存和恢复布局状态 | 中 |
| **布局验证** | 在渲染前验证布局配置的正确性 | 高 |
| **布局调试** | 可视化布局管线的执行过程，方便调试 | 中 |

---

> **文档版本**：v1.0
> **创建日期**：2026-05-17
> **关联文档**：
> - [WdUI_SplitContainer_分割容器支持方案.md](./WdUI_SplitContainer_分割容器支持方案.md)
> - [WdUI_布局管线统一重构方案.md](./WdUI_布局管线统一重构方案.md)
> - [WdUI重构架构全景文档.md](./WdUI重构架构全景文档.md)
