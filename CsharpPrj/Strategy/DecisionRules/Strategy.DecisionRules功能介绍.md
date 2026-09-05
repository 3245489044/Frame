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
| 匹配策略（10 种 + 注册表） | **扩充** | 匹配层 |
| 输出策略（6 种 + 注册表） | **扩充** | 输出层 |
| 加载 / 校验 / 审计 / 热重载 | **扩充** | 加载校验层 |
| 占位符模板渲染 | **扩充**（复用 `Plate.JsonMethod.TemplateFormatter`） | 渲染层 |

> 关键原则：`DecisionEngine` 底座保持**零外部依赖**纯净性；JSON/文件/渲染依赖全部留在扩充层，不污染底座。

---

## 3. 核心类型

### 3.1 声明式模型（扩充层）
- `DecisionRuleSet` / `DecisionRule` / `WhenCondition` / `MatchCondition` / `MatchClause` / `OutputClause`
- 枚举：`MatchStrategy`（Always/Role/Contains/Regex/StartsWith/EndsWith/Wildcard/JsonPath/Semantic/**Expression**/Exact）、`OutputKind`（Text/Json/ToolCall/ToolCalls/Structured/Delegate）、`RuleSetPriorityMode`（Ordered/Declared）

### 3.2 端口（Open-Closed 扩展）
`IMatchStrategy` / `IOutputStrategy` / `ITemplateRenderer` / `IRuleSetLoader` / `IRuleSetValidator` / `IMatchStrategyRegistry` / `IOutputStrategyRegistry` / `IRuleAuditSink`

### 3.3 引擎
- `DecisionRulesEngine`：`MatchWhen(短路) → Select(优先级/数组序) → Render → Execute(输出器) → Fallback(default) → Audit`，**无状态**，单输入→裁决输出。
- `DecisionRuleRouter`：把「输入文本 → 裁决输出」封装为单一调用（路由/护栏/工具选择复用入口）。
- `RuleSetDecisionEngine`（底座适配器）：把声明式 `DecisionRuleSet` 暴露为 `IDecisionEngine<RuleDecisionOutcome, DecisionRuleContext>`，可被 `CompositeDecisionEngine` 组合。

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

---

## 5. 关联规范
- `.clinerules`：`LO.Resolve(table)`（表驱动裁决）、`GlobalDecisionKey`（优先级/冲突裁决思路）、`SingleImplementation` / `TH-12`（单一实现一致性）、`TH-9`（结构对齐→跨轮可比）。
- JSON 统一经 `Plate.JsonMethod`（`JsonPathHelper` / `TemplateFormatter` / `JsonValidationService`）。
