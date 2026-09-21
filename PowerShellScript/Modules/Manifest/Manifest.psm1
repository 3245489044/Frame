<#
.SYNOPSIS
    清单/台账读写与棘轮判定（通用）：形状校验 fail-fast · 键序稳定 · 无变化不写盘 · only-decrease 棘轮。

.DESCRIPTION
    「只降不升」纪律的**单源实现**（原散落在 `Tools\Sync-SpecBaseline.ps1`（基线读写）、
    `Tools\Update-SpecGolden.ps1 -PruneKnownFailures`（名单销账）、`Run-CSharpTests.ps1`（名单比对）
    与守卫侧，各写一份）。

    本模块只承载**机制**（与 spec 语义无关）：形态校验 / 键序 / 幂等写盘 / 棘轮集合运算；
    「哪张清单、哪个单元、什么条目」一律由调用方以参数给出。

    依赖方向（硬约束）：CentralTest → Frame（单向）。
#>

Set-StrictMode -Version Latest

function Read-Manifest {
    <#
    .SYNOPSIS
        读取清单 JSON（fail-fast）：文件缺失 / 非法 JSON / 缺必填顶层键一律抛。
    .PARAMETER Require
        必填顶层键（缺一即抛）；缺省 @()。
    .OUTPUTS
        ordered hashtable（键序 = 文件内顺序，供幂等写盘比对）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [string[]]$Require = @()
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "[Manifest] 清单不存在：$Path"
    }

    $data = $null
    try {
        $data = Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
    }
    catch {
        throw "[Manifest] 清单非法（JSON 解析失败）：$Path —— $($_.Exception.Message)"
    }

    $missing = @($Require | Where-Object { -not $data.Contains($_) })
    if ($missing.Count -gt 0) {
        throw ("[Manifest] 清单结构非法（缺 {0}）：{1}" -f ($missing -join ', '), $Path)
    }

    return $data
}

function Write-Manifest {
    <#
    .SYNOPSIS
        写回清单（键序稳定 + **无变化不写盘**）。
    .DESCRIPTION
        幂等口径：先把新内容序列化为文本，与盘上原文按 LF 归一后逐字比对；相同即不写盘并返回 $false
        （避免「无变化却刷新时间戳/换行」的伪变更）。
    .PARAMETER Depth
        `ConvertTo-Json` 深度（缺省 8）。
    .OUTPUTS
        $true = 已写盘；$false = 无变化未写盘。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Data,
        [int]$Depth = 8
    )

    $new = ($Data | ConvertTo-Json -Depth $Depth)

    if (Test-Path -LiteralPath $Path) {
        $old = Get-Content -LiteralPath $Path -Raw -Encoding utf8
        $norm = { param($text) ($text -replace "`r`n", "`n").TrimEnd("`n") }
        if ((& $norm $old) -eq (& $norm $new)) { return $false }
    }

    Set-Content -LiteralPath $Path -Value $new -Encoding UTF8
    return $true
}

function Get-ManifestUnit {
    <#
    .SYNOPSIS
        取 `units.<Unit>` 条目（缺即返回 $null，不抛 —— 未登记是「有失败必须登记」路径的判定输入）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$Unit
    )

    if (-not $Manifest.ContainsKey('units')) { return $null }
    $units = $Manifest['units']
    if (-not $units.ContainsKey($Unit)) { return $null }
    return $units[$Unit]
}

function Test-Ratchet {
    <#
    .SYNOPSIS
        only-decrease 棘轮判定（通用）：新条目即违规 / 已登记未销账即违规。
    .PARAMETER Actual
        实测集合（如本次失败用例名）。
    .PARAMETER Declared
        登记集合（如名单内已知红）。
    .PARAMETER SkipStale
        跳过销账检查（**收敛运行**语义：本次只跑了子集，未出现的条目不算「已修复」）。
    .OUTPUTS
        @{ New = <实测有登记无>; Stale = <登记有实测无>; Ok = <两者皆空> }
    #>
    [CmdletBinding()]
    param(
        [string[]]$Actual = @(),
        [string[]]$Declared = @(),
        [switch]$SkipStale
    )

    $actual = @($Actual | Sort-Object -Unique)
    $declared = @($Declared | Sort-Object -Unique)
    $new = @($actual | Where-Object { $declared -notcontains $_ })
    # 恒定数组：`if` 分支直接返回 `@()` 会被 PowerShell 解包为 $null，StrictMode 下 `.Count` 即抛
    # （实测：-SkipStale 路径 PropertyNotFoundException）。
    $stale = @()
    if (-not $SkipStale) { $stale = @($declared | Where-Object { $actual -notcontains $_ }) }

    return [pscustomobject]@{ New = $new; Stale = $stale; Ok = ($new.Count -eq 0 -and $stale.Count -eq 0) }
}

Export-ModuleMember -Function @('Read-Manifest', 'Write-Manifest', 'Get-ManifestUnit', 'Test-Ratchet')
