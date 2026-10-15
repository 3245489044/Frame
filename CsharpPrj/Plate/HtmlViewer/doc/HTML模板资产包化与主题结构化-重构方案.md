# HTML 模板资产包化与主题结构化 · 重构方案

> **定位**：把 `AgentRuntimeUI.json` 中"超长内联 HTML/CSS"（`templates` 段）重构为 **HtmlViewer 通用框架能力**：
> 框架只新增「**具名资产包（pack）**」机制（引用 / 合并 / 覆盖 + 主题结构化），**内容按归属分层**（框架基础 / 领域包 / 每配置声明）。
> **口径**：全部落点经本仓库**实测**（标 `文件:行`）；量化数据由脚本实测得出（见 §0.1）。
> **上游**：`Plug/Agent/src/Agent.Assembly/COMPOSITION_FRAMEWORK_DESIGN.md`（HTML 组装收敛主张）、`Projects/Information/DrivenConfig/JsonUI构建文档.md`（JSON UI 配置总览）。
> **既有前例**：`Frame/CsharpPrj/BasicMethod/JsonProcess/JsonUIBeautifier/src/Refactor/ThemeExtractor.cs`（"主题抽取为结构化对象"同思路，服务 WinForms 侧，非本方案实现）。

---

## §0 诊断结论

### 0.1 量化：这个 JSON 到底"长"在哪

源文件 `Projects/Information/DrivenConfig/JsonConfig/AgentRuntimeUI.json`：**421 行 / 38 234 字节**，"超长"部分**全部集中在根级 `templates`**：

| 键 | 字符数 | 性质 |
|---|---|---|
| `theme` | **12 359** | 整段 CSS（浅/深 token + 基础气泡样式 + 会话页 + `db-*` + composer + 运行态徽章） |
| `message` | 1 076 | 气泡消息片段 |
| `taskItem` | 776 | 任务项按钮（6 个条件位） |
| `operationPanel` | 565 | 含内联 `style` 的操作面板 |
| `composer` | 485 | 输入区 |
| `historyItem` | 480 | 历史 → 任务两级树 |
| `flowShell` | 418 | 布局壳 |
| `taskRuntimeRow` | 366 | 运行态行 |
| `taskNodeRow` | 322 | 节点行 |
| `taskNodeGroup` | 262 | 节点分组 |
| `htmlShell` | 247 | **完整文档壳** |
| `taskNodesPanel` | 223 | 节点面板 |
| `round` | 179 | 轮次容器 |
| `attachItem` | 177 | 附着项 |
| `selectorItem` | 151 | 视图切换项 |
| 其余 6 项 | 27–96 | `taskRuntimePanel` 96 / `taskPaneShell` 92 / `taskRuntimeEmpty` 45 / `viewSelector` 35 / `emptyDiv` 31 / `emptyP` 27 |

> `theme` 一项占全文件 **32%**，是"超长 HTML"的主体；`htmlShell`+`emptyDiv`+`emptyP`+`viewSelector`+`selectorItem`+`composer` 合计 972 字符属**框架通用形态**。

### 0.2 四类问题（均有实证）

| # | 问题 | 一句话 | 证据 |
|---|---|---|---|
| **P1** | **基础 CSS 逐字复制 → 静默漂移** | JSON `theme` 开头 ≈**2.2KB（归一化）** 与框架内嵌 `DefaultCss` **同形**；而框架装配点**已经**在做"基础 + 应用"合并 | `HtmlAssetRegistry.cs:288-366`（DefaultCss）；`HtmlBridgeAssembly.cs:137-145`（`baseCss + "\n" + themeCss`）；`HtmlAssetRegistry.cs:361-365` 注释本身即警告"避免两份逐字复制漂移" |
| **P2** | **通用片段被复制进每个配置 → 第二实现** | `htmlShell`/`emptyDiv`/`emptyP`/`viewSelector`/`selectorItem`/`composer` 在框架已有**同名兜底资产**与**对应投影器** | `HtmlAssetKeys.EmptyDivTemplate/EmptyPTemplate`（`IHtmlAssetProvider.cs:25-26`）；`HtmlEmptyState.cs:14/17`；`HtmlComposerProjector.cs:20`（`TemplateName = "composer"`）；`HtmlFlowProjector.cs:16/43/65` |
| **P3** | **领域片段与配置耦合 → 无法复用/独立演化** | Agent 会话页片段（`message/round/taskItem/...`）硬编码在单份配置里，换皮肤要整体复制；键契约只能覆盖这一份 JSON | `HtmlKeyContract.cs:47`（`ValidateAll` 只吃 JSON `templates`）；`Check-AgentRuntimeUIConfig.ps1:276-281`（同父键冲突只扫 JSON `templates`） |
| **P4** | **主题是"裸 CSS 字符串" → 结构语义丢失** | 框架已备好结构化 IR（浅/深 token + 组件 CSS），当前装配方只用 `componentCss:` 单参塞整段裸 CSS | `ThemeDocument.cs:13-17`（`TokensLight/TokensDark/ComponentCss/CssFiles`）；`ConfigThemeProvider.cs:19-26`（四参构造器已就绪）；`HtmlBridgeAssembly.cs:144`（只用单参） |

### 0.3 决定性证据：框架的"机制"早就在，缺的是"内容分层"

| 机制 | 定义 | 现状 |
|---|---|---|
| 具名资产接口 | `IHtmlAssetProvider` / `HtmlAssetKeys`（`IHtmlAssetProvider.cs:37-46`） | 接口注释已写明"**可替换为外部 .js/.css 资源或 JSON 声明（运行时按 key 解析）**"——**未实现** |
| 内嵌资产注册表 | `HtmlAssetRegistry`（`HtmlAssetRegistry.cs:14-42`） | 已承载 4 脚本 / 5 模板 / 2 样式，**命名空间齐备** |
| 文档壳槽 | `HtmlDocumentRenderer.AttachShell`（`HtmlDocumentRenderer.cs:98`） | 已消费 `htmlShell`，且只认"完整文档壳" |
| 主题 IR + IoC | `ThemeDocument` / `IThemeProvider` / `HtmlTheme.AttachProvider`（`HtmlTheme.cs:59`） | 齐备，未用满 |
| 通用投影器 | `HtmlFlowProjector` / `HtmlComposerProjector` / `HtmlGroupedListProjector` / `HtmlEmptyState` / `HtmlRunStateBadgeProjector` / `HtmlSectionListView` | **已通用化**（零领域类型依赖） |
| 装配/合并/校验点 | `HtmlBridgeAssembly.AttachTemplatesAndTheme`（`HtmlBridgeAssembly.cs:121-151`） | **单一汇合点**，改动面最窄 |

> ⇒ 结论：本重构**不是**把整段 HTML 搬进 `Plate.HtmlViewer`，而是**在既有扩展点补上"具名资产包 + 引用/合并/覆盖"这一层机制**，让内容按归属分层。若把 Agent 域 CSS/HTML 直接塞进 plate，将违背其"**纯渲染基础设施（零上层依赖）**"定位（`HtmlViewer.csproj:10` 与 DIP 注释 `HtmlViewer.csproj:24-26`）。

---

## §1 目标与非目标

### 1.1 目标

| # | 目标 | 判据 |
|---|---|---|
| **G1** | 消除 P1 漂移源 | JSON `theme` 中不再出现与 `DefaultCss` 同形的规则 |
| **G2** | 通用片段单点化（P2） | `htmlShell/emptyDiv/emptyP/viewSelector/selectorItem/composer` 仅存在于框架包 `plateBase` |
| **G3** | 领域内容可独立演化（P3） | Agent 域片段/域 CSS 外提到 `agentConversation` 包，不在 JSON 内联 |
| **G4** | 主题结构化（P4） | JSON 用 `themeSpec.tokensLight/tokensDark` 声明 token，域 CSS 走 `componentPack` |
| **G5** | 键契约**不退化** | 校验对象 = **解析后**模板集（包 + JSON 覆盖），门禁脚本同步扫描包 |
| **G6** | 配置显著减重 | `AgentRuntimeUI.json` 38 234 B → **≤ 8 KB** |

### 1.2 非目标

- 不改模板引擎语法（`{field}` / `{field|raw}` / `{#each}` / `{?cond}` / `{>name}`）。
- 不改 `htmlPage` 声明式布局与 `InjectData` 占位注入链路（`HtmlDeclarationParser.cs:187`）。
- 不改消息桥（`data-cmd` / `data-input` / `postMessage`）与写回语义（`writeback`）。
- 不把 Agent 领域内容放入 `Plate.HtmlViewer`。
- 不动 `templates` 之外的 JSON 结构（`form` / `views` / `events` / `bindings`）。

---

## §2 目标架构

### 2.1 三层内容归属（核心）

```
┌────────────────────────────────────────────────────────────────────┐
│ L1 框架内嵌包  plateBase（Plate.HtmlViewer / HtmlAssetRegistry）     │
│     htmlShell · emptyDiv · emptyP · viewSelector · selectorItem    │
│     · composer（默认形态）                                          │
├────────────────────────────────────────────────────────────────────┤
│ L2 领域包  agentConversation（内容不落 plate）                       │
│    选项 A（默认）配置侧资源：DrivenConfig/Assets/<pack>/*.html|*.css │
│    选项 B（备选）插件侧嵌入：Agent.Ui 程序集嵌入资源                  │
├────────────────────────────────────────────────────────────────────┤
│ L3 每配置声明  AgentRuntimeUI.json                                  │
│     templatePacks（引用）· themeSpec（token 表）· templates（覆盖项） │
│     + form / views / events / bindings（布局与绑定，原样保留）        │
└────────────────────────────────────────────────────────────────────┘
```

### 2.2 解析与优先级（单一入口）

```
HtmlPackageResolver.Resolve(packs, jsonTemplates)
    优先级（高 → 低）：jsonTemplates（显式） > 包链后项 > 包链前项 > 框架内嵌兜底
    产出：合成模板字典（唯一交给 AttachTemplates / HtmlKeyContract 的对象）
```

- 「JSON 覆盖包」与既有语义天然吻合：`HtmlEmptyState` 已是"缺 JSON → 回退内嵌资产"（`HtmlEmptyState.cs:34-37`）。
- 主题：`themeSpec` → `ConfigThemeProvider(tokensLight, tokensDark, componentCss: 包 CSS)` → `HtmlTheme.AttachProvider`；旧 `templates.theme`（裸 CSS）路径**保留**，零回归。

### 2.3 装配点改造（唯一改动面）

`HtmlBridgeAssembly.AttachTemplatesAndTheme`（`HtmlBridgeAssembly.cs:121-151`）当前顺序：

```
① host.AttachTemplates(templates)          ← 改为 AttachTemplates(resolved)
② HtmlKeyContract.ValidateAll(templates)   ← 改为校验 resolved（键契约覆盖不退化）
③ theme (templates["theme"]) → ConfigThemeProvider → HtmlTheme.AttachProvider
④ templates["htmlShell"] → HtmlDocumentRenderer.AttachShell
```

改造后：

```
① packs    ← LoadPacks(configPath)                          // 新增：读 templatePacks + 资源目录
② resolved ← HtmlPackageResolver.Resolve(packs, templates)   // 新增
③ host.AttachTemplates(resolved)                            // 原样
④ HtmlKeyContract.ValidateAll(resolved)                     // 原样（对象换成 resolved）
⑤ themeSpec 优先 / theme 兼容 → 主题装配                      // 新增 themeSpec 分支
⑥ htmlShell 装配（包未提供则回退框架内置壳）                   // 原样
```

### 2.4 装配时序（沿用既有约定）

`AgentHtmlHosting.AttachTemplatesAndThemeFromConfigAsync`（`AgentHtmlHosting.cs:62-63`）→ `HtmlBridgeAssembly.AttachTemplatesAndThemeFromConfigAsync`（`HtmlBridgeAssembly.cs:158`）→ `LoadTemplatesAsync`（`HtmlBridgeAssembly.cs:171-176`，经 `JsonPathHelper.ReadStringMap(node, "$.templates")` 仅取 templates 段，与 UI 结构反序列化解耦）。
**包解析插入 `AttachTemplatesAndTheme` 内部**，因此宿主接线代码**零改动**。


---

## §3 框架新增件（接口草案）

> 命名空间 / 目录对齐既有：`UiBackend.Html` + `Frame/CsharpPrj/Plate/HtmlViewer/Html/Assets/`。

### 3.1 新增文件

| 文件 | 职责 |
|---|---|
| `Html/Assets/IHtmlAssetPack.cs` | 具名包契约 |
| `Html/Assets/HtmlPackageResolver.cs` | **唯一解析入口**：合成 + 覆盖 |
| `Html/Assets/FileHtmlAssetPack.cs` | 从目录装载（选项 A：配置侧资源） |
| `Html/Assets/EmbeddedHtmlAssetPack.cs` | 从程序集嵌入资源装载（选项 B：插件侧资源） |

```csharp
#nullable enable

namespace UiBackend.Html;

/// <summary>
/// 具名资产包（pack）——"框架基础 / 领域视图"两类 HTML 片段与 CSS 的可装载单元。
/// 把「HTML 写进每份 JSON」升级为「JSON 引用具名包 + 只写覆盖项」，消除复制与漂移。
/// </summary>
public interface IHtmlAssetPack
{
    /// <summary>包名（JSON <c>templatePacks</c> 引用键；大小写不敏感）。</summary>
    string Name { get; }

    /// <summary>包内模板集（键 = 模板名，值 = 引擎语法模板串）。</summary>
    IReadOnlyDictionary<string, string> Templates { get; }

    /// <summary>包内 CSS 片段（无则空串；供 themeSpec.componentPack 取用）。</summary>
    string Css { get; }
}

/// <summary>
/// 资产包解析器——把「包链 + JSON 显式模板」合成为唯一模板字典。
/// 优先级（高 → 低）：jsonTemplates &gt; 包链后项 &gt; 包链前项。纯函数、零宿主依赖。
/// </summary>
public static class HtmlPackageResolver
{
    /// <summary>合成模板集（<paramref name="packs"/> 按声明序合并，后者覆盖前者；JSON 显式键最终覆盖）。</summary>
    public static IReadOnlyDictionary<string, string> Resolve(
        IReadOnlyList<IHtmlAssetPack>? packs,
        IReadOnlyDictionary<string, string>? jsonTemplates);

    /// <summary>取包链合成 CSS（用于 themeSpec.componentPack；多包则按序拼接）。</summary>
    public static string ResolveCss(IReadOnlyList<IHtmlAssetPack>? packs, string? packName);
}

/// <summary>目录装载包：<c>*.html</c> → 键=文件名（去扩展名）；<c>*.css</c> → Css（多文件按名序拼接）。目录缺失 → 空包（零回归）。</summary>
public sealed class FileHtmlAssetPack : IHtmlAssetPack
{
    public FileHtmlAssetPack(string name, string directory);
    public string Name { get; }
    public IReadOnlyDictionary<string, string> Templates { get; }
    public string Css { get; }
}
```

### 3.2 修改文件（最小面）

| 文件 | 改动 |
|---|---|
| `Html/Assets/IHtmlAssetProvider.cs` | `HtmlAssetKeys` 增 `PlateBasePack = "plateBase"`；`IHtmlAssetProvider` 增 `IHtmlAssetPack? GetPack(string name)`（默认实现返回 null，**零回归**） |
| `Html/Assets/HtmlAssetRegistry.cs` | 注册内嵌包 `plateBase`：`htmlShell`（与 `HtmlDocumentRenderer` 内置壳同形）、`viewSelector`+`selectorItem`、`emptyDiv`/`emptyP`（**复用既有 const**，不新增字面量）、`composer` 默认形态 |
| `Plate/TConfig/Models/UIConfiguration.cs` | 增 `[JsonPropertyName("templatePacks")] List<string>? TemplatePacks`、`[JsonPropertyName("themeSpec")] ThemeSpec? ThemeSpec` |
| `Plate/TConfig/Models/ThemeSpec.cs`（新） | `TokensLight` / `TokensDark`（`Dictionary<string,string>`）、`ComponentPack`（string） |
| `Operation/FrontEndDriven/HtmlHosting/HtmlBridgeAssembly.cs` | 装配合成：`LoadPacks` → `Resolve` → 原链（详见 §2.3） |

### 3.3 关键不变式

| # | 不变式 | 说明 |
|---|---|---|
| **I1** | 校验对象唯一 | `AttachTemplates` / `HtmlKeyContract` / 渲染器读的**同一个** `resolved` 字典（防"两套解析漂移"） |
| **I2** | 优先级单向 | JSON 显式 **永远** 覆盖包；包链**后项**覆盖前项 |
| **I3** | 缺包零回归 | 未声明 `templatePacks` / 目录不存在 / 包名未注册 → 行为与今日**完全一致** |
| **I4** | 旧格式共存 | 旧 `templates.theme`（裸 CSS）路径保留一版并用诊断告警提示迁移 |
| **I5** | plate 零上层依赖 | 包内容只来自内嵌基础 + 外部资源，`Plate.HtmlViewer` 不感知 Agent/Produce 领域名 |
| **I6** | 键契约覆盖不缩小 | 外提片段**必须**进入门禁扫描面（见 §6） |


---

## §4 配置目标形态

### 4.1 目录结构（选项 A）

```
Projects/Information/DrivenConfig/
├── JsonConfig/
│   └── AgentRuntimeUI.json          ← 瘦身后（引用 + token + 覆盖项）
└── Assets/                          ← 新增：资源包根
    └── agentConversation/
        ├── htmlShell.html           ← 若采用框架内置壳，可不提供
        ├── flowShell.html
        ├── taskItem.html
        ├── historyItem.html
        ├── message.html
        ├── round.html
        ├── attachItem.html
        ├── taskPaneShell.html
        ├── taskNodesPanel.html
        ├── taskNodeGroup.html
        ├── taskNodeRow.html
        ├── taskRuntimePanel.html
        ├── taskRuntimeRow.html
        ├── taskRuntimeEmpty.html
        ├── operationPanel.html
        └── agentConversation.css    ← 域 CSS（P0 保留项整体迁入）
```

> `FileDriven/BuildOutput/Debug/Configs/AgentRuntimeUI.json` 是**构建产物**（随构建再生），不手工编辑；源只在 `JsonConfig/`。

### 4.2 JSON 目标形态（after）

```jsonc
{
  "description": "Agent 运行时控制台 — …（原样保留）",
  "initCommands": [ "exec AgentUi_ConversationListHtml output=conversationHistoryViewer" ],

  "templatePacks": [ "plateBase", "agentConversation" ],   // 新增：包链（后覆盖前）

  "themeSpec": {                                            // 新增：结构化主题
    "tokensLight": {
      "bg": "#ffffff", "fg": "#24292f", "muted": "#57606a", "border": "#d0d7de",
      "user-bg": "#dbe7ff", "user-fg": "#0b2a52",
      "agent-bg": "#f1f2f4", "agent-fg": "#1f2328"
    },
    "tokensDark": {
      "bg": "#0d1117", "fg": "#e6edf3", "muted": "#8b949e", "border": "#30363d",
      "user-bg": "#1f3a5f", "user-fg": "#e6edf3",
      "agent-bg": "#21262d", "agent-fg": "#e6edf3"
    },
    "componentPack": "agentConversation"
  },

  "templates": { },        // 仅保留真正覆盖项；无覆盖可整体省略

  "form":  { "title": "…", "width": 1280, "height": 940 },
  "views": [ /* 原样保留：layout / events / bindings / htmlPage */ ]
}
```

### 4.3 对照表（before → after）

| 项 | Before | After | 归属 |
|---|---|---|---|
| `templates.theme`（12 359 B 裸 CSS） | JSON 内联 | → `themeSpec.tokensLight/tokensDark`（token 表）+ `agentConversation.css`（域 CSS） | L3 + L2 |
| `templates.htmlShell`（247 B） | JSON 内联 | → 框架包 `plateBase`（可省略） | L1 |
| `templates.viewSelector` / `selectorItem` | JSON 内联 | → 框架包 `plateBase` | L1 |
| `templates.emptyDiv` / `emptyP` | JSON 内联 | → 框架包 `plateBase`（**已有同名常量**） | L1 |
| `templates.composer` | JSON 内联 | → 框架包 `plateBase` | L1 |
| `templates.flowShell / taskItem / historyItem / round / message / attachItem / taskPaneShell / taskNodes* / taskRuntime* / operationPanel` | JSON 内联 | → `Assets/agentConversation/*.html` | L2 |
| `form` / `views` / `events` / `bindings` / `htmlPage` | JSON 内联 | **不变** | L3 |

> 体积预期：38 234 B → **≈6–8 KB**（G6）。

---

## §5 分阶段实施（文件级）

### P0 · 删除与框架重复的基础 CSS（1 文件，零 C#）

**文件**：`Projects/Information/DrivenConfig/JsonConfig/AgentRuntimeUI.json`

从 `templates.theme` 中删除与 `HtmlAssetRegistry.DefaultCss`（`HtmlAssetRegistry.cs:288-366`）**同形**的段落（装配点 `HtmlBridgeAssembly.cs:137-145` 已自动 `baseCss + "\n" + themeCss` 合并）。

**删除清单**（逐条核对）：

| 段落 | 依据 |
|---|---|
| `:root{--bg…--agent-fg}` 浅色 token 块 | `HtmlAssetRegistry.cs:290-299` |
| `@media (prefers-color-scheme:dark){:root{…}}` | `HtmlAssetRegistry.cs:300-311` |
| `*{box-sizing:border-box}`、`body{…}` | `HtmlAssetRegistry.cs:312-319` |
| `.msg` / `.msg.user` / `.msg.agent` / `.badge` / `.bubble` / `.msg.user .bubble` / `.msg.agent .bubble` | `HtmlAssetRegistry.cs:321-340` |
| `.round` / `.round-header` / `.round-header:hover` / `.msg.tool .tool-layer` / `.tool-summary` / `.tool-summary::before` / `details.tool-layer[open]>.tool-summary::before` | `HtmlAssetRegistry.cs:343-354` |
| `.msg.error .bubble` / `.msg.system` / `.msg.system .bubble` | `HtmlAssetRegistry.cs:355-360` |
| `.empty{color:var(--muted);text-align:center;margin-top:24px}`（含 `db-*` 段内第二次出现的 `.empty{color:var(--muted)}`） | `HtmlAssetRegistry.cs:341` |

**保留清单**：`.prompt-layer*`（DefaultCss 无此规则）+ 全部域规则（`conv-* / session-wrap / session-bar / bar-title / task-item / task-* / history-* / hist-count / hname / node-* / nodes-* / db-* / composer* / runtime-* / rt-* / pool-* / active-tag / status-* / actor-tag / attach-* / bubble-attach / view-switch* / ctrl-btn / ai-payload-* / mono / done / idle / started / running / paused / completed / faulted / cancelled`）。

> 预期 12 359 → ≈9.5 KB。**P0 与 P2 触及同一区域**，可合并一次编辑，但 P0 作为独立可验证检查点保留（构建 + 门禁 + 肉眼回归）后才进 P1。

### P1 · HtmlViewer 引入「具名资产包」机制

见 §3.1 / §3.2。JSON 侧同时增 `templatePacks` 并把 6 个通用片段从 `templates` 移除。

**测试**（`Environment/CentralTest/CSharpTest/Plate.Tests/HtmlViewer/`）：新增 `HtmlPackageResolverTests.cs`
- 包链覆盖顺序（后覆盖前）
- JSON 显式覆盖包
- 空包 / 缺目录 / 未注册包名的兜底（零回归）
- 合成集上 `HtmlKeyContract.ValidateAll` 仍生效（G5）

### P2 · 主题结构化（`themeSpec`）

见 §3.2 / §4.2。**`ConfigThemeProvider` / `ThemeDocument` 已支持四参构造，无需改动**（`ConfigThemeProvider.cs:19-26`、`ThemeDocument.cs:13-17`）。

**测试**：扩展 `Plate.Tests/HtmlViewer/ThemeDocumentTests.cs` —— 断言 token 深浅 → `ToCss()` 输出含 `:root{…}` 与 `@media (prefers-color-scheme: dark){:root{…}}`。

### P3 · 领域片段 / 域 CSS 外提到资源包

见 §4.1。**C# 改动：零**（`ConversationPageRenderer` 等仍只吃"已合并的 `templates` 字典"：`ConversationPageRenderer.cs:37-42` 构造注入 + `EffectiveTemplates`）。

**回归**：
- `Scripts/Agent/agent_html/agent_db_batch_ops_test.json`（分页/多选/详情/编辑/批量删除全链路）
- `Scripts/Agent/agent_conversation/agent_conversation_cline_write_smoke_test.json`
- `Scripts/Agent/agent_conversation/agent_conversation_toolcall_node_display_test.json`

### P4 · 门禁脚本同步（**硬前置，与 P1 同批**）

| 文件 | 改动 |
|---|---|
| `Frame/PowerShellScript/Operation/DSL/Agent/Check-AgentRuntimeUIConfig.ps1` | 增"包解析"步骤：读 `templatePacks` + `<configDir>/../Assets/<pack>/*.html`，与 JSON `templates` 按 §2.2 优先级合成后，再跑 `Check-TemplateKeys`（`:248` 与 `:276-281`） |
| `Frame/PowerShellScript/Share/Validate/Test-JsonFiles.ps1` | 增对 `DrivenConfig/Assets/**` 的资源清单校验（存在性 / 编码 / 空文件） |
| `Plate.Tests/.../HtmlPackageResolutionGateTests.cs`（建议） | 断言"解析后模板集 = 门禁所见集"，防两套解析漂移（I1） |


---

## §6 校验与门禁

### 6.1 每阶段必跑

```powershell
# ① Frame（含 HtmlViewer 改动；必须先于 Agent）
dotnet build d:\Project\Main\Frame\CsharpPrj\MainProj.sln -c Debug

# ② Plate.Tests（HtmlViewer 单测）
dotnet test d:\Project\Main\Environment\CentralTest\CSharpTest\Plate.Tests\HtmlViewer\HtmlViewer.csproj -c Debug

# ③ 配置门禁（键契约 / 布局重叠 / 绑定悬空）
& d:\Project\Main\Frame\PowerShellScript\Operation\DSL\Agent\Check-AgentRuntimeUIConfig.ps1

# ④ JSON 文件门禁
& d:\Project\Main\Frame\PowerShellScript\Share\Validate\Test-JsonFiles.ps1

# ⑤ 若动 Agent 侧（选项 B）
dotnet build d:\Project\Main\Projects\Plug\Agent\Agent.sln -c Debug

# ⑥ 总门禁 / 回归
& d:\Project\Main\Frame\PowerShellScript\...\Invoke-GateCheck.ps1 -All
& d:\Project\Main\Frame\PowerShellScript\...\Invoke-Regression.ps1 -Matrix default
```

### 6.2 阶段验收判据

| 阶段 | 判据 |
|---|---|
| **P0** | `Check-AgentRuntimeUIConfig.ps1` 通过（0 Error）；主对话框 / 数据库页渲染正常；深浅色切换正常；`theme` 不再含同形基础规则 |
| **P1** | `Plate.Tests` 全绿（新增 `HtmlPackageResolverTests`）；`templatePacks` 生效（`plateBase` 片段可从 JSON 删除而不失样式）；键契约仍校验**合成集** |
| **P2** | `ThemeDocumentTests` 扩展通过；`themeSpec` 生效（改动 token 值 → 页面变色）；`themeSpec` 缺失时旧路径零回归 |
| **P3** | 三个 Agent 交互脚本全绿；`AgentRuntimeUI.json` ≤ 8 KB |
| **P4** | 门禁脚本能对**包内** `*.html` 报出同父键冲突（可构造负例验证）；`Invoke-GateCheck.ps1 -All` 无新增红项 |

### 6.3 负例验证（P4 必做）

构造一个含重复 `data-key` 的 `Assets/agentConversation/*.html`，确认门禁**报错**后再还原 —— 证明键契约覆盖未因外提而缩小（I6）。

---

## §7 构建时序与依赖

> **构建时序（勿省）**：改 `Plate.HtmlViewer` → ① 编 Frame（`Frame/CsharpPrj/MainProj.sln`）→ ② 编 Agent（`Projects/Plug/Agent/Agent.sln`）→ ③ 跑测试。
> Agent 经 `$(CsharpBuildOutputDir)Plate.HtmlViewer.dll` **烘焙引用**，Agent 源侧不能 `ProjectReference` 到 Frame 源码（`COMPOSITION_FRAMEWORK_DESIGN.md:161`）。

其余约束：

| 约束 | 说明 |
|---|---|
| 烘焙探针 | `BuildFramework/Build-AgentGeneration.ps1:120`、`BuildFramework/Check-BakeFramework.ps1:338` 的 `coreProbe` 含 `Plate.HtmlViewer.dll` —— 改动后须确认探针仍通过 |
| 既有债白名单 | `Check-BakeFramework.ps1:132-133` 记 `Produce.Launcher` 引用 `LayoutAnalysis/HtmlViewer` 为已知债 —— 本方案**不新增**此类跨层引用 |
| Produce 侧 | `ProduceHtmlHosting.AttachTemplatesAndThemeFromConfigAsync` 与 `ProducePanel.json` 同享该装配路径；机制变更须同步验证 Produce 面板 |

---

## §8 风险与权衡

| # | 风险 | 等级 | 缓解 |
|---|---|---|---|
| **R1** | **键契约被绕过**（片段外提但门禁未同步 → `<details>` 稳定键 / 同父键唯一性失去保护） | **高** | P1 与 P4 **同批交付**；校验对象恒为 `resolved`（I1/I6）；P4 负例验证 |
| **R2** | `theme` 由 string 改对象破坏兼容 | 中 | 用**新增** `themeSpec` 并行；旧 `theme` 保留一版 + 诊断告警 |
| **R3** | plate 定位漂移（域内容渗入框架） | 中 | 内容只落配置侧 / 插件侧；plate 只加**机制**（I5）；评审点 = `Html/Assets/` 是否出现领域名词 |
| **R4** | `HtmlUiHost` 全局静态槽在多插件下互相覆盖 | 中 | 已有多实例槽 `HtmlUiHostRegistry`（`AgentHtmlHosting.cs:31`）；包解析随 slot 走，不引入新全局态 |
| **R5** | CSS 顺序变化导致视觉回归 | 中 | P0 只删"逐字重复且值相同"规则；P2 依赖 `ThemeDocument.ToCss()` 固定顺序（tokens 在前、组件 CSS 在后，与原顺序一致，`ThemeDocument.cs:30-38`） |
| **R6** | 包文件缺失 → 静默丢样式 | 中 | `FileHtmlAssetPack` 缺目录返回空包；装配期经诊断 sink 报告"声明了包但未装载"（`HtmlUiHostSlot.DiagnosticsSink`） |
| **R7** | 单元测试与门禁两套解析漂移 | 低 | `HtmlPackageResolutionGateTests` 断言两集相等（§5 P4） |
| **R8** | 体积收益被"覆盖项"抵消 | 低 | 覆盖优先级单向（I2）；评审时检查 `templates` 是否仅剩真正的单例覆盖 |

---

## §9 附录

### 9.1 模板归属表（22 项全覆盖）

| 模板 | 字符数 | 归属 | 落点 |
|---|---|---|---|
| `htmlShell` | 247 | **L1 框架** | `plateBase`（`HtmlAssetRegistry`；亦可复用 `HtmlDocumentRenderer` 内置壳） |
| `emptyDiv` | 31 | **L1 框架** | `plateBase`（**已存在** `HtmlAssetKeys.EmptyDivTemplate`） |
| `emptyP` | 27 | **L1 框架** | `plateBase`（**已存在** `HtmlAssetKeys.EmptyPTemplate`） |
| `viewSelector` | 35 | **L1 框架** | `plateBase`（`HtmlFlowProjector.RenderSelector` 消费） |
| `selectorItem` | 151 | **L1 框架** | `plateBase` |
| `composer` | 485 | **L1 框架** | `plateBase`（`HtmlComposerProjector.TemplateName`） |
| `flowShell` | 418 | L2 领域 | `Assets/agentConversation/`（`HtmlFlowProjector.RenderShell` 消费） |
| `taskPaneShell` | 92 | L2 领域 | 同上（`HtmlFlowProjector.RenderPaneSplit` 消费） |
| `taskItem` | 776 | L2 领域 | 同上 |
| `historyItem` | 480 | L2 领域 | 同上 |
| `round` | 179 | L2 领域 | 同上 |
| `message` | 1 076 | L2 领域 | 同上 |
| `attachItem` | 177 | L2 领域 | 同上 |
| `operationPanel` | 565 | L2 领域 | 同上 |
| `taskNodesPanel` | 223 | L2 领域 | 同上（`HtmlGroupedListProjector` 消费） |
| `taskNodeGroup` | 262 | L2 领域 | 同上 |
| `taskNodeRow` | 322 | L2 领域 | 同上 |
| `taskRuntimePanel` | 96 | L2 领域 | 同上 |
| `taskRuntimeRow` | 366 | L2 领域 | 同上 |
| `taskRuntimeEmpty` | 45 | L2 领域 | 同上 |
| `theme`（域部分） | ≈9 500 | L2 领域 | `Assets/agentConversation/agentConversation.css` |
| `theme`（token 部分） | ≈700 | L3 声明 | `themeSpec.tokensLight/tokensDark` |

### 9.2 待决事项

| # | 事项 | 默认取值 | 影响 |
|---|---|---|---|
| **D1** | 领域包落点：配置侧资源（A） vs 插件侧嵌入（B） | **A** | 决定 P1/P3 用 `FileHtmlAssetPack` 还是 `EmbeddedHtmlAssetPack` |
| **D2** | 旧 `templates.theme` 兼容期长度 | **保留一版**（下一版加弃用告警） | 决定 P2 是否直接删旧键 |
| **D3** | `plateBase` 是否含 `htmlShell` | **含**（并保留 `AttachShell` 覆盖点） | 决定 JSON 能否省略 `htmlShell` |
| **D4** | 是否同步改造 `ProducePanel.json` | **紧随其后**（同机制、独立批次） | 决定本批次范围 |

### 9.3 完成定义（DoD）

- [ ] `AgentRuntimeUI.json` ≤ 8 KB，`templates` 仅剩覆盖项或为空
- [ ] `Assets/agentConversation/`（或插件嵌入资源）落地，含 20 个片段 + 1 份域 CSS
- [ ] `HtmlPackageResolver` / `FileHtmlAssetPack`（或 `EmbeddedHtmlAssetPack`）落地并入 `HtmlAssetRegistry`
- [ ] `UIConfiguration.TemplatePacks` / `ThemeSpec` 落地，`HtmlBridgeAssembly` 走合成链
- [ ] `Plate.Tests` 新增 `HtmlPackageResolverTests`（+ 建议 `HtmlPackageResolutionGateTests`）全绿
- [ ] `ThemeDocumentTests` 覆盖 `themeSpec` token 深浅
- [ ] `Check-AgentRuntimeUIConfig.ps1` 能对包内 `*.html` 报键冲突（负例已验证）
- [ ] 三个 Agent 交互脚本全绿；`MainProj.sln` 与 `Agent.sln` 0 警告 0 错误
- [ ] `Plate.HtmlViewer` 内**无**领域名词（plate 零上层依赖未破）

