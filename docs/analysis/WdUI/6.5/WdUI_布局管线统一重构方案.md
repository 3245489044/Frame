# WdUI 布局管线统一重构方案

> **问题**：窗口拉伸时 UI 控件无法自适应放大/缩小
> **根因**：WdUI 框架中存在两条并行的布局路径，互相冲突且未正确联动
> **目标**：统一布局管线，确保渲染时和 Resize 时走同一条路径

---

## 目录

- [一、问题分析](#一问题分析)
- [二、两条布局路径的完整追踪](#二两条布局路径的完整追踪)
- [三、根因定位](#三根因定位)
- [四、重构方案](#四重构方案)
  - [4.1 第一阶段：修复自适应布局（最小改动）](#41-第一阶段修复自适应布局最小改动)
  - [4.2 第二阶段：统一布局管线（框架整理）](#42-第二阶段统一布局管线框架整理)
  - [4.3 第三阶段：架构精简（长期优化）](#43-第三阶段架构精简长期优化)
- [五、涉及文件清单](#五涉及文件清单)
- [六、风险与注意事项](#六风险与注意事项)

---

## 一、问题分析

### 1.1 现象

- 窗口初始化时，UI 控件按 JSON 配置的比例布局正确显示
- 拖动窗口边缘拉伸时，控件位置和大小**不跟随变化**
- 期望行为：控件应随窗口大小按比例自动缩放

### 1.2 技术背景

WdUI 是一个基于 JSON 配置驱动的 WinForms UI 框架，核心能力是：

1. 从 JSON 文件加载 `UIConfiguration`（含 `ControlConfig.Layout` 比例值）
2. 根据比例值（0~1）计算控件的像素位置和大小
3. 支持窗口 Resize 时重新计算

比例值示例（`MainLayout.json`）：
```json
{
  "layout": {
    "left": 0.01,
    "top": 0.15,
    "width": 0.1,
    "height": 0.7
  }
}
```

---

## 二、两条布局路径的完整追踪

### 2.1 路径 A：`LayoutMediator` 路径（渲染时使用）

```
WdUIComposer.RenderFromJson()                          # WdUIComposer.cs L238
  → ApplySmartLayout(panel, wrapperConfig)              # WdUIComposer.cs L284
    → LayoutMediator.ConvertFromConfigs(configs)         # LayoutApplier.cs L216
    → LayoutMediator.Apply(parent, definition)           # LayoutApplier.cs L87
      → SmartLayoutEngine.Apply(container)               # 计算像素位置并设置控件
      → _layoutCache.Add(parent, definition)             # 存入 ConditionalWeakTable
```

**特点**：
- 使用 `LayoutMediator` + `SmartLayoutEngine` 计算布局
- 缓存机制：`ConditionalWeakTable<Control, LayoutDefinition>`
- **没有订阅 Resize 事件**，窗口拉伸时不会重新计算

### 2.2 路径 B：`ApplyProportionalLayout` 路径（Resize 时使用）

```
ConfigContainer.OnContainerResize()                     # ConfigContainer.cs L225
  → WdUIComposer.ApplyProportionalLayout(this)           # WdUIComposer.cs L420
    → container.ApplyProportionalLayoutFromTag(tag)      # 从 Tag 提取 ControlConfig.Layout
      → ProportionalMath.CalculateProportionalBounds     # 重新计算像素位置
```

**特点**：
- 依赖 `Control.Tag` 中存储的 `ControlConfig`
- 通过递归遍历所有子控件，从 `Tag` 提取比例值
- **已订阅 Resize 事件**，但依赖 Tag 数据

### 2.3 两条路径的调用时序

```
MainForm.LoadMainLayout()
  → _rootContainer.LoadConfigAsync(MainLayoutPath)
    → ConfigContainer.RenderConfigAsync(config)
      → WdUIComposer.RenderFromJson(json, this)          # 路径 A：ApplySmartLayout
      → WdUIComposer.ApplyProportionalLayout(this)        # 路径 B：首次布局
      → EnsureLayoutSubscribed()                          # 订阅 Resize → 路径 B
```

---

## 三、根因定位

### 3.1 核心问题：Tag 数据丢失

`ApplyProportionalLayoutFromTag` 依赖 `Control.Tag` 中存储的 `ControlConfig`，但 `RenderFromJson` 中 `ApplySmartLayout` 走的是路径 A，**没有将 `ControlConfig` 存入 `Tag`**。

虽然在 `ControlFactory.CreateControl()`（WdUI.Framework.cs L241）中有：
```csharp
control.Tag = config;  // 将 ControlConfig 存入 Tag
```

但 `RenderFromJson` 中创建控件后调用了 `ApplySmartLayout`，而 `ApplySmartLayout` 内部通过 `LayoutMediator` 重新计算布局，**没有利用 Tag 中的 ControlConfig**。之后 `ConfigContainer.RenderConfigAsync` 虽然调用了 `ApplyProportionalLayout(this)`，但此时控件已经被 `ApplySmartLayout` 重新定位，Tag 中的 ControlConfig 可能已被覆盖或丢失。

### 3.2 次要问题：两条路径的缓存机制不一致

| 路径 | 缓存机制 | Resize 支持 |
|------|---------|------------|
| A: LayoutMediator | `ConditionalWeakTable<Control, LayoutDefinition>` | ❌ 无 Resize 事件 |
| B: ApplyProportionalLayout | `Control.Tag` (ControlConfig) | ✅ 有 Resize 事件 |

### 3.3 次要问题：重复订阅

- `WdUIComposer.RenderFromJson` 中为每个 `LayoutPanel` 订阅了 `Resize` 事件（`OnPanelResize`，L281）
- `ConfigContainer.RenderConfigAsync` 中又为自身订阅了 `Resize` 事件（`OnContainerResize`，L218）
- 两者功能重叠，但实现不同

---

## 四、重构方案

### 4.1 第一阶段：修复自适应布局（最小改动）

**目标**：以最小改动让窗口拉伸时 UI 自适应

#### 修改 1：`WdUIComposer.RenderFromJson` — 确保 Tag 设置

在 `ApplySmartLayout` 之后，遍历所有子控件，将 `ControlConfig` 存入 `Tag`：

```csharp
// 在 ApplySmartLayout(panel, wrapperConfig) 之后（L284 附近）
// 将 ControlConfig 存入每个子控件的 Tag，供 Resize 时读取
foreach (var controlConfig in view.Controls)
{
    if (controlConfig.Layout == null) continue;
    
    var control = FindChildControlByName(panel, controlConfig.Name);
    if (control != null)
    {
        control.Tag = controlConfig;
    }
}
```

#### 修改 2：`ConfigContainer.OnContainerResize` — 确保递归应用

当前实现已正确，但需要确保 `ApplyProportionalLayout` 能递归遍历所有层级：

```csharp
private void OnContainerResize(object? sender, EventArgs e)
{
    // 递归应用比例布局，从 Tag 读取 ControlConfig.Layout
    WdUIComposer.ApplyProportionalLayout(this);
}
```

#### 修改 3：`WdUIComposer.ApplyProportionalLayout` — 增强递归深度

确保 `ApplyProportionalLayoutFromTag` 能穿透 `LayoutPanel` 包装器，递归到所有嵌套层级：

```csharp
public static void ApplyProportionalLayout(Control container)
{
    if (container == null) return;

    container.ApplyProportionalLayoutFromTag(tag =>
    {
        if (tag is ControlConfig cfg && cfg.Layout != null)
        {
            var layout = cfg.Layout;
            double left = layout.Left;
            double top = layout.Top;

            if (layout.Bottom >= 0 && layout.Top < 0)
                top = -layout.Bottom;
            if (layout.Right >= 0 && layout.Left < 0)
                left = -layout.Right;

            return (left, top, layout.Width, layout.Height);
        }
        return null;
    });
}
```

#### 第一阶段改动范围

| 文件 | 改动类型 | 风险 |
|------|---------|------|
| `WdUI/UI/WdUIComposer.cs` | 新增 Tag 设置逻辑 | 低 |
| `WdUI/UI/ConfigContainer.cs` | 确认 OnContainerResize 正确 | 低 |
| `WdUI/UI/WdUI.Utilities.cs` | 确认 ApplyProportionalLayoutFromTag 正确 | 低 |

---

### 4.2 第二阶段：统一布局管线（框架整理）

**目标**：消除两条路径的冲突，统一为一条布局管线

#### 4.2.1 统一布局入口

在 `WdUIComposer` 中新增统一的布局方法：

```csharp
/// <summary>
/// 统一布局入口：渲染时和 Resize 时都调用此方法
/// </summary>
public static void ApplyLayout(Control container, List<ControlConfig>? configs = null)
{
    if (container == null) return;

    if (configs != null && configs.Count > 0)
    {
        // 使用 LayoutMediator 路径（精确计算）
        var mediator = new LayoutMediator();
        var definition = mediator.ConvertFromConfigs(configs);
        mediator.Apply(container, definition);
    }
    else
    {
        // 回退到 Tag 路径（兼容已有控件）
        ApplyProportionalLayout(container);
    }
}
```

#### 4.2.2 统一缓存机制

选择一种缓存机制，统一使用：

**方案 A：统一使用 `ConditionalWeakTable`（推荐）**

优点：
- 不占用 `Control.Tag`（Tag 可用于其他用途）
- 自动 GC，无内存泄漏
- 线程安全

缺点：
- 需要修改 `LayoutMediator` 使其支持 Resize 事件

```csharp
// 在 LayoutMediator 中添加 Resize 事件支持
public void ApplyAndTrackResize(Control parent, LayoutDefinition definition)
{
    Apply(parent, definition);
    
    // 使用弱引用事件，防止内存泄漏
    var weakParent = new WeakReference<Control>(parent);
    var weakDef = new WeakReference<LayoutDefinition>(definition);
    
    parent.Resize += (sender, args) =>
    {
        if (weakParent.TryGetTarget(out var p) && weakDef.TryGetTarget(out var d))
        {
            Apply(p, d);
        }
    };
}
```

**方案 B：统一使用 `Control.Tag`**

优点：
- 实现简单，无需修改缓存机制
- 与 `ApplyProportionalLayout` 天然兼容

缺点：
- 占用 `Control.Tag`
- 需要确保所有创建控件的地方都设置了 Tag

#### 4.2.3 消除重复 Resize 订阅

```csharp
// WdUIComposer.RenderFromJson — 移除 OnPanelResize 订阅
// 改为由 ConfigContainer 统一管理 Resize 事件

// ConfigContainer — 统一管理 Resize
private void EnsureLayoutSubscribed()
{
    if (!_layoutSubscribed)
    {
        _layoutSubscribed = true;
        this.Resize += OnContainerResize;
    }
}
```

#### 第二阶段改动范围

| 文件 | 改动类型 | 风险 |
|------|---------|------|
| `WdUI/UI/WdUIComposer.cs` | 新增 `ApplyLayout` 统一入口 | 中 |
| `WdUI/UI/LayoutApplier.cs` | 新增 `ApplyAndTrackResize` | 中 |
| `WdUI/UI/ConfigContainer.cs` | 改用统一入口 | 低 |
| `WdUI/UI/WdUI.Utilities.cs` | 保留 `ApplyProportionalLayoutFromTag` 作为回退 | 低 |

---

### 4.3 第三阶段：架构精简（长期优化）

**目标**：按 WdUI 重构架构全景文档，精简框架

#### 4.3.1 删除重复代码

| 组件 | 当前位置 | 替代方案 |
|------|---------|---------|
| `ProportionalMath` | WdUI 多处 | `UniversalLayoutMath.ProportionalMath` |
| `OverlapMath` | WdUI 多处 | `UniversalLayoutMath.OverlapMath` |
| `LayoutMediator` | `WdUI/Layout/LayoutApplier.cs` | `SmartLayoutEngine` |
| `ControlFactory` | `WdUI/UI/WdUI.Framework.cs` | `GenericControlFactory` |

#### 4.3.2 精简 `WdUIComposer`

- 将布局相关方法提取到 `LayoutManager`
- 将配置加载相关方法提取到 `ConfigurationLoader`
- `WdUIComposer` 只保留编排入口

#### 4.3.3 职责分离

```
WdUIComposer (编排入口)
  ├── 窗体管理 → ViewManager
  ├── 配置加载 → ConfigurationLoader
  ├── 布局管理 → LayoutManager (新增)
  ├── 控件创建 → ControlFactory
  └── 事件总线 → EventBus
```

---

## 五、涉及文件清单

### 核心文件

| 文件路径 | 职责 | 阶段 |
|---------|------|------|
| `WdUI/UI/WdUIComposer.cs` | 框架入口，RenderFromJson，ApplySmartLayout | 一、二、三 |
| `WdUI/UI/ConfigContainer.cs` | 容器控件，Resize 事件处理 | 一、二 |
| `WdUI/UI/LayoutApplier.cs` | LayoutMediator，布局计算 | 二 |
| `WdUI/UI/WdUI.Utilities.cs` | ControlExtensions，ApplyProportionalLayoutFromTag | 一 |
| `WdUI/UI/WdUI.Framework.cs` | ControlFactory，控件创建 | 一 |
| `WdUI/UI/Managers.cs` | UIManager，LayoutManager | 二、三 |

### 配置文件

| 文件路径 | 职责 |
|---------|------|
| `FileDriven/DrivenConfig/JsonConfig/MainLayout.json` | 主布局配置 |
| `FileDriven/DrivenConfig/JsonConfig/ConfigSelector.json` | 配置选择器布局 |

---

## 六、风险与注意事项

### 6.1 风险

| 风险 | 等级 | 缓解措施 |
|------|------|---------|
| Tag 被其他逻辑覆盖 | 中 | 使用 `ConditionalWeakTable` 替代 Tag |
| Resize 事件频繁触发导致性能问题 | 低 | 使用防抖或节流 |
| 递归遍历深度过大 | 低 | 限制递归深度，或使用迭代替代递归 |
| 与现有代码兼容性 | 中 | 分阶段实施，每阶段后编译验证 |

### 6.2 注意事项

1. **Tag 的占用问题**：`Control.Tag` 是 `object` 类型，任何代码都可以读写。如果多个组件同时使用 Tag，可能互相覆盖。建议统一使用 `ConditionalWeakTable`。

2. **Resize 事件的性能**：窗口拉伸时会频繁触发 Resize 事件，每次都需要重新计算所有子控件的位置。对于控件数量较多的界面，建议：
   - 使用 `SuspendLayout` / `ResumeLayout` 批量更新
   - 考虑使用防抖（Debounce）机制

3. **嵌套容器的布局**：`ConfigContainer` 支持嵌套，子容器内部也有自己的布局。需要确保递归遍历能正确处理嵌套场景。

4. **LayoutPanel 包装器**：`RenderFromJson` 会为每个 View 创建 `LayoutPanel`（Panel）作为包装器，子控件在包装器内部。递归遍历需要穿透包装器。

### 6.3 验证方法

1. **编译验证**：`dotnet build` 确保 0 Error
2. **运行时验证**：
   - 启动应用，确认 UI 正常显示
   - 拖动窗口边缘拉伸，确认控件跟随缩放
   - 最大化/还原窗口，确认布局正确
3. **边界测试**：
   - 窗口最小化后还原
   - 快速连续拉伸窗口
   - 加载不同配置切换

---

## 附录：关键代码片段

### A. `ApplyProportionalLayoutFromTag` 实现

```csharp
public static void ApplyProportionalLayoutFromTag(
    this Control container,
    Func<object?, (double left, double top, double width, double height)?> layoutExtractor)
{
    if (container == null) return;

    container.SuspendLayout();
    try
    {
        ApplyProportionalRecursive(container, layoutExtractor);
    }
    finally
    {
        container.ResumeLayout(true);
    }
}

private static void ApplyProportionalRecursive(
    Control parent,
    Func<object?, (double left, double top, double width, double height)?> layoutExtractor)
{
    foreach (Control child in parent.Controls)
    {
        var layout = layoutExtractor(child.Tag);
        if (layout.HasValue)
        {
            var (left, top, width, height) = layout.Value;
            WdUIComposer.ApplyProportionalLayout(
                child, left, top, width, height);
        }

        if (child.Controls.Count > 0)
        {
            ApplyProportionalRecursive(child, layoutExtractor);
        }
    }
}
```

### B. `ProportionalMath.CalculateProportionalBounds` 实现

```csharp
public static (int X, int Y, int Width, int Height) CalculateProportionalBounds(
    int parentWidth, int parentHeight,
    double left, double top, double? widthRatio, double? heightRatio,
    int defaultX, int defaultY, int defaultW, int defaultH)
{
    int x = left >= 0 ? (int)(parentWidth * left) : defaultX;
    int y = top >= 0 ? (int)(parentHeight * top) : defaultY;
    int w = widthRatio.HasValue && widthRatio.Value >= 0
        ? (int)(parentWidth * widthRatio.Value) : defaultW;
    int h = heightRatio.HasValue && heightRatio.Value >= 0
        ? (int)(parentHeight * heightRatio.Value) : defaultH;

    return (x, y, w, h);
}
```
