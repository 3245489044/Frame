<#
.SYNOPSIS
维护 SystemPrompt 与 .clinerules / .codex\rules 之间的目录 junction（目录联接）关系。

.DESCRIPTION
确保以下两个链接都指向源目录 SystemPrompt：
  .clinerules
  .codex\rules

脚本是幂等的：
  - 链接已正确指向源目录  -> 跳过，报告 OK
  - 链接不存在            -> 自动创建
  - 链接指向了其它位置     -> 删除后重建
  - 目标是真实目录        -> 默认拒绝替换，需 -Force 才允许备份后替换

.PARAMETER Source
内容源目录（相对脚本所在目录），默认 SystemPrompt。

.PARAMETER LinkTargets
需要指向 Source 的链接路径列表，默认 @(".clinerules", ".codex\rules")。

.PARAMETER Force
当目标位置是真实目录时，将其备份到 BackupDir 后替换为 junction。

.PARAMETER Check
仅检查并输出状态，不做任何修改。

.PARAMETER BackupDir
替换真实目录时的备份目录（相对脚本所在目录），默认 "junctions_backup"。

.EXAMPLE
.\Maintain-SystemPromptLinks.ps1 -Check
.EXAMPLE
.\Maintain-SystemPromptLinks.ps1 -Force
#>
[CmdletBinding()]
param(
    [string]$Source = "SystemPrompt",
    [string[]]$LinkTargets = @(".clinerules", ".codex\rules"),
    [switch]$Force,
    [switch]$Check,
    [string]$BackupDir = "junctions_backup"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Root)) { $Root = (Get-Location).Path }

function Resolve-RepoPath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    return [System.IO.Path]::GetFullPath((Join-Path $Root $Path))
}

$SourceFull = Resolve-RepoPath $Source
$BackupFull = Resolve-RepoPath $BackupDir

# ---- 校验源目录 ----
if (-not (Test-Path -LiteralPath $SourceFull -PathType Container)) {
    throw "源目录不存在: $SourceFull"
}
$SourceFull = [System.IO.Path]::GetFullPath($SourceFull).TrimEnd('\')

Write-Host ""
Write-Host "源目录 : $SourceFull" -ForegroundColor Cyan

# ---- 逐个处理链接 ----
foreach ($rel in $LinkTargets) {
    $targetFull = Resolve-RepoPath $rel
    Write-Host ""
    Write-Host "== 链接 : $rel" -ForegroundColor Cyan

    $item = $null
    if (Test-Path -LiteralPath $targetFull) {
        $item = Get-Item -LiteralPath $targetFull -Force
    }

    $isJunction = $item -and ($item.LinkType -eq "Junction")
    $pointsToSource = $isJunction -and
        ($item.Target -is [string] -and (([System.IO.Path]::GetFullPath($item.Target).TrimEnd('\')) -eq $SourceFull) -or
         ($item.Target -is [array] -and ($item.Target | ForEach-Object { [System.IO.Path]::GetFullPath($_).TrimEnd('\') }) -contains $SourceFull))

    if ($isJunction -and $pointsToSource) {
        Write-Host "  OK：已是正确的 junction，无需处理。" -ForegroundColor Green
        continue
    }

    if ($Check) {
        if (-not $item) {
            Write-Host "  待修复：链接不存在。" -ForegroundColor Yellow
        } elseif ($isJunction) {
            Write-Host "  待修复：junction 指向了其它位置 ($($item.Target -join '; '))。" -ForegroundColor Yellow
        } else {
            Write-Host "  待修复：存在真实目录（非链接）。" -ForegroundColor Yellow
        }
        continue
    }

    # ---- 执行修复 ----
    # 若目标真实目录（非链接）且未带 -Force，拒绝操作
    if ($item -and -not $isJunction -and -not $Force) {
        throw "目标位置存在真实目录: $targetFull`n请先手动处理，或使用 -Force 自动备份后替换。"
    }

    # 删除旧链接（junction 删除只移除链接本身，不会删除源内容）
    if ($item) {
        if ($isJunction) {
            Remove-Item -LiteralPath $targetFull -Force
            Write-Host "  已移除旧 junction。" -ForegroundColor Yellow
        } else {
            # 真实目录：备份后移除
            if (-not (Test-Path -LiteralPath $BackupFull -PathType Container)) {
                New-Item -ItemType Directory -Path $BackupFull | Out-Null
            }
            $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
            $name = Split-Path $targetFull -Leaf
            $dest = Join-Path $BackupFull ("{0}_backup_{1}" -f $name, $stamp)
            Move-Item -LiteralPath $targetFull -Destination $dest
            Write-Host "  已备份真实目录到: $dest" -ForegroundColor Yellow
        }
    }

    # 创建 junction（目标必须是绝对路径）
    New-Item -ItemType Junction -Path $targetFull -Target $SourceFull | Out-Null
    Write-Host "  已创建 junction: $targetFull -> $SourceFull" -ForegroundColor Green
}

Write-Host ""
Write-Host "完成。$(if ($Check) { '(检查模式，未做修改)' } else { '(已同步链接关系)' })" -ForegroundColor Cyan
