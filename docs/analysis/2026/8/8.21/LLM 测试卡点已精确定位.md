## 结果报告：真实 LLM 测试卡点已精确定位（非 analyze/网关，是 **collect 的 daemon 传输层**）

### 铁证（来自本次新增的步骤日志）

```
22:34:10.799  步骤开始: cline_execute        ← collect 开始
22:34:14.817  请求完成: req-1 (4.0s)          ← collect 的 daemon 侧 4 秒完成（真实 deepseek 调用成功）
     ...（120 秒空窗，无任何步骤日志）...
22:36:16.423  步骤开始: answer                 ← analyze 在超时瞬间才出现 start
22:36:16.425  AEF 超时终止
```

**关键异常**：`步骤完成: cline_execute` **从未出现**。而 `步骤开始: answer` 在 22:36:16（紧贴 120s 超时）才出现——这说明管线一直卡在 collect 步，直到会话取消才被迫推进。

### 根因定位

**collect 的 `ClineClient.AskAsync` 在 daemon 请求"完成"（22:34:14）后，C# 侧没有返回，卡死约 120 秒**（直到会话 AEF 取消才经 OCE 结束）。

- collect 真实调用本身 **4 秒成功**（split-brain 修复已验证，`baseUrl=http....com` 真实端点）。
- 卡点**不是** analyze（answer）：analyze 的 `步骤开始` 在超时瞬间才出现，说明它**从未真正执行**（此前"无 guard_* 日志"即由此解释——analyze 根本没走到网关）。
- 卡点在 **`ClineClient` ↔ daemon 的 IPC 传输/池层**：daemon 侧 `请求完成` 但 C# 侧 `SendAsync` 未收到/未解析结果，持续等待。

### 为什么之前误判

- 前几轮把卡点推断到 analyze（answer/SK）或 act，但**没有步骤级日志**佐证。本次补的 `RoundStepInvoker` 步骤日志直接揭示：卡在 collect 步本身（daemon 完成但 C# 未返回）。

### 附加问题：预算错位

- `daemon run-start timeoutSec=295`（ClineClient 预算 5 分钟 = `ClineCallCap`）。
- 会话 `AgentUi_ConversationSend` 预算 **120s**。
- 二者不一致 → collect 卡住时，ClineClient 5 分钟超时远大于会话 120s，**只能靠会话取消兜底**，白白空等。

### 修复方向（涉 `Agent.Modules/Cline/Pool/*` + `Share.StateManagement.Pooling.DaemonPool`）

1. **修 daemon 结果回传**：`ClineDaemonIpc`/`ClineDaemonHost`/`DaemonPool.SendAsync` 在 daemon 请求完成后，结果为何未被 C# 侧接收（响应解析/事件订阅/Task resolve 断链）。这是根治点。
2. **预算对齐（止血）**：`ClineAgentExecutor` 的 `callTimeout` 应取 `min(循环/会话预算, ClineCallCap)` 并保证会话预算经 `AgentLoopGuard.GetBudget()` 传入（日志显示实际用 295s 而非会话 120s）——使 collect 在会话预算内超时，而非 5 分钟空等。

### 本轮已落地（框架层，全部验证通过）

- split-brain 修复（`Agents.cline.BaseUrl` 真实端点，collect 4s 成功）。
- 治理可观测：`LlmRoutingClient` 超时/失败/成功经 `_log` 回调写 `AgentLog`（`Agent.Runtime` 不依赖 `Agent.Axioms`）。
- 步骤级日志：`RoundStepInvoker` start/completed（Info）。
- `Agent.sln` 0 警告 0 错误；`LlmRoutingClientGuardTests` 4/4。

下一步建议：优先修**预算对齐（止血）**——让 collect 超时遵循会话预算，立即消除 120s 空等；再深入 daemon 传输层定位结果回传断链（根治）。

[22:33:51.755] [INFO] [BackEndDriven] [FrameworkExecutor] 工作流开始执行: Mode=GUI
[22:33:55.097] [INFO] [FunctionStorage] [MathLibrary] 注册完成
[22:33:55.126] [WARN] [FunctionStorage] [WorkflowLibrary] 注册工作流到 Hub 失败: 事件执行服务未装配（IEventExecutionService 为空）
[22:33:58.396] [INFO] [FileDriven.Execution] PostInitHooks 执行完成: summary=FunctionExecutor=injected(type=FunctionExecutor), LoadConfigPort=registered(type=LoadConfigPortAdapter), ClearOutputPort=registered(type=ClearOutputPortAdapter), hookCount=3, hasError=false, elapsedMs=264
[22:34:00.351] [INFO] [FileDriven.Execution] MainForm 从配置启动: bootPhaseCount=2
[22:34:00.359] [WARNING] [FrontEndDriven] [ControlRegistryAwareFinder] Not found: name=contentContainer, openFormsCount=1
[22:34:00.360] [WARNING] [FrontEndDriven] [ControlRegistryAwareFinder] Not found: name=sideContainer, openFormsCount=1
[22:34:01.536] [INFO] [FrontEndDriven] EcsGate 渲染挂载统计 — scope=MainLayout, succeeded=true, collected=18, applied=18, mounted=17, orphan=0, hostActualChildCount=1
[22:34:01.540] [INFO] [FrontEndDriven] ScopeLifecycleAdapter 渲染判据 — scope=MainLayout, config=MainLayout.json, renderSucceeded=true, rootAttached=true, hostActualChildCount=1, contentMounted=true, mountVisible=true, succeeded=true
[22:34:01.562] [INFO] [FrontEndDriven] 静态数据应用 — controlName=configListView, controlType=listview, itemCount=0
[22:34:01.570] [INFO] [FrontEndDriven] 静态数据应用 — controlName=styleComboBox, controlType=combobox, itemCount=5
[22:34:01.595] [INFO] [FrontEndDriven] EcsGate 渲染挂载统计 — scope=Content, succeeded=true, collected=11, applied=11, mounted=10, orphan=0, hostActualChildCount=1
[22:34:01.853] [INFO] [FunctionStorage] [UIToolbox] 加载 JsonConfig 列表完成: count=10
[22:34:01.861] [INFO] [FrontEndDriven] ScopeLifecycleAdapter 渲染判据 — scope=Content, config=ConfigSelector.json, renderSucceeded=true, rootAttached=true, hostActualChildCount=1, contentMounted=true, mountVisible=true, succeeded=true
[22:34:01.870] [INFO] [FrontEndDriven] 增量渲染完成 — scope=, reason=function-executed, result=RenderRunResult { Scope = MainLayout, RootEntity = Entity(0:0), CollectedCount = 18, AppliedCount = 18, ElapsedMs = 7, RenderExecuted = True, MountedCount = 17, ContentMounted = 17, OrphanCount = 0, ControlsSkippedCount = 0, SkipReasons = System.Collections.Generic.List`1[System.String], HostActualChildCount = 1, Succeeded = True }
[22:34:03.354] [INFO] [Agent.Core] [Agent] 统一 LLM 网关已装配（生效后端=semantic）
[22:34:06.786] [WARN] [Agent.Core] [AxiomBinding] drift: AgentRunCancellation: TerminationPriority (drift)
[22:34:06.786] [WARN] [Agent.Core] [AxiomBinding] gap: DerivedDataAutoMaintenance (L2): 无实现绑定
[22:34:06.787] [WARN] [Agent.Core] [AxiomBinding] gap: ClassifyError (L1): 无实现绑定
[22:34:06.788] [WARN] [Agent.Core] [AxiomBinding] gap: DecisionOutputSet (L1): 无实现绑定
[22:34:06.789] [WARN] [Agent.Core] [AxiomBinding] gap: EvaluationOutcomeSet (L1): 无实现绑定
[22:34:06.790] [WARN] [Agent.Core] [AxiomBinding] gap: RoundLifecycle (L1): 无实现绑定
[22:34:06.791] [WARN] [Agent.Core] [AxiomBinding] gap: StratifiedReflectionSchema (L1): 无实现绑定
[22:34:06.792] [WARN] [Agent.Core] [AxiomBinding] gap: SingleImplementation (L0): 无实现绑定
[22:34:06.795] [INFO] [Agent.Core] [AxiomBinding] 公理绑定: axioms=16, bindings=13, drift=1, gaps=7
[AxiomBinding] Agent 公理绑定: axioms=16, bindings=13, drift=1, gaps=7
[AxiomBinding] gap: DerivedDataAutoMaintenance (L2): 无实现绑定
[AxiomBinding] gap: ClassifyError (L1): 无实现绑定
[AxiomBinding] gap: DecisionOutputSet (L1): 无实现绑定
[AxiomBinding] gap: EvaluationOutcomeSet (L1): 无实现绑定
[AxiomBinding] gap: RoundLifecycle (L1): 无实现绑定
[AxiomBinding] gap: StratifiedReflectionSchema (L1): 无实现绑定
[AxiomBinding] gap: SingleImplementation (L0): 无实现绑定
[AxiomBinding] drift: AgentRunCancellation: TerminationPriority (drift)
[22:34:06.810] [INFO] [Agent.Core] [Agent] [BoundaryConfig] 已加载边界配置: D:\Project\Program\WorkFlowPrj\Projects\Agent\Config\boundaries/boundary-config.json
[22:34:06.815] [INFO] [Agent.Core] [Agent] 已注册安全护栏模块
[22:34:06.816] [INFO] [Agent.Core] [Agent] 已注册 MCP 工具模块（通过 MCPBridge）
[22:34:06.817] [INFO] [Agent.Core] [Agent] 开始初始化...
[22:34:06.817] [INFO] [Agent.Core] [Agent] 初始化完成
[22:34:06.819] [INFO] [Agent.Core] [Agent] 已注入全局 LLM 门控（kill switch / 预算）
[22:34:06.956] [INFO] [Agent.Core] [Agent] 已注入轮间暂停检查
[22:34:07.103] [INFO] [Agent.Core] [Agent] [WorkflowRunner] 已加载会话工作流 collect-analyze-act.json（steps=3，path=D:\Project\Program\WorkFlowPrj\Projects\Agent\Config\workflows/collect-analyze-act.json）
[22:34:07.106] [INFO] [FileDriven.Execution] Agent 运行时装配: frameworkAttached=true, databaseAttached=true, rulePoolOnly=false, error=null
[22:34:07.108] [INFO] [FileDriven.Execution] MainForm OnLoad 成功: elapsedMs=6832, collectedNodeCount=11, appliedNodeCount=11, appliedRatio=1
[22:34:07.165] [INFO] [FileDriven.Execution] 开始加载脚本: scriptPath=D:\Project\Program\WorkFlowPrj\FileDriven\DrivenConfig\Scripts\Agent\agent_real_llm_test.json
[22:34:07.171] [INFO] [FileDriven.Utilities] ScriptConverter: 模板展开完成, before=24, after=24
[22:34:07.404] [INFO] [FileDriven.Execution] 脚本加载完成: scriptPath=D:\Project\Program\WorkFlowPrj\FileDriven\DrivenConfig\Scripts\Agent\agent_real_llm_test.json, totalCommands=24
[22:34:07.436] [INFO] [BackEndDriven] [Command] 同步执行函数: function=AgentUi_LocalTestSet
[22:34:07.459] [INFO] [FrontEndDriven] 增量渲染完成 — scope=, reason=function-executed, result=RenderRunResult { Scope = MainLayout, RootEntity = Entity(0:0), CollectedCount = 18, AppliedCount = 18, ElapsedMs = 15, RenderExecuted = True, MountedCount = 17, ContentMounted = 17, OrphanCount = 0, ControlsSkippedCount = 0, SkipReasons = System.Collections.Generic.List`1[System.String], HostActualChildCount = 1, Succeeded = True }
[22:34:07.461] [INFO] [BackEndDriven] [Command] 函数执行成功: function=AgentUi_LocalTestSet, result=测试模式=OFF · 按配置 | 生效后端=semantic | 端点=http://127.0.0.1:8787/ | CallLog=0
[22:34:07.474] [INFO] [FrontEndDriven] ECS 状态写入物理投影 — controlName=statusLabel, entityId=30, value=测试模式=OFF · 按配置 | 生效后端=semantic | 端点=http://127.0.0.1:8787/ | CallLog=0, controlFound=true, physicalAfter=测试模式=OFF · 按配置 | 生效后端=semantic | 端点=http://127.0.0.1:8787/ | CallLog=0, projection=written, error=null
[22:34:07.938] [INFO] [BackEndDriven] [Command] 同步执行函数: function=AgentUi_GetLocalTestStatus
[22:34:07.969] [INFO] [FrontEndDriven] 增量渲染完成 — scope=, reason=function-executed, result=RenderRunResult { Scope = MainLayout, RootEntity = Entity(0:0), CollectedCount = 18, AppliedCount = 18, ElapsedMs = 30, RenderExecuted = True, MountedCount = 17, ContentMounted = 17, OrphanCount = 0, ControlsSkippedCount = 0, SkipReasons = System.Collections.Generic.List`1[System.String], HostActualChildCount = 1, Succeeded = True }
[22:34:07.969] [INFO] [BackEndDriven] [Command] 函数执行成功: function=AgentUi_GetLocalTestStatus, result=测试模式=OFF · 按配置 | 生效后端=semantic | 端点=http://127.0.0.1:8787/ | CallLog=0
[22:34:07.971] [INFO] [FrontEndDriven] ECS 状态写入物理投影 — controlName=statusLabel, entityId=30, value=测试模式=OFF · 按配置 | 生效后端=semantic | 端点=http://127.0.0.1:8787/ | CallLog=0, controlFound=true, physicalAfter=测试模式=OFF · 按配置 | 生效后端=semantic | 端点=http://127.0.0.1:8787/ | CallLog=0, projection=written, error=null
[22:34:08.287] [INFO] [FrontEndDriven] ConfigLoadRender: 渲染进主内容区 — configBaseName=AgentRuntimeUI, contentScope=Content, configPath=D:\Project\Program\WorkFlowPrj\FileDriven\DrivenConfig\JsonConfig\AgentRuntimeUI.json
[22:34:08.334] [INFO] [FrontEndDriven] 静态数据应用 — controlName=taskPoolListView, controlType=listview, itemCount=0
[22:34:08.335] [INFO] [FrontEndDriven] 静态数据应用 — controlName=dbSelectorComboBox, controlType=combobox, itemCount=3
[22:34:08.336] [INFO] [FrontEndDriven] 静态数据应用 — controlName=sessionSelectorComboBox, controlType=combobox, itemCount=0
[22:34:08.431] [WARNING] [FrontEndDriven] SyncActualContainerSizes: 容器尺寸不一致 — entityId=60, controlName=pageTasks, controlType=TabPage, ecsWidth=192, ecsHeight=72, actualWidth=476, actualHeight=705, note=ExternalConsistency: ECS 布局为唯一真源，WinForms 实际尺寸仅审计不回写
[22:34:08.431] [INFO] [FrontEndDriven] EcsGate 渲染挂载统计 — scope=Content, succeeded=true, collected=48, applied=48, mounted=47, orphan=0, hostActualChildCount=1
[22:34:08.460] [INFO] [FrontEndDriven] 增量渲染完成 — scope=Content, reason=function-executed, result=RenderRunResult { Scope = Content, RootEntity = Entity(0:0), CollectedCount = 48, AppliedCount = 48, ElapsedMs = 20, RenderExecuted = True, MountedCount = 47, ContentMounted = 47, OrphanCount = 0, ControlsSkippedCount = 0, SkipReasons = System.Collections.Generic.List`1[System.String], HostActualChildCount = 1, Succeeded = True }
[22:34:08.464] [INFO] [FrontEndDriven] ECS 状态写入物理投影 — controlName=templateListView, entityId=29, value=README.md
graph
graph/diff.json
graph/README.md
graph/rebuild.json
graph/status.json
llm
llm/custom.json
llm/deepseek.json
llm/local.json
llm/README.md
node
node/config.json
node/delete.json
node/event.json
node/keymapping.json
node/output.json
node/README.md
node/record.json
node/resource.json
relations
relations/declare.json
relations/list.json
relations/README.md
relations/undeclare.json
task
task/answer.json
task/delete.json
task/enqueue.json
task/psruntime.json
task/README.md
task/status.json
task/submit.json
taskcenter
taskcenter/addNode.json
taskcenter/createTask.json
taskcenter/deleteTask.json
taskcenter/findNode.json
taskcenter/getNodes.json
taskcenter/README.md
taskcenter/removeNode.json, controlFound=true, physicalAfter=, projection=written, error=null
[22:34:08.486] [INFO] [FrontEndDriven] ECS 状态写入物理投影 — controlName=sessionSelectorComboBox, entityId=85, value=default
tp_answer_test, controlFound=true, physicalAfter=, projection=written, error=null
[22:34:08.491] [INFO] [FrontEndDriven] ScopeLifecycleAdapter 渲染判据 — scope=Content, config=D:\Project\Program\WorkFlowPrj\FileDriven\DrivenConfig\JsonConfig\AgentRuntimeUI.json, renderSucceeded=true, rootAttached=true, hostActualChildCount=1, contentMounted=true, mountVisible=true, succeeded=true
[22:34:08.493] [INFO] [BackEndDriven] sideload 渲染成功 — scope=Content, config=D:\Project\Program\WorkFlowPrj\FileDriven\DrivenConfig\JsonConfig\AgentRuntimeUI.json, nodeCount=1, hostChildCount=1
[22:34:10.161] [INFO] [FrontEndDriven] ECS 事件输出决策 — opId=4030ceaf-9f6f-4418-90fe-420ae04f34ab, controlName=newSessionButton, functionName=AgentUi_ConversationNew, success=true, outcome=Completed, outputTarget=conversationStateTextBox, outputType=String, outputPreview=已新建/清空会话: default
[22:34:10.164] [INFO] [FrontEndDriven] ECS 状态写入物理投影 — controlName=conversationStateTextBox, entityId=93, value=已新建/清空会话: default, controlFound=true, physicalAfter=已新建/清空会话: default, projection=written, error=null
[22:34:10.368] [INFO] [FrontEndDriven] ECS 状态写入物理投影 — controlName=conversationInputTextBox, entityId=90, value=真实 LLM 测试：请用一句话介绍你自己, controlFound=true, physicalAfter=真实 LLM 测试：请用一句话介绍你自己, projection=written, error=null
[22:34:10.570] [INFO] [FrontEndDriven] ECS 状态写入物理投影 — controlName=sessionSelectorComboBox, entityId=85, value=real, controlFound=true, physicalAfter=, projection=written, error=null
[22:34:10.799] [INFO] [Agent.Core] [Agent] [RoundStepInvoker] 步骤开始: function=cline_execute
[22:34:10.816] [INFO] [StateBlock] [StateBlock] 池初始化: poolSize=3, idleTimeoutSec=60, warmupBudgetSec=180, infraRetries=1
[22:34:10.851] [INFO] [StateBlock] [StateBlock] Start_Spawned: {"poolSize":3,"workers":["worker-1","worker-2","worker-0"]}
[22:34:10.866] [INFO] [StateBlock] [StateBlock] EnsureReady: {"effectiveSec":299.9999404,"deadline":"2026-08-21T14:39:10.820023+00:00"}
[22:34:10.916] [INFO] [StateBlock] [StateBlock] Start_Done: {"running":["worker-1","worker-2","worker-0"],"ready":0}
[22:34:10.917] [INFO] [StateBlock] [StateBlock] WarmupCore_Start: {"ids":["worker-1","worker-2","worker-0"],"ready":0}
[22:34:10.921] [INFO] [StateBlock] [StateBlock] WarmSend: {"workerId":"worker-1","requestId":"warm-worker-1","aefBudgetSec":294.8998333}
[22:34:10.934] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-1, stage=engine-spawned, detail={"pid":38300}
[22:34:10.980] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-1, stage=engine-warmup-start, detail={"timeoutMs":289000}
[22:34:10.980] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-1, stage=warmup-start, detail={"timeoutMs":289000}
[22:34:10.981] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-1, stage=ensure-core-start, detail={"timeoutMs":289000,"cacheHit":false}
[22:34:12.881] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-1, stage=load-sdk-ok, detail=
[22:34:12.940] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-1, stage=ensure-core-ok, detail=
[22:34:12.940] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-1, stage=warmup-ok, detail=
[22:34:12.943] [INFO] [StateBlock] [StateBlock] daemon csg: worker=worker-1, stage=ready, form={"nodes":[{"id":"spawned","label":"Spawned","type":"Start","properties":{}},{"id":"loading","label":"Loading","type":"State","properties":{}},{"id":"ready","label":"Ready","type":"State","properties":{}},{"id":"api","label":"Api","type":"Action","properties":{}}],"edges":[{"id":"e_spawned_loading","sourceId":"spawned","targetId":"loading","label":"ensureCore","type":"Transition","condition":null,"weight":1,"properties":{}},{"id":"e_loading_ready","sourceId":"loading","targetId":"ready","label":"warmupOk","type":"Transition","condition":null,"weight":1,"properties":{}},{"id":"e_ready_api","sourceId":"ready","targetId":"api","label":"run","type":"Transition","condition":null,"weight":1,"properties":{}},{"id":"e_api_ready","sourceId":"api","targetId":"ready","label":"complete","type":"Transition","condition":null,"weight":1,"properties":{}}],"metadata":{"csgVersion":"1.0.0","source":"ClineLifecycleStateMachine","currentState":"ready"},"state":1}
[22:34:12.945] [INFO] [StateBlock] [StateBlock] WarmupCore: {"workerId":"worker-1","ok":true}
[22:34:12.945] [INFO] [StateBlock] [StateBlock] WarmSend: {"workerId":"worker-2","requestId":"warm-worker-2","aefBudgetSec":292.8741468}
[22:34:12.954] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-2, stage=engine-spawned, detail={"pid":40540}
[22:34:12.971] [INFO] [StateBlock] [StateBlock] 池就绪: ready=1, elapsedSec=2.1492277
[22:34:12.974] [INFO] [StateBlock] [StateBlock] 请求开始: requestId=req-1, worker=worker-1, cmd=ask, remainingSec=297.8468836
[22:34:12.976] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-1, stage=engine-run-start, detail={"cmd":"ask"}
[22:34:12.977] [INFO] [StateBlock] [StateBlock] daemon csg 迁移: worker=worker-1, state=api
[22:34:12.977] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-1, stage=run-start, detail={"mode":"act","provider":"deepseek","model":"deepseek-chat","baseUrl":"http....com","apiKey":"sk-c...3b56","clientType":"openai-compatible","timeoutSec":295,"cwd":"D:\\Project\\Program\\WorkFlowPrj\\TypeScriptPrj"}
[22:34:12.978] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-1, stage=load-sdk-start, detail=
[22:34:12.984] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-1, stage=phase1-ensure-core, detail={"timeoutMs":295000}
[22:34:12.984] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-1, stage=ensure-core-start, detail={"timeoutMs":295000,"cacheHit":true}
[22:34:12.985] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-1, stage=session-subscribe-ok, detail=
[22:34:12.986] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-1, stage=phase2-core-start, detail=
[22:34:13.004] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-2, stage=engine-warmup-start, detail={"timeoutMs":287000}
[22:34:13.004] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-2, stage=warmup-start, detail={"timeoutMs":287000}
[22:34:13.005] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-2, stage=ensure-core-start, detail={"timeoutMs":287000,"cacheHit":false}
[22:34:14.815] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-1, stage=phase2-core-start-ok, detail=
[22:34:14.817] [INFO] [StateBlock] [StateBlock] 请求完成: requestId=req-1, worker=worker-1, cmd=ask, elapsedMs=3998.0744
[22:34:14.942] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-2, stage=load-sdk-ok, detail=
[22:34:14.995] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-2, stage=ensure-core-ok, detail=
[22:34:14.996] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-2, stage=warmup-ok, detail=
[22:34:14.997] [INFO] [StateBlock] [StateBlock] daemon csg: worker=worker-2, stage=ready, form={"nodes":[{"id":"spawned","label":"Spawned","type":"Start","properties":{}},{"id":"loading","label":"Loading","type":"State","properties":{}},{"id":"ready","label":"Ready","type":"State","properties":{}},{"id":"api","label":"Api","type":"Action","properties":{}}],"edges":[{"id":"e_spawned_loading","sourceId":"spawned","targetId":"loading","label":"ensureCore","type":"Transition","condition":null,"weight":1,"properties":{}},{"id":"e_loading_ready","sourceId":"loading","targetId":"ready","label":"warmupOk","type":"Transition","condition":null,"weight":1,"properties":{}},{"id":"e_ready_api","sourceId":"ready","targetId":"api","label":"run","type":"Transition","condition":null,"weight":1,"properties":{}},{"id":"e_api_ready","sourceId":"api","targetId":"ready","label":"complete","type":"Transition","condition":null,"weight":1,"properties":{}}],"metadata":{"csgVersion":"1.0.0","source":"ClineLifecycleStateMachine","currentState":"ready"},"state":1}
[22:34:14.998] [INFO] [StateBlock] [StateBlock] WarmupCore: {"workerId":"worker-2","ok":true}
[22:34:14.998] [INFO] [StateBlock] [StateBlock] WarmSend: {"workerId":"worker-0","requestId":"warm-worker-0","aefBudgetSec":290.8214187}
[22:34:15.006] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-0, stage=engine-spawned, detail={"pid":43820}
[22:34:15.053] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-0, stage=engine-warmup-start, detail={"timeoutMs":285000}
[22:34:15.053] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-0, stage=warmup-start, detail={"timeoutMs":285000}
[22:34:15.054] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-0, stage=ensure-core-start, detail={"timeoutMs":285000,"cacheHit":false}
[22:34:16.869] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-0, stage=load-sdk-ok, detail=
[22:34:16.914] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-0, stage=ensure-core-ok, detail=
[22:34:16.914] [INFO] [StateBlock] [StateBlock] daemon trace: worker=worker-0, stage=warmup-ok, detail=
[22:34:16.915] [INFO] [StateBlock] [StateBlock] daemon csg: worker=worker-0, stage=ready, form={"nodes":[{"id":"spawned","label":"Spawned","type":"Start","properties":{}},{"id":"loading","label":"Loading","type":"State","properties":{}},{"id":"ready","label":"Ready","type":"State","properties":{}},{"id":"api","label":"Api","type":"Action","properties":{}}],"edges":[{"id":"e_spawned_loading","sourceId":"spawned","targetId":"loading","label":"ensureCore","type":"Transition","condition":null,"weight":1,"properties":{}},{"id":"e_loading_ready","sourceId":"loading","targetId":"ready","label":"warmupOk","type":"Transition","condition":null,"weight":1,"properties":{}},{"id":"e_ready_api","sourceId":"ready","targetId":"api","label":"run","type":"Transition","condition":null,"weight":1,"properties":{}},{"id":"e_api_ready","sourceId":"api","targetId":"ready","label":"complete","type":"Transition","condition":null,"weight":1,"properties":{}}],"metadata":{"csgVersion":"1.0.0","source":"ClineLifecycleStateMachine","currentState":"ready"},"state":1}
[22:34:16.916] [INFO] [StateBlock] [StateBlock] WarmupCore: {"workerId":"worker-0","ok":true}
[22:34:16.918] [INFO] [StateBlock] [StateBlock] 预热汇总: warmed=3, total=3
[22:36:15.872] [WARNING] [BackEndDriven] [Command] 等待超时: target=sendMessageButton, key=b84d6dde-13c0-49ec-b94e-da63b6e5926d, timeoutMs=125000
[22:36:16.079] [INFO] [BackEndDriven] [Command] 同步执行函数: function=AgentUi_GetLocalTestStatus
[22:36:16.098] [INFO] [FrontEndDriven] 增量渲染完成 — scope=, reason=function-executed, result=RenderRunResult { Scope = MainLayout, RootEntity = Entity(0:0), CollectedCount = 18, AppliedCount = 18, ElapsedMs = 16, RenderExecuted = True, MountedCount = 17, ContentMounted = 17, OrphanCount = 0, ControlsSkippedCount = 0, SkipReasons = System.Collections.Generic.List`1[System.String], HostActualChildCount = 1, Succeeded = True }
[22:36:16.099] [INFO] [BackEndDriven] [Command] 函数执行成功: function=AgentUi_GetLocalTestStatus, result=测试模式=OFF · 按配置 | 生效后端=semantic | 端点=http://127.0.0.1:8787/ | CallLog=0
[22:36:16.100] [INFO] [FrontEndDriven] ECS 状态写入物理投影 — controlName=statusLabel, entityId=26, value=测试模式=OFF · 按配置 | 生效后端=semantic | 端点=http://127.0.0.1:8787/ | CallLog=0, controlFound=true, physicalAfter=测试模式=OFF · 按配置 | 生效后端=semantic | 端点=http://127.0.0.1:8787/ | CallLog=0, projection=written, error=null
[22:36:16.423] [INFO] [Agent.Core] [Agent] [RoundStepInvoker] 步骤开始: function=answer
[22:36:16.425] [WARNING] [BackEndDriven] [AEF] 操作非正常终止 — name=AgentUi_ConversationSend:b84d6dde-13c0-49ec-b94e-da63b6e5926d, phase=TimedOut, elapsedMs=125650, error=操作 'AgentUi_ConversationSend:b84d6dde-13c0-49ec-b94e-da63b6e5926d' 超过绝对时限 00:02:00
[22:36:16.427] [WARNING] [BackEndDriven] [InvocationKernel] 函数框架态终止 — functionName=AgentUi_ConversationSend, outcome=FrameworkTimedOut, elapsedMs=125652, stages=Dispatch:0ms|FunctionStart:0ms, error=未在预算内完成（125652ms）
[22:36:16.427] [INFO] [FrontEndDriven] ECS 事件输出决策 — opId=b84d6dde-13c0-49ec-b94e-da63b6e5926d, controlName=sendMessageButton, functionName=AgentUi_ConversationSend, success=false, outcome=FrameworkTimedOut, outputTarget=conversationHistoryTextBox, outputType=(null), outputPreview=null
[22:36:16.429] [WARNING] [FrontEndDriven] ECS 事件函数执行失败 — controlName=sendMessageButton, eventName=click, functionName=AgentUi_ConversationSend, error=框架绝对超时: 函数 'AgentUi_ConversationSend' 未在预算内完成（125652ms）; stages=[Dispatch:0ms|FunctionStart:0ms]
[22:36:16.437] [INFO] [FileDriven.Execution] GUI 模式脚本执行完成: success=false, totalCommands=24, elapsedMs=138015
[22:36:16.442] [INFO] [BackEndDriven] [FrameworkExecutor] 工作流执行完成: Success=false, Time=145302ms

