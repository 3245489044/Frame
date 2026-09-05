# clinerules 逻辑优化记录

> 日期: 2026-08-03 | 范围: clinerules/ 全部 6 文件 + project.md
> 目标: 消除重复逻辑、冗余逻辑、错误逻辑，收敛单一权威源。

## 一、错误逻辑修复（E）

| ID | 文件 | 原问题 | 修复内容 |
|----|------|--------|----------|
| E1 | rules.md §1.3 | 跨层优先级链与 AxiomHierarchyStructure 冲突（L2 出现两次、策略/反模式跨层归属含混） | 重写为对齐层级轴的链: `L0 > L0.5 > L1 > [领域公理(L2) > 规范(L2) > 策略(L2) > 反模式(L2)] > 流程(L3) > 配置参数(L3)`，并注释说明 L2-L3 标注语义 |
| E2 | axioms.md §七 | QDT 悬空引用（缩写未定义） | 添加行内注释: `QDT=QUALITY_DECLINE_THRESHOLD(缩写, 区别于 QDC=当前计数)` |
| E3 | engine.md §3.1 | externalConsistency 默认真=true 掩盖冷启动未知 | 改为 `TriState=⊥`，明确 boot=⊥ 需经 sync_check 校验后置 T/F，对齐 TruthValueDomainLogic |
| E4 | axioms.md IDI-HistoryEmergence | `∀x` 裸量化违反 QuantificationConstraint | 修正为 `∀(x:Entity)` |
| E5 | theorems.md TH-2 | op.domain 引用未定义属性 | 在 axioms.md 添加 `[Type] Domain` 与 `[Def] op.domain ∈ Domain`，TH-2 src/公式标注引用锚点 |
| E7 | axioms.md §七 | proj_cf 语义歧义（前驱替换 vs 累加），且 undefined 跳过 CF 判定 | 重写为: 因果累积 `R.cf=max(R.local, 前驱R'.cf)`（符号对齐 CountableOrderBridge），链首 R.cf=R.local 不跳过；删除 proj_cf/undefined 路径 |

## 二、重复逻辑收敛（D）

| ID | 文件 | 原问题 | 修复内容 |
|----|------|--------|----------|
| D1 | theorems.md TH-1 | completed/enters 三处重复（注释/公理/推论） | 保留公理为唯一权威；TH-1 pf 标注"编译型推论(直接合取)，公理区为权威，此处仅速查" |
| D2/D3/D8 | engine.md §5.2 | CounterMaintenance 完整行为定义与 §5.3 公式重复 | 收敛为纯索引表，行为权威指向 §5.3/§七/axioms.md TerminationPriority(qdc_increment 唯一权威) |
| D5 | rules.md §6.3 | FormatStandard 别名冗余声明 | 精简注释: 明确无独立逻辑、仅引用兼容，权威指向 §5.1 FormatCheck |
| D6 | engine.md §3.1 | externalConsistency 默认值三处语义分裂 | 由 TriState=⊥ 统一 boot 语义，InitHook/StateRecovery 行为保持一致 |

## 三、冗余逻辑清理（R）

| ID | 文件 | 原问题 | 修复内容 |
|----|------|--------|----------|
| R2 | axioms.md | op.domain 悬空属性 | 添加类型与归属定义（见 E5） |
| R3 | project.md §1.5 | Exception 映射与 ThrowRule/FixPriority 脱节 | 添加消费方关联说明: 表驱动读取 projectConfig.exceptions/fixPriority |
| R4 | engine.md §2.2 | SAFE_SwitchLockdown 第二条分支为首条推论 | 合并为单一权威分支，注释说明推论关系 |
| E8 | dsl.md §1 / rules.md §1.2 | 分工注释跨文件整段重复 | dsl.md 精简为单向引用；rules.md 保留权威定义 |

## 四、新增定义（补齐悬空引用）

| 符号 | 位置 | 用途 |
|------|------|------|
| `[Type] Domain` | axioms.md 类型区 | op.domain 类型底座（TH-2） |
| `[Type] LoopDecisions` | axioms.md 类型区 | TH-3 循环决策输出集 |
| `[Def] op.domain ∈ Domain` | axioms.md 定义区 | 跨域判定 |
| `[Def] q(s)=quality(s)∈Quality` | axioms.md 定义区 | engine.md Evaluate 质量比较（Q quality 死类型被激活使用） |
| `[Def] R.goals` | axioms.md 定义区 | TH-3 决策目标追踪 |
| QDT 行内注释 | axioms.md §七 | 阈值缩写消歧 |

## 五、保留未改（说明）

- **OnError 双版（rules.md §5.1 / engine.md §七）**：两版职责不同（单次调用纯分类 vs 循环版含熔断），按 scope 区隔是设计意图，未合并，仅保留现有命名区分注释。
- **D7 算子重复（LogicalClock 内 a∥b 回指）**：引用式写法，可接受，未改。
- **E6 量纲分工**：已在 engine.md §5.3 添加分工注释（CP 次数 vs PARTIAL_RATIO 比例），不合并为单一阈值。

---

## 六、注意力逻辑优化（Attention Overhaul）

> 日期: 2026-08-03（第二轮）| 范围: axioms/engine/rules/dsl 四文件 | 目标: 二元静态注意力 → 连续权重 + 反馈调制 + 迭代收敛

### A 二元→连续权重

| ID | 文件 | 原局限 | 优化内容 |
|----|------|--------|----------|
| A1 | axioms.md §三 | FocusLayer/ScopeGuard 纯布尔, 低层规则恒满激活无区分度 | 新增 [AX] AttentionWeighting: attention_weight(r,p,c)=base×ctx_factor×feedback_factor; active↔w>ATTENTION_EPSILON 推导门(不破坏 FocusLayer 布尔语义) |
| A2 | axioms.md 类型/配置 | — | 新增 [Type] AttentionWeight/AttentionTemperature/Rules/Phases/Contexts; % ATTENTION_EPSILON=0.1 / % ATTENTION_DECAY_N=8 |
| A3 | engine.md §1.1 | 单一行 active 谓词 | 权重公式三因子具象化: base(α_base 层级降权×attentionTemperature)、ctx_factor(预算归一化/水位收窄)、feedback_factor(β_feedback 错误调制) |
| A4 | rules.md §5.2 | BudgetAlloc 与规则注意力脱钩 | 新增 [CC] AttentionBudget: Σw≤BudgetAlloc(phase), 与 ctx_factor 形成链路 |

### B 反馈闭环（焦点动态迁移）

| ID | 文件 | 优化内容 |
|----|------|----------|
| B1 | engine.md §1.4 | FocusDecision 改为"基线映射＋反馈调制 FocusFeedback": CF/compile_error→L1, parse_error→L3, pass 稳定回退基线, 水位>85% 收窄 ctx×0.5; 迁移为基线邻域调制非持久状态 |
| B2 | rules.md §七 | 新增 @ ANTI-FOCUS-STUCK(焦点粘连, 自动修复=FocusFeedback) |

### C 迭代收敛注意力

| ID | 文件 | 优化内容 |
|----|------|----------|
| C1 | engine.md §3.1/§5.3 | 新增 [StateVar] attentionTemperature + [SM] AttentionDecay: 随 iterationCount 单调收敛(下限 ATTENTION_TEMP_MIN=0.4, engine.md §2.1), 连续 pass 探索恢复, switch_strategy 回温再探索; 仅调制 base 因子不干扰 feedback |
| C2 | rules.md §5.2/§5.3 | AttentionBudget 恢复联动: CompactDecision 压缩恢复后按 RECENT_CHAIN_COUNT 推理链所属问题域恢复规则权重(×1.5), 非均匀恢复; CompactDecision 增加 restore 元数据 |
| C3 | rules.md §七 | 新增 @ ANTI-ATTENTION-SPREAD(注意力弥散, 自动修复=权重归一化+温度衰减) |

### D 轴消歧与冲突仲裁

| ID | 文件 | 优化内容 |
|----|------|----------|
| D1 | axioms.md §三 | FocusLayer/ScopeGuard 轴参数化注释: 引擎焦点路由必用 EngineLayerAxis, 领域引用必用 AxiomLayerAxis, 禁止 UNION 语义(两轴均含 L1) |
| D2 | axioms.md §三 | 新增 [AX] AttentionConflictResolution: scope 冲突按规则特异性>时间顺序裁决(复用 rules.md §1.3); 并行域重算仅轮次边界(联动 ParallelMerge) |

### 配套语法

| 文件 | 内容 |
|------|------|
| dsl.md §1 | 新增 [Attention] 声明载体 + 模板示例 |
| dsl.md §2 | 注册 restore 元数据键 |
| dsl.md §3 | 新增 attentionTemperature/α_base/β_feedback/ATTENTION_EPSILON/FocusFeedback 缩写 |

### 设计澄清（消歧）

- **权重 vs 激活双层语义**: attention_weight 为连续调制(影响规则在 LLM 上下文的相对注意配比), active↔w>ε 为推导门(保留 FocusLayer/ScopeGuard 布尔激活公共语义), 两层不冲突。
- **attentionTemperature 与 SWITCH_DECAY_N 分工**: 前者管注意力收敛(engine.md §5.3 AttentionDecay), 后者管 switchCounter 计数衰减(§七 SwitchCounterLinkage), 独立不互斥。
- **轴参数化必要性**: 领域公理轴 L1=社会共识 vs 引擎运算轴 L1=控制流, 同值不同语义, 路由实例化必须显式绑定。

---

## 七、公理冲突消解（Consistency 第三轮）

> 日期: 2026-08-03（第三轮）| 范围: axioms.md 仅（engine/rules 为正确方, 不需改动）| 目标: 消除公式级真冲突与注释豁免型语义张力
> 前置: clinerules_analysis.md 冲突判定——1 处公式级真冲突(active 谓词) + 5 处注释豁免型张力。

| ID | 文件 | 原问题 | 修复内容 |
|----|------|--------|----------|
| C1 | axioms.md §三 AttentionWeighting | active 定义与 engine.md §1.1 不等价(axioms `↔w>ε` 单分量 vs engine 三合取) | 改为完整三合取: `active(r)↔w(r)>ε∧layer(r)≥R.layer∧scope(r)∈R.scope`; 权重门降为必要条件(第一分量), 公共权威 FocusLayer∧ScopeGuard |
| C2 | axioms.md TruthValueDomainLogic | ⊥ 吸收律(⊥∧T=⊥)与 StateValidity ⊥ 投影"按通过"字面矛盾, 仅 rules 侧注释豁免 | 公理内显式声明"聚合例外(StateValidity投影)": ⊥ 维度按 T 投影, 仅供 StateValidity 聚合, 不改变 Scott 域通用真值表; meta 增 exception 标注 |
| C3 | axioms.md §一 InitialStateNullity/IntentClassification | 枚举域 2元素 vs 3元素 未声明对象域交叠 | 声明按时刻互斥: InitialStateNullity 仅约束初始快照(2元素); IntentClassification 为运行时全域(3元素, 含 AMBIGUOUS) |
| C4 | axioms.md TriPerspectiveStructure | s 同时含 C/D 分量与 Discrete-Enumeration "域互斥"字面冲撞 | 注明 C/D 分量经 Coupling-Embedding(embed/sample) 拓扑连接; "互斥"指枚举限定, "并存"指状态构成视角, 两断言对象不同 |
| C5 | axioms.md §三 AttentionConflictResolution | 引用裁决序遗漏最高优先级"参数来源" | 补全完整三级: 参数来源优先级 > 规则特异性 > 时间顺序(与 rules.md §1.3 一致) |
| C6 | axioms.md §五 InitialState | `∈` 集合缺 iterationCount/subRoundAttempts, 与 engine §5.1 R.init 字段封闭性不一 | 改"必含字段"语义并补全: iterationCount=0 与 subRoundAttempts=0; subRoundAttempts 取值权威仍指 SubRoundInitialState |

### 设计澄清（消歧）

- **修复方向统一**: 全部 6 项均为 axioms.md 向 engine/rules 既有正确语义收敛(active 三合取/R.init 字段封闭性/三级裁决序), 反向无改动——engine/rules 为既有权威, 未引入新语义。
- **⊥ 投影例外边界**: C2 的聚合例外仅适用于 StateValidity 三方聚合(Internal∧External∧Historical), 不泛化至 Scott 域其他 ⊥ 运算(ExploratoryEntry/DecisionTrigger 语义不受影响)。
- **字段封闭性 vs 取值权威**: C6 仅声明初始字段存在与初值, subRoundAttempts 的强制取值(=0)权威仍在 SubRoundInitialState(isFresh 限定), 不越权。

### 验证

- [x] C1: 完整三合取 `active↔w>ε∧layer∧scope` 无旧单分量式残留
- [x] C2: 聚合例外条款 + meta exception 标注落地
- [x] C3: 两公理对象域按时刻互斥注释双向落地
- [x] C4: C/D 分量 Coupling-Embedding 连接消歧落地
- [x] C5: 参数来源优先级计入三级裁决序
- [x] C6: 必含字段含 iterationCount/subRoundAttempts
- [x] engine.md/rules.md 无反向需改动(C1 引用"与 engine.md §1.1 一致")
