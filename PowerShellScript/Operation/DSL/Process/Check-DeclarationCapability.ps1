#requires -Version 5.1
<#
.SYNOPSIS
    声明能力门禁（DeclarationCapability）：nullable=/conditional= 端口必须属于 outports。
.DESCRIPTION
    C1 扫描 Config/workflows/*.ushar 的 [Node] 行；
    C2 nullable= / conditional= 列出的端口必须 ⊆ outports；
    C3 负对照：nullable 指向未知端口的合成行必须被判违规。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

function Get-Field {
    param([string]$Line, [string]$Name)
    $m = [regex]::Match($Line, '(?:\||^)\s*' + [regex]::Escape($Name) + '=([^|]+)')
    if ($m.Success) { return $m.Groups[1].Value.Trim() }
    return ''
}

function Split-Ports {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return @() }
    return @($Text.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 })
}

function Get-CapabilityViolations {
    param([string]$Line)

    $violations = [System.Collections.Generic.List[string]]::new()
    if ($Line -notmatch '^\s*\[Node\]') { return $violations }

    $outports = Split-Ports (Get-Field -Line $Line -Name 'outports')
    foreach ($field in @('nullable', 'conditional')) {
        foreach ($port in (Split-Ports (Get-Field -Line $Line -Name $field))) {
            if ($outports -notcontains $port) {
                $violations.Add("$field=$port 不在 outports 内")
            }
        }
    }
    return $violations
}

if ($NegativeControl) {
    $probe = Get-CapabilityViolations -Line '[Node] x: fn=x | outports=a,b | nullable=c'
    if ($probe.Count -eq 0) {
        Write-Output 'FAIL DeclarationCapability: 负对照未命中（检测器恒真）'
        exit 1
    }
    Write-Output "PASS DeclarationCapability (negative control: $($probe.Count) violations)"
    exit 0
}

$workflowDir = Join-Path $Root 'Projects\Plug\Agent\Config\workflows'
$violations = [System.Collections.Generic.List[string]]::new()
if (-not (Test-Path -LiteralPath $workflowDir)) {
    $violations.Add("声明目录缺失：$workflowDir")
} else {
    foreach ($file in Get-ChildItem -LiteralPath $workflowDir -Filter '*.ushar' -File) {
        $lineNo = 0
        foreach ($line in Get-Content -LiteralPath $file.FullName) {
            $lineNo++
            foreach ($v in (Get-CapabilityViolations -Line $line)) {
                $violations.Add("$($file.Name):$lineNo $v")
            }
        }
    }
}

if ($ShowDetail) {
    $violations | ForEach-Object { Write-Output "  - $_" }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Output "  - $_" }
    Write-Output "FAIL DeclarationCapability: $($violations.Count) violation(s)"
    if ($Check) { exit 1 }
    exit 0
}

Write-Output 'PASS DeclarationCapability'
exit 0
