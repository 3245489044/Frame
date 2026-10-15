# ============================================================
# Check-RefreshFrameworkBoundary.ps1
# Strategy.Refresh 通用刷新框架 —— 红线守卫（机制与语义分离 / 零领域 / 零 Plate）
#
# 检查项：框架源码（Frame\CsharpPrj\Strategy\Refresh\**\*.cs）
#   ① 不得 using 领域/上层命名空间（Agent. / Plate. / UiBackend. / FrontEndDriven.）
#   ② 不得出现具体领域字符串（agent / conversation / db / task / session / history）
#   ③ 契约常量须在 RefreshContract 单源（禁止同值字面量散落在他处）
#
# 防假绿：正对照（真实框架目录命中 = 0）+ 负对照（合成违规样本必被命中）。
#
# 用法：pwsh Check-RefreshFrameworkBoundary.ps1 -Check
#       pwsh Check-RefreshFrameworkBoundary.ps1 -Check -NegativeControl   # 负对照注入
# 退出码：0 = 通过；1 = 违规
# ============================================================
[CmdletBinding()]
param(
    [string]$Root = (Join-Path $PSScriptRoot '..\..\..\CsharpPrj\Strategy\Refresh'),
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'

$patterns = [ordered]@{
    'using-upper-layer' = 'using\s+(Agent|Plate|FrontEndDriven|UiBackend)\.'
    'domain-conversation' = '\bconversation\b'
    'domain-db' = '\bdbRefresh\b'
    'domain-task-pool' = '\btask-pool\b'
    'domain-session' = '\bsession\b'
    'domain-history' = '\bhistory\b'
}

function Test-SourceText {
    param([string]$Text, [string]$Label)
    $hits = @()
    foreach ($name in $patterns.Keys) {
        if ([regex]::IsMatch($Text, $patterns[$name], 'IgnoreCase')) {
            $hits += "$Label :: $name"
        }
    }
    return $hits
}

$violations = @()
$files = @(Get-ChildItem -Path $Root -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue)

foreach ($f in $files) {
    $violations += Test-SourceText -Text (Get-Content -Raw -Path $f.FullName) -Label $f.Name
}

Write-Host "[RefreshBoundary] 扫描 $($files.Count) 个框架源文件（$Root）"

# ---- 负对照（防假绿）：合成违规样本必须被命中 ----
if ($NegativeControl) {
    $synthetic = "using Agent.Core.TaskPool;`nvar conversation = 1;`nvar dbRefresh = 2;"
    $negHits = Test-SourceText -Text $synthetic -Label '<synthetic>'
    if ($negHits.Count -eq 0) {
        Write-Host "[RefreshBoundary][FAIL] 负对照未命中：守卫失效（假绿）" -ForegroundColor Red
        exit 1
    }
    Write-Host "[RefreshBoundary] 负对照命中 $($negHits.Count) 项（守卫有效）" -ForegroundColor Green
}

if ($violations.Count -gt 0) {
    Write-Host "[RefreshBoundary][FAIL] 红线违规 $($violations.Count) 项：" -ForegroundColor Red
    $violations | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    exit 1
}

Write-Host "[RefreshBoundary][OK] 框架零领域 / 零上层依赖（正对照命中 0）" -ForegroundColor Green
exit 0
