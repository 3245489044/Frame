$base = 'd:\Project\Program\WorkFlowPrj'

$files = @(
    'Operation\WorkFlowMethod\NodeProcess\Execution\NodeErrorHandlerStage.cs',
    'Operation\WorkFlowMethod\NodeProcess\Execution\NodeLoopStage.cs',
    'Operation\WorkFlowMethod\NodeProcess\Lifecycle\NodeMetricsStage.cs',
    'Operation\WorkFlowMethod\WorkFlowPipeline\Adapters\WorkflowPipelineContextAdapter.cs',
    'Operation\WorkFlowMethod\WorkFlowPipeline\Adapters\WorkflowStagePipeline.cs',
    'Operation\WorkFlowMethod\WorkFlowPipeline\Adapters\OrchestrationStageBridge.cs',
    'Operation\WorkFlowMethod\WorkFlowPipeline\Adapters\PipelineBuilderExtensions.cs',
    'Operation\WorkFlowMethod\Orchestration\State\PipelineContextAdapter.cs',
    'Operation\WorkFlowMethod\Orchestration\State\StageBridge.cs',
    'Operation\WorkFlowMethod\Orchestration\State\Stages\AdapterStage.cs',
    'Operation\WorkFlowMethod\Orchestration\State\Stages\LoopStage.cs',
    'Operation\WorkFlowMethod\Orchestration\State\Stages\ExecutionStage.cs',
    'Operation\WorkFlowMethod\Orchestration\State\Stages\ErrorHandlingStage.cs'
)

foreach ($f in $files) {
    $path = Join-Path $base $f
    if (Test-Path $path) {
        $content = Get-Content $path -Raw
        
        # 替换 PipelineContext 为 WfPipelineContext（但排除 using 别名行本身和注释中的引用）
        # 只替换代码中的 PipelineContext 引用（非 using 行、非注释行）
        $lines = $content -split "`n"
        $newLines = @()
        foreach ($line in $lines) {
            if ($line -match "using WfPipelineContext" -or $line -match "using Modules.Pipeline" -or $line -match "using WorkFlowMethod.CentralPipeline") {
                # 跳过 using 行
                $newLines += $line
            } elseif ($line -match "//.*PipelineContext" -or $line -match "///.*PipelineContext") {
                # 注释行 - 保留原样
                $newLines += $line
            } else {
                # 代码行 - 替换 PipelineContext 为 WfPipelineContext
                $newLine = $line -replace '(?<![a-zA-Z.])PipelineContext(?![a-zA-Z])', 'WfPipelineContext'
                $newLines += $newLine
            }
        }
        $newContent = $newLines -join "`n"
        if ($newContent -ne $content) {
            Set-Content $path $newContent -NoNewline
            Write-Host "Replaced refs: $f"
        } else {
            Write-Host "No changes: $f"
        }
    }
}
