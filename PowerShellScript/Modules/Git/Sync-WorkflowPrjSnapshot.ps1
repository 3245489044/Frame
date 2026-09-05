<#
.SYNOPSIS
    将 Main 分支快照（排除指定目录）强制推送到 workflowprj 远程仓库

.DESCRIPTION
    采用"快照镜像"方式：从当前仓库 Main 分支的最新提交中导出全部文件，
    排除指定目录（默认 Projects），在隔离的临时目录中重新生成一个全新根提交，
    强制推送到 workflowprj 远程仓库的 main 分支。

    整个流程在临时目录内完成，不会改动当前仓库的任何分支引用、暂存区或工作区，
    也不会触碰 origin / github 远程。

    注意：该方式推送的历史与本地 Main 完全无关（无共同祖先），每次都会用新快照
    替换 gitee 上的历史，因此 gitee 上不累积提交历史。

.PARAMETER Repo
    需要同步的主仓库路径，默认取脚本所在仓库根目录。

.PARAMETER RemoteUrl
    目标远程仓库地址，默认 https://gitee.com/Ljy_111/workflowprj.git

.PARAMETER RemoteBranch
    目标远程分支名，默认 main

.PARAMETER ExcludeDirs
    需要排除的顶层目录名列表，默认仅排除 Projects

.PARAMETER DryRun
    模拟运行，生成快照但不上传（仍会创建临时目录并提交，随后被清理）

.PARAMETER SkipConfirm
    跳过推送前的确认交互（仅在显式传入时生效，默认需要确认）

.PARAMETER KeepTemp
    保留临时目录（调试用），默认会自动清理

.EXAMPLE
    .\Sync-WorkflowPrjSnapshot.ps1
    交互模式：生成快照、展示计划、确认后强制推送到 workflowprj

.EXAMPLE
    .\Sync-WorkflowPrjSnapshot.ps1 -DryRun
    只生成快照并展示将要上传的内容，不推送

.EXAMPLE
    .\Sync-WorkflowPrjSnapshot.ps1 -SkipConfirm
    免确认直接推送（快照内容仍会在生成后先行校验）
#>

[CmdletBinding()]
param(
    [string]$Repo,
    [string]$RemoteUrl = "https://gitee.com/Ljy_111/workflowprj.git",
    [string]$RemoteBranch = "main",
    [string[]]$ExcludeDirs = @("Projects"),
    [switch]$DryRun,
    [switch]$SkipConfirm,
    [switch]$KeepTemp
)

# ===== 颜色输出函数 =====
function Write-Info { Write-Host "[INFO] $($args[0])" -ForegroundColor Cyan }
function Write-Ok   { Write-Host "[OK]   $($args[0])" -ForegroundColor Green }
function Write-Warn { Write-Host "[WARN] $($args[0])" -ForegroundColor Yellow }
function Write-Err  { Write-Host "[ERR]  $($args[0])" -ForegroundColor Red }
function Write-Step { Write-Host "`n>>> $($args[0])" -ForegroundColor Magenta }

# ===== 主流程 =====
try {
    # 1. 解析仓库根路径
    if (-not $Repo) {
        $scriptPath = if ($PSCommandPath) { $PSCommandPath } else { $MyInvocation.MyCommand.Path }
        $scriptDir = Split-Path -Parent $scriptPath
        # 向上找到包含 .git 的目录
        $cur = $scriptDir
        while ($cur -and -not (Test-Path (Join-Path $cur ".git"))) {
            $cur = Split-Path -Parent $cur
        }
        if ($cur) { $Repo = $cur }
    }

    if (-not (Test-Path $Repo)) {
        Write-Err "仓库路径不存在: $Repo"
        exit 1
    }
    $repoGit = Join-Path $Repo ".git"
    if (-not (Test-Path $repoGit)) {
        Write-Err "不是 Git 仓库: $Repo"
        exit 1
    }

    Write-Host ""
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host "   WorkFlowPrj 快照镜像同步工具" -ForegroundColor Cyan
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Info "仓库: $Repo"
    Write-Info "目标: $RemoteUrl ($RemoteBranch)"
    Write-Info "排除目录: $($ExcludeDirs -join ', ')"

    # 2. 获取 Main 最新提交信息
    $mainHash = git -C $Repo rev-parse --short Main 2>&1
    if ($LASTEXITCODE -ne 0) { Write-Err "无法解析 Main 分支"; exit 1 }
    $mainSubject = (git -C $Repo log Main -1 --format="%s" 2>&1).Trim()
    Write-Info "基于 Main 最新提交: $mainHash $mainSubject"

    # 3. 创建隔离的临时目录
    $tmp = Join-Path $env:TEMP ("wfprj-snap-" + [guid]::NewGuid().ToString("N"))
    $src = Join-Path $tmp "src"
    New-Item -ItemType Directory $src -Force | Out-Null
    $snapTar = Join-Path $tmp "snap.tar"
    Write-Step "导出 Main 快照到隔离临时目录"

    # 4. 用 git archive 导出已提交文件快照（不依赖工作区，忽略未提交改动）
    git -C $Repo archive --format=tar --output=$snapTar Main . 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Err "导出归档失败"; exit 1 }

    tar -x -f $snapTar -C $src 2>&1
    if ($LASTEXITCODE -ne 0) { Write-Err "解压归档失败"; exit 1 }

    # 5. 排除指定目录并校验
    foreach ($dir in $ExcludeDirs) {
        $target = Join-Path $src $dir
        if (Test-Path $target) {
            Remove-Item $target -Recurse -Force
            Write-Info "已排除: $dir"
        }
    }
    $still = $ExcludeDirs | Where-Object { Test-Path (Join-Path $src $_) }
    if ($still) {
        Write-Err "排除指定目录失败，仍存在: $($still -join ', ')"
        exit 1
    }

    $fileCount = (Get-ChildItem -Recurse $src -File | Measure-Object).Count
    Write-Info "快照文件数: $fileCount"

    # 6. 在临时目录内初始化为全新提交
    Write-Step "生成全新根提交"
    git -C $src init -q -b $RemoteBranch 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Err "git init 失败"; exit 1 }
    git -C $src remote add workflowprj $RemoteUrl 2>&1 | Out-Null

    git -C $src add -A 2>&1 | Out-Null
    $commitMsg = "WorkFlow snapshot from $mainHash ($mainSubject) [exclude: $($ExcludeDirs -join ',')]"
    git -C $src commit -q -m $commitMsg 2>&1
    if ($LASTEXITCODE -ne 0) { Write-Err "生成快照提交失败"; exit 1 }

    $snapHash = (git -C $src rev-parse --short HEAD 2>&1).Trim()
    Write-Info "快照提交: $snapHash"

    # 7. 展示推送计划并确认
    Write-Step "推送计划"
    Write-Host "  远程: $RemoteUrl" -ForegroundColor Cyan
    Write-Host "  分支: $RemoteBranch (强制推送 --force)" -ForegroundColor Yellow
    Write-Host "  快照: $snapHash  ($commitMsg)" -ForegroundColor Cyan
    if ($DryRun) {
        Write-Step "模拟运行，不执行推送"
        exit 0
    }
    if (-not $SkipConfirm) {
        $confirm = Read-Host "`n将强制推送快照到 $RemoteBranch，覆盖其全部历史。确认? (y/n, 默认 n)"
        if ($confirm -ne 'y' -and $confirm -ne 'Y') {
            Write-Warn "已取消操作"
            exit 0
        }
    }

    # 8. 强制推送到 workflowprj
    Write-Step "开始推送 (--force)..."
    $output = git -C $src push --force workflowprj $RemoteBranch 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Ok "推送成功: $RemoteUrl $RemoteBranch <- $snapHash"
        $output | ForEach-Object { Write-Host "       $_" }
    }
    else {
        Write-Err "推送失败"
        $output | ForEach-Object { Write-Host "       $_" -ForegroundColor Red }
        exit 1
    }
}
finally {
    # 清理临时目录
    if ($tmp -and -not $KeepTemp) {
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
        Write-Info "已清理临时目录"
    }
    elseif ($tmp) {
        Write-Info "保留临时目录: $tmp"
    }
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "   完成" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green