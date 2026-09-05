# LoadSide 调用链分析与重构设计

> 版本：v1.0  
> 日期：2026-08-03  
> 范围：FileDriven 框架中 `MainLayout.json` 的 LoadSide（`LoadSidePanel`）按钮调用链  
> 状态：**代码已全部实施**（入口收敛/UI 解耦、绑定来源优先级单一化+冲突检测、投影器职责拆分均已完成；§5.3 编译通过，UI 人工验证待执行）

---

## 1. 调用链现状分析

### 1.1 配置声明（MainLayout.json）

```jsonc
// 按钮（工具栏第5个）
{
  "type": "button",
  "name": "loadSideButton",
  "text": "Load Side",
  "functionName": "LoadSidePanel",          // ← 函数名与注册名一致
  "events": { "click": ["LoadSidePanel"] }
}

// 承载容器（主分割器右侧面板）
{
  "type": "container",
  "name": "sideContainer",                   // ← UIControlNames.SideContainer = "sideContainer"
  "container": { "sourceType": "state", "stateScope": "Side", "autoLoad": false }
}
```

### 1.2 完整调用链

```
┌─ 配置声明层 ─────────────────────────────────────────────┐
│ MainLayout.json                                          │
│   • 按钮 loadSideButton: events.click → "LoadSidePanel"  │
│   • 容器 sideContainer: container:                     │
│       {sourceType:"state", stateScope:"Side"}            │
└────────────────────────┬─────────────────────────────────┘
                         ▼ [① click 事件]
┌─ 事件路由层 (CommandFramework / TConfig) ─────────────────┐
│ ClickCommandHandler.HandleClick                            │
│   → 解析 click 绑定，FunctionName = "LoadSidePanel"        │
│   → ParameterExecutionWorkflow.ExecuteAsync               │
│     (WorkflowMethodExecutionWorkflow)                      │
└────────────────────────┬───────────────────────────────────┘
                         ▼ [② 执行注册函数]
┌─ 函数注册层 (FunctionLibrary) ────────────────────────────┐
│ UIToolbox.RegisterAll                                      │
│   Register("LoadSidePanel") → LoadSidePanelShared()        │
│   → LoadConfigToScope("Side", "sideContainer")             │
│     • 弹出 OpenFileDialog 选择 JSON 配置文件               │
│     • 注入 ILoadConfigPort (AppServiceProvider.Resolve)    │
└────────────────────────┬───────────────────────────────────┘
                         ▼ [③ LoadConfigAsync(filePath, "Side", "sideContainer")]
┌─ 端口适配层 ──────────────────────────────────────────────┐
│ LoadConfigPortAdapter.LoadConfigAsync                      │
│   → resolve ContainerCommandExecutor                       │
│   → executor.LoadToScopeAsync("Side", configPath)          │
└────────────────────────┬───────────────────────────────────┘
                         ▼ [④ 状态命令层]
┌─ 状态执行器 (State.Commands) ─────────────────────────────┐
│ ContainerCommandExecutor.LoadToScopeAsync                  │
│   • 幂等检查：同路径且状态非 Failed → 直接返回              │
│   • 创建 ContainerState(scope=Side, Loaded)               │
│   → projector.RenderAsync("Side", state)                   │
└────────────────────────┬───────────────────────────────────┘
                         ▼ [⑤ 投影器]
┌─ 投影器 (State.Projection) ───────────────────────────────┐
│ ContainerProjector.RenderAsync                             │
│   ① bindingStore.ResolveHostKey("Side") → "sideContainer"  │
│   ② hostResolver("sideContainer") → 宿主 Control           │
│   ③ 加载 UIConfiguration (JsonConfigService)               │
│   ④ UI线程 → gate.LoadConfigAndRenderAsync(config,        │
│                scope="Side", host=sideContainer)           │
└────────────────────────┬───────────────────────────────────┘
                         ▼ [⑥ ECS 渲染]
┌─ ECS 门层 (Gate) ─────────────────────────────────────────┐
│ EcsGate.LoadConfigAndRenderAsync                           │
│   → DoLoadConfig: destroy 旧 scope →                      │
│     composition.Execute(config, sideContainer, "Side")     │
│   → ExecuteRenderStage() 渲染实体到 sideContainer          │
└────────────────────────────────────────────────────────────┘
```

### 1.3 关键绑定建立时机

1. **声明来源**：`sideContainer` 声明 `sourceType:"state"` + `stateScope:"Side"`（MainLayout.json:162）
2. **注册时机**：启动时 `StateServiceCollection.RegisterAll` → `BootHostBindingRegistrar.RegisterFromConfig(bootConfig, bindingStore)`
3. **扫描逻辑**：`BootHostBindingRegistrar.ScanControls` 递归扫描 Controls/Panel1Controls/Panel2Controls/Tabs，命中 state 容器即 `bindingStore.Bind(stateScope, control.Name)` → **"Side" → "sideContainer"**
4. **宿主解析**：`ContainerProjector.hostResolver = rootHostResolver ?? controlPool.GetControl`

---

## 2. 问题清单

| # | 问题 | 位置 | 严重度 |
|---|------|------|--------|
| 1 | 入口重复注册：`LoadSidePanel`/`BrowseConfigFile` 指向同一实现 | `UIToolbox.cs` | 中 |
| 2 | UI 与逻辑耦合：OpenFileDialog 内联在函数注册层，不可测试 | `UIToolbox.LoadConfigToScope` | 高 |
| 3 | sync-over-async：`GetAwaiter().GetResult()` 内联在 UI 线程，有死锁风险 | `UIToolbox.cs`（原） | 高 |
| 4 | 未使用参数：`targetControlName` 在端口适配层根本没用到 | `LoadConfigPortAdapter.cs` | 低 |
| 5 | 双轨绑定机制：boot 数组 host 声明 + state 容器 stateScope 声明都建绑定 | `BootHostBindingRegistrar` + JSON | 中 |
| 6 | 投影器职责过重：宿主解析/配置加载/生命周期/UI线程/事件发布全在一个方法 | `ContainerProjector.RenderAsync` | 高 |
| 7 | `LoadConfigToTarget` 与 `LoadConfigToScope` 管线重复 | `UIToolbox.cs` | 中 |

---

## 3. 重构目标设计

### 3.1 入口收敛：统一"加载配置到指定容器"

```
现状（3 个入口，职责重叠）           目标（2 个标准入口，职责分离）
─────────────────────────            ──────────────────────────────
LoadSidePanel  ─┐
BrowseConfigFile┼→ 交互入口 LoadConfigToContainerInteractive(container)
LoadConfig     ─┘
                                      逻辑入口 LoadConfigFromPath(path, scope)
LoadConfigToTarget ─→ 按路径加载（无对话框）
```

- **交互入口** `LoadConfigToContainerInteractive(targetContainer)`：负责弹窗选文件，然后调用逻辑入口
- **逻辑入口** `LoadConfigFromPath(configPath, scopeName)`：纯逻辑，无 UI 交互，可被命令/列表项复用

### 3.2 职责分层

| 层 | 组件 | 职责 |
|----|------|------|
| 交互层 | `LoadConfigToContainerInteractive` | 文件对话框、用户交互 |
| 逻辑层 | `LoadConfigFromPath` | 路径 → 作用域 → 端口调用 |
| 端口层 | `ILoadConfigPort` / `LoadConfigPortAdapter` | 适配状态管线，屏蔽实现细节 |
| 状态层 | `ContainerCommandExecutor` | 状态幂等 / 版本管理 |
| 投影层 | `ContainerProjector` | 宿主解析 + 配置加载 + 渲染编排 |

### 3.3 消除 sync-over-async

统一使用 `BackEndDriven.Utilities.AsyncHelper.RunSync`：

```csharp
// AsyncHelper 内部使用 Task.Run 包装，避免 UI 线程同步阻塞/死锁
BackEndDriven.Utilities.AsyncHelper.RunSync(
    () => port.LoadConfigAsync(configPath, scopeName), 30_000);
```

> 注：注册表 `ITrConfigRegistry.Register(name, Delegate)` 接受任意委托，但函数分发链
> （FunctionHub auto-wrap → RGE 单节点工作流）最终按同步 `ITrConfig.Invoke` 调用，
> 故保留 `Func<bool>` 签名，仅替换内部阻塞策略。

---

## 4. 已实施改动（本任务完成部分）

### 4.1 `FileDriven.Abstractions/Ports/ILoadConfigPort.cs`

```diff
- Task LoadConfigAsync(string configPath, string scopeName, string? targetControlName);
+ Task LoadConfigAsync(string configPath, string scopeName);
```

理由：`targetControlName` 从未被适配器使用；宿主由 `HostBindingStore` 按 scope 解析，参数冗余。

### 4.2 `FileDriven.Execution/Ports/LoadConfigPortAdapter.cs`

```diff
- public async Task LoadConfigAsync(string configPath, string scopeName, string? targetControlName)
+ public async Task LoadConfigAsync(string configPath, string scopeName)
```

### 4.3 `FileDriven.Business/Registrations/UI/UIToolbox.cs`

注册收敛：

| 注册名 | 原实现 | 新实现 |
|--------|--------|--------|
| `LoadConfig` | `LoadConfigToScope("Content", ...)` | `LoadConfigToContainerInteractive(ContentContainer)` |
| `LoadSidePanel` | `LoadSidePanelShared()` | `LoadConfigToContainerInteractive(SideContainer)` |
| `BrowseConfigFile` | `LoadSidePanelShared()` | `LoadConfigToContainerInteractive(SideContainer)` |
| `LoadConfigToTarget` | 内联端口调用 + `GetAwaiter().GetResult()` | `ResolveScopeFromContainer` + `LoadConfigFromPath` |

私有方法替换：

```csharp
// 容器名 → 作用域名（唯一映射处）
private static string ResolveScopeFromContainer(string targetContainer);

// 交互入口：弹窗选文件 → 加载到目标容器
private static bool LoadConfigToContainerInteractive(string targetContainer);

// 逻辑入口：按路径加载到作用域（无 UI 交互）
private static bool LoadConfigFromPath(string configPath, string scopeName);
```

旧的 `LoadSidePanelShared()` / `LoadConfigToScope(...)` 已删除。

---

## 5. 待办事项（后续步骤）

### 5.1 绑定机制单一化 + 冲突检测（风险：中）（已实施 2026-08-03）

目标：消除"A 轨 boot host 声明 + B 轨 state 容器声明"双轨绑定。

**已实施方案（来源优先级单一化）**：

```
绑定来源优先级唯一权威: state 容器声明(json-declared) > boot 兜底(boot-declared) > code-registered
# state 容器是唯一事实源: 同 scope host 冲突时以 state 声明覆盖 boot/代码绑定(BindAuthoritative)
# boot 仅作兜底: 为无 state 容器声明的启动阶段 scope(如 MainLayout)提供绑定(BindIfAbsent)
```

```csharp
// BootHostBindingRegistrar 已实现:
//   BindIfAbsent  (boot 兜底)  : scope 未绑定才注册; 已绑定不覆盖
//   BindAuthoritative(state容器): 未绑定→绑定 | host 一致→提升来源标记 | host 冲突→覆盖并记 HostBindingRegistrar_Override
//   ScanControls  扫描到 state 容器 → 调用 BindAuthoritative(state 容器为权威)
```

**JSON 已清理**（MainLayout.json）：
- `ConfigSelector` 条目已删除 `"host": "contentContainer"`（state 容器已声明，由 BindAuthoritative 覆盖/统一来源）
- `MainLayout` 条目**保留** `"host": "root"`——该 scope 无 state 容器声明（无 `stateScope="MainLayout"` 容器），boot 是唯一绑定来源；删除会致 `BootstrapAsync` 启动渲染抛"scope 未绑定宿主"异常

**边界说明**：文档早前"boot 不再声明 host"的目标不适用于 `MainLayout` 类「启动阶段 scope」（仅存在于 boot、无 state 容器兜底）。最终语义为：**state 容器是唯一事实源，boot 为无声明 scope 提供兜底**；对同一 scope 的双轨声明，state 容器优先。

### 5.2 投影器按职责拆分（风险：高）

`ContainerProjector.RenderAsync` 拆分为：

```
ContainerProjector.RenderAsync(scope, state)
 ├── ScopeHostResolver        ← 绑定解析 + 宿主解析
 ├── ConfigLoadPipeline       ← 配置加载 + onBeforeLoad 钩子
 ├── UIDispatchRenderer       ← UI线程调度 → EcsGate 渲染
 ├── ResultPublisher          ← Loading/Loaded 事件发布
 └── 编排：依序调用上述组件
```

### 5.3 验证清单

- [x] `dotnet build FileDriven/FileDriven.sln` 编译通过
- [x] `dotnet build CsharpPrj/MainProj.sln` 编译通过（含 BootHostBindingRegistrar 改动所在项目）
- [ ] UI 启动后点击 `Load Side`，OpenFileDialog 正常弹出
- [ ] 选择 JsonConfig 下配置文件，渲染到侧边栏 `sideContainer`
- [ ] `Load Config`（Content）与 `LoadConfigToTarget` 路径不受影响
- [ ] 重复加载同一路径幂等（不重建 scope）
- [ ] `LoadConfigToTarget` 从列表项加载（无对话框）仍正常
- [ ] 启动时 `MainLayout` scope 经 boot 兜底绑定仍渲染到 root（验证 §5.1 边界未破坏）
- [ ] Content 双轨 host 冲突时 state 容器优先（临时改 JSON 或日志断言 `HostBindingRegistrar_Override`）

---

## 6. 附：探索过程中确认的关键机制

### 6.1 函数分发链（事件 → 注册函数）

```
ClickCommandHandler.HandleClick
  → WorkflowMethodExecutionWorkflow.ExecuteAsync      (IParameterExecutionWorkflow)
    → WorkflowMethodExecutionService.ExecuteFunctionAsync (IEventExecutionService)
      → FunctionHub.Resolve(functionName)
        ├─ 已注册工作流 → 直接执行
        └─ 注册表函数   → AutoWrapAsWorkflow（单节点工作流）→ RGE 执行
```

**结论**：注册表函数必须保留同步签名（`Func<bool>`），因为分发链最终走 `ITrConfig.Invoke` 同步调用。

### 6.2 `ITrConfigRegistry.Register` 签名

```csharp
void Register(string name, Delegate function, string? description = null);
```

接受任意 `Delegate`，因此同步 `Func<bool>` 与异步 `Func<Task<bool>>` 均可注册；
但鉴于 6.1 的分发链，本重构保留同步签名，内部用 `AsyncHelper.RunSync` 处理阻塞。

### 6.3 `AsyncHelper.RunSync` 实现要点

```csharp
public static TResult RunSync<TResult>(Func<Task<TResult>> asyncFunc, int timeoutMilliseconds = 30000)
{
    var task = Task.Run(asyncFunc);   // ← 关键：脱离 UI 上下文，避免死锁
    if (!task.Wait(timeoutMilliseconds))
        throw new TimeoutException(...);
    return task.GetAwaiter().GetResult();
}