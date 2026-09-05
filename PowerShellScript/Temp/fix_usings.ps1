# 修复重复 using 语句的简单脚本
param([string]$ProjectRoot = "D:\Project\Program\WorkFlowPrj")

$filesToFix = @(
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

$patterns = @(
    "using FileDriven.UI.Diagnostics;",
    "using FileDriven.Execution.Diagnostics;",
    "using FileDriven.Logging.Diagnostics;",
    "using FileDriven.Pipeline.Diagnostics;",
    "using FileDriven.JsonRefer.Diagnostics;",
    "using FileDriven.Launcher.Diagnostics;",
    "using FileDriven.Business.Diagnostics;",
    "using FileDriven.Abstractions.Diagnostics;",
    "using FileDriven.Utilities.Diagnostics;"
)

foreach ($relPath in $filesToFix) {
    $fullPath = Join-Path $ProjectRoot $relPath
    if (-not (Test-Path $fullPath)) { continue }
    
    $content = Get-Content $fullPath -Raw
    $original = $content
    
    foreach ($pattern in $patterns) {
        $escaped = [regex]::Escape($pattern)
        # 只保留第一个匹配，删除后续所有重复
        $content = $content -replace "($escaped\r?\n)(.*?$escaped)", '$1'
        # 循环直到没有更多重复
        $prev = ""
        while ($prev -ne $content) {
            $prev = $content
            $content = $content -replace "($escaped\r?\n)(.*?$escaped)", '$1'
        }
    }
    
    if ($content -ne $original) {
        Set-Content -Path $fullPath -Value $content -NoNewline
        Write-Host "[FIXED] $relPath" -ForegroundColor Green
    } else {
        Write-Host "[OK] $relPath" -ForegroundColor Gray
    }
}
