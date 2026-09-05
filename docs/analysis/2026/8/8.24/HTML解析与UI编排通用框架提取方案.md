# HTML 解析与 UI 编排逻辑的通用框架提取方案

> **版本**：1.0
> **日期**：2026-08-24
> **范围**：`Projects/Agent/src/Agent.Launcher/Modules`（Agent 侧 HTML 解析/UI 编排）→ 下沉 `CsharpPrj/Plate/HtmlViewer`（通用框架）+ `AgentRuntimeUI.json`（声明合并）
> **性质**：重构方案文档（本文为设计，不随附实施）
> **目标**：把 Agent.Launcher/Modules 中散落的 HTML 解析逻辑（声明解析/脚本命令解析/控件索引/注册表函数调用）与 UI 编排逻辑（布局投影 + 数据注入/事件分发/脚本执行）提取为 Plate.HtmlViewer 下的通用框架；`ConversationPage.json` 不再单独解析，并入 `AgentRuntimeUI.json` 经统一配置加载链路自动解析。**复用优先：全部复用现有实现，不做新轮子。**

---

## 一、现状与痛点

### 1.1 逻辑分布盘点

| 逻辑 | 现状位置 | 归属判定 |
|---|---|---|
| HTML 控件工厂 / 布局投影 / 值契约 / 事件桥 / 脚本桥 | `Plate/HtmlViewer/Html/`（HtmlControlFactory、HtmlLayoutProjector、HtmlUiProjector、HtmlElementNode、HtmlEventBridge、HtmlScriptBridge、IDomBridge） | ✅ 已通用（复用） |
| 声明反序列化（JSON→UIConfiguration） | 散落：`AgentUiLibraryHtml.HtmlRenderAsync`（默认 options）、`ConversationPageRenderer.LoadPageLayout`（嵌入资源）、FileDriven `UnifiedConfigLoadAdapter`（统一加载，case-insensitive） | ❌ 三处三份 |
| 控件树 → name 索引 | `HtmlUiRuntime.BuildControlIndex/IndexControls` | ❌ Agent 侧私有 |
| 脚本 JSON → 命令列表（含 template 展开） | `AgentUiLibraryHtml.ParseScriptCommands` | ❌ Agent 侧私有 |
| 注册表函数调用（形参装配 + 类型转换 + Task 解包） | `AgentUiLibraryHtml.CreateInvoker/BuildArguments/ConvertValue/UnwrapAsync` | ❌ Agent 侧私有 |
| 布局壳 + 数据注入（占位替换） | `ConversationPageRenderer.RenderPage` + `LoadPageLayout` | ❌ Agent 侧私有 + 独立资源 |
| 会话数据提供者（task 列表 / 历史气泡） | `ConversationPageRenderer.RenderTaskList`、`HtmlConversationRenderer` | ✅ Agent 领域数据（保留） |
| 运行时装配（桥 + 事件桥 + invoker） | `HtmlUiRuntime` + `AgentUiHost.HtmlUi` 槽位 | ⚠️ 已定义，**无宿主调用 `AttachHtmlUi`**（能力孤岛） |

### 1.2 痛点

1. **`ConversationPage.json` 单独解析**：会话页内部布局声明作为嵌入资源（`Agent.Launcher/Resources/ConversationPage.json`）独立加载、独立反序列化，与 `AgentRuntimeUI.json`（已声明会话页控件树 conversationSidebar/taskListViewer/conversationHistoryViewer）是**两份声明**，改布局要改两个文件。
2. **通用解析逻辑滞留在 Agent 侧**：`BuildControlIndex`/`ParseScriptCommands`/`CreateInvoker` 均为 Agent.Launcher 内部实现，其他宿主（FileDriven UI、无头测试、后续 WPF/Web 宿主）不可复用。
3. **反序列化 options 不统一**：`AgentUiLibraryHtml.HtmlRenderAsync` 用 `JsonSerializer` 默认 options（大小写敏感），`UnifiedConfigLoadAdapter`/`JsonConfigService` 用 `PropertyNameCaseInsensitive=true`——前者重蹈「小写 JSON 属性绑不上 PascalCase CLR 属性 → 空控件树」陷阱（ConversationPageRenderer 已踩过并修复）。
4. **能力孤岛**：`HtmlUiRuntime`/`AttachHtmlUi` 无装配点，事件桥/脚本桥在当前宿主链路未生效，等于「框架已建、未接线」。

---

## 二、可复用资产盘点（复用优先，不新造轮子）

| 资产 | 位置 | 复用方式 |
|---|---|---|
| `TConfig.UIConfiguration`/`ControlConfig`/`EventBinding`（声明模型） | `CsharpPrj/Plate/TConfig` | 原样复用（唯一声明源） |
| `UnifiedConfigLoadAdapter.LoadAsync`（统一配置加载，case-insensitive + 缓存） | `FileDriven/FileDriven.Execution/Initial/UnifiedConfigLoadAdapter.cs` | **`ConversationPage.json` 合并后，会话页声明经此链路自动解析**（复用现成加载器，不再自建反序列化） |
| `HtmlUiProjector.Project(UIConfiguration)`（声明 → 布局壳 HTML + 主题 CSS） | `Plate/HtmlViewer/Html/HtmlUiProjector.cs` | 原样复用（投影入口） |
| `HtmlLayoutProjector`（TConfig.LayoutPosition → 像素 CSS，对齐 WinForms 语义） | `Plate/HtmlViewer/Html/HtmlLayoutProjector.cs` | 原样复用 |
| `HtmlControlFactory`（type → DOM 模板） | `Plate/HtmlViewer/Html/HtmlControlFactory.cs` | 原样复用 |
| `HtmlEventBridge` / `HtmlScriptBridge` / `HtmlElementNode` / `IDomBridge` | `Plate/HtmlViewer/Html/` | 原样复用（事件分发 / 脚本执行 / 协议节点） |
| `ITrConfigRegistry`（函数注册表：TryGetConfig/Invoke/Parameters） | `CsharpPrj/Plate/TConfig/TrConfig/` | 复用为通用函数调用端口 |
| `IConversationRenderer` / `HtmlConversationRenderer`（气泡渲染） | `Share/Conversation` / `Agent.Launcher` | 保留（会话领域数据渲染） |

---

## 三、目标架构（三层）

```
声明层（已有，复用）
  AgentRuntimeUI.json ── 唯一声明源：会话页控件树 + htmlviewer 内部布局声明（内嵌，非独立文件）
        │  经 UnifiedConfigLoadAdapter 自动解析（case-insensitive，单一 options）
        ▼
HTML 渲染内核（Plate.HtmlViewer，已有）
  HtmlControlFactory / HtmlLayoutProjector / HtmlUiProjector（声明 → HTML/CSS）
  HtmlEventBridge / HtmlScriptBridge / HtmlElementNode / IDomBridge（值/事件/脚本协议）
        │
        ▼
通用编排框架（Plate.HtmlViewer 新增，本文核心：从 Agent.Launcher/Modules 下沉）
  HtmlDeclarationParser   声明解析：统一反序列化 + 控件索引 + 内部声明提取 + 占位注入
  TrConfigFunctionInvoker 注册表函数调用：形参装配 → 类型转换 → Task 解包
  HtmlPageRenderer        页面编排：声明投影 + 数据注入（布局壳由声明决定，C# 只供数据）
        │
        ▼
Agent 侧（收敛为薄层）
  HtmlUiRuntime        装配：IDomBridge + 声明源 + invoker（宿主接线）
  AgentUiLibraryHtml   函数注册薄封装（AgentUi_HtmlRender/HtmlEventDispatch/HtmlScript）
  ConversationPageRenderer → 数据提供者（RenderTaskList / RenderHistory）+ 消费 HtmlPageRenderer
```

分层职责不变（复用方案 §3.1）：声明层不感知后端；渲染内核把声明物化为 HTML；编排框架提供跨宿主通用的「解析 → 投影 → 注入 → 执行」组合；Agent 侧只保留领域数据与装配。

---

## 四、通用框架提取方案（下沉 Plate.HtmlViewer）

> 三个新组件全部为**现有 Agent 侧实现的搬运 + 去 Agent 依赖**，不新增语义。

### 4.1 `HtmlDeclarationParser`（静态类，`Plate/HtmlViewer/Html/`）

从 `HtmlUiRuntime.BuildControlIndex`、`ConversationPageRenderer.LoadPageLayout`、`AgentUiLibraryHtml` 的零散解析下沉，统一 options：

```csharp
namespace UiBackend.Html;

public static class HtmlDeclarationParser
{
    /// <summary>声明反序列化统一 options（PropertyNameCaseInsensitive=true——JSON 小写属性 ↔ CLR PascalCase 的唯一绑定点）。</summary>
    public static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>JSON → UIConfiguration（统一 options；null/空 → null）。</summary>
    public static UIConfiguration? Parse(string json);

    /// <summary>控件树 → name 索引（递归全部 view，大小写不敏感）。[搬运自 HtmlUiRuntime.BuildControlIndex]</summary>
    public static IReadOnlyDictionary<string, ControlConfig> BuildControlIndex(UIConfiguration config);

    /// <summary>取 htmlviewer 控件内嵌的内部页面声明（§5 新增字段 HtmlPageDeclaration）；无 → null。</summary>
    public static UIConfiguration? ExtractHtmlPage(ControlConfig host, string pageName);

    /// <summary>占位注入：把 {name} → 数据片段替换进投影产物（布局壳由声明决定，C# 只供数据）。[搬运自 ConversationPageRenderer.RenderPage 的 Replace 逻辑]</summary>
    public static string InjectData(string bodyHtml, IReadOnlyDictionary<string, string> data);
}
```

### 4.2 `TrConfigFunctionInvoker`（类，`Plate/HtmlViewer/Html/`）

从 `AgentUiLibraryHtml.CreateInvoker/BuildArguments/ConvertValue/UnwrapAsync` 下沉，依赖收敛为 `ITrConfigRegistry`（TConfig，目标无关）：

```csharp
namespace UiBackend.Html;

/// <summary>注册表函数调用器——ITrConfigRegistry → HtmlEventBridge.HtmlFunctionInvoker 的统一实现：
/// 按形参名装配参数（CancellationToken 剔除/回填）→ 基础类型转换 → ITrConfig.Invoke → Task 解包为结果字符串。
/// [搬运自 AgentUiLibraryHtml.CreateInvoker 全链路]</summary>
public sealed class TrConfigFunctionInvoker
{
    public TrConfigFunctionInvoker(ITrConfigRegistry registry);
    public Task<string> InvokeAsync(string functionName, IReadOnlyDictionary<string, object?> parameters);
    public static HtmlEventBridge.HtmlFunctionInvoker ToBridgeInvoker(ITrConfigRegistry registry); // 适配事件桥/脚本桥端口
}
```

### 4.3 `HtmlPageRenderer`（类，`Plate/HtmlViewer/Html/`）

合并 `ConversationPageRenderer.LoadPageLayout + RenderPage` 的通用部分（布局投影 + 数据注入），宿主注入「声明源」：

```csharp
namespace UiBackend.Html;

/// <summary>页面渲染器——由「声明源（UIConfiguration 或内嵌内部页面声明）」投影布局壳 + 注入数据。
/// 布局结构与占位由 JSON 决定，C# 只产数据（数据提供者模式）。[合并自 ConversationPageRenderer 通用部分]</summary>
public sealed class HtmlPageRenderer
{
    public HtmlPageRenderer(UIConfiguration declaration);              // 整体声明
    public HtmlPageRenderer(ControlConfig host, string pageName);      // htmlviewer 内嵌内部页面声明（§5）
    public string Render(IReadOnlyDictionary<string, string> data);    // 投影（缓存）+ 占位注入 → 布局壳 HTML（含主题 style）
    public HtmlRenderResult Project();                                 // 布局壳（含 BaseCss）
}
```

### 4.4 Agent 侧收敛后（保留 / 下沉 / 删除清单）

| 成员 | 动作 |
|---|---|
| `AgentUiLibraryHtml.HtmlRenderAsync` | 改：`HtmlDeclarationParser.Parse` + `HtmlUiProjector.Project`（修复 case-insensitive） |
| `AgentUiLibraryHtml.HtmlScriptAsync` | 保留注册，`ParseScriptCommands` 下沉为 `HtmlScriptBridge` 的静态展开助手（或保留薄包装） |
| `AgentUiLibraryHtml.CreateInvoker/BuildArguments/ConvertValue/UnwrapAsync` | **删除** → 复用 `TrConfigFunctionInvoker` |
| `HtmlUiRuntime.BuildControlIndex/IndexControls` | **删除** → 复用 `HtmlDeclarationParser.BuildControlIndex` |
| `ConversationPageRenderer.LoadPageLayout` | **删除** → 复用 `HtmlPageRenderer`（声明源 = AgentRuntimeUI.json 自动解析） |
| `ConversationPageRenderer.RenderPage` | 改：消费 `HtmlPageRenderer.Render(data)`，data = { taskList, history } |
| `ConversationPageRenderer.RenderTaskList` | 保留（会话数据提供者） |
| `Resources/ConversationPage.json` | **删除**（声明并入 AgentRuntimeUI.json，§5） |
| `HtmlUiRuntime` | 保留（Agent 装配语义），构造参数改为「桥 + 声明源 + `TrConfigFunctionInvoker`」 |

---

## 五、声明合并：`ConversationPage.json` → `AgentRuntimeUI.json`

### 5.1 承载方式：`ControlConfig` 新增可选字段 `HtmlPageDeclaration`

`ConversationPage.json` 描述的是 **htmlviewer 控件内部页面的布局**（与 WinForms 物理控件树不同层）。合并进 `AgentRuntimeUI.json` 的最自然承载点 = htmlviewer 控件自身内嵌「内部页面声明」：

`CsharpPrj/Plate/TConfig/Models/ControlConfig.cs` 新增（可选、向后兼容，缺省 null 不影响既有声明）：

```csharp
/// <summary>htmlviewer/markdownviewer 内部页面布局声明（JSON 控件树，经 HTML 后端投影；
/// 缺省 null = 无内部声明，控件仅作富文本宿主）。</summary>
[JsonPropertyName("htmlPage")]
public HtmlPageDeclaration? HtmlPage { get; set; }
```

```csharp
/// <summary>HTML 内部页面声明：htmlviewer 内部布局 = 控件树 + 数据占位（{name} 注入点）。</summary>
public sealed class HtmlPageDeclaration
{
    public string? Name { get; set; }
    public List<ControlConfig>? Controls { get; set; }
}
```

> 复用 `ControlConfig` 自身作为声明元素——`HtmlPageDeclaration.Controls` 的元素就是 `ControlConfig`（HtmlControlFactory/HtmlLayoutProjector 直接消费，零新渲染语义）。

### 5.2 `AgentRuntimeUI.json` 会话页内嵌声明（示例，替代 ConversationPage.json）

```json
{
  "type": "htmlviewer",
  "name": "conversationHistoryViewer",
  "toolTip": "交互式会话页：内部布局由 htmlPage 声明（改本 JSON 即改布局）",
  "events": { "message": "AgentUi_ConversationMessageDispatch", "bindings": { ... } },
  "htmlPage": {
    "name": "conversationPage",
    "controls": [
      {
        "type": "panel", "name": "sidebar",
        "layout": { "left": 0.01, "top": 0.02, "width": 0.22, "height": 0.96 },
        "controls": [
          { "type": "label", "name": "sidebarTitle", "text": "会话 / 任务",
            "layout": { "left": 0, "top": 0, "width": 1, "height": 0.05 } },
          { "type": "htmlviewer", "name": "taskListViewer", "text": "{taskList}",
            "layout": { "left": 0, "top": 0.07, "width": 1, "height": 0.93 } }
        ]
      },
      { "type": "htmlviewer", "name": "conversationHistoryViewer", "text": "{history}",
        "layout": { "left": 0.25, "top": 0.02, "width": 0.74, "height": 0.96 } }
    ]
  },
  "layout": { "left": 0.25, "top": 0.04, "width": 0.73, "height": 0.62 }
}
```

### 5.3 自动解析链路（复用统一配置加载，不再单独解析）

```
load AgentRuntimeUI.json（sideload/initCommands）
  └─ UnifiedConfigLoadAdapter.LoadAsync（复用：case-insensitive + 缓存）
       └─ UIConfiguration（含 conversationHistoryViewer.HtmlPage）
            └─ HtmlUiRuntime 装配：HtmlPageRenderer(conversationHistoryViewer.HtmlPage)
                 └─ Render({ taskList, history }) → htmlviewer 内容（数据由会话数据提供者产出）
```

- 会话页内部布局声明随 `AgentRuntimeUI.json` **一次性自动解析**，宿主加载配置后即可从控件树提取（`HtmlDeclarationParser.ExtractHtmlPage`）。
- 删除：`Resources/ConversationPage.json`、`ConversationPageRenderer.LoadPageLayout`（嵌入资源单独反序列化）——**单一声明源、单一加载链**。

---

## 六、迁移步骤（每步独立可交付，复用为主）

### 步骤 1：下沉通用组件到 Plate.HtmlViewer
- 新增 `HtmlDeclarationParser`（搬运 BuildControlIndex + 统一 Parse/JsonOpts + InjectData）
- 新增 `TrConfigFunctionInvoker`（搬运 CreateInvoker 全链路）
- 单测：`Plate.Tests/HtmlViewer` 新增 `HtmlDeclarationParserTests` / `TrConfigFunctionInvokerTests`（迁移既有 `HtmlUiRuntime` 相关断言）

### 步骤 2：Agent 侧收敛为薄层
- `AgentUiLibraryHtml` 改用 `HtmlDeclarationParser.Parse`（修复 case-insensitive）+ `TrConfigFunctionInvoker`；删除私有 `CreateInvoker/BuildArguments/ConvertValue/UnwrapAsync`
- `HtmlUiRuntime` 构造改为消费 `HtmlDeclarationParser` / `TrConfigFunctionInvoker`；删除私有 `BuildControlIndex`
- 回归：`Agent.Core.Tests` + `Plate.Tests/HtmlViewer` 绿

### 步骤 3：声明合并
- `ControlConfig` 新增 `HtmlPageDeclaration`（TConfig 模型扩展）
- `AgentRuntimeUI.json` 会话页内嵌 `htmlPage` 声明（内容自 ConversationPage.json 平移）；删除 `Resources/ConversationPage.json` 与 csproj 的 EmbeddedResource
- `ConversationPageRenderer.RenderPage` 改为接收「已解析的 HtmlPage 声明」或经统一加载器获取（不再自建反序列化）
- 单测：`Agent.Core.Tests` 会话页断言保持（输出形态不变：ui-root + data-name + 数据注入）

### 步骤 4：HtmlUiRuntime 宿主接线
- 在 Agent 会话页组合根（宿主创建 htmlviewer 后）调用 `AgentUiHost.AttachHtmlUi(new HtmlUiRuntime(htmlViewerDomBridge, 声明源, TrConfigFunctionInvoker))`
- 使 `AgentUi_HtmlEventDispatch` / `AgentUi_HtmlScript` 在运行态生效（消除能力孤岛）

### 步骤 5：回归验证
- `dotnet build CsharpPrj/MainProj.sln`、`dotnet build FileDriven/FileDriven.sln`
- `dotnet test CentralTest/CSharpTest/Plate.Tests/HtmlViewer` + `CentralTest/AgentTest/Agent.Core.Tests`

---

## 七、验收标准

1. **声明单一**：会话页布局只存在于 `AgentRuntimeUI.json`（htmlPage 内嵌），`ConversationPage.json` 删除；改布局只改 JSON。
2. **解析统一**：全部 `UIConfiguration` 反序列化经 `HtmlDeclarationParser.JsonOpts`（case-insensitive）或 `UnifiedConfigLoadAdapter`，无第三处 options。
3. **框架通用**：`HtmlDeclarationParser` / `TrConfigFunctionInvoker` / `HtmlPageRenderer` 位于 Plate.HtmlViewer，零 Agent 依赖；Agent.Launcher/Modules 无 HTML 解析/函数调用私有实现。
4. **能力接线**：`AttachHtmlUi` 有宿主调用点，`AgentUi_HtmlEventDispatch`/`AgentUi_HtmlScript` 运行态可用（对齐 P2/P3 验收）。
5. **回归**：会话页渲染输出形态不变（ui-root + data-name + {taskList}/{history} 注入），既有测试全绿。

---

## 八、风险与取舍

| 风险 | 应对 |
|---|---|
| TConfig 模型扩展（ControlConfig.HtmlPage）影响既有声明 | 可选字段缺省 null，向后兼容；校验器/契约不感知时零影响 |
| 内嵌声明使 AgentRuntimeUI.json 变大 | 会话页是唯一使用方；替代方案（独立 `htmlPage` 文件 + `container` 引用）作为备选，但违背「不单独解析」诉求 |
| 下沉后 Agent 侧行为漂移 | 全部为搬运（BuildControlIndex/CreateInvoker/Replace），单测先行固化既有断言再迁移 |
| HtmlUiRuntime 接线面不明 | 步骤 4 独立交付；接线前事件桥/脚本桥以单测 + `AgentUi_HtmlScript` 手动冒烟覆盖 |

---

## 九、附：本轮已复用资产对照（不新造轮子）

| 需求 | 复用来源 |
|---|---|
| 布局换算（比例/锚定/fixed） | `HtmlLayoutProjector.ResolveRect`（对齐 WinForms ControlFactory 语义，既有） |
| 声明 → HTML | `HtmlUiProjector` / `HtmlControlFactory`（既有） |
| 事件分发 / 脚本执行 | `HtmlEventBridge` / `HtmlScriptBridge` / `HtmlElementNode` / `IDomBridge`（既有） |
| 统一配置加载 | `UnifiedConfigLoadAdapter` / `JsonConfigService`（FileDriven 既有） |
| 函数调用 | `ITrConfigRegistry.TryGetConfig/Invoke`（TConfig 既有，`TrConfigFunctionInvoker` 仅作适配） |
| 会话数据渲染 | `IConversationRenderer` / `HtmlConversationRenderer`（既有，保留 Agent 侧） |
