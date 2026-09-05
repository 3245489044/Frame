# UIToolkit 函数设计方案

> 基于 `docs/UIToolkit_UI通用工具箱设计方案.md` 构建，为每个模块提供详细的函数签名、参数说明、实现要点和依赖关系。

---

## 目录

- [阶段一：基础设施层](#阶段一基础设施层)
  - [1.1 LayoutSnapshot - 数据模型](#11-layoutsnapshot---数据模型)
  - [1.2 ResizeOperation - 缩放操作](#12-resizeoperation---缩放操作)
  - [1.3 DragMoveAction - 拖拽移动](#13-dragmoveaction---拖拽移动)
  - [1.4 AlignAction - 对齐辅助](#14-alignaction---对齐辅助)
  - [1.5 ResizeAction - 统一尺寸](#15-resizeaction---统一尺寸)
  - [1.6 PropertyItem - 属性项定义](#16-propertyitem---属性项定义)
- [阶段二：可视化组件层](#阶段二可视化组件层)
  - [2.1 SelectionBorder - 选中边框](#21-selectionborder---选中边框)
  - [2.2 ElementSelector - 元素选择器](#22-elementselector---元素选择器)
  - [2.3 ResizeHandleOverlay - 缩放手柄](#23-resizehandleoverlay---缩放手柄)
  - [2.4 PropertyPanel - 属性面板](#24-propertypanel---属性面板)
- [阶段三：持久化层](#阶段三持久化层)
  - [3.1 LayoutStateStore - 布局状态持久化](#31-layoutstatestore---布局状态持久化)
- [阶段四：入口与命令层](#阶段四入口与命令层)
  - [4.1 UIToolkit - 工具箱入口](#41-uitoolkit---工具箱入口)
  - [4.2 ToolkitCommands - 工具箱命令](#42-toolkitcommands---工具箱命令)
- [阶段五：集成](#阶段五集成)
  - [5.1 WdUIComposer 集成扩展](#51-wduicomposer-集成扩展)
- [附录](#附录)
  - [A. 事件定义](#a-事件定义)
  - [B. 异常处理策略](#b-异常处理策略)
  - [C. 资源释放规范](#c-资源释放规范)

---

## 阶段一：基础设施层

### 1.1 LayoutSnapshot - 数据模型

**命名空间：** `WdUI.UI.Toolkit.Persistence`

**文件路径：** `Toolkit/Persistence/LayoutSnapshot.cs`

---

#### 1.1.1 `LayoutSnapshot` 类

布局快照模型，表示某一时刻容器内所有控件的完整状态。

```csharp
public class LayoutSnapshot
{
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<ControlState> Controls { get; set; }
}
```

| 属性 | 类型 | 说明 |
|------|------|------|
| `Name` | `string` | 快照名称，用于标识和检索 |
| `CreatedAt` | `DateTime` | 创建时间戳 |
| `Controls` | `List<ControlState>` | 根级控件状态列表（递归包含子控件） |

**实现要点：**
- 纯数据模型，无行为逻辑
- 支持 JSON 序列化/反序列化（通过 `System.Text.Json`）
- `Controls` 列表中的每个 `ControlState` 可递归包含 `Children`

---

#### 1.1.2 `ControlState` 类

单个控件的状态快照。

```csharp
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

| 属性 | 类型 | 说明 |
|------|------|------|
| `Name` | `string` | 控件名称（`Control.Name`） |
| `TypeName` | `string` | 控件类型全名（`control.GetType().FullName`） |
| `X`, `Y` | `int` | 控件位置（像素） |
| `Width`, `Height` | `int` | 控件尺寸（像素） |
| `LayoutLeft/Top/Width/Height` | `double?` | 比例布局值（0.0~1.0），null 表示未使用比例布局 |
| `Text` | `string?` | 控件文本 |
| `Visible` | `bool` | 可见性 |
| `Enabled` | `bool` | 启用状态 |
| `Dock` | `string?` | Dock 样式字符串（`DockStyle.ToString()`） |
| `Anchor` | `string?` | Anchor 样式字符串（`AnchorStyles.ToString()`） |
| `Children` | `List<ControlState>` | 子控件状态列表 |

**实现要点：**
- 使用 `string` 存储 Dock/Anchor 而非枚举，确保 JSON 序列化兼容性
- `LayoutLeft/Top/Width/Height` 使用 `double?` 以区分"未设置"和"值为 0"

---

#### 1.1.3 `ToolkitSettings` 类

工具箱设置模型。

```csharp
public class ToolkitSettings
{
    public bool EnableHoverHighlight { get; set; } = true;
    public bool EnableSnap { get; set; } = true;
    public int SnapThreshold { get; set; } = 8;
    public Color BorderColor { get; set; } = Color.DodgerBlue;
    public int BorderWidth { get; set; } = 2;
    public int HandleSize { get; set; } = 7;
    public bool KeepAspectRatio { get; set; } = false;
    public int MinControlWidth { get; set; } = 10;
    public int MinControlHeight { get; set; } = 10;
}
```

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `EnableHoverHighlight` | `bool` | `true` | 是否启用悬停高亮 |
| `EnableSnap` | `bool` | `true` | 是否启用拖拽吸附 |
| `SnapThreshold` | `int` | `8` | 吸附阈值（像素） |
| `BorderColor` | `Color` | `DodgerBlue` | 选中边框颜色 |
| `BorderWidth` | `int` | `2` | 选中边框宽度 |
| `HandleSize` | `int` | `7` | 缩放手柄大小 |
| `KeepAspectRatio` | `bool` | `false` | 缩放时是否保持宽高比 |
| `MinControlWidth` | `int` | `10` | 控件最小宽度 |
| `MinControlHeight` | `int` | `10` | 控件最小高度 |

**实现要点：**
- `Color` 类型需自定义 JSON 序列化/反序列化（序列化为 ARGB 整数或 Hex 字符串）
- 提供 `Clone()` 方法用于设置副本

---

### 1.2 ResizeOperation - 缩放操作

**命名空间：** `WdUI.UI.Toolkit.Resize`

**文件路径：** `Toolkit/Resize/ResizeOperation.cs`

---

#### 1.2.1 `ResizeDirection` 枚举

```csharp
public enum ResizeDirection
{
    NW, // 左上
    N,  // 上
    NE, // 右上
    W,  // 左
    E,  // 右
    SW, // 左下
    S,  // 下
    SE  // 右下
}
```

---

#### 1.2.2 `ResizeOperation` 类

纯逻辑类，实现三阶段缩放操作（Start / Update / End）。

```csharp
public class ResizeOperation
{
}
```

##### 构造函数

```csharp
public ResizeOperation();
```

**说明：** 创建缩放操作实例。

---

##### `Start` 方法

```csharp
public void Start(Control target, ResizeDirection direction, Point startPoint);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `target` | `Control` | 要缩放的控件 |
| `direction` | `ResizeDirection` | 缩放方向 |
| `startPoint` | `Point` | 鼠标按下时的屏幕坐标（或相对于父容器的坐标） |

**功能描述：** 开始缩放操作，记录控件的初始 bounds 和鼠标起始位置。

**实现要点：**
- 保存 `_initialBounds = target.Bounds`（矩形结构）
- 保存 `_startPoint = startPoint`
- 保存 `_target` 和 `_direction` 引用
- 保存 `_parent = target.Parent` 用于坐标转换

---

##### `Update` 方法

```csharp
public void Update(Point currentPoint);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `currentPoint` | `Point` | 鼠标当前位置（与 Start 传入的坐标空间一致） |

**功能描述：** 根据鼠标当前位置计算新的 bounds 并实时更新控件。

**实现要点：**
- 计算偏移量：`dx = currentPoint.X - _startPoint.X`, `dy = currentPoint.Y - _startPoint.Y`
- 根据 `_direction` 计算新 bounds：

| 方向 | Left 调整 | Top 调整 | Width 调整 | Height 调整 |
|------|-----------|----------|------------|-------------|
| N | 不变 | `_initialBounds.Y + dy` | 不变 | `_initialBounds.Height - dy` |
| S | 不变 | 不变 | 不变 | `_initialBounds.Height + dy` |
| W | `_initialBounds.X + dx` | 不变 | `_initialBounds.Width - dx` | 不变 |
| E | 不变 | 不变 | `_initialBounds.Width + dx` | 不变 |
| NW | `_initialBounds.X + dx` | `_initialBounds.Y + dy` | `_initialBounds.Width - dx` | `_initialBounds.Height - dy` |
| NE | 不变 | `_initialBounds.Y + dy` | `_initialBounds.Width + dx` | `_initialBounds.Height - dy` |
| SW | `_initialBounds.X + dx` | 不变 | `_initialBounds.Width - dx` | `_initialBounds.Height + dy` |
| SE | 不变 | 不变 | `_initialBounds.Width + dx` | `_initialBounds.Height + dy` |

- 应用 `MinWidth` / `MinHeight` 约束：
  - 如果 `newWidth < MinWidth`，则调整 Left（对于 W/NW/SW 方向）并固定 Width = MinWidth
  - 如果 `newHeight < MinHeight`，则调整 Top（对于 N/NE/NW 方向）并固定 Height = MinHeight
- 如果 `KeepAspectRatio == true`，根据宽高比调整：
  - 以 Width 为基准计算 `newHeight = newWidth / aspectRatio`
  - 或以 Height 为基准计算 `newWidth = newHeight * aspectRatio`
  - 选择变化量较大的维度作为基准
- 调用 `target.SetBounds(newLeft, newTop, newWidth, newHeight)` 实时更新

---

##### `End` 方法

```csharp
public void End();
```

**功能描述：** 结束缩放操作，清理内部状态。

**实现要点：**
- 将 `_target` 置为 null
- 触发 `OnResizeCompleted` 事件（如果有）

---

##### 属性

```csharp
public bool KeepAspectRatio { get; set; } = false;
public int MinWidth { get; set; } = 10;
public int MinHeight { get; set; } = 10;
```

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `KeepAspectRatio` | `bool` | `false` | 是否保持宽高比 |
| `MinWidth` | `int` | `10` | 最小宽度约束 |
| `MinHeight` | `int` | `10` | 最小高度约束 |

---

##### 事件

```csharp
public event Action<Control, Rectangle>? OnResizeCompleted;
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `control` | `Control` | 被缩放的控件 |
| `finalBounds` | `Rectangle` | 缩放完成后的最终 bounds |

---

### 1.3 DragMoveAction - 拖拽移动

**命名空间：** `WdUI.UI.Toolkit.Actions`

**文件路径：** `Toolkit/Actions/DragMoveAction.cs`

---

#### 1.3.1 `DragMoveAction` 类

纯逻辑类，实现三阶段拖拽移动操作（Start / Update / End），支持 Snap 吸附对齐。

```csharp
public class DragMoveAction
{
}
```

##### 构造函数

```csharp
public DragMoveAction();
```

---

##### `Start` 方法

```csharp
public void Start(Control target, Point startPoint);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `target` | `Control` | 要拖拽的控件 |
| `startPoint` | `Point` | 鼠标按下时的屏幕坐标（或相对于父容器的坐标） |

**功能描述：** 开始拖拽移动操作。

**实现要点：**
- 保存 `_initialLocation = target.Location`
- 保存 `_startPoint = startPoint`
- 保存 `_target` 引用
- 如果 `EnableSnap == true`，预先收集兄弟控件的边界信息用于吸附计算

---

##### `Update` 方法

```csharp
public void Update(Point currentPoint);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `currentPoint` | `Point` | 鼠标当前位置 |

**功能描述：** 根据鼠标位置计算新位置并更新控件。

**实现要点：**
- 计算偏移量：`dx = currentPoint.X - _startPoint.X`, `dy = currentPoint.Y - _startPoint.Y`
- 新位置：`newX = _initialLocation.X + dx`, `newY = _initialLocation.Y + dy`
- 如果 `EnableSnap == true`，执行吸附对齐：
  - 获取 `_target.Parent` 下所有兄弟控件（排除自身）
  - 对每个兄弟控件，比较以下 5 条参考线：
    - Left（左边缘）：`target.Left` vs `sibling.Left`
    - Right（右边缘）：`target.Right` vs `sibling.Right`
    - Top（上边缘）：`target.Top` vs `sibling.Top`
    - Bottom（下边缘）：`target.Bottom` vs `sibling.Bottom`
    - CenterX（水平中心）：`target.Left + target.Width/2` vs `sibling.Left + sibling.Width/2`
    - CenterY（垂直中心）：`target.Top + target.Height/2` vs `sibling.Top + sibling.Height/2`
  - 如果差值 < `SnapThreshold`，则将 target 的对应值吸附到兄弟控件的对应值
- 调用 `target.SetBounds(newX, newY, target.Width, target.Height)` 更新位置

---

##### `End` 方法

```csharp
public void End();
```

**功能描述：** 结束拖拽操作，清理内部状态。

**实现要点：**
- 将 `_target` 置为 null
- 触发 `OnDragCompleted` 事件

---

##### 属性

```csharp
public bool EnableSnap { get; set; } = true;
public int SnapThreshold { get; set; } = 8;
```

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `EnableSnap` | `bool` | `true` | 是否启用吸附对齐 |
| `SnapThreshold` | `int` | `8` | 吸附阈值（像素），差值小于此值时触发吸附 |

---

##### 事件

```csharp
public event Action<Control, Point>? OnDragCompleted;
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `control` | `Control` | 被拖拽的控件 |
| `finalLocation` | `Point` | 拖拽完成后的最终位置 |

---

### 1.4 AlignAction - 对齐辅助

**命名空间：** `WdUI.UI.Toolkit.Actions`

**文件路径：** `Toolkit/Actions/AlignAction.cs`

---

#### 1.4.1 `AlignAction` 类

纯逻辑类，提供 8 种对齐/分布操作。

```csharp
public class AlignAction
{
}
```

##### 构造函数

```csharp
public AlignAction();
```

---

##### `AlignLefts` 方法

```csharp
public void AlignLefts(List<Control> targets);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `targets` | `List<Control>` | 要对齐的控件列表（至少 2 个） |

**功能描述：** 将所有控件的左边缘对齐到最左侧控件的左边缘。

**实现要点：**
- 找到 `targets` 中 `Left` 值最小的控件作为基准
- 遍历其他控件，设置 `control.Left = minLeft`
- 如果 `targets.Count < 2`，直接返回

---

##### `AlignRights` 方法

```csharp
public void AlignRights(List<Control> targets);
```

**功能描述：** 将所有控件的右边缘对齐到最右侧控件的右边缘。

**实现要点：**
- 找到 `targets` 中 `Right`（`Left + Width`）值最大的控件作为基准
- 遍历其他控件，设置 `control.Left = maxRight - control.Width`

---

##### `AlignTops` 方法

```csharp
public void AlignTops(List<Control> targets);
```

**功能描述：** 将所有控件的上边缘对齐到最上方控件的上边缘。

**实现要点：**
- 找到 `targets` 中 `Top` 值最小的控件作为基准
- 遍历其他控件，设置 `control.Top = minTop`

---

##### `AlignBottoms` 方法

```csharp
public void AlignBottoms(List<Control> targets);
```

**功能描述：** 将所有控件的下边缘对齐到最下方控件的下边缘。

**实现要点：**
- 找到 `targets` 中 `Bottom`（`Top + Height`）值最大的控件作为基准
- 遍历其他控件，设置 `control.Top = maxBottom - control.Height`

---

##### `AlignCenters` 方法

```csharp
public void AlignCenters(List<Control> targets);
```

**功能描述：** 将所有控件水平居中（CenterX 对齐）。

**实现要点：**
- 计算每个控件的中心 X：`centerX = control.Left + control.Width / 2`
- 找到基准控件的中心 X（通常取第一个或平均值）
- 遍历其他控件，设置 `control.Left = baseCenterX - control.Width / 2`

---

##### `AlignMiddles` 方法

```csharp
public void AlignMiddles(List<Control> targets);
```

**功能描述：** 将所有控件垂直居中（CenterY 对齐）。

**实现要点：**
- 计算每个控件的中心 Y：`centerY = control.Top + control.Height / 2`
- 找到基准控件的中心 Y
- 遍历其他控件，设置 `control.Top = baseCenterY - control.Height / 2`

---

##### `DistributeHorizontally` 方法

```csharp
public void DistributeHorizontally(List<Control> targets);
```

**功能描述：** 水平均匀分布控件。

**实现要点：**
- 按 `Left` 值对控件排序
- 计算总宽度：`totalWidth = targets.Sum(c => c.Width)`
- 计算可用间距：`spacing = (rightmost.Right - leftmost.Left - totalWidth) / (targets.Count - 1)`
- 从最左侧控件开始，依次设置每个控件的 `Left = previous.Right + spacing`

---

##### `DistributeVertically` 方法

```csharp
public void DistributeVertically(List<Control> targets);
```

**功能描述：** 垂直均匀分布控件。

**实现要点：**
- 按 `Top` 值对控件排序
- 计算总高度：`totalHeight = targets.Sum(c => c.Height)`
- 计算可用间距：`spacing = (bottommost.Bottom - topmost.Top - totalHeight) / (targets.Count - 1)`
- 从最上方控件开始，依次设置每个控件的 `Top = previous.Bottom + spacing`

---

### 1.5 ResizeAction - 统一尺寸

**命名空间：** `WdUI.UI.Toolkit.Actions`

**文件路径：** `Toolkit/Actions/ResizeAction.cs`

---

#### 1.5.1 `ResizeAction` 类

纯逻辑类，提供 3 种统一尺寸操作。

```csharp
public class ResizeAction
{
}
```

##### 构造函数

```csharp
public ResizeAction();
```

---

##### `MakeSameWidth` 方法

```csharp
public void MakeSameWidth(List<Control> targets, Control reference);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `targets` | `List<Control>` | 要调整的控件列表 |
| `reference` | `Control` | 参考控件（以此控件的宽度为准） |

**功能描述：** 将所有目标控件的宽度设置为与参考控件相同。

**实现要点：**
- 遍历 `targets`，设置 `control.Width = reference.Width`
- 如果 `targets` 为空或 `reference` 为 null，直接返回

---

##### `MakeSameHeight` 方法

```csharp
public void MakeSameHeight(List<Control> targets, Control reference);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `targets` | `List<Control>` | 要调整的控件列表 |
| `reference` | `Control` | 参考控件（以此控件的高度为准） |

**功能描述：** 将所有目标控件的高度设置为与参考控件相同。

---

##### `MakeSameSize` 方法

```csharp
public void MakeSameSize(List<Control> targets, Control reference);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `targets` | `List<Control>` | 要调整的控件列表 |
| `reference` | `Control` | 参考控件（以此控件的宽高为准） |

**功能描述：** 将所有目标控件的宽度和高度设置为与参考控件相同。

**实现要点：**
- 同时设置 `control.Width = reference.Width` 和 `control.Height = reference.Height`

---

### 1.6 PropertyItem - 属性项定义

**命名空间：** `WdUI.UI.Toolkit.PropertyPanel`

**文件路径：** `Toolkit/PropertyPanel/PropertyItem.cs`

---

#### 1.6.1 `PropertyItem` 类

属性项定义，描述一个可编辑的控件属性及其编辑器类型。

```csharp
public class PropertyItem
{
}
```

##### 构造函数

```csharp
public PropertyItem(string displayName, string propertyName, Type propertyType, PropertyEditorType editorType);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `displayName` | `string` | 属性显示名称（如 "位置 X"） |
| `propertyName` | `string` | 属性名称（如 "Left"），用于反射调用 Setter |
| `propertyType` | `Type` | 属性类型（如 `typeof(int)`） |
| `editorType` | `PropertyEditorType` | 编辑器类型枚举 |

---

#### 1.6.2 `PropertyEditorType` 枚举

```csharp
public enum PropertyEditorType
{
    TextBox,        // string 类型
    NumericUpDown,  // int / double 类型
    CheckBox,       // bool 类型
    ComboBox,       // 枚举类型（如 DockStyle）
    CheckedListBox, // 标志位枚举（如 AnchorStyles）
    Label           // 只读显示
}
```

---

#### 1.6.3 `PropertyItem` 属性

```csharp
public string DisplayName { get; }
public string PropertyName { get; }
public Type PropertyType { get; }
public PropertyEditorType EditorType { get; }
public Func<Control, object?>? Getter { get; set; }
public Action<Control, object?>? Setter { get; set; }
public Func<IEnumerable<object>>? ValueSource { get; set; } // ComboBox 选项源
public object? MinValue { get; set; }  // NumericUpDown 最小值
public object? MaxValue { get; set; }  // NumericUpDown 最大值
public int DecimalPlaces { get; set; } = 0; // NumericUpDown 小数位数
```

| 属性 | 类型 | 说明 |
|------|------|------|
| `DisplayName` | `string` | 属性显示名称 |
| `PropertyName` | `string` | 反射属性名 |
| `PropertyType` | `Type` | 属性类型 |
| `EditorType` | `PropertyEditorType` | 编辑器类型 |
| `Getter` | `Func<Control, object?>?` | 自定义 Getter（null 时使用反射） |
| `Setter` | `Action<Control, object?>?` | 自定义 Setter（null 时使用反射） |
| `ValueSource` | `Func<IEnumerable<object>>?` | ComboBox 的选项源 |
| `MinValue` | `object?` | NumericUpDown 最小值 |
| `MaxValue` | `object?` | NumericUpDown 最大值 |
| `DecimalPlaces` | `int` | NumericUpDown 小数位数 |

---

#### 1.6.4 `PropertyItemFactory` 静态类

属性项工厂，提供预定义的属性项列表。

```csharp
public static class PropertyItemFactory
{
}
```

##### `CreateDefaultProperties` 方法

```csharp
public static List<PropertyItem> CreateDefaultProperties();
```

**功能描述：** 创建默认的属性项列表（对应设计文档中的属性表）。

**返回值：** 包含以下属性项的列表：

| 显示名称 | 属性名 | 类型 | 编辑器 | 备注 |
|---------|--------|------|--------|------|
| Name | `Name` | `string` | TextBox | |
| Text | `Text` | `string` | TextBox | |
| X | `Left` | `int` | NumericUpDown | |
| Y | `Top` | `int` | NumericUpDown | |
| Width | `Width` | `int` | NumericUpDown | |
| Height | `Height` | `int` | NumericUpDown | |
| Dock | `Dock` | `DockStyle` | ComboBox | 选项：None, Top, Bottom, Left, Right, Fill |
| Anchor | `Anchor` | `AnchorStyles` | CheckedListBox | 选项：Top, Bottom, Left, Right |
| Visible | `Visible` | `bool` | CheckBox | |
| Enabled | `Enabled` | `bool` | CheckBox | |
| Layout L | `LayoutLeft` | `double` | NumericUpDown | 0.0~1.0, 4位小数 |
| Layout T | `LayoutTop` | `double` | NumericUpDown | 0.0~1.0, 4位小数 |
| Layout W | `LayoutWidth` | `double` | NumericUpDown | 0.0~1.0, 4位小数 |
| Layout H | `LayoutHeight` | `double` | NumericUpDown | 0.0~1.0, 4位小数 |

**实现要点：**
- Layout 属性（L/T/W/H）使用自定义 Getter/Setter：
  - Getter：通过 `LayoutPositionHelper` 将像素值转换为比例值
  - Setter：直接设置控件的 `LayoutPosition` 属性（Left/Top/Width/Height 比例值），而非调用 `ApplyProportional`
- Dock 属性的 ComboBox 选项通过 `ValueSource` 提供
- Anchor 属性的 CheckedListBox 选项通过 `ValueSource` 提供

---

## 阶段二：可视化组件层

### 2.1 SelectionBorder - 选中边框

**命名空间：** `WdUI.UI.Toolkit.Selection`

**文件路径：** `Toolkit/Selection/SelectionBorder.cs`

---

#### 2.1.1 `SelectionBorder` 类

透明覆盖层 Panel，用于在选中控件周围绘制高亮虚线边框。

```csharp
public class SelectionBorder : IDisposable
{
}
```

##### 构造函数

```csharp
public SelectionBorder(Form parentForm);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `parentForm` | `Form` | 所属窗体，覆盖层将添加到此窗体的控件集合中 |

**功能描述：** 创建选中边框覆盖层。

**实现要点：**
- 内部创建 `Panel` 实例（`_overlay`），设置：
  - `BackColor = Color.Transparent`
  - `Enabled = false`
  - 不拦截鼠标事件
- 覆盖层不添加到 Form.Controls，而是作为独立控件管理

---

##### `Show` 方法

```csharp
public void Show(Control target);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `target` | `Control` | 要显示边框的目标控件 |

**功能描述：** 在指定控件周围显示选中边框。

**实现要点：**
- 如果 `_overlay` 尚未添加到 Form，则添加到 `_parentForm.Controls` 并 `BringToFront()`
- 设置 `_overlay.Bounds = target.Bounds`（与 target 完全重叠）
- 订阅 target 的位置/尺寸变化事件以同步更新覆盖层：
  - `target.LocationChanged += OnTargetLocationChanged`
  - `target.SizeChanged += OnTargetSizeChanged`
  - `target.ParentChanged += OnTargetParentChanged`
  - `target.VisibleChanged += OnTargetVisibleChanged`
- 调用 `_overlay.Invalidate()` 触发重绘

---

##### `Hide` 方法

```csharp
public void Hide();
```

**功能描述：** 隐藏选中边框。

**实现要点：**
- 取消订阅所有 target 事件
- 从 Form 中移除 `_overlay`
- 将 `_overlay` 的 Visible 设为 false

---

##### 属性

```csharp
public Color BorderColor { get; set; } = Color.DodgerBlue;
public int BorderWidth { get; set; } = 2;
```

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `BorderColor` | `Color` | `DodgerBlue` | 边框颜色 |
| `BorderWidth` | `int` | `2` | 边框宽度（像素） |

---

##### `Dispose` 方法

```csharp
public void Dispose();
```

**功能描述：** 释放资源，移除覆盖层。

**实现要点：**
- 调用 `Hide()`
- 销毁 `_overlay` 控件

---

##### 内部事件处理

```csharp
private void OnTargetLocationChanged(object? sender, EventArgs e);
private void OnTargetSizeChanged(object? sender, EventArgs e);
private void OnTargetParentChanged(object? sender, EventArgs e);
private void OnTargetVisibleChanged(object? sender, EventArgs e);
```

**实现要点：**
- `OnTargetLocationChanged` / `OnTargetSizeChanged`：同步更新 `_overlay.Bounds`
- `OnTargetParentChanged`：重新计算覆盖层位置（可能需要从旧父容器移除并添加到新父容器）
- `OnTargetVisibleChanged`：同步 `_overlay.Visible = target.Visible`

---

##### Paint 事件

```csharp
private void OnOverlayPaint(object? sender, PaintEventArgs e);
```

**实现要点：**
- 使用 `ControlPaint.DrawBorder` 绘制虚线边框：
  ```csharp
  ControlPaint.DrawBorder(e.Graphics, _overlay.ClientRectangle,
      BorderColor, BorderWidth, ButtonBorderStyle.Dashed,
      BorderColor, BorderWidth, ButtonBorderStyle.Dashed,
      BorderColor, BorderWidth, ButtonBorderStyle.Dashed,
      BorderColor, BorderWidth, ButtonBorderStyle.Dashed);
  ```
- 在边框四角绘制小方块标记（可选）

---

### 2.2 ElementSelector - 元素选择器

**命名空间：** `WdUI.UI.Toolkit.Selection`

**文件路径：** `Toolkit/Selection/ElementSelector.cs`

---

#### 2.2.1 `ElementSelector` 类

通过鼠标悬停高亮 + 点击选中实现元素选择功能。

```csharp
public class ElementSelector : IDisposable
{
}
```

##### 构造函数

```csharp
public ElementSelector(Form targetForm);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `targetForm` | `Form` | 目标窗体 |

**功能描述：** 创建元素选择器。

**实现要点：**
- 保存 `_targetForm` 引用
- 创建 `SelectionBorder` 实例
- 使用 `WinFormsControlFinder` 用于控件查找（可选）

---

##### `Enable` 方法

```csharp
public void Enable();
```

**功能描述：** 启用选择模式，开始监听鼠标事件。

**实现要点：**
- 订阅 `_targetForm.MouseMove` 事件
- 订阅 `_targetForm.MouseClick` 事件
- 设置 `IsEnabled = true`

---

##### `Disable` 方法

```csharp
public void Disable();
```

**功能描述：** 禁用选择模式，停止监听鼠标事件。

**实现要点：**
- 取消订阅 `MouseMove` 和 `MouseClick` 事件
- 调用 `SelectionBorder.Hide()` 隐藏高亮
- 设置 `IsEnabled = false`

---

##### 属性

```csharp
public bool IsEnabled { get; private set; }
public Control? HoveredControl { get; private set; }
```

| 属性 | 类型 | 说明 |
|------|------|------|
| `IsEnabled` | `bool` | 选择模式是否启用 |
| `HoveredControl` | `Control?` | 当前悬停的控件 |

---

##### 事件

```csharp
public event Action<Control>? OnControlSelected;
public event Action<Control>? OnControlHovered;
```

| 事件 | 说明 |
|------|------|
| `OnControlSelected` | 点击选中控件时触发 |
| `OnControlHovered` | 悬停到控件时触发 |

---

##### 内部事件处理

```csharp
private void OnMouseMove(object? sender, MouseEventArgs e);
private void OnMouseClick(object? sender, MouseEventArgs e);
```

**`OnMouseMove` 实现要点：**
- 使用 `_targetForm.GetChildAtPoint(e.Location)` 获取鼠标下的控件
- **排除工具箱自身的覆盖层**：通过 Tag 标记（如 `Tag = "__TOOLKIT_OVERLAY__"`）或类型判断
- 如果找到的控件与 `HoveredControl` 不同，则更新高亮：
  - 如果 `HoveredControl != null`，取消旧控件的高亮
  - 设置 `HoveredControl = foundControl`
  - 调用 `SelectionBorder.Show(foundControl)` 显示高亮边框
  - 触发 `OnControlHovered` 事件
- 如果 `foundControl == null`，隐藏高亮

**`OnMouseClick` 实现要点：**
- 仅处理左键点击（`e.Button == MouseButtons.Left`）
- 如果 `HoveredControl != null`，触发 `OnControlSelected` 事件
- 不阻止事件继续传播（不设置 `Handled`）

---

##### `Dispose` 方法

```csharp
public void Dispose();
```

**功能描述：** 释放资源。

**实现要点：**
- 调用 `Disable()`
- 调用 `SelectionBorder.Dispose()`

---

### 2.3 ResizeHandleOverlay - 缩放手柄

**命名空间：** `WdUI.UI.Toolkit.Resize`

**文件路径：** `Toolkit/Resize/ResizeHandleOverlay.cs`

---

#### 2.3.1 `ResizeHandleOverlay` 类

透明覆盖层 Panel，在选中控件周围绘制 8 个缩放手柄，支持鼠标拖拽缩放。

```csharp
public class ResizeHandleOverlay : IDisposable
{
}
```

##### 构造函数

```csharp
public ResizeHandleOverlay(Form parentForm);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `parentForm` | `Form` | 所属窗体 |

**功能描述：** 创建缩放手柄覆盖层。

**实现要点：**
- 内部创建 `Panel` 实例（`_overlay`），设置：
  - `BackColor = Color.Transparent`
  - `Enabled = false`
  - `Tag = "__TOOLKIT_OVERLAY__"`（用于 ElementSelector 排除）
- 创建 `ResizeOperation` 实例用于缩放逻辑

---

##### `Show` 方法

```csharp
public void Show(Control target);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `target` | `Control` | 要显示手柄的目标控件 |

**功能描述：** 在指定控件周围显示缩放手柄。

**实现要点：**
- 将 `_overlay` 添加到 `_parentForm.Controls` 并 `BringToFront()`
- 设置 `_overlay.Bounds` 比 target 外扩 `HandleSize` 像素：
  ```csharp
  _overlay.Bounds = new Rectangle(
      target.Left - HandleSize, target.Top - HandleSize,
      target.Width + HandleSize * 2, target.Height + HandleSize * 2);
  ```
- 订阅 target 的位置/尺寸变化事件以同步更新覆盖层
- 订阅 `_overlay.MouseDown`、`MouseMove`、`MouseUp` 事件

---

##### `Hide` 方法

```csharp
public void Hide();
```

**功能描述：** 隐藏缩放手柄。

**实现要点：**
- 取消订阅所有事件
- 从 Form 中移除 `_overlay`
- 如果正在缩放操作中，调用 `ResizeOperation.End()`

---

##### 属性

```csharp
public int HandleSize { get; set; } = 7;
public Color HandleColor { get; set; } = Color.White;
public Color HandleBorderColor { get; set; } = Color.DodgerBlue;
```

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `HandleSize` | `int` | `7` | 手柄方块大小（像素） |
| `HandleColor` | `Color` | `White` | 手柄填充颜色 |
| `HandleBorderColor` | `Color` | `DodgerBlue` | 手柄边框颜色 |

---

##### `Dispose` 方法

```csharp
public void Dispose();
```

**功能描述：** 释放资源。

---

##### Paint 事件

```csharp
private void OnOverlayPaint(object? sender, PaintEventArgs e);
```

**实现要点：**
- 在覆盖层的 8 个位置绘制手柄方块：

| 手柄 | 位置（相对于覆盖层 ClientRectangle） |
|------|--------------------------------------|
| NW | `(0, 0)` |
| N | `((width - handleSize) / 2, 0)` |
| NE | `(width - handleSize, 0)` |
| W | `(0, (height - handleSize) / 2)` |
| E | `(width - handleSize, (height - handleSize) / 2)` |
| SW | `(0, height - handleSize)` |
| S | `((width - handleSize) / 2, height - handleSize)` |
| SE | `(width - handleSize, height - handleSize)` |

- 每个手柄绘制为填充方块 + 边框：
  ```csharp
  using var fillBrush = new SolidBrush(HandleColor);
  using var borderPen = new Pen(HandleBorderColor);
  e.Graphics.FillRectangle(fillBrush, handleRect);
  e.Graphics.DrawRectangle(borderPen, handleRect);
  ```

---

##### 鼠标事件处理

```csharp
private void OnOverlayMouseDown(object? sender, MouseEventArgs e);
private void OnOverlayMouseMove(object? sender, MouseEventArgs e);
private void OnOverlayMouseUp(object? sender, MouseEventArgs e);
```

**`OnOverlayMouseDown` 实现要点：**
- 仅处理左键按下
- 通过 `GetHandleAtPoint(e.Location)` 确定点击的手柄方向
- 如果点击到手柄区域，调用 `ResizeOperation.Start(target, direction, e.Location)`
- 设置 `_isResizing = true`

**`OnOverlayMouseMove` 实现要点：**
- 如果 `_isResizing == true`，调用 `ResizeOperation.Update(e.Location)`
- 否则，根据鼠标位置更新光标样式：
  - NW/SE → `Cursors.SizeNWSE`
  - NE/SW → `Cursors.SizeNESW`
  - N/S → `Cursors.SizeNS`
  - W/E → `Cursors.SizeWE`
  - 其他区域 → `Cursors.Default`

**`OnOverlayMouseUp` 实现要点：**
- 如果 `_isResizing == true`，调用 `ResizeOperation.End()`
- 设置 `_isResizing = false`

---

##### 辅助方法

```csharp
private ResizeDirection? GetHandleAtPoint(Point point);
```

**功能描述：** 根据鼠标点击位置确定对应的缩放方向。

**实现要点：**
- 遍历 8 个手柄的矩形区域，判断 `point` 是否落在某个手柄区域内
- 返回对应的 `ResizeDirection` 枚举值，如果不在任何手柄区域则返回 null

---

### 2.4 PropertyPanel - 属性面板

**命名空间：** `WdUI.UI.Toolkit.PropertyPanel`

**文件路径：** `Toolkit/PropertyPanel/PropertyPanel.cs`

---

#### 2.4.1 `PropertyPanel` 类

继承 `Panel` 的属性编辑面板，支持 Dock 停靠，动态生成属性编辑行。

```csharp
public class PropertyPanel : Panel
{
}
```

##### 构造函数

```csharp
public PropertyPanel();
```

**功能描述：** 创建属性面板。

**实现要点：**
- 设置 `AutoScroll = true`
- 设置 `BorderStyle = BorderStyle.FixedSingle`
- 设置默认宽度 280
- 内部使用 `TableLayoutPanel` 布局属性编辑行

---

##### `BindTo` 方法

```csharp
public void BindTo(Control control);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `control` | `Control` | 要编辑的控件 |

**功能描述：** 绑定到指定控件，显示其可编辑属性。

**实现要点：**
- 如果已有绑定的控件，先调用 `Unbind()`
- 保存 `_boundControl = control`
- 清空 `TableLayoutPanel`
- 调用 `PropertyItemFactory.CreateDefaultProperties()` 获取属性项列表
- 为每个属性项动态创建编辑行：
  - 每行包含：Label（属性名）+ 编辑器控件
  - 根据 `PropertyEditorType` 创建对应的编辑器：
    - `TextBox`：创建 `TextBox`，订阅 `TextChanged`
    - `NumericUpDown`：创建 `NumericUpDown`，设置 `Minimum`/`Maximum`/`DecimalPlaces`，订阅 `ValueChanged`
    - `CheckBox`：创建 `CheckBox`，订阅 `CheckedChanged`
    - `ComboBox`：创建 `ComboBox`，设置 `DataSource`（来自 `ValueSource`），订阅 `SelectedIndexChanged`
    - `CheckedListBox`：创建 `CheckedListBox`，设置 `Items`（来自 `ValueSource`），订阅 `ItemCheck`
  - 从控件读取当前值并设置到编辑器
  - 编辑器 ValueChanged 时调用 Setter 实时更新控件属性
- 订阅 `_boundControl` 的属性变化事件（如果有 `INotifyPropertyChanged`）

---

##### `Unbind` 方法

```csharp
public void Unbind();
```

**功能描述：** 解除绑定，清空属性面板。

**实现要点：**
- 取消订阅所有事件
- 清空 `TableLayoutPanel`
- 设置 `_boundControl = null`

---

##### 属性

```csharp
public Control? BoundControl { get; private set; }
```

| 属性 | 类型 | 说明 |
|------|------|------|
| `BoundControl` | `Control?` | 当前绑定的控件 |

---

##### 编辑器值同步

```csharp
private void OnEditorValueChanged(object? sender, EventArgs e);
```

**功能描述：** 编辑器值变化时更新控件属性。

**实现要点：**
- 通过 `_propertyItemMap` 字典找到对应的 `PropertyItem`
- 如果 `PropertyItem.Setter != null`，调用自定义 Setter
- 否则使用反射设置属性值：
  ```csharp
  var prop = _boundControl.GetType().GetProperty(item.PropertyName);
  prop?.SetValue(_boundControl, convertedValue);
  ```
- 类型转换处理：
  - `DockStyle`：`Enum.Parse(typeof(DockStyle), stringValue)`
  - `AnchorStyles`：从 CheckedListBox 的选中项组合标志位
  - `int`：`(int)numericUpDown.Value`
  - `double`：`(double)numericUpDown.Value`

---

##### 控件值刷新

```csharp
public void RefreshValues();
```

**功能描述：** 刷新所有属性编辑器的值（当控件属性被外部修改时调用）。

**实现要点：**
- 遍历所有属性项，从控件读取当前值并更新到编辑器
- 设置 `_isRefreshing = true` 标志，避免在刷新过程中触发 ValueChanged 导致循环

---

## 阶段三：持久化层

### 3.1 LayoutStateStore - 布局状态持久化

**命名空间：** `WdUI.UI.Toolkit.Persistence`

**文件路径：** `Toolkit/Persistence/LayoutStateStore.cs`

---

#### 3.1.1 `LayoutStateStore` 类

布局状态持久化管理器，负责快照的保存、加载、应用和设置管理。

```csharp
public class LayoutStateStore
{
}
```

##### 静态属性

```csharp
public static readonly string StorePath { get; }
```

**功能描述：** 存储根路径。

**实现要点：**
- 路径：`%LOCALAPPDATA%\WorkFlowPrj\Toolkit\`
- 在静态构造函数中初始化：
  ```csharp
  StorePath = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "WorkFlowPrj", "Toolkit");
  ```
- 确保目录存在：`Directory.CreateDirectory(StorePath)`

---

##### `SaveLayoutSnapshot` 方法

```csharp
public void SaveLayoutSnapshot(string name, Control container);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `name` | `string` | 快照名称 |
| `container` | `Control` | 容器控件（遍历其子控件保存状态） |

**功能描述：** 保存容器内所有控件的布局快照。

**实现要点：**
- 构建 `LayoutSnapshot` 对象：
  - `Name = name`
  - `CreatedAt = DateTime.Now`
  - `Controls = CaptureControlStates(container)`（递归遍历子控件）
- 序列化为 JSON 并写入文件：
  ```csharp
  var filePath = Path.Combine(StorePath, "layouts", $"{SanitizeFileName(name)}.json");
  var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
  File.WriteAllText(filePath, json);
  ```
- 可复用 `WinFormsContainerAdapter` 遍历控件树
- 可复用 `WdUIComposer.SaveToFile` 的序列化逻辑

---

##### `CaptureControlStates` 私有方法

```csharp
private List<ControlState> CaptureControlStates(Control parent);
```

**功能描述：** 递归捕获控件的状态。

**实现要点：**
- 遍历 `parent.Controls`
- 对每个控件创建 `ControlState`：
  - `Name = control.Name`
  - `TypeName = control.GetType().FullName`
  - `X = control.Left`, `Y = control.Top`
  - `Width = control.Width`, `Height = control.Height`
  - `Text = control.Text`
  - `Visible = control.Visible`, `Enabled = control.Enabled`
  - `Dock = control.Dock.ToString()`
  - `Anchor = control.Anchor.ToString()`
  - 读取 Layout 比例值（如果控件有 `LayoutPosition` 属性）
  - `Children = CaptureControlStates(control)`（递归）
- 排除工具箱自身的覆盖层控件（通过 Tag 标记）

---

##### `LoadLayoutSnapshot` 方法

```csharp
public LayoutSnapshot? LoadLayoutSnapshot(string name);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `name` | `string` | 快照名称 |

**返回值：** 反序列化后的 `LayoutSnapshot` 对象，如果文件不存在则返回 null。

**实现要点：**
- 构建文件路径：`Path.Combine(StorePath, "layouts", $"{SanitizeFileName(name)}.json")`
- 如果文件不存在，返回 null
- 读取 JSON 并反序列化

---

##### `ApplyLayoutSnapshot` 方法

```csharp
public void ApplyLayoutSnapshot(LayoutSnapshot snapshot, Control container);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `snapshot` | `LayoutSnapshot` | 要应用的快照 |
| `container` | `Control` | 目标容器 |

**功能描述：** 将快照应用到指定容器。

**实现要点：**
- 遍历 `snapshot.Controls`，对每个 `ControlState`：
  - 通过 `container.FindControlByName(state.Name)` 查找对应控件
  - 如果找到，应用状态：
    - `control.SetBounds(state.X, state.Y, state.Width, state.Height)`
    - `control.Text = state.Text`
    - `control.Visible = state.Visible`
    - `control.Enabled = state.Enabled`
    - 如果 `state.Dock != null`，设置 `control.Dock = Enum.Parse<DockStyle>(state.Dock)`
    - 如果 `state.Anchor != null`，设置 `control.Anchor = Enum.Parse<AnchorStyles>(state.Anchor)`
    - 如果 Layout 值不为 null，调用 `WdUILayout.ApplyProportional(control, state.LayoutLeft.Value, ...)`
  - 递归处理子控件
- 如果控件未找到，记录警告日志

---

##### `ListSnapshots` 方法

```csharp
public string[] ListSnapshots();
```

**功能描述：** 列出所有已保存的快照名称。

**返回值：** 快照名称数组。

**实现要点：**
- 获取 `layouts` 目录下所有 `*.json` 文件
- 去除 `.json` 扩展名并返回文件名列表

---

##### `DeleteSnapshot` 方法

```csharp
public void DeleteSnapshot(string name);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `name` | `string` | 要删除的快照名称 |

**功能描述：** 删除指定的快照文件。

**实现要点：**
- 构建文件路径并检查是否存在
- 如果存在则删除

---

##### `SaveSettings` 方法

```csharp
public void SaveSettings(ToolkitSettings settings);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `settings` | `ToolkitSettings` | 工具箱设置 |

**功能描述：** 保存工具箱设置。

**实现要点：**
- 序列化为 JSON 并写入 `settings.json`
- 自定义 `Color` 的序列化（序列化为 `"#AARRGGBB"` 格式）

---

##### `LoadSettings` 方法

```csharp
public ToolkitSettings LoadSettings();
```

**功能描述：** 加载工具箱设置。

**返回值：** `ToolkitSettings` 对象，如果文件不存在则返回默认设置。

**实现要点：**
- 读取 `settings.json` 文件
- 如果文件不存在，返回 `new ToolkitSettings()`
- 自定义 `Color` 的反序列化

---

##### 辅助方法

```csharp
private static string SanitizeFileName(string name);
```

**功能描述：** 清理文件名中的非法字符。

**实现要点：**
- 替换 `Path.GetInvalidFileNameChars()` 中的字符为 `_`

---

## 阶段四：入口与命令层

### 4.1 UIToolkit - 工具箱入口

**命名空间：** `WdUI.UI.Toolkit`

**文件路径：** `Toolkit/UIToolkit.cs`

---

#### 4.1.1 `UIToolkit` 类

工具箱入口类，负责安装/卸载右键菜单，管理所有子模块的生命周期。

```csharp
public class UIToolkit : IDisposable
{
}
```

##### 构造函数

```csharp
public UIToolkit();
```

**功能描述：** 创建工具箱实例。

**实现要点：**
- 创建所有子模块实例（延迟初始化）：
  - `ElementSelector`（内含 `SelectionBorder`）
  - `PropertyPanel`
  - `ResizeHandleOverlay`
  - `DragMoveAction`
  - `LayoutStateStore`
  - `AlignAction`
  - `ResizeAction`
- 设置 `_nativeWindow` 用于 WndProc 钩子

---

##### `Install` 方法

```csharp
public void Install(Form targetForm);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `targetForm` | `Form` | 要安装工具箱的目标窗体 |

**功能描述：** 在目标窗体上安装右键菜单。

**实现要点：**
- 保存 `_targetForm` 引用
- **使用 WndProc 钩子（子类化 `NativeWindow`）拦截 `WM_CONTEXTMENU` 消息**：
  - 创建 `NativeWindow` 子类，重写 `WndProc` 方法
  - 在 `WM_CONTEXTMENU`（`0x007B`）消息处理中弹出工具箱右键菜单
  - 调用 `targetForm.Handle` 分配窗口句柄
- 创建右键菜单 `ContextMenuStrip`：
  ```
  ├── 🔍 检查元素 (Inspect Element)
  ├── 📐 工具箱 (Toolkit) ▸
  │   ├── 元素选择器
  │   ├── 属性面板
  │   ├── 缩放手柄
  │   ├── 布局分析器
  │   └── 设置...
  ├── 📋 复制控件信息
  └── 📊 布局快照 ▸
      ├── 保存快照
      └── 加载快照...
  ```
- 设置 `IsInstalled = true`

---

##### `Uninstall` 方法

```csharp
public void Uninstall();
```

**功能描述：** 卸载右键菜单，释放子模块资源。

**实现要点：**
- 释放 `NativeWindow` 钩子
- 调用所有子模块的 `Dispose()` 或 `Hide()`
- 设置 `IsInstalled = false`

---

##### 属性

```csharp
public Control? SelectedControl { get; private set; }
public bool IsInstalled { get; private set; }
```

| 属性 | 类型 | 说明 |
|------|------|------|
| `SelectedControl` | `Control?` | 当前选中的控件 |
| `IsInstalled` | `bool` | 工具箱是否已安装 |

---

##### 事件

```csharp
public event Action<Control>? OnControlSelected;
```

| 事件 | 说明 |
|------|------|
| `OnControlSelected` | 选中控件时触发 |

**实现要点：**
- 内部订阅 `ElementSelector.OnControlSelected`
- 选中控件时联动显示 `PropertyPanel` 和 `ResizeHandleOverlay`
- 通过 `WdUIEvents` 事件总线广播 `OnControlSelected` 事件

---

##### 右键菜单命令处理

```csharp
private void OnInspectElementClick(object? sender, EventArgs e);
private void OnToggleElementSelectorClick(object? sender, EventArgs e);
private void OnTogglePropertyPanelClick(object? sender, EventArgs e);
private void OnToggleResizeHandleClick(object? sender, EventArgs e);
private void OnCopyControlInfoClick(object? sender, EventArgs e);
private void OnSaveSnapshotClick(object? sender, EventArgs e);
private void OnLoadSnapshotClick(object? sender, EventArgs e);
private void OnSettingsClick(object? sender, EventArgs e);
```

**`OnInspectElementClick` 实现要点：**
- 启用 `ElementSelector` 进入选择模式
- 用户点击控件后自动选中并显示属性面板和缩放手柄

**`OnToggleElementSelectorClick` 实现要点：**
- 切换 `ElementSelector` 的启用/禁用状态

**`OnTogglePropertyPanelClick` 实现要点：**
- 如果 `PropertyPanel` 未显示，将其 Dock 到 Form 右侧并显示
- 如果已显示，隐藏

**`OnToggleResizeHandleClick` 实现要点：**
- 切换 `ResizeHandleOverlay` 的显示/隐藏

**`OnCopyControlInfoClick` 实现要点：**
- 如果 `SelectedControl != null`，复制控件信息到剪贴板：
  ```
  控件名: btnSubmit
  类型: Button
  位置: (100, 50)
  尺寸: 200x30
  ```

**`OnSaveSnapshotClick` 实现要点：**
- 弹出输入对话框，输入快照名称
- 调用 `LayoutStateStore.SaveLayoutSnapshot(name, _targetForm)`

**`OnLoadSnapshotClick` 实现要点：**
- 弹出快照列表选择对话框
- 调用 `LayoutStateStore.ApplyLayoutSnapshot(snapshot, _targetForm)`

**`OnSettingsClick` 实现要点：**
- 弹出设置对话框，编辑 `ToolkitSettings`

---

##### `Dispose` 方法

```csharp
public void Dispose();
```

**功能描述：** 释放所有资源。

**实现要点：**
- 调用 `Uninstall()`
- 释放所有子模块

---

### 4.2 ToolkitCommands - 工具箱命令

**命名空间：** `WdUI.UI.Toolkit.Commands`

**文件路径：** `Toolkit/Commands/ToolkitCommands.cs`

---

#### 4.2.1 `ToolkitCommands` 类

工具箱命令注册器，将工具箱功能注册到 `ICommandExecutor`。

```csharp
public class ToolkitCommands
{
}
```

##### 构造函数

```csharp
public ToolkitCommands(UIToolkit toolkit);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `toolkit` | `UIToolkit` | 工具箱实例 |

**功能描述：** 创建工具箱命令注册器。

---

##### `RegisterCommands` 方法

```csharp
public void RegisterCommands(ICommandExecutor executor);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `executor` | `ICommandExecutor` | 命令执行器（与 `WinFormsCommandEngine` 共用） |

**功能描述：** 向命令执行器注册所有工具箱命令。

**实现要点：**
- 注册到 `WinFormsCommandEngine` 同一个 `ICommandExecutor`，命令共存互不干扰
- 在 `WdUIComposer.Initialize()` 中自动注册

---

##### 命令注册

```csharp
private void RegisterInspectCommand(ICommandExecutor executor);
private void RegisterSelectCommand(ICommandExecutor executor);
private void RegisterResizeCommand(ICommandExecutor executor);
private void RegisterMoveCommand(ICommandExecutor executor);
private void RegisterLayoutCommand(ICommandExecutor executor);
private void RegisterSnapshotCommands(ICommandExecutor executor);
```

**`RegisterInspectCommand` 实现要点：**
```csharp
executor.RegisterHandler("inspect", command =>
{
    // 进入检查模式
    _toolkit.ElementSelector.Enable();
    return true;
});
```

**`RegisterSelectCommand` 实现要点：**
```csharp
executor.RegisterHandler("select", command =>
{
    // select <name>
    var args = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (args.Length < 1) return false;
    var control = _controlFinder.FindControl(args[0]) as Control;
    if (control == null) return false;
    // 触发选中
    _toolkit.SelectControl(control);
    return true;
});
```

**`RegisterResizeCommand` 实现要点：**
```csharp
executor.RegisterHandler("resize", command =>
{
    // resize <name> <w> <h>
    var args = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (args.Length < 3) return false;
    var control = _controlFinder.FindControl(args[0]) as Control;
    if (control == null) return false;
    if (!int.TryParse(args[1], out var w) || !int.TryParse(args[2], out var h)) return false;
    control.SetBounds(control.Left, control.Top, w, h);
    return true;
});
```

**`RegisterMoveCommand` 实现要点：**
```csharp
executor.RegisterHandler("move", command =>
{
    // move <name> <x> <y>
    var args = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (args.Length < 3) return false;
    var control = _controlFinder.FindControl(args[0]) as Control;
    if (control == null) return false;
    if (!int.TryParse(args[1], out var x) || !int.TryParse(args[2], out var y)) return false;
    control.SetBounds(x, y, control.Width, control.Height);
    return true;
});
```

**`RegisterLayoutCommand` 实现要点：**
```csharp
executor.RegisterHandler("layout", command =>
{
    // layout <name> <left> <top> <w> <h>
    var args = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (args.Length < 5) return false;
    var control = _controlFinder.FindControl(args[0]) as Control;
    if (control == null) return false;
    if (!double.TryParse(args[1], out var l) || !double.TryParse(args[2], out var t) ||
        !double.TryParse(args[3], out var w) || !double.TryParse(args[4], out var h)) return false;
    WdUILayout.ApplyProportional(control, l, t, w, h);
    return true;
});
```

**`RegisterSnapshotCommands` 实现要点：**
```csharp
// snapshot save <name>
executor.RegisterHandler("snapshot", command =>
{
    var args = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (args.Length < 2) return false;
    var subCommand = args[0];
    var name = args.Length > 1 ? args[1] : "";
    switch (subCommand)
    {
        case "save":
            _toolkit.LayoutStateStore.SaveLayoutSnapshot(name, _toolkit.TargetForm);
            return true;
        case "load":
            var snapshot = _toolkit.LayoutStateStore.LoadLayoutSnapshot(name);
            if (snapshot == null) return false;
            _toolkit.LayoutStateStore.ApplyLayoutSnapshot(snapshot, _toolkit.TargetForm);
            return true;
        case "list":
            var snapshots = _toolkit.LayoutStateStore.ListSnapshots();
            Debug.WriteLine($"Snapshots: {string.Join(", ", snapshots)}");
            return true;
    }
    return false;
});
```

---

## 阶段五：集成

### 5.1 WdUIComposer 集成扩展

**文件路径：** `UI/WdUIComposer.cs`（扩展现有类）

---

#### 5.1.1 新增静态方法

```csharp
public static class WdUIComposer
{
    // 现有方法...

    /// <summary>
    /// 安装工具箱到指定窗体
    /// </summary>
    public static void InstallToolkit(Form targetForm);

    /// <summary>
    /// 卸载工具箱
    /// </summary>
    public static void UninstallToolkit();

    /// <summary>
    /// 获取工具箱实例
    /// </summary>
    public static UIToolkit? Toolkit { get; }
}
```

##### `InstallToolkit` 方法

```csharp
public static void InstallToolkit(Form targetForm);
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `targetForm` | `Form` | 目标窗体 |

**功能描述：** 创建并安装工具箱到指定窗体。

**实现要点：**
- 如果 `Toolkit` 已存在且已安装，先卸载
- 创建新的 `UIToolkit` 实例
- 调用 `toolkit.Install(targetForm)`
- 注册 `ToolkitCommands` 到命令执行器

---

##### `UninstallToolkit` 方法

```csharp
public static void UninstallToolkit();
```

**功能描述：** 卸载工具箱。

**实现要点：**
- 如果 `Toolkit != null`，调用 `Toolkit.Dispose()`
- 设置 `Toolkit = null`

---

##### `Toolkit` 属性

```csharp
public static UIToolkit? Toolkit { get; private set; }
```

**功能描述：** 获取当前工具箱实例。

---

#### 5.1.2 Initialize 扩展

在 `WdUIComposer.Initialize()` 中增加可选初始化逻辑：

```csharp
public static void Initialize()
{
    // 现有初始化逻辑...

    // 注册 ToolkitCommands（但不安装，由用户决定何时安装）
    var toolkit = new UIToolkit();
    var toolkitCommands = new ToolkitCommands(toolkit);
    toolkitCommands.RegisterCommands(CommandExecutor);
}
```

---

## 附录

### A. 事件定义

| 事件名称 | 触发时机 | 数据 |
|---------|---------|------|
| `toolkit:controlSelected` | 选中控件时 | `Control` 对象 |
| `toolkit:controlHovered` | 悬停控件时 | `Control` 对象 |
| `toolkit:modeChanged` | 工具箱模式切换时 | 模式名称字符串 |
| `toolkit:snapshotSaved` | 快照保存时 | 快照名称字符串 |
| `toolkit:snapshotLoaded` | 快照加载时 | 快照名称字符串 |

**广播方式：** 通过 `WdUIEvents` 事件总线广播。

---

### B. 异常处理策略

| 场景 | 处理方式 |
|------|---------|
| 控件查找失败 | 返回 null，不抛出异常 |
| 文件读写失败 | 捕获 `IOException`，记录 `Debug.WriteLine` 日志 |
| JSON 序列化/反序列化失败 | 捕获 `JsonException`，返回 null 或默认值 |
| 反射调用失败 | 捕获 `TargetException`/`TargetInvocationException`，记录日志 |
| 无效参数 | 方法内部校验，无效时直接返回（不抛出 `ArgumentException`） |
| UI 线程错误 | 所有 UI 操作必须在主线程执行，使用 `control.InvokeRequired` 检查 |

**核心原则：** 工具箱不应影响宿主应用的正常运行，所有异常必须被捕获并记录，不得向上传播。

---

### C. 资源释放规范

| 模块 | 释放内容 | 释放时机 |
|------|---------|---------|
| `UIToolkit` | 所有子模块 + NativeWindow 钩子 | `Dispose()` 时 |
| `ElementSelector` | SelectionBorder + 事件订阅 | `Dispose()` 时 |
| `SelectionBorder` | 覆盖层 Panel + 事件订阅 | `Dispose()` 时 |
| `ResizeHandleOverlay` | 覆盖层 Panel + ResizeOperation + 事件订阅 | `Dispose()` 时 |
| `PropertyPanel` | 编辑器控件 + 事件订阅 | `Dispose()` 时（继承 Panel 的 Dispose） |
| `LayoutStateStore` | 无（纯文件操作） | 无需释放 |

**释放顺序：** 先释放子模块，再释放自身资源。`UIToolkit.Dispose()` 中按依赖逆序释放：

1. `LayoutStateStore`（无依赖）
2. `ResizeOperation` / `DragMoveAction` / `AlignAction` / `ResizeAction`（无依赖）
3. `ResizeHandleOverlay`（依赖 ResizeOperation）
4. `ElementSelector`（依赖 SelectionBorder）
5. `PropertyPanel`
6. NativeWindow 钩子


