# HtmlViewer 通用框架化：进一步架构重构方案

> **版本**：1.0
> **日期**：2026-08-30
> **范围**：`Projects/Agent/src/Agent.Launcher`（仍滞留的会话页渲染/浮层/编排逻辑）→ 下沉 `CsharpPrj/Plate/HtmlViewer`（通用框架）+ `AgentRuntimeUI.json`（声明单一源）
> **性质**：框架级重构方案（承接 8.24《HTML解析与UI编排通用框架提取方案》、8.26《Agent侧HTML解析逻辑通用框架化重构方案》的进一步增量；纳入 8.30「悬浮窗双轨」根因修复）
> **目标**：把 Agent.Launcher 中仍滞留的 HTML 渲染/浮层/编排逻辑全部下沉到 Plate.HtmlViewer 通用框架，Agent 侧仅保留「会话领域数据模型 + 编排」；依赖单向 Agent→Plate、Plate 零 Agent 依赖。

---

## 一、现状盘点

### 1.1 通用框架 Plate.HtmlViewer 能力已完备（复用资产，不新造轮子）
| 层 | 资产 |
|---|---|
| 解析 | `HtmlDeclarationParser`（域控件索引/ExtractHtmlPage/InjectData/Load）、`TrConfigFunctionInvoker`、`HtmlScriptBridge` |
| 渲染 | `HtmlUiProjector`、`HtmlLayoutProjector`、`HtmlControlFactory`、`HtmlPageRenderer`、`HtmlDocumentRenderer`、`HtmlTemplateEngine`、**`HtmlOverlay`（本轮新增）** |
| 运行时 | `HtmlBackendRuntime`、`HtmlEventBridge`、`HtmlScriptHost/Executor`、`HtmlViewer(IHtmlChannel)`、`DomOps` |
| 主题 | `IThemeProvider`/`DefaultThemeProvider`/`ConfigThemeProvider`/`ThemeDocument`/`ThemeMode` |
| 装配 | `HtmlUiAssembly`（组合根辅助，已接线 AttachHtmlUi + 模板 + 主题 + 声明） |

### 1.2 仍滞留 Agent.Launcher 的逻辑（下沉目标）
| 文件 | 滞留的非领域逻辑 |
|---|---|
| `ConversationPageRenderer.cs` | `Render`：flowShell 投影 + overlay 浮层拼接 + taskPaneShell 内嵌 + 三视图树/任务列表 |
| `HtmlConversationRenderer.cs` | `IConversationRenderer` 气泡渲染（round/message/tool-layer/prompt-layer 分组） |
| `ConversationViewService.cs` | `BuildTaskWindowHtmlAsync`（position:fixed 悬浮层）、`RenderFlow` 编排、`taskWindowHtml` 参数 |
| `ConversationTemplates.cs` | `Resolve()` 读 AgentUiHost 静态槽位 |
| `AgentUiHost.cs` | 15+ 静态槽位（MainDialogViewType/ConversationTemplates/ConversationRenderer/HtmlUi…） |

### 1.3 关键缺陷（驱动用例）
1. **悬浮窗双轨**：task 对话「右侧 taskPaneShell 内嵌」无条件执行；「overlay 悬浮窗」仅 selectTask 消息桥触发，且 `BuildTaskWindowHtmlAsync` 遇「无 Tool 产出 / provider 未装配 / 异常」**静默返回 null** → 悬浮窗从不出现，task 对话被塞进右侧主循环。
2. **静态槽位耦合**：渲染器/模板读 AgentUiHost 全局静态 → 无法脱离宿主在通用框架复用。
3. **命名空间分裂**：`UiBackend.Html` 与 `Plate.HtmlViewer` 并存。
4. **模板 DSL 自研**：`HtmlTemplateEngine` 承担全部渲染，未降为兼容层。

## 二、目标架构（四层，单向依赖）

```
声明层        AgentRuntimeUI.json  —— 唯一声明源（htmlPage / templates / theme）
                  │ 经 HtmlDeclarationParser（通用）
通用框架      Plate.HtmlViewer（统一命名空间 UiBackend.Html，收敛）
  解析   HtmlDeclarationParser · TrConfigFunctionInvoker · HtmlScriptBridge
  渲染   HtmlUiProjector · HtmlPageRenderer · HtmlFlowProjector · HtmlStreamRenderer · HtmlOverlay · HtmlDocumentRenderer · Theme
  运行时 HtmlBackendRuntime · HtmlEventBridge · HtmlScriptHost/Executor · HtmlViewer
  模板   HtmlTemplateEngine（兼容层）+ 页面原生/JS
Agent 领域    Agent.Launcher（仅领域数据模型 + 编排）
```

## 三、分阶段重构

### S1 会话页投影与消息流渲染下沉（核心）
- 新增 `Plate/HtmlViewer/Html/HtmlFlowProjector.cs`：承接 `ConversationPageRenderer.Render` 通用投影（flowShell 布局壳 + tree/history 注入）；三视图树/任务列表的数据模型由 Agent 提供，结构组装由模板承担（对齐 P3）。
- 新增 `Plate/HtmlViewer/Html/HtmlStreamRenderer.cs`：承接 `HtmlConversationRenderer` 通用渲染（round/message/tool-layer/prompt-layer 分组）；`IConversationRenderer` 保留在 Share 作抽象，通用实现移 Plate。
- Agent 侧：`ConversationPageRenderer` 删 C# 内嵌 DOM，只留领域数据模型构造；`HtmlConversationRenderer` 删除，改用 `HtmlStreamRenderer`。
- 验收：`agent_html_view_switch_test` 三视图逐字符一致；渲染器测试迁入 `Plate.Tests/HtmlViewer`。

### S2 浮层通用化 + 悬浮窗双轨消除（✅ 本轮已实施）
- 新增 `Plate/HtmlViewer/Html/HtmlOverlay.cs`：通用浮层构建器（position:fixed 浮层壳 + closeTask 桥协议），数据源由宿主注入。
- `BuildTaskWindowHtmlAsync` 改用 `HtmlOverlay.Build`；**消除「无 Tool 产出 → 静默 null」路径**：回退渲染 task 全量消息（含 user/assistant 主循环），仅 provider 未装配/无消息/异常才走零回归 null。
- 双轨取舍：默认右侧内嵌（taskPaneShell），悬浮窗仅在 selectTask 出现——二者不再互相掩盖，悬浮窗可靠显示。
- 验收：selectTask 时悬浮窗出现、closeTask 收起；无 Tool 产出时悬浮窗仍回退显示主循环。

### S3 运行时去静态化（R，最大）
- `AgentUiHost` 15+ 静态槽位收敛为 `AgentUiComposition` 实例快照；`AgentUiLibrary*`/`ConversationViewService`/渲染器改构造注入。
- `MainDialogViewType` 迁入组合根；`ConversationTemplates` 从静态 `Resolve()` 改实例。
- 单独立项，避免与 S1/S2 混改。

### S4 命名空间统一 + 模板/主题收尾（P4/P5）
- 统一命名空间 `UiBackend.Html`（`HtmlDocumentRenderer` 等迁移，兼容重载保留）。
- `AgentRuntimeUI.json` theme 结构化（tokens/light/dark/componentCss）；`HtmlTemplateEngine` 降安全 fallback。
- 验收：Light/Dark/System 三模式回归；改 JSON 即换版。

## 四、文件清单
| 文件 | 动作 | 阶段 |
|---|---|---|
| `Plate/HtmlViewer/Html/HtmlOverlay.cs` | ✅ 新增（通用浮层） | S2 |
| `Plate/HtmlViewer/Html/HtmlStreamRenderer.cs` | ✅ 新增（通用消息流渲染） | S1 |
| `Plate/HtmlViewer/HtmlViewer.csproj` | ✅ 引用 Share.Conversation 子项目 | S1 |
| `Agent.Launcher/Modules/ConversationViewService.cs` | ✅ 悬浮窗双轨消除 + 浮层下沉 + DefaultRenderer 通用化 | S2/S1 |
| `Agent.Launcher/Modules/HtmlConversationRenderer.cs` | ✅ 改为委托 HtmlStreamRenderer 薄壳 | S1 |
| `Plate/HtmlViewer/Html/HtmlFlowProjector.cs` | ✅ 新增（通用会话页布局壳投影） | S1 |
| `Agent.Launcher/Modules/ConversationPageRenderer.cs` | ✅ 通用投影改经 HtmlFlowProjector；删 RenderSelector/ViewTypeKey | S1 |
| `Agent.Launcher/Modules/ConversationViewService.cs` | ✅ RenderFlow 显式注入模板（渲染链去静态化） | S1/S3 |
| `Agent.Launcher/Modules/AgentUiHost.cs` / `AgentUiLibrary*.cs` / `Composition/*` | 去静态化 → AgentUiComposition | S3 |
| `FileDriven/DrivenConfig/JsonConfig/AgentRuntimeUI.json` | theme 结构化 | S4 |
| `CentralTest/.../Plate.Tests/HtmlViewer` | ✅ 新增 HtmlStreamRendererTests（10 例）；+ 待迁 HtmlFlowProjector 测试 | S1/S2 |

## 五、执行顺序与验收
1. **S1 → S2 → S3 → S4**；每阶段 `dotnet build CsharpPrj/MainProj.sln`、`dotnet build FileDriven/FileDriven.sln` + 对应测试全绿后交付；GUI 冒烟由宿主回归。
2. 最终验收：Agent 侧零 HTML 解析/渲染/浮层私有实现；Plate.HtmlViewer 零 Agent 依赖；改 `AgentRuntimeUI.json` 即改 UI。

## 六、风险与取舍
| 决策 | 取舍 |
|---|---|
| S1 渲染器下沉 | 需同步改模板契约 + 断言；换来"改版不改 C#"（对齐 P3） |
| S2 双轨消除 | 默认右侧内嵌 + selectTask 悬浮窗；悬浮窗改显式回退不再静默 |
| S3 全去静态化 | 波及 9 库注册 + 大量测试，独立立项（对齐既有 R 阶段） |
| S4 模板/主题 | 需迁移 AgentRuntimeUI.json；HtmlTemplateEngine 降兼容层 |

## 七、本轮已实施（S1 消息流渲染下沉 + S2 浮层下沉）

### S2 浮层通用化 + 悬浮窗双轨消除（✅ 已完成）
- ✅ `Plate/HtmlViewer/Html/HtmlOverlay.cs` 新增：通用浮层构建器（`Build(body, title, closeCmd, dataSession, widthCss, heightCss)`），承接 task-window 浮层壳与样式，零宿主依赖。
- ✅ `ConversationViewService.BuildTaskWindowHtmlAsync`：改经 `HtmlOverlay.Build` 构建浮层壳；消除「无 Tool 产出 → 返回 null」静默路径（回退渲染全量消息），保证 selectTask 悬浮窗可靠出现。

### S1 会话页投影与消息流渲染下沉（✅ 已完成）
- ✅ `Plate/HtmlViewer/Html/HtmlStreamRenderer.cs` 新增：通用消息流渲染器（`IConversationRenderer` 实现，round/message/tool-layer/prompt-layer 分组，模板经构造注入延迟 `Func` 源，缺模板走空态兜底）。
- ✅ `Plate/HtmlViewer/Html/HtmlFlowProjector.cs` 新增：通用会话页布局壳投影器（flowShell 布局壳 + 三视图 selector + overlay 拼接，纯模板、零领域类型依赖）；领域数据（`HistoryNode` 树、history 片段）由宿主 `ConversationPageRenderer` 构造后传入。
- ✅ `Plate/HtmlViewer/HtmlViewer.csproj`：只引用 `Share/Conversation/Share.Conversation.csproj` 纯逻辑子项目（避免聚合 Share 引入回 Plate 的循环依赖）。
- ✅ `ConversationViewService.DefaultRenderer` / `ConversationPageRenderer` 默认消息渲染器：改经 `HtmlStreamRenderer(() => ConversationTemplates.Resolve())`。
- ✅ `ConversationPageRenderer.Render`：删私有 `RenderSelector`/`ViewTypeKey`，改用 `HtmlFlowProjector`（同模板同引擎，逐字符零回归）。
- ✅ `ConversationViewService.RenderFlow`：显式传模板给 `ConversationPageRenderer`（渲染链去静态化第一步）。
- ✅ `Agent.Launcher/Modules/HtmlConversationRenderer.cs`：改为委托 `HtmlStreamRenderer` 的薄壳（测试兼容；下一步迁移测试后删除）。
- ✅ 新增 `Plate.Tests/HtmlViewer/HtmlStreamRendererTests.cs`（10 例）+ `HtmlFlowProjectorTests.cs`（4 例），全绿。

### S3 运行时去静态化（⏳ 渲染链已局部化；核心槽位待独立立项）
- ✅ 渲染链去静态化：`ConversationPageRenderer` 模板经构造注入（不再回退读 `AgentUiHost.ConversationTemplates`）；`RenderFlow` 显式传模板。
- ⏳ 完整 S3（`AgentUiHost` 15+ 静态槽位：会话服务/数据库/引用守卫/任务池/HistoryRuntime/HtmlUi/MainDialogViewType… → `AgentUiComposition` 实例注入）波及 9 库注册机制 + 大量测试，**独立大工程，需单独立项**，避免与渲染下沉混改（对齐既有方案 R 阶段）。

### 通用性重构收尾（本轮）
三个下沉组件不硬编码领域专用逻辑，全部参数化：
- `HtmlOverlay`：类名/data-name/关闭语义经 `HtmlOverlayOptions` 注入（默认中性 `floating-window`；Agent 注入 `task-window`/`closeTask`/`data-session` 保持零回归）。
- `HtmlFlowProjector`：切换器项（key/label）由宿主传入（不再硬编码 `node/task/history` 与中文标签）；`ViewTypeKey` 移回 Agent 领域层。
- `HtmlStreamRenderer`：头提示层前缀 `PromptStagePrefix` 配置化（默认 null 不启用；Agent 注入 `"prompt:"`），通用实现不硬编码领域前缀。
- 测试：新增 `HtmlOverlayTests`（3 例）、`HtmlStreamRenderer` prompt 前缀 2 例；`HtmlFlowProjectorTests` 改参数化签名。Plate.Tests/HtmlViewer 121/121、Agent.Core.Tests 渲染相关 22/22。

### 待做
- S3 完整去静态化（独立立项）、S4（命名空间统一 + theme 结构化 + HtmlTemplateEngine 降兼容层）。
