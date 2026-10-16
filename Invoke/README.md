# Frame\Invoke

统一调用入口层——薄转发，无业务逻辑。入口只组装参数（按名传参）并转发到唯一实现。

> **M5 收敛**：原 19 个逐件转发脚本 → **单一入口 `Invoke-Frame.ps1`（`-Action` 分发）** + 分发表 `entries.psd1`；
> 公共机制（目标解析 / 按名传参 / SpecCli 装配 / 退出码归一）下沉基础框架 `PowerShellScript\Modules\Dispatcher\`。
> 作者面（spec 链/用例/清点）经 `SpecSourceGen\Cli` 转发（loc 位置反转：机制下沉 `SpecSourceGen\Core\Authoring`）。

## 入口（单一入口 + `-Action`）

调用：`pwsh Frame\Invoke\Invoke-Frame.ps1 -Action <动作> [-参数…]`

| `-Action` | 转发到 | 用途 |
|---|---|---|
| `search-repo` | `PowerShellScript\Share\Search\Search-Repo.ps1` | 多仓检索（git grep） |
| `test-jsonfiles` | `PowerShellScript\Share\Validate\Test-JsonFiles.ps1` | JSON 批量校验 |
| `invoke-gatecheck` | `PowerShellScript\Operation\DSL\Tooling\Invoke-GateCheck.ps1` | 门禁聚合（件数以 `GateManifests.psd1` 为准） |
| `invoke-regression` | `Environment\CentralTest\Invoke-CentralTest.ps1 -Action regression` | C# 回归一键跑 |
| `locate-test` | `Environment\CentralTest\Invoke-CentralTest.ps1 -Action locate-test` | 诊断相位 · 零运行定位（失败测试名/caseId → 唯一工程） |
| `locate-build` | `Environment\CentralTest\Invoke-CentralTest.ps1 -Action locate-build` | 诊断相位 · 零运行定位（改动文件 → 构建/测试/门禁清单） |
| `locate-port` | `Environment\CentralTest\Invoke-CentralTest.ps1 -Action locate-port` | 诊断相位 · 零运行定位（端口键 → 声明/实现/消费用例/工程） |
| `invoke-verify` | `Environment\CentralTest\Invoke-CentralTest.ps1 -Action verify` | P2 唯一验证命令（bake→test→gate 一次完成） |
| `invoke-buildeffective` | `Projects\BuildFramework\Build-Effective.ps1` | 单一生效入口（构建→生效面上效→生效面断言；`-Assert` 复算断言） |
| `invoke-specgen` | `SpecSourceGen\Cli`（`gen`） | 作者面 · 生成（PS 可调用、产物落生成根、可清点；`-EmitManifest` 另出 `_manifest.json`） |
| `get-spec-chain` | `SpecSourceGen\Cli`（`chain-get`） | 作者面 · 只读（链/引用清单 + 内联载荷去重候选；**混装块按 `port` 子块签名**判定） |
| `new-spec-chain` | `SpecSourceGen\Cli`（`chain-new`） | 作者面 · 抽链（内联 invocation → `chain` + `use`；DryRun 缺省 + 三类自检） |
| `use-spec-chain` | `SpecSourceGen\Cli`（`chain-use`） | 作者面 · 切链（匹配用例的内联 → `use <链>`；DryRun 缺省 + 三类自检） |
| `set-spec-chain` | `SpecSourceGen\Cli`（`chain-set`） | 作者面 · 改链载荷（就地更新；幂等 + DryRun 缺省） |
| `remove-spec-chain` | `SpecSourceGen\Cli`（`chain-remove`） | 作者面 · 删链（引用守卫：仍有 use/and use 引用即拒绝；DryRun 缺省） |
| `foreach-speccase` | `SpecSourceGen\Cli`（`case-list`） | 作者面 · 只读遍历（用例选择器 + 调用形态标注） |
| `new-specchains` | `SpecSourceGen\Cli`（`chains-new`） | 作者面 · 批量抽链（DryRun 缺省 + `-Write` 落盘） |
| `test-specrefs` | `SpecSourceGen\Cli`（`refs`） | 作者面 · 引用完整性（悬空 use；只读 + `-Check`） |
| `get-specgenerated` | `SpecSourceGen\Cli`（`generated-list`） | 生成物清点（只读 `_manifest.json`；`-Verify` 复核 sha256） |
| `read-agentdb` | `PowerShellScript\Share\Data\Read-AgentDb.ps1` | 诊断面 · 只读检视 Agent 数据库（SQLite `agent.db` / LiteDB `graph.db`） |

> 迁移映射：旧 `.\<X>.ps1 -P v` ⇔ 新 `.\Invoke-Frame.ps1 -Action <x> -P v`（`<x>` = 旧文件名小写连字符，如 `Locate-Port` → `locate-port`）。

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
门禁清单与**件数**的唯一来源 = `PowerShellScript\Operation\DSL\Registry\GateManifests.psd1`（禁在文档/注释里写死件数）。
每件可带 `Tier='fast'`（读产物类轻量件）与 `Path=`（Modules 侧守卫的仓库相对路径）；`Args` 须哈希表（见下文守卫）。

**基线单源**：门禁基线/清册一律放 `Frame\PowerShellScript\Baselines\`（`<Gate>.psd1`，`_` 前缀 = 清册）。
脚本内禁内联数值基线（守卫 `GateBaselines`）；生成物禁手改，漂移即红（守卫 `GeneratedArtifactsDrift`，登记表 `Baselines\_generated-artifacts.psd1`）。
`Derived`/`Debt`/`Verdict` 四分与迁移台账见 `Baselines\README.md`。

### 回归
```powershell
# 局部测试（免授权）：定向声明式用例（R32）
.\Invoke-Frame.ps1 -Action invoke-regression -Case core.json-rule-engine.unknown-op.callback-logs

# 局部测试（免授权）：定向工程复跑（自由名 -Test 须与 -Project 同用，FD-14；别名形态如 keys/node 不需）
.\Invoke-Frame.ps1 -Action invoke-regression -Test keys,node -Project 'Keys\.Tests|Node\.Tests'

# 全面测试（矩阵）⇒ 默认禁止，须持授权（权威 .clinerules\axioms.md §9 % VERIFY_FULL_RUN_AUTH_REQUIRED）
.\Invoke-Frame.ps1 -Action invoke-regression -Matrix default -GrantFile 'BuildOutput\Debug\verify-grant.json'
```
矩阵：`default`（6 项）/ `agent`（+3 项）/ `infra`（5 项）。`-Parallel` 需 PowerShell 7+（PS 5.1 自动回退串行）。
**验证面纪律**：局部面（`-Test`/`-Project`/`-Case`）免授权；矩阵属**全量面**，默认禁止，须持授权
（优先级 `-GrantFile` 授权单 > 批次授权单 `verify` 字段 > 宿主令牌 `WF_VERIFY_FULL_GRANT`；未授权 fail-closed）。

### 定位（诊断相位 · 零运行）
```powershell
# 失败测试名 → 唯一工程（0 次试跑；诊断相位不产 pass/fail）
.\Invoke-Frame.ps1 -Action locate-test -Name ConversationPipelineTests

# 声明式 caseId / suiteId 显示名
.\Invoke-Frame.ps1 -Action locate-test -Name 'signal-workflow-runner.run-round-async-without-spec-leaves-nodes-empty'
```
索引源（唯一真源，不试跑）：`.specs` 的 `case <caseId>` 行 + `TestTargets.psd1` 别名表 + 各测试工程源码的 `class/record` 声明。
未命中判 `undetermined`（≠「不存在」）。禁逐工程试跑定位（R1）。

### 定位（构建 / 门禁落点 · 诊断相位 · 零运行）
```powershell
# 改动文件 → 需构建工程 / 需跑测试工程 / 需跑门禁（0 试跑）
.\Invoke-Frame.ps1 -Action locate-build -Changed 'Frame\CsharpPrj\Share\X.cs','Environment\CentralTest\AgentTest\Agent.Core.Tests\A.cs'

# 冻结清单 / JSON（供 Invoke-Verify 消费）
.\Invoke-Frame.ps1 -Action locate-build -Changed 'Frame\PowerShellScript\Operation\DSL\Process\Check-VerifySingleShot.ps1' -Json
```
分类面 = 仓库相对路径前缀（结构事实）；未命中判 `Unmapped`（fail-loud，≠「全部」，禁静默全量）。禁 `dotnet build` 全量烘 / 逐工程试探（G6）。

### 作者面（链清单 · 只读 · 诊断相位）
```powershell
# 链/引用清单 + 内联载荷去重候选（供 D4′ 抽链前定标；零写盘）
.\Invoke-Frame.ps1 -Action get-spec-chain -Explain

# 自检（正/负对照，防解析器空转）
.\Invoke-Frame.ps1 -Action get-spec-chain -Check

# 清单 JSON 出口（显式才写盘）
.\Invoke-Frame.ps1 -Action get-spec-chain -Out 'Environment\CentralTest\BuildOutput\Debug\specchain.json'
```
只读 / 幂等 / 零写盘；不产 pass/fail，**不作放行依据**。写侧（`New-/Use-/Set-SpecChain`）另批。

```powershell
# 抽链（内联 invocation → chain + use；DryRun）
.\Invoke-Frame.ps1 -Action new-spec-chain -Suite 'Environment\...\x.specs' -Name probe

# 切链（把匹配用例切到既有链）
.\Invoke-Frame.ps1 -Action use-spec-chain -Suite 'Environment\...\x.specs' -Chain probe

# 改链载荷（就地更新）
.\Invoke-Frame.ps1 -Action set-spec-chain -Suite 'Environment\...\x.specs' -Chain probe -From case-a

# 只读遍历（用例选择器 + 形态标注）
.\Invoke-Frame.ps1 -Action foreach-speccase -Shape inline
```
写侧工具缺省 `-DryRun`（零写盘），`-Write` 落盘；均带「未改写 / 悬空 / 空产物」三类结构自检。

### 验证（P2 唯一命令）
```powershell
# bake→test→gate 一次完成（agent 面 shell 调用计 1；默认只跑局部面）
.\Invoke-Frame.ps1 -Action invoke-verify -Changed 'Frame\CsharpPrj\Share\X.cs'

# 消费冻结清单
.\Invoke-Frame.ps1 -Action invoke-verify -Plan 'Environment\CentralTest\BuildOutput\Debug\verify-plan.json'

# 全量面（测试矩阵 / 全部门禁 -All）⇒ 默认禁止，须持授权
.\Invoke-Frame.ps1 -Action invoke-verify -Changed 'Frame\PowerShellScript\Operation\DSL\Registry\GateManifests.psd1' -GrantFile 'BuildOutput\Debug\verify-grant.json'
```
`Invoke-Frame.ps1 -Action invoke-verify` = `VERIFY_SINGLE_ENTRY`（`.clinerules\project.md` §1.10）；内部串 `Bake-CsharpPrj`→`Run-CSharpTests`/`Invoke-AgentRegression`→`Invoke-GateCheck`，不复制逻辑。
拆分式 `build`/`test`/`gate` 多条调用、原生 `dotnet` 直呼 ⇒ 违反唯一命令纪律（`VerifySingleShot` 守卫 + `ANTI-MULTI-RUN-VERIFY`）。
**验证面裁决（[VERIFY-SCOPE-GATE]，权威 `% VERIFY_FULL_RUN_AUTH_REQUIRED`）**：局部面（映射出的定向测试工程 / 映射门禁）免授权直跑；
全量面（`-TestSet` 显式请求 / 计划回落全矩阵 / `GatesAll`⇒`-All`）**默认禁止**——显式请求未授权即阻断，
计划隐式回落先**局部优先**（有局部面则抑制矩阵），无局部面则阻断；`GatesAll` 未授权降级为自检件并 `[REPORT]` 未覆盖面。

### 验证自证对齐（源码指纹 = 产物指纹）
`Invoke-Frame.ps1 -Action invoke-regression` 会在验证前打印 `[ALIGN]` 证据行，并前置断言「源码内容指纹 = 产物(dll)内容指纹」：
- `state=aligned`：产物与源码对齐；
- `state=unverified`：首次运行（建立基线）；
- `state=unaligned`：**产品源码已变但未重烘** ⇒ fail-fast 红「未对齐」（**不出「测试失败」**），修法 `dotnet build Frame\CsharpPrj\MainProj.sln`。
可用 `Invoke-Frame.ps1 -Action run -SkipAlignmentCheck`（或 `CentralTest\Invoke-CentralTest.ps1 -Action run -SkipAlignmentCheck`）跳过（仅在确认对齐由外部保证时）。

### 生效（单一生效入口）
```powershell
# 构建 → 生效面上效 → 生效面断言（dev 就地即生效；locked 产代 gen-<n> + 切 current）
.\Invoke-Frame.ps1 -Action invoke-buildeffective

# 仅复算断言（不构建；未对齐 ⇒ 退出码 1）—— EffectiveFaceAssertion 的机械复算命令
.\Invoke-Frame.ps1 -Action invoke-buildeffective -Assert
```
`Invoke-Frame.ps1 -Action invoke-buildeffective` = `BUILD_EFFECTIVE_SINGLE_ENTRY`（`.clinerules\project.md` §1.10）；转发到 `Projects\BuildFramework\Build-Effective.ps1`，内部串 `Build-Product.ps1 -All`（[locked] `Build-AgentGeneration.ps1`）→ `Check-EffectiveFace.ps1`。
完成判据 = **生效面已对齐**（结构事实 = 内容指纹），非「我编译了」（「编译成功」≠「生效」，判 `ANTI-COMPILE-NOT-EFFECTIVE`）。

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
  ├─ Invoke-Frame.ps1     ← 单一入口（-Action 分发）
  ├─ entries.psd1         ← 分发表（20 动作：Kind/Target/Verb/Map/Always/Fixed/Options）
  └─ README.md
Frame/PowerShellScript/   ← 主体实现
  ├─ Modules/             ← .psm1 可复用模块
  │   └─ Dispatcher/      ← 入口转发通用机制（目标解析/按名传参/SpecCli 装配/退出码归一）
  ├─ PScriptManager/Framework/ ← owner 索引加载器（ps-function-index.json 编译表）
  ├─ Share/Search/        ← SR-2
  ├─ Share/Validate/      ← VE-1
  ├─ Share/Data/          ← Read-AgentDb（诊断面只读检视 agent.db / graph.db）
  └─ Operation/DSL/       ← VE-3 + Check-*.ps1 + GateManifests.psd1
Environment/CentralTest/  ← VE-2（薄转发到 Run-CSharpTests）
  ├─ Invoke-CentralTest.ps1 ← A 层单一实现入口（-Action 分发）
  └─ Internal/            ← A 层实现件（原根目录 8 脚本）+ entries.psd1 + Import-FrameModules.ps1
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
- 回归入口不出现 `dotnet test` / `dotnet build`（全部转发到 `CentralTest\Internal\Run-CSharpTests.ps1`）

## 守卫

`InvokeEntrypoints` 门禁（`Operation\DSL\Process\Check-InvokeEntrypoints.ps1`）把下列约束变成机器判据，防同类回归：
① 入口不得出现数组 splatting 传开关；② `GateManifests.psd1` 每件 `Args` 须为哈希表；③ 每入口含 `try` + `catch` + 确定 `exit`；④ 全 DSL 目录 `Check-*.ps1` 不得含硬编码绝对路径；⑤ `entries.psd1` 中 `Kind='Ps'` 的分发目标存在（正/负对照防假绿）。

`VerifySingleShot` 门禁（`Operation\DSL\Process\Check-VerifySingleShot.ps1`）把「回归/验证只经 `Frame\Invoke` 单入口」变成机器判据（G8）：
① `Frame\Invoke\*.ps1` 禁原生 `dotnet test/build`；② `VERIFY_SINGLE_ENTRY` 接线不漂移（`axioms.md` 声明 ∧ 指向已存在入口 ∧ `project.md` §1.10 登记）；③ 正/负对照防判据空转。
边界：本守卫只扫**文件面**，拦不住 agent 运行期直呼 `dotnet` —— 运行面由 `project.md` §1.10 相位白名单 + 回复内 `[批次声明]` 自审承担。

### 过程层治理守卫（修复文档 G/H 批）

把框架自己的「单一生产者 + 单一可判定面 + 判据载体=结构事实」公理，从**代码**扩展到**过程**（批次授权 · 影响面验证）：

| 守卫 | 门禁 | 判据 |
|---|---|---|
| `Operation\DSL\Process\Check-BatchScope.ps1` | `BatchScope` | 实际改动文件集（`git status`，含子模块）⊆ 活动批 `allowSet` ∪ 派生 ∪ 既有；`denySet` 面禁改；正/负对照。授权单 = `Docs\analysis\2026\10\10.5\2026-10-05-过程层缺陷结构治理.batches.json`（路径单源 = `Baselines\BatchScope.psd1`）；缺件 ⇒ 灰度 warn |
| `Operation\DSL\Process\Check-VerifyProportionality.ps1` | `VerifyProportionality` | 产品单工程改动成比例（验证/改动比 ≤ 1.5）∧ 映射落点 ∧ 不静默丢面 ∧ 未分类改动 fail-loud；消费 `Locate-Build`（`Get-VerifyPlan`）的工程影响面映射 |

> 二者把 `[批次声明]` 从「自述」升级为「对结构核验」；基线单源见 `Frame\PowerShellScript\Baselines\`。

