$content = Get-Content 'd:\Project\Program\WorkFlowPrj\PowerShellScript\Md\ANALYSIS_REPORT.md' -Raw
$lines = $content -split "`r`n|`n"

Write-Host "=== MD038: 反引号内前后空格 ===" -ForegroundColor Cyan
for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    # 匹配 `` 内部有前导或后置空格
    if ($line -match '` [^`]+`' -or $line -match '`[^`]+ `') {
        Write-Host "  L$($i+1): $line"
    }
}

Write-Host "`n=== MD056: 表格列数不匹配 ===" -ForegroundColor Cyan
$inTable = $false
$expectedCols = 0
for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i].Trim()
    if ($line -match '^\|.*\|$') {
        if ($line -match '^\|[\s:-]+\|[\s:-]+\|') {
            $cols = ($line -split '\|' | Where-Object { $_ -match '\S' }).Count
            $expectedCols = $cols
            $inTable = $true
            continue
        }
        if ($inTable) {
            $cols = ($line -split '\|' | Where-Object { $_ -match '\S' }).Count
            if ($cols -ne $expectedCols) {
                Write-Host "  L$($i+1): 期望 $expectedCols 列, 实际 $cols 列 -> $line"
            }
        }
    } else {
        $inTable = $false
    }
}

Write-Host "`n=== MD012: 连续空行 ===" -ForegroundColor Cyan
$blankCount = 0
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Trim() -eq '') {
        $blankCount++
        if ($blankCount -gt 1) {
            Write-Host "  L$($i+1): 连续第 $blankCount 个空行"
        }
    } else {
        $blankCount = 0
    }
}
