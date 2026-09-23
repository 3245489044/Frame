#requires -Version 5.1
# @requires Invoke-TemplateReplace, ConvertTo-CompoundKey, Test-ValidIdentifier, Get-NameHead, Get-FirstMatchingPrefix
# @requires Test-MarkdownTableLine, Test-CodeFenceLine, Get-BacktickTokens, Get-MarkdownFirstCell, Test-MarkdownHeadingLine, Test-MarkdownSectionHeader
# @requires Resolve-DefinitionForm, Get-CrReferenceTokens, ConvertFrom-CrReference, ConvertFrom-LegacyCrReference
<#
.SYNOPSIS
    clinerules 符号引用校验器 + 索引编译器 + references.md 生成器

.DESCRIPTION
    编译模式：符号索引不再由人工登记表承载，而是由本脚本从源文件定义点扫描编译生成（幂等）。
    references.md 为【纯编译产物】——整份文档（含 §0/§1.1/§2/§3/§4 规则章节）由
    `-UpdateIndex` 从（dsl.md 枚举 + 源定义点扫描 + 脚本行为契约）模板化生成，禁止手改。
    修改 references.md 内容的唯一途径 = 修改 dsl.md（枚举/文法）/ 源库定义点（符号）/
    本脚本（校验与生成逻辑）后重新运行 `-UpdateIndex`。
    索引唯一权威 = 源文件中的定义点（[AX]/[Def]/[Type]/[Gate]/[SM]/%/## TH-n 等）。

    两遍扫描，实现 dsl.md §4.8 各类检查 + 反向引用报告：
    检查1 悬空引用: [CR: LIB:STYP.NAME] 在编译索引中不可解析
    检查2 未索引定义: 已废弃——编译模式索引=源，无独立登记表（保留字段输出空集）
    检查3 废弃指向:   [CR] 指向 dep=Y 的符号（定义处 `dep:` 标注 → 编译出 `#dep`）
    检查4 循环依赖:   def 权威间 A→B ∧ B→A（按定义点区块归属扫描引用图）
    附:    反向引用报告（谁引用了 X）

    语法规范: [CR: dsl:Sec.4] (clinerules/dsl.md)
    索引权威: 源文件定义点（references.md 为整份生成产物，禁止手工编辑）

.PARAMETER RulesRoot
    clinerules 根目录（默认 ..\..\clinerules 相对本脚本）

.PARAMETER OutputJson
    反向引用报告输出路径（默认 ..\ref_report.json 相对本脚本）

.PARAMETER AllowLegacy
    容忍旧形态 [CR: file.md:anchor]（仅标记"待迁移"，不阻断）。默认开启。

.PARAMETER UpdateIndex
    整份重生成 references.md（全部规则章节由模板 + dsl.md 枚举 + 源扫描索引渲染）。

.EXAMPLE
    .\Check-ClinerulesReferences.ps1
.EXAMPLE
    .\Check-ClinerulesReferences.ps1 -RulesRoot ..\..\clinerules -OutputJson ..\ref_report.json
.EXAMPLE
    .\Check-ClinerulesReferences.ps1 -UpdateIndex
#>
[CmdletBinding()]
param(
    [string]$RulesRoot,
    [string]$OutputJson,
    [string]$PromptsDir,
    [switch]$AllowLegacy = $true,
    [switch]$UpdateIndex
)

# param 默认值延迟计算: [CmdletBinding()] 脚本中 param 块内 $PSScriptRoot 不可用（PS 5.1 限制）
if (-not $RulesRoot)  {
    # 哨兵定位：从 $PSScriptRoot 上溯找 .clinerules 目录（跳过子模块边界）
    $search = $PSScriptRoot
    $found = $null
    while ($search -and $search -ne (Split-Path $search -Parent)) {
        $candidate = Join-Path $search '.clinerules'
        if (Test-Path -LiteralPath $candidate) { $found = $candidate; break }
        $search = Split-Path $search -Parent
    }
    if ($found) {
        $RulesRoot = $found
    } else {
        # 退路：上溯四级到 Main 根
        $fallback = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..\.clinerules'))
        if (Test-Path -LiteralPath $fallback) {
            $RulesRoot = $fallback
        } else {
            Write-Error "找不到 .clinerules 目录（已从 $PSScriptRoot 上溯至仓库根）"
            exit 2
        }
    }
}
if (-not $OutputJson) { $OutputJson = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\ref_report.json')) }

$ErrorActionPreference = 'Stop'

# 模块加载（IoC 按需加载, 依赖声明见文件头 # @requires; 见 Modules\Import-Modules.ps1）
# ---- 模块加载（路径无关：按真实文件名向上自愈定位）----
$__psp = $PSScriptRoot
while ($__psp -and -not (Test-Path -LiteralPath (Join-Path $__psp 'Modules\Import-Modules.ps1'))) { $__psp = Split-Path -Parent $__psp }
if (-not $__psp) { throw '无法定位 Modules\Import-Modules.ps1（PS 框架根）' }
. (Join-Path $__psp 'Modules\Import-Modules.ps1')

# ---------- 注册枚举（单源 = dsl.md §4.4/4.5/4.6；以下硬编码为 dsl.md 解析失败的兜底） ----------
$LIB_LIST    = @('axioms','formula','theorems','logic','project','dsl','instruction')
$STYP_LIST   = @('AX','BAX','BAX-CORE','BAX-IDI','BAX-TIME','BAX-C','BAX-D','BAX-X',
                 'AX-CORE','AX-META','AX-FRAME','Def','Type','Param','Symbol',
                 'Profile','Pattern','Gate','SM','SEP','DR','LO','TH','Schema','Sec')
$INTENT_LIST = @('def','deriv','inst','consume','align','see')

# 从 dsl.md §4.4/4.5/4.6 表格解析注册枚举（覆盖硬编码兜底）

# 参与索引编译的库（project 无前缀符号定义 / dsl 为文法语料 / references 为生成产物本身）
# 范围声明：project/dsl/instruction 库中的引用需经 `Sec` 节引用或旧形态文件引用解析（见主循环）
$COMPILE_LIBS = @('axioms','formula','theorems','logic')

# ---------- 编译索引（key = "LIB/STYP/NAME" → PSCustomObject） ----------
$script:Index = @{}

function Add-IndexEntry {
    param([string]$Lib,[string]$Styp,[string]$Name,[string]$Intent,[string]$Version,[bool]$Dep,[string]$Location)
    if ($Name -eq '') { return }
    $key = "$Lib/$Styp/$Name"
    if (-not $script:Index.ContainsKey($key)) {
        $script:Index[$key] = [PSCustomObject]@{
            Name=$Name; Styp=$Styp; AnchorId="$Styp-$Name"
            Intent=$Intent; Version=$Version; Dep=$Dep; Location=$Location
        }
    }
}

# ---------- 扫描定义点（索引编译源） ----------

#  覆盖定义（重构 v2: 正则解析委托 StringTools.Clinerules, 与旧逻辑行为一致）  
# ============================================================  
function Get-DefinitionNames {  
    param([string]$FilePath,[string]$Lib)  
    $defs = @()  
    $seen = @{}  
    $lines = Get-Content $FilePath -Encoding UTF8  
    for ($i = 0; $i -lt $lines.Count; $i++) {  
        $line = $lines[$i]  
        if (Test-MarkdownTableLine $line) { continue }  
        $def = Resolve-DefinitionForm -Line $line  
        if (-not $def) { continue }  
        foreach ($nm in $def.Names) {  
            $key = $Lib + '/' + $def.Styp + '/' + $nm  
            if (-not $seen.ContainsKey($key)) {  
                $seen[$key] = $true  
                $defs += [PSCustomObject]@{ Lib=$Lib; Styp=$def.Styp; Name=$nm; Line=$i+1; Intent=$def.Intent; Dep=$def.Dep }  
            }  
        }  
    }  
    return $defs  
}  
  
function Read-EnumsFromDsl {  
    param([string]$DslPath)  
    if (-not (Test-Path $DslPath)) { Write-Verbose 'dsl.md 不存在, 使用硬编码枚举'; return }  
    $lines = Get-Content $DslPath -Encoding UTF8  
    $inSec = ''; $libs = @(); $styps = @(); $intents = @()  
    foreach ($line in $lines) {  
        if    (Test-MarkdownSectionHeader -Line $line -Number '4.4') { $inSec = 'lib';    continue }  
        elseif (Test-MarkdownSectionHeader -Line $line -Number '4.5') { $inSec = 'styp';   continue }  
        elseif (Test-MarkdownSectionHeader -Line $line -Number '4.6') { $inSec = 'intent'; continue }  
        elseif (Test-MarkdownHeadingLine -Line $line -MinLevel 2)    { if ($inSec) { $inSec = '' } continue }  
        if ($inSec -eq '') { continue }  
        $cell = Get-MarkdownFirstCell -Line $line  
        if ($null -ne $cell) {  
            foreach ($item in (Get-BacktickTokens -InputObject $cell)) {  
                switch ($inSec) {  
                    'lib'    { if ($item -in $libs)    { continue } $libs    += $item }  
                    'styp'   { if ($item -in $styps)   { continue } $styps   += $item }  
                    'intent' { if ($item -in $intents) { continue } $intents += $item }  
                }  
            }  
        }  
    }  
    if ($libs.Count)    { $script:LIB_LIST    = $libs }  
    if ($styps.Count)   { $script:STYP_LIST   = $styps }  
    if ($intents.Count) { $script:INTENT_LIST = $intents }  
    Write-Verbose ('dsl.md 枚举: LIB=' + ($script:LIB_LIST -join ',') + ' | STYP=' + ($script:STYP_LIST -join ',') + ' | INTENT=' + ($script:INTENT_LIST -join ','))  
}  
# ---------- 构建编译索引（索引唯一权威=源文件定义点） ----------
function Build-IndexFromSources {
    foreach ($libName in $COMPILE_LIBS) {
        $file = Join-Path $RulesRoot "$libName.md"
        if (-not (Test-Path $file)) { Write-Verbose "跳过缺失库文件: $file"; continue }
        foreach ($d in (Get-DefinitionNames -FilePath $file -Lib $libName)) {
            Add-IndexEntry -Lib $d.Lib -Styp $d.Styp -Name $d.Name -Intent $d.Intent -Version '1.0' -Dep $d.Dep -Location "$($d.Line)"
        }
    }
    Write-Verbose "编译索引符号总数: $($script:Index.Count)"
}

# ---------- 扫描 [CR] 引用 ----------
# 跳过表格行（迁移对照表/速查表, 展示性引用）与代码围栏（BNF/公式块, 伪 [CR] 文本）
function Get-CRReferences {
    param([string]$FilePath)
    $refs = @()
    $inFence = $false
    $lines = Get-Content $FilePath -Encoding UTF8
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if (Test-MarkdownTableLine $line) { continue }
        if (Test-CodeFenceLine $line) { $inFence = -not $inFence; continue }
        if ($inFence) { continue }
        # 兼容: 处理一行多个 [CR]（含旧形态 [CR axioms.md:X] 无冒号）
        $pattern = '\[CR[:\s]+\s*([^\]]+?)\s*\]'
        $ms = [regex]::Matches($line, $pattern)
        foreach ($m in $ms) {
            $inner = $m.Groups[1].Value.Trim()
            $obj = [PSCustomObject]@{
                File=$FilePath; Line=$i+1; Raw=$m.Value; Lib=$null; Styp=$null; Name=$null
                Intent='def'; Legacy=$false; Valid=$true; Error=''
            }
            # 尝试严格形态: LIB:STYP.NAME[.SUB...][@INTENT]
            # NAME 允许数字开头（Sec 章节引用如 Sec.4.5）; NAME 允许含点（[Def] 符号如 op.needsSync）
            $strict = '^([a-z]+):([A-Za-z-]+)\.([A-Za-z0-9_][A-Za-z0-9_.-]*?)(?:@(def|deriv|inst|consume|align|see))?$'
            if ($inner -match $strict) {
                $obj.Lib = $Matches[1]; $obj.Styp = $Matches[2]; $obj.Name = $Matches[3]
                if ($Matches[4]) { $obj.Intent = $Matches[4] }
            } else {
                # 旧形态兼容
                $obj.Legacy = $true
                $legacy1 = '^(?:([a-z]+)\.md|([a-z]+))[:\s]+([A-Za-z_][A-Za-z0-9_.-]*)'
                if ($inner -match $legacy1) {
                    $obj.Lib = if ($Matches[1]) { $Matches[1] } else { $Matches[2] }
                    $obj.Name = $Matches[3]
                }
                # 尽力推断 STYP（旧形态无法可靠识别时留空）
            }
            $refs += $obj
        }
    }
    return $refs
}

# ---------- 检查4: 循环依赖（def 权威间 A→B ∧ B→A） ----------
# 引用按"最近上方定义点"归属到符号；仅 intent=def 的严格引用构成权威边
function Get-CyclicDefDependencies {
    param([string]$RulesRoot)
    $edges = @{}   # sourceKey → List[targetKey]
    foreach ($libName in $COMPILE_LIBS) {
        $file = Join-Path $RulesRoot "$libName.md"
        if (-not (Test-Path $file)) { continue }
        $defs = @(Get-DefinitionNames -FilePath $file -Lib $libName | Sort-Object { $_.Line })
        if ($defs.Count -eq 0) { continue }
        $refs = @(Get-CRReferences -FilePath $file)
        for ($idx = 0; $idx -lt $defs.Count; $idx++) {
            $d = $defs[$idx]
            $fromLine = $d.Line
            $toLine = if ($idx + 1 -lt $defs.Count) { $defs[$idx + 1].Line - 1 } else { 2147483647 }
            $srcKey = "$($d.Lib)/$($d.Styp)/$($d.Name)"
            if (-not $script:Index.ContainsKey($srcKey)) { continue }
            foreach ($r in $refs) {
                if ($r.Legacy) { continue }
                if ($r.Line -lt $fromLine -or $r.Line -gt $toLine) { continue }
                if ($r.Intent -ne 'def') { continue }   # 仅 def 权威构成循环依赖
                if ($r.Styp -eq 'Sec') { continue }     # 节引用不构成权威依赖
                $tgtKey = "$($r.Lib)/$($r.Styp)/$($r.Name)"
                $resolvedKey = $null
                if ($script:Index.ContainsKey($tgtKey)) { $resolvedKey = $tgtKey }
                else {
                    $nameHead = Get-NameHead -Name $r.Name
                    $headKey = "$($r.Lib)/$($r.Styp)/$nameHead"
                    if ($script:Index.ContainsKey($headKey)) { $resolvedKey = $headKey }
                }
                if (-not $resolvedKey) { continue }
                if ($resolvedKey -eq $srcKey) { continue }   # 自环忽略
                if (-not $edges.ContainsKey($srcKey)) { $edges[$srcKey] = New-Object System.Collections.Generic.List[string] }
                if ($resolvedKey -notin $edges[$srcKey]) { $edges[$srcKey].Add($resolvedKey) }
            }
        }
    }
    # DFS 三色找环（长度 >= 2），以成员集合标准化去重
    $state = @{}    # 0=未访问 1=在栈 2=完成
    $stack = New-Object System.Collections.Generic.List[string]
    $cycles = New-Object System.Collections.Generic.List[string[]]
    $cycleSet = New-Object System.Collections.Generic.HashSet[string]
    function Visit-CycleNode([string]$k) {
        $state[$k] = 1
        $stack.Add($k)
        if ($edges.ContainsKey($k)) {
            foreach ($n in @($edges[$k])) {
                if (-not $state.ContainsKey($n)) { Visit-CycleNode $n; continue }
                if ($state[$n] -eq 1) {
                    $start = $stack.IndexOf($n)
                    if ($start -ge 0 -and ($stack.Count - $start) -ge 2) {
                        $cycle = @($stack[$start..($stack.Count - 1)])
                        $canonKey = (($cycle | Sort-Object) -join '>')
                        if ($cycleSet.Add($canonKey)) { [void]$cycles.Add(@($cycle)) }
                    }
                }
            }
        }
        [void]$stack.RemoveAt($stack.Count - 1)
        $state[$k] = 2
    }
    foreach ($k in $edges.Keys) { if (-not $state.ContainsKey($k)) { Visit-CycleNode $k } }
    Write-Verbose "循环依赖检测: $($cycles.Count) 个环"
    return @($cycles)
}

# 展示键: "LIB/STYP/NAME" → "LIB:STYP.NAME"（委托 StringTools ConvertTo-CompoundKey）
function Format-SymbolKey([string]$Key) {
    if ($Key -match '^([^/]+)/([^/]+)/(.+)$') {
        return ConvertTo-CompoundKey -Segments @($Matches[1], $Matches[2], $Matches[3]) -Separators @(':','.')
    }
    return $Key
}

# ---------- 整份生成 references.md（纯编译产物, 含全部规则章节） ----------
function Generate-ReferencesDocument {
    param([string]$RefPath)
    if (-not (Test-Path $RefPath)) { Write-Warning "references.md 不存在, 无法生成"; return }

    # §1.2 索引体
    $indexSb = New-Object System.Text.StringBuilder
    [void]$indexSb.AppendLine("<!-- INDEX:BEGIN -->")
    [void]$indexSb.AppendLine("")
    foreach ($libName in $COMPILE_LIBS) {
        $entries = @($script:Index.GetEnumerator() |
                     Where-Object { $_.Key -like "$libName/*" } |
                     ForEach-Object { $_.Value } |
                     Sort-Object Styp, Name)
        if ($entries.Count -eq 0) { continue }
        [void]$indexSb.AppendLine("#### $libName 库")
        [void]$indexSb.AppendLine("")
        foreach ($e in $entries) {
            $sym = "$($e.Styp).$($e.Name)"
            if ($e.Intent -ne 'def') { $sym += "@$($e.Intent)" }
            if ($e.Dep) { $sym += " #dep" }
            [void]$indexSb.AppendLine($sym)
        }
        [void]$indexSb.AppendLine("")
    }
    [void]$indexSb.AppendLine("<!-- INDEX:END -->")
    $indexBody = $indexSb.ToString().TrimEnd("`r","`n")

    # §2 校验器约定（由脚本实际实现自省生成, 杜绝文档与实现脱节）
    $checkItems = @(
        "- **检查 1 悬空引用**：``LIB:STYP.NAME`` 在符号索引中不可解析（当前已实现）",
        "- **检查 3 废弃指向**：``[CR]`` 指向编译出 ``#dep`` 的符号（当前已实现）",
        "- **检查 4 循环依赖**：``def`` 权威间 ``A→B ∧ B→A``（当前已实现）"
    )
    $checkListText = ($checkItems -join "`n")

    # 整份模板（单引号 here-string: 反引号与 $ 原样保留；占位符为 @@NAME@@）
    $template = @'
# 符号引用注册中心（Reference Registry）

> **层级**：元层（元数据） | **职责**：全库 `[CR]` 结构化符号引用的注册、校验与变更治理。
> **文法权威**：`[CR: dsl:Sec.4]`（本文件不重复定义文法；文法、定界符、枚举表见 dsl.md §4）。
> **生成声明**：本文件为【纯编译产物】——由 `Check-ClinerulesReferences.ps1 -UpdateIndex` 整份生成（含全部规则章节），索引唯一权威 = 源文件定义点。禁止手工编辑；内容变更须作用于 dsl.md（枚举/文法）、源库定义点（符号）、脚本（校验与生成逻辑）后重新生成。
> **范围**：clinerules/ 六库（axioms/formula/theorems/logic/project/dsl）+ Instruction/（预留扩展）。其中符号索引编译库为 `@@LIBS@@`；project/dsl/instruction 库不参与符号索引编译，其引用经 `Sec` 节引用（如 `[CR: project:Sec.1.8@see]`）或旧形态文件引用解析。
> **索引模式**：编译式——符号索引由 `Check-ClinerulesReferences.ps1` 从源文件定义点扫描编译（幂等），本文件为只读快照。

---

## §0 核心约定

| 约定 | 内容 |
|------|------|
| 编译式索引 | 索引唯一权威 = 源文件定义点（`[AX]`/`[Def]`/`[Type]`/`[Gate]`/`[SM]`/`%`/`## TH-n` 等）；references.md 为编译产物 |
| 物理锚点 = 逻辑符号 | 定义处 `<a id="{锚点ID}">` 与符号主键严格一致（生成规则 `[CR: dsl:Sec.4.7]`） |
| 单源事实 | 每个符号**至多一个** `def` 权威点；`deriv`/`inst` 必须最终可达某 `def` |
| 废弃即禁入 | 定义处 `dep:"replaced-by(X)"` 标注 → 编译出 `#dep`，禁止被新 `[CR]` 指向（linter 强制） |
| 锚点 ID 不可变 | 变更 = 新增 ID + 旧 ID 置 `dep` |
| 存量兼容 | 旧形态引用不阻断解析，linter 标记"待迁移"（映射见 `[CR: dsl:Sec.4.3]`） |

---

## §1 符号索引（编译产物）

### 1.1 编译规则

| 源文件形态 | 编译产物 | 说明 |
|------|----------|------|
| `[AX] StateSnapshot:` | `AX.StateSnapshot` | 缺省意图 `def` 不写 |
| `[AX-CORE] AxiomHierarchyStructure:` | `AX-CORE.AxiomHierarchyStructure` | 类型前缀即符号类型 |
| `[Def] op.needsSync:` | `Def.op.needsSync` | 含点符号原样 |
| `% MAX_PROGRESS=int:3` | `Param.MAX_PROGRESS` | 配置参数 |
| `## TH-6: ...` | `TH.TH-6@deriv` | 推论自动 `deriv` |
| `[Gate]/[SM]/[SEP]/[Profile]/[Pattern]` | 对应 STYP | 语义类型映射 |
| 定义处 `dep:` 标注 | 行尾 `#dep` | 废弃自动跟随 |

### 1.2 索引数据（源扫描编译快照）

> 编译快照：@@TS@@ | 源扫描 @@SYMBOLS@@ 个符号（索引权威=源文件, 本区勿手改）

@@INDEX@@

---

## §2 校验器约定

校验器（`PowerShellScript/DSL/Check-ClinerulesReferences.ps1`）两遍扫描：

1. **索引编译**：扫描参与库全部定义点 → 编译符号索引（权威源，即本文件 §1 快照）；
2. **引用解析**：解析全部 `[CR]` 引用 → 对照索引执行：

@@CHECKLIST@@

- 当前状态：严格引用 @@STRICT@@ 条、旧形态待迁移 @@LEGACY@@ 条（本行由脚本在生成时更新）。

附：输出**反向引用报告**（`谁引用了 X`）。输出：`ref_report.json`。`-UpdateIndex` 整份重编译本文件（重建 §1.2 索引与全部规则章节）。

---

## §3 变更流程

| 场景 | 必须动作 |
|------|----------|
| 新增符号 | 仅需在源文件定义（前缀 + 锚点 ID）→ 运行 `-UpdateIndex` 重编译 |
| 修改符号语义 | 更新定义处；若破坏兼容 → 执行「deprecated」流程 |
| 废弃符号 | 定义处 `dep:"replaced-by(Y)"` → `-UpdateIndex` 重编译 → 反向引用清单中 `[CR]` 限期改指向 Y |
| 锚点重命名 | 视为废弃旧 ID + 注册新 ID（锚点 ID 不可变原则） |
| 新增库/类型/意图 | 仅更新 dsl.md §4.4/4.5/4.6 枚举（脚本 `-UpdateIndex` 自动读取）→ 重编译生成，文法不变 |

---

## §4 互操作（对接既有规则）

| 现有规则 | 对接方式 |
|----------|----------|
| `DependencyCycleDetection` / `ANTI-CYCLIC-AGGREGATION`（formula §6.5） | 检查 4 的循环依赖检测即其形式化实现 |
| `dep = "replaced-by(X)"` 元数据键 | 编译索引 `#dep` 权威来源 |
| `src = "[CR: ...]"` 元数据键 | 引用中 `deriv`/`inst`/`consume` 意图的结构化别名 |
| axioms.md §10 符号速查 | 与其保持一致；§1 为机器校验编译快照 |
'@

    # 模板占位符整批替换（委托 StringTools Invoke-TemplateReplace）
    $templateParams = @{
        TS        = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
        INDEX     = $indexBody
        CHECKLIST = $checkListText
        LEGACY    = "$($script:LegacyCount)"
        LIBS      = ($COMPILE_LIBS -join '/')
        SYMBOLS   = "$($script:Index.Count)"
        STRICT    = "$($script:StrictRefCount)"
    }
    $doc = Invoke-TemplateReplace -InputObject $template -Parameters $templateParams
    Set-Content -Path $RefPath -Value $doc -Encoding UTF8
    Write-Host "已整份重生成 references.md (符号 $($script:Index.Count)) 到: $RefPath" -ForegroundColor Yellow
}

# ---------- B3：prompts ↔ clinerules 声明对齐检查（真相分裂候选检测） ----------
# 目标：Agent/Config/prompts/*.system.md 里每条公理声明（[AX]/[SM]/[Gate]/[Strategy]/[Pattern]）
# 若在 clinerules 编译索引（axioms/formula/logic）中无同名定义点，则列为「prompts-only 候选」。
# 性质：**信息性**（不阻断 exit；prompt 层操作指令如 ToolCallGuard/Ps1First/L1Template 等
# 属合法 prompt-only 声明，无需下沉 clinerules）。用途 = 变更影响清单：新增/删除时人工确认是否需双向同步。
function Test-PromptsAlignment {
    param([string]$PromptsDir, [string]$RulesRoot)
    if (-not $PromptsDir -or -not (Test-Path $PromptsDir)) { return @() }
    $mis = @()
    # 先建 clinerules 的 lib 名 → 定义符号集（AX/SM/Gate/Strategy/Pattern），供跨库对照
    $clinerulesDefs = @{}   # name(小写) → 找到的 lib:styp
    foreach ($f in (Get-ChildItem $RulesRoot -Filter *.md -File)) {
        $lib = [System.IO.Path]::GetFileNameWithoutExtension($f.Name).ToLower()
        if ($lib -notin @('axioms','formula','logic')) { continue }
        foreach ($def in (Get-DefinitionNames -FilePath $f.FullName -Lib $lib)) {
            $clinerulesDefs[$def.Name.ToLower()] = "$lib/$($def.Styp)"
        }
    }
    foreach ($pf in (Get-ChildItem $PromptsDir -Filter *.md -File)) {
        foreach ($line in (Get-Content $pf.FullName -Encoding UTF8)) {
            $t = $line.Trim()
            if ($t -notmatch '^\[(AX|AX-CORE|AX-META|AX-FRAME|BAX|SM|Gate|Strategy|Pattern)\]\s+([A-Za-z0-9_-]+)\s*:') { continue }
            $name = $Matches[2]
            if (-not $clinerulesDefs.ContainsKey($name.ToLower())) {
                $mis += [pscustomobject]@{
                    PromptFile = $pf.Name
                    Name       = $name
                    Reason     = "prompts-only 声明：clinerules 索引无同名定义点（prompt 层操作指令或需下沉候选）"
                }
            }
        }
    }
    return $mis
}
$promptMisalignments = @(Test-PromptsAlignment -PromptsDir $PromptsDir -RulesRoot $RulesRoot)

# ---------- 主流程 ----------
$danglingRefs = @()
$depRefs = @()
$legacyRefs = @()

Write-Host "== clinerules 引用校验器 + 索引编译器 + references.md 生成器 ==" -ForegroundColor Cyan
Write-Host "规则根: $RulesRoot"

# 枚举单源化：从 dsl.md 读取注册枚举
Read-EnumsFromDsl -DslPath (Join-Path $RulesRoot 'dsl.md')

Build-IndexFromSources

# 第二遍：解析全部 [CR] 引用 → 悬空/废弃检查 + 反向引用收集
$reverseRefs = @{}   # target → List[位置]
$allRefCount = 0
$strictRefCount = 0
$scannedFiles = @()
foreach ($f in (Get-ChildItem $RulesRoot -Filter *.md -File)) {
    $lib = [System.IO.Path]::GetFileNameWithoutExtension($f.Name).ToLower()
    if ($lib -notin $LIB_LIST) { continue }
    $scannedFiles += $f
    foreach ($r in (Get-CRReferences -FilePath $f.FullName)) {
        $allRefCount++
        if ($r.Legacy) {
            $legacyRefs += $r
            continue
        }
        $strictRefCount++
        if ($r.Lib -notin $LIB_LIST) {
            $r.Valid = $false; $r.Error = "未注册库标识: $($r.Lib)"; $danglingRefs += $r; continue
        }
        if ($r.Styp -notin $STYP_LIST) {
            $r.Valid = $false; $r.Error = "未注册符号类型: $($r.Styp)"; $danglingRefs += $r; continue
        }
        # 统一展示键（LIB/STYP/NAME → LIB:STYP.NAME, 委托 Format-SymbolKey）
        $target = Format-SymbolKey -Key "$($r.Lib)/$($r.Styp)/$($r.Name)"
        # Sec 类型: 章节锚点（提示性引用）——不进入符号索引, 跳过索引解析
        if ($r.Styp -eq 'Sec') {
            if (-not $reverseRefs.ContainsKey($target)) { $reverseRefs[$target] = New-Object System.Collections.Generic.List[string] }
            $reverseRefs[$target].Add("$($f.Name):$($r.Line)")
            continue
        }
        # 索引解析：完整名优先，其次取点链首段（SUB 子谓词定位, 见 dsl.md §4.1）
        $resolved = $null
        $baseKey = "$($r.Lib)/$($r.Styp)/$($r.Name)"
        if ($script:Index.ContainsKey($baseKey)) {
            $resolved = $script:Index[$baseKey]
        } else {
            $nameHead = Get-NameHead -Name $r.Name
            $headKey = "$($r.Lib)/$($r.Styp)/$nameHead"
            if ($script:Index.ContainsKey($headKey)) {
                $resolved = $script:Index[$headKey]
            }
        }
        if (-not $resolved) {
            $r.Valid = $false; $r.Error = "悬空引用: $target 未在编译索引中"
            $danglingRefs += $r; continue
        }
        if ($resolved.Dep) {
            $r.Valid = $false; $r.Error = "废弃指向: $target 已置 dep"
            $depRefs += $r; continue
        }
        # 反向引用
        if (-not $reverseRefs.ContainsKey($target)) { $reverseRefs[$target] = New-Object System.Collections.Generic.List[string] }
        $reverseRefs[$target].Add("$($f.Name):$($r.Line)")
    }
}

# 第三遍：检查4 循环依赖（def 权威间）
$cycles = @(Get-CyclicDefDependencies -RulesRoot $RulesRoot)

# 供生成器使用的全局计数
$script:LegacyCount = $legacyRefs.Count
$script:StrictRefCount = $strictRefCount

# ---------- 报告输出 ----------
$report = [ordered]@{
    timestamp = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
    summary = [ordered]@{
        files_scanned = $scannedFiles.Count
        index_symbols = $script:Index.Count
        references_total = $allRefCount
        references_strict = $strictRefCount
        errors_total = $danglingRefs.Count + $depRefs.Count + $cycles.Count
        legacy_pending = $legacyRefs.Count
        prompt_misalignments = $promptMisalignments.Count
    }
    check1_dangling = @($danglingRefs | ForEach-Object { [ordered]@{ file=$_.File; line=$_.Line; raw=$_.Raw; error=$_.Error } })
    check2_unindexed = @()   # 编译模式：索引=源扫描, 无独立登记表, 恒空
    check3_deprecated = @($depRefs | ForEach-Object { [ordered]@{ file=$_.File; line=$_.Line; raw=$_.Raw; error=$_.Error } })
    check4_cycles = @($cycles | ForEach-Object { [ordered]@{ cycle = @(@($_ | ForEach-Object { Format-SymbolKey $_ })) } })
    check5_prompt_alignment = @($promptMisalignments | ForEach-Object { [ordered]@{ file=$_.PromptFile; name=$_.Name; reason=$_.Reason } })
    legacy_migrations = @($legacyRefs | ForEach-Object { [ordered]@{ file=$_.File; line=$_.Line; raw=$_.Raw } })
    reverse_references = [ordered]@{}
}
foreach ($k in $reverseRefs.Keys) {
    $report.reverse_references[$k] = @($reverseRefs[$k])
}

# 输出统计
Write-Host ""
Write-Host "--- 校验结果 ---" -ForegroundColor Cyan
Write-Host "索引符号数 : $($script:Index.Count)"
Write-Host "悬空引用   : $($danglingRefs.Count)"   $(if ($danglingRefs.Count -gt 0) { Write-Host "  (FAIL)" -ForegroundColor Red } else { Write-Host "  (OK)" -ForegroundColor Green })
Write-Host "废弃指向   : $($depRefs.Count)"        $(if ($depRefs.Count -gt 0) { Write-Host "  (FAIL)" -ForegroundColor Red } else { Write-Host "  (OK)" -ForegroundColor Green })
Write-Host "循环依赖   : $($cycles.Count)"         $(if ($cycles.Count -gt 0) { Write-Host "  (FAIL)" -ForegroundColor Red } else { Write-Host "  (OK)" -ForegroundColor Green })
if ($PromptsDir) {
    Write-Host "prompts对齐 : $($promptMisalignments.Count)"  $(if ($promptMisalignments.Count -gt 0) { Write-Host "  (候选/信息性)" -ForegroundColor Yellow } else { Write-Host "  (OK)" -ForegroundColor Green })
}
Write-Host "旧形态待迁移: $($legacyRefs.Count)"    $(if ($legacyRefs.Count -gt 0) { Write-Host "  (待迁移)" -ForegroundColor Yellow })

if ($danglingRefs.Count -gt 0) {
    Write-Host ""
    Write-Host "--- 悬空引用明细 ---" -ForegroundColor Yellow
    $danglingRefs | Format-Table File,Line,Raw,Error -AutoSize | Out-String | Write-Host
}
if ($cycles.Count -gt 0) {
    Write-Host ""
    Write-Host "--- 循环依赖明细 ---" -ForegroundColor Yellow
    foreach ($c in $cycles) {
        Write-Host (($c | ForEach-Object { Format-SymbolKey $_ }) -join ' → ')
    }
}
if ($promptMisalignments.Count -gt 0) {
    Write-Host ""
    Write-Host "--- prompts-only 候选（信息性, 变更影响清单）---" -ForegroundColor Yellow
    $promptMisalignments | Format-Table Name,PromptFile,Reason -AutoSize | Out-String | Write-Host
}

# 整份生成 references.md（-UpdateIndex）
if ($UpdateIndex) {
    Generate-ReferencesDocument -RefPath (Join-Path $RulesRoot 'references.md')
}

# 保存报告
$outputDir = Split-Path $OutputJson -Parent
if ($outputDir -and -not (Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir -Force | Out-Null }
$report | ConvertTo-Json -Depth 6 | Set-Content -Path $OutputJson -Encoding UTF8
Write-Host ""
Write-Host "报告已输出: $OutputJson" -ForegroundColor Cyan

if ($danglingRefs.Count -gt 0 -or $depRefs.Count -gt 0 -or $cycles.Count -gt 0) { exit 1 } else { exit 0 }



  
# ============================================================  

