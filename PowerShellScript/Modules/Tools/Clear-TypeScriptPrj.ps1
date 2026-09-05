<#
.SYNOPSIS
    安全清理 TypeScriptPrj 目录中的冗余文件，释放磁盘空间。
.DESCRIPTION
    本脚本执行以下清理操作（均带确认提示）：
    1. 删除 TsSln/node_modules/ — 冗余的 TypeScript 编译器副本（约 25 MB）
    2. 删除 TsWorkspace/ 下各子项目的 build/ 和 .turbo/ 缓存（约 0.6 MB）
    3. 清理 pnpm 虚拟 store 中非 win32-x64 平台的跨平台二进制包（esbuild、rollup、turbo 等，可回收 ~200 MB）
    4. 运行 pnpm store prune — 清理 pnpm store 中孤立包缓存
    5. 删除根目录 build/ 和 .turbo/（如果存在）
.NOTES
    作者: Auto-generated
    版本: 1.0
    用法: .\PowerShellScript\Tools\Clear-TypeScriptPrj.ps1 [-Force]
.PARAMETER Force
    跳过所有确认提示，直接执行清理。
.PARAMETER WhatIf
    仅预览将要删除的内容，不实际执行删除。
.EXAMPLE
    .\PowerScript\Tools\Clear-TypeScriptPrj.ps1
    交互式执行，每步都会询问确认。
.EXAMPLE
    .\PowerScript\Tools\Clear-TypeScriptPrj.ps1 -Force
    跳过确认，直接清理。
.EXAMPLE
    .\PowerScript\Tools\Clear-TypeScriptPrj.ps1 -WhatIf
    预览将要删除的内容。
#>

[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$WhatIf
)

$ProjectRoot = Resolve-Path "$PSScriptRoot\..\.."
$TypeScriptPrj = Join-Path $ProjectRoot "TypeScriptPrj"

# 颜色输出函数
function Write-Info  { Write-Host "[INFO] $($args[0])" -ForegroundColor Cyan }
function Write-Warn  { Write-Host "[WARN] $($args[0])" -ForegroundColor Yellow }
function Write-OK    { Write-Host "[OK]   $($args[0])" -ForegroundColor Green }
function Write-Action{ Write-Host "[>>>] $($args[0])" -ForegroundColor Magenta }

function Get-DirSize {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return 0 }
    $size = (Get-ChildItem $Path -Recurse -File -Force -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
    return [math]::Round($size / 1MB, 2)
}

function Confirm-Step {
    param([string]$Message)
    if ($Force -or $WhatIf) { return $true }
    $response = Read-Host "`n$Message [Y/n]"
    return ($response -eq '' -or $response -eq 'y' -or $response -eq 'Y')
}

# ============================================================
# 步骤 0: 环境检查
# ============================================================
Write-Host "`n==============================================" -ForegroundColor Cyan
Write-Host "  TypeScriptPrj 空间清理工具" -ForegroundColor Cyan
Write-Host "==============================================" -ForegroundColor Cyan
Write-Info "项目根目录: $ProjectRoot"
Write-Info "TypeScriptPrj: $TypeScriptPrj"

if (-not (Test-Path $TypeScriptPrj)) {
    Write-Warn "TypeScriptPrj 目录不存在，退出。"
    exit 1
}

# 统计清理前空间
$beforeTotal = Get-DirSize $TypeScriptPrj
Write-Info "清理前 TypeScriptPrj 总大小: ${beforeTotal} MB"

# ============================================================
# 步骤 1: 删除 TsSln/node_modules/（冗余 TypeScript 副本）
# ============================================================
Write-Host "`n--- 步骤 1: 删除 TsSln/node_modules/ ---" -ForegroundColor Yellow
$tsSlnNodeModules = Join-Path $TypeScriptPrj "TsSln\node_modules"
$tsSlnSize = Get-DirSize $tsSlnNodeModules
Write-Info "TsSln/node_modules/ 大小: ${tsSlnSize} MB"

if ($tsSlnSize -gt 0 -and (Test-Path $tsSlnNodeModules)) {
    if ($WhatIf) {
        Write-Action "[WhatIf] 将删除: $tsSlnNodeModules (${tsSlnSize} MB)"
    } elseif (Confirm-Step "是否删除 TsSln/node_modules/（可回收 ${tsSlnSize} MB）？") {
        Write-Action "正在删除 TsSln/node_modules/ ..."
        Remove-Item $tsSlnNodeModules -Recurse -Force -ErrorAction SilentlyContinue
        if (-not (Test-Path $tsSlnNodeModules)) {
            Write-OK "已删除 TsSln/node_modules/，回收 ${tsSlnSize} MB"
        } else {
            Write-Warn "删除可能不完整，请检查权限后重试。"
        }
    } else {
        Write-Info "跳过 TsSln/node_modules/"
    }
} else {
    Write-OK "TsSln/node_modules/ 不存在或已为空，跳过。"
}

# ============================================================
# 步骤 2: 删除 TsWorkspace/ 下各子项目的 build/ 和 .turbo/
# ============================================================
Write-Host "`n--- 步骤 2: 清理 TsWorkspace 子项目构建缓存 ---" -ForegroundColor Yellow
$workspaceDirs = @("BasicMethod", "MCPFramework", "MCPBridge", "CsprojContext")
$totalCacheSize = 0
$cacheItems = @()

foreach ($dir in $workspaceDirs) {
    foreach ($cacheDir in @("build", ".turbo")) {
        $target = Join-Path (Join-Path (Join-Path $TypeScriptPrj "TsWorkspace") $dir) $cacheDir
        $size = Get-DirSize $target
        if ($size -gt 0 -and (Test-Path $target)) {
            $cacheItems += @{ Path = $target; Size = $size; Name = "TsWorkspace/$dir/$cacheDir" }
            $totalCacheSize += $size
        }
    }
}

# 根目录的 build/ 和 .turbo/
foreach ($cacheDir in @("build", ".turbo")) {
    $target = Join-Path $TypeScriptPrj $cacheDir
    $size = Get-DirSize $target
    if ($size -gt 0 -and (Test-Path $target)) {
        $cacheItems += @{ Path = $target; Size = $size; Name = $cacheDir }
        $totalCacheSize += $size
    }
}

if ($cacheItems.Count -gt 0) {
    Write-Info "发现 $($cacheItems.Count) 个构建缓存目录，共 ${totalCacheSize} MB"
    foreach ($item in $cacheItems) {
        Write-Host "  - $($item.Name): $($item.Size) MB"
    }

    if ($WhatIf) {
        foreach ($item in $cacheItems) {
            Write-Action "[WhatIf] 将删除: $($item.Path) ($($item.Size) MB)"
        }
    } elseif (Confirm-Step "是否删除以上 $($cacheItems.Count) 个构建缓存目录（可回收 ${totalCacheSize} MB）？") {
        foreach ($item in $cacheItems) {
            Write-Action "正在删除: $($item.Name) ..."
            Remove-Item $item.Path -Recurse -Force -ErrorAction SilentlyContinue
            if (-not (Test-Path $item.Path)) {
                Write-OK "已删除 $($item.Name)，回收 $($item.Size) MB"
            } else {
                Write-Warn "删除 $($item.Name) 可能不完整。"
            }
        }
    } else {
        Write-Info "跳过构建缓存清理。"
    }
} else {
    Write-OK "未发现构建缓存目录，跳过。"
}

# ============================================================
# 步骤 3: 清理 pnpm 虚拟 store 中非 win32-x64 平台的跨平台二进制包
# ============================================================
Write-Host "`n--- 步骤 3: 清理跨平台冗余二进制包（仅保留 win32-x64） ---" -ForegroundColor Yellow
$pnpmStore = Join-Path $TypeScriptPrj "node_modules\.pnpm"
$crossPlatformPkgs = @(
    @{ Prefix = "@esbuild+"; Platforms = @("darwin-arm64", "darwin-x64", "linux-arm64", "linux-x64", "win32-arm64", "android-arm64", "android-arm", "android-x64", "linux-arm", "linux-ia32", "linux-loong64", "linux-mips64el", "linux-ppc64", "linux-riscv64", "linux-s390x", "freebsd-arm64", "freebsd-x64", "netbsd-arm64", "netbsd-x64", "openbsd-arm64", "openbsd-x64", "sunos-x64", "win32-ia32", "aix-ppc64") },
    @{ Prefix = "@rollup+rollup-"; Platforms = @("darwin-arm64", "darwin-x64", "linux-arm64-gnu", "linux-arm64-musl", "linux-arm-gnueabihf", "linux-arm-musleabihf", "linux-loong64-gnu", "linux-loong64-musl", "linux-ppc64-gnu", "linux-ppc64-musl", "linux-riscv64-gnu", "linux-riscv64-musl", "linux-s390x-gnu", "linux-x64-gnu", "linux-x64-musl", "win32-arm64-msvc", "win32-ia32-msvc", "win32-x64-msvc", "android-arm-eabi", "android-arm64", "freebsd-arm64", "freebsd-x64", "openbsd-x64", "openharmony-arm64") },
    @{ Prefix = "@turbo+"; Platforms = @("darwin-64", "darwin-arm64", "linux-64", "linux-arm64", "windows-arm64") }
)

$totalCrossSize = 0
$crossItems = @()

foreach ($pkg in $crossPlatformPkgs) {
    $prefix = $pkg.Prefix
    $keepPlatform = "win32-x64"
    foreach ($platform in $pkg.Platforms) {
        $pattern = "$prefix$platform@*"
        $dirs = Get-ChildItem $pnpmStore -Directory -Filter $pattern -ErrorAction SilentlyContinue
        foreach ($dir in $dirs) {
            $size = Get-DirSize $dir.FullName
            if ($size -gt 0) {
                $crossItems += @{ Path = $dir.FullName; Size = $size; Name = $dir.Name }
                $totalCrossSize += $size
            }
        }
    }
}

if ($crossItems.Count -gt 0) {
    Write-Info "发现 $($crossItems.Count) 个跨平台冗余包，共 ${totalCrossSize} MB"
    foreach ($item in $crossItems) {
        Write-Host "  - $($item.Name): $($item.Size) MB"
    }

    if ($WhatIf) {
        foreach ($item in $crossItems) {
            Write-Action "[WhatIf] 将删除: $($item.Name) ($($item.Size) MB)"
        }
    } elseif (Confirm-Step "是否删除以上 $($crossItems.Count) 个跨平台冗余包（可回收 ${totalCrossSize} MB）？") {
        foreach ($item in $crossItems) {
            Write-Action "正在删除: $($item.Name) ..."
            Remove-Item $item.Path -Recurse -Force -ErrorAction SilentlyContinue
            if (-not (Test-Path $item.Path)) {
                Write-OK "已删除 $($item.Name)，回收 $($item.Size) MB"
            } else {
                Write-Warn "删除 $($item.Name) 可能不完整。"
            }
        }
    } else {
        Write-Info "跳过跨平台冗余包清理。"
    }
} else {
    Write-OK "未发现跨平台冗余包，跳过。"
}

# ============================================================
# 步骤 4: 运行 pnpm store prune
# ============================================================
Write-Host "`n--- 步骤 4: 清理 pnpm store 孤立包 ---" -ForegroundColor Yellow
$pnpmStorePath = & pnpm store path 2>$null
if ($LASTEXITCODE -eq 0 -and $pnpmStorePath) {
    $pnpmStoreSize = Get-DirSize $pnpmStorePath.Trim()
    Write-Info "pnpm store 路径: $($pnpmStorePath.Trim())"
    Write-Info "pnpm store 当前大小: ${pnpmStoreSize} MB"

    if ($WhatIf) {
        Write-Action "[WhatIf] 将执行: pnpm store prune"
    } elseif (Confirm-Step "是否执行 pnpm store prune 清理孤立包缓存？") {
        Write-Action "正在执行 pnpm store prune ..."
        Push-Location $TypeScriptPrj
        $pruneOutput = pnpm store prune 2>&1
        Pop-Location
        Write-Host $pruneOutput
        Write-OK "pnpm store prune 执行完成。"
    } else {
        Write-Info "跳过 pnpm store prune。"
    }
} else {
    Write-Warn "未找到 pnpm 或无法获取 store 路径，跳过 pnpm store prune。"
}

# ============================================================
# 清理后统计
# ============================================================
Write-Host "`n==============================================" -ForegroundColor Cyan
Write-Host "  清理完成统计" -ForegroundColor Cyan
Write-Host "==============================================" -ForegroundColor Cyan

$afterTotal = Get-DirSize $TypeScriptPrj
$freed = [math]::Round($beforeTotal - $afterTotal, 2)
Write-Info "清理前总大小: ${beforeTotal} MB"
Write-Info "清理后总大小: ${afterTotal} MB"
Write-Host "  >>> 释放空间: ${freed} MB <<<" -ForegroundColor Green

if ($WhatIf) {
    Write-Warn "本次为 WhatIf 预览模式，未实际删除任何文件。"
    Write-Warn "去掉 -WhatIf 参数实际执行清理。"
}

Write-Host "`n提示: 如果之后需要恢复依赖，执行以下命令即可：" -ForegroundColor Cyan
Write-Host "  cd TypeScriptPrj && pnpm install" -ForegroundColor Gray
Write-Host "`n"
