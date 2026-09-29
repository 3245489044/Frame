#requires -Version 5.1
<#
NodeGateCommon.ps1 —— 门禁**公共扫描器/基线库**（§8 门禁框架，Node 系列守卫共用）。

设计（「新增判据 = 加数据行，不再复制扫描器」）：
  · 本文件只提供**无判据语义**的取数与输出原语（扫描器库）；
  · 各门禁脚本只保留「扫描面声明 + 判据数据（基线）+ 正/负对照」；
  · 基线一律外置到 Baselines\<Gate>.psd1（纯数据），禁散落脚本常量。

用法（门禁脚本头部）：
  . (Join-Path $PSScriptRoot 'NodeGateCommon.ps1')
#>

function Get-GateRepoRoot {
    param([string]$ScriptRoot)
    return (Resolve-Path (Join-Path $ScriptRoot '..\..\..\..')).Path
}

# ---------------- 取数：C# 常量 ----------------

function Get-CSharpConstMap {
    param([string]$Path)
    $map = @{}
    if (-not (Test-Path -LiteralPath $Path)) { return $map }
    foreach ($m in [regex]::Matches((Get-Content -LiteralPath $Path -Raw), 'public const string (?<n>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*"(?<v>[^"]+)"')) {
        $map[$m.Groups['n'].Value] = $m.Groups['v'].Value
    }
    return $map
}

function Get-DeclaredFlowIds {
    param([string]$Path)
    $map = @{}
    if (-not (Test-Path -LiteralPath $Path)) { return $map }
    foreach ($m in [regex]::Matches((Get-Content -LiteralPath $Path -Raw), '\["(?<b>[a-z0-9\-]+)"\]\s*=\s*"(?<f>[A-Za-z0-9]+)"')) {
        $map[$m.Groups['b'].Value] = $m.Groups['f'].Value
    }
    return $map
}

function Get-WorkflowFiles {
    param([string]$Path, [hashtable]$SpecConsts)
    $files = [System.Collections.Generic.List[string]]::new()
    if (-not (Test-Path -LiteralPath $Path)) { return $files }
    foreach ($m in [regex]::Matches((Get-Content -LiteralPath $Path -Raw), 'WorkflowSpecFiles\.(?<n>[A-Za-z_][A-Za-z0-9_]*)')) {
        $name = $m.Groups['n'].Value
        if ($SpecConsts.ContainsKey($name)) { $files.Add($SpecConsts[$name]) }
    }
    return $files
}

function Get-UsharInportTokens {
    param([string]$Path)
    $tokens = [System.Collections.Generic.List[string]]::new()
    if (-not (Test-Path -LiteralPath $Path)) { return $tokens }
    foreach ($m in [regex]::Matches((Get-Content -LiteralPath $Path -Raw), 'inports\s*=\s*(?<v>[A-Za-z0-9_,]+)')) {
        foreach ($t in ($m.Groups['v'].Value -split ',')) {
            $t = $t.Trim()
            if ($t) { $tokens.Add($t) }
        }
    }
    return $tokens
}

function Test-InportsRegistered {
    param(
        [string]$UsharPath,
        [hashtable]$ValueToName,
        [System.Collections.Generic.HashSet[string]]$SeedNames,
        # 收集式输入端口白名单（**逐字** token；缺省空 = 全部须登记）：token 是「收集端口位置」标记而非键值，
        # 故豁免 RoundKeys / CompatSeedKeys 对账；**仅**对收集端口位置生效（其余 inports 仍须登记）。
        [string[]]$CollectTokens = @()
    )
    $bad = [System.Collections.Generic.List[string]]::new()
    foreach ($token in (Get-UsharInportTokens -Path $UsharPath)) {
        if ($CollectTokens -contains $token) { continue }
        if (-not $ValueToName.ContainsKey($token)) { $bad.Add("$token（RoundKeys 无此键值）"); continue }
        if (-not $SeedNames.Contains($ValueToName[$token])) { $bad.Add("$token（CompatSeedKeys 未登记 $($ValueToName[$token])）") }
    }
    return $bad
}

# ---------------- 扫描器：调用点 / 命名空间取值点（单遍扫描，跳过注释行） ----------------

function Get-CallSiteCounts {
    param([string[]]$Dirs, [string[]]$Members)
    $regexes = @{}
    $counts = @{}
    foreach ($m in $Members) { $regexes[$m] = [regex]::new('\.' + [regex]::Escape($m) + '\s*\('); $counts[$m] = 0 }
    foreach ($dir in $Dirs) {
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        $files = Get-ChildItem -LiteralPath $dir -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notlike '*\_deprecated\*' }
        foreach ($f in $files) {
            foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
                if ($line.TrimStart() -match '^(//|/\*|\*)') { continue }
                foreach ($m in $Members) { if ($regexes[$m].IsMatch($line)) { $counts[$m]++ } }
            }
        }
    }
    return $counts
}

function Get-NamespaceValueCounts {
    param([string]$Dir, [string[]]$FileNames, [string]$Pattern)
    $counts = @{}
    foreach ($n in $FileNames) { $counts[$n] = 0 }
    if (-not (Test-Path -LiteralPath $Dir)) { return $counts }
    $rx = [regex]::new($Pattern)
    foreach ($f in (Get-ChildItem -LiteralPath $Dir -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue)) {
        $leaf = Split-Path $f.FullName -Leaf
        if (-not $counts.ContainsKey($leaf)) { continue }
        foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
            if ($line.TrimStart() -match '^(//|/\*|\*)') { continue }
            if ($rx.IsMatch($line)) { $counts[$leaf]++ }
        }
    }
    return $counts
}

function Get-PatternFileCounts {
    param([string[]]$Dirs, [string]$Pattern, [string[]]$ExcludeLeaf = @())
    $rx = [regex]::new($Pattern)
    $counts = @{}
    foreach ($dir in $Dirs) {
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        $files = Get-ChildItem -LiteralPath $dir -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notlike '*\_deprecated\*' -and $_.FullName -notlike '*\BuildOutput\*' }
        foreach ($f in $files) {
            $leaf = Split-Path $f.FullName -Leaf
            if ($ExcludeLeaf -contains $leaf) { continue }
            $c = 0
            foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
                if ($line.TrimStart() -match '^(//|/\*|\*)') { continue }
                if ($rx.IsMatch($line)) { $c++ }
            }
            if ($c -gt 0) { $counts[$leaf] = [int]$counts[$leaf] + $c }
        }
    }
    return $counts
}

function Get-PatternCountInFiles {
    param([string[]]$Files, [string]$Pattern)
    $rx = [regex]::new($Pattern)
    $total = 0
    $hits = [System.Collections.Generic.List[string]]::new()
    foreach ($f in $Files) {
        if (-not (Test-Path -LiteralPath $f)) { continue }
        foreach ($line in [System.IO.File]::ReadAllLines($f)) {
            if ($line.TrimStart() -match '^(//|/\*|\*)') { continue }
            if ($rx.IsMatch($line)) { $total++; $hits.Add((Split-Path $f -Leaf)) }
        }
    }
    return [PSCustomObject]@{ Total = $total; Hits = $hits }
}

# ---------------- 基线装载（外置纯数据） ----------------

function Import-GateBaseline {
    param([string]$Name)
    $path = Join-Path $PSScriptRoot (Join-Path 'Baselines' "$Name.psd1")
    if (-not (Test-Path -LiteralPath $path)) {
        throw "门禁基线文件缺失：$path（禁内联基线常量；新增门禁须同步 Baselines\<Gate>.psd1）"
    }
    return Import-PowerShellDataFile -LiteralPath $path
}

# ---------------- 输出原语 ----------------

function Write-GateHeader {
    param([string]$Title)
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan
}

function Write-GateVerdict {
    param([System.Collections.Generic.List[string]]$Violations, [switch]$Check, [string]$Key = 'AgentFrameworkSingleSource')
    if ($Violations.Count -eq 0) {
        Write-Host ""
        Write-Host "  [PASS] $Key：0 违规" -ForegroundColor Green
        Write-Host ""
        exit 0
    }
    Write-Host ""
    Write-Host "  违规明细（$($Violations.Count) 项）：" -ForegroundColor Yellow
    foreach ($v in $Violations) { Write-Host "    - $v" -ForegroundColor Yellow }
    Write-Host ""
    if ($Check) { exit 1 }
    exit 0
}

<# 基线核减判据（只减不增）：新增文件（不在基线表中）出现取值点即违规。 #>
function Test-BaselineNoIncrease {
    param([hashtable]$Counts, [hashtable]$Baselines, [string]$Label)
    $violations = [System.Collections.Generic.List[string]]::new()
    foreach ($k in $Counts.Keys) {
        if (-not $Baselines.ContainsKey($k)) {
            $violations.Add("$Label 新增散落点：$k = $($Counts[$k])（基线表无此项 ⇒ 只减不增禁新增）")
            continue
        }
        if ($Counts[$k] -gt $Baselines[$k]) {
            $violations.Add("$Label 取值点新增：$k = $($Counts[$k]) > 基线 $($Baselines[$k])（只减不增）")
        }
    }
    return $violations
}

