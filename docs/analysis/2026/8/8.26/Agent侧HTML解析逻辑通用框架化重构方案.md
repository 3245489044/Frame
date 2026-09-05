# Agent 侧 HTML 解析逻辑 → 通用框架：彻底重构方案

> **版本**：1.0
> **日期**：2026-08-26
> **范围**：`Projects/Agent/src/Agent.Launcher`（Agent 侧 HTML 解析/渲染/装配）→ 下沉/收敛 `CsharpPrj/Plate/HtmlViewer`（通用框架）+ `AgentRuntimeUI.json`（声明单一源）+ 宿主（FileDriven MainForm）
> **性质**：框架级重构方案文档（承接 8.24《HTML解析与UI编排通用框架提取方案》《HtmlViewer_Html后端重复造轮子审查与收敛重构方案》《HTML传递与UI装配框架重构修复方案》的剩余增量）
> **目标**：把所有解析 HTML 的逻辑整理为通用框架——Agent 侧只保留「会话领域数据提供 + 编排」，解析/模板/索引/桥/装配全部收敛到 Plate.HtmlViewer 与组合根单一入口；彻底消除全局静态单例、三层回退链与 C# 内嵌 DOM。

---

## 一、背景与目标

### 1.1 问题现象（8.26 实证）
- `data-cmd` 真实/脚本点击回流断裂：`chrome.webview` 可用、桥已注入，但 postMessage 未达 C# 消息桥（订阅随 CoreWebView2 重建丢失 + 装配时序缺陷）。
- Agent 侧 HTML 解析逻辑散落：脚本命令解析、模板解析、控件索引、函数调用曾为 Agent 私有实现（部分已下沉）。

### 1.2 核心目标
1. **解析通用**：声明反序列化 / 脚本命令解析 / 控件索引 / 函数调用 / 模板渲染全部收敛到 `Plate.HtmlViewer`，Agent 侧零私有实现。
2. **声明单一源**：布局/模板/主题只存在于 `AgentRuntimeUI.json`，改 JSON 即改 UI，不改 C#。
3. **装配单一入口**：`AgentUiComposition` 组合根快照，消除「宿主记得调每个 AttachXxx」与全局静态单例。
4. **结构模板化**：C# 只产数据模型，DOM 结构/角色类/角标全由 `round/message/emptyP` 模板承担。
5. **主题单一源**：`IThemeProvider` 一处产出主题，消除 `DefaultCss/theme/BaseCss/htmlShell` 四份并存。

---

## 二、现状盘点

### 2.1 已完成（可交付验证）
| 项 | 位置 | 状态 |
|---|---|---|
| 声明解析/控件索引/占位注入 | `HtmlDeclarationParser`（Plate） | ✅ |
| 脚本命令解析（含 template 展开） | `HtmlScriptBridge.ParseCommands`（Plate） | ✅ |
| 函数调用端口 | `TrConfigFunctionInvoker`（Plate） | ✅ |
| HTML 运行时装配/消息桥/回流探针 | `HtmlBackendRuntime`（Plate） | ✅ |
| 桥生命周期/DOM 指令 | `HtmlScriptHost/Executor/DomOps/HtmlElementNode`（Plate） | ✅ |
| 组合根单一装配 | `AgentUiComposition` + `AgentUiHost.AttachComposition` | ✅ |
| 生产装配时序自愈 | 会话页首渲染 `EnsureHtmlUiAttachedAsync` | ✅ |

### 2.2 待做（本次彻底重构）
| 阶段 | 内容 | 位置 |
|---|---|---|
| **P3 渲染器瘦身** | 删 `HtmlConversationRenderer` C# 双映射（`DefaultClass`/`BadgeText`/`EmptyTemplate`）；`ConversationPageRenderer` 合并 `RenderFlow/RenderPage` | `HtmlConversationRenderer.cs`、`ConversationPageRenderer.cs` |
| **P4 主题单一源** | `DefaultCss/theme/BaseCss/htmlShell` 四份→`IThemeProvider` 一处；`AgentRuntimeUI.json` theme 结构化 | `HtmlTheme`、`HtmlDocumentRenderer`、`AgentRuntimeUI.json`、`HtmlUiAssembly` |
| **P5 重复轮子收敛** | `HtmlLayoutProjector`（比例→像素）、`HtmlControlFactory`（缩水词表）、`HtmlTemplateEngine`（自研 DSL）降为兼容层/由浏览器原生承担 | `Plate/HtmlViewer/Html/*` |
| **R 运行时去静态化** | `AgentUiHost` 全部静态槽位（Framework/Database/TaskPool/Conversation/HistoryRelations/Renderer/Templates/…）→ 实例服务/构造注入 | `AgentUiHost`、`AgentUiLibrary*`、`ConversationViewService`、测试 |

---

## 三、目标架构（三层）

```
声明层（唯一声明源）   AgentRuntimeUI.json（htmlPage / templates / theme）
        │  统一加载链路（UnifiedConfigLoadAdapter / HtmlDeclarationParser.JsonOpts）
        ▼
HTML 渲染内核（通用框架） Plate.HtmlViewer
  解析：HtmlDeclarationParser · TrConfigFunctionInvoker · HtmlScriptBridge.ParseCommands
  渲染：HtmlUiProjector · HtmlLayoutProjector · HtmlControlFactory · HtmlTemplateEngine · HtmlDocumentRenderer · HtmlTheme
  运行时：HtmlBackendRuntime · HtmlEventBridge · HtmlScriptHost/Executor · IHtmlChannel(HtmlViewer)
        ▲  依赖单向：Agent → Plate，Plate 零 Agent 依赖
Agent 领域编排层       Agent.Launcher
  AgentUiComposition（组合根单一入口）→ AgentUiLibrary*（领域数据提供）
  仅保留「会话领域数据模型 → 行数据 + 编排」，无任何通用解析/模板解析/索引/函数调用私有实现
```

---

## 四、彻底重构步骤

### P3 渲染器瘦身
- `HtmlConversationRenderer`：删除 C# 内嵌 DOM 结构与 `DefaultClass`/`BadgeText` 双映射；角色类/角标改由 `message` 模板的条件绑定承担（C# 只传 `role`/`round`/`stage` 原始数据），保留 XSS 编码。
- `ConversationPageRenderer`：`RenderFlow`/`RenderPage` 合并为 `Render(page, data)`；`FallbackDeclaration` 保留为未接线兜底。
- 验收：会话页回归快照逐字符一致（改模板不改 C# 即可改版）。

### P4 主题单一源
- 收敛 `DefaultCss/theme/BaseCss/htmlShell` 四份为 `IThemeProvider` 一处产出；`HtmlTheme.Resolve()` 为唯一消费入口。
- `AgentRuntimeUI.json` 的 `theme` 从裸 CSS 字符串结构化（token + 深浅色 + CSS 引用清单）。
- 验收：三模式（Light/Dark/System）对比度回归；改 JSON 即换主题。

### P5 重复轮子收敛（取舍明确）
- `HtmlLayoutProjector`：若接受流式排版（HTML 原生 flex/grid/%），C# 不再产出绝对像素，声明 `layout` 降为语义 intent 由页面 CSS/JS 解释；`HtmlControlFactory` 降为兼容迁移层，新样式走原生 `<label>/<textarea>/<button>`。
- `HtmlTemplateEngine`：降为**安全 fallback**（无 JS 场景/单测），常规由页面渲染 JS 承担。
- 取舍边界：若需 WinForms/HTML「同一份声明像素级一致」，绝对定位换算保留——此为产品决策，默认走流式。

### R 运行时去静态化
- `AgentUiHost` 静态槽位收敛为 `AgentUiComposition` 全量快照；`AgentUiLibrary*` 从静态方法组改为**实例服务**，注册时闭包注入组合根；渲染器/库函数经构造注入读取，删「未装配→回退默认」三层回退链。
- 测试改构造注入断言；`MainDialogViewType` 迁入组合根（view 状态）。

---

## 五、文件清单

| 文件 | 动作 | 阶段 |
|---|---|---|
| `Agent.Launcher/Modules/HtmlConversationRenderer.cs` | 删双映射，结构模板化 | P3 |
| `Agent.Launcher/Modules/ConversationPageRenderer.cs` | 合并 RenderFlow/RenderPage | P3 |
| `FileDriven/DrivenConfig/JsonConfig/AgentRuntimeUI.json` | message/round 模板承担角色类/角标；theme 结构化 | P3/P4 |
| `Plate/HtmlViewer/HtmlTheme.cs`、`HtmlDocumentRenderer.cs` | 主题单一源 | P4 |
| `Agent.Launcher/Modules/ConfigHtmlTheme.cs`、`HtmlUiAssembly.cs` | 主题接线收敛 | P4 |
| `Plate/HtmlViewer/Html/HtmlLayoutProjector.cs`、`HtmlControlFactory.cs`、`HtmlTemplateEngine.cs` | 降兼容层/原生承担 | P5 |
| `Agent.Launcher/Modules/AgentUiHost.cs`、`AgentUiLibrary*.cs`、`ConversationViewService.cs`、`Composition/*` | 运行时去静态化 | R |
| 测试：`Plate.Tests/HtmlViewer`、`Agent.Core.Tests` | 就绪/瘦身/去静态断言 | 各阶段 |

---

## 六、验收标准

1. **解析单一真源**：Agent 侧零 HTML 解析私有实现；`HtmlScriptBridge.ParseCommands`/`HtmlDeclarationParser`/`TrConfigFunctionInvoker` 全在 Plate。
2. **声明单一源**：布局/模板/主题只在 `AgentRuntimeUI.json`；改 JSON 即改 UI。
3. **装配单一入口**：`AgentUiComposition` 一次装配全部槽位；无全局静态单例。
4. **结构模板化**：会话页改版不改 C#（只改模板/CSS/JS）。
5. **主题单一源**：仅 `IThemeProvider` 一处产出主题，无四份 CSS 并存。
6. **回归**：`MainProj.sln`/`FileDriven.sln` 全绿；`Plate.Tests/HtmlViewer`、`Agent.Core.Tests` 全绿；GUI `agent_html_view_switch_test` 冒烟通过。

---

## 七、风险与取舍

| 决策 | 取舍 |
|---|---|
| P3 改模板契约 | 输出保真需同步改模板 + 断言 + GUI 冒烟；换「改版不改 C#」 |
| P4 改配置格式 | 需迁移 AgentRuntimeUI.json + HtmlTheme/ConfigHtmlTheme；向后兼容可先加映射 |
| P5 布局下沉浏览器原生 | 需产品定夺「像素级一致 vs 流式」；默认流式，保留兼容层 |
| R 全运行时去静态化 | 波及 9 库注册机制 + 237 测试；单独立项，避免与 P3/P4 混改 |
| 回归无 GUI 自动验证 | 依赖单元测试 + 构建绿 + 用户 GUI 冒烟定版 |

---

## 八、执行顺序

先 **P3 → P4**（HTML 输出/配置收敛，直接服务「解析逻辑通用框架」），再 **P5**（兼容层/原生下沉，需产品确认），最后 **R**（全运行时去静态化，最大、单独立项）。每阶段构建 + 测试全绿后交付；GUI 冒烟由宿主回归。
