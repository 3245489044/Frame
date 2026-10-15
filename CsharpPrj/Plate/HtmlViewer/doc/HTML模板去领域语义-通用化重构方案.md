# HTML 模板去领域语义 · 通用化重构方案

> **定位**：把 `Plate.HtmlViewer` 内嵌的 HTML 模板 / 样式 / 类名 / C# 符号中残留的**领域语义**
> （`三视图 / 历史 / 任务 / 节点 / 运行态 / 会话 / 提示层 / 操作面板` 等）彻底剥离，
> 收敛为一套**中性的结构词汇表**：框架只描述"**文档壳 / 分栏 / 面板 / 列表 / 项 / 行 / 分组 / 折叠 / 分段 / 标签 / 状态**"，
> 语义由配置与数据的**键值**承载。
>
> **前序**：`HTML模板资产包化与主题结构化-重构方案.md`（资产包化，已否决外置方向）；
> 已落地部分：框架内嵌默认模板（`HtmlAssetRegistry.DefaultTemplates`）+ 通用样式模块（`CssModules`）+ 类名一轮通用化（`db-*→data-*`、`ai-payload-*→overlay-*` 等）。
> **本方案承接**：在此之上做**第二轮（语义剥离）**，并补上**防回退门禁**。
>
> **口径**：全部落点为仓库内**实测**项（标 `文件`）；词汇表为正/负两份可机检清单。

---

## §0 结论摘要

| # | 结论 |
|---|---|
| 1 | 框架当前仍**15 个模板键**、**6 个类名族**、**5 组 C# 符号**、**4 个 region 名**带领域语义（见 §2） |
| 2 | 语义剥离**不改渲染语义**：键名/类名/符号名"锁步改名"，HTML 结构与数据绑定字段不变 → 视觉零变化 |
| 3 | 真正需要"外移"的只有 **2 处**：`region-convtitle`（含会话语义的 patch 锚）与 `task-history`（含任务语义的容器类） |
| 4 | 剥离后，框架侧只保留**中性结构词**；`history/task/node/session/...` 仅允许出现在**配置 JSON 的键值**与**Agent 侧数据模型**中 |
| 5 | 新增门禁 `Check-HtmlViewerVocabulary`（负例表驱动）→ 防语义回渗（这是本方案的核心增量） |

---

## §1 目标与非目标

### 1.1 目标

| # | 目标 | 判据（可机检） |
|---|---|---|
| **G1** | 模板键零领域语义 | `DefaultTemplates.Keys` ∩ 负例词表 = ∅ |
| **G2** | class 名零领域语义 | 框架样式模块内 `.xxx` ∩ 负例词表 = ∅ |
| **G3** | C# 公开符号零领域语义 | `Plate.HtmlViewer` 公开类型/方法名 ∩ 负例词表 = ∅ |
| **G4** | region/`data-name` 锚零领域语义 | 框架产出的 `region-*` ∩ 负例词表 = ∅ |
| **G5** | 语义有归属出口 | 领域语义只出现在 ①配置 JSON 键值 ②Agent 数据模型 ③`RegisterStyleModule`/模板覆盖 |
| **G6** | 渲染零变化 | 改名锁步（HTML 值 + CSS 选择器 + 调用点），单测全绿 + 实机视觉不变 |

### 1.2 非目标

- 不改模板引擎语法（`{field}` / `{#each}` / `{?cond}` / `{>name}`）。
- 不改消息桥（`data-cmd` / `data-input` / `writeback`）与 `data-*` 数据契约。
- 不改配置 JSON 的 `views` / `form` / `bindings`（数据面语义属配置域，不属框架 HTML）。
- 不引入新的配置文件 / 资源目录（延续"框架内嵌 + 配置覆盖"）。

---

## §2 残留守则：正/负词表

### 2.1 正词表（框架**允许**的结构词汇）

> 只描述**结构关系**，不描述**业务对象**。新增键/类/符号默认从此表取词，组合方式 = `[区域][部件][角色]`（如 `panel-head`、`row-status`、`seg-item`）。

| 维度 | 允许词 |
|---|---|
| 容器 | `shell` `pane` `region` `panel` `section` `toolbar` `overlay` `callout` |
| 列表 | `list` `item` `row` `group` `fold` `tree` `grid` `table` `data` |
| 切换 | `seg` `tab` `nav` `switch` |
| 部件 | `head` `title` `body` `summary` `detail` `note` `meta` `key` `field` `check` `tag` `chip` `badge` `dot` `attach` |
| 状态 | `state` `status` `active` `empty` `disabled` |
| 表单 | `input` `btn` `select` `editor` |
| 排版 | `code` `mono` `kv` `text` |
| 布局修饰 | `aside` `main` `primary` `secondary` `inline` `block` |
| 通用词 | `html` `page` `document` `block` `split` `layout` `component` `collection` |

### 2.2 负词表（框架**禁止**出现的领域词，门禁词源）

```
history  task  node  conversation  conv  session  agent  prompt  actor  pool
db  payload  round  llm  ai  chat  message  bubble  tool  skill  runner  workflow
```

> 例外白名单（需在门禁基线显式登记并注明理由）：无。
> 说明：`message` / `bubble` / `tool` 属"使用者语义"（哪个 UI 都可能用），但既然本框架定位为**通用结构层**，一律下沉到配置模板覆盖；框架只提供 `block` / `item` / `section`。

### 2.3 实测：当前违规项盘点

**（a）模板键（15/20 带语义）** — `HtmlAssetRegistry.DefaultTemplates`（`Html/Assets/HtmlAssetRegistry.DefaultTemplates.cs`）

| 现键 | 违规词 | 目标键 |
|---|---|---|
| `flowShell` | flow（隐含视图流） | `layoutShell` |
| `taskPaneShell` | task | `splitShell` |
| `viewSelector` | view | `segGroup` |
| `selectorItem` | selector（视图切换项） | `segItem` |
| `historyItem` | history | `foldItem` |
| `taskItem` | task | `listItem` |
| `taskNodesPanel` | task / node | `groupedPanel` |
| `taskNodeGroup` | task / node | `groupBlock` |
| `taskNodeRow` | task / node | `detailRow` |
| `taskRuntimePanel` | task / runtime | `statePanel` |
| `taskRuntimeRow` | task / runtime | `stateRow` |
| `taskRuntimeEmpty` | task / runtime | `emptyBlock`（与 `emptyDiv` 合并） |
| `operationPanel` | operation（业务操作） | `actionPanel` |
| `round` | round（会话轮次） | `sectionHeader` |
| `message` | message | `blockItem` |
| `composer` / `attachItem` / `emptyDiv` / `emptyP` / `htmlShell` | — | **保留**（中性） |

**（b）class 名（残留 6 处）** — `Html/Assets/HtmlAssetRegistry.ConversationCss.cs`

| 现类 | 违规词 | 目标类 | 说明 |
|---|---|---|---|
| `.task-history` | task | `.pane-secondary` | 空规则，直接并入分栏语义 |
| `.tool-summary` | tool | `.fold-summary` | `DefaultCss` 内 |
| `.prompt-layer` | prompt | `.note-layer` | 折叠注记层 |
| `.msg` / `.bubble` / `.badge` | message/bubble | `.block` / `.block-body` / `.block-badge` | **高风险**（`DefaultCss` 主体系 + 模板多处），单独批次 |
| `.runtime-panel` / `.runtime-row` / `.runtime-meta` / `.runtime-note` | runtime | `.state-panel` / `.state-row` / `.state-meta` / `.state-note` | 并入 `state` 词 |
| `.composer-input` / `.composer-send` | composer | `.input-lg` / `.btn-primary` | 输入区语义 → 尺寸/角色修饰 |

**（c）C# 公开符号（4 组）**

| 现符号 | 违规词 | 目标符号 | 文件 |
|---|---|---|---|
| `HtmlFlowProjector` | flow | `HtmlShellProjector` | `Html/Render/HtmlFlowProjector.cs` |
| `HtmlRunStateBadgeProjector` | run/runtime | `HtmlStateBadgeProjector` | `Html/Render/HtmlRunStateBadgeProjector.cs` |
| `HtmlRunStateBadgeProjector.RunStateFields` | run | `StateFields` | 同上 |
| `HtmlComposerProjector` | composer | `HtmlInputProjector` | `Html/Render/HtmlComposerProjector.cs` |
| `HtmlGroupedListProjector` / `HtmlSectionListView` / `HtmlEmptyState` / `HtmlDataRowView` | — | 保留（中性） | — |
| `HtmlDataRow.DetailsClass="node"` / `SummaryClass="node-sum"` | node | `"detail"` / `"detail-summary"` | `Html/Render/HtmlDataRow.cs`（默认值，含领域词） |
| `HtmlAssetRegistry.ConversationCss` / `CssModules` 键 `nodes`/`runtime` | conversation/node/runtime | `FrameworkComponentCss`；键 `grouped`/`state` | `Html/Assets/HtmlAssetRegistry*.cs` |

**（d）region / `data-name` 锚（4/6 带语义）** — `Html/Render/HtmlFlowProjector.cs`

| 现锚 | 违规词 | 目标锚 |
|---|---|---|
| `region-tree` | （隐含"任务树"） | `region-aside` |
| `region-main` | — | 保留 |
| `region-bartitle` | bar（会话侧栏） | `region-head` |
| `region-convtitle` | **conv** | `region-subhead` |
| `region-selector` | selector | `region-seg` |
| `region-overlay` | — | 保留 |

> ⚠ **region 名是 patch 锚**（`DomOps.Patch name=region-*`）：改名必须同步 Agent 侧所有写回调用点，否则区域化写回静默失效。这是本轮**唯一需要跨仓强同步**的项。

---

## §3 机制与契约（让"去语义"可长期维持）

### 3.1 键/名的三层归属（单一权威）

```
L1 框架（Plate.HtmlViewer）     ← 只用正词表；键/类/符号的**唯一权威**
L2 配置（{config}.json）        ← 引框架键 + 只声明差异；可用领域语义的**数据键**（views/字段名）
L3 领域（Agent/Produce 侧）     ← 数据模型与投影器；**不得**新增框架级键/类名
```

**规则**：任何 `history/task/node/...` 只能出现在 L2 的**数据键值**与 L3 的**数据模型**；一旦出现在 L1 的键/类/符号 → 违规（门禁红）。

### 3.2 语义下沉出口（不改框架即可表达领域）

| 需求 | 出口 | 例子 |
|---|---|---|
| 领域专属样式 | `HtmlAssetRegistry.RegisterStyleModule(name, css)` | 域覆盖 `grouped` 模块 |
| 领域专属结构 | 配置 `templates` 同名键覆盖 | `listItem` 覆盖为业务卡片 |
| 领域专属 patch 锚 | 框架产出**中性** `region-*`；域用 `data-*` 承载语义 | `region-aside` + `data-domain="history"` |
| 领域专属文案/字段 | 数据键 | `{historyName}`（模板绑定，属数据面） |

### 3.3 命名守卫（新增门禁，见 §5）

`Frame/PowerShellScript/Operation/DSL/Usharp/Check-HtmlViewerVocabulary.ps1`

- **输入**：`Frame/CsharpPrj/Plate/HtmlViewer/Html/**`（模板值、样式、投影器字符串常量）。
- **判据**：负词表（§2.2）以词边界匹配出现即 **Error**；region 锚额外要求 ∈ 正词表。
- **豁免**：显式基线文件 `Baselines/HtmlViewerVocabulary.psd1`（`Allow = @{ 'word' = '理由' }`），空基线 = 零豁免。
- **接线**：注册进 `GateManifests.psd1`（`Expect = 0`），由 `Invoke-GateCheck -All` 统一跑。

---

## §4 分批实施（相位不可逆：诊断 → 编辑 → 验证）

> 每批原子：**键名 + 模板值 + CSS 选择器 + 调用点**必须同批改完，否则"锁步"断裂（改名不会编译报错，只会静默失去样式）。

### P0 · 诊断（只读）
- 枚举 L1 全部键/类/符号/锚（本方案 §2.3 即产出）。
- `Locate-Build -Changed` 生成"受影响文件/工程"清单（含 Agent 侧调用点：`ConversationPageRenderer` / `ConversationViewService*`）。

### P1 · 模板键去语义（低风险）
- `DefaultTemplates` 键按 §2.3(a) 改名（15 个）。
- 同步：`HtmlBridgeAssembly` 中 `htmlShell` 特判保持；投影器内的 `TemplateName` 常量（`HtmlComposerProjector.TemplateName` 等）。
- 同步：`AgentRuntimeUI.json` 中若有覆盖键（当前为空）→ 无需改。
- **DoD**：`DefaultTemplates.Keys ∩ 负词 = ∅`；`Plate.Tests` 全绿；配置门禁通过。

### P2 · class 去语义（中风险，分两小步）
- **P2a（安全）**：`.task-history` / `.tool-summary` / `.prompt-layer` / `.runtime-*` / `.composer-*`（§2.3(b) 前 4 行）。
- **P2b（高风险）**：`.msg/.bubble/.badge` → `.block/.block-body/.block-badge`（`DefaultCss` 主体系 + `message` 模板 + `round` 模板同批）。
- **DoD**：`CssModules` 值 ∩ 负词 = ∅；P5 一致性测试（模板类 ⊆ 样式类）仍绿。

### P3 · C# 符号去语义（低风险，纯改名）
- 按 §2.3(c) 改类型/方法/字段默认值；`HtmlDataRow` 默认 `detail`/`detail-summary`。
- 同步 Agent 侧引用（`HtmlRunStateBadgeProjector.Apply` 等调用点）。
- **DoD**：`Frame` + `Agent.sln` 均 0 错误；`Plate.Tests` + `Agent.Core.Tests` 全绿。

### P4 · region 锚去语义（高风险，跨仓强同步）
- `HtmlShellProjector` 产出锚改 §2.3(d)；**同批**更新 Agent 侧所有 `DomOps.Patch name=region-*` 调用点。
- **DoD**：区域化写回端到端（视图切换/树选/浮层）在实机上正常；`grep "region-convtitle\|region-tree\|region-selector\|region-bartitle"` 零残留。

### P5 · 门禁落地（本方案核心增量）
- 新增 `Check-HtmlViewerVocabulary.ps1` + 基线 + `GateManifests` 接线。
- **负例验证**：临时在框架模板里塞 `class="task-row"` → 门禁必须报错，再还原。
- **DoD**：门禁 `Expect=0`；负例可复现。

---

## §5 验证

```powershell
# 诊断（0 运行）
Frame\Invoke\Invoke-Frame.ps1 -Action locate-build -Changed <files>
Frame\Invoke\Invoke-Frame.ps1 -Action locate-test  -Name HtmlDefaultTemplatesTests

# 编辑后：编 Frame → 编 Agent → 测试（烘焙引用，顺序不可省）
dotnet build d:\Project\Main\Frame\CsharpPrj\MainProj.sln -c Debug
dotnet build d:\Project\Main\Projects\Plug\Agent\Agent.sln -c Debug
dotnet test  …\Plate.Tests\HtmlViewer\HtmlViewer.csproj -c Debug

# 验证（唯一命令）
Frame\Invoke\Invoke-Frame.ps1 -Action invoke-verify

# 语义门禁（新增）
pwsh …\Check-HtmlViewerVocabulary.ps1
```

**验收判据**（每批）：编译 0 错误；`Plate.Tests` 全绿；`Check-AgentRuntimeUIConfig` 通过；**词汇门禁 0 Error**；实机视觉无回归（深浅色 / 视图切换 / 树选 / 浮层 / 分页多选）。

---

## §6 风险与权衡

| # | 风险 | 等级 | 缓解 |
|---|---|---|---|
| **R1** | 改名"锁步"断裂（只改 CSS 不改模板，或反之）→ 静默丢样式（**无编译报错**） | **高** | 每批原子；P5 一致性测试（模板类 ⊆ 样式类）作为护栏；`grep` 旧名零残留作 DoD |
| **R2** | region 锚改名漏改 Agent 写回点 → 区域化 patch 静默失效 | **高** | P4 单批跨仓；改完全仓 `grep region-` 零残留 + 实机写回验证 |
| **R3** | `.msg/.bubble` 属 `DefaultCss` 主体系，波及所有 HtmlViewer 页面 | 中 | 独立批 P2b；改前后对比渲染快照 |
| **R4** | 配置/官方模板若引用旧键名 → 覆盖失效 | 中 | 当前 JSON `templates` 为空（无覆盖）；门禁扫描配置文档 |
| **R5** | 过度改名使可读性下降（`block`/`item` 泛化后难辨） | 低 | 组合命名（`[区域][部件][角色]`）；`dr:` 注释标注"该键服务于哪类结构" |
| **R6** | 门禁误报（注释/示例里的领域词） | 低 | 门禁只扫**模板值/样式/字符串常量**，跳过 `//`/`///` 注释；基线可登记豁免 |

---

## §7 完成定义（DoD）

- [ ] `DefaultTemplates.Keys`、`CssModules.Values`、`Plate.HtmlViewer` 公开符号、`region-*` 锚：**全部 ∩ 负词表 = ∅**
- [ ] `Check-HtmlViewerVocabulary.ps1` 落地并接入 `GateManifests`（`Expect = 0`）；负例可复现
- [ ] `MainProj.sln` / `Agent.sln` 0 错误 0 警告；`Plate.Tests` / `Agent.Core.Tests` 全绿
- [ ] 实机回归：深浅色 / 视图切换 / 树选 / 浮层 / 分页多选 全通过
- [ ] 文档更新：`HtmlAssetKeys` / `DefaultTemplates` / `CssModules` 的键名与正词表一致

---

## §8 附录 · 语义归属总表（"去哪里找语义"）

| 语义 | 允许出现的位置 | 示例 |
|---|---|---|
| 视图切换（三视图） | 配置 `views` 数据 + 域数据 | `viewType=task\|history\|db` |
| 历史 / 任务 | 域数据模型 + 数据键 | `{historyName}` / `{taskName}` |
| 节点 / 运行态 | 域数据模型 | `nodeKey` / `runState.Stage` |
| 会话 / 消息 / 提示层 | 配置模板覆盖（`blockItem` 等）+ 域数据 | `templates.blockItem = "<div class=\"conv-msg\">…"` |
| 领域样式（`conv-*`/`db-*`…） | 域经 `RegisterStyleModule` 注入 | `RegisterStyleModule("grouped", ".conv-node{…}")` |

> 判据：**框架只回答"这长什么样"（结构 + 样式），语义永远由配置/数据的键值给出。**




