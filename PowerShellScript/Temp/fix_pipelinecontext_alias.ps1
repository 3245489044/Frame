$base = 'd:\Project\Program\WorkFlowPrj'

# 需要添加 using 别名的文件（同时引用 Modules.Pipeline 和 WorkFlowMethod.CentralPipeline）
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

$aliasLine = "using WfPipelineContext = WorkFlowMethod.CentralPipeline.PipelineContext;"

foreach ($f in $files) {
    $path = Join-Path $base $f
    if (Test-Path $path) {
        $content = Get-Content $path -Raw
        
        # 检查是否已存在别名
        if ($content -match "using WfPipelineContext") {
            Write-Host "Already has alias: $f"
            continue
        }
        
        # 在 #endregion 之后添加别名
        $newContent = $content -replace '(#endregion\r?\n)', "`$1`n$aliasLine`n"
        
        if ($newContent -ne $content) {
            Set-Content $path $newContent -NoNewline
            Write-Host "Added alias: $f"
        } else {
            Write-Host "Failed to add alias: $f"
        }
    } else {
        Write-Host "Not found: $f"
    }
}
