# Strategy.DecisionRules 通用决策框架功能介绍

> **定位**：策略层（`CsharpPrj\Strategy`）的通用运行时裁决框架。
> **架构**：以 `BasicMethod.DecisionEngine` 为**基础引擎（底座）**，对基础引擎覆盖不了的声明式/匹配/输出/文件化逻辑在框架层**扩充**。
> **依赖方向**：`Strategy.DecisionRules` → `BasicMethod.Map.DecisionEngine`（底座） + `Plate.JsonMethod`（JSON 统一规范），单向无环。

---

## 1. 为什么放在 Strategy 层

`Agent.Rules`（原决策规则引擎，位于 `Projects/Agent`）已是一套通用、LLM-free 的裁决引擎，被 `LocalLlmClient` 真实消费。
为使其**脱离 Agent 供调度 / 路由 / 护栏 / 工具选择等任意决策点复用**，将其迁入 `CsharpPrj\Strategy` 通用化。

**命名空间**：`Strategy.DecisionRules`（原 `Agent.Rules` 已删除，`Agent.Contract` 直接引用本框架）。

---

## 2. 基础引擎（BasicMethod.DecisionEngine）与扩充层的关系

| 逻辑 | 处置 | 落点 |
|------|------|------|
| 条件求值 `context.key == value` | **复用底座** `ConditionEvaluator` | `MatchStrategy.Expression` 匹配器委托求值 |
| 优先级选择（Priority 降序） | **对齐底座** `RuleBasedDecisionEngine` 语义 | `SelectBest`（declared 模式，`⟨Priority,Specificity,Order⟩`） |
| 多引擎/多规则集组合 | **复用底座** `CompositeDecisionEngine` | `RuleSetDecisionEngine` 适配器 |
| 声明式规则模型（when/then） | **扩充**（底座仅命令式） | 模型层 |
| 轴/参数态/语义词元/分层决策图 声明原子 | **下沉**通用框架 `BasicMethod.Map.Declarative`（`Spec/`+`Graph/`，零外部依赖） | `BasicMethod.Declarative.Spec` / `.Graph` |
| 匹配策略（10 种 + 注册表） | **扩充** | 匹配层 |
| 输出策略（6 种 + 注册表） | **扩充** | 输出层 |
| 加载 / 校验 / 审计 / 热重载 | **扩充** | 加载校验层 |
| 占位符模板渲染 | **扩充**（复用 `Plate.JsonMethod.TemplateFormatter`） | 渲染层 |

> 关键原则：`DecisionEngine` 底座保持**零外部依赖**纯净性；JSON/文件/渲染依赖全部留在扩充层，不污染底座。

---

## 3. 核心类型

### 3.1 声明式模型（扩充层）
- `DecisionRuleSet` / `DecisionRule` / `WhenCondition` / `MatchCondition` / `MatchClause` / `OutputClause`
- 枚举：`MatchStrategy`（Always/Role/Contains/Regex/StartsWith/EndsWith/Wildcard/JsonPath/Semantic/**Expression**/Exact/**ArrayContains**/**NumericCompare**/**JsonSchema**）、`OutputKind`（Text/Json/ToolCall/ToolCalls/Structured/**Merge**/Delegate/**LLM**）、`RuleSetPriorityMode`（Ordered/Declared）
- `OutputClause` LLM 专属字段：`llmSystemTemplate` / `llmUserTemplate`（缺省回退 `template`）/ `llmParseJson`（缺省 true）—— `then.type:"llm"` 经 `LlmOutputStrategy` 同步产出 `LlmCallDescriptor`（延迟请求描述，**LLM-free**，异步执行/解析由消费方适配层完成）。
- `DecisionRuleContext`：可选承载 JSON 根（`Root`）→ field 支持 JSONPath（`$.a.b` / `a.b`），`context.<key>` 归一到 `$.context.<key>`；空根时保持旧 LLM 形状取值（零回归）。
- `JsonPathResolver`（新）：复用 `Plate.JsonMethod.JsonPathHelper` 的取值/节点/数组/数值解析（SingleImplementation）。

### 3.2 端口（Open-Closed 扩展）
`IMatchStrategy` / `IOutputStrategy` / `ITemplateRenderer` / `IRuleSetLoader` / `IRuleSetValidator` / `IMatchStrategyRegistry` / `IOutputStrategyRegistry` / `IRuleAuditSink` / `IDelegateRegistry`（Delegate 委托解析端口）

### 3.3 引擎
- `DecisionRulesEngine`：`MatchWhen(短路) → Select(优先级/数组序) → Render → Execute(输出器) → Fallback(default) → Audit`，**无状态**，单输入→裁决输出。
- JSON 输入重载：`Evaluate(ruleSet, JsonObject input, …)` —— 决策 = 纯函数 `单一 JSON → 单一 JSON`，任意 JSON 消费者直接调用（调度/路由/护栏），LLM 形状经 `DecisionRuleContext` 翻译层兼容。
- `DecisionRuleRouter`：把「输入文本 → 裁决输出」封装为单一调用（路由/护栏/工具选择复用入口）。
- `RuleSetDecisionEngine`（底座适配器）：把声明式 `DecisionRuleSet` 暴露为 `IDecisionEngine<RuleDecisionOutcome, DecisionRuleContext>`，可被 `CompositeDecisionEngine` 组合。

#### 3.3.1 JSON 聚拢契约与门面（单一裁决入口，LLM-free）
- `DecisionOutput`：统一结果信封 `{ matched, ruleId, kind, fallback, result }`，由 `DecisionOutput.FromResult(DecisionRuleResult)` 归一。
- `DecisionPortal`：全决策点唯一裁决入口，标准形态 `Evaluate(ruleSetOf(point), BuildInput(state)) → DecisionOutput`；`DecisionPortal.BuildInput(point,input,context)` 装配 `{ decisionPoint, input, context }`；可注入 `IRuleAuditSink`（命中/兜底审计）。**动态规则源重载** `Evaluate(ruleSet, point, input, exclude)`（ur_config 每次热读/规则文件等规则集随调用变化时显式传本次规则集走门面，消费方不再各自包 `FromResult`）+ **无 provider 默认构造**（rule-set 重载用不到注册表）；provider 版委托该重载保单一引擎路径。
- `IDecisionRuleSetProvider`：决策点 → 规则集供给端口（静态注册点用；动态规则源点走 rule-set 重载）。
- `IConfigRuleTranslator` / `UrConfigRuleTranslator`：把既有 ur_config（scheduler.rules.*，snake_case）翻译为统一 `DecisionRuleSet`（数值谓词 eq、`current_strategy` Ordinal、`last_eval` OrdinalIgnoreCase、空 action 行丢弃、缺 id 补合成 id），作为 Config 调度 / 规则任务的规则源。

### 3.4 加载 / 校验
- `FileRuleSetLoader`（按 LastWriteTime 失效缓存 + 可选热重载，失败回退已加载版本）
- `RuleSetJsonValidator` / `RuleSetJsonValidator`（JSON 语法 + 结构 + 语义 lint，错误正则显式报错不吞）

---

## 4. 使用示例

```csharp
// 声明式规则 + 引擎评估（LLM-free，任意决策点复用）
var rs = new DecisionRuleSet
{
    Id = "router",
    Priority = RuleSetPriorityMode.Declared,
    Rules = new[]
    {
        new DecisionRule { Id = "r1", Priority = 10,
            When = new WhenCondition { All = new[] { new MatchCondition {
                Match = new MatchClause { Strategy = MatchStrategy.Contains, Value = "create" } } } },
            Then = new OutputClause { Type = OutputKind.ToolCall, ToolName = "create_node", Arguments = "{}" } },
    },
    Default = new DecisionRule { Then = new OutputClause { Type = OutputKind.Text, Template = "默认回应：{user}" } },
};
var result = new DecisionRulesEngine().Evaluate(rs, new DecisionRuleContext { User = "请创建任务" });

// 复用底座 ConditionEvaluator 的条件表达式
var rs2 = new DecisionRuleSet { /* Expression 匹配：Value = "context.mode == A" */ };

// 复用底座 CompositeDecisionEngine 组合（有状态/下一步决策）
var composite = new CompositeDecisionEngine<RuleDecisionOutcome, DecisionRuleContext>(
        RuleDecisionOutcome.Empty, name: "composite")
    .AddEngine(new RuleSetDecisionEngine(setA, name: "setA"))
    .AddEngine(new RuleSetDecisionEngine(setB, name: "setB"));
var decision = await composite.DecideAsync(new DecisionRuleContext { User = "x" });
```

**JSON 进/JSON 出（领域中立）**——决策 = 纯函数 `单一 JSON → 单一 JSON`，任意 JSON 消费者直接调用，不依赖 LLM 形状：

```csharp
// 规则：field 用 JSONPath 从输入根取值；then 用 merge 合成单一输出 JSON（含 {{ $.… }} 占位符拉值）。
var rs = new DecisionRuleSet
{
    Id = "json-router",
    Rules = new[]
    {
        new DecisionRule
        {
            Id = "r-sched",
            When = new WhenCondition { All = new[] { new MatchCondition {
                Match = new MatchClause { Field = "$.input.task.schedulingMode", Strategy = MatchStrategy.Exact, Value = "Hybrid" } } } },
            Then = new OutputClause
            {
                Type = OutputKind.Merge,
                MergeSegments = new[]
                {
                    new OutputClause { Type = OutputKind.Json, Template = "{ \"action\": \"continue\" }" },
                    new OutputClause { Type = OutputKind.Json, Template = "{ \"taskKey\": \"{{ $.input.task.taskKey }}\" }" },
                },
            },
        },
    },
};
var input = JsonNode.Parse("""
    { "decisionPoint": "task_scheduler", "input": { "task": { "taskKey": "tp_x", "schedulingMode": "Hybrid" } } }
    """) as JsonObject;
var out = new DecisionRulesEngine().Evaluate(rs, input);      // JSON 输入重载
var merged = out.Structured as JsonObject;                     // → { action, taskKey }（合成单一 JSON）
```

- 匹配器新增 `arrayContains` / `numericCompare`（结构型比较）；`jsonSchema` 预留（对齐 `Semantic` 预留模式，未实现）。
- 输出新增 `merge`（经 `Plate.JsonMethod.JsonMergeService` 深合并，SingleImplementation）；`delegate` 经 `IDelegateRegistry` 端口解析 C# 函数产出 JSON。
- `context.<key>` 旧别名在 JSON 根下归一到 `$.context.<key>`（翻译适配，老规则零回归）。

---

## 5. 关联规范
- `.clinerules`：`LO.Resolve(table)`（表驱动裁决）、`GlobalDecisionKey`（优先级/冲突裁决思路）、`SingleImplementation` / `TH-12`（单一实现一致性）、`TH-9`（结构对齐→跨轮可比）。
- JSON 统一经 `Plate.JsonMethod`（`JsonPathHelper` / `TemplateFormatter` / `JsonValidationService`）。
