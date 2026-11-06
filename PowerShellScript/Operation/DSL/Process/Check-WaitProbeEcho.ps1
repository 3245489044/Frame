#requires -Version 5.1
<#
.SYNOPSIS
    wait-until 恒真判据门禁（WaitProbeEcho）—— 「期望词命中探针输入回显」从散文变成机器判据。

.DESCRIPTION
    判据载体 = 脚本声明的结构事实（commands[].action/expectedContains/target/value 及具名参数），
    依据 = 探针返回文本必然回显其入参 ⇒ expectedContains 若等于任一入参值，则 0 轮必然命中，
    不是等待而是回显（CLI-06 假绿事故的稳定成因）。

    判据：
      E1 wait-until 必须声明 expectedContains/contains（空期望恒真）
      E2 expectedContains 不得等于探针 target，也不得等于任一 value= 段/具名参数值
      E3 负对照(-NegativeControl)：合成 22:47 #30 样本必须判红（判据不许失效）

    模式：默认报告；-Check 违规 → 退出码 1。只读，不写任何文件。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [string]$Path = 'Projects\Information\DrivenConfig\Scripts',
    [switch]$Check,
    [switch]$NegativeControl,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$script:violations = [System.Collections.Generic.List[object]]::new()

$reserved = @(
    'action', 'target', 'value', 'expectedContains', 'contains', 'output',
    'timeout', 'interval', 'failWhen', 'failContains', 'template', 'title',
    'note', 'description', 'critical', 'style'
)

function Get-ProbeArgValues([object]$cmd) {
    $values = [System.Collections.Generic.List[string]]::new()

    if ($cmd.target) { $values.Add([string]$cmd.target) }

    if ($cmd.value) {
        foreach ($seg in ([string]$cmd.value -split '\s+')) {
            if ($seg -match '^[^=]+=(.*)$') {
                $values.Add($Matches[1].Trim('"', "'"))
            }
        }
    }

    foreach ($p in $cmd.PSObject.Properties) {
        if ($reserved -contains $p.Name) { continue }
        if ($null -eq $p.Value) { continue }
        if ($p.Value -is [System.Management.Automation.PSCustomObject]) { continue }
        if ($p.Value -is [System.Array]) { continue }
        $values.Add([string]$p.Value)
    }

    return $values
}

function Test-WaitCommand([object]$cmd, [string]$label) {
    $expected = if ($cmd.expectedContains) { [string]$cmd.expectedContains }
                elseif ($cmd.contains) { [string]$cmd.contains }
                else { '' }

    if ([string]::IsNullOrWhiteSpace($expected)) {
        $script:violations.Add([pscustomobject]@{
            Kind   = 'missing-expected'
            File   = $label
            Detail = 'wait-until 缺 expectedContains/contains（空期望恒真）'
        })
        return
    }

    foreach ($v in (Get-ProbeArgValues $cmd)) {
        if ([string]::IsNullOrWhiteSpace($v)) { continue }
        if ([string]::Equals($expected.Trim(), $v.Trim(), [StringComparison]::OrdinalIgnoreCase)) {
            $script:violations.Add([pscustomobject]@{
                Kind   = 'echo-tautology'
                File   = $label
                Detail = ("expectedContains '{0}' == 探针入参/目标 '{1}'（回显 ⇒ 0 轮必中，非判据）" -f $expected, $v)
            })
            return
        }
    }
}

$scanRoot = Join-Path $Root $Path
if (-not (Test-Path -LiteralPath $scanRoot)) {
    Write-Host "[WaitProbeEcho] 扫描面不存在: $scanRoot"
    if ($Check) { exit 1 }
    exit 0
}

$files = Get-ChildItem -LiteralPath $scanRoot -Recurse -File -Filter '*.json'
$waitCount = 0

foreach ($f in $files) {
    try {
        $doc = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        continue
    }

    if ($null -eq $doc -or $null -eq $doc.commands) { continue }

    foreach ($cmd in $doc.commands) {
        if ($null -eq $cmd) { continue }
        if (-not [string]::Equals([string]$cmd.action, 'wait-until', [StringComparison]::OrdinalIgnoreCase)) { continue }
        $waitCount++
        Test-WaitCommand $cmd $f.FullName
    }
}

# E3 负对照：合成 22:47 事故样本（expectedContains 等于 taskKey 入参）必须判红。
if ($NegativeControl) {
    $before = $script:violations.Count
    $synthetic = [pscustomobject]@{
        action           = 'wait-until'
        target           = 'AgentUi_TaskPoolGetStatus'
        expectedContains = 'tp_hist_read_child'
        value            = 'taskKey=tp_hist_read_child'
        output           = 'statusLabel'
    }
    Test-WaitCommand $synthetic '<negative-control:22:47#30>'
    if ($script:violations.Count -eq $before) {
        Write-Host '[WaitProbeEcho] 负对照未判红 ⇒ 判据失效（禁假绿）'
        exit 1
    }
    $script:violations.RemoveAt($script:violations.Count - 1)
}

$count = $script:violations.Count
if ($ShowDetail -or -not $Check) {
    foreach ($v in $script:violations) {
        Write-Host ("[{0}] {1}`n    {2}" -f $v.Kind, $v.File, $v.Detail)
    }
}

Write-Host "[WaitProbeEcho] wait-until=$waitCount 违规=$count"

if ($Check -and $count -gt 0) { exit 1 }
exit 0
