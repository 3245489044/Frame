$base = 'd:\Project\Program\WorkFlowPrj'
$files = @(
    'Operation\WorkFlowMethod\Orchestration\State\PipelineContextAdapter.cs',
    'Operation\WorkFlowMethod\Orchestration\State\StageBridge.cs',
    'Operation\WorkFlowMethod\Orchestration\State\ContextExtensions.cs',
    'Operation\WorkFlowMethod\Orchestration\State\Stages\AdapterStage.cs',
    'Operation\WorkFlowMethod\Orchestration\State\Stages\ErrorHandlingStage.cs',
    'Operation\WorkFlowMethod\Orchestration\State\Stages\ExecutionStage.cs',
    'Operation\WorkFlowMethod\Orchestration\State\Stages\LoopStage.cs'
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
