# StateHoldDriven 状态管理中心（State Management Center）— 框架级架构设计与实施计划

> **状态**：实施中（P0–P6 代码已完成，编译/单测/结构红线验收通过；GUI 运行验收需真实显示环境执行） | **日期**：2026-08-05
> **目标目录**：`CsharpPrj/Operation/StateHoldDriven`（升级为状态管理中心）
> **关联文档**：
> - `docs/analysis/2026/8.5/宿主挂载链路_重复冗余错误逻辑分析.md`（P7 挂载链）
> - `docs/analysis/2026/8.4/FrontEndDriven_RenderPipeline_MultiScope_Defect.md`（缺陷1-5）
> - `docs/analysis/2026/8.4/FrontEndDriven_ScopeRefactor_StateSummary.md`（S4 现状）
> - `docs/analysis/2026/8/通用状态管理框架_基于InternalStorage扩展设计.md`（存储基座）
> - `docs/KnowLedge/状态与演化/状态的再演进：从模块化格局到函数式-Actor-范畴论的三元融合.md`（愿景）
> **上一轮分析结论**：`mainlayout_run_ui.json` 中「加载侧边栏后再加载其他 json 失败」= 容器生命周期管理缺陷（L1–L5 多层叠加），见本文件 §一。

---

## 〇、实施导航（给下一个对话）

### 0.1 阅读顺序

1. §一（要解决的问题 L1–L5）→ §二（现状碎片）→ §三（目标架构）→ §四（类型签名）→ §五（收敛映射）；
2. §六（文件清单）→ §七（分阶段计划 P0–P6，**按阶段推进，每阶段编译+验证后再进下一阶段**）；
3. §八（验收）→ §九（风险）→ §十（现状代码锚点速查）。

### 0.2 实施红线（违反即返工）

| # | 红线 | 说明 |
|---|------|------|
| R1 | **契约只放 `Share.StateManagement`** | `IStateCenter`/`IStateTransaction`/`IStateSubscriptionHub`/生命周期契约一律在 Share 层定义，禁止下沉到 Operation 层或 FrontEndDriven |
| R2 | **StateHoldDriven 不引用 FrontEndDriven** | StateHoldDriven（Operation 层）保持 UI/ECS 类型无关；FrontEndDriven.State 依赖 StateHoldDriven（单向），禁止反向 |
| R3 | **composition 生命周期唯一归状态中心** | `RenderPipelineComposition.Dispose()` 只允许在 `ICompositionLifecycle.AppShutdown()` 路径触发；`MainForm.Dispose`/`GuiFormRunner.Dispose` 一律改为"解绑视图"，**禁止 Dispose composition** |
| R4 | **Load/Destroy 一律经事务** | `IStateTransaction` 包裹「销毁→加载→挂载→状态提交」；事务未 Commit 前中间态（Loading/半挂载）不得发布到订阅者 |
| R5 | **Mounted 唯一写入点** | `IContainerLifecycle.MarkMounted` 是全库唯一写挂载状态的入口；现有 `EcsGate.DoLoadAndRender`/`RenderStageRunner.DestroyScopeEntities`/`WinFormApplier` 中的挂载状态写改走该入口 |
| R6 | **幂等短路只在同 ConfigVersion 生效** | 不同 ConfigPath 或 ConfigVersion 变更必须走完整事务，禁止"同路径直接 return true"的旧短路 |

### 0.3 命名空间约定（对齐现有基座）

- 契约：`namespace Share.StateManagement`（与 `IStateManager`/`IStateOrchestrator`/`IStateActor` 一致）。
- 历史遗留：`StateChangedEventArgs`/`StateChangeType` 当前在 `namespace Modules.StateManagement`（`Share/StateManagement/StateStorage/Abstractions/StateChangedEventArgs.cs`），新契约用 `using Modules.StateManagement;` 复用，**不重复定义**。
- 实现：`namespace Operation.StateBlock.Core` / `Operation.StateBlock.Lifecycle` / `Operation.StateBlock.Pipeline`（沿用 StateHoldDriven 现有 RootNamespace `Operation.StateBlock`）。

---

## 一、背景：待解决的框架级缺陷（L1–L5）

`mainlayout_run_ui.json` 运行现象：加载侧边栏（MathFunctions → sideContainer）后，再加载其他 json 失败。根因为**容器生命周期管理缺陷**，拆为 5 层：

| # | 缺陷 | 证据位置（现状锚点见 §十） | 本方案对策 |
|---|------|--------------------------|-----------|
| L1 | 共享 `RenderPipelineComposition` 生命周期与 `MainForm` 强耦合：窗体 Dispose → `EcsGate.Dispose()` → `_composition.Dispose()`（内部 `Clear()` 全局清空 ControlPool+EntityManager）→ 之后一切 load 抛 `ObjectDisposedException: Cannot access a disposed object. Object name: 'RenderPipelineComposition'` | `MainForm.cs:157-161`、`EcsGate.cs:213-218`、`RenderPipelineComposition.cs:232-239`、`ScriptExecutionPort.cs:162/285/301` | `ICompositionLifecycle` 应用级持有，进程级唯一释放点（R3） |
| L2 | 「先销毁作用域→再加载」时序下 `Mounted` 写入点断链：`WinFormApplier.ApplyRelations` 已移除 `MarkMounted`（S4），唯一写入点是 `EcsGate.DoLoadAndRender` 的 `HostActualChildCount>0` 判定；挂载时相错过 → `HostActualChildCount=0` → 不置 Mounted → `ContainerCommandExecutor` 以 `NodeCount==0` 判 Failed | `EcsGate.cs:152-186`、`RenderPipelineStages.cs:283-314`、`ContainerCommandExecutor.cs:44-62` | `IScopeLifecycle.LoadAsync` 事务化；`MarkMounted` 唯一写入点（R5） |
| L3 | `ContainerStateStore` 状态残留 + 幂等短路：同 ConfigPath 且非 Failed 直接 return true（假成功）；Failed/旧 NodeCount 不随新加载清理 | `ContainerCommandExecutor.cs:26-32`、`ContainerState.cs` | 事务回滚原子清残留；幂等短路只在同 ConfigVersion 生效（R6） |
| L4 | 函数执行事件 scope 为空 → `FunctionExecutedRenderBridge` 触发 `ExecuteRenderStage("")` 全量重渲染，与投影渲染毫秒级叠加竞态 | `FunctionExecutedRenderBridge.cs:77-88`、`EventPlatform.cs:171-172`、`ClickSetGetSystems.cs` | `StateSubscriptionHub` scope 串行投递 + 版本去重；投影器只读事务提交后的最终快照 |
| L5 | 宿主寻址口径分裂：`validate`（可见树）vs `assert-container`/`validate-mount`（Registry+ControlPool），`validate-mount` 的 `ResolveInHost` 缺 hostKey 反查 | `ContainerAssertionService.cs:85-162`、`ContainerAccessPortAdapter.cs:24-35` | `IContainerLifecycle` 唯一寻址（scope 优先 + hostKey 反查兜底），断言/校验/挂载全经该出口 |

> **判定**：本方案不逐点打补丁，而是把「状态」从三处碎片收敛为**单一权威（状态管理中心）**，使 L1–L5 在架构层消亡。

---

## 二、现状盘点：三碎片状态管理

| 碎片 | 位置 | 实际能力 | 缺口 |
|------|------|---------|------|
| ① 初始化状态 | `CsharpPrj/Operation/StateHoldDriven`（`Core/StateStore.cs`、`Pipeline/StatePipelineBuilder.cs`、`Blocks/*`、`FunctionStorage/*`） | 仅启动期 KV 存储 + Block 管线；csproj 声称 StateActor/StateOrchestrator 但未实现 | 无状态变更通知、无 scope、无生命周期、无事务；`StateStore` 是内存 `ConcurrentDictionary`（lock 与 ConcurrentDictionary 混用），与 InternalStorage 双后端 |
| ② 共享基座 | `CsharpPrj/Share/StateManagement`（`StateManager`/`StateOrchestrator`/`StateActor`/`ObservableStateStore`/`StatePipeline`/`StateStorage/*`） | 已有 scope 管理器 `IStateManager`、Actor 编排 `IStateOrchestrator`、`ObservableStateStore`（有 StateChanged）、Stage 管线 | **半成品未接线**：全库无一处 `Resolve<IStateOrchestrator>`；`StateActor` 无变更通知（愿景文档 §6.2 已点名）；`StateManager` 用 `InMemoryStateStore` 与 InternalStorage 脱节；命名空间 `Share.StateManagement` 与 `Modules.StateManagement` 混用 |
| ③ 容器状态 | `CsharpPrj/Modules/InternalStorage/State/*` + `CsharpPrj/Operation/FrontEndDriven/State/*`（`ContainerStateStore`/`HostBindingStore`/`StateServiceCollection`/`ContainerProjector`/`ContainerCommandExecutor`） | 已有 scope→ContainerState（Status/ConfigVersion/NodeCount）与 scope↔hostKey 双向映射，基于 IStorageCell，有变更事件 | **职责越界**：`ContainerProjector` 直接命令 EcsGate 渲染，状态层与执行层耦合；状态写入分散三处（CommandExecutor 判 Failed / EcsGate 判 Mounted / StateStore 记录）；每 `new MainForm()` 重建整套（U15） |

**根因**：三碎片各持有状态的一部分，但没有一个拥有「全量状态的唯一权威」。L1–L5 全部源于此。

---
## 三、目标架构

### 3.1 分层图

```
┌────────────────────────────────────────────────────────────────────────┐
│ FileDriven.Execution（Composition Root · 进程级装配点）                  │
│   · 创建 StateCenter（应用级单例，与窗体无关）                            │
│   · 注册 FrontEndDriven 生命周期 Provider → StateCenter                  │
│   · MainForm 只"借用视图"（rootView），不"拥有引擎"                       │
├────────────────────────────────────────────────────────────────────────┤
│ StateHoldDriven（升级为状态管理中心 · Operation 层 · UI/ECS 无关）        │
│   StateCenter（门面，实现 IStateCenter）                                  │
│   ├─ Core：StateTransaction / StateSubscriptionHub /                     │
│   │         ScopeLifecycleStateMachine / StateStore（迁移 IStorageCell） │
│   ├─ Lifecycle 契约实现（注入式 Provider 容器，无 UI 类型）               │
│   ├─ StatePipeline / Blocks（保留：初始化管线）                           │
│   └─ FunctionStorage（保留，降级为工具库，不再参与状态权威）               │
├────────────────────────────────────────────────────────────────────────┤
│ Share.StateManagement（契约基座 · Layer 3 · 新增契约）                   │
│   IStateManager / IStateOrchestrator / IStateActor（已有）               │
│   IStateCenter / IStateTransaction / IStateSubscriptionHub /             │
│   IScopeLifecycle / ICompositionLifecycle / IContainerLifecycle          │
│   StateKey / StateSnapshot<T> / ContainerMountState（新增）              │
├────────────────────────────────────────────────────────────────────────┤
│ Modules.InternalStorage（存储后端 · Layer 2）                            │
│   IStorageCell（三层KV + DataChanged + TTL + 双向映射）                   │
│   ContainerStateStore / HostBindingStore（状态存储门面，保留）            │
├────────────────────────────────────────────────────────────────────────┤
│ FrontEndDriven（渲染执行器 · 被状态中心驱动）                             │
## 四、核心类型设计（完整签名）

> 契约一律 `namespace Share.StateManagement`；除注明外均为新增文件。现有 `Modules.StateManagement.StateChangedEventArgs`/`StateChangeType` 复用（`using Modules.StateManagement;`）。

### 4.1 状态模型

`Share/StateManagement/Abstractions/StateKey.cs`：

```csharp
namespace Share.StateManagement;

/// <summary>统一状态键：命名空间(域) + 作用域 + 键名。对齐 GlobalDecisionKey 轴绑定思想。</summary>
public readonly record struct StateKey(string Namespace, string Scope, string Key)
{
    public string FullKey => $"{Namespace}:{Scope}:{Key}";

    public static StateKey Container(string scope) => new("container", scope, "state");
    public static StateKey HostBinding(string scope) => new("host_binding", scope, "hostKey");
    public static StateKey Composition() => new("composition", "*", "lifecycle");
    public static StateKey ScopeLifecycle(string scope) => new("scope", scope, "lifecycle");

    public override string ToString() => FullKey;
}
```

`Share/StateManagement/Abstractions/StateSnapshot.cs`：

```csharp
namespace Share.StateManagement;

/// <summary>不可变状态快照 + 版本。版本单调递增，投影器按版本去重。</summary>
public sealed record StateSnapshot<TState>(
    TState Value,
    long Version,
    DateTime UpdatedAtUtc,
    string? Source);
```

### 4.2 原子事务

`Share/StateManagement/Abstractions/IStateTransaction.cs`：

```csharp
namespace Share.StateManagement;

public interface IStateTransaction : IDisposable
{
    string? Scope { get; }
    void Set<TState>(StateKey key, TState value);
    void Remove(StateKey key);
    /// <summary>批量写入 + 合并事件为一次发布；未 Commit 前中间态不外泄。</summary>
    void Commit();
    /// <summary>丢弃所有未提交变更，不产生任何事件。</summary>
    void Rollback();
}
```

**语义**：一次 `Load`/`Destroy` = 一个事务。事务体内所有 `Set/Remove` 先写事务缓冲，`Commit` 时按序落到 `IStorageCell`（单 key 原子由 `ConcurrentDictionary` 保证），`DataChanged` 合并为一次发布。**中间态（Loading/半挂载）对订阅者不可见**——这是 L4 竞态的根治点。

### 4.3 订阅中枢

`Share/StateManagement/Abstractions/IStateSubscriptionHub.cs`：

```csharp
namespace Share.StateManagement;

public interface IStateSubscriptionHub : IDisposable
{
    /// <summary>订阅指定命名空间+作用域的状态变更；scope 为空=该命名空间全量。</summary>
    IDisposable Subscribe(string stateNamespace, string? scope, Action<StateKey, StateSnapshot<object?>> handler);

    /// <summary>事务提交后由 StateCenter 调用的统一发布入口（投影器唯一数据源）。</summary>
    void Publish(StateKey key, object? value, long version, string? source = null);

    /// <summary>内部：为每个 scope 维护版本表，版本 ≤ lastSeen 的事件丢弃。</summary>
    long GetLastVersion(string stateNamespace, string scope);
}
```

**收敛对象**：现有 `ContainerStateStore.StateChanged`（`Modules/InternalStorage/State/ContainerStateStore.cs:13`）、`HostBindingStore.HostBindingChanged`（`HostBindingStore.cs:13`）、`ObservableStateStore.StateChanged`（`Share/StateManagement/StateStorage/Core/ObservableStateStore.cs:8`）全部改为经 `StateSubscriptionHub` 转发；`StorageCell.DataChanged` 是底层唯一物理事件源。

### 4.4 作用域生命周期

`Share/StateManagement/Abstractions/IScopeLifecycle.cs`：

```csharp
namespace Share.StateManagement;

public enum ScopeState { Empty, Declared, Loading, Loaded, Destroying, Destroyed }

public sealed record ScopeLifecycleInfo(
    string Scope,
    ScopeState State,
    long ConfigVersion,
    string? ConfigPath,
    int NodeCount,
    string? LastError,
    DateTime UpdatedAtUtc);

public interface IScopeLifecycle
{
    Task<LoadResult> LoadAsync(string scope, string configPath, bool forceReload = false);
    Task DestroyAsync(string scope);
    ScopeLifecycleInfo? GetState(string scope);
    IReadOnlyCollection<string> GetScopes();
    event Action<ScopeLifecycleInfo>? ScopeStateChanged;
}

public sealed record LoadResult(string Scope, bool Succeeded, long ConfigVersion, int NodeCount, string? Error = null);
### 4.5 组合生命周期

`Share/StateManagement/Abstractions/ICompositionLifecycle.cs`：

```csharp
namespace Share.StateManagement;

/// <summary>
/// 组合（RenderPipelineComposition/EcsGate）生命周期——应用级唯一权威（治 L1）。
/// 契约不含 UI 类型：composition/gate 由 Provider 构造时持有（R3）。
/// </summary>
public interface ICompositionLifecycle
{
    /// <summary>应用级注册（幂等）：进程启动时由 Composition Root 调用一次。</summary>
    void Register(string id, object composition, object gate);

    /// <summary>StateCenter 在 scope 销毁时调用：只销毁该 scope 实体，绝不 Dispose composition。</summary>
    void ScopeDestroyed(string scope);

    /// <summary>进程退出唯一释放点：唯一允许调用 composition.Dispose() 的路径。</summary>
    void AppShutdown();
}
```

### 4.6 容器生命周期

`Share/StateManagement/Abstractions/IContainerLifecycle.cs`：

```csharp
namespace Share.StateManagement;

public enum ContainerMountState { Registered, Mounted, Orphaned }

public sealed record ContainerLifecycleInfo(
    string Scope,
    string HostKey,
    ContainerMountState MountState,
    string BindingSource,
    DateTime UpdatedAtUtc);

public interface IContainerLifecycle
{
    /// <summary>注册 scope↔hostKey 绑定（幂等；重复注册更新来源标记）。</summary>
    void Register(string scope, string hostKey, string source = "code-registered");

    void Unregister(string scope);

    /// <summary>唯一寻址：scope 优先，hostKey 反查兜底（治 L5）。</summary>
    string? ResolveHostKey(string scopeOrHostKey);

    /// <summary>hostKey → scope 反查。</summary>
    string? ResolveScope(string hostKey);

    bool IsBound(string scope);

    /// <summary>挂载状态唯一写入点（R5）：由 Provider 的宿主注入成功处调用。</summary>
    void MarkMounted(string scopeOrHostKey, bool mounted);

    ContainerMountState GetMountState(string scopeOrHostKey);
}
```

### 4.7 状态中心门面

`Share/StateManagement/Abstractions/IStateCenter.cs`：

```csharp
namespace Share.StateManagement;

public interface IStateCenter : IStateManager, IStateOrchestrator, IDisposable
{
    IStorageCell Cell { get; }                       // 统一存储后端（StorageEngine 根会话）
    IStateStore GlobalStore { get; }                 // 全局字符串 KV（对齐 IStateManager.GlobalStore）
    IStateStore GetOrCreateScope(string scope);      // scope 状态分区（落 StorageEngine.CreateSession）
    IStateTransaction BeginTransaction(string? scope = null);
    IStateSubscriptionHub Subscriptions { get; }
    IScopeLifecycle Scopes { get; }
## 五、与 L1–L5 的收敛映射（实施验收判据）

| # | 缺陷 | 状态中心对策 | 关键改动点 | 验收判据 |
|---|------|------------|-----------|---------|
| L1 | composition 随窗体死 | `ICompositionLifecycle` 应用级持有；`MainForm.Dispose` 解绑视图不释放 | `MainForm.cs:157-161`、`ScriptExecutionPort.cs:162/285/301`、`MainFormGateFactory.cs` | `mainlayout_run_ui.json` 运行结束后再跑 `deepseek_chat_direct.json` 不抛 `ObjectDisposedException` |
| L2 | 先销毁后加载 + Mounted 断链 | `ScopeLifecycle.LoadAsync` 事务化；`MarkMounted` 唯一写入点 | `EcsGate.DoLoadAndRender`（收敛进 Provider）、`RenderStageRunner.DestroyScopeEntities`、`ContainerCommandExecutor` | `assert container sideContainer count>0` PASS；`validate-mount titleLabel parent=sideContainer` PASS |
| L3 | 状态残留 + 幂等短路 | 状态中心统一转移；幂等短路只同 ConfigVersion；事务回滚清残留 | `ContainerCommandExecutor.cs:26-32`（删除旧短路） | 二次加载不同 json 真实执行（ConfigVersion 递增）；加载失败后重试成功 |
| L4 | 全量重渲染竞态 | 订阅中枢 scope 串行 + 版本去重；投影只读最终快照 | `FunctionExecutedRenderBridge`、`EventPlatform.InvokeFunctionAsync(scope)`、`ContainerProjector` | 连续 3 次运行结论稳定（消除 PASS↔FAIL 竞态）；内容区不消失 |
| L5 | 寻址口径分裂 | `IContainerLifecycle` 唯一寻址 | `ContainerAccessPortAdapter`、`ContainerAssertionService.ResolveInHost`、`ContainerRegistry` | `validate` 与 `assert-container`/`validate-mount` 对同一容器结论一致 |

---

## 六、文件清单

### 6.1 新增 — `CsharpPrj/Share/StateManagement`

| 文件 | 内容 |
|------|------|
| `Abstractions/StateKey.cs` | §4.1 StateKey |
| `Abstractions/StateSnapshot.cs` | §4.1 StateSnapshot\<T\> |
| `Abstractions/IStateTransaction.cs` | §4.2 |
| `Abstractions/IStateSubscriptionHub.cs` | §4.3 |
| `Abstractions/IScopeLifecycle.cs` | §4.4（含 ScopeState/ScopeLifecycleInfo/LoadResult） |
| `Abstractions/ICompositionLifecycle.cs` | §4.5 |
| `Abstractions/IContainerLifecycle.cs` | §4.6（含 ContainerMountState/ContainerLifecycleInfo） |
| `Abstractions/IStateCenter.cs` | §4.7 |

### 6.2 新增 — `CsharpPrj/Operation/StateHoldDriven`

| 文件 | 内容 |
|------|------|
| `Core/StateCenter.cs` | §4.8 StateCenter 实现 |
| `Core/StateTransaction.cs` | 事务缓冲 + Commit/Rollback |
| `Core/StateSubscriptionHub.cs` | 订阅注册/发布/版本去重/串行队列 |
| `Core/StateKeyExtensions.cs` | StateKey 与 IStorageCell 键互转辅助（可选） |
| `Lifecycle/ScopeLifecycleStateMachine.cs` | 纯状态机（§4.4 转移表 + 事件） |
| `Lifecycle/StateLifecycleProviders.cs` | Provider 注册容器（空实现占位，未注册即抛） |

### 6.3 修改 — `CsharpPrj/Operation/StateHoldDriven`

| 文件 | 改动 |
|------|------|
| `Core/StateStore.cs` | 后端迁移 `IStorageCell`（Session 层）；补 `StateChanged` 事件；保留 `IStateStore` 接口签名 |
| `StateHoldDriven.csproj` | 新增 `InternalStorage.csproj` 引用 |
| `Pipeline/StatePipelineBuilder.cs` | `CreateDefaultPipeline` 末尾追加 `StateCenterBlock`（创建 StateCenter + 注册到 AppServiceProvider），保持初始化管线语义 |

### 6.4 新增/修改 — `CsharpPrj/Operation/FrontEndDriven`

| 文件 | 改动 |
|------|------|
| `State/Projection/StateCenterFactory.cs`（新增） | 创建 `StateCenter`（IStorageCell 后端）+ 注册三个 Provider；幂等单例 |
| `State/Projection/ScopeLifecycleAdapter.cs`（新增） | `IScopeLifecycle` 实现：包装 EcsGate/composition 渲染（原 DoLoadAndRender 收敛于此） |
| `State/Projection/CompositionLifecycleAdapter.cs`（新增） | `ICompositionLifecycle` 实现：持有 composition/gate，`ScopeDestroyed`→DestroyScopeEntities，`AppShutdown`→唯一 Dispose |
| `State/Projection/ContainerLifecycleAdapter.cs`（新增） | `IContainerLifecycle` 实现：包装 ContainerRegistry/HostMountService/ControlPool，唯一寻址 + MarkMounted 唯一写入点 |
| `State/StateServiceCollection.cs` | `RegisterAll` 收敛为「创建/复用 StateCenter + 注册 Provider」（幂等；消除每窗体重复注册 U15） |
| `State/Projection/ContainerProjector.cs` | `RenderAsync` 改为订阅 `StateSubscriptionHub` 驱动 + 内部事务化调用 `ScopeLifecycle.LoadAsync`；删除直接命令 EcsGate 的路径 |
| `State/Commands/ContainerCommandExecutor.cs` | `LoadToScopeAsync` 改经 `StateCenter.Scopes.LoadAsync`；删除旧幂等短路与独立 Failed 判定（归 StateCenter） |
| `Gate/EcsGate.cs` | `DoLoadAndRender` 中挂载状态写改走 `IContainerLifecycle.MarkMounted`；`DestroyScopeAsync` 改经 `ICompositionLifecycle.ScopeDestroyed` |
| `Initial/RenderPipeline/FunctionExecutedRenderBridge.cs` | 空 scope 事件：经 StateCenter 查触发作用域后局部渲染；或保留全量但加「距上次投影去重」 |
| `Lifecycle/EventPlatform.cs` | `InvokeFunctionAsync`/`FunctionDone` 的 scope 由 StateCenter 注入（触发控件所在 scope） |
| `FrontEndDriven.csproj` | 新增 `StateManagement.csproj`、`StateHoldDriven.csproj` 引用 |

### 6.5 修改 — `FileDriven`

## 七、分阶段实施计划（P0–P6）

> **铁律**：每阶段完成必须 `dotnet build` 通过 + 该阶段验证项通过，才进入下一阶段。禁止跨阶段一次性提交。

### P0 · 基线固化（无代码改动）

- **动作**：`dotnet build CsharpPrj/MainProj.sln` + `dotnet build FileDriven/FileDriven.sln`；复跑 `mainlayout_run_ui.json`，存档当前 FAIL 集（assert-container / validate-mount / 二次加载）。
- **产物**：基线日志（供 P4/P5 后对比）。
- **验证**：日志存档 `docs/analysis/2026/8.5/state_center_p0_baseline.log`（或链接现有 20:03/23:34 日志）。

### P1 · 存储收敛（StateHoldDriven 侧）

- **改动**：
  1. `StateHoldDriven.csproj` 新增 `InternalStorage.csproj` 引用；
  2. `Core/StateStore.cs` 后端迁移 `IStorageCell`（构造注入 `IStorageCell cell`，Session 层读写），保留全部 `IStateStore` 接口签名；补 `event Action<string, object?>? StateChanged`（经 `cell.DataChanged` 转发）。
- **验证**：`MainProj.sln` 编译 0 错误；`StateStore` 现有调用方（若有）不破；新增单测（Set/Get/Remove/RemoveByPrefix/StateChanged 触发）。

### P2 · 中心核心（Share 契约 + StateHoldDriven 实现）

- **改动**：
  1. 新增 §6.1 全部契约文件（StateKey/StateSnapshot/IStateTransaction/IStateSubscriptionHub/IStateCenter）与 §6.2 的 `StateCenter`/`StateTransaction`/`StateSubscriptionHub` 实现；
  2. `StateCenter` 先实现「非 UI」部分：GlobalStore/scope 分区（`StorageEngine.CreateSession`）/事务/订阅；
  3. 生命周期属性（Scopes/Composition/Containers）返回「未注册占位 Provider」——调用即抛 `InvalidOperationException("生命周期 Provider 未注册")`，保证装配顺序可见。
- **验证**：编译 0 错误；单测：事务回滚不发布事件、订阅版本去重、scope 分区隔离。

### P3 · 生命周期状态机（StateHoldDriven 侧，纯逻辑）

- **改动**：新增 `Lifecycle/ScopeLifecycleStateMachine.cs`（§4.4 状态机）+ `StateLifecycleProviders.cs` 注册容器；`IScopeLifecycle` 的 `LoadAsync`/`DestroyAsync` 状态转移由 StateMachine 实现，**渲染段委托给注册的 Provider 委托**（`Func<string,string,Task<LoadResult>>`），P3 阶段注入测试桩。
- **验证**：编译 + 状态机单测（Empty→Declared→Loading→Loaded / Loading→Failed / Loaded→Destroying→Destroyed 全转移）。

### P4 · FrontEndDriven 接线（Provider 实现 + 收敛）

- **改动**（§6.4）：
  1. 新增 `StateCenterFactory`、`ScopeLifecycleAdapter`、`CompositionLifecycleAdapter`、`ContainerLifecycleAdapter`；
  2. `StateServiceCollection.RegisterAll` 收敛为幂等装配（创建/复用 StateCenter + 注册 Provider）；
  3. `EcsGate.DoLoadAndRender` 挂载状态写改走 `IContainerLifecycle.MarkMounted`；`DestroyScopeAsync` 改经 `ICompositionLifecycle.ScopeDestroyed`；
  4. `ContainerCommandExecutor.LoadToScopeAsync` 改经 `StateCenter.Scopes.LoadAsync`（删除旧幂等短路）；
  5. `ContainerProjector` 改订阅 `StateSubscriptionHub` 驱动。
- **验证**：编译 0 错误；`mainlayout_run_ui.json` 断言链回归（validate/assert-container/validate-mount 同口径）；加载/销毁状态经 `layout report` 一致。

### P5 · 应用级提升（治 L1 核心）

## 八、验证与验收标准

### 8.1 编译验收（每阶段必过）

```
dotnet build CsharpPrj/MainProj.sln        # 0 错误
dotnet build FileDriven/FileDriven.sln     # 0 错误
```

### 8.2 运行时验收（P4 之后每次回归）

| # | 用例 | 期望 |
|---|------|------|
| 1 | `mainlayout_run_ui.json` 全量 | 步骤 1-10.1 validate 四控件 PASS；`assert container sideContainer count>0` PASS；`validate-mount titleLabel parent=sideContainer` PASS |
| 2 | 步骤 10.5 手动选**不同** json 加载到 Side | 加载成功（ConfigVersion 递增）；`layout report` 容器归属行与 `assert-container` 计数一致 |
| 3 | 步骤 10.5 重复选**同一** json | 幂等短路（同 ConfigVersion 直接成功，不重渲染） |
| 4 | 脚本运行结束后再跑第二个脚本（如 `deepseek_chat_direct.json`） | 不抛 `ObjectDisposedException`（L1 验收） |
| 5 | 连续运行 ≥3 次 | 结论稳定，无 PASS↔FAIL 竞态（L4 验收） |

### 8.3 结构验收

- [ ] 全库 `_composition.Dispose()` 仅存在于 `ICompositionLifecycle.AppShutdown()` 实现内（R3）。
- [ ] 全库 `MarkMounted`/`MarkUnmounted` 仅经 `IContainerLifecycle` 出口（R5）。
- [ ] `StateHoldDriven.csproj` 无 `FrontEndDriven` 引用（R2）。
- [ ] 无 `ContainerCommandExecutor` 旧幂等短路（R6）。
- [ ] 新增契约全部在 `Share/StateManagement`（R1）。

---

## 九、风险与边界

| # | 风险 | 缓解 |
|---|------|------|
| 1 | 事务体含异步 UI 渲染，跨线程提交可能死锁 | 事务的渲染段必须在 UI 线程内完成（`IUIDispatcher.InvokeAsync` 包裹事务体，对齐现 `ContainerProjector.RenderOnUIThreadAsync`）；Commit 在渲染完成后于 UI 线程调用 |
| 2 | composition 提升应用级后，多窗体共享同一 ECS 引擎 | StateCenter 登记「视图绑定」（form↔rootHost）；MainForm 注销时只解绑，不清 ControlPool 的全局实体（仅清该窗体注册的 root 视图引用） |
| 3 | `StateStore` 迁移 IStorageCell 改变并发语义（lock→ConcurrentDictionary 单层） | 保留接口签名与行为等价；迁移后跑现有调用方回归；IStorageCell 单 key 原子性满足现语义 |
| 4 | 命名空间 `Modules.StateManagement` vs `Share.StateManagement` 混用 | 新代码一律 `Share.StateManagement`；`using Modules.StateManagement;` 仅引 `StateChangedEventArgs`；不重构旧文件（P6 视需再统一） |
| 5 | Provider 未注册时误调用 | 占位 Provider 抛明确异常，装配顺序错误在启动期暴露而非运行期静默 |
| 6 | P5 改动面广（MainForm/ScriptExecutionPort/FrameworkShutdown） | 分小步提交；每步编译 + 单脚本回归；P5 内先改 MainFormGateFactory 幂等，再改 Dispose |

---

## 十、附录：现状代码锚点速查（实施时引用）

| 关注点 | 文件:行 | 备注 |
|--------|--------|------|
| composition 唯一释放链 | `CsharpPrj/Operation/FrontEndDriven/Gate/EcsGate.cs:213-218` | `_composition.Dispose()` |
| 窗体 Dispose → gate | `FileDriven/FileDriven.Execution/Initial/MainForm.cs:157-161` | 需改（R3） |
| composition Dispose 全局清空 | `CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderPipelineComposition.cs:232-239` | `Clear()` 全局 |
| 每窗体新建 gate/composition | `FileDriven/FileDriven.Execution/Initial/MainFormGateFactory.cs:30-33` | 需提升应用级 |
| GUI 窗体生命周期 | `FileDriven/FileDriven.Execution/Ports/ScriptExecutionPort.cs:162/285/301` | `using var runner` + Dispose |
| 先销毁后加载 | `CsharpPrj/Operation/FrontEndDriven/Gate/EcsGate.cs:152-186` | DoLoadAndRender |
| Mounted 判定 | `CsharpPrj/Operation/FrontEndDriven/Gate/EcsGate.cs:177-183` | `HostActualChildCount>0` |
| DestroyScope + MarkUnmounted | `CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderStageRunner.cs:132-183` | 宿主权属守卫 |
| 挂载回填 | `CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderPipelineStages.cs:283-314` | RenderMountStage |
| 幂等短路 + Failed 判定 | `CsharpPrj/Operation/FrontEndDriven/State/Commands/ContainerCommandExecutor.cs:26-32/44-62` | 需删/改 |
| 投影命令 EcsGate | `CsharpPrj/Operation/FrontEndDriven/State/Projection/ContainerProjector.cs:125-135` | 改订阅驱动 |
| 全量重渲染 | `CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/FunctionExecutedRenderBridge.cs:77-88` | 空 scope 全量 |
| 事件 scope 空 | `CsharpPrj/Operation/FrontEndDriven/Lifecycle/EventPlatform.cs:171-172` | FunctionDone(scope="") |
| 寻址口径 | `CsharpPrj/Operation/FrontEndDriven/WinForms/Adapter/ContainerAccessPortAdapter.cs:24-35`、`CsharpPrj/Share/FrameworkAbstractions/ContainerAbstractions/ContainerAssertionService.cs:85-162` | validate-mount 缺 hostKey 反查 |
| 容器注册/寻址 | `CsharpPrj/Operation/FrontEndDriven/WinForms/Container/ContainerRegistry.cs:146-155` | ResolveByScopeOrHostKey（已加兜底） |
| HostMountService | `CsharpPrj/Operation/FrontEndDriven/WinForms/Container/HostMountService.cs:40-51/254-263` | 唯一寻址/挂载写入口（可作 Provider 底座） |
| 现有状态存储 | `CsharpPrj/Modules/InternalStorage/State/ContainerStateStore.cs`、`HostBindingStore.cs` | 保留，经订阅中枢收敛 |
| 现有装配 | `CsharpPrj/Operation/FrontEndDriven/State/StateServiceCollection.cs:27-105` | 收敛为幂等装配 |
| StateManager 基座 | `CsharpPrj/Share/StateManagement/Core/StateManager.cs` | 需迁移 InMemory→StorageEngine session |
| 存储后端 | `CsharpPrj/Modules/InternalStorage/Core/StorageCell.cs`、`StorageEngine.cs:45-72` | CreateSession 分区 |
| StateStore 现状 | `CsharpPrj/Operation/StateHoldDriven/Core/StateStore.cs` | 迁移 IStorageCell |

---

*本文档为实施蓝图；每阶段完成后回填验证结果并更新 §〇 状态与 P0 基线对比。*

- **改动**（§6.5）：
  1. `MainFormGateFactory.Create` 进程级一次创建 composition/gate/StateCenter（静态锁 + 首次创建缓存），后续复用；
  2. `MainForm.Dispose` 删除 `_gate.Dispose()`，改为解绑视图 + 从 StateCenter 注销；
  3. `ScriptExecutionPort.GuiFormRunner` 关闭窗体不销毁引擎；
  4. `StateCenter.Shutdown()` 接入进程退出（`FrameworkShutdown`）。
- **验证**：编译 0 错误；运行 `mainlayout_run_ui.json` 后再运行 `deepseek_chat_direct.json`（或任意第二个脚本），不再抛 `ObjectDisposedException`；二次加载 json 成功。

### P6 · 收尾

- **改动**：
  1. `EventPlatform.InvokeFunctionAsync` 的 scope 由 StateCenter 注入触发作用域；
  2. 按回归结果决定 `FunctionExecutedRenderBridge` 竞态补丁去留；
  3. 更新 clinerules（StateHoldDriven 模块职责）与本文档状态回填。
- **验证**：`MainProj.sln` + `FileDriven.sln` 全量构建 0 警告 0 错误；`mainlayout_run_ui.json` 全绿（含步骤 10.5 手动选文件加载）；连续 3 次运行结论稳定。

---

## 十一、实施记录（2026-08-05 回填）

> 按 §〇 0.1 阅读顺序实施，每阶段"编译 + 验证"后进入下一阶段。

### P0–P6 实施结果汇总

| 阶段 | 结果 | 说明 |
|------|------|------|
| P0 基线 | ✅ | 两 sln 编译 0 警告 0 错误；基线存档 `docs/analysis/2026/8.5/state_center_p0_baseline.log`；注意两 sln 共享输出目录，禁止并行构建 |
| P1 存储收敛 | ✅ | `StateHoldDriven.csproj` 新增 InternalStorage 引用；`StateStore.cs` 迁移 IStorageCell（Session 层，字符串/对象 KV 前缀隔离，保留 IStateStore 接口签名，补 StateChanged 事件）；新建 `CentralTest/.../StateHoldDriven.Tests`（MSTest，11 用例） |
| P2 中心核心 | ✅ | Share 新增 8 个契约（StateKey/StateSnapshot/IStateTransaction/IStateSubscriptionHub/IScopeLifecycle/ICompositionLifecycle/IContainerLifecycle/IStateCenter）；StateManagement.csproj 新增 TConfig 引用（IStorageCell 类型，文档 §3.3"无需新增"的偏差已决策）；实现 StateCenter/StateTransaction/StateSubscriptionHub/StateKeyExtensions + StateCenterBlock 追加管线（27 用例累计） |
| P3 状态机 | ✅ | ScopeLifecycleStateMachine（纯状态机 + R6 幂等短路 + 渲染委托 Func）+ StateLifecycleProviders（34 用例累计） |
| P4 接线 | ✅ | StateCenterFactory/ScopeLifecycleAdapter/CompositionLifecycleAdapter/ContainerLifecycleAdapter；StateServiceCollection 幂等装配；EcsGate 挂载写经 IContainerLifecycle.MarkMounted、销毁经 ICompositionLifecycle.ScopeDestroyed；ContainerCommandExecutor 委托 StateCenter.Scopes.LoadAsync（删旧短路）；ContainerProjector 订阅驱动（删直接命令 EcsGate） |
| P5 应用级 | ✅ | MainFormGateFactory 进程级一次创建；MainForm.Dispose 解绑视图（删 `_gate.Dispose()`）；GuiFormRunner 关闭不释放引擎；CommandExecutor.ShutdownFrameworkImpl 接入 StateCenter.Shutdown() → AppShutdown() |
| P6 收尾 | ✅ | 结构红线 R1–R6 grep 验收通过（`_composition.Dispose()` 仅 AppShutdown 内；`MarkMounted/UnmarkMounted` 仅经 IContainerLifecycle 出口）；`FunctionExecutedRenderBridge._disposed` 补丁保留（防御性）；csproj ModuleRule 更新；本文档回填 |

### 编译与单测验收（最终）

```
dotnet build CsharpPrj/MainProj.sln      # 0 警告 0 错误
dotnet build FileDriven/FileDriven.sln   # 0 警告 0 错误
dotnet test CentralTest/CSharpTest/Operation.Tests/StateHoldDriven.Tests  # 34/34 通过
```

### 运行验收待办（需真实 GUI 显示环境）

| # | 用例 | 期望 |
|---|------|------|
| 1 | `mainlayout_run_ui.json` 全量 | validate 四控件 PASS；`assert container sideContainer count>0` PASS；`validate-mount` PASS |
| 2 | 加载**不同** json 到 Side | 成功，ConfigVersion 递增（L3） |
| 3 | 重复加载**同一** json | 幂等短路（同 ConfigVersion，不重渲染，R6） |
| 4 | 第二个脚本（deepseek_chat_direct.json） | 不抛 `ObjectDisposedException`（L1 验收） |
| 5 | 连续 ≥3 次 | 结论稳定，无 PASS↔FAIL 竞态（L4） |

### 与文档计划的偏差记录

1. **§3.3 引用表偏差**：`StateManagement.csproj` 实际需新增 `TConfig.csproj` 引用（`IStateCenter.Cell` 用 `IStorageCell`），文档原表"无需新增"不成立；TConfig 属 Plate 层，不违反 R1/R2。
2. **P6 第 1 点 EventPlatform scope 注入**：保留现状（`InvokeFunctionAsync` 已有 scope 参数，调用方传入）；"StateCenter 反查触发作用域"依赖函数执行链路上下文，改动面大收益有限，留作后续增强。
3. **`FunctionExecutedRenderBridge` 竞态补丁**：按 §6.6 保留（防御性），注释已更新说明 StateCenter 收敛后 L1 根因消除。
4. **测试项目**：文档未指定测试承载，新建 `StateHoldDriven.Tests`（MSTest，对齐 WorkFlowMethod.Test 风格）。

---

| 文件 | 改动 |
|------|------|
| `FileDriven.Execution/Initial/MainFormGateFactory.cs` | composition/gate/StateCenter 提升为进程级一次创建（首次调用创建，后续复用）；`RegisterAll` 幂等 |
| `FileDriven.Execution/Initial/MainForm.cs` | `Dispose(bool)` 改为「解绑 rootView + 从 StateCenter 注销视图」，**删除 `_gate.Dispose()`**（R3） |
| `FileDriven.Execution/Ports/ScriptExecutionPort.cs` | `GuiFormRunner`：`Close()` 后不 `Dispose()` MainForm（或 Dispose 仅解绑不释放引擎） |
| `FileDriven.Execution/FileDriven.Execution.csproj` | 新增 `StateHoldDriven.csproj` 引用（若未传递） |

### 6.6 删除/降级

| 对象 | 动作 | 说明 |
|------|------|------|
| `ContainerCommandExecutor` 的幂等短路 + 独立 Succeeded 判定 | 删除逻辑 | 归 `StateCenter.Scopes.LoadAsync` |
| `FunctionExecutedRenderBridge` 的 `_disposed` 竞态补丁 | 保留（防御性） | 根因 L1 消除后不再触发，暂不删（P6 视回归再删） |
| `FunctionStorage/*` | 保留降级 | 工具库，不参与状态权威 |

---

    ICompositionLifecycle Composition { get; }
    IContainerLifecycle Containers { get; }

    /// <summary>生命周期 Provider 注册（FrontEndDriven.State 在装配时注入）。</summary>
    void RegisterScopeLifecycle(IScopeLifecycle scopeLifecycle);
    void RegisterCompositionLifecycle(ICompositionLifecycle compositionLifecycle);
    void RegisterContainerLifecycle(IContainerLifecycle containerLifecycle);

    TState? Read<TState>(StateKey key);
    void Write<TState>(StateKey key, TState value, string? source = null);   // 非事务直接写（诊断/审计用，业务变更必须走事务）

    /// <summary>进程退出清理（内部调用 Composition.AppShutdown() 后释放存储）。</summary>
    void Shutdown();
}
```

### 4.8 实现要点（`Operation/StateHoldDriven/Core/*`）

| 类型 | 关键实现约束 |
|------|-------------|
| `StateCenter` | 聚合 `StateManager`（scope 分区）、`StateOrchestrator`（Actor，保留）、`StateSubscriptionHub`、三个生命周期 Provider（未注册前调用抛 `InvalidOperationException`，保证装配顺序可见） |
| `StateTransaction` | 事务缓冲 `List<(StateKey, object?)>`；`Commit` 落 `IStorageCell`（Session 层）+ 单次 `Subscriptions.Publish`；`Dispose` 未 Commit 自动 Rollback |
| `StateSubscriptionHub` | 每 scope 一个 `ConcurrentQueue` + 版本表；`Publish` 版本 ≤ lastSeen 丢弃；handler 异常隔离（单 handler 失败不影响其余） |
| `ScopeLifecycleStateMachine` | 纯状态机（无 UI）：实现 §4.4 转移；`LoadAsync` 的渲染段委托给注册的 `IScopeLifecycle`（Provider 提供） |
| `StateStore`（改造） | 后端从 `ConcurrentDictionary` 迁移到 `IStorageCell`（Session 层）；保留 `IStateStore` 接口签名（兼容现有调用）；补 `StateChanged` 事件 |

---

```

**状态机**：

```
        Declare(容器声明)        Load 事务开始              Load 事务提交
Empty ────────────────► Declared ───────────► Loading ────────────► Loaded
                                                 │ 渲染失败/宿主空      │
                                                 ▼                    │
                                              Failed(仍算 Loading 态) │
                                                                      ▼
Destroy 事务开始                                     Destroy 事务提交
Loaded ───────────────► Destroying ───────────────► Destroyed ──────► (scope 移除 → Empty 语义)
```

**LoadAsync 内部协议（事务化后的 DoLoadAndRender，逐条对应现有实现）**：

```
1. info = GetState(scope)
2. 幂等短路（R6）：info.ConfigPath==configPath ∧ info.ConfigVersion>0 ∧ !forceReload
   → return LoadResult(Succeeded=true, 同版本)   // 旧短路（ContainerCommandExecutor:26-32）删除
3. tx = BeginTransaction(scope)
4. tx.Set(ScopeLifecycle(scope), Loading)
5. [Provider:IScopeLifecycleAdapter.DoLoadAsync(scope, configPath)]  ← FrontEndDriven.State 实现：
   a. 销毁旧 scope 实体（原 EcsGate.DoLoadAndRender 的 DestroyScopeAsync）
   b. composition.Execute(config, host, scope) + ExecuteRenderStage(scope)
   c. HostActualChildCount>0 → Containers.MarkMounted(scope, true)   ← R5 唯一写入点
6. 成功 → tx.Set(ScopeLifecycle(scope), Loaded, NodeCount, ConfigVersion+1)
         tx.Set(Container(scope), ContainerState{Loaded,...})；tx.Commit()
   失败 → tx.Set(LastError)；tx.Rollback() 后按策略重试，或显式置 Failed 状态并 Commit
   （回滚=不发布 Loading，状态停留上一版；Failed 需显式提交，供 L3 诊断可见）
```

---

│   FrontEndDriven.State：Provider 实现（composition/容器/scope 适配器）    │
│   EcsGate / RenderPipelineComposition / HostMountService /               │
│   ContainerRegistry / ContainerProjector（改订阅驱动，不再直接命令）      │
└────────────────────────────────────────────────────────────────────────┘
```

### 3.2 依赖方向决策（为什么契约放 Share 层）

| 决策 | 依据 |
|------|------|
| 契约放 `Share.StateManagement` | 该层已有 `IStateManager`/`IStateOrchestrator`/`IStateActor`/`StateChangedEventArgs` 基座，契约是「补全既有基座」；Share 层引用仅 BasicMethod/Block/Pipeline/DiagnosticContext，无 Operation/UI 引用，放契约无环 |
| `StateHoldDriven` 实现 StateCenter 核心 | Operation 层职责=编排；StateHoldDriven 已引用 Share.StateManagement；其引用链（TConfig/UniversalMiddleware/FrameworkAbstractions）不含 FrontEndDriven，保持 UI/ECS 无关 |
| `FrontEndDriven.State` 实现 Provider | Provider 必然持有 `EcsGate`/`RenderPipelineComposition`/`HostMountService`/`ContainerRegistry`（FrontEndDriven 类型）；实现侧引用契约层，方向 = FrontEndDriven → StateHoldDriven → Share.StateManagement，**无环** |
| `FileDriven.Execution` 装配 | 唯一 Composition Root，创建 StateCenter + 注册 Provider |

### 3.3 csproj 引用变更（新增，无删除）

| 项目 | 新增引用 | 用途 |
|------|---------|------|
| `Share/StateManagement/StateManagement.csproj` | （无需新增；契约保持 UI 无关，不启用 WinForms） | — |
| `Operation/StateHoldDriven/StateHoldDriven.csproj` | `..\..\Modules\InternalStorage\InternalStorage.csproj` | StateStore 迁移 IStorageCell 后端 + 事务/订阅基于 StorageCell |
| `Operation/FrontEndDriven/FrontEndDriven.csproj` | `..\..\Share\StateManagement\StateManagement.csproj`、`..\..\Operation\StateHoldDriven\StateHoldDriven.csproj` | 契约 + 中心实现（Provider 接线） |
| `FileDriven/FileDriven.Execution/FileDriven.Execution.csproj` | `..\..\CsharpPrj\Operation\StateHoldDriven\StateHoldDriven.csproj`（若未传递） | 装配 StateCenter |

> 环验证：`StateHoldDriven → Share.StateManagement`；`FrontEndDriven → StateHoldDriven → Share.StateManagement`；`StateHoldDriven` 不引用 `FrontEndDriven`（R2）。`Modules/Registry.csproj` 不含 InternalStorage，故 StateHoldDriven 必须**直接**引用 `InternalStorage.csproj`。

---

