# EcsContainer 架构解耦重构方案

> **问题**：`EcsConfigContainer : EcsContainer : Panel : Control` 的继承链导致业务容器的生命周期与 WinForms 控件句柄生命周期强耦合。
> **目标**：将业务逻辑与 WinForms 视图层完全分离，使容器在无窗体的测试/批处理环境下可独立运行。

---

## 一、当前架构分析

### 1.1 继承链与耦合点

```
EcsConfigContainer                              // 业务容器类
  └─ EcsContainer : Panel                       // WinForms 容器基类（UI 框架依赖）
      ├─ IContainer                             // 容器接口（业务）
      ├─ IConfigContainer                       // 配置容器接口（业务）
      └─ System.Windows.Forms.Panel             // WinForms 控件（句柄依赖！）
                :
          System.Windows.Forms.Control
                └─ 依赖 Windows 句柄（Handle）   // 需要添加到 Form + 创建句柄
```

### 1.2 耦合导致的三个具体问题

| # | 问题 | 表现 | 根因 |
|---|------|------|------|
| 1 | **句柄未创建** | 容器创建后立即 `LoadConfigAsync` 在 `Invoke` 时崩溃 | 构造函数完成后句柄不存在，需要添加到 Form 后才能创建 |
| 2 | **线程调度混乱** | `RunOnUiThread` / `DispatchIfNeeded` 需要判断 `InvokeRequired`，但容器可能尚未在 UI 线程上 | `this` 是 Control，但未在任何 Form 上 |
| 3 | **不可测试** | 单元测试需要 WinForms 环境配置 | 继承 `Control` 导致无法在纯 .NET 环境下 new 实例 |

### 1.3 完整依赖图谱

```
EcsConfigContainer
  ├── 业务逻辑（应解耦到 Core）
  │   ├── _ecsStorage, _storageRegistry          // ECS 数据存储
  │   ├── _currentConfig                          // 当前配置
  │   ├── _renderBridge → WinFormsRenderBridge    // 渲染管线桥接
  │   │   └── _pipeline → EcsRenderPipeline       // ECS 渲染管线
  │   ├── _subContainers                           // 子容器列表
  │   ├── LoadConfigAsync(config)                  // 加载配置
  │   ├── RenderConfigAsync(config)                // 渲染配置
  │   ├── RegisterSubContainer(sub)                // 注册子容器
  │   └── Event 事件（ConfigLoaded, ContentChanged, EventsBound）
  │
  └── WinForms 视图（应保留在 Control 层）
      ├── 继承 Panel（DockStyle.Fill, SetStyle）
      ├── Controls 集合（添加/移除子控件）
      ├── SuspendLayout / ResumeLayout
      ├── FindForm() / HandleCreated
      └── InvokeRequired / BeginInvoke(Invoke)
```

---

## 二、目标架构

### 2.1 三层分离

```
┌─────────────────────────────────────────────────────┐
│                  业务调用方                          │
│  (FileDrivenWdUIBridge, ConfigSelectorController)    │
└──────────┬────────────────────────────┬──────────────┘
           │ 创建/操作                    │ 获取根 Entity
           ▼                            ▼
┌──────────────────────┐   ┌──────────────────────────┐
│  EcsContainerCore    │   │  IEntityTreeRoot         │
│  (纯业务逻辑)         │   │  (接口)                   │
│                      │   └──────────────────────────┘
│  - ECS 存储/配置管理  │              ▲
│  - 渲染管线生命周期    │              │ 实现
│  - 子容器注册表        │   ┌──────────────────────────┐
│  - LoadConfigAsync    │   │  EcsContainerControl     │
│  - 事件触发           │   │  (WinForms 视图)          │
│                      │   │                          │
│  owns ─→ EcsRenderPipeline│  ├─ Panel 继承            │
│  owns ─→ ContainerControl │  ├─ 持有 Core 引用        │
└──────────────────────┘   │  ├─ 句柄就绪后通知 Core    │
              │             │  └─ Controls 管理          │
              │ 关联        └──────────────────────────┘
              ▼
┌──────────────────────┐
│  WinFormsRenderBridge │
│  (纯非 UI 工具类)      │
│  - 创建/执行管线       │
│  - 不依赖 Control      │
└──────────────────────┘
```

### 2.2 核心类设计

#### `EcsContainerCore`（新建）

```csharp
// 位置：CsharpPrj/Operation/FrontEndDriven/ControlLookup/Container/EcsContainerCore.cs
// 无任何 WinForms 依赖，可在任何 .NET 环境中运行

namespace FrontEndDriven.Container;

public sealed class EcsContainerCore
{
    // ── 依赖（通过构造函数注入，IMPORTANT 无 WinForms 类型）──
    private readonly IECSStorage _ecsStorage;
    private readonly IStorageRegistry _storageRegistry;

    // ── 渲染管线 ──
    private EcsRenderPipeline? _pipeline;          // 纯 ECS 渲染管线
    private WinFormsRenderBridge? _renderBridge;    // 非 UI 桥接器

    // ── 状态 ──
    private TConfig.UIConfiguration? _currentConfig;
    private int _renderVersion;

    // ── 子容器注册表 ──
    private readonly List<EcsContainerCore> _subContainers = new();

    // ── 属性 ──
    public string Name { get; }
    public TConfig.UIConfiguration? CurrentConfig => _currentConfig;
    public IReadOnlyList<EcsContainerCore> SubContainers => _subContainers.AsReadOnly();

    // ── 事件 ──
    public event EventHandler? LoadCompleted;
    public event EventHandler? RenderCompleted;

    public EcsContainerCore(string name, IECSStorage ecsStorage, IStorageRegistry storageRegistry)
    {
        Name = name;
        _ecsStorage = ecsStorage;
        _storageRegistry = storageRegistry;
    }

    public EcsRenderPipeline? Pipeline => _pipeline;
    public WinFormsRenderBridge? RenderBridge => _renderBridge;

    public void InitializePipeline()
    {
        if (_pipeline != null) return;

        _pipeline = new EcsRenderPipeline(_ecsStorage, _storageRegistry);
        _pipeline.Initialize();
        _renderBridge = new WinFormsRenderBridge(_pipeline);
    }

    public Entity LoadConfiguration(TConfig.UIConfiguration config)
    {
        InitializePipeline();
        var entity = _pipeline!.LoadConfiguration(config);
        _currentConfig = config;
        return entity;
    }

    public void ExecuteCreationStage()
    {
        _pipeline?.ExecuteCreationStage();
    }

    public void RegisterSubContainer(EcsContainerCore subContainer)
    {
        if (subContainer != null && !_subContainers.Contains(subContainer))
            _subContainers.Add(subContainer);
    }

    public void UnregisterSubContainer(EcsContainerCore subContainer)
    {
        _subContainers.Remove(subContainer!);
    }
}
```

#### `ContainerControl`（新建）

```csharp
// 位置：CsharpPrj/Operation/FrontEndDriven/WinForms/Container/ContainerControl.cs
// 继承 Panel，仅作为 WinForms 视图承载

namespace FrontEndDriven.WinForms.Container;

[SupportedOSPlatform("windows")]
public class ContainerControl : Panel
{
    private EcsContainerCore? _core;

    /// <summary>关联的 Core 实例</summary>
    public EcsContainerCore? Core => _core;

    /// <summary>设置或创建 Core（Lifecycle 方法）</summary>
    public void AttachCore(EcsContainerCore core)
    {
        _core = core ?? throw new ArgumentNullException(nameof(core));
    }

    /// <summary>句柄创建后自动触发的初始化</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // 句柄就绪后，Core 可安全与 WinForms 交互
        _core?.OnHandleReady(this);
    }

    // 通用初始化和布局设置
    public ContainerControl()
    {
        Dock = DockStyle.Fill;
    }
}
```

#### `WinFormsRenderBridge`（改造）

```csharp
// 不再是 EcsConfigContainer 专属，改为接受 EcsRenderPipeline 而非 Control

namespace FrontEndDriven.RenderPipeline;

public sealed class WinFormsRenderBridge
{
    private readonly EcsRenderPipeline _pipeline;
    private bool _renderStageExecuted;
    private bool _pendingRender;

    public WinFormsRenderBridge(EcsRenderPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public EcsRenderPipeline Pipeline => _pipeline;

    // 纯配置处理（不依赖 Control）
    public void ProcessConfiguration(TConfig.UIConfiguration config)
    {
        _pipeline.LoadConfiguration(config);
        _pipeline.ExecuteCreationStage();
        _pipeline.ExecuteLayoutStage();  // 布局不再依赖 Form
    }

    // CSG Apply 需要 Control 句柄
    public void TryExecuteCsgApply(Control hostControl)
    {
        if (_renderStageExecuted) return;

        var form = hostControl.FindForm();
        if (form == null || !form.Visible)
        {
            _pendingRender = true;
            return;
        }

        _pipeline.ExecuteRenderStage();
        _renderStageExecuted = true;
        _pendingRender = false;
    }
}
```

---

## 三、实施步骤

### 步骤 1：创建 EcsContainerCore

- 新建 `CsharpPrj/Operation/FrontEndDriven/ControlLookup/Container/EcsContainerCore.cs`
- 从 `EcsConfigContainer` 提取所有非 WinForms 的业务逻辑
- **不引用任何 WinForms 命名空间**

### 步骤 2：改造 WinFormsRenderBridge

- 构造函数改为接受 `EcsRenderPipeline` 而非 `Control`
- `LongLived` 方法（`RegisterLifecycleHandlers`）移到 `EcsContainerControl` 侧
- `CSG Apply` 方法改为接受 `Control` 参数（仅在需要时传入）

### 步骤 3：创建 ContainerControl（WinForms 视图）

- 新建 `CsharpPrj/Operation/FrontEndDriven/WinForms/Container/ContainerControl.cs`
- 继承 `Panel`
- 持有 `EcsContainerCore` 引用
- `OnHandleCreated` 触发后续渲染

### 步骤 4：改造 EcsConfigContainer（或废弃）

- 旧的 `EcsConfigContainer` 改为组合 `EcsContainerCore` + `ContainerControl` 的轻量包装
- 或直接废弃，所有引用方改为使用 `EcsContainerCore` + `ContainerControl` 组合

### 步骤 5：更新引用方

| 引用方 | 修改方式 |
|--------|----------|
| `FileDrivenWdUIBridge` | 创建 `EcsContainerCore` 执行业务 + `ContainerControl` 作为视图 |
| `WinFormsEventRegistrar` | 通过 `EntityRegistry` 查找控件，不依赖 `EcsConfigContainer` 类型 |
| `ConfigSelectorController` | 构造函数接受 `EcsContainerCore` 或 `IContainerControlFinder` |
| `ContainerFinder` | 改为基于 Entity 树查找 |
| `ControlFinder.FindNearestConfigContainer` | 改为查找 `IContainer` / `IEntityTreeRoot` |

### 步骤 6：清理旧类型

- 移除 `EcsConfigContainer` 对 `Panel` 的继承依赖
- 移除 `UiThreadHelper.RunOnUiThread` / `DispatchIfNeeded` 中的句柄未创建 hack

---

## 四、生命周期对比

### 当前（耦合）

```
CreateContainer → new EcsConfigContainer()          // Control 创建，句柄未创建
                → LoadConfigAsync(cfg)              // ❌ 崩溃：Invoke 需要句柄
                → Form.Controls.Add(container)      // 添加到 Form，句柄创建
                → 渲染完成
```

### 新架构（解耦）

```
// 步骤 1：创建业务 Core（无 UI 依赖）
var core = new EcsContainerCore("content", ecsStorage, storageRegistry);
core.LoadConfiguration(config);                     // ✅ 正常执行
core.ExecuteCreationStage();                        // ✅ 正常执行
core.ExecuteLayoutStage();                          // ✅ 正常执行

// 步骤 2：创建 WinForms 视图
var control = new ContainerControl();
control.AttachCore(core);
Form.Controls.Add(control);                         // 添加到 Form，句柄创建

// 步骤 3：句柄创建后自动执行 CSG Apply
// ← HandleCreated 事件触发 ContainerControl.OnHandleCreated
// ← core.OnHandleReady(control) → bridge.TryExecuteCsgApply(control)
// ← _pipeline.ExecuteRenderStage()
```

---

## 五、影响范围

### 新增文件（3 个）

| 文件 | 位置 | 说明 |
|------|------|------|
| `EcsContainerCore.cs` | `ControlLookup/Container/` | 纯业务容器 |
| `ContainerControl.cs` | `WinForms/Container/` | WinForms 视图容器 |
| `IContainerCoreHost.cs` | `ControlLookup/Abstractions/` | Core 与 Control 的桥接接口 |

### 修改文件（7 个）

| 文件 | 修改内容 |
|------|----------|
| `WinFormsRenderBridge.cs` | 构造改为接受 `EcsRenderPipeline`，CSG Apply 改为参数传入 Control |
| `EcsConfigContainer.cs` | 改为组合 `EcsContainerCore` + `ContainerControl`，标记 `[Obsolete]` |
| `FileDrivenWdUIBridge.cs` | 改为分别创建 Core 和 Control，延迟 Layout 到 HandleCreated |
| `WinFormsEventRegistrar.cs` | 接受 `EcsContainerCore` 而非 `EcsConfigContainer` |
| `ConfigSelectorController.cs` | 通过 `EcsContainerCore` 访问渲染管线 |
| `ContainerFinder.cs` | 基于 Entity 树查找，不依赖 `EcsConfigContainer` 类型 |
| `ControlFinder.cs` | `FindNearestConfigContainer` 改为查找 `IEntityTreeRoot` |

### 不受影响（零修改）

`EcsRenderPipeline`, `EcsInputPort`, `CsgEntryPoint`, `WinFormsOutputPort`, `EcsCapabilityApplier`, `ControlPool`, `EntityRegistry`, `LayoutSystem`, `StyleSystem`, `SplitContainerSystem`, `EventBindingSystem`, `WinFormsControlValueWriter`, `ControlFactory`, `UIReconciler`, `RenderAuditPort`, `UnifiedControlService`

---

## 六、优先级与实施建议

| 优先级 | 步骤 | 预计工时 | 风险 | 说明 |
|--------|------|----------|------|------|
| **P0** | 1. 创建 EcsContainerCore | 2h | 中 | 提取业务逻辑，确保无 WinForms 依赖泄漏 |
| **P0** | 2. 改造 WinFormsRenderBridge | 1h | 低 | 构造参数变更，不影响外部接口 |
| **P1** | 3. 创建 ContainerControl | 1h | 低 | 纯新增 WinForms 视图 |
| **P1** | 4. 改造 FileDrivenWdUIBridge | 1h | 高 | 核心调用方，影响整个应用生命周期 |
| **P2** | 5. 更新其他引用方 | 2h | 中 | 6 个文件的适配修改 |
| **P2** | 6. 清理旧类型 | 0.5h | 低 | 删除废弃代码 |
| **P3** | 7. 编写单元测试（Core） | 1h | - | 验证 Core 在无 WinForms 环境可运行 |

**总计**：约 **8.5h**（含测试约 10h）
**受益**：运行时句柄崩溃消除 + 可测试性 + WPF/MAUI 复用可能性