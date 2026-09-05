param(
    [string]$EntriesFile,
    [string]$EntryName,
    [string]$EntryTarget,
    [string]$WorkDir
)

$json = Get-Content $EntriesFile -Raw | ConvertFrom-Json

$newEntry = @{
    name        = $EntryName
    description = 'BatTool 启动项'
    type        = 'bat'
    target      = $EntryTarget
    workDir     = $WorkDir
    plugin      = 'code-runner'
    newWindow   = $true
}

$json.entries += $newEntry
$json | ConvertTo-Json -Depth 10 | Set-Content $EntriesFile -Encoding UTF8
Write-Host "[完成] 已添加启动项: $EntryName"
