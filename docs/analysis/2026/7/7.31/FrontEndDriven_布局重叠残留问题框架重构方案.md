# FrontEndDriven 布局重叠残留问题框架重构方案

> **日期**：2026-07-31（第二轮）
> **状态**：层次一已实施（含回归修复）；层次二 Step 1、Step 2 已实施；其余待实施
> **前置文档**：`docs/Refactoring/FrontEndDriven_布局重叠缺陷四层重构方案.md`（第一轮，已基本落地）
> **触发问题**：`layout overlaps` 报告 10 处非父子关系控件重叠（共 78 个总重叠对，33 个控件）
> **回归修复**：层次一实施后 `layout overlaps` 出现 78 处全报 → 根因 Entity 真源短路 + 引用比较恒 false，已修复（见 §十）
> **架构决策**：**ECS 逻辑为主，WinForms 配置适配投影**（延续第一轮原则）

---

## 一、残留问题复盘

第一轮四层重构已落地：位置偏移累加（`ContainerSizeComponent.Left/Top`）、SplitterPanel 影实体尺寸精确计算（`TryComputeSplitterPanelLayout`）、SplitterMoved 触发 `RecalculateLayout`、`"splitterpanel"` 注册为容器、SplitContainer 创建时设置 Panel1/Panel2 唯一 Name。

**残留 10 处重叠分为两类**：

| 类别 | 涉及报告行 | 性质 | 关键证据 |
|------|-----------|------|----------|
| **检测器误报** | 1~4、6~9 行 | SplitterPanel 运行时 Name 唯一化修复仅覆盖 `WinFormApplier.ApplyControls` 新建分支，其它创建路径/二次 Apply 未兜底 → 面板 Name 仍为空 → fallback 为 `"SplitterPanel"` → 多面板撞名 → 祖孙链断裂 → 真实祖孙被误判为非父子 | `contentSplitter↔contentContainer`、`SplitterPanel↔其直系子控件` |
| **真实布局误差** | 5、10 行 | 深层 SplitContainer 嵌套（6~7 层：MainView→mainSplitter→Panel1→contentSplitter→Panel1/Panel2→子控件）中内层面板容器尺寸传播不稳定；`contentContainer` 以 `contentSplitter`（宽 900）而非其 `Panel1`（宽 540）为父基准 → 内容溢出容器；`terminalPanel`/`ConfigSelectorView` 跨区错乱 | `contentContainer` 区域 `(580,406,900,456)` 宽 900 ≠ 面板 540 |

---

## 二、框架层根因（相对第一轮的新增认识）

### 根因 A：SplitterPanel 双重身份无稳定映射 ——「双树不一致的架构源头」

- **ECS 真源树**：`ConfigDrivenControlFactory.CreateSplitterPanelEntity` 创建影实体（带 `SplitterPanelComponent` + `ParentEntityComponent`），但 `CsgEntryPoint.CollectSnapshot`（`Ports.cs` L146）/ `CollectAllInOnePass`（L183）明确跳过影实体，**影实体从不注册到 `ControlPool`**。
- **WinForms 运行时树**：`SplitContainer.Panel1/Panel2` 是 `SplitContainer` 自动创建的真实 `Panel` 对象，其 Name 由 `WinFormApplier.ApplyControls` 新建分支（L179-183）设置 `$"{desc.Name}_Panel1/_Panel2"`。
- **两树之间无 Entity↔Panel 映射**：检测器只能靠 Name 索引猜测，撞名即断链 → 真实父子被误判为非父子。

### 根因 B：布局多遍盲试的脆弱性 ——「父基准可回退到错误层级」

- `LayoutSystem.Update` 用最多 `MaxLayoutPasses=16` 遍"盲试"收敛；`TryGetParentContainerSize` 虽对"容器祖先尺寸未就绪"返回 false 禁止越级回退，但**同一遍内 `ContainerSizeComponent` 回写与子控件读取的时序无结构保证**——内层面板（`contentSplitter_Panel1`）若因 `SplitterRatioComponent` 未就绪被跳过，其子控件在后续遍次可能仍以 `contentSplitter`（上一级容器）为基准计算 → 宽 900 vs 面板 540。
- `SyncActualContainerSizes` 首次 Apply 后从 WinForms 回写尺寸，与 ECS 布局计算形成**双真源张力**（虽保留 Left/Top，但 Width/Height 可能覆盖）。

### 根因 C：检测器与管线脱节 ——「布局错误只能事后人工发现」

- `LayoutInspectSystem.ExecuteOverlaps` 是按需命令，与 `ExecuteRenderStage` 无关；布局错误无法在渲染期自动拦截，只能事后 `layout overlaps` 复检。

---

## 三、框架重构目标

1. **单真源**：ECS Entity 树 + `SplitterPanelRegistry` 为布局/检测的唯一真源；WinForms 仅投影。
2. **确定性布局**：父基准严格取**最近已就绪容器**，禁止回退到上一级未分割容器；收敛失败可诊断。
3. **闭环验证**：重叠检测纳入渲染阶段，`invalid` 触发定向修复。
4. **作用域安全**：`DestroyScopeEntities` 完整回收控件池 + WinForms 树 + 事件订阅。

---

## 四、实施步骤（三个层次）

### 层次一：统一 SplitterPanel 映射（根因 A / 类别一）

> **目标**：建立 ECS 影实体 ↔ 真实 WinForms 面板的一一映射，检测器以 Entity 树为准判祖孙，消除"假重叠"。

#### 1.1 新增 `SplitterPanelRegistry`（新文件）

**路径**：`CsharpPrj/Operation/FrontEndDriven/ControlLookup/SplitterPanelRegistry.cs`

```csharp
// ============================================================
// SplitterPanelRegistry — SplitterPanel 影实体 ↔ 真实面板映射注册表
//
// ★ 2026-07-31 框架重构（层次一）：消除 ECS Entity 树与 WinForms
//   控件树的"双树不一致"。
//   ECS 真源树中的 SplitterPanel 影实体（SplitterPanelComponent）
//   从不注册到 ControlPool；真实 WinForms 面板是 SplitContainer 的
//   Panel1/Panel2 运行时对象。二者间无稳定映射，检测器只能靠
//   Name 索引猜测，撞名即断链 → 真实父子被误判为非父子重叠。
//   本注册表建立 (SplitContainerEntity, PanelSide) → Panel 的映射，
//   供布局、检测、回收三方统一查询。
// ============================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using ECS.Core;

namespace FrontEndDriven.ControlLookup;

/// <summary>
/// SplitterPanel 影实体 ↔ 真实面板映射注册表
/// <para>以 (Entity, PanelSide) 为键，值为真实 WinForms Panel 控件。</para>
/// <para>由 RenderPipelineComposition.Create 创建并注入内部转换器。</para>
/// </summary>
public sealed class SplitterPanelRegistry
{
    // 作用域内面板注册（Key = Entity.Id + '|' + PanelSide）
    private readonly ConcurrentDictionary<string, PanelEntry> _entries = new();

    private sealed record PanelEntry(
        Entity SplitContainerEntity,
        string PanelSide,
        object Panel,
        string Scope);

    /// <summary>
    /// 注册真实面板与其 SplitContainer Entity、PanelSide 的映射
    /// <para>幂等：同键重复注册时覆盖（保持最新控件引用）。</para>
    /// </summary>
    /// <param name="splitContainerEntity">SplitContainer Entity（ECS 真源）</param>
    /// <param name="panelSide">"Panel1" 或 "Panel2"</param>
    /// <param name="panel">真实 WinForms Panel 控件</param>
    /// <param name="scope">作用域名（用于按作用域回收）</param>
    public void Register(Entity splitContainerEntity, string panelSide, object panel, string scope = "MainLayout")
    {
        var key = BuildKey(splitContainerEntity, panelSide);
        _entries[key] = new PanelEntry(splitContainerEntity, panelSide, panel, scope);
    }

    /// <summary>
    /// 解析真实面板（未注册时返回 null）
    /// </summary>
    public object? Resolve(Entity splitContainerEntity, string panelSide)
    {
        return _entries.TryGetValue(BuildKey(splitContainerEntity, panelSide), out var entry)
            ? entry.Panel
            : null;
    }

    /// <summary>
    /// 判断 SplitterPanel 影实体是否已有对应真实面板
    /// </summary>
    public bool IsRegistered(Entity splitContainerEntity, string panelSide)
    {
        return _entries.ContainsKey(BuildKey(splitContainerEntity, panelSide));
    }

    /// <summary>
    /// 按 SplitContainer Entity 注销两侧面板
    /// </summary>
    public void Unregister(Entity splitContainerEntity)
    {
        _entries.TryRemove(BuildKey(splitContainerEntity, "Panel1"), out _);
        _entries.TryRemove(BuildKey(splitContainerEntity, "Panel2"), out _);
    }

    /// <summary>
    /// 按作用域批量注销（供 DestroyScopeEntities 完整回收）
    /// </summary>
    /// <param name="scopeName">作用域名</param>
    public int UnregisterScope(string scopeName)
    {
        int removed = 0;
        foreach (var kvp in _entries)
        {
            if (string.Equals(kvp.Value.Scope, scopeName, StringComparison.Ordinal))
            {
                if (_entries.TryRemove(kvp.Key, out _))
                    removed++;
            }
        }
        return removed;
    }

    /// <summary>
    /// 清空全部注册
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
    }

    private static string BuildKey(Entity entity, string panelSide)
        => $"{entity.Id}|{panelSide}";
}
```

#### 1.2 `WinFormApplier` 提取面板命名 + 注册为公共方法

**路径**：`CsharpPrj/Operation/FrontEndDriven/WinForms/Applier/WinFormApplier.cs`

将 L179-183 的 `sc.Panel1.Name/Panel2.Name` 设置提取为 `EnsureSplitterPanelIdentity`：

```csharp
/// <summary>
/// 确保 SplitContainer 两侧面板的唯一 Name + 注册到 SplitterPanelRegistry
/// <para>★★ 2026-07-31 框架重构（层次一）：</para>
/// <para>   1. 幂等：Name 已正确则跳过，未设置则补设（覆盖新建与存量路径）。</para>
/// <para>   2. 与 ConfigDrivenControlFactory.CreateSplitterPanelEntity 的
///       命名规则一致（{descName}_Panel1/_Panel2），保证 ECS 影实体与
///       真实面板可通过 Name 对应。</para>
/// <para>   3. 注册到注册表，供检测器/回收链路以 Entity 为准查询，不再依赖
///       Name 索引猜测。</para>
/// </summary>
private void EnsureSplitterPanelIdentity(
    SplitContainer sc,
    string descName,
    Entity splitContainerEntity,
    string scope = "MainLayout")
{
    // 幂等补设唯一 Name
    if (string.IsNullOrEmpty(sc.Panel1.Name))
        sc.Panel1.Name = $"{descName}_Panel1";
    if (string.IsNullOrEmpty(sc.Panel2.Name))
        sc.Panel2.Name = $"{descName}_Panel2";

    // 注册到 SplitterPanelRegistry
    _splitterPanelRegistry.Register(splitContainerEntity, UIProtocol.SplitterPanel1, sc.Panel1, scope);
    _splitterPanelRegistry.Register(splitContainerEntity, UIProtocol.SplitterPanel2, sc.Panel2, scope);
}
```

**调用处**：`ApplyControls` 中 `existingControl != null ∧ control is SplitContainer sc` 分支与新建分支**都**调用该方法，覆盖所有创建路径（含二次 Apply 命中缓存池）。

**构造注入**：`WinFormApplier` 构造函数增加 `SplitterPanelRegistry` 参数，由 `RenderPipelineComposition.Create` 装配传入。

#### 1.3 检测器祖先判定以 Entity 树为准

**路径**：`CsharpPrj/Share/LayoutAnalysis/UIOverlapDetector/Calculator/OverlapCalculator.cs`

`IsParentChildRelation` 增加 Entity 真源路径：

```csharp
/// <summary>
/// 判断两个控件是否为父子/祖孙关系
/// <para>优先使用 Entity 真源路径（沿 ParentEntityComponent 链，含 SplitterPanel
/// 回溯）；真源不可用时回退 Name 索引路径（保持既有命令兼容）。</para>
/// <para>★★ 2026-07-31 框架重构（层次一）：消除多个 SplitterPanel 同名导致的
/// BuildNameIndex 撞名 → 祖先链断裂 → 真实父子被误判为非父子。</para>
/// </summary>
private static bool IsParentChildRelation(
    ControlMetadata a,
    ControlMetadata b,
    TreePathFinder<ControlMetadata> pathFinder,
    Func<ControlMetadata, ControlMetadata?>? entityTreeParentResolver = null)
{
    // 优先 Entity 真源路径（注入 ParentEntityComponent 链解析器）
    if (entityTreeParentResolver != null)
    {
        var entityPathFinder = new TreePathFinder<ControlMetadata>(entityTreeParentResolver);
        if (entityPathFinder.IsDescendantOf(a, b)) return true;
        if (entityPathFinder.IsDescendantOf(b, a)) return true;
        return false;
    }

    // 回退：Name 索引路径（无真源时）
    if (pathFinder.IsDescendantOf(a, b)) return true;
    if (pathFinder.IsDescendantOf(b, a)) return true;
    return false;
}
```

`OverlapCalculator` 增加可选的实体树解析器注入（`Func<ControlMetadata, ControlMetadata?>`，由 `LayoutInspectSystem`/`LayoutValidationSystem` 基于 EntityManager + ControlInfoComponent.Name 构建）；真源缺失时自动回退现有 Name 索引，不破坏既有命令。

**改动文件**：`SplitterPanelRegistry.cs`（新增）、`WinFormApplier.cs`、`OverlapCalculator.cs`、`RenderPipelineComposition.cs`（装配注入）。

> **效果**：10 处中的 1~4、6~9 行（检测器误报）在检测层即被排除。

---

### 层次二：布局确定性化 + 收敛诊断（根因 B / 类别二）

> **目标**：父基准严格取最近已就绪容器，禁止越级回退；收敛失败可见。

#### 2.1 `LayoutSystem` 改为深度分层处理

**路径**：`CsharpPrj/Operation/FrontEndDriven/ControlRender/Systems/LayoutSystem.cs`

- `Update()`：将"每遍扫描全部实体"改为**按容器祖先链深度分层**：
  - 第 0 遍只处理无 `ParentEntityComponent` 的根（Root）；
  - 第 N 遍只处理「父容器祖先链深度 = N」的实体（从 `ParentEntityComponent` 链预先计算深度）；
  - 每层完成后**先回写该层所有容器 `ContainerSizeComponent`，再进入下一层**，从结构上保证"父先于子"。
- `TryGetParentContainerSize` 保持"最近容器优先、禁止越级回退"，配合分层后成为结构性安全（子控件只会在其直接容器就绪的层被处理）。
- 收敛诊断：`Update()` 返回 `LayoutConvergenceReport`：

```csharp
/// <summary>
/// 布局收敛报告 — 每层跳过的实体与原因
/// <para>★★ 2026-07-31 框架重构（层次二）：使布局跳过原因可见，
/// 消除静默溢出。</para>
/// </summary>
public sealed class LayoutConvergenceReport
{
    /// <summary>跳过的实体列表（含原因枚举）</summary>
    public List<(Entity Entity, SkipReason Reason)> Skipped { get; } = new();

    /// <summary>是否完整收敛（无跳过）</summary>
    public bool IsConverged => Skipped.Count == 0;

    /// <summary>跳过原因枚举</summary>
    public enum SkipReason
    {
        /// <summary>父容器尺寸未就绪（ContainerSizeComponent 缺失/无效）</summary>
        ParentSizeMissing,
        /// <summary>父 SplitContainer 分割比例未就绪（SplitterRatioComponent 缺失/无效）</summary>
        RatioMissing,
        /// <summary>面板分割宽度无效（SplitterWidth 异常）</summary>
        SplitterWidthInvalid,
        /// <summary>祖先链深度超过 MaxTraversalDepth</summary>
        DepthExceeded
    }
}
```

`LayoutSystem.Update()` 签名改为 `public LayoutConvergenceReport Update()`，每次调用构建并返回报告；跳过分支填充原因。

#### 2.2 `SyncActualContainerSizes` 降级为校验 + 审计

**路径**：`CsharpPrj/Operation/FrontEndDriven/Ports.cs`（`WinFormsOutputPort`）

- 不再回写 `ContainerSizeComponent` 的 Width/Height（ECS 布局为唯一真源）。
- 仅对比 ECS 计算值 vs WinForms 实际值；偏差 > 阈值（如 2px）时记录 `ExternalConsistency` 告警并按 `.clinerules/rules.md §三 StateValidity` 触发降级评估。
- 首次 Apply 只校验 root 容器实际 `ClientSize` 是否与配置一致。

#### 2.3 收敛失败可见性

**路径**：`CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderPipelineComposition.cs`

- `ExecuteCreationStage`/`ExecuteRenderStage` 读取 `LayoutConvergenceReport`；
- 若有 `RatioMissing` 类跳过，输出明确告警（如"`contentSplitter_Panel1` 因 SplitterRatio 未就绪跳过"），写入 `FrontEndDrivenLog`，避免静默溢出。

**改动文件**：`LayoutSystem.cs`、`Ports.cs`、`RenderPipelineComposition.cs`。

---

### 层次三：检测-修复闭环 + 作用域安全（根因 C / 类别三）

> **目标**：重叠检测纳入渲染阶段，`invalid` 触发定向修复；作用域生命周期完整回收。

#### 3.1 新增 `LayoutValidationSystem`（新文件）

**路径**：`CsharpPrj/Operation/FrontEndDriven/CommandExecution/ECS/LayoutValidationSystem.cs`

```csharp
// ============================================================
// LayoutValidationSystem — 渲染期重叠验证 System
//
// ★ 2026-07-31 框架重构（层次三）：将重叠检测纳入渲染阶段。
//   在 ExecuteRenderStage 的 ApplySnapshot 之后、事件绑定之前执行，
//   使布局错误在渲染期即可拦截自愈，而非事后人工 layout overlaps 发现。
//
// 结果语义（遵循 .clinerules 真值域/状态机）：
//   valid     → 继续管线
//   degraded  → 仅检测器可解释边界 → 记 operationLog + 告警
//   invalid   → 真实重叠 → 触发定向重算（最多 1 次）→ 仍失败走
//               switch_strategy/TM 语义（记录 + 请求人工确认）
// ============================================================

namespace FrontEndDriven.CommandExecution.ECS;

/// <summary>
/// 渲染期重叠验证系统
/// <para>复用改进后的 OverlapCalculator（层次一：Entity 真源路径），
/// 输出与 LayoutInspectSystem.ExecuteOverlaps 一致。</para>
/// </summary>
public sealed class LayoutValidationSystem
{
    private readonly EntityManager _entityManager;

    /// <summary>验证结果</summary>
    public sealed class ValidationResult
    {
        public bool IsValid { get; init; }
        public int ProblematicCount { get; init; }
        public List<string> Messages { get; } = new();
    }

    public LayoutValidationSystem(EntityManager entityManager)
    {
        _entityManager = entityManager;
    }

    /// <summary>
    /// 对刚写出的 WinForms 控件树执行重叠验证
    /// <para>valid → 继续；invalid → 触发定向修复（由调用方决定）。</para>
    /// </summary>
    public ValidationResult Validate()
    {
        // 1. 遍历 Application.OpenForms 收集 ControlMetadata（复用 ControlTreeWalker）
        // 2. 调用 OverlapCalculator（含 Entity 树真源解析器）
        // 3. 映射结果语义并返回
        // TODO: 实现细节待 Act 模式落地
        throw new NotImplementedException();
    }
}
```

#### 3.2 `RecalculateLayout` 挂接验证

**路径**：`CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderPipelineComposition.cs`

- `RenderPipelineComposition.Create` 创建 `LayoutValidationSystem`；
- `RecalculateLayout()` 在 `ExecuteRenderStage()` 后追加 `_layoutValidationSystem.Validate()`；
- 验证失败触发定向重算（`_layoutSystem.RecalculateAll` 仅受影响子树，最多 1 次），防 `SplitterMoved → 重算 → Apply → 验证` 闭环。

#### 3.3 `DestroyScopeEntities` 完整回收

**路径**：`CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderPipelineComposition.cs` + `Ports.cs`（`WinFormsOutputPort`）

- `DestroyScopeEntities(scopeName)` 扩展为：
  1. 按 `ScopeComponent` 收集该作用域全部 Entity；
  2. 从 `ControlPool` 注销（新增 `ControlPool.Unregister(entity)` 或按列表批量移除）；
  3. 从 WinForms 父容器移除控件（`parent.Controls.Remove(control)`）；
  4. `UnsubscribeSplitterMoved`（复用 `_splitterUnsubscribeActions` 缓存）；
  5. `SplitterPanelRegistry.UnregisterScope(scopeName)`；
  6. `EntityManager.DestroyEntity`。
- `Clear()` 复用同一回收流程，保证 `LoadConfig`/`DestroyScopeEntities`/`Clear` 三路一致。

**改动文件**：`LayoutValidationSystem.cs`（新增）、`RenderPipelineComposition.cs`、`Ports.cs`、`ControlPool`（若需新增 Unregister）。

---

## 五、验证方式（框架行为检查）

| # | 检查点 | 预期行为 |
|---|--------|----------|
| 1 | SplitterPanel 映射 | 所有 SplitContainer 面板在 `SplitterPanelRegistry` 有注册；Name 唯一（`{parent}_Panel1/2`） |
| 2 | 布局确定性 | `contentContainer` 宽 = `contentSplitter.Panel1` 宽（540 而非 900）；深度分层无越级回退 |
| 3 | 收敛诊断 | `contentSplitter_Panel1` 若跳过，日志明确输出原因，不静默溢出 |
| 4 | 渲染期验证 | `ExecuteRenderStage` 后自动检测重叠；`invalid` 触发定向重算或 `switch_strategy`/`TM` |
| 5 | 作用域回收 | `DestroyScopeEntities`/`Clear` 后控件池 + WinForms 树 + 事件订阅 + 注册表完全清空 |
| 6 | `layout overlaps` 复检 | 父子误报消零；真实跨区重叠数收敛；`terminalPanel`/`ConfigSelectorView` 归位 |

---

## 六、工作量评估

| 层次 | 主要改动 | 预估 |
|------|----------|------|
| 层次一 | `SplitterPanelRegistry`、`WinFormApplier`、`OverlapCalculator`、`RenderPipelineComposition` | 1 天 |
| 层次二 | `LayoutSystem`、`Ports.cs`、`RenderPipelineComposition` | 1 天 |
| 层次三 | `LayoutValidationSystem`、`RenderPipelineComposition`、`Ports.cs` | 1 天 |
| 核验 | 编译 + `layout overlaps` 复检 + 拖拽/重载场景 | 0.5 天 |

---

## 七、风险与缓解

| 风险 | 说明 | 缓解 |
|------|------|------|
| `OverlapCalculator` 增加 Entity 真源依赖 | 检测器从纯 WinForms 反射变为混合，需保持无真源时降级可用 | 真源缺失时回退现有 Name 索引路径（不破坏既有命令） |
| 深度分层处理改变收敛顺序 | 与现存 16 遍迭代行为差异，可能暴露新边界 | 分阶段灰度：先在 `LayoutSystem` 增加诊断开关，验证后再切换分层 |
| `SyncActualContainerSizes` 降级为校验 | 部分容器尺寸可能由 WinForms 自适应（Dock）回写 | 仅对配置明确 Dock 的容器保留回写白名单；其余走校验 |
| `DestroyScopeEntities` 全量回收 | 调用方若依赖残留控件可能报错 | 按 `.clinerules` 作用域语义保证调用顺序（先 Destroy 再 LoadConfig） |
| `LayoutValidationSystem` 依赖 `Application.OpenForms` | 无打开表单时无法验证 | `Validate()` 在无表单时返回 `valid` + 跳过日志 |

---

## 八、附录：修改文件清单

| 文件 | 层次 | 修改内容 |
|------|------|----------|
| `CsharpPrj/Operation/FrontEndDriven/ControlLookup/SplitterPanelRegistry.cs`（新增） | 一 | Entity↔真实面板注册表 |
| `CsharpPrj/Operation/FrontEndDriven/WinForms/Applier/WinFormApplier.cs` | 一 | 面板命名 + 注册提取为公共方法，覆盖全路径 |
| `CsharpPrj/Share/LayoutAnalysis/UIOverlapDetector/Calculator/OverlapCalculator.cs` | 一 | 祖先判定加 Entity 真源路径 |
| `CsharpPrj/Operation/FrontEndDriven/ControlRender/Systems/LayoutSystem.cs` | 二 | 深度分层 + 收敛诊断 + 禁止越级回退结构性保证 |
| `CsharpPrj/Operation/FrontEndDriven/Ports.cs` | 二+三 | `SyncActualContainerSizes` 降级校验；`DestroyScopeEntities` 完整回收 |
| `CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderPipelineComposition.cs` | 一+二+三 | 装配注册表/诊断/验证；三路回收统一 |
| `CsharpPrj/Operation/FrontEndDriven/CommandExecution/ECS/LayoutValidationSystem.cs`（新增） | 三 | 渲染期重叠验证 + 定向修复 |
| `CsharpPrj/Operation/FrontEndDriven/ControlLookup/Container/EcsContainerCore.cs` | 三 | （视需要）提供 Entity 树祖先解析辅助，供检测器注入 |

---

## 十、层次一回归修复记录（Step A，2026-07-31）

### 现象
层次一（Entity 真源路径）实施后，`layout overlaps` 由 10 处变为 **78 处全报**，且报告父子字段已完全正确（`root` 父=`FileDriven`、`MainView` 父=`root`、`mainSplitter_Panel1` 父=`mainSplitter`、`contentSplitter` 父=`mainSplitter_Panel1` 等——证明第一轮 `EnsureSplitterPanelIdentity` 已使面板 Name 唯一化生效）。

### 根因（两个叠加）
1. **引用比较恒 false**：`TreePathFinder.IsDescendantOf` 使用 `EqualityComparer<T>.Default`（class 对象默认按**引用**比较）；而 `LayoutInspectSystem.BuildMetadataFromEntity` 为父节点创建的是**新 `ControlMetadata` 对象**，引用必然不等 → Entity 路径恒 false。
2. **短路不回退**：`OverlapCalculator.IsParentChildRelation` 中 "Entity 路径可用 → 判定后无条件 return"，Entity 恒 false 时**短路掉原本正确的 Name 索引路径** → 全部真实祖孙被判为非父子。

### 修复（`OverlapCalculator.cs`）
- `IsParentChildRelation` 改为**两阶段**：
  1. Entity 真源逐层上溯（`EntityIsAncestorOf`：沿 resolver 循环，**按 Name 忽略大小写比较**，不依赖引用相等性，防环上限 100 层）；
  2. Entity 判定 false 后**仍回退 Name 索引路径**（`pathFinder.IsDescendantOf`）。
- 移除 `AncestorRelationChecker.EntityTreePathFinder`（不再构建基于引用的 TreePathFinder），仅作为 resolver 载体。

### 修复后预期
- 报告中真实祖孙对（`root↔MainView`、`MainView↔toolbarPanel↔按钮`、`mainSplitter↔Panel1↔contentSplitter↔Panel1↔contentContainer` 等）由 Name 索引正确排除 → 78 处中绝大多数消零。
- 剩余为**真实布局溢出**（`contentContainer` 宽 900≠面板 540、`ConfigSelectorView` 动态加载位置错乱、`mainSplitter_Panel2` x=2388 偏移异常），需层次二（布局确定性化）解决。

### 结论
**第一轮 + 层次一（修复后）的目标"检测层消除假重叠"已达成**；检测器现在能正确区分真实祖孙。`layout overlaps` 复检由 10 处 → 78 处（回归）→ 修复后 **2 处**（真实布局溢出）。剩余真实问题由层次二解决。

---

## 十一、层次二实施记录（Step 1：LayoutSystem 深度分层，2026-07-31）

### 目标
消除剩余 2 处真实溢出（`terminalPanel` top=1326 偏移错乱、`contentContainer` 宽 900≠540）——根因是多遍盲试中"子控件读到父容器错误旧基准"的时序不确定性，且 `LayoutAppliedComponent` 锁定错误结果。

### 改动（`LayoutSystem.cs`）
- `Update()` 改为**「按深度分层 + 多遍兜底」双阶段**：
  1. **阶段 1（分层主导）**：将 `PendingLayoutComponent` 实体按容器祖先链深度（`GetEntityDepth`）分组，深度 0 递增处理；每层 `TryComputeEntityLayout` 计算全部实体并回写容器尺寸后进入下一层——结构性保证"父容器先于子控件、以最近已回写容器为基准"。
  2. **阶段 2（兜底）**：保留旧多遍扫描逻辑，处理分层阶段因 `SplitterRatioComponent` 未就绪等跳过的实体，保证最终收敛。
- 提取 `TryComputeEntityLayout`（原 Update 单实体计算逻辑）供两阶段复用。
- 新增 `GetEntityDepth`（ParentEntityComponent 链深度，上限 `MaxTraversalDepth`）。

### 修复后预期
- `contentContainer` 以 `contentSplitter_Panel1`（540×760）为基准 → 宽 540（而非 900）。
- `terminalPanel` 以 `contentSplitter.Panel2` 为基准 → 归位 y≈782~1082。
- `ConfigSelectorView` 动态加载若正确挂到 `contentContainer`，其子控件（`statusLabel`/`styleComboBox`）归位 content 区 → 与 `terminalPanel` 不再重叠 → **2 处 → 0 处**。

### Step 2 实施（`Ports.cs` SyncActualContainerSizes → 校验+审计，2026-07-31）
- 不再回写 `ContainerSizeComponent` 的 Width/Height（ECS 布局为唯一真源，config form.width/height=1200×800）。
- 仅对比 ECS 计算值 vs WinForms 实际 `ClientSize`；偏差 > 2px 时记录 `SyncActualContainerSizes_mismatch` 告警（ExternalConsistency，符合 `.clinerules/rules.md §三 StateValidity`），**不写回**。
- 消除此前首次 Apply 后 root 从 1200×800 被覆盖成实际窗体 ClientSize（如 1384×861）导致的全局比例放大——这是 `ConfigSelectorView`/`terminalPanel` 等坐标大幅越界的直接肇因。

### 待实施
- **Step 3**：动态 View（`ConfigSelector.json`）加载基准确认——确认 `ConfigSelectorView`（Content scope root，HostControl=`contentContainer`）的布局基准正确继承 `contentContainer` 的 `ContainerSizeComponent`（540×760），而非默认 1200×800。
- **复检**：用新构建重跑 `layout overlaps`，预期 root 恢复 1200×800 后坐标错乱消解。

---

## 九、与第一轮方案的关系

| 维度 | 第一轮（已落地） | 第二轮（本方案） |
|------|-----------------|-----------------|
| 位置偏移 | `ContainerSizeComponent` 增加 `Left/Top`，`LayoutSystem` 累加父偏移 | 深度分层，父基准结构性保证，禁止越级回退 |
| SplitterPanel 尺寸 | `TryComputeSplitterPanelLayout` 精确计算四元组 | `SplitterPanelRegistry` 建立 Entity↔真实面板映射 |
| SplitterMoved | 回调注入 + `RecalculateLayout` 补 `ExecuteRenderStage` | 挂接 `LayoutValidationSystem` 验证闭环 |
| 检测层 | SplitContainer 创建时设置 Panel Name（仅新建分支） | 面板命名/注册幂等化覆盖全路径 + Entity 真源判祖孙 |
| 生命周期 | — | `DestroyScopeEntities`/`Clear` 完整回收（控件池+树+事件+注册表） |