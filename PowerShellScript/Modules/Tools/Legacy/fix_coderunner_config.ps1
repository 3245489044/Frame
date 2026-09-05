param(
    [string]$SettingsFile
)

# 读取 VS Code settings.json
# 使用 ConvertFrom-Json 后转为 Hashtable，避免 PSCustomObject 的成员访问问题
$json = Get-Content $SettingsFile -Raw | ConvertFrom-Json

# 确保 executorMap 存在
# 注意：ConvertFrom-Json 返回 PSCustomObject，需要用 PSObject.Properties 访问
$executorMap = $json.PSObject.Properties['code-runner.executorMap']
if ($null -eq $executorMap -or $null -eq $executorMap.Value) {
    # 使用 Add-Member 添加属性
    $json | Add-Member -MemberType NoteProperty -Name 'code-runner.executorMap' -Value ([PSCustomObject]@{}) -Force
}

# 使用 start 命令包装，让 Code Runner spawn 的进程立即返回
# 这样 Code Runner 不会检测到"Code Is Already Running"
# start 会在新窗口中执行 bat 文件，支持中文文件名
$executorMap = $json.PSObject.Properties['code-runner.executorMap'].Value
$batEntry = $executorMap.PSObject.Properties['bat']
if ($null -eq $batEntry -or $null -eq $batEntry.Value) {
    $executorMap | Add-Member -MemberType NoteProperty -Name 'bat' -Value 'start "Code Runner" cmd /c "$fullFileName"' -Force
} else {
    $executorMap.bat = 'start "Code Runner" cmd /c "$fullFileName"'
}

# 确保 runInTerminal 为 false
$runInTerminal = $json.PSObject.Properties['code-runner.runInTerminal']
if ($null -eq $runInTerminal -or $null -eq $runInTerminal.Value) {
    $json | Add-Member -MemberType NoteProperty -Name 'code-runner.runInTerminal' -Value $false -Force
} else {
    $json.'code-runner.runInTerminal' = $false
}

# ★ 写回文件：先转回 Hashtable 再序列化，避免 PSCustomObject 深层嵌套问题
# ConvertFrom-Json 返回的 PSCustomObject 在嵌套深度 > 4 时可能丢失数据
# 使用 Hashtable 中转确保所有层级都被正确序列化
$jsonHashtable = @{}
$json.PSObject.Properties | ForEach-Object { $jsonHashtable[$_.Name] = $_.Value }
$jsonHashtable | ConvertTo-Json -Depth 10 | Set-Content $SettingsFile -Encoding UTF8

Write-Host "[完成] Code Runner 配置已更新"
Write-Host "  bat 执行器: start `"Code Runner`" cmd /c `"`$fullFileName`""
Write-Host "  (start 立即返回，Code Runner 不会检测到进程仍在运行)"


