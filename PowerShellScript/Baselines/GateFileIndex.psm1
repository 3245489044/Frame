#requires -Version 5.1
<#
.SYNOPSIS
    GateFileIndex —— 门禁源文件索引（git ls-files 单源；按子模块工作树分别索引）。

.DESCRIPTION
    背景：Check-*.ps1 大量使用 `Get-ChildItem -Path <root> -Recurse` 枚举源文件，
    在 Windows + 杀软环境下会重复遍历 node_modules/BuildOutput，导致门禁慢且抖动。

    本模块把「源文件枚举」收敛为单一实现：
      · 以 `git ls-files -z --cached --others --exclude-standard` 取当前工作树索引；
      · **按工作树分别索引**（Frame / Projects 是 Main 的子模块，Main 根索引不含其内部文件）；
      · 进程内缓存 + 父进程共享缓存（Invoke-GateCheck 预生成 GATE_FILE_INDEX_CACHE）；
      · git 不可用/非仓库时回退 `Get-ChildItem -Recurse`（WARN 可见化，禁静默假绿）。

    使用（门禁脚本头部）：
        Import-Module (Join-Path $PSScriptRoot '..\..\..\..\Baselines\GateFileIndex.psm1') -Force
        $files = Get-GateFiles -Root $scanDir -Extension cs -Exclude '\\(obj|bin|BuildOutput)\\'

.NOTES
    只用于**源文件**（ts/cs/ps1/...）；运行期数据（*.db*）、被 .gitignore 排除但仍需审计的生成物
    必须继续使用定向 `Get-ChildItem`，并登记到 GateFileIndex.psd1 的 Allowlist。
#>

$script:Cache = @{}
$script:Stats = @{
    Source    = 'none'
    Worktrees = @()
    Files     = 0
    Ms        = 0
}

# 索引面：源文件 + 工程声明。刻意不含 .md/.json（多数门禁只扫源码；需要时显式加扩展名）。
$script:IndexExtensions = @(
    'ts', 'tsx', 'mts', 'cts',
    'js', 'mjs', 'cjs',
    'cs', 'ps1', 'psm1', 'psd1',
    'specs', 'csproj', 'props', 'targets', 'sln', 'slnx'
)

function Resolve-GateWorktree {
    <#
    .SYNOPSIS
        解析目录所属的 git 工作树根（子模块各自独立）。
    #>
    param([Parameter(Mandatory = $true)][string]$Root)

    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { return $null }
    if (-not (Test-Path -LiteralPath $Root)) { return $null }

    $out = & git -C $Root rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    $line = @($out | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Select-Object -First 1)
    if ($line.Count -eq 0) { return $null }
    return ([IO.Path]::GetFullPath([string]$line[0])).TrimEnd('\')
}

function Get-GateIndexCacheFile {
    $path = $env:GATE_FILE_INDEX_CACHE
    if ([string]::IsNullOrWhiteSpace($path)) { return $null }
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    return $path
}

function Read-SharedIndex {
    param([Parameter(Mandatory = $true)][string]$Worktree)

    $path = Get-GateIndexCacheFile
    if (-not $path) { return $null }
    try {
        $json = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($null -eq $json.worktrees) { return $null }
        $prop = @($json.worktrees.PSObject.Properties | Where-Object { $_.Name -ieq $Worktree } | Select-Object -First 1)
        if ($prop.Count -eq 0) { return $null }
        return @($prop[0].Value | ForEach-Object { [string]$_ })
    } catch {
        return $null
    }
}

function Get-GateIndex {
    <#
    .SYNOPSIS
        取（并缓存）单工作树的全量源文件索引（绝对路径）。
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Worktree,
        [switch]$Refresh,
        [switch]$NoCache
    )

    $key = $Worktree.ToLowerInvariant()
    if (-not $NoCache -and -not $Refresh -and $script:Cache.ContainsKey($key)) {
        return $script:Cache[$key]
    }

    if (-not $NoCache -and -not $Refresh) {
        $shared = Read-SharedIndex -Worktree $Worktree
        if ($null -ne $shared) {
            $script:Cache[$key] = $shared
            $script:Stats.Source = 'shared-cache'
            return $shared
        }
    }

    $patterns = @($script:IndexExtensions | ForEach-Object { "*.$_" })
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    # -z：避免空格/中文/特殊字符被行分割；core.quotepath=false：中文路径不转义。
    $raw = & git -C $Worktree -c core.quotepath=false ls-files -z --cached --others --exclude-standard -- @patterns 2>$null
    $rc = $LASTEXITCODE
    $sw.Stop()
    if ($rc -ne 0) { return $null }

    $text = ($raw | Out-String)
    $rel = @($text -split "`0" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $files = [System.Collections.Generic.List[string]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($r in $rel) {
        try {
            $abs = [IO.Path]::GetFullPath((Join-Path $Worktree $r)).Replace('/', '\')
            if ($seen.Add($abs)) { $files.Add($abs) }
        } catch {
            # 单条坏路径不阻断整体索引
        }
    }

    # 被 .gitignore 排除的**源文件**仍须审计（如 Credentials/*.cs）；仅排除构建产物目录。
    $ignoredRaw = & git -C $Worktree -c core.quotepath=false ls-files -z -o -i --exclude-standard -- @patterns 2>$null
    if ($LASTEXITCODE -eq 0) {
        $ignoredText = ($ignoredRaw | Out-String)
        foreach ($r in @($ignoredText -split "`0" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
            try {
                $abs = [IO.Path]::GetFullPath((Join-Path $Worktree $r)).Replace('/', '\')
                if ($abs -match '\\(obj|bin|BuildOutput|node_modules|_deprecated|backups)\\') { continue }
                if ($seen.Add($abs)) { $files.Add($abs) }
            } catch {
                # 单条坏路径不阻断整体索引
            }
        }
    }

    $script:Cache[$key] = $files.ToArray()
    $script:Stats.Source = 'git'
    $script:Stats.Worktrees = @($script:Stats.Worktrees + $Worktree | Select-Object -Unique)
    $script:Stats.Files += $files.Count
    $script:Stats.Ms += [int]$sw.Elapsed.TotalMilliseconds
    return $script:Cache[$key]
}

function Get-GateFiles {
    <#
    .SYNOPSIS
        按目录 + 扩展名枚举源文件（git 索引优先；回退递归枚举）。
    .PARAMETER Root
        扫描根（仓库内任意目录；函数只返回其下的文件）。
    .PARAMETER Extension
        扩展名（不带点；可多值）。空 = 不按扩展名过滤。
    .PARAMETER Exclude
        绝对路径排除正则（反斜杠分隔；如 '\\(obj|bin|node_modules)\\'）。
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [string[]]$Extension = @(),
        [string]$Exclude = '',
        [switch]$Refresh,
        [switch]$NoCache
    )

    if (-not (Test-Path -LiteralPath $Root)) { return @() }

    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $extSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($e in @($Extension)) {
        if ([string]::IsNullOrWhiteSpace($e)) { continue }
        [void]$extSet.Add($e.Trim().TrimStart('.').ToLowerInvariant())
    }

    $worktree = Resolve-GateWorktree -Root $rootFull
    $index = $null
    if ($worktree) {
        $index = Get-GateIndex -Worktree $worktree -Refresh:$Refresh -NoCache:$NoCache
    }

    if ($null -eq $index) {
        # 回退：显式 WARN（禁把「索引不可用」伪装成「0 文件」）
        Write-Warning "[GateFileIndex] git 索引不可用，回退递归枚举：$rootFull"
        $script:Stats.Source = 'filesystem'
        $filter = if ($extSet.Count -eq 1) { "*.$(@($extSet)[0])" } else { '*' }
        $files = @(Get-ChildItem -LiteralPath $rootFull -Recurse -File -Filter $filter -ErrorAction SilentlyContinue |
            Where-Object {
                ($extSet.Count -eq 0 -or $extSet.Contains($_.Extension.TrimStart('.').ToLowerInvariant())) -and
                ([string]::IsNullOrWhiteSpace($Exclude) -or $_.FullName -notmatch $Exclude)
            } |
            Select-Object -ExpandProperty FullName)
        $script:Stats.Files += $files.Count
        return $files
    }

    $prefix = $rootFull + '\'
    $result = [System.Collections.Generic.List[string]]::new()
    foreach ($f in $index) {
        if (-not $f.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) { continue }
        if ($extSet.Count -gt 0) {
            $ext = [IO.Path]::GetExtension($f).TrimStart('.').ToLowerInvariant()
            if (-not $extSet.Contains($ext)) { continue }
        }
        if (-not [string]::IsNullOrWhiteSpace($Exclude) -and $f -match $Exclude) { continue }
        if (-not (Test-Path -LiteralPath $f)) { continue }
        $result.Add($f)
    }
    return $result.ToArray()
}

function Get-GateFileStats {
    <#
    .SYNOPSIS
        本进程索引统计（供 -ShowDetail：扫描文件数 / 耗时 / 来源）。
    #>
    return [pscustomobject]@{
        Source    = $script:Stats.Source
        Worktrees = @($script:Stats.Worktrees)
        Files     = [int]$script:Stats.Files
        Ms        = [int]$script:Stats.Ms
    }
}

function Clear-GateFileIndexCache {
    $script:Cache.Clear()
    $script:Stats = @{ Source = 'none'; Worktrees = @(); Files = 0; Ms = 0 }
}

function Export-GateFileIndexCache {
    <#
    .SYNOPSIS
        父进程预生成共享索引（Invoke-GateCheck 调用；一次会话内只读一次 git）。
    #>
    param(
        [Parameter(Mandatory = $true)][string[]]$Worktree,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $map = [ordered]@{}
    foreach ($w in @($Worktree | Select-Object -Unique)) {
        if ([string]::IsNullOrWhiteSpace($w)) { continue }
        if (-not (Test-Path -LiteralPath $w)) { continue }
        $files = Get-GateIndex -Worktree $w -Refresh
        if ($null -ne $files) { $map[$w] = @($files) }
    }
    $payload = [ordered]@{ schema = 'gate-file-index-v1'; worktrees = $map }
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $payload | ConvertTo-Json -Depth 4 -Compress | Set-Content -LiteralPath $Path -Encoding UTF8
}

Export-ModuleMember -Function @(
    'Resolve-GateWorktree',
    'Get-GateFiles',
    'Get-GateFileStats',
    'Clear-GateFileIndexCache',
    'Export-GateFileIndexCache'
)
