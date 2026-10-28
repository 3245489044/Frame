#requires -Version 5.1
<#
.SYNOPSIS
    图边键单源守卫（GraphKeySingleSource）—— 端点/边键只允许在 GraphKeyGrammar 内拼装。

.DESCRIPTION
    判据（结构事实）：
      GK1 图存储层（Frame Operation\DataStoreDriven）内手拼边键字面量（"::"）= 0（唯一实现 = DeclaredGraphKeyGrammar）；
      GK2 Agent 侧图行端口/数据层同样 = 0（禁第二处拼键）；
      GK3 文法实现点 = 1（DeclaredGraphKeyGrammar）。
    负对照：合成一处 "::" 拼键必须判红。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$scanRoots = @(
    'Frame\CsharpPrj\Operation\DataStoreDriven',
    'Projects\Plug\Agent\src'
)
$whitelist = @('GraphKeyGrammar.cs')

function Get-KeyViolations([string[]]$roots, [string[]]$allow) {
    $out = [System.Collections.Generic.List[string]]::new()
    $definitions = 0
    foreach ($rel in $roots) {
        $dir = Join-Path $Root $rel
        if (-not (Test-Path $dir)) { continue }
        $files = Get-ChildItem -Path $dir -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|\\BuildOutput\\|\\_deprecated\\|\\Tests?\\' }
        foreach ($f in $files) {
            if ($allow -contains $f.Name) {
                if ($f.Name -eq 'GraphKeyGrammar.cs') { $definitions++ }
                continue
            }
            $hits = @(Select-String -LiteralPath $f.FullName -Pattern '"::"' -SimpleMatch -ErrorAction SilentlyContinue)
            foreach ($h in $hits) {
                $relPath = $h.Path -replace [regex]::Escape($Root + '\'), ''
                $out.Add("GK 手拼边键：$relPath`:$($h.LineNumber)  $($h.Line.Trim())")
            }
        }
    }
    if ($definitions -ne 1) { $out.Add("GK3 文法实现点 = $definitions（期望 1：DeclaredGraphKeyGrammar）") }
    return $out
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  图边键单源守卫（GraphKeySingleSource）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$violations = Get-KeyViolations -roots $scanRoots -allow $whitelist

if ($NegativeControl) {
    $probe = Get-KeyViolations -roots @('Frame\CsharpPrj\Operation\DataStoreDriven') -allow @('GraphKeyGrammar.cs')
    $synthetic = 'var edgeId = sourceKey + "::" + targetKey + "::" + refType;'
    $hit = @($synthetic | Select-String -Pattern '"::"' -SimpleMatch).Count
    if ($hit -gt 0 -and @($probe | Where-Object { $_ -like 'GK3*' }).Count -eq 0) {
        Write-Host "  [OK] 负对照：合成拼键被模式命中；正式扫描文法实现点 = 1" -ForegroundColor Green
        exit 0
    }
    Write-Host "  [FAIL] 负对照未命中" -ForegroundColor Yellow
    exit 1
}

if ($violations.Count -eq 0) {
    Write-Host "[OK] GraphKeySingleSource：0 违例" -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] GraphKeySingleSource 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
exit 1
