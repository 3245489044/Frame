param(
    [string]$EntriesFile,
    [string]$OldWorkDir,
    [string]$NewWorkDir
)

$json = Get-Content $EntriesFile -Raw | ConvertFrom-Json
$changed = $false

foreach ($entry in $json.entries) {
    # 只处理 BatTool 目录下的 .bat 文件（target 不以 vscode-plugin-framework/ 开头）
    if ($entry.target -like '*.bat' -and $entry.target -notlike 'vscode-plugin-framework/*' -and $entry.workDir -eq $OldWorkDir) {
        $entry.workDir = $NewWorkDir
        $changed = $true
        Write-Host "  [更新] $($entry.name) -> workDir: $NewWorkDir"
    }
}

if ($changed) {
    $json | ConvertTo-Json -Depth 10 | Set-Content $EntriesFile -Encoding UTF8
    Write-Host '[完成] entries.config.json 已更新'
} else {
    Write-Host '[完成] 无需更新'
}
