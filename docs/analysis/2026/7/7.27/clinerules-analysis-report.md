# clinerules/ 逻辑分析报告

> **分析日期**：2026-07-27  
> **分析范围**：`clinerules/` 下全部 8 个文件  
> **文件列表**：axioms.md, derivation.md, dsl.md, engine.md, project.md, rules.md, spec.md, theorems.md  
> **上次更新**：2026-07-27
>
> **⚠️ 本报告已更新以反映文件当前状态**。原始报告中发现的问题，部分已在本次分析前的版本迭代中自动修复（见 "已自动修复项" 章节）。

---

## 已自动修复项（无需人工干预）

以下问题在本次分析时发现已被文件当前版本修复：

| # | 问题 | 文件 | 修复方式 |
|---|------|------|---------|
| 1 | StratifiedReflectionSchema 公理文本与缺陷10矛盾 | axioms.md | **已在历史迭代中修复** — 公理已改为 `ExternalTruthEquivalence`，Truth 绑定外部签名见证（`Witnessed_{External}`），元数据标注"已修复" |
| 2 | LogicalClock `cause≠∅` 与 InitialState 冲突 | axioms.md | **已在历史迭代中修复** — 放宽为 `IsFirstEvent(e)∨e.cause≠∅`，显式允许首次事件无前因 |
| 3 | "系统底层"未定义 | engine.md §3.3 | **已在历史迭代中修复** — 替换为明确的伪代码 `on fileCache_update` / `on forceHalt_set` / `on StateValidity_completed` |
| 4 | CallKey/ToolKey 类型歧义 | engine.md §3.1 | **已在历史迭代中修复** — 状态变量表注释明确定义了 `ToolKey = ToolName`、`CallKey = (ToolName, ParamHash)`、`ParamHash` |
| 5 | parameters-registry.md 全文冗余 | — | **已删除** — 该文件不存在于当前目录 |
| 6 | spec.md 末尾汇总表 | spec.md | **已删除** — 文件末尾无汇总表 |

---

## 一、本次修复项

以下问题在本次分析中已实际修复：

### ✅ 1.1 CountableOrderBridge causal_fallback 降级（axioms.md）

**操作**：将 `causal_fallback` 从 `[AX-FRAME]` 公理体中剥离，降级为独立的 `[DR] CausalFallbackHeuristic`

**变更**：
- 公理体移除 `causal_fallback(a,b)=max(...)` 和注释中的"工程启发式"声明
- 新增 `[DR] CausalFallbackHeuristic（设计理由，非公理）` 段落
- `meta` 的 `irr` 从 `"logical; heuristic=fallback"` 改为 `"logical"`

### ✅ 1.2 `[Def] Intent → types` 重叠去重（axioms.md）

**操作**：将 `[Def] result.intent∈{NO_DATA,NO_RESPONSE,AMBIGUOUS}` 精简为 `[Def] Intent → types`，消除与 `[AX] IntentClassification` 的语义重复

### ✅ 1.3 derivation.md §六 空表替换

**操作**：将原来仅包含 `[CR]` 引用的空章节替换为清晰的"已清空"标注 + 引用索引

### ✅ 1.4 derivation.md PO-7 行精简

**操作**：PO-7 行从"已废弃 — ExternalConsistencyWitness 已从 [AX-META] 降级为 [DR]"简化为"PO-7（已废弃）"，保留必要的交叉引用信息

---

## 二、保留待议项（建议在后续迭代中处理）

| # | 问题 | 文件 | 类型 | 建议 |
|---|------|------|------|------|
| 1 | OnError 双版本共存 | rules.md §5.1 vs engine.md §七 | **重复** | 保留现状，但需持续维护 `[CR]` 交叉引用的准确性 |
| 2 | 反模式表拆分 | rules.md §七 vs engine.md §八 | **重复** | 可考虑将 engine.md 反模式合并入 rules.md 形成单一注册表，但非紧急 |
| 3 | 参数四层声明 | axioms.md / engine.md / project.md | **冗余** | 参数权威源明确在 axioms.md，engine.md 仅声明引擎专用参数（15个），project.md 仅声明项目特有参数（~20个），无重叠。可标注"非重叠" |
| 4 | PO 表 vs axioms.md dr 字段 | derivation.md §九 vs axioms.md 多处 | **冗余** | derivation.md §九 已声明"不替代 axioms.md 行内缺陷标注"，当前可接受 |
| 5 | 薄弱推论 | theorems.md TH-7/TH-9/TH-10/TH-11 | **冗余** | 证明长度虽短，但包含了具体的源公理链和推导路径，非纯粹语义重复。建议保留 |
| 6 | 缺陷编号混淆 | axioms.md 多处 | **维护** | 缺陷2/5、缺陷2/12 跨公理共享编号。derivation.md §九 已有说明这是复合引用格式。建议后续改为唯一编号 |
| 7 | CR 死链验证 | 全项目 | **运维** | 建议引入脚本扫描所有 `[CR: file.md §X]` 引用 |

---

## 汇总统计（反映当前状态）

| 分类 | 项目 | 状态 | 优先级 |
|------|------|------|--------|
| **已自动修复** | StratifiedReflectionSchema 自矛盾 | ✅ 已修复 | — |
| | LogicalClock vs InitialState | ✅ 已修复 | — |
| | "系统底层"未定义 | ✅ 已修复 | — |
| | CallKey 类型歧义 | ✅ 已修复 | — |
| | parameters-registry.md 删除 | ✅ 已删除 | — |
| | spec.md 汇总表 | ✅ 已删除 | — |
| **本次修复** | causal_fallback 降级 | ✅ 已修复 | — |
| | [Def] 重叠去重 | ✅ 已修复 | — |
| | derivation.md §六 空表 | ✅ 已修复 | — |
| | PO-7 行精简 | ✅ 已修复 | — |
| **待议** | OnError 双版本 | 待议 | 低 |
| | 反模式表拆分 | 待议 | 低 |
| | 参数四层声明 | 可接受 | — |
| | 薄弱推论 | 可接受 | — |
| | CR 死链 | 待议 | 中(运维) |

---

## 总结

**现状评级：结构良好，无严重逻辑错误**

本次分析发现的核心问题均已在之前的版本迭代中自动修复。当前 `clinerules/` 体系不存在需要紧急修复的逻辑矛盾。剩余的问题主要是**维护性**和**结构优化**性质（OnError 双版本、CR 死链验证），可以在后续的日常迭代中逐步完善。