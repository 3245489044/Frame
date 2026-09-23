<#
.SYNOPSIS
    多仓多模式只读检索（替代手写 Get-ChildItem | Select-String）
.DESCRIPTION
    基于 git grep 的多仓检索入口：
    - 默认字面量匹配（-Literal），避免 .\/ 被当正则
    - 支持多 pattern（OR）、多 pathspec、多仓库
    - 分仓分组输出，尾部汇总
    - 无命中时返回空（-FailOnEmpty 时退出码 1）

    检索核心 = Modules\Git\GitHelpers.psm1 的 Find-GitMatch（唯一实现点）。
# @requires Find-GitMatch, Write-Section, Write-Result, Write-Warn
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string[]]$Pattern,
    [string[]]$PathSpec = @('*.cs','*.json','*.jsonc','*.md','*.ps1','*.psm1'),
    [string[]]$Repo = @('Frame','Projects'),
    [switch]$Literal,
    [switch]$Regex,
    [switch]$Untracked,
    [switch]$List,
    [int]$MaxFiles = 200,
    [switch]$FailOnEmpty
)

# 按需加载模块
. (Join-Path $PSScriptRoot "..\..\Modules\Import-Modules.ps1")

# 默认字面量：免转义（避免 pattern 内含 .\/ 被当正则）
if (-not $Literal -and -not $Regex) { $Literal = $true }

# 定位 Main 根（脚本在 Frame\PowerShellScript\Share\Search\，上溯 4 级到 Main）
$mainRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))

$totalHits = 0

foreach ($r in $Repo) {
    $repoPath = Join-Path $mainRoot $r
    if (-not (Test-Path -LiteralPath $repoPath)) {
        Write-Warn "跳过不存在的仓：$r"
        continue
    }

    $hits = @(Find-GitMatch -RepoRoot $repoPath -Pattern $Pattern -PathSpec $PathSpec `
                            -List:$List -Untracked:$Untracked -IgnoreCase `
                            -FixedStrings:$Literal)

    Write-Section "$r（$(@($hits).Count) 命中）"

    if ($hits.Count -gt 0) {
        $hits | Select-Object -First $MaxFiles | ForEach-Object { Write-Result "INFO" $_ }
        if ($hits.Count -gt $MaxFiles) {
            Write-Warn "已截断：仅显示前 $MaxFiles 条（共 $($hits.Count) 条）"
        }
    }

    $totalHits += $hits.Count
}

Write-Result "INFO" "合计命中：$totalHits"

if ($FailOnEmpty -and $totalHits -eq 0) { exit 1 }
exit 0
