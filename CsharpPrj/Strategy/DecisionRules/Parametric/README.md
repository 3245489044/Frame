# 参数化决策内核（Parametric Decision Kernel）

> 所在：`Strategy.DecisionRules/Parametric/`（命名空间 `Strategy.DecisionRules`）
> 定位：本模块地基之一 —— 把「决策 / 混合调度」收敛为一次参数化计算：
> **① 变量→参数（归约）→ ② 参数态闭环演进（内层计算）→ ③ 参数→标签（对外解码）**。
> LLM-free，可脱离 Agent 供任意决策点复用（同 `DecisionRules` 其余类型一致）。
>
> **声明面归属（2026-09-17 提取）**：坐标/词元/分层图三类**纯声明原子**已上移通用框架
> `BasicMethod.Map.Declarative`（零外部依赖），本目录只留引擎侧计算与编排：
> - `BasicMethod.Declarative.Spec`：`ParameterKind`/`AxisRole`/`ParameterSpec`/`ParameterSchema`/
>   `ParameterState`/`ParameterWrite`/`ParameterWriteSource`/`SemanticTerm`/`SemanticVocabulary`/`SemanticClassRules`
> - `BasicMethod.Declarative.Graph`：`DecisionGraph`/`DecisionLayer`/`LayerRule`/`CrossFeature`
> 决策域特有的匹配/输出/编排（`ParameterMatrix`/`ParametricKernel`/`CoordinateInvariants` 等）仍在本目录。

## 为什么这样建模

传统「决策」是多个串联 if/回调：内容轮决策 → 轮末另问导演 → 终值再判一次，每多一个分支就多一层"何时该触发"的边界，假阳性（决策失败仍绿）与"已 stop 仍问导演"由此而生。

本内核反其道：**没有多个决策点，只有一次参数变换**。

- 决策所需的一切语义状态被归约为**参数**（数值/序数/二值），存进一个不可变的**参数态**；
- 内层只对参数态做**一步步确定性演进**（预算单链、进度推进、意图达成合取……），消 if 堆叠；
- 只有到**对外需要输出**时，才把参数态经**解码器**译成标签（终值 / 继续 / 意向未达）。
- LLM / Config / 导演退化为 **①归约 / 策略供给**两类"给参数供值"的来源，永不触碰强制分量。

## 三段式模型

```
[语义变量]           ① ParameterReducer         [参数态]   ② ParameterTransfer  [参数态]
 intent/produced/…  ──────────── 归约 ──────────► p_k  ── 演进(闭环,逐轮) ─────► p_{k+1} …
                                                          │ ③ 仅对外输出时解码
                                                          ▼
                                                    IParametricDecoder → 标签(终值/继续)
```

对应类型：

| 段 | 类型 | 职责 |
|----|------|------|
| 坐标系 | `ParameterSchema` / `ParameterSpec` / `AxisRole` | 轴 = (role, kind, domain)；state 轴（含 `IsForced` 引擎强制）+ `eventAxes`（瞬时，不进状态）+ 内建 const "1" |
| 状态 | `ParameterState` | 不可变参数快照（各分量 double 承载） |
| ①归约 | `ParameterReducer` / `Parametric.ReduceFromVariableMap` | 语义变量 → 参数写 |
| ②演进 | `ParameterMatrix` / `ParameterTransfer` / `Parametric.AsTransfer` | **整向量经矩阵 W·x+b 一次变换 + 逐轴量化**（非逐分量线性写） |
| ③解码 | `IParametricDecoder<TLabel>` / `Parametric.DecoderFrom` | 参数态 → 标签 |
| 闭环 | `ParametricKernel<TLabel>.Run(...)` | 逐轮归约→矩阵演进→解码，直到终止或达上限 |

## ②演进 = 真正的多维矩阵计算（不是逐分量 if/写）

`ParameterMatrix` 把「当前参数态 + 本步事件」当作**输入向量**，一次完成：

```
x_next = Quantize( W·x + b )
```

- 每一行 = 一个输出参数：`out = Bias + Σ weight[i]·Get(role_i, axisId_i)`；引用取规范坐标 `(role, axisId)` ∈ `{(state,参数名), (event,事件键), (const,"1")}`。
- **装载期闸A**：行内每个引用必须已在 `ParameterSchema` 声明（state 轴 / `eventAxes` 声明 / 常量 `"1"`），否则 `Row(...)` 即抛 —— 不再运行期静默计 0；同名跨 role 时裸名报歧义，须显式带 role（如 `Plus(AxisRole.Event,"failure",1)`）。事件轴值为瞬时量，读值经该轴 `Coerce`+`Clamp`（闸B）。
- 所有行由同一矩阵 **并行** 算出（整向量一次变换），不再像早期版本那样逐分量顺序写。
- 逐轴按 <see cref="ParameterKind"/> **离散量化**：`Flag` 阈值门、`Ordinal` 取整 + 边界、`Numeric` 边界 —— 保证结果永远落在可判定参数域。

声明示例（预算/进度在失败时都 +1）：

```csharp
var m = ParameterMatrix.For(schema)
    .Row("progress", r => r.Carry("progress").Plus(AxisRole.Event, "failure", 1))
    .Row("budget",   r => r.Carry("budget").Plus(AxisRole.Event, "failure", 1));
var run = new ParametricKernel<Outcome>(schema, maxSteps: 8).Run(
    variables: new() { ["failure"] = true },
    transfer: Parametric.AsTransfer(m),   // 矩阵即演进算子（Engine 源）
    decoder: myDecoder);
```

> 说明：本矩阵是内层"确定性引擎"计算载体，Engine 源可写强制分量（预算/进度/halt）；
> 与 `Plate.Mathematics.Matrix`（依赖 MathNet.Numerics 的通用数值矩阵）用途不同 —— 本模块保持轻依赖、
> LLM-free，只内置仿射 + 离散量化一层，供决策域规则直接声明使用，不引入第三方数值引擎。

## 语义向量层（有限词元补足语义，取代真实语义）

> **复合矩阵运算基础定律（实现已内建）**：
> - **定律 A · 吸收/闩锁（`ParameterSpec.IsLatched`）**：标记为闩锁的分量在一次运行内单调不降——一旦到达某触发值即吸收态，
>   后续任何来源（含 Engine）的低值写被忽略。用于 halt 这类"一旦置真绝不被后续复合步翻盘"的终值主控位
>   （`ParameterState.Apply` 内实现，见 2026-09-09 参数化决策矩阵接入 Agent 方案）。
> - **定律 D · 显式优先级（`SemanticClassRules.WhenAllOf(label, priority, …)`）**：互斥裁决按「优先级越小越优先 → 注册序」取胜，
>   主控/终值类（TM/TO/intent_fail）显式给低优先级以确定性压过从属类 —— 支配是命名属性，非注册顺序副作用。
>   （默认优先级=注册序，向后兼容。）

数值标量（budget=2、intent=1）丢失了"轴身份 + 取值语义"，是前述可读性/非线性之困的根因。
**当语义宇宙有限、词元可预定义时，无需 LLM 的分布语义**：用一套固定的正交词元基即可**精确**承载语义。

- `SemanticVocabulary`：封闭域单一真源（轴 → 词元集）；每词元一个 one-hot 正交基维。
  `Embed`(离散→向量) 与 `Sample`(向量→离散) 为**双射**，`embed∘sample = id`（比 TH-4 的 ε 统计近似更强）。
- `SemanticClassRules`：语义以**可读规则**声明（决策 C = 词元集 AND），编译产物是正交基上的判别向量：
  权重自动 = 1/必需词元，`bias=-(n-0.5)` 使仅当全部必需词元命中才激活 —— **AND 被转成线性可分**，杜绝手写魔法权重。
- 人读规则、机器算向量、审计还原人话（命中哪些词元 → 为何判该类）。

```csharp
var vocab = new SemanticVocabulary(new Dictionary<string, IReadOnlyList<string>>
{
    ["eval"]    = new[] { "pass", "partial", "fail" },
    ["intent"]  = new[] { "none", "expected" },
    ["produced"]= new[] { "yes", "no" },
});
var rules = new SemanticClassRules(vocab)
    .WhenAllOf("intent_fail", new("eval","partial"), new("produced","no"))
    .WhenAllOf("Success", new("eval","pass"), new("intent","none"));
var label = rules.ClassifyExclusive(new[] { new("eval","partial"), new("produced","no") }); // "intent_fail"
```

> 边界：该层有效前提是"词元宇宙可预定义"。开放输入(无界自然语言)需"输入→向量"的编码函数(即 LLM 学的部分)，
> 固定词元基替代不了 —— 本仓库决策域均属封闭域，故适用。



## 写分域与守卫（对齐 SEP.DeterministicCoreDisjoint）

`ParameterWrite.Source ∈ {Engine, Reduce, Policy}`：

- **Engine**：唯一允许写 `IsForced=true` 分量（预算 / 进度 / halt）——确定性核心写路径单一。
- **Reduce（归约） / Policy（策略/LLM 提议）**：只允许写非强制分量；**越权写强制分量会被守卫拒绝**，记入 `ParametricRun.Rejected`，不改值、不抛——消除"LLM 越权改预算"类风险。

`ParameterState.Apply` 统一施加全部写并做 Clamp；参数态不可变，`Run` 每步留痕到 `Trace`（InformationMonotonicity）。

## 最小示例（成功/终值裁决）

```csharp
// 坐标系
var schema = new ParameterSchema(new[]
{
    new ParameterSpec("progress", ParameterKind.Ordinal, Default: 0, IsForced: true, Min: 0, Max: 3),
    new ParameterSpec("budget",   ParameterKind.Ordinal, Default: 0, IsForced: true, Min: 0, Max: 3),
    new ParameterSpec("halt",     ParameterKind.Flag,    Default: 0, IsForced: true),
    new ParameterSpec("intent",   ParameterKind.Flag),
    new ParameterSpec("produced", ParameterKind.Flag),
},
eventAxes: new[]
{
    // 事件轴（单步瞬时，不进 ParameterState）：矩阵引用须为 (AxisRole.Event, "failure")
    new ParameterSpec("failure", ParameterKind.Flag, Role: AxisRole.Event),
});

// ③ 解码：意图未达成(intent=1∧produced=0)→IntentFail(非绿)；预算触顶→TM
IParametricDecoder<string> decoder = Parametric.DecoderFrom((s, _) =>
    s.Get("halt") >= 1        ? ParametricDecision<string>.Terminal("TM")
    : (s.Get("intent") >= 1 && s.Get("produced") < 1)
                              ? ParametricDecision<string>.Terminal("IntentFail")
                              : ParametricDecision<string>.Terminal("Success"));

// ①归约 + ②演进
ParameterReducer reduce = input => input.Variable("intent") is { } i
    ? new[] { ParameterWrite.Reduce("intent", schema.Get("intent").Coerce(i)) }
    : Array.Empty<ParameterWrite>();
ParameterTransfer transfer = input => input.Variable("failure") is true
    ? new[] { ParameterWrite.Engine("budget", input.State.Get("budget") + 1),
              ParameterWrite.Engine("progress", input.State.Get("progress") + 1) }
    : Array.Empty<ParameterWrite>();

var run = new ParametricKernel<string>(schema).Run(
    variables: new Dictionary<string, object?> { ["intent"] = true },
    transfer: transfer, reducer: reduce, decoder: decoder);
// run.Label == "IntentFail"  （应做而未做，显式非绿，不再靠外部断言纠偏）
```

## 接入约定

- **引擎强制分量**（预算 / 进度 / halt / 轮次）只由 `transfer` 以 `ParameterWrite.Engine` 写，禁止在 reducer / policy 触碰（守卫自动拒绝）。
- **LLM / Config / 导演**在归约或策略供给中供值，是"参数供给源"而非时序分支 → 天然不存在"已 stop 仍问导演"。
- 现有 `DecisionRulesEngine`/`DecisionPortal`（JSON 规则引擎）可作为归约/演进一类的**规则实现载体**与本内核对接（本文件只提供参数化闭环地基，不重复造规则引擎）。

## 测试

`Strategy.Tests/DecisionRules.Tests/`：
- `ParametricKernelTests.cs` 覆盖：单步成功收敛、假阳性命门（IntentFail 非绿）、预算连续升级到 TM、外部越权写强制分量被拒、每步轨迹留痕、吸收/闩锁位不可翻盘。
- `ParametricMatrixTests.cs` 覆盖：整向量一次变换、重复失败升级到 TO、Flag 阈值门、Ordinal 取整+边界、未定义输出参数抛。
- `ParametricCoordinateTests.cs`（C0 统一坐标系）覆盖：装载期闸A（未声明轴即抛）、跨 role 同名须规范坐标、event 轴 Coerce+Clamp、event 轴禁 IsForced/IsLatched、const 轴唯一 `"1"`、裸名唯一声明下的向后兼容。
