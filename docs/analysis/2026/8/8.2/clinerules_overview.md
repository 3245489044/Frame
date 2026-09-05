# clinerules 体系全览：形式化公理驱动的分层系统设计

> 本文是对 `clinerules/` 目录下 6 个源文件的系统性概述。每个章节对应一个源文件，附核心职责说明和关键入口链接。

---

## 目录

- [clinerules 体系全览：形式化公理驱动的分层系统设计](#clinerules-体系全览形式化公理驱动的分层系统设计)
  - [目录](#目录)
  - [一、公理基底 — `axioms.md`](#一公理基底--axiomsmd)
    - [核心概念](#核心概念)
    - [快速入口](#快速入口)
  - [二、DSL 语法规范 — `dsl.md`](#二dsl-语法规范--dslmd)
    - [核心规则](#核心规则)
    - [快速入口](#快速入口-1)
  - [三、派生推论 — `theorems.md`](#三派生推论--theoremsmd)
    - [核心推论](#核心推论)
    - [快速入口](#快速入口-2)
  - [四、领域规则与策略 — `rules.md`](#四领域规则与策略--rulesmd)
    - [核心内容](#核心内容)
    - [快速入口](#快速入口-3)
  - [五、引擎状态机 — `engine.md`](#五引擎状态机--enginemd)
    - [核心内容](#核心内容-1)
    - [快速入口](#快速入口-4)
  - [六、项目配置 — `project.md`](#六项目配置--projectmd)
    - [核心配置](#核心配置)
    - [快速入口](#快速入口-5)
  - [体系总结](#体系总结)

---

## 一、公理基底 — `axioms.md`

> **定位**：整个体系的逻辑起点。声明"什么是逻辑上必然的"，定义 L0~L3 分层结构、三棱镜视角基底、同步协议、因果偏序、连续-离散拓扑嵌入等不可变公理。

### 核心概念

| 概念 | 公理 | 一句话说明 |
|------|------|-----------|
| **分层架构** | `[AX-CORE] AxiomHierarchyStructure` | L0 绝对不可变 → L0.5 逻辑骨架不可变 → L1 签名共识 → L2 可版本化 → L3 运行时可变 |
| **三棱镜视角** | `[BAX] TriPerspectiveStructure` | 系统由 IDI（观察者框架）× C（连续域）× D（离散域）张量积分解 |
| **同步协议** | `[AX-CORE] SyncProtocolTriple` | Try→Commit/Compensate 三阶段替代原子同步，原子性仅限逻辑时钟域 |
| **因果偏序** | `[BAX-TIME] LogicalClock + CausalCone` | 严格偏序 `→` 替代全局物理时钟 |
| **拓扑嵌入** | `[BAX-X] Coupling-Embedding` | 连续↔离散映射为拓扑连续嵌入（非逻辑函子），统计目标而非严格等式 |
| **真值域** | `[AX-CORE] TruthValueDomainLogic` | Scott 信息域 {⊥,T,F}，Truth 绑定内部推导而非外部签名 |
| **分层反射** | `[AX-CORE] StratifiedReflectionSchema` | Ln 定理在 Lnext 中作为可证明性标签被"谈论"，避免哥德尔不完备 |
| **终止优先级** | `[AX] TerminationPriority` | 优先级链：forceHalt > CF > QDC > CP > IC > SWITCH_TH |

### 快速入口

- [axioms.md](clinerules/axioms.md) — 完整公理声明（配置参数、类型基底、定义）
- `[CR: theorems.md]` — 从公理派生的推论集合
- `[CR: engine.md]` — 公理在引擎运行时的实例化

---

## 二、DSL 语法规范 — `dsl.md`

> **定位**：公理书写语法的形式化规范。核心是"三明治结构"——`---meta` 块承载元数据，公式体只含纯逻辑公式。

### 核心规则

| 维度 | 说明 |
|------|------|
| **三明治结构** | `[TAG] 公理名: 公式体` → `---meta` → `key = "value"` → `---` |
| **行类型前缀** | `[AX]`=公理、`[Type]`=类型、`$`=符号、`%`=配置、`@`=缺陷、`[Level]`=层级、`[StateVar]`=状态变量、`[Profile]`=预设、`[Pattern]`=模式、`[SM]`=状态机、`[Gate]`=决策门控 |
| **元数据键** | `view`/`src`/`irr`/`sem`/`enf`/`dr`/`auth`/`cfg` 等 |
| **跨文件引用** | `[CR: file.md:anchor]` |

### 快速入口

- [dsl.md](clinerules/dsl.md) — 完整语法规范
- `[CR: axioms.md:AxiomHierarchyStructure]` — 层级定义
- `[CR: rules.md §1.2]` — 抽象语义表（公式类型概念语义）

---

## 三、派生推论 — `theorems.md`

> **定位**：从 axioms.md 公理派生的逻辑推论集合（TH-1 ~ TH-6），每条含完整逻辑公式、源公理引用链（src）与证明概要（pf）。

### 核心推论

| 推论 | 内容 |
|------|------|
| **索引（非编号）** | 轮次进入条件（EntryCondition + InitialState + SubRoundInitialState 合取速查） |
| **TH-1** | 跨域同步必要性（needsSync 判定） |
| **TH-2** | 决策目标派生（每个决策可追溯至至少一个目标） |
| **TH-3** | 工具调用确定性（相同快照+相同参数⇒相同结果） |
| **TH-4** | 采样-嵌入统计保底（期望误差有界 + 统计一致性约束） |
| **TH-5** | 决策度量（距离归一化判定） |
| **TH-6** | 工具调用格式有效性→执行准入（parse_failure 路径） |

### 快速入口

- [theorems.md](clinerules/theorems.md) — 完整推论
- `[CR: axioms.md]` — 权威公理源
- `[CR: rules.md §八 FormatSelfCorrection]` — TH-6 的失败自纠正流程

---

## 四、领域规则与策略 — `rules.md`

> **定位**：L2-L3 层。领域公理实例化 + 策略/规范/流程/反模式。

### 核心内容

| 章节 | 内容 |
|------|------|
| §一 | 公式类型体系（[AX]/[CD]/[CM]/[SM]/[QA]/[CC] 等 11 种）+ 规则冲突解决 |
| §二 | 配置注入（ConfigInjection）：参数来源优先级与权限分级 |
| §三 | 认知状态三定律（StateValidity）：InternalConsistency + ExternalConsistency + HistoricalIntegrity |
| §五 | 策略定义（工具/上下文/读取策略） |
| §六 | 规范（FailMode、CodeStandard、CohesionCheck 等） |
| §七 | 反模式索引（22 条） |
| §八 | 操作流程（TotalOrderMerge、SyncGuard、IdempotencyGuard、ErrorHandler、CompactProcedure 等 10 个） |

### 快速入口

- [rules.md](clinerules/rules.md) — 完整规则定义
- `[CR: axioms.md §一-§八]` — 源公理
- `[CR: engine.md §四]` — 引擎状态机（五步模型归属引擎视角）

---

## 五、引擎状态机 — `engine.md`

> **定位**：L1-L2 引擎运行时视角。注意力控制 + 运行时状态机 + 迭代循环执行 + OnError 熔断。

### 核心内容

| 章节 | 内容 |
|------|------|
| §一 | 层级定义与策略（[Level] 声明 + FocusDecision） |
| §二 | 迭代循环配置参数（引擎专用参数 + SAFE/DEVELOP/CAMPAIGN 预设） |
| §三 | 状态变量管理（StateManager + 并行合并 + 初始化钩子） |
| §四 | 五步模型与公理实例化链（Execute→Collect→Evaluate→Decide→Adjust） |
| §五 | 状态更新规范（计数器维护 + Evaluate/Decide 驱动更新） |
| §六 | 循环模式（SimpleRetry/IterativeDebug/BatchPolling/NestedLoop/MultiRoundStrategy） |
| §七 | OnError 策略（循环版 OnError_Loop，含熔断决策树） |
| §八 | 循环执行反模式（8 条） |

### 快速入口

- [engine.md](clinerules/engine.md) — 完整引擎规范
- `[CR: axioms.md:AxiomHierarchyStructure]` — 轴消歧（引擎运算层级轴 vs 领域公理层级轴）
- `[CR: rules.md §5.1]` — OnError 单次调用版

---

## 六、项目配置 — `project.md`

> **定位**：WorkFlowPrj 项目的路径、日志、废弃处理和构建规范。

### 核心配置

| 类别 | 内容 |
|------|------|
| **文件阈值** | THRESHOLD_SMALL=50KB, THRESHOLD_LARGE=1MB |
| **执行环境** | IO/Compute/Hybrid 三类模式的 timeout/rate_limit/retry |
| **路径格式** | direct_api/cli/sandbox 三种环境的路径格式映射 |
| **废弃策略** | deprecatedDir=`_deprecated/`, excludeMethod=compile_exclude |
| **异常映射** | C# 异常类型映射（6 种） |

### 快速入口

- [project.md](clinerules/project.md) — 完整配置
- `[CR: rules.md §5 PathStrategy]` — 通用路径原则
- `[CR: rules.md §8 FileDeprecationFlow]` — 通用废弃流程

---

## 体系总结

| 文件 | 层级 | 职责 | 核心产出 |
|------|------|------|---------|
| `axioms.md` | L0-L3 | 逻辑基底 | 分层结构、三棱镜视角、同步协议、因果偏序、真值域、终止优先级 |
| `dsl.md` | 元层 | 语法规范 | 三明治结构、行类型前缀、元数据键 |
| `theorems.md` | 跨层 | 派生推论 | TH-1~TH-6 逻辑推论 + 证明概要 |
| `rules.md` | L2-L3 | 领域规则 | 公式类型体系、认知状态三定律、策略/规范/流程/反模式 |
| `engine.md` | L1-L2 | 运行时引擎 | 五步模型、计数器更新、OnError 熔断、状态变量管理、循环模式 |
| `project.md` | L3 | 项目配置 | 文件阈值、执行环境、路径格式、异常映射 |