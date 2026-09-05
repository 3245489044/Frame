# ECS 渲染管线重构 — 调用链治理与职责整合

> **版本：** v1.0
> **维护者：** WorkFlowPrj Team
> **更新日期：** 2026-07-21
> **目标：** 消除 ECS 渲染管线中的透传链过长、隐式顺序依赖、职责错位等架构缺陷，将 4 层调用链扁平化为 2 层
> **适用范围：** `FrontEndDriven.ECS` 命名空间下的渲染管线相关类
> **关联文档：** [架构重构模式 — 常见架构缺陷与应对策略](./架构重构模式%20—%20常见架构缺陷与应对策略.md)

---

## 目录

- [ECS 渲染管线重构 — 调用链治理与职责整合](#ecs-渲染管线重构--调用链治理与职责整合)
  - [目录](#目录)
  - [1. 重构背景](#1-重构背景)
    - [1.1 当前调用链全景图](#11-当前调用链全景图)
    - [1.2 根因分析](#12-根因分析)
  - [2. 缺陷模式诊断](#2-缺陷模式诊断)
    - [2.1 缺陷模式 1：透传链过长](#21-缺陷模式-1透传链过长)
    - [2.2 缺陷模式 2：重构残留](#22-缺陷模式-2重构残留)
    - [2.3 缺陷模式 3：双数据源不一致](#23-缺陷模式-3双数据源不一致)
    - [2.4 缺陷模式 4：职责错位](#24-缺陷模式-4职责错位)
    - [2.5 缺陷模式 5：隐式顺序依赖](#25-缺陷模式-5隐式顺序依赖)
    - [2.6 缺陷模式 6：逻辑分散](#26-缺陷模式-6逻辑分散)
  - [3. 重构目标](#3-重构目标)
    - [3.1 调用链对比](#31-调用链对比)
    - [3.2 类职责重分配](#32-类职责重分配)
  - [4. 具体变更方案](#4-具体变更方案)
    - [4.1 变更 1：合并 Adapter + Integration → EcsRenderPipeline](#41-变更-1合并-adapter--integration--ecsrenderpipeline)
    - [4.2 变更 2：为根 Entity 添加 ControlTypeComponent](#42-变更-2为根-entity-添加-controltypecomponent)
    - [4.3 变更 3：简化 EntityRegistry](#43-变更-3简化-entityregistry)
    - [4.4 变更 4：简化 EcsConfigContainer.RenderConfigAsync](#44-变更-4简化-ecsconfigcontainerrenderconfigasync)
    - [4.5 变更 5：清理废弃代码](#45-变更-5清理废弃代码)
  - [5. 影响范围](#5-影响范围)
    - [5.1 文件变更清单](#51-文件变更清单)
    - [5.2 外部依赖检查](#52-外部依赖检查)
  - [6. 实施步骤](#6-实施步骤)
    - [6.1 实施顺序](#61-实施顺序)
    - [6.2 验证方法](#62-验证方法)
  - [7. 附录：代码对比](#7-附录代码对比)
    - [7.1 EcsRenderPipeline 新类设计](#71-ecsrenderpipeline-新类设计)
    - [7.2 ConfigToEntityConverter 变更](#72-configtoentityconverter-变更)
    - [7.3 EntityRegistry 变更](#73-entityregistry-变更)

---

## 1. 重构背景

### 1.1 当前调用链全景图

ECS 渲染管线负责将 `UIConfiguration` 配置转换为 WinForms 控件树。当前调用链涉及 4 个类、7 个步骤：

```
EcsConfigContainer.RenderConfigAsync(config)
  │
  ├── [1] SuspendLayout() / Controls.Clear() / ResumeLayout()
  │
  ├── [2] pipeline.ExecuteCreateStageAsync(this, config)
  │     └── EcsRenderPipelineAdapter.ExecuteCreateStageAsync(target, config)    ← 第 1 层透传
  │           ├── [2a] _integration.Initialize()
  │           │     └── FrontEndEcsIntegration.Initialize()                     ← 第 2 层透传
  │           │           ├── 创建 EcsWorld, ControlPool, ControlFactory
  │           │           ├── 创建 6 个 System 实例
  │           │           └── 创建 EntityRegistry
  │           │
  │           ├── [2b] _integration.LoadConfiguration(uiConfig)
  │           │     └── FrontEndEcsIntegration.LoadConfiguration()
  │           │           └── _configToEntitySystem.Execute(config)
  │           │                 └── ConfigToEntitySystem.Execute()
  │           │                       └── _converter.Convert(config)
  │           │                             └── ConfigToEntityConverter.Convert()
  │           │                                   ├── 创建根 Entity (type="form")
  │           │                                   ├── 递归创建 View/Control Entity
  │           │                                   └── 添加 ContainerAncestorComponent
  │           │
  │           ├── [2c] _integration.RegisterRootContainer(targetControl)
  │           │     └── FrontEndEcsIntegration.RegisterRootContainer()
  │           │           └── _entityRegistry.RegisterRoot(ecsContainer, rootEntity)
  │           │                 └── _controlPool.Register(rootEntity, rootContainer.Name, rootContainer)
  │           │                       ↑ rootContainer.Name 为 null → ArgumentNullException
  │           │
  │           ├── [2d] _integration.ExecuteStage(PipelineStage.ControlCreate)
  │           │     └── FrontEndEcsIntegration.ExecuteStage()
  │           │           └── _controlCreateSystem.Update()
  │           │                 ├── 阶段 1：遍历有 ControlTypeComponent 的 Entity，创建控件并注册到 ControlPool
  │           │                 │     ↑ 根 Entity (type="form") 没有 ControlTypeComponent → 跳过
  │           │                 └── 阶段 2：遍历 Entity，查找父控件并添加到 Controls 集合
  │           │                       ↑ 子 Entity 的 Parent 指向根 Entity → FindParent 返回 null → NullReferenceException
  │           │
  │           └── [2e] _integration.RegisterAllSubContainers()
  │                 └── FrontEndEcsIntegration.RegisterAllSubContainers()
  │                       └── _entityRegistry.RegisterAllSubContainers()
  │                             └── RegisterSubContainersRecursive(_rootEntity)
  │                                   ↑ 冗余验证：ControlCreateSystem 已注册所有控件
  │
  ├── [3] pipeline.ExecuteLayoutStageAsync(this)
  │
  └── [4] ConfigLoaded?.Invoke(this, success=true) 或 catch 异常后 return
```

### 1.2 根因分析

| 根因 | 说明 | 触发异常 |
|------|------|---------|
| **根 Entity 缺少 ControlTypeComponent** | `ConfigToEntityConverter.Convert()` 创建根 Entity 时只添加了 `ContainerAncestorComponent`，没有添加 `ControlTypeComponent`。`ControlCreateSystem` 只处理有 `ControlTypeComponent` 的 Entity，导致根 Entity 被跳过 | `NullReferenceException` — `parent.Controls.Add(control)` 中 parent 为 null |
| **Adapter + Integration 纯透传** | `EcsRenderPipelineAdapter` 的 7 个方法全部委托给 `FrontEndEcsIntegration`，自身不做任何实质性处理 | 调用链过长，调试困难 |
| **EntityRegistry 职责过载** | 既负责注册根容器，又负责遍历 Entity 树验证子容器注册，后者与 `ControlCreateSystem` 阶段 1 功能重叠 | 冗余逻辑，双数据源不一致风险 |

---

## 2. 缺陷模式诊断

### 2.1 缺陷模式 1：透传链过长

**表现**：`EcsConfigContainer → EcsRenderPipelineAdapter → FrontEndEcsIntegration → EntityRegistry/ControlCreateSystem`，4 层传递。

```
EcsConfigContainer.RenderConfigAsync
  → WdUIComposer.RenderPipeline.ExecuteCreateStageAsync    ← 接口调用
    → EcsRenderPipelineAdapter.ExecuteCreateStageAsync     ← 第 1 层透传（仅参数校验）
      → _integration.Initialize()                          ← 第 2 层透传
      → _integration.LoadConfiguration()                   ← 第 2 层透传
      → _integration.RegisterRootContainer()               ← 第 2 层透传
        → _entityRegistry.RegisterRoot()                   ← 第 3 层实际逻辑
      → _integration.ExecuteStage(ControlCreate)           ← 第 2 层透传
        → _controlCreateSystem.Update()                    ← 第 3 层实际逻辑
      → _integration.RegisterAllSubContainers()            ← 第 2 层透传
        → _entityRegistry.RegisterAllSubContainers()       ← 第 3 层实际逻辑
```

**影响**：
- 新增参数需修改 Adapter、Integration、System 三处签名
- 调试需追踪 4+ 层调用栈
- 中间层忘记调用某个方法时功能失效

**应对策略**：合并 Adapter 和 Integration，消除透传层。

### 2.2 缺陷模式 2：重构残留

**表现**：多次重构后遗留的废弃代码未清理。

| 残留代码 | 位置 | 说明 |
|---------|------|------|
| `ExecuteCreateStageOnlyAsync` | `EcsRenderPipelineAdapter` 第 212 行 | 与 `ExecuteCreateStageAsync` 完全相同的逻辑 |
| `ApplyLayoutAsync` | `EcsRenderPipelineAdapter` 第 231 行 | 与 `ExecuteLayoutStageAsync` 完全相同的逻辑 |
| `BindEventsAsync` | `EcsRenderPipelineAdapter` 第 250 行 | 与 `ExecuteBindEventsStageAsync` 完全相同的逻辑 |
| `ExecuteStage()` 枚举分支 | `FrontEndEcsIntegration` 第 283 行 | 与 `ExecutePipeline()` 功能重叠 |
| `RegisterSubContainersRecursive()` | `EntityRegistry` 第 125 行 | 与 `ControlCreateSystem` 阶段 1 功能重叠 |

**影响**：
- 新旧路径功能重叠，行为不一致时难以定位
- 维护成本增加（修改一处需同步修改另一处）

**应对策略**：全链路追踪，逐层清理废弃代码。

### 2.3 缺陷模式 3：双数据源不一致

**表现**：Entity 树（ECS 数据源）和 WinForms 控件树（UI 数据源）并存，操作 A 的数据时遍历的是 B。

| 操作 | 当前数据源 | 正确数据源 |
|------|-----------|-----------|
| 注册控件到 ControlPool | Entity 树遍历 | ControlCreateSystem 创建结果 |
| 查找父控件 | ControlPool | ControlPool |
| 注册子容器 | Entity 树遍历（`RegisterSubContainersRecursive`） | ControlCreateSystem 创建结果 |

**影响**：
- `RegisterSubContainersRecursive` 遍历 Entity 树，但依赖 ControlPool 中已有控件
- Entity 树中的 Entity 不一定在 ControlPool 中有对应控件（创建失败时）
- 根 Entity 没有对应控件，导致子 Entity 的 FindParent 失败

**应对策略**：操作哪个数据源的数据，就遍历哪个数据源。控件注册统一由 `ControlCreateSystem` 完成，`EntityRegistry` 只负责查询。

### 2.4 缺陷模式 4：职责错位

**表现**：ControlPool 的注册逻辑分散在三个类中。

| 操作 | 负责方 | 位置 |
|------|--------|------|
| 注册根容器到 ControlPool | `EntityRegistry.RegisterRoot` | `EntityRegistry.cs` 第 101 行 |
| 注册普通控件到 ControlPool | `ControlCreateSystem.Update` 阶段 1 | `ControlCreateSystem.cs` 第 92 行 |
| 验证子容器注册 | `EntityRegistry.RegisterSubContainersRecursive` | `EntityRegistry.cs` 第 143 行 |

**影响**：
- 数据不一致时难以定位责任方
- 新增注册逻辑需修改多处

**应对策略**：谁拥有数据，谁负责维护。ControlPool 的注册统一由 `ControlCreateSystem` 完成。

### 2.5 缺陷模式 5：隐式顺序依赖

**表现**：`ControlCreateSystem.Update()` 阶段 2 的 `FindParent` 依赖阶段 1 已注册所有控件，但根 Entity 没有 `ControlTypeComponent`，不被阶段 1 处理。

```csharp
// ControlCreateSystem.Update() 阶段 1 — 只处理有 ControlTypeComponent 的 Entity
var entities = _entityManager.GetEntities<ControlTypeComponent, ControlStateComponent>()
    .Where(e => !_entityManager.GetComponent<ControlStateComponent>(e).IsCreated)
    .ToList();

// 根 Entity (type="form") 没有 ControlTypeComponent → 不在 entities 中 → 跳过

// ControlCreateSystem.Update() 阶段 2 — FindParent 需要父控件已在 ControlPool 中
var parent = _strategy?.FindParent(entity);
// ↑ 子 Entity 的 hierarchy.Parent 指向根 Entity
//   根 Entity 不在 ControlPool 中 → FindParent 返回 null
//   → parent.Controls.Add(control) 抛出 NullReferenceException
```

**影响**：
- 运行时偶发错误（取决于 Entity 遍历顺序）
- 根 Entity 被静默跳过，不报错

**应对策略**：为根 Entity 添加 `ControlTypeComponent`，使其被阶段 1 处理。或者两阶段处理时确保根 Entity 被特殊处理。

### 2.6 缺陷模式 6：逻辑分散

**表现**：`ControlStrategyRegistry` 已解决控件类型分支问题，但容器策略和普通控件策略的创建逻辑仍有分散。

| 逻辑 | 位置 |
|------|------|
| 容器策略注册 | `ControlStrategyRegistry` 静态构造函数 |
| 标准控件策略注册 | `StandardControlStrategies.RegisterAll()` |
| 根 Entity 的 form 类型 | 无注册 → 返回 null |

**影响**：
- 新增控件类型需了解多个注册点
- form 类型未注册导致根 Entity 创建失败

**应对策略**：统一注册到 `ControlStrategyRegistry`，包括 form 类型。

---

## 3. 重构目标

### 3.1 调用链对比

**重构前（4 层）：**

```
EcsConfigContainer.RenderConfigAsync
  → WdUIComposer.RenderPipeline (IRenderPipeline)
    → EcsRenderPipelineAdapter      ← 透传层
      → FrontEndEcsIntegration      ← 透传层
        → EntityRegistry            ← 实际逻辑
        → ControlCreateSystem       ← 实际逻辑
```

**重构后（2 层）：**

```
EcsConfigContainer.RenderConfigAsync
  → EcsRenderPipeline               ← 合并后的单一入口
    → ControlCreateSystem           ← 直接调用
    → EntityRegistry (简化版)       ← 仅查询
```

### 3.2 类职责重分配

| 类 | 重构前职责 | 重构后职责 |
|---|-----------|-----------|
| `EcsRenderPipelineAdapter` | 透传 IRenderPipeline 调用到 Integration | ❌ 删除 |
| `FrontEndEcsIntegration` | 管理 EcsWorld 生命周期，编排 System 管线 | ❌ 删除 |
| **`EcsRenderPipeline`** | — | **🆕 新建**：合并 Adapter + Integration，直接实现 IRenderPipeline |
| `EntityRegistry` | 注册根容器 + 遍历 Entity 树验证子容器 + 查询 | 仅保留 RegisterRoot + FindContainer + FindControl |
| `ControlCreateSystem` | 两阶段创建控件 + 注册到 ControlPool | 不变（增加对根 Entity 的支持） |
| `ConfigToEntityConverter` | 创建 Entity 树，添加 ContainerAncestorComponent | 增加为根 Entity 添加 ControlTypeComponent |
| `EcsConfigContainer` | 通过 WdUIComposer.RenderPipeline 间接调用管线 | 直接调用 EcsRenderPipeline |

---

## 4. 具体变更方案

### 4.1 变更 1：合并 Adapter + Integration → EcsRenderPipeline

**目标**：消除透传链（缺陷模式 1）和重构残留（缺陷模式 2）。

**操作**：
- 删除 `EcsRenderPipelineAdapter.cs`
- 删除 `FrontEndEcsIntegration.cs`
- 新建 `EcsRenderPipeline.cs`

**新类设计**：

```csharp
// EcsRenderPipeline.cs — ECS 渲染管线
//
// 职责：管理 EcsWorld 生命周期，编排 System 执行管线，
// 实现 IRenderPipeline 接口，作为 ECS 渲染的唯一入口。
//
// 设计目标：
// - 合并 EcsRenderPipelineAdapter + FrontEndEcsIntegration，消除透传层
// - 直接实现 IRenderPipeline 接口
// - System 直接通过 ControlPool 操作控件
//
// 管线执行顺序：
//   ConfigToEntitySystem → ControlCreateSystem → LayoutSystem
//   → StyleSystem → EventBindingSystem → ControlSyncSystem

public class EcsRenderPipeline : IRenderPipeline, IDisposable
{
    // 内部组件（原 FrontEndEcsIntegration 的字段）
    private readonly IECSStorage _ecsStorage;
    private readonly IStorageCell _cell;
    private readonly IStorageRegistry _storageRegistry;
    private EcsWorld? _world;
    private EntityManager? _entityManager;
    private ControlPool? _controlPool;
    private ControlFactory? _controlFactory;
    private EntityRegistry? _entityRegistry;

    // System 实例
    private ConfigToEntitySystem? _configToEntitySystem;
    private ControlCreateSystem? _controlCreateSystem;
    private LayoutSystem? _layoutSystem;
    private StyleSystem? _styleSystem;
    private EventBindingSystem? _eventBindingSystem;
    private ControlSyncSystem? _controlSyncSystem;
    private ControlValueSyncSystem? _controlValueSyncSystem;

    // 事件
    public event Action<Entity>? ConversionCompleted;
    public event Action? PipelineCompleted;
    public event Action<Entity, string, string>? ControlCreated;
    public event EventHandler<RenderCompletedEventArgs>? RenderCompleted;

    // 属性
    public EcsWorld? World => _world;
    public EntityManager? EntityManager => _entityManager;
    public ControlPool? ControlPool => _controlPool;
    public EntityRegistry? EntityRegistry => _entityRegistry;
    public Entity RootEntity => _configToEntitySystem?.RootEntity ?? Entity.Invalid;
    public bool IsInitialized { get; private set; }

    // 事件绑定委托
    public Func<Control, string, string, object, Task>? EventBindHandler { get; set; }

    // ========== 核心方法 ==========

    // 原 FrontEndEcsIntegration.Initialize() — 内联
    public void Initialize() { ... }

    // 原 FrontEndEcsIntegration.LoadConfiguration() — 内联
    public Entity LoadConfiguration(UIConfiguration config) { ... }

    // 原 EcsRenderPipelineAdapter.ExecuteCreateStageAsync() — 直接实现
    public Task ExecuteCreateStageAsync(object target, object config) { ... }

    // 原 EcsRenderPipelineAdapter.ExecuteLayoutStageAsync() — 直接实现
    public Task ExecuteLayoutStageAsync(object target) { ... }

    // 原 FrontEndEcsIntegration.ExecutePipeline() — 内联
    public void ExecutePipeline() { ... }

    // 原 FrontEndEcsIntegration.RegisterRootContainer() — 内联
    public void RegisterRootContainer(Control rootContainer) { ... }

    // IRenderPipeline 接口实现
    public Task RenderAsync(object target, object config) { ... }
    public Task ExecuteBindEventsStageAsync(object target, object config) { ... }
    public void DisposeSession(object target) { ... }
    public object? GetActiveSession(object target) => null;

    // 辅助方法
    public ControlPool? GetControlPool() => _controlPool;
    public void RecalculateLayout() { ... }
    public Entity GetEntity(string controlName) { ... }
    public string? GetControlName(Entity entity) { ... }
}
```

**关键设计决策**：
- `ExecuteCreateStageAsync` 直接包含 Initialize → LoadConfiguration → RegisterRootContainer → ControlCreateSystem.Update 的完整流程
- 不再暴露 `ExecuteStage(PipelineStage)` 枚举方法，只保留 `ExecutePipeline()` 完整管线执行
- 移除所有"仅执行"的冗余方法

### 4.2 变更 2：为根 Entity 添加 ControlTypeComponent

**目标**：修复隐式顺序依赖（缺陷模式 5），使根 Entity 能被 `ControlCreateSystem` 处理。

**操作**：在 `ConfigToEntityConverter.Convert()` 中为根 Entity 添加 `ControlTypeComponent`。

```csharp
// ConfigToEntityConverter.cs — Convert() 方法变更

public Entity Convert(UIConfiguration config)
{
    var rootEntity = _controlFactory.CreateFromConfig(new ControlConfig
    {
        Type = "form",
        Name = "root",
        Text = config.Form?.Title ?? "Root"
    }, Entity.Invalid);

    // ★ 新增：为根 Entity 添加 ControlTypeComponent
    //   使 ControlCreateSystem 能识别并处理根 Entity
    _entityManager.AddComponent(rootEntity, new ControlTypeComponent
    {
        Type = "form",
        Name = "root"
    });

    // ★ 新增：为根 Entity 添加 ControlStateComponent
    //   标记创建状态
    _entityManager.AddComponent(rootEntity, new ControlStateComponent
    {
        IsCreated = false,
        LastUpdated = DateTime.UtcNow
    });

    // 容器祖先：根容器自身就是容器，AncestorEntity = 自身
    _entityManager.AddComponent(rootEntity, new ContainerAncestorComponent
    {
        AncestorEntity = rootEntity,
        IsContainer = true
    });

    // ... 其余逻辑不变
}
```

**配套变更**：在 `ControlStrategyRegistry` 中注册 `form` 类型的策略。

```csharp
// ControlStrategyRegistry.cs — 静态构造函数

static ControlStrategyRegistry()
{
    Register(new ContainerStrategy());
    // ★ 新增：注册 form 类型策略，返回 EcsConfigContainer 自身
    Register("form", () => new EcsConfigContainer());
    // 标准控件由 StandardControlStrategies.RegisterAll() 在初始化时调用
}
```

**影响**：
- `ControlCreateSystem` 阶段 1 会为根 Entity 创建 Control（即 `EcsConfigContainer` 自身）
- 阶段 2 的 `FindParent` 能在 ControlPool 中找到根 Entity 对应的 Control
- 子 Entity 的 `parent.Controls.Add(control)` 正常执行

### 4.3 变更 3：简化 EntityRegistry

**目标**：消除双数据源不一致（缺陷模式 3）和职责错位（缺陷模式 4）。

**操作**：
- 移除 `RegisterAllSubContainers()` 方法
- 移除 `RegisterSubContainersRecursive()` 方法
- 保留 `RegisterRoot()`、`FindContainer()`、`FindControl()` 方法
- 移除 `Clear()` 方法（由 ControlPool 统一管理）

```csharp
// EntityRegistry.cs — 简化后

public class EntityRegistry
{
    private readonly EntityManager _entityManager;
    private readonly ControlPool _controlPool;
    private readonly ConfigToEntityConverter _converter;
    private EcsConfigContainer? _rootContainer;
    private Entity _rootEntity;

    public EcsConfigContainer? RootContainer => _rootContainer;
    public Entity RootEntity => _rootEntity;
    public ConfigToEntityConverter Converter => _converter;

    public EntityRegistry(
        EntityManager entityManager,
        ControlPool controlPool,
        ConfigToEntityConverter converter) { ... }

    // ★ 保留：注册根容器到 ControlPool
    public void RegisterRoot(EcsConfigContainer rootContainer, Entity rootEntity) { ... }

    // ★ 保留：通过 ControlPool 查找容器
    public EcsContainer? FindContainer(string name) { ... }

    // ★ 保留：通过 ControlPool 查找控件
    public Control? FindControl(string name) { ... }

    // ★ 移除：RegisterAllSubContainers() — 冗余，ControlCreateSystem 已注册所有控件
    // ★ 移除：RegisterSubContainersRecursive() — 冗余
    // ★ 移除：Clear() — 由 ControlPool 统一管理
}
```

### 4.4 变更 4：简化 EcsConfigContainer.RenderConfigAsync

**目标**：消除透传链（缺陷模式 1）。

**操作**：
- 直接调用 `EcsRenderPipeline` 的方法，不再通过 `WdUIComposer.RenderPipeline` 间接调用
- 移除 `RegisterSubContainer()` 方法（不再需要）

```csharp
// EcsConfigContainer.cs — RenderConfigAsync 方法变更

private async Task RenderConfigAsync(CfgModels.UIConfiguration config)
{
    Lifecycle.TryTransitionTo(ContainerLifecycleState.Loading);
    var currentVersion = Interlocked.Increment(ref _renderVersion);

    SuspendLayout();
    try { Controls.Clear(); }
    finally { ResumeLayout(true); }

    _currentConfig = config;

    try
    {
        // ★ 重构：直接使用 EcsRenderPipeline，不再通过 WdUIComposer.RenderPipeline
        var pipeline = _ecsRenderPipeline;  // 直接持有 EcsRenderPipeline 实例
        await pipeline.ExecuteCreateStageAsync(this, config);
        await pipeline.ExecuteLayoutStageAsync(this);
    }
    catch (Exception ex)
    {
        Log("EcsConfigContainer_RenderConfigAsync_Exception",
            new { message = ex.Message, stackTrace = ex.StackTrace });
        ConfigLoaded?.Invoke(this, new ContainerConfigLoadedEventArgs(
            config.Form?.Title ?? "(unknown)", false,
            $"渲染配置时发生异常: {ex.Message}"));
        FrameworkErrors.Report("EcsConfigContainer.RenderConfigAsync",
            $"容器 '{Name}' 渲染配置时发生异常: {ex.Message}", ex,
            ErrorSeverity.Error);
        return;
    }

    // ... 正常路径
    ConfigLoaded?.Invoke(this, new ContainerConfigLoadedEventArgs(
        config.Form?.Title ?? "(unknown)", true, loadedConfig: config));
    FinalizeRender(config, currentVersion);
}
```

### 4.5 变更 5：清理废弃代码

**目标**：消除重构残留（缺陷模式 2）。

**操作**：

| 废弃代码 | 位置 | 处理方式 |
|---------|------|---------|
| `ExecuteCreateStageOnlyAsync` | `EcsRenderPipelineAdapter` 第 212 行 | ❌ 删除（类整体删除） |
| `ApplyLayoutAsync` | `EcsRenderPipelineAdapter` 第 231 行 | ❌ 删除（类整体删除） |
| `BindEventsAsync` | `EcsRenderPipelineAdapter` 第 250 行 | ❌ 删除（类整体删除） |
| `ExecuteStage()` 枚举分支 | `FrontEndEcsIntegration` 第 283 行 | ❌ 删除（类整体删除） |
| `PipelineStage` 枚举 | `FrontEndEcsIntegration` 第 460 行 | ❌ 删除（类整体删除） |
| `RegisterSubContainersRecursive` | `EntityRegistry` 第 125 行 | ❌ 删除 |
| `RegisterAllSubContainers` | `EntityRegistry` 第 111 行 | ❌ 删除 |
| `RegisterSubContainer` | `EcsConfigContainer` 第 278 行 | ❌ 删除 |
| `Clear()` | `EntityRegistry` 第 192 行 | ❌ 删除 |

---

## 5. 影响范围

### 5.1 文件变更清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `Operation/FrontEndDriven/ECS/EcsRenderPipelineAdapter.cs` | ❌ 删除 | 合并到 EcsRenderPipeline |
| `Operation/FrontEndDriven/ECS/FrontEndEcsIntegration.cs` | ❌ 删除 | 合并到 EcsRenderPipeline |
| `Operation/FrontEndDriven/ECS/EcsRenderPipeline.cs` | 🆕 新建 | 合并后的新类 |
| `BasicMethod/ECS/Conversion/ConfigToEntityConverter.cs` | ✏️ 修改 | 为根 Entity 添加 ControlTypeComponent |
| `Operation/FrontEndDriven/ECS/EntityRegistry.cs` | ✏️ 修改 | 移除冗余注册方法 |
| `Operation/FrontEndDriven/ECS/ControlStrategyRegistry.cs` | ✏️ 修改 | 注册 form 类型策略 |
| `Operation/FrontEndDriven/Container/EcsConfigContainer.cs` | ✏️ 修改 | 直接使用 EcsRenderPipeline |
| `Operation/FrontEndDriven/ECS/Systems/ControlCreateSystem.cs` | 🔍 检查 | 确认两阶段处理逻辑兼容根 Entity |

### 5.2 外部依赖检查

以下外部类引用了将被删除或修改的类，需要同步更新：

| 引用方 | 引用的类 | 需要更新 |
|--------|---------|---------|
| `WdUIComposer` | `IRenderPipeline RenderPipeline` 属性 | 改为 `EcsRenderPipeline` 类型 |
| `EcsConfigContainer` | `WdUIComposer.RenderPipeline` | 改为直接持有 `EcsRenderPipeline` |
| `ControlFinderWrapper` | `ControlPool` | 不变（ControlPool 未变更） |
| `WinFormsEventRegistrar` | `EcsConfigContainer` | 不变 |
| `UIEventBinder` | `EcsConfigContainer` | 不变 |
| `ConfigLoader` | `EcsConfigContainer` | 不变 |
| `ContainerEventManager` | `EcsConfigContainer` | 不变 |

---

## 6. 实施步骤

### 6.1 实施顺序

```
步骤 1：新建 EcsRenderPipeline.cs
  └── 合并 EcsRenderPipelineAdapter + FrontEndEcsIntegration 的逻辑

步骤 2：修改 ConfigToEntityConverter.cs
  └── 为根 Entity 添加 ControlTypeComponent + ControlStateComponent

步骤 3：修改 ControlStrategyRegistry.cs
  └── 注册 form 类型策略

步骤 4：简化 EntityRegistry.cs
  └── 移除 RegisterAllSubContainers / RegisterSubContainersRecursive / Clear

步骤 5：修改 EcsConfigContainer.cs
  └── 直接使用 EcsRenderPipeline，移除 RegisterSubContainer

步骤 6：更新 WdUIComposer 等外部引用方
  └── RenderPipeline 属性改为 EcsRenderPipeline 类型

步骤 7：删除废弃文件
  └── EcsRenderPipelineAdapter.cs
  └── FrontEndEcsIntegration.cs

步骤 8：构建验证
  └── dotnet build 确认无编译错误
```

### 6.2 验证方法

| 验证项 | 方法 | 预期结果 |
|-------|------|---------|
| 编译通过 | `dotnet build MainProj.sln` | 无错误 |
| 根 Entity 创建 | 调试 `ControlCreateSystem.Update()` 阶段 1 | 根 Entity 被处理，创建 EcsConfigContainer |
| FindParent 正常 | 调试 `ControlCreateSystem.Update()` 阶段 2 | 父控件在 ControlPool 中，parent 不为 null |
| 子容器注册 | 调试 `EntityRegistry.RegisterRoot` | 根容器正确注册到 ControlPool |
| 事件绑定 | 运行 UI 测试 | 控件事件正常触发 |
| 布局计算 | 运行 UI 测试 | 控件布局正常 |

---

## 7. 附录：代码对比

### 7.1 EcsRenderPipeline 新类设计

```csharp
// ============================================================
// EcsRenderPipeline — ECS 渲染管线
//
// 职责：管理 EcsWorld 生命周期，编排 System 执行管线，
// 实现 IRenderPipeline 接口，作为 ECS 渲染的唯一入口。
//
// ★ 2026-07-21 重构：合并 EcsRenderPipelineAdapter + FrontEndEcsIntegration，
//   消除 4 层透传链，扁平化为 2 层。
//
// 管线执行顺序：
//   ConfigToEntitySystem → ControlCreateSystem → LayoutSystem
//   → StyleSystem → EventBindingSystem → ControlSyncSystem
// ============================================================

public class EcsRenderPipeline : IRenderPipeline, IDisposable
{
    // ========== 字段（原 FrontEndEcsIntegration） ==========
    private readonly IECSStorage _ecsStorage;
    private readonly IStorageCell _cell;
    private readonly IStorageRegistry _storageRegistry;
    private EcsWorld? _world;
    private EntityManager? _entityManager;
    private ControlPool? _controlPool;
    private ControlFactory? _controlFactory;
    private EntityRegistry? _entityRegistry;
    private bool _initialized;
    private bool _disposed;

    // System 实例
    private ConfigToEntitySystem? _configToEntitySystem;
    private ControlCreateSystem? _controlCreateSystem;
    private LayoutSystem? _layoutSystem;
    private StyleSystem? _styleSystem;
    private EventBindingSystem? _eventBindingSystem;
    private ControlSyncSystem? _controlSyncSystem;
    private ControlValueSyncSystem? _controlValueSyncSystem;

    // ========== 属性 ==========
    public EcsWorld? World => _world;
    public EntityManager? EntityManager => _entityManager;
    public ControlPool? ControlPool => _controlPool;
    public EntityRegistry? EntityRegistry => _entityRegistry;
    public Entity RootEntity => _configToEntitySystem?.RootEntity ?? Entity.Invalid;
    public bool IsInitialized => _initialized;

    // 事件绑定委托
    public Func<Control, string, string, object, Task>? EventBindHandler { get; set; }

    // ========== 事件 ==========
    public event Action<Entity>? ConversionCompleted;
    public event Action? PipelineCompleted;
    public event Action<Entity, string, string>? ControlCreated;
    public event EventHandler<RenderCompletedEventArgs>? RenderCompleted;

    // ========== 构造函数 ==========
    public EcsRenderPipeline(
        IECSStorage ecsStorage,
        IStorageCell cell,
        IStorageRegistry storageRegistry,
        string? name = null)
    {
        _ecsStorage = ecsStorage ?? throw new ArgumentNullException(nameof(ecsStorage));
        _cell = cell ?? throw new ArgumentNullException(nameof(cell));
        _storageRegistry = storageRegistry ?? throw new ArgumentNullException(nameof(storageRegistry));
        _name = name ?? "EcsRenderPipeline";
    }

    // ========== 初始化 ==========
    public void Initialize()
    {
        ThrowIfDisposed();
        if (_initialized) return;

        // 1. 创建 EcsWorld
        _world = new EcsWorld(_ecsStorage, _storageRegistry);
        _entityManager = _world.EntityManager;

        // 2. 创建 ControlPool
        _controlPool = new ControlPool(_cell, _storageRegistry);

        // 3. 创建 ControlFactory
        _controlFactory = new ControlFactory(_entityManager);

        // 4. 创建 System 实例
        _configToEntitySystem = new ConfigToEntitySystem(_entityManager, _controlFactory);
        var creationStrategy = new EcsControlCreationStrategy(_entityManager, _controlPool);
        _controlCreateSystem = new ControlCreateSystem(_entityManager, _controlPool, creationStrategy);
        _layoutSystem = new LayoutSystem(_entityManager, _controlPool);
        _styleSystem = new StyleSystem(_entityManager, _controlPool);
        _eventBindingSystem = new EventBindingSystem(_entityManager, _controlPool, BindEvent);
        _controlSyncSystem = new ControlSyncSystem(_entityManager, _controlPool);
        _controlValueSyncSystem = new ControlValueSyncSystem(_entityManager, _controlPool);

        // 5. 创建 EntityRegistry
        _entityRegistry = new EntityRegistry(_entityManager, _controlPool, _configToEntitySystem);

        // 6. 连接事件
        ConnectEvents();

        _initialized = true;
    }

    // ========== 加载配置 ==========
    public Entity LoadConfiguration(UIConfiguration config)
    {
        ThrowIfDisposed();
        if (!_initialized) Initialize();

        _configToEntitySystem!.Execute(config);
        return _configToEntitySystem.RootEntity;
    }

    // ========== 注册根容器 ==========
    public void RegisterRootContainer(Control rootContainer)
    {
        ThrowIfDisposed();
        if (_entityRegistry == null)
            throw new InvalidOperationException("Pipeline not initialized");

        var rootEntity = _configToEntitySystem?.RootEntity ?? Entity.Invalid;
        if (!rootEntity.IsValid)
            throw new InvalidOperationException("No root entity found. Call LoadConfiguration first.");

        _entityRegistry.RegisterRoot(rootContainer, rootEntity);
    }

    // ========== 执行创建阶段 ==========
    public async Task ExecuteCreateStageAsync(object target, object config)
    {
        ThrowIfDisposed();

        // 1. 初始化
        Initialize();

        // 2. 加载配置
        var uiConfig = config as UIConfiguration
            ?? throw new ArgumentException("Config must be UIConfiguration", nameof(config));
        LoadConfiguration(uiConfig);

        // 3. 注册根容器
        var targetControl = target as Control
            ?? throw new ArgumentException("Target must be Control", nameof(target));
        RegisterRootContainer(targetControl);

        // 4. 执行 ControlCreateSystem
        _controlCreateSystem?.Update();

        await Task.CompletedTask;
    }

    // ========== 执行布局阶段 ==========
    public async Task ExecuteLayoutStageAsync(object target)
    {
        ThrowIfDisposed();
        _layoutSystem?.Update();
        await Task.CompletedTask;
    }

    // ========== 执行完整管线 ==========
    public void ExecutePipeline()
    {
        ThrowIfDisposed();
        if (!_initialized)
            throw new InvalidOperationException("Pipeline not initialized");

        _controlCreateSystem?.Update();
        _layoutSystem?.Update();
        _styleSystem?.Update();
        _eventBindingSystem?.Update();
        _controlSyncSystem?.Update();
        _controlValueSyncSystem?.Update();

        PipelineCompleted?.Invoke();
    }

    // ========== IRenderPipeline 接口实现 ==========
    public async Task RenderAsync(object target, object config)
    {
        await ExecuteCreateStageAsync(target, config);
        await ExecuteLayoutStageAsync(target);
    }

    public async Task ExecuteBindEventsStageAsync(object target, object config)
    {
        ThrowIfDisposed();
        _eventBindingSystem?.Update();
        await Task.CompletedTask;
    }

    public void DisposeSession(object target)
    {
        Dispose();
    }

    // ========== 辅助方法 ==========
    public ControlPool? GetControlPool() => _controlPool;

    public void RecalculateLayout()
    {
        _layoutSystem?.Update();
    }

    public Entity GetEntity(string controlName)
    {
        if (_controlPool == null) return Entity.Invalid;
        return _controlPool.GetEntity(controlName);
    }

    public string? GetControlName(Entity entity)
    {
        if (_controlPool == null) return null;
        return _controlPool.GetControlName(entity);
    }

    // ========== 事件连接 ==========
    private void ConnectEvents()
    {
        if (_configToEntitySystem != null)
            _configToEntitySystem.ConversionCompleted += OnConversionCompleted;
        if (_controlCreateSystem != null)
            _controlCreateSystem.ControlCreated += OnControlCreated;
    }

    private void OnConversionCompleted(Entity rootEntity)
    {
        ConversionCompleted?.Invoke(rootEntity);
    }

    private void OnControlCreated(Entity entity, string type, string name)
    {
        ControlCreated?.Invoke(entity, type, name);
    }

    // ========== 事件绑定 ==========
    private async Task BindEvent(Control control, string eventName, string handlerName, object target)
    {
        if (EventBindHandler != null)
            await EventBindHandler(control, eventName, handlerName, target);
    }

    // ========== IDisposable ==========
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _world?.Dispose();
        _controlPool?.Clear();
        _initialized = false;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EcsRenderPipeline));
    }
}
```

### 7.2 ConfigToEntityConverter 变更

```csharp
// ★ 重构前：Convert() 方法不为根 Entity 添加 ControlTypeComponent
public Entity Convert(UIConfiguration config)
{
    var rootEntity = _controlFactory.CreateFromConfig(new ControlConfig
    {
        Type = "form",
        Name = "root",
        Text = config.Form?.Title ?? "Root"
    }, Entity.Invalid);

    // 只添加了 ContainerAncestorComponent
    _entityManager.AddComponent(rootEntity, new ContainerAncestorComponent
    {
        AncestorEntity = rootEntity,
        IsContainer = true
    });

    // ★ 问题：没有添加 ControlTypeComponent → ControlCreateSystem 跳过根 Entity
    // ★ 问题：没有添加 ControlStateComponent → 阶段 1 的查询条件不匹配

    // ... 其余逻辑
}

// ★ 重构后：为根 Entity 添加 ControlTypeComponent + ControlStateComponent
public Entity Convert(UIConfiguration config)
{
    var rootEntity = _controlFactory.CreateFromConfig(new ControlConfig
    {
        Type = "form",
        Name = "root",
        Text = config.Form?.Title ?? "Root"
    }, Entity.Invalid);

    // ★ 新增：ControlTypeComponent — 使 ControlCreateSystem 能识别根 Entity
    _entityManager.AddComponent(rootEntity, new ControlTypeComponent
    {
        Type = "form",
        Name = "root"
    });

    // ★ 新增：ControlStateComponent — 标记创建状态
    _entityManager.AddComponent(rootEntity, new ControlStateComponent
    {
        IsCreated = false,
        LastUpdated = DateTime.UtcNow
    });

    // 容器祖先：根容器自身就是容器
    _entityManager.AddComponent(rootEntity, new ContainerAncestorComponent
    {
        AncestorEntity = rootEntity,
        IsContainer = true
    });

    // ... 其余逻辑不变
}
```

### 7.3 EntityRegistry 变更

```csharp
// ★ 重构前：EntityRegistry 职责过载
public class EntityRegistry
{
    // ... 字段、构造函数

    public void RegisterRoot(EcsConfigContainer rootContainer, Entity rootEntity)
    {
        _rootContainer = rootContainer;
        _rootEntity = rootEntity;
        _controlPool.Register(rootEntity, rootContainer.Name, rootContainer);
        // ↑ rootContainer.Name 为 null → ArgumentNullException
    }

    // ★ 冗余：与 ControlCreateSystem 阶段 1 功能重叠
    public void RegisterAllSubContainers()
    {
        RegisterSubContainersRecursive(_rootEntity);
    }

    // ★ 冗余：遍历 Entity 树，但依赖 ControlPool 中已有控件
    private void RegisterSubContainersRecursive(Entity entity)
    {
        var children = _entityManager.GetComponents<ControlHierarchyComponent>(entity);
        foreach (var child in children)
        {
            var control = _controlPool.GetControl(child.Entity);
            if (control is EcsContainer container)
            {
                // 验证子容器已注册
            }
            RegisterSubContainersRecursive(child.Entity);
        }
    }

    // ★ 冗余：由 ControlPool 统一管理
    public void Clear()
    {
        _rootContainer = null;
        _rootEntity = Entity.Invalid;
    }
}

// ★ 重构后：EntityRegistry 只负责查询
public class EntityRegistry
{
    // ... 字段、构造函数

    public void RegisterRoot(EcsConfigContainer rootContainer, Entity rootEntity)
    {
        _rootContainer = rootContainer ?? throw new ArgumentNullException(nameof(rootContainer));
        _rootEntity = rootEntity;
        // ★ 修复：确保 rootContainer.Name 不为 null
        var containerName = rootContainer.Name ?? rootContainer.GetType().Name;
        _controlPool.Register(rootEntity, containerName, rootContainer);
    }

    // ★ 保留：通过 ControlPool 查找容器
    public EcsContainer? FindContainer(string name)
    {
        var entity = _controlPool.GetEntity(name);
        if (!entity.IsValid) return null;
        var control = _controlPool.GetControl(entity);
        return control as EcsContainer;
    }

    // ★ 保留：通过 ControlPool 查找控件
    public Control? FindControl(string name)
    {
        var entity = _controlPool.GetEntity(name);
        if (!entity.IsValid) return null;
        return _controlPool.GetControl(entity);
    }

    // ★ 移除：RegisterAllSubContainers()
    // ★ 移除：RegisterSubContainersRecursive()
    // ★ 移除：Clear()
}
```

---

> **文档版本记录**
>
> | 版本 | 日期 | 变更内容 | 作者 |
> |------|------|---------|------|
