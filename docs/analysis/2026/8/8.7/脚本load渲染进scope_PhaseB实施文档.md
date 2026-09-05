# 脚本 load 渲染进 scope — Phase B（P1 根因）实施文档

> 日期：2026-08-07 | 模块：FrontEndDriven / BackEndDriven / FileDriven.Execution
> 目标：让脚本 `load`/`load-config-show` 从「只读文件」升级为「真正渲染进 ECS 渲染世界」，使独立配置（如 ClineBrowser）的控件可被命令 `get/set/click` 命中。
> 前置：Phase A（作用域感知寻址）已落地，`IScopeContext` 活动作用域机制已具备。

---

## 1. 背景与问题

`cline_browser_test.json`（GUI 模式）执行后 9 条 `get/set` 全 FAIL、4 条 `click` FAIL（已由 P4 从假 PASS 修复为真实 FAIL）：

```
4,FAIL,click versionButton,click: 未找到控件实体 'versionButton'（配置可能未渲染进 ECS 渲染世界）
6,FAIL,get versionLabel,get: 未找到控件 'versionLabel'
12,FAIL,set promptInput,set: 未找到控件 'promptInput'
...
```

**根因**：`ClineBrowser.json` 的控件从未进入命令可寻址的 ECS 渲染世界。`LoadSystem.Execute` → `CommandSystemBase.ReadConfigFile` 仅做「读文件 → 返回 JSON 文本 → SetSuccess」，`modifier=show` 被忽略，**不渲染、不注册 ECS 实体、不创建 scope**。

启动引导（boot）之所以能渲染 MainLayout/Content，是因为走的是另一条完整链路：`ConfigurationLifecycleManager.BootstrapAsync → RenderRequestQueue.EnqueueFullLoad → StateCenter.Scopes.LoadAsync → ScopeLifecycleAdapter.RenderAsync`。脚本 `load` 未接入该链路。

---

## 2. 已完成的框架增量（Phase B 的语义前提）

| 阶段 | 内容 | 状态 |
|------|------|------|
| P4 | 事件分发确认：`IInteractionEventPort.Enqueue` 返回 `EventDispatchResult`；`ClickSystem` 未送达绑定链即 FAIL（`outcome=EntityNotFound`） | ✅ 已编译验证 |
| P3 | 结构化结果码：`CommandOutcome`、`CommandResultComponent.Outcome/Target`；get/set 带 `TargetNotFound` | ✅ 已编译验证 |
| Phase A | 作用域感知寻址：`IScopeContext`/`ScopeContext`/`ScopeAwareResolver`；`LoadSystem` 成功加载后置 `CurrentScope=配置基名`；跨 scope 回退诊断 | ✅ 运行时日志验证：`activeScope=ClineBrowser, hitScope=MainLayout` |

Phase B 使 `IScopeContext.CurrentScope=ClineBrowser` 对应的 scope **真正存在并渲染**，命令寻址即可命中真实控件。

---

## 3. 渲染链路机制（关键事实）

`scope` 渲染是一条**硬依赖宿主绑定**的链路：

```
StateCenter.Scopes.LoadAsync(scope, configPath)
  └─ ScopeLifecycleAdapter.RenderAsync
       ├─ ① 宿主解析：HostMountService.ResolveByScope(scope)
       │      └─ 未绑定宿主 → 返回失败 "scope '{scope}' 未绑定宿主，无法渲染"
       ├─ ② 配置加载：_configLoader(configPath)
       ├─ ⑤ UI 线程渲染：_uiDispatcher.InvokeAsync(() => EcsGate.LoadConfigAndRenderAsync(config, scope, host))
       └─ ⑥ MarkMounted（需 result.HostActualChildCount > 0）
```

- **宿主绑定**：`ContainerRegistry.Register(scope, hostKey, ...)`，且 `hostKey` 对应控件必须在 `ControlPool` 中（`HostMountService.ResolveByScope` 经 ControlPool 解析物理控件）。
- **boot 如何绑定宿主**：`BootHostBindingRegistrar.RegisterFromConfig` 扫描 boot 阶段的 `scope/host` 声明 + 视图里 `container.stateScope` 声明；`MainFormGateFactory`（FileDriven 应用层）创建主窗口并注册进 ControlPool。
- **入口**：`ContainerCommandExecutor.LoadToScopeAsync(scope, configPath)`（StateCenter 事务化）→ `ScopeLifecycleAdapter.RenderAsync`。
- **关键结论**：脚本加载的独立配置（ClineBrowser 有 `form` 段、无 boot、无 container 声明）**没有任何人为它创建 Form/宿主** → scope 无法渲染 → 无 ECS 实体。

**已确认**：CsharpPrj 内不存在「从配置 `form` 段创建新窗体」的可复用工厂（MainForm 创建在 FileDriven 应用层）。

---

## 4. 缺失能力

「**把独立配置作为新 scope 展示**」，含三步：
1. 创建宿主窗体（配置含 `form` 段时）；
2. 注册 scope↔宿主绑定（ContainerRegistry）+ 宿主控件入 ControlPool；
3. 触发 scope 渲染（ContainerCommandExecutor.LoadToScopeAsync）。

---

## 5. 方案设计

### 5.1 新增 `IConfigLoadRenderService`（唯一「加载即渲染」权威服务）

```
LoadAndShow(configPath, options) → LoadRenderResult
  1. 读 + 解析配置（复用 JsonConfig 加载）
  2. 定 scope 名 = 配置基名（如 "ClineBrowser"）；置 IScopeContext.CurrentScope（已具备）
  3. 宿主映射（见 §6 决策点）：
       - 配置含 form 段 → 新建 Form（title/width/height 来自 config.Form），Show，注册进 ControlPool
       - 否则 → 解析现有容器宿主（host 提示 / contentContainer）
  4. 注册宿主绑定：HostMountService.Register(scope, hostKey=form.Name) / 或 ContainerRegistry.Register
  5. 渲染：ContainerCommandExecutor.LoadToScopeAsync(scope, configPath)（StateCenter 事务化）
  6. 消费 ScopeLoadResult/RenderRunResult → 命令 SetSuccess/SetFailure + CommandOutcome
```

### 5.2 改造命令

- `LoadSystem`/`SideloadSystem` 由 `ReadConfigFile`（读文本）改为委托 `IConfigLoadRenderService`；`modifier=show` 语义生效。
- 保留向后兼容：`load` 仍可返回渲染结果摘要到 `CommandResultComponent.ResultValue`。

### 5.3 装配

- `PipelineCompositionFactory` / `StateServiceCollection.RegisterAll`：注册 `IConfigLoadRenderService` 到 AppServiceProvider。

---

## 6. 决策点（宿主映射与异步桥）

### 6.1 宿主映射
- **方案 A（推荐）**：配置含 `form` 段 → **独立新窗体**。符合 ClineBrowser 测试器定位；每个脚本配置独立 scope/窗口，互不干扰。
- 方案 B：渲染进现有容器（如 contentContainer）。与已有 `Content` scope 冲突风险高，不推荐。

### 6.2 同步命令 ↔ 异步渲染桥
- 方案 ①（推荐先做）：命令内 `_uiDispatcher.InvokeAsync(...).GetAwaiter().GetResult()`，需 UI 线程守卫（GUI 模式命令管线本就在 UI 线程，防自锁）。
- 方案 ②（更健壮）：命令置 Pending，渲染完成后回调置 Success（引入命令异步状态模型，改动大）。

---

## 7. 文件落点

| 文件 | 改动 |
|------|------|
| `CsharpPrj/Operation/FrontEndDriven/.../LoadRender/IConfigLoadRenderService.cs` | 新增：唯一「加载即渲染」权威服务 |
| `.../LoadRender/ConfigLoadRenderService.cs` | 新增：实现（Form 宿主创建 + 宿主绑定 + 渲染） |
| `.../LoadRender/FormHostFactory.cs` | 新增：「配置 form 段 → WinForms Form」工厂 |
| `BackEndDriven/CommandExecution/CommandSystems.cs`（LoadSystem/SideloadSystem） | 委托该服务，消费渲染结果 |
| `FrontEndDriven/Initial/RenderPipeline/PipelineCompositionFactory.cs` | 装配 `IConfigLoadRenderService` |
| `StateServiceCollection` / `MainFormGateFactory` | 宿主创建依赖（如 ControlPool 注册入口） |

---

## 8. 实施步骤

1. **FormHostFactory**：实现「配置 `form` 段 → Form」，设置 Title/ClientSize，注册进 ControlPool（按 `form.Name`）。
2. **ConfigLoadRenderService**：组合 宿主创建/绑定 + `ContainerCommandExecutor.LoadToScopeAsync`，返回结构化结果。
3. **命令接入**：`LoadSystem`/`SideloadSystem` 委托，`modifier=show` 生效；保留 ResultValue。
4. **异步桥**：UI 线程守卫的同步桥（方案 ①）。
5. **装配**：注册服务到 AppServiceProvider。
6. **回归**：本机运行 `cline_browser_test.json`（见 §10）。

---

## 9. 风险与验证要求

- **必须 GUI 端到端运行验证**：创建新窗体、注册宿主、渲染、命令命中、`clearButton` 命中 ClineBrowser 而非 MainLayout——需实际运行 WinForms 界面，本开发环境无法启动界面做端到端回归。
- **回归面**：boot 渲染路径不触碰（不动 `ConfigurationLifecycleManager`/`ScopeLifecycleAdapter`）；需验证新 Form 的销毁生命周期（避免孤儿实体/重复注册）。
- **异步桥死锁**：方案 ① 需确认调用线程；若命令管线不在 UI 线程，需改用方案 ② 或 await 桥。

---

## 10. 验收标准

| # | 验收项 | 判据 |
|---|--------|------|
| 1 | ClineBrowser 渲染 | 日志出现 `ClineBrowser` scope 渲染/收敛记录，新窗体出现 |
| 2 | 命令命中 | `get versionLabel` / `set promptInput` / `click versionButton` 等由 FAIL 转 PASS |
| 3 | clearButton 命中正确 scope | `click clearButton` 命中 ClineBrowser.clearButton（不再 `hitScope=MainLayout`） |
| 4 | 活动 scope | 日志 `Command_Load_ActiveScope: scope=ClineBrowser` |
| 5 | 结果契约 | `GUI 模式脚本执行完成: success=false`（存在真实 FAIL 时）；全 PASS 时为 true |

---

## 附录：关键代码位置速查

- `ContainerCommandExecutor.LoadToScopeAsync`：`CsharpPrj/Operation/FrontEndDriven/State/Commands/ContainerCommandExecutor.cs`
- `ScopeLifecycleAdapter.RenderAsync`（宿主解析 @①）：`CsharpPrj/Operation/FrontEndDriven/State/Projection/ScopeLifecycleAdapter.cs`
- `HostMountService`（ResolveByScope / Register）：`CsharpPrj/Operation/FrontEndDriven/WinForms/Container/HostMountService.cs`
- `BootHostBindingRegistrar.RegisterFromConfig`：`CsharpPrj/Operation/FrontEndDriven/State/Projection/BootHostBindingRegistrar.cs`
- `LoadSystem` / `ReadConfigFile`：`CsharpPrj/Operation/BackEndDriven/CommandExecution/CommandSystems.cs`、`CsharpPrj/Share/CommandFramework/Execution/CommandSystemBase.cs`
- `MainFormGateFactory`（boot 窗体/宿主创建入口）：`FileDriven/FileDriven.Execution/Initial/MainFormGateFactory.cs`

