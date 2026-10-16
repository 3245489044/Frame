#requires -Version 5.1
<#
.SYNOPSIS
    域族分离守卫（DomainRosterSingleSource）—— 存储域集合单源 + 域族分离声明在位。

.DESCRIPTION
    判据（B4/SCT-09）：
      G1 存储域单源：存储域集合字面量出现 = 1（`NodeGatherCatalog.StoreDomains` 引用 `DbStoreKeys.All`）。
      G2 域族分离声明：`NodeDomainCatalog.cs` 含「域族分离」显式声明。
      G3 正/负对照：合成样本（硬编码存储域清单）必被捕获。

    模式：默认报告；-Check 时违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()
Write-Host ''
Write-Host '============================================================'
Write-Host '  域族分离守卫（DomainRosterSingleSource）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$catalog = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules\Framework\Gather\NodeGatherCatalog.cs'
$domains = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules\Framework\NodeSpec\NodeDomainCatalog.cs'

if (-not (Test-Path -LiteralPath $catalog)) {
    $violations.Add('[G1] 未见 NodeGatherCatalog.cs')
} else {
    $t = [System.IO.File]::ReadAllText($catalog)
    if ((Get-Count $t 'DbStoreKeys\.All') -eq 0) {
        $violations.Add('[G1] 存储域集合未引用 DbStoreKeys.All（疑似硬编码清单）')
    }
    $hardcoded = Get-Count $t 'StoreDomains\s*=\s*new\[\]'
    if ($hardcoded -gt 0) { $violations.Add("[G1] 存储域硬编码清单 × $hardcoded") }
}

if (-not (Test-Path -LiteralPath $domains)) {
    $violations.Add('[G2] 未见 NodeDomainCatalog.cs')
} elseif ((Get-Count ([System.IO.File]::ReadAllText($domains)) '域族分离') -eq 0) {
    $violations.Add('[G2] NodeDomainCatalog 缺「域族分离」显式声明')
}

# 正/负对照（违规形态须被捕获）
if ((Get-Count 'StoreDomains = new[] { "nodes", "tasks" }' 'StoreDomains\s*=\s*new\[\]') -eq 0) {
    $violations.Add('[G3] 负对照失效：合成样本（硬编码存储域清单）未被捕获')
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2 域族分离通过（正负对照绿）' -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host ''
Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
Write-Host '  结果: 失败' -ForegroundColor Red
Write-Host ''
if ($Check) { exit 1 }
exit 0
