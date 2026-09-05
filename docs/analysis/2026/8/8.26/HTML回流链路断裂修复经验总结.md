# HTML 回流链路断裂修复经验总结（agent_html_view_switch_test）

> 日期：2026-08-26 | 模块：`CsharpPrj/Plate/HtmlViewer`（HtmlViewer / HtmlAssetRegistry / HtmlScriptBridge / HtmlEventBridge）+ `Projects/Agent/src/Agent.Launcher`（AgentUiLibraryHtml / HtmlRuntimeReadinessReport）
> 状态：已修复并经 GUI 冒烟 `agent_html_view_switch_test` 验证通过
> 性质：三层根因逐步定位的经验复盘——同一"点击无效"现象背后叠了三层独立断点

---

## 1. 背景与现象

`FileDriven/DrivenConfig/Scripts/Agent/agent_html/agent_html_view_switch_test.json` 是主对话框三视图（nodes/task/history）切换 + 任务内切换的 GUI 冒烟脚本。连续多次运行均"流程成功但功能静默失败"：

- 工作流 `Success=true`（65 条命令按序执行完、无硬错误），但真正的功能断言全部落空；
- `click task-vh_task_a / task-vh_task_b` → `NOT_FOUND`（任务视图从未渲染出任务按钮）；
- `AgentUi_HtmlAttach` 早期返回 `回流探针=缺失`，后期（修复后）为 `回流探针=OK` 但仍 NOT_FOUND。

> 关键：`Success=true` 只代表脚本命令无硬错误，不代表功能通过——`NOT_FOUND` 被当作普通函数返回值记录，不触发脚本失败。真正的失败藏在回流自检标志里。

---

## 2. 三条核心链路（先建立心智模型）

```
C#（后台线程/FunctionExecutor）
  │ ① C#→JS  DOM 指令（EnsureCoreWebView2Async → __domBridge.handle → ExecuteScriptAsync）
  ▼
WebView2 document（含 [data-cmd] 按钮，挂 document 级点击监听器）
  │ ② JS→C#  回流（点击 → window.chrome.webview.postMessage → OnWebMessageReceived → MessageReceived → 路由 → 重渲染）
  ▼
C# 消息桥
```

三层根因分别击中 ①②③ 中三处不同环节，且**任意一环断**都表现为同一个"点击无效"。逐层排除后才定位全。

---

## 3. 第一层根因：线程封送缺陷（回流自检异常）

### 现象
```
回流自检异常 1/3: InvalidOperationException: The method EnsureCoreWebView2Async can be invoked only from the UI thread.
回流自检异常 2/3: InvalidOperationException: CoreWebView2 can only be accessed from the UI thread.
```

### 根因
`HtmlViewer.RunOnUiThreadAsync` 只在 `InvokeRequired && IsHandleCreated` 时才封送 UI 线程。装配/探针由后台线程（FunctionExecutor）发起，而**挂载前 `IsHandleCreated=false`** → 直接在后台线程执行 `EnsureCoreWebView2Async` → 抛"仅限 UI 线程"。

连锁后果：`EnsureReadyAsync` 抛错 → `CoreWebView2` 恒为 null → `EnsureMessageBridgeInstalled` 因 `CoreWebView2 is null` **永远不订阅 `WebMessageReceived`** → 回流链从未建立 → postMessage 到不了 C#。

> 该异常在修复前被 `catch{}` 静默吞掉，是"点击无效却无任何日志"的直接元凶之一。

### 修复（`HtmlViewer.cs`）
- `RunOnUiThreadAsync`（两个重载）改为**三路封送**：
  ① 句柄已创建 → `Control.InvokeAsync`（原路径）；
  ② 后台线程但句柄未创建（sideload/EcsGate 挂载前）→ 回退**构造时捕获的 `SynchronizationContext.Post`**；
  ③ 已在 UI 线程 → 直接执行。
- 构造函数捕获 `SynchronizationContext.Current`（UI 线程创建控件时为 `WindowsFormsSynchronizationContext`）。
- 封送回退带 **5s 超时兜底**（对齐 `UiDispatcherCore`，防 UI 泵阻塞时无限挂起）。

### 验证
- `回流链路自检通过（postMessage → C# 消息桥入口闭环）`；`AgentUi_HtmlAttach` → `已装配 ... 回流探针=OK`。

---

## 4. 第二层根因：window 级幂等守卫跨重渲染失效

### 现象
第一层修好后探针/装配全绿，但 `click task-*` 仍 `NOT_FOUND`；`probe={"installed":true}`（桥在）但点击仍无 postMessage。

### 根因
`BridgeScript` 幂等守卫用 `window.__domBridgeInstalled`。WebView2 的 `NavigateToString` 每次重渲染会**替换 document 但保留 window**：
- 新 document **没有 document 级点击监听器**；
- `IsBridgeReadyAsync` 查 `window.__domBridgeInstalled` → 仍为 true → `EnsureBridgeLoadedAsync` 误判"已就绪" → **不再重新注入** → 新 document 缺点击监听器。

### 修复（document 级守卫）
- `HtmlAssetRegistry.cs` `BridgeScript`：`window.__domBridgeInstalled` → `document.__domBridgeInstalled`（重渲染即重新注入、重新挂新 document 监听器）；
- `HtmlViewer.cs` `IsBridgeReadyCoreAsync`：就绪查询同步改 `(!!document.__domBridgeInstalled)`；
- 诊断探针（`HtmlScriptBridge` click probe、`BridgeDiagnosisTemplate`）同步改 document 级，使 `installed` 反映真实 per-document 状态；
- `TabSwapScript` 同隐患 `window.__tabCtrlInit` → `document.__tabCtrlInit`。

---

## 5. 第三层根因：对象形态 postMessage 在传输层被静默丢弃（最终断点）

### 现象
第二层修好后，`[click] viewType-* → OK` 后**依然没有任何 `收到 postMessage` 带 `selectType`**；探针（正常）与点击（失效）行为不对称。

### 根因（对比两处 postMessage 形态）
| 来源 | postMessage 形态 | 结果 |
|---|---|---|
| 回流探针 | `JSON.stringify({...})`（**字符串**） | `TryGetWebMessageAsString()` 正常返回 → 到达 ✅ |
| 点击桥 | `postMessage({cmd,...})`（**对象**） | `TryGetWebMessageAsString()` 对对象返回 null → `string.IsNullOrWhiteSpace(json)` → 静默丢弃 ❌ |

`HtmlViewer.OnWebMessageReceived` 只调 `e.TryGetWebMessageAsString()`，**不处理对象形态消息** → 点击的 postMessage 在传输层入口即被丢弃，视图永不切换。

> 这是唯一直接命中"点击 → C#"这一环的根因，也是 8.26 方案"真实鼠标点击 data-cmd 无效"最底层的真相。

### 修复（`HtmlViewer.OnWebMessageReceived`）
`TryGetWebMessageAsString()` 为 null 时**兜底用 `e.WebMessageAsJson`**（对对象/字符串均返回 JSON 表示），统一处理对象形态消息。

### 验证（决定性）
```
收到 postMessage: {"cmd":"selectType","type":"node"} → 路由 → 重渲染 → 收敛写回 Converged
收到 postMessage: {"cmd":"selectTask","session":"vh_task_a",...} → 重渲染
[click] task-vh_task_a → OK（此前一直 NOT_FOUND）
[click] task-vh_task_b → OK
```
三视图切换、任务创建/归拢、任务内切换全部生效，`agent_html_view_switch_test` 整体跑通。

---

## 6. 三层断点定位总览

| 层 | 断点环节 | 表象 | 根因 | 修复 | 最终验证 |
|---|---|---|---|---|---|
| 1 | ① C#→JS 初始化 | `回流自检异常`：EnsureCoreWebView2Async 仅限 UI 线程 | 句柄未创建时后台线程不封送 → CoreWebView2 恒 null → 消息桥永不订阅 | 三路 UI 线程封送 + SynchronizationContext 回退 + 5s 超时 | 探针通过、回流探针=OK |
| 2 | ② 文档内点击桥 | bridge 在但点击无 postMessage | 幂等守卫挂 window（NavigateToString 保留 window 替换 document）→ 新 document 无监听器 | 守卫改 document 级 | click 产生 postMessage |
| 3 | ② JS→C# 传输 | 探针正常、点击无 `收到 postMessage` | 点击桥以对象 postMessage，`TryGetWebMessageAsString` 对对象返回 null → 传输层丢弃 | `OnWebMessageReceived` 兜底 `WebMessageAsJson` | selectType/selectTask 路由+重渲染，task-* → OK |

---

## 7. 方法论经验（可复用到其他链路排查）

1. **把"不可观测的跨进程链路"变成可断言的闭环自检**。
   回流探针（`LoopbackProbeAsync`，注入 `__loopback__` postMessage 并确认回 C#）的价值在于：把"点击是否到达 C#"这个原本靠猜的环节，变成可验证的探针。探针通过 ≠ 点击可用——它只证明"传输通道通"，不证明"点击桥通"。两者要分开验证。

2. **同一症状背后可能是多层断点，逐层建立独立判据**。
   `NOT_FOUND` 是终点症状，不代表根因在点击本身。要分别为每一环建立独立判据：
   - 初始化是否成功（探针/异常日志）
   - 桥是否注入到**当前 document**（`installed` 探针，且必须是 document 级）
   - 消息是否**实际到达 C#**（`收到 postMessage` 日志）
   - 路由/重渲染是否执行（`EventBridge_route` / `收敛写回`）

3. **静默 catch 是排查的天敌**。
   探针异常曾被 `catch{}` 吞掉，导致"无日志=未达 C#"的伪证。修复中把异常改为结构化重试诊断（`回流自检异常 1/3: ...`），一次运行即暴露根因。原则：**链路的每一段都要有可观测出口，禁止空 catch**。

4. **生命周期差异（window 持久 vs document 替换）是前端注入类 bug 的高发区**。
   `NavigateToString` 保留 window、替换 document。凡以 window 全局标志做"是否已注入"判断的，重渲染后都会产生"标志为真但监听器丢失"的假阳性。正确做法：**依赖 document 的元素（监听器）用 document 级标志**。

5. **传输协议形态是契约的一部分**。
   `postMessage` 传字符串 vs 传对象，接收侧读取方式不同（`TryGetWebMessageAsString` vs `WebMessageAsJson`）。同一通道混用两种形态会造成"探针通、业务断"的隐蔽不对称。**发送侧与接收侧必须对齐消息形态契约**，或接收侧做统一兜底。

6. **线程封送的单点纪律**。
   WebView2 的 `EnsureCoreWebView2Async`/`CoreWebView2` 访问严格限 UI 线程。封送判断不能只依赖 `InvokeRequired && IsHandleCreated`——句柄未创建（控件已建但未挂载）时后台线程也会绕过封送。需在控件构造时捕获 UI 线程的 `SynchronizationContext` 作兜底，并加超时防挂死。

7. **成功 ≠ 功能通过**。
   GUI 脚本 `success=true` 只代表命令无硬错误。断言类结果（如 `NOT_FOUND`）会被当普通返回值记录。排查时必须依赖"行为是否真正发生"（重渲染、路由日志），而非脚本返回值。

---

## 8. 涉及文件汇总

| 层 | 文件 | 改动 |
|---|---|---|
| 1 | `CsharpPrj/Plate/HtmlViewer/HtmlViewer.cs` | `RunOnUiThreadAsync` 三路封送 + SynchronizationContext 回退 + 5s 超时；探针自愈重试 |
| 1 | `Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryHtml.cs` | 探针重试 + 失败入就绪契约（不再「已装配」掩盖） |
| 1 | `Projects/Agent/src/Agent.Launcher/Modules/HtmlRuntimeReadinessReport.cs` | `TransportLoopbackOk` 语义注释更新 |
| 2 | `CsharpPrj/Plate/HtmlViewer/Html/Assets/HtmlAssetRegistry.cs` | BridgeScript / TabSwapScript 守卫改 document 级；诊断探针同步 |
| 2 | `CsharpPrj/Plate/HtmlViewer/HtmlViewer.cs` | `IsBridgeReadyCoreAsync` 查 document 级标志 |
| 2 | `CsharpPrj/Plate/HtmlViewer/Html/HtmlScriptBridge.cs` | click probe 改 document 级 |
| 3 | `CsharpPrj/Plate/HtmlViewer/HtmlViewer.cs` | `OnWebMessageReceived` 兜底 `WebMessageAsJson` |
| 观测 | `FileDriven/FileDriven.Execution/Diagnostics/execution_log_messages.json` | 注册 `HtmlChannel_message_bridge` 诊断 |

---

## 9. 根因定位源码速查

- 线程封送：`CsharpPrj/Plate/HtmlViewer/HtmlViewer.cs`（`RunOnUiThreadAsync` / `InvokeOnUiSyncContextAsync`）
- 消息桥订阅：`CsharpPrj/Plate/HtmlViewer/HtmlViewer.cs`（`EnsureMessageBridgeInstalled` / `OnWebMessageReceived` / `ProcessMessage`）
- 回流探针：`CsharpPrj/Plate/HtmlViewer/HtmlViewer.cs`（`LoopbackProbeCoreAsync`）+ `IHtmlChannel.LoopbackProbeAsync`
- 桥脚本/守卫：`CsharpPrj/Plate/HtmlViewer/Html/Assets/HtmlAssetRegistry.cs`（`BridgeScript`）
- 桥就绪查询：`CsharpPrj/Plate/HtmlViewer/Html/Script/HtmlScriptHost.cs`（`EnsureBridgeLoadedAsync`）
- 点击命令/探针：`CsharpPrj/Plate/HtmlViewer/Html/HtmlScriptBridge.cs`
- 事件桥路由：`CsharpPrj/Plate/HtmlViewer/Html/HtmlEventBridge.cs`（`HandleHostMessageAsync`）
- 装配与就绪契约：`Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryHtml.cs`（`HtmlAttachAsync`）
- 冒烟脚本：`FileDriven/DrivenConfig/Scripts/Agent/agent_html/agent_html_view_switch_test.json`
- 背景文档：`docs/analysis/2026/8/8.26/HTML传递与UI装配框架重构修复方案.md`、`Agent侧HTML解析逻辑通用框架化重构方案.md`
