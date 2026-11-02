# 键空间镜像门禁（E26）：仓库声明 vs Data 运行时声明（family 模板 + port 集合）。
# 口径：Data 缺失 = 干净 checkout，跳过并显式提示（不判红）；Data 存在则必须与仓库等价。
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$RequireData
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) {
    $probe = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
    if (-not $probe) { throw 'KeyspaceMirror gate：未找到仓库根（Projects\Plug\Agent）' }
    $Root = $probe
}

# 仓库侧真源 = 键空间声明产物（agent.keyspace.generated.json；手写 agent.keyspace.json 已退役，
# 恒红缺件会掩盖真实漂移 ⇒ 载体指向实际产物）。
$repoPath = Join-Path $Root 'Projects\Plug\Agent\Config\keyspaces\agent.keyspace.generated.json'
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

$violations = New-Object System.Collections.Generic.List[string]


function Read-Keyspace([string]$Path) {
    $json = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    $families = @{}
    foreach ($f in @($json.families)) {
        if ($null -ne $f -and -not [string]::IsNullOrWhiteSpace([string]$f.id)) {
            $families[[string]$f.id] = [PSCustomObject]@{
                Template = [string]$f.template
                Business = [string]$f.businessKeyTemplate
                Derived  = (@($f.derivedPrefixes) -join ',')
            }
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

function Compare-Declarations([string]$Label, [string]$Path, $RepoDoc) {
    $doc = Read-Keyspace $Path
    foreach ($id in $RepoDoc.Families.Keys) {
        if (-not $doc.Families.ContainsKey($id)) {
            $violations.Add("${Label} 缺 family：$id")
            continue
        }
        $repo = $RepoDoc.Families[$id]
        $other = $doc.Families[$id]
        if ($other.Template -ne $repo.Template) {
            $violations.Add("${Label} family 模板漂移：$id（repo=$($repo.Template); $Label=$($other.Template)）")
        }
        if ($other.Business -ne $repo.Business) {
            $violations.Add("${Label} family 业务键模板漂移：$id（repo=$($repo.Business); $Label=$($other.Business)）")
        }
        if ($other.Derived -ne $repo.Derived) {
            $violations.Add("${Label} family 派生前缀漂移：$id（repo=$($repo.Derived); $Label=$($other.Derived)）")
        }
    }
    foreach ($id in $doc.Families.Keys) {
        if (-not $RepoDoc.Families.ContainsKey($id)) {
            $violations.Add("${Label} 多出 family（仓库未声明）：$id")
        }
    }
    foreach ($name in $RepoDoc.Ports.Keys) {
        if (-not $doc.Ports.ContainsKey($name)) { $violations.Add("${Label} 缺 port：$name") }
    }
    foreach ($name in $doc.Ports.Keys) {
        if (-not $RepoDoc.Ports.ContainsKey($name)) { $violations.Add("${Label} 多出 port（仓库未声明）：$name") }
    }
}

$repoDoc = Read-Keyspace $repoPath
Compare-Declarations -Label 'Data' -Path $dataPath -RepoDoc $repoDoc

# 代目录镜像（部署态）：current 指针存在则必须同步（否则「单测绿 / 运行红」的声明分叉不可见）。
$currentPtr = Join-Path $Root 'Runtime\Plugins\Agent\current'
if (Test-Path -LiteralPath $currentPtr) {
    $generation = (Get-Content -LiteralPath $currentPtr -Raw).Trim()
    if (-not [string]::IsNullOrWhiteSpace($generation)) {
        $genPath = Join-Path $Root "Runtime\Plugins\Agent\Generations\$generation\assets\Config\keyspaces\agent.keyspace.json"
        if (Test-Path -LiteralPath $genPath) {
            Compare-Declarations -Label "Gen($generation)" -Path $genPath -RepoDoc $repoDoc
        }
        else {
            $violations.Add("Gen($generation) 缺 keyspace 声明：$genPath")
        }
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host "VIOLATION: $_" -ForegroundColor Red }
    if ($Check) { exit 1 }
    exit 0
}
Write-Host ("KeyspaceMirror gate OK（families={0}, ports={1}）" -f $repoDoc.Families.Count, $repoDoc.Ports.Count) -ForegroundColor Green
exit 0
