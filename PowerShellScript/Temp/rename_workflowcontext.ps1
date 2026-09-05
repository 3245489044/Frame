$base = 'd:\Project\Program\WorkFlowPrj'
$files = @(
    'Operation\WorkFlowMethod\WorkFlowPipeline\WorkflowContextExtensions.cs',
    'Operation\WorkFlowMethod\WorkFlowPipeline\PipelineBuilder.cs',
    'Operation\WorkFlowMethod\WorkFlowPipeline\Adapters\WorkflowContextStateStoreAdapter.cs',
    'Operation\WorkFlowMethod\WorkFlowPipeline\Adapters\WorkflowPipelineContextAdapter.cs',
    'Operation\WorkFlowMethod\WorkFlowPipeline\Adapters\WorkflowStagePipeline.cs',
    'Operation\WorkFlowMethod\WorkFlowPipeline\Adapters\OrchestrationStageBridge.cs',
    'Operation\WorkFlowMethod\WorkFlowPipeline\Adapters\PipelineBuilderExtensions.cs',
    'Operation\WorkFlowMethod\NodeProcess\NodeProcessSkeleton.cs',
    'Operation\WorkFlowMethod\NodeProcess\NodeChainSkeleton.cs',
    'Operation\WorkFlowMethod\NodeProcess\Graph\GraphDefinitionStage.cs',
    'Operation\WorkFlowMethod\NodeProcess\Graph\GraphChainUpdateStage.cs',
    'Operation\WorkFlowMethod\NodeProcess\Lifecycle\NodeInitStage.cs',
    'Operation\WorkFlowMethod\NodeProcess\Lifecycle\NodeMetricsStage.cs',
    'Operation\WorkFlowMethod\NodeProcess\Execution\NodeLoopStage.cs',
    'Operation\WorkFlowMethod\NodeProcess\Execution\NodeErrorHandlerStage.cs',
    'Operation\WorkFlowMethod\NodeProcess\Adapters\NodeExtractorAdapter.cs',
    'Operation\WorkFlowMethod\Execution\OrchestrationBridge.cs'
)

foreach ($f in $files) {
    $path = Join-Path $base $f
    if (Test-Path $path) {
        $content = Get-Content $path -Raw
        $newContent = $content -replace 'WorkflowContext', 'PipelineContext'
        if ($newContent -ne $content) {
            Set-Content $path $newContent -NoNewline
            Write-Host "Updated: $f"
        } else {
            Write-Host "No changes: $f"
        }
    } else {
        Write-Host "Not found: $f"
    }
}
