# ============================================================
# fix_duplicate_usings.ps1 — 修复重复的 using 语句
# ============================================================

param(
    [string]$ProjectRoot = "D:\Project\Program\WorkFlowPrj"
)

$files = @(
    "FileDriven\FileDriven.Execution\FileDrivenExecutor.cs",
    "FileDriven\FileDriven.Execution\ScriptExecutionModeGuard.cs",
    "FileDriven\FileDriven.Execution\WinFormsEventBinder.cs",
    "FileDriven\FileDriven.Execution\FileDrivenGuiInitializerOrchestrator.cs",
    "FileDriven\FileDriven.Execution\FrameworkInitializer.cs",
    "FileDriven\FileDriven.Execution\FrameworkShutdown.cs",
    "FileDriven\FileDriven.Execution\FileDrivenPipelineRunner.cs",
    "FileDriven\FileDriven.JsonRefer\GraphKeyFrameUsings.cs",
    "FileDriven\FileDriven.Logging\FileDrivenLogManager.cs",
    "FileDriven\FileDriven.Logging\Skeletons.cs",
    "FileDriven\FileDriven.UI\Core\MainForm.cs"
)

foreach ($relativePath in $files) {
    $fullPath = Join-Path $ProjectRoot $relativePath
    if (-not (Test-Path $fullPath)) {
        Write-Host "[SKIP] 文件不存在: $fullPath" -ForegroundColor Yellow
        continue
    }

    $content = Get-Content $fullPath -Raw
    $original = $content

    # 删除重复的 using FileDriven.X.Diagnostics; 语句（保留第一个）
    $content = $content -replace '(using FileDriven\.(UI|Execution|Logging|Pipeline|JsonRefer|Launcher|Business|Abstractions|Utilities)\.Diagnostics;\r?\n)(.*?\r?\n)*?\1', '$1'

    # 如果还有重复（比如连续出现多次），用循环清理
    $prevContent = ""
    while ($prevContent -ne $content) {
        $prevContent = $content
        $content = $content -replace '(using FileDriven\.(UI|Execution|Logging|Pipeline|JsonRefer|Launcher|Business|Abstractions|Utilities)\.Diagnostics;\r?\n)(.*?\r?\n)*?\1', '$1'
    }

    if ($content -ne $original) {
        Set-Content -Path $fullPath -Value $content -NoNewline
        Write-Host "[FIXED] $($relativePath)" -ForegroundColor Green
    } else {
        Write-Host "[OK] $($relativePath) - 无需修复" -ForegroundColor Gray
    }
}
