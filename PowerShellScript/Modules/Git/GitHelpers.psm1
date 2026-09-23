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

function Find-GitMatch {
    <#
    .SYNOPSIS
        仓储多模式只读检索（git grep 封装）。
    .DESCRIPTION
        把递归 grep 收敛为 git grep 调用；多 pattern = OR；支持 pathspec / 只列文件 / 未跟踪文件。
        与同模块 Get-GitDiff / Test-GitClean / Get-GitRevisionFile 同族：-RepoRoot + --no-pager + 数组返回。
        无命中返回空数组（不抛异常）。
    .PARAMETER RepoRoot
        仓库工作区根（子模块须显式传入各自根）。
    .PARAMETER Pattern
        多值 = OR（逐项转 -e）。
    .PARAMETER PathSpec
        多值 = 多 pathspec（如 '*.cs','*.json','Plug/Agent/**/*.cs'）。
    .PARAMETER List
        只列命中文件（-l）。
    .PARAMETER Untracked
        含未跟踪文件（--untracked，新增文件必需）。
    .PARAMETER IgnoreCase
        忽略大小写（-i）。
    .PARAMETER MaxCount
        每 pattern 上限（-m），默认不限。
    .PARAMETER FixedStrings
        按字面量匹配（-F），避免 pattern 内 .\/ 被当正则。
    .OUTPUTS
        字符串数组，元素形如 relpath:line:text（-List 时仅 relpath）；无命中 = 空数组。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string[]]$Pattern,
        [string[]]$PathSpec = @(),
        [switch]$List,
        [switch]$Untracked,
        [switch]$IgnoreCase,
        [int]$MaxCount = 0,
        [switch]$FixedStrings
    )

    $args = @('-C', $RepoRoot, '--no-pager', 'grep', '-n')
    if ($List)         { $args += '-l' }
    if ($IgnoreCase)   { $args += '-i' }
    if ($Untracked)    { $args += '--untracked' }
    if ($FixedStrings) { $args += '-F' }
    if ($MaxCount -gt 0) { $args += @('-m', $MaxCount) }
    foreach ($p in $Pattern) { $args += @('-e', $p) }
    if ($PathSpec.Count -gt 0) { $args += @('--') + $PathSpec }

    # git grep 无命中时退出码为 1，不视为错误；静默 stderr
    $output = @(& git @args 2>$null)
    return $output
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

Export-ModuleMember -Function @('Get-GitDiff', 'Test-GitClean', 'Get-GitRevisionFile', 'Find-GitMatch', 'Resolve-GitRevisionWithPath')
