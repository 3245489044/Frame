# 主循环区域化信号框架 — 节点与信号驱动的 LLM 模式 设计规格

> **版本**：0.2（范式升级：从「主循环自主读取节点」到「节点 + 信号驱动的 LLM 模式」）
> **日期**：2026-08-31
> **状态**：待评审
> **目标产物**：确立**节点 + 信号**作为主循环的核心驱动范式——历史主循环不再以线性链把节点顺序强拼成单一对话，而是按**功能/模块自动划分群组（区域）**；每次循环**按区域构建上下文**；「最近 / 最有价值」经**节点信号（NodeSignal）**评判；不相关区域**仅经信号框架取最有价值节点作为附加信息**。读取载体 = host 侧 C# MCP 工具 `read_nodes`；指导注入 = L1 skill。

---

## 〇、范式声明（本设计的根）

> 本框架本质要实现的功能是：**节点和信号驱动的 LLM 模式**。以下三点是后续全部设计的根，不满足任一即偏离本范式。

1. **节点是事实载体**：节点（`ur_nodes` 中的 `NodeOutput`，载荷 JSON、端口值经 Codec 解码）是 LLM 决策的唯一事实来源，与「节点即存储单元」既有架构对齐。
2. **信号是推进唯一机制**：信号（外部输入 ∪ 节点/子步骤产出）是触发一轮折叠处理的唯一方式；主循环不靠内部预算自触发层推进（对齐 ANTI-NO-TERMINATION）。
3. **主循环是这一范式的载体**：消费信号 → 按区域构建上下文 → LLM 决策 / 产出节点 → 节点落库并发节点信号 → 再触发。它**不是** task 编排 shell，也**不维护一条全局线性对话**。

本范式把「历史主循环」从「维护 task 顺序、把节点拼成对话」中解放出来：**节点顺序本质是多维且不定的**，交由**区域（功能/模块）**与**节点信号**组织。

---

## 一、背景与问题

### 1.1 现状：主循环仍以线性链强拼节点顺序

当前主循环决策链路仍是「线性对话强拼」形态：

- **历史视图全局线性拼接**：`StandardContextBuilder.BuildHistoryAsync` 把该历史全部 task 对话并集 + 各任务节点产出按**节点键升序**拼成单一消息序列（`StandardContext(History)`），喂给决策导演（`HistoryLoopDecider`）。节点顺序被强压进一条对话。
- **信号驱动折叠沿线性链推进**：`FoldConversationRunner.BuildChain` 从 `RoundSpec.Steps` 构造 `start → step[0] → …` 的**单一 on_success 转移表**，`ChainSignalResolver` 线性推进。主循环（`SkConversationFoldOperator`）每轮都走同一条线性链。
- **「最近」靠线性时序推断**：原设计按 `round desc → round_sequence` 推断最近 + `sticky/priority` 覆盖，与线性对话强拼同构。

**结构缺陷**（正是本次要解决的）：

1. **上下文无限膨胀**：历史上下文是全局线性拼接，多 task 交错、功能混杂，主循环被迫看全量无关节点，`read_nodes` 的「最近优先」仍逃不出线性时序。
2. **「最近」无法表达多维价值**：线性时序只表达「时间新近」，无法表达「该节点在功能上是否当前焦点 / 是否有价值」（价值是多维的）。
3. **节点顺序被强拼成单一对话**：与「节点本质多维且不定」冲突。执行节点与任务节点本应分属不同分组，线性拼接抹平了这种结构。

### 1.2 目标「应该」的样子

历史主循环按**功能 / 模块**自动划分群组，不强行维护节点顺序拼成对话：

- 主循环生成**执行节点** → 归入**执行模块**，自动执行；
- 生成**任务节点** → 归入**任务模块**，按任务划分对应顺序（任务内有序，跨任务不全局强拼）；
- 每次循环处理时，**根据划分的区域和模块构建上下文**（区域化构建）；
- 不相关的区域**只通过信号框架获取最有价值的节点作为附加信息**（不读全量正文）。
---

## 二、整体信号框架（核心）

### 2.1 信号是唯一推进机制

现有信号面：`ExternalInput` / `StepCompleted`（`SubAgentCompleted`）经 `ConversationSignalFoldAdapter` 桥接为 `FoldSignalKind`。本设计在既有信号面上**完善整体信号框架**，引入第三种信号——**节点产出信号 `NodeProduced`**，使「节点」与「信号」真正耦合（本框架本质=节点与信号驱动）。

| 信号 | 触发 | 载荷 | 作用 |
|------|------|------|------|
| `ExternalInput` | 用户 / 外部智能体消息 | 消息文本 | 启动 / 介入 |
| `StepCompleted` | 子智能体 / 子步骤产出 | 产出文本 + 步骤键 | 沿区域推进 |
| **`NodeProduced`（新增）** | 节点落库（`NodeWritePipeline`） | **`NodeSignal`** | 评判最近 / 价值 / 区域，决定读正文 or 摘要 |

信号解析不再依赖单一全局线性链（`ChainSignalResolver` 仅作为区域内子机制），改为**区域化信号解析**（见 §四）。

### 2.2 节点信号 NodeSignal（解决「最近」）

节点落库时（`NodeWritePipeline` 写 `ur_nodes` + `cross_db_link` 边），**同步产出一条 `NodeProduced` 信号**，携带 `NodeSignal`：

| 字段 | 语义 |
|------|------|
| `nodeKey` / `workflowId` | 节点定位 |
| `module` | 功能模块（execution/task/plan/decide/...） |
| `region` | 区域（由 module × taskKey 派生） |
| `nodeType` | 节点类别（execution_node / task_node / ...） |
| `freshness` | 新鲜度（落库时间戳 / round 递增） |
| `valueScore` | 价值评分（由产出评估 eval 结果映射，可选） |
| `outputDigest` | 输出摘要（正文截断摘要，供信号传播免读正文） |
| `sticky` / `priority` | 显式焦点标记 |

**「最近」的评判 = 消费节点信号**（freshness / valueScore / sticky），而非线性 round 时序。主循环据此判定当前焦点节点——这正是「最近通过节点信号进行评判」。

### 2.3 信号价值评估与传播

- 节点信号携带 `outputDigest` + `valueScore`，使**不相关区域无需读正文**即可判断「是否有价值」。
- 区域外附加信息 = 经信号框架取各区域价值最高的 top-k 节点（其 digest + valueScore + sticky）作为附加信息注入；**只有激活区域才读正文**（`read_nodes`）。
- 对齐理念：**不相关区域只通过信号框架获取最有价值的节点作为附加信息**，不读全量正文。
---

## 三、区域与模块划分

### 3.1 模块 Module（功能分组，自动分组依据）

模块 = 功能域。节点写库时标注 `module`，作为**自动分组**的依据：

| 模块 | 语义 | 分组依据 |
|------|------|----------|
| `execution` | 执行模块：主循环生成执行节点 → 自动执行 | module=execution |
| `task` | 任务模块：按任务划分顺序（task 内有序） | module=task + taskKey |
| `plan` | 规划模块 | module=plan |
| `decide` | 决策模块 | module=decide |
| ... | 扩展 | ... |

### 3.2 区域 Region（上下文分组单元）

区域 = 一次循环处理时的**上下文分组单元** = (module × taskKey) 维度（可多级）。

- 每个节点天然归属一个区域（由 module + taskKey 派生）。
- 主循环生成的**执行节点** → 归入 `(execution, *)` 区域 → 执行模块自动执行；
- 生成的**任务节点** → 归入 `(task, taskKey)` 区域 → 该任务内保持顺序。
- **节点顺序多维且不定**：同一历史下节点按区域切分，区域间无全局顺序；区域内部保持其天然顺序（task 内时序 / execution 内时序）。

### 3.3 区域索引

区域索引（由 ur_nodes + cross_db_link 派生，或显式 region 边）：

- `region → 节点集`；`module → 节点集`；`nodeType → 节点集`。
- 供 `read_nodes` 按区域检索、供上下文构建按区域投影。

---

## 四、主循环信号驱动模型（取消线性强拼）

### 4.1 不再以全局线性链强拼

- 弃用「历史主循环把全部 task 对话 + 全部节点拼成单一对话」的 History 视图作为决策唯一输入。
- 折叠解析器不再依赖单一 `RoundSpec.Steps` 全局线性链；改为**区域化信号解析**：
  - 首信号（ExternalInput）→ 解析激活区域（本轮焦点 module/task）；
  - 每步产出（StepCompleted / NodeProduced）→ 按其 region 解析下一步：同区域推进；跨区域按信号价值选取。
- 保留 `ChainSignalResolver` 作为**区域内**线性步骤的可选子机制（task 内顺序），**不作为跨区域全局唯一链**。

### 4.2 区域化上下文构建 RegionScopedContext

每次循环处理时，据激活区域构建上下文，替代全局 `BuildHistoryAsync`：

- **激活区域**：完整正文（其 task 对话 + 该区域节点产出，区域内有序）。
- **非激活区域**：仅经信号框架取 top-k 有价值节点（digest + valueScore + sticky）作为附加信息。
- 输出仍为 `StandardContext.Messages`，但按区域分组（Binding 增 region 绑定或新增 RegionBinding）。

新增构建入口（`IStandardContextBuilder` 或新端口）：

- `BuildRegionAsync(region, ...)`：构建单区域完整上下文。
- `BuildRegionSignalsAsync(regions, topK, ...)`：构建各区域信号摘要（不读正文）。

### 4.3 主循环 = 节点与信号的消费循环

- 消费信号（ExternalInput / NodeProduced）→ 解析激活区域 → 构建区域化上下文 → LLM 决策 → 产出节点 → `NodeWritePipeline` 落库 + 发节点信号 → 再触发。
- 决策端（`HistoryLoopDecider`）注入的 historyView 从「全局线性文本」改为「区域化视图」（激活区域正文 + 信号摘要），导演据此决策而非盲拼全文。
---

## 五、「最近读取原则」（经节点信号评判，D2 重述）

- 排序依据 = 消费节点信号，取代「线性 round desc → round_sequence」：
  - `sticky` / `priority`（显式焦点）恒最前；
  - 其次 `valueScore` 高者提前（最有价值）；
  - 再按 `freshness` 降序（最近）。
- **rank = sticky_first → (valueScore desc) → (freshness desc)**
- 默认按 `freshness` 推断最近（信号缺失时零 schema 改动降级为落库时序）；显式 `sticky`/`priority` 覆盖（焦点恒优先）。

---

## 六、read_nodes MCP 工具（区域化读取载体）

### 6.1 归属与位置

挂 `Agent.Modules.McpServerTools`，与 `answer`/`cline`/`local_llm` 同级，`[McpServerTool]` 暴露。后端数据源 `INodeOutputStore` + 区域索引；工具方法本身零业务逻辑（执行端 DI 注入）。

### 6.2 入参（增强）

| 参数 | 类型 | 必填 | 语义 |
|------|------|------|------|
| `scope` | string | 是 | `node` \| `task` \| `history` \| `workflow` \| **`region`** \| `all` |
| `key` | string | 依 scope | nodeKey / taskKey / historyId / **regionKey** / workflowId |
| `module` | string | 否 | 按功能模块过滤 |
| `nodeType` | string | 否 | 按节点类别过滤 |
| `limit` | int | 否 | 返回条数上限（默认 20） |
| `recentOnly` | bool | 否 | 仅返回最近节点（默认 true） |
| `digestOnly` | bool | 否 | true=只返回信号摘要（digest+valueScore），免读正文 |
| `workflowId` | string | 否 | 节点命名空间（默认 `agent-round`） |

### 6.3 出参（JSON）

```json
{
  "nodes": [
    {
      "nodeKey": "nodes.agent-round.conv_note_1",
      "scope": "region",
      "historyId": "proj-x",
      "taskKey": "proj-x",
      "workflowId": "agent-round",
      "module": "task",
      "region": "task.proj-x",
      "nodeType": "task_node",
      "ports": { "out": "…等 NodeOutput.OutputPorts" },
      "freshness": 171…,
      "valueScore": 0.92,
      "outputDigest": "…摘要…",
      "sticky": true
    }
  ],
  "total": 1,
  "sortedBy": "signal_rank|region"
}
```

### 6.4 实现要点

- 排序由节点信号（freshness/valueScore/sticky）驱动（§五）；`recentOnly=false` 可精确读任意节点（满足「自主读取任意节点」）。
- `scope=region` 检索经区域索引；`digestOnly=true` 仅读 `NodeSignal` 摘要，不触碰正文（对齐「不相关区域只取信号摘要」）。
- 作用域过滤复用人 `EnumerateOutputsScopedAsync`/`TryGetNodeOutputScopedAsync` 的可见性语义（跨工作流仅 Exported 可见）。
- 可观测：每次调用写 `AgentLog.Domain` 诊断（session/scope/key/module/返回条数/sortedBy），便于回查主循环读了什么区域、哪些信号。

---

## 七、L1 skill 注入（「区域化读取 + 信号优先」）

### 7.1 Skill 定位

一个 `L1` 级别、`always: true`、`module: L0` 体系的**描述性 skill**（正文即读取指导），经 DSL 承载由 `PromptCatalog`/`PromptDslSource` 装配，注入代理上下文（L0 system 头之后）。不与 `read_nodes` 绑定执行逻辑，只给模型「何时读、优先读哪类节点、是否免读正文」的原则指导。

### 7.2 SKILL.md front-matter 草案

```yaml
---
name: signal_driven_region_read
description: 主/子循环读取节点时的“信号优先 + 区域化”原则：先经节点信号判断最近/价值/焦点，激活区域读正文，非激活区域只取信号摘要；按模块/区域检索，不全局线性读全量。
kind: lifecycle       # 或 operation
module: l0
always: true
dsl: format
---
读取优先经节点信号判断（最近 = 节点信号 freshness、焦点 = sticky/priority、价值 = valueScore）。
激活区域读完整正文（区域内有序）；不相关区域仅经 read_nodes digestOnly 取最有价值节点作为附加信息，不读全量正文。
按模块/区域检索（execution / task / plan / decide），不把不同功能节点拼成单一对话。
info 不足时应调用 read_nodes 获取节点信号/事实后再决策，而非凭状态盲判。
```

### 7.3 装配位置

- 经 `PromptCatalog`→`PromptDslSource` 进 L0/L1 稳定头（`RoundKeys.L0`/`L1`）。
- 与既有 `SkillInjector` 结合：`always:true` 技能从「技能操作指南块」排除，转由 L0 DSL 承载——避免重复注入。
---

## 八、主循环数据流（节点 + 信号驱动）

```
一轮 begins（ExternalInput / NodeProduced 信号激活）
  ① 种子上下文：L0 头 + L1「区域化读取 + 信号优先」skill + 会话种子（system/conversation/…）
  ② 信号解析：解析激活区域（焦点 module/task）
  ③ 区域化上下文构建：激活区域正文 + 非激活区域信号摘要（top-k）
  ④ 折叠步推进：LLM 决策/盘点步经 Function Calling 调 read_nodes
       - 读激活区域正文（scope=region）或 digestOnly 信号摘要
       - 注入节点信号事实（freshness/valueScore/sticky/region）→ 该步 LLM 上下文
       - 模型据节点信号 + 区域上下文产出下一步意图/决策
  ⑤ 产出决策词（DecisionWordClassifier，stop ↔ 再调用）
  ⑥ 产出节点经 NodeWritePipeline 落库 + cross_db_link 建边 + round_sequence（区域内时序）→ 发 NodeProduced 信号
  ⑦ 若产出带再调用信号 → 再次信号触发下一轮；链终点 run_to_end 收敛
```

---

## 九、错误处理

| 场景 | 行为 |
|------|------|
| `INodeOutputStore` / 区域索引未装配 | `read_nodes` 返回诊断文本（不抛、不阻断），对齐「未装配返回空集」约定 |
| 节点信号缺失 | 降级回线性时序（freshness=落库时序）推断最近，零回归 |
| 区域索引未装配 | 回退全局 `BuildHistoryAsync` 视图，零回归 |
| 目录/存储不可达 | 返回 `{"status":"error",...}`，主循环回退纯聊天路径 |
| scope/key/module 非法 | 返回明确诊断；`read_nodes` 不做猜测 |
| 节点无输出 | 返回空 nodes 列表（不报错） |

---

## 十、测试

1. `NodeSignal` 单元测试：节点落库 → 节点信号生成（module/region/freshness/valueScore/digest）与传播。
2. 区域索引单元测试：`region`/`module`/`nodeType` 检索。
3. 区域化上下文构建测试：激活区域读正文（区域内有序）、非激活区域仅信号摘要（不读正文）。
4. `read_nodes` 检索测试：`scope=region`、`module`/`nodeType` 过滤、`digestOnly` 免读正文、信号排序（sticky→valueScore→freshness）。
5. 主循环 `NodeProduced` 再触发集成测试：节点落库发信号 → 激活下一轮折叠；未装配回退全局视图零回归。
6. L1 skill 装配测试：`always:true` + `dsl:format` 技能进入 L0/L1 稳定头；不进 `SkillInjector` 操作指南块（防双注入）。

---

## 十一、落地分期

| 阶段 | 内容 | 验收 |
|------|------|------|
| P0 | 节点信号 `NodeSignal`：`NodeWritePipeline` 落库发 `NodeProduced` 信号 + 节点 module/region/nodeType 标注；`read_nodes` 增 module/nodeType/digestOnly | `NodeSignal` 单测通过；`read_nodes` 可返信号摘要 |
| P1 | 区域索引 + 区域化上下文构建（`BuildRegionAsync` / `BuildRegionSignalsAsync`） | 区域化构建单测通过；非激活区域不读正文 |
| P2 | 主循环改消费 `NodeProduced` + `HistoryLoopDecider` 注入区域化视图 | 集成测试：主循环消费节点信号后决策 |
| P3 | L1 skill 更新为「区域化读取 + 信号优先」；可观测诊断（读量/区域/排序依据） | skill 装配进 L0/L1；诊断日志含区域/信号排序 |

---

## 附：设计边界（不做什么）

- 不把读节点做进 `HistoryMainLoop` 纯编排层——那是零 LLM 调度外壳，读节点是决策脑（信号循环）的职责。
- 不新增独立存储引擎——区域索引由 ur_nodes + cross_db_link 派生，节点信号复用节点落库点。
- `read_nodes` 只读不改——写入/更新仍走 `NodeWritePipeline`，避免读写职责杂交。
- 不弃用既有线性链能力——`ChainSignalResolver` 降为「区域内」有序子机制（task 内顺序），不做跨区域全局唯一链。
- 信号框架未装配时一律降级回退（线性时序 / 全局 History / 纯聊天），零回归。

---

参考：`SignalDrivenFoldEngine.cs`、`ChainSignalResolver.cs`、`FoldSignal.cs`、`RuleSignalResolver.cs`、`ConversationSignalFoldAdapter.cs`、`SkConversationFoldOperator.cs`、`ConversationFoldVertex.cs`、`FoldConversationRunner.cs`、`NodeWritePipeline.cs`、`NodeWriteKeys.cs`、`StandardContextBuilder.cs`、`IStandardContextBuilder.cs`、`HistoryMainLoop.cs`、`HistoryLoopDecider.cs`、`HistoryDecisionMapper.cs`、`TaskOrchestrator.cs`、`McpServerTools.cs`、`INodeOutputStore.cs`、`NodeOutput.cs`、`NodeOutputStore.cs`、`NodeTaskLinker.cs`、`SkillInjector.cs`。