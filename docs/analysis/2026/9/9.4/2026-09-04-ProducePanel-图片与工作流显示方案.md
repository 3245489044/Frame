# ProducePanel 显示方案：产出图片 + 编码工作流可视化（完全 C#）

> **日期**：2026-09-04
> **定位**：面向 `FileDriven/DrivenConfig/JsonConfig/ProducePanel.json` 的优化分析，回答两个问题：
> **Q1** 如何让 UI 框架显示"产出链"生成的图片？
> **Q2** 仓库里是否已有 HTML 包/管道能直接渲染"编码的工作流"（含在面板中显示当前链的节点-边结构）？
> **范围**：只到"分析 + 设计 + 落点清单"，不包含本次修改。涉及模块：`FrontEndDriven/TConfig`（UI 框架）、`FileDriven`（宿主）、`Produce`（确定性产出链）。
> **总原则**：本次改写后，工作流的"显示"与"动态编排执行"**全部在 C# 侧落盘**，不再依赖 TypeScript / JS 侧渲染。

***

## 〇、结论速览（TL;DR）

| 问题                 | 结论                                                                                                                                                                                                                                                                                         | 依据                                               |
| ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------ |
| Q1 能显示图吗           | **能，控件类型本就支持**：`picturebox`/`htmlviewer`/`markdownviewer`/`webbrowser` 都已注册                                                                                                                                                                                                                | `ControlTypeRegistry.cs`、`ControlTypeCatalog.cs` |
| 卡点在哪               | 出图链**图已落盘**，但 `ProduceUiRuntime.FormatRun` 把结果当纯文本塞进 textbox                                                                                                                                                                                                                               | `ProduceUiRuntime.cs`、`ImageHandlers.cs`         |
| Q2 有现成 HTML 管道吗    | **有**：`htmlviewer`(WebView2) + HTML 后端/DOM 桥 + 模板引擎，先例=Agent 会话页                                                                                                                                                                                                                           | `AgentRuntimeUI.json`、`AgentUiLibraryHtml.cs`    |
| Q2 能直接渲染"工作流 DAG"吗 | **能：C# 侧原生现算渲染（完全 C#）**。建图化复用 `LayoutAnalysis`（`LayoutGraphBuilder` → `LayoutGraphContext`），渲染新增 C# SVG 生成器（`LayoutSvgGenerator`）→ 输出自包含 SVG/HTML 给 `htmlviewer`。**弃用 mermaid**（不内嵌 3.3MB 第三方 JS）**与 TS WorkflowGraph 渲染职责**。机制：C# 读 workflow JSON → 建图化 → DAG 分层布局 → 现算输出 SVG（无运行时 JS 依赖） | 本方案 §5、§6.3                                      |
| 推荐做法               | 图 = 新 `htmlviewer` + base64 `<img>`；链 = C# `LayoutAnalysis` 建图化 + 新增 `LayoutSvgGenerator` 现算 SVG/HTML 喂给同 `htmlviewer`，**不在 C# 内联 mermaid、也不依赖 TS 渲染**                                                                                                                                     | 见 §6                                             |

***

## 一、背景与现状

### 1.1 ProducePanel 的现状

`ProducePanel.json`（FileDriven 面板，经 `sideload` 渲染进宿主 `contentContainer`）结构：

- `chainCombo` 选择链：`text-generate` / `text-to-comic` / `image-generate`

- `inputJsonTextBox` 填输入 JSON

- `runButton` 调 `ProduceUi_Run(chain, inputJson)` → `ProduceChainWorkflow.json` → 结果**回写为文本**到 `resultTextBox`

- `infoButton` 调 `ProduceUi_ChainInfo`

也就是说：**产出链只回显文本**，即便 `image-generate` 出了图，面板也只能看到被序列化成 JSON 文本的图片路径，看不到图。

### 1.2 出图链路（图确实生成了）

出图链配置 `Projects/Produce/Config/workflows/image-generate.json`：

```
guard_comfy → build_payload → queue_prompt → poll_status → fetch_output → output
   │（onUnavailable）                                       （onFail）
   └→ degrade ───────────────────────────────────────┐  └→ output_failed ─┐
                                                      ▼                    ▼
                                           （均为 produce.flow.output 终态）
```

- `fetch_output`（`Produce.Functions/Image/ImageHandlers.cs`）调用 `ComfyClient.FetchImageAsync` 把图片字节落盘到 `comfy.json.outputDirectory`，产物 `stored[]`（每项含 `{filename, path, bytes}`）。

- 终态节点 `output` 的 `parameterKeys:["stored"]`，经 `produce.flow.output` 把 `stored` 写进运行结果数据上下文。

- 所以**图片已存在本地文件**（`stored[].path`），只是从未暴露给 UI 控件。

### 1.3 运行结果的形状

`ProduceUiRuntime.RunAsync → FormatRun` 把所有 `run.Data` 键值序列化成文本。对图片链而言数据上下文含 `stored`（数组）。这是改出 HTML 预览的天然原料点。

***

## 二、目标与边界（先分清两件事）

本方案必须区分两个"显示"概念，避免混为一谈造成方案漂移：

| 显示对象                | 含义                                             | 技术难度                     |
| ------------------- | ---------------------------------------------- | ------------------------ |
| **A. 显示产出图片**       | 把生成的 PNG 显示在面板里                                | 低（控件已支持）                 |
| **B. 显示编码工作流**      | 把"链/工作流"的节点-边结构渲染出来（读懂这个 JSON 在干什么）            | 中（C# 侧现算，需补 SVG 生成器）     |
| **B′. 渲染"UI 面板本身"** | ProducePanel.json 这类"用 JSON 编码 UI/流程"被 HTML 渲染 | 已是现成能力（htmlviewer 会话页即例） |

用户通常三者混着问。本文 A、B 各自给方案，B′ 作为 Q2 的直接回答。

***

## 三、UI 框架显示图片能力盘点（Q1 核心）

UI 框架= `FrontEndDriven`（控件后端）+ `TConfig`（语义层词表）。相关注册：

**`CsharpPrj/Operation/FrontEndDriven/Shared/ControlTypeRegistry.cs`**

- `picturebox` → WinForms `PictureBox`，字段 `image / sizeMode / borderStyle`

- `htmlviewer` → `HtmlViewer`（WebView2 基座），字段 `html / fontSize`

- `markdownviewer` → `MarkdownViewer`，字段 `markdown`

- `webbrowser` → `WebBrowser`，字段 `url / documentText`

**`CsharpPrj/Plate/TConfig/Controls/ControlTypeCatalog.cs`**（语义层词表，无 UI 工具包依赖）

- `picturebox` 默认语义 = `ValueSemantic.Path`（按路径装载）

- `htmlviewer / markdownviewer / webbrowser` 已收录

**值回写契约**（function `output=<控件>` 写回）位于 `CsharpPrj/Modules/WinFormsValueContract/BuiltInValueContracts.cs`，其中明确 **htmlviewer 的 Text →** **`SetHtmlAsync`**——即：函数返回一段字符串、`output` 指向 htmlviewer 控件时，字符串会被当 HTML 渲染。

**结论**：ProducePanel 用的宿主（`sideload` / ECS / `contentContainer`）与 Agent 面板同一套，图片/富文本显示从控件层是通的，不需要新增第三方控件。

***

## 四、已有 HTML 渲染管道（Q2 直接回答）

仓库内**已有一套可用的 HTML 渲染管道，无需引第三方包**，先例是 Agent 运行时主对话框：

- `FileDriven/DrivenConfig/JsonConfig/AgentRuntimeUI.json` 声明：

  ```jsonc
  { "type": "htmlviewer", "name": "conversationHistoryViewer", "backend": "web", ... }
  ```

- `initCommands`/按钮用 `output=conversationHistoryViewer` 把 HTML 写回控件；`AgentUiLibraryHtml.cs` 装配"HTML 后端运行时（DOM 桥 + 会话页声明 + 模板 + 主题）"。

- 模板引擎支持 `{#each …}{/each}`、`{?cond}…{:else}…{/}`、`{text|raw}` 等占位符（见 `AgentRuntimeUI.json.templates`）。

所以对 **B′（渲染 JSON 编码的 UI/流程）** 和 **对"HTML 内嵌任意内容（含** **`<img>`）"**，答案都是：**是，已有管道可直接渲染**。

> 结论：**图片显示**不是"缺 html 包"，而是"当前没把图片转成 HTML/或接到支持图片的控件上"。

***

## 五、"编码工作流 → 显示"的现状盘点（Q2 之"工作流 DAG"）

把"工作流本身的节点-边结构"渲染成图（可视化 DAG），库内现状如下：

| 形态                                        | 是否已有                                                                                                                                        | 证据/说明                                                                                                             |
| ----------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| 渲染任意 HTML 的 `htmlviewer`                  | ✅                                                                                                                                           | §4                                                                                                                |
| 从"工作流 JSON"建图化（nodes/edges 图模型 + 入口/出口/簇） | ✅ **C# 已有**：`LayoutGraphBuilder` → `CoreGraph<string,LayoutNodeData,LayoutEdgeData>` → `LayoutGraphContext`；`LayoutDataCollector` 已能从原始数据出图 | `CsharpPrj/Share/LayoutAnalysis/`（`LayoutGraphBuilder.cs`、`LayoutDataCollector.cs`、`Models/LayoutGraphModels.cs`） |
| 把图结构序列化为中间产物                              | ✅ **C# 已有**：`LayoutGraphFormatter.GenerateJson`（节点/边/簇/入口/出口 JSON）                                                                          | `LayoutAnalysis/Formatting/LayoutGraphFormatter.cs`                                                               |
| 直接把 `.json` 链画成节点-边图的**运行时渲染器（SVG/HTML）** | ⚠️ **C# 侧缺，需新增一个** **`LayoutSvgGenerator`**：消费 `LayoutGraphContext` → DAG 分层（最长路径）布局 → 现算输出自包含 SVG/HTML。C# 图模型与建图化已备好，渲染器是唯一补齐点             | 本方案决策（完全 C# 原生 SVG，弃用 mermaid 与 TS 渲染）                                                                            |
| 图数据结构（CSG nodes/edges、GraphFoundation 等)  | ✅ 有数据模型                                                                                                                                     | `CsharpPrj/Protocol/CSG`、`CsharpPrj/BasicMethod/Map/GraphFoundation`、`DataStoreDriven` 图存储                        |
| 动态编排执行内核                                  | ✅ **C# 已有**：`Operation/WorkFlowMethod`（`WorkflowMethodExecutionService` 执行 + `ICallFrameService` 断点/续步）                                     | §6.3b                                                                                                             |

**结论**：**工作流渲染完全走 C#**：建图化与图模型已备（`LayoutAnalysis`），只需**新增一个 C# SVG 生成器**补齐"图 → SVG/HTML"的最后一环；Produce 的链自带图结构（`nodes[].next.{onSuccess,onFail,onUnavailable}`），C# `LayoutGraphBuilder` 消费同一结构的 workflow JSON → `LayoutGraphContext`；`next` 三分支化为不同 `EdgeData` 语义（`onFail` 标红、`onUnavailable` 标橙虚线降级、`onMax` 标蓝重试），成功主链高亮。C# 侧不内嵌 mermaid（避免 3.3MB 第三方 JS 内联负担），**TS WorkflowGraph 不再承担工作流 SVG 渲染**。

***

## 六、推荐实现（分区设计）

### 6.1 显示产出图片 —— 方案 A（推荐）：`htmlviewer` + base64

**C# 侧（`Projects/Produce/src/Produce.Launcher/ProduceUiRuntime.cs`）**
新增/扩展一个函数（如 `ProduceUi_RunPreview`），复用 `RunAsync` 的执行段，但收敛改为：

- 数据上下文含 `stored`（图片链成功）时，把每个 `stored[].path` 读字节 → `Convert.ToBase64String` → 拼

  ```html
  <img src="data:image/png;base64,…" style="max-width:100%">
  ```

  返回整段 HTML；

- `stored` 缺省/空或链为文本链 → 返回纯文本/降级占位（ComfyUI 不可用也走 degrade，不空窗）。

**为什么 base64 而非** **`file://`**：`htmlviewer` 底层 WebView2 对本地 `file://` 图片默认有加载限制；预览图尺寸小（512²），base64 内嵌稳妥、宿主无关。

### 6.2 方案 B（备选，不采用）：原生 `picturebox` 显示首图

仅把首图 `path` 喂给 `picturebox.image`（`ControlTypeCatalog` 已声明图片语义=Path）。局限：只适合单图首图，多格漫画/可交互不如 htmlviewer。**不作为主方案**，仅在方案 A 不可用时的降级补充。

### 6.3 显示编码工作流（链的节点-边）—— 方案 C：C# 原生现算 SVG/HTML（弃用 mermaid 与 TS 渲染）

用仓库已建的 **`CsharpPrj/Share/LayoutAnalysis`** 建图化 + **新增 C# SVG 生成器** **`LayoutSvgGenerator`**，产物是自包含 SVG/HTML，供同 `htmlviewer` 内联渲染（**全程 C#，无 TS / JS 运行时依赖**）：

1. **建图化在 C#（复用现有）**：`LayoutGraphBuilder` 把 workflow JSON（`nodes[]` + `next{onSuccess/onFail/onUnavailable/onMax}`）构造成 `CoreGraph<string,LayoutNodeData,LayoutEdgeData>`，产出 `LayoutGraphContext`（含节点、边、入口/出口、簇、环检测）。`next` 三分支化为不同 `EdgeData.Kind/RelationType/Description` 语义，为 SVG 派生边样式（`onFail` 红、`onUnavailable` 橙虚线降级、`onMax` 蓝重试、成功主链高亮）。
2. **渲染在 C#（新增** **`LayoutSvgGenerator`）**：消费 `LayoutGraphContext` → 最长路径 DAG 分层布局（层级=最长入边链长）→ 同层排 x、跨层排 y 分配像素坐标 → 现算输出自包含 SVG/HTML 字符串。无三方运行时 JS 依赖，SVG 仅 KB 级。入口节点描边、成功主链高亮、非成功分支标注 `onFail`/`degrade`。
3. **调用方式（C# 现算）**：函数 `ProduceUi_ChainGraph` 内取 workflow JSON → 反序列化 → `LayoutDataCollector.CollectGraph` → `LayoutSvgGenerator.GenerateSvg(html:true)` → 返回整段 HTML；产物直接交给 `htmlviewer`（`output=<htmlviewer>` → 经 `SetHtmlAsync` 给 WebView2 `NavigateToString`）。
4. **配置即数据**：workflow 是 `Config/workflows/*.json` 数据，渲染逻辑（C# 固定）；新加 workflow JSON 文件即可现算渲染，**无需改 C# 渲染代码**，也不用内嵌第三方 JS。
5. **弃用项**：**mermaid**（embedded `mermaid.min.js` 资源 + csproj 引用已删除）与 **TS WorkflowGraph 渲染职责**（`TypeScriptPrj/TsWorkspace/WorkflowGraph` 不再承担工作流 SVG 渲染，避免跨语言双重维护）。

> **关键机制（N3 已确认）**：HtmlViewer 走 `NavigateToString`，是整页字符串注入、无静态文件基址，第三方库只能内联 `<script src>` 不行。选用 **C# 原生 SVG 生成器**正是规避"运行期内嵌大体积 JS"——它与 TS WorkflowGraph 一样现算产出纯 SVG 文本，直接字符串注入，不依赖任何第三方 JS 运行时；且省去跨语言桥接，进可接动态编排（§6.3b）退可独立显示。

### 6.3b 动态编排推进设计（C# 侧建"动态执行"，与显示共用同一 C# 图/执行基建）

> **定位**：§6.3 的 `LayoutSvgGenerator` 只解决"链的静态显示"。若要把工作流从"静态拓扑展示"推进到"动态编排执行"（运行期节点随结果选边、分支跳转、活体高亮），本仓库 C# 侧**已具备可复用图架构与执行内核，无需引第三方，更无需 TS**。本小节给出复用现有两大模块 + 执行内核的方案落点。

**复用两处现有图基建 + 一套执行内核（均已在仓库内、均在 C#）：**

| 模块                                   | 提供能力                          | 关键类型                                                                                                                                | 复用点                                                                |
| ------------------------------------ | ----------------------------- | ----------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------ |
| `CsharpPrj/Modules/GraphProcessing`  | 图编排基座：建链、合并、事务式图操作、键/节点/边生成器  | `OrchestrationService<TKey,TNodeData,TEdgeData>`、`IChainBuilder` / `IChainMerger` / `IEdgeManager`、`ChainResult<TKey>`、`ICoreGraph` | 把 workflow JSON 的 `nodes[]`+`next{}` 建为真图（节点+分支边），支持运行期按结果动态选边/重建边 |
| `CsharpPrj/Share/LayoutAnalysis`     | 布局分析与图模型，基于 `GraphFoundation` | `LayoutNodeData` / `LayoutEdgeData` / `LayoutGraphContext`、`LayoutGraphBuilder`、`LayoutGraphFormatter`、`LayoutSvgGenerator`(新增)     | 承载建图化 + 布局/显示（静态 SVG）与动态编排的**显示/执行共享落点**                           |
| `CsharpPrj/Operation/WorkFlowMethod` | 执行内核 + 断点/续步                  | `WorkflowMethodExecutionService`、`ICallFrameService`、`CallChainGuard`、`IFunctionHub`                                                | **执行层直接复用，不重造调度器与断点队列**                                            |

**设计落点：动态编排逻辑构建在** **`Share/LayoutAnalysis`，执行内核复用** **`Operation/WorkFlowMethod`**

1. 复用 `GraphProcessing.OrchestrationService` 作为**图构建与修改引擎**：`CreateChainFromTemplate` 从 chain 模板建链，`ConnectChains` / `RebuildSequentialRelations` 在运行期合并/重建边——对应 `next` 分支在运行期选边。
2. `LayoutAnalysis`（`LayoutGraphBuilder` + `LayoutNodeData/EdgeData`）作**建图化与显示语义层**：从 `ProduceWorkflow`（`nodes[]`）解析出 `LayoutNodeData`（节点+`FuncName`）与 `LayoutEdgeData`（分支边+`Kind/RelationType`），生成 `LayoutGraphContext`（含 Entry/Exit/Clusters），供显示（现算 SVG/活体高亮）与执行衔接共用。
3. **执行层直接复用** **`Operation/WorkFlowMethod`**：workflow JSON 注册进 `IFunctionHub`，编排走 `WorkflowMethodExecutionService`（RGE 内核），断点/续步走 `ICallFrameService`。`LayoutAnalysis` 不重复实现执行内核；**显示层（`LayoutSvgGenerator`）与执行层（`WorkflowMethodExecutionService`）各自消费同一份 workflow JSON /** **`LayoutGraphContext`**，二选一或并存。

> **复用边界**：GraphProcessing 提供**建图与结构修改**；`Operation/WorkFlowMethod` 提供**执行内核（RGE）与断点/续步（CallFrame）**；`LayoutAnalysis` 提供**建图化 + 布局/显示**。三者各司其职，业务叶子函数（Generate/Poll/Fetch 等）仍走 Produce 既有 `WorkFlowMethod` 函数注册表，本设计**不重造执行器与调度器**。

#### 6.3b-1 对齐说明：为何 C# 原生方案无需 TS / 重造轮子

相比早期"保留 TS WorkflowGraph 做显示、C# 做执行"的跨语言方案，本版改为**完全 C#**，理由：

- **建图化与执行内核仓库均已就绪（C#）**：`LayoutGraphBuilder` 已产出 `LayoutGraphContext`，`Operation/WorkFlowMethod` 已提供完整执行内核与断点/续步。补一个 `LayoutSvgGenerator` 即可打通"显示"。

- **单一语言、无跨语言桥接线**：layout 算法（最长路径分层）与 SVG 输出全部在 C# 内实现，避免 TS 侧 `WorkflowGraph` 与 C# 侧各维护一份消费 workflow JSON 的代码，也无 CLI/进程调用开销。

- **可平滑过渡动态编排**：`LayoutGraphContext` 同时是"静态 SVG 输入"与"动态执行态载体"，同一图模型向上即可做活体高亮/运行期选边（见 §6.3b），无需在显示与执行之间做异构映射。

- 早期参考的 Unity 端 `UnityRelate.cs`（`ProcessingGraph`/`ConditionalNode` 等）仅作**设计范式对照**，非复用对象——其可借鉴的"节点抽象 + IsEnabled + 每帧 Execute"思想，已在 C# `WorkflowMethodExecutionService` + `ICallFrameService` 落地，本设计不再另起炉灶。

### 6.4 面板整合示意（建议，未落盘）

在 `ProducePanel.json` 中把"结果"拆成两块预览 `htmlviewer`（示意）：

```jsonc
{
  "type": "label", "name": "previewLabel",
  "text": "图片 / 工作流预览",
  "layout": { "left": 0.05, "top": 0.44, "width": 0.4, "height": 0.04 }
},
{
  "type": "htmlviewer", "name": "resultViewer", "backend": "web",
  "layout": { "left": 0.05, "top": 0.5, "width": 0.9, "height": 0.3 }
},
{
  "type": "htmlviewer", "name": "chainGraphViewer", "backend": "web",
  "layout": { "left": 0.05, "top": 0.82, "width": 0.9, "height": 0.16 }
}
```

按钮接线示意：runButton 的 `functionName` 改指返回 HTML 的函数、`output` 指向 `resultViewer`；infoButton 可保留 `resultTextBox`（文字说明），另加"链结构"按钮 → `chainGraphViewer`。`resultTextBox` 是否保留由是否仍需原始 JSON 文本决定。

***

## 七、文件级改动清单

| 文件                                                                                                   | 改动                                                                                                       | 归属      |
| ---------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------- | ------- |
| `Projects/Produce/src/Produce.Launcher/ProduceUiLibrary.cs`                                          | 新增 `ProduceUi_RunPreview` / `ProduceUi_ChainGraph` 注册                                                    | 函数门面    |
| `Projects/Produce/src/Produce.Launcher/ProduceUiRuntime.cs`                                          | 收敛 HTML（base64 图 / 链图接入 htmlviewer）——链图改用 C# `LayoutSvgGenerator` 现算 SVG，不再产 mermaid、不依赖 TS              | 运行态     |
| `CsharpPrj/Share/LayoutAnalysis/Formatting/LayoutSvgGenerator.cs`                                    | **（新增）C# SVG 生成器**：`LayoutGraphContext` → 最长路径 DAG 分层布局 → 自包含 SVG/HTML（`LayoutSvgGenerator.GenerateSvg`） | 渲染（新增）  |
| `CsharpPrj/Share/LayoutAnalysis/`（`LayoutGraphBuilder`/`LayoutGraphFormatter`/`LayoutDataCollector`） | （复用）建图化 + 图 JSON 中间产物，作 SVG 输入                                                                           | 图模型（复用） |
| `CsharpPrj/Modules/GraphProcessing/`                                                                 | （复用）`OrchestrationService` 等图编排基座，作动态编排的建图/改边引擎（§6.3b）                                                   | 图引擎（复用） |
| `Projects/Produce/src/Produce.Launcher/`                                                             | **（已执行）删除** embedded `mermaid.min.js`（assets 文件 + csproj `EmbeddedResource` 引用）——弃用 mermaid，避免 3.3MB 冗余  | 资源（清理）  |
| `TypeScriptPrj/TsWorkspace/WorkflowGraph/`                                                           | **不再承担工作流 SVG 渲染**（渲染职责移除；是否删除文件按是否仍有其他 C# 外消费决定）                                                        | TS（弃渲染） |
| `FileDriven/DrivenConfig/JsonConfig/ProducePanel.json`                                               | 加 `htmlviewer` 控件 + 按钮接线                                                                                 | 面板配置    |
| `Projects/Produce/Config/workflows/*.json`                                                           | （可选）加"结果图收集"节点规范化                                                                                        | 链配置     |
| 测试脚本                                                                                                 | 扩展 `Scripts/Produce/*` 断言 htmlviewer 挂载/可见/HTML 含 SVG（原 mermaid 断言随弃用去除；TS 侧渲染断言改为 C# 侧）                 | 回归      |

***

## 八、验证与回归建议

沿用既有两个测试文件扩展（同一套 sideload/ECS/断言原语）：

- `FileDriven/DrivenConfig/Scripts/Produce/produce_panel_test.json`：加一节"出图后 resultViewer HTML 含 `<img>`"断言。

- `FileDriven/DrivenConfig/Scripts/Produce/produce_panel_ui_render_test.json`：对新 `htmlviewer` 加 `validate-mount`(parent=contentContainer, visible=true)、`get`、`layout positions/overlaps`；对链图断言 `chainGraphViewer` HTML 含 `<svg>` 与预期节点/边数（来自 C# `LayoutSvgGenerator`）。

- 降级路径：ComfyUI 不可用时断言面板不空窗、显示确定性降级文字。

- (回归 C# 渲染)：为 `LayoutSvgGenerator` 加单测，给定 `LayoutGraphContext` 断言输出为合法自包含 SVG、入口节点/`onFail`/`onUnavailable` 样式正确。

***

## 九、未决问题与后续

| #  | 问题                                                                                             | 影响                   | 建议                                                                                                                                                                  |
| -- | ---------------------------------------------------------------------------------------------- | -------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| N1 | `htmlviewer` 在 Produce 会话的 HTML 后端装配是否需先 `backend:"web"`/`AgentUiLibraryHtml`                  | 决定 6.4 配置            | 实现前先跑一个最小 sideload 探针确认                                                                                                                                             |
| N2 | `output=<htmlviewer>` 的写回契约在当前宿主是否齐全（相对 `SetHtmlAsync`）                                        | 决定方案 A/C 是否开箱即用      | 查 `BuiltInValueContracts` + GUI 回归                                                                                                                                  |
| N3 | 工作流 DAG 渲染方式——**已定案：C# 原生现算 SVG，弃用 mermaid 与 TS WorkflowGraph 渲染**                             | 已确认机制                | 基于 `NavigateToString` 整页注入、无静态基址特性；C# `LayoutGraphBuilder` 建图化 + 新增 `LayoutSvgGenerator` 现算纯 SVG 注入；mermaid（3.3MB 已删）与 TS 渲染职责均移除；见 §6.3、§7                         |
| N4 | 是否保留原始 JSON 文本 `resultTextBox`                                                                 | 布局                   | 按产品取舍                                                                                                                                                               |
| N5 | 动态编排是否推进（§6.3b）——复用 `GraphProcessing` 建图 + `Operation/WorkFlowMethod` 执行 + `LayoutAnalysis` 显示 | 决定是否从"静态显示"升级到"动态执行" | 按产品取舍；采用时在 `Share/LayoutAnalysis` 落 `LayoutGraphBuilder`+`LayoutSvgGenerator` 图化/显示层，执行/断点复用 `WorkflowMethodExecutionService`+`ICallFrameService`，不重造调度器；全程 C#，无 TS |

***

## 附：证据文件索引

- UI 控件词表/注册：`CsharpPrj/Operation/FrontEndDriven/Shared/ControlTypeRegistry.cs`、`CsharpPrj/Plate/TConfig/Controls/ControlTypeCatalog.cs`

- 值契约（htmlviewer Text→SetHtmlAsync）：`CsharpPrj/Modules/WinFormsValueContract/BuiltInValueContracts.cs`

- HTML 渲染先例：`FileDriven/DrivenConfig/JsonConfig/AgentRuntimeUI.json`、`Projects/Agent/src/Agent.Launcher/Modules/Libraries/AgentUiLibraryHtml.cs`、`CsharpPrj/Plate/HtmlViewer/HtmlViewer.cs`

- 出图链/落盘：`Projects/Produce/Config/workflows/image-generate.json`、`Projects/Produce/src/Produce.Functions/Image/ImageHandlers.cs`、`Projects/Produce/Config/comfy.json`

- Produce UI 宿主：`Projects/Produce/src/Produce.Launcher/ProduceUiLibrary.cs`、`ProduceUiRuntime.cs`

- 面板配置与测试：`FileDriven/DrivenConfig/JsonConfig/ProducePanel.json`、`FileDriven/DrivenConfig/Scripts/Produce/*`

- **C# 建图化/图模型（完全 C# 方案支柱）**：`CsharpPrj/Share/LayoutAnalysis/`（`LayoutGraphBuilder.cs`、`LayoutDataCollector.cs`、`Models/LayoutGraphModels.cs`、`Formatting/LayoutGraphFormatter.cs`）；新增落点 `Formatting/LayoutSvgGenerator.cs`

- 图编排基座（§6.3b 建图/改边引擎）：`CsharpPrj/Modules/GraphProcessing/`（`OrchestrationService.cs` 等）

- 相关设计参照：`docs/analysis/2026/6/6.19/UI-Workflow桥接转换框架设计.md`、`docs/analysis/2026/8/8.24/HTML解析与UI编排通用框架提取方案.md`

- 动态编排执行内核（loc 复用对象）：`CsharpPrj/Operation/WorkFlowMethod/`（`WorkflowMethodExecutionService.cs`、`CallFrame/Abstractions/ICallFrameService.cs`、`WorkflowExecution/Engine/WorkflowEngine.cs`、`Guards/CallChainGuard.cs`）

- TS 侧（已弃渲染职责，仅存查）：`TypeScriptPrj/TsWorkspace/WorkflowGraph/`（`src/layout.ts`、`src/svg.ts` 仅作**算法参照**，非复用对象）

<br />
