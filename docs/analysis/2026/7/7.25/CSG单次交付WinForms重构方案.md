# CSG → WinForms 一次性交付重构方案

> 2026-07-25 框架级重构

## 一、重构背景

### 1.1 问题现状

在原有架构中，CSG（Canonical Scene Graph）管线的最终输出是逐步、分散地写入 WinForms 控件的，存在多个写入点：

| 写入点 | 位置 | 操作 |
|--------|------|------|
| `WinFormsOutputPort.CreateControl()` | 控件创建阶段 | 逐个创建控件实例 |
| `WinFormsOutputPort.AddToParent()` | 控件创建阶段 | 逐个建立父子关系 |
| `WinFormsUIAdapter.ApplyPropertyChanges()` | CSG Apply 阶段 | 逐个属性调用 |
| `WinFormsOutputPort.SyncControlInfoComponent()` | CSG Apply 阶段 | 读后写回 |

这些分散的写入点导致了两个核心问题：

**问题 A：Entity 遗漏（gap）**

管线输出 `collected=34 / applied=28`，6 个 Entity 找不到对应控件：
- `Entity(1:0)` —— MainLayout 根容器，因 `ContainerView` 未设置 `Name` 属性导致注册时跳过
- `Entity(24:0)` —— Content 区域根容器，同上
- `Entity(13:0/15:0/22:0)` —— SplitterPanel 中间 Entity，无对应 WinForms 控件但被 CSG 收集

**问题 B：UI 渲染空白**

因为问题 A 中根容器 Entity 未被应用，整个控件树不可见 → UI 全白。

### 1.2 根本原因

原有架构的根因是：**CSG 管线的输出产物（CanonicalForm）到最终 WinForms 控件树之间，没有明确的、原子的数据交付点。** 管线在多个阶段、多个类中分别操作控件，导致：
1. 任何阶段出问题都会导致 UI 不完整（gap）
2. 无法通过单一日志点追踪"什么内容被写入了 WinForms"
3. 差异比较（UIReconciler）需要维护内部快照，增加了复杂度

---

## 二、重构目标

1. **单一数据通道**：CSG 管线产出唯一 POCO 给 WinForms 层
2. **单一写入入口**：所有对 WinForms 控件的写操作集中在一个类的 `Apply()` 方法中
3. **完整日志记录**：`Apply()` 方法第一行打印完整的交付数据
4. **Entity 创建路径统一**：所有 Entity（包括根容器）走相同的管线标记路径

---

## 三、新增类型

### 3.1 ScopeComponent

**文件**: `CsharpPrj/Operation/FrontEndDriven/ControlRender/Components/ScopeComponent.cs`

```csharp
public struct ScopeComponent : IComponent
{
    public string Scope { get; set; }
}
```

用途：标记每个 Entity 属于哪个 UI 作用域（"MainLayout" / "Content" / "Side"）。

设计决策：使用 `string` 而非 `enum` 类型，避免跨项目依赖（FrontEndDriven → FileDriven.Execution.Models）。

### 3.2 WinFormSnapshot（POCO）

**文件**: `CsharpPrj/Operation/FrontEndDriven/WinForms/Applier/WinFormSnapshot.cs`

CSG 管线的最终输出产物，WinFormApplier 的唯一输入。纯数据类，无业务逻辑，可 JSON 序列化。

```csharp
public sealed class WinFormSnapshot
{
    public List<ControlDescriptor> Controls { get; set; } = [];
    public List<ChildRelation> Relations { get; set; } = [];
}

public sealed class ControlDescriptor
{
    public string Id { get; set; } = "";         // 对应 Entity.ToString()
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Text { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool Visible { get; set; } = true;
    public string? FontFamily { get; set; }
    public float FontSize { get; set; }
    public bool FontBold { get; set; }
    public bool FontItalic { get; set; }
    public bool FontUnderline { get; set; }
    public string? ForeColor { get; set; }
    public string? BackColor { get; set; }
    public int? SplitterDistance { get; set; }
}

public sealed class ChildRelation
{
    public string ParentId { get; set; } = "";
    public string ChildId { get; set; } = "";
    public string? TargetPanel { get; set; }
}
```

### 3.3 WinFormApplier

**文件**: `CsharpPrj/Operation/FrontEndDriven/WinForms/Applier/WinFormApplier.cs`

**系统中对 WinForms 控件树执行写操作的唯一入口。** `Apply()` 方法第一行即打印完整 snapshot 日志。

```csharp
public sealed class WinFormApplier
{
    private readonly ControlPool _controlPool;
    private readonly IWinFormsControlAdapter _adapter;

    public void Apply(WinFormSnapshot snapshot)
    {
        // ★ 唯一日志入口：打印完整 snapshot
        FrontEndDrivenLog.Log("WinFormApplier_Apply_start", /* 完整数据 */);

        // 阶段 1：创建或更新控件
        ApplyControls(snapshot.Controls);
        // 阶段 2：建立父子关系
        ApplyRelations(snapshot.Relations);
    }
}
```

---

## 四、修改的文件

### 4.1 ConfigDrivenControlFactory（`.../ECS.Creation/ConfigDrivenControlFactory.cs`）

**变更**：

| 修改点 | 说明 |
|--------|------|
| `Convert()` 新增 `string scope` 参数 | 从调用侧传递 scope 标识 |
| `CreateRootEntity()` 调用 `AddPendingComponents()` | 使根 Entity 与子 Entity 走相同的管线路径 |
| `CreateRootEntity()` 移除 `PendingCapabilityComponent` | 根容器控件已由 `RegisterRootContainer` 注册，不重复创建 |
| `AddPendingComponents()` 包含 `ScopeComponent` | 统一为每个 Entity 附加 scope 标识 |

**关键设计**：`CreateRootEntity` 不再特殊处理，根 Entity 与 `ConvertControl` 创建的每个子 Entity 具有相同的 Pending 标记，确保所有 Entity 走完整的管线路径（LayoutSystem → ComputedLayoutComponent → 有效布局）。

### 4.2 EcsRenderPipeline（`.../RenderPipeline/EcsRenderPipeline.cs`）

**变更**：

| 修改点 | 说明 |
|--------|------|
| `InitializeCore()` 中 ControlPool 传入 EntityManager | 订阅 `EntityDestroying` 事件，自动清理映射 |
| `LoadConfiguration()` 新增 `string scope` 参数 | 透传给 ConfigDrivenControlFactory |
| `RegisterRootContainer()` 增加 null name fallback | 当 `rootContainer.Name` 为空时使用 `rootEntity.ToString()` |
| `ExecuteRenderStage()` 改用 CollectSnapshot + ApplySnapshot | 替换原来的 Collect + Apply 路径 |
| 新增 `DestroyScopeEntities(string)` | 按 ScopeComponent 筛选并销毁 Entity |

### 4.3 Ports.cs（CsgEntryPoint + WinFormsOutputPort）

**CsgEntryPoint**：

| 方法 | 说明 |
|------|------|
| `CollectSnapshot()` | 新增。与 `Collect()` 相同的遍历逻辑，但产出 `WinFormSnapshot` 而非 `CanonicalForm` |
| `CollectAllInOnePass()` | 新增 `SplitterPanelComponent` 跳过逻辑，消除 gap |

**WinFormsOutputPort**：

| 方法 | 说明 |
|------|------|
| 构造函数中创建 `WinFormApplier` 实例 | |
| `ApplySnapshot(WinFormSnapshot)` | 新增。委托给 WinFormApplier 执行实际写入 |

### 4.4 FileDrivenApplicationService（`.../Services/FileDrivenApplicationService.cs`）

| 修改点 | 说明 |
|--------|------|
| `LoadUIConfigAsync()` 加载前调用 `DestroyScopeEntities()` | 确保旧 scope 的 Entity 被清理 |
| 传递 `scope.ToString()` 给 `DestroyScopeEntities()` | 字符串参数避免跨项目依赖 |

### 4.5 fde_log_messages.json

新增日志 key：

| Key | 级别 |
|-----|------|
| `CsgEntryPoint_CollectSnapshot_start` | Debug |
| `CsgEntryPoint_CollectSnapshot_complete` | Debug |
| `WinFormsOutputPort_ApplySnapshot_start` | Info |
| `WinFormApplier_Apply_start` | Info |
| `WinFormApplier_Apply_skipped_empty` | Debug |

### 4.6 WinFormsUIAdapter（`.../WinForms/Adapter/WinFormsUIAdapter.cs`）

已在之前修改中将 `ClearEntityIdCache()` 从 `internal` 改为 `public`。

---

## 五、架构变迁

### 5.1 旧架构

```
配置文件 (JSON)
  → ConfigDrivenControlFactory
    → ECS World (Entity + Component)
      → EcsCapabilityApplier.Update()
        → WinFormsOutputPort.CreateControl()        ← 写入点 1
        → WinFormsOutputPort.AddToParent()           ← 写入点 2
      → LayoutSystem/StyleSystem/EventSystem
      → CsgEntryPoint.Collect()
        → CanonicalForm
          → WinFormsOutputPort.Apply()
            → UIReconciler
              → WinFormsUIAdapter.ApplyCanonical()
                → ApplyPropertyChanges()
                  → _adapter.SetBounds()             ← 写入点 3..N
                  → _adapter.SetText()
                  → _adapter.SetFont()
                  → ...
```

### 5.2 新架构

```
配置文件 (JSON)
  → ConfigDrivenControlFactory
    → ECS World (Entity + Component, 含 ScopeComponent)
      → EcsCapabilityApplier.Update()               ← 控件创建
      → LayoutSystem/StyleSystem/EventSystem
      → CsgEntryPoint.CollectSnapshot()
        → WinFormSnapshot (单一 POCO)
          → WinFormsOutputPort.ApplySnapshot()
            → WinFormApplier.Apply(snapshot)         ← ★ 唯一写入入口
              → [日志: 完整 snapshot]
              → 创建/更新控件属性
              → 建立父子关系
```

### 5.3 效果对比

| 指标 | 旧架构 | 新架构 |
|------|--------|--------|
| WinForms 写入入口数 | 3 类（10+ 方法） | **1 个**（`WinFormApplier.Apply()`） |
| 数据交付形态 | 逐个属性散打 | 完整 POCO 一次性交付 |
| 日志追踪 | 每属性一条日志，难以串读 | **1 条日志**，展示完整快照数据 |
| 根容器处理 | 特殊分支，不参与管线 | 统一走 `AddPendingComponents` 流程 |
| gap 检测 | `collected=34, applied=28` | `collected=30, applied=30` |

---

## 六、修复的问题清单

### 6.1 UI 渲染空白

**根因**：`_rootView = new ContainerView { Dock = DockStyle.Fill }` 没有设置 `Name` 属性，`RegisterRootContainer` 调用 `_controlPool.Register(rootEntity, null, rootContainer)` 被 `ControlPool` 的空名称保护逻辑跳过。

**修复**：`EcsRenderPipeline.RegisterRootContainer()` 中当 `rootContainer.Name` 为空时，使用 `rootEntity.ToString()` 作为 fallback 名称。

### 6.2 根容器 ComputedLayoutComponent 缺失

**根因**：`CreateRootEntity()` 创建的根 Entity 没有 `PendingLayoutComponent`，`LayoutSystem` 不会处理它，因此 `ComputedLayoutComponent` 不存在。`CollectSnapshot()` 读取时返回默认值 (0,0,0,0)，`WinFormApplier.ApplyProperties()` 将其写入控件，覆盖了 `Dock = Fill` 布局。

**修复**：`CreateRootEntity()` 调用 `AddPendingComponents()`，使根 Entity 与子 Entity 走相同的管线路径。

### 6.3 Scope 混合

**根因**：两次 `LoadUIConfigAsync()`（MainLayout + Content）在同一 EntityManager 上追加 Entity，新旧 Entity 混合，CSG Collect 收集了所有 Entity。

**修复**：新增 `ScopeComponent` 标记每个 Entity 的作用域。`LoadUIConfigAsync()` 加载前先调用 `DestroyScopeEntities()` 清除旧 scope 的 Entity。

### 6.4 SplitterPanel 类型被收集

**根因**：`EcsCapabilityApplier` 跳过 `SplitterPanelComponent` Entity（不创建控件），但 `CsgEntryPoint.Collect()` 收集了所有有 `ControlTypeComponent` 的 Entity。

**修复**：`CollectAllInOnePass()` 中新增 `HasComponent<SplitterPanelComponent>` 检查。

### 6.5 ControlPool 未订阅 EntityDestroying 事件

**根因**：`ControlPool` 创建时未传入 `EntityManager`，Entity 销毁后 ControlPool 中的映射未被清理。

**修复**：`EcsRenderPipeline.InitializeCore()` 中使用带 EntityManager 的 ControlPool 构造函数。

---

## 七、设计原则

1. **Entity 创建路径一致性**：所有 Entity（根容器、子控件、SplitterPanel）通过 `AddPendingComponents` 附加相同的标记组件，走相同的管线路径。差异通过移除不必要的标记实现（如根容器移除 `PendingCapabilityComponent`）。

2. **单一交付点**：`WinFormSnapshot` 是 CSG → WinForms 的唯一数据通道。管线不再逐个属性写入控件，而是完整构建一个 POCO 后一次性交付。

3. **无回退逻辑**：`CollectSnapshot()` 不提供属性缺失时的默认值回退。Component 的完备性由管线前序阶段保证。如果某 Component 缺失，说明管线上游有 bug，应在上游修复而非下游补偿。

4. **日志第一线**：`WinFormApplier.Apply()` 的第一行即打印完整的 snapshot 数据。任何对 WinForms 的写入都可以通过这条日志追溯。