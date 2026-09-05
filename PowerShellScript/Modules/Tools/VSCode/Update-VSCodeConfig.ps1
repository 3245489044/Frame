<#
.SYNOPSIS
    VS Code 配置更新工具 — 通用 settings.json 配置管理
.DESCRIPTION
    通用化 VS Code settings.json 配置管理工具，支持：
    - 更新任意配置节（如 code-runner.executorMap）
    - 添加/修改/删除配置项
    - 预览变更
    - 支持多个 VS Code 配置文件

    替代原 fix_coderunner_config.ps1（已移至 Legacy/）

.PARAMETER SettingsFile
    目标 settings.json 文件路径（默认自动检测）
.PARAMETER Section
    配置节路径，如 "code-runner.executorMap"（默认）
.PARAMETER AddOrUpdate
    要添加或更新的配置项，格式为 Hashtable
.PARAMETER Remove
    要删除的配置项键名数组
.PARAMETER DryRun
    仅预览变更，不实际写入文件
.PARAMETER Backup
    修改前备份原文件（默认启用）

.EXAMPLE
    # 更新 code-runner.executorMap
    .\Update-VSCodeConfig.ps1 -AddOrUpdate @{
        "powershell" = "cd $dir && pwsh -NoProfile -File $fileName"
    }

.EXAMPLE
    # 指定配置文件并添加多个配置项
    .\Update-VSCodeConfig.ps1 -SettingsFile "D:\Project\.vscode\settings.json" `
        -AddOrUpdate @{
            "files.autoSave" = "onFocusChange"
            "editor.fontSize" = 14
        } -DryRun

.EXAMPLE
    # 删除配置项
    .\Update-VSCodeConfig.ps1 -Remove @("code-runner.executorMap.python")
#>

[CmdletBinding(DefaultParameterSetName = "Update")]
param(
    [Parameter()]
    [string]$SettingsFile = "",

    [Parameter()]
    [string]$Section = "code-runner.executorMap",

    [Parameter(ParameterSetName = "Update")]
    [hashtable]$AddOrUpdate = @{},

    [Parameter(ParameterSetName = "Remove")]
    [string[]]$Remove = @(),

    [Parameter()]
    [switch]$DryRun,

    [Parameter()]
    [switch]$Backup
)

# ============================================================
# 加载公共模块
# ============================================================
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$modulesPath = Join-Path $scriptPath "modules"
Import-Module (Join-Path $modulesPath "Write-Format.psm1") -Force -ErrorAction SilentlyContinue
Import-Module (Join-Path $modulesPath "Test-Environment.psm1") -Force -ErrorAction SilentlyContinue

# ============================================================
# 辅助函数（模块加载失败时的后备）
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

function Find-VSCodeSettingsFile {
    <#
    .SYNOPSIS
        自动检测 VS Code settings.json 文件
    #>
    $candidates = @()

    # 1. 当前项目 .vscode/settings.json
    $projectRoot = Resolve-Path (Join-Path $scriptPath "..\..")
    $candidates += Join-Path $projectRoot ".vscode\settings.json"

    # 2. 用户全局 settings.json
    $candidates += "$env:APPDATA\Code\User\settings.json"

    # 3. VSCode Portable 模式
    $candidates += "$env:USERPROFILE\AppData\Local\Programs\Microsoft VS Code\data\user-data\User\settings.json"

    foreach ($path in $candidates) {
        if (Test-Path $path) { return $path }
    }

    return $null
}

function Get-JsonValueByPath {
    <#
    .SYNOPSIS
        通过点分隔路径获取 JSON 对象中的值
    .PARAMETER JsonObj
        PSObject 或 Hashtable
    .PARAMETER Path
        点分隔路径，如 "code-runner.executorMap"
    #>
    param($JsonObj, [string]$Path)

    $current = $JsonObj
    $parts = $Path -split '\.'
    foreach ($part in $parts) {
        if ($null -eq $current) { return $null }
        if ($current -is [PSCustomObject]) {
            $current = $current.$part
        } elseif ($current -is [Hashtable]) {
            $current = $current[$part]
        } else {
            return $null
        }
    }
    return $current
}

function Set-JsonValueByPath {
    <#
    .SYNOPSIS
        通过点分隔路径设置 JSON 对象中的值
    .PARAMETER JsonObj
        PSObject（引用传递）
    .PARAMETER Path
        点分隔路径
    .PARAMETER Value
        要设置的值
    #>
    param(
        [ref]$JsonObj,
        [string]$Path,
        $Value
    )

    $parts = $Path -split '\.'
    $current = $JsonObj.Value
    for ($i = 0; $i -lt $parts.Count - 1; $i++) {
        $part = $parts[$i]
        if ($current -is [PSCustomObject]) {
            if ($null -eq $current.$part) {
                $current | Add-Member -NotePropertyName $part -NotePropertyValue ([PSCustomObject]@{})
            }
            $current = $current.$part
        } elseif ($current -is [Hashtable]) {
            if (-not $current.ContainsKey($part)) {
                $current[$part] = @{}
            }
            $current = $current[$part]
        }
    }

    $lastPart = $parts[-1]
    if ($current -is [PSCustomObject]) {
        $current | Add-Member -NotePropertyName $lastPart -NotePropertyValue $Value -Force
    } elseif ($current -is [Hashtable]) {
        $current[$lastPart] = $Value
    }
}

function Remove-JsonValueByPath {
    <#
    .SYNOPSIS
        通过点分隔路径删除 JSON 对象中的值
    .PARAMETER JsonObj
        PSObject（引用传递）
    .PARAMETER Path
        点分隔路径
    #>
    param(
        [ref]$JsonObj,
        [string]$Path
    )

    $parts = $Path -split '\.'
    $current = $JsonObj.Value
    for ($i = 0; $i -lt $parts.Count - 1; $i++) {
        $part = $parts[$i]
        if ($null -eq $current) { return }
        if ($current -is [PSCustomObject]) {
            $current = $current.$part
        } elseif ($current -is [Hashtable]) {
            $current = $current[$part]
        } else {
            return
        }
    }

    $lastPart = $parts[-1]
    if ($current -is [PSCustomObject]) {
        $current.PSObject.Properties.Remove($lastPart)
    } elseif ($current -is [Hashtable]) {
        $current.Remove($lastPart)
    }
}

function ConvertTo-OrderedHashtable {
    <#
    .SYNOPSIS
        将 PSCustomObject 转换为排序后的 Hashtable（保持键排序）
    #>
    param($InputObject)

    if ($null -eq $InputObject) { return $null }
    if ($InputObject -is [PSCustomObject]) {
        $sorted = [Ordered]@{}
        $InputObject.PSObject.Properties | Sort-Object Name | ForEach-Object {
            $sorted[$_.Name] = ConvertTo-OrderedHashtable $_.Value
        }
        return $sorted
    }
    if ($InputObject -is [System.Collections.IList]) {
        $list = @()
        foreach ($item in $InputObject) {
            $list += ConvertTo-OrderedHashtable $item
        }
        return $list
    }
    return $InputObject
}

# ============================================================
# 主流程
# ============================================================

function Main {
    Write-Banner -Title "VS Code 配置更新工具" -Subtitle "通用 settings.json 配置管理"

    # ─── 确定配置文件路径 ───
    if ([string]::IsNullOrEmpty($SettingsFile)) {
        $SettingsFile = Find-VSCodeSettingsFile
        if (-not $SettingsFile) {
            Write-Error "未找到 VS Code settings.json 文件"
            Write-Info "请使用 -SettingsFile 参数指定配置文件路径"
            exit 1
        }
        Write-Info "自动检测到配置文件: $SettingsFile"
    } else {
        if (-not (Test-Path $SettingsFile)) {
            Write-Error "指定的配置文件不存在: $SettingsFile"
            exit 1
        }
        Write-Info "使用指定配置文件: $SettingsFile"
    }

    if ($DryRun) {
        Write-Warning "预览模式（不会写入任何文件）"
    }
    Write-Host ""

    # ─── 读取配置文件 ───
    Write-Step "读取配置文件"
    try {
        $content = Get-Content $SettingsFile -Raw -Encoding UTF8
        $config = $content | ConvertFrom-Json
        Write-Success "配置文件读取成功"
    } catch {
        Write-Error "配置文件解析失败: $($_.Exception.Message)"
        exit 1
    }

    # ─── 执行更新 ───
    $changes = @()

    if ($AddOrUpdate.Count -gt 0) {
        Write-Step "添加/更新配置项"

        $sectionObj = Get-JsonValueByPath -JsonObj $config -Path $Section
        if ($null -eq $sectionObj) {
            Write-Info "配置节 '$Section' 不存在，将创建"
            $sectionObj = [PSCustomObject]@{}
            Set-JsonValueByPath -JsonObj ([ref]$config) -Path $Section -Value $sectionObj
        }

        foreach ($key in $AddOrUpdate.Keys) {
            $oldValue = Get-JsonValueByPath -JsonObj $sectionObj -Path $key
            $newValue = $AddOrUpdate[$key]

            if ($null -ne $oldValue) {
                $oldStr = "$oldValue"
                $newStr = "$newValue"
                if ($oldStr -eq $newStr) {
                    Write-Info "  未变更: $key = $oldStr"
                    continue
                }
                Write-Info "  更新: $key = $oldStr → $newStr"
            } else {
                Write-Info "  新增: $key = $newValue"
            }

            if (-not $DryRun) {
                Set-JsonValueByPath -JsonObj ([ref]$sectionObj) -Path $key -Value $newValue
            }
            $changes += @{ Action = "AddOrUpdate"; Key = $key; OldValue = $oldValue; NewValue = $newValue }
        }
    }

    if ($Remove.Count -gt 0) {
        Write-Step "删除配置项"

        foreach ($key in $Remove) {
            $oldValue = Get-JsonValueByPath -JsonObj $config -Path $key
            if ($null -eq $oldValue) {
                Write-Info "  不存在: $key（跳过）"
                continue
            }
            Write-Info "  删除: $key = $oldValue"

            if (-not $DryRun) {
                Remove-JsonValueByPath -JsonObj ([ref]$config) -Path $key
            }
            $changes += @{ Action = "Remove"; Key = $key; OldValue = $oldValue; NewValue = $null }
        }
    }

    # ─── 写入文件 ───
    if ($changes.Count -eq 0) {
        Write-Success "没有需要变更的配置项"
        return
    }

    if ($DryRun) {
        Write-WhatIf "预览模式结束 — 共 $($changes.Count) 项变更，未写入文件"
        return
    }

    Write-Step "写入配置文件"

    # 备份
    if ($Backup) {
        $backupPath = "$SettingsFile.backup_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
        Copy-Item $SettingsFile $backupPath -Force
        Write-Info "已备份: $backupPath"
    }

    try {
        # 转换为排序后的 JSON 并写入
        $orderedConfig = ConvertTo-OrderedHashtable $config
        $jsonContent = $orderedConfig | ConvertTo-Json -Depth 10
        $utf8NoBom = New-Object System.Text.UTF8Encoding $false
        [System.IO.File]::WriteAllText($SettingsFile, $jsonContent, $utf8NoBom)
        Write-Success "配置文件已更新: $SettingsFile"
        Write-Info "共 $($changes.Count) 项变更"
    } catch {
        Write-Error "写入配置文件失败: $($_.Exception.Message)"
        if ($Backup -and (Test-Path $backupPath)) {
            Write-Warning "正在从备份恢复..."
            Copy-Item $backupPath $SettingsFile -Force
            Write-Success "已从备份恢复"
        }
        exit 1
    }
}

# ============================================================
# 启动
# ============================================================
Main
