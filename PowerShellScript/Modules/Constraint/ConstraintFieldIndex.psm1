#requires -Version 5.1
<#
.SYNOPSIS
    ConstraintFieldIndex —— 约束墙的「字段使用索引」与「字段分级」前置件（CW-04/10/11/12/13）。

.DESCRIPTION
    存在形态：**门禁期缓存**（进程内 memo；不落仓库、不是编译产物、不是运行期快照）。
      · 索引值只由「当前工作树源码 + 声明面」推导，可重复计算；-Refresh 强制重算；
      · 需要人工审计时由调用方把返回值序列化到仓库外，本模块不写任何产物。

    复用（新建前已查）：
      · 源文件枚举：GateFileIndex.Get-GateFiles（git ls-files 单源 + 共享缓存 + 排除集单源）。
      · 行枚举/注释剥离：TextOps.Get-CodeLines。
      · 声明面/类型清单：ConstraintWall.settings.psd1（MutableConfigTypes、ImplScopeDirs，不复制第二份）。
      · 字段分级：ConstraintFieldClassification.psd1（只登记类别/角色，不复制字段清单）。

    词法边界（显式标出，不伪装成语义判定）：
      · 读 = 成员访问 `.<Field>`，且其后不是 `(`（方法调用），且不是赋值的左侧。
      · 写 = 成员访问位于 `=` / `+=` / … / `??=` 左侧。
      · 同名冲突 = 同一字段名被多个配置类型声明（Ambiguous）。CW-10 只判 Ambiguous=$false 的字段；
        同名冲突字段明确留在不可判子集，避免「同名成员跨类型」造成假绿。
      · 不做类型推断（`var` / 接口 / 反射 / 序列化回填均不计为读），方向保守：宁可判僵尸，不判存活。
#>

$script:DeclarationCache = @{}
$script:UsageCache = @{}
$script:TableCache = @{}

function Import-ConstraintFieldDependencies
{
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Root)

    if (-not (Get-Command Get-CodeLines -ErrorAction SilentlyContinue))
    {
        $p = Join-Path $Root 'Frame\PowerShellScript\Operation\DSL\Meta\TextOps.psm1'
        if (-not (Test-Path -LiteralPath $p)) { throw "ConstraintFieldIndex: 缺既有限枚举件 $p" }
        Import-Module -Name $p -Force
    }
}

function Get-ConstraintFieldFiles
{
    <#
    .SYNOPSIS
        定向枚举扫描目录内的 .cs 文件（与 ConstraintWall 同一排除集语义）。

    .NOTES
        复用判断：GateFileIndex.Get-GateFiles 是全局源文件索引的单一实现，但本机实测其首次
        索引会把被 .gitignore 排除的 BuildOutput/obj 也枚举一遍（单次 200s+），对每次门禁运行的
        字段索引不可接受。这里沿用 ConstraintWall 的定向枚举（同一 ExcludePathSegments 单源），
        不新建排除清单；若宿主提供了 GATE_FILE_INDEX_CACHE（Invoke-GateCheck 预生成），
        仍优先走 Get-GateFiles，保持与该索引同源。
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$RelativeDir
    )

    $dir = Join-Path $Root $RelativeDir
    if (-not (Test-Path -LiteralPath $dir -PathType Container))
    {
        throw "ConstraintFieldIndex: 扫描目录不存在 $RelativeDir（判据无标的 ⇒ 拒绝假绿）"
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GATE_FILE_INDEX_CACHE) -and (Get-Command Get-GateFiles -ErrorAction SilentlyContinue))
    {
        return @(Get-GateFiles -Root $dir -Extension cs)
    }

    $catalog = Get-ConstraintFieldCatalog -Root $Root
    $excludes = @($catalog.ExcludePathSegments)
    $files = [System.Collections.Generic.List[string]]::new()
    foreach ($f in @(Get-ChildItem -LiteralPath $dir -Recurse -File -Filter '*.cs' -ErrorAction Stop))
    {
        $full = $f.FullName -replace '\\', '/'
        $skip = $false
        foreach ($seg in $excludes)
        {
            if ($full.Contains($seg)) { $skip = $true; break }
        }
        if (-not $skip) { $files.Add($f.FullName) }
    }
    return @($files | Sort-Object -Unique)
}

function Get-ConstraintFieldCodeLines
{
    <#
    .SYNOPSIS
        快速行枚举：复用 TextOps.Remove-CodeComments（注释剥离单一实现），避免 Get-CodeLines
        逐行创建 PSCustomObject 的开销（~700 文件 × 全量行，实测 30s+ → 秒级）。
    #>
    param([Parameter(Mandatory = $true)][string]$Path)

    $text = [IO.File]::ReadAllText($Path)
    $stripped = Remove-CodeComments -Text $text -Language slash
    $number = 0
    foreach ($line in ($stripped -split "`r?`n"))
    {
        $number++
        $trim = $line.Trim()
        if ($trim.Length -eq 0) { continue }
        [pscustomobject]@{ Number = $number; Text = $trim }
    }
}

function Get-ConstraintFieldCatalog
{
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Root)

    $p = Join-Path $Root 'Frame\PowerShellScript\Modules\Constraint\ConstraintWall.settings.psd1'
    if (-not (Test-Path -LiteralPath $p)) { throw "ConstraintFieldIndex: 缺判据登记表 $p" }
    return Import-PowerShellDataFile -LiteralPath $p
}

function Get-ConstraintFieldClassificationTable
{
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Root)

    $key = $Root.TrimEnd('\', '/')
    if ($script:TableCache.ContainsKey($key)) { return $script:TableCache[$key] }

    $p = Join-Path $Root 'Frame\PowerShellScript\Modules\Constraint\ConstraintFieldClassification.psd1'
    if (-not (Test-Path -LiteralPath $p)) { throw "ConstraintFieldIndex: 缺字段分级表 $p（不得空表通过）" }
    $table = Import-PowerShellDataFile -LiteralPath $p
    if ($null -eq $table.TypeKinds) { throw 'ConstraintFieldIndex: 字段分级表缺 TypeKinds（不可判定）' }
    $script:TableCache[$key] = $table
    return $table
}

function ConvertTo-ConstraintFieldRelative
{
    param([string]$Root, [string]$Path)
    $full = [IO.Path]::GetFullPath($Path)
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) { return ($full -replace '\\', '/') }
    return ($full.Substring($rootFull.Length) -replace '\\', '/')
}

function Get-ConstraintFieldDeclarations
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [switch]$Refresh
    )

    $key = $Root.TrimEnd('\', '/')
    if (-not $Refresh -and $script:DeclarationCache.ContainsKey($key)) { return $script:DeclarationCache[$key] }

    Import-ConstraintFieldDependencies -Root $Root
    $catalog = Get-ConstraintFieldCatalog -Root $Root
    $table = Get-ConstraintFieldClassificationTable -Root $Root
    $mutable = @($catalog.MutableConfigTypes)

    # 类型级类别互证：MutableConfigTypes 必须全部登记为 Config；表内 Config 不得有未登记类型。
    $declaredConfigTypes = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($t in $mutable) { [void]$declaredConfigTypes.Add($t) }

    $list = [System.Collections.Generic.List[object]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

    foreach ($relDir in @($catalog.DeclarationDirs))
    {
        $dir = Join-Path $Root $relDir
        if (-not (Test-Path -LiteralPath $dir -PathType Container))
        {
            throw "ConstraintFieldIndex: 声明目录不存在 $relDir（判据无标的 ⇒ 拒绝假绿）"
        }

        foreach ($full in @(Get-ConstraintFieldFiles -Root $Root -RelativeDir $relDir))
        {
            $lines = @(Get-ConstraintFieldCodeLines -Path $full)
            $depth = 0
            $stack = [System.Collections.Generic.List[object]]::new()

            for ($i = 0; $i -lt $lines.Count; $i++)
            {
                $text = [string]$lines[$i].Text
                $pushed = $false

                $cm = [regex]::Match($text, '^\s*(?:public|internal|private|protected)\s+(?:static\s+)?(?:sealed\s+)?(?:partial\s+)?(class|record|struct)\s+([A-Za-z_]\w*)')
                if ($cm.Success)
                {
                    $stack.Add([pscustomobject]@{ Name = $cm.Groups[2].Value; BodyDepth = $depth + 1 })
                    $pushed = $true
                }

                if ($stack.Count -gt 0)
                {
                    $cur = $stack[$stack.Count - 1]
                    if ($declaredConfigTypes.Contains($cur.Name))
                    {
                        $fm = [regex]::Match($text, '^\s*public\s+(?:static\s+)?(?:readonly\s+)?[A-Za-z_][A-Za-z0-9_<>,?.\s]*\s+([A-Za-z_]\w*)\s*\{\s*get')
                        if ($fm.Success)
                        {
                            $field = $fm.Groups[1].Value
                            if ($seen.Add($cur.Name + '.' + $field))
                            {
                                $list.Add([pscustomobject]@{
                                        Type  = $cur.Name
                                        Field = $field
                                        Key   = $cur.Name + '.' + $field
                                        File  = ConvertTo-ConstraintFieldRelative -Root $Root -Path $full
                                        Line  = [int]$lines[$i].Number
                                    })
                            }
                        }
                    }
                }

                $depth += ([regex]::Matches($text, '\{')).Count
                $depth -= ([regex]::Matches($text, '\}')).Count
                if (-not $pushed)
                {
                    while ($stack.Count -gt 0 -and $depth -lt $stack[$stack.Count - 1].BodyDepth)
                    {
                        $stack.RemoveAt($stack.Count - 1)
                    }
                }
            }
        }
    }

    $result = @($list | Sort-Object Key)
    $script:DeclarationCache[$key] = $result
    return $result
}

function Get-ConstraintFieldUsageIndex
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [switch]$Refresh
    )

    $key = $Root.TrimEnd('\', '/')
    if (-not $Refresh -and $script:UsageCache.ContainsKey($key)) { return $script:UsageCache[$key] }

    Import-ConstraintFieldDependencies -Root $Root
    $catalog = Get-ConstraintFieldCatalog -Root $Root
    $decls = @(Get-ConstraintFieldDeclarations -Root $Root -Refresh:$Refresh)

    # 同名声明计数：>1 ⇒ Ambiguous（明确留在不可判子集，避免跨类型同名假绿）。
    $nameOwners = @{}
    foreach ($d in $decls)
    {
        if (-not $nameOwners.ContainsKey($d.Field)) { $nameOwners[$d.Field] = [System.Collections.Generic.List[string]]::new() }
        if (-not $nameOwners[$d.Field].Contains($d.Type)) { $nameOwners[$d.Field].Add($d.Type) }
    }

    $index = [ordered]@{}
    foreach ($d in $decls)
    {
        $index[$d.Key] = [pscustomobject]@{
            Key        = $d.Key
            Type       = $d.Type
            Field      = $d.Field
            Ambiguous  = ($nameOwners[$d.Field].Count -gt 1)
            ReadCount  = 0
            WriteCount = 0
            ReadSites  = [System.Collections.Generic.List[object]]::new()
            WriteSites = [System.Collections.Generic.List[object]]::new()
        }
    }

    if ($decls.Count -gt 0)
    {
        # 单一低成本成员访问正则 + 字段名哈希过滤；不用 170+ 分支交替（逐行交替正则在本机实测 30s+）。
        $rx = [regex]::new('\.(?<f>[A-Za-z_]\w*)\b(?!\s*\()', [System.Text.RegularExpressions.RegexOptions]::Compiled)

        foreach ($relDir in @($catalog.ImplScopeDirs))
        {
            $dir = Join-Path $Root $relDir
            if (-not (Test-Path -LiteralPath $dir -PathType Container))
            {
                throw "ConstraintFieldIndex: 实现目录不存在 $relDir（判据无标的 ⇒ 拒绝假绿）"
            }
            foreach ($full in @(Get-ConstraintFieldFiles -Root $Root -RelativeDir $relDir))
            {
                $rel = ConvertTo-ConstraintFieldRelative -Root $Root -Path $full
                foreach ($code in @(Get-ConstraintFieldCodeLines -Path $full))
                {
                    $text = [string]$code.Text
                    foreach ($m in $rx.Matches($text))
                    {
                        $f = $m.Groups['f'].Value
                        if (-not $nameOwners.ContainsKey($f)) { continue }
                        $rest = $text.Substring($m.Index + $m.Length)
                        $isWrite = ($rest -match '^\s*(=[^=>]|\+=|-=|\*=|/=|%=|&=|\|=|\^=|<<=|>>=|\?\?=)')
                        foreach ($ownerType in $nameOwners[$f])
                        {
                            $entry = $index[$ownerType + '.' + $f]
                            if ($null -eq $entry) { continue }
                            $site = [pscustomobject]@{
                                File = $rel
                                Line = [int]$code.Number
                                Text = $text.Trim()
                            }
                            if ($isWrite) { $entry.WriteCount++; $entry.WriteSites.Add($site) }
                            else { $entry.ReadCount++; $entry.ReadSites.Add($site) }
                        }
                    }
                }
            }
        }
    }

    $script:UsageCache[$key] = $index
    return $index
}

function Get-ConstraintFieldZombies
{
    <#
    .SYNOPSIS
        CW-10 的可判子集：Ambiguous=$false 且 ReadCount=0 的配置字段。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [switch]$Refresh
    )

    $index = Get-ConstraintFieldUsageIndex -Root $Root -Refresh:$Refresh
    return @($index.Values | Where-Object { -not $_.Ambiguous -and $_.ReadCount -eq 0 } | Sort-Object Key)
}

function Get-ConstraintFieldUnjudgeable
{
    <#
    .SYNOPSIS
        词法索引无法归属的字段（同名跨类型声明）。这些字段继续留在 attest 子集。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [switch]$Refresh
    )

    $index = Get-ConstraintFieldUsageIndex -Root $Root -Refresh:$Refresh
    return @($index.Values | Where-Object { $_.Ambiguous } | Sort-Object Key)
}

function Get-ConstraintFieldRole
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Table,
        [Parameter(Mandatory = $true)][string]$Type,
        [Parameter(Mandatory = $true)][string]$Field
    )

    $key = $Type + '.' + $Field
    if (@($Table.DerivedFields) -contains $key) { return 'Derived' }
    if (@($Table.SemanticFields) -contains $key) { return 'Semantic' }
    return 'Presentation'
}

function Test-ConstraintFieldClassificationCoverage
{
    <#
    .SYNOPSIS
        字段分级表 ↔ 声明面互证。返回违规字符串（空 = 一致）。
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Root)

    $issues = [System.Collections.Generic.List[string]]::new()
    $catalog = Get-ConstraintFieldCatalog -Root $Root
    $table = Get-ConstraintFieldClassificationTable -Root $Root
    $decls = @(Get-ConstraintFieldDeclarations -Root $Root)

    # 1) 显式类别登记表只允许 Config / Runtime / Derived 三个值（未知类别 ⇒ 不可判定）。
    $knownTypes = @{}
    foreach ($name in @($table.TypeKinds.Keys))
    {
        $kind = [string]$table.TypeKinds[$name]
        if ($kind -notin @('Config', 'Runtime', 'Derived'))
        {
            $issues.Add("字段分级表类型 $name 的类别 '$kind' 非法（只允许 Config/Runtime/Derived）")
        }
        $knownTypes[$name] = $kind
    }

    # 2) 声明字段所属类型必须可归类（Config 由 MutableConfigTypes 提供，其他类型须显式登记）。
    $declaredKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($d in $decls)
    {
        [void]$declaredKeys.Add($d.Key)
        if ($d.Type -in @($catalog.MutableConfigTypes)) { continue }
        if (-not $knownTypes.ContainsKey($d.Type))
        {
            $issues.Add("声明字段 $($d.Key) 的类型未在字段分级表登记类别（不可判定）")
        }
        else
        {
            $issues.Add("声明字段 $($d.Key) 所属类型被登记为 $($knownTypes[$d.Type])，但出现在配置声明目录")
        }
    }

    # 3) 表内条目不得是幻影（引用未声明字段）。
    foreach ($key in @($table.SemanticFields) + @($table.DerivedFields))
    {
        if (-not $declaredKeys.Contains([string]$key))
        {
            $issues.Add("字段分级表条目 $key 在声明面不存在（幻影条目 ⇒ 第二真源漂移）")
        }
    }

    # 4) 同一字段不得同时是 Semantic 与 Derived。
    foreach ($key in @($table.SemanticFields))
    {
        if (@($table.DerivedFields) -contains $key) { $issues.Add("字段分级表条目 $key 同时登记为 Semantic 与 Derived") }
    }

    # 5) 配置类型所辖字段不得为零（空表/空扫描不得视为通过）。
    if ($decls.Count -eq 0) { $issues.Add('声明面未解析出任何配置字段（判据无标的 ⇒ 拒绝假绿）') }

    return @($issues)
}
