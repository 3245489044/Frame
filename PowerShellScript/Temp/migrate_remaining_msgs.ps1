# 迁移剩余 70 条消息到对应的子项目
param(
    [string]$ProjectRoot = "D:\Project\Program\WorkFlowPrj"
)

$sourceFile = "$ProjectRoot\FileDriven\filedriven_log_messages.json"
$sourceJson = Get-Content $sourceFile -Raw | ConvertFrom-Json

# 定义每个子项目应包含的消息前缀
$moduleMappings = @{
    "$ProjectRoot\FileDriven\FileDriven.Execution\Diagnostics\execution_log_messages.json" = @(
        'BuildParameters_', 'ConfigLoader_', 'ConfigLoadSkeleton_', 'ExecutionCenter_',
        'ExecutionReportLogSkeleton_', 'FrameworkError_', 'RuntimeContext_'
    )
    "$ProjectRoot\FileDriven\FileDriven.Pipeline\Diagnostics\pipeline_log_messages.json" = @(
        'RouterAdapterLogSkeleton_', 'RouterLogCommandLogSkeleton_', 'RouterNormalizeLogSkeleton_',
        'ScriptBuilder_', 'ScriptConverter_', 'ScriptConverterLogSkeleton_',
        'ScriptLoad_', 'ScriptMode_', 'ValueExtractionLogSkeleton_'
    )
    "$ProjectRoot\FileDriven\FileDriven.UI\Diagnostics\ui_log_messages.json" = @(
        'UICommandManager_'
    )
    "$ProjectRoot\FileDriven\FileDriven.JsonRefer\Diagnostics\jsonrefer_log_messages.json" = @(
        'WorkflowBuilder_'
    )
}

# EventBinding/EventBinder/EventPipeline/EventTrigger 系列属于 CsharpPrj，不迁移到 FileDriven 子项目
# 这些消息由 CsharpPrj 的 EventBindingOrchestrator 使用

$totalMigrated = 0
foreach ($targetFile in $moduleMappings.Keys) {
    $prefixes = $moduleMappings[$targetFile]
    
    # 提取匹配的消息
    $matchedMessages = @{}
    $sourceJson.messages.PSObject.Properties | Where-Object {
        $matched = $false
        foreach ($prefix in $prefixes) {
            if ($_.Name -like "$prefix*") {
                $matched = $true
                break
            }
        }
        $matched
    } | ForEach-Object {
        $matchedMessages[$_.Name] = $_.Value
    }
    
    if ($matchedMessages.Count -eq 0) {
        Write-Host "[SKIP] No matching messages for $targetFile"
        continue
    }
    
    # 读取现有目标文件
    $targetJson = Get-Content $targetFile -Raw | ConvertFrom-Json
    
    # 合并消息
    $mergedMessages = @{}
    $targetJson.messages.PSObject.Properties | ForEach-Object {
        $mergedMessages[$_.Name] = $_.Value
    }
    $matchedMessages.Keys | Sort-Object | ForEach-Object {
        $mergedMessages[$_] = $matchedMessages[$_]
    }
    
    Write-Host "[$($matchedMessages.Count) msgs] Merging into $targetFile"
    $totalMigrated += $matchedMessages.Count
    
    # 构建输出 JSON
    $sortedMessages = $mergedMessages.GetEnumerator() | Sort-Object Name | ForEach-Object -Begin {$h=@{}} -Process {$h[$_.Key]=$_.Value} -End {$h}
    $output = @{
        version = "1.0"
        description = $targetJson.description
        messages = $sortedMessages
    }
    
    $finalJson = $output | ConvertTo-Json -Depth 5
    Set-Content -Path $targetFile -Value $finalJson -NoNewline
}

Write-Host "[DONE] Total migrated: $totalMigrated"
