#requires -Version 5.1
<#
.SYNOPSIS
    ConstraintIndex —— 语义字段使用索引（FieldUsageIndex）的生成/新鲜度/门禁期缓存。

.DESCRIPTION
    存在形态：**门禁期产物 + 进程内状态**。
      · 产物 = `Environment\CentralTest\BuildOutput\<Configuration>\ConstraintIndex\FieldUsageIndex.json`（不入库）；
      · 生成 = 离线 CLI `Frame\CsharpPrj\Plate\RoslynMethod.Tools\FieldUsageIndex`（语义模型）；
      · 门禁侧只读产物，并按**逐文件 SHA256 + 文件集合相等 + 生成器版本**判新鲜度；过期/缺失 ⇒ fail-closed。

    复用（新建前已查）：
      · 源文件枚举：ConstraintFieldIndex.Get-ConstraintFieldFiles（与既有门禁同一排除集单源）。
      · 声明面/分级表：ConstraintWall.settings.psd1 + ConstraintFieldClassification.psd1（唯一真源）。
      · 语义门面：RoslynMethod.Core CompilationSkeleton.BuildFromTrees（不另造编译门面）。
      · 单源指纹：ArtifactFreshness 的逐文件 SHA256 口径（本模块只做多输入组合，不改其 1:1 契约）。

    并发：跨进程命名 Mutex 串行；五条门禁共享同一产物，过期时只重算一次。
#>

$script:StateCache = @{}

function Get-FileSha256Bytes
{
    param([Parameter(Mandatory = $true)][string]$Path)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try
    {
        $bytes = [IO.File]::ReadAllBytes($Path)
        return ([BitConverter]::ToString($sha.ComputeHash($bytes)) -replace '-', '').ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function Get-TextSha256
{
    param([Parameter(Mandatory = $true)][string]$Path)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try
    {
        $text = [IO.File]::ReadAllText($Path)
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($text)
        return ([BitConverter]::ToString($sha.ComputeHash($bytes)) -replace '-', '').ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function Get-ConstraintIndexSettings
{
    param([Parameter(Mandatory = $true)][string]$Root)
    $p = Join-Path $Root 'Frame\PowerShellScript\Modules\Constraint\ConstraintWall.settings.psd1'
    if (-not (Test-Path -LiteralPath $p)) { throw "ConstraintIndex: 缺判据登记表 $p" }
    return Import-PowerShellDataFile -LiteralPath $p
}

function Import-ConstraintIndexDependencies
{
    param([Parameter(Mandatory = $true)][string]$Root)
    if (-not (Get-Command Get-ConstraintFieldFiles -ErrorAction SilentlyContinue))
    {
        $p = Join-Path $Root 'Frame\PowerShellScript\Modules\Constraint\ConstraintFieldIndex.psm1'
        if (-not (Test-Path -LiteralPath $p)) { throw "ConstraintIndex: 缺字段索引模块 $p" }
        Import-Module -Name $p -Force -DisableNameChecking
    }
    if (-not (Get-Command Get-ConstraintFieldDeclarations -ErrorAction SilentlyContinue))
    {
        $p = Join-Path $Root 'Frame\PowerShellScript\Modules\Constraint\ConstraintFieldIndex.psm1'
        Import-Module -Name $p -Force -DisableNameChecking
    }
}

function ConvertTo-ConstraintIndexRelative
{
    param([string]$Root, [string]$Path)
    $full = [IO.Path]::GetFullPath($Path)
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) { return ($full -replace '\\', '/') }
    return ($full.Substring($rootFull.Length) -replace '\\', '/')
}

function Get-ConstraintIndexArtifactPath
{
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [string]$Configuration = 'Debug'
    )
    $settings = Get-ConstraintIndexSettings -Root $Root
    $dir = Join-Path (Join-Path $Root $settings.IndexOutputDirRel) $Configuration
    return (Join-Path $dir 'ConstraintIndex\FieldUsageIndex.json')
}

function Get-ConstraintIndexExpectation
{
    <#
    .SYNOPSIS
        复算「当前工作树应被索引的输入集」：源文件、引用、设置面。门禁与生成器共用。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [string]$Configuration = 'Debug'
    )

    Import-ConstraintIndexDependencies -Root $Root
    $settings = Get-ConstraintIndexSettings -Root $Root
    $buildOut = Join-Path (Join-Path $Root $settings.IndexBuildOutputDirRel) $Configuration
    if (-not (Test-Path -LiteralPath $buildOut -PathType Container))
    {
        throw "ConstraintIndex: BuildOutput 缺失 $buildOut（拒绝产出部分绑定索引）"
    }

    $roots = New-Object System.Collections.Generic.List[string]
    foreach ($d in @($settings.DeclarationDirs) + @($settings.ImplScopeDirs) + @($settings.CoreDirs))
    {
        if ($roots.Contains($d)) { continue }
        $roots.Add($d)
    }

    $files = New-Object System.Collections.Generic.List[string]
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($relDir in $roots)
    {
        foreach ($full in @(Get-ConstraintFieldFiles -Root $Root -RelativeDir $relDir))
        {
            if ($seen.Add([IO.Path]::GetFullPath($full))) { $files.Add([IO.Path]::GetFullPath($full)) }
        }
    }
    $files.Sort([StringComparer]::OrdinalIgnoreCase)
    if ($files.Count -eq 0) { throw 'ConstraintIndex: 源文件集为空（拒绝空转索引）' }

    $excludeNames = @{}
    foreach ($n in @($settings.IndexExcludedAssemblies)) { $excludeNames[$n + '.dll'] = $true }
    foreach ($n in @($settings.IndexIgnoredAssemblies)) { $excludeNames[$n + '.dll'] = $true }

    $refs = New-Object System.Collections.Generic.List[string]
    foreach ($f in @(Get-ChildItem -LiteralPath $buildOut -Filter '*.dll' -File -ErrorAction Stop))
    {
        if ($excludeNames.ContainsKey($f.Name)) { continue }
        $refs.Add($f.FullName)
    }
    $refs.Sort([StringComparer]::OrdinalIgnoreCase)
    if ($refs.Count -eq 0) { throw 'ConstraintIndex: 引用集为空（拒绝空转索引）' }

    $settingsFiles = @(
        (Join-Path $Root 'Frame\PowerShellScript\Modules\Constraint\ConstraintWall.settings.psd1'),
        (Join-Path $Root 'Frame\PowerShellScript\Modules\Constraint\ConstraintFieldClassification.psd1')
    )

    $decls = @(Get-ConstraintFieldDeclarations -Root $Root)
    $table = Get-ConstraintFieldClassificationTable -Root $Root

    return [pscustomobject]@{
        Root              = $Root
        Configuration     = $Configuration
        Settings          = $settings
        BuildOutputDir    = $buildOut
        ArtifactPath      = (Get-ConstraintIndexArtifactPath -Root $Root -Configuration $Configuration)
        SourceFiles       = @($files)
        References        = @($refs)
        SettingsFiles     = @($settingsFiles)
        ExpectedFields    = @($decls | ForEach-Object { [string]$_.Key })
        ConfigTypes       = @($settings.MutableConfigTypes)
        SemanticFields    = @($table.SemanticFields)
        DerivedFields     = @($table.DerivedFields)
        MutatorApis       = @($settings.IndexMutatorApis)
        ExemptTypes       = @($settings.IndexExemptTypes)
        GeneratorVersion  = [string]$settings.IndexGeneratorVersion
    }
}

function New-ConstraintIndexManifest
{
    param(
        [Parameter(Mandatory = $true)]$Expectation,
        [Parameter(Mandatory = $true)][string]$ManifestPath
    )
    $settingsInputs = @()
    foreach ($f in @($Expectation.SettingsFiles))
    {
        if (-not (Test-Path -LiteralPath $f)) { throw "ConstraintIndex: 设置面文件缺失 $f" }
        $settingsInputs += [pscustomobject]@{
            Path   = (ConvertTo-ConstraintIndexRelative -Root $Expectation.Root -Path $f)
            Sha256 = (Get-FileSha256Bytes -Path $f)
        }
    }

    $manifest = [ordered]@{
        Schema             = 'constraint-field-index-manifest-v1'
        GeneratorVersion   = $Expectation.GeneratorVersion
        RepoRoot           = $Expectation.Root
        BuildOutputDir     = $Expectation.BuildOutputDir
        SourceFiles        = @($Expectation.SourceFiles)
        References         = @($Expectation.References)
        ConfigTypes        = @($Expectation.ConfigTypes)
        ExpectedFields     = @($Expectation.ExpectedFields)
        SemanticFields     = @($Expectation.SemanticFields)
        DerivedFields      = @($Expectation.DerivedFields)
        MutatorApis        = @($Expectation.MutatorApis)
        ExemptTypes        = @($Expectation.ExemptTypes)
        ExcludedAssemblies = @($Expectation.Settings.IndexExcludedAssemblies)
        SettingsInputs     = @($settingsInputs)
    }
    $json = $manifest | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText($ManifestPath, $json, (New-Object System.Text.UTF8Encoding $false))
}

function Invoke-ConstraintFieldIndexBuild
{
    <#
    .SYNOPSIS
        构建索引工具（增量）+ 运行，产出 FieldUsageIndex.json。返回耗时与产物。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [string]$Configuration = 'Debug',
        [switch]$NoBuild
    )

    $expectation = Get-ConstraintIndexExpectation -Root $Root -Configuration $Configuration
    $sw = [Diagnostics.Stopwatch]::StartNew()

    $buildMs = 0
    $toolProj = Join-Path $Root 'Frame\CsharpPrj\Plate\RoslynMethod.Tools\FieldUsageIndex\FieldUsageIndex.csproj'
    $exe = Join-Path $expectation.BuildOutputDir 'FieldUsageIndex.exe'
    if (-not (Test-Path -LiteralPath $toolProj)) { throw "ConstraintIndex: 缺索引工具 $toolProj" }

    if (-not $NoBuild)
    {
        $buildSw = [Diagnostics.Stopwatch]::StartNew()
        $out = & dotnet build $toolProj -c $Configuration -v q --nologo 2>&1
        $buildSw.Stop()
        $buildMs = [int]$buildSw.ElapsedMilliseconds
        if ($LASTEXITCODE -ne 0) { throw "ConstraintIndex: 索引工具构建失败（exit=$LASTEXITCODE）`n$($out -join "`n")" }
    }
    if (-not (Test-Path -LiteralPath $exe))
    {
        $alt = Join-Path $Root ("Frame\CsharpPrj\Plate\RoslynMethod.Tools\FieldUsageIndex\bin\{0}\net10.0-windows\FieldUsageIndex.exe" -f $Configuration)
        if (Test-Path -LiteralPath $alt) { $exe = $alt }
    }
    if (-not (Test-Path -LiteralPath $exe)) { throw "ConstraintIndex: 未找到索引工具可执行文件（$exe）" }

    $manifestPath = [IO.Path]::GetTempFileName()
    try
    {
        New-ConstraintIndexManifest -Expectation $expectation -ManifestPath $manifestPath
        $artifactDir = Split-Path -Parent $expectation.ArtifactPath
        if (-not (Test-Path -LiteralPath $artifactDir)) { [void](New-Item -ItemType Directory -Path $artifactDir -Force) }

        $runOut = & $exe --manifest $manifestPath --output $expectation.ArtifactPath 2>&1
        if ($LASTEXITCODE -ne 0) { throw "ConstraintIndex: 索引生成失败（exit=$LASTEXITCODE）`n$($runOut -join "`n")" }
    }
    finally
    {
        Remove-Item -LiteralPath $manifestPath -Force -ErrorAction SilentlyContinue
    }

    $sw.Stop()
    return [pscustomobject]@{
        ArtifactPath = $expectation.ArtifactPath
        BuildMs      = $buildMs
        TotalMs      = [int]$sw.ElapsedMilliseconds
        ToolOutput   = @($runOut)
    }
}

function Test-ConstraintFieldIndexFresh
{
    <#
    .SYNOPSIS
        校验产物新鲜度与完整性。返回空字符串 = fresh；否则返回原因（fail-closed 门禁直接判红）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [string]$Configuration = 'Debug'
    )

    $expectation = Get-ConstraintIndexExpectation -Root $Root -Configuration $Configuration
    if (-not (Test-Path -LiteralPath $expectation.ArtifactPath))
    {
        return "字段使用索引缺失：$($expectation.ArtifactPath)（运行 Build-FieldUsageIndex.ps1）"
    }

    try { $artifact = Get-Content -LiteralPath $expectation.ArtifactPath -Raw | ConvertFrom-Json }
    catch { return "字段使用索引不可解析：$($_.Exception.Message)" }

    if ($artifact.Schema -ne 'constraint-field-usage-v1') { return "字段使用索引 schema 不符：$($artifact.Schema)" }
    if ([string]$artifact.GeneratorVersion -ne $expectation.GeneratorVersion)
    {
        return "字段使用索引生成器版本不符（产物 $($artifact.GeneratorVersion) / 登记 $($expectation.GeneratorVersion)）"
    }

    $checkSet = {
        param($Name, $ExpectedPaths, $Records, [scriptblock]$Hash)
        $actual = @{}
        foreach ($r in @($Records)) { $actual[[string]$r.Path] = [string]$r.Sha256 }
        $expected = @{}
        foreach ($p in @($ExpectedPaths)) { $expected[(ConvertTo-ConstraintIndexRelative -Root $Root -Path $p)] = $p }
        if ($expected.Count -ne $actual.Count) { return "$Name 文件集合与产物不一致（当前 $($expected.Count) / 产物 $($actual.Count)）" }
        foreach ($rel in $expected.Keys)
        {
            if (-not $actual.ContainsKey($rel)) { return "$Name 缺少输入 $rel" }
            $hashNow = & $Hash $expected[$rel]
            if ($hashNow -ne $actual[$rel]) { return "$Name 输入已变更 $rel（过期）" }
        }
        return ''
    }

    $r = & $checkSet '源文件' $expectation.SourceFiles $artifact.Inputs { param($p) Get-TextSha256 -Path $p }
    if ($r -ne '') { return $r }
    $r = & $checkSet '引用装配' $expectation.References $artifact.References { param($p) Get-FileSha256Bytes -Path $p }
    if ($r -ne '') { return $r }
    $r = & $checkSet '设置面' $expectation.SettingsFiles $artifact.SettingsInputs { param($p) Get-FileSha256Bytes -Path $p }
    if ($r -ne '') { return $r }

    return ''
}

function Get-ConstraintFieldIndexState
{
    <#
    .SYNOPSIS
        门禁统一入口：返回 { Fresh / Reason / Artifact / Generated / GenerationMs }。
        缺失或过期时：默认在**跨进程 Mutex** 下只重算一次；-NoAutoGenerate 则直接返回过期原因。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [string]$Configuration = 'Debug',
        [switch]$NoAutoGenerate,
        [switch]$Refresh
    )

    $key = $Root.TrimEnd('\', '/') + '|' + $Configuration + '|' + [bool]$Refresh
    if ($script:StateCache.ContainsKey($key)) { return $script:StateCache[$key] }

    $settings = Get-ConstraintIndexSettings -Root $Root
    if (-not $Refresh)
    {
        $reason = Test-ConstraintFieldIndexFresh -Root $Root -Configuration $Configuration
        if ($reason -eq '')
        {
            $artifactPath = Get-ConstraintIndexArtifactPath -Root $Root -Configuration $Configuration
            $artifact = Get-Content -LiteralPath $artifactPath -Raw | ConvertFrom-Json
            $state = [pscustomobject]@{ Fresh = $true; Reason = ''; Artifact = $artifact; Path = $artifactPath; Generated = $false; GenerationMs = 0 }
            $script:StateCache[$key] = $state
            return $state
        }
    }
    else
    {
        $reason = '强制重算（-Refresh）'
    }

    if ($NoAutoGenerate -or (-not [bool]$settings.IndexAutoGenerate))
    {
        return [pscustomobject]@{ Fresh = $false; Reason = $reason; Artifact = $null; Path = (Get-ConstraintIndexArtifactPath -Root $Root -Configuration $Configuration); Generated = $false; GenerationMs = 0 }
    }

    $mutex = $null
    $acquired = $false
    try
    {
        $hash = ([System.BitConverter]::ToString([System.Security.Cryptography.SHA256]::Create().ComputeHash(
                [System.Text.Encoding]::UTF8.GetBytes($Root.TrimEnd('\', '/')))) -replace '-', '')
        $mutex = New-Object System.Threading.Mutex($false, ('Global\ConstraintFieldIndex_' + $hash))
        $acquired = $mutex.WaitOne([TimeSpan]::FromMinutes(10))
    }
    catch { $acquired = $true }

    try
    {
        if (-not $Refresh)
        {
            $reason2 = Test-ConstraintFieldIndexFresh -Root $Root -Configuration $Configuration
            if ($reason2 -eq '')
            {
                $artifactPath = Get-ConstraintIndexArtifactPath -Root $Root -Configuration $Configuration
                $artifact = Get-Content -LiteralPath $artifactPath -Raw | ConvertFrom-Json
                $state = [pscustomobject]@{ Fresh = $true; Reason = ''; Artifact = $artifact; Path = $artifactPath; Generated = $false; GenerationMs = 0 }
                $script:StateCache[$key] = $state
                return $state
            }
        }

        Write-Host ("字段使用索引过期，重算一次（$reason）") -ForegroundColor DarkGray
        $build = Invoke-ConstraintFieldIndexBuild -Root $Root -Configuration $Configuration
        $reason3 = Test-ConstraintFieldIndexFresh -Root $Root -Configuration $Configuration
        if ($reason3 -ne '')
        {
            return [pscustomobject]@{ Fresh = $false; Reason = ($reason3 + '（重算后仍过期）'); Artifact = $null; Path = $build.ArtifactPath; Generated = $true; GenerationMs = $build.TotalMs }
        }
        $artifact = Get-Content -LiteralPath $build.ArtifactPath -Raw | ConvertFrom-Json
        $state = [pscustomobject]@{ Fresh = $true; Reason = ''; Artifact = $artifact; Path = $build.ArtifactPath; Generated = $true; GenerationMs = $build.TotalMs }
        $script:StateCache[$key] = $state
        return $state
    }
    finally
    {
        if ($acquired -and $null -ne $mutex)
        {
            try { [void]$mutex.ReleaseMutex() } catch { }
        }
        if ($null -ne $mutex) { $mutex.Dispose() }
    }
}
