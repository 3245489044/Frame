<#
.SYNOPSIS
    启动面自测（干跑，不启 GUI）—— 契约 / 参数 / 退出码 / trace / 产品无关性。

.DESCRIPTION
    把「只能人工双击」变成可复算：全部判据 = 文件结构 + 表项比对（结构事实，不读执行者叙述）。
      T1 契约在位且 Schema 匹配
      T2 契约合格性（Test-LaunchContract：字段完备 / 入口双向映射 / 禁 BuildOutput 字面）
      T3 执行体退出码：常量退出码 ⊆ 契约 ExitCodes，契约 ExitCodes\{0} ⊆ 执行体常量退出码（0 只来自进程）
      T4 参数白名单 ⇔ 执行体开关分支（双向）
      T5 trace：契约 Trace.Dir 非空 + 执行体含阶段/trace 写入锚点
      T6 产品无关性：执行体不含产品名字面（filedriven / FileDriven / Launcher.exe / 运行测试GUI模式）

    退出码：0 pass / 1 fail / 2 前置缺失。
#>
[CmdletBinding()]
param(
    [string]$Product = 'filedriven',
    [switch]$Json
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'LaunchContract.psm1') -Force -DisableNameChecking

$repoRoot = Get-LaunchRepoRoot -StartPath $PSScriptRoot
$executor = Join-Path $PSScriptRoot 'Invoke-LaunchEntry.ps1'

$results = [System.Collections.Generic.List[object]]::new()
function Add-Result([string]$Id, [bool]$Ok, [string]$Detail) {
    $results.Add([pscustomobject]@{ Id = $Id; Ok = $Ok; Detail = $Detail })
}

if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    Add-Result 'T0' $false '仓库根定位失败'
}
else {
    $contractPath = Get-LaunchContractPath -RepoRoot $repoRoot -Product $Product
    if (-not (Test-Path -LiteralPath $contractPath)) {
        Add-Result 'T1' $false ("契约缺失 {0}" -f $contractPath)
    }
    elseif (-not (Test-Path -LiteralPath $executor)) {
        Add-Result 'T1' $false ("执行体缺失 {0}" -f $executor)
    }
    else {
        $contract = Import-LaunchContract -Path $contractPath
        Add-Result 'T1' ([string]$contract['Schema'] -eq 'launch-contract-v1') ("Schema={0}" -f [string]$contract['Schema'])

        $violations = @(Test-LaunchContract -Contract $contract -ContractPath $contractPath -RepoRoot $repoRoot)
        Add-Result 'T2' ($violations.Count -eq 0) ($(if ($violations.Count -eq 0) { '契约合格' } else { $violations -join ' | ' }))

        $text = [System.IO.File]::ReadAllText($executor)
        $declared = @($contract['ExitCodes'].Keys | ForEach-Object { [int]$_ })
        $literalExits = @([regex]::Matches($text, '(?m)^\s*(?:Exit-Launch\s+-Code|exit)\s+(\d+)\s*$') | ForEach-Object { [int]$_.Groups[1].Value } | Sort-Object -Unique)
        $undeclaredLiteral = @($literalExits | Where-Object { $declared -notcontains $_ })
        $missingLiteral = @($declared | Where-Object { $_ -ne 0 -and $literalExits -notcontains $_ })
        $hasZeroLiteral = [regex]::IsMatch($text, '(?m)^\s*(?:Exit-Launch\s+-Code|exit)\s+0\s*$')
        $ok3 = ($undeclaredLiteral.Count -eq 0) -and ($missingLiteral.Count -eq 0) -and (-not $hasZeroLiteral) -and
            ($text -match '\.WaitForExit\(') -and ($text -match 'exit\s+\$processExitCode')
        Add-Result 'T3' $ok3 ("literals=[{0}] declared=[{1}] zeroLiteral={2}" -f ($literalExits -join ','), ($declared -join ','), $hasZeroLiteral)

        $whitelist = @($contract['Parameters'].Keys)
        $switchCases = @([regex]::Matches($text, "(?m)^\s*'(--[A-Za-z0-9\-]+)'\s*\{") | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        $missingInImpl = @($whitelist | Where-Object { $switchCases -notcontains $_ })
        $extraInImpl = @($switchCases | Where-Object { $whitelist -notcontains $_ })
        Add-Result 'T4' (($missingInImpl.Count -eq 0) -and ($extraInImpl.Count -eq 0)) ("whitelist={0} impl={1} missing={2} extra={3}" -f $whitelist.Count, $switchCases.Count, ($missingInImpl -join ','), ($extraInImpl -join ','))

        $traceDir = [string]$contract['Trace']['Dir']
        $ok5 = (-not [string]::IsNullOrWhiteSpace($traceDir)) -and ($text -match 'trace_version=1') -and ($text -match '\[S1\] 契约校验') -and ($text -match '\[S6\] 启动')
        Add-Result 'T5' $ok5 ("Trace.Dir={0}" -f $traceDir)

        $forbidden = @('filedriven', 'FileDriven', 'Launcher.exe', '运行测试GUI模式')
        $hits = @($forbidden | Where-Object { $text.Contains($_) })
        Add-Result 'T6' ($hits.Count -eq 0) ($(if ($hits.Count -eq 0) { '执行体零产品字面' } else { '命中：' + ($hits -join ', ') }))
    }
}

$failed = @($results | Where-Object { -not $_.Ok })

if ($Json) {
    [pscustomobject]@{ schema = 'launch-selftest-v1'; product = $Product; ok = ($failed.Count -eq 0); checks = @($results) } |
        ConvertTo-Json -Depth 5
}
else {
    Write-Host ''
    Write-Host '============================================================' -ForegroundColor Cyan
    Write-Host ("  启动面自测（干跑）产品={0}" -f $Product) -ForegroundColor Cyan
    Write-Host '============================================================' -ForegroundColor Cyan
    foreach ($r in $results) {
        $tag = if ($r.Ok) { '[PASS]' } else { '[FAIL]' }
        $color = if ($r.Ok) { 'Green' } else { 'Red' }
        Write-Host ("  {0} {1} — {2}" -f $tag, $r.Id, $r.Detail) -ForegroundColor $color
    }
    Write-Host ''
    if ($failed.Count -eq 0) { Write-Host '  [OK] 启动面自测通过（未启 GUI）' -ForegroundColor Green }
    else { Write-Host ("  [FAIL] {0} 项不合格" -f $failed.Count) -ForegroundColor Red }
}

if ($failed.Count -gt 0) { exit 1 }
exit 0
