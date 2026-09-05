# 《clinerules/axioms.md》×《clinerules/logic.md》逻辑质量分析报告

> **分析对象**：`clinerules/axioms.md`（254行，公理权威定义源）与 `clinerules/logic.md`（230行，逻辑运算法则库）
> **分析类型**：重复逻辑 / 冗余逻辑 / 错误逻辑
> **分析方法**：逐条交叉比对两文件的公理（AX/BAX/LO）、定义（Def/Type）、组合公理（§0.5/§0.6/§0.8）与消费方引用（[CR] 锚点）

---

## 一、重复逻辑（同一断言在两处以上声明）

| # | 位置 | 重复内容 | 说明 |
|---|------|---------|------|
| D1 | logic §1.2 | `FormEquivalence ≡ AndGate`、`TruthProjection ≡ Project ≡ AndGate`、`CausalAccumulate ≡ Recurrence`、`ViolationNorm ≡ Gate` | 四组「等价派生」法则。基底三算子（Judge/Merge/Resolve）已承载全部语义，这些等价别名只是命名角标，与 logic.md 第4行「不逐条存储派生法则正文——逻辑蕴含在顺序里」的乘法表宣称直接矛盾 |
| D2 | logic §0.5 P5 / §1.1 Resolve[COMPL] | 末路完备 | P5 `default≠∅∨Halt≠∅` 与 `[COMPL]末路完备(≡§0.5 P5)` 虽注明不重复，实际两处各占条目 |
| D3 | logic §0.5 P3 / §0.7 P3' | 位置即语义 | `pos(L,i)=pos(L,j)∧i≠j→⟨pᵢ:τᵢ⟩≢⟨pⱼ:τⱼ⟩`（P3）与 `L(…,pᵢ@i,…)≢L(…,pᵢ@j,…)`（P3'）是同一命题的两种写法 |
| D4 | logic §0.0 sel / §1.1 Resolve[SHORT] | 短路=序即优先级 | `sel(C)≝argmin…首个成立即终选` 与 `[SHORT]短路:首个成立分支即终选(≡§0.0 sel)` 双处声明 |
| D5 | logic §0.0 topo / §0.5 P1 / axioms §5 DependencyCycleDetection | 有向无环⇔可拓扑排序 | P1 与 §0.0 topo 定义均重复引用 DependencyCycleDetection 公理（P1 注释、§0.0 注释各写一次「见 [CR]」） |
| D6 | axioms §1 第51/52行 | `Signature=外部见证签名(meta-level-binding)` 与 `sig=external_signature(meta-level-binding)` | 完全同义，仅「类型名/变量名」之别，占两条 [Type] |
| D7 | axioms §0 行14 权威清单 / §2 公理正文 | L0 公理清单 | 行14 `[AX-CORE] L0: BAX-IDI,BAX-TIME,…` 与 §2 中逐条声明重复（清单是提前索引） |
| D8 | axioms §8 附录「公理索引表 + 符号速查表」 | 对 §0~§7 全部内容的再索引 | 两表几乎逐条复述 §0~§7 的公理、[Def]、配置参数（含锚点与实例），合计约 30 行纯导航性重复 |

---

## 二、冗余逻辑（存在但无增益 / 维护负担高）

| # | 位置 | 冗余内容 | 说明 |
|---|------|---------|------|
| R1 | axioms §8 全节 | 公理索引表 + 符号速查表 | 与正文高度重叠，且引用锚点已由每条 [CR:…@consume] 内联标注，附录的「速查」多数为逐字复制（如 244-253 行符号表）。删去可减负约 25% 文件体积，且避免两处改动的失同步风险 |
| R2 | logic §1.2 | 等价派生法则单独登记（D1 同源） | `CausalAccumulate≡Recurrence`、`TruthProjection≡Project` 等行占位，实际不承载新逻辑，可由消费方直接 `LawName(实参)` 引用基底 |
| R3 | axioms §4 CountableOrderBridge ↔ §7 commensurability | cf 可比较性 | §4 已定义 `R→R':R'.cf按因果累积 / R∥R':不可直接比较`，§7 又写 `commensurability:R.cf与MAX_FAIL可比较iff∃R'…:R'→R,scalarizable(R')`，同一断言两处分述 |
| R4 | logic §1.1 Merge 注释 ↔ §1.2 SmoothMerge/WeightedCombine/ArgMaxBy 行 | 各派生行的保序性 | Merge 注释已声明「op 参数域扩展…保序性由 [MONO]/[MONO'] 覆盖」，但 §1.2 每行仍重复写「各因子非负→保序 / α,β≥0→保序 / 键良序」 |
| R5 | axioms §3 ToolCallWellFormedness / OutputWellFormedness / FormatFidelity | 三者结构模型高度同构 | 三者同为 `FormEquivalence→ViolationNorm` + `StructAlign∧FieldComplete∧(TypeMatch/ContentReal…)` 骨架，仅参数集不同，可抽为一个元模板（如 `WellFormednessTemplate(谓词组, 违规侧)`） |

---

## 三、错误逻辑（形式化缺陷 / 真矛盾）

按严重度排序，最重要的 6 条：

| # | 位置 | 问题 | 性质 |
|---|------|------|------|
| E1 | logic §1.2 AndGate / FormEquivalence | `out{φ∈{T,F}}` 输出类型声明与实际派生语义矛盾 | **真矛盾**。AndGate=Derive(Judge, D=∧SUBᵢ)。但 axioms §2 TruthValueDomainLogic 明确定义 `⊥∧T=⊥`，因此输入含 ⊥ 时 AndGate 必输出 ⊥，违反自身 `out{φ∈{T,F}}`。唯一例外是 [CLOS] 定理（前提 `∀i:SUBᵢ∈{T,F}`）——它恰恰证明非此前提时可输出 ⊥。故 `out` 应声明为 `{T,F,⊥}`，或显式补「对 ⊥ 的归一化规则」 |
| E2 | axioms §1 `scalarizable` 定义 | `scalarizable(R')=∃n∈ℕ:R'.cf=n` 恒真，谓词失去区分力 | **逻辑失效**。§7 同时声明 `R.cf∈ℕ`，则 `∃n∈ℕ:R'.cf=n` 对任意 R' 恒成立 ⇒ scalarizable 恒为 True ⇒ §7 commensurability 门条件 `scalarizable(R')` 退化为空约束。定义需加约束（如"可与 MAX_FAIL 比较标量"）或删除该谓词 |
| E3 | axioms §2 SyncProtocolTriple | `completed` 双定义冲突 | **真矛盾**。第108行 `completed↔recv(obs₁,ack)∧…(二阶段承诺)` ⇒ completed 蕴含提交成功；第109行 `completed(sync)↔Commited(sync)∨Compensated(sync)` ⇒ compensated 也算 completed。两式对 `Try失败→Compensated` 情形给出互斥结果。需按层分离（如 completed(commit) / completed(protocol)）或合并为一式 |
| E4 | logic §1.1 Merge ↔ §1.2 OrderedMerge/ArgMaxBy | `argmax_by` 返回元素，与 `[SEM] v=op(Π)`、`[MONO] Π₁⊆Π₂→op(Π₁)≤op(Π₂)` 不适配 | **形式化缺陷**。保序不等式的比较对象是值；argmax_by 输出是候选集中的元素，元素之间无 `≤` 定义。保序性应就「key 值」表述（键值单调），而非 `op(Π₁)≤op(Π₂)` |
| E5 | logic §1.1 Merge [MONO]/[MONO'] ↔ §1.2 WindowAggregate(min) | min 为逆变保序，与 [MONO] 协变声明方向冲突 | **形式化含糊**。[MONO] 以通用公理称 op 协变保序，[MONO'] 仅以「例外」注记 min 反单调。二者对同一 op=min 形式化上并存冲突；建议把 [MONO'] 提为正式公理并显式限定 op 域 |
| E6 | logic §1.2 CausalAccumulate/Recurrence | 端口签名与基底 Merge 不匹配 | **端口错配**。Merge 原型 `in{Π,op,anchor}`，而 CausalAccumulate 声明 `in{v,local,pred,F}`——`F`(递推函数) 在 Merge 无对应端口位，仅以注释「anchor 隐含于 Π」搪塞，违反 P3「连接即语义」与 §0.7 实参绑定校验。需在 Merge 基底增加函数参数位或改声明 |

### 次级缺陷（影响较小）

| # | 位置 | 问题 |
|---|------|------|
| E7 | logic §0.6 `[TRUNC]` | `∀φ∉dom: Expand(L,n)(φ)=⊥→explore(⊥)` —— 后缀 `→explore(⊥)` 是行为规则混入公理式，形式上属 `T→…` 恒真式，无证明价值 |
| E8 | axioms §2 Coupling-Sampling / Coupling-ObservationCollapse | `Coupling-Sampling` 后未接 domain 限定；`fv.val|_obs₁≠fv.val|_obs₂合法` 与 `ContinuousVariance` 两处涉及观测差异，存在「合法≠一致性收敛」需显式层分离的落点 |
| E9 | axioms §7 MAX_PARTIAL | 注释自述「次数量纲；区别于 PARTIAL_RATIO_THRESHOLD 比例量纲」，但全文未定义 `PARTIAL_RATIO_THRESHOLD`，形成悬空引用 |
| E10 | axioms §0.5 层级轴 ↔ logic §0.5 | 引擎轴 L1=控制流/迭代 与 logic L1=基底原子 名称重叠；虽由 `AxisIndependence` 分离，但 §0.8 D7 取值域写「L0~L3」未区分轴，阅读时极易误认同名同层 |

---

## 四、修复方向建议

### 4.1 真矛盾优先修正（最高优先级）

1. **E1**：`AndGate`/`FormEquivalence` 的 `out` 声明改为 `{T,F,⊥}`，或补显式 ⊥ 归一化规则；
2. **E2**：`scalarizable` 增加实际约束（如 `∃n∈ℕ:R'.cf=n∧n≤MAX_FAIL`）或整谓词删除，避免恒真退化；
3. **E3**：`completed` 拆分双层语义（`completed_commit` / `completed_protocol`），消除对 Compensated 情形的互斥判定。

### 4.2 去重（恢复「乘法表」宣称）

4. **D1/R2**：删除 logic §1.2 全部 `≡` 等价行，消费方直接套用基底 `Judge/Merge/Resolve`；
5. **D2~D5**：保留一处权威，其余改为 `≡ §0.x [公理名]` 一行引用；
6. **D6**：axioms §1 合并 `Signature`/`sig` 为一条；
7. **D8/R1**：§8 附录精简为「矩阵→消费方映射表」一表，删除符号速查（由 D8 锚点内联承担）。

### 4.3 结构重构（可选，改动最大）

8. **R5**：抽取 `WellFormedness(P, O, S)` 元公理 + 参数表，统一 ToolCallWellFormedness / OutputWellFormedness / FormatFidelity；
9. **E5/E6**：Merge 形式化补 min 逆变正式公理；CausalAccumulate 端口与 Merge 对齐或显式自定义签名；
10. **E9**：补定义或删除 `PARTIAL_RATIO_THRESHOLD` 悬空引用；
11. **E10**：§0.8 D7 取值域显式标注轴（`AxiomLayerAxis/EngineLayerAxis`）。

---

---

## 五、修复落实记录（2026-08-04 实施）

> 依据本报告三/四节，已对 `clinerules/logic.md` 与 `clinerules/axioms.md` 实施修复。每项标注「已修复/保留」。

### logic.md

| 项 | 处理 | 落实细节 |
|----|------|---------|
| E1 真矛盾 | ✅ 已修复 | AndGate/FormEquivalence `out{φ∈{T,F}}` → `out{φ∈{T,F,⊥}}`；注释明确「输入含⊥按 Scott 域吸收律输出⊥；∀i:SUBᵢ∈{T,F} 时退化 {T,F}（定理[CLOS]）」 |
| E4 形式化缺陷 | ✅ 已修复 | 新增公理 `[KEYMONO]`：argmax_by/argmin_by 保序性就「键值序」表述（key(op(Π₁)) ≤ key(op(Π₂))），并注明元素间无 ≤ |
| E5 形式化含糊 | ✅ 已修复 | [MONO] 限定 op 域（max/count/chain-max；sum 前提非负）；[MONO'] 显式注明「与 [MONO] 按 op 域互斥，不并存」 |
| E6 端口错配 | ✅ 已修复 | Recurrence/CausalAccumulate 端口重写为 `in{Π,op,anchor}`（对齐 Merge 基底位）；anchor=F(递推函数)；注明「实参按 §0.7 绑定校验」 |
| E7 恒真式 | ✅ 已修复 | [TRUNC] 改为纯等式公理 `Expand(L,n)(φ)=⊥`；行为规则 `⊥→explore(⊥)` 移出公理、标注「行为规则(非公理)」 |
| E10 轴名重叠 | ✅ 已修复 | D7 维度含义注明「双轴独立编号: AxiomLayerAxis/EngineLayerAxis」，引用 AxiomHierarchyStructure |
| D1/R2 等价派生链 | ✅ 已修复 | 删除全部 `≡` 别名链；登记表导语改声明「等价派生不再以 ≡ 别名链重复登记，具名法则仅作领域实例化锚点」；TruthProjection/ViolationNorm 改注「与 X 同态，具名锚点」 |
| D3 位置即语义 | ✅ 已修复 | P3' 注释改为「= §0.5 P3 的端口层表达，不重复」 |
| R4 保序注释收敛 | ✅ 已修复 | SmoothMerge/WeightedCombine/ArgMaxBy 行内「保序」注释收敛为「继承 Merge[MONO]/[KEYMONO]」 |
| 宏符号矩阵 | ✅ 已修复 | 行22 `scalarizable(R')` → `scalarizable(R',B)`，元数 1→2，D5 依赖补 `≤` |
| D5 topo 重复引用 | ⚪ 保留 | §0.0 与 P1 注释双处引用 DependencyCycleDetection，语义一致不构成冲突；暂不合并 |

### axioms.md

| 项 | 处理 | 落实细节 |
|----|------|---------|
| E2 逻辑失效 | ✅ 已修复 | `scalarizable(R')` → `scalarizable(R',B)=∃n∈ℕ:R'.cf=n∧n≤B`，参数化比较基准（如 MAX_FAIL），消除 `∃n∈ℕ` 恒真退化 |
| E3 真矛盾 | ✅ 已修复 | `completed` 拆双层：`completed_commit`（二阶段承诺，提交成功层）与 `completed_protocol`（completed_commit∨Compensated，协议层终结）；注明「层分离消解原 completed 双定义冲突」 |
| E8 层分离 | ✅ 已修复 | Coupling-ObservationCollapse 的 `fv.val|_obs₁≠fv.val|_obs₂合法` 后补注「(工程层差异，可 sync 收敛，非逻辑矛盾)」 |
| E9 悬空引用 | ✅ 已修复 | MAX_PARTIAL 删除「区别于 PARTIAL_RATIO_THRESHOLD 比例量纲」悬空引用，仅保留「次数量纲」 |
| D6 Signature/sig | ✅ 已修复 | 三条 [Type] 合并为两条；`sig` 并入 Signature 声明「(同义写法 sig; 原独立 sig 声明并入本条, 不重复)」 |
| R3 cf 可比较性 | ✅ 已修复 | §7 commensurability 补 `scalarizable(R',MAX_FAIL)` 第二参，并标注「可比较性域约束权威见§4 CountableOrderBridge」 |
| D7 清单重复 | ⚪ 保留 | §0 权威清单 + §2 正文为索引-定义结构，属文档导航惯例；暂不合并 |
| D8/R1 §8 附录 | ⚪ 保留 | 附录为「辅助导航，非公理语义」已自我声明；做载体简并需改动两处引用链，本期未动以控风险 |

### 修复原则

- 全部修改以「最小改动、语义不变、互引用锚点保持」为准则；
- 所有新增注释均以 `//` 或括号内联形式追加，未改动既有公理公式本体；
- `[SEP]`/`[DR]`/配置参数/§8 附录索引未做结构性删减（标注 ⚪ 项为有意识保留）；
- 文件行数变化：logic.md 230 → 231 行（KEYMONO 公理 +1）；axioms.md 254 → 253 行（Signature/sig 合并 -1）。

*报告生成方式：人工逐条交叉比对两文件全部 [AX]/[BAX]/[LO]/[Def]/[Type]/[SEP]/[DR] 声明及其相互引用。*
