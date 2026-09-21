<#
.SYNOPSIS
    已知红名单棘轮比对（FD-03，通用）：新红即红 / 已修复未销账亦红（只降不升）。

.DESCRIPTION
    原实现位于 `Environment\CentralTest\TestRunner\TestRunner.psm1`，本模块为抽取目标（N1/M1，函数体原样搬移）。
    名单**文件路径由调用方给出**（`-Path`），本模块不假设任何仓库布局。

    依赖方向（硬约束）：CentralTest → Frame（单向）。
#>

Set-StrictMode -Version Latest

function Compare-KnownFailures {
    <#
    .SYNOPSIS
        已知红名单比对（FD-03）：新红即红 / 已修复未销账亦红（棘轮只降不升）。

    .DESCRIPTION
        背景：守卫长期红的集合原先只写在文档里（「84/9/1」），新回归会被存量红淹没 —— 真回归不可见。
        本器把「已知红」落为**机检名单**：逐单元比对失败用例名集合。
        - 名单缺失：不阻断，但打印**实测失败明细**（一次性 bootstrap：抄进名单即启用棘轮）；
        - 明细不可得（有失败但提取为空）：报错（否则名单比对会静默退化为「无失败」）；
        - 目标收敛运行（-Project/-Test）只判定**本次出现**的单元，未出现的单元不判定。

    .OUTPUTS
        $true = 名单一致（或名单缺失时的 bootstrap 模式）；$false = 新红 / 未销账 / 明细不可得。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object[]]$Results,
        [string]$Path = '',
        [switch]$PartialRun
    )

    $units = @($Results | Where-Object { $_.Kind -eq 'MsTest' -and $_.Failed -gt 0 })
    if ($units.Count -eq 0) { return $true }

    $opaque = @($units | Where-Object { @($_.FailedNames).Count -eq 0 })
    if ($opaque.Count -gt 0) {
        Write-Host ("  [KNOWN-FAIL] 失败明细不可得：{0} —— 名单机制失效（不静默），须修输出解析。" -f
            (($opaque | ForEach-Object { $_.ProjectName }) -join ', ')) -ForegroundColor Red
        return $false
    }

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path $Path)) {
        Write-Host "  [KNOWN-FAIL] 未找到已知红名单（FD-03）—— 本次不阻断，实测失败明细如下：" -ForegroundColor Yellow
        if (-not [string]::IsNullOrWhiteSpace($Path)) {
            Write-Host ("              名单路径：{0}" -f $Path) -ForegroundColor DarkGray
        }
        foreach ($unit in $units) {
            Write-Host ("    {0}（{1} 条）:" -f $unit.ProjectName, @($unit.FailedNames).Count) -ForegroundColor Yellow
            foreach ($name in @($unit.FailedNames)) { Write-Host ("      {0}" -f $name) -ForegroundColor DarkYellow }
        }
        Write-Host "    提示：把上述清单登记到名单文件即启用棘轮（新红即红 / 已修复未销账亦红）。" -ForegroundColor DarkGray
        return $true
    }

    $manifest = Get-Content $Path -Raw | ConvertFrom-Json
    $registry = $manifest.units
    $ok = $true
    $unregistered = New-Object System.Collections.Generic.List[string]

    foreach ($unit in $units) {
        $key = ([string]$unit.ProjectName) -replace '\.csproj$', ''
        $entry = $null
        if ($registry -and ($registry.PSObject.Properties.Name -contains $key)) { $entry = $registry.$key }

        if ($null -eq $entry) {
            $ok = $false
            $unregistered.Add($key)
            continue
        }

        $actual = @($unit.FailedNames | Sort-Object -Unique)
        $known = @($entry.knownFailures | Sort-Object -Unique)
        $new = @($actual | Where-Object { $known -notcontains $_ })
        # 目标收敛运行（-Test/-Class/…）只跑了单元内**子集**：未运行的已知红不在本次实际失败集里，
        # 若照全量口径判「未销账」即误报（实测：收敛跑 1 个用例失败 → 误报「名单未销账 9 条」）。
        # 故收敛运行只判「新失败」；销账检查以全量运行为权威。
        $stale = if ($PartialRun) { @() } else { @($known | Where-Object { $actual -notcontains $_ }) }

        if ($new.Count -gt 0) {
            $ok = $false
            Write-Host ("  [KNOWN-FAIL] 新失败 {0} 条（不在名单）：{1}" -f $new.Count, $key) -ForegroundColor Red
            foreach ($name in $new) { Write-Host ("      {0}" -f $name) -ForegroundColor Red }
        }

        if ($stale.Count -gt 0) {
            $ok = $false
            Write-Host ("  [KNOWN-FAIL] 名单未销账 {0} 条（已修复则同提交下调名单）：{1}" -f $stale.Count, $key) -ForegroundColor Red
            foreach ($name in $stale) { Write-Host ("      {0}" -f $name) -ForegroundColor Red }
        }

        if ($new.Count -eq 0 -and $stale.Count -eq 0) {
            Write-Host ("  [KNOWN-FAIL] 名单一致：{0}（已知红 {1} 条，零新增零销账）" -f $key, $known.Count) -ForegroundColor DarkGray
        }
    }

    if ($unregistered.Count -gt 0) {
        Write-Host ("  [KNOWN-FAIL] 运行单元未登记名单（有失败必须登记，禁无基线纳入）：{0}" -f ($unregistered -join ', ')) -ForegroundColor Red
        Write-Host ("              名单：{0}" -f $Path) -ForegroundColor DarkGray
    }

    return $ok
}

Export-ModuleMember -Function @('Compare-KnownFailures')
