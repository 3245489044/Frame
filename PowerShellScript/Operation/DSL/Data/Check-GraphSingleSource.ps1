# Check-GraphSingleSource.ps1
# 图写口单源判据（DP3 / DP7 / TH-12）：图事实只读端口 = 0 写成员；provenance 记录写入点 = 1。
# 结构事实载体：枚举指定源文件内的成员形态计数（非叙述、非执行者自评）。
# 用法：Check-GraphSingleSource.ps1 [-Check]
# 判据（-Check 时非零退出）：
#   ① IGraphStore.cs 中写形态成员（'void ' / 'set;' / 'Task '）计数 = 0（读端口无写口）
#   ② ProvenanceStore.cs 中追加写点 '_records.Add(' 计数 = 1（唯一写入口）
#   ③ LineageProjector.cs / GraphQuery.cs 中 'state' 写调用（Write/Set/Save）计数 = 0（图侧无独立写口）

[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$protocol = Join-Path $Root 'Frame\DataCenter\DataCenter.Protocol\Dataflow\IGraphStore.cs'
$coreDir = Join-Path $Root 'Frame\DataCenter\DataCenter.Core\Dataflow'
$provenance = Join-Path $coreDir 'ProvenanceStore.cs'
$projector = Join-Path $coreDir 'LineageProjector.cs'
$query = Join-Path $coreDir 'GraphQuery.cs'

foreach ($path in @($protocol, $provenance, $projector, $query)) {
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Host "缺件：$path（图内核未落地）"
        if ($Check) { exit 1 }
        exit 0
    }
}

$violations = [System.Collections.Generic.List[string]]::new()

$portText = Get-Content -LiteralPath $protocol -Raw
$writeMembers = ([regex]::Matches($portText, '(?m)^\s*(void|set;|Task)\s')).Count
if ($writeMembers -gt 0) {
    $violations.Add("IGraphStore 含写形态成员 $writeMembers 处（读端口必须 0 写口）")
}

$provText = Get-Content -LiteralPath $provenance -Raw
$appendPoints = ([regex]::Matches($provText, '_records\.Add\(')).Count
if ($appendPoints -ne 1) {
    $violations.Add("ProvenanceStore 追加写点 = $appendPoints（须恰为 1：唯一写入口）")
}

foreach ($path in @($projector, $query)) {
    $text = Get-Content -LiteralPath $path -Raw
    $stateWrites = ([regex]::Matches($text, '\b(Write|SetValue|Save|Persist)\s*\(')).Count
    if ($stateWrites -gt 0) {
        $violations.Add("$([System.IO.Path]::GetFileName($path)) 含状态写调用 $stateWrites 处（图侧不得写状态）")
    }
}

Write-Host ("Check-GraphSingleSource: 读端口写成员={0} | provenance 写点={1} | 违规={2}" -f $writeMembers, $appendPoints, $violations.Count)
if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host "  [违规] $_" }
    if ($Check) { exit 1 }
}

exit 0
