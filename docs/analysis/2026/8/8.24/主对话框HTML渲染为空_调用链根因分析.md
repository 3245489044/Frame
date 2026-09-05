# 主对话框 HTML 渲染为空 —— 调用链根因分析

> **版本**：1.0　**日期**：2026-08-24
> **范围**：`agent_local_test_gui_test.json` 运行结果 `success=false`，主对话框 `conversationHistoryViewer` 渲染为空
> **性质**：调用链取证分析（仅分析，不随附实施）
> **关联**：`docs/analysis/2026/8/8.24/HtmlViewer_Html后端重复造轮子审查与收敛重构方案.md`（阶段 1/3 接线缺口）、`docs/analysis/2026/8/8.23/会话工作流三管理面重构方案.md`（M1 验收未达成）

---

## 一、结论

运行**失败（`success=false`）**，但失败点高度收敛：**本地联调开关、统一网关本地路由、会话工作流（collect→analyze→act）全部跑通，唯独④ 主对话框的会话历史（HTML 气泡）没有回显——`AgentUi_ConversationSend` / `AgentUi_ConversationListHtml` 返回给 `conversationHistoryViewer` 的是空字符串**。

根因（三层）：

1. **渲染层**：`ConversationViewService.RenderFlow → HtmlTemplateEngine.Render(templates, "flowShell", …)` 因模板集为空而输出空串；
2. **注入层**：`MainForm` 的 `HtmlUiAssembly.AttachTemplatesAndThemeFromConfigAsync(...)` **抛异常被 catch 吞掉**（日志 `MainForm_HtmlUiTheme_error` 仅 catch 分支触发），`AgentUiHost.ConversationTemplates` 槽位保持 `null` → 渲染回退空集；
3. **结构性缺口**：完整 `HtmlUiAssembly.Attach`（DOM 桥 `AttachHtmlUi` / 会话页声明 `AttachConversationPageDeclaration` / 模板 / 主题）在**生产宿主无调用点**（仅单测 `HtmlUiAssemblyTests` 调用）。

---

## 二、现象（运行日志证据）

### 正常段

```
[21:15:28.341] controlName=localTestOnButton, functionName=AgentUi_RunJson, success=true,
             outputPreview=测试模式=ON · 本地联调 | 生效后端=local-http | CallLog=0
[21:15:30.456] 本轮骨架完成: ok=true, decision=(无), steps=3        ← collect→analyze→act 跑通
[21:15:30.530] 会话历史审计: hasResult=true, nodes=3, msgs=15, roles=User,Tool,…,Assistant
[21:15:30.870] AgentUi_GetLocalTestStatus … 生效后端=local-http | CallLog=3 | 网关调用=3
[21:15:31.257] localTestOffButton … outputPreview=测试模式=OFF · 按配置 | 生效后端=semantic
```

### 异常段（渲染为空）

```
[21:15:25.618] [WARN] 日志消息定义未找到: msgId='MainForm_HtmlUiTheme_error'   ← 仅 MainForm catch 分支调用
[21:15:26.441] ECS 状态写入物理投影 — controlName=conversationHistoryViewer, value=, physicalAfter=   ← init 即空
[21:15:30.636] ECS 事件输出决策 — controlName=sendMessageButton, functionName=AgentUi_ConversationSend,
             success=true, outcome=Completed, outputTarget=conversationHistoryViewer, outputPreview=   ← 发送后仍空
[21:15:30.643] click 等待函数完成成功: target=sendMessageButton, output=                              ← 输出空
[21:15:31.956] GUI 模式脚本执行完成: success=false, totalCommands=38
```

> 关键：启动 init 命令 `exec AgentUi_ConversationListHtml output=conversationHistoryViewer` 与发送后 `AgentUi_ConversationSend` 输出**均为空**，且二者共用同一条渲染链 → 判定为渲染链路系统性出空，而非单次偶发。

---

## 三、调用链逐层分析

### 3.1 数据 / 工作流层（正常）

```
AgentUi_ConversationSend(sessionName, text)
  └─ AgentUiLibraryConversation.ConversationSendAsync      [AgentUiLibraryConversation.cs:36]
       └─ svc.SendAsync(name, text, ct) → 会话工作流 collect→analyze→act（steps=3, ok=true）
       └─ ConversationViewService.RenderViewAsync(messages: reply.Messages)   [:54]
```

证据：日志 `本轮骨架完成 ok=true steps=3`、`会话历史审计 hasResult=true msgs=15`、`生效后端=local-http | CallLog=3`。**数据与执行链路无问题**。

### 3.2 渲染层（空输出的直接点）

```
ConversationViewService.RenderViewAsync                              [ConversationViewService.cs:31]
  └─ RenderFlow(histories, currentHistory, sessionName, msgs)       [:84]
       ├─ treeHtml  = RenderHistoryTree(histories, …)               [:106]
       ├─ historyHtml = messageRenderer.Render(items)               [HtmlConversationRenderer.cs:48]
       └─ return HtmlTemplateEngine.Render(EffectiveTemplates,
               "flowShell", { tree, history })                        [ConversationPageRenderer.cs:92]
```

`HtmlTemplateEngine.Render` 对**缺失模板返回 fallback（默认空串）**：

```
[HtmlTemplateEngine.cs:33-35]
if (templates is null || !templates.TryGetValue(name, out var tpl) || string.IsNullOrWhiteSpace(tpl))
    return fallback;   // fallback = ""
```

而模板来源 `EffectiveTemplates = _templates ?? ConversationTemplates.Resolve()`：

```
[ConversationTemplates.cs:17-18]
public static IReadOnlyDictionary<string, string> Resolve()
    => AgentUiHost.ConversationTemplates ?? Empty;      // 槽位 null → 空集
```

结论：**只要 `flowShell/round/message/…` 模板不在生效集里，`RenderFlow` 就整体输出空串**。即便历史为空，只要 `flowShell` 模板存在也会输出布局壳 `<div class="conv-layout">…`（非空）。实测输出**完全为空** → 模板集确实为空。

### 3.3 注入层（失败被吞）

模板唯一注入入口：`AgentUiHost.AttachConversationTemplates(templates)`。生产由 `MainForm` 触发：

```
[MainForm.cs:134-145]
try {
    var path = PathConstants.ResolveConfigFilePath("AgentRuntimeUI.json");
    await HtmlUiAssembly.AttachTemplatesAndThemeFromConfigAsync(path);   // 注入模板+主题
} catch (Exception ex) {
    ExecutionLog.Log("MainForm_HtmlUiTheme_error", new { errorMessage = ex.Message });  // ← 吞异常，仅记日志
}
```

```
[HtmlUiAssembly.cs:64-73]
AttachTemplatesAndThemeFromConfigAsync(path):
    cfg = HtmlDeclarationParser.Parse(readAllText(path));
    if (cfg?.Templates is not { Count: > 0 } templates) return false;
    AttachTemplatesAndTheme(templates);     // → AgentUiHost.AttachConversationTemplates + HtmlTheme.Attach
```

证据链：

- 日志 `[21:15:25.618] msgId='MainForm_HtmlUiTheme_error'` 的 msgId 在**全代码库仅出现在该 catch 分支**（`git grep` 证实）→ **catch 必已执行**，即 `AttachTemplatesAndThemeFromConfigAsync` 或路径解析抛了异常；
- 异常被吞（只写日志、未透传/未回退），`AgentUiHost.ConversationTemplates` 槽位维持 `null`；
- 随后 `[21:15:26.441]` 主对话框首次渲染即为空 → 与「模板未注入」严格一致。

### 3.4 结构性缺口：完整 `HtmlUiAssembly.Attach` 无生产调用点

```
HtmlUiAssembly.Attach(bridge, uiConfig, registry)   // 完整装配：AttachHtmlUi + AttachConversationPageDeclaration + AttachConversationTemplates + HtmlTheme.Attach
```

`git grep -n "HtmlUiAssembly.Attach" -- *.cs`：

| 调用点 | 类型 |
|---|---|
| `CentralTest/…/HtmlUiAssemblyTests.cs:93/172` | 单测 |
| `MainForm.cs:140` | 仅调用 `AttachTemplatesAndThemeFromConfigAsync`（模板+主题子集） |

即：**DOM 桥运行时（`AttachHtmlUi`）、会话页内部声明注入（`AttachConversationPageDeclaration`）在生产宿主从未接线**。因此即便修复 3.3，让气泡渲染出来，HTML 消息桥交互（`data-cmd=selectTask` 选会话 / `selectHistory` 滚动 / `AgentUi_ConversationMessageDispatch`）在生产仍不可用。

---

## 四、根因汇总

| 层 | 直接原因 | 代码证据 |
|---|---|---|
| 渲染层 | 模板集为空 → `RenderFlow` 输出空 | `ConversationTemplates.cs:17-18`、`HtmlTemplateEngine.cs:33-35`、`ConversationPageRenderer.cs:92` |
| 注入层 | `AttachTemplatesAndThemeFromConfigAsync` 抛异常被 catch 吞掉，槽位未填充 | `MainForm.cs:137-145`、`HtmlUiAssembly.cs:64-73`、日志 `MainForm_HtmlUiTheme_error` |
| 结构性 | 完整 `HtmlUiAssembly.Attach` 生产无调用点 | `git grep`：仅单测 `HtmlUiAssemblyTests.cs` 调用 |
| 数据/工作流 | 正常（本地开关/网关/工作流均通过） | 日志 steps=3、msgs=15、CallLog=3 |

---

## 五、修复建议（对齐 8.24 方案阶段 1/3）

1. **先让 catch 可观测**：`MainForm_HtmlUiTheme_error` 在 `ExecutionLog.Log` 里补日志消息定义（`execution_log_messages.json`），并把 `errorMessage`/`StackTrace` 完整落盘，以精确定位抛错行（当前日志只显示了 msgId 警告，未显示异常正文）。
2. **修正注入失败**：定位 `AttachTemplatesAndThemeFromConfigAsync` 抛错点（路径解析 or `HtmlDeclarationParser.Parse` 反序列化），保证 `AgentUiHost.AttachConversationTemplates` 成功；注入成功后主对话框气泡即应渲染。
3. **补完整接线**：按 8.24 方案阶段 3，在组合根 `AgentFrameworkHosting.TryBootstrapGuiAsync` 补调 `HtmlUiAssembly.Attach(...)`（含 `AttachHtmlUi` DOM 桥 / `AttachConversationPageDeclaration` / 模板 / 主题），使 HTML 消息桥交互在生产可用，消除「生产无调用点」缺口。
4. **回归验收**（对齐 8.23 M1）：重跑 `agent_local_test_gui_test.json`，`conversationHistoryViewer` 出现 user/assistant/sub-agent 气泡、终答非空；`AgentUi_GetLocalTestStatus` CallLog≥1；脚本 `success=true`。

---

## 六、涉及文件清单

| 文件 | 角色 |
|---|---|
| `FileDriven/DrivenConfig/Scripts/Agent/agent_local_test_gui_test.json` | 被测脚本（命令流正确，仅文案已同步 HTML 渲染） |
| `FileDriven/DrivenConfig/JsonConfig/AgentRuntimeUI.json` | 声明与模板（`templates.flowShell/round/message/…` 齐全） |
| `FileDriven/FileDriven.Execution/Initial/MainForm.cs` | 生产注入点（catch 吞异常） |
| `Projects/Agent/src/Agent.Launcher/Modules/HtmlUiAssembly.cs` | 装配辅助（完整 Attach 生产未调用） |
| `Projects/Agent/src/Agent.Launcher/Modules/ConversationTemplates.cs` | 模板槽位解析 |
| `Projects/Agent/src/Agent.Launcher/Modules/ConversationViewService.cs` | 会话视图编排 |
| `Projects/Agent/src/Agent.Launcher/Modules/ConversationPageRenderer.cs` / `HtmlConversationRenderer.cs` | 渲染器 |
| `CsharpPrj/Plate/HtmlViewer/Html/HtmlTemplateEngine.cs` | 模板引擎（缺失→空） |