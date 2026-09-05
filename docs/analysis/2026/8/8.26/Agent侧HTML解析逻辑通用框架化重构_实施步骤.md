# Agent 侧 HTML 解析逻辑 → 通用框架：彻底重构实施步骤

> **版本**：1.0
> **日期**：2026-08-26
> **性质**：承接《Agent侧HTML解析逻辑通用框架化重构方案.md》的**实施步骤规划**（代码/框架层面，文件级 + 方法级可落地）
> **顺序**：P3 → P4 → P5 → R（方案 §八 执行顺序；每阶段构建 + 测试全绿后交付）

---

## 〇、前置现状核实（代码实证，与方案文档差异）

| 项 | 方案文档所述 | 代码现状（8.26 实证） | 影响 |
|---|---|---|---|
| `RenderPage/LoadPageLayout` | P3 合并 RenderFlow/RenderPage | **已删除**（8.24），仅剩 `RenderFlow`；布局已由 Plate `HtmlPageRenderer` 接管 | P3 该条基本完成，仅需命名收敛为 `Render(page,data)` |
| 主题接口 | 目标 `IThemeProvider`+`ThemeDocument` | 现库 `IHtmlTheme`（单 Css）+ `HtmlTheme` 静态 + `ThemeMode` | P4 需新引入 `IThemeProvider`/`ThemeDocument` |
| 四份 CSS | `DefaultCss/theme/BaseCss/htmlShell` | `HtmlDocumentRenderer.DefaultCss`(←Assets)、JSON `templates.theme`、`HtmlLayoutProjector.BaseCss`(←Assets)、JSON `templates.htmlShell` | P4 收敛为 IThemeProvider 一处 |
| `HtmlTemplateEngine` | P5 降安全 fallback | 被 `HtmlConversationRenderer`/`ConversationPageRenderer`/`AgentUiLibraryHtml` 深度依赖 | P5 该条风险最高，须先迁 JS 渲染再降级 |
| `AgentUiHost` 静态槽位 | "全部静态槽位" | **15 个**（Framework/Database/ReferenceGrounding/TaskPool/Conversation/HistoryRelations/Renderer/PageDeclaration/Templates/HtmlUi/DiagnosticsSink/ViewerResolver/HtmlSink/ConfigPath/MainDialogViewType） | R 最大单体，独立立项 |

---

## 阶段 0：基线锁定（每阶段开始前必做）
1. 构建基线：`MainProj.sln` + `FileDriven.sln` 全绿；`Plate.Tests/HtmlViewer`、`Agent.Core.Tests` 全绿。
2. 会话页回归快照：`HtmlConversationRendererTests`/`ConversationPageRendererTests` 输出逐字符基线。
3. 主题三模式（Light/Dark/System）对比度基线（P4 复用）。

---

## 阶段 P3：渲染器瘦身（结构模板化）

### 目标
C# 只产数据模型（`role`/`round`/`stage`/`subagent`/`text` 原始字段），角色类/角标全由 `round/message/emptyP` 模板条件绑定承担，删除 C# 双映射（`RoleClasses`/`RoleNames`/`BadgeText`）。

### 步骤
- **P3.0 模板引擎扩展**：`HtmlTemplateEngine` 增加 `{?field=value}` 等式条件绑定（CondNode 判断 model[field]==value），纯增量、向后兼容，使模板能按 `role` 原始值承担角色类判断。
- **P3.1 `HtmlConversationRenderer.ToMessageRow` 数据模型化**：只输出 `role`(ToLower 原始名)/`round`/`stage`/`subagent`/`tool`(布尔位)/`text`；删除 `RoleClasses`/`RoleNames`/`BadgeText`/`summary` 派生；保留 XSS encode。
- **P3.2 `AgentRuntimeUI.json` `message` 模板承担角色类/角标**：`{roleClass}` → `{?role=user} user{/}{?role=assistant} agent{/}{?role=tool} agent{/}{?role=error} error{/}{?role=system} system{/}`；`{badge}` → `{round} {role}{?stage}:{stage}{/}`；`{summary}` → `{stage}{?subagent} · {subagent}{/}`。
- **P3.3 `ConversationPageRenderer` 命名收敛**：`RenderFlow(viewType, histories, ...)` → `Render(page, data)`；`ConversationViewService.RenderFlow` 同步。
- **P3.4 同步测试**：`HtmlConversationRendererTests` 删/改依赖 `RoleClasses`/`BadgeFormat` 的用例（`Options_OverrideRoleClass_BadgeFormat_EmptyText`），保留角色类/角标/tool-layer/encode/round 断言并核对逐字符。

### 验收
- `HtmlConversationRendererTests`/`ConversationPageRendererTests` 快照逐字符一致（先改模板+断言再删 C# 映射）。
- 只改 JSON 模板即可改版（角色类/角标），不改 C#。

---

## 阶段 P4：主题单一源

### 目标
四份 CSS → `IThemeProvider` 一处产出；`HtmlTheme.Resolve()` 唯一消费入口；`AgentRuntimeUI.json` theme 结构化（token + 深浅色 + CSS 引用清单）。

### 步骤
- **P4.1** `CsharpPrj/Plate/HtmlViewer/Theme/ThemeDocument.cs`（token + 深浅两套 + 组件样式 + CSS 引用清单）、`IThemeProvider.cs`（`ThemeDocument Resolve(ThemeMode, ThemeScope)`，全仓唯一真源）、`ConfigThemeProvider.cs`（读结构化 theme）。
- **P4.2** 消费端收敛：`HtmlTheme.Resolve()` 唯一入口（内部委托 IThemeProvider）；`HtmlDocumentRenderer.DefaultCss`/`HtmlLayoutProjector.BaseCss`/JSON `htmlShell` 收敛；`HtmlViewer`/`ToHtml` 显式 `ThemeDocument + ThemeMode`。
- **P4.3** Agent 接线：`AgentRuntimeUI.json` theme 结构化；`ConfigHtmlTheme` → 实现 `IThemeProvider`；`HtmlUiAssembly` 读结构化 theme（先加映射保向后兼容）。

### 验收
三模式对比度回归；改 JSON 即换主题；全仓仅 `IThemeProvider` 一处产出，无四份并存。

---

## 阶段 P5：重复轮子收敛（需产品决策）

> 决策前置：确认"WinForms/HTML 同一份声明像素级一致"。默认走流式。

### 步骤
- **P5.1 `HtmlLayoutProjector`**：流式 → 声明 `layout` 降为语义 intent（proportional/dock/anchor/stack/grid），由页面 CSS/JS 解释；像素一致路线保留为兼容层（产品决策开关）。
- **P5.2 `HtmlControlFactory`**：降兼容迁移层（`listview/combobox/treeview/tabcontrol` 标注迁移）；新样式走原生 `<label>/<textarea>/<button>`；`IsContainer` 词表单一真源保留。
- **P5.3 `HtmlTemplateEngine` 降安全 fallback**：**先迁会话渲染到页面 JS，再降级**（C# 仅把结构化数据 JSON 交给 `window` 渲染器）；引擎本身保留为无 JS/单测 fallback。

### 验收
流式排版正确；删除代码可编译、单测绿、回归基线不退化；无"死而不可删"抽象。

---

## 阶段 R：运行时去静态化（最大单体，独立立项）

> 策略：与 P3/P4/P5 隔离，不混改。涉及 15 静态槽位 + 6 注册库 + `ConversationViewService` + 测试。

### 步骤
- **R.1** 扩展 `AgentUiComposition` 为全量快照（覆盖全部槽位）；`AgentUiHost.AttachComposition` 一次装配，删散落 `AttachXxx`。
- **R.2** `AgentUiLibrary*` 静态方法组 → 实例服务（构造注入依赖）；`RegisterAll(ITrConfigRegistry)` 接收组合根快照，注册委托闭包捕获实例；`ConversationViewService` 改实例。
- **R.3** 删除"未装配→回退默认"三层回退链（`ConversationTemplates.Resolve() ?? Empty`、`DefaultRenderer` 等）。
- **R.4** 消费点改造：`HtmlUiAssembly`/`EnsureHtmlUiAttachedAsync` 经注入实例；`MainDialogViewType` 迁入组合根。
- **R.5** 测试改构造注入断言。

### 验收
全仓无 `AgentUiHost` 静态槽位读取；测试全绿；无全局静态单例。

---

## 依赖与顺序

```
P3（渲染器瘦身，低风险） → P4（主题单一源，中风险）
   └──────────────┬──────────────┘
                  ▼
        P5（需产品决策；模板引擎降级依赖先迁 JS）
                  ▼
        R（去静态化，最大单体，独立立项，P3/P4 之后）
```
- P3/P4 先行（直接服务"解析逻辑通用框架"，改 JSON 即改 UI）。
- P5 中 `HtmlLayoutProjector`/`HtmlControlFactory` 可独立；`HtmlTemplateEngine` 降级依赖 JS 迁移，风险最高。
- R 与 P3/P4 隔离，但 P3/P4 不得新增对静态槽位的新依赖（为 R 铺路）。

---

## 风险与取舍
| 决策 | 取舍 | 建议 |
|---|---|---|
| P4 接口命名 | `IHtmlTheme` vs `IThemeProvider` | 引入 `IThemeProvider`+`ThemeDocument`，`IHtmlTheme` 保留兼容或标废弃 |
| P5 布局 | 流式 vs 像素一致 | 默认流式，绝对定位降兼容层，产品定夺 |
| P5 模板引擎 | C# 模板 DSL vs 页面 JS 渲染 | **先迁 JS 渲染，再降 C# 引擎为 fallback** |
| R 去静态化 | 波及 6 注册库 + 测试 | 独立立项、闭包注入、逐步替换、每步构建绿 |

---

## 执行进度（2026-08-26）

| 阶段 | 状态 | 验证 |
|---|---|---|
| **P3 渲染器瘦身** | ✅ 完成 | `HtmlConversationRendererTests`+`ConversationPageRendererTests` 18/18；`HtmlTemplateEngineTests` 7/7 |
| **P4 基础设施** | ✅ 完成 | `HtmlThemeTests`+`ThemeModeTests`+`HtmlThemeBackgroundTests` 10/10 |
| **P4.2 Agent 接线** | ✅ 完成 | `HtmlUiAssemblyTests`+P3 相关 32/32 |
| **P4.3 消费端迁移** | ✅ 完成 | Plate `HtmlViewer` 全量 96/96；`Agent.Core.Tests` 构建 0 错 |
| **P4.4 三模式/主题文档回归** | ✅ 完成 | `ThemeDocumentTests` 5/5 |
| **P5.1 流式能力就绪** | ✅ 完成 | Plate `HtmlViewer` 全量 102/102 |
| **P5.2 兼容层标注 / P5.3 引擎 fallback 标注** | ✅ 完成 | `Agent.Core.Tests` 构建 0 错 |
| 生产路径切流式 / 完整 JS 渲染迁移 / R | ⬜ 待做（需 GUI 冒烟验证） | — |

### P3 完成项
- `HtmlTemplateEngine` 新增 `{?field=value}` 等式条件（`EvalCond`），向后兼容。
- `HtmlConversationRenderer.ToMessageRow` 只产 `role/round/stage/subagent/text` 原始字段；删 `RoleClasses/RoleNames/BadgeText/summary` 派生。
- `AgentRuntimeUI.json` `message` 模板承担角色类/角标/summary（3 份副本同步）。
- `ConversationPageRenderer.RenderFlow` → `Render`（生产 + 测试调用点同步）。

### P4 基础设施（本批落地）
- 新增 `Theme/ThemeDocument.cs`（token 浅/深 + 组件样式 + CSS 引用清单 + `ToCss()` 收敛点）。
- 新增 `Theme/IThemeProvider.cs`（`Resolve(ThemeMode, ThemeScope)` 唯一真源）+ `ThemeScope`。
- 新增 `Theme/ConfigThemeProvider.cs`（结构化主题数据 → ThemeDocument）。
- `HtmlTheme` 支持 `AttachProvider(IThemeProvider)`（优先级高于 `Attach(IHtmlTheme)`），`Resolve()` 唯一消费入口；`IHtmlTheme` 保留兼容。

### P4.2/P4.3（已接线落地）
- `HtmlUiAssembly.AttachTemplatesAndTheme` 改经 `ConfigThemeProvider` + `HtmlTheme.AttachProvider`（裸 CSS → `ThemeDocument.componentCss`，向后兼容不动格式）；**删除 `ConfigHtmlTheme.cs`**。
- 新增 `Theme/DefaultThemeProvider.cs`（按 scope 产出 DefaultCss/BaseCss，收敛散落 AssetRegistry 常量）。
- `HtmlTheme.ResolveProvider()` 唯一真源（装配 provider 优先，否则 `DefaultThemeProvider`）；`HtmlViewer`/`HtmlDocumentRenderer` 经其直接消费 `ThemeDocument`（新增 `ToHtml(body, ThemeDocument, mode, ...)` 核心重载，保留 `IHtmlTheme` 无 mode 兼容重载）。
- `HtmlViewer.ResolveThemeDocument()`：控件级 `Theme`（IHtmlTheme）优先，否则 `ResolveProvider().Resolve(ThemeMode, Document)`。
- `HtmlUiAssemblyTests` 断言同步（`ConfigHtmlTheme`→非内嵌默认）。

### P5（渐进收敛落地）
- **P5.1 流式能力就绪**：`HtmlControlFactory.RenderElement` 输出 `data-layout="{intent}"`（layout 降语义 intent）；新增 `HtmlLayoutProjector.ProjectFlow` + `HtmlControlRenderer.RenderFlow`（不产绝对像素，CSS flex/grid 承担）；`BaseCss` 增 `.ui-root.flow` flex 类。`ResolveRect`/`Project`（绝对定位）保留为兼容层（像素级一致目标）。
- **P5.2**：`HtmlControlFactory` 类注释标注 listview/combobox/treeview/tabcontrol 为兼容迁移层（新样式走原生元素）。
- **P5.3**：`HtmlTemplateEngine` 类注释定位为安全 fallback（无 JS/单测），常规渲染可交页面 JS；**会话渲染路径保守保留 C# 引擎**（不破坏已验证的 32 个渲染测试）。

> 约束：把流式设为生产默认（`AgentUi_HtmlRender`/htmlPage）与完整 JS 渲染迁移，会改变实际 GUI 渲染机制，需 **GUI 冒烟验证**（`agent_html_view_switch_test`）后才可启用——当前环境无 GUI 验证，能力已就绪但未激进切换。

### P4 剩余接线（下一步）
- **P4.2 Agent 接线**：`AgentRuntimeUI.json` `theme` 从裸 CSS 结构化（tokens/light/dark/componentCss）；`HtmlUiAssembly` 编译 → `ConfigThemeProvider` → `HtmlTheme.AttachProvider`（先加映射保向后兼容，旧裸 CSS 字符串仍可读）。
- **P4.3 消费端迁移**：`HtmlViewer.ResolveTheme`/`HtmlDocumentRenderer.ToHtml` 直接消费 `ThemeDocument`（显式 `ThemeDocument + ThemeMode`）；`HtmlDocumentRenderer.DefaultCss`/`HtmlLayoutProjector.BaseCss` 收敛到 `IThemeProvider`（Component 作用域）。
- **P4.4 三模式回归**：Light/Dark/System 对比度断言。

#### theme JSON 结构化格式建议
```json
"theme": {
  "tokens": {
    "light": { "bg": "#ffffff", "fg": "#24292f", "muted": "#57606a", "border": "#d0d7de", "user-bg": "#dbe7ff", "user-fg": "#0b2a52", "agent-bg": "#f1f2f4", "agent-fg": "#1f2328" },
    "dark":  { "bg": "#0d1117", "fg": "#e6edf3", "muted": "#8b949e", "border": "#30363d", "user-bg": "#1f3a5f", "user-fg": "#e6edf3", "agent-bg": "#21262d", "agent-fg": "#e6edf3" }
  },
  "componentCss": "…（对话气泡 + 布局类组件样式，原 DefaultCss/BaseCss 合并）",
  "cssFiles": []
}
```

### P5 子步骤细化（用户已确认：布局走流式 flex/grid，绝对定位保留为兼容层）
- **P5.1 `HtmlLayoutProjector` 流式化**：`Project` 不再产绝对像素；声明 `layout` 降为语义 `intent`（proportional/dock/anchor/stack/grid）由页面 CSS/JS 解释；`ResolveRect`（比例→px）保留为 `Compat` 兼容层（像素级一致目标时启用）。
- **P5.2 `HtmlControlFactory` 兼容层**：`listview/combobox/treeview/tabcontrol` 标注迁移；新样式走原生 `<label>/<textarea>/<button>`；`IsContainer` 词表单一真源保留。
- **P5.3 `HtmlTemplateEngine` 降安全 fallback**：**先迁会话渲染到页面 JS**（C# 仅把结构化数据 JSON 交给 `window` 渲染器），再降引擎为无 JS/单测 fallback（风险最高，独立小步）。
