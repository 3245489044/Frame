<#
.SYNOPSIS
    CLI 工具数据真源承载（通用）：fail-fast 加载 + 点路径取值 + 消息模板渲染。

.DESCRIPTION
    自 `Tools\Convert-CSharpToSpec.ps1` 的外壳段抽取（N2/M1，函数体原样搬移 + 参数化）。
    机制 = 「数据文件是唯一真源，缺失/非法一律中止，不设第二份内建真源」；
    「哪张数据文件、哪些键」由调用方给出。

    依赖方向（硬约束）：CentralTest → Frame（单向）；本件零 CentralTest 引用。
#>

Set-StrictMode -Version Latest

function Import-ToolData {
    <#
    .SYNOPSIS
        fail-fast 加载工具数据文件（JSON → ordered hashtable）。
    .PARAMETER Context
        诊断前缀（工具名），用于异常文案定位。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$Context = 'ToolKit'
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw ("[{0}] 数据文件缺失：{1}（数据真源，勿删）" -f $Context, $Path)
    }

    try {
        return (Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable)
    }
    catch {
        throw ("[{0}] 数据文件非法（JSON 解析失败）：{1} —— {2}" -f $Context, $Path, $_.Exception.Message)
    }
}

function Get-ToolDataValue {
    <#
    .SYNOPSIS
        按点路径取数据值（`a.b.c`）；任一段缺失即抛（不静默返回空）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Node,
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$Context = 'ToolKit'
    )

    $cur = $Node
    foreach ($seg in $Path.Split('.')) {
        if ($null -eq $cur -or -not ($cur -is [System.Collections.IDictionary]) -or -not $cur.Contains($seg)) {
            throw ("[{0}] 数据键缺失：{1}" -f $Context, $Path)
        }
        $cur = $cur[$seg]
    }
    return $cur
}

function Get-ToolMessage {
    <#
    .SYNOPSIS
        取消息模板并按实参渲染（无实参原样返回；模板内 `{{ }}` 为 `-f` 转义写法）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Data,
        [Parameter(Mandatory = $true)][string]$Key,
        [object[]]$Arguments = @(),
        [string]$Context = 'ToolKit'
    )

    $template = [string](Get-ToolDataValue -Node $Data -Path $Key -Context $Context)
    if ($null -eq $Arguments -or $Arguments.Count -eq 0) { return $template }
    return ($template -f $Arguments)
}

Export-ModuleMember -Function @('Import-ToolData', 'Get-ToolDataValue', 'Get-ToolMessage')
