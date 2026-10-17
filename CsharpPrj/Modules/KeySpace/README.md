# Modules.KeySpace — 键空间语义框架

层 2，只依赖 `Modules.Parsing.Keys`（BCL-only）。职责边界：

| 层 | 回答的问题 | 实现 |
|---|---|---|
| `Modules.Parsing.Keys` | 键**长什么样**（段/模板/正反解/族判据/键序） | 既有，不改 |
| `Modules.KeySpace` | 键**指谁 / 值有没有 / 集合怎么裁 / 空集怎么办 / 读写如何同源** | 本工程 |

## 类型速览

- `KeyRef` / `KeyPath`：族 + 原值 + 结构路径（`KeyPath` 结构相等）。
- `KeyValue<T>`：三态 `Known / Unknown / NotApplicable`，禁止 `default`/`""` 冒充。
- `KeySetPlan` + `EmptySetPolicy`：目标集 + 理由 + 策略 + 分类丢弃计数（无裸数组出口）。
- `PortContract` / `KeyBinder`：Required 缺失可见失败，Optional 缺失 ⇒ `Unknown`。
- `KeySpaceDeclaration`：族 + 端口 + 策略的**数据载体**（域词由调用方声明）。

## 使用

```csharp
var declaration = AgentKeySpaceDeclaration.Current;          // 产品侧构造
var facade = new KeySpaceFacade(
    declaration,
    new GrammarKeyClassifier(declaration),
    new KeyEquivalence(declaration),
    new KeySetPlanner(),
    new KeyBinder(),
    NullKeyDiagnosticsSink.Instance);

var plan = facade.PlanSet(new KeySetPlanInput(
    RawMembers: candidates,
    Self: entity,
    MaxTargets: 32,
    AllowFanout: false,
    EmptyPolicy: declaration.Policy("history.dispatch.empty"),
    Declaration: declaration));
```

## 纪律（门禁 `KeySpaceCoverage`）

- KS1 端口契约族命中 / `.ushar` 端口名可解析
- KS2 三态禁坍缩（`?? ""` / `= default`）
- KS3 `PlanSet` 必带 `EmptyPolicy`
- KS4 词法唯一（禁自建 `Regex` / `Split('.')`）
- KS5 框架源码零域词（history/task/session）
- KS6 诊断码登记于 `KeyReasonCodes`
- KS7 族 id 唯一；KS8 负对照
