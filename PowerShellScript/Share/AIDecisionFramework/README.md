# AI 决策框架 — PowerShell 脚本模块

> 基于 `docs/KnowLedge/AI决策框架.md` 的工程决策引擎
> 核心原则：**所有操作通过运行 ps1 脚本实现**

## 架构概览

```
PowerShellScript/AIDecisionFramework/
├── README.md                 # 本文件 — 使用说明
├── _init.ps1                 # 模块初始化入口
├── DecisionEngine.ps1        # 核心决策引擎（决策矩阵逻辑）
├── TokenAccounting.ps1       # Token 经济算账（含错期望、重启税、摊销）
├── ToolBridge.ps1            # 工具调用桥接（ctx_execute / CLI 命令替代）
├── Invoke-Decision.ps1       # 主入口脚本
└── test-invoke.ps1           # MCP 调用测试脚本
```

## 调用方式

### 方式一：通过 MCP 工具（推荐）

```json
{
  "tool": "invoke_ps_script",
  "script_path": "PowerShellScript/AIDecisionFramework/Invoke-Decision.ps1",
  "arguments": {
    "TaskDesc": "批量重命名文件",
    "N": 1,
    "T": 2
  }
}
```

### 方式二：直接 PowerShell 执行

```powershell
.\PowerShellScript\AIDecisionFramework\Invoke-Decision.ps1 -ShowMatrix
.\PowerShellScript\AIDecisionFramework\Invoke-Decision.ps1 -TaskDesc "分析" -N 3 -T 10 -ShowEconomics
```

### 方式三：点源加载模块

```powershell
. .\PowerShellScript\AIDecisionFramework\_init.ps1
Invoke-CliCommand -Command "git status"
Read-File -Path "docs/KnowLedge/AI决策框架.md"
```

## 模块说明

| 文件 | 功能 | 主要函数 |
|------|------|---------|
| `_init.ps1` | 模块初始化 | 加载所有子模块、设置全局变量 |
| `DecisionEngine.ps1` | 决策矩阵 | `Get-ExecutionMode`, `Get-DecisionMatrix`, `Test-DecisionParams` |
| `TokenAccounting.ps1` | Token 经济算账 | `Get-TokenCost`, `Get-CognitiveRestartTax`, `Get-AmortizationAnalysis` |
| `ToolBridge.ps1` | 工具桥接 | `Invoke-CtxExecute`, `Invoke-CliCommand`, `Get-GitStatus` |
| `Invoke-Decision.ps1` | 主入口 | 决策流程编排，支持 `-ShowMatrix`, `-ShowEconomics`, `-OutputJson` |

## 决策矩阵速查

| 场景 | N | T | 推荐模式 |
|------|---|---|---------|
| 临时探索（逻辑不闭合） | 任意 | 任意 | 强制实时 ReAct |
| 简单别名 | N=1 | <1s | 实时终端 |
| 确定脚本，单次运行 | N=1 | <10min | 实时 Agent（串行） |
| 确定脚本，多次运行 | N≥5 | 任意 | 离线脚本/编译 |
| 确定脚本，单次极耗时 | N=1 | >1h | 强制离线脚本（异步） |

## 优先级总则

```
AI决策框架脚本（优先） → MCP 工具（备选） → CLine 内置工具（再次） → CLI 命令（最后手段）
```

> 文件读写操作优先使用 `FileAccess.ps1` 中的脚本函数（如 `Read-File`、`Write-File`、`BatchWrite-File` 等），而非直接调用 CLine 内置工具。

## MCP 工具桥接

通过 `invoke_ps_script` MCP 工具，可直接在宿主机上调用 PowerShell 脚本/模块中的函数：

```json
{
  "tool": "invoke_ps_script",
  "script_path": "PowerShellScript/AIDecisionFramework/FileAccess.ps1",
  "function_name": "BatchRead-File",
  "arguments": {
    "Path": "CsharpPrj/**/*.cs",
    "Pattern": "class.*Registry"
  }
}
```

**优先级**：`invoke_ps_script` MCP 工具（优先） → `Invoke-CliCommand`（备选） → CLine 内置工具（最后手段）

## 测试

```powershell
# 测试 MCP 桥接是否正常
.\PowerShellScript\AIDecisionFramework\test-invoke.ps1 -Name "测试"
```
