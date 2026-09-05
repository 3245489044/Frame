# AgentRuntimeUI — 主对话框与 LLM 上下文绑定校验框架重构方案

> **版本**：v0.2　**日期**：2026-08-30
> **范围**：`FileDriven/DrivenConfig/JsonConfig/AgentRuntimeUI.json`（④ 主对话框）的历史显示，与 Agent 会话 LLM 输入（`conversation_context` + `provide_prompt` 装配的 system 提示）在框架层的绑定与一致性校验
> **性质**：重构方案文档（规划，非实施变更）
> **关联**：`docs/analysis/2026/8/8.28/AgentRuntimeUI_历史主循环调用链.md`、`docs/analysis/2026/8/8.30/AgentRuntimeUI_主对话框重复冗余错误逻辑分析.md`、`Projects/Agent/doc/Answer接收完整对话对齐Cline-重构方案.md`、`Projects/Agent/doc/AI交互标准上下文三粒度重构方案.md`

---

## 一、背景与动机

### 1.1 设计目的（本方案的硬约束）

主对话框的显示内容，必须与发送给 LLM 的输入**完全一致**，**不得包含任何额外逻辑或额外展示内容**。

```
UI 显示 ≡ LLM 输入      （双向逐字一致，非子集关系）
```

- 不是"LLM ⊆ UI"，而是 **UI == LLM**：两边消息集合相等、每条内容逐字相同。
- 任何额外展示——中间业务节点、Skill 末尾节点、重算/近似的提示词、旁路合成内容——都属于**"额外逻辑"**，必须移除。
- 主对话框只是 LLM 输入的一个**等价渲染视图**：LLM 收到的是 `[{role, content}]` wire，UI 收到的是气泡 HTML，但**承载的内容与集合完全一致**。

### 1.2 为什么需要框架层绑定

当前 UI 显示与 LLM 输入**各自拼装、不同源、无标注、无法自动校验**，且 UI 存在独立的展示层逻辑（`ConversationScenarioProjector` 的中间节点/末尾节点/重算近似），正是"额外逻辑"。需要一个框架把两者绑定到同一条规范序列，并在框架层校验"完全一致、零额外"。

---

## 二、现状与差距（已核实）

| 维度 | 主对话框（UI 视图） | 发给 LLM（LLM 视图） |
|---|---|---|
| 对话历史 | ur_histories（user/assistant/sub-agent） | `RenderWire(log)` = `log.Messages`（同源） |
| ur_nodes `output` 节点产出 | ✅ `StandardContextBuilder.BuildTaskAsync` 合成 | ❌ 不在 `conversation_context` |
| 头提示层 L0/L1/L2/Skill 块 | ✅ `ConversationScenarioProjector` 展示（**模板/重算近似**） | 走 system（`provide_prompt` 装配，**填槽真实值**） |
| 中间业务节点 / Skill 末尾节点 | ✅ 展示层（**额外逻辑**） | ❌ |
| **来源** | `BuildTaskAsync` + `EnrichAsync` | `RenderWire(log)` + `provide_prompt` |

### 2.1 四个关键缺口

1. **G1 — system 提示 UI 与 LLM 不一致**：LLM 经 `provide_prompt` 收到**填槽后的 L1 全文 + 该轮真实选中的 Skill 块**；UI 头提示层用 **L1 模板（未填槽）+ 渲染时现场重算的 Skill**。UI ≠ LLM。
2. **G2 — history 不同源**：LLM `conversation_context` 来自 `log.Messages`（`ConversationPipeline.cs:152,278`），UI 用 `BuildTaskAsync`（含 ur_nodes output）。UI 与 AI 未真正同源（TH-12 背离）。
3. **G3 — 无标注无校验**：两侧消息无 `Origin/Feed/AnchorKey` 标注，无法自动判定"该进 LLM / 仅展示"，无法校验 `UI ≡ LLM`。
4. **G4 — UI 存在额外展示逻辑**：`ConversationScenarioProjector` 叠加中间业务节点、Skill 末尾节点、重算近似提示词——这些**不是 LLM 输入**，违反"完全一致、零额外"。须移除。

---

## 三、重构目标与原则

### 3.1 目标
- 把 UI 显示与 LLM 输入收敛到**同一条规范消息序列**（单一真相源）。
- 为每条消息标注**来源域 / 投喂目标 / 稳定锚点键**，形成可机检绑定契约。
- 框架层提供**一致性校验**，硬性保证 `UI ≡ LLM` 且**零额外逻辑**。

### 3.2 原则
| 原则 | 语义 |
|------|------|
| 单一真相源（SSOT / TH-12） | UI 与 LLM 同源自 `CanonicalContext`，消除 `RenderWire(log)` 与 `BuildTaskAsync` 双源 |
| 投影同构 | `UiView` 与 `LlmView` 是**同一份内容**的两种渲染（气泡 HTML / wire JSON），集合与文本逐字相等 |
| 可校验契约 | 每条消息带 `Origin/Feed/AnchorKey`，框架据此双向比对 |
| 写侧捕获 | LLM 实际装配的 system 值（L0/L1/L2/SkillBlock）在提供时落快照，供 UI 与 LLM 共用同一份 |
| 零额外逻辑 | 禁止展示层、旁路合成、重算近似；UI 不得出现 LLM 输入之外的任何消息 |

---

## 四、核心不变量

```
I1（双向相等）:  ∀m ∈ UiView : ∃m′ ∈ LlmView : AnchorKey(m′)=AnchorKey(m) ∧ Text(m′)=Text(m)
                ∧ ∀m ∈ LlmView : ∃m′ ∈ UiView : AnchorKey(m′)=AnchorKey(m) ∧ Text(m′)=Text(m)
                // UI 与 LLM 输入逐条一一对应、内容逐字相等
I2（零额外）  :  ∄m ∈ UiView : m ∉ LlmView（UI 不存在 LLM 输入之外的任何消息）
I3（system）  :  LlmSystem[prompt.*] ≡ UiView[prompt.*]（哈希一致）
I4（键）      :  AnchorKey 全局唯一、可被 SplitWfKey/映射表解析（无悬空/重复）
```

> 与 v0.1 的区别：I1 从 `LLM ⊆ UI` 收紧为 **`UI ≡ LLM` 双向相等**；I2 由"展示层可豁免"反转为"**禁止任何展示层/额外逻辑**"。

---

## 五、框架架构（四层）

```
┌─────────────────────────────────────────────────────────────┐
│ ① 规范层 CanonicalContextBuilder（单一真相，升级 StandardContextBuilder）│
│    Canonical = ur_histories 对话 + ur_nodes output 产出        │
│    每条带  Origin / Feed / AnchorKey                          │
├─────────────────────────────────────────────────────────────┤
│ ② 投影层  LlmProjector ⇄ UiProjector（同构，内容一致）           │
│    LlmView = Canonical(history) + provide_prompt(system)      │
│    UiView  = 同一份内容的气泡渲染（无任何额外展示层）             │
├─────────────────────────────────────────────────────────────┤
│ ③ 绑定层  ConversationContextBinding                          │
│    AnchorKey ↔ 消息 双向索引（Ui↔Llm 一一对应）                 │
├─────────────────────────────────────────────────────────────┤
│ ④ 校验层  ContextBindingValidator                             │
│    I1 双向相等 / I2 零额外 / I3 system逐字 / I4 键唯一可解析      │
└─────────────────────────────────────────────────────────────┘
```

> 关键：`UiView` 与 `LlmView` **从同一条 Canonical + 同一份 system 快照派生**，`UiProjector` 只做渲染不增内容。`UI ≡ LLM` 由构造保证，校验器验证无遗漏、无多余、内容逐字相等。

---

## 六、消息模型与绑定元数据

新增（纯 Share 层，可单测）。**不引入"仅展示"来源**——所有来源都是真实投喂 LLM 的内容：

```csharp
public enum MessageOrigin
{
    HistoryLog,        // ur_histories 对话（user/assistant/sub-agent）→ LLM history
    NodeOutput,        // ur_nodes NodeType=output → LLM history
    SystemPrompt,      // L0/L1/L2 提示层 → LLM system
    SkillBlock,        // skill.{name}.block 头块 → LLM system
}

[Flags] public enum FeedTarget { LlmHistory, LlmSystem, Both }

public sealed record MessageBinding(
    string AnchorKey,          // wf.{wf}.{node} / history.{seq} / prompt.l0 / skill.{name}.block
    MessageOrigin Origin,
    FeedTarget Feed);

public sealed record CanonicalMessage(ConversationMessage Message, MessageBinding Binding);
public sealed record CanonicalContext(StandardScope Scope, string ScopeId, IReadOnlyList<CanonicalMessage> Messages);
```

- **AnchorKey 复用既有约定**：`wf.{wf}.{node}`（节点，`SplitWfKey` 可解析）、`history.{seq}`（对话序号）、`prompt.l0/l1/l2`、`skill.{name}.block`。
- **无 `UiOnly` / `DisplayOnly`**：v0.1 的 `UiOnly`/`DisplayOnly` 是"额外展示"的入口，与"零额外逻辑"矛盾，**移除**。UI 只显示 `Feed ∈ {LlmHistory, LlmSystem, Both}` 的消息。

---

## 七、写侧捕获（ProvidePromptCapture）—— 消除 G1

provide_prompt 装配结果是 ephemeral（L1 在内存 `L1Cache`、SkillBlock 不落盘、L2 每步生成），UI 事后无法还原"LLM 实际喂的"。新增写侧捕获：

```
写侧 ProvidePromptCapture（注入 PromptFrameworkFunction 装配后）
  → 把 { l0, l1(填槽全文), l2, skillBlock, selectedSkills, round, taskKey }
     落 state.Data["__prompt_snapshot"]（按任务/轮，随快照持久化）

读侧 LlmProjector / UiProjector
  → system 槽位（LlmView）与头提示层（UiView）都从 __prompt_snapshot 取同一份装配值
```

---

## 八、投影层

### 8.1 LlmView（发给 LLM）
```
LlmView = Canonical(HistoryLog ∪ NodeOutput, Feed ∈ {LlmHistory, Both})  → conversation_context
        + ProvidePromptCapture(system): l0 / l1(填槽) / l2 / skillBlock   → system 槽位
```
`ConversationPipeline.SeedWorkflow` 的 `conversation_context` 从 `RenderWire(log)` 改为 `LlmProjector(canonical).RenderToWire()`（修复 G2 / TH-12）。

### 8.2 UiView（主对话框显示）
```
UiView = 同一份 Canonical + 同一份 __prompt_snapshot 的气泡渲染
         （不新增、不删减、不重算，仅渲染形式不同）
```
- 头提示层（L0/L1/L2/Skill）与 `LlmView.system` 共用 `__prompt_snapshot`；
- 对话/节点产出与 `LlmView.history` 共用同一条 Canonical。

### 8.3 移除额外逻辑（G4 修复）
- **删除** `ConversationScenarioProjector` 的展示层：中间业务节点（NodeType≠output）、Skill 末尾节点（`skill.{name}.end`）、重算近似提示词；
- `UiProjector` 只做**渲染镜像**，不含任何旁路合成；
- 保留折叠/样式渲染能力（`prompt-layer` 模板 + CSS），但**数据一律来自 Canonical / `__prompt_snapshot`**。

---

## 九、绑定层

```csharp
public sealed class ConversationContextBinding
{
    IReadOnlyDictionary<string, ConversationMessage> UiByKey;         // AnchorKey → Ui 消息
    IReadOnlyDictionary<string, ConversationMessage> LlmHistoryByKey; // AnchorKey → Llm history
    IReadOnlyDictionary<string, string> SystemByKey;                  // prompt.l0/l1/l2, skill.{name}.block

    ConversationMessage? ResolveUiToLlm(string anchorKey);
    ConversationMessage? ResolveLlmToUi(string anchorKey);
}
```

在生成 LlmView 与 UiView 的同一处（组合根 `ContextBindingService`）构建，保证两边键一致、一一对应。

---

## 十、校验层

```csharp
public sealed record BindingViolation(ConversationMessage Msg, string Rule, string Detail);

public static class ContextBindingValidator
{
    public static IReadOnlyList<BindingViolation> Validate(
        CanonicalContext canonical, IReadOnlyList<ConversationMessage> uiView, LlmInput llmView);
}
```

### 10.1 校验规则（双向一致 + 零额外）

| # | 规则 | 语义 | 对应反模式 |
|---|---|---|---|
| R1 | **UI ⊆ LLM** | `∀m ∈ UiView : ∃m′ ∈ LlmInput : AnchorKey 相等 ∧ Text 相等`（UI 不出现 LLM 输入之外的任何消息） | `ANTI-BLOAT` / `ANTI-DUPLICATED-IMPLEMENTATION` |
| R2 | **LLM ⊆ UI** | `∀m ∈ LlmInput(History ∪ System) : ∃m′ ∈ UiView : AnchorKey 相等 ∧ Text 相等`（LLM 喂的每一条都显示） | `ANTI-RAW-OUTPUT-LOSS` / `ANTI-UNLOGGED-FIX` |
| R3 | system 逐字 | `LlmSystem[prompt.*] ≡ UiView[prompt.*] ≡ __prompt_snapshot`（哈希一致） | `ANTI-FIDELITY-LOSS` |
| R4 | 键唯一 | AnchorKey 全局唯一，无 `wf.` 冲突/重复 | `ANTI-CYCLIC-AGGREGATION` / `SingleImplementation` |
| R5 | 锚点可解析 | AnchorKey 能被 `SplitWfKey`/映射表解析，无悬空键 | `ANTI-PLACEHOLDER-PARAM` |
| R6 | **零额外** | `UiView` 不存在 `Feed∉{LlmHistory,LlmSystem}` 或未绑定 LLM 输入的消息（无展示层） | `ANTI-STRUCTURE-DRIFT` |

> R1 + R2 合起来即 **`UI ≡ LLM` 双向相等**（I1）；R6 兜底"零额外逻辑"（I2）。

### 10.2 差异报告输出
`{ rule, anchorKey, role, source: Ui|Llm|System, detail }` → 诊断 sink / `state.Data["__context_binding"]` 落盘（重启可查、跨轮可校验）。

---

## 十一、接入点

| 层 | 接入位置 | 改动 |
|---|---|---|
| 写侧捕获 | `PromptFrameworkFunction` 装配后 | 落 `__prompt_snapshot` |
| 生成规范 | `StandardContextBuilder` → 升级 `CanonicalContextBuilder` | 每条带 Binding |
| LLM 投影 | `ConversationPipeline.SeedWorkflow`（`conversation_context`）→ `LlmProjector(canonical)` | **消除 `RenderWire(log)` 双源（G2）** |
| UI 投影 | `ConversationViewService.RenderViewAsync` 的 `EnrichAsync` → `UiProjector(canonical)` | **移除展示层 + 头提示层改读真实装配值（G1/G4）** |
| 绑定构建 | 组合根 `ContextBindingService`（Agent.Launcher/Orchestration） | 建 AnchorKey 双向索引 |
| 校验触发 | ① `SendAsync` 后 ② `RenderViewAsync` 时 ③ 单测 | 差异写诊断 / 可选 strictConfig fail-fast |
| 快照 | `__prompt_snapshot` + `__context_binding` | 重启可查、跨轮可校验 |

---

## 十二、演进路径（里程碑）

| 里程碑 | 内容 | 验收 |
|---|---|---|
| **P0** | `MessageOrigin/FeedTarget/MessageBinding/AnchorKey` 元数据模型 + `__prompt_snapshot` 写侧捕获 | Share 层单测；provide_prompt 后快照可查 |
| **P1** | `CanonicalContextBuilder` 统一产出；`conversation_context` 改用 Canonical（替换 `RenderWire(log)`） | UI 与 LLM history 同源；既有 cline/answer 投喂回归通过 |
| **P2** | UI 头提示层改读 `__prompt_snapshot`；**移除 `ConversationScenarioProjector` 展示层（中间/末尾节点、重算近似）** | UI 显示 ≡ LLM 输入（无额外消息） |
| **P3** | `ConversationContextBinding` + `ContextBindingValidator`（R1–R6）+ 差异报告 + 快照 | 校验报告输出；`UI ≡ LLM` 可机检 |
| **P4** | strictConfig 门禁 + `CentralTest` 一致性单测（"Ui ≡ Llm 双向逐字"断言） | 门禁 fail-fast；单测全绿 |

---

## 十三、风险与权衡

1. **展示层移除**：中间业务节点、Skill 末尾节点不再在 UI 显示（LLM 也不喂它们），`ConversationScenarioProjector` 的展示逻辑删除；若日后需要看这些，应作为独立的"诊断视图"而非主对话框，不违反"主对话框 ≡ LLM"。
2. **行为兼容（P1）**：`conversation_context` 从 `log.Messages` 换成 Canonical 会多出 ur_nodes output，需回归确认 cline/answer 语义；可加"仅追加 output、保持 history 顺序"兼容开关。
3. **写侧快照体积**：`__prompt_snapshot` 每轮存 L0/L1/L2/SkillBlock 全文，随快照持久化；按轮滚动或仅存本轮，避免无限增长（对齐 `QuantificationConstraint` / 归档只增不丢）。
4. **L1 填槽差异**：UI 与 LLM 都从 `__prompt_snapshot` 取同一份，天然一致；禁止 UI 另用模板重算。
5. **校验成本**：按任务粒度 + 增量（仅新 AnchorKey）校验，避免每轮全量。

---

## 十四、验收标准

1. 主对话框显示的内容与 `conversation_context`（LLM history）+ `provide_prompt` system 提示（L0/L1/L2/Skill）**双向逐字一致**（`I1`）。
2. `ContextBindingValidator` 对任意会话返回 `R1`/`R2` 无违规（`UI ≡ LLM`）。
3. `R6` 无违规：UI **不存在任何** LLM 输入之外的额外消息（无展示层/旁路/重算近似）。
4. UI 头提示层与 LLM 实际喂的 L1/Skill 块**内容哈希一致**（`I3`）。
5. 现有 `ConversationPipeline` / cline / answer 投喂链路回归通过，无新增 LLM 上下文内容。
6. `CentralTest` 增加"Canonical → Llm → Ui 双向逐字一致 + 零额外"单测，全绿。

---

> **一句话总结**：把 UI 与 LLM 收敛到同一 `CanonicalContext` + 同一 `__prompt_snapshot`（单一真相），`UiView` 只是 `LlmView` 的等价渲染、**不增不减不重算**，`ContextBindingValidator` 以 `UI ≡ LLM` 双向相等 + **零额外逻辑**（R6）为硬性不变量在框架层校验。

