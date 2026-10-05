# Frame\Invoke

统一调用入口层——薄转发，无业务逻辑。入口只组装参数（哈希表 splatting，按名传参）并转发到唯一实现。

## 入口

| 脚本 | 转发到 | 用途 |
|---|---|---|
| `Search-Repo.ps1` | `PowerShellScript\Share\Search\Search-Repo.ps1` | 多仓检索（git grep） |
| `Test-JsonFiles.ps1` | `PowerShellScript\Share\Validate\Test-JsonFiles.ps1` | JSON 批量校验 |
| `Invoke-GateCheck.ps1` | `PowerShellScript\Operation\DSL\Invoke-GateCheck.ps1` | 门禁聚合（件数以 `GateManifests.psd1` 为准） |
| `Invoke-Regression.ps1` | `Environment\CentralTest\Invoke-AgentRegression.ps1` | C# 回归一键跑 |
| `Locate-Test.ps1` | `Environment\CentralTest\Run-CSharpTests.ps1 -Locate` | 诊断相位 · 零运行定位（失败测试名/caseId → 唯一工程） |
| `Locate-Build.ps1` | `Environment\CentralTest\Get-VerifyPlan.ps1` | 诊断相位 · 零运行定位（改动文件 → 构建/测试/门禁清单） |
| `Invoke-Verify.ps1` | `Environment\CentralTest\Invoke-Verify.ps1` | P2 唯一验证命令（bake→test→gate 一次完成） |

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
# 全量
.\Invoke-GateCheck.ps1 -All

# 快层：只跑清单内 Tier='fast' 的「读产物」类轻量件（秒级反馈；全量慢是结构性的：多数件扫源码 + 串行）
.\Invoke-GateCheck.ps1 -All -Tier fast

# 指定
.\Invoke-GateCheck.ps1 -Only AgentBuildPoint,DataContracts
```
门禁清单与**件数**的唯一来源 = `PowerShellScript\Operation\DSL\GateManifests.psd1`（禁在文档/注释里写死件数）。
每件可带 `Tier='fast'`（读产物类轻量件）与 `Path=`（Modules 侧守卫的仓库相对路径）；`Args` 须哈希表（见下文守卫）。

**基线单源**：门禁基线/清册一律放 `Frame\PowerShellScript\Baselines\`（`<Gate>.psd1`，`_` 前缀 = 清册）。
脚本内禁内联数值基线（守卫 `GateBaselines`）；生成物禁手改，漂移即红（守卫 `GeneratedArtifactsDrift`，登记表 `Baselines\_generated-artifacts.psd1`）。
`Derived`/`Debt`/`Verdict` 四分与迁移台账见 `Baselines\README.md`。

### 回归
```powershell
# 默认矩阵（串行；PowerShell 5.1 下 -Parallel 自动回退串行）
.\Invoke-Regression.ps1 -Matrix default

# 定向声明式用例（R32）
.\Invoke-Regression.ps1 -Case core.json-rule-engine.unknown-op.callback-logs

# 定向工程复跑（自由名 -Test 须与 -Project 同用，FD-14；别名形态如 keys/node 不需）
.\Invoke-Regression.ps1 -Test keys,node -Project 'Keys\.Tests|Node\.Tests'
```
矩阵：`default`（6 项）/ `agent`（+3 项）/ `infra`（5 项）。`-Parallel` 需 PowerShell 7+（PS 5.1 自动回退串行）。

### 定位（诊断相位 · 零运行）
```powershell
# 失败测试名 → 唯一工程（0 次试跑；诊断相位不产 pass/fail）
.\Locate-Test.ps1 -Name ConversationPipelineTests

# 声明式 caseId / suiteId 显示名
.\Locate-Test.ps1 -Name 'signal-workflow-runner.run-round-async-without-spec-leaves-nodes-empty'
```
索引源（唯一真源，不试跑）：`.specs` 的 `case <caseId>` 行 + `TestTargets.psd1` 别名表 + 各测试工程源码的 `class/record` 声明。
未命中判 `undetermined`（≠「不存在」）。禁逐工程试跑定位（R1）。

### 定位（构建 / 门禁落点 · 诊断相位 · 零运行）
```powershell
# 改动文件 → 需构建工程 / 需跑测试工程 / 需跑门禁（0 试跑）
.\Locate-Build.ps1 -Changed 'Frame\CsharpPrj\Share\X.cs','Environment\CentralTest\AgentTest\Agent.Core.Tests\A.cs'

# 冻结清单 / JSON（供 Invoke-Verify 消费）
.\Locate-Build.ps1 -Changed 'Frame\PowerShellScript\Operation\DSL\Check-VerifySingleShot.ps1' -Json
```
分类面 = 仓库相对路径前缀（结构事实）；未命中判 `Unmapped`（fail-loud，≠「全部」，禁静默全量）。禁 `dotnet build` 全量烘 / 逐工程试探（G6）。

### 验证（P2 唯一命令）
```powershell
# bake→test→gate 一次完成（agent 面 shell 调用计 1）
.\Invoke-Verify.ps1 -Changed 'Frame\CsharpPrj\Share\X.cs'

# 消费冻结清单
.\Invoke-Verify.ps1 -Plan 'Environment\CentralTest\BuildOutput\Debug\verify-plan.json'
```
`Invoke-Verify.ps1` = `VERIFY_SINGLE_ENTRY`（`.clinerules\project.md` §1.10）；内部串 `Bake-CsharpPrj`→`Run-CSharpTests`/`Invoke-AgentRegression`→`Invoke-GateCheck`，不复制逻辑。
拆分式 `build`/`test`/`gate` 多条调用、原生 `dotnet` 直呼 ⇒ 违反唯一命令纪律（`VerifySingleShot` 守卫 + `ANTI-MULTI-RUN-VERIFY`）。

### 验证自证对齐（源码指纹 = 产物指纹）
`Invoke-Regression.ps1` 会在验证前打印 `[ALIGN]` 证据行，并前置断言「源码内容指纹 = 产物(dll)内容指纹」：
- `state=aligned`：产物与源码对齐；
- `state=unverified`：首次运行（建立基线）；
- `state=unaligned`：**产品源码已变但未重烘** ⇒ fail-fast 红「未对齐」（**不出「测试失败」**），修法 `dotnet build Frame\CsharpPrj\MainProj.sln`。
可用 `Run-CSharpTests.ps1 -SkipAlignmentCheck` 跳过（仅在确认对齐由外部保证时）。

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
① 入口不得出现数组 splatting 传开关；② `GateManifests.psd1` 每件 `Args` 须为哈希表；③ 每入口含 `try` + `catch` + 确定 `exit`；④ 全 DSL 目录 `Check-*.ps1` 不得含硬编码绝对路径。

`VerifySingleShot` 门禁（`Operation\DSL\Check-VerifySingleShot.ps1`）把「回归/验证只经 `Frame\Invoke` 单入口」变成机器判据（G8）：
① `Frame\Invoke\*.ps1` 禁原生 `dotnet test/build`；② `VERIFY_SINGLE_ENTRY` 接线不漂移（`axioms.md` 声明 ∧ 指向已存在入口 ∧ `project.md` §1.10 登记）；③ 正/负对照防判据空转。
边界：本守卫只扫**文件面**，拦不住 agent 运行期直呼 `dotnet` —— 运行面由 `project.md` §1.10 相位白名单 + 回复内 `[批次声明]` 自审承担。

### 过程层治理守卫（修复文档 G/H 批）

把框架自己的「单一生产者 + 单一可判定面 + 判据载体=结构事实」公理，从**代码**扩展到**过程**（批次授权 · 影响面验证）：

| 守卫 | 门禁 | 判据 |
|---|---|---|
| `Operation\DSL\Check-BatchScope.ps1` | `BatchScope` | 实际改动文件集（`git status`，含子模块）⊆ 活动批 `allowSet` ∪ 派生 ∪ 既有；`denySet` 面禁改；正/负对照。授权单 = `Docs\analysis\2026\10\10.5\2026-10-05-过程层缺陷结构治理.batches.json`（路径单源 = `Baselines\BatchScope.psd1`）；缺件 ⇒ 灰度 warn |
| `Operation\DSL\Check-VerifyProportionality.ps1` | `VerifyProportionality` | 产品单工程改动成比例（验证/改动比 ≤ 1.5）∧ 映射落点 ∧ 不静默丢面 ∧ 未分类改动 fail-loud；消费 `Locate-Build`（`Get-VerifyPlan`）的工程影响面映射 |

> 二者把 `[批次声明]` 从「自述」升级为「对结构核验」；基线单源见 `Frame\PowerShellScript\Baselines\`。

