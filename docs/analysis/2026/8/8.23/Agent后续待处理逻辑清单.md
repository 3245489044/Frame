# Agent 后续待处理逻辑清单

> 目的：登记 `Projects/Agent` 框架层面已识别、但**尚未实施**的重复/冗余/错误逻辑与设计优化项，供后续分批推进。
> 状态说明：`⭐ 已修复` 表示本清单来源批次中已落地；以下均为**待处理**项。
> 变更记录：本文件建立于 2026-08-23，基于源码核查 + 2026-08-23 复核报告 + 框架缺陷清单梳理。

---

## 一、错误逻辑（应修复）

### E1.【中】`McpClientManager.Dispose` 同步阻塞线程池（部分修复）
- **位置**：`src/Agent.Modules/MCP/McpClientManager.cs` `Dispose()`。
- **现状**：已实现 `IAsyncDisposable.DisposeAsync`（await `DisconnectAsync`），并由 `AgentOptionalModules` 经 `AsyncDisposeAdapter` 接入编排器释放链。
- **残留**：同步 `Dispose()` 仍保留 `Task.Run(...).GetAwaiter().GetResult()` 的阻塞兜底（同步调用方仍会阻塞线程池线程，最坏约 10s shutdown + 5s WaitForExit）。
- **待处理**：将所有同步 `Dispose` 调用点迁移到 `DisposeAsync`/`AsyncDisposeAdapter`，或标注 `Dispose()` 仅作兜底并明确文档。
- **优先级**：P2

### E2.【中】`PromptValidator` 同步阻塞（已修复）
- **位置**：`src/Agent.Axioms/Prompts/PromptValidator.cs`。
- **现状**：`ValidateRequired` → async `ValidateRequiredAsync`，`TryAcceptL1/L2` → async 并 await，调用点（`PromptFrameworkFunction.cs`）已同步 await。✅ 已修复，留档防重复申报。

### E3.【低】`GuardFunction` 敏感信息子串误报（待处理）
- **位置**：`src/Agent.Guardrails/GuardFunction.cs` `CheckOutputSafetyAsync`。
- **问题**：用 `IndexOf("password")`/`IndexOf("secret")` 子串匹配，合法文本（含 `secret` 的字段名/文档）会被误拦截；且 `CheckPatternsAsync` 是假 async（纯同步却声明 Task）。
- **待处理**：改为词边界匹配，或明确声明局限；`CheckPatternsAsync` 去掉 `async` 形态（改同步返回）。
- **优先级**：P3

---

## 二、重复逻辑（应收敛）

### D1.【中】提示词三解析器重复文本扫描原语，且健壮性不一致
- **位置**：
  - `src/Agent.Operation/Prompts/FormatSpecParser.cs` `FindMatchingBrace`（朴素平衡括号，遇 `"{...}"` 字面量误判边界）
  - `src/Agent.Operation/Prompts/AgentReturnSpecParser.cs` `ExtractLastJsonObject`（带字符串转义感知，C6 已修边界 bug）
  - `src/Agent.Operation/Prompts/PromptDslParser.cs` `ReadFencedBlock`（围栏扫描）+ 三处 `Split(new[]{"\r\n","\n","\r"})` 行切分
- **问题**：两处平衡花括号实现逻辑不一致（朴素版 vs 转义感知版），行切分三处重复。
- **待处理**：抽共享 `TextScan` 工具（`SplitLines` / `FindMatchingBrace(string, open, skipStrings)` / `ExtractFenced`），统一取转义感知版，三解析器复用。
- **优先级**：P2

### D2.【低】8 个日志门面薄壳类重复（已部分收敛）
- **位置**：`src/*/Diagnostics/` 下 `AgentLog/CalculateLog/GuardLog/LauncherLog/ModuleLog/OperationLog/RuntimeLog/BridgeLog`。
- **现状**：均已委托共享 `ModuleLogFacade`（`Agent.Contract/Diagnostics`），仅 `ModuleName`/`KeyPrefix` 不同，重复度已降低。
- **待处理**：可收敛为「单参数化门面 + `LoggingStage` 表驱动 `(ModuleName, KeyPrefix, file)`」，8 类删除或降为薄委托。
- **优先级**：P3

### D3.【低】LLM 门面层叠（已部分收敛）
- **位置**：`LlmGateway`（`Agent.Runtime/State/Infrastructure`）与 `LlmClientResolver`（`Agent.Modules/Invoke/Resolution`）。
- **现状**：`LlmClientResolver.Current` 冗余转发已移除，现仅剩 `Resolve(provider)`（缺省=网关、显式 provider=工厂例外），两层尚可接受。
- **待处理**：确认 `LlmClientResolver` 命名与语义（"仅显式 provider 例外"），避免新消费方误当通用入口。
- **优先级**：P3

---

## 三、冗余逻辑（应清理）

### R1.【中】死代码 `IsTaskComplete` / `CompressHistory` / `ShouldCompressContext`
- **位置**：`src/Agent.Runtime/State/Loops/AgentLoopFunction.cs`（`:37/69/76`）。
- **现状**：全仓（含 CsharpPrj/FileDriven）**无生产调用方**，仅被测试绑定（`Agent.Core.Tests` 的 `DynamicWorkflowCoreTests` / `EndToEndIntegrationTests`）。`JsonShim.cs` 注释仍称其为"被收敛的重复前导"。
- **待处理**：二选一——
  - a) 接入生产消费链（如压缩历史真被使用），消除"只测不用"；
  - b) 删除方法 + 同步删除对应测试方法。
- **注意**：涉及测试存废，需开发者决策。
- **优先级**：P2（需决策）

### R2.【中】`AgentLogCategoryEx` 位宽/聚合不合理
- **位置**：`src/Agent.Axioms/Diagnostics/AgentLogCategoryEx.cs`。
- **问题**：`ulong` 位宽 + `Agent_All` 聚合值，但仅作分类参数（非位运算），且需 `(LogCategory)` 强转。
- **待处理**：改普通 `int` 枚举，移除 `Agent_All`。
- **优先级**：P3

### R3.【低】`SemanticKernelLlmClient.Instance` 双层静态单例 + 惰性自建兜底
- **位置**：`src/Agent.Axioms/SemanticKernel/SemanticKernelLlmClient.cs` `Instance`。
- **问题**：`Instance` 同时承担"读工厂"与"惰性自建 + 自注册"，正式路径由组合根注入，`Instance` 兜底可能触发隐式后端创建，破坏"后端选择唯一收敛于网关"。
- **待处理**：`Instance` 收敛为只读组合根产物，移除惰性自建分支。
- **优先级**：P3

### R4.【低】`RunAsync` 4 重载 API 面冗余
- **位置**：`src/Agent.Core/Loops/AgentRunner.cs`（4 个 `RunAsync` 重载：string×2 + item×2）。
- **问题**：本质是 `CreateItem` 参数排列；string 输入两重载生产无调用方（仅直跑/测试）。
- **待处理**：收敛为单一 `RunAsync(string, TimeSpan? timeout = null, ...)` + 保留 `RunAsync(AgentTaskItem, ...)`；需先核对 `AgentUiLibraryRuntime` 的 Runtime 接口调用面。
- **优先级**：P3（公开 API，需评估）

---

## 四、设计优化项（框架层）

### F1.【高】两层嵌套编排管线职责重叠（缺陷二）
- **位置**：
  - 外层：`AgentRunner.BuildRoundPipeline`（`pre_round → lease_derive → run_round → post_round → wrap`）
  - 内层：`WorkflowRunner`（`guard_input → provide_prompt → cline_execute → rule_match → guard_output`）
- **问题**：两层都做"声明式编排 + IsFatal 截断 + 逐阶段计时 + 决策词处理"；异常要穿透两层才到 `ExecutePipelineRoundAsync` 的 `throw FirstFatalError`；外层 `post_round`（逻辑边界）与内层 `rule_match`（决策词）职责边界模糊。
- **建议**：外层只保留"租约派生/收割 + 边界钩子"（其语义 `RoundLifecycle` 五步已承载），把 `post_round` 的边界评估并入单一 `onRoundCompleted` 钩子，减少一层编排包装。
- **优先级**：P1（改动面大，建议先出方案）

### F2.【中】`AgentRunner` 职责过载（缺陷三）
- **位置**：`src/Agent.Core/Loops/AgentRunner.cs`。
- **问题**：兼组合根、调度智能体、单轮管线装配者、状态发布端；`_mapper = new LoopDecisionMapper()` 自行 new 与外部注入骨架路径（`_runner` 走 StateCenter）名实略符。
- **建议**：`_mapper` 纳入 `StateCenter` 注入路径，使装配输入统一；将"组合根装配"与"运行调度"拆分。
- **优先级**：P2

### F3.【低】`NextHybridRoundAsync` + `FinalizeRunAsync` 末轮/终态双写库
- **位置**：`AgentRunner.cs` `NextHybridRoundAsync`（每轮边界 PersistRuntimeAsync）+ `FinalizeRunAsync`（终态 PersistRuntimeAsync）。
- **问题**：同轮可能写两次 PayloadJson（设计如此，性能低优先）。
- **待处理**：确认写入幂等即可，无需强改。
- **优先级**：P3

### F4.【低】`GlobalUsings.cs` 全局别名掩盖外部契约来源
- **位置**：`src/GlobalUsings.cs`。
- **问题**：把大量 `Share.TaskStatusPool` 类型 `global using` 到每项目（每项目 `<Compile Link>` 一份），掩盖真实来源 + 跨项目重复维护。
- **待处理**：作为兼容过渡层管理，标注生命周期；后续迁往显式 using。
- **优先级**：P3

---

## 五、优先级汇总

| 优先级 | 项 | 类别 | 位置 |
|---|---|---|---|
| **P1** | 两层嵌套编排管线去重（F1） | 设计优化 | AgentRunner / WorkflowRunner |
| **P2** | 死代码存废决策（R1） | 冗余 | AgentLoopFunction.cs |
| **P2** | `McpClientManager.Dispose` 阻塞调用点迁移（E1） | 错误 | McpClientManager / 调用方 |
| **P2** | 三解析器文本扫描原语抽 `TextScan`（D1） | 重复 | Operation/Prompts/* |
| **P2** | `AgentRunner` 职责过载收敛（F2） | 设计优化 | AgentRunner.cs |
| **P3** | `GuardFunction` 子串误报 + 假 async（E3） | 错误 | GuardFunction.cs |
| **P3** | 8 日志门面薄壳收敛（D2） | 重复 | Diagnostics/* |
| **P3** | `AgentLogCategoryEx` 位宽清理（R2） | 冗余 | AgentLogCategoryEx.cs |
| **P3** | `SemanticKernelLlmClient.Instance` 兜底移除（R3） | 冗余 | SemanticKernelLlmClient.cs |
| **P3** | `RunAsync` 4 重载收敛（R4） | 冗余 | AgentRunner.cs |
| **P3** | LLM 门面命名收敛（D3） | 重复 | LlmClientResolver.cs |
| **P3** | `GlobalUsings.cs` 生命周期标注（F4） | 设计优化 | GlobalUsings.cs |

---

## 六、已确认修复项（留档防重复申报）

| 项 | 状态 |
|---|---|
| `PromptValidator` 同步阻塞 async（E2） | ✅ 已修复 |
| `McpClientManager` 实现 `IAsyncDisposable` + `DisposeAsync` | ✅ 已修复 |
| `IWorkflowRunner.GetNodeOutputsAsync` 死代码删除 | ✅ 已修复 |
| 运行时快照双写版本串分裂（`RuntimeStage` 受控命名） | ✅ 已修复 |
| 持久化失败静默吞（`RunFailed` 事件行） | ✅ 已修复 |
| `AgentRunCancellation` 注释引用不存在方法 | ✅ 已修复 |

---

*建立时间：2026-08-23。行号基于分析时点源码，随仓库演进可能偏移，实施前以对应文件为准。*

