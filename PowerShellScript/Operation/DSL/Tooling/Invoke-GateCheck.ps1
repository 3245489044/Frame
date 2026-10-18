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
    [string]$Tier = 'full',

    # 并发门禁进程数（0/1 = 串行；>1 = 每门禁独立 pwsh 进程，按上限并发）
    [int]$Parallel = 0,

    # 共享文件索引（R1）：父进程预生成一次，子门禁进程复用；-NoFileIndex 关闭
    [switch]$NoFileIndex,

    # 慢件预算（R5）：-NoTimingGate 诊断期关闭；比例相对 _gate-timing.psd1
    [switch]$NoTimingGate,
    [string]$TimingReport = '',
    [double]$TimingWarnRatio = 1.5,
    [double]$TimingFailRatio = 2.0
)

# 按需加载模块（Tooling\ → DSL\ → Operation\ → PowerShellScript\Modules）
. (Join-Path $PSScriptRoot "..\..\..\Modules\Import-Modules.ps1")

# 门禁运行时（根定位 + 落点派生**单源**，委托 PScriptManager\Framework\DirectoryMap）
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateFileIndex.psm1') -Force

$repoRoot = Get-GateRepoRoot -ScriptRoot $PSScriptRoot

# ---- 共享文件索引（R1）：父进程一次 git 索引，子门禁进程经 GATE_FILE_INDEX_CACHE 复用 ----
$script:IndexCacheFile = $null
function Initialize-GateFileIndexCache {
    if ($NoFileIndex) { return }
    $worktrees = [System.Collections.Generic.List[string]]::new()
    $worktrees.Add($repoRoot)
    $gitmodules = Join-Path $repoRoot '.gitmodules'
    if (Test-Path -LiteralPath $gitmodules) {
        foreach ($line in [IO.File]::ReadAllLines($gitmodules)) {
            $m = [regex]::Match($line, '^\s*path\s*=\s*(?<p>.+?)\s*$')
            if ($m.Success) {
                $p = Join-Path $repoRoot ($m.Groups['p'].Value -replace '/', '\')
                if (Test-Path -LiteralPath $p) { $worktrees.Add($p) }
            }
        }
    }
    $path = Join-Path ([IO.Path]::GetTempPath()) ("gate-index-" + [guid]::NewGuid().ToString('N') + ".json")
    try {
        Export-GateFileIndexCache -Worktree @($worktrees) -Path $path
        $script:IndexCacheFile = $path
        $env:GATE_FILE_INDEX_CACHE = $path
    } catch {
        Write-Warn "[R1] 共享文件索引生成失败（各门禁回退自身索引）：$($_.Exception.Message)"
    }
}
function Remove-GateIndexCacheFile {
    Remove-Item Env:GATE_FILE_INDEX_CACHE -ErrorAction SilentlyContinue
    if ($script:IndexCacheFile -and (Test-Path -LiteralPath $script:IndexCacheFile)) {
        Remove-Item -LiteralPath $script:IndexCacheFile -Force -ErrorAction SilentlyContinue
    }
}
Initialize-GateFileIndexCache

# ---- 加载清单（Registry\）----
if (-not $Manifest) {
    $Manifest = Join-Path $PSScriptRoot '..\Registry\GateManifests.psd1'
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

Write-Section "门禁检查（共 $($names.Count) 件，层=$Tier，并发=$Parallel）"

# ---- 并发执行器（门禁均为只读件；每件独立 pwsh 进程，避免 runspace 的 exit 语义歧义）----
function ConvertTo-GateArgString {
    param([hashtable]$SpecArgs)
    $parts = @()
    if ($null -eq $SpecArgs) { return '' }
    foreach ($k in $SpecArgs.Keys) {
        $v = $SpecArgs[$k]
        if ($v -is [bool]) { if ($v) { $parts += "-$k" } }
        elseif ($v -is [System.Management.Automation.SwitchParameter]) { if ($v.IsPresent) { $parts += "-$k" } }
        elseif ($null -ne $v) { $parts += ("-$k `"$v`"") }
    }
    return ($parts -join ' ')
}

function Invoke-GatesParallel {
    param(
        [string[]]$Names,
        [hashtable]$Gates,
        [int]$MaxParallel
    )
    $pwsh = Join-Path $PSHOME 'pwsh.exe'
    if (-not (Test-Path -LiteralPath $pwsh)) { $pwsh = 'powershell.exe' }
    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("gatecheck-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tmp -Force | Out-Null

    $queue = [System.Collections.Generic.Queue[string]]::new()
    foreach ($n in $Names) { $queue.Enqueue($n) }
    $running = @{}
    $collected = @{}

    while ($queue.Count -gt 0 -or $running.Count -gt 0) {
        while ($running.Count -lt $MaxParallel -and $queue.Count -gt 0) {
            $name = $queue.Dequeue()
            $spec = $Gates[$name]
            try { $scriptPath = Find-GateScript -Name $name -Domain ([string]$spec.Domain) -AllowFallback } catch { $scriptPath = '' }
            if (-not (Test-Path -LiteralPath $scriptPath)) {
                $collected[$name] = [PSCustomObject]@{ Name = $name; Status = 'FAIL'; Ms = 0; Rc = 2; Output = @("脚本不存在: $scriptPath") }
                continue
            }
            $outFile = Join-Path $tmp "$name.out.txt"
            $errFile = Join-Path $tmp "$name.err.txt"
            $argString = "-NoProfile -File `"$scriptPath`" $(ConvertTo-GateArgString -SpecArgs $spec.Args)"
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            $proc = Start-Process -FilePath $pwsh -ArgumentList $argString -NoNewWindow -PassThru `
                -RedirectStandardOutput $outFile -RedirectStandardError $errFile
            $running[$proc.Id] = [PSCustomObject]@{ Name = $name; Proc = $proc; Out = $outFile; Err = $errFile; Sw = $sw; Spec = $spec }
        }

        Start-Sleep -Milliseconds 100
        foreach ($id in @($running.Keys)) {
            $r = $running[$id]
            if (-not $r.Proc.HasExited) { continue }
            $r.Sw.Stop()
            $text = @()
            if (Test-Path -LiteralPath $r.Out) { $text += Get-Content -LiteralPath $r.Out -ErrorAction SilentlyContinue }
            if (Test-Path -LiteralPath $r.Err) { $text += Get-Content -LiteralPath $r.Err -ErrorAction SilentlyContinue }
            $rc = $r.Proc.ExitCode
            $status = if ($rc -eq $r.Spec.Expect) { 'PASS' } else { 'FAIL' }
            $collected[$r.Name] = [PSCustomObject]@{
                Name = $r.Name; Status = $status; Ms = [int]$r.Sw.Elapsed.TotalMilliseconds; Rc = $rc; Output = $text
            }
            Remove-Item -LiteralPath $r.Out, $r.Err -Force -ErrorAction SilentlyContinue
            $running.Remove($id)
        }
    }

    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
    return @($Names | ForEach-Object { $collected[$_] } | Where-Object { $null -ne $_ })
}

# ---- 执行 ----
$failed = 0
$results = New-Object System.Collections.Generic.List[PSCustomObject]
$failedOutputs = New-Object System.Collections.Generic.List[string]

if ($Parallel -gt 1 -and $names.Count -gt 1) {
    $parallelResults = @(Invoke-GatesParallel -Names $names -Gates $gates -MaxParallel $Parallel)
    foreach ($r in $parallelResults) {
        if ($r.Status -eq 'PASS') {
            Write-Result "OK" "$($r.Name) (ms=$($r.Ms))"
            $results.Add([PSCustomObject]@{ Name = $r.Name; Status = 'PASS'; Ms = $r.Ms; Message = '' })
        } else {
            Write-Result "FAIL" "$($r.Name) (ms=$($r.Ms), exit=$($r.Rc), expect=$($gates[$r.Name].Expect))"
            $failed++
            $results.Add([PSCustomObject]@{ Name = $r.Name; Status = 'FAIL'; Ms = $r.Ms; Message = "exit=$($r.Rc)" })
            $tail = @($r.Output | Select-Object -Last 20 | ForEach-Object { $_.ToString() })
            if ($tail.Count -gt 0) {
                $failedOutputs.Add("--- $($r.Name) 尾部输出 ---")
                $tail | ForEach-Object { $failedOutputs.Add("  $_") }
            }
        }
    }
}
else {

foreach ($name in $names) {
    $spec = $gates[$name]
    # 脚本定位单源 = 门禁运行时（委托目录映射框架）：Domain → 候选目录（+ 名称例外）派生；
    #   DSL\ 全域兜底 ⇒ 与目录布局解耦（移动/分类脚本无需改本聚合器）。
    $scriptPath = ''
    try {
        $scriptPath = Find-GateScript -Name $name -Domain ([string]$spec.Domain) -AllowFallback
    }
    catch {
        $scriptPath = ''
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
}

# ---- 慢件预算（R5）：逐件 ms 对比 _gate-timing.psd1；WARN 不拦，FAIL 计入失败 ----
$timingProfile = if ($Parallel -gt 1) { "parallel$Parallel" } else { 'serial' }
$timingPath = Join-Path $PSScriptRoot '..\..\..\Baselines\_gate-timing.psd1'
$timingEntries = @{}
if (Test-Path -LiteralPath $timingPath) {
    try {
        $timingData = Import-PowerShellDataFile -LiteralPath $timingPath
        if ($timingData.Tiers -and $timingData.Tiers.ContainsKey($timingProfile)) {
            $timingEntries = $timingData.Tiers[$timingProfile]
        }
    } catch {
        Write-Warn "[R5] 耗时基线不可读：$($_.Exception.Message)"
    }
}
$timingRows = [System.Collections.Generic.List[object]]::new()
$timingFailed = 0
foreach ($r in $results) {
    $base = 0
    if ($timingEntries -and $timingEntries.ContainsKey($r.Name)) {
        $base = [int]$timingEntries[$r.Name].Value
    }
    $ratio = $null
    $status = 'OK'
    $message = ''
    if ($base -gt 0) {
        $ratio = [math]::Round(([double]$r.Ms / [double]$base), 2)
        if ($ratio -ge $TimingFailRatio) {
            $status = 'FAIL'
            $message = "$($r.Name) ms=$($r.Ms) > $TimingFailRatio×基线 $base（ratio=$ratio）"
        } elseif ($ratio -ge $TimingWarnRatio) {
            $status = 'WARN'
            $message = "$($r.Name) ms=$($r.Ms) > $TimingWarnRatio×基线 $base（ratio=$ratio）"
        }
    } else {
        $status = 'NO-BASELINE'
    }
    $timingRows.Add([pscustomobject]@{
        Name = $r.Name; Ms = $r.Ms; Baseline = $base; Ratio = $ratio; Status = $status; Message = $message
    })
    if (-not $NoTimingGate) {
        if ($status -eq 'FAIL') {
            Write-Result "FAIL" "[TIMING] $message"
            $failed++
            $timingFailed++
            $failedOutputs.Add("[TIMING] $message")
        } elseif ($status -eq 'WARN') {
            Write-Warn "[TIMING] $message"
        }
    }
}

if ($TimingReport) {
    $reportDir = Split-Path -Parent $TimingReport
    if ($reportDir -and -not (Test-Path -LiteralPath $reportDir)) {
        New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
    }
    [pscustomobject]@{
        schema   = 'gate-timing-report-v1'
        profile  = $timingProfile
        parallel = $Parallel
        totalMs  = [int](($results | Measure-Object -Property Ms -Sum).Sum)
        gates    = @($timingRows)
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $TimingReport -Encoding UTF8
    Write-Result "INFO" "耗时报告：$TimingReport"
}

# ---- 失败摘要 ----
if ($failed -gt 0 -and $failedOutputs.Count -gt 0) {
    Write-Section "失败摘要"
    $failedOutputs | ForEach-Object { Write-Warn $_ }
}

# ---- 汇总 ----
$gateFailed = @($results | Where-Object { $_.Status -eq 'FAIL' }).Count
$passed = $names.Count - $gateFailed
Write-Result "INFO" "门禁 $($names.Count) 件 / 通过 $passed / 失败 $gateFailed / 计时失败 $timingFailed"

Remove-GateIndexCacheFile
if ($failed -gt 0) { exit 1 }
exit 0

