# WdUI 布局逻辑整合优化 — 执行方案

> **文档目的**：提供可执行的、分步骤的布局逻辑整合方案。
> **分析依据**：对 `Operation/WdUI` 下 12 个布局相关文件的逐行阅读。
> **当前问题**：布局逻辑分散在 12 个文件中，4 个入口点，职责边界模糊。

---

## 一、现状总览

### 1.1 布局逻辑分布地图

| # | 文件 | 布局职责 | 行数 |
|---|------|---------|------|
| 1 | `UI/WdUI.Layout.cs` | 比例布局、Min/Max约束、重叠检测、优化入口 | 328 |
| 2 | `UI/WdUI.LayoutEngine.cs` | 布局中介器、适配器、比例预检、配置转换 | 369 |
| 3 | `Layout/WdUI.Strategies.cs` | SplitContainer 策略 | 125 |
| 4 | `Layout/LayoutContext.cs` | 策略执行、Resize订阅 | 120 |
| 5 | `Layout/LayoutState.cs` | SplitterRatio、Config缓存 | 33 |
| 6 | `Layout/LayoutStrategyRegistry.cs` | 策略注册表 | 35 |
| 7 | `Layout/RenderSession.cs` | 布局会话状态 | 136 |
| 8 | `WdUI.RenderPipeline.cs` | 三阶段生命周期、Relayout、重入保护 | 509 |
| 9 | `Layout/Skeletons/ApplyLayoutSkeleton.cs` | TreeWalker两遍遍历、比例布局应用 | 287 |
| 10 | `Layout/Skeletons/TrackResizeSkeleton.cs` | Resize→Relayout | 68 |
| 11 | `Layout/Skeletons/CreateLayoutPanelSkeleton.cs` | 控件创建（含布局相关） | 324 |
| 12 | `UI/WdUI.Management.cs` | LayoutManager（StackLayout） | 547 |
| | **合计** | | **~2,861** |

### 1.2 四个布局入口点

```
入口1: WdUILayout.ApplyProportional()       — 单控件比例布局
入口2: LayoutMediator.Apply()               — 通过 LayoutDefinition 应用
入口3: WinFormsRenderPipeline.ApplyLayout() — 通过管线应用
入口4: LayoutManager.ApplyLayoutConfig()    — StackLayout 布局
```

### 1.3 SplitContainer 逻辑分散在 4 处

```
SplitContainerStrategy.Execute()           — 订阅 SplitterMoved 事件
ApplyLayoutSkeleton.ApplySplitterDistance() — 设置初始 SplitterDistance
ApplyLayoutSkeleton.ApplyPostLayoutProperties() — Orientation + SplitterDistance
LayoutContext.GetSplitterRatio()            — 获取用户拖拽比例
```

---

## 二、目标架构

### 2.1 整合后文件结构

```
Operation/WdUI/Layout/
├── ILayoutService.cs          ← [新增] 统一布局接口
├── LayoutService.cs           ← [新增] 布局服务实现
├── LayoutPipeline.cs          ← [新增] 布局管线（合并管线+Skeletons）
├── LayoutStrategies.cs        ← [合并] 策略接口+SplitContainer策略+注册表
├── LayoutContext.cs           ← [保留] 布局上下文（已较合理）
└── LayoutState.cs             ← [保留] 布局状态（已较合理）
```

### 2.2 整合后调用关系

```
调用方
  │
  ▼
ILayoutService  ← 唯一入口
  │
  ├── ApplyProportional()    → ProportionalMath（外部）
  ├── ApplyLayout()          → LayoutPipeline.ExecuteLayout()
  ├── ApplyStackLayout()     → StackLayoutMath（外部）
  ├── DetectOverlaps()       → OverlapMath（外部）
  ├── Optimize()             → AutoLayoutOptimizer（外部）
  └── Relayout()             → LayoutPipeline.Relayout()
                                  │
                                  └── LayoutStrategies（SplitContainer）
                                  └── LayoutContext（状态/Resize）
```

---

## 三、Phase 1：统一布局入口 ✅ 已完成

### 3.1 目标

创建 `ILayoutService` 接口和 `LayoutService` 实现类，将所有布局操作统一到一个入口。

### 3.2 操作步骤

#### Step 1.1：创建 `ILayoutService.cs` ✅

**文件路径**：`Operation/WdUI/Layout/ILayoutService.cs`

#### Step 1.2：创建 `LayoutService.cs` ✅

**文件路径**：`Operation/WdUI/Layout/LayoutService.cs`

**当前实现**：委托到 `LayoutPipeline` + `LayoutManager` + `LayoutMediator` + `WdUILayout`

#### Step 1.3：注册到 WdUIComposer ✅

在 `WdUIComposer` 中已添加 `ILayoutService` 属性。

#### Step 1.4：迁移调用方 ⏳ 待完成

### 3.3 验证标准

- [x] `WdUIComposer.LayoutService` 可正常访问
- [ ] 比例布局结果与之前一致
- [ ] SplitContainer 布局行为不变
- [ ] Resize→Relayout 行为不变
- [ ] 重叠检测结果一致

---

## 四、Phase 2：合并布局管线 ✅ 核心完成

### 4.1 目标

将 `WinFormsRenderPipeline` 中的布局部分 + `ApplyLayoutSkeleton` + `TrackResizeSkeleton` 合并为 `LayoutPipeline`。

### 4.2 操作步骤

#### Step 2.1：创建 `LayoutPipeline.cs` ✅

**文件路径**：`Operation/WdUI/Layout/LayoutPipeline.cs`

**当前状态**：已创建，包含完整实现：
- `ExecuteLayout()` — 两遍遍历（先序比例布局+后序 Splitter/后属性）
- `Relayout()` — 含重入保护
- `SubscribeResize()` — Resize→Relayout 链路
- `Cleanup()` — 清理重入保护
- 修复了 🔴 SplitterDistance 双重设置 Bug
- 修复了 🟡 WeakReference GC 风险（直接持有回调引用）

#### Step 2.2：修改 WinFormsRenderPipeline ✅

**当前状态**：已添加 `_layoutPipeline` 字段，构造函数中初始化 `new LayoutPipeline()`

#### Step 2.3：修改 TrackResizeSkeleton ✅

**说明**：TrackResizeSkeleton 已删除，Resize 订阅已迁移到 LayoutPipeline.SubscribeResize

#### Step 2.4：修改 ApplyLayoutSkeleton ✅

**说明**：ApplyLayoutSkeleton 已删除，布局逻辑已迁移到 LayoutPipeline

#### Step 2.5：更新 LayoutService.cs ✅

**当前状态**：
- 依赖从 `WinFormsRenderPipeline` 改为 `LayoutPipeline`
- 添加了 `IWinFormsLayoutContext` 和 `WinFormsRenderPipeline` 依赖
- `ApplyLayout(Control, UIConfiguration)` → 委托到 `_pipeline.ExecuteLayout()`
- `Relayout(Control)` → 从 `_renderPipeline.GetActiveSession()` 获取配置后委托到 `_pipeline.Relayout()`

#### Step 2.6：更新 WdUI.Core.cs ✅

**当前状态**：`LayoutService` 的 Lazy 初始化改为使用 `new LayoutPipeline()` 和新增的依赖

### 4.3 验证标准

- [x] `LayoutPipeline.ExecuteLayout()` 已实现（从 ApplyLayoutSkeleton 迁移）
- [x] `LayoutPipeline.Relayout()` 重入保护已实现
- [x] `LayoutPipeline.SubscribeResize()` 已实现
- [x] Splitter 设置时不会触发 Resize 循环（IsApplyingSplitter 标志）
- [ ] 旧 `WinFormsRenderPipeline.ApplyLayout()` 行为不变（需编译验证）

---

## 五、Phase 3：合并布局策略

### 5.1 目标

将 `SplitContainerStrategy` + `LayoutStrategyRegistry` 合并到 `LayoutStrategies.cs`。

### 5.2 操作步骤

#### Step 3.1：创建 `LayoutStrategies.cs`

**文件路径**：`Operation/WdUI/Layout/LayoutStrategies.cs`

```csharp
#nullable enable
using System.Collections.Concurrent;
using System.Windows.Forms;
using BasicMethod.Common;
using BasicMethod.ConfigurationModels;
using WdUI.Diagnostics;

namespace WdUI.Layout.Strategies
{
    // ================================================================
    // 布局策略接口
    // ================================================================

    public interface ILayoutStrategy
    {
        string ContainerType { get; }
        void Execute(Control target, ControlConfig config, IWinFormsLayoutContext context);
    }

    public abstract class LayoutStrategyBase : ILayoutStrategy
    {
        public abstract string ContainerType { get; }
        public string Key => ContainerType;
        public int Order => 0;
        public abstract void Execute(Control target, ControlConfig config, IWinFormsLayoutContext context);
    }

    // ================================================================
    // SplitContainer 策略
    // ================================================================

    public class SplitContainerStrategy : LayoutStrategyBase
    {
        private static readonly DedupFlags<SplitContainer> _splitterSubscribed = new();

        public override string ContainerType =>
            global::WdUI.Settings.WdUISettings.Instance.EnumDefaults.SplitContainerType;

        public override void Execute(Control control, ControlConfig config, IWinFormsLayoutContext context)
        {
            if (control is not SplitContainer splitter) return;
            SubscribeSplitterMoved(splitter, context);
        }

        private void SubscribeSplitterMoved(SplitContainer splitter, IWinFormsLayoutContext context)
        {
            if (!_splitterSubscribed.TrySet(splitter)) return;

            splitter.SplitterMoved += (s, e) =>
            {
                if (s is SplitContainer sp)
                {
                    int dimension = sp.Orientation == Orientation.Vertical ? sp.Width : sp.Height;
                    if (dimension > 0)
                    {
                        double ratio = (double)sp.SplitterDistance / dimension;
                        context.State.GetOrCreate(sp).SplitterRatio = ratio;
                    }
                }
            };
        }
    }

    // ================================================================
    // 布局策略注册表
    // ================================================================

    public class LayoutStrategyRegistry
    {
        private readonly ConcurrentDictionary<string, ILayoutStrategy> _strategies = new();

        public void RegisterStrategy(ILayoutStrategy strategy)
        {
            _strategies[strategy.ContainerType] = strategy;
        }

        public bool TryGet(string key, out ILayoutStrategy? strategy)
            => _strategies.TryGetValue(key, out strategy);
    }
}
```

#### Step 3.2：删除旧文件

- 删除 `Layout/WdUI.Strategies.cs`
- 删除 `Layout/LayoutStrategyRegistry.cs`

### 5.3 验证标准

- [ ] `SplitContainerStrategy` 行为不变
- [ ] `LayoutStrategyRegistry` 注册/查找功能正常
- [ ] `LayoutContext` 中的策略调用不受影响

---

## 六、Phase 4：清理废弃代码

### 6.1 可删除的废弃方法

| 位置 | 方法 | 原因 |
|------|------|------|
| `WdUI.Layout.cs` | `ApplyLayout()` | 标记 `[Obsolete]`，空实现 |
| `WdUI.LayoutEngine.cs` | `DetectOverlaps()` 委托 | 直接委托到 `WdUILayout`，可内联 |
| `WdUI.Management.cs` | `LayoutManager.ApplyLayout()` | 由 `ILayoutService` 替代 |
| `WdUI.Management.cs` | `LayoutManager.ApplyLayoutConfig()` | 由 `ILayoutService` 替代 |

### 6.2 可清理的临时注释

搜索以下模式并删除（重构完成后的噪音）：

```
// ★ 修复：...
// ★ 统一：...
// ★ 整合：...
// ★ 重构：...
// ★ 迁移：...
// ★ 清理：...
// ★ 消除：...
```

### 6.3 验证标准

- [ ] 删除后编译通过
- [ ] 无运行时行为变化

---

## 七、Phase 5：切换调用方

### 7.1 目标

将所有外部调用从旧入口切换到 `ILayoutService`，然后删除旧入口代码。

### 7.2 操作步骤

#### Step 5.1：搜索所有旧入口调用

```bash
# 搜索 WdUILayout 的直接调用
grep -r "WdUILayout\." --include="*.cs" Operation/

# 搜索 LayoutMediator 的直接调用
grep -r "LayoutMediator" --include="*.cs" Operation/

# 搜索 LayoutManager 的直接调用
grep -r "LayoutManager\." --include="*.cs" Operation/

# 搜索 RenderPipeline 的布局相关调用
grep -r "\.ApplyLayout\|\.Relayout\|\.Render\b" --include="*.cs" Operation/
```

#### Step 5.2：逐个替换

| 旧代码 | 新代码 |
|--------|--------|
| `WdUILayout.ApplyProportional(c, l, t, w, h)` | `WdUIComposer.LayoutService.ApplyProportional(c, l, t, w, h)` |
| `new LayoutMediator().Apply(parent, def)` | `WdUIComposer.LayoutService.ApplyLayout(parent, def)` |
| `LayoutManager.Instance.ApplyLayout(parent, name)` | `WdUIComposer.LayoutService.ApplyStackLayout(parent, name)` |
| `pipeline.ApplyLayout(target)` | `WdUIComposer.LayoutService.ApplyLayout(target, config)` |
| `pipeline.Relayout(target)` | `WdUIComposer.LayoutService.Relayout(target)` |

#### Step 5.3：删除旧代码

当所有调用方都迁移后，删除以下旧代码：

- `WdUILayout` 类中已委托的方法（保留 `GetParentSize` 等被多处引用的方法）
- `LayoutMediator` 类（如果不再被直接使用）
- `LayoutManager` 类（如果不再被直接使用）

### 7.3 验证标准

- [ ] 所有调用方已迁移
- [ ] 旧代码删除后编译通过
- [ ] 所有布局场景测试通过

---

## 八、时间线与依赖关系

```
Phase 1: 统一入口     ← 无依赖，可立即开始
    │
    ▼
Phase 2: 合并管线     ← 依赖 Phase 1（LayoutService 需要 LayoutPipeline）
    │
    ▼
Phase 3: 合并策略     ← 无依赖，可与 Phase 2 并行
    │
    ▼
Phase 4: 清理废弃     ← 依赖 Phase 1-3（清理已整合的旧代码）
    │
    ▼
Phase 5: 切换调用方   ← 依赖 Phase 1-4（所有整合完成后）
```

**建议**：
- Phase 1 和 Phase 3 可以并行执行（互不依赖）
- Phase 2 可以在 Phase 1 之后开始
- Phase 4 和 Phase 5 是收尾阶段，在所有整合完成后执行

---

## 九、现有逻辑错误（需在整合中修复）

### 9.1 🔴 SplitterDistance 双重设置（Bug）

**位置**：`ApplyLayoutSkeleton.cs` 第 186 行 + 第 220-248 行

**问题**：`ApplyLayoutViaWalker` 的第二遍后序遍历中，对 SplitContainer 做了两次 SplitterDistance 设置：

```
第一次：ApplySplitterDistance(splitter, context)       ← 第 186 行
第二次：ApplyPostLayoutProperties(control, node, context) ← 第 202 行
        → 内部又设置了 splitter.SplitterDistance       ← 第 235-247 行
```

**后果**：第二次设置会覆盖第一次。如果 `state.SplitterRatio`（用户拖拽比例）和 `config.SplitterDistance`（配置默认值）不同，最终使用的是配置默认值，用户拖拽比例被丢弃。

**修复方案**：在 `LayoutPipeline` 中统一 SplitterDistance 设置逻辑，只在一个地方处理。

---

### 9.2 🟡 `RenderFullAsync` 的 SuspendLayout 时序问题

**位置**：`WdUI.RenderPipeline.cs` 第 288-299 行

```csharp
public async Task RenderFullAsync(Control target, UIConfiguration config)
{
    Render(target, config);       // 创建阶段，内部 SuspendLayout
    ApplyLayout(target);          // 布局阶段，此时可能仍在 SuspendLayout
    await BindEventsAsync(target);
}
```

**问题**：`Render()` 内部在 `CreateLayoutPanelSkeleton` 中调用了 `input.SuspendLayout()`，`ResumeLayout(true)` 在 finally 块中。`ApplyLayout()` 在 ResumeLayout 之后调用，**但注释说"调用方应在 ResumeLayout 后再次调用 ApplyLayout"**，暗示当前实现可能不可靠。

**修复方案**：在 `LayoutPipeline` 中确保布局阶段在正确的 Layout 状态下执行。

---

### 9.3 🟡 `LayoutContext.SubscribeResize` 的 WeakReference 回调可能被提前 GC

**位置**：`LayoutContext.cs` 第 71-96 行

**问题**：`callback` 是 `Action<Control>` 委托实例。当 `SubscribeResize` 方法返回后，如果调用方没有保持对 `callback` 的引用，这个委托可能被 GC 回收。`WeakReference` 在这里反而成了问题——它本意是防止事件阻止控件 GC，但回调本身也可能被意外回收。

**修复方案**：在 `LayoutPipeline` 中直接持有回调引用，或使用强引用 + 显式取消订阅模式。

---

### 9.4 🟡 `LayoutMediator.Apply` 的 Tag 覆盖风险

**位置**：`WdUI.LayoutEngine.cs` 第 183-194 行

**问题**：`_engine.Apply(container)` 执行期间，`container.Tag` 被临时覆盖为 `definition`。如果在此期间有其他代码读取 `Tag`，会读到错误的值。

**修复方案**：在 `LayoutPipeline` 中避免临时修改 Tag，或使用独立的上下文传递数据。

---

## 十、现有冗余逻辑（需在整合中消除）

### 10.1 `IsApplyingSplitter` 的双重管理

**跨文件**：
- `WinFormsRenderPipeline.cs` 第 479-489 行：定义属性
- `ApplyLayoutSkeleton.cs` 第 59-61 行：设置值
- `TrackResizeSkeleton.cs` 第 50 行：读取值

**消除方案**：集中到 `LayoutPipeline` 中统一管理。

---

### 10.2 `Relayout` 重入保护的双重实现

**位置**：
- `WinFormsRenderPipeline.cs` 第 360-406 行：`_relayoutGuards`
- `LayoutPipeline` 方案中又计划添加一套

**消除方案**：只保留 `LayoutPipeline` 中的一套。

---

### 10.3 `ApplyLayoutSkeleton` 与 `LayoutMediator` 的两条布局路径

| 路径 | 机制 | 使用场景 |
|------|------|---------|
| `ApplyLayoutSkeleton` | TreeWalker 两遍遍历 + 手动 `WdUILayout.ApplyProportional` | RenderPipeline 的布局阶段 |
| `LayoutMediator.Apply` | `SmartLayoutEngine` + `WinFormsContainerAdapter` | 外部直接调用 |

**问题**：同一个布局操作有两种不同的实现方式，可能导致行为不一致（例如 `LayoutMediator` 有 `RunProportionalPreflight` 预检，而 `ApplyLayoutSkeleton` 没有）。

**消除方案**：统一到 `LayoutPipeline` 中，选择一种作为标准路径。

---

### 10.4 `LayoutMediator` 的重叠检测纯委托方法

**位置**：`WdUI.LayoutEngine.cs` 第 323-333 行

```csharp
public OverlapReport DetectOverlaps(Control parent, LayoutDefinition definition)
    => WdUILayout.DetectOverlapsInDefinition(definition);

public OverlapReport DetectOverlapsInOutput(List<ElementOutput> elements, int parentWidth, int parentHeight)
    => OverlapMath.DetectOverlapsInElements(WdUILayout.ConvertToElementDescs(elements));
```

**问题**：两个方法都是纯委托，没有额外逻辑。调用方可以直接调用 `WdUILayout` 或 `OverlapMath`。

**消除方案**：删除这两个方法，调用方直接使用 `ILayoutService.DetectOverlaps()`。

---

### 10.5 `LayoutManager` 中的样式应用与布局无关

**位置**：`WdUI.Management.cs` 第 236-338 行

**问题**：`ApplyDefaultStyle`、`ApplyDarkStyle`、`SafeReplaceFont` 是样式/主题功能，与布局完全无关。

**消除方案**：迁移到独立的 `ThemeService` 或 `StyleApplier` 类中。

---

### 10.6 `RenderSession.SkeletonContext` 死代码

**位置**：`RenderSession.cs` 第 77 行

**问题**：`SkeletonContext` 属性存在的唯一原因是传递 `PendingContainers`，但 `PendingContainers` 已不再用于注册（仅用于日志）。

**消除方案**：删除 `SkeletonContext` 属性。

---

### 10.7 `LayoutStrategyRegistry` 单策略过度设计

**位置**：`LayoutStrategyRegistry.cs` + `WdUI.Strategies.cs`

**问题**：注册表中目前只注册了 `SplitContainerStrategy` 一个策略。`ContainerStrategy` 已被移除。整个策略模式在当前代码中只有一个实现类。

**消除方案**：合并到 `LayoutStrategies.cs` 中，去掉注册表抽象，直接使用策略实例。

---

## 十一、风险评估

| 风险 | 等级 | 缓解措施 |
|------|------|---------|
| Phase 1 新增接口与现有接口不一致 | 低 | 委托到现有实现，行为不变 |
| Phase 2 合并时遗漏逻辑 | 中 | 逐行对比 ApplyLayoutSkeleton 和 LayoutPipeline |
| Phase 3 策略注册表被外部引用 | 低 | 保留 `LayoutStrategyRegistry` 类名，仅移动位置 |
| Phase 5 调用方遗漏 | 中 | 使用 grep 搜索所有旧调用，逐个确认 |
| 整合后性能退化 | 低 | 委托调用仅增加一层间接，性能影响可忽略 |
| **Bug 修复引入回归** | **中** | SplitterDistance 修复需重点测试拖拽→Relayout 场景 |

---

## 十二、整合前后对比


| 指标 | 整合前 | 整合后 | 改善 |
|------|--------|--------|------|
| 布局相关文件数 | 12 个 | 5 个 | -58% |
| 布局入口点 | 4 个 | 1 个 | -75% |
| SplitContainer 逻辑分散 | 4 处 | 1 处 | -75% |
| Resize→Relayout 链路跨文件 | 3 个 | 1 个 | -67% |
| 重叠检测入口 | 2 个 | 1 个 | -50% |
| 布局相关代码行数 | ~2,861 行 | ~2,000 行（预估） | -30% |
