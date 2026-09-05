# Agent.Calculate 计算内核：扩展设计与可实现性评估

> **日期**：2026-08-09
> **范围**：`Agent/src/Agent.Calculate` 计算内核模块（纯计算叶，无副作用、无编排，`canonical` 单实现）
> **约束锚点**：`Agent.Calculate.csproj` `ModuleRule_*`（纯计算叶 / 同一公式单一实现 / 不反向依赖 Agent.Core/Axioms）；L3 参数运行时可变、不硬编码。

## 结论摘要（TL;DR）

- **能实现且值得优先**：D2 迟滞（防反复重构省 token）、B3 场景补齐（防御性防误触发）。
- **需先补数据**：D4 累计无进展步数、D3 窗口 EWMA（均需跨轮状态）。
- **实现概率低**：D1 分层估算（缺 per-layer 数据、低杠杆兜底）、trend 动作差异化（依赖 Agent.Core 改造）、四层重构（非 token 导向）、B1 TokenPressure（依赖未定的预警动作）。
- **不引入**：B2 融合、Error/StopReason/Proactive/Eval 维度、w₃ 子轮深度、错误堆叠（重复或无消费方）。

---

## 目录

0. [设计原则：计算唯一目的是提高运行效率、减少 token 消耗](#〇设计原则计算唯一目的是提高运行效率减少-token-消耗)
1. [现状：计算内核到底算了什么](#一现状计算内核到底算了什么)（含 1.3 可复用底座 Plate/Mathematics）
2. [根因短板](#二根因短板)
3. [深度轴扩展（含可实现性）](#三深度轴扩展含可实现性)
4. [广度轴扩展（含可实现性）](#四广度轴扩展含可实现性)
5. [层次化架构](#五层次化架构)
6. [可实现性汇总与实施排序](#六可实现性汇总与实施排序)
7. [约束合规自检](#七约束合规自检)
8. [分阶段路线](#八分阶段路线)

---

## 〇、设计原则：计算唯一目的是提高运行效率、减少 token 消耗

### 0.1 元目的（最高准则）

`Agent.Calculate` 内任何计算量的**唯一合法性来源**是**最终减少 token 消耗 / 提高智能体运行效率**；不服务于该目标的"更精确 / 更丰富 / 更层次"一律不引入。本模块为本地纯计算（CPU 成本可忽略），故判断只看 **它触发的动作 × 触发时机 → 净 token 节省**，而非计算本身。

### 0.2 目的-消费方契约（五问）

每个计算量在**元目的**之下必须通过五问，任一不满足即**无目的计算**（为了计算而计算），禁止引入：

| # | 问题 | 判据 | 不合格处置 |
|---|------|------|-----------|
| ① | 谁消费 | 存在具名消费方（评估器/仲裁/动作选择） | 无消费方 → 不引入 |
| ② | 改变什么决策 | 结果改变触发边界 / 动作选择 / 阈值 | 仅观测不改决策 → 降级为遥测（不进 Score） |
| ③ | 缺失会怎样 | 移除后决策退化或错误 | 移除后无影响 → 该计算冗余，删除 |
| ④ | 是否省 token/提效率 | 触发的动作最终净减少 token 消耗 | 有消费方但不省 token → 不引入（避免为精确而精确） |
| ⑤ | 是否重复 | 与既有短路 / 门控 / 特判重复 | 已消费 → 不并入数值面（单一实现） |

### 0.3 边界动作的 token 价值（效率轴）

| 动作 | 对 token/效率的作用 |
|------|--------------------|
| `compact` | 压缩历史 → 后续每轮直接省 token；时机过晚→上下文膨胀、推理退化→被迫更大窗口/重做 |
| `rescope` | 收窄问题域 → 避免漫无边际探索 → 省 token、提效率 |
| `resubmit_with_info` | 回写"需改变输入条件" → 避免无效重试 → 省 token |
| `escalate` | 升预算/换模型 → 仅当避免在低效方向上耗 token 才省；**可能增 token，需谨慎** |
| `continue`（不触发） | 避免过早 compact 丢失进度 → 防止"省了又重做"的净增 token |

> **效率结论**：token 价值集中在**在正确时机触发正确动作**——防过晚（上下文膨胀）、防过早（丢进度重做）、防打转（反复重构）。任何不改善这三者的"精度/层次提升"均无目的。

> 对齐仓库既有原则：`Strategy.SingleImplementation` / `Strategy.RedundancyElimination`（axioms.md §8）、反模式 `ANTI-DUPLICATED-IMPLEMENTATION` / `ANTI-REDUNDANT-CODE`。本条是后续 D1–B3 各项的**前置裁判**。

---

## 一、现状：计算内核到底算了什么

`Agent.Calculate` 为纯计算叶模块（`ModuleRule_Layer=4`，无副作用、无编排，`canonical` 单实现），承担三块计算：

| 文件 | 计算 | 现状特征 |
|------|------|---------|
| `Estimation/ContextEstimator` | `chars = baseChars + ResultTextLength + MessageCount×128`；`tokens = chars / 4.0`；有真实 token 时透传 | 单公式、单线性、全局单一密度系数 4.0 |
| `Pressure/BoundaryPressureCalculator` | 三维压力 + 加权 Score：`ContextPressure` / `InfoGainPressure` / `ComplexityPressure` / `Score=Σ(wᵢ·pᵢ)`，全部 `Clamp[0,1]` | 一个平铺的 `Compute`，无分层 |
| `Hashing/KeyDeriver`、`Numbers/ScalarCoercer` | 稳定键 / 标量归一 | 工具函数，非扩展重点 |

### 1.1 消费方与数据流

```
DefaultBoundaryFramework.PostRoundAsync
  → ContextEstimator.Estimate(signal, ReadPromptChars(ctx))   // 估算上下文占用
  → ReactiveBoundaryTrigger → LogicalBoundaryEvaluator.Evaluate
       → BoundaryPressureCalculator.Compute(3压力 + Score)    // 纯数值
  → ProactiveBoundaryTrigger → 白名单/置信度门控（单取 ContextPressure）
  → BoundaryTriggerArbiter.Merge                               // 仲裁（Agent.Core）
```

### 1.2 已识别但未充分利用的输入（候选清单）

`BoundarySignal` / `LogicalBoundaryConfig` 中**已有但计算器未消费**的字段。是否引入、以何形态引入，一律经 §〇 契约裁判：

| 字段 | 现状 | 候选用途 | 裁判结果 |
|------|------|---------|---------|
| `BudgetLevel` | 未参与 | ErrorPressure 维度 | **剔除**（重复，见 §四 B1） |
| `TokenValue/TokenMax` | 仅二值特判 | 连续 TokenPressure | 有条件（依赖预警动作） |
| `Eval` | 未参与 | QualityPressure | **剔除**（重复） |
| `ErrorClass` | 未参与 | ErrorPressure | **剔除**（重复） |
| `OutputTokens` | 只用 Input | 输入/输出分离 | 并入 D1（低优先） |
| `StopReason` | 仅短路 | 协议强信号维度 | **剔除**（重复） |
| `InfoGainWindow`（配置） | 未读 | 窗口趋势 | 保留（D3） |
| `ComplexityMaxSteps`（配置） | 未读 | 累计无进展步数 | 保留（D4） |

### 1.3 可复用底座：Plate/Mathematics

> **规划前提**：Agent.Calculate 的**数值/统计运算一律优先复用** `CsharpPrj\Plate\Mathematics`（命名空间 `MathematicalAlgorithms`，net10.0-windows，与 Calculate 同框架），避免重复实现，符合仓库 `SingleImplementation`。需在 `Agent.Calculate.csproj` 增加 `ProjectReference` 到 `..\..\..\CsharpPrj\Plate\Mathematics\Mathematics.csproj`。

**能力映射**（扩展项 → 复用 API）：

| 文档项 | 复用 API（`MathematicalAlgorithms`） | 用途 |
|--------|-------------------------------------|------|
| D2 响应曲线 | `ActivationFunctions.Sigmoid` / `MathUtility.LinearInterpolation` | ContextPressure 阈值附近更敏感的分段/非线性曲线 |
| D2 迟滞触发 | `ActivationFunctions`（ReLU 硬截断）+ 跨轮状态 | 触发判定 |
| D3 窗口平滑 | `MathUtility.WeightedAverage(rate, α·(1-α)^i)` | **EWMA 用加权平均实现**（库无现成 EWMA） |
| D3 趋势判稳 | `StatisticalAnalysis.Median / Variance / Skewness`、`MathUtility.Percentile` | 平台期/回退/波动识别 |
| D4 自适应阈值 | `MathUtility.Percentile` | 累计无进展阈值自适应 |
| L3 加权 Score | `MathUtility.WeightedAverage(压力, 权重)` | 替代手写加权和（现 `Compute` 内手写） |
| L3 非线性融合(可选) | `SoftmaxCalculator.Compute(double[])` | softmax 非线性融合 |
| InfoGain 增强(可选) | `StatisticalAnalysis.CalculateEntropy / Gini` | 信息论度量 |

**EWMA 复用方案**：`MathUtility.WeightedAverage(values, weights)` 中 weights 取指数衰减 `wᵢ = α·(1-α)^i`，即可在不新增循环实现的前提下获得 EWMA。若后续确需专门 API，优先在 Mathematics 的 `MathUtility` 扩展（对齐其接口式设计），而非在 Agent.Calculate 另写。

---

## 二、根因短板

1. **估算单一线性、单一密度系数**：不分层；对中文(~1.5~2 chars/token)、英文(~4)、代码(~5~6)、工具 JSON(~4~6)、System 指令密度差异不区分，误差可成倍放大。
2. **真实 token 时"往返换算"**：`tokens→chars→pressure` 复用同一系数，丢失输入/输出分开的信息。
3. **InfoGain 只看单步**：`rate = 本轮/上轮` 单轮噪声敏感，无法区分"平台期"与"回退"；`InfoGainWindow` 未被消费。
4. **Complexity 只看轮次占比**：忽略累计无进展步数（`ComplexityMaxSteps` 闲置）、子轮深度、错误堆叠。
5. **Score 单一线性加权和**：各维被 `Clamp` 拍平，无曲线/置信度/交互，单点过热可被权重稀释掩盖（单点盲区）。
6. **无层次**：`Compute` 一步算完 特征→压力→分数→门控，四层职责耦合。

---

## 三、深度轴扩展（含可实现性）

> 逐项给：**元目的 → 判定 → 可实现性**。核心：把"一把公式算到底"拆成更细粒度、且**有消费方且省 token** 的项。

### D1. 分层估算 + 分密度【低杠杆 · 兜底项】 — 可实现性：**低**

- **元目的**：不直接省 token；仅在无真实遥测且估算逼近决策边界时，让 `ContextPressure` 少犯错（防过早/过晚 compact）。真正省 token 靠真实遥测 + 正确时机，估算仅兜底。
- **判定**：真实遥测优先（有 `InputTokens/OutputTokens` 直接消费，不往返失真）；估算精细化仅当"无遥测 且 逼近阈值"才做，否则保持现状。
- **可实现性（低）**：当前 `ContextEstimator` 只收 `baseChars`（`System+Prompt+L1+L2` 合并总和），无 per-layer 数据；工具/记忆/各角色字符数无单独统计。需改造调用层拆层传入 + 新增逐层统计，改动面大，且是低杠杆兜底，投入产出比差。

```
估算输入（Signal/ctx）:                    // 仅供无遥测时兜底
  System  层 密度 4.5 / History 层(按角色 tool=5,asst=3.5,user=3)
  ToolOut 层 密度 5.5 / Current 层 密度 4.0 / Memory 层 密度 3.5
tokens = Σ( chars_layer / density_layer )
```

### D2. ContextPressure 迟滞【只保留有消费方的项】 — 可实现性：**高**

- **元目的**：触发时机正确性 = 防过晚（上下文膨胀）/ 防过早（丢进度）→ 直接省 token。
- **迟滞（保留）**：防 `compact` 后立刻再次触发（重构后打转）。消费方=跨轮状态 `BoundaryContext.ReconstructCount / PhiOld`（**已有**，非新增）；每次重构都有 token 成本，防打转 = 直接省 token。
- **分级水位 / 响应曲线（不引入）**：当前消费方仅单一阈值二值触发，无"软水位→降权 / 告警→换动作"的真实消费方，属无目的计算；除非确需"接近耗尽前预警"的差异化动作。
- **可实现性（高）**：消费方与状态已存在，仅需给 `Compute` 增加"上次触发状态"参数并传递。

### D3. InfoGain 窗口趋势（消费 `InfoGainWindow`） — rate_t：**中** / trend：**低**

- **元目的**：区分**平台期**（增益低但未恶化 → 继续，省去无谓切换开销）与**回退**（增益低且恶化 → 该换，避免在死胡同烧 token）。
- **rate_t（EWMA，保留）**：消费方=信息增益压力（触发）；缺失会→单轮抖动误判。需跨轮持有长度历史。
- **trend（斜率，有条件）**：消费方必须是**动作差异化**；若动作选择未实现则不单独输出，仅作 rate_t 辅助权重。
- **可实现性**：rate_t 中（需在 `DefaultBoundaryFramework` 扩展跨轮窗口状态，当前只维护单值 `_lastResultLength`）；trend 低（依赖 Agent.Core 评估器的动作差异化改造，而 `LogicalBoundaryEvaluator` 目前是 `Actions.FirstOrDefault` 固定顺序选动作，无按 trend 分流逻辑）。

```
rate_t    = EWMA(rate, α, window)              // 消费：信息增益压力（触发）
trend     = 窗口首尾差分（平台期/回退方向）        // 消费：动作选择（差异化，未实现则不输出）
infoPressure = f(rate_t, minRate) + trend 修正    // 仅当 trend 有动作消费时才并入
```

### D4. Complexity 累计无进展步数 — 可实现性：**中**

- **元目的**：识别"多轮原地打转"→ 触发重规划 → 避免在无进展方向继续烧 token。
- **w₂ 累计无进展步数（保留）**：消费方=复杂度压力（触发重规划）；`ComplexityMaxSteps` 配置已存在却闲置，属**消费既有配置**。
- **w₃ 子轮深度 / 错误堆叠修正（剔除）**：子轮深度无差异化动作消费（`ClassifyKind` 不据此分流）；错误堆叠已被 `ClassifyKind` 短路（ErrorClass）消费，再并入是重复计算。
- **可实现性（中）**：`BoundarySignal` **无"累计无进展步数"字段**，需先定义"无进展"判定（长度不增长？`Eval≠Pass`？）并在框架层新增跨轮累计计数器。

```
complexity = w₁·(Round/maxRounds)                  // 轮次占比（浅）
           + w₂·(累计无进展步数/ComplexityMaxSteps)  // 深度累计（消费闲置配置）

---

## 四、广度轴扩展（含可实现性）

> 广度轴对契约最严格：`BoundarySignal` 多数字段**已有既有消费方**（短路/门控/特判），再并入 Score 即重复计算。结论：**不新增维度**。

### B1. 维度判定（先查消费方，剔除重复） — 结论：**不新增维度**（可实现性：不适用）

| 候选维度 | 既有消费方 | 判定 |
|----------|-----------|------|
| `TokenPressure`（连续化） | 仅二值特判（耗尽→ComplexityPressure=1） | **有条件保留**：仅当需"接近耗尽前 escalate 预警"（差异化动作）才连续化；否则维持二值（可实现性：中低） |
| `ErrorPressure` | `ClassifyKind` 已短路（ErrorClass） | **剔除**（重复） |
| `QualityPressure`（Eval） | `ComplexityPressure` 已含"Eval≠Pass∧耗尽→打满"特判 | **剔除**（重复） |
| `ProactiveConfidence` | `ProactiveBoundaryTrigger` 已 `MinConfidence` 门控 | **剔除**（重复） |
| `StopReasonPressure` | `LogicalBoundaryEvaluator` 已 `stopBreach` 短路 | **剔除**（重复） |

> **结论（按元目的）**：真正缺的不是"更多计算"，而是"维度→动作的差异化映射"，且该映射必须**净省 token**（平台期/回退用对动作、接近耗尽正确预警）。优先补齐**动作选择层**，而非堆叠 Score 权重。`escalate` 是增 token 动作，作为预警动作需确认净省 token，否则 TokenPressure 连续化无意义。

### B2. 融合广度 — 以仲裁消费为准（可实现性：**不引入**）

`BoundaryTriggerArbiter.Merge` 已是反应式+主动式的**既有仲裁消费方**。若 `Merge` 已能表达"两者同面"，计算层**不新增**"证据向量"；仅当仲裁确实需要"历史趋势"且 `Merge` 无此输入时，才提供**有明确接收方**的输入。否则新增均无消费者，属冗余。

### B3. 场景补齐 — 防误算，而非新增计算（可实现性：**高**）

> 这些分支目的都是"当前无信号时不产生无意义压力"（防误触发），是**消隐而非产出**。均可用现有字段判空，不动计算主体：

- **首轮**（无 prior）：`rate=1.0` 默认已表达"无上一轮=无增益判断"，保持显式默认。
- **空结果**（`ResultTextLength=0`）：归入 `FirstCallTransient`（不计代价），避免把"首轮空"误判为平台期。
- **重构后复位**：配合 D2 迟滞，消费方=`BoundaryContext.ReconstructCount`，防 compact 后打转。
- **token 上限未配置**（`TokenMax=0`）：`TokenPressure` 不产生压力（退化），不污染总分。

---

## 五、层次化架构 — 可实现性：**低**（非 token 导向）

把现在一个 `Compute` 平铺方法，重构为**四层可组合纯函数流水线**（保持"纯计算叶、无编排"约束，编排仍在 Agent.Core）：

```
                BoundarySignal / Context / Config(weights,curves,thresholds)
                                    │
        ┌───────────────────────────▼───────────────────────────┐
  L1   │ FeatureLayer    原始信号 → 归一特征(速率/比率/趋势/密度)  │  ← D1/D3
        └───────────────────────────┬───────────────────────────┘
        ┌───────────────────────────▼───────────────────────────┐
  L2   │ DimensionLayer  特征 → 单维压力[0,1]（曲线/迟滞/置信度）  │  ← D2/D4/B1
        └───────────────────────────┬───────────────────────────┘
        ┌───────────────────────────▼───────────────────────────┐
  L3   │ FusionLayer     单维压力 → Score（加权/非线性/置信度修正） │  ← 现有加权升级
        └───────────────────────────┬───────────────────────────┘
        ┌───────────────────────────▼───────────────────────────┐
  L4   │ GatingLayer     Score/阈值/迟滞 → Triggered + Action     │  ← 数值部分留此，
        └───────────────────────────┬───────────────────────────┘  编排回 Agent.Core
                                    │
                         BoundaryAssessment
```

- 每层一个纯函数、各自可单测（对齐 `BoundaryFrameworkTests`）。
- `BoundaryPressureCalculator.Compute` 保留为**最外层 canonical 入口**（`Feature→Dimension→Fusion` 组合），消费方零改动——满足"同一公式单一实现"的 `ModuleRule`。
- 新增参数（密度/曲线/迟滞/新权重）全部进 `LogicalBoundaryConfig`（L3 运行时可变），不硬编码。
- **可实现性（低）**：技术上纯计算层内部拆分可行，但它是**工程可维护性改进，本身不省 token**；在"计算唯一目的是省 token"的元目的下属非目标项，仅当 P2/P3 落地需要分层承载时才必要。

---

## 六、可实现性汇总与实施排序

| 条目 | 可实现性 | 前置依赖 / 理由 |
|------|:--------:|----------------|
| D2 迟滞 | 🟢 高 | 消费方 `BoundaryContext` 已有；仅传参 |
| B3 场景补齐 | 🟢 高 | 现有字段判空；纯防御 |
| D4 累计无进展步数 | 🟡 中 | 需新增"无进展步数"信号 + 跨轮累计 |
| D3 rate_t EWMA | 🟡 中 | 需扩展跨轮窗口状态（当前 `_lastResultLength` 单值） |
| B1 TokenPressure | 🟠 中低 | 依赖未定的"接近耗尽预警"动作；escalate 可能增 token |
| D1 分层估算 | 🔴 低 | 缺 per-layer 数据、低杠杆兜底 |
| D3 trend 动作差异化 | 🔴 低 | 依赖 Agent.Core 评估器改造 |
| §五 四层重构 | 🔴 低 | 非 token 导向，仅当需要分层承载 |
| B2 融合 / 已剔除项 | ⛔ 不引入 | 重复或无消费方 |

**建议实施顺序**：① D2 迟滞 + ② B3 场景补齐（低成本高收益、直接省 token）→ ③ D4 累计无进展 / ④ D3 rate_t（先补跨轮状态）→ 其余视前置是否满足再定。

---

## 七、约束合规自检

| `ModuleRule` 约束 | 本方案是否满足 |
|---|---|
| 纯计算叶模块、无副作用、无编排 | ✅ 各层均为纯函数；编排/仲裁留在 Agent.Core |
| 同一公式单一 canonical 实现 | ✅ `Compute` 为唯一入口组合，不新增重复公式 |
| 不得反向依赖 Agent.Core / Axioms | ✅ 新输入仅经 `BoundarySignal`/`LogicalBoundaryConfig`（均属 Agent.Contract） |
| L3 参数运行时可变、不硬编码 | ✅ 密度/曲线/迟滞/新权重全部配置化 |
| 每项计算有消费方、无重复、净省 token | ✅ §〇 元目的 + 五问契约前置裁判 |

---

## 八、分阶段路线

> 每步标注**复用的 Mathematics API**（命名空间 `MathematicalAlgorithms`）。前置：`Agent.Calculate.csproj` 增加 Mathematics 引用。

| 阶段 | 内容 | 复用的 Mathematics API | 收益（省 token/提效率） |
|------|------|------------------------|------|
| P0 | 现状基线：为当前公式补齐单测快照 | —（验证手写加权 = `WeightedAverage` 基线） | 防回归 |
| P1（效率·真实遥测优先） | 消费真实 `Input/OutputTokens` 优先（避免估算往返失真→错误触发）；`ContextPressure` 迟滞防打转 | `ActivationFunctions.Sigmoid`（响应曲线）、`MathUtility.LinearInterpolation`（迟滞阈值） | 触发时机更准、防反复重构 → 直接省 token |
| P2（效率·正确时机） | `InfoGainWindow` 窗口 EWMA（rate_t）；`ComplexityMaxSteps` 累计无进展识别原地打转 | `MathUtility.WeightedAverage`（EWMA）、`StatisticalAnalysis.Variance/Skewness/Median`（趋势判稳）、`MathUtility.Percentile`（无进展阈值） | 避免在死胡同烧 token |
| P3（效率·动作差异化） | "平台期/回退/接近耗尽"→动作差异化映射（须净省 token） | `SoftmaxCalculator` / `MathUtility.WeightedAverage`（如需非线性融合） | 让既有信号真正改决策省 token |
| P4（可选·兜底估算） | 分层估算 + 分密度：仅当无遥测且逼近阈值 | `MathUtility.LinearInterpolation`（分层密度）、`StatisticalAnalysis` | 兜底决策正确（低杠杆） |
| P5 | 边界场景补齐 + 遥测可观测 + 单测 | `MathUtility` 边界处理 | 稳定性 |

**逐步实施细化**：

- **P0**：写 `BoundaryPressureCalculator` 基线单测，确认手写 `Score=Σ(wᵢ·pᵢ)` 与 `MathUtility.WeightedAverage` 结果一致（为后续替换做回归锚）。
- **P1**：新增 `ProjectReference`；`ContextPressure` 响应曲线用 `ActivationFunctions.Sigmoid(拐点平移)`；迟滞阈值判定用 `MathUtility.LinearInterpolation` 构造"触发阈值/复位阈值"两档。
- **P2**：`rate_t = MathUtility.WeightedAverage(窗口rate, 指数权重)`（无新 EWMA 实现）；用 `StatisticalAnalysis.Variance`（低方差=平台期）与 `Skewness`（<0=回退倾向）辅助 trend；`ComplexityMaxSteps` 用 `MathUtility.Percentile` 取自适应无进展阈值。
- **P3**：若动作差异化需要非线性区分，用 `SoftmaxCalculator.Compute` 对多动作得分软归一；否则沿用 `WeightedAverage`。
- **P4**：分层密度系数与分段曲线用 `MathUtility.LinearInterpolation` 插值，避免硬编码断点。

> **阶段门槛（元目的）**：每阶段新增计算量须先通过 §〇 五问契约——尤其第④问（净省 token）；不省 token 者不并入。
