# formula.md 专项逻辑分析报告（当前状态复核）

> 分析对象：`clinerules/formula.md`（552 行，当前工作区状态）
> 对照权威源：`clinerules/axioms.md`（327 行）· `clinerules/dsl.md`（260 行）· `clinerules/theorems.md`（149 行）
> 前置背景：`docs/analysis/clinerules-logic-analysis.md`（2026-08-03）已完成第一轮全库分析并全部修复（终态校验 208 符号 / 悬空 0 / 废弃 0 / 循环依赖 0）。
> 本报告定位：对**修复后当前状态**的专项复核——① 验证已修复项是否引入回归；② 补查上一轮遗漏项；③ 对初轮分析中误判项予以撤销。
> 分析日期：2026-08-03

---

## 结论速览

| 类别 | 数量 | 关键条目 |
|------|------|----------|
| 错误逻辑（E） | 10 | **E1 恢复权重"×1.5"悬空引用**、E2 compilation_error 与计数规则矛盾（回归）、E3 skip_remaining 死元素、E4 CountTable 时序未定义、E5 QDC 缺 causal_valid、E6 CF 衰减无授权、E7 跨层序新增、E8 §1.1 轴未声明、E9 ⊥投影表述、E10 OnError 双入口 |
| 重复逻辑（D） | 7 | **D1 Profile delta 违反自身 Delta 原则（上轮遗漏）**、D2 switch 熔断双写、D3 AttentionBudget 双述、D4 compilation_error 三处、D5 InitHook 双处、D6 熔断上限双述、D7 ctx_watermark 双处 |
| 冗余逻辑（R） | 4 | R1 §4.2/§4.3 MetaGate 复述、R2 权威注释散置、R3 FormatSelfCorrection 三处引用、R4 prose 与 [SEP] 重复 |

> 相比上一轮报告：**本报告撤销初轮 E3 误判**（NO_DATA→switch_strategy 实为 axioms 已授权）；**新增回归项 E2**（上轮 E4 修复副作用）与**上轮遗漏项 D1**（Profile delta 只修了与 axioms 默认重复，漏了与 formula 自身默认重复）。

---

## 一、错误 / 不一致逻辑（按严重度排序）

### E1.【高】§6.2 恢复权重"×1.5（对齐 §4.4）"为悬空引用
- **位置**：formula.md §6.2 注释（L384）`恢复联动: 压缩恢复后按问题域×1.5 恢复权重(对齐 §4.4)`
- **对照**：
  - §4.4「初始化」内容仅为 `R.init(IC=0)` 字段具值 / `R.init(IC>0)` 继承上轮状态——**无任何权重恢复语义**，"对齐 §4.4" 指向错位。
  - 同节 §6.2 CompactDecision.restore（L391）另有 `恢复后按 RECENT_CHAIN_COUNT 条推理链问题域恢复权重`——**无 ×1.5 系数**。
  - 全库（axioms/dsl/theorems）均无"问题域权重 ×1.5"公式。
- **结论**：`×1.5` 系数无权威来源；"对齐 §4.4" 引用错位；同节两处恢复描述不一致（一处带系数、一处不带）。

### E2.【高·回归】§5.2 compilation_error→continue 与 OnErrorCounterRules 计数矛盾（上轮 E4 修复引入）
- **位置**：§5.2 OnErrorDecisionTree（L279）`error=compilation_error → continue`（上轮修复：前移至 CountTable 分支之前，消除短路遮蔽）vs OnErrorCounterRules（L287）`error∈{execution_failure,compilation_error} → retryCounter+=1`
- **矛盾**：
  - `continue` 语义（§七 ErrorHandler）：记录错误摘要 → **继续后续步骤（不重试当前步骤）**。
  - 但 `retryCounter+=1` 与 `CountTable.total+=1`（L288）仍照常触发。
  - 结果：连续 N 次 compilation_error 虽全部被 continue 优雅跳过（不重试），`retryCounter` 仍累计至 `ONERROR_RETRY_LIMIT(=3)` → 触发 `retryCounter≥ONERROR_RETRY_LIMIT → forcehalt`（L278）。
- **本质**：编译错误"不重试却被计入重试计数"，continue 绕过重试机制但未绕过熔断计数，"重试链熔断"（本意限制无意义重试）对 continue 路径误伤。
- **修复方向**：continue 分支应跳过 retryCounter 累计（将 L287 限定为 `error∈{execution_failure}` 或 continue 前置时不计入 retryCounter），或明确"编译错误累计也应对应 continue 侧熔断语义"。

### E3.【中】§5.2 动作集声明含 `skip_remaining`，但决策树无任何产生路径（死元素）
- **位置**：§5.2（L274）`$ OnError(...)→{retry,switch_strategy,halt,halt+persist,continue,skip_remaining,forcehalt}`
- **核查**：
  - OnErrorDecisionTree 全分支输出：forcehalt / continue / retry / switch_strategy / Adjust_Parser / halt+persist / shared-branch。
  - shared-branch（§6.1 L340）：`continue | halt | report+halt`——无 skip_remaining。
  - §6.1 单次版 OnError（L342-345）：`continue | report+retry_optional`——无 skip_remaining。
  - 全库中 `skip_remaining` 仅出现于 §5.2 动作集声明与 §七 ErrorHandler 动作映射（L470）——**产生源缺失**。
- **结论**：声明的有效输出元素无触发分支，§七 ErrorHandler 对应动作为不可达代码。

### E4.【中】§5.2 CountTable 计数时序未定义 → `total=0→retry` 分支可达性依赖未声明时序
- **位置**：§5.2（L288）`CountTable(T,params).total+=1(每次执行)` vs （L280）`CountTable(T,params).total=0 → retry(瞬态)`
- **问题**：
  - 若 `total+=1` 于 Execute 后、OnError 判定前执行，则任何已执行过的工具进入 OnError 时 total≥1 恒成立，`total=0→retry` 分支**仅首次执行即失败时可达**；
  - 若 `total+=1` 于 OnError 判定后执行，则首次失败 total=0→retry，但"每次执行"措辞倾向前者。
  - 文档未定义该时序（仅 L219 说明 OnError 执行于 Execute→Collect 间，未细化 total 更新与分支判定的先后）。
- **结论**：FirstCallTransient（total=0→retry）分支的触发条件依赖未声明时序，属语义不完整。

### E5.【中】§4.6 QDC++ 引用权威公式时省略 `causal_valid` 条件
- **位置**：§4.6 EvaluateDrivenUpdate（L239）`eval=partial∧q(current)<q(best) → QDC++(唯一权威: [CR: axioms.md TerminationPriority.qdc_increment])`
- **对照**：axioms.md TerminationPriority.qdc_increment（L242）`(ΔQDC=+1)↔eval=partial∧q(current)<q(best)∧causal_valid`
- **结论**：标注"唯一权威"却省略合取条件 `causal_valid`，引用不忠实（作为参数化实例亦应完整继承前置条件）。

### E6.【中】§4.6 CF 衰减公式 `max(0,floor(preEvaluateCF/2))` 为 axioms 未授权
- **位置**：§4.6 DecideDrivenUpdate（L246）`decide(switch_strategy) → ... CF=max(0,floor(preEvaluateCF/2))`
- **对照**：axioms.md TerminationPriority（L246）`decay_scope(R,switch_strategy)=R.local(仅作用本次局部CF...)`——axioms 仅定义衰减**作用域**（局部），**未定义衰减函数**（/2、floor、max）。
- **结论**：具体衰减公式为 formula 自创；axioms L246"例: 决策驱动更新实例化"提供了实例化授权框架，但具体系数缺 `@inst` 关联与公理参数化依据。
- **备注**：初轮分析曾误判该条与 NO_DATA 同类，经复核——NO_DATA→switch_strategy 已被 axioms 显式授权（见撤销声明 §四），本条 CF 衰减公式则确认无授权。

### E7.【中】§1.3 跨层优先级新增 axioms 未定义的层内排序
- **位置**：§1.3（L53）`跨层优先级(对齐 AxiomHierarchyStructure): ... [领域公理(L2) > 规范(L2) > 策略(L2) > 反模式(L2)] > 流程(L3) > 配置参数(L3)`
- **对照**：axioms.md AxiomHierarchyStructure（L19-23）仅定义 `LAYER_ORDER=[L0,L0.5,L1,L2,L3]` 跨层全序——**无层内"公理>规范>策略>反模式"细粒度序**，亦无"流程>配置参数"的 L3 内排序。
- **结论**：新增层内排序语义却标注"对齐 AxiomHierarchyStructure"（未标 @inst）；且"社会共识(L1)"为领域轴语义，与 §二 引擎轴声明（L1=控制流/迭代）混用（见 E8）。
- **修复方向**：改为 `[CR: axioms:AX-CORE.AxiomHierarchyStructure@inst]` 并注明层内序为引擎侧裁决实例化，或上收至 axioms。

### E8.【中】§1.1 规则分类层级表未声明所用轴，且"流程(L3)"不匹配任一轴
- **位置**：§1.1（L24-30）类型表 `领域公理(L2)/策略(L2-L3)/规范(L2)/反模式(L2-L3)/流程(L3)`
- **对照**：
  - 领域轴（axioms AxiomAxisDeclaration）：L2=领域公理、L3=配置参数——**"流程(L3)"不对应**（领域轴 L3 无流程）；
  - 引擎轴（formula §二）：L2=实现细节/代码质量、L3=工具调用/文件操作——**"领域公理(L2)"不对应**（引擎轴 L2 无公理）。
- **结论**：§1.1 表未声明轴，两轴均无法完整容纳该分类（尤其 L3 行），与 §二 顶部轴声明（EngineLayerAxis）无衔接依据。
- **修复方向**：§1.1 若指领域轴应显式声明并修正 L3 行；若指引擎轴应重排分类——当前处于两轴夹缝。

### E9.【低】§5.4 ⊥投影"该维度:=T"赋值式表述与 axioms"按 T 投影"不一致
- **位置**：§5.4（L323）`⊥投影: ⊥(ExternalConsistency)∨⊥(HistoricalIntegrity)→该维度:=T(权威: [CR: axioms.md:TruthValueDomainLogic] 聚合例外)`
- **对照**：axioms L126 `聚合例外: ... ⊥ 按 T 投影, 不参与⊥吸收律聚合`——投影不改 Scott 域真值表。
- **结论**："该维度:=T" 易被读作维度赋值（改真值），与"投影例外"语义有偏差；建议改为"该维度按 T 参与聚合（投影例外）"。

### E10.【低】`OnError` 同名双入口，靠别名与交叉说明区分，引用易混淆
- **位置**：§5.2 循环版（L274）vs §6.1 `[CD] OnError(error)（别名 OnError_SingleCall）`（L342）
- **问题**：两个同名符号（循环版 OnError 为 §5.2 段落标题、单次版为 [CD] OnError），且 §5.2 注"单次调用版见 §6.1"、§6.1 注"见 §5.2"，交叉引用形成循环说明；dsl 载体映射下 `[CD]` 与 `[Gate]` 均落 Gate 域，符号索引层面 OnError 双语义（上轮已修复点号解析，但语义层命名仍并列）。
- **建议**：循环版显式改名（如 OnErrorLoop）或在 §6.1 别名处加 `[SEP] LoopVsSingleScope` 双向锚点（现仅 §6.4 单侧声明）。

---

## 二、重复逻辑（同一语义 / 常量出现在多处）

### D1.【高·上轮遗漏】§3.2 Profile delta 违反自身 Delta 原则（与 formula 自身默认值重复）
- **位置**：§3.2（L119-121）vs §3.1（L100/L102）
- **核查**（上轮 D1 只删了与 **axioms 默认**重复的项，漏了与 **formula §3.1 自身默认**重复的项）：
  | Profile | delta 声明 | formula §3.1 默认 | 判定 |
  |---------|-----------|------------------|------|
  | CAMPAIGN | `PARTIAL_RATIO_THRESHOLD=0.8` | `0.8`（L100） | **同值，违反 Delta 原则** |
  | DEVELOP | `PARTIAL_RATIO_THRESHOLD=0.8` | `0.8`（L100） | **同值，违反** |
  | DEVELOP | `SUBROUND_MAX_ATTEMPTS=3` | `3`（L102） | **同值，违反** |
  | SAFE | `MAX_ITER=5 / PARTIAL_RATIO_THRESHOLD=1.0 / SUBROUND_MAX_ATTEMPTS=2` | 10 / 0.8 / 3 | 均为差异 ✓ |
- **讽刺**：DEVELOP 注释（L120）自述"MAX_ITER=10 与默认值相同, 依 Delta 原则不声明"——作者有 Delta 意识，却漏掉同 delta 块内另外两项同值项；CAMPAIGN 注释（L121）自述其余项与默认相同不声明，但保留的 PARTIAL_RATIO_THRESHOLD=0.8 自身也同默认（依原则 CAMPAIGN delta 应为空）。
- **结论**：CAMPAIGN/DEVELOP 的 delta 表失去"差异快照"纯语义；上轮修复不完整。

### D2.【中】switch_strategy 熔断降级判定双写
- **位置**：§5.2 OnErrorDecisionTree（L279）`switchCounter≥ONERROR_SWITCH_LIMIT → forcehalt` 与 §5.2 SwitchCounterLinkage（L293）`switchCounter≥ONERROR_SWITCH_LIMIT → switch_strategy 降级 forcehalt`
- **结论**：同一判定条件写在两个 [Gate]/[SM] 中，语义重复；建议一处引用一处。

### D3.【中】AttentionBudget 约束双述
- **位置**：§2.1 注释（L71）`ctx_factor = c.attentionBudget(scope)/Σ...` vs §6.2 `[CC] AttentionBudget: Σw≤BudgetAlloc(phase)`（L383），两者均声明"权威见 axioms AttentionWeighting.ctx_factor"
- **结论**：上轮 D6 已将 `$ ctx_factor` 降为注释，但归一化公式正文仍在 §2.1 注释中，与 §6.2 约束重复（文本级）。建议 §2.1 仅保留系数实例化说明，归一化公式引 §6.2/axioms。

### D4.【低】compilation_error→continue 三处动作文本
- **位置**：§6.1 shared-branch（L340）`error∈{compilation_error}→continue`；§6.1 OnError 别名（L343）`compilation_error → continue`；§5.2 决策树（L279）`error=compilation_error → continue`
- **结论**：虽各有分工注释（"仅分类"/"仅决策"），动作文本三处重复；建议共享分支仅保留一处权威动作，其余 [CR] 引用。

### D5.【低】InitHook 跳过语义双处声明
- **位置**：§3.5 InitHook（L157-159）`fileCache=∅→skipExternalConsistency ∧ skipHistoricalIntegrity...`；§5.4（L323/324）`⊥投影...对齐 InitHook "fileCache=∅→跳过 External/Historical"` + `init_hook = "行为权威定义见 §3.5 InitHook, 此处不重复"`
- **结论**：已注明"不重复"，但⊥投影处仍复述跳过语义，双处维护同一事实（一处"跳过标志"、一处"投影为T"），建议 §5.4 仅引用。

### D6.【低】CountTable 空结果熔断上限双述
- **位置**：§5.2 注释（L275）`熔断上限(工具级 CountTable(T,·))优先于参数级 empty 分类` + 分支（L278）`CountTable(T,·).empties≥ONERROR_EMPTY_T_LIMIT → forcehalt`
- **结论**：注释与分支独立陈述同一"工具级优先"语义，可合并。

### D7.【低】ctx_watermark>85% 阈值双处
- **位置**：§2.1 注释（L71）`ctx_watermark>85% 时×0.5 收窄`；§2.4 FocusFeedback（L90）`ctx_watermark>85% → ... 收窄 ctx_factor×0.5`
- **结论**：两处一致（均 0.5），非矛盾但重复；建议阈值常量上收 §3.1 参数。

---

## 三、冗余逻辑（可精简表述）

### R1. §4.2 InstanceChain 与 §4.3 LoopModel 的 MetaGate 说明重复
- §4.2（L177）定义 MetaGate 完整语义；§4.3 注释（L183）`与 §4.2 差异仅在 MetaGate` 复述插入条件 `(Decide=TM∧R.local≥META_THRESHOLD)`。
- 建议 §4.3 仅 `[CR]` 指向 §4.2。

### R2. §4.6 两处"唯一权威"注释散置
- §4.6 EvaluateDrivenUpdate（L239）与 DecideDrivenUpdate（L246）各自行内注释权威引用点；同节上早已声明"递增/重置公式唯一权威见各定义点"（§4.5 L219）。
- 建议收敛为节级统一引用声明。

### R3. FormatSelfCorrection 触发链路三处引用
- §6.1 FormatCheck 注释（L364）、§5.2 parse_failure 分支（L282）、§七 FormatSelfCorrection 定义（L447-453）三处提及 TH-6/TH-8 触发。
- 建议统一由 §七 定义承载，§5.2/§6.1 仅 [CR] 指向。

### R4. §5.2 顶部 prose 与 §6.4 [SEP] 分工重复
- §5.2 顶部注释（L271-272）"与单次版差异..."与 §6.4 `[SEP] LoopVsSingleScope`（L408）重复陈述"循环版/单次版动作域互斥"机制。
- [SEP] 已形式化该分工，prose 可精简为指向。

---

## 四、初轮分析撤销声明（诚实性复核）

| 初轮条目 | 结论 | 撤销原因 |
|----------|------|----------|
| ~~E3: NO_DATA→switch_strategy 越权~~ | **撤销** | axioms.md IntentClassification（L190）已显式声明 `空结果计数实例化: total≥1∧empties≥1→switch_strategy`——formula §5.2 分支正是该公理实例化，非越权。 |
| ~~E2: CF 衰减自创~~ | **降级** | axioms 的 `例: 决策驱动更新实例化`（L246）提供实例化授权框架，但具体 `/2/floor` 系数仍无依据（保留为 E6，降级为中）。 |
| E1（QDC 缺 causal_valid） | 保留 | 已验证 axioms 权威含 extra 条件。 |
| E4/E5/E6（轴/跨层序/⊥投影） | 保留 | 与复核结果一致。 |

---

## 五、修复建议汇总

| 编号 | 类别 | 建议动作 | 涉及文件位置 |
|------|------|----------|--------------|
| E1 | 错误 | `×1.5` 删除或上收 §3.1 参数并修正"对齐 §4.4"引用（§4.4 无权重恢复语义），与 CompactDecision.restore 合并为单一恢复公式 | formula.md §6.2 |
| E2 | 错误·回归 | `continue` 分支跳过 retryCounter/CountTable 累计，或为 compile_error 定义独立计数语义（当前 continue 却计数熔断，语义矛盾） | formula.md §5.2 |
| E3 | 错误 | 删除动作集 `skip_remaining` 或为决策树增加产生分支（全库无产生源） | formula.md §5.2 |
| E4 | 错误 | 显式声明 CountTable.total+=1 与 OnError 分支判定的时序先后 | formula.md §5.2 |
| E5 | 错误 | QDC++ 分支补充 `causal_valid` 前置条件（与 axioms 权威一致） | formula.md §4.6 |
| E6 | 错误 | CF 衰减公式补 `[CR] @inst` 关联或上收 axioms 参数化授权 | formula.md §4.6 + axioms.md |
| E7 | 错误 | 跨层优先级标注实例化意图，层内排序上收 axioms 或显式 @inst | formula.md §1.3 |
| E8 | 错误 | §1.1 显式声明所用轴并修正 L3 行（当前两轴均不容纳"流程(L3)"） | formula.md §1.1 |
| E9 | 错误 | ⊥投影改为"按 T 参与聚合（投影例外）"措辞 | formula.md §5.4 |
| E10 | 错误 | 循环版 OnError 改名或增强双向锚点 | formula.md §5.2/§6.1 |
| D1 | 重复 | CAMPAIGN delta 置空（或删 PARTIAL_RATIO_THRESHOLD=0.8）；DEVELOP 删 PARTIAL_RATIO_THRESHOLD=0.8、SUBROUND_MAX_ATTEMPTS=3 | formula.md §3.2 |
| D2 | 重复 | SwitchCounterLinkage 与决策树熔断分支互引，去一处 | formula.md §5.2 |
| D3 | 重复 | §2.1 归一化公式收敛至 §6.2/axioms 引用 | formula.md §2.1 |
| D4/D5/D6/D7 | 重复 | 保留单一权威动作/事实点，其余 [CR] 引用 | formula.md 各处 |
| R1-R4 | 冗余 | 收敛为单一定义点 + 引用 | formula.md §4.2/§4.3/§4.6/§5.2/§七 |

---

## 附：与上一轮报告的差异说明

- **上轮已修复且复核通过**：§5.2 compilation_error 分支前置（E4）、`[SEP] LoopVsSingleScope`（E5）、§8.2 反模式三行并入（R5）、§3.2 与 axioms 默认重复项删除（D1 部分）、`$ ctx_factor` 降注释（D6）、CountTable `$` 双符号消除（E8）、§4.5 收敛映射（D3）。
- **上轮修复引入的回归**：见 **E2**（compilation_error 前置后未同步调整计数规则）。
- **上轮遗漏项**：见 **D1**（Profile delta 与 formula 自身默认重复）、**E1**（×1.5 悬空引用）、**E3**（skip_remaining 死元素）、**E4**（CountTable 时序未定义）——上轮分析未覆盖。