# ECS 渲染管线重构 — 废除转换层，Entity 作为能力卡片

> **日期：** 2026-07-22
> **状态：** 重构方案
> **背景：** `MainLayout.json` 渲染时因根 Entity Parent=Invalid 导致 `FindParent` 抛异常，暴露了架构层面的根本问题。

---

## 1. 问题根因分析

### 1.1 当前架构

```
JSON Config → [ConfigToEntityConverter] → Entity 树（镜像控件树结构）
                                            → [ControlCreateSystem] → WinForms 控件树
```

当前架构存在**两棵树**：Entity 树和 WinForms 控件树，结构必须保持一致。

### 1.2 核心矛盾

| 问题 | 表现 |
|------|------|
| **存在不必要的"转换"层** | `ConfigToEntityConverter` 将配置"转换"为 Entity 树，但配置本身就是 UI 描述，不需要中间表示 |
| **Entity 树镜像控件树** | Entity 通过 `ControlHierarchyComponent.Parent/Children` 表达层级，与 WinForms 控件树结构完全一致，造成两棵树同步问题 |
| **根 Entity 的特殊性未被处理** | 根 Entity 的 Parent = `Entity.Invalid`，但 `ControlCreateSystem` 阶段 2 仍试图为其建立父子关系，导致 `FindParent` 抛异常 |
| **职责重叠** | `ConfigToEntityConverter` 已同步创建 Control 并注册到 ControlPool，但 `ControlCreateSystem` 阶段 2 又遍历同一批 Entity 建立父子关系 |

### 1.3 异常链

```
1. ControlCreateSystem.Update() 阶段 2
   → FindParent(entity) 发现 hierarchy.Parent.IsValid == false
   → 抛出异常："Entity Entity(1:0) 的父 Entity 无效，无法查找父控件。"
   → ConfigLoaded 事件触发 (success=false)

2. MainForm.OnRootContainerLoaded 收到失败事件
   → 内部调用抛出 ArgumentNullException: Value cannot be null. (Parameter 'key')

3. Form.Shown 事件中脚本执行失败
   → 前置阶段 'MainLayoutLoaded' 等待失败
```

---

## 2. 新架构设计

### 2.1 核心思想

**WinForms 控件树是唯一的"结构树"，Entity 是扁平的"能力卡片"。**

配置直接创建控件，Entity 只是控件的附件——承载样式、布局、事件绑定等 ECS 能力数据，但 Entity 之间没有层级关系。

### 2.2 新旧对比

```
❌ 当前架构（两棵树）：
   JSON → [ConfigToEntityConverter] → Entity 树（镜像控件树结构）
                                       → [ControlCreateSystem] → WinForms 控件树
   Entity 树和控件树结构一致，需要同步 → 各种不一致问题

✅ 新架构（一棵树 + 扁平 Entity）：
   JSON → [ConfigDrivenControlFactory] → WinForms 控件树（唯一结构树）
                                          ├→ 为每个控件创建扁平 Entity（通过 ControlPool 关联）
                                          │   Entity 只持有：StyleComponent / LayoutComponent / EventBindingComponent
                                          │   Entity 不持有：ControlHierarchyComponent（已移除）
                                          └→ [EcsCapabilityApplier] 遍历 Entity，应用样式/布局/事件绑定
```

### 2.3 组件变更

| 当前 | 新方案 | 说明 |
|------|--------|------|
| `ConfigToEntityConverter` | **废除** → 替换为 `ConfigDrivenControlFactory` | 不再有"转换"步骤，配置直接驱动控件创建 |
| `ControlHierarchyComponent` | **移除** | Entity 不再表达层级关系 |
| `ContainerAncestorComponent` | **移除** | 不再需要 |
| `ControlCreateSystem` | **改为 `EcsCapabilityApplier`** | 不再创建控件，只应用 ECS 能力 |
| `ControlPool` | **保留并强化** | 作为唯一的 Entity↔Control 查找入口 |
| `EntityRegistry` | **保留** | 按名称查找 Entity |
| `StyleSystem` / `LayoutSystem` / `EventBindingSystem` | **保留** | 能力应用 System，逻辑不变 |

### 2.4 Entity 的创建规则

Entity 不再通过"转换"批量创建，而是在控件创建时**按需同步创建**：

```
ConfigDrivenControlFactory.CreateControlTree(config):
  ├→ 递归解析 JSON 配置
  ├→ 直接创建 WinForms 控件（Panel, SplitContainer, Button...）
  ├→ 直接建立父子关系（parent.Controls.Add(child)）
  ├→ 为每个控件创建轻量 Entity
  │     ├→ Entity 通过 ControlPool.Register(entity, name, control) 关联到 Control
  │     ├→ Entity 持有 StyleComponent（如果配置中有 style 引用）
  │     ├→ Entity 持有 LayoutComponent（如果配置中有 layout 定义）
  │     ├→ Entity 持有 EventBindingComponent（如果配置中有 events 定义）
  │     └→ Entity 不持有 ControlHierarchyComponent（无层级）
  └→ 返回根 Control（而非根 Entity）
```

### 2.5 SplitContainer 的处理

SplitContainer 的 Panel1/Panel2 子控件分配在控件创建时直接处理：

```
遇到 SplitContainer 配置：
  1. 创建 SplitContainer 控件
  2. 递归创建 Panel1Controls 下的子控件，添加到 splitContainer.Panel1.Controls
  3. 递归创建 Panel2Controls 下的子控件，添加到 splitContainer.Panel2.Controls
  4. 为 SplitContainer 创建 Entity（持有能力组件）
  5. 为 Panel1/Panel2 创建 Entity（持有 SplitterPanelComponent + 能力组件）
```

Panel1/Panel2 的 Entity 仍然存在（用于承载 SplitterPanelComponent），但不再通过 `ControlHierarchyComponent` 表达父子关系——父子关系由 WinForms 的 `Controls` 集合管理。

---

## 3. 实施步骤

### 步骤 1：新建 `ConfigDrivenControlFactory`

**位置：** `CsharpPrj/Operation/FrontEndDriven/ControlRender/Creation/`

**职责：** 将 JSON 配置直接转换为 WinForms 控件树，同步创建扁平 Entity。

**核心方法：**

```csharp
public class ConfigDrivenControlFactory
{
    // 输入：UIConfiguration → 输出：根 Control
    public Control BuildControlTree(UIConfiguration config)
    {
        // 1. 创建根容器控件（EcsConfigContainer 或 Panel）
        // 2. 递归创建子控件
        // 3. 为每个控件创建 Entity（通过 ControlPool 关联）
        // 4. 返回根 Control
    }

    private Control BuildControl(ControlConfig controlConfig, Control parentControl)
    {
        // 1. 根据 controlConfig.Type 创建对应 WinForms 控件
        // 2. parentControl.Controls.Add(新控件)
        // 3. 为控件创建 Entity（按需附加能力组件）
        // 4. 递归处理子控件
        // 5. 返回创建的控件
    }
}
```

### 步骤 2：移除层级组件

**删除：**
- `ControlHierarchyComponent` — Entity 不再需要层级关系
- `ContainerAncestorComponent` — 不再需要

**清理引用：**
- `ConfigToEntityConverter.cs` 中所有对 `ControlHierarchyComponent` 和 `ContainerAncestorComponent` 的引用
- `ControlCreateSystem.cs` 中所有对 `ControlHierarchyComponent` 的引用
- `EcsControlCreationStrategy.cs` 中 `FindParent` 方法（不再需要）
- 其他引用这些组件的文件

### 步骤 3：改造 `ControlCreateSystem` 为 `EcsCapabilityApplier`

**新职责：** 遍历所有有 Entity 的控件，应用 ECS 能力组件。

```csharp
public class EcsCapabilityApplier
{
    public void Apply()
    {
        // 遍历 ControlPool 中所有 Entity
        foreach (var (entity, control) in _controlPool.GetAll())
        {
            // 应用样式（StyleSystem 的逻辑移入此处或保持独立 System）
            // 应用布局（LayoutSystem 的逻辑移入此处或保持独立 System）
            // 绑定事件（EventBindingSystem 的逻辑移入此处或保持独立 System）
        }
    }
}
```

**或者保持独立 System 架构：** `EcsCapabilityApplier` 只是协调器，实际能力应用仍由 `StyleSystem`、`LayoutSystem`、`EventBindingSystem` 各自完成。这些 System 的查询条件从"遍历 Entity 树"改为"遍历 ControlPool 中所有 Entity"。

### 步骤 4：改造 `EcsRenderPipeline`

**管线执行顺序变更：**

```
当前：
  ConfigToEntitySystem → ControlCreateSystem → LayoutSystem → StyleSystem → EventBindingSystem → ...

新架构：
  ConfigDrivenControlFactory.BuildControlTree() → EcsCapabilityApplier.Apply()
  （或保持 System 架构：BuildControlTree() → LayoutSystem → StyleSystem → EventBindingSystem → ...）
```

**关键变化：**
- `ConfigToEntitySystem` 不再需要（被 `ConfigDrivenControlFactory` 替代）
- `ControlCreateSystem` 不再需要（控件在 `BuildControlTree` 中已创建）
- 管线入口从"执行 System"变为"调用 Factory + 执行 System"

### 步骤 5：改造 `MainForm` 和 `EcsConfigContainer`

**`EcsConfigContainer` 的创建：**
- 不再通过 Entity 树创建，而是由 `ConfigDrivenControlFactory` 直接创建
- `EcsConfigContainer` 本身就是一个 WinForms 控件（Panel/UserControl），不需要 Entity 来表示

**`MainForm.OnRootContainerLoaded`：**
- 不再接收 `Entity` 参数，改为接收 `Control` 参数
- 成功路径：`e.Control` 为根控件
- 失败路径：直接处理异常，不再有 Entity 相关的 null key 问题

### 步骤 6：清理废弃代码

**删除的文件/类：**
- `ConfigToEntityConverter.cs` — 整个文件
- `ConfigToEntitySystem.cs` — 整个文件
- `ControlHierarchyComponent.cs` — 整个文件
- `ContainerAncestorComponent.cs` — 整个文件
- `ControlCreateSystem.cs` — 改造为 `EcsCapabilityApplier`（或删除后新建）
- `EcsControlCreationStrategy.cs` — 不再需要（或大幅简化）

**清理的引用：**
- 所有对上述组件的 using 引用
- `ControlPool` 中与 Entity 树遍历相关的方法
- `EntityRegistry` 中与层级遍历相关的方法

---

## 4. 影响范围

### 4.1 直接修改的文件

| 项目 | 文件 | 改动 |
|------|------|------|
| FrontEndDriven | `ControlRender/Creation/ConfigDrivenControlFactory.cs` | **新建** |
| FrontEndDriven | `ControlRender/Creation/Systems/ControlCreateSystem.cs` | 改造为 `EcsCapabilityApplier` |
| FrontEndDriven | `RenderPipeline/EcsRenderPipeline.cs` | 管线流程改造 |
| FrontEndDriven | `Container/EcsConfigContainer.cs` | 创建方式改造 |
| BasicMethod | `ECS/Conversion/ConfigToEntityConverter.cs` | **删除** |
| BasicMethod | `ECS/Conversion/ConfigToEntitySystem.cs` | **删除** |
| BasicMethod | `ECS/Components/ControlHierarchyComponent.cs` | **删除** |
| BasicMethod | `ECS/Components/ContainerAncestorComponent.cs` | **删除** |
| FileDriven.UI | `Core/MainForm.cs` | `OnRootContainerLoaded` 参数类型变更 |

### 4.2 间接影响

| 组件 | 影响 |
|------|------|
| `ControlPool` | 不再需要 Entity 树遍历方法，但三向映射保留 |
| `EntityRegistry` | 不再需要层级遍历，按名称查找保留 |
| `StyleSystem` | 查询条件从 Entity 树遍历改为 ControlPool 遍历 |
| `LayoutSystem` | 同上 |
| `EventBindingSystem` | 同上 |
| `SplitContainerSystem` | 逻辑不变，但数据来源改为 ControlPool |
| `ControlSyncSystem` | 同上 |
| `ControlValueSyncSystem` | 同上 |
| `IControlCreationStrategy` | 不再需要（或大幅简化） |
| `EcsControlCreationStrategy` | 不再需要（或大幅简化） |

### 4.3 不受影响的组件

- `EcsWorld` / `EntityManager` — ECS 核心不变
- `ControlFactory` — 控件创建逻辑不变
- `ControlPool` 的三向映射 — 核心功能不变
- 所有能力组件（`StyleComponent` / `LayoutComponent` / `EventBindingComponent`）— 不变
- 所有能力 System 的内部逻辑 — 不变，只改查询方式

---

## 5. 收益

| 维度 | 收益 |
|------|------|
| **架构简化** | 消除"转换"层，配置直接驱动 UI 创建 |
| **消除两棵树同步问题** | 只有 WinForms 控件树，Entity 是扁平的 |
| **消除根 Entity 异常** | Entity 没有父概念，不存在 Parent=Invalid 的问题 |
| **职责清晰** | `ConfigDrivenControlFactory` 负责创建控件，`EcsCapabilityApplier` 负责应用能力 |
| **代码量减少** | 删除 4+ 个文件，简化多个 System |
| **调试难度降低** | 不再需要追踪两棵树的一致性 |

---

## 6. 风险与注意事项

1. **SplitContainer 的 Panel1/Panel2 子控件分配**：需要在 `ConfigDrivenControlFactory` 中正确处理，确保子控件添加到正确的 Panel 的 Controls 集合
2. **ControlPool 的遍历方式**：当前 `ControlPool` 可能没有 `GetAll()` 方法，需要新增
3. **事件绑定时机**：事件绑定需要在控件创建完成后进行，确保 `BuildControlTree` 返回后再执行 `EcsCapabilityApplier`
4. **向后兼容**：`IRenderPipeline` 接口可能需要调整，影响 `RenderPipelineOrchestrator` 等调用方
5. **Entity 的按需创建**：需要明确"什么情况下需要为控件创建 Entity"的规则，避免不必要的 Entity 创建
