# GUI 脚本内容断言能力 —— 框架缺陷分析与优化（assert text-contains）

> **状态**：已实施（框架 + 脚本用例 + 日志注册 + 结果级 expectedContains） | **日期**：2026-09-04
> **关联**：`agent_mcpinvoke_create_task_test.json`（GUI 业务脚本难自动判定"完全通过"）、
> `BackEndDriven\CommandExecution\UiCommandSystems.cs`（`AssertSystem`/`ValidateSystem`）
> **触发**：mcp 创建任务脚本复跑显示 `success=true` 却 `Path A 疑似未激活`，且脚本无内建断言、只能人工对照输出 —— 暴露 GUI 脚本框架**缺"内容断言"能力**的缺陷。

---

## §0 一句话
GUI 脚本框架（BackEndDriven `AssertSystem`/`ValidateSystem`）的断言只覆盖**控件挂载/精确文本/坐标/容器计数**，
**缺少"对函数执行结果文本做包含断言"的能力**，导致业务类 GUI 脚本"命令全绿（success=true）但结果不达标"仍判成功、只能人工看输出。
本优化给 `AssertSystem` 新增子命令 `text-contains`（控件文本包含期望子串，失配 fail-fast），并在 mcp 脚本落地为自动校验用例。

---

## §1 框架现状与断言能力盘点

### 1.1 脚本执行链路
```
GUI 脚本 *.json → FileDriven.Utilities.ScriptRunner（ScriptCommand/ToCommandText）
    → MetaCommand 命令行 → BackEndDriven.CommandExecution.UiCommandSystems
        ├─ AssertSystem    （verb=assert：event-bound / openforms / text-empty / container）
        ├─ ValidateSystem  （verb=validate：控件 expectedText 精确相等 / 坐标 / 挂载可见）
        └─ DiscoverSystem 等
exec AgentUi_* → BackEndDriven 函数执行，返回值写 output 控件（statusLabel / resultTextBox）
```

### 1.2 已有断言能力（`UiCommandSystems.cs`）
| 系统/子命令 | 判据 | 局限 |
|---|---|---|
| `AssertSystem.text-empty` | 控件文本**为空** | 仅空判定 |
| `AssertSystem.container` | 宿主容器直接子控件数（count>0 等） | 仅容器结构 |
| `AssertSystem.event-bound` / `openforms` | 事件绑定 / 窗体数 | UI 结构 |
| `ValidateSystem`（`expectedText`） | 控件文本 **精确相等** | 含动态片段（时间/计数/拼接）即失配 |

### 1.3 缺陷（缺口）
1. **无"内容包含"断言**：`exec` 把函数返回值写入控件，但脚本无法断言其**包含**某关键字（如
   `TaskPoolGetStatus` 的 `Completed`、`DbRead ur_tasks` 表是否含某 `taskKey`）。
   `ValidateSystem` 是精确相等，对 `"tp_mcpinvoke_ctrl -> Completed"` 这类动态整段文本不可直接断言。
2. **业务假阳性**：`exec` 命令本身"函数返回即 success"（含"未找到任务 X（无删除）"也 success），
   脚本无内容断言 → `totalCommands` 全执行完、`success=true`，但业务未达标（如任务未激活）仍被判"绿"。
3. 结果只能 `get` 输出给人工对照 —— 与"自动判定通过/失败"目标相悖。

> 反模式对应：`@ ANTI-EMPTY-IGNORED` / 结构 `FormatFidelity` —— 产物存在 ≠ 内容正确，需显式内容校验闭环。

---

## §2 优化设计

### 2.1 目标能力
`assert` 子命令新增 **`text-contains`**：断言某控件当前文本**包含**期望子串（忽略大小写）。
- 定位：读函数返回所写入的控件文本（如 `statusLabel` / `resultTextBox`），验证是否含关键业务标记。
- 语义：失配即 `SetFailure`（fail-fast）→ 脚本 `HasErrors` 终止，**杜绝假绿**；空期望拒绝（防"恒真"静默通过）。
- 语法：`assert text-contains <controlName> <expectedSubstring...>`
  （控件名无空格；期望可含空格，取首空格后整段）。
- 与 `ValidateSystem` 差异：`text-contains` 是**包含**，用于含动态片段的整段文本；`validate expectedText` 保留**精确相等**语义。

### 2.2 实现位置
`CsharpPrj\Operation\BackEndDriven\CommandExecution\UiCommandSystems.cs` → `AssertSystem`
- `Execute` switch 增 `case "text-contains": AssertTextContains(entity, meta);`
- 新私有方法 `AssertTextContains`：`ExtractSubTarget(meta,"text-contains")` 取 rest →
  首空格切 `controlName`/`expected` → `_controlFinder.FindByName<Control>` → `Text.IndexOf(expected, OrdinalIgnoreCase)>=0` 判定。
- 复用既有 `ExtractSubTarget` / `FindByName` / `SetSuccess` / `SetFailure` / `BackEndLog`，零新依赖。

---

## §3 应用用例：mcp 创建任务脚本自动校验

`FileDriven\DrivenConfig\Scripts\Agent\agent_mcpinvoke\agent_mcpinvoke_create_task_test.json`（69→75 命令）新增 4 处 `text-contains` 自动断言 + 1 处结果级 `expectedContains`：

```jsonc
// [B4] Path B 对照任务须确定性收敛为 Completed（本任务 D4 修复的验收点）
{ "action": "exec",  "target": "AgentUi_TaskPoolGetStatus", "value": "taskKey=tp_mcpinvoke_ctrl", "output": "statusLabel" },
{ "action": "sleep", "target": "3000" },
{ "action": "assert", "target": "text-contains statusLabel tp_mcpinvoke_ctrl" },   // 须入池
{ "action": "assert", "target": "text-contains statusLabel Completed" },           // 须 Completed（失配即 fail-fast）

// [V1a] ur_tasks 须含两任务 —— 缺 tp_llm_mcp_demo 即显性化 Path A 未激活（而非假绿）
{ "action": "assert", "target": "text-contains resultTextBox tp_mcpinvoke_ctrl" },
{ "action": "assert", "target": "text-contains resultTextBox tp_llm_mcp_demo" },
```

效果：脚本能**自动判定** Path A/B 任务是否真的创建并完成；任一缺失 → fail-fast 红，代替人工读表对照。
另有 V1 读采用**结果级 `expectedContains=tp_mcpinvoke_ctrl`**（§6.1 框架能力，对 DB 读函数返回文本直接断言，Path B 确定性必在），与控件级 `text-contains` 双轨演示。

---

## §4 验证与结论
- `dotnet build BackEndDriven.csproj`：**0 警告 0 错误**（含 ExecSystem 结果级 `expectedContains`）。
- `dotnet build FileDriven.Utilities.csproj`：**0 警告 0 错误**（ScriptCommand `expectedContains` 字段接线）。
- 脚本 JSON `ConvertFrom-Json` 校验通过（75 命令；4 条 `assert text-contains` + 1 条 `exec expectedContains` 正确解析）。
- 需 GUI 复跑确认（见 §5 待确认项——本环境无法运行 WinForms GUI 端到端）。

---

## §5 风险 / 待确认
| # | 项 | 说明 / 缓解 |
|---|----|------|
| ① | `exec output=` 是**覆盖**还是**追加**控件文本 | 追加累积仍可能使控件级 `text-contains` 假阳；**结果级 `expectedContains`（§6.1 本次实施）已下沉到函数返回值、不依赖控件覆盖语义**，从源头规避该类依赖；控件级断言仍建议用独立专用校验控件 |
| ② | `MetaCommand` 对 `target` 含空格子串的保留 | `AssertSystem.container count>0` 已有同形态先例（空格保留于 subAction），本优化沿用该形态，风险低；仍以 GUI 复跑为准 |
| ③ | `Path A`（LocalLlm 决策创建）若当前未激活 | 新增 `tp_llm_mcp_demo` 断言将令脚本**显性红**（揭示待修项），非框架缺陷——恰是"完善校验"目标 |
| ④ | 日志消息定义未注册 | **已解决**：`Command_Assert_TextContains` + `Command_Exec_ExpectedContains_Match/_Mismatch` 均已注册进 `backEnd_log_messages.json`，不再打 WARN |

---

## §6 可选后续
> **本次已实施 ① 结果级 `expectedContains`**：`ExecSystem` 增命令级 `expectedContains`（对函数**返回值**文本做包含断言，
> 忽略大小写、空期望拒绝、失配 `SetFailure` fail-fast——与 `assert text-contains` 语义一致但免依赖控件覆盖/追加语义）。
> - 框架语法：`exec <function> ... output=<ctl> expectedContains=<substring>`（`expectedContains` 为命令级元数据，同 `output`/`timeout`，不入函数参数）。
> - 脚本接线：`ScriptCommand` 增 `expectedContains` json 字段，并接线 `ToMetaCommand`/`ToCommandText` 两路径。
> - 日志：已注册 `Command_Exec_ExpectedContains_Match/_Mismatch`。
> - 落地示例：§3 V1 exec（`expectedContains=tp_mcpinvoke_ctrl`）。
> 仍未做（保留为后续可选）：
2. `ValidateSystem` 增加包含模式（如 `expectedText` + `op=contains`），统一 validate 家族。
3. 用 `text-contains` 推广到其它业务 GUI 脚本（会话/任务池/Db 一致性），消除"人工看表"范式。
