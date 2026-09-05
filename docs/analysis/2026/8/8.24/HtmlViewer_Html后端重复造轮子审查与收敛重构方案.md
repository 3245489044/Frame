# HtmlViewer 后端重复造轮子审查与收敛重构方案

> **版本**：1.0
> **日期**：2026-08-24
> **范围**：`CsharpPrj/Plate/HtmlViewer`（Html 渲染内核）——聚焦 WebView2/HTML 后端中**重复实现浏览器原生能力**的抽象层
> **性质**：重构方案文档（设计，不随附实施）
> **与既有 8.24 两方案的关系**：
> - 《HTML解析与UI编排通用框架提取方案.md》解决「Agent 侧解析逻辑下沉 Plate.HtmlViewer」（复用/归属）；
> - 《JSON声明式UI通用化重构方案_HTML后端与json-script桥.md》解决「同一 JSON 声明多后端泛化」（补 HTML 适配器）；
> - **本文只审查「HtmlViewer 里哪些抽象是重复造的轮子」并给出收敛清单与优先级**，与上述两方案互补而非冲突：它们回答"放哪、复用谁"，本文回答"要不要造、能否用浏览器原生取代"。
> **目标**：C# 尽力成为「数据供应端 + 单条消息桥」，把布局、模板、主题、控件抽象尽量下沉到 WebView2 内的原生 HTML/CSS/JS；凡浏览器原生已有且更强者，C# 侧不再重造。

---

## 一、审查结论摘要

HtmlViewer 在「复用 JsonUI 的声明式 JSON 控件树」与「面向 HTML 原生能力」之间，多数时候**把 JsonUI/WinForms 的抽象层在 HTML 里再实现了一遍**，且多数是**弱化子集**。按严重度分四级：

| 级别 | 点位 | 处置 |
|---|---|---|
| **A 真重复（可收敛）** | `HtmlLayoutProjector`（比例→绝对像素）、`HtmlControlFactory`（缩水控件词表）、`HtmlTemplateEngine`（自研模板 DSL）、`HtmlDocumentRenderer/HtmlTheme`（内置 CSS 主题） | 逐步下沉/简化，改由浏览器原生或页面内 JS/CSS 承担 |
| **B 内部重复 / 两套并存** | 事件桥两路线（`data-event`+`04-json-script`）、消息桥多层封装（`__domBridge`/`IDomBridge`/`HtmlElementNode`） | 收敛为单条桥、单协议 |
| **C 必要（保留）** | WebView2 承载与 C#↔JS `postMessage`/`ExecuteAsync`、`HtmlEncode` XSS 防线、`HtmlDeclarationParser` 反序列化 | 不动，属跨进程通信与安全基础 |
| **D 反哺（保留并推广）** | `HtmlEventBridge` 复用 `TConfig.EventBinding/ParameterBinding`，与 JsonUI 共享同一事件模型 | 正确的「少造轮子」范例 |

> 全文判定标准只有一个：**某个语义在「底层执行环境」（WebView2 的 HTML/CSS/JS）里是否原生拥有且更强；若是，则 C# 侧那层抽象即为重复造轮子。**

---

## 二、重复造轮子点位清单（A 级，重点）

### 2.1 `HtmlLayoutProjector` — 比例布局 → 绝对定位像素（最重）

- **现状**：把 `LayoutPosition` 手工换算为 `position:absolute; left/top/width/height px`，并在 C# 维护嵌套父容器相对坐标、TabControl inset、收敛逻辑。
- **判定**：WebView2 内 CSS 原生具备 `flexbox / grid / width:% / aspect-ratio / sticky`，天然完成比例、自适应、嵌套、回流，且是真正的响应式。C# 手工把 `%` 算成 `px` 是**把 WinForms「父尺寸×比例」思维硬搬到 HTML，并丢了流式排版**。
- **证据链**：[HtmlLayoutProjector.cs](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Plate/HtmlViewer/Html/HtmlLayoutProjector.cs) 复用 WinForms 语义（对应 Operation 侧 `LayoutPositionConverter`），其本质是 GDI 坐标模型的 HTML 移植。
- **收敛目标**：布局交给页面内 CSS。声明仍保留 `layout` 作为**语义化 intent**，但由页面 JS/CSS 解释（`width:22%`、`.stack`、`.grid`），C# 不再产出绝对像素。
- **边界（勿越界）**：若目标是"WinForms 与 HTML 用**同一份**声明产出**像素级一致**的排布"，则绝对定位换算有存在意义——此为 B/目标约束，而非技术必要。需由产品取舍（见 §五 路线对比）。

### 2.2 `HtmlControlFactory` — 缩水控件词表 → DOM

- **现状**：JSON `type`（label/textbox/button/combobox/listview/tabcontrol…）映射 `span/input/select/ul/div`，仅覆盖 WinForms 控件宇宙子集。
- **判定**：目标本身就是 HTML，**直接用 HTML 标签写即可**；再套一层"控件词表"抽象多余，且只覆盖子集、易漂移（`combobox→select`、`listview→ul` 都是弱化映射）。
- **收敛目标**：普通内页直接写 `<label> <textarea> <button data-name>` 原生元素；本轮词表保留为**兼容迁移层**（存量 JSON 声明仍能渲染），新样式走原生。

### 2.3 `HtmlTemplateEngine` — 自研模板 DSL

- **现状**：手写 `{#each}{?cond}{:else}{>name}{field|raw}` 字符串模板解析器，把数据注入 HTML。
- **判定**：JS 生态成熟模板方案无数（模板字面量、`document.createElement`、`Map`+渲染函数等）；且若让**注入页面自带的 JS 负责渲染**，连这条 DSL 都省了。C# 侧再做一套字符串替换模板是重复。
- **收敛目标**：模板逻辑迁到页面 JS（每类片段一个渲染函数），C# 仅把结构化数据以 JSON 交给 `window` 底下的渲染器，或经 `ExecuteScriptAsync` 传对象。C# 端 `InjectData` 仅保留为**安全 fallback**（无 JS 场景 / 单元测试）。

### 2.4 `HtmlDocumentRenderer` / `HtmlTheme` — 内置对话 CSS + 主题接口

- **现状**：C# 侧维护一套完整对话气泡 CSS 与 `IHtmlTheme` 抽象（暗/亮主题）。
- **判定**：用真实 `.css` 文件 + `prefers-color-scheme` 即可，`IHtmlTheme`/C# 字符串拼接 CSS 属额外封装。
- **收敛目标**：主题与组件样式下沉为独立 CSS（主题文件 + 组件主题），C# 只决定"引用哪个 CSS 文件名"，不再内联拼接样式。

---

## 三、内部重复 / 两套并存（B 级）

### 3.1 事件桥两路线交错
- 声明式 `data-event → HtmlEventBridge`（复用 TConfig 绑定）与 `04-json-script` 命令协议 `HtmlScriptBridge`（exec/set/get/click/sleep）**并存**，同一件事两套交互语法。
- **收敛**：明确一主一辅——把 `json-script` 作为统一命令协议（与既有《json-script 桥》方案一致），`data-event` 降为「点击类事件的语法糖」或由桥统一翻译；避免两套解析、两套分发。

### 3.2 消息桥多层封装
- `HtmlViewer` 内注入的 `__domBridge`（data-cmd/data-event 点击桥）之外又叠 `IDomBridge` / `HtmlElementNode` / `HtmlEventBridge` 多层同名"桥"。
- **收敛**：对外只保留**一条** `IDomBridge`（含可信通道 + XSS 防线），其余收敛为其内部实现细节；`HtmlElementNode` 是否保留由「是否要统一的 `IUiElementNode`」决定（见 §五 路线选型）。

---

## 四、必要、不算造轮子（C 级，明确不动）

| 资产 | 为何保留 |
|---|---|
| `HtmlViewer` 控件本身（WebView2 承载） + `postMessage` / `ExecuteAsync` 桥 | 跨进程通信、WPF/WinForms 嵌入 WebView2 的**必要基础设施** |
| 一切用户/模型文本的 `HtmlEncode` 与白名单 DOM 操作 | **安全必守线**，不可下沉到不可信侧 |
| `HtmlDeclarationParser` 统一反序列化（case-insensitive options + `BuildControlIndex`） | 声明加载基础；与既有《提取方案》一致复用 |
| `UnifiedConfigLoadAdapter` 统一配置加载 | 单一加载链，避免多处 options 分叉 |

---

## 五、根因

JsonUI 是为**原生 WinForms** 设计的声明抽象（`layout` 比例 + 控件词表 + 事件绑定面向 GDI 控件）。HtmlViewer 迁移时**没有利用 WebView2 的 HTML/CSS/JS 原生能力**，而是把 WinForms 式抽象（坐标布局、控件类、主题、模板）在 HTML 里**再实现了一遍**。于是用户看到"同一套 UI 描述、两套后端、各自再造一套布局/模板/控件词表"。

> 根因即结论：**顺 JsonUI 的声明模型而来是"复用"，但落到 HTML 后仍坚持用 C# 承担浏览器本就擅长的能力，就是重复造轮子。**

---

## 六、两条收敛路线与选型

### 路线 A：C# 只供数据 + 页面内 JS/CSS（本文推荐主线）
- C# 角色收敛为：**声明 → 结构化数据 + 引用 CSS/JS 清单 + 一条 `IDomBridge`**。
- 布局/模板/主题/交互全部由 WebView2 内页面按原生 HTML/CSS/JS 完成。
- 收益：去掉 A 级四类抽象；最大程度利用浏览器能力；真正的响应式。
- 代价：`layout` 声明不再产出"与 WinForms 像素级一致"的排布（如需一致则退回路线 B）；需在一侧撰写页面模板/JS（可用静态 html/css/js 资源，不入 csproj 拼接）。

### 路线 B：保留声明驱动 + 绝对定位换算（= 既有《json-script 桥/通用化》方案主张）
- C# 继续以 `HtmlLayoutProjector` 产出绝对定位，使同一 JSON 声明在 WinForms 与 HTML **像素级一致**。
- 收益：双后端行为对齐、`layout` 是唯一真源。
- 代价：A 级抽象保留，且低估浏览器原生能力（绝对定位非响应式）。

### 选型建议
| 诉求 | 优先路线 |
|---|---|
| 内嵌会话页/历史气泡等「富展示」页 | **A**（表现力、可维护性优先） |
| 同一声明需 WinForms 与 HTML 像素级一致排布 | **B** |
| 存量 HTML 资产 / 迁移期 | `B → A` 渐进：先下沉归属（既有两方案），再逐步去除 A 级抽象 |

> 建议以"页面用途"为界：**工具/表单类**保持 B（对齐自动化脚本），**展示/会话类**转 A（用浏览器排版）。两种路线可并存于同一代码库。

---

## 七、分步实施与验收标准（每步可独立交付）

### 步骤 0（配合既有两方案）：归属收敛，先复用后去除
- 完成《HTML解析与UI编排通用框架提取方案》P0–P3（`HtmlDeclarationParser`/`TrConfigFunctionInvoker`/`HtmlPageRenderer` 下沉）与《json-script 桥》P1–P3，确保 HtmlViewer 能力已接线、无孤岛。
- **验收**：`AttachHtmlUi` 有宿主调用点；UnitTests 全绿；WinForms 后端不动。

### 步骤 1：B 级收敛 — 消息桥与事件协议单一化
- 收敛 `__domBridge`/`IDomBridge`/`HtmlEventBridge` 为**单桥单协议**；确立 `json-script` 为统一命令协议。
- **验收**：移除重复桥类后，`data-event` 与 `04-json-script` 用例在同一桥下均通过；无行为漂移。

### 步骤 2：A 级试点 — 会话展示页转路线 A
- 选 `conversationHistoryViewer` 为试点：C# 只产结构化数据（消息列表 JSON），布局/气泡/折叠/主题移到独立 CSS + 页面渲染 JS；`layout` 声明降级为 intent。
- **验收**：会话页视觉与现有等价的回归快照一致；用户/模型文本仍被 `HtmlEncode`；改布局只改 CSS 或 JS，不改 C#。

### 步骤 3：A/B 分离界定
- 依据 `§六选型`，为控件声明打标记（如 `backend:"native-layout"|"web"`），工具/表单走 B、展示走 A。
- **验收**：任一声明可用标记显式选后端；两后端各有独立单测与回归基线。

### 步骤 4：去重收尾
- 对纯 A 路线的页面，移除其专属的 `HtmlLayoutProjector` 绝对定位输出、`HtmlTemplateEngine` 占位 DSL、`IHtmlTheme` 内联 CSS；`HtmlControlFactory` 降为**兼容层**。
- **验收**：删除代码后可编译、单测绿、回归基线不退化；无遗留"死而不可删"抽象。

> 全流程遵守项目约束**"新特性优先增强既有循环，而非新建模块"**：以上步骤均为**收敛/删除或迁移归属**，不以新增抽象为前提。

---

## 八、风险与取舍

| 风险 | 应对 |
|---|---|
| 转 A 后 `layout` 声明不再"数学等价"于 WinForms | 以页面用途（表单 vs 展示）为边界分级；工具/表单保留 B |
| 页面内 JS 增多 → 脚本失控/安全面扩大 | 坚持 `HtmlEncode` 可信通道、白名单 DOM 操作、命令在 C# 侧解析；页面 JS 只做渲染不做特权操作 |
| 去除 A 级抽象影响存量会话渲染 | 步骤 2-4 逐页试点 + 回归快照，`HtmlControlFactory` 降级为兼容层兜底 |
| 与既有两方案"继续用绝对定位"主张冲突 | 本文为**审查/收敛路线**，不推翻既有下沉；落地时按 §七 渐进，争议点（是否绝对定位）由 §5.1 边界/选型决定 |

---

## 九、附录：可直接"少造轮子"的最小判断表

| 需求 | 浏览器原生（择一） | C# 侧本可省略 |
|---|---|---|
| 比例/自适应布局 | flexbox / grid / `width:%` / `aspect-ratio` | `HtmlLayoutProjector` 绝对像素换算 |
| 列表/循环渲染 | 页面 JS `map()`/render 函数 | `HtmlTemplateEngine` `{#each}` DSL |
| 条件/模板 | 页面 JS 条件 + 模板字面量 | `HtmlTemplateEngine` 解析器 |
| 控件外观与行为 | 原生 `<input>/<select>/<button>` + CSS | `HtmlControlFactory` 词表（降级兼容层） |
| 暗/亮主题 | `.css` + `prefers-color-scheme` | `IHtmlTheme`/内联字符串 CSS |
| C#↔JS 通信 | `postMessage` / `ExecuteAsync` | 必要，**保留** |
| 文本注入防 XSS | `HtmlEncode`（C# 侧） | 必要，**保留** |

---

## 十、实施细节与文件目录（追加）

> 本节把 §七 的步骤落到**文件级改动清单**，并给出涉及工程/目录全貌。
> 已核实的**关键事实**（决定实施顺序）：`AgentFrameworkHosting.TryBootstrapGuiAsync` 生产装配链**未调用 `HtmlUiAssembly.Attach`**——
> 即 HTML 事件桥（`AttachHtmlUi`）、JSON 主题注入（`HtmlTheme.Attach(templates.theme)`）、会话模板（`AttachConversationTemplates`）在生产宿主里**均未接线**。
> 后果：`HtmlViewer.SetHtmlAsync` 走 `HtmlTheme.Resolve()` 落回 `HtmlDocumentRenderer.DefaultCss`（内嵌默认），其 `@media (prefers-color-scheme: dark)` 在系统深色时命中 `--bg:#0d1117` → 呈现"一片黑"；
> 且 JSON `templates.theme` 是**死配置**（改了不生效）。本节的阶段 1/3 必须同时修复此接线缺口。

### 10.1 涉及工程与目录总览

| 工程/目录 | 职责 | 关键文件 |
|---|---|---|
| `CsharpPrj/Plate/HtmlViewer/` | HTML 渲染内核（WebView2 承载 + 文档壳 + 主题 + HTML 投影/模板/控件词表） | `HtmlViewer.cs`、`HtmlDocumentRenderer.cs`、`HtmlTheme.cs`、`IHtmlTheme.cs`、`HtmlViewerDomBridge.cs`、`Html/*.cs` |
| `Projects/Agent/src/Agent.Launcher/Modules/` | Agent UI 宿主与渲染器（会话页/模板/事件桥/主题透传） | `AgentUiHost.cs`、`HtmlUiAssembly.cs`、`HtmlUiRuntime.cs`、`ConfigHtmlTheme.cs`、`ConversationTemplates.cs`、`HtmlConversationRenderer.cs`、`ConversationPageRenderer.cs`、`AgentUiLibraryHtml.cs`、`AgentUiLibraryConversation.cs` |
| `Projects/Agent/src/Agent.Launcher/Composition/` | Agent 运行时组合根 | `AgentFrameworkHosting.cs`（缺 HTML 接线）、`AgentFramework.cs`、`AgentFrameworkAssembler.cs` |
| `CsharpPrj/Operation/FrontEndDriven/` | WinForms 原生后端（控件创建/布局/渲染） | `Shared/ControlTypeRegistry.cs`（`"htmlviewer"→new HtmlViewer()`）、`ControlRender/**`、`WinForms/**` |
| `CsharpPrj/Modules/WinFormsValueContract/` | 控件值契约（output 写回） | `BuiltInValueContracts.cs`（htmlviewer Text → `SetHtmlAsync`） |
| `FileDriven/FileDriven.Execution/` | FileDriven 宿主 | `Initial/MainForm.cs`（调 `TryBootstrapGuiAsync`，未接 `HtmlUiAssembly.Attach`）、`Initial/UnifiedConfigLoadAdapter.cs` |
| `FileDriven/DrivenConfig/JsonConfig/` | 声明与主题配置 | `AgentRuntimeUI.json`（`templates.theme` / `htmlShell` / `flowShell` / `conversationHistoryViewer.htmlPage`） |
| `CentralTest/.../Plate.Tests/HtmlViewer/`、`CentralTest/.../Agent.Core.Tests/` | 回归测试 | `HtmlThemeTests.cs`、`HtmlPageRendererTests.cs`、`HtmlLayoutProjectorTests.cs`、`HtmlUiAssemblyTests.cs`、`ConversationPageRendererTests.cs` |

### 10.2 目标态：单条声明流水线 + 显式组合根

```
AgentRuntimeUI.json ──→ UIConfiguration ──→ 编译(Composer) → 不可变 IR
   · ControlTree IR   (唯一布局/控件树)
   · ThemeDocument IR (唯一主题：token + 深浅色 + 组件样式)
   · EventMap+DataSlots
        ↓
渲染后端 (IControlRenderer 双实现)
   · WinForms 后端 (FrontEndDriven, 现成)
   · HTML 后端 (HtmlDocumentRenderer + 页面 CSS/JS)
        ↓
注入：AgentUiComposition（显式构造，废静态单例）→ WebView2 锚定配色
```
---

### 10.3 阶段 1：主题单一源 + 深浅色显式锚定 + 补生产接线（先行，根治"黑色"）

**新增**
- `CsharpPrj/Plate/HtmlViewer/Theme/ThemeDocument.cs`：主题数据模型（`--bg/--fg/--muted/--border/--user-bg/...` token + 组件样式 + `Light/Dark` 两套值 + 引用页面 CSS 清单）。
- `CsharpPrj/Plate/HtmlViewer/Theme/IThemeProvider.cs`：`ThemeDocument Resolve(ThemeMode mode, ThemeScope scope)`；`enum ThemeMode { Light, Dark, System }`。**仅此一个主题真源**。
- `CsharpPrj/Plate/HtmlViewer/Theme/ConfigThemeProvider.cs`：读 `AgentRuntimeUI.json` `templates.theme`（或独立 `theme.json`）编译为 `ThemeDocument`，不再直接吃裸 CSS 字符串。
- `CsharpPrj/Plate/HtmlViewer/Web/theme.light.css`、`Web/theme.dark.css`（路线 A：主题下沉为真实 `.css`，C# 只决定引用哪个文件，见 §二 2.4）。

**修改**
- `CsharpPrj/Plate/HtmlViewer/HtmlDocumentRenderer.cs`：
  - `DefaultCss` 从"权威主题"降级为**测试/无 provider 兜底**（不再是唯一真源）；删除其 `@media dark` 分支，改由 `ThemeMode` 显式注入。
  - `ToHtml` 签名显式化：`ToHtml(string? body, ThemeDocument theme, ThemeMode mode, double? fontSizePx)`，**删除内部 `HtmlTheme.Resolve()` 全局单例读取**。
  - 文档壳补 `<meta name="color-scheme" content="light dark">` 与 `<html style="background:...">`（不只 body 设背景）。
- `CsharpPrj/Plate/HtmlViewer/HtmlViewer.cs`：
  - `SetHtmlAsync`/`ApplyFontSizeAsync`/`EnsureReadyAsync` 改经显式 `ThemeDocument + ThemeMode`；控件新增 `ThemeMode` 属性。
  - 初始化锚定配色：`CoreWebView2.Profile.PreferredColorScheme = mode.Map()`、`CoreWebView2.DefaultBackgroundColor = theme.Background(mode)`，**消除"随系统猜测而黑"**。
- `Projects/Agent/src/Agent.Launcher/Composition/AgentFrameworkHosting.cs`：`TryBootstrapGuiAsync` 补调用 **`HtmlUiAssembly.Attach(...)`**（含 `HtmlTheme.Attach`/`AttachHtmlUi`/`AttachConversationTemplates`/`AttachConversationPageDeclaration`），使 JSON 主题与事件桥在生产真正生效。
- `FileDriven/FileDriven.Execution/Initial/MainForm.cs`：装配后把 htmlviewer 桥/配置传给组合根（见阶段 3）。

**删除/降级**
- `Projects/Agent/src/Agent.Launcher/Modules/ConfigHtmlTheme.cs`：透传 wrapper 取消，改由 `ConfigThemeProvider` 产出 `ThemeDocument`（或删除）。
- `CsharpPrj/Plate/HtmlViewer/HtmlTheme.cs` / `IHtmlTheme.cs`：全局静态单例 + `Attach/Detach` 移除（阶段 3 合并处理），`Resolve()` 逻辑并入 `IThemeProvider`。

**验收（阶段 1）**
- `HtmlThemeTests.cs` 改为测 `IThemeProvider` 与显式 `ToHtml`；新增"`Dark`=暗 token、`Light`=亮 token、`System` 才随系统"断言。
- 运行 App：深色系统下 `conversationHistoryViewer` 不再"整片黑"；改 JSON theme 立即生效（接线已补）。

---

### 10.4 阶段 2：统一渲染后端，消除双后端漂移

**新增**
- `CsharpPrj/Plate/HtmlViewer/Backend/IControlRenderer.cs`：后端契约 `Render(ControlConfig, LayoutRect, ThemeDocument) → string`。
- `CsharpPrj/Plate/HtmlViewer/Backend/HtmlBackend.cs`：`HtmlLayoutProjector`/`HtmlControlFactory` 收敛为该契约的 HTML 实现。
- `CsharpPrj/Operation/FrontEndDriven/Abstractions/IControlRenderer.cs`：WinForms 原生后端实现同一契约。

**修改**
- `CsharpPrj/Plate/HtmlViewer/Html/HtmlLayoutProjector.cs`：删除独立 `BaseCss`，组件样式并入 `ThemeDocument`；布局换算改为"语义 intent"输出（`%`/`.stack`/`.grid`），仅路线 B 保留绝对像素换算。
- `CsharpPrj/Plate/HtmlViewer/Html/HtmlUiProjector.cs`：不再产出独立 `Css`，改经 `IThemeProvider.Resolve` 取主题；`HtmlRenderResult` 收敛为"布局结构 + 数据槽 + 主题引用"。
- `CsharpPrj/Plate/HtmlViewer/Html/HtmlPageRenderer.cs`：与 `HtmlControlFactory` 共享 IR，删除各自独立主题/布局实现。

**删除/降级**
- `HtmlControlFactory.cs`：控件词表降为**兼容迁移层**（存量声明可渲染），新样式走原生 HTML 标签。

**验收（阶段 2）**：任一声明两后端行为一致；`HtmlLayoutProjectorTests`/`HtmlControlFactoryTests` 绿；无"同一样式两处定义"。
---

### 10.5 阶段 3：显式组合根，废静态单例

**新增**
- `Projects/Agent/src/Agent.Launcher/Composition/AgentUiComposition.cs`：显式构造并持有 `IThemeProvider / IConversationRenderer / ConversationTemplates / HtmlUiRuntime / IDomBridge / DatabaseModule`，全部构造器注入；单一 `Compose(...)` 入口替代 `HtmlUiAssembly.Attach` + 一堆 `AgentUiHost.Attach*`。

**修改**
- `Projects/Agent/src/Agent.Launcher/Modules/AgentUiHost.cs`：静态槽位（`Current/Database/Conversation/Templates/HtmlUi/HistoryRelations/ConversationRenderer`）改为**实例服务**或收敛为单一 `AgentUiServices` 快照（组合根产物），渲染器/库函数经构造注入读取，不再读全局单例。
- `Projects/Agent/src/Agent.Launcher/Modules/HtmlUiAssembly.cs`：改由 `AgentUiComposition` 调用，消除"生产无调用点"缺口；`ExtractConversationPageDeclaration` 复用上层已建控件索引（修 §四 R1 重复解析）。
- `Projects/Agent/src/Agent.Launcher/Modules/HtmlUiRuntime.cs`、`ConversationPageRenderer.cs`、`HtmlConversationRenderer.cs`、`AgentUiLibrary*.cs`：构造函数接收依赖，删 `?? DefaultRenderer` / `ConversationTemplates.Resolve()` 三层回退链（§四 R2/R3）。
- `Projects/Agent/src/Agent.Launcher/Composition/AgentFrameworkHosting.cs`：调 `AgentUiComposition.Compose(...)`。

**删除/降级**：`HtmlTheme.cs` 全局单例、`ConfigHtmlTheme.cs`、`ConversationTemplates.cs` 静态转发（并入实例服务）。

**验收（阶段 3）**：单测并行/隔离绿（注入 stub）；装配时序由组合根唯一决定；删"未装配→回退默认"分支后行为稳定；`HtmlUiAssemblyTests`/`AgentUiHostConversationRendererTests` 更新为构造器注入断言。

---

### 10.6 阶段 4：收敛消息渲染三份实现

**修改**
- `Projects/Agent/src/Agent.Launcher/Modules/HtmlConversationRenderer.cs`：删除 C# 内嵌 DOM（msg/bubble/round）、`Options.EmptyTemplate`、`DefaultClass`/`BadgeText` 双映射；结构统一由 `round/message/emptyP` 模板承担，C# 只产数据模型。
- `Projects/Agent/src/Agent.Launcher/Modules/ConversationPageRenderer.cs`：`RenderFlow`/`RenderPage` 合并为 `Render(page, data)`；`FallbackDeclaration` 保留为未接线兜底。
- `Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryHtml.cs`：`htmlShell` 与 `theme` 职责合并（见阶段 1），`HtmlRenderAsync` 改经 `IThemeProvider` 取主题，消除"改 JSON 不影响该路径"（§五 E3）。

**删除/降级**：`HtmlTemplateEngine` 占位 DSL 降为**安全 fallback**（无 JS 场景/单测），常规走页面渲染 JS（§二 2.3 路线 A）。

**验收（阶段 4）**：会话页回归快照一致；改布局只改模板/CSS/JS 不改 C#。

---

### 10.7 阶段 5：数据/编排去重

**新增**
- `Projects/Agent/src/Agent.Launcher/Modules/ConversationViewService.cs`：单一 `RenderViewAsync(...)` 收敛四处 `ListHistories→ResolveHistoryForTask→GetHistory→RenderFlow` 编排（`ConversationSendAsync`/`ConversationListHtmlAsync`/`ConversationHistoryAsync`/`ConversationMessageDispatchAsync`）。

**修改**
- `AgentUiLibraryConversation.cs`：四入口改调 `ConversationViewService`；`RenderFlow`/`RenderHistory` 私有编排收敛。
- `HtmlUiAssembly.cs`：`BuildControlIndex` 传参复用（修 §四 R1）。

**验收（阶段 5）**：四入口输出与现有一致；编排只在一处维护。

---

### 10.8 阶段 6：配置与迁移（AgentRuntimeUI.json + 宿主）

**修改**
- `FileDriven/DrivenConfig/JsonConfig/AgentRuntimeUI.json`：
  - `templates.theme` 从"裸 CSS 字符串"改为结构化 `theme`（token + 深浅色 + 引用 CSS 清单）或直接引用 `theme.light.css/theme.dark.css`（路线 A）。
  - 收敛 `htmlShell`/`flowShell` 与 `theme` 的 CSS 注入职责为 `PageShell`。
  - 新增后端标记（路线 A/B）：`conversationHistoryViewer` 标 `backend:"web"`。
- `CsharpPrj/Modules/WinFormsValueContract/BuiltInValueContracts.cs`：htmlviewer 值契约新增 `ThemeMode`/`Theme` 参数通道（写入 `SetHtmlAsync` 时携带显式主题）。
- `CsharpPrj/Operation/FrontEndDriven/Shared/ControlTypeRegistry.cs`：htmlviewer 构造支持注入 `ThemeMode`。

**验收（阶段 6）**：改 `AgentRuntimeUI.json` 即可换主题/换后端标记；`UnifiedConfigLoadAdapter` 加载链不回归。

---

### 10.9 全局验证清单

- `dotnet test` 覆盖：`Plate.Tests/HtmlViewer`、`Agent.Core.Tests` 全绿。
- 回归快照：`conversationHistoryViewer` 会话页在 Light/Dark/System 三种模式下背景/文字对比度均通过（新增测试）。
- 主题单一源：全仓仅 `IThemeProvider` 一处产出主题，无 `DefaultCss`/`theme`/`BaseCss`/`htmlShell` 四份并存。
- 接线完整：生产链路 `MainForm → TryBootstrapGuiAsync → AgentUiComposition.Compose → HtmlUiAssembly.Attach` 存在可断言的调用点。
- 废弃清理：`HtmlTheme` 静态单例、`ConfigHtmlTheme`、`HtmlLayoutProjector.BaseCss`、`HtmlControlFactory`（降兼容层）无"死而不可删"。


