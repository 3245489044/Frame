# LocalLlm 规则引擎框架化方案（Decision Rules Framework）

> **版本**：1.0　**日期**：2026-09-01
> **实现状态（2026-09-01 落地）**：规则引擎已通用化迁入 `CsharpPrj\Strategy\DecisionRules`（命名空间 `Strategy.DecisionRules`），
> 以 `BasicMethod\Map\DecisionEngine` 为底座（复用 `ConditionEvaluator` 条件求值 / `CompositeDecisionEngine` 引擎组合），
> 覆盖不了的声明式/匹配/输出/文件化逻辑在框架层扩充。原 `Agent.Rules` 项目已删除，`Agent.Contract` 直接引用新框架（P3 通用化完成）。
> **范围**：把 `Projects/Agent/Config/agent-config.json → AgentConfig:LocalLlm:Rules` 中内嵌的规则匹配机制
> （`LocalLlmRule`：`Match / Mode / Response / ToolName / ToolArguments` + `LocalLlmClient` 的 `Matches / Render / BuildToolCall`）
> **提取并重构为一个通用、规范、可扩展的规则匹配框架**（Decision Rules Engine），可脱离 LocalLlm 独立复用、可校验、可文件化、可热更。
> **性质**：设计方案（不修改代码，本文为唯一权威，实现按本文执行）。
> **关联**：`LocalLlm函数调用模拟方案.md`（tool_call 能力，Phase 1/1.5）、`Agent自由调用注册函数_skill实时装配方案.md`（skill 实时装配）、
> `.clinerules`（SingleImplementation / TH-12 / GlobalDecisionKey / LO.Resolve(table/chain)）。

---

## 一、现状梳理：当前规则引擎的问题

现状（`Agent.Contract/Llm/LocalLlmClient.cs` + `agent-config.json`）：

| 现状 | 问题 |
|------|------|
| 规则内嵌在 `agent-config.json` 的 `LocalLlm.Rules`（单行数组） | 与 Agent 配置强耦合；规则与运行配置不分；无法单独版本管理/多人协作/校验 |
| `LocalLlmRule` 只有 `Match / Mode / Response / ToolName / ToolArguments` | 语义单薄：`Mode` 兼任「输出类型」与「匹配通道」双重含义（`isJson != jsonMode` 隐式分流） |
| 匹配逻辑（`Matches`）散落在 `LocalLlmClient`：子串 / `regex:` / `role:` 三态硬编码 | 匹配策略不可枚举、不可扩展（加一种匹配需改引擎核心）；无组合（AND/OR/not） |
| 输出逻辑（`Render` 占位符 + `BuildToolCall`）与匹配耦合在一个方法 `ResolveResponse` | 输出器不可插拔；新增输出类型需改核心 |
| 无规则 schema / 校验 / lint；写错静默不命中或恒命中 | 不可观测、不可防御；正则写错 `catch` 吞掉难排查 |
| 无优先级语义（靠数组顺序） | 冲突裁决不显式、不可声明（对齐 GlobalDecisionKey 缺 Layer/Specificity） |
| 无独立规则文件 / 热重载 / 审计粒度 | 规则变更需重启；审计只有 `CallLog` 无规则命中明细 |
| 只服务 `LocalLlmClient` | 不可被其它决策点复用（调度/路由/护栏/工具选择各自重写一套 if-else） |

---

## 二、目标与非目标

### 2.1 目标

1. **提取**：把「匹配 → 选择 → 渲染 → 执行」从 `LocalLlmClient` 抽出为独立规则引擎；
2. **通用**：引擎不感知 LocalLlm；任何"按输入文本/上下文决定输出或动作"的决策点可复用（LLM 后端、调度、路由、护栏、工具选择）；
3. **规范**：规则有正式 Schema（JSON Schema 可校验）、枚举化匹配/输出策略、显式优先级与兜底、lint 与审计；
4. **完善**：支持匹配策略扩展（子串/regex/role/精确/通配/JSONPath/语义预留）、条件组合（AND/OR/not）、输出类型扩展（text/json/tool_call/结构化/委托）、规则文件化 + 热重载；
5. **兼容**：LocalLlm 现有行为零回归（旧规则可平滑迁移）。

### 2.2 非目标

- 不改 LocalLlm 的返回语义（`LlmResult.ToolCalls` 等既有契约）；
- 不引入编排/工作流（规则引擎只管"单条输入→裁决输出"，不做多步状态机）；
- 不替代 clinerules（那是静态规范库；本框架是运行时确定性裁决引擎）。

---

## 三、总体设计：模块边界 + 分层

```
┌─ 规则定义层（Rule 模型 / Schema / 文件）──────────────────────────────┐
│  RuleSet = { id, version, priority?, rules[], default }               │
│  来源：规则文件 *.rules.json（首选） / agent-config 内联（兼容降级）      │
└───────────────────────────────────────────────────────────────────────┘
┌─ 规则引擎层（DecisionRulesEngine，独立于 LocalLlm）────────────────────┐
│  Match（匹配器） → Select（选择：优先级/打分） → Render（模板渲染）       │
│  → Execute（输出器/动作/工具调用） → Fallback（默认/兜底）                │
│  横切：Validator（校验） / Audit（命中明细） / Cache（规则集缓存+热重载）   │
└───────────────────────────────────────────────────────────────────────┘
┌─ 消费层 ─────────────────────────────────────────────────────────────┐
│  LocalLlmClient 委托引擎（旧 Rules 映射为新 RuleSet）                    │
│  其它决策点（调度/路由/护栏/工具选择）按需接入（复用，SingleImplementation）│
└───────────────────────────────────────────────────────────────────────┘
```

- **放哪**：规则定义 + 引擎核心放独立命名空间/库（如 `Agent.Contract.Rules` 起步，后续可拆独立 csproj），消费方只依赖引擎端口，不反向依赖 LocalLlm。

---

## 四、规则模型（Rule / RuleSet）

### 4.1 RuleSet（规则集）

```jsonc
{
  "id": "local-llm-default",
  "version": "1",
  "priority": "ordered",          // ordered（数组序）| declared（Rule.priority 权重）
  "caseSensitive": false,
  "rules": [ /* Rule[] */ ],
  "default": { /* Rule：兜底输出 */ }
}
```

### 4.2 Rule（规则）—— 从「Match+Mode 平铺」升级为「条件 + 输出」分离

```jsonc
{
  "id": "r-create-task",
  "comment": "命中"创建任务"→ 输出一个 create_node_reference 工具调用",
  "priority": 10,                         // 显式优先级（越大越先；缺省按数组序）
  "when": {                                // 条件（可组合，见 §六）
    "all": [
      { "match": { "field": "user", "strategy": "regex", "value": "LLM_MCP_DEMO|请用create_node_reference创建任务" } }
    ]
  },
  "then": {                                // 输出（见 §七）
    "type": "tool_call",
    "toolName": "create_node_reference",
    "arguments": "{\"TaskKey\":\"tp_llm_mcp_demo\",\"SchedulingMode\":\"Hybrid\"}"
  }
}
```

> 关键拆解：把旧 `Match`（匹配）+ `Mode`（既是通道又是输出类型）分离为 **`when`（条件）** 与 **`then`（输出/动作）**，两者可独立扩展。

---

## 五、匹配器（MatchStrategy 枚举化 + 扩展点）

`match` 统一为 `{ field, strategy, value, options? }`，`field` 从消息数组/上下文取（`system / user / history / messages / role:<名> / context.<key>`）。

| strategy | 语义 | 现状 |
|----------|------|------|
| `contains`（子串） | 包含即命中 | ✅ 既有（默认） |
| `regex` | 正则匹配 | ✅ 既有（`regex:` 前缀） |
| `role` | 按角色聚合匹配 | ✅ 既有（`role:名:子串`） |
| `exact` | 全等 | 🆕 规范化 |
| `startsWith` / `endsWith` / `wildcard` | 前缀/后缀/通配 | 🆕 规范化 |
| `jsonPath` | 对结构化字段取值比较 | 🆕 预留（工具参数/上下文对象） |
| `semantic`（向量） | 语义近似 | 🆕 预留（对接向量检索，阈值注入） |
| `always` | 恒命中（兜底） | ✅ 既有（空 Match） |

> 规则引擎只依赖 `IMatchStrategy` 端口，新增匹配 = 注册一个策略实现，不改核心（Open-Closed）。

---

## 六、条件组合（谓词 AND/OR/not）

```jsonc
"when": {
  "all": [ { "match": {...} }, { "not": { "match": {...} } } ],   // AND + 取反
  "any": [ { "match": {...} } ]                                    // OR
}
```

- 组合算子：`all`（合取，默认）/ `any`（析取）/ `not`（取反）；可嵌套；
- 语义对齐 `.clinerules` 三值逻辑（`TruthValueDomainLogic`：⊥ 吸收）与 `LO.Assess(conjunction)`；
- 缺省 `when` = 恒命中（兜底规则）。
---

## 七、输出器（OutputStrategy 枚举化 + 扩展点）

`then` 的 `type` 决定输出形态；由 `IOutputStrategy` 端口分发，新增输出 = 注册实现：

| type | 语义 | 产出 |
|------|------|------|
| `text` | 渲染文本（占位符模板） | `LlmResult.Content` |
| `json` | 渲染 JSON（对象/字符串） | `LlmResult.Content`（JSON 文本） |
| `tool_call` | 一次工具调用 | `LlmResult.ToolCalls`（Name + Arguments） |
| `tool_calls` | 多次工具调用 | `LlmResult.ToolCalls[]` |
| `structured` | 结构化对象（非 JSON 字符串） | 对象透传（供非 LLM 消费方） |
| `delegate` | 委托 C# 函数（动态动作） | 任意（含副作用，如写库/触发） |

> 占位符模板统一由 `ITemplateRenderer` 渲染（沿用 `{system}/{user}/{history}/{index}/{messages}/{last_assistant}/{last_message}/{role:<名>}`，并预留 `{context.<key>}`）。

---

## 八、规则引擎核心（匹配 → 选择 → 渲染 → 执行 → 兜底）

```
DecisionRulesEngine.Evaluate(context, request)
  → LoadRuleSet(cache)                       // 缓存 + 版本校验 + 可选热重载
  → 逐条规则：MatchWhen(rule.when, ctx) 命中?   // 短路
  → Select：按 priority/数组序 取最高命中规则
  → Render：占位符模板（命中规则的 then）
  → Execute：按 OutputStrategy 产出（text/json/tool_call/…）
  → 无命中 → Fallback(default 规则)
  → Audit：记录 命中规则 id / 匹配字段 / 输出类型 / 兜底与否
```

- **引擎端口**（供消费方注入，保持 LocalLlm 无关）：
  - `IRuleSetLoader`（文件/内联/内存）
  - `IRuleSetValidator`（JSON Schema + 语义 lint）
  - `IMatchStrategyRegistry` / `IOutputStrategyRegistry`（扩展点注册表）
  - `IRuleAuditSink`（命中明细，接 `CallLog` / `IObservabilitySink`）

---

## 九、配置分层与文件化（agent-config 分离 + 热重载）

| 层 | 载体 | 用途 |
|----|------|------|
| 全局默认 | `Agent.Contract` 内建 RuleSet | 零配置可用 |
| 规则文件 | `Projects/Agent/Config/rules/*.rules.json` | **首选**：独立文件、可版本管理、可多人协作、可 lint |
| agent-config 内联 | `LocalLlm.Rules`（旧形态） | 兼容降级：缺失规则文件时回退旧内联，**零回归** |

- 优先级：**规则文件 > agent-config 内联 > 全局默认**；
- `agent-config.json` 的 `LocalLlm.Rules` 迁移为 `LocalLlm.RulesFile: "rules/local-llm.rules.json"`（可选，缺省回退内联）；
- **热重载**：`IRuleSetLoader` 支持按文件 LastWriteTime 失效缓存（`ReloadOnChange` 开关），规则变更免重启；
- **Schema 校验**：提供 `*.rules.schema.json`（JSON Schema），加载期校验 + 开发期 lint（对齐 `.clinerules` linter 的"校验四类检查"思路）。

---

## 十、与 LocalLlm 集成（兼容路径，零回归）

- `LocalLlmClient` 内部 `ResolveResponse / Matches / Render / BuildToolCall` **委托**给 `DecisionRulesEngine`（旧 `LocalLlmRule` 映射为新 `Rule`：`Match→when.match(contains/regex/role)`、`Mode/Response/ToolName/ToolArguments→then.type/...`）；
- `LocalLlmRule` / `LocalLlmOptions.Rules` 保留为**兼容壳**（解析时转换），或标记 `deprecated` 走迁移；
- `ChatAsync/ChatMessagesAsync/GenerateJsonAsync/ChatWithToolsAsync` 四个入口仍归一到引擎的 `Evaluate`，语义与既有 `CallLog` 审计一致；
- 消费层（`LlmToolExecutor` / `SkConversationFoldOperator` / `StatelessAnswerExecutor` / `agent_mcpinvoke` 测试）**零改动**——只换后端裁决实现。

---

## 十一、优先级与冲突裁决（对齐 GlobalDecisionKey）

多条规则同时命中时，裁决键（字典序）：

```
GlobalRuleKey = ⟨Priority, Specificity, Order⟩
```

- `Priority`：显式声明的优先级（int，越大越先；缺省 0）；
- `Specificity`：匹配策略精确度（exact > jsonPath > wildcard > regex > contains > role > always，由引擎按策略归一给分）；
- `Order`：规则数组序（最终 tie-break，确定性）。

> 语义对齐 `.clinerules GlobalDecisionKey` 的字典序比较思路，但作用在运行时规则（非公理），两者不冲突。

---

## 十二、落地路径（Phase）+ 验收

| Phase | 内容 | 验收 |
|-------|------|------|
| **P0** | 定义 `RuleSet / Rule / Match / Output` 模型 + JSON Schema + `IMatchStrategy/IOutputStrategy` 端口（纯新增，不动 LocalLlm） | 模型/端口编译通过；Schema 可校验示例规则文件 |
| **P1** | 实现 `DecisionRulesEngine` 核心（Match→Select→Render→Execute→Fallback）+ `LocalLlmClient` 委托接入（保留旧 `LocalLlmRule` 兼容壳） | `LocalLlmToolCallTests` 全绿（零回归）+ 新增引擎单测；`agent_mcpinvoke` 冒烟通过 |
| **P2** | 规则文件化：`Config/rules/*.rules.json` + `RulesFile` 引用 + 热重载 + lint | 规则从 agent-config 迁出；改文件不重启生效；`CallLog` 记录命中规则 id |
| **P3** | 通用化：抽独立命名空间/库 + 复用接入（调度/路由/护栏试点） | 至少一个非 LocalLlm 决策点接入；`LocalLlmRule` 标记 `deprecated`（迁移后移除） |

---

## 十三、风险与边界

| 风险 | 对策 |
|------|------|
| 提取后 LocalLlm 回归 | P1 保留旧 `LocalLlmRule` 兼容壳，映射转换 + 既有单测全绿后再内层替换 |
| 规则文件热重载竞态 | 单写者 + 版本号 + 缓存失效按 LastWriteTime；失败回退已加载版本 |
| 匹配语义漂移（`regex:` 写错静默） | Schema + lint（错误正则显式报错，不再 `catch` 吞掉）；Audit 记录未命中/命中 |
| 过度设计（非目标扩到编排/工作流） | 引擎只做"单输入→裁决输出"，状态机/编排明确排除 |
| 与 clinerules 静态规范混淆 | 定位为运行时确定性裁决引擎（消费方 LocalLlm/调度/路由），clinerules 是静态规范库，二者互补不替代 |

---

## 十四、一句话总结

> **方案**：把 `agent-config.json → LocalLlm.Rules` 的"匹配+输出"从 `LocalLlmClient` 提取为独立的 **Decision Rules Engine**——
> 规则模型升级为「`when`（条件：`{field, strategy}` 枚举化 + AND/OR/not 组合）→ `then`（输出：text/json/tool_call/structured/delegate 枚举化）」，
> 匹配器/输出器/规则集加载/校验/审计全部端口化（Open-Closed 扩展），规则从 agent-config **分离为独立 `rules/*.rules.json`**（JSON Schema 校验 + 热重载），
> LocalLlm 以兼容壳委托接入（零回归），并对齐 `GlobalDecisionKey` 的优先级/冲突裁决——使其成为**通用、规范、可扩展的运行时裁决框架**，可脱离 LocalLlm 供调度/路由/护栏等任意决策点复用。

