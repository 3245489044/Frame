<#
.SYNOPSIS
  从产品项目 csproj 中批量移除指向 CsharpPrj 的 leaf 级 ProjectReference。
.DESCRIPTION
  方案B/聚合转发：产品项目改由产品级 Directory.Build.props 统一 ProjectReference 顶层聚合
  （BasicMethod/Modules/Share/Operation/Plate/Strategy），因此各 csproj 中逐个手抄的
  `$(CsharpProjectRoot)\...\X.csproj` 或相对 `..\..\CsharpPrj\...\X.csproj` leaf PR 变为冗余，
  需移除避免与聚合闭包重复引用。
  仅移除「单行 <ProjectReference Include="..." />」且路径含 CsharpPrj/$(CsharpProjectRoot)/$(CsharpRootDir) 的项。保留产品内部与指向 Agent 的 PR。
.PARAMETER Root
  待处理产品根目录（递归搜索 *.csproj）。
.PARAMETER WhatIf
  DryRun：仅打印将移除的行，不写盘。
.EXAMPLE
  .\Remove-CsharpPrjLeafProjectReferences.ps1 -Root D:\Project\Program\WorkFlowPrj\FileDriven -WhatIf
  .\Remove-CsharpPrjLeafProjectReferences.ps1 -Root D:\Project\Program\WorkFlowPrj\FileDriven
#>
param(
  [Parameter(Mandatory = $true)]
  [string]$Root,
  [string]$ExcludePattern = '',
  [switch]$WhatIf
)

# 子串排除清单：命中此类叶子路径的 ProjectReference 保留不删（如脱离聚合的 Operation\WdUI）。分号分隔。
$excluded = @($ExcludePattern -split ';' | Where-Object { $_ -ne '' })

# 匹配两种形式：$(...)MSBuild 属性 / 相对 ..\..\CsharpPrj\ 目录
$leafPattern = '<ProjectReference Include="[^"]*(?:CsharpPrj[\\/]|\$\(CsharpProjectRoot\)|\$\(CsharpRootDir\))[^"]*"\s*/>'

if (-not (Test-Path $Root)) { throw "Root not found: $Root" }

$files = Get-ChildItem -Path $Root -Recurse -Filter '*.csproj' -File | Where-Object { $_.Extension -eq '.csproj' }
$totalRemoved = 0

foreach ($file in $files) {
  $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
  $matches = [regex]::Matches($content, $leafPattern, [System.Text.RegularExpressions.RegexOptions]::Multiline)
  if ($matches.Count -eq 0) { continue }

  Write-Host "== $($file.FullName) : $($matches.Count) 处 leaf PR 待移除"

  # 按行删除（单行元素），保留其余内容与缩进
  $lines = $content -split "`r?`n"
  $kept = New-Object System.Collections.Generic.List[string]
  $removedInFile = 0
  foreach ($line in $lines) {
    if ($line -match $leafPattern) {
      # 命中排除子串的叶子 → 保留（该 leaf 脱离聚合，需显式声明）
      if ($excluded.Count -gt 0 -and ($excluded | Where-Object { $line -match [regex]::Escape($_) }).Count -gt 0) {
        $kept.Add($line)
        if ($WhatIf) { Write-Host ("   * 保留(排除): " + $line.Trim()) }
        continue
      }
      $removedInFile++
      if ($WhatIf) { Write-Host ("   - " + $line.Trim()) }
    }
    else {
      $kept.Add($line)
    }
  }

  $totalRemoved += $removedInFile

  if (-not $WhatIf -and $removedInFile -gt 0) {
    $newContent = $kept -join "`r`n"
    # 结尾统一单换行
    Set-Content -LiteralPath $file.FullName -Value ($newContent.TrimEnd("`r", "`n") + "`r`n") -Encoding UTF8 -NoNewline:$false
  }
}

Write-Host ""
if ($WhatIf) {
  Write-Host "DryRun 完成：共命中 $totalRemoved 处 leaf PR（未写盘）。"
}
else {
  Write-Host "完成：共移除 $totalRemoved 处 leaf PR。"
}