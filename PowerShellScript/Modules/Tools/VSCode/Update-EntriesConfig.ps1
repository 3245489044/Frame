<#
.SYNOPSIS
    VS Code 启动项配置管理工具 — 通用 entries.config.json 管理
.DESCRIPTION
    通用化 VS Code 启动项（entries）配置管理工具，支持：
    - 列出所有启动项
    - 添加/更新启动项
    - 删除启动项
    - 批量更新工作目录
    - 同步启动项配置

    替代原 register_entry.ps1 和 sync_vscode_entries.ps1（已移至 Legacy/）

.PARAMETER EntriesFile
    entries.config.json 文件路径（默认自动检测）
.PARAMETER Action
    操作类型：list / add / remove / update / sync-workdir
.PARAMETER EntryName
    启动项名称（add/remove/update 时需要）
.PARAMETER EntryTarget
    启动项目标路径（add/update 时需要）
.PARAMETER WorkDir
    工作目录（add/update 时需要）
.PARAMETER OldWorkDir
    旧工作目录（sync-workdir 时需要）
.PARAMETER NewWorkDir
    新工作目录（sync-workdir 时需要）
.PARAMETER DryRun
    仅预览变更，不实际写入文件

.EXAMPLE
    # 列出所有启动项
    .\Update-EntriesConfig.ps1 -Action list

.EXAMPLE
    # 添加启动项
    .\Update-EntriesConfig.ps1 -Action add -EntryName "MyProject" `
        -EntryTarget "D:\Project\MyProject" -WorkDir "D:\Project\MyProject"

.EXAMPLE
    # 删除启动项
    .\Update-EntriesConfig.ps1 -Action remove -EntryName "MyProject"

.EXAMPLE
    # 批量更新工作目录
    .\Update-EntriesConfig.ps1 -Action sync-workdir `
        -OldWorkDir "D:\Old\Path" -NewWorkDir "D:\New\Path" -DryRun
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$EntriesFile = "",

    [Parameter(Mandatory = $true)]
    [ValidateSet("list", "add", "remove", "update", "sync-workdir")]
    [string]$Action,

    [Parameter()]
    [string]$EntryName = "",

    [Parameter()]
    [string]$EntryTarget = "",

    [Parameter()]
    [string]$WorkDir = "",

    [Parameter()]
    [string]$OldWorkDir = "",

    [Parameter()]
    [string]$NewWorkDir = "",

    [Parameter()]
    [switch]$DryRun
)

# ============================================================
# 加载公共模块
# ============================================================
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$modulesPath = Join-Path $scriptPath "modules"
Import-Module (Join-Path $modulesPath "Write-Format.psm1") -Force -ErrorAction SilentlyContinue

# ============================================================
# 后备函数
# ============================================================
if (-not (Get-Command Write-Banner -ErrorAction SilentlyContinue)) {
    function Write-Banner($Title, $Color = "Cyan") { Write-Host "`n=== $Title ===" -ForegroundColor $Color }
    function Write-Step($Title, $Color = "Cyan") { Write-Host "`n--- $Title ---" -ForegroundColor $Color }
    function Write-Info($Msg) { Write-Host "  $Msg" -ForegroundColor Gray }
    function Write-Success($Msg) { Write-Host "  ✅ $Msg" -ForegroundColor Green }
    function Write-Warning($Msg) { Write-Host "  ⚠️  $Msg" -ForegroundColor Yellow }
    function Write-Error($Msg) { Write-Host "  ❌ $Msg" -ForegroundColor Red }
    function Write-WhatIf($Msg) { Write-Host "  [WhatIf] $Msg" -ForegroundColor Magenta }
}

# ============================================================
# 核心函数
# ============================================================

function Find-EntriesFile {
    <#
    .SYNOPSIS
        自动检测 entries.config.json 文件
    #>
    $candidates = @()

    # 1. vscode-plugin-framework 配置目录
    $pluginDir = "$env:USERPROFILE\.vscode-plugin-framework"
    $candidates += Join-Path $pluginDir "entries.config.json"

    # 2. 当前项目目录
    $projectRoot = Resolve-Path (Join-Path $scriptPath "..\..")
    $candidates += Join-Path $projectRoot "entries.config.json"

    # 3. VS Code 用户数据目录
    $candidates += "$env:APPDATA\Code\User\entries.config.json"

    foreach ($path in $candidates) {
        if (Test-Path $path) { return $path }
    }

    return $null
}

function Read-EntriesFile {
    <#
    .SYNOPSIS
        读取 entries.config.json 文件
    #>
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return @{ entries = @() }
    }

    try {
        $content = Get-Content $Path -Raw -Encoding UTF8
        return $content | ConvertFrom-Json
    } catch {
        Write-Error "解析 entries 文件失败: $($_.Exception.Message)"
        return $null
    }
}

function Write-EntriesFile {
    <#
    .SYNOPSIS
        写入 entries.config.json 文件
    #>
    param([string]$Path, $Config)

    try {
        $jsonContent = $Config | ConvertTo-Json -Depth 5
        $utf8NoBom = New-Object System.Text.UTF8Encoding $false
        [System.IO.File]::WriteAllText($Path, $jsonContent, $utf8NoBom)
        return $true
    } catch {
        Write-Error "写入 entries 文件失败: $($_.Exception.Message)"
        return $false
    }
}

function Show-EntriesList {
    <#
    .SYNOPSIS
        显示所有启动项列表
    #>
    param($Config)

    $entries = $Config.entries
    if (-not $entries -or $entries.Count -eq 0) {
        Write-Info "没有配置任何启动项"
        return
    }

    Write-Host ""
    Write-Host ("  {0,-30} {1,-50} {2}" -f "名称", "目标路径", "工作目录") -ForegroundColor White
    Write-Host ("  $('─' * 100)")

    foreach ($entry in $entries) {
        $name = if ($entry.name) { $entry.name } else { "(未命名)" }
        $target = if ($entry.target) { $entry.target } else { "-" }
        $workDir = if ($entry.workDir) { $entry.workDir } else { "-" }
        Write-Host ("  {0,-30} {1,-50} {2}" -f $name, $target, $workDir) -ForegroundColor Gray
    }

    Write-Host ("  $('─' * 100)")
    Write-Success "共 $($entries.Count) 个启动项"
}

function Add-Entry {
    <#
    .SYNOPSIS
        添加或更新启动项
    #>
    param(
        $Config,
        [string]$Name,
        [string]$Target,
        [string]$WorkDir
    )

    if (-not $Name) {
        Write-Error "请指定启动项名称（-EntryName）"
        return $null
    }

    if (-not $Target) {
        Write-Error "请指定启动项目标路径（-EntryTarget）"
        return $null
    }

    if (-not $WorkDir) {
        $WorkDir = $Target
    }

    # 检查是否已存在
    $entries = $Config.entries
    $existing = $null
    if ($entries) {
        $existing = $entries | Where-Object { $_.name -eq $Name } | Select-Object -First 1
    }

    if ($existing) {
        Write-Info "更新启动项: $Name"
        Write-Info "  目标: $($existing.target) → $Target"
        Write-Info "  工作目录: $($existing.workDir) → $WorkDir"

        if (-not $DryRun) {
            $existing.target = $Target
            $existing.workDir = $WorkDir
        }
    } else {
        Write-Info "新增启动项: $Name"
        Write-Info "  目标: $Target"
        Write-Info "  工作目录: $WorkDir"

        if (-not $DryRun) {
            $newEntry = [PSCustomObject]@{
                name    = $Name
                target  = $Target
                workDir = $WorkDir
            }
            if (-not $entries) {
                $Config | Add-Member -NotePropertyName "entries" -NotePropertyValue @($newEntry) -Force
            } else {
                $Config.entries += $newEntry
            }
        }
    }

    return $Config
}

function Remove-Entry {
    <#
    .SYNOPSIS
        删除启动项
    #>
    param(
        $Config,
        [string]$Name
    )

    if (-not $Name) {
        Write-Error "请指定要删除的启动项名称（-EntryName）"
        return $null
    }

    $entries = $Config.entries
    if (-not $entries) {
        Write-Error "没有配置任何启动项"
        return $null
    }

    $existing = $entries | Where-Object { $_.name -eq $Name } | Select-Object -First 1
    if (-not $existing) {
        Write-Error "未找到启动项: $Name"
        return $null
    }

    Write-Info "删除启动项: $Name"
    Write-Info "  目标: $($existing.target)"

    if (-not $DryRun) {
        $Config.entries = @($entries | Where-Object { $_.name -ne $Name })
    }

    return $Config
}

function Sync-WorkDir {
    <#
    .SYNOPSIS
        批量更新工作目录路径
    #>
    param(
        $Config,
        [string]$OldPath,
        [string]$NewPath
    )

    if (-not $OldPath -or -not $NewPath) {
        Write-Error "请指定旧工作目录（-OldWorkDir）和新工作目录（-NewWorkDir）"
        return $null
    }

    $entries = $Config.entries
    if (-not $entries) {
        Write-Error "没有配置任何启动项"
        return $null
    }

    $changedCount = 0
    foreach ($entry in $entries) {
        $changed = $false

        if ($entry.target -and $entry.target -like "$OldPath*") {
            $newTarget = $entry.target -replace [regex]::Escape($OldPath), $NewPath
            Write-Info "  更新目标: $($entry.target) → $newTarget"
            if (-not $DryRun) { $entry.target = $newTarget }
            $changed = $true
        }

        if ($entry.workDir -and $entry.workDir -like "$OldPath*") {
            $newWorkDir = $entry.workDir -replace [regex]::Escape($OldPath), $NewPath
            Write-Info "  更新工作目录: $($entry.workDir) → $newWorkDir"
            if (-not $DryRun) { $entry.workDir = $newWorkDir }
            $changed = $true
        }

        if ($changed) { $changedCount++ }
    }

    if ($changedCount -eq 0) {
        Write-Info "没有需要更新的启动项"
    } else {
        Write-Success "已更新 $changedCount 个启动项的工作目录"
    }

    return $Config
}

# ============================================================
# 主流程
# ============================================================

function Main {
    Write-Banner -Title "VS Code 启动项配置管理工具" -Subtitle "通用 entries.config.json 管理"

    # ─── 确定配置文件路径 ───
    if ([string]::IsNullOrEmpty($EntriesFile)) {
        $EntriesFile = Find-EntriesFile
        if (-not $EntriesFile) {
            Write-Error "未找到 entries.config.json 文件"
            Write-Info "请使用 -EntriesFile 参数指定配置文件路径"
            exit 1
        }
        Write-Info "自动检测到配置文件: $EntriesFile"
    } else {
        if (-not (Test-Path $EntriesFile)) {
            Write-Error "指定的配置文件不存在: $EntriesFile"
            exit 1
        }
        Write-Info "使用指定配置文件: $EntriesFile"
    }

    if ($DryRun) {
        Write-Warning "预览模式（不会写入任何文件）"
    }
    Write-Host ""

    # ─── 读取配置文件 ───
    $config = Read-EntriesFile -Path $EntriesFile
    if ($null -eq $config) { exit 1 }

    # ─── 执行操作 ───
    switch ($Action) {
        "list" {
            Show-EntriesList -Config $config
            return
        }

        "add" {
            $result = Add-Entry -Config $config -Name $EntryName -Target $EntryTarget -WorkDir $WorkDir
            if ($null -eq $result) { exit 1 }
            $config = $result
        }

        "remove" {
            $result = Remove-Entry -Config $config -Name $EntryName
            if ($null -eq $result) { exit 1 }
            $config = $result
        }

        "update" {
            $result = Add-Entry -Config $config -Name $EntryName -Target $EntryTarget -WorkDir $WorkDir
            if ($null -eq $result) { exit 1 }
            $config = $result
        }

        "sync-workdir" {
            $result = Sync-WorkDir -Config $config -OldPath $OldWorkDir -NewPath $NewWorkDir
            if ($null -eq $result) { exit 1 }
            $config = $result
        }
    }

    # ─── 写入文件 ───
    if ($DryRun) {
        Write-WhatIf "预览模式结束 — 未写入文件"
        return
    }

    Write-Step "写入配置文件"
    $success = Write-EntriesFile -Path $EntriesFile -Config $config
    if ($success) {
        Write-Success "配置文件已更新: $EntriesFile"
    } else {
        exit 1
    }
}

# ============================================================
# 启动
# ============================================================
Main
