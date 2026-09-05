# 从 filedriven_log_messages.json 中删除已迁移的消息，只保留 EventBinding 系列
param(
    [string]$ProjectRoot = "D:\Project\Program\WorkFlowPrj"
)

$sourceFile = "$ProjectRoot\FileDriven\filedriven_log_messages.json"
$sourceJson = Get-Content $sourceFile -Raw | ConvertFrom-Json

# 只保留 EventBinding/EventBinder/EventPipeline/EventTrigger 系列（属于 CsharpPrj）
$keepPrefixes = @('EventBinder_', 'EventBinding_', 'EventPipeline_', 'EventTrigger_')

$remainingMessages = @{}
$sourceJson.messages.PSObject.Properties | ForEach-Object {
    $keep = $false
    foreach ($prefix in $keepPrefixes) {
        if ($_.Name -like "$prefix*") {
            $keep = $true
            break
        }
    }
    if ($keep) {
        $remainingMessages[$_.Name] = $_.Value
    }
}

Write-Host "Original messages: $($sourceJson.messages.PSObject.Properties.Count)"
Write-Host "Remaining messages: $($remainingMessages.Count)"

# 构建输出 JSON
$sortedMessages = $remainingMessages.GetEnumerator() | Sort-Object Name | ForEach-Object -Begin {$h=@{}} -Process {$h[$_.Key]=$_.Value} -End {$h}
$output = @{
    version = "1.0"
    description = "FileDriven 日志消息定义（回退） - 仅保留 CsharpPrj 使用的 EventBinding 系列"
    messages = $sortedMessages
}

$finalJson = $output | ConvertTo-Json -Depth 5
Set-Content -Path $sourceFile -Value $finalJson -NoNewline
Write-Host "[DONE] Written to $sourceFile"
