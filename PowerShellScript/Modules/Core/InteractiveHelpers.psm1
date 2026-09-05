# InteractiveHelpers.psm1 - 交互式确认工具模块
# 提供 Y/N/S 交互确认模式，适用于需要用户确认的脚本场景

<#
.SYNOPSIS
    通用确认删除函数：支持 Y（全部删除）/ N（跳过）/ S（逐项确认）
.DESCRIPTION
    列出所有要删除的目录及其大小，询问用户确认方式
.PARAMETER dirList
    要删除的目录路径数组
.PARAMETER promptMessage
    提示信息（可选，有默认值）
.PARAMETER showSize
    是否显示目录大小（可选，默认 $true）
.OUTPUTS
    [hashtable] 包含 deletedCount, failedCount, skippedCount 的哈希表
#>
function Confirm-And-DeleteDirectories {
    param(
        [string[]]$dirList,
        [string]$promptMessage = "确认删除以上目录？(Y/N/S=逐项确认)",
        [bool]$showSize = $true
    )
    if ($dirList.Count -eq 0) {
        Write-Host "  没有需要删除的目录。" -ForegroundColor Green
        return @{deletedCount=0; failedCount=0; skippedCount=0}
    }

    $totalSize = 0
    foreach ($dir in ($dirList | Sort-Object)) {
        if ($showSize) {
            $sizeMB = Get-DirectorySizeMB -dirPath $dir
            $totalSize += $sizeMB
            Write-Host "  [$sizeMB MB] $dir" -ForegroundColor Gray
        } else {
            Write-Host "  $dir" -ForegroundColor Gray
        }
    }
    if ($showSize) {
        Write-Host "  总计可释放空间: $totalSize MB" -ForegroundColor Yellow
    }
    Write-Host ""

    $confirm = Read-Host $promptMessage
    $deletedCount = 0
    $failedCount = 0
    $skippedCount = 0

    if ($confirm -eq 'Y' -or $confirm -eq 'y') {
        foreach ($dir in $dirList) {
            if (Test-Path $dir) {
                if (Remove-DirectoryWithLog -dirPath $dir) {
                    Write-Host "  [已删除] $dir" -ForegroundColor Green
                    $deletedCount++
                } else {
                    $failedCount++
                }
            }
        }
    } elseif ($confirm -eq 'S' -or $confirm -eq 's') {
        foreach ($dir in $dirList) {
            if (Test-Path $dir) {
                if ($showSize) {
                    $sizeMB = Get-DirectorySizeMB -dirPath $dir
                    $itemConfirm = Read-Host "  删除 [$sizeMB MB] $dir ? (Y/N)"
                } else {
                    $itemConfirm = Read-Host "  删除 $dir ? (Y/N)"
                }
                if ($itemConfirm -eq 'Y' -or $itemConfirm -eq 'y') {
                    if (Remove-DirectoryWithLog -dirPath $dir) {
                        Write-Host "    [已删除]" -ForegroundColor Green
                        $deletedCount++
                    } else {
                        $failedCount++
                    }
                } else {
                    Write-Host "    [已跳过]" -ForegroundColor Gray
                    $skippedCount++
                }
            }
        }
    } else {
        Write-Host "  已跳过删除。" -ForegroundColor Gray
        return @{deletedCount=0; failedCount=0; skippedCount=$dirList.Count}
    }

    Write-Host "  删除完成：成功 $deletedCount，失败 $failedCount，跳过 $skippedCount" -ForegroundColor Cyan
    return @{deletedCount=$deletedCount; failedCount=$failedCount; skippedCount=$skippedCount}
}

<#
.SYNOPSIS
    通用确认修复函数：列出项目并询问是否修复
.DESCRIPTION
    显示需要修复的项目列表，询问用户是否执行修复操作
.PARAMETER projectList
    项目描述字符串数组（用于显示）
.PARAMETER displayLabel
    显示标签（如"使用了无效 TargetFramework 的项目"）
.PARAMETER confirmMessage
    确认提示信息
.PARAMETER fixAction
    修复操作的脚本块
.OUTPUTS
    [bool] 执行了修复返回 $true，跳过返回 $false
#>
function Confirm-And-FixProjects {
    param(
        [string[]]$projectList,
        [string]$displayLabel,
        [string]$confirmMessage,
        [scriptblock]$fixAction
    )
    if ($projectList.Count -eq 0) {
        Write-Host "  未发现需要修复的 $displayLabel。" -ForegroundColor Green
        return $false
    }

    Write-Host "  发现 $($projectList.Count) 个 $displayLabel：" -ForegroundColor Yellow
    foreach ($p in $projectList) {
        Write-Host "    - $p" -ForegroundColor Magenta
    }
    Write-Host ""
    $confirm = Read-Host $confirmMessage
    if ($confirm -eq 'Y' -or $confirm -eq 'y') {
        & $fixAction
        return $true
    } else {
        Write-Host "  已跳过 $displayLabel 修复。" -ForegroundColor Gray
        return $false
    }
}

<#
.SYNOPSIS
    显示修复摘要
.DESCRIPTION
    以格式化方式显示修复操作的摘要信息
.PARAMETER summary
    修复摘要字符串数组
.PARAMETER backupDir
    备份目录路径（可选）
#>
function Show-FixSummary {
    param(
        [string[]]$summary,
        [string]$backupDir = ""
    )
    if ($summary.Count -gt 0) {
        Write-Host "  ─────────────────────────────────────────────" -ForegroundColor Cyan
        Write-Host "  修复摘要：" -ForegroundColor Cyan
        foreach ($s in $summary) {
            Write-Host "    ✓ $s" -ForegroundColor Green
        }
        if ($backupDir -ne "") {
            Write-Host "  备份目录: $backupDir" -ForegroundColor Gray
        }
        Write-Host "  ─────────────────────────────────────────────" -ForegroundColor Cyan
        Write-Host ""
    }
}

<#
.SYNOPSIS
    按文件路径对项目列表进行分组
.DESCRIPTION
    当多个操作项指向同一文件时，按文件路径分组以避免同一文件被多次处理。
    常用于修复操作中，当同一文件有多个节点需要处理时，确保每个文件只备份一次。
.PARAMETER items
    包含 File 属性的哈希表数组，每个哈希表应有 File 属性（System.IO.FileInfo 或包含 FullName 属性的对象）
.PARAMETER filePathProperty
    用于分组的文件路径属性名（可选，默认为 "File"）
.OUTPUTS
    [hashtable] 键为文件路径，值为包含 File 和 Nodes 属性的哈希表
.EXAMPLE
    $grouped = Group-ByFilePath -items $projectsWithIssues
    foreach ($key in $grouped.Keys) {
        $entry = $grouped[$key]
        # $entry.File - 文件对象
        # $entry.Nodes - 该文件对应的所有操作项
    }
#>
function Group-ByFilePath {
    param(
        [hashtable[]]$items,
        [string]$filePathProperty = "File"
    )
    $grouped = @{}
    foreach ($item in $items) {
        $fileObj = $item[$filePathProperty]
        $filePath = if ($fileObj -is [System.IO.FileInfo]) {
            $fileObj.FullName
        } elseif ($fileObj -is [string]) {
            $fileObj
        } else {
            $fileObj.FullName
        }

        if (-not $grouped.ContainsKey($filePath)) {
            $grouped[$filePath] = @{
                File = $fileObj
                Nodes = @()
            }
        }
        $grouped[$filePath].Nodes += $item
    }
    return $grouped
}

# 导出公共函数
Export-ModuleMember -Function Confirm-And-DeleteDirectories,
                              Confirm-And-FixProjects,
                              Show-FixSummary,
                              Group-ByFilePath
