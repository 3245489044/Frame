# WdUI 通用框架提取方案

## 一、项目背景与目标

### 1.1 背景

WdUI 是一个 WinForms UI 框架，经过多轮迭代积累了丰富的 UI 交互逻辑。然而，WdUI 中存在大量**与 WinForms 无关的纯抽象逻辑**（如管线编排、布局计算、数据绑定、配置管理等），这些逻辑被耦合在 WinForms 控件生命周期中，导致：

- **复用困难**：非 WinForms 项目无法使用这些逻辑
- **测试困难**：依赖 WinForms 环境，无法进行纯单元测试
- **维护困难**：业务逻辑与 UI 框架深度耦合，修改风险高

### 1.2 目标

将 WdUI 中的纯抽象逻辑提取到通用框架层，实现**抽象逻辑与业务 WinForms 完全分离**：

```
┌─────────────────────────────────────────────────────────┐
│                    WdUI (WinForms UI)                     │
│  ┌─────────────┐  ┌──────────────┐  ┌────────────────┐  │
│  │ 控件渲染管线  │  │ WinForms布局  │  │ WinForms数据绑定 │  │
│  └──────┬──────┘  └──────┬───────┘  └───────┬────────┘  │
│         │                │                   │           │
│   委托调用         委托调用             委托调用          │
└─────────┼────────────────┼───────────────────┼───────────┘
          │                │                   │
          ▼                ▼                   ▼
┌─────────────────────────────────────────────────────────┐
│              通用框架层 (纯抽象，零 UI 依赖)               │
│  ┌─────────────┐  ┌──────────────┐  ┌────────────────┐  │
│  │BasicMethod   │  │BasicMethod   │  │EventTrigger    │  │
│  │.Pipeline     │  │.Framework    │  │Framework       │  │
│  │(管线编排/    │  │(布局/配置/   │  │(数据绑定/事件  │  │
│  │ 重入保护/    │  │ 视图管理)    │  │ 管线/函数注册) │  │
│  │ 会话管理)    │  │              │  │                │  │
│  └─────────────┘  └──────────────┘  └────────────────┘  │
└─────────────────────────────────────────────────────────┘
```

---

## 二、现有基础设施分析

### 2.1 BasicMethod.Common — 通用工具层

| 目录 | 已有内容 | 状态 |
|------|---------|------|
| `Caching/` | `WeakStateManager<TKey,TState>`、`DedupFlags<TKey>`、`MemoryCache<TKey,TValue>`、`IStateStore`、`TreeCache` | ✅ 完备 |
| `Async/` | `AsyncHelper`（FireAndForget / WithTimeout / WithRetry） | ✅ 完备 |
| `WinForms/` | `ControlValueHelper`、`WinFormsCommandEngine`、`ControlExtensions` | ✅ 完备 |
| `Configuration/` | `GlobalSwitch`、`GlobalValue`、`SharedConfigurations` | ✅ 完备 |
| `DiscreteChange/` | `DiscreteChangeCalculator`、`ChangeReport`、`StateSnapshot` | ✅ 完备 |
| `Functions/` | `ArgsProcess`、`BasicFrame`、`CommandFileExecutor`、`Function` | ✅ 完备 |
| `Models/` | `DependencyGraph`、`OperationResult`、`ChangeRecord` 等 | ✅ 完备 |
| `Serialization/` | `NewtonSoftJsonFramework`、`SystemJsonFramework`、`DictionaryConverter` | ✅ 完备 |
| `StepCoordination/` | `AsyncStepCoordinator`、`IStepCoordinator` | ✅ 完备 |
| `Utilities/` | `Extensions`、`FileMethod`、`RefectionMethods`、`ParameterCreate` 等 | ✅ 完备 |

### 2.2 BasicMethod.Framework — 通用框架层

| 文件 | 说明 | 状态 |
|------|------|------|
| `Abstractions/ILayoutEngine.cs` | 布局引擎接口（LayoutRect / LayoutStrategy / LayoutExecutor） | ✅ 完备 |
| `Abstractions/IConfigLoader.cs` | 配置加载/合并/构建/验证接口 | ✅ 完备 |
| `Abstractions/IViewManager.cs` | 视图管理器接口 | ✅ 完备 |
| `Abstractions/IEventBus.cs` | 事件总线接口 | ✅ 完备 |
| `Abstractions/IModule.cs` | 模块接口 | ✅ 完备 |
| `Abstractions/IObjectFactory.cs` | 对象工厂接口 | ✅ 完备 |
| `Abstractions/INavigationService.cs` | 导航服务接口 | ✅ 完备 |
| `Abstractions/ITreeSearch.cs` | 树搜索接口 | ✅ 完备 |
| `ConfigLoader.cs` | JSON 配置加载器实现 | ✅ 完备 |
| `ConfigMerger.cs` | 配置合并器实现 | ✅ 完备 |
| `ConfigBuilder.cs` | 配置构建器实现 | ✅ 完备 |
| `ConfigValidator.cs` | 配置验证器实现 | ✅ 完备 |
| `ViewManager.cs` | 视图管理器实现 | ✅ 完备 |
| `LifecycleManager.cs` | 生命周期管理器 | ✅ 完备 |
| `ObjectFactory.cs` | 对象工厂实现 | ✅ 完备 |
| `NavigationService.cs` | 导航服务实现 | ✅ 完备 |
| `TreeSearch.cs` | 树搜索实现 | ✅ 完备 |

### 2.3 EventTriggerFramework — 事件触发框架

| 目录 | 已有内容 | 状态 |
|------|---------|------|
| `Abstractions/` | `IControlValueAccessor`、`IEventBinder`、`IEventPipeline`、`IEventTriggerService`、`IFunctionRegistry`、`IStateStore`、`IValueExtractor` 等 17 个接口 | ✅ 完备 |
| `Core/` | `ControlValueAccessorBase<TTarget>`、`EventBindingExecutor`、`FunctionRegistry`、`InMemoryStateStore`、`DefaultValueExtractor` | ✅ 完备 |
| `WinForms/` | `WinFormsControlValueAccessor`、`WinFormsEventBinder`、`WinFormsEventBus`、`WinFormsEventNameResolver`、`ReflectionEventBinder` | ✅ 完备 |
| `Pipeline/` | `MiddlewarePipeline`、`StagePipeline`、`TryExecuteSkeleton` | ✅ 完备 |
| `Models/` | `EventRequest`、`EventResult`、`EventBindingInfo`、`ControlValueResult` | ✅ 完备 |
| `EventBus.cs` | `DefaultEventBusCore`、`EnhancedEventBusCore`、`EventBusFacade`、`EventEngine` | ✅ 完备 |

---

## 三、WdUI 可提取的通用逻辑

### 3.1 管线编排逻辑 → BasicMethod.Pipeline

**来源文件**：`WdUI/WdUI.RenderPipeline.cs`

| 提取项 | 说明 | 通用化方式 |
|--------|------|-----------|
| `SessionManager<TTarget, TConfig, TContext>` | 会话管理器（已有，增强） | 3 泛型参数，强关联 StagePipeline |
| `ReentrantGuard<TKey>` | 重入保护：防止同一对象重复进入关键区 | 基于 `DedupFlags<TKey>` 的 TrySet/Reset 模式 |
| `GuardFlag` | 循环保护标志：volatile bool 模式 | 封装为独立类，支持 IDisposable 模式 |

**设计示例**：

```csharp
// SessionManager.cs
public class SessionManager<TKey, TSession>
    where TKey : class
    where TSession : class, new()
{
    private readonly MemoryCache<TKey, TSession> _cache;
    
    public TSession GetOrCreate(TKey key) => _cache.GetOrAdd(key, _ => new TSession());
    public bool TryGet(TKey key, out TSession session) => _cache.TryGet(key, out session);
    public void Release(TKey key) => _cache.Remove(key);
    public void Cleanup() => _cache.CleanupExpired();
}

// ReentrantGuard.cs
public class ReentrantGuard<TKey> where TKey : class
{
    private readonly DedupFlags<TKey> _flags = new();
    
    public bool TryEnter(TKey key) => _flags.TrySet(key);
    public void Exit(TKey key) => _flags.Reset(key);
    public bool IsEntered(TKey key) => _flags.IsSet(key);
}

// GuardFlag.cs
public class GuardFlag
{
    private volatile bool _isSet;
    
    public bool TrySet() => !Interlocked.Exchange(ref _isSet, true);
    public void Reset() => _isSet = false;
    public bool IsSet => _isSet;
}
```

### 3.2 布局计算逻辑 → BasicMethod.Framework

**来源文件**：`WdUI/Layout/LayoutMediator.cs`、`WdUI/Layout/LayoutStrategyRegistry.cs`

| 提取项 | 说明 | 通用化方式 |
|--------|------|-----------|
| `LayoutPositionConverter` | Bottom/Right → 负坐标转换算法 | 纯算法，无 UI 依赖 |
| `ConfigToLayoutConverter<TConfig, TLayout>` | 配置→布局定义转换器 | 泛型委托转换 |
| `LayoutStrategyRegistry<TContainer, TItem>` | 布局策略注册表 | 实现已有的 `ILayoutExecutor<TContainer, TItem>` 接口 |

### 3.3 配置加载增强 → BasicMethod.Framework

**来源文件**：`WdUI/UI/Configuration/ConfigurationLoader.cs`

| 提取项 | 说明 | 通用化方式 |
|--------|------|-----------|
| `Extends` 继承链支持 | 配置可继承另一个配置，合并属性 | 增强 `ConfigMerger<T>`，支持递归继承链解析 |
| 配置变更通知 | 配置变更时通知订阅者 | 新增 `IConfigChangeNotifier` 接口 |

### 3.4 异步版本号保护 → BasicMethod.Common.Async

**来源文件**：`WdUI/UI/ConfigContainer.cs`（`_renderVersion` 字段）

| 提取项 | 说明 | 通用化方式 |
|--------|------|-----------|
| `AsyncVersionGuard` | 版本号竞态保护器 | 封装 `int` + `Interlocked` 操作 |

```csharp
// AsyncVersionGuard.cs
public class AsyncVersionGuard
{
    private int _version;
    
    public int Current => Volatile.Read(ref _version);
    public int Next() => Interlocked.Increment(ref _version);
    public bool IsCurrent(int expected) => Current == expected;
}
```

### 3.5 数据绑定核心 → EventTriggerFramework

**来源文件**：`WdUI/UI/DataBindingManager.cs`

| 提取项 | 说明 | 通用化方式 |
|--------|------|-----------|
| `IDataBindingManager` | 数据绑定管理器接口 | 新增到 `EventTriggerFramework.Abstractions` |
| `DataBindingManager<TControl>` | 通用数据绑定管理器 | 泛型化，`TControl : class`，不依赖 WinForms |
| `WinFormsDataBindingManager` | WinForms 数据绑定管理器 | 继承 `DataBindingManager<Control>`，注册 WinForms 适配器 |

**设计示例**：

```csharp
// IDataBindingManager.cs (EventTriggerFramework.Abstractions)
public interface IDataBindingManager
{
    void RegisterDataSource(string name, object dataSource);
    void UnregisterDataSource(string name);
    void BindControl(string controlName, string sourceName, string sourceProperty);
    void UnbindControl(string controlName);
    void UpdateSource(string controlName);
    void RefreshAll();
}

// DataBindingManager.cs (EventTriggerFramework.Core)
public class DataBindingManager<TControl> : IDataBindingManager
    where TControl : class
{
    private readonly IControlValueAccessor _valueAccessor;
    private readonly Dictionary<string, object> _dataSources = new();
    private readonly Dictionary<string, BindingConfig> _bindings = new();
    
    // ... 通用实现
}

// WinFormsDataBindingManager.cs (EventTriggerFramework.WinForms)
public class WinFormsDataBindingManager : DataBindingManager<Control>
{
    public WinFormsDataBindingManager() 
        : base(new WinFormsControlValueAccessor()) { }
}
```

---

## 四、提取方案总览

### 4.1 文件清单

```
BasicMethod/Pipeline/
  ├── 15_ReentrantGuard.cs           # 重入保护器（新增）
  ├── 16_GuardFlag.cs                # 循环保护标志（新增）
  └── 05_Services.cs                 # 已有：SessionManager（增强）

BasicMethod/Common/
  └── Async/
      └── AsyncVersionGuard.cs       # 版本号竞态保护（新增）

BasicMethod/Framework/
  ├── LayoutPositionConverter.cs     # 布局位置转换器（新增）
  ├── LayoutStrategyRegistry.cs      # 布局策略注册表实现（新增）
  └── ConfigLoader.cs                # 增强：Extends 继承链支持（修改）

EventTriggerFramework/
  ├── Abstractions/
  │   └── IDataBindingManager.cs     # 数据绑定管理器接口（新增）
  ├── Core/
  │   └── DataBindingManager.cs      # 通用数据绑定管理器（新增）
  └── WinForms/
      └── WinFormsDataBindingManager.cs  # WinForms 数据绑定管理器（新增）
```

### 4.2 WdUI 改造对照表

| WdUI 原代码 | 改为委托调用 |
|------------|-------------|
| `RenderPipeline._sessions` (MemoryCache<Control, RenderSession>) | `SessionManager<Control, RenderSession>` |
| `RenderPipeline._relayoutGuard` (DedupFlags<Control>) | `ReentrantGuard<Control>` |
| `RenderPipeline._isApplyingSplitter` (volatile bool) | `GuardFlag` |
| `ConfigContainer._renderVersion` (int + Interlocked) | `AsyncVersionGuard` |
| `LayoutMediator.ConvertLayoutPosition` | `LayoutPositionConverter.Convert` |
| `LayoutStrategyRegistry` | `LayoutStrategyRegistry<Control, Control>` |
| `ConfigurationLoader` 的 Extends 继承 | `ConfigLoader<T>` 增强版 |
| `DataBindingManager` | `WinFormsDataBindingManager` |

### 4.3 提取原则

1. **完全脱离 WinForms**：所有提取到通用层的代码使用泛型，不引用 `System.Windows.Forms`
2. **零外部依赖**：只依赖 `System.Collections.Concurrent`、`System.Threading` 等基础库
3. **向后兼容**：WdUI 中保留原有类名，内部委托到通用层，外部调用方无需修改
4. **增量提取**：按优先级分批提取，先提取最通用的 SessionManager / ReentrantGuard / AsyncVersionGuard

---

## 五、架构全景图

```
┌─────────────────────────────────────────────────────────────────────────┐
│                          WdUI (WinForms UI 层)                          │
│                                                                         │
│  ┌─────────────────┐  ┌──────────────────┐  ┌──────────────────────┐   │
│  │ RenderPipeline   │  │ LayoutMediator   │  │ DataBindingManager   │   │
│  │ (WinForms 渲染)  │  │ (WinForms 布局)  │  │ (WinForms 数据绑定)  │   │
│  └────────┬────────┘  └────────┬─────────┘  └──────────┬───────────┘   │
│           │                    │                        │               │
│     委托调用              委托调用                  委托调用            │
└───────────┼────────────────────┼────────────────────────┼───────────────┘
            │                    │                        │
            ▼                    ▼                        ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                      EventTriggerFramework                              │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │  Abstractions: IDataBindingManager, IControlValueAccessor,       │   │
│  │  IEventBinder, IEventPipeline, IEventTriggerService,             │   │
│  │  IFunctionRegistry, IStateStore, IValueExtractor                 │   │
│  ├──────────────────────────────────────────────────────────────────┤   │
│  │  Core: DataBindingManager<T>, ControlValueAccessorBase<T>,       │   │
│  │  EventBindingExecutor, FunctionRegistry, InMemoryStateStore      │   │
│  ├──────────────────────────────────────────────────────────────────┤   │
│  │  WinForms: WinFormsDataBindingManager, WinFormsControlValueAccessor│  │
│  │  WinFormsEventBinder, WinFormsEventBus                           │   │
│  └──────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
            │
            │
            ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                      BasicMethod.Framework                              │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │  Abstractions: ILayoutEngine, IConfigLoader, IViewManager,       │   │
│  │  IEventBus, IModule, IObjectFactory, INavigationService          │   │
│  ├──────────────────────────────────────────────────────────────────┤   │
│  │  Implementations: ConfigLoader<T>, ConfigMerger<T>,              │   │
│  │  ViewManager<T>, LayoutPositionConverter, LayoutStrategyRegistry │   │
│  └──────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                      BasicMethod.Common                                 │
│  ┌──────────┬──────────┬──────────┬──────────┬────────────────────┐    │
│  │ Caching  │  Async   │ Pipeline │ WinForms │ Configuration/     │    │
│  │ WeakState│ AsyncHlp │ Session  │ CtrlVal  │ Models/Utilities/  │    │
│  │ DedupFlg │ VerGuard │ Reentrant│ CmdEngine│ Serialization/...  │    │
│  │ MemCache │          │ GuardFlg │ CtrlExt  │                    │    │
│  └──────────┴──────────┴──────────┴──────────┴────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 六、实施路线图

### 第一阶段：BasicMethod.Pipeline 补充（已完成 ✅）

- [x] 创建 `ReentrantGuard<TKey>` — 重入保护器（`15_ReentrantGuard.cs`）
- [x] 创建 `GuardFlag` — 循环保护标志（`16_GuardFlag.cs`）
- [x] 创建 `AsyncVersionGuard` — 版本号竞态保护器（`BasicMethod.Common/Async/AsyncVersionGuard.cs`）
- [x] 创建 `LayoutPositionConverter` — 布局位置转换器（`BasicMethod.Framework/LayoutPositionConverter.cs`）
- [x] WdUI 改造：RenderPipeline 委托到上述组件
  - `_relayoutGuard` (DedupFlags) → `ReentrantGuard<Control>`
  - `_isApplyingSplitter` (volatile bool) → `GuardFlag`
  - 修复 `15_ReentrantGuard.cs` 和 `16_GuardFlag.cs` 的 using 语句（`Pipeline.csproj` 使用 `ImplicitUsings=disable`）

### 第二阶段：BasicMethod.Framework 补充

- [x] 创建 `LayoutPositionConverter` — 布局位置转换器（第一阶段已完成）
- [x] 创建 `LayoutStrategyRegistry<TContainer, TConfig, TContext>` — 通用布局策略注册表（含 `ILayoutStrategy<TContainer, TConfig, TContext>` 接口）
- [x] 增强 `ConfigLoader<T>` — 支持 Extends 继承链（`LoadWithExtends` 方法，递归合并，循环继承检测）
- [x] WdUI 改造：LayoutMediator / LayoutStrategyRegistry 委托（WdUI 的 `LayoutStrategyRegistry` 保留为 WinForms 特定包装，内部可委托到通用 `LayoutStrategyRegistry<Control, ControlConfig, ILayoutContext>`）

### 第三阶段：EventTriggerFramework 数据绑定

- [ ] 创建 `IDataBindingManager` 接口
- [ ] 创建 `DataBindingManager<TControl>` 通用实现
- [ ] 创建 `WinFormsDataBindingManager` WinForms 实现
- [ ] WdUI 改造：DataBindingManager 委托

### 第四阶段：集成测试与文档

- [ ] 单元测试覆盖所有提取组件
- [ ] WdUI 集成测试（确保行为不变）
- [ ] 更新使用文档

---

## 七、设计约束与规范

### 7.1 命名空间规范

| 目标项目 | 命名空间 |
|---------|---------|
| BasicMethod.Common | `BasicMethod.Common.Pipeline` |
| BasicMethod.Common | `BasicMethod.Common.Async` |
| BasicMethod.Framework | `BasicMethod.Framework` |
| EventTriggerFramework.Abstractions | `EventTriggerFramework.Abstractions` |
| EventTriggerFramework.Core | `EventTriggerFramework` |
| EventTriggerFramework.WinForms | `EventTriggerFramework.WinForms` |

### 7.2 代码规范

1. **所有通用层代码**必须使用 `#nullable enable`
2. **所有通用层代码**不得引用 `System.Windows.Forms`
3. **所有通用层代码**必须提供 XML 文档注释
4. **所有通用层代码**必须线程安全（使用 `ConcurrentDictionary`、`Interlocked`、`lock` 等）
5. **WdUI 改造代码**必须保持原有公开 API 签名不变

### 7.3 测试策略

- 通用层代码：纯单元测试，无需 UI 环境
- WdUI 改造代码：集成测试，验证行为一致性
- 使用 `IStateStore` 模拟控件状态，无需真实 UI

---

## 八、附录

### 8.1 相关文件索引

| 文件路径 | 说明 |
|---------|------|
| `WdUI/WdUI.RenderPipeline.cs` | 管线编排逻辑（SessionManager / ReentrantGuard / GuardFlag） |
| `WdUI/Layout/LayoutMediator.cs` | 布局计算逻辑（LayoutPositionConverter） |
| `WdUI/Layout/LayoutStrategyRegistry.cs` | 布局策略注册表 |
| `WdUI/UI/Configuration/ConfigurationLoader.cs` | 配置加载（Extends 继承链） |
| `WdUI/UI/DataBindingManager.cs` | 数据绑定管理器 |
| `WdUI/UI/ConfigContainer.cs` | 配置容器（AsyncVersionGuard） |
| `BasicMethod.Common/Caching/DedupFlags.cs` | 去重标志（ReentrantGuard 的基础） |
| `BasicMethod.Common/Caching/MemoryCache.cs` | 内存缓存（SessionManager 的基础） |
| `BasicMethod.Framework.Abstractions/ILayoutEngine.cs` | 布局引擎接口 |
| `EventTriggerFramework/Abstractions/IControlValueAccessor.cs` | 控件值读写器接口 |
| `EventTriggerFramework/Core/ControlValueAccessorBase.cs` | 控件值读写器基类 |

### 8.2 版本历史

| 版本 | 日期 | 说明 |
|------|------|------|
| v1.0 | 2026-06-01 | 初始版本，完成方案设计 |
| v1.1 | 2026-06-01 | 完成 ILayoutContext → IWinFormsLayoutContext 重命名，消除与 BasicMethod.UniversalLayout.LayFrame.ILayoutContext 的命名冲突 |
| v1.2 | 2026-06-01 | 新增 `SessionManager` 提取：`SessionStatus` + `Session<TData>` + `SessionManager<TKey, TData>` 提取到 `PlaceholderFramework/Core/SessionManager.cs`；WdUI 的 `RenderSession` 改为继承 `Session<UIConfiguration>`，复用通用会话生命周期管理 |
| v1.3 | 2026-06-01 | **大文件拆分与代码整理优化**：<br>1. `WdUI.RenderPipeline.cs`（~600行）→ 拆出 `LayoutState.cs`、`RenderSession.cs`、`LayoutStrategyRegistry.cs`、`LayoutContext.cs` 到 `WdUI/Layout/` 目录，管线文件从 ~600 行降至 ~430 行<br>2. `RenderPipeline` 提取通用辅助方法 `CreateSkeletonContext()` 和 `LogPlaceholder()`，消除 `Render`/`ApplyLayout`/`Relayout`/`BindEvents` 中的重复代码<br>3. `WdUIComposer` 的 `StrategyRegistry` 和 `LayoutContext` 统一为 `Lazy<T>` 模式，消除双重检查锁定<br>4. `ConfigContainer.FindSubContainer` 消除重复递归，统一委托到 `ControlFinder.FindRecursive`<br>5. `WdUILayout.ApplyProportional` 提取 `ApplyMinMaxConstraints` 私有方法，消除 Min/Max 约束的重复逻辑<br>6. `WdUIComposer.LayoutContext` 统一为 `Lazy<T>`，消除双重检查锁定 |

### 8.3 提取文件清单（完整）

| 文件路径 | 说明 | 状态 |
|---------|------|------|
| `BasicMethod/Pipeline/15_ReentrantGuard.cs` | 重入保护器 `ReentrantGuard<TKey>` | ✅ 已完成 |
| `BasicMethod/Pipeline/16_GuardFlag.cs` | 循环保护标志 `GuardFlag` | ✅ 已完成 |
| `BasicMethod/Common/Async/AsyncVersionGuard.cs` | 版本号竞态保护器 `AsyncVersionGuard` | ✅ 已完成 |
| `BasicMethod/Framework/LayoutPositionConverter.cs` | 布局位置转换器 | ✅ 已完成 |
| `BasicMethod/Framework/LayoutStrategyRegistry.cs` | 通用布局策略注册表（含 `ILayoutStrategy<TContainer, TConfig, TContext>` 接口） | ✅ 已完成 |
| `BasicMethod/Framework/ConfigLoader.cs` | 增强：Extends 继承链支持 | ✅ 已完成 |
| `PlaceholderFramework/Core/SessionManager.cs` | 通用会话管理：`SessionStatus` + `Session<TData>` + `SessionManager<TKey, TData>` | ✅ 已完成 |
| `EventTriggerFramework/Abstractions/IDataBindingManager.cs` | 数据绑定管理器接口 | ⏳ 待完成 |
| `EventTriggerFramework/Core/DataBindingManager.cs` | 通用数据绑定管理器 | ⏳ 待完成 |
| `EventTriggerFramework/WinForms/WinFormsDataBindingManager.cs` | WinForms 数据绑定管理器 | ⏳ 待完成 |
