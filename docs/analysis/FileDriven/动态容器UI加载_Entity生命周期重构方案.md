# 动态容器 UI 加载 — Entity 生命周期框架缺陷分析与重构方案

> **日期**：2026-08-01
> **现象**：`math_functions_test.json` 脚本加载 `MathFunctions.unified.json` 到动态容器后，前 3 组运算正常，第 4 组 `set valueATextBox = "3.14"` 时抛异常中断。
> **错误**：`Pipeline execution failed: Invalid entity: Entity(23:0) (Parameter 'entity')`
> **性质**：框架设计缺陷（Entity 生命周期与其他层引用的同步缺失），需框架层重构修复。

---

## 一、问题现象（日志还原）

### 1.1 关键日志时间线

```
[20:06:04.600] [FrontEndDriven] EcsInputPort: UI 配置加载完成, rootEntityId=23        ← ConfigSelector.json (scope=Content)
[20:06:04.877] [FrontEndDriven] RenderPipelineComposition: 执行完整管线 — scope=Content  ← LoadConfigToTarget → MathFunctions.unified.json
[20:06:04.880] [FrontEndDriven] EcsInputPort: UI 配置加载完成, rootEntityId=23           ← ID 复用：旧 23:0 销毁，新 23:1
[20:06:05.421] [ECHO] 加法运算测试 (10 + 25)        ← 第 1 组：正常（set/click/get）
[20:06:05.741] [ECHO] 加法运算测试 (100 + 200)      ← 第 2 组：正常
[20:06:06.042] [ECHO] 加法边界测试 (-50 + 30)       ← 第 3 组：正常
[20:06:06.344] [ECHO] 加法小数测试 (3.14 + 2.86)    ← 第 4 组：ECHO 后 2ms 立即失败
[20:06:06.346] [ERROR] Execute 失败: Pipeline execution failed: Invalid entity: Entity(23:0) (Parameter 'entity')
[20:06:06.363] [INFO] GUI 模式脚本执行完成: success=true, totalCommands=0    ← 脚本被中断，剩余命令未执行
```

### 1.2 现象要点

| 要点 | 说明 |
|------|------|
| 错误 Entity | `Entity(23:0)` — **ConfigSelector.json 的 root Entity**（启动时加载，scope=Content） |
| 首次加载 | `MainLayout` → `ConfigSelector`（rootEntityId=23，version 0） |
| 二次加载 | `MathFunctions.unified.json` 到 `contentContainer`（scope=Content）时，`DestroyScopeEntities("Content")` 销毁了 23:0 |
| ID 复用 | 销毁后 ID 23 被回收，新 UI root 复用为 `Entity(23:1)`（version 递增） |
| 时间特征 | 第 4 组 ECHO 后 2ms 立即失败 → 异常发生在 `set` 命令执行的**第一步** |

---

## 二、根因分析

### 2.1 调用链定位

```
脚本: set valueATextBox = "3.14"
  └─ CommandInputPort (LoadMetaCommands → ECS Entity)
      └─ CommandSystemCoordinator.Update()
          └─ SetSystem.Execute(entity, meta)
              └─ PerformDirectSet("valueATextBox", "3.14")        ← 直接操作 WinForms 控件
                  └─ textBox.Text = "3.14"                         ← 触发 TextChanged 事件
                      └─ [事件处理器] → 访问 Entity(23:0) 的 Component
                          └─ EntityManager.GetComponent / AddComponent
                              └─ ArchetypeManager.IsEntityValid(Entity(23:0))
                                  └─ _entityArchetype.ContainsKey → FALSE
                                      └─ throw ArgumentException("Invalid entity: Entity(23:0)")
```

### 2.2 根因：Entity 生命周期与「控件事件委托」的引用脱节

**核心缺陷**：`EcsGate.DoLoadConfig` 加载新 UI 到动态容器时，`DestroyScopeEntities(scope)` 只能销毁 **ECS 侧** 的 Entity，但 **WinForms 控件树及其事件委托** 中已捕获的旧 Entity 引用无法被框架层自动追踪清理。

具体断点：

```csharp
// EcsGate.cs — DoLoadConfig
private bool DoLoadConfig(LoadConfigOperation op)
{
    // ① 销毁旧 scope 的 Entity（ECS 侧清理）
    _composition.DestroyScopeEntities(op.Scope);

    // ② 加载新 UI（创建新 Entity 树）
    _composition.Execute(op.Config, op.HostControl, op.Scope);
    return true;
}
```

- **① 只清理 ECS World 侧**：Entity 从 `_entityArchetype` 移除、version 递增、ID 回收
- **② 创建新 Entity 树**：ID 可能被复用（23:1），但**控件查找/事件委托/外部索引中残留的 `23:0` 引用未被识别和刷新**

### 2.3 为什么前 3 组测试通过，第 4 组才失败？

| 阶段 | 行为 | 结果 |
|------|------|------|
| 前 3 组 | `set valueATextBox = "10"/"100"/"-50"` — `ControlFinder.FindByName` 命中**新控件**，触发新 TextChanged | 正常（可能命中新 Entity 或旧控件已被覆盖） |
| 第 4 组 | `set valueATextBox = "3.14"` — 此时某条**残留事件委托/引用链**持有旧 `Entity(23:0)` | 崩溃 |

> **推断**：第 4 组的 `set` 写入触发了一个持有旧 Entity 引用的回调路径（例如旧 ConfigSelector 控件的事件委托未被完整卸载，或 ControlPool/EventBindingSystem 局部缓存中残留旧 Entity）。该路径在 `valueATextBox.Text` 变化时被触发 → 访问已销毁的 `23:0` → 崩溃。
>
> *（注：第 4 组为小数输入，与 -50 的差异仅在字符串格式，因此更可能是累积状态触发，而非值内容本身导致。）*

### 2.4 框架设计缺陷清单

#### 缺陷 1：Entity 销毁缺少框架级「引用追踪-失效广播」机制 [核心]

`EntityManager.DestroyEntity()` 仅完成存储层清理（移除 archetype 记录、回收 ID、version 递增），**没有框架级机制通知所有持有该 Entity 引用的外部层**：

| 持有者 | 现状 | 风险 |
|--------|------|------|
| ControlPool | 通过 `EntityDestroying` 事件清理 entity↔control 映射（已实现） | ✅ 基本覆盖 |
| EventBindingSystem / 事件委托 | **事件委托闭包中捕获的 Entity 无法自动失效** | ❌ **根因** |
| CommandExecution 命令 Entity | 命令 Entity 不随 scope 销毁，但 `SourceEntityId` 可能引用旧控件 | ⚠️ 悬空引用 |
| WinForms 控件树 | `WinFormApplier` 在下次 Apply 覆盖，但**未物理移除旧控件 + 卸载事件** | ⚠️ 残留触发 |

#### 缺陷 2：Entity ID 复用导致「同 ID 不同代」难以区分

`TakeFreeEntityId` 复用被销毁的 ID，version 递增。但框架中多处通过 `Entity.ToString()`（如 `Entity(23:0)`）做**字符串键**存储（ControlPool 索引、EventBinding 声明、日志），一旦旧字符串引用未被清理，会与新实体 Key 冲突或指向已失效实体。

#### 缺陷 3：`DestroyScopeEntities` 只清 ECS，不负责 WinForms 物理清理

`RenderStageRunner.DestroyScopeEntities` 做了三方回收（SplitterPanelRegistry、SplitterMoved 订阅、SplitContainerSystem 订阅），**但没有统一回收 WinForms 控件树**（从父容器移除控件、解除全部事件订阅），导致旧控件的事件处理器仍可能被新控件的同名查找触发。

#### 缺陷 4：命令执行链路与 UI 生命周期无关联

`CommandInputPort` 创建的命令 Entity（`CommandMetaComponent`）不与任何 scope 绑定，`set`/`click` 通过 `ControlFinder.FindByName` 全局查找控件，**不校验控件所属 scope 是否仍有效**，也不在控件重建后刷新引用。

---

## 三、重构方案

### 方案 A（推荐）：引入框架级「实体失效感知」机制（EntityLifetimeBridge）

在框架层建立统一的 **Entity 生命周期桥接服务**，使所有持有 Entity 引用的组件在实体销毁时获得通知并可自愈。

#### 3.1 新增 `EntityLifetimeBridge`（框架层服务）

```csharp
/// <summary>
/// Entity 生命周期桥接 — 框架层统一追踪「外部层对 Entity 的引用」，
/// 在 Entity 销毁时广播失效通知，供各持有者自愈（刷新/卸载/忽略）。
/// 由 RenderPipelineComposition 装配时创建，注入所有需要感知生命周期的系统。
/// </summary>
public sealed class EntityLifetimeBridge
{
    private readonly EntityManager _entityManager;
    // 监控点：控件名 → 最近一次有效 Entity（按代刷新）
    private readonly ConcurrentDictionary<string, Entity> _lastKnownEntityByName = new();

    // Entity 销毁时：对外广播（事件委托、命令系统、EventBinding 等订阅）
    public event Action<Entity>? EntityInvalidated;

    public EntityLifetimeBridge(EntityManager entityManager)
    {
        _entityManager = entityManager;
        _entityManager.EntityDestroyed += OnEntityDestroyed;
    }

    private void OnEntityDestroyed(Entity entity)
    {
        EntityInvalidated?.Invoke(entity);

        // 清除名称→Entity 索引中指向已销毁代的条目
        foreach (var kv in _lastKnownEntityByName)
        {
            if (kv.Value == entity)  // 值相等 → 同 ID 同代 → 失效
                _lastKnownEntityByName.TryRemove(kv.Key, out _);
        }
    }

    /// <summary>注册/更新「控件名 → Entity」的最新代引用</summary>
    public void RegisterEntityBinding(string controlName, Entity entity)
        => _lastKnownEntityByName[controlName] = entity;

    /// <summary>按控件名解析当前有效的 Entity（自动跳过已销毁代）</summary>
    public bool TryResolveEntity(string controlName, out Entity entity)
    {
        if (_lastKnownEntityByName.TryGetValue(controlName, out entity)
            && _entityManager.IsEntityValid(entity))
            return true;
        entity = Entity.Invalid;
        return false;
    }
}
```

#### 3.2 各层接入点

| 层 | 接入方式 | 效果 |
|----|----------|------|
| **EventBindingSystem / Orchestrator** | 创建事件委托时，不直接闭包捕获 `Entity`；改为捕获 `controlName`，触发时通过 `EntityLifetimeBridge.TryResolveEntity(controlName, out entity)` **按名解析当前代 Entity** | 旧 Entity 失效后自动解析到新代；解析失败则静默忽略 |
| **EventBindingSystem.Update** | 收集 Pending Entities 前，过滤 `!IsEntityValid` 的 Entity（跳过已销毁的） | 避免对已销毁 Entity 执行 GetComponent |
| **ControlPool** | `Register` 时同时调用 `bridge.RegisterEntityBinding(controlName, entity)` | 建立「名称→当前代」索引 |
| **ClickSetGetSystems** | `PerformDirectSet/Click/Get` 保留 WinForms 直接操作（正确路径），但**触发前通过 bridge 校验控件所属 Entity 是否为本 scope 有效实体**；无效则跳过 | 防止跨 scope 误触旧控件 |
| **CommandInputPort** | 命令 Entity 增加 `Scope` 属性；`LoadMetaCommands` 时绑定当前 scope；scope 销毁时清理该 scope 的 Pending 命令 | 命令生命周期与 UI scope 对齐 |

#### 3.3 核心收益

1. **消除悬空引用**：所有对 Entity 的访问统一经 `IsEntityValid`（或 bridge 解析）校验，不再直接使用捕获的旧值
2. **按名解析自愈**：控件重建后（ID 复用），事件委托按 `controlName` 自动解析到新代 Entity
3. **scope 感知**：命令系统与 UI scope 绑定，旧 scope 销毁时自动清理对应命令

---

### 方案 B（轻量）：`DestroyScopeEntities` 增强 — 物理清理 WinForms + 统一卸载

在现有 `RenderStageRunner.DestroyScopeEntities` 中补充：

```csharp
public void DestroyScopeEntities(string scopeName)
{
    // ... 现有三方回收（SplitterPanel/事件订阅/订阅集合）...

    // 【新增】① 物理移除 WinForms 控件树 + 卸载事件
    var scopeControls = _controlPool.GetAllEntities()
        .Where(e => _entityManager.TryGetComponent<ScopeComponent>(e, out var s) && s.Scope == scopeName)
        .Select(e => _controlPool.GetControl(e))
        .Where(c => c != null)
        .ToList();

    foreach (var control in scopeControls)
    {
        control.Parent?.Controls.Remove(control);
        control.Dispose();   // Dispose 会解除事件订阅（标准 WinForms 行为）
    }

    // 【新增】② EventBindingSystem 卸载该 scope 的委托订阅
    _eventBindingSystem.UnbindScope(scopeName);

    // 【新增】③ 命令系统清理该 scope 的 Pending 命令
    _commandInputPort.ClearScopeCommands(scopeName);

    // ... 现有 Entity 销毁 ...
}
```

---

### 方案 C（最小改动）：在 `set/click/get` 执行链中增加「实体有效性 + scope 匹配」双保险

```csharp
// SetSystem.PerformDirectSet 前置校验（最小侵入）
private static bool PerformDirectSet(string controlName, string value)
{
    var control = ControlFinder.FindByName<Control>(controlName);
    if (control == null) return false;

    // 【新增】仅操作「当前活动 scope」内的控件
    // 通过 ControlPool 校验该控件对应的 Entity 是否有效
    var pool = AppServiceProvider.Resolve<ECS.Bridge.ControlPool>();
    var entity = pool?.GetEntity(controlName) ?? Entity.Invalid;
    if (entity.IsValid && !entityManager.IsEntityValid(entity))
    {
        // 控件对应的 Entity 已销毁（旧代残留）→ 跳过，避免触发悬空事件
        return false;
    }
    // ... 原有写入逻辑 ...
}
```

---

## 四、推荐路径与实施步骤

### 推荐：方案 A + 方案 B 组合

- **方案 A** 解决根本性「引用失效感知」问题（长期正确性）
- **方案 B** 解决「旧控件/事件残留物理清理」问题（短期止血，防止残留触发）

### 实施步骤（建议顺序）

| 步骤 | 内容 | 涉及文件 |
|------|------|----------|
| **① 止血** | 方案 B：`DestroyScopeEntities` 增加 WinForms 控件树物理移除 + 事件卸载 | `RenderStageRunner.cs` |
| **② 防御** | 方案 B-②③：`EventBindingSystem.UnbindScope`、`CommandInputPort.ClearScopeCommands` | `EventBindingSystem.cs`、`CommandInputPort.cs` |
| **③ 根治** | 方案 A：新增 `EntityLifetimeBridge`，Register 装配，EventBinding 委托改按名解析 | 新建 `EntityLifetimeBridge.cs`、`PipelineCompositionFactory.cs`、`EventBindingSystem.cs` |
| **④ 加固** | 方案 C：`set/click/get` 前置有效性检查 | `ClickSetGetSystems.cs` |
| **⑤ 验证** | 复跑 `math_functions_test.json`，确认 47 条命令全部执行、totalCommands=47 > 0 | — |

---

## 五、相关公理追溯

| 公理 | 关联 |
|------|------|
| `[AX] StateSnapshot` | 任何操作基于快照必须确定性；对已销毁 Entity 的操作违反该公理 |
| `[AX-CORE] SyncProtocolTriple` | CommitPhase 要求逻辑原子性；控件重建时旧引用未清理导致状态不一致 |
| `[BAX-D] Discrete-Countability` | Entity ID 回收复用依赖 version 区分代际；字符串索引未携带代际导致混淆 |
| `[AX] ScopeGuard` | 命令应仅作用于当前 scope；跨 scope 残留引用违反作用域约束 |

---

## 六、附录

### 6.1 涉及的关键代码位置

| 文件 | 关键片段 |
|------|----------|
| `CsharpPrj/Operation/FrontEndDriven/Gate/EcsGate.cs` | `DoLoadConfig` → `DestroyScopeEntities` + `Execute` |
| `CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderStageRunner.cs` | `DestroyScopeEntities`（三方回收，缺 WinForms 物理清理） |
| `CsharpPrj/BasicMethod/ECS/ECSFrame.cs` | `EntityManager.GetComponent/AddComponent` 的 `IsEntityValid` 检查（抛异常点） |
| `CsharpPrj/BasicMethod/ECS/Archetype/ArchetypeManager.cs` | `IsEntityValid`（`_entityArchetype.ContainsKey` + version 校验） |
| `CsharpPrj/BasicMethod/ECS/Bridge/ControlPool.cs` | `OnEntityDestroying` → `Unregister`（已实现 entity→control 清理） |
| `CsharpPrj/Operation/FrontEndDriven/ControlInteraction/EventBinding/EventBindingSystem.cs` | `CollectPendingEntities` / `BuildDeclarations`（持有 Entity 引用） |
| `CsharpPrj/Operation/FrontEndDriven/CommandExecution/ECS/ClickSetGetSystems.cs` | `PerformDirectSet/Click/Get`（WinForms 直操作，未校验 Entity 生命周期） |