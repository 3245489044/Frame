$base = 'd:\Project\Program\WorkFlowPrj'
$files = @(
    'Operation\WorkFlowMethod\Orchestration\State\StateStoreAdapter.cs'
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
