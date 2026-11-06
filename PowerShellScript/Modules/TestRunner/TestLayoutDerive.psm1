#requires -Version 5.1
<#
.SYNOPSIS
    TestLayout 推导引擎（通用：根 / 适配器 / 约定 / 证据）。

.DESCRIPTION
    把「测试 ↔ 源码」的对齐从**手写账本**改为**可复算的投影**：
      L0 路径：根映射 + 组镜像 + 容器段压平 + `.Tests` 后缀  → 测试工程 ↔ 源码工程目录
      L1 文件名：`Foo.cs` ↔ `FooTests.cs` / `Foo.Test.cs` / `FooSpec.cs`
      L2 内容：源码「声明类型名 → 声明文件」倒排索引（特异性过滤）→ 测试文件引用集
      L3 声明：`.specs` 的 `project` / `migrate source`；C# 的 `// covers: <path>` / `[Covers("...")]`

    判定模型 = **投影（projection），非双射**：
      · 一个测试文件可投影到多个源码文件（合法）；一个源码文件可被多个测试投影（合法）；
      · 零证据 = Unmapped（不猜）；同名多候选 = 多候选（不选唯一）。

    纪律：
      · 依赖单向：本件不接受/不引用任何 CentralTest 路径（账本由调用方注入）；
      · 未知取值（未知 scheme / adapter / kind）⇒ Undecidable，不判红也不判绿；
      · 事实来源：文件系统 + 账本规则；**不**使用构建产物（共享输出根不可反推）。

.NOTES
    消费方：Environment\CentralTest\TestRunner\TestLayout.psm1（薄壳）
            Environment\CentralTest\Internal\Get-TestPathPlan.ps1（诊断入口）
            Environment\CentralTest\Tools\Gate\TestLayout\Check-*.ps1（判据）
#>

$script:TestLayoutDefaults = @{
    # L2 证据：一个测试文件至少命中 N 个「特异类型」（类型声明文件数 ≤ MaxDeclaringFile）
    EvidenceMinTypeHits      = 1
    EvidenceMaxDeclaringFile = 3
    # 源码声明类型（用于倒排索引）
    TypeDeclPattern          = '\b(?:class|record|struct|enum|interface)\s+(?<n>[A-Za-z_][A-Za-z0-9_]*)'
    # 测试文件中的标识符（大写开头、长度 ≥5；过滤 `System`/`Assert` 之类短通用名）
    IdentifierPattern        = '\b[A-Z][A-Za-z0-9_]{4,}\b'
    # 测试载体判定
    TestAttributePattern     = '(?m)\[\s*(?:TestClass|TestMethod|DataTestMethod|TestInitialize|Fact|Theory|TestCase)\b'
    TestNamePattern          = '(?:Tests?|Spec)\.cs$'
    # L3 声明
    CoverCommentPattern      = '(?m)^\s*//\s*covers\s*:\s*(?<p>.+?)\s*$'
    CoverAttributePattern    = '\[\s*Covers\s*\(\s*"(?<p>[^"]+)"\s*\)\s*\]'
    # L3 声明（非 dotnet 载体）
    PesterCoverCommentPattern = '(?m)^\s*#\s*covers\s*:\s*(?<p>.+?)\s*$'
    # `.specs` 指令
    SpecProjectPattern       = '(?m)^\s*project\s+(?<p>\S+)\s*$'
    SpecMigrateSourcePattern = '(?m)^\s*migrate\s+source\s+(?<p>.+?)\s*$'
    # 结构性免测（可被账本 Conventions.Exemptions 覆写）
    GeneratedPattern         = '(\.g\.cs$|\.Designer\.cs$|^AssemblyInfo\.cs$|^GlobalUsings\.cs$|\.Generated\.cs$)'
}

# ============================================================
#  路径归一（PS 5.1 兼容：不用 [IO.Path]::GetRelativePath）
# ============================================================

function Get-TestLayoutRelativePath {
    <#
    .SYNOPSIS
        绝对路径 → 相对 Base 的路径（分隔符 `\`）；不在 Base 下 ⇒ $null。
    #>
    param(
        [Parameter(Mandatory)][string]$Base,
        [Parameter(Mandatory)][string]$Path
    )

    $b = [IO.Path]::GetFullPath($Base).TrimEnd('\')
    $p = [IO.Path]::GetFullPath($Path)
    if ($p -ieq $b) { return '' }
    if ($p.StartsWith($b + '\', [StringComparison]::OrdinalIgnoreCase)) {
        return $p.Substring($b.Length + 1)
    }
    return $null
}

function ConvertTo-TestLayoutPath {
    <#
    .SYNOPSIS
        任意输入 → 仓库相对路径（`\` 分隔、无尾斜杠）；越界返回 $null。
    #>
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Path
    )

    $p = ([string]$Path).Trim().Replace('/', '\')
    if ([string]::IsNullOrWhiteSpace($p)) { return $null }
    if ([IO.Path]::IsPathRooted($p)) {
        $p = Get-TestLayoutRelativePath -Base $RepoRoot -Path $p
        if ($null -eq $p) { return $null }
    }
    while ($p.StartsWith('.\')) { $p = $p.Substring(2) }
    $p = $p.TrimEnd('\')
    if ($p -eq '.' -or $p -eq '') { return '' }
    return $p
}

function Read-TestLayoutLedger {
    <#
    .SYNOPSIS
        读取账本（纯数据 psd1）；缺失/不可解析 ⇒ throw（fail-closed）。
    #>
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "[TestLayout] 账本缺失：$Path（禁以「无账本」伪装成「无对齐义务」）"
    }
    try {
        $data = Import-PowerShellDataFile -LiteralPath $Path -ErrorAction Stop
    }
    catch {
        throw "[TestLayout] 账本不可解析：$Path（$($_.Exception.Message)）"
    }
    if ($null -eq $data) { throw "[TestLayout] 账本为空：$Path" }
    return $data
}

# ============================================================
#  配置：账本 v3（Roots / Adapters / Groups / Conventions / Overrides）
# ============================================================

function New-TestLayoutConfig {
    <#
    .SYNOPSIS
        校验并规范化账本 → 配置对象（Roots 补绝对路径、Adapters 建索引、Groups 建反向表）。
    .DESCRIPTION
        fail-closed：Schema / Kind / Adapter 引用 / 测试根 Lane / 键重复，任一非法即 throw。
        未知 Adapter 不做「降级猜测」—— 直接拒绝（禁把配置错误伪装成对齐结果）。
    #>
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][hashtable]$Ledger,
        [string]$LedgerPath = ''
    )

    $repo = [IO.Path]::GetFullPath($RepoRoot).TrimEnd('\')
    $schema = [string]$Ledger.Schema
    if ($schema -ne 'test-layout-v3') {
        throw "[TestLayout] 账本 Schema 非法：'$schema'（期望 test-layout-v3）"
    }

    # ---- Adapters ----
    $adapterMap = @{}
    foreach ($a in @($Ledger.Adapters)) {
        if ($null -eq $a) { continue }
        $key = [string]$a.Key
        if ([string]::IsNullOrWhiteSpace($key)) { throw '[TestLayout] Adapters 存在缺 Key 的条目' }
        if ($adapterMap.ContainsKey($key)) { throw "[TestLayout] Adapters 重复：$key" }
        $adapterMap[$key] = @{
            Key            = $key
            Carrier        = [string]$a.Carrier
            Glob           = [string]$a.Glob
            TestPattern    = $(if ($a.ContainsKey('TestPattern')) { [string]$a.TestPattern } else { '' })
            NonTestPattern = $(if ($a.ContainsKey('NonTestPattern')) { [string]$a.NonTestPattern } else { '' })
            DepthPolicy    = $(if ($a.ContainsKey('DepthPolicy')) { [string]$a.DepthPolicy } else { 'container-flatten' })
        }
    }

    # ---- Roots ----
    $roots = New-Object System.Collections.Generic.List[object]
    $rootByKey = @{}
    foreach ($r in @($Ledger.Roots)) {
        if ($null -eq $r) { continue }
        $key = [string]$r.Key
        if ([string]::IsNullOrWhiteSpace($key)) { throw '[TestLayout] Roots 存在缺 Key 的条目' }
        if ($rootByKey.ContainsKey($key)) { throw "[TestLayout] Roots 重复：$key" }
        $kind = [string]$r.Kind
        if (@('Source', 'Test') -notcontains $kind) { throw "[TestLayout] Root '$key' Kind 非法：$kind" }
        $adapterKey = [string]$r.Adapter
        if (-not $adapterMap.ContainsKey($adapterKey)) { throw "[TestLayout] Root '$key' 引用未知 Adapter：$adapterKey" }
        $rel = ([string]$r.Path).Replace('/', '\').TrimEnd('\')
        $lane = $(if ($r.ContainsKey('Lane')) { [string]$r.Lane } else { '' })
        if ($kind -eq 'Test' -and [string]::IsNullOrWhiteSpace($lane)) {
            throw "[TestLayout] 测试根 '$key' 缺 Lane（Strict/SpecOnly/Exempt）"
        }
        $node = [pscustomobject]@{
            Key      = $key
            Kind     = $kind
            Path     = $rel
            AbsPath  = (Join-Path $repo $rel)
            Adapter  = $adapterKey
            Lane     = $lane
            Manifest = $(if ($r.ContainsKey('Manifest')) { [string]$r.Manifest } else { '' })
        }
        $rootByKey[$key] = $node
        $roots.Add($node)
    }
    if ($roots.Count -eq 0) { throw '[TestLayout] 账本 Roots 为空（禁以空配置判绿）' }

    # ---- Groups（测试组叶名 → 源码组叶名）----
    $groups = @{}
    foreach ($k in @($Ledger.Groups.Keys)) {
        $testGroup = ([string]$k).Trim()
        $srcGroup = ([string]$Ledger.Groups[$k]).Replace('/', '\').TrimEnd('\')
        if ($testGroup -and $srcGroup) { $groups[$testGroup] = $srcGroup }
    }
    $reverseGroups = @{}
    foreach ($k in @($groups.Keys)) {
        $src = $groups[$k]
        if (-not $reverseGroups.ContainsKey($src)) { $reverseGroups[$src] = $k }
    }

    # ---- Conventions（与默认值合并；缺省即用默认）----
    $conv = @{
        Evidence = @{
            MinTypeHits      = $script:TestLayoutDefaults.EvidenceMinTypeHits
            MaxDeclaringFile = $script:TestLayoutDefaults.EvidenceMaxDeclaringFile
            IgnoredTypes     = @()
        }
        Exemptions = @{
            GeneratedPattern = $script:TestLayoutDefaults.GeneratedPattern
            InterfaceOnly    = $true
            EnumOnly         = $true
        }
        NamingVariants = @()
    }
    if ($Ledger.ContainsKey('Conventions') -and $null -ne $Ledger.Conventions) {
        $c = $Ledger.Conventions
        if ($c.ContainsKey('Evidence') -and $null -ne $c.Evidence) {
            if ($c.Evidence.ContainsKey('MinTypeHits')) { $conv.Evidence.MinTypeHits = [int]$c.Evidence.MinTypeHits }
            if ($c.Evidence.ContainsKey('MaxDeclaringFile')) { $conv.Evidence.MaxDeclaringFile = [int]$c.Evidence.MaxDeclaringFile }
            if ($c.Evidence.ContainsKey('IgnoredTypes')) { $conv.Evidence.IgnoredTypes = @($c.Evidence.IgnoredTypes) }
        }
        if ($c.ContainsKey('Exemptions') -and $null -ne $c.Exemptions) {
            foreach ($k in @($c.Exemptions.Keys)) { $conv.Exemptions[$k] = $c.Exemptions[$k] }
        }
        if ($c.ContainsKey('NamingVariants')) { $conv.NamingVariants = @($c.NamingVariants) }
    }

    $overrides = @()
    if ($Ledger.ContainsKey('Overrides')) { $overrides = @($Ledger.Overrides) }

    $cap = @{ Schemes = @(); EdgeTypes = @(); WaiverKinds = @(); Origins = @('Derived', 'Debt', 'Verdict') }
    if ($Ledger.ContainsKey('Capabilities') -and $null -ne $Ledger.Capabilities) {
        foreach ($k in @('Schemes', 'EdgeTypes', 'WaiverKinds')) {
            if ($Ledger.Capabilities.ContainsKey($k)) { $cap[$k] = @($Ledger.Capabilities[$k]) }
        }
    }

    return [pscustomobject]@{
        Schema       = $schema
        RepoRoot     = $repo
        LedgerPath   = $LedgerPath
        Roots        = $roots.ToArray()
        RootByKey    = $rootByKey
        Adapters     = $adapterMap
        Groups       = $groups
        ReverseGroups = $reverseGroups
        Conventions  = $conv
        Overrides    = $overrides
        Capabilities = $cap
    }
}

# ============================================================
#  分类
# ============================================================

function Get-TestLayoutSourceClass {
    <#
    .SYNOPSIS
        源码文件分类：Testable / Exempt:<原因>。规则可枚举，禁「清单式豁免」。
    #>
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Content,
        $Exemptions = $null
    )

    $genPattern = $script:TestLayoutDefaults.GeneratedPattern
    $ifaceOnly = $true
    $enumOnly = $true
    if ($null -ne $Exemptions) {
        if ($Exemptions.ContainsKey('GeneratedPattern') -and $Exemptions.GeneratedPattern) { $genPattern = [string]$Exemptions.GeneratedPattern }
        if ($Exemptions.ContainsKey('InterfaceOnly')) { $ifaceOnly = [bool]$Exemptions.InterfaceOnly }
        if ($Exemptions.ContainsKey('EnumOnly')) { $enumOnly = [bool]$Exemptions.EnumOnly }
    }

    if ($Name -match $genPattern) { return 'Exempt:Generated' }
    if ($Content -match '\[GeneratedCode') { return 'Exempt:Generated' }

    $hasIface = $Content -match '\binterface\s+[A-Za-z_]'
    $hasClass = $Content -match '\b(?:class|record|struct)\s+[A-Za-z_]'
    $hasEnum = $Content -match '\benum\s+[A-Za-z_]'

    if ($ifaceOnly -and $hasIface -and -not $hasClass -and -not $hasEnum) { return 'Exempt:InterfaceOnly' }
    if ($enumOnly -and $hasEnum -and -not $hasClass -and -not $hasIface) { return 'Exempt:EnumOnly' }
    return 'Testable'
}

function Get-TestLayoutTestClass {
    <#
    .SYNOPSIS
        测试文件分类：Carrier（测试载体）/ Helper（辅助：Fake/Builder/GlobalUsings…）。
    #>
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Content
    )

    if ($Content -match $script:TestLayoutDefaults.TestAttributePattern) { return 'Carrier' }
    if ($Name -match $script:TestLayoutDefaults.TestNamePattern) { return 'Carrier' }
    return 'Helper'
}

# ============================================================
#  枚举
# ============================================================

function Get-TestLayoutFiles {
    <#
    .SYNOPSIS
        枚举各根的 `.cs` / `.csproj`，以及适配器声明的载体扩展（`.specs` / `*.Tests.ps1` / `package.json`）。
    .DESCRIPTION
        默认走**文件系统枚举**（本仓实测 4.7s；git 索引在大型子模块上首建 22s）。
        需要共享索引时显式 `-UseGitIndex`（GATE_FILE_INDEX_CACHE 预热场景）。

        口径说明（与施工单的偏差，已记录）：
          · `.cs` / `.csproj` 对所有根**始终保留**（结构面：工程目录用于 L0；且 spec 根下仍混装 `.cs` 载体，
            严格「只按 Adapter.Glob」会静默丢弃存量载体 ⇒ 行为倒退）；
          · Adapter.Glob 只**追加**该适配器的载体扩展，不削减结构枚举。
    .PARAMETER Enumerate
        注入枚举器（测试用）：入参 = 根绝对路径，返回绝对文件路径数组。
    #>
    param(
        [Parameter(Mandatory)]$Config,
        [scriptblock]$Enumerate = $null,
        [switch]$UseGitIndex,
        [switch]$NoCache
    )

    $useGateIndex = ($null -eq $Enumerate) -and $UseGitIndex
    if ($useGateIndex -and -not (Get-Command Get-GateFiles -ErrorAction SilentlyContinue)) {
        $idxModule = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'Baselines\GateFileIndex.psm1'
        if (Test-Path -LiteralPath $idxModule) { Import-Module -Name $idxModule -Force -DisableNameChecking }
        if (-not (Get-Command Get-GateFiles -ErrorAction SilentlyContinue)) { $useGateIndex = $false }
    }

    $result = @{
        SourceCs       = New-Object System.Collections.Generic.List[string]
        TestCs         = New-Object System.Collections.Generic.List[string]
        SourceProjects = New-Object System.Collections.Generic.List[string]
        TestProjects   = New-Object System.Collections.Generic.List[string]
        TestSpecs      = New-Object System.Collections.Generic.List[string]
        TestScripts    = New-Object System.Collections.Generic.List[string]
        TestPackages   = New-Object System.Collections.Generic.List[string]
    }
    $exclude = '\\BuildOutput\\|\\obj\\|\\bin\\|node_modules|\\_deprecated\\'

    foreach ($root in @($Config.Roots)) {
        if (-not (Test-Path -LiteralPath $root.AbsPath)) {
            Write-Warning "[TestLayout] 根不存在（跳过，WARN 可见化）：$($root.Key) → $($root.AbsPath)"
            continue
        }

        # 结构扩展（始终）+ 适配器载体扩展（追加）
        $globs = New-Object System.Collections.Generic.List[string]
        foreach ($g in @('*.cs', '*.csproj')) { if (-not $globs.Contains($g)) { $globs.Add($g) } }
        if ($Config.Adapters.ContainsKey($root.Adapter)) {
            foreach ($g in @(([string]$Config.Adapters[$root.Adapter].Glob) -split '[;,]')) {
                $gg = $g.Trim()
                if ($gg -and -not $globs.Contains($gg)) { $globs.Add($gg) }
            }
        }

        if ($Enumerate) {
            $all = @(& $Enumerate $root.AbsPath)
        }
        elseif ($useGateIndex) {
            $all = @(Get-GateFiles -Root $root.AbsPath -Extension @('cs', 'csproj', 'specs', 'ps1', 'json') -NoCache:$NoCache)
        }
        else {
            $all = @(Get-ChildItem -LiteralPath $root.AbsPath -Recurse -File -ErrorAction SilentlyContinue |
                Select-Object -ExpandProperty FullName)
        }

        foreach ($f in $all) {
            if ([string]::IsNullOrWhiteSpace($f)) { continue }
            if ($f -match $exclude) { continue }
            $name = [IO.Path]::GetFileName($f)
            $matched = $false
            foreach ($g in $globs) { if ($name -like $g) { $matched = $true; break } }
            if (-not $matched) { continue }

            if ($f.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase)) {
                if ($root.Kind -eq 'Source') { $result.SourceCs.Add($f) } else { $result.TestCs.Add($f) }
            }
            elseif ($f.EndsWith('.csproj', [StringComparison]::OrdinalIgnoreCase)) {
                if ($root.Kind -eq 'Source') { $result.SourceProjects.Add($f) } else { $result.TestProjects.Add($f) }
            }
            elseif ($root.Kind -eq 'Test' -and $f.EndsWith('.specs', [StringComparison]::OrdinalIgnoreCase)) {
                $result.TestSpecs.Add($f)
            }
            elseif ($root.Kind -eq 'Test' -and $f.EndsWith('.Tests.ps1', [StringComparison]::OrdinalIgnoreCase)) {
                $result.TestScripts.Add($f)
            }
            elseif ($root.Kind -eq 'Test' -and $name -ieq 'package.json') {
                $result.TestPackages.Add($f)
            }
        }
    }

    return $result
}

function Get-TestLayoutTestDirCandidates {
    <#
    .SYNOPSIS
        由一个源码目录生成「期望测试目录」后缀候选（含限定叶名与容器段压平）。
    .EXAMPLE
        src = Modules\Data\Parsing\Keys
        → Modules.Tests\Data\Parsing\Keys.Tests
        → Modules.Tests\Data.Parsing.Keys.Tests
        → Modules.Tests\Parsing\Keys.Tests
        → Modules.Tests\Parsing.Keys.Tests
        → Modules.Tests\Keys.Tests
    #>
    param(
        [Parameter(Mandatory)][string]$SourceRelPath,
        [string]$SourceRootPath = '',
        [hashtable]$ReverseGroups = @{},
        [string]$DepthPolicy = 'container-flatten'
    )

    $rel = $SourceRelPath
    if ($SourceRootPath -and $rel.StartsWith($SourceRootPath, [StringComparison]::OrdinalIgnoreCase)) {
        $rel = $rel.Substring($SourceRootPath.Length)
    }
    $rel = $rel.Trim('\')
    if ([string]::IsNullOrWhiteSpace($rel)) { return @() }

    $segs = @($rel -split '\\')
    $group = $segs[0]
    $testGroup = $(if ($ReverseGroups.ContainsKey($group)) { [string]$ReverseGroups[$group] } else { $group + '.Tests' })
    $rest = @($segs | Select-Object -Skip 1)

    $out = New-Object System.Collections.Generic.List[string]
    $flat = New-Object System.Collections.Generic.List[string]
    if ($rest.Count -eq 0) { $out.Add($testGroup); return $out.ToArray() }

    for ($k = 0; $k -lt $rest.Count; $k++) {
        if ($DepthPolicy -ne 'container-flatten' -and $k -gt 0) { break }
        $tail = @($rest | Select-Object -Skip $k)
        $leaf = $tail[$tail.Count - 1]
        if ($tail.Count -gt 1) {
            $prefix = @($tail | Select-Object -First ($tail.Count - 1))
            $out.Add($testGroup + '\' + ($prefix -join '\') + '\' + $leaf + '.Tests')
            $out.Add($testGroup + '\' + (($prefix + $leaf) -join '.') + '.Tests')
            $flat.Add(($prefix -join '\') + '\' + $leaf + '.Tests')
            $flat.Add((($prefix + $leaf) -join '.') + '.Tests')
        }
        else {
            $out.Add($testGroup + '\' + $leaf + '.Tests')
            $flat.Add($leaf + '.Tests')
        }
        if ($DepthPolicy -eq 'strict') { break }
    }
    # 平坦根（无组段）的载体：如 AgentTest\Agent.Core.Tests / UsharpTests\X.Tests
    foreach ($f in $flat) { if (-not $out.Contains($f)) { $out.Add($f) } }
    return $out.ToArray()
}

function Get-TestLayoutOwningRoot {
    <#
    .SYNOPSIS
        找出包含该相对路径的最深根（同 Kind）。
    #>
    param(
        [Parameter(Mandatory)]$Config,
        [Parameter(Mandatory)][string]$RelPath,
        [string]$Kind = ''
    )

    $best = $null
    foreach ($r in @($Config.Roots)) {
        if ($Kind -and $r.Kind -ne $Kind) { continue }
        if ($RelPath -ieq $r.Path -or $RelPath.StartsWith($r.Path + '\', [StringComparison]::OrdinalIgnoreCase)) {
            if ($null -eq $best -or $r.Path.Length -gt $best.Path.Length) { $best = $r }
        }
    }
    return $best
}

# ============================================================
#  索引（一次扫描：文件 / 类型倒排 / 工程目录 / L0 映射）
# ============================================================

function New-TestLayoutIndex {
    <#
    .SYNOPSIS
        构建对齐索引：源码/测试文件分类、类型倒排、工程目录与 L0 映射。
    .OUTPUTS
        pscustomobject：SourceFiles / TestFiles / SourceProjectDirs / TestProjectDirs /
                        ProjectMap / StemToSources / TypeToSources / Stats
    #>
    param(
        [Parameter(Mandatory)]$Config,
        [scriptblock]$Enumerate = $null,
        [switch]$UseGitIndex,
        [switch]$NoCache
    )

    $repo = $Config.RepoRoot
    $files = Get-TestLayoutFiles -Config $Config -Enumerate $Enumerate -UseGitIndex:$UseGitIndex -NoCache:$NoCache

    # ---- 源码：分类 + 声明类型倒排 ----
    $sourceFiles = New-Object System.Collections.Generic.List[object]
    $typeAll = @{}
    $typePattern = $script:TestLayoutDefaults.TypeDeclPattern

    foreach ($abs in $files.SourceCs) {
        $rel = Get-TestLayoutRelativePath -Base $repo -Path $abs
        if ([string]::IsNullOrWhiteSpace($rel)) { continue }
        $content = ''
        try { $content = [IO.File]::ReadAllText($abs) } catch { $content = '' }
        $name = [IO.Path]::GetFileName($abs)
        $class = Get-TestLayoutSourceClass -Name $name -Content $content -Exemptions $Config.Conventions.Exemptions
        $sourceFiles.Add([pscustomobject]@{
            Rel   = $rel
            Abs   = $abs
            Stem  = [IO.Path]::GetFileNameWithoutExtension($abs)
            Dir   = [IO.Path]::GetDirectoryName($rel)
            Class = $class
        })
        foreach ($m in [regex]::Matches($content, $typePattern)) {
            $t = $m.Groups['n'].Value
            if ($null -eq $t -or $t.Length -lt 5) { continue }
            if (-not $typeAll.ContainsKey($t)) {
                $typeAll[$t] = New-Object System.Collections.Generic.HashSet[string]
            }
            [void]$typeAll[$t].Add($rel)
        }
    }

    # ---- 类型特异性过滤（声明文件数 ≤ MaxDeclaringFile）----
    $maxDecl = [int]$Config.Conventions.Evidence.MaxDeclaringFile
    $typeToSources = @{}
    $typeDeclaringCounts = @{}
    foreach ($t in @($typeAll.Keys)) {
        $set = $typeAll[$t]
        $typeDeclaringCounts[$t] = $set.Count
        if ($set.Count -le $maxDecl) { $typeToSources[$t] = @($set) }
    }

    # ---- 词干索引（L1）----
    $stemToSources = @{}
    foreach ($e in $sourceFiles) {
        $k = $e.Stem.ToLowerInvariant()
        if (-not $stemToSources.ContainsKey($k)) { $stemToSources[$k] = New-Object System.Collections.Generic.List[string] }
        $stemToSources[$k].Add($e.Rel)
    }

    # ---- 测试文件分类 ----
    $testFiles = New-Object System.Collections.Generic.List[object]
    foreach ($abs in $files.TestCs) {
        $rel = Get-TestLayoutRelativePath -Base $repo -Path $abs
        if ([string]::IsNullOrWhiteSpace($rel)) { continue }
        $content = ''
        try { $content = [IO.File]::ReadAllText($abs) } catch { $content = '' }
        $name = [IO.Path]::GetFileName($abs)
        $testFiles.Add([pscustomobject]@{
            Rel            = $rel
            Abs            = $abs
            Stem           = [IO.Path]::GetFileNameWithoutExtension($abs)
            Dir            = [IO.Path]::GetDirectoryName($rel)
            Class          = (Get-TestLayoutTestClass -Name $name -Content $content)
            Kind           = 'Cs'
            ProjectName    = ''
            MigrateSources = @()
        })
    }

    # ---- `.specs`（适配器 spec）：`project` → 测试工程；`migrate source` → L3 证据 ----
    foreach ($abs in $files.TestSpecs) {
        $rel = Get-TestLayoutRelativePath -Base $repo -Path $abs
        if ([string]::IsNullOrWhiteSpace($rel)) { continue }
        $content = ''
        try { $content = [IO.File]::ReadAllText($abs) } catch { $content = '' }
        $projectName = ''
        $pm = [regex]::Match($content, $script:TestLayoutDefaults.SpecProjectPattern)
        if ($pm.Success) { $projectName = $pm.Groups['p'].Value.Trim() }
        $migrates = New-Object System.Collections.Generic.List[string]
        foreach ($mm in [regex]::Matches($content, $script:TestLayoutDefaults.SpecMigrateSourcePattern)) {
            $v = $mm.Groups['p'].Value.Trim().Trim('"').Trim()
            if ($v) { $migrates.Add($v) }
        }
        # 规则（非清单）：既无 project 又无 migrate source ⇒ 共享台账/辅助，不计载体
        $class = $(if ($projectName -or $migrates.Count -gt 0) { 'Carrier' } else { 'Helper' })
        $testFiles.Add([pscustomobject]@{
            Rel            = $rel
            Abs            = $abs
            Stem           = [IO.Path]::GetFileNameWithoutExtension($abs)
            Dir            = [IO.Path]::GetDirectoryName($rel)
            Class          = $class
            Kind           = 'Spec'
            ProjectName    = $projectName
            MigrateSources = @($migrates)
        })
    }

    # ---- Pester 载体（`*.Tests.ps1`）：工程 = 所在目录 ----
    foreach ($abs in $files.TestScripts) {
        $rel = Get-TestLayoutRelativePath -Base $repo -Path $abs
        if ([string]::IsNullOrWhiteSpace($rel)) { continue }
        $testFiles.Add([pscustomobject]@{
            Rel            = $rel
            Abs            = $abs
            Stem           = [IO.Path]::GetFileNameWithoutExtension($abs)
            Dir            = [IO.Path]::GetDirectoryName($rel)
            Class          = 'Carrier'
            Kind           = 'Pester'
            ProjectName    = ''
            MigrateSources = @()
        })
    }

    # ---- npm 载体（`package.json`，`scripts.test` 存在）----
    foreach ($abs in $files.TestPackages) {
        $rel = Get-TestLayoutRelativePath -Base $repo -Path $abs
        if ([string]::IsNullOrWhiteSpace($rel)) { continue }
        $class = 'Carrier'   # fail-closed：不可解析时按载体处理（不得当辅助放过）
        try {
            $j = [IO.File]::ReadAllText($abs) | ConvertFrom-Json -ErrorAction Stop
            $testScript = $j.scripts.test
            if ($null -ne $testScript -and -not [string]::IsNullOrWhiteSpace([string]$testScript)) { $class = 'Carrier' }
            else { $class = 'Helper' }
        }
        catch { }
        $testFiles.Add([pscustomobject]@{
            Rel            = $rel
            Abs            = $abs
            Stem           = [IO.Path]::GetFileNameWithoutExtension($abs)
            Dir            = [IO.Path]::GetDirectoryName($rel)
            Class          = $class
            Kind           = 'Npm'
            ProjectName    = ''
            MigrateSources = @()
        })
    }

    # ---- 工程目录（含 csproj 的目录）----
    $sourceProjectDirs = @{}
    foreach ($abs in $files.SourceProjects) {
        $rel = Get-TestLayoutRelativePath -Base $repo -Path $abs
        if ([string]::IsNullOrWhiteSpace($rel)) { continue }
        $dir = [IO.Path]::GetDirectoryName($rel)
        if ([string]::IsNullOrWhiteSpace($dir)) { continue }
        if (-not $sourceProjectDirs.ContainsKey($dir)) { $sourceProjectDirs[$dir] = New-Object System.Collections.Generic.List[string] }
        $sourceProjectDirs[$dir].Add($abs)
    }
    $testProjectDirs = @{}
    foreach ($abs in $files.TestProjects) {
        $rel = Get-TestLayoutRelativePath -Base $repo -Path $abs
        if ([string]::IsNullOrWhiteSpace($rel)) { continue }
        $dir = [IO.Path]::GetDirectoryName($rel)
        if ([string]::IsNullOrWhiteSpace($dir)) { continue }
        if (-not $testProjectDirs.ContainsKey($dir)) { $testProjectDirs[$dir] = New-Object System.Collections.Generic.List[string] }
        $testProjectDirs[$dir].Add($abs)
    }

    # ---- 测试目录「根内后缀」索引（供 L0 候选比对）----
    $suffixToFull = @{}
    foreach ($dir in @($testProjectDirs.Keys)) {
        $root = Get-TestLayoutOwningRoot -Config $Config -RelPath $dir -Kind 'Test'
        if ($null -eq $root) { continue }
        $suffix = ''
        if ($dir.Length -gt $root.Path.Length) { $suffix = $dir.Substring($root.Path.Length + 1) }
        if (-not $suffixToFull.ContainsKey($suffix)) { $suffixToFull[$suffix] = New-Object System.Collections.Generic.List[string] }
        $suffixToFull[$suffix].Add($dir)
    }

    # ---- L0：源码工程目录 → 期望测试目录（容器段压平 / 限定叶名）----
    $projectMap = @{}
    foreach ($sd in @($sourceProjectDirs.Keys)) {
        $root = Get-TestLayoutOwningRoot -Config $Config -RelPath $sd -Kind 'Source'
        if ($null -eq $root) { continue }
        $policy = 'container-flatten'
        if ($Config.Adapters.ContainsKey($root.Adapter)) { $policy = [string]$Config.Adapters[$root.Adapter].DepthPolicy }
        $cands = @(Get-TestLayoutTestDirCandidates -SourceRelPath $sd -SourceRootPath $root.Path `
                -ReverseGroups $Config.ReverseGroups -DepthPolicy $policy)
        foreach ($c in $cands) {
            if (-not $suffixToFull.ContainsKey($c)) { continue }
            foreach ($full in @($suffixToFull[$c])) {
                if (-not $projectMap.ContainsKey($full)) { $projectMap[$full] = $sd }
            }
            break
        }
    }

    # 计数用显式循环（PowerShell 7.6 对 `@($对象列表 | Where-Object).Count` 会抛「Argument types do not match」）
    $csCarriers = 0; $specs = 0; $pester = 0; $npm = 0
    foreach ($tf in $testFiles) {
        switch ($tf.Kind) {
            'Cs' { if ($tf.Class -eq 'Carrier') { $csCarriers++ } }
            'Spec' { $specs++ }
            'Pester' { $pester++ }
            'Npm' { if ($tf.Class -eq 'Carrier') { $npm++ } }
        }
    }

    $stats = [pscustomobject]@{
        SourceFiles    = $sourceFiles.Count
        TestFiles      = $testFiles.Count
        SourceProjects = $sourceProjectDirs.Count
        TestProjects   = $testProjectDirs.Count
        SpecificTypes  = $typeToSources.Count
        L0Mapped       = $projectMap.Count
        CsCarriers     = $csCarriers
        Specs          = $specs
        PesterCarriers = $pester
        NpmCarriers    = $npm
    }

    return [pscustomobject]@{
        Config            = $Config
        RepoRoot          = $repo
        Generated         = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        SourceFiles       = $sourceFiles.ToArray()
        TestFiles         = $testFiles.ToArray()
        SourceProjectDirs = $sourceProjectDirs
        TestProjectDirs   = $testProjectDirs
        ProjectMap        = $projectMap
        StemToSources     = $stemToSources
        TypeToSources     = $typeToSources
        TypeDeclaringCounts = $typeDeclaringCounts
        Stats             = $stats
    }
}

# ============================================================
#  对齐测量（投影：L0/L1/L2/L3 → filemap）
# ============================================================

function Resolve-TestLayoutLocator {
    <#
    .SYNOPSIS
        解析 `scheme:Root/rel` 定位符（src / tst / spec / pkg）。
    .DESCRIPTION
        · scheme 必须在账本 `Capabilities.Schemes` 内，Root 必须存在；
        · src ⇒ Source 根；tst/spec/pkg ⇒ Test 根（Kind 不符即不成立）；
        · `Rel` = 仓库相对路径；`Exists` 单独给出（悬空是可判定事实，不在此静默吞掉）。
    .OUTPUTS
        { Ok; Reason; Scheme; Kind('Source'|'Test'); RootKey; Rel; Abs; Exists }
    #>
    param(
        [Parameter(Mandatory)]$Config,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Value
    )

    $result = [pscustomobject]@{ Ok = $false; Reason = ''; Scheme = ''; Kind = ''; RootKey = ''; Rel = ''; Abs = ''; Exists = $false }
    $v = $Value.Trim()
    if ([string]::IsNullOrWhiteSpace($v)) { $result.Reason = '空定位符'; return $result }
    $m = [regex]::Match($v, '^(?<s>[A-Za-z]+):(?<r>[^/\\]+)[/\\](?<p>.+)$')
    if (-not $m.Success) { $result.Reason = '格式非 scheme:Root/rel'; return $result }

    $scheme = $m.Groups['s'].Value.ToLowerInvariant()
    if (@($Config.Capabilities.Schemes) -notcontains $scheme) { $result.Reason = "未知 scheme：$scheme"; return $result }
    $rootKey = $m.Groups['r'].Value
    if (-not $Config.RootByKey.ContainsKey($rootKey)) { $result.Reason = "未知根：$rootKey"; return $result }
    $root = $Config.RootByKey[$rootKey]
    $wantKind = $(if ($scheme -eq 'src') { 'Source' } else { 'Test' })
    if ($root.Kind -ne $wantKind) { $result.Reason = "scheme '$scheme' 与根 '$rootKey' 的 Kind（$($root.Kind)）不符"; return $result }

    $rel = ConvertTo-TestLayoutPath -RepoRoot $Config.RepoRoot -Path (Join-Path $root.AbsPath $m.Groups['p'].Value)
    if ([string]::IsNullOrWhiteSpace($rel) -or -not $rel.StartsWith($root.Path, [StringComparison]::OrdinalIgnoreCase)) {
        $result.Reason = '定位符越出所属根'; return $result
    }

    $result.Ok = $true
    $result.Scheme = $scheme
    $result.Kind = $root.Kind
    $result.RootKey = $rootKey
    $result.Rel = $rel
    $result.Abs = (Join-Path $Config.RepoRoot $rel)
    $result.Exists = (Test-Path -LiteralPath $result.Abs)
    return $result
}

function Resolve-TestLayoutEvidenceTarget {
    <#
    .SYNOPSIS
        把一条 L3 声明值解析为「真实存在的源码文件」；目录展开为其下源码文件。
    .DESCRIPTION
        U7 口径：声明值必须指向**存在的文件/目录**，否则 `Exists=$false`（调用方负责可见化/判红）。
        指向测试文件时，取该测试文件已推导出的 Sources（`migrate source` 的历史文件即此形态）。
        既非源码也非已登记测试的文件 ⇒ 不产生证据（避免用非源码制品伪装归属）。
    .OUTPUTS
        { Exists; Targets(string[]); Reason }
    #>
    param(
        [Parameter(Mandatory)]$Config,
        [Parameter(Mandatory)]$Index,
        [Parameter(Mandatory)][AllowEmptyCollection()][hashtable]$TestRecords,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Value
    )

    $out = New-Object System.Collections.Generic.List[string]
    $rels = @(Resolve-TestLayoutCoverTarget -Config $Config -Value $Value)
    if (@($rels).Count -eq 0) {
        return [pscustomobject]@{ Exists = $false; Targets = @(); Reason = '声明值不可解析' }
    }

    $exists = $false
    foreach ($rel in $rels) {
        if ([string]::IsNullOrWhiteSpace([string]$rel)) { continue }
        $rel = ([string]$rel).TrimEnd('\')
        $abs = Join-Path $Config.RepoRoot $rel
        if (Test-Path -LiteralPath $abs -PathType Container) {
            $exists = $true
            $dirPrefix = $rel + '\'
            foreach ($s in $Index.SourceFiles) {
                if ($s.Rel.StartsWith($dirPrefix, [StringComparison]::OrdinalIgnoreCase)) { $out.Add([string]$s.Rel) }
            }
            continue
        }
        if (-not (Test-Path -LiteralPath $abs -PathType Leaf)) { continue }
        $exists = $true
        $isSource = $false
        foreach ($s in $Index.SourceFiles) { if ($s.Rel -ieq $rel) { $isSource = $true; break } }
        if ($isSource) { $out.Add($rel); continue }
        if ($TestRecords.ContainsKey($rel)) {
            foreach ($x in @($TestRecords[$rel].Sources)) { $out.Add([string]$x) }
        }
    }

    return [pscustomobject]@{
        Exists  = $exists
        Targets = @($out | Select-Object -Unique)
        Reason  = $(if ($exists) { '' } else { '目标不存在（悬空声明）' })
    }
}

function Measure-TestLayoutAlignment {
    <#
    .SYNOPSIS
        计算文件级投影（不写盘）：Sources→Tests / Tests→Sources / 统计 / 未决项。
    .DESCRIPTION
        · 投影非双射：多候选是正常结果，不判唯一；
        · I3 收敛：L1/L2 候选必须落在该测试工程 L0 对应的源码工程目录内；
        · L3 声明（`// covers:` / `[Covers]` / `.specs` 的 `migrate source` / Pester `# covers:` / npm `testLayout.covers`）可跨目录；
        · `.specs` 的 `project` 指令给工程（叶名必须唯一），`migrate source` 给证据；
        · Overrides（Kind='Edge'）优先级最高：强制投影并标记 `Evidence='Override'`（可见化，禁静默）。
    #>
    param(
        [Parameter(Mandatory)]$Index
    )

    $config = $Index.Config
    $minTypeHits = [int]$config.Conventions.Evidence.MinTypeHits
    $maxDecl = [int]$config.Conventions.Evidence.MaxDeclaringFile
    $idPattern = $script:TestLayoutDefaults.IdentifierPattern
    $coverComment = $script:TestLayoutDefaults.CoverCommentPattern
    $coverAttr = $script:TestLayoutDefaults.CoverAttributePattern
    $pesterCover = $script:TestLayoutDefaults.PesterCoverCommentPattern

    $ignoredTypes = @{}
    foreach ($x in @($config.Conventions.Evidence.IgnoredTypes)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$x)) { $ignoredTypes[[string]$x] = $true }
    }

    # ---- 测试工程叶名 → 目录（spec `project` 指令用；多命中 ⇒ 不猜）----
    $projectByLeaf = @{}
    foreach ($d in @($Index.TestProjectDirs.Keys)) {
        $leaf = [IO.Path]::GetFileName([string]$d)
        if (-not $projectByLeaf.ContainsKey($leaf)) { $projectByLeaf[$leaf] = New-Object System.Collections.Generic.List[string] }
        $projectByLeaf[$leaf].Add([string]$d)
    }

    $testProjectDirsSorted = @($Index.TestProjectDirs.Keys | Sort-Object { $_.Length } -Descending)
    $testRecords = @{}
    $sourceToTests = @{}
    $unowned = New-Object System.Collections.Generic.List[object]
    $specDangling = New-Object System.Collections.Generic.List[object]
    $evidenceCounts = @{ L1 = 0; L2 = 0; L3 = 0 }
    $typeRefCounts = @{}

    # ---- U4：Overrides(Kind='Edge') 预解析（From=tst，To=src；To 可为目录）----
    $forced = @{}
    $overrideEdges = New-Object System.Collections.Generic.List[object]
    foreach ($o in @($config.Overrides)) {
        if ($null -eq $o) { continue }
        if ([string]$o.Kind -ne 'Edge') { continue }
        $from = Resolve-TestLayoutLocator -Config $config -Value ([string]$o.From)
        $to = Resolve-TestLayoutLocator -Config $config -Value ([string]$o.To)
        $edge = [pscustomobject]@{
            Kind = 'Edge'; From = [string]$o.From; To = [string]$o.To
            FromRel = $from.Rel; ToRel = $to.Rel
            Applied = $false; Reason = ''
        }
        if (-not $from.Ok) { $edge.Reason = "From 不可解析：$($from.Reason)"; $overrideEdges.Add($edge); continue }
        if (-not $to.Ok) { $edge.Reason = "To 不可解析：$($to.Reason)"; $overrideEdges.Add($edge); continue }
        if ($from.Kind -ne 'Test') { $edge.Reason = 'From 必须是测试侧定位符（tst:/spec:/pkg:）'; $overrideEdges.Add($edge); continue }
        if ($to.Kind -ne 'Source') { $edge.Reason = 'To 必须是源码侧定位符（src:）'; $overrideEdges.Add($edge); continue }
        if (-not $to.Exists) { $edge.Reason = 'To 目标不存在（悬空 override）'; $overrideEdges.Add($edge); continue }

        $targets = New-Object System.Collections.Generic.List[string]
        if (Test-Path -LiteralPath $to.Abs -PathType Container) {
            $dirPrefix = $to.Rel.TrimEnd('\') + '\'
            foreach ($s in $Index.SourceFiles) {
                if ($s.Rel.StartsWith($dirPrefix, [StringComparison]::OrdinalIgnoreCase)) { $targets.Add([string]$s.Rel) }
            }
        }
        else { $targets.Add($to.Rel) }

        if ($targets.Count -eq 0) { $edge.Reason = 'To 目录内无源码文件'; $overrideEdges.Add($edge); continue }
        if (-not $forced.ContainsKey($from.Rel)) { $forced[$from.Rel] = New-Object System.Collections.Generic.List[string] }
        foreach ($x in $targets) { if (-not $forced[$from.Rel].Contains($x)) { $forced[$from.Rel].Add($x) } }
        $overrideEdges.Add($edge)
    }

    foreach ($t in @($Index.TestFiles | Sort-Object Kind)) {
        if ($t.Class -ne 'Carrier') { continue }

        $projDir = $null
        $projReason = ''
        if ($t.Kind -eq 'Spec') {
            if ([string]::IsNullOrWhiteSpace($t.ProjectName)) { $projReason = 'spec 缺 project 指令' }
            elseif (-not $projectByLeaf.ContainsKey($t.ProjectName)) { $projReason = "project 指令未命中测试工程：$($t.ProjectName)" }
            elseif (@($projectByLeaf[$t.ProjectName]).Count -ne 1) { $projReason = "project 指令叶名多命中（不猜）：$($t.ProjectName)" }
            else { $projDir = [string]@($projectByLeaf[$t.ProjectName])[0] }
        }
        elseif ($t.Kind -eq 'Pester' -or $t.Kind -eq 'Npm') { $projDir = $t.Dir }
        else {
            foreach ($d in $testProjectDirsSorted) {
                if ($t.Dir -ieq $d -or $t.Dir.StartsWith($d + '\', [StringComparison]::OrdinalIgnoreCase)) { $projDir = $d; break }
            }
        }
        $sourceDir = ''
        if ($projDir -and $Index.ProjectMap.ContainsKey($projDir)) { $sourceDir = [string]$Index.ProjectMap[$projDir] }
        $prefix = ''
        if ($sourceDir) { $prefix = $sourceDir + '\' }

        $found = New-Object System.Collections.Generic.List[string]
        $evidence = New-Object System.Collections.Generic.List[string]
        $seen = New-Object System.Collections.Generic.HashSet[string]

        # ---- L0 ----
        if ($sourceDir) { $evidence.Add('L0:path') }

        # ---- L1 词干（.cs / Pester；spec 词干不是类型名，跳过）----
        if ($t.Kind -ne 'Spec') {
            $stem = ($t.Stem -replace '(?i)(Tests?|Spec)$', '')
            if (-not [string]::IsNullOrWhiteSpace($stem)) {
                $key = $stem.ToLowerInvariant()
                if ($Index.StemToSources.ContainsKey($key)) {
                    $before = $found.Count
                    foreach ($s in @($Index.StemToSources[$key])) {
                        if ($prefix -and -not $s.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { continue }
                        if ($seen.Add($s)) { $found.Add($s) }
                    }
                    if ($found.Count -gt $before) { $evidence.Add('L1:stem'); $evidenceCounts.L1++ }
                }
            }
        }

        # ---- L2 内容（声明类型倒排，特异性过滤 + I3 收敛；.cs / .specs）----
        $content = ''
        try { $content = [IO.File]::ReadAllText($t.Abs) } catch { $content = '' }
        if ($t.Kind -eq 'Cs' -or $t.Kind -eq 'Spec') {
            $fileTypes = New-Object System.Collections.Generic.HashSet[string]
            foreach ($m in [regex]::Matches($content, $idPattern)) {
                $id = $m.Value
                if (-not $Index.TypeToSources.ContainsKey($id)) { continue }
                if ($ignoredTypes.ContainsKey($id)) { continue }
                [void]$fileTypes.Add($id)
                foreach ($s in @($Index.TypeToSources[$id])) {
                    if ($prefix -and -not $s.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { continue }
                    if ($seen.Add($s)) { $found.Add($s) }
                }
            }
            foreach ($id in @($fileTypes)) { if ($typeRefCounts.ContainsKey($id)) { $typeRefCounts[$id]++ } else { $typeRefCounts[$id] = 1 } }
            if ($fileTypes.Count -gt 0) { $evidence.Add('L2:type'); $evidenceCounts.L2++ }
        }

        # ---- L3 显式声明（存在性校验；可跨工程；目录展开）----
        $l3 = $false
        if ($t.Kind -eq 'Cs' -or $t.Kind -eq 'Pester') {
            $covers = New-Object System.Collections.Generic.List[string]
            if ($t.Kind -eq 'Cs') {
                foreach ($m in [regex]::Matches($content, $coverComment)) { if ($m.Groups['p'].Success) { $covers.Add($m.Groups['p'].Value) } }
                foreach ($m in [regex]::Matches($content, $coverAttr)) { if ($m.Groups['p'].Success) { $covers.Add($m.Groups['p'].Value) } }
            }
            else { foreach ($m in [regex]::Matches($content, $pesterCover)) { if ($m.Groups['p'].Success) { $covers.Add($m.Groups['p'].Value) } } }
            foreach ($raw in $covers) {
                $r = Resolve-TestLayoutEvidenceTarget -Config $config -Index $Index -TestRecords $testRecords -Value $raw
                foreach ($s in @($r.Targets)) { if ($seen.Add($s)) { $found.Add($s); $l3 = $true } }
            }
            if ($l3) { $evidence.Add('L3:covers'); $evidenceCounts.L3++ }
        }
        elseif ($t.Kind -eq 'Spec') {
            foreach ($raw in @($t.MigrateSources)) {
                $r = Resolve-TestLayoutEvidenceTarget -Config $config -Index $Index -TestRecords $testRecords -Value $raw
                if (-not $r.Exists) {
                    $specDangling.Add([pscustomobject]@{ Path = $t.Rel; Project = $t.ProjectName; Target = $raw; Reason = $r.Reason })
                    continue
                }
                foreach ($s in @($r.Targets)) { if ($seen.Add($s)) { $found.Add($s); $l3 = $true } }
            }
            if ($l3) { $evidence.Add('L3:migrate-source'); $evidenceCounts.L3++ }
        }
        elseif ($t.Kind -eq 'Npm') {
            try {
                $j = $content | ConvertFrom-Json -ErrorAction Stop
                foreach ($raw in @($j.testLayout.covers)) {
                    if ([string]::IsNullOrWhiteSpace([string]$raw)) { continue }
                    $r = Resolve-TestLayoutEvidenceTarget -Config $config -Index $Index -TestRecords $testRecords -Value ([string]$raw)
                    foreach ($s in @($r.Targets)) { if ($seen.Add($s)) { $found.Add($s); $l3 = $true } }
                }
            }
            catch { }
            if ($l3) { $evidence.Add('L3:covers'); $evidenceCounts.L3++ }
        }

        # ---- U4：Edge override（最高优先级，标记可见）----
        if ($forced.ContainsKey($t.Rel)) {
            foreach ($s in @($forced[$t.Rel])) { if ($seen.Add($s)) { $found.Add($s) } }
            $evidence.Add('Override')
        }

        $rec = [pscustomobject]@{
            Path      = $t.Rel
            Class     = $t.Class
            Project   = $(if ($projDir) { $projDir } else { '' })
            SourceDir = $sourceDir
            Sources   = @($found)
            Evidence  = @($evidence)
        }
        $testRecords[$t.Rel] = $rec
        foreach ($s in $found) {
            if (-not $sourceToTests.ContainsKey($s)) { $sourceToTests[$s] = New-Object System.Collections.Generic.HashSet[string] }
            [void]$sourceToTests[$s].Add($t.Rel)
        }
        if ($found.Count -eq 0) {
            $reason = 'L1/L2/L3 均无证据'
            if ($t.Kind -eq 'Spec' -and $projReason) { $reason = $projReason }
            $unowned.Add([pscustomobject]@{ Path = $t.Rel; Project = $projDir; Reason = $reason })
        }
    }

    # override 生效判定（记录落定后回填，供校验门；禁「声明了但没生效」静默）
    foreach ($e in $overrideEdges) {
        if ($e.Reason) { continue }
        if ($testRecords.ContainsKey($e.FromRel) -and @($testRecords[$e.FromRel].Evidence) -contains 'Override') { $e.Applied = $true }
    }

    # ---- 汇总源码侧（覆盖/缺测/豁免）----
    $sourceRecords = @{}
    $testable = 0; $exempt = 0; $covered = 0; $uncovered = 0
    foreach ($s in $Index.SourceFiles) {
        $tests = @()
        $ev = @()
        if ($sourceToTests.ContainsKey($s.Rel)) {
            $tests = @($sourceToTests[$s.Rel])
            foreach ($tr in $tests) {
                if (-not $testRecords.ContainsKey($tr)) { continue }
                foreach ($e in $testRecords[$tr].Evidence) {
                    if ($e -like 'L0:*') { continue }
                    if ($ev -notcontains $e) { $ev += $e }
                }
            }
        }
        if ($s.Class -ne 'Testable') { $exempt++ }
        else { $testable++; if ($tests.Count -gt 0) { $covered++ } else { $uncovered++ } }
        $sourceRecords[$s.Rel] = [pscustomobject]@{ Path = $s.Rel; Class = $s.Class; Tests = $tests; Evidence = $ev }
    }

    # ---- U11：候选数直方图（每测试文件 Sources 数 + Top 被引用类型）----
    $buckets = [ordered]@{ '0' = 0; '1' = 0; '2' = 0; '3' = 0; '4' = 0; '5-10' = 0; '>10' = 0 }
    $over10 = 0
    foreach ($r in $testRecords.Values) {
        $n = @($r.Sources).Count
        if ($n -gt 10) { $buckets['>10']++; $over10++ }
        elseif ($n -ge 5) { $buckets['5-10']++ }
        else { $buckets[[string]$n]++ }
    }
    $ratio = $(if ($testRecords.Count -gt 0) { [math]::Round(($over10 * 100.0) / $testRecords.Count, 2) } else { 0 })
    $topTypes = @($typeRefCounts.Keys |
            Sort-Object @{ Expression = { -[int]$typeRefCounts[$_] } }, @{ Expression = { $_ } } |
            Select-Object -First 15 |
            ForEach-Object { [pscustomobject]@{ Type = [string]$_; TestFiles = [int]$typeRefCounts[$_]; DeclaringFiles = [int]$Index.TypeDeclaringCounts[$_] } })
    $histogram = [pscustomobject]@{
        Carriers         = $testRecords.Count
        Buckets          = [pscustomobject]$buckets
        Over10           = $over10
        Over10Ratio      = $ratio
        TopTypes         = $topTypes
        MinTypeHits      = $minTypeHits
        MaxDeclaringFile = $maxDecl
        IgnoredTypes     = @($config.Conventions.Evidence.IgnoredTypes)
    }

    $helperCount = 0
    foreach ($tf in $Index.TestFiles) { if ($tf.Class -eq 'Helper') { $helperCount++ } }

    $stats = [pscustomobject]@{
        SourceFiles  = $Index.SourceFiles.Count
        Testable     = $testable
        Exempt       = $exempt
        Covered      = $covered
        Uncovered    = $uncovered
        Carriers     = $testRecords.Count
        Helpers      = $helperCount
        Unowned      = $unowned.Count
        L1           = $evidenceCounts.L1
        L2           = $evidenceCounts.L2
        L3           = $evidenceCounts.L3
        Specs        = $Index.Stats.Specs
        SpecDangling = $specDangling.Count
        Pester       = $Index.Stats.PesterCarriers
        Npm          = $Index.Stats.NpmCarriers
        Overrides    = $overrideEdges.Count
    }

    return [pscustomobject]@{
        RepoRoot         = $Index.RepoRoot
        Generated        = $Index.Generated
        Config           = $config
        Tests            = $testRecords
        Sources          = $sourceRecords
        SourceToTests    = $sourceToTests
        Unowned          = $unowned.ToArray()
        SpecDangling     = $specDangling.ToArray()
        OverrideEdges    = $overrideEdges.ToArray()
        Histogram        = $histogram
        Evidence         = $evidenceCounts
        Stats            = $stats
        TestProjects     = @($Index.TestProjectDirs.Keys)
        SourceProjects   = @($Index.SourceProjectDirs.Keys)
        ProjectMap       = $Index.ProjectMap
        UnmappedProjects = @($Index.TestProjectDirs.Keys | Where-Object { -not $Index.ProjectMap.ContainsKey($_) })
    }
}

function Resolve-TestLayoutCoverTarget {
    <#
    .SYNOPSIS
        解析 L3 声明值：`src:Root/rel` 定位符 或 仓库相对路径。
    #>
    param(
        [Parameter(Mandatory)]$Config,
        [Parameter(Mandatory)][string]$Value
    )

    $v = $Value.Trim().Trim('"').Trim()
    if ([string]::IsNullOrWhiteSpace($v)) { return @() }

    $m = [regex]::Match($v, '^(?<s>[a-z]+):(?<r>[^/\\]+)[/\\](?<p>.+)$')
    if ($m.Success) {
        $rootKey = $m.Groups['r'].Value
        if ($Config.RootByKey.ContainsKey($rootKey)) {
            $root = $Config.RootByKey[$rootKey]
            return @($root.Path + '\' + $m.Groups['p'].Value.Replace('/', '\'))
        }
        return @()
    }

    $rel = ConvertTo-TestLayoutPath -RepoRoot $Config.RepoRoot -Path $v
    if ([string]::IsNullOrWhiteSpace($rel)) { return @() }
    return @($rel)
}

function Resolve-TestLayoutPath {
    <#
    .SYNOPSIS
        文件级位置化选择：路径（文件/目录/工程）→ 候选测试工程 + 证据。
    .DESCRIPTION
        · 不做唯一归属判定：多候选按「文件」返回，工程去重后给运行面；
        · 零证据 ⇒ Unmapped（fail-loud；不回落全量）；
        · -AllowProjectFallback 仅给**运行面**用（工程级兜底），门禁必须关掉。
    #>
    param(
        [Parameter(Mandatory)]$FileMap,
        [Parameter(Mandatory)][string[]]$Path,
        [switch]$AllowProjectFallback
    )

    $repo = $FileMap.RepoRoot
    $selections = New-Object System.Collections.Generic.List[object]
    $unmapped = New-Object System.Collections.Generic.List[object]

    $testsBySourceProject = @{}
    foreach ($k in @($FileMap.ProjectMap.Keys)) {
        $sd = [string]$FileMap.ProjectMap[$k]
        if (-not $testsBySourceProject.ContainsKey($sd)) { $testsBySourceProject[$sd] = New-Object System.Collections.Generic.List[string] }
        $testsBySourceProject[$sd].Add($k)
    }
    $sourceProjectsSorted = @($FileMap.SourceProjects | Sort-Object { $_.Length } -Descending)
    $testProjectsSorted = @($FileMap.TestProjects | Sort-Object { $_.Length } -Descending)

    foreach ($raw in $Path) {
        $rel = ConvertTo-TestLayoutPath -RepoRoot $repo -Path $raw
        if ([string]::IsNullOrWhiteSpace($rel)) {
            $unmapped.Add([pscustomobject]@{ Input = $raw; Path = ''; Reason = '路径为空或越出仓库根' })
            continue
        }

        $testFiles = New-Object System.Collections.Generic.List[string]
        $projects = New-Object System.Collections.Generic.List[string]
        $evidence = New-Object System.Collections.Generic.List[string]
        $kind = ''
        $reason = ''

        if ($FileMap.Tests.ContainsKey($rel)) {
            # 统一：`.cs` / `.specs` / `*.Tests.ps1` / `package.json` 载体
            $kind = 'TestFile'
            $rec = $FileMap.Tests[$rel]
            $testFiles.Add($rel)
            if ($rec.Project) { $projects.Add([string]$rec.Project) }
            foreach ($e in @($rec.Evidence)) { $evidence.Add([string]$e) }
            if (@($rec.Sources).Count -eq 0) { $reason = '测试文件无源码投影（FA2 候选）' }
        }
        elseif ($rel -match '\.specs$') {
            $kind = 'SpecFile'
            $reason = 'spec 不在任何已登记测试根'
        }
        elseif ($rel -match '\.cs$') {
            if ($FileMap.Sources.ContainsKey($rel)) {
                $kind = 'SourceFile'
                $rec = $FileMap.Sources[$rel]
                if ($rec.Class -ne 'Testable') { $reason = "源码已豁免：$($rec.Class)" }
                foreach ($tf in @($rec.Tests)) {
                    if (-not $FileMap.Tests.ContainsKey($tf)) { continue }
                    if (-not $FileMap.Tests[$tf].SourceDir) { continue }
                    $testFiles.Add([string]$tf)
                    if ($FileMap.Tests[$tf].Project) { $projects.Add([string]$FileMap.Tests[$tf].Project) }
                }
                foreach ($e in @($rec.Evidence)) { $evidence.Add([string]$e) }
                if ($testFiles.Count -eq 0 -and $AllowProjectFallback) {
                    $sp = ''
                    foreach ($d in $sourceProjectsSorted) { if ($rel.StartsWith($d + '\', [StringComparison]::OrdinalIgnoreCase)) { $sp = $d; break } }
                    if ($sp -and $testsBySourceProject.ContainsKey($sp)) {
                        foreach ($tp in @($testsBySourceProject[$sp])) { $projects.Add([string]$tp) }
                        $evidence.Add('L0:project-fallback')
                        $reason = '文件级零证据，回落工程级（投影缺口）'
                    }
                }
            }
            else {
                $kind = 'UnknownFile'
                $reason = '不在任何已登记源码根/测试根下'
            }
        }
        else {
            $kind = 'Directory'
            if ($rel -match '\.csproj$') { $kind = 'Project'; $rel = [IO.Path]::GetDirectoryName($rel) }

            $tp = ''
            foreach ($d in $testProjectsSorted) {
                if ($rel -ieq $d -or $rel.StartsWith($d + '\', [StringComparison]::OrdinalIgnoreCase)) { $tp = $d; break }
            }
            if ($tp) {
                foreach ($k in @($FileMap.Tests.Keys)) {
                    $tr = [string]$k
                    if (-not $tr.StartsWith($tp + '\', [StringComparison]::OrdinalIgnoreCase)) { continue }
                    $testFiles.Add($tr)
                    if ($FileMap.Tests[$tr].Project) { $projects.Add([string]$FileMap.Tests[$tr].Project) }
                    foreach ($e in @($FileMap.Tests[$tr].Evidence)) { $evidence.Add([string]$e) }
                }
                if ($projects.Count -eq 0) { $reason = '测试目录内无载体文件' }
            }
            else {
                # 目录语义 = 该目录下「源码工程」的 L0 测试工程（工程级，噪声最小）
                $srcProjUnder = @($sourceProjectsSorted | Where-Object {
                        $rel -ieq $_ -or $_.StartsWith($rel.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)
                    })
                if ($srcProjUnder.Count -gt 0) {
                    foreach ($sp in $srcProjUnder) {
                        if (-not $testsBySourceProject.ContainsKey($sp)) { continue }
                        foreach ($t in @($testsBySourceProject[$sp])) { $projects.Add([string]$t) }
                    }
                    if ($projects.Count -gt 0) {
                        foreach ($k in @($FileMap.Tests.Keys)) {
                            if ($projects -notcontains [string]$FileMap.Tests[$k].Project) { continue }
                            $testFiles.Add([string]$k)
                            foreach ($e in @($FileMap.Tests[$k].Evidence)) { $evidence.Add([string]$e) }
                        }
                    }
                    else {
                        $reason = '目录下源码工程无 L0 测试工程（FA1/缺测候选）'
                    }
                }
                else {
                    # 非工程目录（源码根/容器段）：按文件级证据聚合，仅取 I3 已锚定的测试
                    foreach ($k in @($FileMap.Sources.Keys)) {
                        $sr = [string]$k
                        if (-not ($sr -ieq $rel -or $sr.StartsWith($rel.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase))) { continue }
                        foreach ($tf in @($FileMap.Sources[$sr].Tests)) {
                            if (-not $FileMap.Tests.ContainsKey($tf)) { continue }
                            if (-not $FileMap.Tests[$tf].SourceDir) { continue }
                            $testFiles.Add([string]$tf)
                            if ($FileMap.Tests[$tf].Project) { $projects.Add([string]$FileMap.Tests[$tf].Project) }
                        }
                        foreach ($e in @($FileMap.Sources[$sr].Evidence)) { $evidence.Add([string]$e) }
                    }
                    if ($testFiles.Count -eq 0 -and $AllowProjectFallback) {
                        $sp = ''
                        foreach ($d in $sourceProjectsSorted) {
                            if ($rel -ieq $d -or $rel.StartsWith($d + '\', [StringComparison]::OrdinalIgnoreCase)) { $sp = $d; break }
                        }
                        if ($sp -and $testsBySourceProject.ContainsKey($sp)) {
                            foreach ($t in @($testsBySourceProject[$sp])) { $projects.Add([string]$t) }
                            $evidence.Add('L0:project-fallback')
                            $reason = '目录内源码文件零证据，回落工程级'
                        }
                    }
                    if ($projects.Count -eq 0 -and -not $reason) { $reason = '目录内无任何源码/测试证据' }
                }
            }
        }

        $uniqProjects = @($projects | Select-Object -Unique)
        $sel = [pscustomobject]@{
            Input        = $raw
            Path         = $rel
            Kind         = $kind
            TestProjects = $uniqProjects
            TestFiles    = @($testFiles | Select-Object -Unique)
            Evidence     = @($evidence | Select-Object -Unique)
            Reason       = $reason
        }
        if ($uniqProjects.Count -eq 0) {
            $unmapped.Add([pscustomobject]@{ Input = $raw; Path = $rel; Reason = $(if ($reason) { $reason } else { '未解析到任何测试工程' }) })
        }
        else { $selections.Add($sel) }
    }

    return [pscustomobject]@{
        Selections = $selections.ToArray()
        Unmapped   = $unmapped.ToArray()
    }
}

function ConvertTo-TestLayoutFileMapJson {
    <#
    .SYNOPSIS
        filemap → JSON 文本（派生件；可冻结/可 diff）。
    #>
    param([Parameter(Mandatory)]$FileMap)

    $sources = @{}
    foreach ($k in @($FileMap.Sources.Keys)) {
        $r = $FileMap.Sources[$k]
        $sources[[string]$k] = [ordered]@{ class = $r.Class; tests = @($r.Tests); evidence = @($r.Evidence) }
    }
    $tests = @{}
    foreach ($k in @($FileMap.Tests.Keys)) {
        $r = $FileMap.Tests[$k]
        $tests[[string]$k] = [ordered]@{ class = $r.Class; project = $r.Project; sources = @($r.Sources); evidence = @($r.Evidence) }
    }

    $s = $FileMap.Stats
    $doc = [ordered]@{
        schema           = 'test-layout-filemap-v1'
        generated        = $FileMap.Generated
        stats            = [ordered]@{
            sourceFiles = $s.SourceFiles
            testable    = $s.Testable
            exempt      = $s.Exempt
            covered     = $s.Covered
            uncovered   = $s.Uncovered
            carriers    = $s.Carriers
            helpers     = $s.Helpers
            unowned     = $s.Unowned
            l1          = $s.L1
            l2          = $s.L2
            l3          = $s.L3
            specs       = $s.Specs
            specDangling = $s.SpecDangling
            pester      = $s.Pester
            npm         = $s.Npm
            overrides   = $s.Overrides
        }
        sources          = $sources
        tests            = $tests
        unowned          = @($FileMap.Unowned)
        unmappedProjects = @($FileMap.UnmappedProjects)
        specDangling     = @($FileMap.SpecDangling)
        overrideEdges    = @($FileMap.OverrideEdges)
        histogram        = $FileMap.Histogram
    }
    return ($doc | ConvertTo-Json -Depth 12)
}

Export-ModuleMember -Function @(
    'ConvertTo-TestLayoutPath',
    'Get-TestLayoutRelativePath',
    'Read-TestLayoutLedger',
    'New-TestLayoutConfig',
    'Get-TestLayoutSourceClass',
    'Get-TestLayoutTestClass',
    'Get-TestLayoutFiles',
    'Get-TestLayoutOwningRoot',
    'Get-TestLayoutTestDirCandidates',
    'New-TestLayoutIndex',
    'Measure-TestLayoutAlignment',
    'Resolve-TestLayoutCoverTarget',
    'Resolve-TestLayoutLocator',
    'Resolve-TestLayoutEvidenceTarget',
    'Resolve-TestLayoutPath',
    'ConvertTo-TestLayoutFileMapJson'
)








