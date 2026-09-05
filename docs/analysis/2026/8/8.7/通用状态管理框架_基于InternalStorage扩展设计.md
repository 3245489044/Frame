# 通用状态管理框架 — 基于 InternalStorage 扩展设计

> **定位**：基础设施层（Storage）重构文档
> **目标**：在 `CsharpPrj/Modules/InternalStorage` 基础上扩展出「通用 UI 状态管理框架」，为应用层（动态容器、视图状态、宿主绑定）提供与 UI 解耦的声明式状态基座。
> **日期**：2026-08-02
> **关联**：应用层重构文档《容器状态解耦_基于通用状态管理框架的应用层重构设计.md》

---

## 一、背景与问题

### 1.1 现状问题（以 sideContainer 动态容器为例）

```
LoadSidePanel (UIToolbox)
  → LoadConfigToScope("Side","sideContainer")      ← 硬编码 逻辑作用域↔物理控件名
    → ILoadConfigPort.LoadConfigAsync(...)
      → ConfigurationLifecycleManager.LoadAsync(...)
        → ResolveHost → ControlPool.GetControl("sideContainer")  ← 跨层状态查询宿主
        → EcsGate.LoadConfigAndRenderAsync(...)
```

**问题本质**：
1. **状态存放位置错误**：`ContainerView : Panel` 持有 `EcsContainerCore`（业务状态），状态生命周期与 WinForms 控件生命周期绑定。
2. **作用域与宿主强耦合**：逻辑作用域（"Side"）与物理控件（"sideContainer"）在 `UIToolbox` 硬编码映射。
3. **加载非原子**：加载前需 `ControlPool.GetControl` 跨层查询宿主，容器未注册时挂载到错误宿主（时序脆弱）。
4. **函数与 UI 耦合**：本应如 click 一样的"函数命令"，因持有 UI 状态而变成混合调用链。

### 1.2 解决方向

在 **InternalStorage**（已具备三层键值存储 + 变更事件 + 索引/映射扩展）之上，新增：
1. **状态领域模型层**：`ContainerState`、`ViewState` 等纯数据模型（不含 UI 引用）。
2. **状态存储门面**：`XxxStateStore` 包装 `IStorageCell`，提供类型安全读写 + 变更订阅。
3. **宿主绑定存储**：基于 `StorageBidirectionalExtensions` 的 scope↔host 双向映射。

---

## 二、InternalStorage 现有能力盘点

| 能力 | 组件/原语 | 用途 |
|------|----------|------|
| 三层键值存储 | `StorageCell.Set<T>/Get<T>/Remove` | 键值状态存储 |
| 分层语义 | `StorageLayer{Config,Session,Transient}` | 静态声明/运行态/瞬态分层 |
| 变更通知 | `StorageCell.DataChanged`（Added/Updated/Removed） | 状态变更→投影调度 |
| 过期机制 | `StorageEntry.IsExpired`（TTL） | 瞬态状态自动回收 |
| 跨层查找 | `StorageCell.TryGet（Config→Session→Transient→Parent）` | 声明覆盖/回退 |
| 类型安全注册 | `StorageRegistry.Register<T>/Resolve<T>` | 状态服务注册中心 |
| 唯一/非唯一索引 | `StorageIndexExtensions` | 按属性快速定位 |
| 双向映射 | `StorageBidirectionalExtensions`（MapForward/GetForward/GetBackward/Unmap） | scope↔host 双向绑定 |
| 集合存储 | `StorageCell.SetAdd/SetRemove/SetGetAll` | 一对多关系/订阅集合 |

> **结论**：InternalStorage 已覆盖状态管理的全部底层原语，**无需新建存储引擎**；扩展工作是「补领域模型 + 补类型安全门面 + 补投影调度」，全部为**增量新增**，不修改现有实现。

---

## 三、扩展架构总览

```
┌────────────────────────────────────────────────────────────────┐
│  Application Layer（应用层，见应用重构文档）                     │
│    UIToolbox / LoadSidePanel / ContainerProjector              │
└───────────────▲────────────────────────────────────────────────┘
                │ 使用
┌───────────────┴────────────────────────────────────────────────┐
│  State Domain Layer（新增：状态领域模型 + 类型安全门面）          │
│    ContainerStateStore    : scope → ContainerState              │
│    ViewStateStore         : viewName → ViewState                │
│    HostBindingStore       : scope ↔ hostKey（双向）             │
│    StateSubscriptionHub   : 订阅聚合/转发                        │
└───────────────▲────────────────────────────────────────────────┘
                │ 包装/复用
┌───────────────┴────────────────────────────────────────────────┐
│  InternalStorage（现有，复用不扩展）                             │
│    StorageCell(IStorageCell) · StorageRegistry(IStorageRegistry)│
│    StorageIndexExtensions · StorageBidirectionalExtensions      │
└────────────────────────────────────────────────────────────────┘
```

**分层原则**：
- 状态领域层 **只依赖 InternalStorage 的接口**（`IStorageCell`/`IStorageRegistry`），不依赖 WinForms/ECS。
- 状态模型（`ContainerState` 等）**不含任何 UI 控件引用**（不存 `Control`，只存逻辑标识）。
- 主机绑定（`HostBindingStore`）**面向逻辑绑定**：绑定 `scope → host key`，由应用层把 host key 解析为控件。

---

## 四、状态领域模型（新增）

### 4.1 ContainerState（容器状态）

```csharp
namespace Modules.InternalStorage.State.Model;

public enum ContainerStatus { Empty, Loading, Loaded, Failed }

/// <summary>容器状态 —— 与 UI 无关的业务容器状态（不可变 record，天然快照）</summary>
public sealed record ContainerState(
    string Scope,               // 逻辑作用域（"Side"/"Content"/...）
    string? ConfigPath = null,  // 已加载配置源（绝对路径）
    long ConfigVersion = 0,     // 配置版本号（幂等判断）
    int NodeCount = 0,          // 上次渲染控件节点数
    ContainerStatus Status = ContainerStatus.Empty,
    string? LastError = null,   // Status=Failed 时非空
    DateTime UpdatedAtUtc = default);
```

**设计要点**：
- `record` 不可变，写入即新实例 → 天然支持快照语义与变更比较。
- 不含 `UIConfiguration`/`Control` 引用 → 可跨层（UI/后台/CLI）共享。
- `ConfigVersion` 用于幂等判断（与 `JsonConfigService` 缓存的时间戳校验对齐）。

### 4.2 ViewState（视图状态，可选扩展）

```csharp
namespace Modules.InternalStorage.State.Model;

/// <summary>视图状态 —— 单个视图控件的逻辑状态</summary>
public sealed record ViewState(
    string ViewName,            // 对应 UI 控件 Name
    string Scope,               // 所属作用域
    bool Visible = true,
    bool Enabled = true,
    object? Payload = null);
```

### 4.3 HostBinding（宿主绑定，逻辑层）

```csharp
namespace Modules.InternalStorage.State.Model;

/// <summary>宿主绑定记录 —— scope 与宿主标识的绑定（不含控件引用）</summary>
public sealed record HostBinding(
    string Scope,
    string HostKey,             // 如 "sideContainer" 控件名 / "root" 视图名
    string BindingSource);      // "json-declared" / "code-registered"
```

---

## 五、类型安全状态门面（新增）

### 5.1 ContainerStateStore

包装 `IStorageCell`，键约定 `container:{scope}:state`，分层语义：静态声明→Config，运行态→Session。**事件转发不新建机制**，直接订阅 `StorageCell.DataChanged` 并做类型过滤。

```csharp
namespace Modules.InternalStorage.State;

public sealed class ContainerStateStore
{
    public const string KeyPrefix = "container:";
    private readonly IStorageCell _cell;

    /// <summary>状态变更事件（写入线程触发，订阅方需自行切 UI 线程）</summary>
    public event Action<string, ContainerState>? StateChanged;

    public ContainerStateStore(IStorageCell cell);

    // 运行态（Session 层）
    public ContainerState? Get(string scope);
    public void Set(string scope, ContainerState state);
    public void Remove(string scope);

    // 静态声明蓝图（Config 层）
    public ContainerState? GetDeclaration(string scope);
    public void SetDeclaration(string scope, ContainerState state);
}
```

**设计要点**：
- **门面而非裸 Key 操作**：键前缀/后缀集中在常量，杜绝散落字符串。
- **事件转发**：`_cell.DataChanged += (_, e) => { if (e.NewValue is ContainerState s && e.Key.StartsWith(KeyPrefix)) { /* 解析 scope → StateChanged */ } }`。

### 5.2 HostBindingStore

复用 `StorageBidirectionalExtensions`，仅存字符串键，不存控件引用。控件解析由应用层完成。

```csharp
namespace Modules.InternalStorage.State;

public sealed class HostBindingStore
{
    public const string MapName = "host_binding";
    private readonly IStorageCell _cell;

    public HostBindingStore(IStorageCell cell);

    public void Bind(string scope, string hostKey, string source = "code-registered");
    public string? ResolveHostKey(string scope);       // GetForward
    public string? ResolveScope(string hostKey);       // GetBackward
    public void Unbind(string scope);                  // Unmap
    public bool IsBound(string scope);                 // ContainsForward
}
```

### 5.3 StateServices 装配

```csharp
namespace Modules.InternalStorage.State;

public static class StateServices
{
    // 经 StorageRegistry 注册单例，与现有 IStorageCell 解析方式一致
    public static void Register(IStorageRegistry registry, IStorageCell cell)
    {
        registry.Register("ContainerStateStore", new ContainerStateStore(cell));
        registry.Register("HostBindingStore", new HostBindingStore(cell));
    }
}
```

---

## 六、投影调度契约（新增）

本模块只定义**契约接口**，实现放应用层（依赖 EcsGate/WinForms）。

```csharp
namespace Modules.InternalStorage.State;

/// <summary>状态变更处理器 —— 由应用层实现，订阅状态变更并执行投影（渲染）</summary>
public interface IStateChangedHandler
{
    Task OnContainerStateChangedAsync(string scope, ContainerState state);
    Task OnHostBindingChangedAsync(string scope, string hostKey);
}

/// <summary>状态订阅中枢 —— 聚合多 Store 事件，转发给处理器</summary>
public sealed class StateSubscriptionHub
{
    public StateSubscriptionHub(ContainerStateStore containerStore, HostBindingStore hostBindingStore);
    public void Attach(IStateChangedHandler handler);  // 订阅 Store 事件
    public void Detach();
}
```

---

## 七、键空间约定

为避免与控制池现有键冲突，定义 **State 模块命名空间前缀**：

| 前缀 | 用途 | 示例 |
|------|------|------|
| `container:{scope}:state` | 容器状态（Session/Config） | `container:Side:state` |
| `view:{viewName}:state` | 视图状态 | `view:configListView:state` |
| `host_binding`（MapName） | scope↔hostKey 双向映射 | `host_binding:Side` |
| `state_sub:{scope}` | 订阅者集合（可选） | `state_sub:Side` |

> 与控制池现有键（`entity_to_control:*`、`control_to_entity:*`、`name_to_entity` 索引）**完全隔离**。

---

## 八、事务性与快照语义说明

- **Set 原子性**：`StorageCell.Set` 对单个 key 是原子的（`ConcurrentDictionary` 索引赋值）。
- **多 key 一致性**：状态集（`container:*` + `host_binding:*`）的跨 key 一致性需由调用方保证写入顺序；如需强一致，可扩展 `StorageEngine` 提供轻量事务（未来扩展）。
- **快照**：`ContainerState` 为不可变 record，读取即获得一致快照（无需锁）。

---

## 九、与现有组件的关系矩阵

| 现有组件 | 重构后职责 | 变化 |
|----------|-----------|------|
| `StorageCell` | 保持三层键值存储 | 不变 |
| `StorageRegistry` | 保持类型安全注册 | 不变（新增状态门面注册） |
| `StorageIndexExtensions` | 保持索引 | 不变 |
| `StorageBidirectionalExtensions` | 保持双向映射 | 不变（HostBindingStore 复用） |
| `ControlPool` | Entity↔name↔Control 纯索引 | **剥离状态查询职责**（上位到 ContainerStateStore） |
| `ContainerView` | 纯 Panel 视图 | **剥离 `_core` 业务状态** |
| `ConfigurationLifecycleManager` | 保留底层编排 | 不变（由上层投影器驱动） |
| `EcsGate` | 保留渲染门面 | 不变 |

---

## 十、未来可扩展点（本次不实现，预留方向）

1. **轻量事务**：`StorageEngine` 增加 `BeginTransaction/Commit/Rollback`（批量 Set + 事件合并）。
2. **状态持久化**：`StorageLayer.Config` 层可序列化到磁盘（配置即状态）。
3. **派生状态**：基于 `DataChanged` 的派生计算（如 `pendingCount = Σ scope.Status==Loading`）。
4. **撤销/重做**：`ContainerState` 不可变性天然支持操作历史栈。
5. **跨窗口作用域**：`StorageCell.Parent` 级联查找已支持父单元共享状态。

---

## 十一、验收标准

- [ ] 新增文件全部位于 `Modules.InternalStorage.State` 命名空间，不修改 InternalStorage 现有实现。
- [ ] `ContainerState`/`ViewState` 不含任何 `System.Windows.Forms` 引用。
- [ ] `ContainerStateStore`/`HostBindingStore` 仅依赖 `IStorageCell`/`IStorageRegistry` 接口。
- [ ] 状态变更经 `StorageCell.DataChanged` 转发，无自建事件机制。
- [ ] 键空间与控制池现有键完全隔离（前缀约定）。