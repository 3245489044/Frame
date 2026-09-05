# FixRefactoringErrors.ps1
# 批量修复因删除旧 Obsolete 文件导致的编译错误
# 将旧命名空间引用替换为 Share/GraphFramework 泛型版本

param(
    [string]$ProjectRoot = "d:\Project\Program\WorkFlowPrj"
)

$WorkFlowMethodDir = Join-Path $ProjectRoot "CsharpPrj\Operation\WorkFlowMethod"

# ============================================================
# 批次1：替换 WorkFlowMethod.GraphFramework.Snapshots 命名空间
# ============================================================
Write-Host "=== 批次1：替换 Snapshots 命名空间 ===" -ForegroundColor Cyan

$snapshotsFiles = @(
    "CSG\Adapters\GraphSnapshotGraphAdapter.cs",
    "CSG\CsgIntegrationService.cs",
    "Execution\Adapters\ModelMapping.cs",
    "Execution\Engine\HotReloadService.cs",
    "Execution\Engine\SessionFactory.cs",
    "Execution\Session\ExecutionSession.cs",
    "GraphUsing\Services\GraphValidator.cs",
    "NodeProcess\Adapters\NodeExtractorAdapter.cs",
    "NodeProcess\Execution\NodeExecutionStage.cs",
    "NodeProcess\Execution\NodeExecutionStepSkeleton.cs",
    "NodeProcess\Graph\GraphChainUpdateStage.cs",
    "NodeProcess\Graph\GraphDefinitionStage.cs",
    "NodeProcess\NodeProcessPipelineBuilder.cs",
    "WorkFlowPipeline\PipelineContextAccessor.cs",
    "WorkFlowPipeline\PipelineContextExtensions.cs"
)

foreach ($relPath in $snapshotsFiles) {
    $fullPath = Join-Path $WorkFlowMethodDir $relPath
    if (-not (Test-Path $fullPath)) {
        Write-Host "  [跳过] $relPath (不存在)" -ForegroundColor Yellow
        continue
    }
    
    $content = Get-Content $fullPath -Raw
    
    # 替换 using 语句
    $content = $content -replace 'using WorkFlowMethod\.GraphFramework\.Snapshots;', 'using GraphFramework.Snapshots;'
    
    # 替换 GraphSnapshot 类型引用（非泛型 → 泛型）
    # 注意：有些地方是 GraphSnapshot 作为类型参数，有些是作为变量类型
    $content = $content -replace '(?<![a-zA-Z.])GraphSnapshot(?![a-zA-Z<])', 'GraphSnapshot<string, NodeConfig, object>'
    
    # 替换 GraphDiff 类型引用
    $content = $content -replace '(?<![a-zA-Z.])GraphDiff(?![a-zA-Z<])', 'GraphDiff<string, NodeConfig, object>'
    
    # 替换 GraphAdapter 类型引用
    $content = $content -replace '(?<![a-zA-Z.])GraphAdapter(?![a-zA-Z<])', 'GraphAdapter<string, NodeConfig, object>'
    
    # 特殊处理：ModelMapping.cs 中的 GetEntryNode() → GetEntryNodeKeys()
    if ($relPath -eq "Execution\Adapters\ModelMapping.cs") {
        $content = $content -replace 'snapshot\.GetEntryNode\(\)', 'snapshot.GetEntryNodeKeys()'
        $content = $content -replace 'entryNode\.NodeKey', 'entryNode'  # GetEntryNodeKeys() 返回 IReadOnlyList<TKey>
    }
    
    Set-Content $fullPath $content -NoNewline
    Write-Host "  [修复] $relPath" -ForegroundColor Green
}

# ============================================================
# 批次2：替换 ChainInfo 类型引用
# ============================================================
Write-Host "=== 批次2：替换 ChainInfo 类型 ===" -ForegroundColor Cyan

$chainInfoFiles = @(
    "Context\Abstractions\IRuntimeContext.cs",
    "Context\Runtime\WorkflowRuntimeContext.cs",
    "Context\Execution\WorkflowExecutionHelper.cs",
    "Execution\Builders\WorkflowTools.cs",
    "Execution\Chain\ChainMergeStrategy.cs",
    "Execution\Chain\WorkflowMediator.cs",
    "Execution\Chain\WorkflowRegistrar.cs",
    "Execution\Serialization\WorkflowDeserializer.cs",
    "GraphUsing\Deduplication\ConfigDeduplicationProcessor.cs",
    "GraphUsing\Deduplication\ConfigRelationHandler.cs",
    "GraphUsing\Pipeline\GraphWorkflowConnector.cs",
    "GraphUsing\Services\NodeChainService.cs",
    "NodeProcess\Abstractions\INodeChainService.cs",
    "NodeProcess\Abstractions\IWorkflowMediator.cs",
    "NodeProcess\KeyMapping\KeyProcessor.cs",
    "NodeProcess\Models\ConnectionModels.cs",
    "NodeProcess\Models\Dtos.cs",
    "NodeProcess\Storage.cs",
    "Orchestration\Validation.cs"
)

foreach ($relPath in $chainInfoFiles) {
    $fullPath = Join-Path $WorkFlowMethodDir $relPath
    if (-not (Test-Path $fullPath)) {
        Write-Host "  [跳过] $relPath (不存在)" -ForegroundColor Yellow
        continue
    }
    
    $content = Get-Content $fullPath -Raw
    
    # 添加 using GraphFramework.Models;（如果还没有）
    if ($content -notmatch 'using GraphFramework\.Models;') {
        $content = $content -replace '(using System;|using System\.Collections\.Generic;)', "`$1`r`nusing GraphFramework.Models;"
    }
    
    # 替换 ChainInfo<TNode> → Chain<string, TNode>
    $content = $content -replace 'ChainInfo<([^>]+)>', 'Chain<string, $1>'
    
    # 替换 ChainInfo（非泛型）→ Chain<string, NodeConfig>
    # 注意：避免重复替换已经处理过的 Chain<string, ...>
    $content = $content -replace '(?<![a-zA-Z.<])ChainInfo(?![a-zA-Z.<])', 'Chain<string, NodeConfig>'
    
    Set-Content $fullPath $content -NoNewline
    Write-Host "  [修复] $relPath" -ForegroundColor Green
}

# ============================================================
# 批次3：替换旧接口/类引用
# ============================================================
Write-Host "=== 批次3：替换旧接口/类引用 ===" -ForegroundColor Cyan

# 3.1 GraphChainUpdateStage.cs - GraphUpdateService → DefaultDirectiveApplier
$file = Join-Path $WorkFlowMethodDir "NodeProcess\Graph\GraphChainUpdateStage.cs"
if (Test-Path $file) {
    $content = Get-Content $file -Raw
    $content = $content -replace 'using WorkFlowMethod\.GraphFramework\.Snapshots;', 'using GraphFramework.Snapshots;'
    $content = $content -replace 'using WorkFlowMethod\.GraphFramework\.Services;', 'using GraphFramework.Directives;'
    $content = $content -replace 'GraphUpdateService', 'DefaultDirectiveApplier<string, NodeConfig, object>'
    Set-Content $file $content -NoNewline
    Write-Host "  [修复] NodeProcess\Graph\GraphChainUpdateStage.cs" -ForegroundColor Green
}

# 3.2 NodeProcessPipelineBuilder.cs - GraphUpdateService + INodeProcessor
$file = Join-Path $WorkFlowMethodDir "NodeProcess\NodeProcessPipelineBuilder.cs"
if (Test-Path $file) {
    $content = Get-Content $file -Raw
    $content = $content -replace 'using WorkFlowMethod\.GraphFramework\.Snapshots;', 'using GraphFramework.Snapshots;'
    $content = $content -replace 'using WorkFlowMethod\.GraphFramework\.Services;', 'using GraphFramework.Directives;'
    $content = $content -replace 'GraphUpdateService', 'DefaultDirectiveApplier<string, NodeConfig, object>'
    $content = $content -replace 'INodeProcessor(?!<)', 'GraphFramework.Execution.INodeProcessor<string, NodeConfig, object, PipelineContext>'
    Set-Content $file $content -NoNewline
    Write-Host "  [修复] NodeProcess\NodeProcessPipelineBuilder.cs" -ForegroundColor Green
}

# 3.3 WorkflowMediator.cs - IChainLinkProcessor
$file = Join-Path $WorkFlowMethodDir "Execution\Chain\WorkflowMediator.cs"
if (Test-Path $file) {
    $content = Get-Content $file -Raw
    $content = $content -replace 'IChainLinkProcessor', 'GraphFramework.Execution.IChainLinkProcessor<string, NodeConfig, object>'
    Set-Content $file $content -NoNewline
    Write-Host "  [修复] Execution\Chain\WorkflowMediator.cs" -ForegroundColor Green
}

# 3.4 NodeChainService.cs - IChainLinkProcessor
$file = Join-Path $WorkFlowMethodDir "GraphUsing\Services\NodeChainService.cs"
if (Test-Path $file) {
    $content = Get-Content $file -Raw
    $content = $content -replace 'IChainLinkProcessor', 'GraphFramework.Execution.IChainLinkProcessor<string, NodeConfig, object>'
    Set-Content $file $content -NoNewline
    Write-Host "  [修复] GraphUsing\Services\NodeChainService.cs" -ForegroundColor Green
}

# ============================================================
# 批次4：特殊文件处理
# ============================================================
Write-Host "=== 批次4：特殊文件处理 ===" -ForegroundColor Cyan

# 4.1 Orchestration\State\Stages\ErrorHandlingStage.cs
$file = Join-Path $WorkFlowMethodDir "Orchestration\State\Stages\ErrorHandlingStage.cs"
if (Test-Path $file) {
    $content = Get-Content $file -Raw
    $content = $content -replace 'WorkFlowMethod\.GraphFramework\.Snapshots\.GraphSnapshot', 'GraphFramework.Snapshots.GraphSnapshot<string, NodeConfig, object>'
    Set-Content $file $content -NoNewline
    Write-Host "  [修复] Orchestration\State\Stages\ErrorHandlingStage.cs" -ForegroundColor Green
}

# 4.2 Orchestration\State\Stages\ExecutionStage.cs
$file = Join-Path $WorkFlowMethodDir "Orchestration\State\Stages\ExecutionStage.cs"
if (Test-Path $file) {
    $content = Get-Content $file -Raw
    $content = $content -replace 'WorkFlowMethod\.GraphFramework\.Snapshots\.GraphSnapshot', 'GraphFramework.Snapshots.GraphSnapshot<string, NodeConfig, object>'
    Set-Content $file $content -NoNewline
    Write-Host "  [修复] Orchestration\State\Stages\ExecutionStage.cs" -ForegroundColor Green
}

# 4.3 CSG\CsgIntegrationService.cs - 特殊处理 OldChainInfo
$file = Join-Path $WorkFlowMethodDir "CSG\CsgIntegrationService.cs"
if (Test-Path $file) {
    $content = Get-Content $file -Raw
    # 替换 OldChainInfo using
    $content = $content -replace 'using OldChainInfo = WorkFlowMethod\.Skeletons\.NodeProcess\.Models\.ChainInfo<WorkFlowMethod\.Skeletons\.NodeProcess\.Models\.NodeConfig>;', 'using GraphFramework.Models;'
    # 替换 OldChainInfo 类型引用
    $content = $content -replace 'OldChainInfo', 'Chain<string, NodeConfig>'
    Set-Content $file $content -NoNewline
    Write-Host "  [修复] CSG\CsgIntegrationService.cs" -ForegroundColor Green
}

# 4.4 GraphUsing\Snapshots\GraphAdapter.cs - 特殊处理 NewChainInfo
$file = Join-Path $WorkFlowMethodDir "GraphUsing\Snapshots\GraphAdapter.cs"
if (Test-Path $file) {
    $content = Get-Content $file -Raw
    $content = $content -replace 'using NewChainInfo = WorkFlowMethod\.Skeletons\.NodeProcess\.Models\.ChainInfo<WorkFlowMethod\.Skeletons\.NodeProcess\.Models\.NodeConfig>;', 'using GraphFramework.Models;'
    $content = $content -replace 'NewChainInfo', 'Chain<string, NodeConfig>'
    Set-Content $file $content -NoNewline
    Write-Host "  [修复] GraphUsing\Snapshots\GraphAdapter.cs" -ForegroundColor Green
}

Write-Host ""
Write-Host "=== 修复完成 ===" -ForegroundColor Green
Write-Host "请运行 dotnet build 验证" -ForegroundColor Yellow
