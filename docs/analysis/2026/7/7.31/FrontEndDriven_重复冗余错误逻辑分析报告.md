# FrontEndDriven 重复 / 冗余 / 错误逻辑分析报告

> **分析目标**：`CsharpPrj/Operation/FrontEndDriven`
> **分析日期**：2026-07-31
> **分析方法**：静态代码阅读 + 跨项目引用核验（FileDriven / FrontEndDriven 命名空间比对）
> **范围**：Ports.cs、Shared/、CommandExecution/、WinForms/、ControlLookup/、Initial/、ControlInteraction/ 等核心目录

---

## 一、重复逻辑（Duplicate）

### 1.1 跨项目复制代码（最严重）

FrontEndDriven 项目目录内存在从 **FileDriven** 项目整体复制而来、命名空间未改的代码：

| 文件位置（位于 FrontEndDriven） | 实际命名空间 / 类名 | 问题 |
|---|---|---|
| `Shared/Utils/PathMananger.cs` | `FileDriven.Abstractions.PathMananger` | 全部路径硬编码指向 `FileDriven/*` 目录（`FileDriven/DrivenConfig/JsonConfig`、`FileDriven/filedriven_log_messages.json` 等），与 FrontEndDriven 项目无关联，同名类却同时编译进 FrontEndDriven 程序集 |
| `Shared/EventHelper.cs` | `FileDriven.Abstractions.EventHelper` | 引用 `UniversalMiddleware`，命名空间错位 |
| `Shared/EventBindingFormatter.cs` | `FileDriven.Abstractions.EventBindingFormatter` | 同上 |
| `CommandExecution/Core/CommandExecutionEngine.cs` | `FileDriven.Execution.Core.FileDrivenExecutionEngine` | 「FileDriven 执行引擎门面」整体复制到 FrontEndDriven 项目 |
| `Shared/ContractBuilder.cs` | `FileDriven.Pipeline.Contracts.ContractBuilder` | 命名空间为 FileDriven.Pipeline.Contracts，类实现了 FrontEndDriven 的 Schema 体系却挂 FileDriven 命名空间 |
| `Shared/ContractRegistry.cs` | `FileDriven.Pipeline.Contracts.ContractRegistry` | 同上 |

**后果**：

- 其他模块（如 `CsharpPrj/Modules/DiagnosticContext/`）通过反射 `Type.GetType("FileDriven.Abstractions.PathMananger, FileDriven.Abstractions")` 查找此类——反射第一参数格式为 `类型全名, 程序集名`。但该类实际上编译进的是 **FrontEndDriven** 程序集，程序集名不匹配导致 `Type.GetType` 返回 null → 路径解析静默回退（`DiagnosticsConfigReader` 中 catch 吞掉异常）。
- 跨项目职责严重错位：FrontEndDriven 内复制了 FileDriven 的路径管理 / 执行引擎 / 契约注册体系，形成两套并行实现。

### 1.2 控件查找体系三重冗余（导致各自不一致）

存在 **3 套独立实现**「按名称查找控件 / Entity」的能力：

| 实现 | 查找方式 | 位置 |
|---|---|---|
| `ControlFinder` | WinForms 控件树递归遍历（`TreeWalker`） | `WinForms/Finder/ControlFinder.cs` |
| `EntityTreeService` | ControlPool O(1) → EntityManager 组件查询 | `WinForms/Services/EntityTreeService.cs` |
| `EcsEntityTreeWalker` | Entity 树 BFS 遍历（ParentEntityComponent 链） | `ControlLookup/EcsEntityTreeWalker.cs` |

- `ControlFinder` 与 `ControlRegistryAwareFinder` 大量方法互相委托（`FindByName` / `FindInForm` / `IsDescendantOf` / `GetDepth`），职责边界模糊，形成「适配器 → 适配器 → 适配器」的多层转发。
- `ControlFinder` 内部 `Find` / `FindRecursive` / `FindByPredicate` / `FindByType` 四个方法本质都为「带递归策略的深度优先查找」，差异仅在谓词构造，缺少统一的入口收敛。
- `ClickSetGetSystems`（见 §3.1）又完全绕过这三套体系，手写第四套遍历——体系碎片化的直接体现。

### 1.3 SplitterPanel 属性 / 布局重复写入

- `Ports.cs` 中 `CalculateSplitterDistance` 与 `CollectComponentData` 均计算 Splitter 距离，重复出现 `TryGetComponent<SplitterRatioComponent>` + `TryGetComponent<ComputedLayoutComponent>` 模式。
- `WinFormApplier.BuildPropertyDict` 与 `Ports.ToCanonicalProps` 各自独立构建属性字典——同一批 `UIProtocol.*` 属性键（Left/Top/Width/Height/Text/Enabled/Visible/FontFamily/…）在两处手动拼装，新增属性需同步改两处。

### 1.4 控件创建双注册链

`ControlTypeRegistry`（静态注册 30+ 类型）→ `StandardControlStrategies.RegisterAll()`（遍历搬移到 `ControlStrategyRegistry`）。中间层 `StandardControlStrategies` 仅作搬运，属多余间接层。

### 1.5 Entity ID 解析的缓存与委托

`WinFormsUIAdapter` 与 `WinFormApplier` 各自维护 `_entityIdCache: Dictionary<string, Entity>`，均委托给 `EntityIdParser.TryParse`。两处缓存相互独立、重复维护，且都在管线 `Clear()` 时手动清空，逻辑分散。

---

## 二、冗余逻辑（Redundant）

### 2.1 `EcsContainerCore.cs` 桩代码 / 死代码（最严重）

```csharp
public Entity GetRootEntity()   // 恒返回 Invalid
{
    if (_composition != null && _containerView != null)
    {
        var entity = _composition.OutputPort != null
            ? new Entity()      // 恒等于 Entity.Invalid（默认构造）
            : Entity.Invalid;
        // 临时方案：从 InputPort 获取最后加载的配置结果 —— 未实现
    }
    return Entity.Invalid;      // 无条件返回 Invalid
}

public ControlPool? ControlPool
    => _composition?.OutputPort != null ? null : null;   // 恒 return null

public Control? FindControlByName(string name)
{
    if (string.IsNullOrEmpty(name)) return null;
    return null;   // 注释「改用其他方式」但没有任何实现
}
```

- `ControlPool` 属性三元表达式两个分支都是 `null` → 恒为 null，纯死代码。
- `FindControlByName` 无条件返回 null。
- `GetRootEntity` 内部条件分支永不改变返回值，整方法等价于 `return Entity.Invalid;`。

### 2.2 `UIReconciler` 的结构变更路径实际退化

```csharp
bool hasStructuralChange = changes.Any(c =>
    c.Type == UIChangeType.NodeAdded || c.Type == UIChangeType.NodeRemoved);

if (hasStructuralChange)
{
    // 节点增删需要创建/销毁控件…UIReconciler 不具备这些能力…
    return _adapter.ApplyCanonical(targetState);  // 全量回退
}
```

只要存在 `NodeAdded` / `NodeRemoved` 就回退全量应用——差异比较在结构变更时完全无意义，`UIReconciler` 实际退化为「全量应用器 + 属性差异增量」。差异引擎的复杂计算（`ComputeChanges` / `GetChangedProperties`）在结构变更场景下全部白算。

### 2.3 `RecursionPolicy.DirectChildren` 冗余且行为不统一

```csharp
public static Control? FindRecursive(Control parent, string name, RecursionPolicy? policy = null)
{
    if (policy.MaxDepth == 1)
        return FindDirectChild(parent, name);   // 直接 parent.Controls[name]，不分大小写、不走 TreeWalker
    return _treeWalker.FindFirst(parent, ... TraversalOptions.MaxDepth = policy.MaxDepth ...);
}
```

`MaxDepth == 1` 分支绕开 `TreeWalker` 走 `Controls[name]` 索引——与 `FindByType<T>` 等其它方法使用 `TraversalOptions.MaxDepth` 的行为不一致（索引查找大小写敏感、TreeWalker 大小写不敏感）。冗余特判且语义分裂。

### 2.4 控件类型 Schema 契约构建两次

`ControlTypeRegistry` 静态构造函数 `RegisterAll()` 构建全部 Schema；`ContractRegistry` 静态构造函数又从 `ControlTypeRegistry.SchemaFactories` **再次遍历构建一次**（`Register(DefaultKey, ContractBuilder.DefaultContract)` + 循环 Register）。同批 Schema 构建执行两遍，且 `ControlTypeRegistry.DefaultContract` 与 `ContractRegistry` 中的 DefaultKey 契约各自独立构建。

### 2.5 `EventHub` 与事件绑定系统的事件触发职责不清

`CommandExecution/EventHub.cs` 提供 `RaiseInitializationCompleted` / `RaiseConfigLoaded` / `ClearAllSubscriptions`，而 `EventBindingSystem` 独立处理 UI 事件绑定。`InitializationCompleted` / `ConfigLoaded` 事件由外部手动 Raise、无人订阅——事件中心形同虚设，与真正的绑定流程脱节。

---

## 三、错误逻辑（Bug / Logic Error）

### 3.1 `ClickSetGetSystems.cs` 绕开统一查找体系（架构级错误）

```csharp
private static bool PerformDirectClick(string controlName)
{
    foreach (Form? form in System.Windows.Forms.Application.OpenForms)
    {
        var controls = form.Controls.Find(controlName, true);
        if (controls.Length > 0 && controls[0] is System.Windows.Forms.Button btn)
        {
            btn.PerformClick();
            return true;
        }
    }
    return false;
}
```

- **完全绕过**已有的 `ControlPool → EntityTreeService → ContainerRegistry → ControlFinder` 查找链。
- 手写 `Application.OpenForms` 遍历 + `Controls.Find(controlName, true)` 与 `ControlFinder.FindAcrossOpenForms` **功能等价**。
- 后果：动态创建但未挂入 Form.Controls 树的控件（通过 ControlPool 单独注册）无法被 `click/set/get` 命令命中 → 「未找到控件」误报。
- 与 `ControlFinder` 注释「整合：替代 WinFormsCommandEngine.FindControlOrLog 中的手写跨 Form 遍历」直接矛盾——新的 ECS 命令系统又一次回归手写遍历。
- `SetSystem.PerformDirectSet` / `GetSystem.PerformDirectGet` 存在同样问题，且三处重复同一遍历骨架。

### 3.2 `PathMananger.cs` 跨项目路径必然解析错误

`GetFrontEndDrivenLogMessagesPath()` 硬编码：

```csharp
Path.Combine(GetProjectRoot(), "CsharpPrj", "Operation", "FrontEndDriven", "fde_log_messages.json")
```

此类若被 FileDriven 程序集通过反射真正引用（即通过 `Type.GetType` 找到但程序集名不匹配的路径之外），项目根不同将指向错误位置。当前因反射找不到（程序集名不匹配）而走 catch 回退——属于「错误被静默掩盖」而非「正确运行」。

### 3.3 `CommandInputPort.ExecuteSingle` 空 catch 静默失败

```csharp
try
{
    var execService = AppServiceProvider.Resolve<TConfig.IEventExecutionService>();
    if (execService != null) { ... }
}
catch
{
    // 工作流执行失败，回退到 ECS 管线
}
```

工作流执行失败被完全吞掉，无任何日志输出。根据 `rules.md` OnError 策略（`error∈{execution_failure...}` 应 report/record），至少应记入 operationLog。调用方无法得知命令实际走了哪条路径（工作流 vs ECS 管线），排障困难。

### 3.4 `UIReconciler.Apply` 空表单时提前置位 `_initialized`

```csharp
if (targetState.Nodes == null || targetState.Nodes.Count == 0)
{
    if (!_initialized) { _initialized = true; }   // ← 错误置位
    return 0;
}
```

首次渲染 ECS 尚未填充时，空表单被当作「已初始化」。虽然 `_lastAppliedState == null` 的检查恰好兜底了后续全量路径，但 `_initialized` 语义已被破坏——它表示「已成功应用过」，而此处并未发生任何实际应用。属边界逻辑缺陷。

### 3.5 `EcsConfigContainer.GetRootEntity()` 恒返回 Invalid 引发运行时异常

```csharp
public override Entity GetRootEntity()
{
    return Entity.Invalid;
}
```

但 `EcsConfigContainer` 实现了 `IEntityTreeRoot` 接口，被 `ControlRegistryAwareFinder` 调用：

```csharp
var control = entityTree.FindControlByName(root.GetRootEntity(), name);
```

而 `EntityTreeService.FindControlByName(root, ...)` 收到 Invalid Entity 会 `throw new InvalidOperationException("根 Entity 无效…")`。

**即**：通过 `ControlRegistryAwareFinder.FindInForm` / `FindByName` 查找时，若 Form 实现 `IEntityTreeRoot`（如 `EcsConfigContainer` 宿主），会直接抛异常而非返回 null。配合 2.1 中 `EcsContainerCore` 的桩实现，整个「容器 → 根 Entity」链在真实场景下不可用。

---

## 四、修复建议优先级

| 优先级 | 项 | 建议 |
|---|---|---|
| **P0** | 移除 FileDriven 复制文件 | `PathMananger` / `EventHelper` / `EventBindingFormatter` / `CommandExecutionEngine` 移回 FileDriven 项目或删除；`ContractBuilder` / `ContractRegistry` 重命名空间为 `FrontEndDriven.*`，消除跨程序集反射查找的静默失败 |
| **P0** | 统一控件查找链 | `ClickSetGetSystems` 改走 `ControlFinder.FindAcrossOpenForms` 或 `IControlFinder` 注入；合并 `EcsEntityTreeWalker` 与 `EntityTreeService` 的 Entity 查找能力，保留一套 |
| **P1** | 修复 `EcsConfigContainer.GetRootEntity()` 与 `EcsContainerCore` 桩代码 | 实现真实根 Entity 返回，或删除接口实现并同步清理 `ControlRegistryAwareFinder` 的调用链，消除运行时异常 |
| **P1** | `RecursionPolicy.DirectChildren` 统一走 TreeWalker | 删除 `MaxDepth==1` 特判，统一 `TraversalOptions.MaxDepth` 行为 |
| **P2** | 消除 UIReconciler 结构变更全量回退 | 补齐增删处理能力（依赖 ControlPool / ControlStrategyRegistry）或直接废弃差异引擎，声明 `UIReconciler` 仅属性增量应用 |
| **P2** | `ExecuteSingle` 空 catch 补日志 | catch 块记录 `OperationLog` 并标记回退路径 |
| **P2** | SplitterDistance / EntityIdCache 双实现收敛 | `CalculateSplitterDistance` 合并入 `CollectComponentData`；`_entityIdCache` 收敛为单例或在 `EntityIdParser` 内统一缓存 |

---

## 五、附：涉及文件清单

| 文件 | 问题类别 |
|---|---|
| `Shared/Utils/PathMananger.cs` | 跨项目复制（P0） |
| `Shared/EventHelper.cs` | 跨项目复制（P0） |
| `Shared/EventBindingFormatter.cs` | 跨项目复制（P0） |
| `Shared/ContractBuilder.cs` | 跨项目复制（P0） |
| `Shared/ContractRegistry.cs` | 跨项目复制（P0） |
| `CommandExecution/Core/CommandExecutionEngine.cs` | 跨项目复制（P0） |
| `CommandExecution/ECS/ClickSetGetSystems.cs` | 绕过统一查找链（P0）+ 三处重复骨架 |
| `Ports.cs` | SplitterDistance 重复计算、ToCanonicalProps/BuildPropertyDict 重复映射 |
| `WinForms/Applier/WinFormApplier.cs` | EntityIdCache 重复、BuildPropertyDict 重复 |
| `WinForms/Adapter/WinFormsUIAdapter.cs` | EntityIdCache 重复、IControlMap/UIChange 职责漂移 |
| `WinForms/Finder/ControlFinder.cs` | 查找方法冗余、DirectChildren 特判 |
| `WinForms/Finder/ControlRegistryAwareFinder.cs` | 多层转发、依赖 EcsConfigContainer 根 Entity 导致异常 |
| `WinForms/Services/EntityTreeService.cs` | 与 EcsEntityTreeWalker 功能重叠 |
| `ControlLookup/EcsEntityTreeWalker.cs` | 与 EntityTreeService 功能重叠 |
| `ControlLookup/Container/EcsContainerCore.cs` | 桩代码 / 死代码（恒 null / Invalid） |
| `ControlLookup/Container/EcsConfigContainer.cs` | GetRootEntity 恒 Invalid → 运行时异常 |
| `Initial/RenderPipeline/UIReconciler.cs` | 结构变更全量回退、_initialized 提前置位 |
| `CommandExecution/Ports/CommandInputPort.cs` | ExecuteSingle 空 catch 静默失败 |
| `ControlInteraction/CompositeValueWriter.cs` | 与 WinFormsControlValueWriter 职责重叠 |
| `ControlInteraction/EventBinding/EventBindingSystem.cs` | 与 EventHub 事件触发职责不清 |

---

## 六、2026-07-31 二次深度审查补充

> **审查日期**：2026-07-31（当日二次审查）
> **审查方法**：对目录内 100+ 源文件逐文件静态阅读，核验上文报告结论与当前代码的实际一致性。
> **重要说明**：本目录在首次报告后经历了密集迭代修复（代码注释中大量 `★★ 2026-07-31` 修复标记），**部分首次报告的结论已被后续重构修复或所引用文件已不存在**，本节逐条更新。

### 6.1 首次报告中已过时 / 已修复的结论

| 原结论 | 当前代码状态（2026-07-31 二次审查） |
|---|---|
| `EcsContainerCore.GetRootEntity()` 恒返 Invalid | ✅ 已修复：`GetRootEntity` 已被删除（注释「2026-07-31 死代码清理」），`FindControlByName`/`FindControl<T>`/`FindSubContainer` 已委托 `ControlPool` 功能实现 |
| `EcsConfigContainer.GetRootEntity()` 恒返 Invalid 引发运行时异常 | ✅ 已修复：`EcsConfigContainer.cs` 中已无 `GetRootEntity` 方法；`EntityTreeService.FindByName<T>` 对无效 rootEntity 改为返回 null 不再抛异常 |
| `EcsContainerCore.ControlPool` 恒 return null | ✅ 已修复：委托 `RenderPipelineComposition.ControlPool` |
| `CommandExecution/Core/CommandExecutionEngine.cs`（FileDriven 执行引擎门面） | ❌ 文件不存在：`CommandExecution/Core/` 下仅 `TemplatePreprocessSkeleton.cs` |
| `ControlLookup/EcsEntityTreeWalker.cs`（Entity 树 BFS 遍历） | ❌ 文件不存在：`ControlLookup/` 下仅 `SplitterPanelRegistry.cs` 与 `Container/` |
| `CommandExecution/EventHub.cs`（事件中心形同虚设） | ❌ 文件不存在 |
| `ControlInteraction/CompositeValueWriter.cs` 与 WinFormsControlValueWriter 职责重叠 | ⚠️ 部分保留：两者职责已通过接口注释区隔（Composite = 组合优先级链；WinForms 单一实现），但 `RegisterAdapter` 语义混乱仍存在（见 6.2 §9） |
| `UIReconciler` 结构变更全量回退 | ✅ 保留但需更新：`UIReconciler` 的差异比较逻辑在主流程中**已不再被调用**（见 6.2 §2） |

### 6.2 二次审查新发现的问题

#### ① 【P0 错误逻辑】`ControlStrategyRegistry.Create` 抛异常 vs 返回 null 语义矛盾

```csharp
// ControlStrategyRegistry.cs
public static object? Create(string controlType)
{
    ...
    // 1. 优先查完整策略
    if (_strategyMap.TryGetValue(...)) return strategy.Create();
    // 2. 再查简单工厂
    if (_factoryMap.TryGetValue(...)) return factory();
    throw new InvalidOperationException(...);   // ← 签名可空却抛异常
}
```

- 调用方 `WinFormsOutputPort.CreateControl`（Ports.cs L603）：`var control = ControlStrategyRegistry.Create(controlType); if (control == null) return;` —— **防御分支是死代码**，未注册类型直接抛异常终止管线。
- 调用方 `WinFormApplier.ApplyControls`（L181）：`var rawControl = ControlStrategyRegistry.Create(desc.Type); if (rawControl == null)` —— 同样死代码。
- **建议**：未注册时返回 null（由调用方决定降级），或将签名改为 `object` 非可空并去掉调用方的 null 防御。

#### ② 【P1 冗余】`UIReconciler` 已从主流程退役（267 行死代码）

- `RenderPipelineComposition.Create` 创建了 `UIReconciler`（L183），但：
  - `RecalculateLayout` 走 `ExecuteRenderStage()` 全量渲染，不经过 `UIReconciler.Apply()`
  - `RenderAuditPort.CollectDiffStats` 读取 `reconciler.LastAppliedState`，但 reconciler 从未被实际调用 Apply —— 审计数据恒为「尚未应用任何状态」
- **建议**：从 `RenderPipelineComposition` 中移除该依赖，或将其声明为仅用于审计的属性增量器。

#### ③ 【P1 错误逻辑】Scope 清理不完整（Entity 销毁但注册表悬挂）

```csharp
// EcsGate.cs DoLoadConfig / DoClearScope
_composition.DestroyScopeEntities(op.Scope);   // 只销毁 Entity
```

- `RenderPipelineComposition.DestroyScopeEntities` 只调 `_entityManager.DestroyEntity`，**不清理**：
  - `_controlPool` 中对应 Entity↔Control 注册
  - `_splitterPanelRegistry` 中 SplitContainer↔Panel 映射
  - `SplitContainerSystem._subscribedSplitters`
  - `WinFormsOutputPort._splitterUnsubscribeActions`
- 后果：同 scope 反复加载 → 注册表/Cache 悬挂 → `SplitContainerSystem.Update()` 的 D9 快速跳过（见④）判断失效。
- **建议**：`DestroyScopeEntities` 内补全四类清理，或在 `EcsGate` 中按 scope 统一回收。

#### ④ 【P1 错误逻辑】`SplitContainerSystem.Update()` D9 提前跳过缺陷

```csharp
int expectedSplitterCount = 0;
foreach (var e in allEntities) { if (splitcontainer) expectedSplitterCount++; }
if (expectedSplitterCount > 0 && _subscribedSplitters.Count >= expectedSplitterCount)
    return;   // ← 提前跳过
```

- `_subscribedSplitters` 只增不减（无 Unsubscribe/回收路径）。若 SplitContainer Entity 被 `DestroyScopeEntities` 销毁而集合未清理，`Count` 将持续大于实际数量 → 后续新添加的 SplitContainer **永远不会被订阅**。
- **建议**：订阅集合改为与 Entity 生命周期绑定（Entity 销毁时移除），或移除该快速跳过优化。

#### ⑤ 【P1 错误逻辑】`EventBindingSystem.GetTargetForm()` 未按注释筛选主窗体

```csharp
// 注释: 优先返回非 disposed 且非隐藏的主窗体
for (int i = 0; i < Application.OpenForms.Count; i++)
{
    var form = Application.OpenForms[i];
    if (form != null && !form.IsDisposed)
        return form;   // ← 直接返回第一个，无「非隐藏/主窗体」筛选
}
```

- 多窗体场景下事件会绑定到**错误**的窗体。
- **建议**：补充 `form.Visible` 筛选并优先匹配 `Application.OpenForms[0]` 或显式指定的主窗体。

#### ⑥ 【P1 冗余】`EnqueueExecutionWithResult` / `ExecuteNext` 名不副实

```csharp
// Ports.cs
public Entity EnqueueExecutionWithResult(...) => EnqueueExecution(...);   // 纯转发

// CommandInputPort.cs
public bool ExecuteNext() { ExecuteAll(); return ...; }   // 名称误导，实际执行全部
```

- 两处注释均承认「仅为向后兼容」，但保留在多处被调用的公开 API 面。
- **建议**：标注 `[Obsolete]` 并规划删除窗口；`ExecuteNext` 改为 `HasPendingCommands()`。

#### ⑦ 【P2 重复】SplitterPanel 面板侧判断逻辑重复 5 处

「`"Panel1"`/`"Panel2"` 三元判断」出现于：
1. `SplitterPanelHelper.ResolveSplitterParent`（L35）
2. `WinFormsControlAdapter.GetSplitterPanel`（L240-244）
3. `WinFormApplier.ApplyRelations`（L268-273）
4. `WinFormApplier.EnsureSplitterPanelIdentity`（L239-240）
5. `LayoutSystem.TryComputeSplitterPanelLayout`（L322-323）

- **建议**：在 `SplitterPanelHelper` 增 `GetPanelSide(bool isPanel1)` / `TryParsePanelSide(string)` 统一收敛。

#### ⑧ 【P2 重复】`AddOrSetComponent` 模式 3 处手写

- `ConfigDrivenControlFactory.ConvertControl`（L231-238）：手写 Has/Set/Add 三分支
- `LayoutSystem.TryComputeEntityLayout`（L199-206）：手写 Has/Set/Add
- `LayoutSystem.AddOrSetContainerSize`（L389-416）：手写 Has/Set/Add
- 项目已有 `EntityManagerExtensions.AddOrSetComponent<T>` 扩展方法。
- **建议**：统一替换为扩展方法。

#### ⑨ 【P2 冗余】`CompositeValueWriter.RegisterAdapter` 语义混乱

```csharp
// CompositeValueWriter.cs
public void RegisterAdapter(Type controlType, Func<object, object?> getter, Action<object, object?> setter)
{
    if (_writers.Count == 0) return;
    _writers[_writers.Count - 1].RegisterAdapter(...);   // 只注册到末位 writer
}
```

- `GetValue/SetValue/IsSupported` 按**添加顺序从前往后**尝试；`RegisterAdapter` 却写入**末位** writer —— 两套语义互相独立且易混淆（注释也用 D5 澄清说明是历史包袱）。
- **建议**：`RegisterAdapter` 改为广播到所有 writer，或提供 `RegisterToWriter(index/writer)` 显式 API。

#### ⑩ 【P2 错误】命名空间错位未归位完全（其余 7 个文件）

首次报告已指出 `ContractBuilder`/`ContractRegistry` 命名空间错位（已修复）。但**相同问题仍存在于**：

| 文件 | 当前命名空间 | 期望 |
|---|---|---|
| `Shared/ContractValidator.cs` | `FileDriven.Pipeline.Contracts` | `FrontEndDriven.Shared` |
| `Shared/ControlConfigTarget.cs` | `FileDriven.Pipeline.Contracts` | `FrontEndDriven.Shared` |
| `Shared/ConfigLoadInput.cs` | `FileDriven.Pipeline.Core` | `FrontEndDriven.Shared` |
| `Shared/JsonProcessingOptions.cs` | `FileDriven.JsonRefer.JsonProcessing` | `FrontEndDriven.Shared` |
| `Shared/Utils/PathMananger.cs` | `FileDriven.Abstractions` | 归属 FileDriven 项目 |
| `Shared/Utils/JsonNodeExtensions.cs` | `FileDriven.Utilities.Extensions` | 归属 FileDriven 项目 |
| `Shared/Abstractions/*.cs` | `FileDrivenFramework.Abstractions` | 归属 FileDriven 项目 |
| `Shared/Diagnostics/PipelineLog.cs` | `FileDriven.Pipeline.Diagnostics` | 归属 FileDriven 项目 |

- **建议**：按首次报告 P0 方案继续归位/移交。

---

## 七、2026-07-31 已实施重构记录

> **重构日期**：2026-07-31（二次审查同日）
> **实施范围**：按「六、二次深度审查补充」优先级 P0 → P2 落地
> **验证**：`FrontEndDriven.csproj` + `MainProj.sln` 全量构建 0 警告 0 错误

### 7.1 已修复项

| 问题编号 | 问题 | 修复文件 | 修复方式 |
|---|---|---|---|
| ① P0 | `ControlStrategyRegistry.Create` 抛异常 vs 返回 null 语义矛盾 | `ControlRender/Creation/ControlStrategyRegistry.cs` | 未注册时返回 null；调用方既有 null 防御分支恢复有效性 |
| ③ P0 | Scope 清理不完整（注册表悬挂） | `Ports.cs` + `ControlRender/Systems/SplitContainerSystem.cs` + `Initial/RenderPipeline/RenderPipelineComposition.cs` | 新增 `WinFormsOutputPort.UnsubscribeSplitterMoved(Entity)`、`SplitContainerSystem.RemoveSubscription(Entity)`；`DestroyScopeEntities` 销毁前统一回收注册表/订阅 |
| ④ P0 | `SplitContainerSystem.Update()` D9 提前跳过缺陷 | `ControlRender/Systems/SplitContainerSystem.cs` | 移除不可靠的 expectedSplitterCount 快速跳过，保留 Contains 去重遍历 |
| ⑤ P0 | `EventBindingSystem.GetTargetForm()` 未按注释筛选主窗体 | `ControlInteraction/EventBinding/EventBindingSystem.cs` | 「可见 → 非可见」两级优先级筛选 |
| ② P1 | `UIReconciler` 悬空实例（Composition 创建未使用） | `Initial/RenderPipeline/RenderPipelineComposition.cs` | 移除悬空 `new UIReconciler(...)`；类保留作为审计 API |
| ⑥ P1 | `EnqueueExecutionWithResult` / `ExecuteNext` 名不副实 | `Ports.cs` + `CommandExecution/Ports/CommandInputPort.cs` + `Gate/EcsGate.cs` | 两者标注 `[Obsolete]`；`EcsGate` 调用点改为 `EnqueueExecution`；新增 `HasPendingCommands()` 替代语义 |
| ⑧ P2 | `AddOrSetComponent` 模式 3 处手写 | `ControlRender/Creation/ConfigDrivenControlFactory.cs` + `ControlRender/Systems/LayoutSystem.cs` | 统一替换为 `EntityManagerExtensions.AddOrSetComponent<T>`；`AddOrSetContainerSize` 保留"值未变不写回"优化语义 |

### 7.2 未处理项（降级为后续迭代）

| 问题编号 | 问题 | 原因 |
|---|---|---|
| ⑦ P2 | SplitterPanel 面板侧判断 5 处重复 | 需先在 `SplitterPanelHelper` 新增统一 API，涉及跨 5 文件调用点批量替换，风险较高，建议单独迭代 |
| ⑨ P2 | `CompositeValueWriter.RegisterAdapter` 语义混乱 | 行为变更（广播 vs 末位）可能影响外部 writer 注册顺序假设，需业务侧确认 |
| ⑩ P2 | 命名空间错位 7 个文件 | 跨项目移动/改名空间涉及 csproj 引用面与反射 `Type.GetType` 调用方，需单独治理方案 |
| 3.1 首报 P0 | `ClickSetGetSystems` 绕过统一查找链 | 需配合 `ControlFinder.FindAcrossOpenForms` 改造三系统，涉及命令语义验证 |
| 3.3 首报 P2 | `ExecuteSingle` 空 catch 静默失败 | 注释已说明修复意图，但 catch 补充日志需与 `ExecutionLog` 的日志键（fde: 前缀）对齐，待确认日志约定 |
