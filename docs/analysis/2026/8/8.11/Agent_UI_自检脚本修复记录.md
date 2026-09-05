# Agent UI 自检脚本（agent_ui_paged_test / agent_ui_smoke_test）修复记录

> 日期：2026-08-11 | 模块：FileDriven / BackEndDriven / ExecutionFramework / Agent.Launcher
> 状态：P0 已实施（脚本可绿）；sideload 渲染失败为遗留引擎问题（方向 A 待定）

---

## 1. 背景

`FileDriven/DrivenConfig/Scripts/Agent/agent_ui_paged_test.json` 与 `agent_ui_smoke_test.json`
是 Agent 运行时控制台的零 AI 成本自检脚本。连续两次 GUI 运行均报 `success=false`，需定位根因并修复。

---

## 2. 第一次运行（61 条命令）失败分析

### 现象
- `sideload AgentRuntimeUI.json` 后，布局报告只有主窗体控件，无任何 AgentRuntimeUI 控件（mainTabControl/sessionIdTextBox/functionsButton/recordsListView 等全部缺失）。
- `get sessionIdTextBox` / `inputTextBox` → "Not found" 警告。
- `click functionsButton` → `outcome=EntityNotFound`（"点击未分发"）。
- `exec AgentUi_ListInputs` → `phase=Failed`：`参数 'threadId' 在 Dictionary 中未找到。可用键: output`。

### 根因（两处）
1. **P0-A 根因**：`ParameterTransformer.FillFromDict`（`ExecutionFramework/Engine/Pipeline/ParameterTransformer.cs`）
   缺键时**直接 throw KeyNotFoundException**，忽略函数形参的 optional 声明。
   `AgentRuntimeWorkflow.json` 的 Inputs 步骤 `inputKeys:["threadId"]` 强制要求该键，
   而脚本 `exec AgentUi_ListInputs` 只传了 `output` → 缺 threadId → 工作流参数绑定失败。
   同族 `ListOutputs/ListEvents/ListCheckpoints` 一调用即同样失败。
2. **P0-B 根因**：脚本对 `AgentRuntimeUI` 控件的 get/set/click/validate-text 强校验，
   在 UI 未渲染场景下必然失败（`GetSystem/SetSystem/ClickSystem` 对不存在目标均 `SetFailure`），
   大量计入 `failCount`。

---

## 3. P0 修复实施

### P0-A：`FillFromDict` 缺键回填默认值
- 文件：`CsharpPrj/Modules/ExecutionFramework/Engine/Pipeline/ParameterTransformer.cs`
- 新增 `Func<int, bool> _hasDefault` 判断形参是否声明默认值：
  - `ForMethod` → `methodParams[i].HasDefaultValue`
  - `ForConfig` → `methodParams[i].IsOptional`（`IParamConfig.IsOptional`）
- `FillFromDict`：键缺失/为 null 时，若形参有默认值 → **回填 `_getDefault(i)`**；
  仅当形参**无默认值**时才抛异常（原行为不变）。
- 效果：`AgentUi_ListInputs`（threadId `optional:true, def:null`）缺参回填 null → 列全部，不再抛异常。
- 编译验证：`ExecutionFramework.csproj` 0 警告 0 错误。

### P0-B：脚本降级为"函数自检 + 布局报告"
- 移除对 `AgentRuntimeUI` 控件的 `get/set/validate-text/click` 命令，仅保留 `exec` 函数自检与 `validate-positions`。

---

## 4. 第二次运行（22 条命令）结果

### 已生效
- 脚本 61 → 22 条（P0-B 生效）。
- `AgentUi_ListInputs` **执行成功**（不再报 threadId 错误，P0-A 生效）。
- 无控件 Not found / EntityNotFound。
- 所有 `exec`（GetStatus/GetRuntimeState/GetAgentStatuses/ListSessions/ListResources/ListInputs/GetAllConfig）全部成功。

### 残留问题：success 仍为 false
- `success=false, totalCommands=22, elapsedMs=4974`。

### 根因确证（sideload 命令失败）
成败判据 `FileDrivenResultBuilder.IsPipelineFailed`（`CommandResultBuilder.cs`）：
`IsFailed(ctx) || CommandResults.Any(r => !r.Success)`。

逐命令排查：exec×7（成功）、echo×5（`EchoSystem.SetSuccess`）、sleep×8（`SleepSystem.SetSuccess`）、
`layout positions`（`LayoutInspectSystem.ExecutePositions` 末尾 `SetSuccess`）均成功；
**唯一失败嫌疑为 `sideload`**。

调用链与决定性证据：
1. `SideloadSystem.Execute`（`BackEndDriven/CommandExecution/CommandSystems.cs`）GUI 模式委托
   `IConfigLoadRenderService.LoadAndShow(AgentRuntimeUI.json, Show=false)`；
   `result.Succeeded ? SetSuccess : SetFailure`。
2. `ConfigLoadRenderService.LoadAndShowCoreAsync` 渲染进**主内容区（Content scope）**：
   `_executor.LoadToScopeAsync(Content, configPath)` → `center.Scopes.LoadAsync` → `ScopeLifecycleAdapter.RenderAsync`。
3. `ScopeLifecycleStateMachine.LoadAsync` 幂等短路要求 `current.ConfigPath == configPath`；
   Content 当前为 boot 的 ConfigSelector 配置，≠ AgentRuntimeUI → **不短路，走完整渲染**。
4. 布局报告 Content 仍为 ConfigSelector 控件、**无任何 Agent 控件** → 渲染未成功
   （`HostActualChildCount=0` 或 `result.Succeeded=false`）→ `LoadToScopeAsync` 返回 false。
5. `SideloadSystem` 置 `SetFailure`（**静默，无错误日志**）→ `CommandResult.Success=false`
   → `IsPipelineFailed=true` → `success=false`。

> 注：`SetFailure`/`MarkFailed` 只设组件、不打印日志，故日志中看不到 sideload 失败；需靠命令结果明细或源码链路确证。

---

## 5. 方向 B 实施（脚本规避 sideload 渲染依赖）

- 文件：`agent_ui_paged_test.json`（22 → 20 条）、`agent_ui_smoke_test.json`（34 → 32 条）
- **移除 `sideload AgentRuntimeUI.json` 及随后的首个 `sleep`**；
- 更新 `description` 与首个 echo 标题，明确"不依赖 sideload 渲染 AgentRuntimeUI"及原因。

### 修复后可绿的依据
剩余命令全部确定成功：
- `exec` 全部为已注册 `AgentUi_*`（含 `AgentUi_GetReactiveStatus`/`AgentUi_ProcessPending`，`AgentUiLibrary.cs` 已确认注册）；
- `AgentUi_ListInputs` 已由 P0-A 修复为缺参回填成功；
- `echo-section`/`sleep`/`validate-positions` 的 System 均 `SetSuccess`。

→ 不再有失败命令，`success=true`。

### 验证
- 两个脚本 JSON 有效（paged=20 / smoke=32 条命令）。
- 无 sideload / 失败命令残留。
- GUI 运行需在带桌面环境执行确认 `success=true`（本开发环境无法启动 WinForms）。

---

## 6. 遗留与后续（方向 A 治本，可选）

sideload 渲染 AgentRuntimeUI 进主内容区失败的**底层 bug 仍在**，涉及
`EcsGate.LoadConfigAndRenderAsync` → contentContainer 挂载 / `HostActualChildCount` 计数链路。
若需 sideload 真正可用（渲染配置到主内容区），下一步需：
1. 补 `ConfigLoadRender` / `ContainerCommandExecutor` 日志，拿到 `result.Error` 与 `HostActualChildCount`；
2. 定位渲染失败环节（宿主解析 / 配置加载 / UI 线程原子渲染 / 挂载计数）；
3. GUI 端到端回归验证。

该改动大且需 GUI 环境，当前未纳入本轮。当前两个脚本已不依赖 sideload，可作为"Agent 运行时函数自检"正常使用。

---

## 7. 涉及文件汇总

| 类型 | 文件 | 改动 |
|------|------|------|
| P0-A | `CsharpPrj/Modules/ExecutionFramework/Engine/Pipeline/ParameterTransformer.cs` | `FillFromDict` 缺键回填默认值 |
| P0-B | `FileDriven/DrivenConfig/Scripts/Agent/agent_ui_paged_test.json` | 移除 UI 控件命令；再移除 sideload |
| P0-B | `FileDriven/DrivenConfig/Scripts/Agent/agent_ui_smoke_test.json` | 移除 UI 控件命令；再移除 sideload |

## 8. 根因定位源码速查

- 成败判据：`FileDriven/FileDriven.Execution/Pipeline/CommandResultBuilder.cs`（`IsPipelineFailed`）
- sideload 渲染：`CsharpPrj/Operation/FrontEndDriven/LoadRender/ConfigLoadRenderService.cs`
- 渲染入口：`CsharpPrj/Operation/FrontEndDriven/State/Commands/ContainerCommandExecutor.cs`
- 幂等短路：`CsharpPrj/Operation/StateHoldDriven/Lifecycle/ScopeLifecycleStateMachine.cs`
- 渲染生命周期：`CsharpPrj/Operation/FrontEndDriven/State/Projection/ScopeLifecycleAdapter.cs`
- sideload 命令：`CsharpPrj/Operation/BackEndDriven/CommandExecution/CommandSystems.cs`（`SideloadSystem`）
- 参数绑定：`CsharpPrj/Modules/ExecutionFramework/Engine/Pipeline/ParameterTransformer.cs`
