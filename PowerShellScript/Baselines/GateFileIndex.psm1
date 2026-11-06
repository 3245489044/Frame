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
        $files = Get-GateFiles -Root $scanDir -Extension cs
        # -Exclude 缺省 = GateRuntime.Get-GateSourceExcludePattern（obj/bin/BuildOutput/_deprecated 单源）
        # 需要包含构建产物时显式 -IncludeBuildOutput（或自行传 -Exclude）。

.NOTES
    只用于**源文件**（ts/cs/ps1/...）；运行期数据（*.db*）、被 .gitignore 排除但仍需审计的生成物
    必须继续使用定向 `Get-ChildItem`，并登记到 GateFileIndex.psd1 的 Allowlist。
#>

# 排除集单源：默认 -Exclude 取自 GateRuntime（不复制第二个正则常量）。
# 注意：嵌套导入**不加 -Force**（-Force 会把调用方会话里已加载的同名模块卸载）。
Import-Module (Join-Path $PSScriptRoot 'GateRuntime.psm1') -DisableNameChecking

$script:Cache = @{}
$script:Stats = @{
    Source    = 'none'
    Worktrees = @()
    Files     = 0
    Ms        = 0
}

# 索引面：源文件 + 工程声明 + 声明/数据资产。
# 刻意不含 .md（文档，非判据载体）；json / ushar 必须纳入：
#   · ushar = 声明资产（`[Gather]/[Edge]/[Table]` 等），Check-DeclaredFlowConsumers 曾因索引缺此扩展而恒红；
#   · json  = 数据资产（`*_log_messages.json`、`*.contracts.json`、`contracts.index.json`），Check-LogMessages / Check-RuntimeMirror / Check-ContractRegistry 依赖；
#   · bat   = 构建/发布脚本（Runtime 镜像生成链），Check-RuntimeMirror 需按脚本内容对账。
# 量级（2026-11-06 实测）：json 652 / ushar 330（四个工作树合计，已被 gitignore 剔除），对索引内存与枚举耗时可忽略。
$script:IndexExtensions = @(
    'ts', 'tsx', 'mts', 'cts',
    'js', 'mjs', 'cjs',
    'cs', 'ps1', 'psm1', 'psd1',
    'specs', 'csproj', 'props', 'targets', 'sln', 'slnx',
    'json', 'ushar', 'bat'
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
                # 与 Get-GateSourceExcludePattern 同源（禁第二份排除清单；含 Runtime\Payload 部署镜像）。
                if ($abs -match (Get-GateSourceExcludePattern)) { continue }
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
        绝对路径排除正则（反斜杠分隔）。缺省 = Get-GateSourceExcludePattern（D5 单源）；
        显式传入时以调用方为准。
    .PARAMETER IncludeBuildOutput
        关闭缺省排除（含 obj/bin/BuildOutput/_deprecated）。显式 -Exclude 优先。
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [string[]]$Extension = @(),
        [string]$Exclude = '',
        [switch]$IncludeBuildOutput,
        [switch]$Refresh,
        [switch]$NoCache
    )

    if (-not (Test-Path -LiteralPath $Root)) { return @() }
    if ([string]::IsNullOrWhiteSpace($Exclude) -and -not $IncludeBuildOutput) {
        $Exclude = Get-GateSourceExcludePattern
    }

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

    # fail-closed：请求的扩展名不在索引面 ⇒ 回退递归枚举（否则索引过滤会静默返回 0）。
    if ($null -ne $index -and $extSet.Count -gt 0) {
        $indexed = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($e in $script:IndexExtensions) { [void]$indexed.Add($e) }
        $missing = @($extSet | Where-Object { -not $indexed.Contains($_) })
        if ($missing.Count -gt 0) {
            Write-Warning ("[GateFileIndex] 索引未覆盖扩展名（{0}），本调用回退递归枚举：{1}" -f ($missing -join ','), $rootFull)
            $index = $null
        }
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

function Find-GatePattern {
    <#
    .SYNOPSIS
        源码文本命中（文件 + 行号 + 行内容），统一「按行扫描 + 注释行排除」。
    .DESCRIPTION
        动机：门禁脚本各自用 Get-ChildItem/Select-String 枚举，注释行排除写法不一
        （`//` / `*` / `/*` / `#` 四套），同一"代码事实"口径反复漂移。
        判据载体 = 行文本（结构事实），不做任何语义解释。
    .PARAMETER Files
        待扫描文件绝对路径（通常由 Get-GateFiles 产出）。
    .PARAMETER Pattern
        行级正则（[regex]::IsMatch）。
    .PARAMETER SkipComments
        跳过整行注释（`//`、`*`、`/*`、`#` 开头）；行尾注释不剥离（保持行文本原样）。
    .PARAMETER Exclude
        绝对路径排除正则（反斜杠分隔）。缺省 = Get-GateSourceExcludePattern（D5 单源，
        含 obj/bin/BuildOutput/_deprecated/测试产物/`Runtime\Payload` 镜像）；显式传入时以调用方为准。
    .PARAMETER IncludeBuildOutput
        关闭缺省排除（要让镜像/构建产物进入统计时才用）。
    #>
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Files,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [switch]$SkipComments,
        [string]$Exclude = '',
        [switch]$IncludeBuildOutput
    )

    if ([string]::IsNullOrWhiteSpace($Exclude) -and -not $IncludeBuildOutput) {
        $Exclude = Get-GateSourceExcludePattern
    }

    $hits = [System.Collections.Generic.List[object]]::new()
    foreach ($f in @($Files)) {
        if ([string]::IsNullOrWhiteSpace($f) -or -not (Test-Path -LiteralPath $f)) { continue }
        if (-not [string]::IsNullOrWhiteSpace($Exclude) -and $f -match $Exclude) { continue }
        $lineNo = 0
        foreach ($line in [System.IO.File]::ReadAllLines($f)) {
            $lineNo++
            $text = $line.Trim()
            if ($SkipComments -and ($text.StartsWith('//') -or $text.StartsWith('*') -or $text.StartsWith('/*') -or $text.StartsWith('#'))) { continue }
            if ([regex]::IsMatch($line, $Pattern)) {
                $hits.Add([pscustomobject]@{ File = $f; Line = $lineNo; Text = $text })
            }
        }
    }
    return $hits
}

function Measure-GatePatternCount {
    <#
    .SYNOPSIS
        命中计数（文件数 / 行数）——复用 Find-GatePattern，不做语义解释。
    .DESCRIPTION
        动机：6 份域内 Get-Count 直接 `[regex]::Matches` 全文件文本，无法区分「同一行多处」
        与「多行命中」，也无法与 Find-GatePattern 的行级口径对齐。本函数统一返回两者。
    .PARAMETER Files
        待扫描文件绝对路径
    .PARAMETER Pattern
        行级正则
    .PARAMETER SkipComments
        跳过整行注释（透传 Find-GatePattern）
    .OUTPUTS
        { Count = 命中文件数; Hits = 命中行数; Files = 命中文件清单 }
    #>
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Files,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [switch]$SkipComments
    )

    $hits = @(Find-GatePattern -Files $Files -Pattern $Pattern -SkipComments:$SkipComments)
    $hitFiles = @($hits | ForEach-Object { $_.File } | Select-Object -Unique)
    return [pscustomobject]@{
        Count = $hitFiles.Count
        Hits  = $hits.Count
        Files = $hitFiles
    }
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

New-Alias -Name Get-GatePatternCount -Value Measure-GatePatternCount

Export-ModuleMember -Function @(
    'Resolve-GateWorktree',
    'Get-GateFiles',
    'Find-GatePattern',
    'Measure-GatePatternCount',
    'Get-GateFileStats',
    'Clear-GateFileIndexCache',
    'Export-GateFileIndexCache'
) -Alias @('Get-GatePatternCount')
