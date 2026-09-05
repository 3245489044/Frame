# JSON 框架重构：加载阶段抽象与公共事件平台设计

> **日期**：2026-08-01
> **触发问题**：`MainLayout.json` 的 `loadSideButton`（Load Side）把 `ConfigSelector.json` 加载到侧边容器后，`configListView` 未被填充（预期显示 JsonConfig 目录的配置文件列表）。
> **根因**：配置加载后要执行的 `InitCommands`（`LoadJsonConfigList`）消费逻辑被硬编码在 `MainForm.OnLoadAsync` 启动路径；动态加载路径（`LoadConfigPortAdapter` → `EcsGate.LoadConfigAndRenderAsync`）只渲染控件、不执行 `InitCommands`。
> **战略升级**：从 bug 修复 → 架构级「加载阶段抽象 + 公共事件平台」。

---

## 一、背景与动机

### 1.1 问题现象

```
点击 loadSideButton → LoadSidePanel（UIToolbox）
  └─ OpenFileDialog 选择 ConfigSelector.json
      └─ ILoadConfigPort.LoadConfigAsync(path, "Side", "sideContainer")
          └─ LoadConfigPortAdapter.LoadConfigAsync
              └─ EcsGate.LoadConfigAndRenderAsync(config, "Side", sideContainer)
                  ├─ LoadConfigOperation → DoLoadConfig → 渲染控件
                  └─ ExecuteRenderStage → 事件绑定
                  ❌ 从不读取 config.InitCommands
```

`ConfigSelector.json` 声明了 `initCommands: ["LoadJsonConfigList"]`（第 3 行），但**消费这段声明的代码只在 `MainForm.OnLoadAsync`（启动路径）**：

```
MainForm.OnLoadAsync (MainForm.cs:154-180)  ← 唯一消费 InitCommands 的地方
  └─ 仅遍历 GetInitPhases()（硬编码 MainLayout、ConfigSelector）
      └─ 对每个 phase.config.InitCommands 执行 FunctionCallFacade.ExecuteSyncRequest
```

### 1.2 战略结论

当前框架的「配置加载」与「配置后行为」是两条割裂路径：

- **JSON 只能描述**：控件长什么样（静态结构）+ 控件点了什么（事件绑定）
- **JSON 不能声明**：加载后自动执行什么、销毁前/后执行什么、作用域如何引导启动

这些"配置后行为"被硬编码在 `MainForm`、`EcsGate`、`LoadConfigPortAdapter` 等多处 C# 代码里。

---

## 二、现状诊断

### 2.1 硬编码点清单

| 编号 | 硬编码点 | 位置 | 后果 |
|------|---------|------|------|
| **H1** | 启动加载阶段 | `MainForm.GetInitPhases()` 硬编码 MainLayout/ConfigSelector | 新增界面需改 C# 代码，无法 JSON 声明初始加载顺序 |
| **H2** | InitCommands 消费 | `MainForm.OnLoadAsync:154-180` 只处理 2 个启动 phase | 动态加载路径不消费 → **本 bug 根因** |
| **H3** | Scope 生命周期 | `EcsGate.DoLoadConfig`/`DestroyScopeEntities` 散落 | scope 创建/销毁无统一事件通知，旧控件/事件残留 |
| **H4** | 触发异步不可观测 | `ExecuteFunctionDelegate` fire-and-forget（B3） | 按钮点击结果不可观测 |

### 2.2 可复用资产（不重复建设）

```
✅ 已有：FunctionCallFacade（统一函数门面）
✅ 已有：EventBindingSystem + EventBindingOrchestrator（统一事件绑定）
✅ 已有：EcsGate（统一 ECS 入口）
✅ 已有：LoadConfigPortAdapter（统一动态加载入口）
❌ 缺失：加载阶段驱动的【配置声明化】          ← 关键缺失
❌ 缺失：统一生命周期事件平台（加载/销毁前后）  ← 关键缺失
❌ 缺失：InitCommands 与事件绑定的统一调度       ← 关键缺失
```

---

## 三、目标架构（三层模型）

```
┌────────────────────────────────────────────────────────────┐
│              ① 配置声明层 (UIConfiguration)                  │
│  {                                                          │
│    "boot": [                                             ← NEW
│       { "id": "MainLayout",   "load": "MainLayout.json",   "scope": "MainLayout", "host": "root" },
│       { "id": "ConfigSelector", "load": "ConfigSelector.json", "scope": "Content", "host": "contentContainer" }
│    ],
│    "lifecycle": {                                          ← NEW
│       "onBeforeLoad":  ["actionA"],
│       "onAfterLoad":   ["LoadJsonConfigList"],            ← 替代原 initCommands
│       "onBeforeDestroy": ["actionB"],
│       "onAfterDestroy": ["actionC"]
│    },
│    "initCommands": ["LoadJsonConfigList"]                  ← 保留向后兼容（迁移期）
│  }
└──────────────┬─────────────────────────────────────────────┘
               │ ① JSON 配置解析
               ▼
┌────────────────────────────────────────────────────────────┐
│              ② 编排层 ConfigurationLifecycleManager        │
│              （配置生命周期编排器 - NEW）                      │
│                                                          │
│  按阶段执行：                                               │
│  lifecycle.onBeforeLoad → Render → lifecycle.onAfterLoad  │
│  → 事件绑定 (EventBindingSystem) → 完成                    │
│                                                          │
│  作用域切换/销毁时：                                        │
│  lifecycle.onBeforeDestroy → Destroy → onAfterDestroy     │
└──────────────┬─────────────────────────────────────────────┘
               │ ② 所有行为经统一
               ▼
┌────────────────────────────────────────────────────────────┐
│              ③ 公共事件平台 EventPlatform                    │
│                                                          │
│  ┌──────────────────────────────────────────────────────┐ │
│  │ EventBus（统一事件总线）                                │ │
│  │  ├─ UI事件（click/change/...） → FunctionCallFacade    │ │
│  │  ├─ 生命周期事件（ConfigLoaded/Destroying/Destroyed）  │ │
│  │  └─ 业务事件（FunctionExecuted/ScopeSwitched）— 订阅    │ │
│  └──────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────┘
```

---

## 四、配置声明层：JSON Schema 扩展

### 4.1 模型变更（`CsharpPrj/Plate/TConfig/Models/UIConfiguration.cs`）

```csharp
public class UIConfiguration
{
    // ... 现有字段 ...

    /// <summary>启动引导阶段声明（替代 MainForm.GetInitPhases 硬编码）</summary>
    public List<BootPhase>? Boot { get; set; }

    /// <summary>生命周期钩子声明（替代 initCommands 分散消费）</summary>
    public LifecycleHooks? Lifecycle { get; set; }
}

/// <summary>启动引导阶段</summary>
public record BootPhase
{
    public string Id { get; init; } = "";      // "MainLayout"
    public string Load { get; init; } = "";    // "MainLayout.json"
    public string Scope { get; init; } = "";   // "MainLayout" / "Content" / "Side"
    public string Host { get; init; } = "";    // "root" / "contentContainer" / "sideContainer"
}

/// <summary>生命周期钩子声明</summary>
public class LifecycleHooks
{
    public List<string>? OnBeforeLoad { get; set; }    // 渲染前执行
    public List<string>? OnAfterLoad { get; set; }     // 渲染+事件绑定后执行（原 initCommands）
    public List<string>? OnBeforeDestroy { get; set; } // 作用域销毁前
    public List<string>? OnAfterDestroy { get; set; }  // 作用域销毁后
}
```

### 4.2 向后兼容策略

| 优先级 | 来源 | 说明 |
|--------|------|------|
| ① | `Lifecycle.OnAfterLoad` | 新声明方式，优先 |
| ② | `InitCommands` | 旧声明方式，fallback（迁移期双支持） |
| ③ | 两者都空 | 无加载后行为 |

迁移完成后 `initCommands` 标注 `[Obsolete]` 并在下一迭代移除。

---

## 五、编排层：ConfigurationLifecycleManager

### 5.1 职责

唯一负责「配置加载 + 生命周期钩子执行 + 事件绑定」的编排器，消除 MainForm 硬编码。

### 5.2 核心方法

```csharp
public sealed class ConfigurationLifecycleManager
{
    private readonly EcsGate _gate;
    private readonly Dictionary<string, Func<string, Control>> _hostResolvers;

    /// <summary>启动引导：从 UIConfiguration.Boot 声明加载所有阶段</summary>
    public async Task BootstrapAsync(UIConfiguration bootConfig);

    /// <summary>加载单个 phase（渲染 + 生命周期钩子 + 事件绑定统一调度）</summary>
    public async Task LoadAsync(BootPhase phase);

    /// <summary>销毁作用域（触发 onBeforeDestroy/onAfterDestroy 钩子）</summary>
    public Task DestroyScopeAsync(string scope);

    /// <summary>统一钩子执行（经 FunctionCallFacade/工作流）</summary>
    private async Task ExecuteHooksAsync(IEnumerable<string>? hookNames);
}
```

### 5.3 与现有路径的接线

| 现有调用方 | 改造后 |
|-----------|--------|
| `MainForm.OnLoadAsync` 的加载循环 | 改调 `lifecycleManager.BootstrapAsync(bootConfig)` |
| `MainForm.OnLoadAsync` 的 InitCommands 循环 | 删除（并入 LifecycleManager） |
| `LoadConfigPortAdapter.LoadConfigAsync` | 改调 `lifecycleManager.LoadAsync(phase)`（包装 dynamic phase） |
| `EcsGate.LoadConfigAndRenderAsync` | 保留（底层渲染原语），由 LifecycleManager 编排 |

---

## 六、执行层：公共事件平台 EventPlatform

### 6.1 统一事件模型

```csharp
public interface IEvent
{
    string EventType { get; }        // "ConfigLoaded" / "ConfigDestroyed" / ...
    string? Source { get; }          // 来源（控件名/函数名/phase id）
    string Scope { get; }            // 作用域（Content/Side/MainLayout）
    object? Payload { get; }         // 事件负载
    DateTime Timestamp { get; }
}
```

### 6.2 标准生命周期事件

| 事件类型 | 触发时机 | Payload |
|----------|---------|---------|
| `ConfigLoaded` | 渲染+钩子执行完成后 | `{ phaseId, scope, configPath, nodeCount }` |
| `ConfigLoading` | 渲染前 | `{ phaseId, scope, configPath }` |
| `ConfigDestroying` | 作用域销毁前 | `{ scope }` |
| `ConfigDestroyed` | 作用域销毁后 | `{ scope }` |
| `FunctionExecuted` | 函数调用完成后 | `{ functionName, isSuccess, error }` |

### 6.3 EventPlatform 结构

```
EventPlatform
  ├─ Register<TEvent>(Action<TEvent>)      // 订阅
  ├─ Unregister<TEvent>(Action<TEvent>)    // 退订
  ├─ Publish<TEvent>(TEvent evt)           // 发布（同步顺序；异常隔离）
  ├─ InvokeFunction(name, params, source)  // 委托 FunctionCallFacade（统一函数入口）
  └─ PublishLifecycle(scope, phase, type)  // 生命周期便捷发布
```

### 6.4 接入点

| 层 | 接入方式 | 效果 |
|----|----------|------|
| **ConfigurationLifecycleManager** | 渲染各阶段 `Publish` 生命周期事件 | 状态栏/副面板订阅刷新 |
| **EventBindingSystem/Orchestrator** | 委托触发改为 `EventPlatform.InvokeFunction` | 函数调用统一经平台可观测 |
| **FunctionCallFacade** | 执行完成 `Publish(FunctionExecuted)` | 结果可订阅（修复 B3 fire-and-forget 不可观测） |
| **RenderStageRunner.DestroyScopeEntities** | 销毁前/后 `Publish` | 订阅者清理旧引用（缓解 Entity 生命周期缺陷） |

### 6.5 三聚拢点统一

| 聚拢点 | 现状 | 目标 |
|--------|------|------|
| 配置加载 | MainForm / LoadConfigPortAdapter / EcsGate（3 处） | **ConfigurationLifecycleManager**（1 处） |
| 事件绑定 | EventBindingSystem / Orchestrator | **EventPlatform**（1 处） |
| 函数调用 | FunctionCallFacade（已统一） | 保留 |

---

## 七、分阶段实施计划

| 阶段 | 内容 | 改动范围 | 风险 |
|------|------|---------|------|
| **P1 止血** | `LoadConfigAndRenderAsync` 渲染后若 `config.InitCommands` 非空则同步执行 | `EcsGate.cs` + `LoadConfigPortAdapter.cs` | 低 |
| **P2 抽象** | 新增 `ConfigurationLifecycleManager`，抽离 MainForm 加载阶段 + InitCommands 消费 | 新建 `Lifecycle/` + `MainForm.cs` 改造 | 中 |
| **P3 声明化** | `UIConfiguration` 增加 `Boot` + `Lifecycle`；`MainLayout.json`/`ConfigSelector.json` 声明化 | `TConfig/Models` + JSON 配置 | 中 |
| **P4 事件平台** | 新增 `EventPlatform`（EventBus + LifecycleBus），生命周期事件发布/订阅；EventBinding 委托经平台 | 新建 `EventPlatform.cs` + `EventBindingSystem.cs` + `FunctionCallFacade.cs` | 高 |
| **P5 加固** | scope 生命周期事件驱动旧控件清理（解决 Entity 生命周期缺陷） | `RenderStageRunner.cs` + 订阅者 | 中 |

### 7.1 每阶段验证

| 阶段 | 验证标准 |
|------|---------|
| P1 | 点击 Load Side → ConfigSelector → `configListView` 已填充文件列表 |
| P2 | 启动加载行为与重构前一致；Load Side 场景仍正常 |
| P3 | `MainLayout.json` 的 `boot` 声明驱动启动；无 `GetInitPhases` 硬编码 |
| P4 | 生命周期事件可订阅；`FunctionExecuted` 可观测 |
| P5 | 复跑 `math_functions_test.json`（47 条命令全执行），无 Invalid entity 崩溃 |

---

## 八、风险与回滚策略

| 风险 | 缓解 |
|------|------|
| 事件平台高耦合 | 接口隔离（`IEventPlatform`）+ 降级开关（未发布时静默跳过） |
| `initCommands` 迁移 | 双支持 fallback，老配置零改动 |
| MainForm 重构回归 | 启动行为逐阶段对比（阶段完成日志 + appliedRatio） |
| 每阶段独立可回滚 | Git 分阶段提交，标注可回滚点 |

---

## 九、公理追溯（对齐 .clinerules）

| 公理 | 关联 |
|------|------|
| `[AX] ScopeGuard` | scope 创建/销毁统一事件管理，命令只作用于有效作用域 |
| `[AX-CORE] SyncProtocolTriple` | 加载提交原子性；生命周期钩子异常时状态回退 |
| `[AX] TruthValueDomainLogic` | 生命周期钩子执行结果三值化（成功/失败/信息不足） |
| `[CR: engine.md §四]` | 五步模型实例化链对齐（Execute→Collect→Evaluate→Decide→Adjust） |
| `[CR: rules.md §5.2]` | CompactDecision/StateRecovery 对齐作用域生命周期 |
| `[CR: engine.md §3.1]` | StateManager 状态变量（operationLog/fileCache）统一管理 |

---

## 十、目标 JSON 示例（前瞻）

### MainLayout.json（改造后示意）

```json
{
  "$schema": "MainLayout.schema.json",
  "version": "2.0",
  "boot": [
    { "id": "MainLayout", "load": "MainLayout.json", "scope": "MainLayout", "host": "root" },
    { "id": "ConfigSelector", "load": "ConfigSelector.json", "scope": "Content", "host": "contentContainer" }
  ],
  "form": { "title": "FileDriven", "width": 1200, "height": 800 },
  "views": [ ... ]
}
```

### ConfigSelector.json（改造后示意）

```json
{
  "description": "配置文件选择器",
  "lifecycle": {
    "onAfterLoad": ["LoadJsonConfigList"]
  },
  "views": [ ... ]
}
```

---

## 附录：涉及文件索引

| 文件 | 角色 | 状态 |
|------|------|------|
| `CsharpPrj/Plate/TConfig/Models/UIConfiguration.cs` | 配置模型扩展（Boot/Lifecycle） | ✅ 已实施 |
| `CsharpPrj/Operation/FrontEndDriven/Lifecycle/ConfigurationLifecycleManager.cs` | 新增：加载编排器 | ✅ 已实施 |
| `CsharpPrj/Operation/FrontEndDriven/Eventing/EventPlatform.cs` | 新增：公共事件平台 | ✅ 已实施 |
| `CsharpPrj/Operation/FrontEndDriven/Gate/EcsGate.cs` | Phase 5：销毁生命周期事件发布 | ✅ 已实施 |
| `FileDriven/FileDriven.Execution/Initial/MainFormGateFactory.cs` | 注册 EventPlatform/LifecycleManager + boot 路径解析 | ✅ 已实施 |
| `FileDriven/FileDriven.Execution/Ports/LoadConfigPortAdapter.cs` | 动态加载接入 LifecycleManager | ✅ 已实施 |
| `FileDriven/FileDriven.Execution/Initial/MainForm.cs` | 去掉硬编码阶段，改调 Bootstrap | ✅ 已实施 |
| `FileDriven/FileDriven.JsonRefer/JsonConfigService.cs` | 配置加载（含模板预处理） | —— 未改（复用） |
| `FileDriven/DrivenConfig/JsonConfig/MainLayout.json` | `boot` 声明化 | ✅ 已实施 |
| `FileDriven/DrivenConfig/JsonConfig/ConfigSelector.json` | `lifecycle.onAfterLoad` 声明化 | ✅ 已实施 |
| `CsharpPrj/Operation/FrontEndDriven/ControlInteraction/EventBinding/EventBindingSystem.cs` | 委托经 EventPlatform | ⏳ 后续迭代（事件平台已就绪） |
| `CsharpPrj/Operation/FrontEndDriven/Gate/FunctionCallFacade.cs` | `FunctionExecuted` 事件发布 | ✅ 已由 EventPlatform.InvokeFunction 发布 |

---

## 附录 B：实施记录（2026-08-01）

### 已完成

| Phase | 内容 | 验证 |
|-------|------|------|
| **P1 止血** | `LoadConfigPortAdapter` 渲染后消费 `InitCommands` | 编译通过 |
| **P2 抽象** | 新增 `ConfigurationLifecycleManager`；`MainForm` 改调 `BootstrapAsync`；`LoadConfigPortAdapter` 改调 `LifecycleManager.LoadAsync` | 编译通过 |
| **P3 声明化** | `UIConfiguration` 增加 `Boot`/`Lifecycle`；`MainLayout.json` 声明 `boot`；`ConfigSelector.json` 迁移 `initCommands` → `lifecycle.onAfterLoad` | 编译通过 |
| **P4 事件平台** | 新增 `EventPlatform`（事件模型 + 订阅/发布 + InvokeFunction）；工厂注册单例 | 编译通过 |
| **P5 加固** | `EcsGate.DoLoadConfig/DoClearScope` 发布 `ConfigDestroying/ConfigDestroyed` 生命周期事件 | 编译通过 |

### 关键设计确认

- `FileDrivenAdapter.ConvertBareNamesToPrefixed` 仅转换 `template`/`style`/`extends` 三个特定字段的值 → 新增的 `boot`（含 `id/load/scope/host`）与 `lifecycle`（含 `onBeforeLoad/onAfterLoad/onBeforeDestroy/onAfterDestroy`）字段名与值均不受影响。
- `FileDrivenConfigValidator` 默认 Loose 模式 → 未知顶层字段仅记录日志，不阻断加载。
- `EventPlatform` 在 `MainFormGateFactory` 中注册为单例，`EcsGate.SetEventPlatform` 注入（懒解析 + null 安全），旧调用路径零破坏。
- `initCommands` 保留为 `Lifecycle.OnAfterLoad` 的 fallback（向后兼容，迁移期双支持）。

### 后续可扩展（迭代方向）

1. **事件平台深度接入 EventBindingSystem**：将 `EventBindingOrchestrator` 的委托触发改为经 `EventPlatform.InvokeFunction`，使按钮点击结果经 `FunctionExecuted` 事件可观测（替代 B3 fire-and-forget 不可观测）。
2. **生命周期订阅示范**：注册 `lifecycle.ConfigLoaded` 订阅者（如状态栏刷新、副面板同步），验证事件平台驱动 UI 联动。
3. **作用域销毁物理清理**：在 `ConfigDestroyed` 订阅者中执行 WinForms 控件物理移除 + 事件卸载（方案 B），彻底解决 Entity 生命周期缺陷。
