<#
.SYNOPSIS
    开发环境迁移工具 — 公共模块（低风险版）
.DESCRIPTION
    仅支持 env 和 config 策略：设置环境变量 + 删除 C 盘原数据。
    不再复制数据，不再支持 symlink/wsl/docker/copy 高风险策略。
    通过 . .\EnvMigration-Common.ps1 引用。
#>

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Resolve-Path (Join-Path $scriptPath "..\..\..")
$basicMethodOutput = Join-Path $projectRoot "PowerShellScript\BasicMethod\Core\Output.psm1"
if (Test-Path $basicMethodOutput) {
    Import-Module $basicMethodOutput -Force -DisableNameChecking
}



function Resolve-EnvPath($Path) {
    if ([string]::IsNullOrEmpty($Path)) { return "" }
    $maxIter = 10; $iter = 0
    while ($Path -match '%([^%]+)%' -and $iter -lt $maxIter) {
        $iter++
        $v = [Environment]::GetEnvironmentVariable($Matches[1], "Process")
        if ([string]::IsNullOrEmpty($v)) { $v = [Environment]::GetEnvironmentVariable($Matches[1], "User") }
        if ([string]::IsNullOrEmpty($v)) { $v = [Environment]::GetEnvironmentVariable($Matches[1], "Machine") }
        if ([string]::IsNullOrEmpty($v)) { return $Path }
        $Path = $Path -replace [regex]::Escape("%$($Matches[1])%"), $v
    }
    return $Path
}

function Get-HumanSize($Bytes) {
    if (-not $Bytes) { return "0 B" }
    if ($Bytes -gt 1GB) { return "{0:N2} GB" -f ($Bytes / 1GB) }
    if ($Bytes -gt 1MB) { return "{0:N2} MB" -f ($Bytes / 1MB) }
    if ($Bytes -gt 1KB) { return "{0:N2} KB" -f ($Bytes / 1KB) }
    return "$Bytes B"
}

function Confirm-Prompt($Prompt, $Default = "n") {
    $optStr = if ($Default -eq "y") { "Y/n" } else { "y/N" }
    $r = Read-Host "$Prompt [$optStr]"
    if ([string]::IsNullOrWhiteSpace($r)) { $r = $Default }
    return $r.ToLower() -eq "y"
}

function Move-ToRecycleBin {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return }
    try {
        Remove-Item $Path -Recurse -Force -ErrorAction Stop
        Write-Host "  🗑️ 已删除: $Path" -ForegroundColor Gray
    } catch {
        Write-Host "  ⚠️ 删除失败: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "   请手动删除: $Path" -ForegroundColor Gray
    }
}

function Remove-Directory {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return }
    try {
        Remove-Item $Path -Recurse -Force -ErrorAction Stop
        Write-Host "  🗑️ 已删除: $Path" -ForegroundColor Gray
    } catch {
        Write-Host "  ⚠️ 删除失败: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "   请手动删除: $Path" -ForegroundColor Gray
    }
}

function Get-ItemSourcePaths {
    param($Source)
    $paths = @()
    if ($Source -is [string]) { $paths += Resolve-EnvPath $Source }
    elseif ($Source -is [PSCustomObject]) {
        $Source.PSObject.Properties | ForEach-Object {
            $v = $_.Value
            if ($v -is [string] -and ($v -match '[\\/]') -and ($v -match '^[A-Za-z]:[\\/]' -or $v -match '^%')) {
                $paths += Resolve-EnvPath $v
            }
        }
    }
    return $paths
}

function Get-EnabledItems {
    param($Config)
    $result = @()
    $Config.migrationItems.PSObject.Properties | ForEach-Object { if ($_.Value.enabled) { $result += $_.Name } }
    return $result
}

# ============================================================
# 配置加载
# ============================================================

function Load-MigrationConfig {
    param($ConfigPath)
    if (!(Test-Path $ConfigPath)) {
        Write-Host "❌ 配置不存在: $ConfigPath" -ForegroundColor Red
        exit 1
    }
    try {
        $Config = Get-Content $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    } catch {
        Write-Host "❌ 配置文件格式错误: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
    return $Config
}

# ============================================================
# 交互式选择
# ============================================================

function Select-MigrationItems {
    param($Config, $All, $Force, $Items)

    $itemsToProcess = @()
    if ($All -or $Force) {
        $itemsToProcess = Get-EnabledItems $Config
        Write-Host "✅ 全部模式" -ForegroundColor Green
    } elseif ($Items) {
        $itemsToProcess = $Items -split "," | ForEach-Object { $_.Trim() }
        Write-Host "✅ 指定: $($itemsToProcess -join ', ')" -ForegroundColor Green
    } else {
        $i = 1; $map = @{}
        $Config.migrationItems.PSObject.Properties | Sort-Object { $_.Value.order } | ForEach-Object {
            if (!$_.Value.enabled) { return }
            Write-Host "  [$i] $($_.Value.displayName) ($($_.Value.strategy))" -ForegroundColor White
            Write-Host "       $($_.Value.description)" -ForegroundColor Gray
            $map[$i.ToString()] = $_.Name; $i++
        }
        Write-Host "`n  [A] 全部  [Q] 退出" -ForegroundColor Yellow
        $c = Read-Host "`n请输入编号"
        if ($c -eq "Q" -or $c -eq "q") { exit 0 }
        if ($c -eq "A" -or $c -eq "a") { $itemsToProcess = Get-EnabledItems $Config }
        else {
            $c -split "," | ForEach-Object { $_.Trim() } | ForEach-Object { if ($map.ContainsKey($_)) { $itemsToProcess += $map[$_] } }
        }
        Write-Host "`n✅ 将处理: $($itemsToProcess -join ', ')" -ForegroundColor Green
    }
    if ($itemsToProcess.Count -eq 0) { Write-Host "❌ 未选择项目。"; exit 1 }
    return $itemsToProcess
}

# ============================================================
# 扫描
# ============================================================

function Scan-SourceData {
    param($Config, $ItemsToScan)
    $results = @()
    foreach ($key in $ItemsToScan) {
        $item = $Config.migrationItems.$key
        $paths = Get-ItemSourcePaths $item.source
        $size = 0L
        $anyExists = $false
        foreach ($p in $paths) {
            if (Test-Path $p) {
                $anyExists = $true
                $files = Get-ChildItem $p -Recurse -ErrorAction SilentlyContinue | Where-Object { -not $_.PSIsContainer }
                $size += ($files | Measure-Object Length -Sum).Sum
            }
        }
        $results += [PSCustomObject]@{
            Key       = $key
            Name      = $item.displayName
            Strategy  = $item.strategy
            Paths     = $paths -join ", "
            Size      = $size
            SizeHuman = Get-HumanSize $size
            Exists    = $anyExists
        }
    }
    return $results
}

function Write-ScanResultsTable {
    param($ScanResults)
    Write-Host ("{0,-6} {1,-22} {2,-10} {3,-12}" -f "状态","项目","策略","大小") -ForegroundColor Yellow
    Write-Host $("-"*60) -ForegroundColor DarkGray
    $ScanResults | % { Write-Host ("{0,-6} {1,-22} {2,-10} {3,-12}" -f $(if($_.Exists){"📦"}else{"⬜"}), $_.Name, $_.Strategy, $_.SizeHuman) -ForegroundColor White }
    Write-Host $("-"*60) -ForegroundColor DarkGray
    Write-Host "总计: $(Get-HumanSize (($ScanResults | Measure-Object Size -Sum).Sum))" -ForegroundColor Cyan
}

# ============================================================
# 迁移执行函数
# ============================================================

<#
.SYNOPSIS
    执行 env 策略：设置环境变量 + 删除 C 盘原数据
.DESCRIPTION
    设置用户级环境变量指向 D:\DevEnv 目标路径，然后将 C 盘原数据移到回收站。
    不复制数据，仅重定向 + 清理。
#>
function Invoke-MigrationEnv {
    param($Item, $Key, $DevEnvRoot, $TargetPath)
    $src = Resolve-EnvPath $Item.source
    $srcExists = Test-Path $src

    # 设置主环境变量
    [Environment]::SetEnvironmentVariable($Item.envVar, $TargetPath, "User")
    Set-Item "Env:$($Item.envVar)" $TargetPath
    Write-Host "  [$Key] ✅ 环境变量: $($Item.envVar) = $TargetPath" -ForegroundColor Green

    # Rust 特殊处理：同时设置 CARGO_HOME
    if ($Item.cargoSource -and $Item.cargoTarget) {
        $cargoSrc = Resolve-EnvPath $Item.cargoSource
        $cargoTgt = Join-Path $DevEnvRoot $Item.cargoTarget
        
        [Environment]::SetEnvironmentVariable("CARGO_HOME", $cargoTgt, "User")
        Set-Item "Env:CARGO_HOME" $cargoTgt
        Write-Host "  [$Key] ✅ 环境变量: CARGO_HOME = $cargoTgt" -ForegroundColor Green

        if (Test-Path $cargoSrc) {
            Write-Host "  [$Key] 正在删除原数据: $cargoSrc" -ForegroundColor Yellow
            Move-ToRecycleBin $cargoSrc
            Write-Host "  [$Key] ✅ Cargo 原数据已删除" -ForegroundColor Green
        }

        # 更新 PATH 添加 cargo bin
        $cargoBin = Join-Path $cargoTgt "bin"
        $currentPath = [Environment]::GetEnvironmentVariable("Path", "User") -split ";"
        if ($currentPath -notcontains $cargoBin) {
            $currentPath += $cargoBin
            [Environment]::SetEnvironmentVariable("Path", ($currentPath -join ";"), "User")
            $env:Path = ($currentPath -join ";")
            Write-Host "  [$Key] ✅ PATH 添加: $cargoBin" -ForegroundColor Green
        }
    }

    # 删除 C 盘原数据
    if ($srcExists) {
        Write-Host "  [$Key] 正在删除原数据: $src" -ForegroundColor Yellow
        Move-ToRecycleBin $src
        Write-Host "  [$Key] ✅ 原数据已删除" -ForegroundColor Green
    } else {
        Write-Host "  [$Key] ⬜ 原数据不存在，无需删除" -ForegroundColor Yellow
    }

    return $true
}

<#
.SYNOPSIS
    执行 config 策略：修改 npm 配置 + 删除 C 盘原数据
.DESCRIPTION
    更新 npm config prefix/cache 指向 D:\DevEnv，更新 PATH，
    然后将 C 盘原数据移到回收站。
#>
function Invoke-MigrationConfig {
    param($Item, $Key, $DevEnvRoot)
    if ($Item.configType -eq "npm") {
        $srcP = Resolve-EnvPath $Item.source.prefix
        $srcC = Resolve-EnvPath $Item.source.cache
        $tgtP = Join-Path $DevEnvRoot $Item.target.prefix
        $tgtC = Join-Path $DevEnvRoot $Item.target.cache

        # 复制前解除 workflowprj 全局链接，避免删除时因嵌套路径报错
        $wpjPath = Join-Path $srcP "node_modules\workflowprj"
        if (Test-Path $wpjPath) {
            Write-Host "  [$Key] 检测到 workflowprj 全局包，正在解除链接..." -ForegroundColor Yellow
            if (Get-Command "npm" -ErrorAction SilentlyContinue) {
                npm uninstall -g workflowprj 2>$null
                Write-Host "  [$Key] ✅ workflowprj 已解除链接" -ForegroundColor Green
            }
        }

        # 更新 npm 配置
        if (Get-Command "npm" -ErrorAction SilentlyContinue) {
            npm config set prefix "$tgtP" 2>$null
            npm config set cache "$tgtC" 2>$null
            Write-Host "  [$Key] ✅ npm config 已更新" -ForegroundColor Green
        }

        # 更新 PATH：移除源路径，添加目标路径（去重）
        $cp = [Environment]::GetEnvironmentVariable("Path","User") -split ";" | Where-Object {
            $_ -and $_.TrimEnd('\') -ne $srcP.TrimEnd('\')
        }
        if ($cp -notcontains $tgtP) { $cp += $tgtP }
        [Environment]::SetEnvironmentVariable("Path",($cp -join ";"),"User")
        $env:Path = $cp -join ";"
        Write-Host "  [$Key] ✅ PATH 已更新" -ForegroundColor Green

        # 删除 C 盘原数据
        if (Test-Path $srcP) {
            Write-Host "  [$Key] 正在删除原数据: $srcP" -ForegroundColor Yellow
            Move-ToRecycleBin $srcP
        }
        if (Test-Path $srcC) {
            Write-Host "  [$Key] 正在删除原数据: $srcC" -ForegroundColor Yellow
            Move-ToRecycleBin $srcC
        }
        Write-Host "  [$Key] ✅ 原数据已删除" -ForegroundColor Green

        return $true
    }
    return $false
}

<#
.SYNOPSIS
    策略调度器 — 根据 strategy 分发到对应的迁移执行函数
.DESCRIPTION
    仅支持 env 和 config 两种低风险策略。
#>
function Invoke-MigrationItem {
    param($Item, $Key, $DevEnvRoot)
    $targetPath = Join-Path $DevEnvRoot $Item.target

    Write-Host "`n────────────────────────────────────────────────────" -ForegroundColor DarkGray
    Write-Host "  迁移: $($Item.displayName)" -ForegroundColor White
    Write-Host "────────────────────────────────────────────────────" -ForegroundColor DarkGray

    # 创建目标目录
    if (!(Test-Path $targetPath)) { New-Item $targetPath -ItemType Directory -Force | Out-Null }

    $success = $false
    switch ($Item.strategy) {
        "env"     { $success = Invoke-MigrationEnv $Item $Key $DevEnvRoot $targetPath }
        "config"  { $success = Invoke-MigrationConfig $Item $Key $DevEnvRoot }
        default   { Write-Host "  [$Key] ❌ 未知策略: $($Item.strategy)" -ForegroundColor Red }
    }

    return [PSCustomObject]@{
        Key    = $Key
        Name   = $Item.displayName
        Status = $(if($success){"✅"}else{"❌"})
        Detail = $(if($success){"成功"}else{"失败"})
    }
}

<#
.SYNOPSIS
    完整迁移流水线 — 遍历迁移项并执行
.DESCRIPTION
    遍历指定的迁移项列表，对每个项调用 Invoke-MigrationItem 执行迁移。
#>
function Invoke-MigrationPipeline {
    param($Config, $DevEnvRoot, $ItemsToMigrate)
    if (!(Test-Path $DevEnvRoot)) { New-Item $DevEnvRoot -ItemType Directory -Force | Out-Null }
    $migrationLog = @()

    foreach ($key in $ItemsToMigrate) {
        $item = $Config.migrationItems.$key
        $result = Invoke-MigrationItem $item $key $DevEnvRoot
        $migrationLog += $result
    }

    return $migrationLog
}

<#
.SYNOPSIS
    验证迁移结果
.DESCRIPTION
    检查迁移日志中是否有失败项，返回是否全部通过。
#>
function Test-MigrationResult {
    param($MigrationLog)
    $allPassed = $true
    foreach ($log in $MigrationLog) {
        Write-Host "  $($log.Status) $($log.Name): $($log.Detail)" -ForegroundColor White
        if ($log.Status -eq "❌") { $allPassed = $false }
    }
    return $allPassed
}

<#
.SYNOPSIS
    输出迁移摘要
.DESCRIPTION
    输出迁移完成后的摘要信息，包括目标路径和最终结果。
#>
function Write-MigrationSummary {
    param($DevEnvRoot, $AllPassed)
    Write-Host "📂 目标: $DevEnvRoot" -ForegroundColor Cyan
    if ($AllPassed) {
        Write-Host "`n✅ 迁移成功！请重启终端。" -ForegroundColor Green
    } else {
        Write-Host "`n⚠️ 部分迁移失败，请检查。" -ForegroundColor Yellow
    }
}
