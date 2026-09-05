> 本文件为 `skills/SKILL.md` 的模块化拆分 —— **② 创建 JSON 配置 · 工作流（WorkFlow/**/*.json）**（原 §2.2 + §2.3 + §2.4）。
> 完整三段式流程、模块导航与端到端示例见 [SKILL.md](SKILL.md)。本模块章节沿用原编号（§2.x），便于与 SKILL.md 导航表对照。
> 相关模块：① 注册 → [01-register-function.md](01-register-function.md) · ② 配置（UI）→ [02-json-ui.md](02-json-ui.md) · ③ 脚本 → [04-json-script.md](04-json-script.md)。

---

## 2.2 工作流配置（`WorkFlow/**/<WorkflowName>.json`）

> `WorkflowLibrary` 自动扫描 `WorkFlow` 目录并注册每个工作流为可调用函数（工作流名即函数名）。

```json
{
  "description": "工作流说明",
  "workflowName": "<WorkflowName>",
  "displayName": "显示名",
  "defaultInput": { "<key1>": "", "<key2>": "", "<key3>": "" },
  "steps": [
    { "stepName": "Init",    "funcName": "<FuncA>", "inputKeys": [], "outputKey": "<outA>", "parameters": [] },
    { "stepName": "Execute", "funcName": "<FuncB>", "inputKeys": ["<key2>", "<key3>"], "outputKey": "result", "parameters": ["${<key2>}", "${<key3>}"] }
  ],
  "outputMapping": { "<outA>": "<outA>", "result": "result" }
}
```

**字段说明**：

| 字段 | 含义 |
|------|------|
| `stepName` | 步骤名，也是 `_stepSelector` 的取值 |
| `funcName` | 该步骤调用的已注册函数名 |
| `inputKeys` | 从工作流上下文读取的参数键（**引擎实际取参来源**） |
| `outputKey` | 步骤结果存入上下文的键 |
| `parameters` | `${key}` 模板（历史/部分引擎版本用；与 `inputKeys` 不一致时以 `inputKeys` 生效） |
| `outputMapping` | 步骤 outputKey → 输出名（与 UI `output` 控件绑定配合） |

### 2.3 `_stepSelector` 机制（关键陷阱 ⚠️）

- 事件绑定通过 `_stepSelector` 指定从**哪个步骤开始**执行。
- **级联行为**：`WorkflowPipeline` 的 `CanExecute: index >= StartIndex` —— 从选中步骤起**后续所有步骤都会执行**；步骤失败仅记录 `failedSteps`，**不中断**。

```mermaid
flowchart LR
    SEL["_stepSelector = SelectLibrary"] --> ST1["SelectLibrary"]
    ST1 --> ST2["SelectFunction"]
    ST2 --> ST3["Execute"]
    ST3 --> ST4["Search"]
    ST4 --> TAIL["... 后续步骤全部执行<br/>（步骤失败不中断）"]
    classDef sel fill:#3b82f6,color:#fff
    classDef cascade fill:#ef4444,color:#fff
    class SEL sel
    class ST1,ST2,ST3,ST4,TAIL cascade
```

- **后果**：`Init` 会连带执行其后的所有步骤（如 `Search("")` 可能覆盖函数列表）。设计步骤顺序时，把"会互相覆盖 outputKey"的步骤分开 outputKey，或在绑定中显式用 `_stepSelector` 精确跳转。

### 2.4 initCommands 与 exec 命令

```json
"initCommands": [
  "exec <WorkflowName> _stepSelector=Init",
  "exec <WorkflowName> _stepSelector=GetStats output=statsLabel"
]
```

| 形态 | 含义 |
|------|------|
| `exec <Workflow> _stepSelector=<Step>` | 从指定步骤执行工作流 |
| `exec <FuncName> output=<Control>` | 执行函数并写回控件（初始化填充列表/统计） |
| `initCommandMode: "function"` + `initCommands: ["<FuncName>"]` | 函数模式：裸函数名列表（如 `AgentConfigUI.json` 用 `InitWorkflowBrowser`），结果按函数默认输出落位 |

---
