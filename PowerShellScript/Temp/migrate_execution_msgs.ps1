# 迁移 Execution 模块消息 ID 到 execution_log_messages.json
param(
    [string]$ProjectRoot = "D:\Project\Program\WorkFlowPrj"
)

$sourceFile = Join-Path $ProjectRoot "FileDriven\filedriven_log_messages.json"
$targetFile = Join-Path $ProjectRoot "FileDriven\FileDriven.Execution\Diagnostics\execution_log_messages.json"

# 读取源文件
$sourceJson = Get-Content $sourceFile -Raw | ConvertFrom-Json

# 提取 Execution 模块的消息
$executionMessages = @{}
$sourceJson.messages.PSObject.Properties | Where-Object {
    $_.Name -match '^(Executor_|FrameworkInitializer_|FrameworkExecutor_|GuiInitializer_|GraphKeyFrameUsings_)'
} | ForEach-Object {
    $executionMessages[$_.Name] = $_.Value
}

Write-Host "Execution messages count: $($executionMessages.Count)"

# 读取现有目标文件
$targetJson = Get-Content $targetFile -Raw | ConvertFrom-Json

# 合并消息（保留现有，添加新的）
$mergedMessages = @{}
# 先添加现有的
$targetJson.messages.PSObject.Properties | ForEach-Object {
    $mergedMessages[$_.Name] = $_.Value
}
# 再添加新的（会覆盖同名的）
$executionMessages.Keys | Sort-Object | ForEach-Object {
    $mergedMessages[$_] = $executionMessages[$_]
}

Write-Host "Existing messages: $($targetJson.messages.PSObject.Properties.Count)"
Write-Host "Merged messages: $($mergedMessages.Count)"

# 构建输出 JSON
$output = @{
    version = "1.0"
    description = "FileDriven.Execution 日志消息定义 - 使用 ClassName_MethodName_description 命名"
    messages = $mergedMessages
}

# 转换为 JSON（按 key 排序）
$jsonString = $output | ConvertTo-Json -Depth 5
# 美化格式：确保 messages 内的 key 按字母排序
$jsonObj = $jsonString | ConvertFrom-Json
$sortedJson = @{}
$sortedJson["version"] = $jsonObj.version
$sortedJson["description"] = $jsonObj.description
$sortedJson["messages"] = $jsonObj.messages.PSObject.Properties | Sort-Object Name | ForEach-Object -Begin {$h=@{}} -Process {$h[$_.Name]=$_.Value} -End {$h}

$finalJson = $sortedJson | ConvertTo-Json -Depth 5
Set-Content -Path $targetFile -Value $finalJson -NoNewline
Write-Host "[DONE] Written to $targetFile"
