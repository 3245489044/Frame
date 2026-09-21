<#
.SYNOPSIS
    git 辅助（通用）：diff / 干净度 / 取历史版本文件 / 定位仍含某路径的修订。

.DESCRIPTION
    原为 3 处各写一份：`Tools\Update-SpecGolden.ps1`（自拼 `git diff`）、
    `Tools\Convert-CSharpToSpec.ps1`（`-FromGit` 的 `git show` 与 E1-2 的修订定位）、本目录既有脚本。

    机制与本模块既有 `Modules\Git\*.ps1` 同族（同为 git 操作）；本件补齐**仓储只读查询面**（不涉及 push/branch 变更）。

    依赖方向（硬约束）：CentralTest → Frame（单向）；本件零 CentralTest 引用。
#>

Set-StrictMode -Version Latest

function Get-GitDiff {
    <#
    .SYNOPSIS
        取工作树 diff（可选路径过滤 / --stat）。
    .PARAMETER Path
        仓库相对路径过滤（多值 = 逐项追加 `-- <path>`）；缺省全仓。
    .OUTPUTS
        输出行（数组）；无 diff 时为空数组。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [string[]]$Path = @(),
        [switch]$Stat
    )

    $args = @('-C', $RepoRoot, '--no-pager', 'diff')
    if ($Stat) { $args += '--stat' }
    if ($Path.Count -gt 0) { $args += @('--') + $Path }
    return @(& git @args)
}

function Test-GitClean {
    <#
    .SYNOPSIS
        判定工作树是否干净（`git status --porcelain` 无输出）。
    .OUTPUTS
        $true = 干净；$false = 有变更（含未跟踪）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    $status = @(& git -C $RepoRoot --no-pager status --porcelain)
    return ($status.Count -eq 0)
}

function Get-GitRevisionFile {
    <#
    .SYNOPSIS
        取某修订下的文件内容（`git show <rev>:<path>`）；失败即抛（不静默返回空）。
    .PARAMETER Revision
        修订（`HEAD` / 提交号 / `<rev>^` 等）。
    .PARAMETER Path
        仓库相对路径（正斜杠或反斜杠均可，内部归一为正斜杠）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Revision,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $rel = $Path.Replace('\', '/')
    $text = & git -C $RepoRoot --no-pager show ('{0}:{1}' -f $Revision, $rel) 2>$null | Out-String
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($text)) {
        throw "[GitHelpers] 取历史版本失败：{0}:{1}（仓库 {2}）" -f $Revision, $rel, $RepoRoot
    }
    return $text
}

function Resolve-GitRevisionWithPath {
    <#
    .SYNOPSIS
        定位「仍含该路径」的修订（E1-2：verify 免手工 -FromGit）。
    .DESCRIPTION
        语义：HEAD 有则用 HEAD；否则取「最后一次触碰该路径的提交」的**父提交**（源在该版本仍在树中）。
        找不到即返回空串（调用方决定是否报错）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$RelPath
    )

    $rel = $RelPath.Replace('\', '/')
    $head = @(& git -C $RepoRoot --no-pager show ('HEAD:{0}' -f $rel) 2>$null)
    if ($LASTEXITCODE -eq 0 -and $head.Count -gt 0) { return 'HEAD' }

    $revs = @(& git -C $RepoRoot --no-pager rev-list -1 HEAD -- $rel 2>$null)
    if ($LASTEXITCODE -eq 0 -and $revs.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace([string]$revs[0])) {
        $parent = ('{0}^' -f ([string]$revs[0]).Trim())
        $hit = @(& git -C $RepoRoot --no-pager show ('{0}:{1}' -f $parent, $rel) 2>$null)
        if ($LASTEXITCODE -eq 0 -and $hit.Count -gt 0) { return $parent }
    }
    return ''
}

Export-ModuleMember -Function @('Get-GitDiff', 'Test-GitClean', 'Get-GitRevisionFile', 'Resolve-GitRevisionWithPath')
