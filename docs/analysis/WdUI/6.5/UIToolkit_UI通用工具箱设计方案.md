# UI 通用工具箱设计方案

## 概述

UI 通用工具箱（UIToolkit）是一个独立可复用的 WinForms 组件库，提供类似浏览器 DevTools 的 UI 元素检查和操作能力。用户可以通过鼠标右键菜单激活工具箱，像操作 HTML 元素一样操作 WinForms UI 控件。

## 设计目标

1. **独立可复用**：作为 WdUI 框架的一部分，不依赖 CentralConsole，可被任何 WinForms 项目引用
2. **右键激活**：通过鼠标右键菜单打开工具箱功能
3. **C 盘持久化**：UI 调整信息保存到 `%LOCALAPPDATA%\WorkFlowPrj\Toolkit\`
4. **实时操作**：悬停高亮、点击选中、拖拽缩放、属性编辑

## 架构总览

```
WdUI/
├── Toolkit/                          # UI工具箱组件库
│   ├── UIToolkit.cs                  # 工具箱入口（安装/卸载右键菜单）
│   ├── Selection/
│   │   ├── ElementSelector.cs        # 元素选择器（悬停高亮+点击选中）
│   │   └── SelectionBorder.cs        # 选中边框绘制（透明覆盖层）
│   ├── PropertyPanel/
│   │   ├── PropertyPanel.cs          # 属性面板控件
│   │   └── PropertyItem.cs           # 属性项定义
│   ├── Resize/
│   │   ├── ResizeHandleOverlay.cs    # 缩放手柄覆盖层
│   │   └── ResizeOperation.cs        # 缩放操作逻辑
│   ├── Actions/
│   │   ├── DragMoveAction.cs         # 拖拽移动
│   │   ├── ResizeAction.cs           # 统一尺寸
│   │   └── AlignAction.cs            # 对齐辅助
│   ├── Persistence/
│   │   ├── LayoutStateStore.cs       # 布局状态持久化（C盘）
│   │   └── LayoutSnapshot.cs         # 布局快照模型
│   └── Commands/
│       └── ToolkitCommands.cs        # 工具箱命令注册
└── docs/
    └── UIToolkit_UI通用工具箱设计方案.md
```

## 交互流程

```
用户右键点击任意控件
        │
        ▼
弹出右键菜单（ContextMenuStrip）
   ┌─────────────────────────────────────┐
   │ 🔍 检查元素 (Inspect Element)       │
   │ 📐 工具箱 (Toolkit) ▸               │
   │   ├── 元素选择器                    │
   │   ├── 属性面板                      │
   │   ├── 缩放手柄                      │
   │   ├── 布局分析器                    │
   │   └── 设置...                       │
   │ 📋 复制控件信息                     │
   │ 📊 布局快照 ▸                       │
   │   ├── 保存快照                      │
   │   └── 加载快照...                   │
   └─────────────────────────────────────┘
        │
        ▼
选择功能 → 工具箱激活对应工具
```

## 模块设计

### 1. UIToolkit（工具箱入口）

```csharp
public class UIToolkit : IDisposable
{
    public void Install(Form targetForm);          // 安装右键菜单
    public void Uninstall();                       // 卸载
    public Control? SelectedControl { get; }       // 当前选中控件
    public event Action<Control>? OnControlSelected; // 选中事件
    public bool IsInstalled { get; }
}
```

**实现要点：**
- 使用 **WndProc 钩子**（子类化 `NativeWindow`）拦截 `WM_CONTEXTMENU` 消息，而非递归为每个控件设置 ContextMenuStrip
- 动态添加的控件自动支持右键菜单，无需重新安装
- 内部维护子模块实例：`ElementSelector`（内含 `SelectionBorder`）、`PropertyPanel`、`ResizeHandleOverlay`、`DragMoveAction`、`LayoutStateStore`
- 选中控件时联动显示 PropertyPanel 和 ResizeHandleOverlay
- 通过 `WdUIEvents` 事件总线广播 `OnControlSelected` 事件

### 2. ElementSelector（元素选择器）

```csharp
public class ElementSelector : IDisposable
{
    public void Enable(Form targetForm);           // 启用选择模式
    public void Disable();                         // 禁用
    public bool IsEnabled { get; }
    public event Action<Control>? OnControlSelected;
}
```

**实现要点：**
- 在 Form 级别订阅 `MouseMove`，使用 `Control.GetChildAtPoint()` 获取鼠标下控件
- 排除工具箱自身的覆盖层（通过 Tag 标记或类型判断）
- 悬停时调用 `SelectionBorder.Show()` 绘制高亮边框
- 点击时触发 `OnControlSelected` 事件
- 可复用 `WinFormsControlFinder` 的查找逻辑

### 3. SelectionBorder（选中边框）

```csharp
public class SelectionBorder : IDisposable
{
    public void Show(Control target);              // 显示选中边框
    public void Hide();                            // 隐藏
    public Color BorderColor { get; set; } = Color.DodgerBlue;
    public int BorderWidth { get; set; } = 2;
}
```

**实现要点：**
- 使用**透明覆盖层 Panel**（`BackColor=Transparent`, `Enabled=false`）绘制边框，不污染原控件
- 覆盖层作为 Form 的子控件，置于最上层（`BringToFront()`）
- 在 Paint 事件中用 `ControlPaint.DrawBorder` 绘制虚线边框
- 订阅 target 的 `LocationChanged`/`SizeChanged`/`ParentChanged`/`VisibleChanged` 事件，同步更新覆盖层位置

### 4. PropertyPanel（属性面板）

```csharp
public class PropertyPanel : Panel
{
    public void BindTo(Control control);           // 绑定控件，显示属性
    public void Unbind();                          // 解除绑定
    public Control? BoundControl { get; }
}
```

**实现要点：**
- 继承 `Panel`，支持 Dock=Right/Left 停靠
- `BindTo(Control)` 时动态生成属性编辑行（TableLayoutPanel 布局）
- 属性编辑器映射：
  - string → TextBox
  - int → NumericUpDown
  - bool → CheckBox
  - DockStyle → ComboBox
  - AnchorStyles → CheckedListBox
  - double → NumericUpDown（0.0~1.0, 4位小数）
- 编辑器 ValueChanged 时调用 Setter 实时更新控件属性
- Layout 属性编辑：直接设置控件的 `LayoutPosition` 属性（Left/Top/Width/Height 比例值），而非调用 `ApplyProportional`
- 可复用 `ControlValueHelper` 获取/设置控件值
- 可复用 `LayoutPositionHelper` 进行像素/比例转换

**显示的属性：**

| 属性 | 类型 | 编辑器 |
|------|------|--------|
| Name | string | TextBox |
| Text | string | TextBox |
| Location (X, Y) | int, int | 两个 NumericUpDown |
| Size (Width, Height) | int, int | 两个 NumericUpDown |
| Dock | DockStyle | ComboBox |
| Anchor | AnchorStyles | CheckedListBox |
| Visible | bool | CheckBox |
| Enabled | bool | CheckBox |
| Layout (L, T, W, H) | double | 四个 NumericUpDown (0.0~1.0) |

### 5. ResizeHandleOverlay（缩放手柄）

```csharp
public class ResizeHandleOverlay : IDisposable
{
    public void Show(Control target);              // 显示缩放手柄
    public void Hide();                            // 隐藏
    public int HandleSize { get; set; } = 7;
    public Color HandleColor { get; set; } = Color.White;
    public Color HandleBorderColor { get; set; } = Color.DodgerBlue;
}
```

**手柄布局：**
```
┌───┬─────────────┬───┐
│ ↑ │      ↑      │ ↑ │
├───┼─────────────┼───┤
│ ← │             │ → │
├───┼─────────────┼───┤
│ ↓ │      ↓      │ ↓ │
└───┴─────────────┴───┘
   NW    N    NE
    W    .    E
   SW    S    SE
```

**实现要点：**
- 透明覆盖层 Panel，Bounds 比 target 外扩 HandleSize 像素
- 在 Paint 事件中绘制 8 个手柄方块
- 鼠标进入手柄区域时改变 Cursor（SizeNWSE, SizeNS, SizeWE 等）
- 鼠标按下时创建 `ResizeOperation` 并调用 Start/Update/End

### 6. ResizeOperation（缩放操作）

```csharp
public enum ResizeDirection { NW, N, NE, W, E, SW, S, SE }

public class ResizeOperation
{
    public void Start(Control target, ResizeDirection direction, Point startPoint);
    public void Update(Point currentPoint);
    public void End();
    public bool KeepAspectRatio { get; set; }
    public int MinWidth { get; set; } = 10;
    public int MinHeight { get; set; } = 10;
}
```

**实现要点：**
- 纯逻辑类，三阶段模式（Start/Update/End）
- 根据方向计算新 bounds：
  - N/NE/NW：调整 Top + Height
  - S/SE/SW：调整 Height
  - W/NW/SW：调整 Left + Width
  - E/NE/SE：调整 Width
- 支持 `KeepAspectRatio`、`MinWidth`、`MinHeight`
- 调用 `target.SetBounds()` 实时更新

### 7. DragMoveAction（拖拽移动）

```csharp
public class DragMoveAction
{
    public void Start(Control target, Point startPoint);
    public void Update(Point currentPoint);
    public void End();
    public bool EnableSnap { get; set; } = true;
    public int SnapThreshold { get; set; } = 8;
}
```

**实现要点：**
- 三阶段模式，计算偏移量后调用 `target.SetBounds()`
- Snap 对齐：比较 target 的 Left/Right/Top/Bottom/Center 与兄弟控件的对应值，差值 < SnapThreshold 则吸附

### 8. AlignAction（对齐辅助）

```csharp
public class AlignAction
{
    public void AlignLefts(List<Control> targets);
    public void AlignRights(List<Control> targets);
    public void AlignTops(List<Control> targets);
    public void AlignBottoms(List<Control> targets);
    public void AlignCenters(List<Control> targets);   // 水平居中
    public void AlignMiddles(List<Control> targets);   // 垂直居中
    public void DistributeHorizontally(List<Control> targets);
    public void DistributeVertically(List<Control> targets);
}
```

### 9. ResizeAction（统一尺寸）

```csharp
public class ResizeAction
{
    public void MakeSameWidth(List<Control> targets, Control reference);
    public void MakeSameHeight(List<Control> targets, Control reference);
    public void MakeSameSize(List<Control> targets, Control reference);
}
```

### 10. LayoutStateStore（布局状态持久化）

```csharp
public class LayoutStateStore
{
    public static readonly string StorePath { get; }  // %LOCALAPPDATA%\WorkFlowPrj\Toolkit\
    public void SaveLayoutSnapshot(string name, Control container);
    public LayoutSnapshot? LoadLayoutSnapshot(string name);
    public void ApplyLayoutSnapshot(LayoutSnapshot snapshot, Control container);
    public string[] ListSnapshots();
    public void DeleteSnapshot(string name);
    public void SaveSettings(ToolkitSettings settings);
    public ToolkitSettings LoadSettings();
}
```

**存储路径：**
```
%LOCALAPPDATA%\WorkFlowPrj\Toolkit\
├── layouts\*.json          # 布局快照
├── settings.json           # 工具箱设置
└── history.json            # 操作历史（支持撤销/重做）
```

**数据模型：**
```csharp
public class LayoutSnapshot
{
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<ControlState> Controls { get; set; }
}

public class ControlState
{
    public string Name { get; set; }
    public string TypeName { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double? LayoutLeft { get; set; }
    public double? LayoutTop { get; set; }
    public double? LayoutWidth { get; set; }
    public double? LayoutHeight { get; set; }
    public string? Text { get; set; }
    public bool Visible { get; set; }
    public bool Enabled { get; set; }
    public string? Dock { get; set; }
    public string? Anchor { get; set; }
    public List<ControlState> Children { get; set; }
}
```

**实现要点：**
- 可复用 `WdUIComposer.SaveToFile` / `LoadFromFile`
- 可复用 `WinFormsContainerAdapter` 遍历控件树

### 11. ToolkitCommands（工具箱命令）

```csharp
public class ToolkitCommands
{
    public void RegisterCommands(ICommandExecutor executor);
}
```

**支持的命令：**

| 命令 | 说明 | 示例 |
|------|------|------|
| `inspect` | 进入检查模式 | `inspect` |
| `select <name>` | 按名称选中控件 | `select mainSplitter` |
| `resize <name> <w> <h>` | 调整大小 | `resize btn1 200 50` |
| `move <name> <x> <y>` | 移动位置 | `move btn1 100 50` |
| `layout <name> <left> <top> <w> <h>` | 应用比例布局 | `layout btn1 0.1 0.1 0.5 0.3` |
| `snapshot save <name>` | 保存布局快照 | `snapshot save myLayout` |
| `snapshot load <name>` | 加载布局快照 | `snapshot load myLayout` |
| `snapshot list` | 列出所有快照 | `snapshot list` |

**实现要点：**
- 注册到 `WinFormsCommandEngine` 同一个 `ICommandExecutor`，命令共存互不干扰
- 在 `WdUIComposer.Initialize()` 中自动注册

## 可复用的现有基础设施

| 现有模块 | 所在文件 | Toolkit 复用场景 |
|---------|---------|----------------|
| **`WinFormsControlFinder`** | `UI/Utilities/WinFormsControlFinder.cs` | ElementSelector 按名称查找控件 |
| **`ControlExtensions`** | `UI/WdUI.Utilities.cs` | `FindControlByName`、`SetBoundsRelative`、`SetControlValue` |
| **`WdUILayout`** | `UI/WdUI.Layout.cs` | PropertyPanel 的 Layout 属性编辑 |
| **`LayoutPositionHelper`** | `UI/Utilities/LayoutPositionHelper.cs` | PropertyPanel 像素/比例转换 |
| **`ControlValueHelper`** | `UI/Controls/ControlValueHelper.cs` | PropertyPanel 获取/设置控件值 |
| **`WinFormsContainerAdapter`** | `UI/Adapters/WinFormsContainerAdapter.cs` | LayoutStateStore 遍历控件树 |
| **`WdUIEvents`** | `UI/WdUI.Events.cs` | Toolkit 选中事件广播 |
| **`WdUIComposer`** | `UI/WdUIComposer.cs` | 集成入口，注册 ToolkitCommands |

## 模块依赖关系

```
LayoutSnapshot (无依赖)
    ↑
LayoutStateStore (依赖 LayoutSnapshot, WinFormsContainerAdapter, WdUIComposer)
    ↑
ResizeOperation (无依赖)    DragMoveAction (无依赖)
    ↑                            ↑
ResizeHandleOverlay (依赖 ResizeOperation)
    ↑
ElementSelector (内含 SelectionBorder, 依赖 WinFormsControlFinder)
    ↑
PropertyItem (无依赖)
    ↑
PropertyPanel (依赖 PropertyItem, ControlValueHelper, LayoutPositionHelper, WdUILayout)
    ↑
AlignAction (无依赖)    ResizeAction (无依赖)
    ↑
UIToolkit (依赖以上所有模块)
    ↑
ToolkitCommands (依赖 UIToolkit, WinFormsControlFinder, WdUILayout)
    ↑
WdUIComposer (集成入口)
```

## 关键设计决策

| 决策 | 方案 | 理由 |
|------|------|------|
| 右键菜单实现 | WndProc 钩子（子类化 NativeWindow） | 性能优，动态控件自动支持 |
| 可视化元素 | 透明覆盖层 Panel | 不污染原控件 Paint 事件 |
| 命令注册 | 注册到现有 ICommandExecutor | 与 WinFormsCommandEngine 共存 |
| 事件集成 | 通过 WdUIEvents 广播 | 其他模块可订阅 |
| 持久化路径 | `%LOCALAPPDATA%\WorkFlowPrj\Toolkit\` | 标准 Windows 应用数据目录 |

## 详细开发步骤

按依赖逆序分为 **5 个阶段、15 个步骤**。

### 阶段一：基础设施层（无外部依赖）

| 步骤 | 文件 | 内容 | 预估 |
|------|------|------|------|
| 1 | — | 创建 `Toolkit/` 目录结构 | 0.1天 |
| 2 | `Persistence/LayoutSnapshot.cs` | `LayoutSnapshot`、`ControlState`、`ToolkitSettings` 数据模型 | 0.5天 |
| 3 | `Resize/ResizeOperation.cs` | `ResizeDirection` 枚举 + 三阶段缩放逻辑 | 0.5天 |
| 4 | `Actions/DragMoveAction.cs` | 三阶段拖拽 + Snap 对齐逻辑 | 0.5天 |
| 5 | `Actions/AlignAction.cs` | 8 种对齐/分布操作 | 0.5天 |
| 6 | `Actions/ResizeAction.cs` | 3 种统一尺寸操作 | 0.3天 |
| 7 | `PropertyPanel/PropertyItem.cs` | 属性项定义 + 编辑器工厂 | 0.5天 |

### 阶段二：可视化组件层

| 步骤 | 文件 | 依赖 | 内容 | 预估 |
|------|------|------|------|------|
| 8 | `Selection/SelectionBorder.cs` | 无 | 透明覆盖层 Panel 绘制虚线边框 | 0.5天 |
| 9 | `Selection/ElementSelector.cs` | Step 8 | MouseMove 悬停高亮 + 点击选中 | 0.5天 |
| 10 | `Resize/ResizeHandleOverlay.cs` | Step 3 | 8 个缩放手柄覆盖层 | 1天 |
| 11 | `PropertyPanel/PropertyPanel.cs` | Step 7 | 属性编辑面板，Dock 停靠 | 1天 |

### 阶段三：持久化层

| 步骤 | 文件 | 依赖 | 内容 | 预估 |
|------|------|------|------|------|
| 12 | `Persistence/LayoutStateStore.cs` | Step 2 | 快照保存/加载/应用 + 设置管理 | 1天 |

### 阶段四：入口与命令层

| 步骤 | 文件 | 依赖 | 内容 | 预估 |
|------|------|------|------|------|
| 13 | `UIToolkit.cs` | Step 3-12 | WndProc 钩子 + 右键菜单 + 子模块联动 | 1天 |
| 14 | `Commands/ToolkitCommands.cs` | Step 13 | 8 个工具箱命令注册 | 0.5天 |

### 阶段五：集成

| 步骤 | 文件 | 依赖 | 内容 | 预估 |
|------|------|------|------|------|
| 15 | `WdUIComposer.cs` + `WdUIComposerTestForm.cs` | Step 13-14 | 集成 InstallToolkit/UninstallToolkit + 测试按钮 | 1天 |

### 汇总

| 阶段 | 步骤数 | 文件数 | 预估 |
|------|--------|--------|------|
| 一：基础设施 | 7 | 6 | 2.9天 |
| 二：可视化组件 | 4 | 4 | 3天 |
| 三：持久化 | 1 | 1 | 1天 |
| 四：入口与命令 | 2 | 2 | 1.5天 |
| 五：集成 | 1 | 2 | 1天 |
| **合计** | **15** | **15** | **9.4天** |

## 使用示例

### 在 CentralConsole 中使用

```csharp
public partial class MainForm : Form
{
    private UIToolkit _toolkit;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _toolkit = new UIToolkit();
        _toolkit.Install(this);
        _toolkit.OnControlSelected += control =>
        {
            Console.WriteLine($"选中控件: {control.Name} ({control.GetType().Name})");
        };
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _toolkit?.Dispose();
        base.OnFormClosed(e);
    }
}
```

### 通过 WdUIComposer 使用

```csharp
WdUIComposer.Initialize();
WdUIComposer.InstallToolkit(this);
// ...
WdUIComposer.UninstallToolkit();
WdUIComposer.Shutdown();
```

## 注意事项

1. **右键菜单**：使用 WndProc 钩子统一拦截 `WM_CONTEXTMENU` 消息，动态控件自动支持
2. **覆盖层排除**：ElementSelector 的 `GetChildAtPoint` 需排除工具箱自身的覆盖层控件
3. **线程安全**：所有 UI 操作必须在主线程执行
4. **异常处理**：工具箱不应影响宿主应用，所有异常应被捕获并记录
5. **资源释放**：使用完后必须调用 `Dispose()` 释放资源
6. **兼容性**：支持 .NET 10.0-windows 及以上版本
