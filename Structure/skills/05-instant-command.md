---
name: instant-agent-command
description: WorkFlowPrj 通用技能 — 经 EnableMent\交互启动.bat 用「即时指令（Raw-Body）」直接投喂 Agent JSON 请求体并执行，无需编写 Scripts\ 脚本文件。请求体含 action（task/runLoop/resource/reference 任一）且无 commands 即被自动识别为即时指令；承载通道为管道输入（经环境变量 FILEDRIVEN_INSTANT_BODY）或位置参数。
---

# Skill: 即时指令（Raw-Body）— 经交互启动.bat 直接投喂 Agent 请求体

> **目标**：把 JSON 请求体（`action=task/runLoop/resource/reference`）**直接**喂给 `EnableMent\交互启动.bat` 执行，不经脚本文件、不写 `Scripts\` 目录。LLM/脚本只需把请求体丢进管道即可触发 Agent 运行时（`AgentUi_RunJson`）执行。
> **一句话机制**：参数**含 `action` 且无 `commands`** → 运行器识别为即时指令 → 合成单命令 `exec AgentUi_RunJson requestJson=<body>` → 复用既有 Launcher/管道/结果回读管线执行。

---

## 0. 适用判定（先读）

| 输入 | 判别 | 走向 |
|------|------|------|
| JSON 请求体，**含 `action` 且无 `commands`** | 即时指令（Raw-Body） | 合成单命令执行 |
| JSON 脚本，含 `commands` | 脚本文件 | 走既有 ScriptRunner 命令序列 |
| 以 `{`/`[` 开头但 JSON 解析失败 | 畸形请求体 | 输出解析诊断（提示转义/引号问题） |
| 其它（脚本路径 / 序号 / 空） | 既有逻辑 | 脚本 / 数字预处理 / 交互菜单 |

> **通用性**：`action` 不限 `task`——`runLoop` / `task` / `addResource` / `getResource` / `removeResource` / `listResources` / `addReference` / `removeReference` / `listReferences` 全部支持（`AgentUi_RunJson` 的既有 action 白名单，见 `Agent/src/Agent.Launcher/Modules/AgentUiLibrary.cs`）。`task` 内部再按 `op`（enqueue/status/delete/submit）分流。
> **前提**：`EnableMent\交互启动.bat` 经 `运行测试GUI模式.ps1` 启动 Agent 运行时（GUI 宿主）；未装配任务池/数据库时各动作返回对应诊断，不消耗 AI 额度。

---

## 1. 快速上手（5 秒跑通）

```powershell
# ① 预置启动器路径变量（以下所有示例复用）
$fd = 'D:\Project\Program\WorkFlowPrj\EnableMent\交互启动.bat'

# ② 最小即时指令：列出全部资源
'{"action":"listResources"}' | & $fd
```

---

## 2. 承载通道

### 2.1 管道输入（推荐，最通用）

> 规避命令行引号/空格拆分问题。管道内容经 `交互启动.bat` 捕获，若以 `{` 开头则**仅**经环境变量 `FILEDRIVEN_INSTANT_BODY` 传入 `运行测试GUI模式.ps1`；`运行测试GUI模式.ps1` 在无命令行脚本参数时将其作为即时指令的请求体。

```powershell
$fd = 'D:\Project\Program\WorkFlowPrj\EnableMent\交互启动.bat'
'{"action":"<Action>","<key>":"<value>"}' | & $fd
```

### 2.2 位置参数（简短 body 用）

> 适合不含内部引号/空格的简短 body（如 `{"action":"listResources"}`）。含内部引号或空格的 body 会被 cmd 拆分，请改用管道。

```powershell
$fd = 'D:\Project\Program\WorkFlowPrj\EnableMent\交互启动.bat'
& $fd '{"action":"listResources"}'
```

### 2.3 既有脚本照常

```powershell
$fd = 'D:\Project\Program\WorkFlowPrj\EnableMent\交互启动.bat'
& $fd 'Agent\agent_task_node_loop_test.json'
```

---

## 3. 通用模板

> 与 `skills/SKILL.md` 同一占位符精神：任何 `AgentUi_RunJson` 动作统一套用，只需替换 `<Action>`/字段。**字段权威**：`FileDriven/DrivenConfig/Templates/AgentRun/`（`README.md` 字段参考 + 各模板 JSON/说明）。模板即请求体预置，可直接作为即时指令 body。

```powershell
$fd   = 'D:\Project\Program\WorkFlowPrj\EnableMent\交互启动.bat'
$body = '{"action":"<Action>","<key>":"<value>","<key2>":<value2>}'
$body | & $fd
```

---

## 4. 常用动作示例（全部复用 `$fd`）

```powershell
# 入队任务（op=enqueue，写 ur_tasks 激活消费）
'{"action":"task","op":"enqueue","input":"整理资源清单","taskKey":"tp_x","mode":"Hybrid","priority":0,"maxRetries":0}' | & $fd

# 仅状态写（op=status，不激活消费；publish 控实时发布）
'{"action":"task","op":"status","taskKey":"tp_x","status":"Paused","publish":true}' | & $fd

# 删除任务（op=delete）
'{"action":"task","op":"delete","taskKey":"tp_x"}' | & $fd

# 提交输入区（op=submit，ur_inputs，不进主循环）
'{"action":"task","op":"submit","input":"{\"type\":\"user\",\"text\":\"hi\"}"}' | & $fd

# 前台循环（action=runLoop，mode Hybrid/Config）
'{"action":"runLoop","input":"测试输入","mode":"Hybrid"}' | & $fd

# 基础资源读写（resource / reference）
'{"action":"listResources"}' | & $fd
'{"action":"addResource","resourceType":"node","id":"demo_x","name":"演示节点"}' | & $fd
```

---

## 5. 规则与注意

| 项 | 说明 |
|----|------|
| **命令判别** | 含 `commands` → 脚本；含 `action` 无 `commands` → 即时指令 |
| **路径反斜杠** | JSON 内 `\` 需写 `\\`（如 `D:\\Project\\...`），否则解析失败走诊断分支 |
| **承载优先** | 管道（env `FILEDRIVEN_INSTANT_BODY`）优先；位置参数适合简短 body |
| **数字序号不冲突** | `echo 5 | 交互启动.bat` 走数字预处理解析脚本，不受影响 |
| **未装配诊断** | 未装配任务池/数据库时各动作返回对应诊断，不消耗 AI 额度 |
| **op=status 副作用** | 新增 `status=NotStarted/Running` 不当下执行，但重启回填（ProcessPendingAsync）可能捞起执行；要真执行用 `op=enqueue` |
| **结果回读** | 结果写 `resultTextBox` 并经管道回显（当前为 GUI 宿主运行时可见） |

---

## 6. 相关

- **请求体字段权威**：`FileDriven/DrivenConfig/Templates/AgentRun/`（`README.md` + `task.md` 等模板说明）。
- **实现位置**：`PowerShellScript/FileDriven/FileDriven/运行测试GUI模式.ps1`（run 分支 Raw-Body 判别 + 合成）、`EnableMent/交互启动.bat`（管道→env `FILEDRIVEN_INSTANT_BODY`）、`Agent/src/Agent.Launcher/Modules/AgentUiLibrary.cs`（`AgentUi_RunJson` action 白名单）。
- **配套技能**：脚本（commands）形态见 [04-json-script.md](04-json-script.md)；端到端三段式（注册/配置/脚本）见 [SKILL.md](SKILL.md)。

