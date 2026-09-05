# clinerules 进一步重构计划（公式公理化 + 注释逻辑化）

> 日期: 2026-08-03 | 前置: 已读取全部 6 文件 + analysis/optimization_log 两文档
> 目标: ①可判定断言全部公式化 ②注释承载的真逻辑提升为公式 ③消歧/设计理由保留但标记 dr:/sem:
> 方法论: 逻辑表述三级提升——可判定断言→谓词合取/cond→action/[SM]/[Gate]；真值域化→衔接 TruthValueDomainLogic；消歧→dr:/sem://!

## 批次一: axioms.md（逻辑密度最高, 优先）— ✅ 已完成

- [x] 新增 [Def]: Placeholder/UnfoundedAssertion/FictitiousPath/scalarizable/typeResolution/εγδ_c 符号约定
- [x] ToolCallWellFormedness 五要素 → 谓词合取(TagAlign∧ValReal∧NameMatch∧SingleTool∧TemplateConform)
- [x] OutputWellFormedness 六要素 → 谓词合取(StructClosed∧ContentReal∧NameExact∧CarrierAlign∧TemplateConform_Out∧SelfCheckAlign)
- [x] FormatFidelity CausalMerge → 绑定 TH-7 公式(Fidelity→extract_deterministic), 统计语义指向 Coupling-Embedding
- [x] QuantificationConstraint priority 行 → typeResolution 公式(explicit>contextual>fallback)
- [x] TerminationPriority → qdc_increment 符号化(ΔQDC) + commensurability 引用 scalarizable + retry桥接标记 dr
- [x] Coupling-Embedding 符号约定 → dr + [Def] 引用(消除行内重复)

## 批次二: engine.md — ✅ 已完成

- [x] subRoundAttempts 重置互斥 //! → 公式(reset_pass∧reset_exhaust→互斥, 耗尽优先)
- [x] MetaGate 注释 → enters(MetaRound) 公式
- [x] FocusFeedback //! 消歧 → dr

## 批次三: rules.md — ✅ 已完成

- [x] §2.2 参数缺失处理 `$` → [CD] ParamFallback 公式
- [x] §三 StateValidity 真值映射/⊥投影注释 → 正式 | 公式行
- [x] §2.3 L3 参数修改权限 `$` → [CS] L3ModifyAuth 公式

## 批次四: dsl.md / theorems.md / project.md — ✅ 已完成

- [x] dsl.md §0 新增约定⑤: 可判定断言必须公式化; 消歧用 dr:/sem:
- [x] theorems.md(仅 TH-6 pf 五要素对齐新谓词名) | project.md(纯配置, 不动)

## 验证 — ✅ 已完成

- [x] 全量 [CR]/[Def] 引用一致性检查(新谓词均被消费: Placeholder→ValReal/ContentReal, UnfoundedAssertion/FictitiousPath→ContentReal, scalarizable→commensurability, typeResolution→QuantificationConstraint)
- [x] 五要素/六要素中文描述同步谓词名(StructuralGrounding src绑定/FormatCheck/TH-6 pf)
- [x] 注释→公式 信息无丢失核对
