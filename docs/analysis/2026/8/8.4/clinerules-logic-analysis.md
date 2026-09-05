# clinerules 逻辑分析报告（重复 / 冗余 / 错误）

> 分析对象：`clinerules/` 下 6 个文件（axioms.md / dsl.md / formula.md / project.md / references.md / theorems.md）
> 分析方法：全文件交叉对照 + 运行引用校验脚本 `PowerShellScript/DSL/Check-ClinerulesReferences.ps1` + 复现脚本扫描逻辑定位根因
> 分析日期：2026-08-03 | 校验脚本快照：2026-08-03 12:38（198 符号 / 0 悬空 / 64 旧形态待迁移）
> **修复状态（2026-08-03）**：报告发布后已按建议执行修复——E1/E3（脚本）、E2/E6（axioms+theorems）、E4/E5/D1/E7/E8/R5（formula）、dsl §4.5（载体映射）均已落地并重编译；见文末「修复执行记录」。

---

## 结论速览

| 类别 | 数量 | 关键条目 |
|------|------|----------|
| 重复逻辑（D） | 6 | Profile delta 冗余、CallKey 双定义、计数器规则三处重复、SAFE 禁闭双声明、符号速查表重叠、注意力公式双写 |
| 冗余逻辑（R） | 4 | dsl §0/§5 重复、axioms 导航清单多重、preEvaluateCF 多处解释、反模式索引割裂 |
| 错误/不一致（E） | 8 | 索引编译缺陷（根因）、`Def.R.goals` 悬空、STYP 映射与去重冲突、OnError 分支不可达、error 动作两处冲突、`em:` 未定义缩写、auth 声明措辞、CountTable 双符号 |
| 其中可直接修复的确定性错误 | 4 | E1 / E2 / E4 / E6 |

---

## 一、重复逻辑（同一语义 / 常量 / 规则多于一处权威）

### D1. Profile delta 与默认值重复（违反自身 Delta 原则）
- **位置**：formula.md §3.2（L118-120） vs axioms.md §7（L285-290）、formula.md §3.1（L112）
- **内容**：§3.2 声明"Delta 原则: 仅声明与默认值差异"，但以下 delta 项与默认值**完全相同**：
  - `DEVELOP: delta:{MAX_ITER=10}` ≡ axioms `MAX_ITER=int:10`
  - `CAMPAIGN: delta:{ROUNDS=2}` ≡ formula `ROUNDS=int:2`
  - `CAMPAIGN: delta:{SWITCH_THRESHOLD=2}` ≡ axioms `SWITCH_THRESHOLD=int:2`
- **影响**：delta 表失去"差异快照"语义；若默认值变更而 delta 未同步，易产生"覆盖值与默认同值"的视觉误导。

### D2. `CallKey=(ToolName,ParamHash)` 两处定义
- **位置**：axioms.md §1 L56 `[Def] ... CallKey=(ToolName,ParamHash)`；formula.md §3.3 L142 `$ ToolName | ParamHash=hash(params) | CallKey=(ToolName,ParamHash)`
- **说明**：axioms 为 `[Def]` 权威，formula 又用 `$` 符号速记重写（含新增 `ParamHash=hash(params)` 细节）。虽 formula 自注"权威见 axioms §8"，但 `$` 形态在 references.md 中会被编译为独立符号 `Symbol.ToolName`，形成双符号（见 E8 同类问题）。
- **建议**：formula 侧仅保留 `[CR]` 引用，删除 `$` 重定义。

### D3. CF / CP / QDC 计数器维护规则三处重复
- **位置**：
  - axioms.md §七 TerminationPriority.qdc_increment（L244）
  - formula.md §4.5 计数器维护权威表（L220-231）
  - formula.md §4.6 EvaluateDrivenUpdate / DecideDrivenUpdate（L236-247）+ §5.2 OnErrorCounterRules（L284-287）
- **说明**：同一"递增/重置/衰减"语义在公理、速查表、状态机三处各自描述。§4.5 自称"本节为速查索引"但仍全文复述，未真正引用收拢。
- **建议**：§4.5 改为纯 `[CR]` 映射表（计数器 → 唯一权威定义点），删除公式复述。

### D4. MODE=SAFE 禁 switch_strategy 双处声明
- **位置**：axioms.md §六 DecisionOutputSet（L233）；formula.md §3.2 `[Gate] SAFE_SwitchLockdown`（L123）
- **说明**：formula 已自注"推论自 [CR: axioms.md:DecisionOutputSet]，非新增语义"。属可接受的推论声明，但形式上重复。
- **建议**：保留权威声明于 axioms；formula 侧可精简为引用注释，或保留 Gate（用于引擎实现挂载）但明确标注"纯实现载体"。

### D5. 符号速查表重叠
- **位置**：axioms.md §8 符号速查（L314-329） vs dsl.md §3 常用缩写（L107-115）
- **说明**：两表对 CF/CP/QDC/TS/TM/TO/ATTENTION_EPSILON 等大量重叠。dsl 自注"非权威记忆表"，分层意图合理，但重复度高，存在"以 dsl 为准"的误读风险。

### D6. 注意力权重公式双写
- **位置**：axioms.md §三 AttentionWeighting（L204-210，唯一权威）；formula.md §2.1（L69-73）`$ ctx_factor` / `$ feedback_factor` / `$ active 实例化`
- **说明**：formula 自注"参数化实例，不新增公理语义"。属合理实例化，但 `$` 形态与 D2 同理易被编译为独立 `Symbol` 符号，建议按 `[CR]` + `S` 型子公式定位引用。

---

## 二、冗余逻辑（可精简、无附加语义的表述/章节）

### R1.（已修正）references.md 章节性质
- 经核查脚本 `Generate-ReferencesDocument`（Check-ClinerulesReferences.ps1 L341-471），references.md **确为整份编译产物**——§0/§1.1/§2/§3/§4 均由模板一括生成，"禁止手工编辑"声明与实现一致。**初版报告中的 R1 判断有误，予以撤销。**

### R2. dsl.md §0 约定与 §5 文法注释重复
- **位置**：dsl.md §0（L8-14）与 §5（L245-256）
- **内容**：①"`|` 默认合取" 与 §5 行内式说明；③ 元数据块 与 §5 多行式说明；④ 数值判断 与正则锚——同一解析语义在速查与实现参考两处陈述。

### R3. axioms.md 导航信息多重陈列
- **位置**：文件顶部区块导航（L5-16）、§0 各层权威清单（L30-34）、物理区块映射（L36-37）、§8 公理索引（L300-312）
- **说明**：同一"层级 → 公理 → 区块"映射被陈述 4~5 次，且部分内容本身又标注"此处不再重复陈列"（L37），说明作者已意识到重复。
- **建议**：保留一处权威清单（拓扑序），导航块与 §8 索引改为 `[CR]` 指向或折叠。

### R4. `preEvaluateCF` 语义多处解释
- **位置**：axioms.md §七 L247、§8 符号速查 L327；formula.md §4.6 L239/L245
- **说明**：同一"R.local@Evaluate 前快照"定义反复出现。公理处为权威，其余处应仅引用。

### R5. 反模式索引割裂为两套
- **位置**：formula.md §2.3 三个 `@ ANTI-LAYER-LEAK / ANTI-SYNTAX-OVERRIDE / ANTI-SCOPE-ESCAPE`（L79-81，`@` 形态） vs §8.1 / §8.2 反模式索引（L506-545，`@` + 表格形态）
- **说明**：§8 自称"通用反模式为规则全文的违规检测与修复索引"，但 §2.3 三个反模式未并入 §8 索引，形成两套维护点。

---

## 三、错误 / 不一致逻辑（按严重度排序）

### E1.【高·根因】索引编译脚本冒号过滤缺陷 → 权威符号系统性缺失
- **位置**：`PowerShellScript/DSL/Check-ClinerulesReferences.ps1` L200
  ```powershell
  if ($prefix -in $PRIVATE_PREFIXES -and $prefix -notin $NO_COLON_PREFIXES -and $body -notmatch ':\s|：') { continue }
  ```
- **缺陷**：过滤条件 `':\s|：'` 要求定义头冒号后**必须紧跟空白字符**。axioms.md 中多行式公理定义为"冒号后直接换行"，全部被跳过，未进入索引：
  | 被跳过的符号 | 位置 |
  |---|---|
  | `[BAX-IDI] IDI-Identity:` | axioms.md L82 |
  | `[AX-CORE] TruthValueDomainLogic:` | L126 |
  | `[AX-CORE] QuantificationConstraint:` | L133 |
  | `[AX-META] MetaEvolution(InferenceRule):` | L139 |
  | `[AX-META] ToolCallWellFormedness:` | L152 |
  | `[AX-META] OutputWellFormedness:` | L159 |
  | `[AX-META] FormatFidelity:` | L166 |
  | `[AX-FRAME] StratifiedReflectionSchema:` | L174 |
- **验证**：复现脚本扫描逻辑，上述 11 行（含 3 个 `[Type]`/`[Def]`/注释干扰项）确实被过滤；`references.md §1.2` 索引中不存在这些符号。
- **为什么未检出悬空引用**：formula.md / theorems.md 对上述符号的引用全部为**旧形态**（`[CR: axioms.md:ToolCallWellFormedness]` / `[CR axioms.md:...]`），被 `-AllowLegacy` 归类为"待迁移 64 条"而不做严格校验 → 悬空引用 0。
- **影响**：索引权威性受损；当引用方改为严格形态 `[CR: axioms:AX-META.ToolCallWellFormedness]` 后，将立即全部判定为悬空引用。
- **修复建议**：将过滤条件放宽为"冒号后空白 **或 行尾**"（如 `':(\s|$)'`），并同步修正 `$PREFIX_ORDER` / `$PRIVATE_PREFIXES` 判定；或在 `[Prefix] Name:` 行尾冒号也视作定义头（与 dsl.md §1 多行式模板一致）。

### E2.【高】theorems.md TH-2 引用不存在的 `Def.R.goals`
- **位置**：theorems.md TH-2 字段表（L37）：`R.goals` 见 `[CR: axioms.md:Def R.goals]`
- **事实**：axioms.md 中 `R.goals` 仅内嵌于 `[Def] isFresh(R) ↔ R.preRound=null | decision(R)∈DecisionOutputSet.output | R.goals = 由R.history累积的目标集`（L64）行内，**无独立 `[Def] R.goals` 定义点**；索引及符号速查亦无该条目。
- **影响**：悬空引用（旧形态 + legacy 机制下未被检出）；TH-2 公式语义依赖一个不存在的权威符号。
- **修复建议**：在 axioms.md §1 独立成 `[Def] R.goals = 由 R.history 累积的目标集`，或 TH-2 改为引用 `[CR: axioms.md:Def.isFresh]`（子谓词定位），并注明 `R.goals` 为其行内属性。

### E3.【高】STYP 映射与去重规则冲突（dsl.md 与脚本不一致）
- **位置**：Check-ClinerulesReferences.ps1 L102-104 `$PREFIX_STYP_MAP`；dsl.md §4.5（L183-205）；formula.md §6.1（L338/340）
- **问题 1**：脚本将 `[QA]→SM`、`[CD]/[CM]/[CC]/[CS]/[TD]→Gate`，而 dsl.md §4.5 STYP 枚举表中**未列出** CD/CM/QA/CC/CS/TD 类型，仅注释"目前上述语义类型经 [SM]/[Gate]/[CD] 载体引用"——文档声明的映射（经 `[CD]` 载体）与脚本实际映射（`[CD]→Gate`）不一致。
- **问题 2**：`[CM] OnError.shared-branch(error):`（formula L338）与 `[CD] OnError(error)`（L340）因脚本形态 1 正则 `^\[[A-Za-z-]+\]\s*([A-Za-z_][A-Za-z0-9_-]*)` 不含点号，两者都解析为 `Name='OnError'`、`Styp='Gate'` → 去重合并为单一 `Gate.OnError`（refereences.md L168），`shared-branch` 子谓词**丢失**，且 `OnError_SingleCall` 别名无独立条目。
- **影响**：引用 `[CR: formula:Gate.OnError.shared-branch@inst]` 将无法解析（点链解析只取首段 `OnError`，回退到合并后的 `Gate.OnError`），语义指向错误。
- **修复建议**：① dsl.md §4.5 增补 CD/CM/QA/CC/CS/TD 的 STYP 归属说明，或在脚本注释处同步说明映射依据；② 脚本正则支持点号（`[A-Za-z_][A-Za-z0-9_.-]*`）并将 `OnError.shared-branch` 编译为独立符号（或纳入 `OnError` 的子谓词族）。

### E4.【高】OnErrorDecisionTree 分支短路使 `compilation_error→continue` 不可达
- **位置**：formula.md §5.2 `[Gate] OnErrorDecisionTree`（L275-282）；对照 §6.1 `OnError.shared-branch`（L338）
- **分支序**：④ `CountTable(T,params).total=0 → retry(瞬态)` → ⑤ `total≥1∧empties≥1 → switch_strategy` → ⑥ `total≥1∧empties=0 → retry` → … → ⑨ `error=compilation_error → continue`
- **冲突**：compilation_error 调用后（按 §5.2 OnErrorCounterRules，`CountTable.total+=1` 每次执行）：
  - 若 total=0（首次/未计入）→ ④ 先捕获 → **retry**，非 continue；
  - 若 total≥1 且 empties=0（稳态重复编译错误）→ ⑥ 先捕获 → **retry**，非 continue；
  - ⑨ 在短路序中**不可达**（当 CountTable 有任何记录时），与 §6.1 shared-branch 声明的 "compilation_error→continue（仅分类）"语义矛盾。
- **修复建议**：将 `error=compilation_error → continue` 上移至 ④ 之前（紧随 forceHalt/熔断分支之后），或将 CountTable 分支限定为 `result=∅` 语境（`retry/switch_strategy` 仅针对空结果，编译错误不应经 CountTable 计数分流）。

### E5.【中】execution_failure / timeout 动作两处冲突（缺少 scope 分派显式约束）
- **位置**：formula.md §5.2 L281（循环版）`error∈{timeout,execution_failure}∧stepIndex<len → halt+persist`；§6.1 L341-343（单次版）`execution_failure,timeout → report+retry_optional`
- **说明**：两处对同一错误类型的动作不同（halt+persist vs report+retry_optional）。虽注释"循环版/单次版分工"，但缺少一条显式的"错误分类语义唯一、动作随 scope 分派"桥接规则；§5.2 声明"共享分支分类语义见 §6.1"，但 `timeout/execution_failure` 并未真正进入 shared-branch，实为两处独立决策。
- **修复建议**：在 §6.4 域分离声明处增补 `[SEP] LoopVsSingleScope`（Dom(循环动作)×Dom(单次动作)=∅），并让 §5.2 与 §6.1 互相以 `[CR]` 引用，明确"分类唯一、动作属 scope"。

### E6.【中】axioms.md 未定义缩写 `em:`（疑似笔误）
- **位置**：axioms.md L248 两处：`decay_scope(R,switch_strategy)=R.local(...; em: 决策驱动更新实例化)`、`R.cf@τ_decision=...(...; 场景说明, 非独立公式)`
- **事实**：dsl.md §2 元数据键表（L71-101）无 `em` 键；`dr:`/`sem:`/`//!` 均已有标准标注。
- **修复建议**：`em:` 应为 `例如:`（或并入 `dr:`），按 dsl.md 约定改用 `dr:` 或 `//!` 注释。

### E7.【低·澄清】auth 授权声明的措辞张力
- **位置**：axioms.md L22（"纯 admin 参数不可被 Profile 覆盖"） vs formula.md §3.2 L127（"当前无纯 admin 参数"）
- **说明**：二者在**当前状态**下一致（所有参数 `auth` 均含 `config`），不构成实际冲突。但"当前无纯 admin"是动态事实、易随配置演化失步；建议改为静态声明"Profile 仅可覆盖 auth 含 config 的参数"即可，删除"当前无纯 admin"句。

### E8.【低】`StateVar.CountTable` 与 `Symbol.CountTable` 双符号
- **位置**：formula.md §3.3 L139 `[StateVar] CountTable` 与 L143 `$ CountTable(T,params)=...`；references.md 索引 `StateVar.CountTable`（L222）与 `Symbol.CountTable`（L233）并存
- **说明**：违反 references.md §0"单源事实：每个符号至多一个 def 权威点"。`$` 行是对 `[StateVar]` 的访问语义，不应编译为独立 `Symbol`。
- **修复建议**：`$` 行改为子谓词/映射注释，或脚本对 `Symbol` 类型与 `StateVar` 同名时去重。

---

## 四、修复建议汇总

| 编号 | 类别 | 建议动作 | 涉及文件 |
|------|------|----------|----------|
| E1 | 错误 | 修改 `Check-ClinerulesReferences.ps1` L200 冒号过滤为 `':(\s|$)'`，重跑 `-UpdateIndex` | 脚本 + references.md |
| E2 | 错误 | axioms 独立 `[Def] R.goals` 或 TH-2 改引用 `Def.isFresh` | axioms.md + theorems.md |
| E3 | 错误 | dsl §4.5 补 CD/CM/QA/CC/CS/TD；脚本正则支持点号并去重修正 | dsl.md + 脚本 + references.md |
| E4 | 错误 | OnErrorDecisionTree 分支重排，compilation_error 前置 | formula.md |
| E5 | 错误 | 增补 [SEP] LoopVsSingleScope + 互引 | formula.md |
| E6 | 错误 | `em:` → `dr:`/`例如` | axioms.md |
| D1 | 重复 | 删除与默认值相同的 delta 项 | formula.md |
| D2 | 重复 | formula 侧删除 `$` CallKey 重定义 | formula.md |
| D3 | 重复 | §4.5 改为纯 [CR] 映射表 | formula.md |
| D7* | 冗余 | 导航清单收敛为单一权威 + 引用 | axioms.md |
| R2 | 冗余 | §0 与 §5 折叠或互引 | dsl.md |
| R4 | 冗余 | preEvaluateCF 仅权威处保留 | axioms.md + formula.md |
| R5 | 冗余 | §2.3 反模式并入 §8 索引 | formula.md |
| E7/E8 | 低 | 措辞收敛 / 符号去重 | axioms.md + formula.md + 脚本 |

> D7*：对应 R3（导航信息多重陈列）。

---

## 附：修复执行记录（2026-08-03）

按报告建议完成以下修复，并重跑校验器：

| 编号 | 修复内容 | 涉及文件 | 验证结果 |
|------|----------|----------|----------|
| E1 | 冒号过滤放宽为 `':(\s|$)'`，多行式定义头纳入索引 | 脚本 L200 | 索引符号 198→**210**（+12），`AX-META.*`/`AX-FRAME.StratifiedReflectionSchema`/`AX-CORE.TruthValueDomainLogic`/`BAX-IDI.IDI-Identity` 补齐 |
| E3 | 名称正则支持点号 → `Gate.OnError.shared-branch`、`ToolStrategy.Toolchain/ToolPriority/ToolSelect` 独立成符号 | 脚本 L187 + references.md | L178/L191-193 独立条目 |
| E2 | axioms 独立 `[Def] R.goals`；theorems TH-2 引用同步 | axioms.md L64 + theorems.md L37 | 索引 L113 `Def.R.goals` |
| E4 | `compilation_error→continue` 前移至 CountTable 分支之前（消除短路遮蔽） | formula.md §5.2 | 分支可达性恢复 |
| E5 | 增补 `[SEP] LoopVsSingleScope`（循环版/单次版动作域互斥） | formula.md §6.4 | 索引 L219 `SEP.LoopVsSingleScope` |
| E6 | `em:`（2 处）→ `例:` | axioms.md §七 | 消除未定义缩写 |
| D1 | DEVELOP 删 MAX_ITER、CAMPAIGN 删 ROUNDS/SWITCH_THRESHOLD（与默认值相同） | formula.md §3.2 | Delta 原则恢复 |
| E7 | "当前无纯 admin 参数"措辞改为静态约束声明 | formula.md §3.2 | 消除动态事实漂移风险 |
| E8 | `$ ToolName/ParamHash/CallKey` 与 `$ CountTable(...)` 改为注释（非 `$` 符号行）→ `Symbol.CountTable`/`Symbol.ToolName` 消除 | formula.md §3.3 | 索引仅剩 `StateVar.CountTable` |
| R5 | §2.3 三个 `@ ANTI-*` 并入 §8.2 统一索引 | formula.md §8.2 | 三行新条目 |
| dsl §4.5 | 增补载体编译映射说明（`[QA]→SM`、`[CD]/[CM]/[CC]/[CS]/[TD]→Gate`） | dsl.md §4.5 | 文档与脚本一致 |

**第二批（残余重复/冗余收敛）**：

| 编号 | 修复内容 | 涉及文件 | 验证结果 |
|------|----------|----------|----------|
| D3 | §4.5 计数器维护表由"公式复述"收敛为纯映射索引（唯一公式来源回到各定义点） | formula.md §4.5 | 表体精简，权威重归单一 |
| D6 | §2.1 `$ ctx_factor`/`$ feedback_factor`/`$ 参数化实例`/`$ active 实例化` 改为注释（消除冗余 `Symbol.ctx_factor`/`Symbol.feedback_factor` 双写） | formula.md §2.1 | 索引 210→**208**（-2），Symbol 区段仅剩 `Symbol.LoopModel` |
| 新增 | dsl.md §4.8 校验脚本路径修正（`PowerShellScript/` → `PowerShellScript/DSL/`） | dsl.md §4.8 | 路径与实际脚本一致 |

**第三批（剩余重复/冗余，D4/D5/R2/R3/R4 全部实施）**：

| 编号 | 修复内容 | 涉及文件 | 验证结果 |
|------|----------|----------|----------|
| D4 | formula §3.2 `SAFE_SwitchLockdown` 追加"纯实现载体"标注（不新增语义，权威见 axioms） | formula.md §3.2 | 无符号变化 |
| D5 | dsl §3 旧"非权威记忆表"替换为严格权威指引（`[CR: axioms.md:Sec.8@see]`） | dsl.md §3 | 权威指向显式化 |
| R2 | dsl §0 追加职责边界注释（书写速览 vs §5 解析器参考），④条目指向 §5 | dsl.md §0 | 重复定位显式化 |
| R4 | formula §4.6 `preEvaluateCF` 改为"权威定义见 axioms §七, 此处仅引用"（DecideDrivenUpdate 行另自注"仅引用不重复"） | formula.md §4.6 | 措辞收敛 |
| R3 | axioms 顶部删除冗余"物理区块映射"2 行（内容已被"区块导航"与 §8 公理索引覆盖） | axioms.md | 导航收敛 |

**终态校验**：索引 208 符号 / 悬空引用 0 / 废弃指向 0 / 循环依赖 0 / 旧形态待迁移 66（+1 = dsl §3 新增 `Sec.8@see` 严格引用，属预期计数）。

> **剩余未改动项已清零**：原 5 项遗留（D4/D5/R2/R3/R4）本批全部实施。至此分析报告中所有条目均已处理完毕（E 类修复、D 类收敛、R 类精简/inlining，R1 撤销、E9 确认为编译产物）。

---

## 附：验证记录

1. **校验脚本运行**（`Check-ClinerulesReferences.ps1`，2026-08-03 12:44）：索引 198 符号、悬空 0、废弃指向 0、循环依赖 0、旧形态待迁移 64。
2. **扫描逻辑复现**：对 axioms.md 应用脚本 L199-200 过滤规则，确认 `[AX-META]` 等 8 个公理定义头被跳过（前述 E1 表）。
3. **分支可达性分析**：按 §5.2 OnErrorCounterRules 的 CountTable 更新语义，静态推演 compilation_error 的短路走向（E4）。
4. **符号定义点核查**：线性搜索 axioms.md 确认无 `Def.R.goals` 独立定义（E2）；references.md 索引确认 `StateVar.CountTable`/`Symbol.CountTable` 并存（E8）。