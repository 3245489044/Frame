<#
.SYNOPSIS
  删除仓库中的备份文件（如 *.csproj.before-csharpprj.*.bak）。
.DESCRIPTION
  递归查找并删除 Root 下的备份文件。默认匹配所有 *.bak；
  如需只删特定模式（如 csproj 备份），可用 -Pattern 覆盖。
.PARAMETER Root
  搜索根目录（默认仓库根 D:\Project\Program\WorkFlowPrj）。
.PARAMETER Pattern
  文件名匹配模式，默认 *.bak。
.PARAMETER WhatIf
  DryRun：仅列出将删除的文件，不真正删除。
.EXAMPLE
  .\Remove-BackupFiles.ps1 -Root D:\Project\Program\WorkFlowPrj -WhatIf
  .\Remove-BackupFiles.ps1 -Root D:\Project\Program\WorkFlowPrj
#>
param(
  [string]$Root = 'D:\Project\Program\WorkFlowPrj',
  [string]$Pattern = '*.bak',
  [switch]$WhatIf
)

if (-not (Test-Path $Root)) { throw "Root not found: $Root" }

$files = Get-ChildItem -Path $Root -Recurse -Filter $Pattern -File
if ($files.Count -eq 0) { Write-Host "未找到匹配 $Pattern 的备份文件。"; return }

Write-Host "共发现 $($files.Count) 个备份文件："
$files | ForEach-Object { Write-Host "   " $_.FullName }

if ($WhatIf) {
  Write-Host "`nDryRun：以上文件将被删除（未实际删除）。"
  return
}

$files | ForEach-Object {
  Remove-Item -LiteralPath $_.FullName -Force
  Write-Host "已删除: $($_.FullName)"
}
Write-Host "`n完成：共删除 $($files.Count) 个备份文件。"