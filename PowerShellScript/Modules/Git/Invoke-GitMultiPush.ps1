<#
.SYNOPSIS
    基于 JSON 配置的 Git 多远程仓库批量推送工具

.DESCRIPTION
    从 remote-config.json 读取远程仓库列表，逐个推送到所有启用的远程仓库。
    支持模拟运行、选择推送、强制推送、推送标签等功能。
    每个远程仓库可独立配置 localBranch 和 remoteBranch。

.PARAMETER ConfigPath
    JSON 配置文件路径，默认与脚本同目录下的 remote-config.json

.PARAMETER Branch
    指定推送的分支名称，覆盖配置文件中的默认值。
    如果配置了 localBranch:remoteBranch 格式（如 "Main:master"），则同时覆盖本地和远程分支。
    如果是纯分支名（如 "Main"），则同时覆盖本地和远程分支为同名。

.PARAMETER DryRun
    模拟运行，只显示将要执行的操作，不实际推送

.PARAMETER Force
    强制推送（git push --force）

.PARAMETER NoTags
    不推送标签（默认会推送标签）

.PARAMETER Auto
    自动模式，跳过远程仓库选择确认，直接推送所有启用的仓库

.PARAMETER Remote
    指定只推送特定的远程仓库（可指定多个，用逗号分隔）

.PARAMETER Commit
    自动提交所有更改（配合 -Auto 使用），需提供提交信息

.EXAMPLE
    .\Invoke-GitMultiPush.ps1
    交互式选择要推送的远程仓库

.EXAMPLE
    .\Invoke-GitMultiPush.ps1 -Auto
    自动推送所有启用的远程仓库

.EXAMPLE
    .\Invoke-GitMultiPush.ps1 -DryRun
    模拟运行，查看会推送什么

.EXAMPLE
    .\Invoke-GitMultiPush.ps1 -Branch "Main:master"
    用本地 Main 分支推送到远程 master 分支（覆盖所有配置）

.EXAMPLE
    .\Invoke-GitMultiPush.ps1 -Branch "feature"
    用本地 feature 分支推送到远程 feature 分支

.EXAMPLE
    .\Invoke-GitMultiPush.ps1 -Remote "github,origin"
    只推送到 github 和 origin

.EXAMPLE
    .\Invoke-GitMultiPush.ps1 -Auto -Commit "修复bug"
    自动提交所有更改并推送到所有仓库
#>

[CmdletBinding()]
param(
    [string]$ConfigPath,
    [string]$Branch,
    [switch]$DryRun,
    [switch]$Force,
    [switch]$NoTags,
    [switch]$Auto,
    [string]$Remote,
    [string]$Commit
)

# ===== 颜色输出函数 =====
function Write-Info  { Write-Host "[INFO] $($args[0])" -ForegroundColor Cyan }
function Write-Ok    { Write-Host "[OK]   $($args[0])" -ForegroundColor Green }
function Write-Warn  { Write-Host "[WARN] $($args[0])" -ForegroundColor Yellow }
function Write-Err   { Write-Host "[ERR]  $($args[0])" -ForegroundColor Red }
function Write-Step  { Write-Host "`n>>> $($args[0])" -ForegroundColor Magenta }

# ===== 辅助函数 =====
function Test-GitRepo {
    $gitDir = git rev-parse --git-dir 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Err "当前目录不是 Git 仓库"
        exit 1
    }
    Write-Info "Git 仓库: $(git rev-parse --show-toplevel 2>&1)"
}

function Get-CurrentBranch {
    $branch = git branch --show-current 2>&1
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($branch)) {
        return "master"
    }
    return $branch.Trim()
}

function Get-ConfigPath {
    if ($ConfigPath) {
        return $ConfigPath
    }
    $scriptPath = if ($PSCommandPath) { $PSCommandPath } else { $MyInvocation.MyCommand.Path }
    $scriptDir = Split-Path -Parent $scriptPath
    return Join-Path $scriptDir "remote-config.json"
}

function Read-Config {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        Write-Err "配置文件不存在: $Path"
        exit 1
    }

    try {
        $config = Get-Content $Path -Raw -Encoding UTF8 | ConvertFrom-Json
        return $config
    }
    catch {
        Write-Err "读取配置文件失败: $_"
        exit 1
    }
}

function Get-EnabledRemotes {
    param($Config)

    $remotes = $Config.remotes | Where-Object { $_.enabled -eq $true }

    if ($Remote) {
        $filterNames = $Remote.Split(',', [StringSplitOptions]::TrimEntries)
        $remotes = $remotes | Where-Object { $_.name -in $filterNames }
    }

    return $remotes
}

function Get-AllRemotes {
    param($Config)

    $remotes = $Config.remotes

    if ($Remote) {
        $filterNames = $Remote.Split(',', [StringSplitOptions]::TrimEntries)
        $remotes = $remotes | Where-Object { $_.name -in $filterNames }
    }

    return $remotes
}

<#
.SYNOPSIS
    获取推送规格，返回 @{ localBranch; remoteBranch; refspec } 哈希表

    优先级（从高到低）：
    1. -Branch 参数（支持 "local:remote" 或纯分支名格式）
    2. 远程仓库配置的 localBranch + remoteBranch
    3. 当前分支 + defaults.remoteBranch
#>
function Get-PushSpec {
    param($RemoteConfig, $GlobalDefaultRemoteBranch, $CurrentBranch)

    $localBranch = $CurrentBranch
    $remoteBranch = $GlobalDefaultRemoteBranch

    # 优先级 1: -Branch 命令行参数
    if ($Branch) {
        if ($Branch -match '^(.+):(.+)$') {
            # "local:remote" 格式
            $localBranch = $matches[1]
            $remoteBranch = $matches[2]
        }
        else {
            # 纯分支名，本地和远程都用同名
            $localBranch = $Branch
            $remoteBranch = $Branch
        }
    }
    else {
        # 优先级 2: 远程仓库独立配置
        if ($RemoteConfig.localBranch) {
            $localBranch = $RemoteConfig.localBranch
        }
        if ($RemoteConfig.remoteBranch) {
            $remoteBranch = $RemoteConfig.remoteBranch
        }
    }

    $refspec = "$localBranch`:$remoteBranch"

    return @{
        localBranch  = $localBranch
        remoteBranch = $remoteBranch
        refspec      = $refspec
    }
}

function Test-LocalBranchExists {
    param([string]$BranchName)

    $branches = git branch --format "%(refname:short)" 2>&1
    return $branches -contains $BranchName
}

function Ensure-RemoteConfigured {
    param($RemoteConfig)

    $existing = git remote 2>&1
    if ($existing -contains $RemoteConfig.name) {
        $currentUrl = git remote get-url $RemoteConfig.name 2>&1
        if ($currentUrl -ne $RemoteConfig.url) {
            Write-Warn "远程仓库 '$($RemoteConfig.name)' URL 不匹配:"
            Write-Warn "  当前: $currentUrl"
            Write-Warn "  配置: $($RemoteConfig.url)"
            Write-Warn "  将更新 URL..."
            git remote set-url $RemoteConfig.name $RemoteConfig.url 2>&1 | Out-Null
        }
        return $true
    }

    Write-Info "添加远程仓库 '$($RemoteConfig.name)' -> $($RemoteConfig.url)"
    git remote add $RemoteConfig.name $RemoteConfig.url 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Err "添加远程仓库 '$($RemoteConfig.name)' 失败"
        return $false
    }
    return $true
}

function Get-PushArgs {
    param($RemoteName, $Refspec)

    $argsList = @("push", $RemoteName, $Refspec)

    if ($Force) {
        $argsList += "--force"
    }

    if (-not $NoTags) {
        $argsList += "--tags"
    }

    if ($DryRun) {
        $argsList += "--dry-run"
    }

    return $argsList
}

function Invoke-PushToRemote {
    param($RemoteConfig, $PushSpec)

    $remoteName = $RemoteConfig.name
    $description = $RemoteConfig.description
    $localBranch = $PushSpec.localBranch
    $refspec = $PushSpec.refspec

    Write-Step "推送到 $remoteName ($description)..."
    Write-Host "       规格: $refspec"

    # 确保远程仓库已配置
    if (-not (Ensure-RemoteConfigured $RemoteConfig)) {
        return $false
    }

    # 检查本地分支是否存在
    if (-not (Test-LocalBranchExists $localBranch)) {
        Write-Err "本地分支 '$localBranch' 不存在，跳过推送"
        return $false
    }

    # 构建推送参数
    $pushArgs = Get-PushArgs -RemoteName $remoteName -Refspec $refspec

    # 执行推送
    $output = & "git" $pushArgs 2>&1

    if ($LASTEXITCODE -eq 0) {
        Write-Ok "$remoteName 推送成功"
        if ($output) {
            $output | ForEach-Object { Write-Host "       $_" }
        }
        return $true
    }
    else {
        Write-Err "$remoteName 推送失败"
        $output | ForEach-Object { Write-Host "       $_" -ForegroundColor Red }
        return $false
    }
}

function Show-PushSummary {
    param($Results)

    $successCount = @($Results | Where-Object { $_.success }).Count
    $failCount = @($Results | Where-Object { -not $_.success }).Count
    $totalCount = $Results.Count

    Write-Step "推送结果汇总"
    Write-Host "  成功: $successCount / $totalCount" -ForegroundColor Green
    if ($failCount -gt 0) {
        Write-Host "  失败: $failCount / $totalCount" -ForegroundColor Red
        Write-Host ""
        Write-Warn "失败的仓库:"
        $Results | Where-Object { -not $_.success } | ForEach-Object {
            Write-Host "  - $($_.name) ($($_.description))" -ForegroundColor Red
            Write-Host "    规格: $($_.refspec)" -ForegroundColor Red
            if ($_.reason) {
                Write-Host "    原因: $($_.reason)" -ForegroundColor Red
            }
        }
    }
}

# ===== 主流程 =====
function Main {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host "   Git 多远程仓库批量推送工具" -ForegroundColor Cyan
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host ""

    # 1. 检查 Git 仓库
    Test-GitRepo

    # 2. 读取配置
    $configPath = Get-ConfigPath
    Write-Info "配置文件: $configPath"
    $config = Read-Config $configPath

    # 3. 获取当前分支和默认值
    $currentBranch = Get-CurrentBranch
    $globalDefaultRemoteBranch = $config.defaults.remoteBranch
    Write-Info "当前分支: $currentBranch"
    Write-Info "默认远程分支: $globalDefaultRemoteBranch"

    # 4. 检查是否有未提交的更改
    $hasChanges = (git status --porcelain 2>&1) -ne ""
    if ($hasChanges) {
        if ($Commit) {
            Write-Step "自动提交所有更改..."
            git add -A 2>&1 | Out-Null
            git commit -m $Commit 2>&1
            if ($LASTEXITCODE -eq 0) {
                Write-Ok "提交成功: $Commit"
            }
            else {
                Write-Warn "提交失败或没有需要提交的更改"
            }
        }
        else {
            Write-Warn "存在未提交的更改:"
            git status --short 2>&1 | ForEach-Object { Write-Host "       $_" -ForegroundColor Yellow }
            Write-Host ""
            if (-not $Auto) {
                $confirm = Read-Host "是否添加所有更改并提交? (y/n, 默认 n)"
                if ($confirm -eq 'y' -or $confirm -eq 'Y') {
                    $msg = Read-Host "请输入提交信息"
                    if ($msg) {
                        git add -A 2>&1 | Out-Null
                        git commit -m $msg 2>&1
                        Write-Ok "提交成功"
                    }
                }
            }
        }
    }

    # 5. 获取要推送的远程仓库列表
    $remotes = if ($Auto) { Get-EnabledRemotes $config } else { Get-AllRemotes $config }

    if ($remotes.Count -eq 0) {
        Write-Warn "没有找到要推送的远程仓库"
        exit 0
    }

    # 6. 非自动模式下选择要推送的仓库
    $selectedRemotes = @()
    if (-not $Auto) {
        Write-Step "选择要推送的远程仓库"
        for ($i = 0; $i -lt $remotes.Count; $i++) {
            $r = $remotes[$i]
            $enabled = if ($r.enabled) { "启用" } else { "禁用" }
            $defaultChoice = if ($r.enabled) { "y" } else { "n" }
            $pushSpec = Get-PushSpec -RemoteConfig $r -GlobalDefaultRemoteBranch $globalDefaultRemoteBranch -CurrentBranch $currentBranch
            $prompt = "  [$($i+1)] $($r.name) $($pushSpec.refspec) - $($r.description) [$enabled] 推送? (y/n, 默认 $defaultChoice): "
            $choice = Read-Host $prompt
            if ([string]::IsNullOrWhiteSpace($choice)) {
                $choice = $defaultChoice
            }
            if ($choice -eq 'y' -or $choice -eq 'Y') {
                $selectedRemotes += $r
            }
        }
    }
    else {
        $selectedRemotes = $remotes
    }

    if ($selectedRemotes.Count -eq 0) {
        Write-Warn "未选择任何远程仓库，取消操作"
        exit 0
    }

    # 7. 显示推送计划
    Write-Step "推送计划"
    if ($Force) { Write-Host "  模式: 强制推送 (--force)" -ForegroundColor Yellow }
    if ($DryRun) { Write-Host "  模式: 模拟运行 (--dry-run)" -ForegroundColor Cyan }
    if ($NoTags) { Write-Host "  标签: 不推送标签" }
    else { Write-Host "  标签: 推送标签" }
    Write-Host "  远程仓库:"
    $selectedRemotes | ForEach-Object {
        $pushSpec = Get-PushSpec -RemoteConfig $_ -GlobalDefaultRemoteBranch $globalDefaultRemoteBranch -CurrentBranch $currentBranch
        Write-Host "    - $($_.name) $($pushSpec.refspec) ($($_.description))"
    }

    # 8. 非自动模式下确认
    if (-not $Auto -and -not $DryRun) {
        $confirm = Read-Host "`n确认执行推送? (y/n, 默认 n)"
        if ($confirm -ne 'y' -and $confirm -ne 'Y') {
            Write-Warn "已取消操作"
            exit 0
        }
    }

    # 9. 执行推送
    Write-Step "开始推送..."
    $results = @()
    foreach ($remote in $selectedRemotes) {
        $pushSpec = Get-PushSpec -RemoteConfig $remote -GlobalDefaultRemoteBranch $globalDefaultRemoteBranch -CurrentBranch $currentBranch
        $success = Invoke-PushToRemote -RemoteConfig $remote -PushSpec $pushSpec
        $results += [PSCustomObject]@{
            name        = $remote.name
            description = $remote.description
            refspec     = $pushSpec.refspec
            success     = $success
        }
    }

    # 10. 显示汇总
    Show-PushSummary -Results $results

    # 11. 最终状态
    Write-Host ""
    $failCount = @($results | Where-Object { -not $_.success }).Count
    if ($failCount -eq 0) {
        Write-Host "============================================" -ForegroundColor Green
        Write-Host "   ✓ 所有远程仓库推送完成!" -ForegroundColor Green
        Write-Host "============================================" -ForegroundColor Green
    }
    else {
        Write-Host "============================================" -ForegroundColor Yellow
        Write-Host "   ⚠ 有 $failCount 个远程仓库推送失败" -ForegroundColor Yellow
        Write-Host "============================================" -ForegroundColor Yellow
        exit 1
    }
}

# 执行主流程
Main
