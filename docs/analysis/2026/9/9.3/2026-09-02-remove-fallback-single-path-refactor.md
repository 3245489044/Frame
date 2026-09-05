# 移除回退冗余 — 单一路径彻底重构方案

> **日期**：2026-09-02
> **触发**：GUI 运行呈现「任务池/会话/统一 LLM 网关全部未装配、仅数据库可用」的静默半装配态，且无法从日志直接归因；根因是装配层逐层隐式降级 + 调用链/运行入口多旁路。用户判定：**整体架构回退冗余过多，需彻底重构为一条自洽的单一路径**。
> **定位**：承接 9.3《function-callchain-arch-optimization》、9.1 卡死重构、9.2 写串行化。本方案是**装配层 + 运行入口 + 调用链 + 决策 + 错误 + 写 + 发现**七面的"去回退、单一路径"总纲。
> **状态**：S1（装配）/ S2（运行入口）/ S4-A·S4-B 已实施（逐阶段独立 commit，见 `2026-09-02-remove-fallback-single-path-refactor-实施看板.md`）；S3/S4-C·S4-D/S5/S6 待实施（各自技术路径与依赖见看板「剩余阶段续推进路径」）。本文件保持方案权威，实施进度以看板为单一记录。
> **原则**：机制强制 > 打补丁；**可声明的单一路径 > 可回退的多形态决策树**；fail-fast 带失败链 > fail-silent 吞异常；保留的是"显式容错"，消灭的是"隐式架构回退"。

---

## 〇、目标与判据（先立"单一路径"的定义）

**单一路径 = 满足四条可检验不变量**：
1. **单一声明目标态**：宿主只声明一个运行目标（例如 `CompleteAgent`）。装配/运行不存在"完整形态、降级形态、再降级形态"的决策树。
2. **确定性装配链**：从声明到可用，走一条线性、可列举的装配链；每个模块要么 `Attached`（可携带其显式 disabled 的增强子项），要么链在某一步**抛错中止并带完整失败链**。不存在"某模块悄悄是 null 而整体仍算成功"。
3. **运行时唯一咽喉**：任何输入、任何触发（手动 / 反应式 / 任务池）都汇入**同一个运行入口**，无"直跑旁路"。能力不是横切切换的。
4. **失败必可归因**：没有 `catch { return null; }` / `catch { /* 静默 */ }` 去压平异常链；装配与关键路径失败即抛，且失败原因落在结构化摘要/事件里，UI 能强制反映"Agent 不可用 + 原因"。

> **判据**：重构后代码里不应再出现「能力整组降级」「某能力因可选开关静默缺席仍成功」「双入口旁路」「catch 吞异常转 null 的能力模块」。判定是否保留某条"兜底"的唯一标准：它属于**同一形态内的显式容错（可重试/有界等待）**，而非**跨形态的架构切换（把核心能力降级掉）**。前者保留，后者消灭。

---

## 一、回退 / 多路径 / 静默半装配 现状全景

> 每个"形态"标注：`[回退冗余]` 指应消灭；`[合理·显式化]` 指保留但需显式登记、不得吞异常。

| 面 | 现状形态 | 判定 |
|----|---------|------|
| **装配目标** | `TryBootstrapGuiAsync`：① 完整框架 → catch → ② RulePoolOnly(零 LLM，砍会话+统一网关) → ② 又失败 → DB-only | `[回退冗余]` 三级降级决策树，能力整组消失仍"成功" |
| **装配主链** | `AgentFrameworkAssembler`(StagePipeline) 本身 **fail-fast**（`HasError→throw`） | ✅ 好设计，被外层 catch 打穿 |
| **配置开关** | `EnableTaskPool` / `EnableReactiveTrigger` / `RegisterStatelessAnswer` / `RegisterCline` 等 → 多形态装配 | `[回退冗余]` 声明与达成无校验桥 |
| **数据连接** | 完整框架 DB vs 独立 `GetOrBootstrapDatabaseAsync` 惰性自举（双连接/双真相来源之一） | `[回退冗余]` 应单一连接由装配主链唯一产出 |
| **运行入口** | `AgentHost`：EnableTaskPool=true 走池 `RunThroughPoolAsync`；否则 `fw.RunAsync` **直跑兜底** | `[回退冗余]` 双入口旁路 |
| **触发** | 手动 RunAsync + 反应式 `ReactiveTrigger`（Enable 开关）可并行 | `[边界]` 汇入同一咽喉后保留 |
| **任务池** | `TaskPoolHosting.TryBootstrapRulePoolAsync`：`catch { return null; }` → "任务池未装配" | `[回退冗余]` catch 吞异常转 null |
| **会话** | `ConversationHosting.TryAttach(pool==null) → 会话未装配`；降级路径干脆不装 | `[回退冗余]` 会话依赖核心能力却随降级静默消失 |
| **统一网关/LLM** | 仅完整框架装配；RulePoolOnly 不装 → `本地联调不可用` | `[回退冗余]` LLM 是核心能力，不该被"零 LLM 形态"切换掉 |
| **调用链** | 7 层 async 编排，横切内联；决策三源（rule_match/Decide/导演） | `[回退冗余]`（见 9.3） |
| **持久化** | `enableOutbox` 开=Outbox / 关=同步持久化，双路径 | `[回退冗余]` 应统一 Outbox |
| **写面** | 多连接/多门（曾进程门锁序反转，9.2 已证伪） | `[回退冗余]` → 单库单写连接/事务 |
| **发现** | `FunctionLibraryLoader` 反射 + `AssemblyLoadContext.Resolving` 兜底 | `[回退冗余]` → 显式清单/ALC |
| **无害增强** | 历史未落盘→内存态；图守卫未装配→只写 ur_nodes 不建边 | `[合理·显式化]` 单点增强缺失，须带 reason 登记 |

---

## 二、根因（为什么会堆成这样）

1. **"可回退、可开关、不阻断宿主"成为默认装配文化**：每个模块注释都以"可开关可回退 / 失败不阻断"开头。增量每加一层能力，就顺手加一个 if/开关或 catch-null，没人定义"最小完整目标态"。
2. **fail-silent 优于 fail-fast**：把"让 UI 能开起来"误当成"让 Agent 可用"，于是一次装配失败不报错、降级、再降级，最终跑一个"只有数据库、Agent 全线不可用"的壳，还自认为成功。
3. **能力是横切的，降级却按模块包切**：会话要 LLM+任务池，规则池形态却把 LLM/会话/网关一起砍掉，制造无法穷举的中间态。
4. **装配意图与装配产物无契约**：`EnableTaskPool=true` 是声明，产物却是"可能 null 的 TaskPool"，没有一步校验"声明达成"。
5. **架构回退与运行容错混淆**：把"数据库锁冲突重试 / IO 瞬时失败"这类运行期容错，和被实现成"核心能力消失"的架构回退，混在同一套 catch/兜底里。
---

## 三、目标：单一路径的收敛原则

把"降级决策树 + 多旁路 + catch-null"收敛为三条可执行原则：

- **P-A 目标态声明（Manifest）**：宿主/入口只做一件事——声明目标形态并交给**确定性装配链**。链结束若不是目标态，即装配失败（抛错，带失败链）。引入单一 `AgentManifest`（目标形态）与 `AssemblyManifest`（产物对账表：模块 × State=Attached/Disabled(reserved-only) × Source × Error）。
- **P-B 核心能力不可降级，增强项显式 Disabled**：LLM 网关、任务池、会话、数据库 = **核心能力**，要么装配要么装配失败（fail-fast），绝无"零 LLM 形态"把它切换掉。历史落盘、图边投影、Outbox 消费 = **增强项**，装配不上只置 `Disabled(reason)`，**不改变装配主态、不砍核心能力、reason 进摘要**。
- **P-C 运行时唯一咽喉**：所有输入（手动 / 反应式 / 任务池 enqueue）汇入同一个 `RunAsync` 咽喉；"直跑 vs 池"合并为"合成瞬态 task 走同一池"（沿用 `RunThroughPoolAsync` 语义为唯一入口）。持久化统一走 Outbox（无同步回退开关）。写面单库单写连接/单事务。

> 与"容错"的边界：锁冲突/瞬时 IO 用**有界重试 + 取消令牌**（运行期容错，不改变形态）；"某核心模块装配失败仍成功"属于**架构回退**，一律消灭。

---

## 四、七面收敛设计（现状 → 去回退 → 单一路径）

### 4.1 装配面（最大改动）
**现状**：`AgentFrameworkHosting.TryBootstrapGuiAsync` ①完整→②RulePoolOnly→DB-only；`StartAsync` 内 `ConversationHosting.TryAttach(a.TaskPool)` 依赖 pool 非空。
**去回退后**：
- 删除 `TryBootstrapGuiAsync` 的两级降级 catch；宿主只调 `AgentFramework.StartAsync(manifest)`。
- 装配主链唯一产出 `AssemblyManifest`：模块登记（任务池/会话/LLM 网关/数据库/历史/图/Outbox）各带 `State` 与 `reason`；**核心模块未达成 → 装配失败抛错**（附失败链），不再有"DB-only 成功"。
- 会话装配不再以"pool==null 就跳过"为前置；会话与任务池由同一装配链按声明装配，任一核心失败即 fail-fast，不存在"会话因池 null 静默缺失"。
- 数据库由装配主链唯一构建（单连接），删除 `GetOrBootstrapDatabaseAsync` 惰性独立自举路径（消除双连接/双真相）。

### 4.2 运行入口面
- `AgentHost.RunAsync` 不再有"EnableTaskPool=false → 直跑兜底"分支；直跑 = 合成 task 走统一池（唯一咽喉）。`EnableTaskPool` 声明进 manifest 并对账：声明了却没池 = fail-fast。
- 反应式 `ReactiveTrigger` 保留为**输入源**，其 handler 调用同一咽喉；与手动入口共用同一池/同一 RunAsync。

### 4.3 调用链面
承接 9.3 §三/§四：唯一 `StepExec.InvokeAsync` 中间件栈（取消/单步超时/watchdog/LLM门/错误归一/观测 为中间件）；决策信号化单 `LoopSignal` 面；外层 post_round 并入 `onRoundCompleted`（消双层编排）。

### 4.4 决策面
rule_match decision / RoundLifecycle.Decide / 调度导演提议 → 单一 `LoopDecision` 信号面，一处裁决（决策点收敛），消灭"双层各自写决策"。

### 4.5 错误面
- `ErrorClassifier` 统一分类；装配与关键路径失败**即抛**并带 `ErrorClass`。
- 消灭所有 `catch { return null; }`（TaskPoolHosting / DatabaseBootstrapper / 能力模块）；改为**不 catch** 或 catch 后 `throw`（保留失败链）或在装配链登记 `Disabled(reason)`。
- 取消令牌/有界重试为显式运行期容错（保留），但它不吞装配失败。

### 4.6 写面
采纳 9.2 §四：单库单写连接/单事务；Outbox 与主循环同写通道；图/LiteDB 与同库隔离同写序；删除 `enableOutbox` 同步回退开关（统一 Outbox），门退化为有界护栏。

### 4.7 发现/装配面
采纳 9.2-FunctionLibraryLoader：scoped ALC + PrjBridge 构建期装配清单（部署即注册）；删除 `AssemblyLoadContext.Resolving` 运行时兜底与反射 `Type.GetType` 撞运气。

---

## 五、装配形态模型（单一路径的"形态"载体）

```
宿主声明：AgentManifest{ Mode = CompleteAgent }         // 无降级模式枚举
   └─ AgentFramework.StartAsync(manifest)               // 装配主链唯一入口
        └─ StagePipeline：... FrameworkTaskPoolStage / FrameworkConversationStage / FrameworkLlmGatewayStage ...
             （每阶段只做"装配 or 抛错"，不做"装配 or 降级"）
        └─ 产物对账：AssemblyManifest[ 任务池=Attached, 会话=Attached, LLM网关=Attached,
                                        历史=Disabled(reason), 图投影=Attached, Outbox=Attached ]
        └─ 核心校验：任一核心模块 ≠ Attached → throw AssemblyFailed(failedModules, chain)
   └─ MainForm/入口读 AssemblyManifest：核心缺失 → UI 明确"Agent 不可用 + failedModules/reason"
         （而不是 DB-only 继续 OnLoad 成功、Agent 静默不可用）
```

要点：
- **没有模式枚举**（CompleteAgent / RulePoolOnly / DB-only 之类全删）；只有"目标态达成 / 装配失败"二元。
- **增强项 Disabled 是显式登记**，进摘要与事件，不吞异常，不改变主态。
- **单连接**由装配主链唯一产出并回注（数据库 = 装配产物，非惰性自举）。

---

## 六、分阶段落地

> 每阶段独立、可回退（回退 = git revert 该阶段），逐 sln 编译 + 既有测试回归。**建议先做 E 面（装配），它是"静默半装配"的直接根因**。

| 阶段 | 面 | 内容 | 关键改动文件 |
|------|----|------|-------------|
| **S1** | 装配 | 引 `AgentManifest/AssemblyManifest`；删除 `TryBootstrapGuiAsync` 两级降级；装配主链 fail-fast；会话/任务池/网关按声明装配；删惰性独立 DB | `AgentFrameworkHosting`、`AgentFramework.StartAsync`、`ConversationHosting`、`TaskPoolHosting`、`MainForm`(读摘要) |
| **S2** | 运行入口 | 删直跑兜底分支；统一咽喉 RunAsync/池；Reactive 汇入同咽喉 | `AgentHost`、`AgentOrchestrator` |
| **S3** | 调用链 | 唯一 `StepExec.InvokeAsync` 中间件栈 + 单步取消/超时/watchdog（承接 9.3 S1/S2 的调用链部分） | `RoundStepInvoker`/`NodeExecutionService`/新增 `Invocation/` |
| **S4** | 决策/错误 | `LoopSignal` 单面；装配/关键路径失败即抛带 ErrorClass；清 `catch{return null}` | `DecisionPoint`/`ErrorClassifier`/各装配点 |
| **S5** | 写 | 单库单写连接/事务；统一 Outbox（删 enableOutbox 回退）；图同写序 | 存储 `Sqlite/*`、`OutboxWriter`、`NodeExecutionService.PersistAsync` |
| **S6** | 发现 | scoped ALC + 构建清单；删 Resolving 兜底 | `FunctionLibraryLoader`、`PrjBridge.csproj` |

> 依赖：S1 独立先行；S2 依赖 S1（咽喉基于目标态）；S3/S4 可与 S1/S2 并行；S5 独立；S6 独立。

---

## 七、验收与"单一路径"判据（回归基准）

统一复跑 `agent_mcpinvoke_create_task_test.json` / `agent_taskpool_test.json` / GUI 启动，应观测：
1. **无双入口旁路**：EnableTaskPool=true 时任务必然走统一池；不存在"直跑兜底"或"Reactive 另起直跑"。
2. **装配非二元即失败**：核心模块（任务池/会话/LLM 网关/数据库）任一未 Attached → 启动即 fail，日志给出 `failedModules + 失败链`，UI 明确"Agent 不可用+原因"；**不再出现"DB-only 但 OnLoad 成功、Agent 静默不可用"**。
3. **无 catch 吞异常转 null 的能力模块**：全库 grep 无 `catch { return null }` 承载核心能力。
4. **单连接**：agent.db 仅由装配主链唯一连接，无惰性自举第二条连接。
5. **调用链单入口**：单步函数调用经唯一 `StepExec`，横切为中间件；单步超时/卡超自证。
6. **决策单源**：continue/stop/TO/TM 由单一 LoopSignal 面裁决，无双层写决策。
7. `FileDriven.sln` / `Agent.sln` 逐 sln 0 警告 0 错误；既有测试全绿。

---

## 八、风险与边界（诚实的"零回退"限定）

| 项 | 说明 |
|----|------|
| "零回退" ≠ 零容错 | 锁冲突/瞬时 IO 保留**有界重试 + 取消令牌**（运行期容错）；此处消灭的是"核心能力架构切换/静默半装配"。 |
| 改动面大 | S1–S6 相互独立、逐阶段可回退；`enableOutbox`/`EnableTaskPool` 等存量开关在过渡期保留为"声明+对账"，不保留"降级分支"。 |
| 既有形态（RulePoolOnly 等）删除风险 | 若确有"无凭据也要跑 Config 任务"的真实诉求，把它建模为**显式目标态 `RulesOnlyAgent`（仍是完整装配、只是无 LLM 执行器）**，而非"降级形态"；进 manifest 并对账，绝不 catch-null。 |
| 外部宿主契约（IAgentUiBridge/BootstrapResult） | 调整契约字段（RulePoolOnly→failedModules+reason），更新 MainForm/AgentUiBridge；一次性迁移。 |

---

*本文档基于 2026-09-02 会话对装配层（AgentFrameworkHosting/StartAsync/ConversationHosting/TaskPoolHosting/AgentHost/Assembler）与调用链的源码核实整理，作为"移除回退冗余、单一路径"彻底重构的分阶段执行依据。*

