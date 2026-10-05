#requires -Version 5.1
<#
.SYNOPSIS
    识别词表守卫（RecognizerVocabulary）—— 「识别规则与键逻辑声明化」的机器判据（K1 / W-1…W-5）。

.DESCRIPTION
    声明面真源（唯一）：Projects\Plug\Agent\Config\recognizers.json
    （决策 A：JSON 过渡；上移语言面归 K4c。本门禁不引入第二份契约副本 —— 副本即第二事实源。）

    判据：
      R1 声明面存在 + JSON 合法 + 结构齐备（vocabulary / kinds[] / recognizers[] 非空）。
      R2 kind 唯一（无重复）且含兜底类 unrecognized（D-3 的兜底面）。
      R3 每条规则的 kind ∈ 词表（W-3：非法识别声明在装配前被拒）。
      R4 match 谓词仅结构形态（标量 / 数组∈集合 / {exists:bool}）—— 禁嵌套对象谓词（语义判据载体）。
      R5 W-2 零词表副本：ClineApi\src\recognize\*.ts 内不得出现声明面 kind 的**带引号字面量**
         （唯一允许 = 兜底类 unrecognized；词表清单只能来自声明面）。
      R6 负对照（防假绿）：去掉兜底类 / 注入 rule kind ∉ 词表 ⇒ 同一判据函数必须报错。
      R7 正对照（防「声明被删而门禁恒绿」）：kind 数 ≥ 基线 ∧ recognizer 数 ≥ 基线。
      R8 C# 镜像对账：Agent.Contract\NodeDomain\RecognizedKinds.cs 存在时逐 kind 对账；
         缺失 ⇒ 记入 pending（迁移期可见化，K3 收口；不算违规、不假绿）。
      R9 去重（决策 C / D-8）：**成节点**类别集 `live`（origin 缺省/非 Snapshot）与 `snapshot`（origin=Snapshot）
         的**交集必须为空** —— 同一内容不得由两个来源各写一遍（否则过程面出现重复行）；
         附负对照（注入一条 live/snapshot 重复成节点规则必须被捕获）。

    模式：默认报告模式（列出命中，不设退出码）；-Check 校验模式（违规 > 0 → 退出码 1）。

.PARAMETER Root
    仓库根目录（默认由本脚本位置上溯四级推导：Frame\PowerShellScript\Modules\Parsing → 仓库根）。

.PARAMETER Check
    校验模式：R1–R7 任一失效 → 退出码 1。

.PARAMETER ShowDetail
    输出逐条实测计数（含达标项与 pending）。

.EXAMPLE
    .\Check-RecognizerVocabulary.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()
$pending    = [System.Collections.Generic.List[string]]::new()

$specPath   = Join-Path $Root 'Projects\Plug\Agent\Config\recognizers.json'
$tsDir      = Join-Path $Root 'Frame\TypeScriptPrj\TsWorkspace\ClineApi\src\recognize'
$mirrorPath = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Contract\NodeDomain\RecognizedKinds.cs'

# 正对照基线（防「声明被删而门禁恒绿」）；只增不减。
# 2026-09-27（K3/A 批）：kind 18 → 24（+ user-message / assistant-message / attachment 及词表同步）；
#                        rule 15 → 38（快照逐消息规则 +6）。
# 基线单源 = Baselines\RecognizerVocabulary.psd1（禁内联数值基线）
Import-Module (Join-Path $PSScriptRoot '..\..\Baselines\GateBaseline.psm1') -Force
$g = Import-GateBaseline -Name RecognizerVocabulary
$kindBaseline = [int]$g.KindCount.Value
$ruleBaseline = [int]$g.RuleCount.Value
$fallbackKind = 'unrecognized'

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  识别词表守卫（声明面单源 / 结构谓词 / 零词表副本）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

<#
    判据核心（纯函数；负对照直接复用 ⇒ 判据不可被"恒真"绕过）。
    返回违规清单；空 = 通过。
#>
function Get-SpecIssues
{
    param(
        [string[]]$Kinds,
        [object[]]$Rules
    )

    $issues = [System.Collections.Generic.List[string]]::new()
    $kindList = @($Kinds)

    # R2 kind 唯一 + 兜底类在位
    $dupes = @($kindList | Group-Object | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })
    if ($dupes.Count -gt 0) { $issues.Add("[R2] 词表 kind 重复：$($dupes -join ', ')") }
    if ($kindList -notcontains $fallbackKind) { $issues.Add("[R2] 词表缺兜底类 '$fallbackKind'（D-3 无静默丢的兜底面）") }

    $kindSet = @{}
    foreach ($k in $kindList) { $kindSet[$k] = $true }

    foreach ($rule in @($Rules))
    {
        $ruleKind = [string]$rule.kind

        # R3 规则 kind ∈ 词表
        if (-not $kindSet.ContainsKey($ruleKind)) { $issues.Add("[R3] 规则 kind '$ruleKind' ∉ 词表（非法识别声明）") }

        # R4 谓词仅结构形态
        if ($null -eq $rule.match) { $issues.Add("[R4] 规则 kind '$ruleKind' 缺 match（无判据载体）"); continue }
        foreach ($prop in $rule.match.PSObject.Properties)
        {
            $value = $prop.Value
            if ($null -eq $value) { $issues.Add("[R4] 规则 kind '$ruleKind' 谓词 '$($prop.Name)' 为空值"); continue }
            if ($value -is [System.Management.Automation.PSCustomObject])
            {
                $hasExists = $null -ne $value.PSObject.Properties['exists']
                $isBool    = $hasExists -and ($value.exists -is [bool])
                if (-not $isBool)
                {
                    $issues.Add("[R4] 规则 kind '$ruleKind' 谓词 '$($prop.Name)' 为嵌套对象（仅允许 {exists:bool}；语义判据禁作载体）")
                }
                continue
            }
        }
    }

    return $issues
}

<#
    判据核心（R9；纯函数 ⇒ 负对照直接复用，判据不可被"恒真"绕过）：
    返回 live 与 snapshot 的**成节点**类别交集（空 = 通过）。
#>
function Get-EmittedKindOverlap
{
    param([object[]]$Rules)

    $live = [System.Collections.Generic.HashSet[string]]::new()
    $snapshot = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($rule in @($Rules))
    {
        if ($rule.node -eq $false) { continue }
        $origin = [string]$rule.origin
        if ($origin -eq 'Snapshot') { [void]$snapshot.Add([string]$rule.kind) }
        else { [void]$live.Add([string]$rule.kind) }
    }

    return @($live | Where-Object { $snapshot.Contains($_) })
}

# ---------------- R1 声明面存在 + JSON 合法 + 结构齐备 ----------------
$spec = $null
if (-not (Test-Path -LiteralPath $specPath))
{
    $violations.Add("[R1] 识别声明面缺失：Projects\Plug\Agent\Config\recognizers.json")
}
else
{
    try
    {
        $spec = Get-Content -LiteralPath $specPath -Raw | ConvertFrom-Json
    }
    catch
    {
        $violations.Add("[R1] 识别声明面 JSON 非法：$($_.Exception.Message)")
    }
}

if ($null -ne $spec)
{
    $hasVocabulary = -not [string]::IsNullOrWhiteSpace([string]$spec.vocabulary)
    $kinds = @($spec.kinds)
    $rules = @($spec.recognizers)

    if (-not $hasVocabulary) { $violations.Add("[R1] 声明面缺 vocabulary（词表标识/契约版本）") }
    if ($kinds.Count -eq 0) { $violations.Add("[R1] 声明面 kinds[] 为空") }
    if ($rules.Count -eq 0) { $violations.Add("[R1] 声明面 recognizers[] 为空") }

    # ---------------- R2/R3/R4 ----------------
    if ($kinds.Count -gt 0)
    {
        foreach ($issue in @(Get-SpecIssues -Kinds $kinds -Rules $rules)) { $violations.Add($issue) }
    }

    # ---------------- R5 W-2 零词表副本（TS 侧） ----------------
    if (Test-Path -LiteralPath $tsDir)
    {
        $tsFiles = @(Get-ChildItem -LiteralPath $tsDir -Recurse -Filter *.ts -File -ErrorAction SilentlyContinue)
        $quote = [char]34
        $apos = [char]39
        foreach ($kind in $kinds)
        {
            if ($kind -eq $fallbackKind) { continue }   # 兜底类常量是本层唯一允许的字面量
            $pattern = "[$quote$apos]" + [regex]::Escape($kind) + "[$quote$apos]"
            foreach ($file in $tsFiles)
            {
                $text = Get-Content -LiteralPath $file.FullName -Raw
                if ($text -match $pattern)
                {
                    $violations.Add("[R5] TS 侧存在词表副本：$($file.Name) 含字面量 '$kind'（W-2：清单只能来自声明面）")
                }
            }
        }
    }
    else
    {
        $violations.Add("[R5] 识别层目录缺失：ClineApi\src\recognize")
    }

    # ---------------- R6 负对照（防假绿） ----------------
    $negNoFallback = @(Get-SpecIssues -Kinds @($kinds | Where-Object { $_ -ne $fallbackKind }) -Rules $rules)
    if ($negNoFallback.Count -eq 0) { $violations.Add("[R6] 负对照失效：去掉兜底类未被判据捕获（判据恒真）") }

    $ghostRules = @($rules) + @([pscustomobject]@{ kind = 'ghost-not-in-vocabulary'; match = @{ type = 'never' } })
    $negGhost = @(Get-SpecIssues -Kinds $kinds -Rules $ghostRules)
    if ($negGhost.Count -eq 0) { $violations.Add("[R6] 负对照失效：rule kind ∉ 词表 未被判据捕获（R3 恒真）") }

    # ---------------- R7 正对照（基线计数） ----------------
    if ($kinds.Count -lt $kindBaseline)
    {
        $violations.Add("[R7] kind 数 $($kinds.Count) < 基线 $kindBaseline（词表被削 ⇒ 门禁失效，禁假绿）")
    }
    if ($rules.Count -lt $ruleBaseline)
    {
        $violations.Add("[R7] recognizer 数 $($rules.Count) < 基线 $ruleBaseline（识别规则被删 ⇒ 门禁失效，禁假绿）")
    }

    # ---------------- R8 C# 镜像对账 ----------------
    if (Test-Path -LiteralPath $mirrorPath)
    {
        $mirrorText = Get-Content -LiteralPath $mirrorPath -Raw
        foreach ($kind in $kinds)
        {
            if ($kind -eq $fallbackKind) { continue }
            if ($mirrorText -notmatch ('"' + [regex]::Escape($kind) + '"'))
            {
                $violations.Add("[R8] C# 镜像缺 kind '$kind'（RecognizedKinds.cs 与声明面不一致）")
            }
        }
    }
    else
    {
        $pending.Add("[R8] C# 镜像未落地（Agent.Contract\NodeDomain\RecognizedKinds.cs）—— K3 收口项，当前由 TS 侧容错兜底（D-5）")
    }

    # ---------------- R9 去重（决策 C / D-8：live ∩ snapshot 成节点类别 = ∅） ----------------
    $overlap = @(Get-EmittedKindOverlap -Rules $rules)
    if ($overlap.Count -gt 0)
    {
        $violations.Add("[R9] 决策 C 违反：live 与 snapshot 的**成节点**类别交集非空：$($overlap -join ', ')（同内容双写 ⇒ 过程面重复行）")
    }

    $liveKinds = @($rules |
        Where-Object { $_.node -ne $false -and [string]$_.origin -ne 'Snapshot' } |
        ForEach-Object { [string]$_.kind })
    if ($liveKinds.Count -eq 0)
    {
        $violations.Add("[R9] 判据无标的：声明面不存在 live 成节点规则（规则被削 ⇒ 去重判据失效）")
    }
    else
    {
        # 负对照（防假绿）：注入一条 snapshot 成节点规则且 kind = 某 live 成节点 kind ⇒ 同一判据必须捕获
        $probe = @($rules) + @([pscustomobject]@{ kind = $liveKinds[0]; node = $true; origin = 'Snapshot' })
        if (@(Get-EmittedKindOverlap -Rules $probe).Count -eq 0)
        {
            $violations.Add("[R9] 负对照失效：live/snapshot 成节点重复类别未被判据捕获（判据恒真）")
        }
    }

    # ---------------- R10 聚拢声明品类闭合（node-gather-*.json ⊆ 词表） ----------------
    # 权威：节点聚拢声明（families[].kinds ∪ kind-shaping 键）出现的每个 kind 必在识别词表内；
    # 通配塑形键 '*' 非具体 kind，不作闭合标的。覆盖「新增一域/一 kind = 改声明 + 词表」的守卫。
    $gatherDir = Join-Path $Root 'Projects\Plug\Agent\Config\nodes'
    $gatherFiles = @(Get-ChildItem -LiteralPath $gatherDir -Filter 'node-gather-*.json' -File -ErrorAction SilentlyContinue)
    if ($gatherFiles.Count -eq 0)
    {
        $violations.Add("[R10] 未发现任何聚拢声明（node-gather-*.json）—— 声明被删 ⇒ 门禁失效（禁假绿）")
    }
    else
    {
        $kindLookup = @{}
        foreach ($k in $kinds) { $kindLookup[$k] = $true }

        foreach ($gFile in $gatherFiles)
        {
            $gSpec = $null
            try
            {
                $gSpec = Get-Content -LiteralPath $gFile.FullName -Raw | ConvertFrom-Json
            }
            catch
            {
                $violations.Add("[R10] 聚拢声明 JSON 非法：$($gFile.Name) — $($_.Exception.Message)")
                continue
            }

            $declaredKinds = [System.Collections.Generic.List[string]]::new()
            if ($null -ne $gSpec.families)
            {
                foreach ($fam in $gSpec.families.PSObject.Properties)
                {
                    foreach ($k in @($fam.Value))
                    {
                        if (-not [string]::IsNullOrWhiteSpace([string]$k)) { $declaredKinds.Add([string]$k) }
                    }
                }
            }
            if ($null -ne $gSpec.'kind-shaping')
            {
                foreach ($sh in $gSpec.'kind-shaping'.PSObject.Properties)
                {
                    if ($sh.Name -eq '*') { continue }   # 通配塑形非具体 kind
                    $declaredKinds.Add([string]$sh.Name)
                }
            }

            foreach ($kind in $declaredKinds)
            {
                if (-not $kindLookup.ContainsKey($kind))
                {
                    $violations.Add("[R10] 聚拢声明 $($gFile.Name) 的 kind '$kind' ∉ 识别词表（品类闭合：新增类别须先入 recognizers.json kinds[]）")
                }
            }
        }
    }

    if ($ShowDetail)
    {
        Write-Host "  声明面：$specPath" -ForegroundColor Gray
        Write-Host "  聚拢声明：$(@($gatherFiles).Count) 文件（R10：families/kind-shaping ⊆ 识别词表）" -ForegroundColor Gray
        Write-Host "  vocabulary：$($spec.vocabulary) | kind 数：$($kinds.Count)（基线 ≥ $kindBaseline）| 规则数：$($rules.Count)（基线 ≥ $ruleBaseline）" -ForegroundColor Gray
    }
}

# ---------------- 汇总 ----------------
Write-Host ""
if ($ShowDetail -and $pending.Count -gt 0)
{
    Write-Host "[RV] --- 迁移期可见化（不算违规） ---" -ForegroundColor DarkGray
    foreach ($item in $pending) { Write-Host "  - $item" -ForegroundColor DarkGray }
}

if ($violations.Count -eq 0)
{
    Write-Host "[OK] 识别词表守卫：0 违规（声明面单源 / 结构谓词 / 零词表副本 / 正负对照在位）" -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] 识别词表守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($item in $violations) { Write-Host "  - $item" -ForegroundColor Yellow }
Write-Host "  修复口径：新增识别类别 ⇒ 只改 Config\recognizers.json（kinds +1 ∧ recognizers +1），TS/C# 零代码改动；" -ForegroundColor Gray
Write-Host "            match 只用结构谓词；payload 只白名单投影。" -ForegroundColor Gray

if ($Check) { exit 1 }
exit 0

