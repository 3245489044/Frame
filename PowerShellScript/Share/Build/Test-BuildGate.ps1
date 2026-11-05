<#
.SYNOPSIS
    启动前构建门（构建判定单源）—— 判定「本次启动是否需要编译」。

.DESCRIPTION
    判据（结构事实；全部由 git 产出，不引入第二套指纹体系）：
      A 提交态   : 各仓 HEAD（Main + 逐级子模块 checked-out SHA）与上次成功构建记录一致
      B 工作树态 : 各仓工作树内容标识一致
                   = 已跟踪改动（git diff HEAD --raw 的 blob 哈希）⊕ 构建相关未跟踪文件（git hash-object）
                   判据载体 = 内容哈希，非 mtime
      C 产物态   : 启动链关键产物在位（闭包派生，禁手抄清单）
    Skip ⇔ A ∧ B ∧ C；任一不符 ⇒ Build；判据不可得 ⇒ Build（fail-closed）。

    语义（消除旧实现的两极）：工作区脏既不是「跳过」的理由，也不是「永远重编」的理由 ——
    脏而内容未再变（上次已构建过这份内容）⇒ Skip；脏且内容变了 ⇒ Build。
    只改 Docs/crash 等非构建文件不进入 B 判据（Test-BuildRelevantPath 过滤）。

    账本：<RepoRoot>\BuildOutput\.build-gate\last-built.json（schema=build-gate-v2）
          唯一写者 = 本脚本 -UpdateStamp（脏状态照写：账本记录「这份内容已构建过」）。
          由唯一启动入口（Share\Launch\Invoke-Launch.ps1）在构建成功后调用。

    退出码：0 = 需要编译 / 10 = 跳过（新鲜）/ 2 = 前置失败（路径/模块缺失）
    用法：
      Test-BuildGate.ps1                → 判定（0/10）
      Test-BuildGate.ps1 -Json          → 判定 + 证据（stdout 仅 JSON）
      Test-BuildGate.ps1 -UpdateStamp   → 记录当前状态为「已构建」
      Test-BuildGate.ps1 -Invalidate    → 清除账本（强制下次编译）
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = '',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$UpdateStamp,
    [switch]$Invalidate,
    [switch]$Json,
    # 未跟踪文件纳入 B 判据的上限；超限 ⇒ 判据不可得 ⇒ Build（fail-closed，避免全树哈希）
    [int]$UntrackedHashCap = 200
)

$ErrorActionPreference = 'Stop'

function Write-Gate {
    param([string]$Text, [string]$Color = 'Gray')
    Write-Host "[BUILD-GATE] $Text" -ForegroundColor $Color
}

$coreModule = Join-Path $PSScriptRoot '..\..\Modules\Core\ReadProjectConfig.ps1'
if (-not (Test-Path -LiteralPath $coreModule)) {
    Write-Gate ("缺少框架路径模块：{0}" -f $coreModule) 'Red'
    exit 2
}
. $coreModule

if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Get-ProjectRoot }
if (-not (Test-Path -LiteralPath $RepoRoot)) {
    Write-Gate ("仓库根不存在：{0}" -f $RepoRoot) 'Red'
    exit 2
}
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path

$stampDir = Join-Path $RepoRoot 'BuildOutput\.build-gate'
$stampFile = Join-Path $stampDir 'last-built.json'
$stampSchema = 'build-gate-v2'

if ($Invalidate) {
    if (Test-Path -LiteralPath $stampFile) {
        Remove-Item -LiteralPath $stampFile -Force
        Write-Gate '已清除构建账本（下次启动按 git 态重判）' 'DarkGray'
    }
    exit 0
}

$gitCommand = Get-Command -Name 'git' -ErrorAction SilentlyContinue
if ($null -eq $gitCommand -or [string]::IsNullOrWhiteSpace($gitCommand.Source)) {
    Write-Gate '未找到 git：判据不可得，按需要编译' 'Yellow'
    if ($UpdateStamp) { exit 1 }
    if ($Json) { (@{ decision = 'Build'; reasonCode = 'toolchain-unknown'; evidence = @{} } | ConvertTo-Json -Depth 6 -Compress) }
    exit 0
}
$gitExe = $gitCommand.Source

function Invoke-GitText {
    param(
        [string]$RepoPath,
        [string[]]$GitArgs,
        [switch]$AllowFail
    )

    $out = & $gitExe -c core.quotepath=false -C $RepoPath @GitArgs 2>$null
    if ($LASTEXITCODE -ne 0 -and -not $AllowFail) {
        throw ("git {0} 失败（exit {1}，repo={2}）" -f ($GitArgs -join ' '), $LASTEXITCODE, $RepoPath)
    }
    return @($out)
}

function Get-Sha256Hex {
    param([string]$Text)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

# ---- 构建相关性（哪些改动算「代码变更」；判据载体 = 路径结构事实）----
function Test-IsGitlink {
    param([string]$Path)

    try {
        $out = & $gitExe -C $RepoRoot ls-files -s -- $Path 2>$null
        return [bool]($out -match '^160000\s')
    }
    catch {
        return $false
    }
}

function Test-BuildRelevantPath {
    param(
        [string]$Path,
        [switch]$MainRepo
    )

    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    $normalized = $Path.Replace('/', '\')

    # 构建产物/临时目录不属于「代码修改在途」
    if ($normalized -match '(^|\\)(BuildOutput|obj|bin|Temp|\.vs|\.vscode)(\\|$)') { return $false }
    if ($normalized -match '^(Docs|doc)(\\|$)') { return $false }

    if ($MainRepo) {
        if (Test-IsGitlink -Path $normalized) { return $false }   # 子模块条目由逐仓检查覆盖
        if ($normalized -match '^(Frame|Projects)(\\|$)') { return $true }
        if ($normalized -match '^(Directory\.Build\.(props|targets)|Directory\.Packages\.props|global\.json|NuGet\.config|\.gitmodules)$') { return $true }
        return $false
    }

    # 子模块内部：除 Docs/产物外的改动都视为构建相关
    return $true
}

function Get-SubmodulePaths {
    $lines = @(Invoke-GitText -RepoPath $RepoRoot -GitArgs @('submodule', 'status', '--recursive') -AllowFail)
    $paths = foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line -match '^(?<flag>[ +\-U])(?<sha>[0-9a-fA-F]{7,40})\s+(?<path>\S+)') { $Matches['path'] }
    }
    return @($paths)
}

# 单仓状态：head（提交态）+ worktreeId（工作树内容态）+ 可见化明细
function Get-RepoState {
    param([string]$RelPath = '')

    $isMain = [string]::IsNullOrWhiteSpace($RelPath)
    $repoPath = if ($isMain) { $RepoRoot } else { Join-Path $RepoRoot $RelPath }

    $head = ''
    $headOut = @(Invoke-GitText -RepoPath $repoPath -GitArgs @('rev-parse', 'HEAD') -AllowFail)
    if ($LASTEXITCODE -eq 0 -and $headOut.Count -gt 0) { $head = ([string]$headOut[0]).Trim().ToLowerInvariant() }
    if ([string]::IsNullOrWhiteSpace($head)) {
        return [pscustomobject]@{
            Path = $RelPath; Head = ''; WorktreeId = ''; Unborn = $true
            Changed = @(); Unresolved = @('unborn-or-unreadable')
        }
    }

    $entries = [System.Collections.Generic.List[string]]::new()
    $changed = [System.Collections.Generic.List[string]]::new()
    $unresolved = [System.Collections.Generic.List[string]]::new()

    # 已跟踪改动：git diff HEAD --raw（blob 哈希为内容判据，二进制无编码问题）
    $raw = @(Invoke-GitText -RepoPath $repoPath -GitArgs @('diff', 'HEAD', '--raw', '--no-abbrev', '--no-color') -AllowFail)
    foreach ($line in $raw) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line -notmatch '^:\d+\s+\d+\s+(?<old>[0-9a-f]+)\s+(?<new>[0-9a-f]+)\s+(?<status>[A-Za-z?]+)\t(?<path>.+)$') { continue }
        $p = $Matches['path']
        if ($p -match ' -> ') { $p = ($p -split ' -> ')[-1] }
        if (-not (Test-BuildRelevantPath -Path $p -MainRepo:$isMain)) { continue }
        $entries.Add(('{0}|{1}|{2}' -f $p, $Matches['status'], $Matches['new']))
        $changed.Add($p)
    }

    # 构建相关未跟踪文件：内容哈希（git hash-object，不写对象库）
    $untracked = @(Invoke-GitText -RepoPath $repoPath -GitArgs @('ls-files', '-o', '--exclude-standard') -AllowFail)
    $relevantUntracked = @($untracked | Where-Object { $_ -and (Test-BuildRelevantPath -Path $_ -MainRepo:$isMain) })
    if ($relevantUntracked.Count -gt $UntrackedHashCap) {
        $unresolved.Add(('untracked-over-cap:{0}>{1}' -f $relevantUntracked.Count, $UntrackedHashCap))
    }
    else {
        foreach ($p in $relevantUntracked) {
            $fp = @(Invoke-GitText -RepoPath $repoPath -GitArgs @('hash-object', '--', $p) -AllowFail)
            if ($LASTEXITCODE -ne 0 -or $fp.Count -eq 0) { $unresolved.Add(('untracked-unhashable:{0}' -f $p)); continue }
            $entries.Add(('{0}|U|{1}' -f $p, ([string]$fp[0]).Trim()))
            $changed.Add($p)
        }
    }

    $worktreeId = if ($unresolved.Count -gt 0) { '' } else { Get-Sha256Hex -Text ((@($entries) | Sort-Object) -join "`n") }

    return [pscustomobject]@{
        Path = $RelPath; Head = $head; WorktreeId = $worktreeId; Unborn = $false
        Changed = @($changed); Unresolved = @($unresolved)
    }
}

# 关键产物 = 启动链自身两件 + 装配闭包（声明派生；单一实现 = Share\AssemblyClosure\AssemblyClosure.psm1）
function Test-KeyArtifacts {
    $required = [System.Collections.Generic.List[string]]::new()
    $required.Add((Join-Path $RepoRoot "Projects\FileDriven\BuildOutput\$Configuration\FileDriven.Launcher.exe"))
    $required.Add((Join-Path $RepoRoot "Frame\CsharpPrj\BuildOutput\$Configuration\BasicMethod.dll"))

    $closureModule = Join-Path $RepoRoot 'Frame\PowerShellScript\Share\AssemblyClosure\AssemblyClosure.psm1'
    if (Test-Path -LiteralPath $closureModule) {
        Import-Module $closureModule -Force -DisableNameChecking
        foreach ($s in @(Get-ClosureStates -RepoRoot $RepoRoot -Configuration $Configuration)) {
            if (-not $s.Required) { continue }
            foreach ($m in @($s.Missing)) {
                if ($m -like 'manifest:*') { $required.Add((Join-Path $s.Root $m.Substring('manifest:'.Length))) }
                elseif ($m -like 'entry:*') { $required.Add((Join-Path $s.Root $m.Substring('entry:'.Length))) }
                elseif ($m -like 'root:*') { $required.Add($s.Root) }
            }
        }
    }
    else {
        # 闭包实现缺失 ⇒ 判据不可得，按缺失处理（禁静默降级为「两件都在」）
        $required.Add('assembly-closure-module-missing')
    }

    return @($required | Where-Object { -not (Test-Path -LiteralPath $_) } | Select-Object -Unique)
}

# ---- 汇总当前 git 态（Main + 逐级子模块；逐仓判定，子模块内部脏主仓看不见）----
$stateRows = [System.Collections.Generic.List[object]]::new()
$stateRows.Add((Get-RepoState ''))
foreach ($sub in @(Get-SubmodulePaths)) { $stateRows.Add((Get-RepoState $sub)) }

$evidence = [ordered]@{}
$unresolvedAll = [System.Collections.Generic.List[string]]::new()
foreach ($s in $stateRows) {
    $key = if ([string]::IsNullOrWhiteSpace($s.Path)) { '.' } else { $s.Path }
    $evidence[$key] = [ordered]@{
        head       = $s.Head
        worktreeId = $s.WorktreeId
        changed    = @($s.Changed)
        unresolved = @($s.Unresolved)
    }
    foreach ($u in @($s.Unresolved)) { $unresolvedAll.Add(('{0}:{1}' -f $key, $u)) }
}

function Format-Short {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '(none)' }
    return $Value.Substring(0, [Math]::Min(12, $Value.Length))
}

# ---- 写账本（唯一写者；脏状态照写 —— 记录的是「这份内容已构建过」）----
if ($UpdateStamp) {
    if ($unresolvedAll.Count -gt 0) {
        Write-Gate ("判据不可得，未记录构建账本：{0}" -f (($unresolvedAll | Select-Object -First 3) -join '; ')) 'Yellow'
        exit 1
    }
    New-Item -ItemType Directory -Path $stampDir -Force | Out-Null
    $payload = [ordered]@{
        schema        = $stampSchema
        configuration = $Configuration
        builtAt       = (Get-Date).ToString('o')
        repoRoot      = $RepoRoot
        repos         = $evidence
    }
    $payload | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $stampFile -Encoding UTF8
    Write-Gate ("已记录构建账本（{0} 仓；脏状态照写）" -f $evidence.Keys.Count) 'DarkGray'
    exit 0
}

# ---- 判定：A 提交态 ∧ B 工作树态 ∧ C 产物态 ⇒ Skip；任一不符 ⇒ Build（fail-closed）----
$decision = 'Build'
$reason = ''
$missing = @(Test-KeyArtifacts)

if ($unresolvedAll.Count -gt 0) {
    $reason = 'evidence-unavailable'
}
elseif ($missing.Count -gt 0) {
    $reason = 'artifacts-missing'
}
elseif (-not (Test-Path -LiteralPath $stampFile)) {
    $reason = 'no-stamp'
}
else {
    $stamp = $null
    try { $stamp = Get-Content -LiteralPath $stampFile -Raw | ConvertFrom-Json }
    catch { $stamp = $null }

    if ($null -eq $stamp -or [string]$stamp.schema -ne $stampSchema) {
        $reason = 'stamp-incompatible'
    }
    elseif ([string]$stamp.configuration -ne $Configuration) {
        $reason = 'config-changed'
    }
    else {
        $diffs = [System.Collections.Generic.List[string]]::new()
        foreach ($key in @($evidence.Keys)) {
            $cur = $evidence[$key]
            $old = $null
            if ($null -ne $stamp.repos) {
                $prop = @($stamp.repos.PSObject.Properties | Where-Object { $_.Name -eq $key })
                if ($prop.Count -gt 0) { $old = $prop[0].Value }
            }
            if ($null -eq $old) { $diffs.Add(('repo-unknown:{0}' -f $key)); continue }
            if ([string]$old.head -ne [string]$cur.head) { $diffs.Add(('commit-changed:{0}' -f $key)); continue }
            if ([string]$old.worktreeId -ne [string]$cur.worktreeId) { $diffs.Add(('worktree-changed:{0}' -f $key)) }
        }
        if ($diffs.Count -eq 0) { $decision = 'Skip'; $reason = 'fresh' }
        else { $reason = ($diffs -join ',') }
    }
}

if ($Json) {
    $result = [ordered]@{
        decision         = $decision
        reasonCode       = $reason
        configuration    = $Configuration
        artifactsMissing = @($missing)
        repos            = $evidence
        stampFile        = $stampFile
    }
    Write-Output (($result | ConvertTo-Json -Depth 8 -Compress))
}

if ($decision -eq 'Skip') {
    Write-Gate ("decision=Skip reason=fresh（{0} 仓：提交态/工作树态/产物均一致）" -f $evidence.Keys.Count) 'Green'
    foreach ($key in @($evidence.Keys)) {
        $row = $evidence[$key]
        $wt = if ([string]::IsNullOrWhiteSpace([string]$row.worktreeId)) { '(clean)' } else { Format-Short ([string]$row.worktreeId) }
        Write-Host ("             {0,-10} head={1} worktree={2} changed={3}" -f $key, (Format-Short ([string]$row.head)), $wt, @($row.changed).Count) -ForegroundColor DarkGray
    }
    exit 10
}

Write-Gate ("decision=Build reason={0}" -f $reason) 'Yellow'
if ($missing.Count -gt 0) {
    Write-Gate ("关键产物缺失 {0} 项：" -f $missing.Count) 'Yellow'
    foreach ($path in ($missing | Select-Object -First 8)) { Write-Host ("             {0}" -f $path) -ForegroundColor DarkGray }
}
foreach ($key in @($evidence.Keys)) {
    $row = $evidence[$key]
    if (@($row.changed).Count -eq 0) { continue }
    Write-Gate ("{0}：{1} 项构建相关改动" -f $key, @($row.changed).Count) 'DarkGray'
    foreach ($p in (@($row.changed) | Select-Object -First 6)) { Write-Host ("             {0}" -f $p) -ForegroundColor DarkGray }
    if (@($row.changed).Count -gt 6) { Write-Host '             ...' -ForegroundColor DarkGray }
}
exit 0


