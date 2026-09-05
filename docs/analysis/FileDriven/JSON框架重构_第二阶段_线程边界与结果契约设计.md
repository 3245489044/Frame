# JSON 框架重构（第二阶段）：统一线程边界 + bool 结果契约

> **日期**：2026-08-01
> **前置**：第一阶段已实施「加载阶段抽象 + 公共事件平台」（见 `JSON框架重构_加载阶段抽象与公共事件平台设计.md`，P1-P5 全部完成）
> **触发**：复跑 `math_functions_test.json` 日志暴露三个框架层缺失原语
> **战略**：从「点修复」升级为「框架级贯通」——线程边界、函数结果契约、加载管线三原语统一

---

## 一、日志暴露的框架层缺陷

### 1.1 关键日志

```
[23:09:44.350] [EXEC] 同步执行: function=LoadConfigToTarget, parameters={...}
[23:09:44.375] [ERROR] UIToolbox 加载配置到目标容器失败: 在某个线程上创建的控件不能成为在另一个线程上创建的控件的父级
[23:09:44.431] [INFO] FunctionCallFacade: 执行完成 — LoadConfigToTarget, isSuccess=true   ← 矛盾
[23:09:44.433] [INFO] [EXEC] 成功: LoadConfigToTarget, result=加载失败: ...
```

### 1.2 三个缺陷

| 缺陷 | 现象 | 根因 |
|------|------|------|
| **A：线程错位** | 侧边栏渲染失败（跨线程创建控件） | 脚本管线在后台线程 → 渲染控件时直接加 UI 线程的 `sideContainer` → `InvalidOperationException` |
| **B：结果契约缺失** | 加载失败却 `isSuccess=true` | 注册函数 `catch { return "加载失败: ..." }` 把错误变成字符串；门面以"是否抛异常"判成功 → 字符串失败被误判成功 |
| **C：加载管线部分分散** | 各路径自行处理线程/错误 | 启动走 LifecycleManager，脚本 `LoadConfigToTarget` 经 UIToolbox 手动 catch → 契约不统一 |

---

## 二、目标架构（两新原语 + 一强化管线）

```
┌─────────────────────────────────────────────────────────────┐
│           ① UI 线程边界原语（新增）                            │
│         IUiThreadDispatcher / WinFormsUiThreadDispatcher    │
│  ├─ 所有 WinForms 渲染/控件操作统一经此切到 UI 线程            │
│  ├─ 消除各路径自行判断 InvokeRequired                         │
│  └─ 线程错位 → 不可能发生                                     │
└──────────────┬──────────────────────────────────────────────┘
┌──────────────▼──────────────────────────────────────────────┐
│           ② 函数结果契约原语（bool 失败判据）                  │
│  ├─ 注册函数成功 → return true                               │
│  ├─ 注册函数失败 → return false（错误详情经 FsLog 记录）       │
│  └─ FunctionCallFacade：bool 返回值 → IsSuccess = bool       │
└──────────────┬──────────────────────────────────────────────┘
┌──────────────▼──────────────────────────────────────────────┐
│           ③ 加载管线强化（既有 LifecycleManager）             │
│  ├─ 渲染前自动切 UI 线程（dispatcher.InvokeAsync）            │
│  ├─ 失败经 bool 契约传播（不再静默）                          │
│  └─ 所有加载源（脚本/按钮/InitCommands/生命周期钩子）统一走此管线│
└─────────────────────────────────────────────────────────────┘
```

---

## 三、详细设计

### 3.1 UI 线程边界原语

**新增文件**：`CsharpPrj/Operation/FrontEndDriven/Threading/IUiThreadDispatcher.cs` + `WinFormsUiThreadDispatcher.cs`

```csharp
public interface IUiThreadDispatcher
{
    /// <summary>在 UI 线程执行异步操作（本线程即 UI 时直接执行）</summary>
    Task InvokeAsync(Func<Task> action);

    /// <summary>在 UI 线程执行同步操作（带结果）</summary>
    Task<T> InvokeAsync<T>(Func<T> func);
}

public sealed class WinFormsUiThreadDispatcher : IUiThreadDispatcher
{
    // 委托已有 UiThreadHelper.RunOnUiThreadWhenReady（句柄未创建回退 SynchronizationContext）
    // 无宿主控件时回退 Application.OpenForms 首窗体 / SynchronizationContext
}
```

**接入点**（框架级，消除分散判断）：

| 调用方 | 现状 | 改造后 |
|--------|------|--------|
| `ConfigurationLifecycleManager.LoadAsync` | 直接渲染 | 渲染前 `dispatcher.InvokeAsync(渲染)` |
| `LoadConfigPortAdapter.LoadConfigAsync` | 委托 LifecycleManager（已统一） | 不变（渲染已由 LifecycleManager 切线程） |
| `WinFormApplier` 控件创建 | 后台线程创建 | 加 dispatcher 防线（根治跨线程父子） |
| `CommandExecution` click/set | 各自判断（DispatchIfNeeded 已有） | 预留接口统一（后续） |
| `EventPlatform.InvokeFunction` | 直接经 FunctionCallFacade | 预留：如需 UI 操作由调用方经 dispatcher |

### 3.2 bool 结果契约

**原则**：
- 注册函数**成功 → `return true`**；**失败 → `return false`**（错误详情经 `FsLog.Log` 记录，保留可观测）
- 不依赖异常抛出传播（避免 WinForms 线程上异常副作用）
- bool 是二值判据；字符串输出保留（非失败逻辑，如成功信息）

**FunctionCallFacade 增强**（框架级适配）：

```csharp
// FunctionCallFacade 返回值判定：
// 返回值是 bool → IsSuccess = (bool)value
// 返回值是 string → 保持现状（非失败逻辑）
```

**涉及函数改造**：

| 函数 | 现签名 | 改后签名 | 行为 |
|------|--------|---------|------|
| `LoadConfigToTarget` | `Func<string,string,string>` | `Func<string,string,bool>` | 成功 true / 失败 false（FsLog 记详情） |
| `LoadConfigToScope`（私有） | `Func<string>` | `Func<bool>` | 同上 |
| `BrowseConfigFile` | `Func<string>` | `Func<bool>` | 委托 LoadConfigToScope |

### 3.3 加载管线强化（LifecycleManager）

- `LoadAsync` 渲染外包 `dispatcher.InvokeAsync` → 线程安全
- 钩子经 `EventPlatform.InvokeFunction` → 其内部发布 `FunctionExecuted`（可观测）
- 失败经 bool 契约回传

---

## 四、实施计划

| 步骤 | 内容 | 层 | 风险 |
|------|------|----|------|
| **S1** | 新增 `IUiThreadDispatcher` + `WinFormsUiThreadDispatcher`（复用 UiThreadHelper） | 线程边界原语 | 低 |
| **S2** | 装配：`MainFormGateFactory` 注册 dispatcher 单例；注入 LifecycleManager / AppServiceProvider | 装配 | 低 |
| **S3** | `ConfigurationLifecycleManager.LoadAsync` 渲染前经 dispatcher；`WinFormApplier` 控件创建加防线 | 接入线程边界 | 中 |
| **S4** | `FunctionCallFacade` 识别 bool 返回值 → `IsSuccess = bool` | 结果契约 | 中 |
| **S5** | `UIToolbox.LoadConfigToTarget/LoadConfigToScope` 改为返回 bool；依赖方适配 | 结果契约落地 | 中 |
| **S6** | 复跑 `math_functions_test.json`：加载成功（线程已切 UI）+ 失败正确显示（bool 判据生效） | 验证 | —— |

### 4.1 每步验证

| 步骤 | 验证 |
|------|------|
| S1 | dispatcher 单测：UI 线程直接执行；后台线程切 UI |
| S2 | 构建通过；AppServiceProvider.Resolve<IUiThreadDispatcher> 非空 |
| S3 | `LoadConfigToTarget` 不再报跨线程；`sideContainer` 控件加载成功 |
| S4 | `FunctionCallFacade` 对 bool 返回值正确判定 IsSuccess |
| S5 | `LoadConfigToTarget` 失败时脚本显示 `[EXEC] 失败`（非误报成功） |
| S6 | 47 条命令全执行；无 Invalid entity / 无线程错位 / 失败可见 |

---

## 五、与第一阶段架构的关系

| 第一阶段 | 本阶段 |
|----------|--------|
| P2 `ConfigurationLifecycleManager`（加载编排） | ② 加载管线强化：内部切 UI 线程 |
| P4 `EventPlatform`（公共事件平台） | ① 事件平台经 dispatcher 执行 UI 操作（预留） |
| P1-P3 加载/声明化 | 保留不变 |
| —— | ③ 新增 `IUiThreadDispatcher` 原语 |
| —— | ④ 新增 `bool` 结果契约 |

**原则**：线程边界、结果契约、加载管线各为单一框架责任，任何调用源统一经三原语，无旁路。

---

## 六、公理追溯

| 公理 | 关联 |
|------|------|
| `[AX] StateSnapshot` | 任何操作基于快照确定性；线程错位违反确定性 |
| `[AX-CORE] SyncProtocolTriple` | CommitPhase 逻辑原子性；UI 线程边界保证提交一致性 |
| `[AX] TruthValueDomainLogic` | bool 结果契约三值化（true/false/信息不足→日志） |
| `[CR: axioms.md:ToolCallWellFormedness]` | 函数参数/返回值契约精确匹配 |
| `[CR: rules.md §5.1 OnError]` | bool 失败触发错误处理链；error 详情经日志可观测 |

---

## 附录：涉及文件

| 文件 | 改动 |
|------|------|
| `CsharpPrj/Operation/FrontEndDriven/Threading/IUiThreadDispatcher.cs` | 新增 |
| `CsharpPrj/Operation/FrontEndDriven/Threading/WinFormsUiThreadDispatcher.cs` | 新增 |
| `CsharpPrj/Operation/FrontEndDriven/Lifecycle/ConfigurationLifecycleManager.cs` | LoadAsync 渲染切 UI 线程 |
| `CsharpPrj/Operation/FrontEndDriven/WinForms/Applier/WinFormApplier.cs` | 控件创建加 dispatcher 防线 |
| `CsharpPrj/Operation/FrontEndDriven/Gate/FunctionCallFacade.cs` | bool 返回值判定 |
| `FileDriven/FileDriven.Execution/Initial/MainFormGateFactory.cs` | 注册 dispatcher 单例 |
| `FileDriven/FileDriven.Business/Registrations/UI/UIToolbox.cs` | LoadConfigToTarget/LoadConfigToScope 改 bool |