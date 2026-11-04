#requires -Version 5.1
<#
.SYNOPSIS
    伞工程解决方案守卫（SolutionUmbrella）—— MainProj.sln 只登记根伞工程，叶子经 ProjectReference 引入。

.DESCRIPTION
    背景：Modules\Gather、Modules\Concurrency 等叶子工程曾被直接挂进 MainProj.sln，并附带
    Solution Folder（Modules.Gather）与 NestedProjects。叶子直挂 = 烘焙根重复、单源清单二元化，
    且下次同步/VS「添加现有项目」会持续复制该形态。本件把「一 sln 一伞」变成 fail-closed 判据。

    判据：
      S1 伞齐全：modules.json roots 推导出的根伞工程必须全部登记（缺 ⇒ 该根整片叶子不进烘焙）。
      S2 禁叶子：sln 的 C# 工程必须 ⊆ 根伞集合；违规则给出归属根与「应经 <root>\<root>.csproj 引入」提示。
      S3 禁解决方案文件夹：任何 {2150E333-...} 条目即红。
      S4 禁遗留嵌套：NestedProjects 存在非空行即红（空段容忍）。
      S5 悬挂/重复：登记的 csproj 必须存在于磁盘；同一 GUID / 同一路径不得重复登记。
      S6 配置双向一致：每条 ProjectConfigurationPlatforms 的 GUID 必须属于已登记工程（无孤儿配置）；
         每个 C# 工程至少一条 ActiveCfg（否则 dotnet build sln 会静默跳过该工程）。
      对照：合成「合法 sln」必 0 违规；合成「叶子直挂 + 文件夹 + 嵌套」必被捕获（防判据空转）。

    伞集合推导单源 = Share\Solution\ModulePaths.Common.psm1 的 Get-UmbrellaProjects
    （根目录直属 csproj ∧ 该根目录内无独立 *.sln；Protocol 因自带 Protocol.sln 不并入 MainProj）。

.PARAMETER Root
    仓库超根（默认由本脚本位置上溯；与目录层级解耦）。
.PARAMETER SolutionPath
    被测 .sln（默认 Frame\CsharpPrj\MainProj.sln）。
.PARAMETER Check
    校验模式：违规 → 退出码 1。
.PARAMETER ShowDetail
    输出伞集合与逐工程实测。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [string]$SolutionPath = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/裁决单源；逐级上溯至同时含 Frame\ 与 Projects\ 的目录）
$runtimeRoot = $PSScriptRoot
while ($runtimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $runtimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $runtimeRoot 'Projects')))) {
    $runtimeRoot = Split-Path -Parent $runtimeRoot
}
if (-not $runtimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $runtimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $runtimeRoot 'Frame\PowerShellScript\Share\Solution\ModulePaths.Common.psm1') -Force -DisableNameChecking

$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Detail:$ShowDetail -Key 'SolutionUmbrella'
$Root = $ctx.Root
$violations = $ctx.Violations

Write-Host ''
Write-Host '============================================================'
Write-Host '  伞工程解决方案守卫（SolutionUmbrella）'
Write-Host '============================================================'

$csharpPrj = Join-Path $Root 'Frame\CsharpPrj'
$manifestPath = Join-Path $csharpPrj 'modules.json'
if ([string]::IsNullOrWhiteSpace($SolutionPath)) { $SolutionPath = Join-Path $csharpPrj 'MainProj.sln' }

Assert-GatePrerequisite -Path $manifestPath -Label '模块清单 modules.json' -Key 'SolutionUmbrella'
Assert-GatePrerequisite -Path $SolutionPath -Label '解决方案文件' -Key 'SolutionUmbrella'

$manifest = Read-ModuleManifest -Path $manifestPath

# ---- 伞集合（单源推导）----
$umbrellaSet = @{}          # 绝对路径(小写) -> 伞对象
foreach ($u in (Get-UmbrellaProjects -CsharpPrjRoot $csharpPrj -Manifest $manifest)) {
    $umbrellaSet[$u.FullPath.ToLowerInvariant()] = $u
}
if ($umbrellaSet.Count -eq 0) {
    Write-Host '  [FAIL] 伞集合为空（modules.json roots 下无根伞 csproj）⇒ 判据无标的，禁假绿' -ForegroundColor Yellow
    exit 2
}

# ---- 归属根映射（叶子违规时给出正确引入点）----
$ownerMap = @{}
foreach ($p in $manifest.projects.PSObject.Properties) {
    $abs = [System.IO.Path]::GetFullPath((Join-Path $csharpPrj ([string]$p.Value))).ToLowerInvariant()
    $seg = ([string]$p.Value -split '[\\/]')[0]
    $ownerMap[$abs] = $seg
}

$folderTypeGuid = '{2150E333-8FDC-42A3-9474-1A3956D46DE8}'
# C# 工程类型 GUID：经典（FAE04EC0）与 SDK 风格（9A19103F，VS 新版会写这个）
$csharpTypeGuids = @('{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}', '{9A19103F-16F7-4668-BE54-9A1E7A4F7556}')

<#
.SYNOPSIS
    对给定 sln 行做 S1–S6 判定（真实文件与合成对照共用同一判据）。
#>
function Get-SolutionUmbrellaViolations {
    param(
        [AllowEmptyCollection()][string[]]$Lines,
        [Parameter(Mandatory)][string]$SolutionDirectory,
        [Parameter(Mandatory)][hashtable]$Umbrellas,
        [hashtable]$Owners = @{},
        [bool]$CheckFiles = $true
    )

    $issues = [System.Collections.Generic.List[string]]::new()
    $projectRx = [regex]'^Project\("(?<TypeGuid>[^"]+)"\)\s*=\s*"(?<Name>[^"]+)",\s*"(?<Path>[^"]+)",\s*"(?<Guid>[^"]+)"'
    $configRx = [regex]'^\{(?<G>[^}]+)\}\.(?<Cfg>[^.]*)\.(?<Kind>ActiveCfg|Build\.0)'

    $projects = [System.Collections.Generic.List[object]]::new()
    $folders = [System.Collections.Generic.List[object]]::new()
    $nested = [System.Collections.Generic.List[string]]::new()
    $seenGuid = @{}
    $seenPath = @{}
    $configGuids = @{}
    $activeCfgGuids = @{}

    $inNested = $false
    $inProjectConfig = $false

    foreach ($line in $Lines) {
        $m = $projectRx.Match($line)
        if ($m.Success) {
            $item = [pscustomobject]@{
                Name     = $m.Groups['Name'].Value
                Path     = $m.Groups['Path'].Value
                Guid     = '{' + $m.Groups['Guid'].Value.Trim('{', '}').ToUpperInvariant() + '}'
                TypeGuid = $m.Groups['TypeGuid'].Value.ToUpperInvariant()
            }
            if ($item.TypeGuid -eq $folderTypeGuid) { $folders.Add($item) } else { $projects.Add($item) }
            continue
        }

        $trim = $line.Trim()
        if ($trim -match '^GlobalSection\(NestedProjects\)') { $inNested = $true; continue }
        if ($inNested) {
            if ($trim -match '^EndGlobalSection') { $inNested = $false }
            elseif ($trim -ne '') { $nested.Add($trim) }
            continue
        }

        if ($trim -match '^GlobalSection\(ProjectConfigurationPlatforms\)') { $inProjectConfig = $true; continue }
        if ($inProjectConfig) {
            if ($trim -match '^EndGlobalSection') { $inProjectConfig = $false; continue }
            $cm = $configRx.Match($trim)
            if ($cm.Success) {
                $g = '{' + $cm.Groups['G'].Value.ToUpperInvariant() + '}'
                $configGuids[$g] = $true
                if ($cm.Groups['Kind'].Value -eq 'ActiveCfg') { $activeCfgGuids[$g] = $true }
            }
        }
    }

    # ---- S3 禁解决方案文件夹 ----
    foreach ($f in $folders) {
        $issues.Add("[S3] 解决方案文件夹 '$($f.Name)'（$folderTypeGuid）不得出现在伞工程 sln（真实目录层级由 VS 呈现）")
    }

    # ---- S4 禁遗留嵌套 ----
    if ($nested.Count -gt 0) {
        $issues.Add("[S4] NestedProjects 残留 $($nested.Count) 条（文件夹已删除时嵌套关系必须一并清除）")
    }

    $csharpPaths = @{}
    foreach ($p in $projects) {
        $full = [System.IO.Path]::GetFullPath((Join-Path $SolutionDirectory $p.Path))
        $key = $full.ToLowerInvariant()

        # ---- S5 重复/悬挂 ----
        if ($seenGuid.ContainsKey($p.Guid)) { $issues.Add("[S5] 工程 GUID 重复登记：$($p.Guid)（$($p.Name)）") }
        $seenGuid[$p.Guid] = $true
        if ($seenPath.ContainsKey($key)) { $issues.Add("[S5] 同一工程重复登记：$($p.Path)") }
        $seenPath[$key] = $true
        if ($CheckFiles -and -not (Test-Path -LiteralPath $full)) {
            $issues.Add("[S5] 悬挂工程（磁盘不存在）：$($p.Path)")
        }

        if ($csharpTypeGuids -notcontains $p.TypeGuid) { continue }
        $csharpPaths[$key] = $true

        # ---- S2 禁叶子直挂 ----
        if (-not $Umbrellas.ContainsKey($key)) {
            $owner = if ($Owners.ContainsKey($key)) { $Owners[$key] } else { '<未知根>' }
            $issues.Add("[S2] 叶子工程 '$($p.Name)' 直挂 sln（$($p.Path)）；应经 $owner\$owner.csproj 的 ProjectReference 引入")
        }

        # ---- S6 配置存在 ----
        if (-not $activeCfgGuids.ContainsKey($p.Guid)) {
            $issues.Add("[S6] 工程 '$($p.Name)' 缺 ProjectConfigurationPlatforms.ActiveCfg（dotnet build sln 会静默跳过）")
        }
    }

    # ---- S1 伞齐全 ----
    foreach ($u in $Umbrellas.Values) {
        if (-not $csharpPaths.ContainsKey($u.FullPath.ToLowerInvariant())) {
            $issues.Add("[S1] 根伞工程未登记：$($u.RelPath)（该根下全部叶子不进烘焙闭包）")
        }
    }

    # ---- S6 孤儿配置 ----
    foreach ($g in $configGuids.Keys) {
        if (-not $seenGuid.ContainsKey($g)) {
            $issues.Add("[S6] 孤儿配置行：$g 不属于任何已登记工程（删工程须同步删配置）")
        }
    }

    return ,$issues
}

# ---- 真实文件判定 ----
$slnLines = @([System.IO.File]::ReadAllLines($SolutionPath))
foreach ($v in (Get-SolutionUmbrellaViolations -Lines $slnLines -SolutionDirectory (Split-Path -Parent $SolutionPath) -Umbrellas $umbrellaSet -Owners $ownerMap)) {
    $violations.Add($v)
}

if ($ctx.Detail) {
    Write-Host ''
    Write-Host '  伞集合（modules.json roots 推导）：' -ForegroundColor Gray
    foreach ($u in ($umbrellaSet.Values | Sort-Object RelPath)) { Write-Host ("    - {0}" -f $u.RelPath) -ForegroundColor Gray }
}

# ---- 判据自证（正/负对照）----
$goodLines = @(
    'Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Modules", "Modules\Modules.csproj", "{11111111-1111-1111-1111-111111111111}"'
    'EndProject'
    'Global'
    "`tGlobalSection(ProjectConfigurationPlatforms) = postSolution"
    "`t`t{11111111-1111-1111-1111-111111111111}.Debug|Any CPU.ActiveCfg = Debug|Any CPU"
    "`tEndGlobalSection"
    'EndGlobal'
)
$badLines = @(
    'Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "Modules.Gather", "Modules.Gather", "{22222222-2222-2222-2222-222222222222}"'
    'EndProject'
    'Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Gather", "Modules\Gather\Gather.csproj", "{33333333-3333-3333-3333-333333333333}"'
    'EndProject'
    'Global'
    "`tGlobalSection(ProjectConfigurationPlatforms) = postSolution"
    "`t`t{33333333-3333-3333-3333-333333333333}.Debug|Any CPU.ActiveCfg = Debug|Any CPU"
    "`tEndGlobalSection"
    "`tGlobalSection(NestedProjects) = preSolution"
    "`t`t{33333333-3333-3333-3333-333333333333} = {22222222-2222-2222-2222-222222222222}"
    "`tEndGlobalSection"
    'EndGlobal'
)
$sampleFull = [System.IO.Path]::GetFullPath('C:\fake\Modules\Modules.csproj')
$sampleUmbrellas = @{
    $sampleFull.ToLowerInvariant() = [pscustomobject]@{ Name = 'Modules'; RelPath = 'Modules\Modules.csproj'; FullPath = $sampleFull }
}

$controls = @(
    @{
        Id        = 'S2/S3/S4'
        Predicate = {
            param($lines)
            $v = Get-SolutionUmbrellaViolations -Lines $lines -SolutionDirectory 'C:\fake' -Umbrellas $sampleUmbrellas -CheckFiles $false
            return (@($v | Where-Object { $_ -match '^\[S(2|3|4)\]' }).Count -gt 0)
        }
        Good      = $goodLines
        Bad       = $badLines
    }
)
foreach ($issue in (Assert-GateControls -Cases $controls -Label 'SolutionUmbrella')) { $violations.Add($issue) }

Write-GateVerdict -Violations $violations -Check:$Check -Key 'SolutionUmbrella' `
    -Hint '删除叶子工程条目及其配置行；叶子只经伞工程 ProjectReference 引入。可运行 Frame\PowerShellScript\Share\Solution\Sync-SlnToDisk.ps1 -Force 自愈。'

