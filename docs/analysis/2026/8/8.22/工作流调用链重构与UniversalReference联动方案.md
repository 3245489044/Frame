# 工作流调用链重构与 UniversalReference 联动方案

> 日期：2026-08-22 ｜ 范围：Agent 会话/主循环调用链 + 引用关系（UniversalReference）联动
> 状态：**总目标 = 在优化重构架构的前提下解决 D1–D5（部分落地）** —— A（词汇表对齐）+ B（控制流边静态化）已实现（架构优化）；解决 D1–D5 的 P0–P2 主线待落地

---

## 一、背景与目标

会话工作流运行出现「轮次完成后返回链挂起 / 步骤完成永不打印 / 结果静默丢失」，经调用链分析确认**不是单点 bug，而是架构级系统缺陷**。

**总目标：在优化重构架构的前提下，解决最初列出的 D1–D5 架构问题** ——
（D1 返回链挂起/完成信号不握手、D2 持久化耦合/失败吞掉、D3 状态多源无单一事实源、D4 错误不归一、D5 超时多域/取消传播）。

**手段（服务于总目标，非独立目标）**：把过程式编排重构为声明式节点模型 + 与 UniversalReference 联动，使「解决 D1–D5」能在可维护、可查询的架构上落地。本文档沉淀：

1. 调用链现状与问题画像（D1–D5，带代码位置）；
2. 重构手段总览与「手段 ↔ 问题」映射；
3. 已落地改动（A + B）与决策记录；
4. 以解决 D1–D5 为主线的待落地计划（P0–P2 + 节点化 Step 1–3）与验证方法。

---

## 二、现状调用链与问题画像

### 2.1 完整调用链

```
[GUI/AEF] AgentUi_ConversationSend —— 120s AbsoluteTimeout
  → ConversationSendAsync（AgentUiLibraryConversation.cs:30）
       ├─ svc.SendAsync(..., CancellationToken.None)   ← 37 行：GUI 取消令牌被丢弃
       └─ catch(Exception ex) → ex.Message             ← 44-47：异常降级为字符串
  → ConversationSessionService.SendAsync（ConversationSessionService.cs:34）
       ├─ SemaphoreSlim.WaitAsync / finally Release     ← 38/46：try-finally 已正确
  → ConversationPipeline.RunSignalAsync（ConversationPipeline.cs:77）
       ├─ EnsureSession → SeedWorkflow → await _runWorkflow(lease, ct)   ← 101
       ├─ 回写 stages/assistant、Round++、Status=Completed、UpdatePayload  ← 104-121
       └─ diagnostics = "步骤异常: {result.Error}"                          ← 126-128（字符串）
  → WorkflowRunner.RunRoundAsync（WorkflowRunner.cs:129）
       ├─ StaticAsyncPipeline（每步 IsFatal:false，BuildPipeline:111）
       ├─ 步骤异常归集：ctx.Error = result.FirstError?.Exception.Message  ← 152（类型/栈丢失）
       └─ 成功判定依赖 decision（193-196，主循环 rule_match 语义）
  → RoundStepInvoker.ExecuteStepAsync（RoundStepInvoker.cs:60）
       ├─ start 日志(:69) → ResolveInputs → ExecuteAndPersistAsync(:88) → completed 日志(:99)
  → NodeExecutionService.ExecuteAndPersistAsync（NodeExecutionService.cs:145）
       ├─ CallFunctionAsync → ExtractProps → vars.Set(:166，先写内存)
       ├─ _store.WriteOutputAsync ur_nodes(:169-172)
       └─ PersistNodeMetadataAsync SQL KeyMapping + 图边(:175)
  → [daemon] CallFunctionAsync → ClineAgentExecutor → ClineClient(池)
       └─ ← 断点：daemon 完成但 C# 未收结果（预算错位，只能靠最外层 AEF 兜底）
```

### 2.2 问题画像（D1–D5）

| # | 问题 | 关键证据 | 公理违背 |
|---|------|----------|---------|
| **D1** | 嵌套异步编排，完成信号不握手；返回链挂起靠最外层 AEF 兜底 | `ConversationSendAsync.cs:37` `CancellationToken.None`；`RunSignalAsync:101-121` 无 try/finally；daemon 结果回传断链 | 无显式运行完成信封 |
| **D2** | 持久化嵌入步骤调用链，失败被降级为非致命吞掉 | `ExecuteAndPersistAsync:145-177` 混函数+内存写+落库；`IsFatal:false`(:111)；`步骤完成` 不打印 | `ErrorClass`/`FailureSubtyping`/`EvaluationOutcomeSet` 未落地 |
| **D3** | 多层状态叠加，无单一事实源，无事务边界 | vars→ur_nodes→SQL→图边→任务池，先内存后落库(:166 先于 :169/175)；SQL 事务仅包 KeyMapping | `StateRootSingleSource` |
| **D4** | 错误对象各层自造、互不归一，降级为字符串 | `WorkflowRunner.cs:152` `ctx.Error=Message`；`ConversationPipeline:126-128`；`AgentUiLibraryConversation:46` | 错误分类与动作域分离未落地 |
| **D5** | 超时/取消多域并存，取消传播靠隐式令牌链 | GUI 120s / daemon 295s(`ClineCallCap`) / 网关 60s 竞速；`CancellationToken.None` 使 AEF 杀外层后孤儿任务锁死会话 gate | — |

> 注：日志铁证（`LLM 测试卡点已精确定位.md`）显示 daemon 4s 完成但 C# `SendAsync` 未收，`步骤完成: cline_execute` 永不打印——D1 的**具体断点在 daemon 传输层**，纯 C# 返回链信封治不了它。

---

## 三、重构手段总览与「手段 ↔ 问题」映射

三条手段最终统一到「声明式节点模型 + 可查询引用图」，**共同服务于解决 D1–D5**：

```
① 节点化重构：把过程式编排降维为 RoundSpec 节点（编排 = 加节点）
② UniversalReference 联动：把调用流程静态化为可维护引用关系（本批已落地 A+B）
③ P0 止血：终结挂起与 120s 盲等（daemon 回传 / 取消透传 / 预算对齐）
```

| 手段 | 直接解决的最初问题 | 状态 |
|---|---|---|
| ① 节点化重构（横切挂钩 / 会话边界节点 / 完成信封） | D1、D2、D3、D4、D5 | 待落地（Step 1–3） |
| ② UniversalReference 联动（词汇表对齐 + 控制流静态化） | 不直接解决 D1–D5；为 ① 提供可查询引用图基底（G1/G2） | ✅ 本批已落地 A+B |
| ③ P0 止血（daemon 回传 / 取消透传 / 预算对齐） | D1、D5 | 待落地 |

> 主线 = ③ → ①：先止住挂起（③），再以节点化重构（①）根治 D2/D3/D4 与完成信封（D1），② 作为架构优化提供「调用流程可查询」。

---

## 四、已落地：与 UniversalReference 联动（A + B）

> 本批改动解决第三方向 G1（词汇表未对齐）+ G2（控制流边未静态化）。

### 4.1 A —— 词汇表对齐

**文件**：`CsharpPrj/Operation/DataStoreDriven/ReferenceSchema/reference-schema.dsl.md`（+ 重生成 `reference-schema.generated.json` / `schema-report.txt`）

| 类别 | 新增 | 对齐对象 |
|---|---|---|
| `[Node]` | `node` / `task` / `keymapping` | 运行时 `NodeResourceTypes.Node/Task/KeyMapping`（原 DSL 缺失） |
| `[Edge]` | `node_to_node` / `keymapping_to_node` | 协调器实际写入的 refType（消除「DSL 声明 ≠ 实际写边」分裂） |
| `[Edge]` | `Calls`（控制流）/ `Owns`（工作流包含） | 新增控制流/组合语义边 |

### 4.2 B —— 控制流边静态化

| 文件 | 改动 |
|---|---|
| `DataStoreDriven/Abstractions/StaticReference.cs` | 新增：原始 `(src,tgt,refType)` 三元组，对齐 `AddReferenceAsync` |
| `DataStoreDriven/Abstractions/NodeRelationStore.cs` | 新增 `NodeResourceTypes.Workflow` + `WorkflowReferenceTypes.Calls/Owns` |
| `DataStoreDriven/Core/NodeRelationDeriver.cs` | 新增 `DeriveWorkflowControlFlowEdges`：`Owns`(workflow→每步骤) + `Calls`(步骤 i→i+1)，模板粒度 |
| `DataStoreDriven/Abstractions/ReferenceMaintenance.cs` | 接口新增 `WriteStaticReferencesAsync` |
| `DataStoreDriven/Core/ReferenceGrounding.cs` | 实现：建端点→删旧边→新增（幂等，经协调器单一写边原语） |
| `Agent.Orchestration/StepExecution/WorkflowReferenceStaticizer.cs` | 新增：`RoundSpec` → 静态引用边 → 守卫写入 |
| `Agent.Orchestration/StepExecution/WorkflowRunner.cs` | `_grounding` 字段 + 每进程每工作流一次守卫，`RunRoundAsync` 首轮调用 |
| `Agent.Core/Diagnostics/agent_log_messages.json` | 新增 Warn 消息 `WorkflowRunner_controlFlowStaticize_failed` |
| `ReferenceSchema.Tests/ReferenceSchemaDslTests.cs` | 新增 3 例：Owns+Calls 推导 / 空步骤空集 / 单步骤仅 Owns |

### 4.3 决策记录

- **为什么控制流边走独立写路径**：协调器 `Ref.Encode` 仅支持 `KeyMappingToNode/NodeToNode`，未知类型抛异常；且 `WriteRelationsAsync` 是**按目标节点的权威边集**（删残留+补缺失），控制流边走它会被后续数据流写删掉。故走 `AddReferenceAsync`（幂等、加法），与数据流边互补互不干扰。
- **为什么挂 `RunRoundAsync`**：模板粒度 + 每进程每工作流一次守卫；未装配守卫（DB-only）零开销跳过；**infra 失败非致命**（告警 + 不登记以便重试，不翻转业务 ok，对齐 D2/D4 原则）。
- **G3（实例端点）/ `OnError` 边后置**：实例轨迹需定保留策略（防无限增长，对齐 `InformationMonotonicity` 有界遥测）；`OnError` 需先让 `RoundSpecLoader` 保留 on_error 边。

---

## 五、待落地计划（以解决 D1–D5 为主线）

> 本计划是**解决最初问题**的执行主线：P0 止挂起（D1/D5）→ P0 错误分类（D4）→ P1 持久化解耦/单一事实源（D2/D3）→ 节点化 Step 1–3（根治 D1–D5）→ P2 完成信号统一（D1）。

### 5.1 P0 止血（终结挂起 / 120s 盲等）—— 建议最先切

| 项 | 动作 | 目标 |
|---|---|---|
| P0-1 | 修 daemon 结果回传断链（`ClineDaemonIpc` / `ClineDaemonHost` / `DaemonPool.SendAsync` 完成帧接收） | 让 `步骤完成` 出现（根治点） |
| P0-2 | `ConversationSendAsync` 的 `CancellationToken.None` → 透传 GUI ct | 取消可传播，孤儿任务不再锁死会话 gate |
| P0-3 | 预算对齐：`ClineAgentExecutor` 的 `callTimeout = min(会话预算, ClineCallCap)` | collect 在会话预算内超时，消除 5 分钟空等 |

### 5.2 P0 错误分类落地（D4）

轮次/会话边界引入强类型错误信封 `category∈{business,infra,llm}`（对齐 `ErrorClass`），替换 `WorkflowRunner.cs:152` 的字符串 `Message`。

### 5.3 P1 持久化解耦 + 单一事实源（D2/D3）

- 把 `ExecuteAndPersistAsync` 的落库副作用拆成**步骤后横切后置**（`StaticAsyncPipeline` 的 `onAfterStep`），失败单独归类、不翻转 ok、有归因日志；
- 以 ur_nodes 持久态为唯一权威写，内存 vars / 任务池存储只做派生读，消除双写歧义。

### 5.4 节点化重构（手段①，编排 = 加节点）

| Step | 内容 | 目标 |
|---|---|---|
| 1 | 启用 `StaticAsyncPipeline` 的 `onBeforeStep/onAfterStep/onStepError` 挂钩，横切入参解析/持久化/错误归类 | D2/D4 最小落地，零新引擎 |
| 2 | 把 `ConversationPipeline.RunSignalAsync` 的会话编排（Ensure/Seed/Harvest/Round++/Status/WriteStatus/BackendAudit）注册成头/尾节点 | D1/D3 会话边界节点化 |
| 3 | 复用 `DynamicWorkflowMerger` 恢复 `on_success/on_error` 图语义；`session_complete` 节点显式完成信号 | D1/D5 完成信封 |

### 5.5 P2 完成信号统一

「一次运行 = 一个完成结果对象」在 `ConversationPipeline` 层出，GUI 直接消费，不再隐式依赖 Task 续延。

---

## 六、验证结果（本轮 A+B）

| 验证 | 结果 |
|---|---|
| `DataStoreDriven` 编译 | ✅ 0 警告 0 错误 |
| `Agent.Orchestration` 编译 | ✅ 0 警告 0 错误 |
| 新增 3 个控制流推导测试 | ✅ 3/3 通过 |
| `reference-schema.generated.json` 反序列化 | ✅ 7 节点 / 10 边 / 10 约束，含 Calls/Owns |

> 注：`ReferenceSchema.Tests` 另有 5 个失败为**预存环境问题**——`RepoRoot()` 要求根目录同时含 `CsharpPrj`+`Agent`，而本仓库 Agent 在 `Projects\Agent`（根无顶层 `Agent` 目录），失败全发生在读取 committed schema 之前的 `RepoRoot()`，与本次改动无关。

---

## 七、风险与边界

- **A+B 不是 D1–D5 修复**：它解决「工作流↔引用图联动」，不改挂起/错误归一/持久化耦合/单一事实源。
- **模板边 vs 实例边须分离**：B 落静态模板（一次）；G3 实例轨迹（受控），勿混，否则图失控。
- **词汇表强制风险**：`IReferenceTypeRegistry.IsDeclared` 一旦对协调器写路径强制执行，未注册的 refType 会被拒——本批已注册 `node_to_node`/`keymapping_to_node`/`Calls`/`Owns`，其余新增边须先注册。
- **控制流边的查询面**：`Calls/Owns` 走原始 `AddReferenceAsync`，在 `INodeRelationStore.GetRelationsAsync` 中被 `ParseNodeRelationType` 过滤（仅识别两类节点关系）；控制流边经通用引用读面（`GetReferencesAsync`/CoreGraph）可查，供图算法/影响分析消费。

---

## 八、下一步建议（总目标：解决最初问题）

1. **主线（解决 D1–D5）**：P0 止血（5.1，D1/D5）→ P0 错误分类（5.2，D4）→ P1 持久化解耦/单一事实源（5.3，D2/D3）→ 节点化 Step 1–3（5.4，根治）→ P2 完成信号统一（5.5，D1）。
2. **首刀**：P0 止血 —— 直接终结挂起与 120s 盲等，不依赖节点化重构。
3. **联动方向（G3 实例端点 / `OnError` 边）**：作为架构优化，在节点化恢复图语义后接入，服务于「调用流程可查询」。

