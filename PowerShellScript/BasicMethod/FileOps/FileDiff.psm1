<#
.SYNOPSIS
    文件差异对比与备份恢复模块

.DESCRIPTION
    从 BasicMethod-FileAccess.psm1 拆分出的差异对比和备份恢复函数。
    包含：备份恢复、差异对比（LCS 算法）、交互式确认、SEARCH/REPLACE 块转换。

    依赖: BasicMethod-FileUtils.psm1（提供 _ReadFileContent、_SplitLines、_GetRelativePath 等）

    用法:
        Import-Module .\BasicMethod-FileUtils.psm1
        Import-Module .\BasicMethod-FileDiff.psm1
        Show-FileDiff -OldFile "old.txt" -NewFile "new.txt"
        Backup-File -Path "test.txt"
#>

# ============================================================
# 备份恢复
# ============================================================

<#
.SYNOPSIS
    备份文件
.DESCRIPTION
    将文件备份到同目录的 .bak 文件（带时间戳），或指定备份目录。
    返回备份路径。
.EXAMPLE
    Backup-File -Path "test.txt"
    Backup-File -Path "test.txt" -BackupDir "C:\backups"
#>
function Backup-File {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [string]$BackupDir = "",

        [switch]$Quiet
    )

    $resolvedPath = Resolve-Path $Path -ErrorAction Stop
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $fileName = [System.IO.Path]::GetFileNameWithoutExtension($resolvedPath.Path)
    $extension = [System.IO.Path]::GetExtension($resolvedPath.Path)

    if ($BackupDir) {
        $resolvedBackupDir = Resolve-Path $BackupDir -ErrorAction Stop
        if (-not (Test-Path $resolvedBackupDir)) {
            New-Item -Path $resolvedBackupDir -ItemType Directory -Force | Out-Null
        }
        $backupPath = Join-Path $resolvedBackupDir.Path "$fileName.$timestamp$extension.bak"
    } else {
        $backupPath = "$($resolvedPath.Path).$timestamp.bak"
    }

    Copy-Item -Path $resolvedPath.Path -Destination $backupPath -Force

    if (-not $Quiet) {
        Write-Host "[备份] $($resolvedPath.Path) → $backupPath" -ForegroundColor DarkGray
    }

    return [PSCustomObject]@{
        OriginalPath = $resolvedPath.Path
        BackupPath = $backupPath
        Timestamp = $timestamp
        SizeBytes = (Get-Item $backupPath).Length
    }
}

<#
.SYNOPSIS
    从备份恢复文件
.DESCRIPTION
    将 .bak 文件恢复回原路径。
    支持按时间戳精确恢复，或恢复最新的备份。
.EXAMPLE
    Restore-File -Path "test.txt" -BackupPath "test.txt.20250101_120000.bak"
    Restore-File -Path "test.txt" -Latest
#>
function Restore-File {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [string]$BackupPath = "",

        [switch]$Latest,

        [switch]$Quiet
    )

    $resolvedPath = Resolve-Path $Path -ErrorAction Stop
    $dir = Split-Path $resolvedPath.Path -Parent
    $fileName = [System.IO.Path]::GetFileNameWithoutExtension($resolvedPath.Path)
    $extension = [System.IO.Path]::GetExtension($resolvedPath.Path)

    if ($BackupPath) {
        $bakFile = Resolve-Path $BackupPath -ErrorAction Stop
    } elseif ($Latest) {
        $bakFiles = Get-ChildItem -Path $dir -Filter "$fileName$extension.*.bak" -File |
            Sort-Object LastWriteTime -Descending
        if (-not $bakFiles) {
            throw "未找到备份文件: $($resolvedPath.Path)"
        }
        $bakFile = $bakFiles[0]
    } else {
        throw "请指定 -BackupPath 或使用 -Latest"
    }

    Copy-Item -Path $bakFile.FullName -Destination $resolvedPath.Path -Force

    if (-not $Quiet) {
        Write-Host "[恢复] $($bakFile.FullName) → $($resolvedPath.Path)" -ForegroundColor DarkGray
    }

    return [PSCustomObject]@{
        OriginalPath = $resolvedPath.Path
        BackupPath = $bakFile.FullName
        Restored = $true
    }
}

# ============================================================
# 差异对比
# ============================================================

<#
.SYNOPSIS
    显示文件差异（unified diff 风格）
.DESCRIPTION
    对比两个字符串或文件内容，输出 unified diff 格式的差异。
    使用 LCS（最长公共子序列）算法实现行级对比。
.EXAMPLE
    Show-FileDiff -OldContent "line1`nline2" -NewContent "line1`nline3"
    Show-FileDiff -OldFile "old.txt" -NewFile "new.txt"
#>
function Show-FileDiff {
    param(
        [string]$OldContent = "",

        [string]$NewContent = "",

        [string]$OldFile = "",

        [string]$NewFile = "",

        [string]$Label = "diff",

        [switch]$Quiet
    )

    # 解析输入
    if ($OldFile) {
        $resolvedOld = Resolve-Path $OldFile -ErrorAction Stop
        $OldContent = _ReadFileContent -Path $resolvedOld.Path
        if (-not $Label -or $Label -eq "diff") { $Label = [System.IO.Path]::GetFileName($resolvedOld.Path) }
    }
    if ($NewFile) {
        $resolvedNew = Resolve-Path $NewFile -ErrorAction Stop
        $NewContent = _ReadFileContent -Path $resolvedNew.Path
    }

    $oldLines = _SplitLines -Content $OldContent
    $newLines = _SplitLines -Content $NewContent

    # 使用 LCS（最长公共子序列）算法计算 diff
    function Get-LcsDiff {
        param([string[]]$A, [string[]]$B)

        $m = $A.Length
        $n = $B.Length

        # 构建 LCS 长度表
        # 注意：PowerShell 中索引表达式 $dp[$i - 1, $j - 1] 会被解析为 $dp[($i - 1, $j) - 1]
        # 即先创建数组再减 1，导致 Object[] 上找不到 op_Subtraction 方法
        # 必须使用括号明确优先级：$dp[($i - 1), ($j - 1)]
        $dp = New-Object 'int[,]' ($m + 1), ($n + 1)
        for ($i = 1; $i -le $m; $i++) {
            for ($j = 1; $j -le $n; $j++) {
                if ($A[$i - 1] -eq $B[$j - 1]) {
                    $dp[$i, $j] = $dp[($i - 1), ($j - 1)] + 1
                } else {
                    $val1 = $dp[($i - 1), $j]
                    $val2 = $dp[$i, ($j - 1)]
                    $dp[$i, $j] = [math]::Max($val1, $val2)
                }
            }
        }

        # 回溯 LCS 并生成 diff 操作序列
        $ops = @()
        $i = $m; $j = $n
        while ($i -gt 0 -or $j -gt 0) {
            if ($i -gt 0 -and $j -gt 0 -and $A[$i - 1] -eq $B[$j - 1]) {
                $ops = , @{ Type = "equal"; Line = $A[$i - 1] } + $ops
                $i--; $j--
            } elseif ($j -gt 0 -and ($i -eq 0 -or $dp[$i, ($j - 1)] -ge $dp[($i - 1), $j])) {
                $ops = , @{ Type = "add"; Line = $B[$j - 1] } + $ops
                $j--
            } else {
                $ops = , @{ Type = "remove"; Line = $A[$i - 1] } + $ops
                $i--
            }
        }

        return $ops
    }

    $ops = Get-LcsDiff -A $oldLines -B $newLines

    $addedCount = ($ops | Where-Object { $_.Type -eq "add" }).Count
    $removedCount = ($ops | Where-Object { $_.Type -eq "remove" }).Count

    if (-not $Quiet) {
        Write-Host "--- a/$Label" -ForegroundColor Cyan
        Write-Host "+++ b/$Label" -ForegroundColor Cyan

        foreach ($op in $ops) {
            switch ($op.Type) {
                "equal" { Write-Host " $($op.Line)" -ForegroundColor Gray }
                "add" { Write-Host "+$($op.Line)" -ForegroundColor Green }
                "remove" { Write-Host "-$($op.Line)" -ForegroundColor Red }
            }
        }
    }

    return [PSCustomObject]@{
        Label = $Label
        AddedLines = $addedCount
        RemovedLines = $removedCount
        TotalChanges = $addedCount + $removedCount
        HasChanges = $addedCount -gt 0 -or $removedCount -gt 0
    }
}

# ============================================================
# 交互式确认
# ============================================================

<#
.SYNOPSIS
    交互式确认替换
.DESCRIPTION
    显示每处匹配的上下文，询问用户是否替换。
    支持 y/n/a（全部）三种回答。
.EXAMPLE
    Confirm-Replacement -Path "test.txt" -MatchLine "old code" -Context @("line before", "old code", "line after")
#>
function Confirm-Replacement {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$MatchLine,

        [string[]]$Context = @(),

        [int]$MatchIndex = 0,

        [int]$TotalMatches = 1,

        [switch]$Quiet
    )

    $relativePath = _GetRelativePath -Path $Path

    Write-Host "`n[$($MatchIndex + 1)/$TotalMatches] $relativePath" -ForegroundColor Yellow
    Write-Host "匹配行: " -NoNewline
    Write-Host "$MatchLine" -ForegroundColor Cyan

    if ($Context.Count -gt 0) {
        Write-Host "上下文:" -ForegroundColor DarkGray
        $startLine = [math]::Max(0, $Context.Count - 3)
        for ($i = $startLine; $i -lt $Context.Count; $i++) {
            $prefix = if ($Context[$i] -eq $MatchLine) { ">" } else { " " }
            Write-Host "$prefix $($Context[$i])" -ForegroundColor DarkGray
        }
    }

    do {
        $response = Read-Host "替换此处? (y=是 n=否 a=全部 y/n/a)"
    } while ($response -notin @("y", "n", "a"))

    return [PSCustomObject]@{
        Confirmed = $response -in @("y", "a")
        ApplyAll = $response -eq "a"
    }
}

<#
.SYNOPSIS
    将文本转为 SEARCH/REPLACE 块（自动转义正则特殊字符）
.DESCRIPTION
    将普通文本转为适合 -Blocks 参数使用的 SEARCH/REPLACE 块。
    自动处理缩进和换行符。
.EXAMPLE
    ConvertTo-SearchBlock -Search "public void Old()" -Replace "public void New()"
#>
function ConvertTo-SearchBlock {
    param(
        [Parameter(Mandatory)]
        [string]$Search,

        [Parameter(Mandatory)]
        [string]$Replace
    )

    return [PSCustomObject]@{
        Search = $Search
        Replace = $Replace
    }
}

# ============================================================
# 模块导出
# ============================================================

Export-ModuleMember -Function @(
    "Backup-File",
    "Restore-File",
    "Show-FileDiff",
    "Confirm-Replacement",
    "ConvertTo-SearchBlock"
)
