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

    # 分层：fast = 只跑「读产物」类轻量件（清单逐件 Tier='fast'）；full = 全量（缺省）
    [ValidateSet('fast', 'full')]
    [string]$Tier = 'full'
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

# ---- 分层：-Tier fast 只跑「读产物」类轻量件（逐件 Tier='fast'；缺省 full）----
# 目的：编辑/复核循环里拿秒级反馈（全量 34 件串行 + 31 件扫源码，无超时预算 ⇒ 不可能快）。
if ($Tier -eq 'fast' -and $Only.Count -eq 0) {
    $names = @($names | Where-Object { $gates[$_].ContainsKey('Tier') -and $gates[$_].Tier -eq 'fast' })
    if ($names.Count -eq 0) {
        Write-Result "FAIL" "fast 层为空（清单无 Tier='fast' 件 ⇒ 判据无标的，禁假绿）"
        exit 1
    }
}

# 验证名称有效性
$invalid = $names | Where-Object { -not $gates.ContainsKey($_) }
if ($invalid) {
    Write-Result "FAIL" "未知门禁名称：$($invalid -join ', ')"
    Write-Result "INFO" "可用门禁：$($gates.Keys -join ', ')"
    exit 1
}

Write-Section "门禁检查（共 $($names.Count) 件，层=$Tier）"

# ---- 执行 ----
$failed = 0
$results = New-Object System.Collections.Generic.List[PSCustomObject]
$failedOutputs = New-Object System.Collections.Generic.List[string]

foreach ($name in $names) {
    $spec = $gates[$name]
    # 脚本定位：清单可选 Path（仓库根相对，供 Modules\* 侧守卫登记）；缺省 = 本目录 Check-<name>.ps1。
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
    $scriptPath = if ($spec.ContainsKey('Path') -and -not [string]::IsNullOrWhiteSpace($spec.Path)) {
        Join-Path $repoRoot $spec.Path
    } else {
        Join-Path $PSScriptRoot "Check-$name.ps1"
    }

    if (-not (Test-Path -LiteralPath $scriptPath)) {
        Write-Result "FAIL" "$name — 脚本不存在: $scriptPath"
        $failed++
        $results.Add([PSCustomObject]@{ Name = $name; Status = 'FAIL'; Ms = 0; Message = '脚本不存在' })
        continue
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()

    # 哈希表 splatting（按名传参）；$spec.Args 若为 hashtable 则直接用，若为空数组则空 splatting
    $argList = $spec.Args
    $output = @()
    $rc = 2
    try {
        $output = @(& $scriptPath @argList 2>&1)
        $rc = if ($null -eq $LASTEXITCODE) { 2 } else { $LASTEXITCODE }
    } catch {
        $rc = 2
        $output = @($_.Exception.Message)
    }

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
