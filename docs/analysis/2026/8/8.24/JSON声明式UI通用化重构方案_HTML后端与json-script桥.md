# JSON 声明式 UI 通用化重构方案 —— HTML 后端 + json-script 原生操作桥

> **版本**：1.0
> **日期**：2026-08-24
> **范围**：P0–P4 全量（跨 `CsharpPrj/Operation/FrontEndDriven` JSON UI 引擎 与 `Projects/Agent` 会话渲染器两层）
> **性质**：重构方案文档（本文件仅为设计，不随附实施）
> **目标**：把「JSON 自动排版 + 控件树 + 事件绑定 + json-script」这套声明式 UI 引擎**泛化为多后端**——在既有 WinForms 后端之外补一个 **HTML 后端**，让同一份 JSON 声明可渲染为 WinForms 或 HTML；并把 `04-json-script.md` 的脚本命令协议（`exec/set/click/get`）落到 **HTML 原生元素**上。

---

## 一、背景与问题

### 1.1 现状：两套并存的「排版 + 操作」系统

| 维度 | JSON 自动排版（WinForms 后端） | 当前会话页渲染器（手写 HTML） |
|---|---|---|
| 声明 | `layout:{left,top,width,height}` + 控件树 + `events` | 硬编码 `<div class="conv-layout">` + CSS flex |
| 布局数学 | `LayoutPositionConverter`（纯函数）→ `LayoutRect` | 手写 flex/grid CSS |
| 控件实例化 | `ControlTypeRegistry` type→factory | 手写 `<aside class="session-bar">` 等 |
| 值读写 | `ControlValueContract`（`ValueSemantic`） | 字符串拼接文本 |
| 事件 | `events`+`bindings` → `EventSubscriptionHelper` | `[data-cmd]` 消息桥 |
| 脚本 | `exec/set/click/get`（`WinFormsExecutor`） | 无 |

> 触发点：`HtmlConversationRenderer` / `ConversationPageRenderer` 为展示会话历史而**手写 HTML/CSS 排版**，与 JSON 自动排版（`layout` 比例坐标）是两套互不相通的世界；`04-json-script.md` 的脚本只能操作 WinForms 控件，无法操作 HTML 原生元素。

### 1.2 痛点
1. **排版分叉**：会话页布局靠手写 CSS（flex/`conv-layout`），无法复用 JSON `layout` 声明，改布局要改 C#。
2. **操作分叉**：json-script（`exec/set/click/get`）只能驱动 WinForms 控件；HTML 内元素只能靠 `[data-cmd]` 单点桥，没有通用命令协议。
3. **重复实现**：同一份「控件 + 布局 + 事件」语义，在 WinForms 与 HTML 各自表达一次。

### 1.3 目标
- **复用同一份 JSON 声明**（`views[].controls[]` + `layout` + `events` + `initCommands`）渲染成 HTML。
- **json-script 协议落到 HTML 原生元素**：`exec/set/click/get` 命令的 `target` 可解析为 HTML 元素（id / `[data-name]`）。
- **会话页迁移到声明式**：手写渲染器退化为「数据提供者」，布局与交互交给声明系统。

---

## 二、关键洞察：目标无关层已存在，缺的是 HTML 后端

调研确认三处已是目标无关资产，可直接复用（非重新发明）：

1. **`IControlAdapter`**（`CsharpPrj/Operation/FrontEndDriven/Abstractions/IControlAdapter.cs`）
   注释原文：*"UI 无关抽象——后端（WinForms/WPF/无头测试）必须提供的能力契约"*。签名仅依赖 `System.Drawing` 与 `EventHandler`，无 `System.Windows.Forms` 类型。覆盖：`SetBounds/GetLeft/GetTop/GetWidth/GetHeight/GetParentClientSize/AddControl/Contains/RemoveControl/SetText/SetEnabled/SetVisible/SetFont/SetForeColor/SetBorderStyle/GetSplitter*`。**这是为多后端设计的端口。**

2. **`LayoutPositionConverter`**（`CsharpPrj/BasicMethod/Framework/LayoutPositionConverter.cs`）
   `Convert(LayoutPosition, containerW, containerH) → LayoutRect{X,Y,Width,Height}`，支持 比例/像素/right-bottom/alignment，**纯数学、零 WinForms 依赖**。JSON `layout:{left,top,width,height}` → 像素 rect 的换算可原样复用于 HTML。

3. **声明层与值/事件语义**：
   - `UIConfiguration` / `ControlConfig`（控件树 + `layout` + `events` + 子 `controls`）——目标无关。
   - `EventBinding` 声明（`eventName → functionName` + `bindings.parameters/output`）——目标无关。
   - `ControlValueContract`（`ValueSemantic: Text/Selection/Content/Checked/Range/Path` + `ValueAccessor{read,write}`）——语义化值契约。

→ 结论：通用框架不是"重写排版"，而是**补一个 HTML 后端适配器**，复用同一份声明与同一套内核抽象。

---

## 三、总体架构

```
声明层（目标无关，已有）
  UIConfiguration / ControlConfig（控件树 + layout + events）
  ScriptDefinition（04-json-script.md commands）
        │
        ▼
内核层（目标无关，复用 + 新增抽象）
  LayoutPositionConverter ──── 复用（布局数学：LayoutSpec → LayoutRect）
  ControlValueContract ─────── 复用（值语义：Text/Selection/...）
  EventBinding 解析 ────────── 复用（eventName→function + bindings）
  IUiElementNode ───────────── 新增（统一节点访问器，两诉求的连接点）
        │
        ├─► WinForms 后端（已有，不动）
        │     ControlTypeRegistry → Control
        │     LayoutProjector → SetBounds
        │     WinFormsControlAdapter（实现 IControlAdapter）
        │     EventSubscriptionHelper → 物理事件
        │     WinFormsExecutor → exec/set/click/get
        │
        └─► HTML 后端（新增）
              HtmlControlFactory    type → DOM 元素模板
              HtmlLayoutProjector   LayoutRect → CSS（绝对 px 或 %）
              HtmlElementAdapter    实现 IControlAdapter（经 WebView2）
              HtmlValueContract     DOM value / textContent 读写
              HtmlEventBridge       data-event → 消息桥 → C# 函数
              HtmlScriptBridge      exec/set/click/get 操作 DOM
```

### 3.1 分层职责
- **声明层**：不感知后端，只描述"是什么"（控件、布局、事件、脚本）。
- **内核层**：把声明归一为"布局 rect + 值语义 + 事件绑定 + 命令动作"，对后端不感知。
- **后端层**：把内核输出物化为具体 UI 工具包的物理元素（WinForms `Control` / HTML DOM），并反向把用户交互（点击/取值/命令）接回内核。

### 3.2 复用与新增边界
| 资产 | 归属 | 动作 |
|---|---|---|
| `LayoutPositionConverter` / `LayoutPositionHelper` | 内核 | **复用**（纯函数） |
| `IControlAdapter` | 内核端口 | **复用**（HTML 后端实现它） |
| `ControlValueContract`（ValueSemantic） | 内核语义 | **复用语义**，HTML 后端实现 DOM 访问器 |
| `EventBinding` 声明 | 内核声明 | **复用** |
| `ScriptDefinition`（commands） | 内核声明 | **复用** |
---

## 四、核心抽象：`IUiElementNode`（统一节点访问器）

### 4.1 接口定义
```csharp
namespace UiBackend.Abstractions;   // 新共享层（避免依赖 FrontEndDriven / WinForms）

/// <summary>UI 元素节点统一访问器——把「布局投影 + 值读写 + 事件触发 + 子节点」归一，供
/// 内核（布局投影器 / 脚本命令执行器 / 事件分派）消费，与具体后端（WinForms/HTML/无头）解耦。</summary>
public interface IUiElementNode
{
    string Name { get; }                              // 控件名（脚本 target、参数 source 用）
    string TypeName { get; }                          // 声明类型（button/textbox/panel/...）

    // 值语义（get / set / 参数 source=control / 输出写回）
    object? GetValue();
    void SetValue(object? value);

    // 事件（click / 脚本命令触发）
    void Invoke(string eventName, object? payload);

    // 布局投影（LayoutRect 像素 → 物理定位）
    void SetBounds(int x, int y, int width, int height);
    (int X, int Y, int Width, int Height) GetBounds();

    // 树
    IEnumerable<IUiElementNode> Children { get; }
}
```

### 4.2 WinForms 实现
`WinFormsElementNode` 包装 `Control`：
- `GetValue/SetValue` → 委托 `ControlValueContract`（按 `ValueSemantic`）读写 `Control`。
- `Invoke("click")` → `control.PerformClick()`（Button）/ 触发物理事件。
- `SetBounds` → `control.SetBounds(x,y,w,h)`（或经 `IControlAdapter`）。

### 4.3 HTML 实现
`HtmlElementNode` 两种形态：
- **C# 侧持有 DOM 引用**：经 `CoreWebView2.ExecuteScriptAsync` 对元素执行受控 JS（`el.value=...` / `el.textContent=...` / `el.dispatchEvent(...)`）。
- **协议形态**：仅持有 `id`，把 `{op, id, value, event}` 通过消息桥发给 WebView2 内渲染器执行。

> 推荐先用**协议形态**（后端在 WebView2 内，C# 只发指令），与既有 `[data-cmd]` 消息桥对称，线程 marshal 已有基础。

---

## 五、阶段设计

### P0 — 抽象与骨架
**产出**：`IUiElementNode` 接口 + 共享层工程 + 后端接入点。
- 新建共享库 `CsharpPrj/Share/UiBackend/`（Abstractions）。
- 定义 `IUiElementNode`（§4.1）与 `IValueAdapter`（读 ValueSemantic 的 DOM/Control 差异封装）。
- 确认 `LayoutPositionConverter` / `IControlAdapter` 复用面；WinForms 侧做一个最小 `WinFormsElementNode` 包装验证。

### P1 — HTML 控件工厂 + HTML 布局投影（声明 → HTML/CSS）
**产出**：`HtmlControlFactory` + `HtmlLayoutProjector`，输入 `UIConfiguration` 输出 body HTML/CSS。
- **HtmlControlFactory**：`type → DOM 模板` 映射（语义映射表）：
  | JSON type | HTML 元素 | 值语义 |
  |---|---|---|
  | `label` | `<span>` | textContent |
  | `textbox` | `<textarea>` / `<input>` | `.value` |
  | `button` | `<button data-event="click">` | textContent |
  | `groupbox`/`panel` | `<div class="container">` | 子容器 |
  | `listview` | `<ul class="list">` | 列表项 |
  | `combobox` | `<select>` | `.value` |
  | `tabcontrol` | tabbar + panes（CSS/JS 切换） | 选中页 |
  | `htmlviewer` | 内嵌 `<div>` 宿主 | 富文本 |
  - 所有元素带 `data-name="{name}"`（脚本 target 用）。
- **HtmlLayoutProjector**：递归 `views[].controls[]`，对每个节点用 `LayoutPositionConverter.Convert(layout, parentW, parentH)` 算像素 rect → 生成 CSS：
  ```
  position: absolute; left:{x}px; top:{y}px; width:{w}px; height:{h}px;
  ```
  容器相对父定位（父元素 `position: relative`），嵌套容器坐标相对父。
- 渲染模式：C# 纯函数把声明编译为 HTML 字符串 → 复用 `htmlviewer.SetHtmlAsync` 输出通道（可单测）。
- **单测**：给定一个含嵌套 panel + label + button + listview 的声明，断言生成的 HTML 结构与 CSS 定位换算正确（对齐 `LayoutPositionConverter` 输出）。

### P2 — HTML 值契约 + HTML 事件桥（events/bindings 在 HTML 生效）
**产出**：`HtmlValueContract` + `HtmlEventBridge`。
- **HtmlValueContract**：实现 `ControlValueContract` 的 DOM 访问器（`textbox→.value`、`label→.textContent`、`combobox→.value`…），供参数 `source=control` 读值与输出写回用。
- **HtmlEventBridge**：渲染端把声明 `events` 编译为 `data-event="{eventName}" data-func="{func}" data-bindings=...`；复用已就绪的 `[data-cmd]` 消息桥，把 HTML 点击 postMessage 到宿主 → 解析绑定 → 调 C# 函数 → 结果写回 `output` 元素。
  - 与既有 `conversationHistoryViewer` 的 `events.message` 接线同构，可扩展为通用 `data-event`。
### P3 — HTML 脚本桥（json-script 落到 HTML 原生操作）
**产出**：`HtmlScriptBridge`，复用 `04-json-script.md` commands 协议。
- 命令 `target` 解析为 HTML 元素（`[data-name="id"]` / id）：
  | 命令 | HTML 原生操作 |
  |---|---|
  | `exec` | 调 C# 函数，`output` 写回 DOM 元素 |
  | `set` | `el.value` / `el.textContent = value` |
  | `get` | 读回 `el.value` / `textContent` 并返回 |
  | `click` | `el.dispatchEvent(new MouseEvent('click'))` → 触发事件桥 → C# 函数 |
- 两种执行形态：
  1. **宿主侧**：新增 `AgentUi_HtmlScript(scriptJson)` 函数，在 C# 侧解析 commands，逐条经 `HtmlScriptBridge` 发 WebView2 指令执行（推荐，可复用 `ScriptCommandExecutor` 的 `IUiElementNode` 后端切换）。
  2. **渲染器内**：WebView2 内 JS 解析 commands 执行（后续增强）。
- 安全：只执行白名单化 DOM 操作，不跑任意 JS；线程 marshal 复用既有桥。

### P4 — 会话页迁移到声明式
**产出**：会话页布局由 JSON 声明描述，手写渲染器退化为数据提供者。
- 会话页声明（示意）：
  ```json
  {
    "name": "conversationGroup",
    "controls": [
      { "type": "panel", "name": "sidebar", "layout": { "left": 0.01, "top": 0.04, "width": 0.22, "height": 0.92 },
        "controls": [ { "type": "htmlviewer", "name": "taskList", "layout": {...} } ] },
      { "type": "htmlviewer", "name": "conversationHistoryViewer", "layout": { "left": 0.25, "top": 0.04, "width": 0.73, "height": 0.92 }, "events": { "message": "AgentUi_ConversationMessageDispatch" } },
      { "type": "textbox", "name": "conversationInputTextBox", "layout": {...} },
      { "type": "button", "name": "sendMessageButton", "events": { "click": "AgentUi_ConversationSend" } }
    ]
  }
  ```
- `ConversationPageRenderer` / `HtmlConversationRenderer` 退化为「**数据提供者**」：只产出会话/历史数据（session 列表、分层消息渲染项），布局与容器由 HTML 后端按声明渲染；手写 CSS 退化为「组件主题」（`.task-item`、气泡、分层折叠等样式保留为主题，不再承担布局）。
- 布局改动只改 JSON，不再改 C#。

---

## 六、涉及文件清单

| 文件 | 动作 | 阶段 |
|---|---|---|
| `CsharpPrj/Share/UiBackend/Abstractions/IUiElementNode.cs` | 新增 | P0 |
| `CsharpPrj/Share/UiBackend/Abstractions/IValueAdapter.cs` | 新增 | P0 |
| `CsharpPrj/Share/UiBackend/Html/HtmlControlFactory.cs` | 新增 | P1 |
| `CsharpPrj/Share/UiBackend/Html/HtmlLayoutProjector.cs` | 新增 | P1 |
| `CsharpPrj/Share/UiBackend/Html/HtmlElementNode.cs` | 新增 | P2 |
| `CsharpPrj/Share/UiBackend/Html/HtmlEventBridge.cs` | 新增 | P2 |
| `CsharpPrj/Share/UiBackend/Html/HtmlScriptBridge.cs` | 新增 | P3 |
| `CsharpPrj/Operation/FrontEndDriven/Abstractions/IControlAdapter.cs` | 复用（HTML 后端实现） | P1 |
| `CsharpPrj/BasicMethod/Framework/LayoutPositionConverter.cs` | 复用 | P1 |
| `CsharpPrj/Operation/FrontEndDriven/Shared/EventSubscriptionHelper.cs` | 不变（HTML 走消息桥） | — |
| `Projects/Agent/.../Modules/ConversationPageRenderer.cs` | 改：退化为数据提供者 | P4 |
| `Projects/Agent/.../Modules/HtmlConversationRenderer.cs` | 改：退化为数据提供者 + 主题 | P4 |
| `FileDriven/DrivenConfig/JsonConfig/AgentRuntimeUI.json` | 改：会话页声明化 | P4 |
| 测试 | 新增 `UiBackend.Tests` | P1-P3 |

---

## 七、安全边界

- **可信通道不变**：HTML 渲染输出仍经 `HtmlDocumentRenderer`（生产端渲染器 encode 后才入 DOM），用户/模型文本一律 `HtmlEncode`。
- **脚本桥白名单**：`HtmlScriptBridge` 只执行受控 DOM 操作（`set/get/click`），不执行任意 JS；命令解析在 C# 侧，命令值按数据处理。
- **线程 marshal**：WebView2 消息回调非 UI 线程，须 marshal 回 UI 线程再碰 C# 控件/状态（复用既有 `WinFormsThreadDispatcher` / HtmlViewer 桥）。
- **双真源防护**：HTML 后端只新增能力，WinForms 后端不动，避免回归；两后端共享同一份声明与内核抽象（SingleImplementation，不复制布局/值/事件语义）。

---

## 八、风险与取舍

| 风险 | 应对 |
|---|---|
| HTML 控件 ≠ WinForms 控件 | 语义映射表 + 组件主题（样式层）显式声明，缺省回退（如 combobox→select、listview→ul） |
| 双后端并存维护成本 | HTML 作为增量，WinForms 不动；共享内核抽象避免逻辑复制 |
| 布局语义差异（绝对定位 vs 响应式） | 默认绝对定位 + 百分比，与 JSON `layout` 语义一致；容器相对父定位对齐嵌套语义 |
| 脚本桥执行任意 JS 风险 | 白名单化 DOM 操作 + 命令在 C# 侧解析 |
| 会话页迁移改动面大 | P4 独立阶段，先以 `data-provider` 收敛数据，再迁移声明 |

---

## 九、验收标准

1. **P1**：给定一段 JSON 控件声明，`HtmlLayoutProjector` 产出与 `LayoutPositionConverter` 一致的布局 CSS（单测通过），htmlviewer 内可渲染该声明。
2. **P2**：声明中的 button + bindings 在 HTML 内点击 → 经消息桥调 C# 函数 → 结果写回 `output` 元素（对齐 WinForms 行为）。
3. **P3**：`AgentUi_HtmlScript` 可对 HTML 元素执行 `set/get/click`；`04-json-script.md` 的用例语义在 HTML 原生元素上复现。
4. **P4**：会话页（侧栏 task 列表 + 分层历史 + 输入 + 发送）改由 JSON 声明渲染，布局改 JSON 即生效，原手写 CSS 退化为主题。
5. **回归**：WinForms 后端全部既有 UI/脚本用例不变（编译通过 + 相关测试绿）。

---

## 十、落地顺序与依赖

```
P0（抽象 + 共享层）
  │
  ├─ P1（HtmlControlFactory + HtmlLayoutProjector）   ← 依赖 P0
  │
  ├─ P2（HtmlValueContract + HtmlEventBridge）        ← 依赖 P0/P1
  │
  ├─ P3（HtmlScriptBridge）                            ← 依赖 P0/P2
  │
  └─ P4（会话页迁移）                                  ← 依赖 P1-P3
```

> 每阶段可独立交付、独立单测；P4 为可选收尾（会话页声明化）。WinForms 后端始终不动，作为回归基线。


