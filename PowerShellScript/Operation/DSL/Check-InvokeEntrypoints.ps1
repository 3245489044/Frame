# Check-InvokeEntrypoints.ps1
# 入口框架守卫 —— 把 S1/S2 设计条款变成机器判据，防同类回归。
# 判据：
#   ① 4 入口不得出现数组 splatting 传开关（无 @($…Args…) / 无 += @('-）
#   ② GateManifests.psd1 每件 Args 须为 hashtable
#   ③ 每入口含 try + catch + 确定 exit
#   ④ 全 DSL 目录 Check-*.ps1 不得含硬编码绝对路径（正则 [a-zA-Z]:\\）
# 模式：-Check 时违规 exit 1；默认报告模式（不设退出码）。

param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# 定位仓库根（含 Projects\ 与 Frame\）
$__repo = $PSScriptRoot
while ($__repo -and $__repo -ne (Split-Path $__repo -Parent)) {
    if ((Test-Path (Join-Path $__repo 'Projects')) -and (Test-Path (Join-Path $__repo 'Frame'))) { break }
    $__repo = Split-Path $__repo -Parent
}

$invokeDir = Join-Path $__repo 'Frame\Invoke'
$manifestPath = Join-Path $PSScriptRoot 'GateManifests.psd1'
$violations = [System.Collections.Generic.List[string]]::new()

# ---- 判据 ①：入口不得用数组 splatting 传开关 ----
$entryScripts = @(
    'Search-Repo.ps1'
    'Test-JsonFiles.ps1'
    'Invoke-GateCheck.ps1'
    'Invoke-Regression.ps1'
)
foreach ($script in $entryScripts) {
    $path = Join-Path $invokeDir $script
    if (-not (Test-Path -LiteralPath $path)) {
        $violations.Add("[${script}] 入口脚本不存在: $path")
        continue
    }
    $content = Get-Content -LiteralPath $path -Raw
    # 检测数组 splatting 传开关模式：@('-$something') 或 += @('-
    if ($content -match "@\(\s*['""]-") {
        $violations.Add("[${script}] 检测到数组 splatting 传开关（@('...')），应改用哈希表 splatting")
    }
    if ($content -match "\+=\s*@?\s*\(\s*['""]-") {
        $violations.Add("[${script}] 检测到 += @('...') 数组拼接传开关，应改用哈希表")
    }
}

# ---- 判据 ②：GateManifests 每件 Args 须为 hashtable ----
if (Test-Path -LiteralPath $manifestPath) {
    $gates = Import-PowerShellDataFile -LiteralPath $manifestPath
    foreach ($name in $gates.Keys) {
        $spec = $gates[$name]
        $args = $spec.Args
        if ($null -ne $args -and -not ($args -is [hashtable])) {
            $violations.Add("[GateManifests:$name] Args 须为 hashtable，当前类型: $($args.GetType().Name)")
        }
    }
} else {
    $violations.Add("[GateManifests] 清单文件不存在: $manifestPath")
}

# ---- 判据 ③：每入口含 try + catch + exit ----
foreach ($script in $entryScripts) {
    $path = Join-Path $invokeDir $script
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $content = Get-Content -LiteralPath $path -Raw
    if ($content -notmatch '\btry\b') {
        $violations.Add("[${script}] 缺少 try 块（退出码契约要求 try/catch）")
    }
    if ($content -notmatch '\bcatch\b') {
        $violations.Add("[${script}] 缺少 catch 块（退出码契约要求 try/catch）")
    }
    if ($content -notmatch '\bexit\s+\$rc\b') {
        $violations.Add("[${script}] 缺少 exit `$rc（确定退出码契约）")
    }
}

# ---- 判据 ④：Check-*.ps1 不得含硬编码绝对路径 ----
$checkScripts = Get-ChildItem -Path $PSScriptRoot -Filter 'Check-*.ps1' -File
foreach ($f in $checkScripts) {
    $lines = Get-Content -LiteralPath $f.FullName
    $inBlockComment = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $trimmed = $line.TrimStart()
        # 跳过块注释 <# ... #> 与行注释 #（含 .EXAMPLE 用法示例）
        if ($inBlockComment) {
            if ($line -match '#>') { $inBlockComment = $false }
            continue
        }
        if ($trimmed.StartsWith('<#')) {
            if ($line -notmatch '#>') { $inBlockComment = $true }
            continue
        }
        if ($trimmed.StartsWith('#')) { continue }
        # 检测硬编码绝对路径：[a-zA-Z]:\
        if ($line -match '[a-zA-Z]:\\') {
            $violations.Add("[$($f.Name):$($i+1)] 硬编码绝对路径: $($line.Trim())")
        }
    }
}

# ---- 报告 ----
Write-Host "=== 入口框架守卫 ==="
Write-Host "检查项: ① 数组 splatting  ② GateManifests Args 类型  ③ try/catch/exit  ④ 硬编码路径"
Write-Host "违规数: $($violations.Count)"

if ($violations.Count -gt 0) {
    Write-Host "--- 违规清单 ---"
    foreach ($v in $violations) {
        Write-Host "  [FAIL] $v"
    }
    if ($Check) {
        Write-Host "校验失败：入口框架守卫发现 $($violations.Count) 项违规"
        exit 1
    }
    exit 0
}

Write-Host "  [OK] 入口框架守卫通过"
exit 0
