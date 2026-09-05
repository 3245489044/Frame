# 检查 filedriven_log_messages.json 中哪些消息 ID 尚未迁移到子项目
param(
    [string]$ProjectRoot = "D:\Project\Program\WorkFlowPrj"
)

$sourceFile = "$ProjectRoot\FileDriven\filedriven_log_messages.json"
$json = Get-Content $sourceFile -Raw | ConvertFrom-Json
$allKeys = $json.messages.PSObject.Properties | ForEach-Object { $_.Name }

# 读取所有子项目消息
$subProjectFiles = @(
    "$ProjectRoot\FileDriven\FileDriven.Execution\Diagnostics\execution_log_messages.json",
    "$ProjectRoot\FileDriven\FileDriven.UI\Diagnostics\ui_log_messages.json",
    "$ProjectRoot\FileDriven\FileDriven.JsonRefer\Diagnostics\jsonrefer_log_messages.json",
    "$ProjectRoot\FileDriven\FileDriven.Logging\Diagnostics\logging_log_messages.json",
    "$ProjectRoot\FileDriven\FileDriven.Pipeline\Diagnostics\pipeline_log_messages.json",
    "$ProjectRoot\FileDriven\FileDriven.Launcher\Diagnostics\launcher_log_messages.json",
    "$ProjectRoot\FileDriven\FileDriven.Business\Diagnostics\business_log_messages.json",
    "$ProjectRoot\FileDriven\FileDriven.Abstractions\Diagnostics\abstractions_log_messages.json",
    "$ProjectRoot\FileDriven\FileDriven.Utilities\Diagnostics\utilities_log_messages.json"
)

$subKeys = @{}
foreach ($file in $subProjectFiles) {
    if (Test-Path $file) {
        $subJson = Get-Content $file -Raw | ConvertFrom-Json
        $subJson.messages.PSObject.Properties | ForEach-Object {
            $subKeys[$_.Name] = $true
        }
    } else {
        Write-Warning "File not found: $file"
    }
}

$notMigrated = $allKeys | Where-Object { -not $subKeys.ContainsKey($_) }
Write-Host "Total in filedriven_log_messages.json: $($allKeys.Count)"
Write-Host "Already in sub-projects: $($subKeys.Count)"
Write-Host "Not migrated yet: $($notMigrated.Count)"
$notMigrated | Sort-Object | ForEach-Object { Write-Host "  $_" }
