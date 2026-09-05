# FileDriven UI 自适应布局改造方案

## 一、现状分析

### 1.1 当前问题

FileDriven 的 UI 布局目前存在以下问题：

| 问题 | 描述 | 影响 |
|------|------|------|
| **硬编码布局** | `ConfigSelectorForm.cs` 使用绝对像素坐标（如 `Location = new Point(12, 12)`） | 窗口拉伸时控件位置/大小不变 |
| **独立 Form** | ConfigSelector 是一个独立的 `Form`，不是嵌入到 MainForm 的容器中 | 无法与主界面融合，需要额外窗口管理 |
| **无比例布局** | 所有控件使用 `Location` + `Size` 固定值 | 不同分辨率下显示效果差异大 |
| **无嵌套容器** | 没有利用 `ConfigContainer` 的嵌套能力 | 无法实现动态内容区切换 |

### 1.2 现有基础设施

项目已有强大的自适应布局基础设施：

```
WdUI/
├── UI/
│   ├── ConfigContainer.cs      # 可嵌套的 JSON 配置 UI 容器
│   ├── LayoutApplier.cs        # 布局中介器（比例布局引擎）
│   ├── WdUIComposer.cs         # 核心编排器（JSON→UI 渲染管线）
│   ├── WdUI.Configuration.cs   # 配置模型（ControlConfig, LayoutPosition 等）
│   └── WdUI.Utilities.cs       # 工具方法（FindControlByName 等）
```

**关键能力：**
- `ConfigContainer`：支持嵌套，可加载 JSON 配置并渲染 UI
- `LayoutPosition`：支持比例值（0.0~1.0），自动计算像素位置
- `ApplyProportionalLayout`：递归应用比例布局，窗口 Resize 时自动重算
- `FindSubContainer` / `FindControl<T>`：作用域限定的控件查找

---

## 二、改造目标

### 2.1 核心目标

```
┌─────────────────────────────────────────────────────────┐
│  MainForm (可拉伸)                                       │
│  ┌─────────────────────────────────────────────────────┐ │
│  │  toolbarPanel (height: 5%)                          │ │
│  │  [Load Config] [Switch Template] [Load Workflow]    │ │
│  │  [Clear] [Help] [Load Side]                         │ │
│  ├──────────────────────────┬──────────────────────────┤ │
│  │                          │                          │ │
│  │  contentContainer        │  sideContainer           │ │
│  │  (width: 75%)            │  (width: 25%)            │ │
│  │  ┌────────────────────┐  │  ┌────────────────────┐  │ │
│  │  │ ConfigSelector     │  │  │ (动态加载内容)      │  │ │
│  │  │ (默认加载)          │  │  │                    │  │ │
│  │  └────────────────────┘  │  └────────────────────┘  │ │
│  ├──────────────────────────┴──────────────────────────┤ │
│  │  terminalPanel (height: 25%)                        │ │
│  │  ┌────────────────────────────────────────────────┐ │ │
│  │  │ outputTextBox (height: 82%)                    │ │ │
│  │  ├────────────────────────────────────────────────┤ │ │
│  │  │ commandBar (height: 18%)                       │ │ │
│  │  │ [Command:] [________________________] [Execute]│ │ │
│  │  └────────────────────────────────────────────────┘ │ │
│  └─────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

### 2.2 关键设计决策

| 决策 | 选择 | 理由 |
|------|------|------|
| 布局定义方式 | **JSON 配置驱动** (`MainLayout.json`) | 与 WdUI 框架一致，无需硬编码 |
| 容器嵌套 | **ConfigContainer 嵌套** | 支持动态内容区切换 |
| 比例单位 | **0.0~1.0 比例值** | 自适应窗口大小变化 |
| 事件绑定 | **代码中 FindControl + 事件订阅** | 保持类型安全，避免 JSON 事件绑定复杂性 |
| ConfigSelector | **嵌入 contentContainer** | 不再是独立 Form，与主界面融合 |

---

## 三、文件清单

### 3.1 新增文件

| 文件 | 用途 |
|------|------|
| `FileDriven/DrivenConfig/JsonConfig/MainLayout.json` | 主布局 JSON 配置 |
| `FileDriven/docs/FileDriven_UI自适应布局改造方案.md` | 本文档 |

### 3.2 修改文件

| 文件 | 修改内容 |
|------|----------|
| `FileDriven/DrivenConfig/JsonConfig/ConfigSelector.json` | 改为独立 View 配置（嵌入容器用） |
| `FileDriven/FileDriven.UI/Core/MainForm.cs` | 完全重写：JSON 驱动布局 + 事件绑定 |

### 3.3 可删除文件

| 文件 | 理由 |
|------|------|
| `FileDriven/FileDriven.UI/Core/ConfigSelectorForm.cs` | 功能已合并到 MainForm + ConfigSelector.json |

---

## 四、JSON 配置详解

### 4.1 MainLayout.json

```json
{
  "form": {
    "title": "FileDriven",
    "width": 1200,
    "height": 800,
    "maximizeBox": true,
    "minimizeBox": true,
    "windowState": "Normal",
    "startPosition": "CenterScreen"
  },
  "views": [
    {
      "name": "MainView",
      "type": "Panel",
      "controls": [
        // ===== 工具栏 (5%) =====
        {
          "type": "panel",
          "name": "toolbarPanel",
          "backColor": "#F3F4F6",
          "layout": { "left": 0.0, "top": 0.0, "width": 1.0, "height": 0.05 },
          "controls": [
            {
              "type": "button", "name": "loadConfigButton", "text": "Load Config",
              "layout": { "left": 0.01, "top": 0.15, "width": 0.1, "height": 0.7 }
            },
            // ... 更多按钮
          ]
        },
        // ===== 主内容区 (75% 宽, 95% 高) =====
        {
          "type": "panel",
          "name": "mainArea",
          "layout": { "left": 0.0, "top": 0.05, "width": 0.75, "height": 0.95 },
          "controls": [
            {
              "type": "container",
              "name": "contentContainer",
              "layout": { "left": 0.0, "top": 0.0, "width": 1.0, "height": 0.75 },
              "container": { "sourceType": "dynamic", "autoLoad": false }
            },
            // ===== 终端面板 (25%) =====
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
                // ===== 命令栏 (18%) =====
                {
                  "type": "panel",
                  "name": "commandBar",
                  "backColor": "#2D2D2D",
                  "layout": { "left": 0.0, "top": 0.82, "width": 1.0, "height": 0.18 },
                  "controls": [
                    {
                      "type": "label", "name": "cmdLabel", "text": "Command:",
                      "foreColor": "#CCCCCC",
                      "layout": { "left": 0.01, "top": 0.2, "width": 0.08, "height": 0.6 }
                    },
                    {
                      "type": "textbox", "name": "commandTextBox",
                      "backColor": "#3C3C3C", "foreColor": "#FFFFFF",
                      "layout": { "left": 0.1, "top": 0.15, "width": 0.75, "height": 0.7 }
                    },
                    {
                      "type": "button", "name": "executeButton", "text": "Execute",
                      "backColor": "#2563EB", "foreColor": "White",
                      "layout": { "left": 0.87, "top": 0.15, "width": 0.11, "height": 0.7 }
                    }
                  ]
                }
              ]
            }
          ]
        },
        // ===== 侧边容器 (25% 宽) =====
        {
          "type": "container",
          "name": "sideContainer",
          "layout": { "left": 0.75, "top": 0.05, "width": 0.25, "height": 0.95 },
          "container": { "sourceType": "dynamic", "autoLoad": false }
        }
      ]
    }
  ]
}
```

### 4.2 ConfigSelector.json

```json
{
  "form": {
    "title": "配置选择器 - FileDrivenFramework",
    "width": 800,
    "height": 550
  },
  "views": [
    {
      "name": "ConfigSelectorView",
      "type": "Panel",
      "controls": [
        {
          "type": "label", "name": "titleLabel", "text": "选择配置文件",
          "backColor": "#2563EB", "foreColor": "White",
          "font": { "size": 12, "bold": true },
          "layout": { "left": 0.0, "top": 0.0, "width": 1.0, "height": 0.06 }
        },
        {
          "type": "textbox", "name": "searchTextBox",
          "placeholder": "输入配置文件名搜索...",
          "layout": { "left": 0.02, "top": 0.08, "width": 0.96, "height": 0.05 }
        },
        {
          "type": "listview", "name": "configListView",
          "fullRowSelect": true, "gridLines": true,
          "layout": { "left": 0.02, "top": 0.15, "width": 0.96, "height": 0.55 }
        },
        {
          "type": "button", "name": "loadSelButton", "text": "加载配置",
          "backColor": "#2563EB", "foreColor": "White", "enabled": false,
          "layout": { "left": 0.02, "bottom": 0.12, "width": 0.12, "height": 0.06 }
        },
        {
          "type": "button", "name": "browseButton", "text": "浏览...",
          "layout": { "left": 0.16, "bottom": 0.12, "width": 0.12, "height": 0.06 }
        },
        {
          "type": "label", "name": "statusLabel", "text": "就绪 - 选择一个配置文件开始",
          "foreColor": "#6B7280",
          "layout": { "left": 0.3, "bottom": 0.125, "width": 0.6, "height": 0.05 }
        },
        {
          "type": "label", "name": "separatorLabel",
          "text": "────────────────── Json 处理 ──────────────────",
          "foreColor": "#9CA3AF", "textAlign": "MiddleCenter",
          "layout": { "left": 0.02, "bottom": 0.06, "width": 0.96, "height": 0.04 }
        },
        {
          "type": "checkbox", "name": "enableProcessingCheckBox",
          "text": "启用 Json 美化处理",
          "layout": { "left": 0.02, "bottom": 0.01, "width": 0.18, "height": 0.05 }
        },
        {
          "type": "label", "name": "styleLabel", "text": "风格预设:",
          "enabled": false,
          "layout": { "left": 0.22, "bottom": 0.015, "width": 0.08, "height": 0.04 }
        },
        {
          "type": "combobox", "name": "styleComboBox",
          "dropDownStyle": "DropDownList", "enabled": false,
          "items": ["modern-light", "modern-dark", "material-design", "fluent-ui", "minimalist"],
          "selectedIndex": 0,
          "layout": { "left": 0.3, "bottom": 0.01, "width": 0.25, "height": 0.05 }
        }
      ]
    }
  ]
}
```

---

## 五、MainForm 改造详解

### 5.1 架构变化

```
改造前:                             改造后:
┌─────────────────────┐            ┌─────────────────────────────┐
│ MainForm            │            │ MainForm                    │
│  ├─ 硬编码控件       │            │  ├─ rootContainer (Fill)    │
│  ├─ ConfigSelector   │            │  │   └─ MainLayout.json    │
│  │   (独立 Form)     │    ──→    │  │      ├─ toolbarPanel     │
│  └─ 终端面板         │            │  │      ├─ contentContainer │
│                     │            │  │      │   └─ ConfigSelector│
│ 布局: 固定像素       │            │  │      ├─ terminalPanel    │
│ 拉伸: 不变          │            │  │      └─ sideContainer    │
└─────────────────────┘            │  └─ 布局: 比例值 0.0~1.0   │
                                   │     拉伸: 自动重算          │
                                   └─────────────────────────────┘
```

### 5.2 核心流程

```
MainForm 构造函数
  │
  ├─ WdUIComposer.Initialize()          // 初始化 WdUI 框架
  ├─ InitializeForm()                   // 设置 Form 基本属性
  │
  └─ LoadMainLayout()                   // ★ 核心：加载 JSON 布局
       │
       ├─ 创建 rootContainer (Dock=Fill)
       ├─ 订阅 ConfigLoaded 事件
       └─ 异步加载 MainLayout.json
            │
            └─ OnRootContainerLoaded()  // ★ 布局加载完成回调
                 │
                 ├─ FindSubContainer("contentContainer")  // 获取内容容器
                 ├─ FindSubContainer("sideContainer")     // 获取侧边容器
                 ├─ FindControl<TextBox>("outputTextBox") // 获取终端控件
                 ├─ FindControl<TextBox>("commandTextBox")
                 │
                 ├─ BindToolbarButtons()  // 绑定按钮事件
                 └─ LoadConfigSelector()  // 默认加载配置选择器
                      │
                      └─ OnConfigSelectorLoaded()  // 配置选择器加载完成
                           ├─ FindControl<ListView>("configListView")
                           ├─ FindControl<TextBox>("searchTextBox")
                           ├─ 绑定事件（搜索、选择、加载、浏览）
                           └─ RefreshConfigList()
```

### 5.3 事件绑定模式

所有事件绑定都在代码中完成，通过 `FindControl<T>` 获取控件引用：

```csharp
// 示例：绑定工具栏按钮
private void BindToolbarButtons()
{
    var loadConfigBtn = _rootContainer.FindControl<Button>("loadConfigButton");
    if (loadConfigBtn != null)
        loadConfigBtn.Click += LoadConfigButton_Click;

    var clearBtn = _rootContainer.FindControl<Button>("clearButton");
    if (clearBtn != null)
        clearBtn.Click += (s, e) => _outputTextBox.Clear();
}
```

### 5.4 动态内容加载

```csharp
// 加载配置到主内容区
await _contentContainer.LoadConfigAsync(config);

// 加载配置到侧边容器
_sideContainer.LoadConfig(config);
```

---

## 六、比例布局原理

### 6.1 布局值类型

| 值范围 | 含义 | 示例 |
|--------|------|------|
| `0.0 ~ 1.0` | 相对于父容器的比例 | `"width": 0.75` = 父容器宽度的 75% |
| `-1` | 未指定，保持控件默认值 | `"left": -1` |

### 6.2 布局计算

```
控件像素位置 = 父容器像素尺寸 × 比例值

例如：
  父容器宽度 = 1200px
  控件 layout.width = 0.75
  → 控件像素宽度 = 1200 × 0.75 = 900px
```

### 6.3 自适应机制

```
窗口 Resize → ConfigContainer.OnContainerResize
  → WdUIComposer.ApplyProportionalLayout(this)
    → 递归遍历所有子控件
      → 从 ControlConfig Tag 提取比例值
        → ProportionalMath.CalculateProportionalBounds
          → 计算新的像素位置和大小
            → 设置控件 Location 和 Size
```

---

## 七、实施步骤

### Step 1: 创建 MainLayout.json ✅
- 定义完整的比例布局结构
- 包含 toolbarPanel、mainArea、terminalPanel、sideContainer
- 所有 layout 值使用 0.0~1.0 比例

### Step 2: 改造 ConfigSelector.json ✅
- 从独立 Form 配置改为 View 配置
- 所有控件使用比例布局
- 移除 Form 级别的窗口属性（由 MainForm 管理）

### Step 3: 重写 MainForm.cs ✅
- 移除所有硬编码控件创建
- 使用 ConfigContainer 加载 MainLayout.json
- 通过事件回调获取嵌套容器和控件引用
- 在代码中绑定事件处理

### Step 4: 删除 ConfigSelectorForm.cs ✅
- 功能已合并到 MainForm + ConfigSelector.json
- 确保没有其他文件引用 ConfigSelectorForm

### Step 5: 编译测试 ✅
- 验证布局加载
- 验证窗口拉伸自适应

---

## 八、注意事项

### 8.1 已知限制

| 限制 | 说明 | 解决方案 |
|------|------|----------|
| ListView 列宽 | JSON 配置不支持 ListView 列定义 | 在代码中通过 `FindControl<ListView>` 后手动设置列 |
| 事件绑定 | JSON 的 EventConfig 绑定复杂 | 统一在代码中通过 FindControl 绑定 |
| 异步加载 | LoadConfigAsync 是异步的 | 使用 ConfigLoaded 事件回调获取控件引用 |

### 8.2 调试技巧

```csharp
// 1. 检查布局是否正确应用
WdUIComposer.DetectOverlaps(_rootContainer);

// 2. 启用运行时重叠监控
WdUIComposer.EnableRuntimeOverlapMonitoring(_rootContainer);

// 3. 手动触发布局重算
WdUIComposer.ApplyProportionalLayout(_rootContainer);
```

### 8.3 扩展建议

1. **分割器支持**：在 contentContainer 和 terminalPanel 之间添加 Splitter，允许用户调整比例
2. **布局预设**：提供多个 MainLayout.json 预设（如 "开发模式"、"演示模式"）
3. **布局热重载**：监听 MainLayout.json 文件变化，自动重新加载布局
4. **主题支持**：通过 JSON 的 StyleConfig 实现主题切换

---

## 九、架构图

```
┌─────────────────────────────────────────────────────────────────────┐
│                        FileDriven 架构                              │
│                                                                     │
│  ┌─────────────┐    ┌──────────────────┐    ┌──────────────────┐   │
│  │ MainLayout  │    │ ConfigSelector   │    │ 用户自定义配置    │   │
│  │ .json       │    │ .json            │    │ .json            │   │
│  └──────┬──────┘    └───────┬──────────┘    └───────┬──────────┘   │
│         │                  │                       │              │
│         ▼                  ▼                       ▼              │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │                    WdUIComposer                             │   │
│  │  ┌─────────────┐  ┌──────────────┐  ┌──────────────────┐   │   │
│  │  │ LoadConfig  │  │ RenderFromJson│  │ ApplyProportional│   │   │
│  │  │ Async()     │  │ ()           │  │ Layout()         │   │   │
│  │  └─────────────┘  └──────────────┘  └──────────────────┘   │   │
│  └─────────────────────────────────────────────────────────────┘   │
│         │                  │                       │              │
│         ▼                  ▼                       ▼              │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │                    ConfigContainer                          │   │
│  │  ┌─────────────┐  ┌──────────────┐  ┌──────────────────┐   │   │
│  │  │ FindSub     │  │ FindControl  │  │ LoadConfig       │   │   │
│  │  │ Container() │  │ <T>()        │  │ Async()          │   │   │
│  │  └─────────────┘  └──────────────┘  └──────────────────┘   │   │
│  └─────────────────────────────────────────────────────────────┘   │
│         │                  │                       │              │
│         ▼                  ▼                       ▼              │
│  ┌─────────────┐    ┌──────────────┐    ┌──────────────────┐     │
│  │ MainForm    │    │ content      │    │ sideContainer    │     │
│  │ (事件绑定)   │    │ Container    │    │ (动态加载)       │     │
│  └─────────────┘    └──────────────┘    └──────────────────┘     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 十、总结

本次改造的核心思路是 **"JSON 驱动布局 + 代码绑定事件"**：

1. **布局定义** → `MainLayout.json`（比例值 0.0~1.0）
2. **布局渲染** → `ConfigContainer` + `WdUIComposer`
3. **控件查找** → `FindSubContainer` / `FindControl<T>`
4. **事件绑定** → 代码中通过 FindControl 获取引用后绑定
5. **自适应** → `ApplyProportionalLayout` 在 Resize 时自动重算

改造后，MainForm 不再有任何硬编码的控件创建逻辑，所有 UI 结构由 JSON 配置驱动，窗口拉伸时所有控件自动按比例调整大小和位置。
