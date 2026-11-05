#requires -Version 5.1
<#
.SYNOPSIS
    GateFacts —— C# facts 命令的「取数 + 归一化 + 比较」单源，不承载任何业务常量。

.DESCRIPTION
    唯一事实来源仍是 C# 工具（LedgerForge / Usharp.Bundle 的 facts 子命令）。
    本模块只把各门重复写的「建临时目录 → 调外部工具 → 重试读取 → JSON 解析 → 清理」
    收成一个实现，并提供 facts 文本的 Ordinal 对拍原语。

    5.1 兼容（硬约束）：
      · 本模块**不使用** #requires -Version 7.0；
      · 不使用 ConvertFrom-Json -AsHashtable / ConvertFrom-Json -Depth（5.1 无这些参数）；
      · 不复制常量：前缀 / kind 词表 / 槽长 / 规则号全部来自 C# facts 输出。

.NOTES
    用法（门禁脚本内）：
        Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateFacts.psm1') -Force -DisableNameChecking
        $facts = Get-GateTransferFacts -LedgerForge $ledgerForge -UsharRoot $path -ConstantsPath $consts -CodecPath $codec
        if (-not $facts.Ok) { ... }
#>

<#
.SYNOPSIS
    执行外部事实命令并把产物读成结构化结果（不抛异常；失败由调用方判）。

.PARAMETER ToolPath
    事实工具绝对路径。
.PARAMETER Arguments
    工具参数（不含输出路径；输出路径由本函数追加）。
.PARAMETER OutputStyle
    Flag = 追加 `--out <path>`；Positional = 追加 `<path>`（Usharp.Bundle 用位置参数）。
.PARAMETER AcceptExit
    视为成功的退出码集合（默认仅 0）。
.PARAMETER RawText
    只读原始文本，不做 JSON 解析（keyspace facts 为规范化文本）。
.OUTPUTS
    { Ok; ExitCode; Raw; Payload; OutPath; Error }
#>
function Invoke-GateFactTool {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ToolPath,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Arguments,
        [ValidateSet('Flag', 'Positional')][string]$OutputStyle = 'Flag',
        [int[]]$AcceptExit = @(0),
        [switch]$RawText
    )

    $probeDir = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), ('gatefacts-' + [guid]::NewGuid().ToString('N')))
    $outPath = Join-Path $probeDir 'out.json'
    $result = [pscustomobject]@{
        Ok       = $false
        ExitCode = -1
        Raw      = $null
        Payload  = $null
        OutPath  = $null
        Error    = $null
    }

    try {
        New-Item -ItemType Directory -Force -Path $probeDir | Out-Null
        $result.OutPath = $outPath

        $argv = @($Arguments)
        if ($OutputStyle -eq 'Positional') { $argv += $outPath }
        else { $argv += @('--out', $outPath) }

        & $ToolPath @argv | Out-Null
        $code = $LASTEXITCODE
        $result.ExitCode = $code

        if (-not (Test-Path -LiteralPath $outPath)) {
            $result.Error = "未产出事实产物（退出码 $code）"
            return $result
        }

        # 生成器刚写完的产物可能被索引/杀软短暂占用：重试读取（不改内容）。
        $raw = $null
        for ($attempt = 0; $attempt -lt 5; $attempt++) {
            try { $raw = [System.IO.File]::ReadAllText($outPath); break }
            catch { Start-Sleep -Milliseconds 50 }
        }
        if ($null -eq $raw) {
            $result.Error = "无法读取事实产物：$outPath"
            return $result
        }

        $result.Raw = $raw
        $result.Ok = ($AcceptExit -contains $code)

        if (-not $RawText) {
            try {
                $result.Payload = $raw | ConvertFrom-Json
            }
            catch {
                $result.Ok = $false
                $result.Error = "事实产物不是合法 JSON：$($_.Exception.Message)"
            }
        }
        return $result
    }
    catch {
        $result.Ok = $false
        $result.Error = $_.Exception.Message
        return $result
    }
    finally {
        if (Test-Path -LiteralPath $probeDir) { Remove-Item -LiteralPath $probeDir -Recurse -Force }
    }
}

<#
.SYNOPSIS
    gather spec 校验包装：LedgerForge gather-validate（唯一实现 = C# GatherSpecLoader）。
.OUTPUTS
    { Ok; ExitCode; Raw; Payload(issues); OutPath; Error }
#>
function Get-GateGatherIssues {
    param(
        [Parameter(Mandatory = $true)][string]$LedgerForge,
        [Parameter(Mandatory = $true)][string]$Directory
    )
    return Invoke-GateFactTool -ToolPath $LedgerForge -Arguments @('gather-validate', '--dir', $Directory) -AcceptExit @(0, 1)
}

<#
.SYNOPSIS
    keyspace facts 规范化文本（Usharp.Bundle keyspace-facts / keyspace-facts-json）。
.DESCRIPTION
    失败即抛错（与门原行为一致：无法产出事实 = 不可判定）。
#>
function Get-GateKeyspaceFactsText {
    param(
        [Parameter(Mandatory = $true)][string]$BundlePath,
        [Parameter(Mandatory = $true)][string]$InputPath,
        [ValidateSet('keyspace-facts', 'keyspace-facts-json')][string]$Mode = 'keyspace-facts'
    )
    $probe = Invoke-GateFactTool -ToolPath $BundlePath -Arguments @($Mode, $InputPath) -OutputStyle Positional -AcceptExit @(0) -RawText
    if (-not $probe.Ok) {
        throw "Usharp.Bundle $Mode 失败（输入：$InputPath）"
    }
    return $probe.Raw
}

<#
.SYNOPSIS
    facts 文本 Ordinal 对拍（大小写不同即不等；不做归一化猜测）。
#>
function Test-GateFactTextEqual {
    param(
        [Parameter(Mandatory = $true)][AllowNull()][string]$Expected,
        [Parameter(Mandatory = $true)][AllowNull()][string]$Actual
    )
    return [string]::Equals($Expected, $Actual, [System.StringComparison]::Ordinal)
}

<#
.SYNOPSIS
    转移键事实：LedgerForge transfer-facts（词表 / 文法 / 槽长边界）。
#>
function Get-GateTransferFacts {
    param(
        [Parameter(Mandatory = $true)][string]$LedgerForge,
        [Parameter(Mandatory = $true)][string]$UsharRoot,
        [Parameter(Mandatory = $true)][string]$ConstantsPath,
        [Parameter(Mandatory = $true)][string]$CodecPath
    )
    return Invoke-GateFactTool -ToolPath $LedgerForge -Arguments @(
        'transfer-facts', '--ushar', $UsharRoot, '--consts', $ConstantsPath, '--codec', $CodecPath
    ) -AcceptExit @(0, 1)
}

<#
.SYNOPSIS
    PS 运行时事实：LedgerForge ps-runtime-facts（契约根唯一实现 = PowerShellHostRoots）。
#>
function Get-GatePsRuntimeFacts {
    param(
        [Parameter(Mandatory = $true)][string]$LedgerForge,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$AgentRoot,
        [Parameter(Mandatory = $true)][string]$FrameRoot
    )
    return Invoke-GateFactTool -ToolPath $LedgerForge -Arguments @(
        'ps-runtime-facts', '--repo', $RepoRoot, '--agent', $AgentRoot, '--frame-root', $FrameRoot
    ) -AcceptExit @(0)
}

<#
.SYNOPSIS
    契约事实：LedgerForge contract-facts；生成常量过期即抛错（不算通过）。
#>
function Get-GateContractFacts {
    param(
        [Parameter(Mandatory = $true)][string]$LedgerForge,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$AgentRoot
    )
    $probe = Invoke-GateFactTool -ToolPath $LedgerForge -Arguments @(
        'contract-facts', '--repo', $RepoRoot, '--agent', $AgentRoot
    ) -AcceptExit @(0)
    if (-not $probe.Ok) {
        throw "contract-facts 不可执行（退出码 $($probe.ExitCode)）"
    }

    $facts = $probe.Payload
    if ($facts.constantsStale) {
        $detail = (@($facts.constantMismatches) | Select-Object -First 3) -join '；'
        throw "生成契约常量已过期（与已提交 manifest 不一致）：$detail"
    }
    return $facts
}

Export-ModuleMember -Function @(
    'Invoke-GateFactTool',
    'Get-GateGatherIssues',
    'Get-GateKeyspaceFactsText',
    'Test-GateFactTextEqual',
    'Get-GateTransferFacts',
    'Get-GatePsRuntimeFacts',
    'Get-GateContractFacts'
)
