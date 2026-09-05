# WdUI SplitContainer 分割容器支持方案

> **文档版本**：v1.0  
> **创建日期**：2026-05-17  
> **涉及项目**：WdUI（UI 实现层）、FileDriven（应用层）  
> **核心目标**：为 WdUI 增加 SplitContainer 控件支持，使 FileDriven 等应用层项目能够通过 JSON 配置实现容器左右/上下拉伸

---

## 目录

- [一、背景与需求](#一背景与需求)
- [二、架构分析](#二架构分析)
- [三、WdUI 改造方案](#三wdUI-改造方案)
- [四、FileDriven 应用方案](#四filedriven-应用方案)
- [五、实施步骤](#五实施步骤)
- [六、风险与注意事项](#六风险与注意事项)
- [七、附录](#七附录)

---

## 一、背景与需求

### 1.1 当前问题

FileDriven 的 `MainLayout.json` 使用比例布局将主界面分为左右两个区域：

| 区域 | 比例 | 用途 |
|------|------|------|
| `mainArea` | `width: 0.75` | 主内容区（含 contentContainer + terminalPanel） |
| `sideContainer` | `width: 0.25` | 侧边容器（动态加载配置） |

**问题**：这两个区域的比例是固定的（0.75 / 0.25），用户无法通过鼠标拖拽来调整左右区域的大小。

### 1.2 需求描述

```
┌──────────────────────┬──────────────────────────┐
│                      │     ← 可拖拽分割条 →      │
│  主内容区             │                          │
│  (可调整宽度)         │  侧边容器                 │
│                      │  (可调整宽度)              │
└──────────────────────┴──────────────────────────┘
```

- 用户可以通过拖拽分割条来调整左右区域的大小
- 窗口 Resize 时保持用户调整后的分割比例
- 支持通过 JSON 配置定义分割方向和初始比例

### 1.3 设计决策

| 决策 | 选择 | 理由 |
|------|------|------|
| 实现方式 | **WinForms 原生 SplitContainer** | 稳定可靠，无需自建拖拽逻辑 |
| 修改层级 | **WdUI（UI 实现层）** | 基础设施层提供能力，应用层只需改配置 |
| 分割比例持久化 | **运行时内存保持** | 简单可靠，不引入持久化复杂度 |
| JSON 配置结构 | **扩展 ControlConfig** | 与现有架构完全兼容 |

---

## 二、架构分析

### 2.1 当前渲染管线

```
WdUIComposer.RenderFromJson(json, target)
  → CreateLayoutPanel(view)           // 创建 LayoutPanel (Dock=Fill)
    → CreateControl(controlConfig)    // 遍历 view.Controls
      → CreateControlByType(type)     // 类型分发：button → Button, panel → Panel...
      → CreateChildControls(...)      // 递归创建子控件
  → ApplySmartLayout(panel, config)   // 应用比例布局
```

### 2.2 SplitContainer 的特殊性

WinForms 的 `SplitContainer` 与普通 Panel 不同：

| 特性 | 普通 Panel | SplitContainer |
|------|-----------|----------------|
| 子控件容器 | `panel.Controls` | `panel.Panel1.Controls` + `panel.Panel2.Controls` |
| 分割功能 | 无 | 内置可拖拽分割条 |
| Orientation | 无 | `Vertical`（左右） / `Horizontal`（上下） |
| 分割位置 | 无 | `SplitterDistance`（像素值） |

**关键挑战**：`CreateChildControls` 目前将所有子控件添加到 `parent.Controls`，但对于 SplitContainer，需要将子控件分别添加到 `Panel1.Controls` 和 `Panel2.Controls`。

### 2.3 比例布局与 SplitContainer 的配合策略

| 场景 | 策略 |
|------|------|
| **首次加载** | 根据 JSON 中的 `splitterDistance`（比例值 0.0~1.0）计算初始像素值 |
| **用户拖拽** | 记录新的分割比例（`splitterDistance / parentWidth`），不触发比例布局重算 |
| **窗口 Resize** | 按上次用户调整后的比例重新计算 `SplitterDistance` |
| **布局重载** | 重置为 JSON 中定义的默认比例 |

---

## 三、WdUI 改造方案

### 3.1 修改文件清单

| 文件 | 修改内容 | 优先级 |
|------|----------|--------|
| `WdUI/UI/WdUI.Configuration.cs` | `ControlConfig` 增加 SplitContainer 相关属性 | P0 |
| `WdUI/UI/WdUI.Framework.cs` | `ControlFactory` 增加 SplitContainer 创建和子控件处理 | P0 |
| `WdUI/UI/WdUIComposer.cs` | 可选：处理 SplitContainer 的 Resize 比例保持 | P1 |

### 3.2 ControlConfig 模型扩展

在 `WdUI/UI/WdUI.Configuration.cs` 的 `ControlConfig` 类中增加：

```csharp
/// <summary>SplitContainer 方向</summary>
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public string? Orientation { get; set; }  // "Vertical" 或 "Horizontal"

/// <summary>分割位置比例 (0.0~1.0)，相对于容器尺寸</summary>
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public double? SplitterDistance { get; set; }

/// <summary>Panel1 的子控件配置</summary>
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public List<ControlConfig>? Panel1Controls { get; set; }

/// <summary>Panel2 的子控件配置</summary>
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public List<ControlConfig>? Panel2Controls { get; set; }
```

### 3.3 ControlFactory 扩展

在 `WdUI/UI/WdUI.Framework.cs` 中：

#### 3.3.1 CreateControlByType 增加 SplitContainer

```csharp
"splitcontainer" => new SplitContainer(),
```

#### 3.3.2 CreateControl 中处理 SplitContainer

```csharp
// 在 CreateControl 方法中，创建控件后处理 SplitContainer 的特殊逻辑
if (control is SplitContainer splitter)
{
    // 设置方向
    if (!string.IsNullOrEmpty(config.Orientation))
    {
        splitter.Orientation = config.Orientation.Equals("Horizontal", 
            StringComparison.OrdinalIgnoreCase) 
            ? Orientation.Horizontal 
            : Orientation.Vertical;
    }

    // 设置初始分割位置（比例值转换为像素值）
    // 注意：此时控件可能还没有实际宽度，需要在布局完成后设置
    // 所以这里只保存配置，在 ApplySmartLayout 后设置 SplitterDistance

    // Panel1 的子控件
    if (config.Panel1Controls != null)
    {
        foreach (var childConfig in config.Panel1Controls)
        {
            var child = CreateControl(childConfig, mediator);
            splitter.Panel1.Controls.Add(child);
        }
    }

    // Panel2 的子控件
    if (config.Panel2Controls != null)
    {
        foreach (var childConfig in config.Panel2Controls)
        {
            var child = CreateControl(childConfig, mediator);
            splitter.Panel2.Controls.Add(child);
        }
    }

    // 不执行默认的 CreateChildControls（避免将子控件添加到 SplitContainer 本身）
    control.Tag = config;
    return control;
}
```

#### 3.3.3 处理 SplitterDistance 的设置时机

SplitterDistance 需要在控件有实际宽度后才能设置，因此在 `ApplySmartLayout` 之后处理：

```csharp
// 在 WdUIComposer.RenderFromJson 中，应用布局后设置 SplitterDistance
private static void ApplySplitterDistances(Control parent)
{
    foreach (Control control in parent.Controls)
    {
        if (control is SplitContainer splitter && control.Tag is ControlConfig config)
        {
            if (config.SplitterDistance.HasValue && splitter.Width > 0)
            {
                if (splitter.Orientation == Orientation.Vertical)
                    splitter.SplitterDistance = (int)(splitter.Width * config.SplitterDistance.Value);
                else
                    splitter.SplitterDistance = (int)(splitter.Height * config.SplitterDistance.Value);
            }
        }
        if (control.Controls.Count > 0)
            ApplySplitterDistances(control);
    }
}
```

### 3.4 SplitterMoved 事件处理

在 SplitContainer 创建时，订阅 `SplitterMoved` 事件以记录用户调整后的比例：

```csharp
// 在 CreateControl 的 SplitContainer 分支中
splitter.SplitterMoved += (s, e) =>
{
    // 记录用户调整后的比例，供 Resize 时使用
    if (s is SplitContainer sc)
    {
        double ratio;
        if (sc.Orientation == Orientation.Vertical)
            ratio = sc.SplitterDistance / (double)sc.Width;
        else
            ratio = sc.SplitterDistance / (double)sc.Height;

        // 将比例存回 Tag 中的 ControlConfig
        if (sc.Tag is ControlConfig cfg)
        {
            cfg.SplitterDistance = ratio;
        }
    }
};
```

### 3.5 Resize 时保持分割比例

在 `ConfigContainer.OnContainerResize` 或 `WdUIComposer` 的 Resize 处理中，增加对 SplitContainer 的处理：

```csharp
// 递归处理所有 SplitContainer，按比例恢复 SplitterDistance
private static void RestoreSplitterDistances(Control parent)
{
    foreach (Control control in parent.Controls)
    {
        if (control is SplitContainer splitter && 
            splitter.Tag is ControlConfig config && 
            config.SplitterDistance.HasValue)
        {
            if (splitter.Orientation == Orientation.Vertical && splitter.Width > 0)
                splitter.SplitterDistance = (int)(splitter.Width * config.SplitterDistance.Value);
            else if (splitter.Orientation == Orientation.Horizontal && splitter.Height > 0)
                splitter.SplitterDistance = (int)(splitter.Height * config.SplitterDistance.Value);
        }
        if (control.Controls.Count > 0)
            RestoreSplitterDistances(control);
    }
}
```

---

## 四、FileDriven 应用方案

### 4.1 修改文件清单

| 文件 | 修改内容 |
|------|----------|
| `FileDriven/DrivenConfig/JsonConfig/MainLayout.json` | 用 SplitContainer 替换 `mainArea` + `sideContainer` 平铺结构 |

### 4.2 MainLayout.json 改造

**改造前**（平铺结构，无分割功能）：

```json
{
  "type": "panel",
  "name": "mainArea",
  "layout": { "left": 0.0, "top": 0.05, "width": 0.75, "height": 0.95 },
  "controls": [
    // contentContainer + terminalPanel
  ]
},
{
  "type": "container",
  "name": "sideContainer",
  "layout": { "left": 0.75, "top": 0.05, "width": 0.25, "height": 0.95 },
  "container": { "sourceType": "dynamic", "autoLoad": false }
}
```

**改造后**（SplitContainer 分割结构）：

```json
{
  "type": "splitcontainer",
  "name": "mainSplitter",
  "orientation": "Vertical",
  "splitterDistance": 0.75,
  "layout": { "left": 0.0, "top": 0.05, "width": 1.0, "height": 0.95 },
  "panel1Controls": [
    {
      "type": "container",
      "name": "contentContainer",
      "layout": { "left": 0.0, "top": 0.0, "width": 1.0, "height": 0.75 },
      "container": { "sourceType": "dynamic", "autoLoad": false }
    },
    {
      "type": "panel",
      "name": "terminalPanel",
      "backColor": "#1E1E1E",
      "layout": { "left": 0.0, "top": 0.75, "width": 1.0, "height": 0.25 },
      "controls": [
        // outputTextBox + commandBar
      ]
    }
  ],
  "panel2Controls": [
    {
      "type": "container",
      "name": "sideContainer",
      "layout": { "left": 0.0, "top": 0.0, "width": 1.0, "height": 1.0 },
      "container": { "sourceType": "dynamic", "autoLoad": false }
    }
  ]
}
```

### 4.3 MainForm.cs 适配

由于 SplitContainer 的引入，控件层级发生变化：

```
改造前：rootContainer → LayoutPanel → mainArea/sideContainer
改造后：rootContainer → LayoutPanel → mainSplitter → Panel1/contentContainer, Panel2/sideContainer
```

`FindSubContainer` 和 `FindControl<T>` 使用递归查找（`Controls.Find(name, true)`），所以**不需要修改 MainForm.cs 的查找逻辑**。

但需要注意：`sideContainer` 现在位于 `mainSplitter.Panel2.Controls` 中，而不是 `rootContainer` 的直接子控件中。由于 `ConfigContainer.ScanSubContainersRecursive` 已经是递归扫描，所以子容器的注册不受影响。

---

## 五、实施步骤

### Step 1：WdUI 模型层扩展

**文件**：`WdUI/UI/WdUI.Configuration.cs`

- [ ] 在 `ControlConfig` 中增加 `Orientation` 属性
- [ ] 在 `ControlConfig` 中增加 `SplitterDistance` 属性
- [ ] 在 `ControlConfig` 中增加 `Panel1Controls` 属性
- [ ] 在 `ControlConfig` 中增加 `Panel2Controls` 属性

### Step 2：WdUI ControlFactory 扩展

**文件**：`WdUI/UI/WdUI.Framework.cs`

- [ ] `CreateControlByType` 增加 `"splitcontainer"` 分支
- [ ] `CreateControl` 中增加 SplitContainer 处理分支
- [ ] 处理 Panel1/Panel2 的子控件创建
- [ ] 订阅 SplitterMoved 事件记录比例

### Step 3：WdUI 布局适配

**文件**：`WdUI/UI/WdUIComposer.cs`

- [ ] 在 `RenderFromJson` 中增加 `ApplySplitterDistances` 调用
- [ ] 在 Resize 处理中增加 `RestoreSplitterDistances` 调用

### Step 4：FileDriven 配置改造

**文件**：`FileDriven/DrivenConfig/JsonConfig/MainLayout.json`

- [ ] 用 SplitContainer 替换 `mainArea` + `sideContainer`
- [ ] 配置 `orientation`、`splitterDistance` 属性
- [ ] 将子控件分别移到 `panel1Controls` 和 `panel2Controls`

### Step 5：编译测试

- [ ] 编译整个解决方案，确保 0 Error
- [ ] 运行 FileDriven，验证左右拉伸功能
- [ ] 验证窗口 Resize 时分割比例保持
- [ ] 验证 contentContainer 和 sideContainer 的功能正常

---

## 六、风险与注意事项

### 6.1 已知风险

| 风险 | 等级 | 说明 | 缓解措施 |
|------|------|------|----------|
| SplitterDistance 设置时机 | **中** | 控件刚创建时 Width/Height 可能为 0 | 在布局完成后（ApplySmartLayout 之后）设置 |
| Panel1/Panel2 的 Controls 访问 | **低** | Panel1/Panel2 是只读属性，但 Controls 是正常的 ControlCollection | 直接使用 `splitter.Panel1.Controls.Add()` |
| 嵌套 SplitContainer | **低** | 支持 SplitContainer 嵌套 SplitContainer | 递归处理，与普通控件嵌套逻辑一致 |
| 比例布局与 SplitterMoved 冲突 | **低** | Resize 时比例布局可能覆盖用户调整的分割位置 | 在 SplitterMoved 中记录比例，Resize 时使用记录值 |

### 6.2 注意事项

1. **SplitterDistance 是像素值**：JSON 中配置的是比例值（0.0~1.0），需要在代码中转换为像素值
2. **最小分割距离**：SplitContainer 有 `Panel1MinSize` 和 `Panel2MinSize` 属性，默认 25px，可配置
3. **分割条宽度**：默认 `SplitterWidth` 为 4px，可通过 JSON 配置调整
4. **固定面板**：支持 `FixedPanel` 属性，可固定 Panel1 或 Panel2 的大小

### 6.3 向后兼容性

- 所有新增属性都有默认值（null），不影响现有 JSON 配置的解析
- 现有 `controls` 属性仍然有效，仅 SplitContainer 类型使用 `panel1Controls` / `panel2Controls`
- 不修改任何现有 API 签名

---

## 七、附录

### A. JSON 配置完整示例

```json
{
  "type": "splitcontainer",
  "name": "mainSplitter",
  "orientation": "Vertical",
  "splitterDistance": 0.75,
  "splitterWidth": 6,
  "panel1MinSize": 200,
  "panel2MinSize": 150,
  "layout": {
    "left": 0.0,
    "top": 0.05,
    "width": 1.0,
    "height": 0.95
  },
  "panel1Controls": [
    {
      "type": "container",
      "name": "contentContainer",
      "layout": { "left": 0.0, "top": 0.0, "width": 1.0, "height": 0.75 },
      "container": { "sourceType": "dynamic", "autoLoad": false }
    },
    {
      "type": "panel",
      "name": "terminalPanel",
      "backColor": "#1E1E1E",
      "layout": { "left": 0.0, "top": 0.75, "width": 1.0, "height": 0.25 },
      "controls": [
        {
          "type": "textbox",
          "name": "outputTextBox",
          "multiline": true,
          "readOnly": true,
          "scrollBars": "Vertical",
          "backColor": "#1E1E1E",
          "foreColor": "#90EE90",
          "font": { "name": "Consolas", "size": 10 },
          "layout": { "left": 0.0, "top": 0.0, "width": 1.0, "height": 0.82 }
        },
        {
          "type": "panel",
          "name": "commandBar",
          "backColor": "#2D2D2D",
          "layout": { "left": 0.0, "top": 0.82, "width": 1.0, "height": 0.18 },
          "controls": [
            {
              "type": "label",
              "name": "cmdLabel",
              "text": "Command:",
              "foreColor": "#CCCCCC",
              "layout": { "left": 0.01, "top": 0.2, "width": 0.08, "height": 0.6 }
            },
            {
              "type": "textbox",
              "name": "commandTextBox",
              "backColor": "#3C3C3C",
              "foreColor": "#FFFFFF",
              "layout": { "left": 0.1, "top": 0.15, "width": 0.75, "height": 0.7 }
            },
            {
              "type": "button",
              "name": "executeButton",
              "text": "Execute",
              "backColor": "#2563EB",
              "foreColor": "White",
              "layout": { "left": 0.87, "top": 0.15, "width": 0.11, "height": 0.7 }
            }
          ]
        }
      ]
    }
  ],
  "panel2Controls": [
    {
      "type": "container",
      "name": "sideContainer",
      "layout": { "left": 0.0, "top": 0.0, "width": 1.0, "height": 1.0 },
      "container": { "sourceType": "dynamic", "autoLoad": false }
    }
  ]
}
```

### B. 扩展建议

1. **上下分割**：`contentContainer` 和 `terminalPanel` 之间也可以使用 SplitContainer（`Orientation: "Horizontal"`），实现上下拉伸
2. **分割比例持久化**：可将用户调整后的分割比例保存到配置文件，下次启动时恢复
3. **分割条样式**：可通过 `SplitterIncrement`、`SplitterWidth` 等属性自定义分割条行为
4. **三栏布局**：通过嵌套 SplitContainer 实现左-中-右三栏布局

### C. 相关文档

- [WdUI 布局管线统一重构方案](./WdUI_布局管线统一重构方案.md)
- [ConfigContainer 容器系统架构设计与实践指南](./ConfigContainer_容器系统_架构设计与实践指南.md)
- [FileDriven UI 自适应布局改造方案](../../FileDriven/docs/FileDriven_UI自适应布局改造方案.md)
