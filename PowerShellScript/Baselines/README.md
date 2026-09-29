# Baselines\ —— 门禁基线（纯数据）

> 本目录 = 门禁的**基线/清册单源**；脚本内禁内联数值基线（守卫：`Operation\DSL\Check-GateBaselines.ps1`）。

## 命名与类型

| 项 | 约定 |
|---|---|
| 文件名 | `<Gate>.psd1`（与 `GateManifests.psd1` 的键同名；`Check-<Gate>.ps1` 用 `Import-GateBaseline -Name <Gate>` 装载） |
| 前缀 `_` | 非单件门禁基线（清册/登记表），如 `_inline-baselines.psd1` / `_generated-artifacts.psd1` |
| 格式 | `Import-PowerShellDataFile` 可读的纯数据（仅字面量/数组/哈希表；禁表达式、禁变量） |
| 每条基线 | **必带 `Origin`**，取值 `Derived` / `Debt` / `Verdict`（见下） |

## 事实四分（Origin 的语义，禁混用）

| Origin | 含义 | 处置 | 示例 |
|---|---|---|---|
| `Derived` | 可由产物/声明机械复算 | **必须由生成器产出**；不得长期内联 | `_generated-artifacts.psd1` 登记的产物数、键族文法落点数 |
| `Debt` | 存量债务棘轮（人工登记，只减不增） | 保留人工，但**必带 `DueWhen`（销账条件）**；**禁由生成器重算** | `KeyAddressingBaselines`、`Check-SessionDuplication` 目标表 |
| `Verdict` | 裁决类（不可生成） | 只记裁决 + 重裁触发条件 | `PostureRequireFlowRequired`、U17 loc 分域 |

> ⚠️ `Debt` 一旦被生成器重算成"当前值"，棘轮即失真为恒绿（假绿）。这是 `Origin` 必须显式标注的原因（对齐 `ANTI-PROXY-SCOREBOARD`）。

## 生成物哨兵与漂移判据

同目录 `_generated-artifacts.psd1` 登记「生成物 ↔ 生成器 ↔ 易变字段」。守卫
`Operation\DSL\Check-GeneratedArtifactsDrift.ps1`：① 产物存在 ② 哨兵在位 ③ 复算到 temp 逐字比对 ⇒ 漂移即红。

| 产物 | 生成器 | 易变字段 |
|---|---|---|
| `Frame\CsharpPrj\generated\Directory.Build.props.modules.generated.props` | `Share\Solution\Generate-ModulePaths.ps1` | `生成时间:` 行 |
| `Frame\PowerShellScript\PScriptManager\Framework\ps-function-index.json` | `Build-FunctionIndex.ps1`（`-IndexOut`） | `builtAt` |
| `Frame\CsharpPrj\generated\ContractIndex.json` | `Share\Index\Build-ContractIndex.ps1`（`-Update`） | `builtAt` |

`Frame\CsharpPrj\Generate-CsharpBakeManifest.ps1` 不在本表：它是**烘焙就绪判定器**（写 manifest + stamp，`generatedUtc` 天然易变），新鲜度判据由 `BakeFramework` 件承担。

## 迁移进度（只减不增）

| 面 | 状态 |
|---|---|
| `Baselines\` 规范根（12 件 `<Gate>.psd1`） | ✅ `KeyGrammar` · `RecognizerVocabulary` · `NodeKeyAddress` · `NodeLocPlacement` · `NodeLocConsistency` · `NodeHierarchyRefs` · `NodeCollectPort` · `ObservabilitySurface` · `ReferenceVocabulary` · `AgentOrchestrationFlow` · `OperationLayerIsolation` · `AgentFrameworkSingleSource`（SR-2a 并入；文件名 = `GateManifests` 键名，U7 契约） |
| **遗留子根** `Modules\Node\Baselines\` | ✅ **已并入规范根并删除**（禁第二基线根；U8「退役名再入即红」强制） |
| 内联数值基线（`$*Baseline* = <n>` 形态） | ✅ **0 处 / 0 件**（起点 15 处 / 9 件；实测命令：`Check-GateBaselines.ps1 -Check -ShowDetail`） |
| 事实行类型（`@{ Value; Origin; DueWhen }`） | ✅ 全部事实键已改事实行（**U9 强制**；此前裸值可静默通过 U3 ⇒ 外置基线可退化为「另一种散落常量」） |
| `_inline-baselines.psd1` 清册 | ✅ 已清空（终端态；新增内联基线无自动在册路径） |
| **第二形态**：内联目标表（`<X> = <n>` 键值行） | ⏳ SR-1c：实测 **47 行 / 2 件**（`Check-SessionDuplication.ps1` 32 · `Check-ContentSingleSource.ps1` 15）+ `Check-AgentOrchestrationFlow.ps1` 的两张 `[ordered]@{}` 表（7 行）⇒ 待整体外置为 `<Gate>.psd1` |

## 判据清单（守卫 = `Check-GateBaselines.ps1`）

| # | 判据 | 拦什么 |
|---|---|---|
| U1 | 内联数值基线清点 | 脚本内出现 `$*Baseline* = <十进制>` |
| U2 | 在册性 | 内联基线未在 `_inline-baselines.psd1` 在册（拦新增；只减不增） |
| U3 | 条目类型 | `Origin` 非法（∉ Derived/Debt/Verdict）/ Debt·Verdict 缺 `DueWhen` |
| U4 | 正对照 | 基线文件数 0 或 K1 事实 < 7（判据无标的 ⇒ 假绿） |
| U5 | 负对照 | 合成样本未捕获 / 非赋值式样被误捕（正则被改坏） |
| U6 | 可见化（不拦） | 清册 `[待迁移]` / `[可销账]` 台账 |
| U7 | 接线一致性 | psd1 未被同名消费脚本装载；事实键名未出现于消费脚本（拼写漂移 ⇒ 取 `$null` 静默降级为 0） |
| U8 | 命名唯一性 | 规范函数名定义数 > 1；**退役名再入**（`Import-NodeGateBaseline` / `Get-LegacyBaselinesRoots`） |
| U9 | 事实行类型强制 | 事实键是裸值 / 行元素缺 `Origin`（否则 U3 静默跳过） |
| U10 | 声明面路径存在性 | 声明面（`GateManifests` / `Baselines\*.psd1` / 本 README / 门禁脚本头注）引用的 `…\Baselines\<X>.psd1` 不存在 |
| U11 | 行形态唯一 | 事实行出现第二形态（非 `Value`，如 `Baseline`/`RelativePath`）⇒ 消费端读法分裂 |

> 扫描面 = 「`Frame\PowerShellScript` 目录规约」 ∪ 「`GateManifests` 登记的 `Path` 所在目录」—— 后者覆盖 `Modules`/`Projects` 侧门禁（此前硬编码单根 ⇒ 覆盖漏洞）。

## 命名唯一性（U8，SR-1f/SR-2a 实测教训）

`Import-GateBaseline` **曾同名两实现**（本目录 `GateBaseline.psm1` 与 `Modules\Node\NodeGateCommon.ps1`，根不同）
⇒ 消费脚本运行时**绑到旧实现**（找遗留子根的 psd1 不存在）而抛错；
`ps-function-index.json` 早已把它记为「多属主」，但**无判据消费该记录**。

| 名 | 状态 | 守卫 |
|---|---|---|
| `Import-GateBaseline`（根 = `PowerShellScript\Baselines\`） | ✅ 唯一实现 = `Baselines\GateBaseline.psm1` | U8（定义数 > 1 即红） |
| `Get-BaselinesRoot` / `Get-BaselineFiles` / `Test-BaselineEntryTyped` / `Get-InlineBaselineRegex` | ✅ 同件唯一实现 | 同上 |
| `Import-NodeGateBaseline` / `Get-LegacyBaselinesRoots` | ❌ **已退役**（SR-2a 并根 ⇒ 禁第二基线根） | U8（定义数 > 0 即红） |

## 用法

```powershell
# 在门禁脚本内（**必须** Import-Module：.psm1 点源不带函数进调用方作用域）
Import-Module (Join-Path $PSScriptRoot '..\..\Baselines\GateBaseline.psm1') -Force
$b = Import-GateBaseline -Name KeyGrammar
$k2 = [int]$b.KeySlice.Value          # 事实行：Value + Origin + DueWhen

# 内联基线守卫（报告模式 / 严格模式；U1–U10）
& Frame\PowerShellScript\Operation\DSL\Check-GateBaselines.ps1
& Frame\PowerShellScript\Operation\DSL\Check-GateBaselines.ps1 -Check -ShowDetail

# 生成物漂移守卫
& Frame\PowerShellScript\Operation\DSL\Check-GeneratedArtifactsDrift.ps1 -Check
```
