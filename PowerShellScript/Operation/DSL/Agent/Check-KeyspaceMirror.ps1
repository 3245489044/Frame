# 键空间镜像门禁（E26）：仓库声明 vs Data 运行时声明（family 模板 + port 集合）。
# 口径：Data 缺失 = 干净 checkout，跳过并显式提示（不判红）；Data 存在则必须与仓库等价。
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$RequireData
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $probe = $PSScriptRoot
    while ($probe -and -not (Test-Path (Join-Path $probe 'Projects\Plug\Agent'))) { $probe = Split-Path $probe -Parent }
    if (-not $probe) { throw 'KeyspaceMirror gate：未找到仓库根（Projects\Plug\Agent）' }
    $Root = $probe
}

$repoPath = Join-Path $Root 'Projects\Plug\Agent\Config\keyspaces\agent.keyspace.json'
$dataPath = Join-Path $Root 'Data\Agent\Config\keyspaces\agent.keyspace.json'

if (-not (Test-Path -LiteralPath $repoPath)) {
    Write-Host "VIOLATION: 缺仓库 keyspace：$repoPath" -ForegroundColor Red
    exit 1
}
if (-not (Test-Path -LiteralPath $dataPath)) {
    Write-Host 'SKIP: Data keyspace 不存在（干净 checkout），跳过镜像比对。' -ForegroundColor Yellow
    if ($RequireData) { exit 1 }
    exit 0
}

function Read-Keyspace([string]$Path) {
    $json = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    $families = @{}
    foreach ($f in @($json.families)) {
        if ($null -ne $f -and -not [string]::IsNullOrWhiteSpace([string]$f.id)) {
            $families[[string]$f.id] = [string]$f.template
        }
    }
    $ports = @{}
    foreach ($p in @($json.ports)) {
        if ($null -ne $p -and -not [string]::IsNullOrWhiteSpace([string]$p.name)) {
            $ports[[string]$p.name] = [string]$p.direction
        }
    }
    return [PSCustomObject]@{ Families = $families; Ports = $ports }
}

$repoDoc = Read-Keyspace $repoPath
$dataDoc = Read-Keyspace $dataPath
$violations = New-Object System.Collections.Generic.List[string]

foreach ($id in $repoDoc.Families.Keys) {
    if (-not $dataDoc.Families.ContainsKey($id)) {
        $violations.Add("Data 缺 family：$id")
    } elseif ($dataDoc.Families[$id] -ne $repoDoc.Families[$id]) {
        $violations.Add("family 模板漂移：$id（repo=$($repoDoc.Families[$id]); data=$($dataDoc.Families[$id])）")
    }
}
foreach ($id in $dataDoc.Families.Keys) {
    if (-not $repoDoc.Families.ContainsKey($id)) {
        $violations.Add("Data 多出 family（仓库未声明）：$id")
    }
}
foreach ($name in $repoDoc.Ports.Keys) {
    if (-not $dataDoc.Ports.ContainsKey($name)) { $violations.Add("Data 缺 port：$name") }
}
foreach ($name in $dataDoc.Ports.Keys) {
    if (-not $repoDoc.Ports.ContainsKey($name)) { $violations.Add("Data 多出 port（仓库未声明）：$name") }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host "VIOLATION: $_" -ForegroundColor Red }
    if ($Check) { exit 1 }
    exit 0
}
Write-Host ("KeyspaceMirror gate OK（families={0}, ports={1}）" -f $repoDoc.Families.Count, $repoDoc.Ports.Count) -ForegroundColor Green
exit 0
