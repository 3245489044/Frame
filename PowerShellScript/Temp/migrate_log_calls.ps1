# ============================================================
# migrate_log_calls.ps1 — 批量迁移 FileDrivenLogPlaceholderHandler.Log 调用
#
# 将 FileDrivenLogPlaceholderHandler.Log("xxx") 替换为
# 对应子项目的 XxxLog.Log("xxx")
#
# 映射规则（按文件路径）：
#   FileDriven/FileDriven.UI/*       → UILog.Log()
#   FileDriven/FileDriven.Execution/* → ExecutionLog.Log()
#   FileDriven/FileDriven.Logging/*   → LoggingLog.Log()
#   FileDriven/FileDriven.Pipeline/*  → PipelineLog.Log()
#   FileDriven/FileDriven.JsonRefer/* → JsonReferLog.Log()
#   FileDriven/FileDriven.Launcher/*  → LauncherLog.Log()
#   FileDriven/FileDriven.Business/*  → BusinessLog.Log()
#   FileDriven/FileDriven.Abstractions/* → AbstractionsLog.Log()
#   FileDriven/FileDriven.Utilities/* → UtilitiesLog.Log()
# ============================================================

param(
    [string]$ProjectRoot = "D:\Project\Program\WorkFlowPrj"
)

# 映射表：子项目目录 → (日志类名, 命名空间, using语句)
$moduleMap = @{
    "FileDriven\FileDriven.UI" = @{
        ClassName = "UILog"
        Namespace = "FileDriven.UI.Diagnostics"
        UsingStatement = "using FileDriven.UI.Diagnostics;"
    }
    "FileDriven\FileDriven.Execution" = @{
        ClassName = "ExecutionLog"
        Namespace = "FileDriven.Execution.Diagnostics"
        UsingStatement = "using FileDriven.Execution.Diagnostics;"
    }
    "FileDriven\FileDriven.Logging" = @{
        ClassName = "LoggingLog"
        Namespace = "FileDriven.Logging.Diagnostics"
        UsingStatement = "using FileDriven.Logging.Diagnostics;"
    }
    "FileDriven\FileDriven.Pipeline" = @{
        ClassName = "PipelineLog"
        Namespace = "FileDriven.Pipeline.Diagnostics"
        UsingStatement = "using FileDriven.Pipeline.Diagnostics;"
    }
    "FileDriven\FileDriven.JsonRefer" = @{
        ClassName = "JsonReferLog"
        Namespace = "FileDriven.JsonRefer.Diagnostics"
        UsingStatement = "using FileDriven.JsonRefer.Diagnostics;"
    }
    "FileDriven\FileDriven.Launcher" = @{
        ClassName = "LauncherLog"
        Namespace = "FileDriven.Launcher.Diagnostics"
        UsingStatement = "using FileDriven.Launcher.Diagnostics;"
    }
    "FileDriven\FileDriven.Business" = @{
        ClassName = "BusinessLog"
        Namespace = "FileDriven.Business.Diagnostics"
        UsingStatement = "using FileDriven.Business.Diagnostics;"
    }
    "FileDriven\FileDriven.Abstractions" = @{
        ClassName = "AbstractionsLog"
        Namespace = "FileDriven.Abstractions.Diagnostics"
        UsingStatement = "using FileDriven.Abstractions.Diagnostics;"
    }
    "FileDriven\FileDriven.Utilities" = @{
        ClassName = "UtilitiesLog"
        Namespace = "FileDriven.Utilities.Diagnostics"
        UsingStatement = "using FileDriven.Utilities.Diagnostics;"
    }
}

# 查找所有包含 FileDrivenLogPlaceholderHandler.Log 调用的 .cs 文件
Write-Host "=== 开始扫描文件 ===" -ForegroundColor Cyan

$files = Get-ChildItem -Path "$ProjectRoot\FileDriven" -Recurse -Filter "*.cs" | Where-Object {
    $_.FullName -notmatch "\\Diagnostics\\" -and
    $_.FullName -notmatch "\\obj\\" -and
    $_.FullName -notmatch "\\bin\\"
}

$totalFiles = 0
$totalReplacements = 0

foreach ($file in $files) {
    $content = Get-Content -Path $file.FullName -Raw
    if ($content -match "FileDrivenLogPlaceholderHandler\.Log\(") {
        # 确定所属子项目
        $matchedModule = $null
        foreach ($key in $moduleMap.Keys) {
            if ($file.FullName -match [regex]::Escape($key)) {
                $matchedModule = $moduleMap[$key]
                break
            }
        }

        if ($matchedModule -eq $null) {
            Write-Host "  [SKIP] 未匹配模块: $($file.FullName)" -ForegroundColor Yellow
            continue
        }

        # 统计替换次数
        $matches = [regex]::Matches($content, "FileDrivenLogPlaceholderHandler\.Log\(")
        $count = $matches.Count

        # 执行替换
        $newContent = $content -replace "FileDrivenLogPlaceholderHandler\.Log\(", "$($matchedModule.ClassName).Log("

        # 检查是否需要添加 using 语句
        $usingStmt = $matchedModule.UsingStatement
        if ($newContent -notmatch [regex]::Escape($usingStmt)) {
            # 在最后一个 using 语句后添加
            $newContent = $newContent -replace "(using [^;]+;\r?\n)(?!.*using [^;]+;)", "`$1$usingStmt`n"
        }

        # 写回文件
        Set-Content -Path $file.FullName -Value $newContent -NoNewline
        Write-Host "  [OK] $($file.Name): $count 处替换 → $($matchedModule.ClassName).Log()" -ForegroundColor Green
        $totalFiles++
        $totalReplacements += $count
    }
}

Write-Host "=== 完成 ===" -ForegroundColor Cyan
Write-Host "修改文件数: $totalFiles" -ForegroundColor Green
Write-Host "替换总数: $totalReplacements" -ForegroundColor Green
