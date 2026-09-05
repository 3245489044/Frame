# HTML 传递与 UI 装配框架重构修复方案（就绪契约 · 观测 · 域隔离 · 单协议）

> **版本**：1.0
> **日期**：2026-08-26
> **范围**：跨 `CsharpPrj/Plate/HtmlViewer`（传递通道）、`Projects/Agent/src/Agent.Launcher`（装配/渲染）、`FileDriven`（宿主/配置）
> **性质**：框架级重构修复方案文档（从缺陷根因出发，落地为分阶段实施）
> **触发**：真实鼠标点击 `data-cmd`（任务/历史/视图切换）无效，而脚本合成点击显示成功、`<details>` 原生展开有效——暴露框架级缺陷

---

## 一、问题现象

| 交互 | 结果 | 是否依赖 JS 点击桥/消息桥 |
|---|---|---|
| 脚本合成点击 `[click] viewType-*` | 日志显示“成功” | 是（`__domBridge.handle` → dispatchEvent） |
| 真实鼠标点击 `data-cmd`（selectType/selectTask/selectHistory） | **无效** | 是（document 点击桥 → postMessage） |
| `<details>`/`<summary>` 展开内部节点 | 有效 | **否**（浏览器原生，不依赖桥） |

> “展开有效”恰是揭示机制的关键：它不依赖任何 JS 桥。而切换类交互依赖整条无人观测的桥链，任意一环断裂都表现为同一个“点击无效”。

---

## 二、根因归纳（框架级）

### R1 装配无“就绪契约”——`装配成功 ≠ 能力就绪`
- `AgentUi_HtmlAttach` 返回“桥就绪=True”，但该布尔值只反映 `HtmlUiRuntime` 创建成功，**不反映** `AttachHostMessage` 是否真的绑定了 `hostWriter` 与 `MessageReceived`。
- `AttachHostMessage` 内守卫 `if (!索引命中 || !HasMessageEvent) return;` **静默返回、不日志、不抛错**。
- 宿主/用户看到“桥就绪”，实际消息桥可能从未订阅。

### R2 链路无观测——每段静默吞异常
- `NavigationCompleted` 补注入 `catch(Exception){}`；
- 消息回调 `catch{}`（“不向页面回抛”）；
- 值契约 `_ = hv.SetHtmlAsync(text)`（fire-and-forget）；
- 事件桥/会话操作失败降级为字符串返回。
- 错误全部被吞或转文本，**无结构化诊断出口**，导致“看似成功实则失败”。

### R3 状态依赖全局静态 + 时序假设
- `AgentUiHost` 为静态类，十余个静态槽位（HtmlUi / ConversationTemplates / ConversationPageDeclaration / HtmlViewerResolver / ConversationHtmlSink…），依赖“宿主记得调每个 AttachXxx”。
- **点击桥注入依赖 `NavigationCompleted` 时序**：真实鼠标点击不经过“每次 DOM 指令前补注入”路径，注入失败即被空 catch 吞掉、永不重试 → 点击桥永久缺失。
- 脚本点击每次先 `EnsureBridgeInstalledAsync()` 再 dispatch，天然自愈 → 与真实点击行为不一致。

### R4 声明模型与运行实例同名冲突
- `AgentRuntimeUI.json` 出现**两个同名 `conversationHistoryViewer`**：外层物理控件（承载 htmlviewer + `events.message`）与 `htmlPage` 内部嵌套的同名 htmlviewer。
- `BuildControlIndex` 把同名控件都收进索引，事件绑定、渲染写回、脚本 target 指哪个 **语义含混**，output 写回目标不唯一。

### R5 交互协议双轨并存 / 桥抽象重复
- `data-cmd`（消息桥，postMessage）与 `data-event`（声明式事件桥）**两套交互语法、两套解析分发**。
- 历史上桥抽象层叠加 `__domBridge / IDomBridge / HtmlElementNode / HtmlEventBridge / HtmlScriptBridge` 多层（部分已删），职责重叠。

### 根因一句话
> 能力通过“宿主人肉装配 + 全局静态槽位 + 静默守卫”建立，运行时是否真正就绪缺乏任何可断言、可观测的验证；跨线程交互链路被拆成多段各自封送、各自吞异常的黑盒。

---

## 三、重构目标（四个支柱 + 一收敛）

| 支柱 | 目标 | 对应根因 |
|---|---|---|
| **P1 就绪契约（Readiness Contract）** | 装配返回结构化就绪报告（哪些绑定成功/失败）；守卫不再静默 | R1 |
| **P2 观测埋点（Observability）** | 链路每段有结构化诊断；静默 catch 收敛为可观测错误；错误有统一出口 | R2 |
| **P3 域隔离（Domain Isolation）** | 外层 host 控件与内层 htmlPage 元素按域命名，消除同名歧义 | R4 |
| **P4 注入/线程就绪机制（Injection & Marshal）** | 点击桥注入从“时序依赖+静默失败”改为“运行时自愈+就绪断言”；脚本/鼠标统一路径；线程封送单点 | R3 |
| **P5 单协议收敛（Single Protocol）** | `data-cmd` 为唯一交互协议，`data-event` 由桥统一翻译 | R5 |

---

## 四、总体架构设计

```
就绪契约层：IHtmlChannel.Readiness() / HtmlUiAssembly.Attach → HtmlUiReadinessReport
观测层：   统一诊断出口（msgId 定义）+ 消息桥链分段埋点 + 装配校验器接线检查
域隔离层：  host:<name>（物理控件） vs page:<name>（htmlPage 内元素），BuildControlIndex 按域索引
注入层：   点击桥“渲染完成后同步确认注入成功”，失败可重试可观测；脚本/鼠标共用就绪断言
协议层：   data-cmd 单一消息协议；data-event 由事件桥翻译为 data-cmd
```

---

## 五、分阶段落地

### 阶段 0：故障修复 + 注入可观测化（先让“鼠标点击无效”可诊断、可自愈）
- **注入失败可观测**：`EnsureBridgeInstalledAsync` 不再空 catch，失败输出结构化日志（msgId + 原因）。
- **注入自愈**：`SetHtmlAsync` 渲染完成后同步 `await` 确认点击桥注入成功；注入失败重试而非静默。
- **就绪断言**：`IHtmlChannel` 新增就绪查询（当前 document 是否已注入 `__domBridge` 与点击桥）。
- **验收**：真实点击 `data-cmd` 恢复有效；注入失败时日志可见并可重试；`agent_html_view_switch_test` 全绿。

### 阶段 1：就绪契约（P1）
- `HtmlUiAssembly.Attach` 返回 `HtmlUiReadinessReport`（含 hostWriter 是否注册、MessageReceived 是否订阅、函数是否在注册表、output 目标是否唯一）。
- `AttachHostMessage` 守卫改为返回失败原因（不再静默 return）。
- `AgentUi_HtmlAttach` 输出报告摘要，不再用“桥就绪=True”掩盖。
- **验收**：装配后可断言全部绑定；缺任一绑定即有明确错误文本与日志。

### 阶段 2：观测埋点（P2）
- 在 `fde_log_messages.json` / `execution_log_messages.json` 注册消息桥链各段 msgId：`HtmlChannel_inject_bridge`、`HtmlChannel_webmessage`、`EventBridge_route`、`EventBridge_invoke`、`EventBridge_writeback`。
- 收敛静默 `catch`：至少记录错误分类 + 上下文，不再空 catch。
- 消息桥链（postMessage → 路由 → invoker → 写回）每段有诊断。
- **验收**：一次“点击无效”可在日志中精确定位断点（订阅缺失/解析失败/函数未注册/写回目标错）。

### 阶段 3：域隔离（P4 一部分，针对同名冲突）
- 约定命名域：外层宿主控件 `host:conversationHistoryViewer`；htmlPage 内元素 `page:conversationHistoryViewer`（或改名）。
- `BuildControlIndex` 按域索引，`output`/脚本 target 显式指定域。
- 消除同名双份，写回目标唯一。
- **验收**：`conversationHistoryViewer` 在整个声明里唯一；消息桥写回目标确定。

### 阶段 4：单协议收敛（P5）
- 确立 `data-cmd`（json-script）为唯一交互协议；`data-event` 由 `HtmlEventBridge` 翻译为同一协议。
- 移除双套解析/分发路径。
- **验收**：data-event 用例与 data-cmd 用例在同一桥下均通过，无行为漂移。

### 阶段 5：装配去静态化与线程封送单点（P3/P4 收尾）
- `AgentUiHost` 静态槽位收敛为可查询的装配清单（或构造注入的组合根快照），消除“人肉接线/能力孤岛”。
- 线程封送统一单点（HtmlViewer 唯一封送点），规则文档化。
- **验收**：`MainProj.sln`/`FileDriven.sln` 全绿；`Plate.Tests/HtmlViewer`、`Agent.Core.Tests` 全绿；GUI 冒烟。

---

## 六、涉及文件清单

| 文件 | 动作 | 阶段 |
|---|---|---|
| `CsharpPrj/Plate/HtmlViewer/HtmlViewer.cs` | 注入自愈 + 就绪查询 + 观测埋点 | 0/2 |
| `CsharpPrj/Plate/HtmlViewer/Abstractions/IHtmlChannel.cs` | 新增就绪查询能力 | 0 |
| `CsharpPrj/Plate/HtmlViewer/Html/HtmlEventBridge.cs` | data-event 翻译单协议；链路埋点 | 2/4 |
| `CsharpPrj/Plate/HtmlViewer/Html/HtmlDeclarationParser.cs` | 按域索引（BuildControlIndex） | 3 |
| `Projects/Agent/src/Agent.Launcher/Modules/HtmlUiRuntime.cs` | 就绪报告；守卫返回原因 | 1 |
| `Projects/Agent/src/Agent.Launcher/Modules/HtmlUiAssembly.cs` | Attach 返回就绪报告 | 1 |
| `Projects/Agent/src/Agent.Launcher/Modules/AgentUiHost.cs` | 静态槽位收敛为装配清单 | 5 |
| `Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryHtml.cs` | Attach 输出就绪摘要 | 1 |
| `FileDriven/DrivenConfig/JsonConfig/AgentRuntimeUI.json` | 同名控件域化 | 3 |
| `CsharpPrj/Operation/FrontEndDriven` 日志定义 json | 注册消息桥链 msgId | 2 |
| `FileDriven` ConfigValidator | 接线校验（函数在注册表 / output 唯一） | 2 |
| 测试 | 就绪报告断言 / 注入自愈 / 单协议 | 各阶段 |

---

## 七、验收标准

1. **就绪可断言**：装配后能查询每个绑定是否成功，缺任一绑定即有明确错误。
2. **故障可定位**：一次“点击无效”可在日志中定位到断点，无静默吞异常。
3. **真实点击可用**：鼠标点击 `data-cmd`（任务/历史/视图切换）有效，与脚本一致。
4. **声明无歧义**：`conversationHistoryViewer` 全局唯一（或显式域化）。
5. **单协议**：data-event 与 data-cmd 收敛为一条交互协议。
6. **回归**：`MainProj.sln` / `FileDriven.sln` 全绿；`Plate.Tests/HtmlViewer`、`Agent.Core.Tests` 全绿；GUI `agent_html_view_switch_test` 冒烟通过。

---

## 八、风险与取舍

| 决策 | 取舍 |
|---|---|
| 就绪报告改变 Attach 返回类型 | 影响 `AgentUiLibraryHtml.HtmlAttachAsync` 与测试；但消除“桥就绪=True”误导，值得 |
| 域化命名改 JSON 声明 | 需迁移 `AgentRuntimeUI.json` 与 `BuildControlIndex`；向后兼容可先加域前缀映射 |
| 注入自愈引入等待 | `SetHtmlAsync` 后 await 注入确认，可能增加毫秒级延迟；换取真实点击确定性 |
| 单协议收敛 | 触碰既有 data-event 用例；按 B 级既有方案先收敛后去除，避免行为漂移 |

---

## 九、建议执行顺序

先 **阶段 0**（让故障可观测、可自愈，直接解决“鼠标点击无效”），再按 1→5 推进框架固化。阶段 0 与 1-5 独立可交付，每阶段均带验收。