# InternalStorage 复用分析

> 分析日期：2026-07-13
> 目标：复用 `BasicMethod/Map` 和 `BasicMethod/ContextFrame` 中已有的逻辑，在 `Modules/InternalStorage` 中构建**通用运行时内存存储管理框架**，**覆盖并统一**现有 4 套独立的内存存储体系

---

## 一、现有可复用资源总览

### 1.1 `ContextFrame`（上下文框架）

| 文件 | 可复用内容 | 行号 |
|------|-----------|------|
| `Abstractions/IContext.cs` | `SetData<T>` / `GetData<T>` / `TryGetData` / `RemoveData` / `ContainsData` / `GetAllData` 接口 | 54-100 |
| `Abstractions/IContextSnapshot.cs` | `IContextSnapshot`（快照数据载体）+ `ISnapshotable`（快照/恢复接口） | 23-52 |
| `Abstractions/IStateStore.cs` | `IStateStore`（类型安全键值存储接口，含 `GetOrCreate`） | 22-69 |
| `Abstractions/IPipelineContext.cs` | `IPipelineContext`（阶段状态跟踪 + `SharedData`） | - |
| `Abstractions/IScopedContext.cs` | `IScopedContext`（作用域管理） | - |
| `Core/ContextBase.cs` | `ConcurrentDictionary` 实现 + 父子级联 + 数据变更通知 + 服务定位 | 32-242 |
| `Core/PipelineContextBase.cs` | 阶段状态跟踪 + `SharedData` + `Reset()` | 33-130 |
| `Core/StateStoreBase.cs` | 纯 `ConcurrentDictionary` 键值存储实现 | 27-66 |
| `Utilities/ContextExtensions.cs` | `SetDataRange` + `CreateDataSnapshot` + `RestoreDataFromSnapshot` | 33-170 |
| `Utilities/ContextSnapshot.cs` | `IContextSnapshot` 实现 | - |
| `Scoping/ContextScope.cs` | 作用域管理 | - |
| `Scoping/AsyncContext.cs` | 异步上下文 | - |
| `Locking/Lock.cs` | 锁机制 | - |
| `Builders/ContextBuilder.cs` | 上下文构建器 | - |
| `Adapters/ContextAdapter.cs` | 上下文适配器 | - |

### 1.2 `Map/Container`（执行容器）

| 文件 | 可复用内容 | 行号 |
|------|-----------|------|
| `Abstractions/IRuntimeContext.cs` | `Items` 字典 + `GetService` 服务定位 | 15-36 |
| `Abstractions/IExecutionCell.cs` | 原子执行单元接口 | 16-30 |
| `Abstractions/IExecutionEngine.cs` | 引擎生命周期管理 | 14-36 |
| `Core/ExecutionCell.cs` | MediatR 委托实现 | 18-47 |
| `Core/ExecutionEngine.cs` | 状态机 + 生命周期 + 超时/取消处理 | 18-205 |
| `Behaviors/` | `CircuitBreakerBehavior` / `LoggingBehavior` / `RetryBehavior` / `TimeoutBehavior` | - |
| `Decorators/` | `CircuitBreakerDecorator` / `LoggingDecorator` / `RetryDecorator` / `TimeoutDecorator` | - |

### 1.3 `Map/GraphFoundation`（图存储）

| 文件 | 可复用内容 | 行号 |
|------|-----------|------|
| `Storage/IGraphStorage.cs` | 完整存储接口（节点/边 CRUD + 索引查询 + 生命周期） | 14-89 |
| `Storage/InMemoryGraphStorage.cs` | `ConcurrentDictionary` 内存实现 + 双向索引 | 59-309 |
| `Storage/IGraphStorageProvider.cs` | 存储提供者接口 | 15-24 |
| `Snapshots/GraphSnapshot.cs` | 不可变快照 + 版本管理 + 拓扑验证 | 19-261 |
| `Snapshots/GraphDiff.cs` | 差异计算 + 变更集 | 17-121 |

### 1.4 `Map/Transformers`（转换器）

| 文件 | 可复用内容 | 行号 |
|------|-----------|------|
| `TransformContext.cs` | `AsyncLocal` 隐式传播 + 父子作用域 + 快照 + 日志 | 28-174 |
| `Interfaces.cs` | `ITransformer` / `IAsyncTransformer` 接口 | 12-94 |
| `Metadata.cs` | `ITransformMetadata` 元数据接口 + 构建器 | 15-171 |

### 1.5 `Map/Block`（块执行管线）

| 文件 | 可复用内容 | 行号 |
|------|-----------|------|
| `BlockContext.cs` | `ConcurrentDictionary` 数据传递 + 诊断记录 + 取消令牌 | 108-171 |
| `ExecutionPipeline.cs` | 顺序执行 + 条件跳过 + 重试 + 诊断 + 超时 | 29-336 |

### 1.6 `Map/StateMachine`（状态机存储）

| 文件 | 可复用内容 | 行号 |
|------|-----------|------|
| `StateMachineStore.cs` | `ConcurrentDictionary` 多实例管理 + TTL 自动清理 + 容量控制 | 30-115 |
| `StateManager.cs` | 并发控制 + 动作执行记录 + `SemaphoreSlim` 限流 | 37-184 |

---

## 二、现有 4 套体系的问题映射

### 2.1 体系一：`PipelineContextBase`（`SetData`/`GetData` 键值存储）

**核心问题**：
- 19 个属性混用同一 `ConcurrentDictionary`，配置级与控件级数据无区分
- 快照机制无差别恢复，`WorkflowDefinitions` 等配置级数据被误清除
- 父子级联查找导致意外的数据共享

**InternalStorage 覆盖方案**：
- 用分层存储（Config/Session/Transient）替代单一 `ConcurrentDictionary`
- 快照只作用于 Session 层，Config 层不受影响
- 用显式作用域替代隐式父子级联

### 2.2 体系二：`EventBindingOptions`（构造函数注入的配置对象）

**核心问题**：
- 12 个属性通过构造函数注入，与体系一存在数据双重拷贝
- `ApplyOptions` 桥接方法手动同步两份数据，无自动同步机制

**InternalStorage 覆盖方案**：
- 所有运行时配置统一通过 `IStorageEngine` 的 Config 层访问
- 消除 `EventBindingOptions` 与 `EventBindingContext` 之间的数据拷贝
- 通过 `IStorageEngine` 提供统一的生命周期管理

### 2.3 体系三：`[JsonExtensionData]`（JSON 反序列化的隐式存储）

**核心问题**：
- `workflows` 等核心业务数据通过 `[JsonExtensionData]` 隐式存储
- 硬编码字符串 `"workflows"` 访问，无编译期检查
- 提取后通过 `SetData` 存入体系一，依赖快照机制的正确性

**InternalStorage 覆盖方案**：
- 提供显式的类型安全注册机制替代 `[JsonExtensionData]`
- 通过 `ITransformMetadata` 元数据接口自动发现和注册
- 数据直接存入 Config 层，不受快照影响

### 2.4 体系四：`PipelineContextBase.SharedData`（管线共享数据）

**核心问题**：
- `SharedData` 与 `SetData`/`GetData` 是两套独立的键值存储，用途重叠
- 开发者无明确规则判断数据应存入哪套存储

**InternalStorage 覆盖方案**：
- `SharedData` 的用途明确为 Transient 层（临时数据，不参与快照）
- 统一通过 `IStorageCell` 的 Transient 层访问，消除两套存储的混淆

---

## 三、InternalStorage 架构设计

### 3.1 核心抽象层

```
┌──────────────────────────────────────────────────────────────────┐
│                      InternalStorage                              │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │                    IStorageCell                            │    │
│  │  ┌──────────────┐  ┌──────────────┐  ┌────────────────┐  │    │
│  │  │  Config Layer │  │ Session Layer│  │ Transient Layer│  │    │
│  │  │  (体系二替代)  │  │ (体系一替代)  │  │ (体系四替代)   │  │    │
│  │  │  Preserve     │  │  Snapshot    │  │  Exclude       │  │    │
│  │  └──────────────┘  └──────────────┘  └────────────────┘  │    │
│  └──────────────────────────────────────────────────────────┘    │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │                   ISnapshotManager                         │    │
│  │  ┌──────────────┐  ┌──────────────┐  ┌────────────────┐  │    │
│  │  │ 分层快照      │  │ 差异计算      │  │  版本管理      │  │    │
│  │  │ (只作用于     │  │ (借鉴        │  │ (借鉴          │  │    │
│  │  │  Session层)   │  │  GraphDiff)  │  │  GraphSnapshot)│  │    │
│  │  └──────────────┘  └──────────────┘  └────────────────┘  │    │
│  └──────────────────────────────────────────────────────────┘    │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │                   IStorageEngine                           │    │
│  │  ┌──────────────┐  ┌──────────────┐  ┌────────────────┐  │    │
│  │  │ 会话管理      │  │ 生命周期      │  │  统一入口      │  │    │
│  │  │ (多会话隔离)  │  │ (状态机)      │  │ (替代体系二)   │  │    │
│  │  └──────────────┘  └──────────────┘  └────────────────┘  │    │
│  └──────────────────────────────────────────────────────────┘    │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │              IStorageRegistry（体系三替代）                 │    │
│  │  ┌──────────────┐  ┌──────────────┐                     │    │
│  │  │ 类型安全注册  │  │ 元数据自动    │                     │    │
│  │  │ (替代         │  │ 发现          │                     │    │
│  │  │  JsonExtData) │  │ (借鉴 Metadata)│                    │    │
│  │  └──────────────┘  └──────────────┘                     │    │
│  └──────────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────────┘
```

### 3.2 分层存储设计

| 层级 | 生命周期 | 快照行为 | 覆盖的现有体系 | 用途 |
|------|---------|----------|---------------|------|
| **Config Layer** | 应用级 | `Preserve`（不受快照影响） | 体系二（`EventBindingOptions`）+ 体系三（`[JsonExtensionData]` 中的 workflows） | 配置、工作流定义、注册信息、运行时服务 |
| **Session Layer** | 管线级 | `Snapshot`（受快照管理） | 体系一（`EventBindingContext` 的控件级属性） | 当前控件、当前视图、临时绑定数据 |
| **Transient Layer** | 单次操作级 | `Exclude`（不参与快照） | 体系四（`SharedData`） | 循环变量、临时计算结果、跳过标志 |

### 3.3 快照行为枚举

```csharp
public enum SnapshotBehavior
{
    /// <summary>快照时保存，恢复时还原（Session 层默认行为）</summary>
    Snapshot,
    /// <summary>快照时保存，但恢复时保留（Config 层默认行为）</summary>
    Preserve,
    /// <summary>不参与快照（Transient 层默认行为）</summary>
    Exclude
}
```

### 3.4 核心接口设计

```csharp
// ========== 存储层级 ==========

/// <summary>存储层级标识</summary>
public enum StorageLayer
{
    Config,    // 配置级（全局，不受快照影响）— 覆盖体系二、体系三
    Session,   // 会话级（受快照管理）— 覆盖体系一
    Transient  // 临时级（不参与快照）— 覆盖体系四
}

/// <summary>存储条目（带元数据）</summary>
public class StorageEntry
{
    public object? Value { get; set; }
    public StorageLayer Layer { get; init; }
    public SnapshotBehavior SnapshotBehavior { get; init; }
    public DateTime CreatedAt { get; init; }
    public TimeSpan? Ttl { get; init; }  // 可选 TTL
}

// ========== 存储单元（覆盖体系一、体系四）==========

/// <summary>存储单元 — 带分层的键值存储</summary>
public interface IStorageCell
{
    string Name { get; }
    
    // 基础操作
    void Set<T>(string key, T value, StorageLayer layer = StorageLayer.Session);
    T? Get<T>(string key, StorageLayer? layer = null);
    bool TryGet<T>(string key, out T? value, StorageLayer? layer = null);
    bool Remove(string key, StorageLayer? layer = null);
    bool Contains(string key, StorageLayer? layer = null);
    
    // 层级操作
    void ClearLayer(StorageLayer layer);
    IEnumerable<string> GetKeys(StorageLayer layer);
    
    // 数据变更通知（继承 ContextBase 的事件机制）
    event EventHandler<StorageDataChangedEventArgs>? DataChanged;
}

// ========== 快照管理（覆盖体系一的快照问题）==========

/// <summary>分层快照 — 只包含 Session 层数据</summary>
public interface ILayeredSnapshot
{
    string Id { get; }
    DateTime CreatedAt { get; }
    IReadOnlyDictionary<string, object?> SessionData { get; }
    // Config 层和 Transient 层数据不在此处
}

/// <summary>快照管理器</summary>
public interface ISnapshotManager
{
    ILayeredSnapshot CreateSnapshot(IStorageCell cell);
    void RestoreSnapshot(IStorageCell cell, ILayeredSnapshot snapshot);
    ISnapshotDiff CalculateDiff(ILayeredSnapshot before, ILayeredSnapshot after);
}

// ========== 存储引擎（覆盖体系二）==========

/// <summary>存储引擎 — 统一生命周期管理</summary>
public interface IStorageEngine : IDisposable
{
    string Name { get; }
    IStorageCell RootCell { get; }
    
    // 会话管理
    IStorageCell CreateSession(string sessionId);
    void DisposeSession(string sessionId);
    
    // 快照
    ILayeredSnapshot CreateSnapshot(string sessionId);
    void RestoreSnapshot(string sessionId, ILayeredSnapshot snapshot);
    
    // 生命周期
    event EventHandler<StorageEngineStateChangedEventArgs>? StateChanged;
}

// ========== 存储注册中心（覆盖体系三）==========

/// <summary>存储注册中心 — 替代 [JsonExtensionData] 的隐式存储</summary>
public interface IStorageRegistry
{
    // 类型安全注册
    void Register<T>(string key, T value) where T : notnull;
    T? Resolve<T>(string key) where T : notnull;
    bool TryResolve<T>(string key, out T? value) where T : notnull;
    
    // 批量注册（替代 ExtensionData 的隐式收集）
    void RegisterFromJson(Dictionary<string, object?> extensionData);
    void RegisterFromJsonElement(Dictionary<string, JsonElement> extensionData);
    
    // 元数据发现（借鉴 ITransformMetadata）
    IReadOnlyDictionary<string, Type> GetRegisteredTypes();
}
```

---

## 四、复用映射表

### 4.1 直接复用（引用现有库）

| InternalStorage 组件 | 复用来源 | 复用方式 |
|---------------------|---------|----------|
| `IStorageCell` 基础接口 | `ContextFrame.Abstractions.IStateStore` | 直接引用接口，扩展分层参数 |
| `IStorageCell` 基础实现 | `ContextFrame.Core.StateStoreBase` | 继承 + 扩展分层逻辑 |
| `ILayeredSnapshot` | `ContextFrame.Abstractions.IContextSnapshot` | 继承接口，扩展分层 |
| `ISnapshotManager` | `ContextFrame.Abstractions.ISnapshotable` | 继承接口，扩展分层 |
| 快照创建/恢复逻辑 | `ContextFrame.Utilities.ContextExtensions` | 复用 `CreateDataSnapshot` / `RestoreDataFromSnapshot`，增加分层过滤 |
| 父子级联查找 | `ContextFrame.Core.ContextBase` | 复用 `GetData` 的父子级联逻辑 |
| 数据变更通知 | `ContextFrame.Core.ContextBase.DataChanged` | 复用事件机制 |
| 服务定位 | `ContextFrame.Core.ContextBase.GetOrAddService` | 复用 |
| 作用域管理 | `ContextFrame.Scoping.ContextScope` | 复用 |
| 锁机制 | `ContextFrame.Locking.Lock` | 复用 |

### 4.2 设计借鉴（参考模式，不直接引用）

| InternalStorage 组件 | 参考来源 | 参考内容 |
|---------------------|---------|----------|
| 分层存储架构 | `Map/Transformers/TransformContext` | `AsyncLocal` 隐式传播 + 父子作用域嵌套 |
| 快照差异计算 | `Map/GraphFoundation/Snapshots/GraphDiff` | 两个快照间的变更集计算模式 |
| 不可变快照 | `Map/GraphFoundation/Snapshots/GraphSnapshot` | 版本号 + 创建时间 + 只读视图 |
| 存储索引 | `Map/GraphFoundation/Storage/InMemoryGraphStorage` | `ConcurrentDictionary` + 双向索引 |
| 引擎生命周期 | `Map/Container/Core/ExecutionEngine` | 状态机 + 超时/取消处理 |
| 行为装饰器 | `Map/Container/Behaviors/` | `CircuitBreaker` / `Retry` / `Timeout` 模式 |
| 元数据注册 | `Map/Transformers/Metadata.cs` | `ITransformMetadata` 自动发现 |
| 多实例管理 | `Map/StateMachine/StateMachineStore` | TTL 自动清理 + 容量控制 |
| 并发控制 | `Map/StateMachine/StateManager` | `SemaphoreSlim` 限流 |
| 块执行管线 | `Map/Block/ExecutionPipeline` | 顺序执行 + 条件跳过 + 重试 |

### 4.3 需要新增的逻辑

| 组件 | 说明 |
|------|------|
| `StorageLayer` 枚举 | 三层存储层级定义 |
| `SnapshotBehavior` 枚举 | 快照行为定义 |
| `StorageEntry` 类 | 带元数据的存储条目 |
| 分层快照创建/恢复 | 按 `SnapshotBehavior` 过滤的快照逻辑 |
| `IStorageEngine` 会话管理 | 多会话创建/销毁 |
| `IStorageRegistry` 注册中心 | 替代 `[JsonExtensionData]` 的显式注册 |

---

## 五、覆盖路径：4 套体系 → InternalStorage

### 5.1 体系一覆盖路径

```
当前：
  EventBindingContext (PipelineContextBase)
    ├── SetData/GetData (ConcurrentDictionary) ← 19 个属性混存
    ├── CreateSnapshot → 全部数据
    └── RestoreSnapshot → 全部数据（误清除 WorkflowDefinitions）

覆盖后：
  EventBindingContext → 依赖 IStorageCell
    ├── Config Layer: WorkflowDefinitions, EventBus, SearchRoot, ...
    ├── Session Layer: CurrentControlConfig, CurrentView, CurrentOutput, ...
    ├── Transient Layer: SkipCurrentControl, ...
    ├── CreateSnapshot → 只作用于 Session Layer
    └── RestoreSnapshot → 只恢复 Session Layer（Config 层不受影响）
```

### 5.2 体系二覆盖路径

```
当前：
  EventBindingOptions (构造函数注入)
    ├── 12 个属性
    └── ApplyOptions → 手动拷贝到 EventBindingContext

覆盖后：
  EventBindingOptions → 废弃，改为：
    ├── 通过 IStorageEngine.RootCell.Config 层访问
    └── 运行时配置直接注册到 Config Layer
    └── 消除数据双重拷贝
```

### 5.3 体系三覆盖路径

```
当前：
  [JsonExtensionData] Dictionary<string, object?>
    ├── workflows → 硬编码字符串访问
    └── BindingMetadataTransformer.ExtractFromConfig → SetData

覆盖后：
  IStorageRegistry
    ├── RegisterFromJsonElement(extensionData) → 自动注册到 Config Layer
    ├── Resolve<WorkflowDefinitions>("workflows") → 类型安全访问
    └── 消除硬编码字符串
```

### 5.4 体系四覆盖路径

```
当前：
  PipelineContextBase.SharedData (ConcurrentDictionary)
    ├── "DiagnosticsScope" → IDisposable
    ├── "SkipPipeline" → bool
    └── 与 SetData 用途重叠

覆盖后：
  IStorageCell.Transient Layer
    ├── Set("DiagnosticsScope", scope, StorageLayer.Transient)
    ├── Set("SkipPipeline", true, StorageLayer.Transient)
    └── 统一通过 IStorageCell 访问，消除两套存储的混淆
```

---

## 六、数据流全景图（覆盖后）

```
AgentConfigUI.json
  │
  ▼
JSON 反序列化（System.Text.Json）
  │
  ├─→ ControlConfig.Events.EventsDictionary ←── 保留（显式属性）
  │     "afterselect" → ["workflowTreeView_afterSelect"]
  │
  ├─→ ControlConfig.Events.Bindings.Parameters ←── 保留（显式属性）
  │
  ├─→ ControlConfig.Events.Bindings.ExtensionData ←── IStorageRegistry 接管
  │     "workflows" → RegisterFromJsonElement() → Config Layer
  │
  ▼
BindingMetadataTransformer.ExtractFromConfig
  │
  ├─→ input.CurrentParameterBindings = ... ←── Session Layer（受快照管理）
  ├─→ input.WorkflowDefinitions = ...      ←── Config Layer（不受快照影响）
  │
  ▼
GenericEventBindingPipeline.WalkAndBind
  │
  ├─→ context.CreateSnapshot()              ←── 只作用于 Session Layer
  ├─→ Execute(context)                      ←── Transformer 链执行
  ├─→ context.RestoreSnapshot(snapshot)     ←── 只恢复 Session Layer
  │                                           ✅ WorkflowDefinitions 不受影响
  ▼
EventBinderTransformer → ConfigBindingStage
  │
  ├─→ EventsDictionary["afterselect"] → ["workflowTreeView_afterSelect"]
  ├─→ CreateEventHandler(control, "AfterSelect", "workflowTreeView_afterSelect", ...)
  │
  ▼
事件触发时（lambda 执行）
  │
  ├─→ bindingContext?.WorkflowDefinitions  ←── Config Layer（✅ 存在！）
  ├─→ workflowDefinitions.ContainsKey("workflowTreeView_afterSelect") → true
  ├─→ 执行工作流定义中的节点逻辑 ←── ✅ 正常执行
```

---

## 七、迁移路径

### 7.1 阶段一：构建 InternalStorage 核心

```
InternalStorage/
├── InternalStorage.csproj
├── Abstractions/
│   ├── IStorageCell.cs          # 存储单元接口（覆盖体系一、四）
│   ├── IStorageEngine.cs        # 存储引擎接口（覆盖体系二）
│   ├── IStorageRegistry.cs      # 存储注册中心接口（覆盖体系三）
│   ├── ILayeredSnapshot.cs      # 分层快照接口
│   ├── ISnapshotManager.cs      # 快照管理器接口
│   ├── ISnapshotDiff.cs         # 快照差异接口
│   ├── StorageLayer.cs          # 存储层级枚举
│   └── SnapshotBehavior.cs      # 快照行为枚举
├── Core/
│   ├── StorageCell.cs           # 存储单元实现（复用 StateStoreBase）
│   ├── StorageEngine.cs         # 存储引擎实现（复用 ExecutionEngine）
│   ├── StorageRegistry.cs       # 存储注册中心实现
│   ├── LayeredSnapshot.cs       # 分层快照实现（复用 ContextSnapshot）
│   └── SnapshotManager.cs       # 快照管理器实现（复用 ContextExtensions）
├── Extensions/
│   └── StorageCellExtensions.cs # 扩展方法
└── InternalStorage.csproj
```

### 7.2 阶段二：替换现有体系

1. **替换体系一**：`EventBindingContext` 内部改用 `IStorageCell`，配置级数据存 Config Layer，控件级数据存 Session Layer
2. **替换体系二**：废弃 `EventBindingOptions`，运行时配置通过 `IStorageEngine` 的 Config 层访问
3. **替换体系三**：`BindingMetadataTransformer` 改用 `IStorageRegistry` 注册 workflows 到 Config Layer
4. **替换体系四**：`SharedData` 的使用方改用 `IStorageCell.Transient` 层

### 7.3 阶段三：清理旧代码

1. 移除 `EventBindingContext` 中 19 个 `SetData`/`GetData` 属性
2. 移除 `EventBindingOptions` 类
3. 移除 `[JsonExtensionData]` 的 workflows 提取逻辑
4. 移除 `PipelineContextBase.SharedData`（或标记为过时）

---

## 八、关键设计决策

### 8.1 为什么用分层存储而非标签？

**方案 A（标签）**：在每个 `SetData` 调用时附加 `SnapshotBehavior` 标签
- 优点：粒度细，每个数据项可独立控制
- 缺点：调用方需要知道每个数据的快照行为，侵入性强

**方案 B（分层）**：按生命周期将数据分为 Config/Session/Transient 三层
- 优点：调用方只需选择层级，快照行为由层级决定，零侵入
- 缺点：粒度粗，同一层级内所有数据快照行为一致

**选择**：**方案 B（分层）**，因为：
1. 当前问题中，数据归属权混乱的本质是**生命周期不匹配**，而非快照行为不一致
2. 分层更符合"配置级 vs 控件级"的自然分类
3. 可在分层基础上叠加标签机制作为扩展点

### 8.2 为什么复用 `ContextFrame` 而非 `Map/Container`？

`ContextFrame` 提供的是**纯数据存储抽象**（`IStateStore`、`IContext`），而 `Map/Container` 提供的是**执行容器抽象**（`IExecutionCell`、`IExecutionEngine`）。InternalStorage 的核心需求是数据存储管理，因此：

- **直接复用**：`ContextFrame` 的 `IStateStore`、`IContextSnapshot`、`ISnapshotable`
- **设计借鉴**：`Map/Container` 的引擎生命周期模式、行为装饰器模式
- **不直接依赖**：`Map/Container` 的 MediatR 委托（InternalStorage 不需要命令分发）

### 8.3 为什么需要 `IStorageEngine`？

当前 4 套存储体系各自为政，没有统一的生命周期管理。`IStorageEngine` 提供：
1. **统一入口**：所有存储操作通过引擎访问
2. **会话管理**：支持多会话隔离（如 contentContainer vs sideContainer）
3. **生命周期事件**：状态变更通知（如 `StateChanged`）
4. **资源清理**：统一 Dispose，防止内存泄漏

### 8.4 为什么需要 `IStorageRegistry`？

`[JsonExtensionData]` 是 JSON 反序列化的"兜底"机制，用于捕获未映射的属性。但 `workflows` 是核心业务数据，不应通过兜底机制存储。`IStorageRegistry` 提供：
1. **类型安全**：`Register<T>` / `Resolve<T>` 替代 `Dictionary<string, object?>`
2. **自动注册**：`RegisterFromJsonElement` 批量处理 `ExtensionData`
3. **元数据发现**：借鉴 `ITransformMetadata`，支持自动发现已注册的类型

---

## 九、文件索引

| 文件 | 可复用内容 | 行号 |
|------|-----------|------|
| `CsharpPrj/BasicMethod/ContextFrame/Abstractions/IStateStore.cs` | `IStateStore` 接口 | 22-69 |
| `CsharpPrj/BasicMethod/ContextFrame/Abstractions/IContextSnapshot.cs` | `IContextSnapshot` + `ISnapshotable` | 23-52 |
| `CsharpPrj/BasicMethod/ContextFrame/Abstractions/IContext.cs` | `SetData`/`GetData` 接口 + 父子级联 | 54-100 |
| `CsharpPrj/BasicMethod/ContextFrame/Core/StateStoreBase.cs` | `ConcurrentDictionary` 键值存储 | 27-66 |
| `CsharpPrj/BasicMethod/ContextFrame/Core/ContextBase.cs` | 父子级联 + 数据变更通知 + 服务定位 | 32-242 |
| `CsharpPrj/BasicMethod/ContextFrame/Core/PipelineContextBase.cs` | `SharedData` + 阶段状态跟踪 | 33-130 |
| `CsharpPrj/BasicMethod/ContextFrame/Utilities/ContextExtensions.cs` | `CreateDataSnapshot` / `RestoreDataFromSnapshot` | 137-170 |
| `CsharpPrj/BasicMethod/ContextFrame/Utilities/ContextSnapshot.cs` | `IContextSnapshot` 实现 | - |
| `CsharpPrj/BasicMethod/ContextFrame/Scoping/ContextScope.cs` | 作用域管理 | - |
| `CsharpPrj/BasicMethod/ContextFrame/Locking/Lock.cs` | 锁机制 | - |
| `CsharpPrj/BasicMethod/Map/Transformers/TransformContext.cs` | `AsyncLocal` 隐式传播 + 父子作用域 + 快照 | 28-174 |
| `CsharpPrj/BasicMethod/Map/GraphFoundation/Snapshots/GraphSnapshot.cs` | 不可变快照 + 版本管理 | 19-261 |
| `CsharpPrj/BasicMethod/Map/GraphFoundation/Snapshots/GraphDiff.cs` | 差异计算 | 17-121 |
| `CsharpPrj/BasicMethod/Map/GraphFoundation/Storage/InMemoryGraphStorage.cs` | `ConcurrentDictionary` + 双向索引 | 59-309 |
| `CsharpPrj/BasicMethod/Map/Container/Core/ExecutionEngine.cs` | 引擎生命周期 + 状态机 | 18-205 |
| `CsharpPrj/BasicMethod/Map/Container/Abstractions/IRuntimeContext.cs` | `Items` 字典 + 服务定位 | 15-36 |
| `CsharpPrj/BasicMethod/Map/StateMachine/StateMachineStore.cs` | TTL 自动清理 + 容量控制 | 30-115 |
| `CsharpPrj/BasicMethod/Map/StateMachine/StateManager.cs` | `SemaphoreSlim` 并发控制 | 37-184 |
| `CsharpPrj/BasicMethod/Map/Block/ExecutionPipeline.cs` | 顺序执行 + 条件跳过 + 重试 | 29-336 |
