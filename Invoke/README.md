# Frame\Invoke

统一调用入口层——薄转发，无业务逻辑。入口只组装参数（哈希表 splatting，按名传参）并转发到唯一实现。

## 入口

| 脚本 | 转发到 | 用途 |
|---|---|---|
| `Search-Repo.ps1` | `PowerShellScript\Share\Search\Search-Repo.ps1` | 多仓检索（git grep） |
| `Test-JsonFiles.ps1` | `PowerShellScript\Share\Validate\Test-JsonFiles.ps1` | JSON 批量校验 |
| `Invoke-GateCheck.ps1` | `PowerShellScript\Operation\DSL\Invoke-GateCheck.ps1` | 门禁聚合（16 件） |
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

**默认面**（D9 修复后）：
- 默认 `PathSpec` = `*.cs, *.json, *.jsonc, *.md, *.ps1, *.psm1, *.ushar, *.specs`
- **默认仅检索已跟踪文件**；未跟踪文件须显式 `-Untracked`
- 无命中时返回空（`-FailOnEmpty` 时退出码 1）

### JSON 校验
```powershell
# 目录递归校验
.\Test-JsonFiles.ps1 -Path ..\PowerShellScript\Operation\DSL

# 单文件静默模式（只输出失败）
.\Test-JsonFiles.ps1 -Path appsettings.json -Quiet

# 归档校验批次（覆盖 _deprecated 目录）
.\Test-JsonFiles.ps1 -Path ..\..\Projects\Plug\Agent\Config\_deprecated -IncludeDeprecated
```
**默认排除面**（D10 修复后）：`BuildOutput|obj|bin|_deprecated|node_modules|backups|TestResults`；`-IncludeDeprecated` 时退役/归档 JSON（`_deprecated`）也纳入校验。

### 门禁
```powershell
# 全量（16 件）
.\Invoke-GateCheck.ps1 -All

# 指定
.\Invoke-GateCheck.ps1 -Only AgentBuildPoint,DataContracts
```
16 件门禁清单见 `PowerShellScript\Operation\DSL\GateManifests.psd1`（含 `InvokeEntrypoints` 入口框架守卫）。

### 回归
```powershell
# 默认矩阵（串行；PowerShell 5.1 下 -Parallel 自动回退串行）
.\Invoke-Regression.ps1 -Matrix default

# 定向声明式用例（R32）
.\Invoke-Regression.ps1 -Case core.json-rule-engine.unknown-op.callback-logs
```
矩阵：`default`（6 项）/ `agent`（+3 项）/ `infra`（5 项）。`-Parallel` 需 PowerShell 7+（PS 5.1 自动回退串行）。

## 门禁清单契约（GateManifests.psd1）

纯数据文件，登记每件只读验收开关与期望退出码。扩展点 = 新增一件门禁：

```powershell
MyNewGate = @{
    Args   = @{ Check = $true }   # 必须为哈希表（具名 splatting）；默认即校验的件用 @{}
    Expect = 0                    # 期望退出码（通过时的值）
    Note   = '说明'               # 可选：参数语义 / 写开关禁用提示
}
```

- **`Args` 必须用哈希表**——数组 splatting（`@('-Check')`）会把开关名当位置参数绑到首形参（如 `$Root`），导致恒 FAIL（D1/D2 根因，`InvokeEntrypoints` 守卫强制此条）。
- **`Expect` 语义**：聚合器以 `exit code == Expect` 判定 PASS；`-All`/`-Only` 只跑只读验收，写开关（如 `-UpdateIndex`/`-Fix`）在清单 `Note` 中禁用。

## 结构

```
Frame/Invoke/             ← 入口（本目录，仅转发）
Frame/PowerShellScript/   ← 主体实现
  ├─ Modules/             ← .psm1 可复用模块
  ├─ PScriptManager/Framework/ ← owner 索引加载器（ps-function-index.json 编译表）
  ├─ Share/Search/        ← SR-2
  ├─ Share/Validate/      ← VE-1
  └─ Operation/DSL/       ← VE-3 + Check-*.ps1 + GateManifests.psd1
Environment/CentralTest/  ← VE-2（薄转发到 Run-CSharpTests）
```

**前置依赖**：`PScriptManager\Framework\ps-function-index.json`（owner 函数索引编译表）。缺失时加载器自动调用 `Build-FunctionIndex.ps1` 自举生成（D3 修复）；自举失败则以固定码退出并打印可复制的修复命令。

## 约束

- 入口只转发，不含业务逻辑；**参数一律哈希表 splatting（按名传参）**，禁止数组 splatting 传开关
- **退出码**：
  | 码 | 语义 |
  |---|---|
  | 0 | 通过 |
  | 1 | 失败（门禁任一件 FAIL / 校验未通过） |
  | 2 | 异常/依赖缺失（目标抛错或未显式 exit；入口统一 try/catch 归一） |
- 零静默失败（零匹配/路径不存在均显式报错；子脚本必 `exit`；聚合器 per-gate 隔离，单件异常不中断整轮）
- 回归入口不出现 `dotnet test` / `dotnet build`（全部转发到 `Run-CSharpTests.ps1`）

## 守卫

`InvokeEntrypoints` 门禁（`Operation\DSL\Check-InvokeEntrypoints.ps1`）把下列约束变成机器判据，防同类回归：
① 4 入口不得出现数组 splatting 传开关；② `GateManifests.psd1` 每件 `Args` 须为哈希表；③ 每入口含 `try` + `catch` + 确定 `exit`；④ 全 DSL 目录 `Check-*.ps1` 不得含硬编码绝对路径。
