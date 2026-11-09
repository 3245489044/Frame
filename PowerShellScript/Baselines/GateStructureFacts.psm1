<#requires -Version 5.1
<#
.SYNOPSIS
    GateStructureFacts —— 「层 → 文件 → Must 令牌」结构事实的单源原语（不承载任何域词表）。

.DESCRIPTION
    动机（GB §G-9 / GB 逐件证据）：
      · `Get-MissingTokens` 在 5 个 Meta 门内逐字重复（异体仅 `[string[]]` / `[object[]]`）；
      · 6 个门重复「遍历层声明 → Join-Path → 缺件违规 → 读文件 → 逐 Must 令牌违规」循环；
      · 注释感知行提取（`//` / `*` / `/*` / `#`）在 4 处写法不一。
    本模块只做这三件事；层清单、Must 词表、违规编号一律由调用方传入。

.NOTES
    用法：
        Import-Module (Join-Path $PSScriptRoot '..\..\Baselines\GateStructureFacts.psm1') -Force
        Test-GateLayerTokenFacts -Root $root -Layers $layers -Id 'K2' -Violations $ctx.Violations
#>

# 违规追加 / 对象取数 / 文本读取：全部走既有单源，不复制第二实现。
# 注意：嵌套导入**不加 -Force**（-Force 会把调用方会话里已加载的同名模块卸载）。
Import-Module (Join-Path $PSScriptRoot 'GateRuntime.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot '..\Operation\DSL\Meta\TextOps.psm1') -DisableNameChecking

<#
.SYNOPSIS
    缺失的 Must 令牌（Ordinal 子串判定）。
.DESCRIPTION
    与 5 份域内 Get-MissingTokens 逐字等价：`$Text.IndexOf($_, [StringComparison]::Ordinal) -lt 0`。
    接受 `[object[]]` 以覆盖 `[string[]]` 与 `[object[]]` 两种异体；空白令牌忽略。
.PARAMETER Text
    被检文本（$null 视为空串 ⇒ 全部缺失）
.PARAMETER Must
    必须出现的令牌
.OUTPUTS
    [string[]] 缺失令牌（可能为空）
#>
function Get-GateMissingToken {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [AllowNull()]
        [AllowEmptyString()]
        [string]$Text,

        [Parameter(Mandatory = $true, Position = 1)]
        [AllowEmptyCollection()]
        [object[]]$Must
    )

    if ($null -eq $Text) { $Text = '' }
    $missing = [System.Collections.Generic.List[string]]::new()
    foreach ($token in @($Must)) {
        $s = [string]$token
        if ([string]::IsNullOrWhiteSpace($s)) { continue }
        if ($Text.IndexOf($s, [System.StringComparison]::Ordinal) -lt 0) { $missing.Add($s) }
    }
    return $missing.ToArray()
}

<#
.SYNOPSIS
    「层 → 文件 → Must 令牌」结构事实核验（6 份同构循环的统一实现）。
.DESCRIPTION
    逐层：
      1) 取 `File`（相对仓库根）；缺失 ⇒ 违规 `层文件不在位：<rel>`；
      2) 读文件 → 逐 `Must` 令牌 Ordinal 判定，缺 ⇒ 违规 `缺结构事实「<token>」：<rel>`。
    违规编号前缀由 `-Id` 传入（经 Add-GateViolation 统一成 `[Id]`）。
    不自行判定「层清单为空」是否违规（由调用方按域语义处置）。
.PARAMETER Root
    仓库根（层文件相对此根）
.PARAMETER Layers
    层声明集合；每项需含 `File`（string）与 `Must`（string[]）
.PARAMETER Id
    违规编号（如 'K2'、'R3'）
.PARAMETER Violations
    `New-GateContext` 提供的违规清单
.PARAMETER Detail
    逐层打印已核项数（默认静默）
.PARAMETER Encoding
    读取编码（默认 UTF8）
.OUTPUTS
    [int] 实际核验的层文件数
#>
function Test-GateLayerTokenFacts {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$Layers,

        [Parameter(Mandatory = $true)]
        [string]$Id,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[string]]$Violations,

        [switch]$Detail,

        [string]$Encoding = 'UTF8'
    )

    $checked = 0
    foreach ($layer in @($Layers)) {
        if ($null -eq $layer) { continue }

        $rel = [string](Get-GateJsonProperty -Object $layer -Name 'File')
        if ([string]::IsNullOrWhiteSpace($rel)) { continue }

        $abs = Join-Path $Root $rel
        if (-not (Test-Path -LiteralPath $abs)) {
            Add-GateViolation $Id "层文件不在位：$rel" -Violations $Violations
            continue
        }

        $text = Read-TextFile -Path $abs -Encoding $Encoding
        $must = @(Get-GateJsonProperty -Object $layer -Name 'Must')
        foreach ($token in (Get-GateMissingToken -Text $text -Must $must)) {
            Add-GateViolation $Id "缺结构事实「$token」：$rel" -Violations $Violations
        }

        if ($Detail) { Write-Host "  [$Id] $rel 已核（$($must.Count) 项）" -ForegroundColor DarkGray }
        $checked++
    }
    return $checked
}

Export-ModuleMember -Function @(
    'Get-GateMissingToken',
    'Test-GateLayerTokenFacts'
)
