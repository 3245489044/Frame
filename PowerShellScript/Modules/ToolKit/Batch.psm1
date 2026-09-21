<#
.SYNOPSIS
    子进程批量驱动（通用）：并发派发 + 逐 job 记账 + 摘要筛选 + 汇总 + JSON 证据。

.DESCRIPTION
    消**两份平行实现**：`Tools\Convert-CSharpToSpec.ps1 -BatchFile`（job → 本工具开关）与
    `Tools\Invoke-SpecBatch.ps1`（stage → 运行器开关）。二者原先各自手写「组装 argv → 派发 pwsh 子进程 →
    逐条记 exit/时长 → 筛摘要行 → 汇总 → 落 JSON 证据」。

    本模块只承载**机制**：job 的 argv / 标签 / 消息模板一律由调用方给出（零领域词）。

    依赖方向（硬约束）：CentralTest → Frame（单向）；本件零 CentralTest 引用。
#>

Set-StrictMode -Version Latest

function Invoke-ProcessBatch {
    <#
    .SYNOPSIS
        并发派发 job（每 job = 一次 `pwsh -NoProfile -File <argv[0]> @argv[1..]` 子进程）并逐条记账。
    .PARAMETER Jobs
        `@{ Label = <记账标签>; Argv = <argv 数组> }`（Argv[0] = 目标脚本路径）。
    .PARAMETER ThrottleLimit
        并发上限（≤0 视作 1）。
    .OUTPUTS
        records：`[{ Label; ExitCode; DurationS; Output }]`（序 = 完成序）。
    .NOTES
        「逐个跑 + 边跑边回声 + 失败即停」的场景用 <see cref="Invoke-ProcessBatchJob"/> 自行编排
        （消费方需要逐 job 回显，本函数为并发批量路径，不承担回声）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object[]]$Jobs,
        [int]$ThrottleLimit = 1
    )

    $limit = [Math]::Max(1, $ThrottleLimit)

    # 自然枚举返回（勿用 `return , $results`：那会把整批包成单元素数组，消费方 `@()` 再包一层即嵌套）
    return @($Jobs | ForEach-Object -Parallel {
            $job = $_
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            $argv = @($job.Argv)
            $output = (& pwsh -NoProfile -File $argv[0] @($argv | Select-Object -Skip 1) 2>&1 | Out-String)
            $sw.Stop()
            [pscustomobject]@{
                Label     = [string]$job.Label
                ExitCode  = $LASTEXITCODE
                DurationS = [math]::Round($sw.Elapsed.TotalSeconds, 1)
                Output    = $output
            }
        } -ThrottleLimit $limit)
}

function Invoke-ProcessBatchJob {
    <#
    .SYNOPSIS
        派发单个 job（同步；`Invoke-ProcessBatch -StopOnError` 的逐步路径）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Job
    )

    $argv = @($Job.Argv)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $output = (& pwsh -NoProfile -File $argv[0] @($argv | Select-Object -Skip 1) 2>&1 | Out-String)
    $sw.Stop()

    return [pscustomobject]@{
        Label     = [string]$Job.Label
        ExitCode  = $LASTEXITCODE
        DurationS = [math]::Round($sw.Elapsed.TotalSeconds, 1)
        Output    = $output
    }
}

function Get-BatchSummary {
    <#
    .SYNOPSIS
        汇总批次记录。
    .OUTPUTS
        `{ Total; Passed; Failed; FailedLabels }`
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object[]]$Records
    )

    $all = @($Records)
    $failed = @($all | Where-Object { $_.ExitCode -ne 0 })
    return [pscustomobject]@{
        Total        = $all.Count
        Passed       = $all.Count - $failed.Count
        Failed       = $failed.Count
        FailedLabels = @($failed | ForEach-Object { $_.Label })
    }
}

function Get-BatchOutputLines {
    <#
    .SYNOPSIS
        取子进程输出中匹配 `-Pattern` 的行（Trim 后返回；无 `-Pattern` 即全行）。
    .DESCRIPTION
        两处消费方都只需「回显子运行器摘要行」—— 避免把子进程全量输出重复刷屏。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Output,
        [string]$Pattern = ''
    )

    $lines = @($Output -split "`r?`n")
    if ($Pattern) { $lines = @($lines | Where-Object { $_ -match $Pattern }) }
    # 自然枚举返回（消费方以 `foreach` 逐行回显；`return , @(...)` 会让整批行落成单元素，回显挤成一行）
    return @($lines | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

function Write-BatchReport {
    <#
    .SYNOPSIS
        JSON 证据落盘（目录不存在则建；尾补换行）。
    .DESCRIPTION
        两处消费方的 payload 形状不同（`jobs[]` / `stages[]`），故只收口「落盘」机制；键名与内容由调用方给出。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Payload,
        [int]$Depth = 6
    )

    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    (($Payload | ConvertTo-Json -Depth $Depth) + "`n") | Set-Content -LiteralPath $Path -Encoding utf8
}

Export-ModuleMember -Function @(
    'Invoke-ProcessBatch', 'Invoke-ProcessBatchJob', 'Get-BatchSummary', 'Get-BatchOutputLines', 'Write-BatchReport'
)

