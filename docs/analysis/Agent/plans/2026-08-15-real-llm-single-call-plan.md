# 真实 LLM 单次调用 — 实施计划（供新对话执行）

> **日期**：2026-08-15
> **目标**：在 WorkFlowPrj 框架内触发一次**真实 LLM 单次调用**（非任务池异步、非 Agent 多轮循环），并回读结果。
> **一句话方案**：走修复后的 GUI 通道，先 `Cline_PoolReady` 预热 Cline daemon 池，再经脚本 `exec Cline_Ask` 发起单次 ask 调用，结果经 BackEndDriven 标准日志经命名管道回读。
> **依赖前置**：`交互启动.bat` 崩溃已修复；`运行测试GUI模式.ps1` 可用；Cline node daemon 已构建且有凭据。

---

## 0. 术语与调用链

**"真实 LLM 单次调用"** = `Cline_Ask(prompt, model, provider, timeoutSec)`（ask 模式，单次无状态补全）。`Cline_Plan` 为带工具的规划模式（非单次），`runLoop mode=Hybrid` 为 Agent 多轮循环——三者都不是本目标，除非明确。

**调用链（外部 → LLM）**：
```
交互启动.bat（管道 JSON 或脚本参数）
  └─ 运行测试GUI模式.ps1（读 FILEDRIVEN_INSTANT_BODY / 脚本名）
      └─ FileDriven.Launcher.exe --show-gui   ← 必须 GUI 宿主（headless CLI 无 IEventPlatform，跑不了 Cline）
          └─ 脚本 exec Cline_Ask ... output=resultTextBox
              └─ FunctionStorage.Registrations.ClineLibrary（Cline_Ask 委托）
                  └─ Agent.Modules.ClineClient.AskAsync
                      └─ ClineDaemonPool（node daemon 预热 + IPC）
                          └─ @cline/sdk ClineCore → LLM API（deepseek）
  结果 ── BackEndDriven 日志"函数执行成功: result=…"──▶ 命名管道 ──▶ ps1 stdout ──▶ 调用方
```

---

## 1. 涉及文件目录（权威路径）

| 用途 | 绝对路径 |
|------|----------|
| 启动器（已修复崩溃） | `D:\Project\Program\WorkFlowPrj\EnableMent\交互启动.bat` |
| 运行器 | `D:\Project\Program\WorkFlowPrj\PowerShellScript\FileDriven\FileDriven\运行测试GUI模式.ps1` |
| Cline 函数注册库（Ask/Plan/PoolReady 签名源） | `D:\Project\Program\WorkFlowPrj\FileDriven\FileDriven.Business\Registrations\Cline\ClineLibrary.cs` |
| Cline 客户端 / daemon 池 | `D:\Project\Program\WorkFlowPrj\Agent\src\Agent.Modules\Cline\ClineClient.cs`、`Agent\src\Agent.Modules\Cline\Pool\ClineDaemonPool.cs` |
| Cline 配置（默认 provider/model/timeout） | `D:\Project\Program\WorkFlowPrj\TypeScriptPrj\TsWorkspace\ClineApi\cline.config.json` |
| 集中凭据（provider/model/apiKey） | `D:\Project\Program\WorkFlowPrj\credentials\cline.json` |
| 现有 Cline 脚本（零成本冒烟 / 真实 UI 调用） | `D:\Project\Program\WorkFlowPrj\FileDriven\DrivenConfig\Scripts\Agent\Cline\cline_smoke_test.json`、`cline_browser_test.json` |
| **新对话将新建**最小单次调用脚本 | `D:\Project\Program\WorkFlowPrj\FileDriven\DrivenConfig\Scripts\Agent\Cline\cline_ask_min_test.json` |
| 模板（字段权威） | `D:\Project\Program\WorkFlowPrj\FileDriven\DrivenConfig\Templates\AgentRun\` |
| 即时指令技能 | `D:\Project\Program\WorkFlowPrj\FileDriven\Structure\skills\05-instant-command.md` |
| 诊断日志 | `%TEMP%\fileDriven_launcher.log` |

---

## 2. 前置核查（零 AI 成本）

1. **凭据存在**：`D:\Project\Program\WorkFlowPrj\credentials\cline.json` 存在（默认 provider 键 `deepseek_`，model `deepseek-v4-flash`）。
2. **Cline 配置**：`TypeScriptPrj\TsWorkspace\ClineApi\cline.config.json`（`provider=deepseek`、`model=deepseek-v4-flash`、`credentialsFile=../../../credentials/cline.json`）。
3. **Cline node 已构建**：`D:\Project\Program\WorkFlowPrj\TypeScriptPrj\TsWorkspace\ClineApi\build\cli.js` 存在；缺失则先 `pnpm install && pnpm build`（在 `TypeScriptPrj\TsWorkspace\ClineApi`）。
4. **零成本冒烟**（GUI）：`& "D:\...\运行测试GUI模式.ps1" "Agent\Cline\cline_smoke_test.json"`，确认凭据加载、Cline CLI 版本、配置、历史正常。此步验证宿主/凭据/CLI 全链路，**不消耗 AI 额度**。

---

## 3. 新建最小脚本 `Scripts\Agent\Cline\cline_ask_min_test.json`

```json
{
  "description": "真实 LLM 单次调用最小测试：预热 Cline daemon 池 → Cline_Ask 单次调用 → 回读结果（消耗额度）。",
  "script": "cline_ask_min_test",
  "commands": [
    { "template": "echo-section", "title": "[1] 预热 Cline daemon 池（冷启动移出请求路径）" },
    { "action": "exec", "target": "Cline_PoolReady", "output": "resultTextBox", "timeout": 240000 },
    { "action": "sleep", "target": "400" },

    { "template": "echo-section", "title": "[2] 真实 LLM 单次调用（Cline_Ask，消耗额度）" },
    { "action": "exec", "target": "Cline_Ask",
      "value": "prompt=用一句话介绍 Cline 编码代理，只返回这句话。 model= provider= timeoutSec=60",
      "output": "resultTextBox", "timeout": 120000 },
    { "action": "sleep", "target": "600" },
    { "action": "get", "target": "resultTextBox" },

    { "template": "echo-section", "title": "真实 LLM 单次调用测试完成" }
  ]
}
```
> 说明：`Cline_Ask` 形参 `(prompt, model, provider, timeoutSec)`；`model`/`provider` 留空回退 `cline.config.json` 默认（deepseek / deepseek-v4-flash）；`timeoutSec=60` 收紧默认 20 分钟超时；`output=resultTextBox` 使结果经 BackEndDriven 标准日志回显。

---

## 4. 执行

**方式 A（推荐·脚本）**：GUI 宿主运行（headless CLI 无 `IEventPlatform`，跑不了 Cline）
```powershell
& "D:\Project\Program\WorkFlowPrj\PowerShellScript\FileDriven\FileDriven\运行测试GUI模式.ps1" "Agent\Cline\cline_ask_min_test.json"
```

**方式 B（即时指令，复用已修复 bat）**——仅用于验证通道；即时指令只支持 `AgentUi_RunJson` action，**无法直调 `Cline_Ask`**，故真正单次调用用脚本方式 A。
```powershell
$fd = 'D:\Project\Program\WorkFlowPrj\EnableMent\交互启动.bat'
'{"action":"listResources"}' | & $fd   # 验证通道（零成本）
```

---

## 5. 验证（结果回读）

- 预期在 stdout 出现 BackEndDriven 日志：`函数执行成功: function=Cline_Ask, result=<LLM 返回的一句话>`。
- **成功判据**：`result` 为非空、无 `调用失败`/`超时`/`IEventPlatform 未注册`；退出码 0；`[完成] 测试执行结束`。
- **回读通道**：结果写入 `resultTextBox` 并经命名管道回传（`LOG_OUTPUT_MODE=console` 时标准日志含 `result=`）；诊断日志在 `%TEMP%\fileDriven_launcher.log`。

---

## 6. 已知约束与坑（新对话必读）

| 项 | 说明 |
|----|------|
| **必须 GUI 宿主** | headless `-Headless`/CLI 宿主未装配 `IEventPlatform`，`Cline_Ask` 会报 `IEventPlatform 未注册到 AppServiceProvider`（实测）。Agent/Cline 动作一律走 GUI 宿主（bat 默认）。 |
| **耗额度/耗时** | 真实调用消耗 API 额度，默认超时 20 分钟；建议 `timeoutSec=60`。 |
| **冷启动** | 首次需 `Cline_PoolReady` 预热（3-daemon 并行预热较慢）；不预热直接调用有挂起风险。 |
| **daemon 池已知坑** | 探活 pong 终结关联 / 挂起有历史修复记录：`Agent/docs/analysis/2026/8/8.8-Cline初始化挂起根因与架构修复方案.md`、`2026/8/8.7-Cline传输层挂起根因分析与重构方案.md`。若挂起，检查 `%TEMP%\fileDriven_launcher.log` 与 daemon 诊断。 |
| **Cline node 未构建** | `build/cli.js` 缺失时首次调用抛友好错误；先构建。 |
| **结果回显机制** | 走框架标准日志管线（`LOG_OUTPUT_MODE=console`），非 ad-hoc Console.WriteLine；本会话已通用化 `WriteToTarget` 并新增 `WriteResult_Ok` 日志（不影响 Ask 路径，Ask 结果走 BackEndDriven 日志）。 |

---

## 7. 本次会话已完成的相关修复（供上下文）

- `EnableMent\交互启动.bat`：修复 `for /f do(...)` 括号块崩溃（P0，原即时指令通道完全不可用）。
- `PowerShellScript\...\运行测试GUI模式.ps1`：新增 `-Headless`（CLI 虚拟控件；注意 Agent/Cline 动作仍须 GUI 宿主）。
- `CsharpPrj\Operation\WorkFlowMethod\WorkflowExecution\WorkflowMethodExecutionWorkflow.cs` + `workflowmethod_log_messages.json`：`WriteToTarget` 通用化，成功写入即发标准 `WriteResult_Ok` 日志。已重建 `FileDriven.sln` 通过。

