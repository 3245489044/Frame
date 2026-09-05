<#
.SYNOPSIS
    删除本地除 Main 和 main 以外的所有分支
.DESCRIPTION
    遍历本地所有分支（除 Main 和 main 外），逐个提示确认后删除。
    支持 -Force 参数跳过确认直接删除。
.PARAMETER Force
    跳过确认提示，直接删除所有非 Main/main 分支
.EXAMPLE
    .\Script\Remove-BranchesExceptMain.ps1
    交互式删除，每个分支都会询问是否删除

    .\Script\Remove-BranchesExceptMain.ps1 -Force
    直接删除所有非 Main/main 分支，不提示
#>

param(
    [switch]$Force
)

# 切换到脚本所在的项目根目录（Script 的父目录）
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $projectRoot

# 获取当前分支名
$currentBranch = git branch --show-current
Write-Host "当前分支: $currentBranch" -ForegroundColor Cyan

# 获取所有本地分支（去掉 * 标记）
$branches = git branch | ForEach-Object { $_.Trim().TrimStart('*').Trim() }

# 要保留的分支
$keepBranches = @('Main', 'main')

$deletedCount = 0
$skippedCount = 0

foreach ($branch in $branches) {
    if ($branch -in $keepBranches) {
        Write-Host "  保留: $branch" -ForegroundColor Green
        continue
    }

    if ($branch -eq $currentBranch) {
        Write-Host "  跳过（当前所在分支）: $branch" -ForegroundColor Yellow
        $skippedCount++
        continue
    }

    if ($Force) {
        Write-Host "  删除: $branch" -ForegroundColor Red
        # ★ Bug 修复：不要使用管道到 Out-Null，否则 $LASTEXITCODE 会被重置为 0
        $output = git branch -d $branch 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host "    → 强制删除（未合并）: $branch" -ForegroundColor DarkRed
            $output = git branch -D $branch 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Host "    → 删除失败: $branch" -ForegroundColor Red
                $skippedCount++
                continue
            }
        }
        $deletedCount++
    }
    else {
        $choice = Read-Host "  删除分支 '$branch' ? (y/n, 默认 n)"
        if ($choice -eq 'y' -or $choice -eq 'Y') {
            # ★ Bug 修复：不要使用管道到 Out-Null，否则 $LASTEXITCODE 会被重置为 0
            $output = git branch -d $branch 2>&1
            if ($LASTEXITCODE -ne 0) {
                $forceChoice = Read-Host "    → 分支未合并，强制删除? (y/n, 默认 n)"
                if ($forceChoice -eq 'y' -or $forceChoice -eq 'Y') {
                    git branch -D $branch 2>&1 | Out-Null
                    Write-Host "    → 已强制删除: $branch" -ForegroundColor DarkRed
                }
                else {
                    Write-Host "    → 跳过: $branch" -ForegroundColor Yellow
                    $skippedCount++
                    continue
                }
            }
            else {
                Write-Host "    → 已删除: $branch" -ForegroundColor Red
            }
            $deletedCount++
        }
        else {
            Write-Host "    → 跳过: $branch" -ForegroundColor Yellow
            $skippedCount++
        }
    }
}

Write-Host "`n===== 完成 =====" -ForegroundColor Cyan
Write-Host "删除: $deletedCount 个分支" -ForegroundColor Red
Write-Host "跳过: $skippedCount 个分支" -ForegroundColor Yellow
Write-Host "保留: Main, main" -ForegroundColor Green
