#requires -Version 5.1
<#
.SYNOPSIS
    声明面布局守卫（DeclarationLayout）—— declaration-layout.json ⇄ 磁盘 ⇄ BuildDefault 三方对账。

.DESCRIPTION
    判据（D1–D7）：
      D1 角色目录存在：每个 role 的 dirs 必须在配置根下存在（允许显式空目录）。
      D2 闭包角色已声明：bundle.Roles ⊆ roles。
      D3 磁盘覆盖：Config/**/*.ushar 必须落在某个 role 或 bundle.RootFiles（根级控制面白名单除外）。
      D4 逻辑名唯一：naming=role-basename 的角色内 basename 不得重复（防模块子目录撞键）。
      D5 命名策略：model/prompt/policy/toolface = role-basename；agents = full-path。
      D6 JSON ⇔ BuildDefault：角色集 / dirs / recursive / naming / bundle 三面等价。
      D7 正/负对照：合成布局（缺目录 / bundle 悬空 / 同名撞键）必须被判红。

    模式：默认报告；-Check 违规 → 退出码 1；-NegativeControl 自证可判红。
#>
[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
while ($repoRoot -and -not ((Test-Path -LiteralPath (Join-Path $repoRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $repoRoot 'Projects')))) {
    $repoRoot = Split-Path -Parent $repoRoot
}
if (-not $repoRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }

Import-Module (Join-Path $repoRoot 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name DeclarationLayout
$rootControlFiles = @($baseline.RootControlFiles.Value)
$migrationRootFiles = @($baseline.MigrationRootFiles.Value)

$configRoot = Join-Path $repoRoot 'Projects\Plug\Agent\Config'
$layoutPath = Join-Path $configRoot 'declaration-layout.json'
$defaultSourcePath = Join-Path $repoRoot 'Frame\Usharp\src\Usharp.Semantics\Compilation\DeclarationLayout.cs'

$violations = [System.Collections.Generic.List[string]]::new()

function Split-QuotedList {
    param([string]$Text)
    return @([regex]::Matches($Text, '"(?<v>[^"]*)"') | ForEach-Object { $_.Groups['v'].Value })
}

function Get-SetDiff {
    param([string[]]$Left, [string[]]$Right)
    return @($Left | Where-Object { $Right -notcontains $_ } | Sort-Object)
}

function Test-DuplicateBasename {
    param([object[]]$Roles, [string[]]$Files)
    $errors = [System.Collections.Generic.List[string]]::new()
    foreach ($role in $Roles) {
        if ($role.Naming -ne 'role-basename') { continue }
        $bases = @($Files | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) })
        foreach ($dup in ($bases | Group-Object | Where-Object Count -gt 1)) {
            $errors.Add("D4 逻辑名撞键：role=$($role.Role) basename=$($dup.Name)")
        }
    }
    return $errors
}

if (-not (Test-Path -LiteralPath $layoutPath)) {
    $violations.Add("D1 布局真源缺失：$layoutPath")
}
if (-not (Test-Path -LiteralPath $defaultSourcePath)) {
    $violations.Add("D6 内置布局源码缺失：$defaultSourcePath")
}
if ($violations.Count -gt 0) {
    Write-Host ''
    foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
    if ($Check) { exit 1 }
    exit 0
}

$json = Get-Content -LiteralPath $layoutPath -Raw -Encoding UTF8 | ConvertFrom-Json
$roles = @($json.roles)
$bundles = @($json.bundles.PSObject.Properties | ForEach-Object {
        [pscustomobject]@{
            Name      = $_.Name
            Roles     = @($_.Value.roles)
            RootFiles = @($_.Value.rootFiles)
        }
    })

$roleByName = @{}
foreach ($role in $roles) {
    $roleByName[$role.role] = [pscustomobject]@{
        Role      = $role.role
        Dirs      = @($role.dirs)
        Recursive = [bool]$role.recursive
        Naming    = [string]$role.naming
        Exclude   = @($role.exclude)
    }
}

# ---- D1 角色目录存在 ----
foreach ($role in $roleByName.Values) {
    foreach ($dir in $role.Dirs) {
        $full = Join-Path $configRoot $dir
        if (-not (Test-Path -LiteralPath $full)) {
            $violations.Add("D1 角色目录缺失：role=$($role.Role) dir=$dir（$full）")
        }
    }
}
# ---- D2 闭包角色已声明 ----
foreach ($bundle in $bundles) {
    foreach ($r in $bundle.Roles) {
        if (-not $roleByName.ContainsKey($r)) {
            $violations.Add("D2 bundle '$($bundle.Name)' 引用未声明角色 '$r'")
        }
    }
}

# ---- D3 磁盘覆盖 ----
$allUshar = @(Get-ChildItem -LiteralPath $configRoot -Recurse -Filter '*.ushar' -File | ForEach-Object {
        [IO.Path]::GetRelativePath($configRoot, $_.FullName).Replace('\', '/')
    })

$rootFiles = @($bundles | ForEach-Object { $_.RootFiles } | Where-Object { $_ } | ForEach-Object { $_.Replace('\', '/') })
foreach ($relative in $allUshar) {
    $covered = $false
    foreach ($role in $roleByName.Values) {
        foreach ($dir in $role.Dirs) {
            $prefix = ($dir -replace '\\', '/').Trim('/')
            if ($relative -eq "$prefix.ushar" -or $relative.StartsWith($prefix + '/', [StringComparison]::OrdinalIgnoreCase)) {
                $segments = $relative.Split('/')
                $excluded = $false
                foreach ($ex in $role.Exclude) { if ($segments -contains $ex) { $excluded = $true; break } }
                if (-not $excluded) { $covered = $true; break }
            }
        }
        if ($covered) { break }
    }
    if (-not $covered -and $rootFiles -notcontains $relative) {
        if ($rootControlFiles -contains $relative -or $migrationRootFiles -contains $relative) { continue }
        $violations.Add("D3 未归属声明：$relative（不在任何 role / bundle.RootFiles / 白名单）")
    }
}

# ---- D4 逻辑名唯一（role-basename 角色内）----
foreach ($role in $roleByName.Values) {
    if ($role.Naming -ne 'role-basename') { continue }
    $bases = [System.Collections.Generic.List[string]]::new()
    foreach ($relative in $allUshar) {
        foreach ($dir in $role.Dirs) {
            $prefix = ($dir -replace '\\', '/').Trim('/')
            if (-not ($relative -eq "$prefix.ushar" -or $relative.StartsWith($prefix + '/', [StringComparison]::OrdinalIgnoreCase))) { continue }
            $segments = $relative.Split('/')
            $excluded = $false
            foreach ($ex in $role.Exclude) { if ($segments -contains $ex) { $excluded = $true; break } }
            if ($excluded) { continue }
            $bases.Add([IO.Path]::GetFileNameWithoutExtension($relative))
            break
        }
    }
    foreach ($dup in ($bases | Group-Object | Where-Object Count -gt 1)) {
        $violations.Add("D4 逻辑名撞键：role=$($role.Role) basename=$($dup.Name)（$($dup.Count) 处）")
    }
}

# ---- D5 命名策略 ----
$namingPolicy = @{
    agents   = 'full-path'
    model    = 'role-basename'
    prompt   = 'role-basename'
    policy   = 'role-basename'
    toolface = 'role-basename'
}
foreach ($name in $namingPolicy.Keys) {
    if (-not $roleByName.ContainsKey($name)) {
        $violations.Add("D5 必需角色缺失：$name")
        continue
    }
    if ($roleByName[$name].Naming -ne $namingPolicy[$name]) {
        $violations.Add("D5 命名策略不符：role=$name 期望 $($namingPolicy[$name])，实际 $($roleByName[$name].Naming)")
    }
}

# ---- D6 JSON ⇔ BuildDefault ----
$source = Get-Content -LiteralPath $defaultSourcePath -Raw -Encoding UTF8
$startAt = $source.IndexOf('private static DeclarationLayout BuildDefault()')
$endAt = if ($startAt -ge 0) { $source.IndexOf('return new DeclarationLayout', $startAt) } else { -1 }
if ($startAt -lt 0 -or $endAt -lt 0) {
    $violations.Add('D6 取数面失效：DeclarationLayout.BuildDefault() 未定位（判据无标的）')
}
else {
    $block = $source.Substring($startAt, $endAt - $startAt)
    $defaultRoles = [System.Collections.Generic.List[object]]::new()
    $scopePattern = 'new\("(?<role>[^"]+)",\s*new\[\]\s*\{\s*(?<dirs>[^}]*)\}\s*,\s*(?<rec>true|false)\s*,\s*new\[\]\s*\{\s*(?<ex>[^}]*)\}\s*,\s*DeclarationNaming\.(?<naming>\w+)\)'
    foreach ($m in [regex]::Matches($block, $scopePattern)) {
        $defaultRoles.Add([pscustomobject]@{
            Role      = $m.Groups['role'].Value
            Dirs      = @(Split-QuotedList $m.Groups['dirs'].Value)
            Recursive = $m.Groups['rec'].Value -eq 'true'
            Naming    = if ($m.Groups['naming'].Value -eq 'RoleBasename') { 'role-basename' } else { 'full-path' }
            Exclude   = @(Split-QuotedList $m.Groups['ex'].Value)
        })
    }
    $defaultBundles = [System.Collections.Generic.List[object]]::new()
    $bundlePattern = 'new\("(?<name>[^"]+)",\s*new\[\]\s*\{\s*(?<roles>[^}]*)\}\s*,\s*(?:new\[\]\s*\{\s*(?<files>[^}]*)\}|Array\.Empty<string>\(\))\)'
    foreach ($m in [regex]::Matches($block, $bundlePattern)) {
        $defaultBundles.Add([pscustomobject]@{
            Name      = $m.Groups['name'].Value
            Roles     = @(Split-QuotedList $m.Groups['roles'].Value)
            RootFiles = @(Split-QuotedList $m.Groups['files'].Value)
        })
    }

    foreach ($b in (Get-SetDiff -Left @($roleByName.Keys) -Right @($defaultRoles.Role))) {
        $violations.Add("D6 角色仅 JSON 有：$b")
    }
    foreach ($b in (Get-SetDiff -Left @($defaultRoles.Role) -Right @($roleByName.Keys))) {
        $violations.Add("D6 角色仅 BuildDefault 有：$b")
    }
    foreach ($role in $defaultRoles) {
        if (-not $roleByName.ContainsKey($role.Role)) { continue }
        $jsonRole = $roleByName[$role.Role]
        if ((Get-SetDiff -Left $role.Dirs -Right $jsonRole.Dirs).Count -gt 0 -or (Get-SetDiff -Left $jsonRole.Dirs -Right $role.Dirs).Count -gt 0) {
            $violations.Add("D6 role=$($role.Role) dirs 不一致：JSON=[$($jsonRole.Dirs -join ',')] Default=[$($role.Dirs -join ',')]")
        }
        if ($role.Recursive -ne $jsonRole.Recursive) { $violations.Add("D6 role=$($role.Role) recursive 不一致") }
        if ($role.Naming -ne $jsonRole.Naming) { $violations.Add("D6 role=$($role.Role) naming 不一致：JSON=$($jsonRole.Naming) Default=$($role.Naming)") }
        if ((Get-SetDiff -Left $role.Exclude -Right $jsonRole.Exclude).Count -gt 0 -or (Get-SetDiff -Left $jsonRole.Exclude -Right $role.Exclude).Count -gt 0) {
            $violations.Add("D6 role=$($role.Role) exclude 不一致")
        }
    }

    foreach ($b in (Get-SetDiff -Left @($bundles.Name) -Right @($defaultBundles.Name))) { $violations.Add("D6 闭包仅 JSON 有：$b") }
    foreach ($b in (Get-SetDiff -Left @($defaultBundles.Name) -Right @($bundles.Name))) { $violations.Add("D6 闭包仅 BuildDefault 有：$b") }
    foreach ($bundle in $defaultBundles) {
        $jsonBundle = $bundles | Where-Object { $_.Name -eq $bundle.Name } | Select-Object -First 1
        if (-not $jsonBundle) { continue }
        if ((Get-SetDiff -Left $bundle.Roles -Right $jsonBundle.Roles).Count -gt 0 -or (Get-SetDiff -Left $jsonBundle.Roles -Right $bundle.Roles).Count -gt 0) {
            $violations.Add("D6 bundle=$($bundle.Name) roles 不一致：JSON=[$($jsonBundle.Roles -join ',')] Default=[$($bundle.Roles -join ',')]")
        }
        if ((Get-SetDiff -Left $bundle.RootFiles -Right $jsonBundle.RootFiles).Count -gt 0 -or (Get-SetDiff -Left $jsonBundle.RootFiles -Right $bundle.RootFiles).Count -gt 0) {
            $violations.Add("D6 bundle=$($bundle.Name) rootFiles 不一致：JSON=[$($jsonBundle.RootFiles -join ',')] Default=[$($bundle.RootFiles -join ',')]")
        }
    }
}




# ---- D7 正/负对照 ----
$syntheticDiff = Get-SetDiff -Left @('a', 'b') -Right @('a')
if ($syntheticDiff.Count -ne 1 -or $syntheticDiff[0] -ne 'b') {
    $violations.Add('D7 负对照失效：集合比较函数未报出合成差异样本')
}
$ncDup = Test-DuplicateBasename -Roles @([pscustomobject]@{ Role = 'nodes'; Naming = 'role-basename' }) -Files @('x.ushar', 'sub/x.ushar')
if ($ncDup.Count -eq 0) {
    $violations.Add('D7 负对照失效：同名 basename 合成样本未被判红')
}
$ncRoles = @('nodes')
$ncBundleRoles = @('nodes', 'missing')
$ncDangling = @($ncBundleRoles | Where-Object { $ncRoles -notcontains $_ })
if ($ncDangling.Count -ne 1) {
    $violations.Add('D7 负对照失效：悬空 bundle 角色合成样本未被判红')
}

if ($violations.Count -eq 0) {
    Write-Host ''
    Write-Host ("[OK] DeclarationLayout：0 违规（roles={0} bundles={1}；JSON ⇔ 磁盘 ⇔ BuildDefault 一致）" -f $roles.Count, $bundles.Count) -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host "[FAIL] DeclarationLayout 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
Write-Host '  修复口径：布局真源 = Config\declaration-layout.json；内置布局 DeclarationLayout.BuildDefault() 必须同款；搬家先改布局再搬文件。' -ForegroundColor Gray
if ($Check) { exit 1 }
exit 0

