# Check-KeyDeclNoLogic.ps1
# KP6 声明无逻辑（阻断）：DomainKeyFamilies 只允许 id 常量与只读转发；禁模板/面/形态构造。
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot }
$file = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Contract\NodeDomain\DomainKeyFamilies.cs'
$forbidden = 'new\s+KeyFamily|KeySurfaceSpec|KeyDomainId\.Of|\+\s*"|"\s*\+|\{\w+\}'

function Get-DeclViolations {
    param([string]$Text, [string]$Pattern)
    # 注释剥离（判据载体 = 代码结构事实，注释提及不算逻辑）。
    $code = [regex]::Replace($Text, '/\*.*?\*/', '', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    $code = [regex]::Replace($code, '//[^\r\n]*', '')
    $out = [System.Collections.Generic.List[string]]::new()
    foreach ($hit in [regex]::Matches($code, $Pattern)) {
        $out.Add("禁止在声明目录内出现逻辑/模板：$($hit.Value)")
    }

    return $out
}

$violations = [System.Collections.Generic.List[string]]::new()
if (-not (Test-Path -LiteralPath $file)) {
    $violations.Add("声明目录缺失：$file")
} else {
    $found = @(Get-DeclViolations -Text (Get-Content -LiteralPath $file -Raw) -Pattern $forbidden)
    if ($found.Count -gt 0) { $violations.AddRange([string[]]$found) }
}

if ($NegativeControl) {
    $probe = 'new KeyFamily("x", "p.{c}")'
    if (@(Get-DeclViolations -Text $probe -Pattern $forbidden).Count -eq 0) {
        $violations.Add('负对照失败：形态构造样本未被判红')
    }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'KeyDeclNoLogic'
