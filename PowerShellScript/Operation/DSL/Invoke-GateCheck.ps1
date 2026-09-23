<#
.SYNOPSIS
    门禁聚合器 —— 一条命令跑齐 Operation\DSL\Check-*.ps1 并输出 PASS/FAIL 表
.DESCRIPTION
    把「手写 grep 做契约核对」替换为批量门禁检查。
    清单来源 = GateManifests.psd1（纯数据，登记每件的只读验收开关与期望退出码）。
    输出：[PASS|FAIL] <Gate> (ms=<n>) 逐件 + 失败件尾部摘要 + 汇总行。
    退出码：全 PASS 0；任一 FAIL 1。

    注意：不跑写开关（如 -UpdateIndex / -Fix），仅供只读验收。
# @requires Write-Section, Write-Result, Write-Warn
#>
[CmdletBinding()]
param(
    [switch]$All,
    [string[]]$Only = @(),
    [string]$Manifest = '',
    [int]$Parallel = 1
)

# 按需加载模块
. (Join-Path $PSScriptRoot "..\..\Modules\Import-Modules.ps1")

# ---- 加载清单 ----
if (-not $Manifest) {
    $Manifest = Join-Path $PSScriptRoot 'GateManifests.psd1'
}
if (-not (Test-Path -LiteralPath $Manifest)) {
    Write-Result "FAIL" "门禁清单文件不存在：$Manifest"
    exit 1
}

$gates = Import-PowerShellDataFile -LiteralPath $Manifest

# ---- 确定要运行的门禁 ----
if ($Only.Count -gt 0) {
    $names = $Only
} else {
    $names = @($gates.Keys)
}

# 验证名称有效性
$invalid = $names | Where-Object { -not $gates.ContainsKey($_) }
if ($invalid) {
    Write-Result "FAIL" "未知门禁名称：$($invalid -join ', ')"
    Write-Result "INFO" "可用门禁：$($gates.Keys -join ', ')"
    exit 1
}

Write-Section "门禁检查（共 $($names.Count) 件，并行度=$Parallel）"

# ---- 执行 ----
$failed = 0
$results = New-Object System.Collections.Generic.List[PSCustomObject]
$failedOutputs = New-Object System.Collections.Generic.List[string]

foreach ($name in $names) {
    $spec = $gates[$name]
    $scriptPath = Join-Path $PSScriptRoot "Check-$name.ps1"

    if (-not (Test-Path -LiteralPath $scriptPath)) {
        Write-Result "FAIL" "$name — 脚本不存在: $scriptPath"
        $failed++
        $results.Add([PSCustomObject]@{ Name = $name; Status = 'FAIL'; Ms = 0; Message = '脚本不存在' })
        continue
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()

    # 收集输出以便失败时回显（数组 splatting 传参）
    $argList = @($spec.Args)
    $output = @(& $scriptPath @argList 2>&1)
    $rc = $LASTEXITCODE

    $sw.Stop()
    $ms = [int]$sw.Elapsed.TotalMilliseconds

    if ($rc -eq $spec.Expect) {
        Write-Result "OK" "$name (ms=$ms)"
        $results.Add([PSCustomObject]@{ Name = $name; Status = 'PASS'; Ms = $ms; Message = '' })
    } else {
        Write-Result "FAIL" "$name (ms=$ms, exit=$rc, expect=$($spec.Expect))"
        $failed++
        $results.Add([PSCustomObject]@{ Name = $name; Status = 'FAIL'; Ms = $ms; Message = "exit=$rc" })

        # 收集尾部 20 行输出用于摘要
        $tail = @($output | Select-Object -Last 20 | ForEach-Object { $_.ToString() })
        if ($tail.Count -gt 0) {
            $failedOutputs.Add("--- $name 尾部输出 ---")
            $tail | ForEach-Object { $failedOutputs.Add("  $_") }
        }
    }
}

# ---- 失败摘要 ----
if ($failed -gt 0 -and $failedOutputs.Count -gt 0) {
    Write-Section "失败摘要"
    $failedOutputs | ForEach-Object { Write-Warn $_ }
}

# ---- 汇总 ----
$passed = $names.Count - $failed
Write-Result "INFO" "门禁 $($names.Count) 件 / 通过 $passed / 失败 $failed"

if ($failed -gt 0) { exit 1 }
exit 0
