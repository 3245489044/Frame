#requires -Version 5.1
<#
.SYNOPSIS
    GateRuntime —— 门禁运行时（根定位 / 脚本发现 / 裁决 / 结果结构），纯原语，无判据语义。

.DESCRIPTION
    本件把门禁脚本内重复散落的四类样板收敛为单源（分析文档 D1/D2/D4/D6/E2/E4 的收拢面）：
      · 根定位   —— 委托**目录映射基础框架** PScriptManager\Framework\DirectoryMap.psm1
                    （超根 = 含 Frame\+Projects\ 的目录；与深度无关；禁门内再写 ..\..\..\..\ 上溯）。
      · 脚本发现 —— 委托 DirectoryMap 的 Resolve-GateScript / Get-GateScriptCell
                    （Domain→候选目录 + 名称例外；歧义即抛）。
      · 裁决     —— Write-GateVerdict 单义退出码（0 无违规 / -Check 且违规 1 / 异常由调用方 2）。
      · 结果结构 —— New-GateResult（供组合门读结果产物，不解析文本）。

    形态：本 .psm1 以 Import-Module 装载 DirectoryMap（函数不重复导出 ⇒ 无同名多属主）。
    本件无 Export-ModuleMember：默认仅导出本文件自定义函数（导入件不外溢）。

.NOTES
    用法（门禁脚本头部）：
        Import-Module (Join-Path $PSRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force
        $violations = [System.Collections.Generic.List[string]]::new()
        # ... 判据 ...
        Write-GateVerdict -Violations $violations -Check:$Check -Key 'GateTaxonomy'
#>

Import-Module (Join-Path $PSScriptRoot '..\PScriptManager\Framework\DirectoryMap.psm1') -Force -DisableNameChecking

<#
.SYNOPSIS
    解析仓库超根（委托目录映射框架；与目录深度无关）。
.PARAMETER ScriptRoot
    门禁脚本所在目录（缺省 = 本文件目录）。
#>
function Get-GateRepoRoot {
    param([string]$ScriptRoot)
    $start = if ($ScriptRoot) { $ScriptRoot } else { $PSScriptRoot }
    return Get-SuperRoot -StartPath $start
}

<#
.SYNOPSIS
    按名（+ Domain）发现门脚本绝对路径（委托目录映射框架；歧义即抛）。
#>
function Find-GateScript {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string]$Domain = '',
        [switch]$AllowFallback
    )
    return Resolve-GateScript -Gate $Name -Domain $Domain -AllowFallback:$AllowFallback
}

<#
.SYNOPSIS
    构造结构化门结果（供组合门读结果产物，脱文本耦合）。Exit: 0 无违规 / 1 违规 / 2 异常前置缺失。
#>
function New-GateResult {
    param(
        [Parameter(Mandatory = $true)][string]$Gate,
        [System.Collections.Generic.List[string]]$Violations,
        [int]$Exit = 0
    )
    $v = if ($null -eq $Violations) { @() } else { @($Violations) }
    return [pscustomobject]@{
        Gate           = $Gate
        Violations     = $v
        ViolationCount = $v.Count
        Exit           = $Exit
    }
}

<#
.SYNOPSIS
    统一门禁标题头（收敛各门自写 header）。
#>
function Write-GateHeader {
    param([string]$Title)
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan
}

<#
.SYNOPSIS
    统一门禁裁决尾巴（单义退出码：0 无违规 / -Check 且违规 → 1）。
.DESCRIPTION
    · 无违规 → 打印 [PASS]，exit 0；
    · 有违规 → 打印明细；-Check 时 exit 1（判定模式），否则 exit 0（报告模式）；
    · 前置缺失等异常场景由调用方 `throw` 或在调用前 `exit 2`（本函数不产生 2）。
.PARAMETER Violations
    违规串列表。
.PARAMETER Check
    判定模式：有违规即退出码 1。
.PARAMETER Key
    门名（用于 PASS 行）。
.PARAMETER ReportOnly
    报告模式提示语（有违规且非 -Check 时附加）。
#>
function Write-GateVerdict {
    param(
        [System.Collections.Generic.List[string]]$Violations,
        [switch]$Check,
        [string]$Key = '',
        [string]$ReportOnly = '（报告模式：不设退出码；加 -Check 进入门禁判定）'
    )
    $count = if ($null -eq $Violations) { 0 } else { $Violations.Count }
    if ($count -eq 0) {
        Write-Host ""
        Write-Host "  [PASS] $Key：0 违规" -ForegroundColor Green
        Write-Host ""
        exit 0
    }
    Write-Host ""
    Write-Host "  违规明细（$count 项）：" -ForegroundColor Yellow
    foreach ($v in $Violations) { Write-Host "    - $v" -ForegroundColor Yellow }
    Write-Host ""
    if ($Check) { exit 1 }
    if ($ReportOnly) { Write-Host $ReportOnly -ForegroundColor DarkGray }
    exit 0
}

<#
.SYNOPSIS
    基线核减判据（只减不增）：新增键（不在基线表中）或计数上涨即违规。
.DESCRIPTION
    上提自 Modules\Node\NodeGateCommon.ps1（全局化后 Node 侧可复用）。
.PARAMETER Counts
    实测计数（键 → 计数）。
.PARAMETER Baselines
    基线表（键 → 冻结值）。
.PARAMETER Label
    违规文案前缀（如「AgentFrameworkSingleSource」）。
#>
function Test-BaselineNoIncrease {
    param(
        [hashtable]$Counts,
        [hashtable]$Baselines,
        [string]$Label
    )
    $violations = [System.Collections.Generic.List[string]]::new()
    foreach ($k in $Counts.Keys) {
        if (-not $Baselines.ContainsKey($k)) {
            $violations.Add("$Label 新增散落点：$k = $($Counts[$k])（基线表无此项 ⇒ 只减不增禁新增）")
            continue
        }
        if ($Counts[$k] -gt $Baselines[$k]) {
            $violations.Add("$Label 取值点新增：$k = $($Counts[$k]) > 基线 $($Baselines[$k])（只减不增）")
        }
    }
    return $violations
}


# 显式导出面（自有 API + 门禁面向的目录映射 API）：
#   固定契约，禁依赖「默认全导出」的隐式面（防随导入件漂移）。
Export-ModuleMember -Function @(
    # 自有（门禁运行时）
    'Get-GateRepoRoot',
    'Find-GateScript',
    'New-GateResult',
    'Write-GateHeader',
    'Write-GateVerdict',
    'Test-BaselineNoIncrease',
    # 目录映射（委托 DirectoryMap；一键装载供门禁消费）
    'Get-SuperRoot',
    'Get-RepositoryRoot',
    'Get-FrameRoot',
    'Get-MappedPath',
    'Get-DomainPaths',
    'Get-DomainPath',
    'Get-GateDomains',
    'Get-GateScriptOverride',
    'Get-GateScriptLeafName',
    'Resolve-GateScript',
    'Get-GateScriptCell'
)

