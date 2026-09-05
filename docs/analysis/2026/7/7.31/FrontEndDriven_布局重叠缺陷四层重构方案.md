# FrontEndDriven 布局重叠缺陷四层重构方案

> **日期**：2026-07-31
> **状态**：待实施
> **架构决策**：**ECS 逻辑为主，WinForms 配置适配**（不采用 WinForms 回写 ECS 的反向数据流）

---

## 一、背景与目标

`layout overlaps` 报告 31 处重叠问题，根因分布在四层架构缺陷中。本文档给出**以框架逻辑为唯一真实来源、WinForms 仅做适配投影**的重构方案，使容器位置偏移正确累加、SplitterPanel 影实体尺寸与 WinForms 一致、拖拽后布局自动收敛、检测层父子树正确建立。

### 架构原则

1. **ECS 是唯一真实来源**：所有比例→像素换算、位置偏移累加、分割比例计算全部在 ECS 层确定性完成。
2. **WinForms 是配置适配投影**：`ApplySnapshot` 将 ECS 计算结果写入 WinForms 控件（`SetBounds` + `SetSplitterDistance`）。
3. **配置驱动**：`SplitterRatioComponent`/`ProportionalLayoutComponent`/容器尺寸/面板分割宽度均由配置初始化，运行时以 ECS 为准。
4. **用户拖拽是输入事件，非布局决策**：`SplitterMoved` 更新 ECS 比例 → 触发重算 → 管线 Apply 将 ECS 计算出的像素距离写回 WinForms。

---

## 二、缺陷确认（代码级证据）

### 缺陷 A：父子容器位置偏移与尺寸信息分离 ——「位置丢失」

| 证据 | 文件:行号 |
|------|----------|
| `ComputedLayoutComponent` 仅含 `Left/Top/Width/Height/ParentWidth/ParentHeight` | `CsharpPrj/Plate/TConfig/ECS/Components/ComputedLayoutComponent.cs` |
| `ContainerSizeComponent` 仅含 `Width/Height/IsValid`，无位置 | `CsharpPrj/Operation/FrontEndDriven/ControlRender/Components/ContainerSizeComponent.cs` |
| 子控件计算 `Left = (int)(propLayout.Left * parentWidth)` 无父偏移累加 | `LayoutSystem.Update` L129-137 |
| `TryGetParentContainerSize` 只输出 `out width, out height`，不返回父位置 | `LayoutSystem.TryGetParentContainerSize` L348-387 |

**影响**：父容器位于非原点时（如 `mainSplitter.Panel2` Left=904），子控件以 (0,0) 为基准绝对定位 → 报告大量不规则起点（`580≈0.484×1200`、`1124≈0.484×1200+232`）。

### 缺陷 B：SplitterPanel 是「影实体」—— ECS 算尺寸但 WinForms 永不创建

| 证据 | 文件:行号 |
|------|----------|
| `CollectSnapshot` 跳过 `SplitterPanelComponent` 实体 | `Ports.cs` L146-147 |
| `CollectAllInOnePass` 跳过 `SplitterPanelComponent` 实体 | `Ports.cs` L183-184 |
| `EcsCapabilityApplier.Update` 跳过 SplitterPanel，不创建 WinForms 控件 | `ControlCreateSystem.cs` L72-77 |
| `SyncActualContainerSizes` 对 SplitterPanel 实体 `_controlPool.GetControl(entity)` 返回 null → 永不回写 | `Ports.cs` L695-715 |
| `TryComputeSplitterPanelLayout` 按比例推算面板尺寸（`900=0.75×1200`） | `LayoutSystem.cs` L215-303 |
| `splitterWidth` 硬编码 = 4 | `LayoutSystem.cs` L248 |

**影响**：ECS 推算尺寸与 WinForms 实际 `SplitterDistance` 像素可能不一致 → 子控件父基准与真实挂载面板漂移 → 溢出/重叠。

### 缺陷 C：SplitterMoved 只更新 Component，不触发布局重算 —— 状态不同步

| 证据 | 文件:行号 |
|------|----------|
| `OnSplitterMoved` 仅 `AddOrSetComponent(SplitterRatioComponent)` | `Ports.cs` L635-649 |
| `RecalculateLayout` 未被 SplitterMoved 触发 | `RenderPipelineComposition.RecalculateLayout` L532-538 |

**影响**：拖拽后 WinForms 面板实际像素变化，ECS 仍用旧比例 → 下次 `CollectSnapshot` 用旧 ECS 值覆盖新分割。

### 缺陷 D：SplitterPanel 运行时重名 —— 检测父子索引错乱（放大误报）

| 证据 | 文件:行号 |
|------|----------|
| `ExtractNameFallback`：Name 为空 → 类型名 fallback | `ControlTreeWalker.cs` L100-104 |
| 多个 SplitterPanel 运行时 Name 为空 → 全部变为 `"SplitterPanel"` | 同上 |
| `BuildNameIndex`：重复名保留首个 → 祖先链断裂 | `LayoutTreeTraversal.cs` L112-122 |

**影响**：真实父子/祖孙被误判为非父子重叠（报告中的 `contentSplitter↔contentContainer`、`SplitterPanel↔其直系子控件`）。

### 缺陷 E：`IsContainerEntity` 判定依赖 `ControlTypeComponent.Type`

| 证据 | 文件:行号 |
|------|----------|
| `"splitcontainer"`、`"panel"`、`"container"` 已注册为容器 | `ControlTypeRegistry.cs` L179-206 |
| **`"splitterpanel"` 未注册**（唯一遗漏） | 同上 |
| `LayoutSystem.IsContainerEntity` 依赖 `FrontEndDrivenConstants.ContainerTypes` → SplitterPanel 返回 false | `LayoutSystem.cs` L183-189 |

**修正说明**：用户任务描述中称 `"container"` 也未注册，实际代码表明 `"container"` 已注册（L187-190）。真正的缺陷是 `"splitterpanel"` 未注册。`TryGetParentContainerSize` 虽有 `IsSplitterPanelEntity` 特判（L373），但语义不统一，应通过注册表统一。

---

## 三、架构决策

### 决策 1：尺寸唯一基准 —— **ECS 逻辑值为准，WinForms 适配**

已验证框架已有该机制：

- `CsgEntryPoint.CalculateSplitterDistance`（`Ports.cs` L433-442）从 `SplitterRatioComponent × ComputedLayoutComponent` 计算像素距离。
- `WinFormApplier` 将 `splitterDistance` 写入属性字典（`WinFormApplier.cs`）。
- `WinFormsControlAdapter` 应用 `sc.SplitterDistance = distance`（`WinFormsControlAdapter.cs`）。
- `PropertyApplier` 通用路径 `adapter.SetSplitterDistance(control, dist)`（`PropertyApplier.cs`）。

**因此无需反向回写 ECS**。只需保证：
1. ECS 计算正确（位置偏移累加、面板四元组完整）。
2. 用户拖拽后触发 ECS 重算 + 管线重新 Apply。

### 决策 2：阶段范围 —— 本次实施阶段 1+2+3，阶段 4 留作后续迭代

阶段 4（检测层语义增强：按容器分组 + 分类输出修复建议）不在本次范围内。

### 决策 3：Ports ↔ Composition 解耦 —— `WinFormsOutputPort` 注入回调

`WinFormsOutputPort` 新增 `SetSplitterChangedCallback(Action)` 方法，由 `RenderPipelineComposition.Create` 末尾注入。避免端口反向依赖 Composition，也避免构造期循环依赖。

---

## 四、实施步骤

### 阶段 1：修复位置偏移缺失（缺陷 A）

**目标**：让层级嵌套的子控件绝对定位正确 = 各级父容器偏移累加。

#### 1.1 扩展 `ContainerSizeComponent`（CsharpPrj/Operation/FrontEndDriven/ControlRender/Components/ContainerSizeComponent.cs）

```csharp
public struct ContainerSizeComponent : IComponent
{
    /// <summary>容器宽度（像素）</summary>
    public int Width;

    /// <summary>容器高度（像素）</summary>
    public int Height;

    /// <summary>父容器内的左边界坐标（相对坐标，语义同 Control.Left）</summary>
    public int Left;

    /// <summary>父容器内的上边界坐标（相对坐标，语义同 Control.Top）</summary>
    public int Top;

    /// <summary>尺寸是否有效（Width > 0 && Height > 0）</summary>
    public readonly bool IsValid => Width > 0 && Height > 0;
}
```

#### 1.2 修改 `LayoutSystem`（CsharpPrj/Operation/FrontEndDriven/ControlRender/Systems/LayoutSystem.cs）

**`TryGetParentContainerSize`** 改为输出四元组：

```csharp
private bool TryGetParentContainerSize(Entity entity,
    out int parentLeft, out int parentTop, out int width, out int height)
{
    // 通过 ParentEntityComponent 链向上查找
    var current = entity;
    int depth = 0;
    while (depth < MaxTraversalDepth)
    {
        if (!_entityManager.TryGetComponent<ParentEntityComponent>(current, out var parentComp))
            break;

        var parentEntity = parentComp.ParentEntity;
        if (!parentEntity.IsValid)
            break;

        // 检查父 Entity 是否有 ContainerSizeComponent
        if (_entityManager.TryGetComponent<ContainerSizeComponent>(parentEntity, out var containerSize)
            && containerSize.IsValid)
        {
            parentLeft = containerSize.Left;
            parentTop = containerSize.Top;
            width = containerSize.Width;
            height = containerSize.Height;
            return true;
        }

        // 父实体是容器或 SplitterPanel 但尚无尺寸 → 返回 false 等待下一遍迭代
        if (IsContainerEntity(parentEntity) || IsSplitterPanelEntity(parentEntity))
        {
            parentLeft = 0; parentTop = 0; width = 0; height = 0;
            return false;
        }

        current = parentEntity;
        depth++;
    }

    parentLeft = 0; parentTop = 0; width = 0; height = 0;
    return false;
}
```

**`Update` 普通控件计算** —— 累加父偏移：

```csharp
if (!TryGetParentContainerSize(entity, out var parentLeft, out var parentTop, out var parentWidth, out var parentHeight))
{
    continue;
}

var propLayout = _entityManager.GetComponent<ProportionalLayoutComponent>(entity);

ComputedLayoutComponent computed;
if (IsSplitterPanelEntity(entity))
{
    if (!TryComputeSplitterPanelLayout(entity, parentLeft, parentTop, parentWidth, parentHeight, out computed))
        continue;
}
else
{
    computed = new ComputedLayoutComponent
    {
        Left = parentLeft + (int)(propLayout.Left * parentWidth),
        Top  = parentTop  + (int)(propLayout.Top  * parentHeight),
        Width = (int)(propLayout.Width * parentWidth),
        Height = (int)(propLayout.Height * parentHeight),
        ParentWidth = parentWidth,
        ParentHeight = parentHeight
    };
}
```

**`AddOrSetContainerSize`** 回写位置：

```csharp
private void AddOrSetContainerSize(Entity entity, int left, int top, int width, int height)
{
    if (_entityManager.HasComponent<ContainerSizeComponent>(entity))
    {
        var current = _entityManager.GetComponent<ContainerSizeComponent>(entity);
        if (current.Left != left || current.Top != top || current.Width != width || current.Height != height)
        {
            _entityManager.SetComponent(entity, new ContainerSizeComponent
            {
                Left = left, Top = top, Width = width, Height = height
            });
        }
    }
    else
    {
        _entityManager.AddComponent(entity, new ContainerSizeComponent
        {
            Left = left, Top = top, Width = width, Height = height
        });
    }
}
```

**调用处更新**（`Update` L157-160）：

```csharp
if (IsContainerEntity(entity) || IsSplitterPanelEntity(entity))
{
    AddOrSetContainerSize(entity, computed.Left, computed.Top, computed.Width, computed.Height);
}
```

#### 1.3 `ConfigDrivenControlFactory.CreateRootEntity` 初始化位置

`CsharpPrj/Operation/FrontEndDriven/ControlRender/Creation/ConfigDrivenControlFactory.cs` L122-126：

```csharp
_entityManager.AddComponent(rootEntity, new ContainerSizeComponent
{
    Left = 0, Top = 0,
    Width = width > 0 ? width : 1200,
    Height = height > 0 ? height : 800
});
```

### 阶段 2：统一 SplitterPanel 影实体尺寸（缺陷 B）

**目标**：SplitterPanel 的 `ContainerSizeComponent` 携带完整四元组（含位置），子控件父基准正确；ECS 推算尺寸与 WinForms `SplitterDistance` 通过管线 Apply 收敛一致。

#### 2.1 `TryComputeSplitterPanelLayout` 签名扩展

`LayoutSystem.cs` L215-303：

```csharp
private bool TryComputeSplitterPanelLayout(
    Entity panelEntity,
    int parentLeft, int parentTop,
    int parentWidth, int parentHeight,
    out ComputedLayoutComponent computed)
{
    computed = default;

    if (!_entityManager.TryGetComponent<SplitterPanelComponent>(panelEntity, out var panel))
        return false;

    // 找到父 SplitContainer Entity
    if (!_entityManager.TryGetComponent<ParentEntityComponent>(panelEntity, out var parentComp)
        || !parentComp.ParentEntity.IsValid)
        return false;

    var splitContainerEntity = parentComp.ParentEntity;

    // 读取分割比例
    double r;
    if (_entityManager.TryGetComponent<SplitterRatioComponent>(splitContainerEntity, out var ratio)
        && ratio.Ratio > 0.0 && ratio.Ratio < 1.0)
    {
        r = ratio.Ratio;
    }
    else
    {
        r = 0.5;
    }

    bool isVertical = string.Equals(panel.Orientation, "Vertical", StringComparison.OrdinalIgnoreCase);
    bool isPanel1 = string.Equals(panel.PanelSide, "Panel1", StringComparison.OrdinalIgnoreCase);

    // 分割条宽度：优先从面板组件读取（配置可覆盖），默认 4
    int splitterWidth = panel.SplitterWidth > 0 ? panel.SplitterWidth : 4;

    if (isVertical)
    {
        int panel1Width = (int)(parentWidth * r);
        int panel2Width = parentWidth - panel1Width - splitterWidth;

        if (isPanel1)
        {
            computed = new ComputedLayoutComponent
            {
                Left = parentLeft, Top = parentTop,
                Width = panel1Width, Height = parentHeight,
                ParentWidth = parentWidth, ParentHeight = parentHeight
            };
        }
        else
        {
            computed = new ComputedLayoutComponent
            {
                Left = parentLeft + panel1Width + splitterWidth, Top = parentTop,
                Width = panel2Width, Height = parentHeight,
                ParentWidth = parentWidth, ParentHeight = parentHeight
            };
        }
    }
    else
    {
        int panel1Height = (int)(parentHeight * r);
        int panel2Height = parentHeight - panel1Height - splitterWidth;

        if (isPanel1)
        {
            computed = new ComputedLayoutComponent
            {
                Left = parentLeft, Top = parentTop,
                Width = parentWidth, Height = panel1Height,
                ParentWidth = parentWidth, ParentHeight = parentHeight
            };
        }
        else
        {
            computed = new ComputedLayoutComponent
            {
                Left = parentLeft, Top = parentTop + panel1Height + splitterWidth,
                Width = parentWidth, Height = panel2Height,
                ParentWidth = parentWidth, ParentHeight = parentHeight
            };
        }
    }

    // 尺寸有效保护
    return computed.Width > 0 && computed.Height > 0;
}
```

> **说明**：新增 `parentLeft/parentTop` 入参，使 SplitterPanel 的 `ComputedLayoutComponent.Left/Top` 反映**真实父坐标系**位置（`Panel2` 在 `mainSplitter` 中 = `parentLeft + 900 + 4`）。这同时解决了缺陷 B 中「子控件父基准错误」的问题——SplitterPanel 子控件会以待 `904+比例` 而非 `0+比例` 计算。

#### 2.2 扩展 `SplitterPanelComponent` 支持 SplitterWidth

`CsharpPrj/BasicMethod/ECS/Components/SplitterPanelComponent.cs`：

```csharp
public struct SplitterPanelComponent : IComponent
{
    /// <summary>Panel 侧："Panel1" 或 "Panel2"</summary>
    public string PanelSide;

    /// <summary>父 SplitContainer 方向："Vertical" 或 "Horizontal"</summary>
    public string Orientation;

    /// <summary>分割条宽度（像素）。0 表示使用默认值 4。</summary>
    public int SplitterWidth;
}
```

> 默认 0 → `LayoutSystem` 回退到 4。配置可显式指定精确值。

#### 2.3 不修改 `SyncActualContainerSizes`

**关键架构决策**：ECS 为唯一真相，`ContainerSizeComponent` 由 `LayoutSystem` 确定性计算并回写。SplitterPanel 的 WinForms 实际尺寸由 `SplitContainer.SplitterDistance` 在管线 Apply 时适配，**不从 WinForms 回读**。因此 `SyncActualContainerSizes` 保持现有逻辑（仅对**有真实 WinForms 控件的容器**生效，恰为 root + 普通容器），无需为 SplitterPanel 扩展。

### 阶段 3：SplitterMoved 状态同步（缺陷 C）

**目标**：用户拖动分割条后，更新 ECS 比例 → 触发 ECS 重算 → 管线重新 Apply → WinForms 收敛到新比例计算的位置（无漂移）。

#### 3.1 `WinFormsOutputPort` 新增回调注入

`CsharpPrj/Operation/FrontEndDriven/Ports.cs`：

```csharp
/// <summary>SplitterMoved 后的布局重算回调（由 Composition 注入，避免端口反向依赖）</summary>
private Action? _onSplitterChanged;

/// <summary>
/// 设置 SplitterMoved 变更回调
/// <para>由 RenderPipelineComposition 在组合完成后注入，</para>
/// <para>避免端口反向依赖 Composition。</para>
/// </summary>
public void SetSplitterChangedCallback(Action callback)
{
    _onSplitterChanged = callback ?? throw new ArgumentNullException(nameof(callback));
}
```

#### 3.2 `OnSplitterMoved` 触发回调

`Ports.cs` L635-649：

```csharp
private void OnSplitterMoved(object? sender, EventArgs e, Entity entity)
{
    if (sender == null || !entity.IsValid) return;

    var orientation = _adapter.GetOrientation(sender);
    int dimension = orientation == Orientation.Vertical
        ? _adapter.GetWidth(sender)
        : _adapter.GetHeight(sender);

    if (dimension <= 0) return;

    double ratio = (double)_adapter.GetSplitterDistance(sender) / dimension;

    _entityManager.AddOrSetComponent(entity, new SplitterRatioComponent { Ratio = ratio });

    // ★ 2026-07-31 修复：触发布局重算 + WinForms 适配（ECS 逻辑为主，WinForms 仅投影）
    _onSplitterChanged?.Invoke();
}
```

#### 3.3 `RenderPipelineComposition` 注入回调 + 补全重算后 Apply

`CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderPipelineComposition.cs`：

**Create 静态工厂末尾**（L272-274 之间）：

```csharp
// ★ 2026-07-31 修复：SplitterMoved → 布局重算回调注入
composition.OutputPort.SetSplitterChangedCallback(() => composition.RecalculateLayout());

return composition;
```

**RecalculateLayout 补全**（L532-538）——当前仅重算 ECS，缺 CSG 收集 + Apply：

```csharp
public void RecalculateLayout()
{
    if (_disposed) return;

    // 1. ECS 重算（LayoutSystem.RecalculateAll 内部已调用 Update()）
    _layoutSystem.RecalculateAll();
    _splitContainerSystem.RecalculateAll();

    // 2. CSG 收集 + WinForms 适配（将新比例计算的位置应用到 WinForms）
    ExecuteRenderStage();
}
```

> **注意**：`ExecuteRenderStage`（L507-525）内部会执行 `CollectSnapshot()` + `OutputPort.ApplySnapshot()` + `_eventBindingSystem.Update()`。这里会触发 `SyncActualContainerSizes`？——不会，`SyncActualContainerSizes` 仅在 `_isFirstApplySnapshot` 时执行一次（Ports.cs L547-550），后续 Apply 不重复回写，符合「ECS 为主」。

#### 3.4 `SplitContainerSystem.RecalculateAll` 确认

`SplitContainerSystem.cs` L108-125 已正确为 SplitContainer 添加 `PendingLayoutComponent`。`LayoutSystem.RecalculateAll`（L397-424）会清空中间容器 `ContainerSizeComponent` 并用新窗口/新比例尺寸重算回写。**无需修改**。

### 阶段 4：消除 SplitterPanel 重名 + 补注册容器类型（缺陷 D + E）

**目标**：检测/查找链路正确建立父子树；`IsContainerEntity` 语义统一。

#### 4.1 （缺陷 E）`ControlTypeRegistry` 补注册 `"splitterpanel"`

`CsharpPrj/Operation/FrontEndDriven/Shared/ControlTypeRegistry.cs` RegisterAll() 末尾：

```csharp
// ================================================================
// SplitterPanel 影实体（特殊：EcsCapabilityApplier 跳过创建，
// 由 SplitContainer 自身管理；注册仅为容器类型识别）
// ================================================================
Register("splitterpanel", () => new Panel(),
    () => ContractBuilder.ContainerControl().Build(),
    isContainer: true);
```

**效果**：
- `FrontEndDrivenConstants.ContainerTypes` 自动包含 `"splitterpanel"`。
- `LayoutSystem.IsContainerEntity` 对 SplitterPanel 返回 true，与 `IsSplitterPanelEntity` 语义统一。
- `ConfigDrivenControlFactory.L91` 的私有 `ContainerTypes` 自动同步。

> 注意：注册的工厂 `() => new Panel()` 实际**不会被调用**——`EcsCapabilityApplier.Update`（ControlCreateSystem.cs L72-77）对 SplitterPanel 实体跳过创建。注册仅为类型注册表的完整性与容器识别。

#### 4.2 （缺陷 D）SplitContainer 创建时设置 Panel1/Panel2 唯一 Name

**目标**：WinForms 侧 SplitContainer 自动创建的 Panel1/Panel2 运行时 `Name` 为空 → 检测层 fallback 为 `"SplitterPanel"` 导致重名。需与 ECS 实体命名规则一致（`ConfigDrivenControlFactory.CreateSplitterPanelEntity` L259-268：`$"{control.Name}_Panel1"` / `$"{control.Name}_Panel2"`）。

**注入点定位**：SplitContainer 控件创建后、属性应用前。最合适的注入点在 **`WinFormApplier` 创建/应用 SplitContainer 时**（该文件已有 `SplitterDistance` 写入逻辑），或 **`WinFormsControlAdapter.SetPropertyValue` 处理 SplitContainer 的路径**。

**实施位置**（需按实际文件确认后落点）：
- 在 SplitContainer 控件创建后：
  ```csharp
  if (control is SplitContainer sc)
  {
      sc.Panel1.Name = $"{control.Name}_Panel1";
      sc.Panel2.Name = $"{control.Name}_Panel2";
  }
  ```
- `control.Name` 为 ECS 实体的 `ControlInfoComponent.Name`（已在创建时设置，见 `WinFormsOutputPort.CreateControl` L563-565 `controlName`）。

**补充**：若某些场景下 `Name` 仍为空（动态创建的 SplitContainer），`ControlTreeWalker.ExtractNameFallback` 的 fallback 仍会生效。为彻底消除重名，可将 fallback 强化为**带父链的唯一名**（如 `$"{parentName}.{typeName}"`），但**本次不实施**——阶段 4 留作后续迭代。本次仅保证正常配置路径下 Panel.Name 唯一。

#### 4.3 `SplitterPanelHelper` 确认

`SplitterPanelHelper.ResolveSplitterParent`（L26-47）已正确处理 SplitterPanel 回溯，**无需修改**。

---

## 五、验证方式（框架行为检查，非单元测试）

> 用户明确要求不考虑测试问题。以下为**框架行为检查清单**，用于实施后人工核验：

| # | 检查点 | 预期行为 |
|---|--------|----------|
| 1 | 嵌套容器位置累加 | Panel-in-Panel 子控件 `Left/Top` = 各级父偏移累加（`ContainerSizeComponent.Left/Top` 正确传递） |
| 2 | SplitterPanel 完整四元组 | Panel2 子控件基准 = `父偏移 + panel1Width + splitterWidth`（如 904 而非 0） |
| 3 | SplitterPanel 容器识别 | `LayoutSystem.IsContainerEntity` 对 `"splitterpanel"` 返回 true（注册后自动） |
| 4 | SplitterMoved → 收敛 | 拖拽后 ECS 比例更新 → RecalculateLayout → Apply → WinForms `SplitterDistance` = 新比例 × 父尺寸 |
| 5 | Panel 唯一 Name | WinForms SplitContainer 的 Panel1/Panel2 `Name` = `{parent}_Panel1/2`，检测层树索引无冲突 |
| 6 | `layout overlaps` 复检 | 父子误报消零；真实跨区重叠数收敛；`contentContainer` 恢复 540 宽；`terminalPanel` 收敛到右侧面板内 |

---

## 六、工作量评估

| 阶段 | 主要修改文件 | 预估工作量 |
|------|-------------|-----------|
| 阶段 1 | `ContainerSizeComponent`、`LayoutSystem`、`ConfigDrivenControlFactory` | 0.5 天 |
| 阶段 2 | `LayoutSystem`、`SplitterPanelComponent` | 0.5 天 |
| 阶段 3 | `WinFormsOutputPort`（Ports.cs）、`RenderPipelineComposition` | 0.5 天 |
| 阶段 4 | `ControlTypeRegistry`、`WinFormApplier`（或 SplitContainer 创建路径） | 0.5-1 天 |
| 核验 | 编译 + layout overlaps 复检 | 0.5 天 |

---

## 七、风险与缓解

| 风险 | 说明 | 缓解 |
|------|------|------|
| `RecalculateLayout` 触发 `ExecuteRenderStage` 可能重新 Apply 全部控件 | 性能开销（当前全量重渲染） | 本次接受全量重渲染；后续迭代可增量优化（MarkDirty 已有预留位） |
| `SyncActualContainerSizes` 与 ECS 重算的时序 | 首次 Apply 后回写真实尺寸，后续 ECS 重算可能覆盖 | 首次回写仅用于 root 容器；中间容器由 LayoutSystem 按新窗口/新比例重算，行为一致 |
| SplitterPanel 控件名设置位置需确认 | `WinFormApplier` 具体创建路径未在本次勘察中完整确认 | 实施阶段 4 前先读 `WinFormApplier.cs` + `WinFormsControlAdapter.cs` 定位注入点 |
| `SplitterWidth` 配置默认值 | 未配置时回退 4（WinForms Vertical 默认 4） | 配置可显式指定；Horizontal 默认 SplitterWidth 同为 4（像素宽度） |

---

## 八、附录：修改文件清单

| 文件 | 阶段 | 修改内容 |
|------|------|----------|
| `CsharpPrj/Operation/FrontEndDriven/ControlRender/Components/ContainerSizeComponent.cs` | 1 | 增加 `Left`/`Top` 字段 |
| `CsharpPrj/Operation/FrontEndDriven/ControlRender/Systems/LayoutSystem.cs` | 1+2 | 位置累加、四元组输出、`TryComputeSplitterPanelLayout` 支持位置 |
| `CsharpPrj/Operation/FrontEndDriven/ControlRender/Creation/ConfigDrivenControlFactory.cs` | 1 | Root 容器初始化 `Left=0, Top=0` |
| `CsharpPrj/BasicMethod/ECS/Components/SplitterPanelComponent.cs` | 2 | 增加 `SplitterWidth` 字段 |
| `CsharpPrj/Operation/FrontEndDriven/Ports.cs` | 3 | 新增 `SetSplitterChangedCallback`，`OnSplitterMoved` 触发回调 |
| `CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderPipelineComposition.cs` | 3 | 注入回调；`RecalculateLayout` 补 `ExecuteRenderStage()` |
| `CsharpPrj/Operation/FrontEndDriven/Shared/ControlTypeRegistry.cs` | 4 | 注册 `"splitterpanel"` 为容器 |
| `CsharpPrj/Operation/FrontEndDriven/WinForms/Applier/WinFormApplier.cs`（或 `WinFormsControlAdapter`） | 4 | SplitContainer 创建后设置 Panel1/Panel2 唯一 Name |