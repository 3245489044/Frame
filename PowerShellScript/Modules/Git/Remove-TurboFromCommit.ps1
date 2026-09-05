<#
.SYNOPSIS
    从指定提交中移除 TypeScript 项目的 turbo 构建工具相关文件，保留 pnpm 工作区配置。
.DESCRIPTION
    该脚本用于修复错误提交，从提交 5dfd699 中移除 turbo 相关文件：
    - TypeScriptPrj/turbo.json
    - TypeScriptPrj/.turbo/cache/ (构建缓存)
    - TypeScriptPrj/package.json 中的 turbo scripts 和依赖（保留 pnpm 配置）
    
    保留 pnpm 工作区配置：
    - TypeScriptPrj/pnpm-lock.yaml
    - TypeScriptPrj/pnpm-workspace.yaml
    - TypeScriptPrj/package.json 中的 pnpm 相关配置
.PARAMETER CommitHash
    需要修复的提交哈希（默认：5dfd699）
.PARAMETER PushToRemote
    是否推送到远程（默认：$false）
.PARAMETER RemoteName
    远程仓库名称（默认：origin）
.PARAMETER BranchName
    分支名称（默认：Main）
.EXAMPLE
    .\Remove-TurboFromCommit.ps1
    从 5dfd699 中移除 turbo 文件，不推送
.EXAMPLE
    .\Remove-TurboFromCommit.ps1 -PushToRemote -RemoteName origin -BranchName Main
    从 5dfd699 中移除 turbo 文件并推送到 origin/Main
#>

param(
    [string]$CommitHash = "5dfd699",
    [switch]$PushToRemote = $false,
    [string]$RemoteName = "origin",
    [string]$BranchName = "Main"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

Write-Host "=== 从提交 $CommitHash 中移除 turbo 构建工具相关文件 ===" -ForegroundColor Cyan
Write-Host "仓库根目录: $repoRoot" -ForegroundColor Gray

# 步骤 1: 检查当前状态
Write-Host "`n[1/6] 检查当前 git 状态..." -ForegroundColor Yellow
$status = git -C $repoRoot status --short
if ($status) {
    Write-Host "当前有未提交的修改，正在暂存..." -ForegroundColor DarkYellow
    git -C $repoRoot stash
}

# 步骤 2: 确认提交存在
Write-Host "`n[2/6] 确认提交 $CommitHash 存在..." -ForegroundColor Yellow
$commitExists = git -C $repoRoot cat-file -t $CommitHash 2>$null
if (-not $commitExists) {
    Write-Error "提交 $CommitHash 不存在！"
    exit 1
}

# 获取父提交哈希
$parentCommit = git -C $repoRoot rev-parse "$CommitHash^1"
Write-Host "父提交: $parentCommit" -ForegroundColor Gray

# 步骤 3: 重置到父提交，保留工作区修改
Write-Host "`n[3/6] 重置到父提交，保留工作区修改..." -ForegroundColor Yellow
git -C $repoRoot reset --soft $parentCommit

# 步骤 4: 从 5dfd699 中取出所有合法文件（排除 turbo 相关文件）
Write-Host "`n[4/6] 从 $CommitHash 中取出合法变更文件（排除 turbo 相关文件）..." -ForegroundColor Yellow

# 获取 5dfd699 中变更的所有文件
$allFiles = git -C $repoRoot diff --name-only $parentCommit $CommitHash

# 定义需要排除的 turbo 文件模式
$turboPatterns = @(
    "TypeScriptPrj/turbo.json",
    "TypeScriptPrj/.turbo/*"
)

# 筛选出合法文件（非 turbo 文件）
$legalFiles = @()
foreach ($file in $allFiles) {
    $isTurbo = $false
    foreach ($pattern in $turboPatterns) {
        if ($file -like $pattern) {
            $isTurbo = $true
            break
        }
    }
    if (-not $isTurbo) {
        $legalFiles += $file
    }
}

Write-Host "找到 $($allFiles.Count) 个变更文件，其中 $($legalFiles.Count) 个合法文件" -ForegroundColor Gray

# 从 5dfd699 中 checkout 合法文件到工作区和暂存区
foreach ($file in $legalFiles) {
    try {
        git -C $repoRoot checkout $CommitHash -- $file 2>$null
        Write-Host "  ✓ $file" -ForegroundColor Green
    } catch {
        Write-Host "  ⚠ $file (跳过，可能在 $CommitHash 中不存在)" -ForegroundColor DarkYellow
    }
}

# 步骤 5: 处理 package.json - 去掉 turbo 相关，保留 pnpm 相关
Write-Host "`n[5/6] 处理 TypeScriptPrj/package.json（去掉 turbo，保留 pnpm）..." -ForegroundColor Yellow

$pkgPath = Join-Path $repoRoot "TypeScriptPrj/package.json"
if (Test-Path $pkgPath) {
    $pkgContent = Get-Content $pkgPath -Raw | ConvertFrom-Json
    
    # 保留 pnpm 相关配置，去掉 turbo 相关
    $newScripts = @{}
    $turboScripts = @("build", "dev", "clean", "test", "lint", "format")
    
    # 添加原始 scripts（非 turbo）
    $originalScripts = @{
        "build" = "node build/TsSln/cli.js build"
        "clean" = "node build/TsSln/cli.js clean"
        "rebuild" = "node build/TsSln/cli.js rebuild"
        "list" = "node build/TsSln/cli.js list"
        "info" = "node build/TsSln/cli.js info"
        "build:ts" = "npm run build --workspace=TsSln"
    }
    
    # 保留 pnpm 相关 scripts（如果存在）
    if ($pkgContent.scripts.tsm) {
        $originalScripts["tsm"] = $pkgContent.scripts.tsm
    }
    if ($pkgContent.scripts."install:deps") {
        $originalScripts["install:deps"] = $pkgContent.scripts."install:deps"
    }
    
    $pkgContent.scripts = $originalScripts
    
    # 删除 turbo 依赖
    if ($pkgContent.devDependencies -and $pkgContent.devDependencies.turbo) {
        $pkgContent.devDependencies.PSObject.Properties.Remove("turbo")
        # 如果 devDependencies 为空，删除整个字段
        if ($pkgContent.devDependencies.PSObject.Properties.Count -eq 0) {
            $pkgContent.PSObject.Properties.Remove("devDependencies")
        }
    }
    
    # 保留 packageManager（pnpm 配置）
    # 不需要额外操作，已经存在
    
    # 写回文件
    $pkgContent | ConvertTo-Json -Depth 10 | Set-Content $pkgPath -Encoding UTF8
    git -C $repoRoot add "TypeScriptPrj/package.json"
    Write-Host "  ✓ TypeScriptPrj/package.json 已更新" -ForegroundColor Green
}

# 从暂存区移除 turbo 文件（它们不应该出现在新提交中）
Write-Host "`n[6/6] 从暂存区移除 turbo 相关文件..." -ForegroundColor Yellow
$turboFiles = git -C $repoRoot diff --cached --name-only | Where-Object { $_ -like "TypeScriptPrj/.turbo/*" -or $_ -eq "TypeScriptPrj/turbo.json" }
foreach ($file in $turboFiles) {
    git -C $repoRoot rm --cached $file 2>$null
    Write-Host "  ✗ $file (从提交中移除)" -ForegroundColor Red
}

# 提交
Write-Host "`n=== 创建新提交 ===" -ForegroundColor Cyan
git -C $repoRoot commit -m "Revert turbo logic mistakenly included in $CommitHash

移除 TypeScript 项目中错误提交的 turbo 构建工具相关文件：
- TypeScriptPrj/turbo.json
- TypeScriptPrj/.turbo/cache/ (构建缓存)
- TypeScriptPrj/package.json (去掉 turbo scripts 和依赖，保留 pnpm 配置)

保留 pnpm 工作区配置：
- TypeScriptPrj/pnpm-lock.yaml
- TypeScriptPrj/pnpm-workspace.yaml
- TypeScriptPrj/package.json (保留 pnpm 相关配置)"

$newCommit = git -C $repoRoot rev-parse HEAD
Write-Host "新提交: $newCommit" -ForegroundColor Green

# 推送
if ($PushToRemote) {
    Write-Host "`n=== 推送到 $RemoteName/$BranchName ===" -ForegroundColor Cyan
    git -C $repoRoot push $RemoteName $BranchName
    Write-Host "推送成功！" -ForegroundColor Green
}

# 恢复 stash
$stashList = git -C $repoRoot stash list
if ($stashList) {
    Write-Host "`n恢复暂存的修改..." -ForegroundColor Yellow
    git -C $repoRoot stash pop
}

Write-Host "`n=== 完成 ===" -ForegroundColor Cyan
Write-Host "已从提交 $CommitHash 中移除 turbo 构建工具相关文件" -ForegroundColor Green
Write-Host "新提交哈希: $newCommit" -ForegroundColor Green

# 显示最终状态
git -C $repoRoot log --oneline -3
