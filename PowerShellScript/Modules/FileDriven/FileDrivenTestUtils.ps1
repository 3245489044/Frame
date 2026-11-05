<#
.SYNOPSIS
    FileDriven 测试通用工具函数

.DESCRIPTION
    提供 FileDriven 测试脚本所需的通用功能：
    - 获取项目配置（路径、别名等）
    - 运行模式管理
    - 辅助输出函数

    用法:
        . "$PSScriptRoot\FileDrivenTestUtils.ps1"   # 点引入加载所有函数
        $config = Get-FileDrivenConfig               # 获取配置
        $runMode = Get-RunMode                       # 获取运行模式
        Write-Header "标题"                          # 输出标题
        Write-Info "标签" "值"                       # 输出信息

.NOTES
    此脚本设计为通过点引入（dot-source）方式加载，不直接执行。
#>

# ============================================
#  配置获取
# ============================================

function Get-FileDrivenConfig {
    <#
    .SYNOPSIS
        获取 FileDriven 项目配置信息

    .DESCRIPTION
        计算项目根目录、FileDriven 目录、脚本目录等路径，
        并加载路径别名。
        ★ 重构：委托给 ProjectPaths 模块统一处理路径检测

    .PARAMETER ScriptPath
        调用方脚本的完整路径（通常使用 $MyInvocation.MyCommand.Path）
        ★ 不再需要，保留仅为兼容旧调用方

    .PARAMETER ProjectRootUpLevels
        ★ 不再需要，ProjectPaths 模块自动检测项目根目录

    .OUTPUTS
        Hashtable，包含以下键：
        - ProjectRoot      : 项目根目录
        - FileDrivenDir    : FileDriven 目录
        - ScriptsDir       : 测试脚本目录
        - Aliases          : 路径别名哈希表
        - BatToolConfigPath: BatToolConfig.bat 路径
    #>
    param(
        [Parameter(Mandatory=$false)]
        [string]$ScriptPath = "",

        [Parameter(Mandatory=$false)]
        [int]$ProjectRootUpLevels = 3
    )

    # ★ 使用 ProjectPaths 模块自动检测项目根目录
    $projectRoot = Get-ProjectRoot

    # ★ 使用统一路径函数获取 FileDriven 目录（自动拼接 CsharpProjectRoot）
    $fileDrivenDir = Get-FileDrivenDir -ProjectRoot $projectRoot

    # 测试脚本目录：FileDriven 的 DrivenConfig 资源已整体迁入 Information 资源仓，
    # 脚本现位于 Main\Projects\Information\DrivenConfig\Scripts
    $scriptsDir = Join-Path $projectRoot "Projects\Information\DrivenConfig\Scripts"

    # BatToolConfig 路径：PowerShellScript 现位于 Main\Frame\PowerShellScript
    $batToolConfigPath = Join-Path (Get-ScriptModulesDir) "Core\BatToolConfig.psm1"

    # ★ 使用 ProjectPaths 模块获取路径别名
    $aliases = Get-PathAliases -ProjectRoot $projectRoot

    return @{
        ProjectRoot       = $projectRoot
        FileDrivenDir     = $fileDrivenDir
        ScriptsDir        = $scriptsDir
        Aliases           = $aliases
        BatToolConfigPath = $batToolConfigPath
    }
}

# ============================================
#  运行模式管理
# ============================================

function Test-PipeMode {
    <#
    .SYNOPSIS
        检测是否处于管道输入模式（即时指令经管道投喂）

    .DESCRIPTION
        管道模式有两个判定信号：
        1. 环境变量 FILEDRIVEN_INSTANT_BODY 非空（交互启动.bat 经管道捕获 JSON 请求体时设置）
        2. 标准输入不是控制台（stdin 被管道重定向，无 TTY 可读）
        管道模式下禁止任何 Read-Host 等待按键（无 TTY 会无限阻塞），
        所有手动暂停/交互循环必须跳过。

    .OUTPUTS
        bool: true=管道模式（禁止 Read-Host）
    #>
    if (-not [string]::IsNullOrWhiteSpace($env:FILEDRIVEN_INSTANT_BODY)) {
        return $true
    }
    try {
        # IsInputRedirected=true 表示 stdin 被重定向（管道输入），判定为管道模式
        return [Console]::IsInputRedirected
    } catch {
        # [Console]::IsInputRedirected 在某些宿主（如 ISE）不可用
        return $false
    }
}

function Get-RunMode {
    <#
    .SYNOPSIS
        获取当前运行模式

    .DESCRIPTION
        从环境变量 FILEDRIVEN_RUN_MODE 获取运行模式，
        未设置时默认为 auto。
        ★ 管道模式（Test-PipeMode=true）强制返回 auto——
          管道输入无 TTY 可读，manual 模式的 Read-Host 将无限阻塞。

    .PARAMETER Default
        默认模式，默认为 "auto"

    .OUTPUTS
        string: "auto" 或 "manual"
    #>
    param(
        [Parameter(Mandatory=$false)]
        [ValidateSet("auto", "manual")]
        [string]$Default = "auto"
    )

    if (Test-PipeMode) {
        return "auto"
    }

    $mode = $env:FILEDRIVEN_RUN_MODE
    if ([string]::IsNullOrWhiteSpace($mode)) {
        $mode = $Default
    }
    return $mode
}

function Set-RunMode {
    <#
    .SYNOPSIS
        设置运行模式到环境变量

    .PARAMETER Mode
        运行模式: "auto" 或 "manual"
    #>
    param(
        [Parameter(Mandatory=$true)]
        [ValidateSet("auto", "manual")]
        [string]$Mode
    )

    $env:FILEDRIVEN_RUN_MODE = $Mode
}

function Switch-RunMode {
    <#
    .SYNOPSIS
        切换运行模式（auto ↔ manual）

    .OUTPUTS
        string: 切换后的模式
    #>
    $current = Get-RunMode
    if ($current -eq "auto") {
        Set-RunMode "manual"
        Write-Host "`n  [信息] 已切换到手动模式（运行结束后将暂停等待按键）"
    } else {
        Set-RunMode "auto"
        Write-Host "`n  [信息] 已切换到自动模式（运行结束后自动关闭）"
    }
    return (Get-RunMode)
}

# ============================================
#  辅助输出函数
# ============================================

function Write-Header {
    <#
    .SYNOPSIS
        输出带等号线框的标题

    .PARAMETER Text
        标题文本
    #>
    param([string]$Text)
    $line = "=" * 44
    Write-Host ""
    Write-Host $line
    Write-Host "  $Text"
    Write-Host $line
}

function Write-Info {
    <#
    .SYNOPSIS
        输出标签-值格式的信息

    .PARAMETER Label
        标签名

    .PARAMETER Value
        值
    #>
    param([string]$Label, [string]$Value)
    Write-Host ("  {0,-18}: {1}" -f $Label, $Value)
}

# ============================================
#  路径别名注入
# ============================================

function Set-AliasEnvironment {
    <#
    .SYNOPSIS
        将路径别名注入到环境变量

    .PARAMETER Aliases
        别名哈希表（来自 Get-FileDrivenConfig 的 Aliases）

    .PARAMETER Overrides
        要覆盖的别名键值对哈希表（可选）
        例如: @{ "Output" = "D:\some\path" }
    #>
    param(
        [Parameter(Mandatory=$true)]
        [hashtable]$Aliases,

        [Parameter(Mandatory=$false)]
        [hashtable]$Overrides = @{}
    )

    foreach ($key in $Aliases.Keys) {
        $envKey = "FILEDRIVEN_ALIAS_$key"
        $value = $Aliases[$key]
        if ($Overrides.ContainsKey($key)) {
            $value = $Overrides[$key]
        }
        Set-Item -Path "env:$envKey" -Value $value
    }
}

# ============================================
#  脚本列表获取
# ============================================

function Get-ScriptFiles {
    <#
    .SYNOPSIS
        获取指定目录中的脚本文件列表（支持递归扫描子目录）

    .PARAMETER ScriptsDir
        脚本目录路径

    .PARAMETER FilePattern
        文件扩展名过滤，用 | 分隔，默认为 ".json"（.txt 属历史文本命令脚本，须显式指定才纳入）

    .PARAMETER Recurse
        是否递归扫描子目录（默认为 $false）

    .OUTPUTS
        System.IO.FileInfo[] 文件对象数组
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$ScriptsDir,

        [Parameter(Mandatory=$false)]
        [string]$FilePattern = ".json",

        [Parameter(Mandatory=$false)]
        [switch]$Recurse
    )

    $extensions = $FilePattern -split '\|'
    
    if ($Recurse) {
        $scripts = Get-ChildItem -Path $ScriptsDir -File -Recurse |
            Where-Object { $_.Extension -in $extensions } |
            Sort-Object FullName
    } else {
        $scripts = Get-ChildItem -Path $ScriptsDir -File |
            Where-Object { $_.Extension -in $extensions } |
            Sort-Object Name
    }

    return $scripts
}

# ============================================
#  启动器查找
# ============================================

function Find-LauncherDir {
    <#
    .SYNOPSIS
        查找 FileDriven.Launcher.exe 的输出目录

    .DESCRIPTION
        委托给 FindLauncherDir.ps1 脚本查找启动器目录。
        优先 Debug，其次 Release，未找到时自动构建。

    .PARAMETER FindScriptPath
        FindLauncherDir.ps1 的完整路径

    .PARAMETER FileDrivenDir
        FileDriven 项目根目录

    .OUTPUTS
        string: 启动器目录路径，未找到时返回 $null
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$FindScriptPath,

        [Parameter(Mandatory=$true)]
        [string]$FileDrivenDir
    )

    # FindLauncherDir.ps1 使用 Write-Output 输出路径（stdout），
    # 使用 Write-Warning/Write-Error 输出日志（stderr），
    # 因此分别捕获 stdout 和 stderr
    $stdout = & powershell -NoProfile -File $FindScriptPath -FileDrivenDir $FileDrivenDir 2>&1 | Where-Object { $_ -is [string] }
    $pathLine = $stdout | Select-Object -Last 1

    return $pathLine
}

# ============================================
#  脚本目录分层浏览（文件夹 → 用例）
#  编号规则：同一层级内先列子文件夹、再列脚本文件；
#            两组均按名称 OrdinalIgnoreCase 排序，编号稳定可预测，
#            不随文件系统枚举顺序抖动。
# ============================================

function Sort-CatalogName {
    <#
    .SYNOPSIS
        按 OrdinalIgnoreCase 排序名称数组（稳定、与区域设置无关）
    #>
    param([string[]]$Names)

    if ($null -eq $Names -or $Names.Count -eq 0) { return @() }
    $sorted = [string[]]($Names.Clone())
    [System.Array]::Sort($sorted, [System.StringComparer]::OrdinalIgnoreCase)
    return ,$sorted
}

function Get-ScriptFileMeta {
    <#
    .SYNOPSIS
        读取并缓存脚本文件元信息（是否测试脚本、description）

    .DESCRIPTION
        判定依据（与 pipeline/04-json-script.md §3.1 的脚本结构一致）：
        JSON 顶层同时存在非空 "script" 字符串和 "commands" 数组 → 测试脚本。
        由此排除同名同扩展名但非测试脚本的文件（如 cline_write_smoke\package.json）。
        缓存以 完整路径 + LastWriteTimeUtc 为键，文件变更后自动失效；
        全局缓存使同一次菜单会话内多次渲染/统计不重复读盘。
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$Path
    )

    if ($null -eq $global:FileDrivenScriptMetaCache) { $global:FileDrivenScriptMetaCache = @{} }

    $item = Get-Item -LiteralPath $Path -ErrorAction SilentlyContinue
    if ($null -eq $item) { return $null }

    $stamp = $item.LastWriteTimeUtc.Ticks
    $cached = $global:FileDrivenScriptMetaCache[$Path]
    if ($null -ne $cached -and $cached.Stamp -eq $stamp) { return $cached }

    $meta = [PSCustomObject]@{
        Stamp       = $stamp
        IsJson      = ($item.Extension -ieq '.json')
        IsScript    = $false
        Description = ''
    }

    if ($meta.IsJson) {
        try {
            $json = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            $hasScript = ($null -ne $json.PSObject.Properties['script']) -and
                         (-not [string]::IsNullOrWhiteSpace([string]$json.script))
            $hasCommands = ($null -ne $json.PSObject.Properties['commands']) -and
                           ($json.commands -is [System.Array])
            $meta.IsScript = ($hasScript -and $hasCommands)
            if ($null -ne $json.PSObject.Properties['description']) {
                $meta.Description = [string]$json.description
            }
        } catch {
            $meta.IsScript = $false
        }
    }

    $global:FileDrivenScriptMetaCache[$Path] = $meta
    return $meta
}

function Test-ScriptCatalogFile {
    <#
    .SYNOPSIS
        判断文件是否应纳入脚本清单（扩展名匹配 + JSON 须为测试脚本结构）
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$Path,

        [Parameter(Mandatory=$true)]
        [string[]]$Extensions
    )

    $ext = [System.IO.Path]::GetExtension($Path)
    if (-not ($Extensions -contains $ext)) { return $false }

    if ($ext -ieq '.json') {
        $meta = Get-ScriptFileMeta -Path $Path
        return ($null -ne $meta -and $meta.IsScript)
    }
    return $true
}

function Get-ScriptCatalogChildren {
    <#
    .SYNOPSIS
        获取某一层级下的目录项（子文件夹 + 脚本文件），按固定规则排序编号

    .OUTPUTS
        PSCustomObject[]：Kind(Folder/File)、Name、RelativePath（相对 ScriptsDir）
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$ScriptsDir,

        [Parameter(Mandatory=$false)]
        [string]$RelativeDir = "",

        [Parameter(Mandatory=$false)]
        [string]$FilePattern = ".json"
    )

    $fullDir = if ([string]::IsNullOrWhiteSpace($RelativeDir)) { $ScriptsDir } else { Join-Path $ScriptsDir $RelativeDir }
    if (-not (Test-Path -LiteralPath $fullDir)) { return @() }

    $extensions = $FilePattern -split '\|'
    $entries = New-Object System.Collections.Generic.List[object]

    $folderNames = @([System.IO.Directory]::GetDirectories($fullDir) | ForEach-Object { Split-Path -Leaf $_ })
    foreach ($name in (Sort-CatalogName -Names $folderNames)) {
        $rel = if ([string]::IsNullOrWhiteSpace($RelativeDir)) { $name } else { $RelativeDir + '\' + $name }
        $entries.Add([PSCustomObject]@{
            Kind         = 'Folder'
            Name         = $name
            RelativePath = $rel
        })
    }

    $fileNames = @([System.IO.Directory]::GetFiles($fullDir) |
        Where-Object { Test-ScriptCatalogFile -Path $_ -Extensions $extensions } |
        ForEach-Object { Split-Path -Leaf $_ })
    foreach ($name in (Sort-CatalogName -Names $fileNames)) {
        $rel = if ([string]::IsNullOrWhiteSpace($RelativeDir)) { $name } else { $RelativeDir + '\' + $name }
        $entries.Add([PSCustomObject]@{
            Kind         = 'File'
            Name         = $name
            RelativePath = $rel
        })
    }

    return $entries.ToArray()
}

function Get-ScriptDescription {
    <#
    .SYNOPSIS
        读取脚本 JSON 的 description 字段（失败时返回空串）
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$ScriptsDir,

        [Parameter(Mandatory=$true)]
        [string]$RelativePath,

        [Parameter(Mandatory=$false)]
        [int]$MaxLength = 60
    )

    $full = Join-Path $ScriptsDir $RelativePath
    $meta = Get-ScriptFileMeta -Path $full
    if ($null -eq $meta -or [string]::IsNullOrWhiteSpace($meta.Description)) { return '' }

    $desc = $meta.Description
    $one = ($desc -replace '\s+', ' ').Trim()
    if ($one.Length -gt $MaxLength) { $one = $one.Substring(0, $MaxLength) + '...' }
    return $one
}

function Resolve-ScriptCatalogSpec {
    <#
    .SYNOPSIS
        解析路径式编号（如 2、2-3、2-*、run 2、run:2-*）为目录项

    .OUTPUTS
        PSCustomObject：Kind(File/Folder/Invalid)、RelativePath、Batch、Message
        路径始终从 ScriptsDir（根目录）起算。
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$ScriptsDir,

        [Parameter(Mandatory=$true)]
        [string]$Spec,

        [Parameter(Mandatory=$false)]
        [string]$FilePattern = ".json"
    )

    $result = [PSCustomObject]@{
        Kind         = 'Invalid'
        RelativePath = ''
        Batch        = $false
        Message      = ''
    }

    $s = $Spec.Trim()
    if ($s -match '(?i)^run(\s*:\s*|\s+)') {
        $s = $s -replace '(?i)^run(\s*:\s*|\s+)', ''
    } elseif ($s -match '(?i)^run$') {
        $s = ''
    }

    $batch = $false
    if ($s.EndsWith('*')) {
        $batch = $true
        $s = $s.Substring(0, $s.Length - 1).TrimEnd([char]'-')
    }
    $s = $s.Trim()

    if ($s -eq '') {
        if ($batch) {
            $result.Kind = 'Folder'
            $result.Batch = $true
            return $result
        }
        $result.Message = '请指定文件夹编号或路径，例如 2 或 2-3'
        return $result
    }

    if ($s -notmatch '^\d+(-\d+)*$') {
        $result.Message = "无法识别的路径：$Spec（应为 2、2-3、2-* 或 run 2）"
        return $result
    }

    $segments = $s -split '-'
    $currentRel = ''
    $kind = 'Folder'

    for ($i = 0; $i -lt $segments.Count; $i++) {
        $children = @(Get-ScriptCatalogChildren -ScriptsDir $ScriptsDir -RelativeDir $currentRel -FilePattern $FilePattern)
        $num = [int]$segments[$i]
        if ($num -lt 1 -or $num -gt $children.Count) {
            $range = if ($children.Count -eq 0) { '该目录为空' } else { "1-$($children.Count)" }
            $result.Message = "路径 '$s' 的第 $($i + 1) 级编号 '$($segments[$i])' 超出有效范围（$range）"
            return $result
        }

        $entry = $children[$num - 1]
        $currentRel = $entry.RelativePath
        $kind = $entry.Kind

        if ($kind -eq 'File' -and $i -lt ($segments.Count - 1)) {
            $result.Message = "路径 '$s' 无效：'$($entry.Name)' 是文件，不能继续向下定位"
            return $result
        }
    }

    $result.Kind = $kind
    $result.RelativePath = $currentRel
    $result.Batch = $batch
    return $result
}

function Get-ScriptBatchFiles {
    <#
    .SYNOPSIS
        递归枚举某个文件夹下的全部脚本（按相对路径 OrdinalIgnoreCase 排序）

    .OUTPUTS
        PSCustomObject[]：RelativePath、Name
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$ScriptsDir,

        [Parameter(Mandatory=$false)]
        [string]$RelativeDir = "",

        [Parameter(Mandatory=$false)]
        [string]$FilePattern = ".json"
    )

    $fullDir = if ([string]::IsNullOrWhiteSpace($RelativeDir)) { $ScriptsDir } else { Join-Path $ScriptsDir $RelativeDir }
    if (-not (Test-Path -LiteralPath $fullDir)) { return @() }

    $extensions = $FilePattern -split '\|'
    $map = @{}
    foreach ($f in @(Get-ChildItem -LiteralPath $fullDir -File -Recurse)) {
        if (Test-ScriptCatalogFile -Path $f.FullName -Extensions $extensions) {
            $map[$f.FullName.Substring($ScriptsDir.Length + 1)] = $f
        }
    }

    $rels = Sort-CatalogName -Names ([string[]]@($map.Keys))
    $out = foreach ($rel in $rels) {
        [PSCustomObject]@{
            RelativePath = $rel
            Name         = Split-Path -Leaf $rel
        }
    }
    return @($out)
}

function Show-ScriptTree {
    <#
    .SYNOPSIS
        按文件夹分组显示脚本树（一级文件夹 → 其下用例）

    .PARAMETER Recurse
        是否递归展开子文件夹（默认只显示当前层）
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$ScriptsDir,

        [Parameter(Mandatory=$false)]
        [string]$FilePattern = ".json",

        [Parameter(Mandatory=$false)]
        [string]$RelativeDir = "",

        [Parameter(Mandatory=$false)]
        [switch]$Recurse,

        [Parameter(Mandatory=$false)]
        [int]$Depth = 0,

        [Parameter(Mandatory=$false)]
        [int]$MaxDepth = 6
    )

    $children = @(Get-ScriptCatalogChildren -ScriptsDir $ScriptsDir -RelativeDir $RelativeDir -FilePattern $FilePattern)
    if ($children.Count -eq 0) {
        if ($Depth -eq 0) { Write-Host "  （该目录下没有脚本）" }
        return
    }

    $indent = '  ' * ($Depth + 1)
    $idx = 0
    foreach ($entry in $children) {
        $idx++
        if ($entry.Kind -eq 'Folder') {
            $count = @(Get-ScriptBatchFiles -ScriptsDir $ScriptsDir -RelativeDir $entry.RelativePath -FilePattern $FilePattern).Count
            Write-Host ("{0}[{1,2}] {2}\  ({3} 个用例)" -f $indent, $idx, $entry.Name, $count)
            if ($Recurse -and $Depth -lt $MaxDepth) {
                Show-ScriptTree -ScriptsDir $ScriptsDir -FilePattern $FilePattern `
                    -RelativeDir $entry.RelativePath -Recurse -Depth ($Depth + 1) -MaxDepth $MaxDepth
            }
        } else {
            $desc = Get-ScriptDescription -ScriptsDir $ScriptsDir -RelativePath $entry.RelativePath
            if ($desc) {
                Write-Host ("{0}[{1,2}] {2}  — {3}" -f $indent, $idx, $entry.Name, $desc)
            } else {
                Write-Host ("{0}[{1,2}] {2}" -f $indent, $idx, $entry.Name)
            }
        }
    }
}
