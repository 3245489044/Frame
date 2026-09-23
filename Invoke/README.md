# Frame\Invoke

统一调用入口层——薄转发，无业务逻辑。

## 入口

| 脚本 | 转发到 | 用途 |
|---|---|---|
| `Search-Repo.ps1` | `PowerShellScript\Share\Search\Search-Repo.ps1` | 多仓检索（git grep） |
| `Test-JsonFiles.ps1` | `PowerShellScript\Share\Validate\Test-JsonFiles.ps1` | JSON 批量校验 |
| `Invoke-GateCheck.ps1` | `PowerShellScript\Operation\DSL\Invoke-GateCheck.ps1` | 门禁聚合（13 件） |
| `Invoke-Regression.ps1` | `Environment\CentralTest\Invoke-AgentRegression.ps1` | C# 回归一键跑 |

## 用法

### 检索
```powershell
# 单 pattern，限定文件类型与仓
.\Search-Repo.ps1 -Pattern data-poll-cmd -PathSpec '*.cs' -Repo Frame

# 多 pattern（OR），只列文件
.\Search-Repo.ps1 -Pattern 'A,B' -List -Repo Frame,Projects
```
默认字面量匹配（免转义），分仓输出带汇总。

### JSON 校验
```powershell
# 目录递归校验
.\Test-JsonFiles.ps1 -Path ..\PowerShellScript\Operation\DSL

# 单文件静默模式（只输出失败）
.\Test-JsonFiles.ps1 -Path appsettings.json -Quiet
```

### 门禁
```powershell
# 全量
.\Invoke-GateCheck.ps1 -All

# 指定
.\Invoke-GateCheck.ps1 -Only AgentBuildPoint,DataContracts
```
13 件门禁清单见 `PowerShellScript\Operation\DSL\GateManifests.psd1`。

### 回归
```powershell
# 默认矩阵 + 并发 4
.\Invoke-Regression.ps1 -Matrix default -Parallel 4

# 定向声明式用例（R32）
.\Invoke-Regression.ps1 -Case core.json-rule-engine.unknown-op.callback-logs
```
矩阵：`default`（6 项）/ `agent`（+3 项）/ `infra`（5 项）。

## 结构

```
Frame/Invoke/             ← 入口（本目录，仅转发）
Frame/PowerShellScript/   ← 主体实现
  ├─ Modules/             ← .psm1 可复用模块
  ├─ Share/Search/        ← SR-2
  ├─ Share/Validate/      ← VE-1
  └─ Operation/DSL/       ← VE-3 + Check-*.ps1
Environment/CentralTest/  ← VE-2（薄转发到 Run-CSharpTests）
```

## 约束

- 入口只转发，不含业务逻辑
- 退出码：0 = 通过 / 1 = 失败
- 零静默失败（零匹配/路径不存在均显式报错）
