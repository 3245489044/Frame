# ConstraintWall.psm1
# 约束墙（Wall）侧判据的**单一实现**：门禁脚本与正负对照共用同一谓词，避免「控制样本」与「扫描面」两套判据。
# 依赖既有框架：Get-CodeLines（TextOps.psm1）做行级枚举并剥离注释；不在本件重造文本扫描。

$script:CatalogCache = @{}
$script:RuleRegexCache = @{}

function Get-ConstraintWallCachedRegex
{
    param(
        [Parameter(Mandatory = $true)][string]$Key,
        [Parameter(Mandatory = $true)][string]$Pattern
    )
    if ($script:RuleRegexCache.ContainsKey($Key)) { return $script:RuleRegexCache[$Key] }
    $rx = [regex]::new($Pattern, [System.Text.RegularExpressions.RegexOptions]::Compiled)
    $script:RuleRegexCache[$Key] = $rx
    return $rx
}

function Get-ConstraintWallCatalog
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root
    )

    $key = $Root.TrimEnd('\', '/')
    if ($script:CatalogCache.ContainsKey($key)) { return $script:CatalogCache[$key] }

    $path = Join-Path $Root 'Frame\PowerShellScript\Modules\Constraint\ConstraintWall.settings.psd1'
    if (-not (Test-Path -LiteralPath $path))
    {
        throw "ConstraintWall: 缺判据登记表 $path"
    }

    $catalog = Import-PowerShellDataFile -LiteralPath $path
    $script:CatalogCache[$key] = $catalog
    return $catalog
}

function Import-ConstraintWallTextOps
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root
    )

    if (Get-Command Get-CodeLines -ErrorAction SilentlyContinue) { return }
    $path = Join-Path $Root 'Frame\PowerShellScript\Operation\DSL\Meta\TextOps.psm1'
    if (-not (Test-Path -LiteralPath $path))
    {
        throw "ConstraintWall: 缺既有限枚举件 $path"
    }
    Import-Module -Name $path -Force
}

function ConvertTo-ConstraintWallRelative
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $full = [IO.Path]::GetFullPath($Path)
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase))
    {
        return ($full -replace '\\', '/')
    }
    return ($full.Substring($rootFull.Length) -replace '\\', '/')
}

function Get-ConstraintWallScanFiles
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Rule
    )

    $catalog = Get-ConstraintWallCatalog -Root $Root
    $dirs = switch ($Rule)
    {
        'W01-ImplRef' { $catalog.DeclarationDirs }
        'W02-SurfaceDeny' { $catalog.DeclarationDirs }
        'W03-MutableConfigDecl' { $catalog.CoreDirs }
        'W04-ConfigMutation' { $catalog.ImplScopeDirs }
        'W05-ConfigConstruction' { $catalog.ImplScopeDirs }
        'W07-PlatformType' { $catalog.ImplScopeDirs }
        'W08-AmbientLookup' { $catalog.ImplScopeDirs }
        'W09-StaticMutableField' { $catalog.ImplScopeDirs }
        'W11-ConfigDefaultFallback' { $catalog.ImplScopeDirs }
        'W13-SnapshotOnly' { $catalog.ImplScopeDirs }
        default { throw "ConstraintWall: 未登记规则 '$Rule'" }
    }

    $excludes = @($catalog.ExcludePathSegments)
    $files = [System.Collections.Generic.List[string]]::new()
    foreach ($rel in @($dirs))
    {
        $dir = Join-Path $Root $rel
        if (-not (Test-Path -LiteralPath $dir -PathType Container))
        {
            throw "ConstraintWall: 扫描目录不存在 $rel（判据无标的 ⇒ 拒绝假绿）"
        }

        foreach ($f in @(Get-ChildItem -LiteralPath $dir -Recurse -File -Filter '*.cs' -ErrorAction Stop))
        {
            $full = $f.FullName -replace '\\', '/'
            $skip = $false
            foreach ($seg in $excludes)
            {
                if ($full.Contains($seg)) { $skip = $true; break }
            }
            if (-not $skip) { $files.Add($full) }
        }
    }

    $declarationFree = $Rule -in @('W04-ConfigMutation', 'W11-ConfigDefaultFallback', 'W13-SnapshotOnly')
    if ($declarationFree)
    {
        # 声明面（TConfig/Models 等）本身就是可变配置模型的定义处，不参与「实现侧持有/写/缺省」判定。
        $files = @($files | Where-Object {
                -not (Test-ConstraintWallInDeclarationDir -Catalog $catalog -RelativePath (ConvertTo-ConstraintWallRelative -Root $Root -Path $_))
            })
    }

    return @($files | Sort-Object -Unique)
}

function Test-ConstraintWallInDeclarationDir
{
    param(
        [Parameter(Mandatory = $true)]$Catalog,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )
    foreach ($dir in @($Catalog.DeclarationDirs))
    {
        $prefix = $dir.TrimEnd('/', '\') + '/'
        if ($RelativePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Test-ConstraintWallInPlatformDir
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Catalog,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    foreach ($dir in @($Catalog.PlatformDirs))
    {
        if ($RelativePath.StartsWith($dir.TrimEnd('/', '\') + '/', [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    if ($RelativePath -match '/Platform/' -or $RelativePath -match '/WinForms/') { return $true }
    return $false
}

function Test-ConstraintWallLine
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Catalog,
        [Parameter(Mandatory = $true)][string]$Rule,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text,
        [string]$RelativePath = ''
    )

    $trim = $Text.Trim()

    switch ($Rule)
    {
        'W01-ImplRef'
        {
            if ($trim.StartsWith('using '))
            {
                # 别名形式 `using X = A.B.Type;`：若 RHS 末段是已登记的配置模型（D 自身类型），
                # 属「D 模型寄居在 I 命名空间」的结构债，不作为**依赖 I 类型**计（避免同程序集自引用假阳）。
                $alias = [regex]::Match($trim, 'using\s+[A-Za-z_][A-Za-z0-9_]*\s*=\s*(?<rhs>[A-Za-z0-9_.]+)\s*;')
                if ($alias.Success)
                {
                    $tail = ($alias.Groups['rhs'].Value -split '\.')[-1]
                    if ($tail -in @($Catalog.MutableConfigTypes)) { return $false }
                }
                foreach ($ns in @($Catalog.ImplNamespacePrefixes))
                {
                    if ([regex]::IsMatch($trim, 'using\s+(?:[A-Za-z_][A-Za-z0-9_]*\s*=\s*)?' + [regex]::Escape($ns) + '(?=\.|;|\s)')) { return $true }
                }
                return $false
            }
            foreach ($ns in @($Catalog.ImplNamespacePrefixes))
            {
                if ([regex]::IsMatch($Text, '\b' + [regex]::Escape($ns) + '\.')) { return $true }
            }
            return $false
        }

        'W02-SurfaceDeny'
        {
            foreach ($token in @($Catalog.DeclarationDenyTokens))
            {
                if ($token -match '\.')
                {
                    if ($Text.Contains($token)) { return $true }
                }
                elseif ([regex]::IsMatch($Text, '\b' + [regex]::Escape($token) + '\b')) { return $true }
            }
            return $false
        }

        'W03-MutableConfigDecl'
        {
            foreach ($type in @($Catalog.MutableConfigTypes))
            {
                $t = [regex]::Escape($type)
                # 显式类型 + 标识符 + 声明终结符：字段/参数/局部/属性。
                foreach ($m in [regex]::Matches($Text, '(?<![A-Za-z0-9_.])' + $t + '\s*(?:<[^>]*>)?\s*[?]?\s*(?:\[\s*\])?\s+[A-Za-z_][A-Za-z0-9_]*\s*(?=[=;,){])'))
                {
                    $before = $Text.Substring(0, $m.Index)
                    if ($before -match 'new\s+$') { continue }
                    return $true
                }
                # 作为泛型实参持有（List<NodeConfig> 等）。
                if ([regex]::IsMatch($Text, '<\s*' + $t + '\s*>')) { return $true }
            }
            return $false
        }

        'W05-ConfigConstruction'
        {
            foreach ($type in @($Catalog.MutableConfigTypes))
            {
                $t = [regex]::Escape($type)
                if ([regex]::IsMatch($Text, '\bnew\s+(?:[A-Za-z_][A-Za-z0-9_.]*\.)?' + $t + '\b')) { return $true }
                if ([regex]::IsMatch($Text, '\bDeserialize(?:Object)?\s*<\s*' + $t + '\s*>')) { return $true }
            }
            return $false
        }

        'W07-PlatformType'
        {
            if ($RelativePath -ne '' -and (Test-ConstraintWallInPlatformDir -Catalog $Catalog -RelativePath $RelativePath)) { return $false }
            foreach ($token in @($Catalog.PlatformTokens))
            {
                if ($token -match '\.')
                {
                    if ($Text.Contains($token)) { return $true }
                }
                elseif ([regex]::IsMatch($Text, '\b' + [regex]::Escape($token) + '\b')) { return $true }
            }
            return $false
        }

        'W08-AmbientLookup'
        {
            foreach ($pattern in @($Catalog.AmbientPatterns))
            {
                if ([regex]::IsMatch($Text, $pattern)) { return $true }
            }
            return $false
        }

        'W09-StaticMutableField'
        {
            if ($Text -notmatch '\bstatic\b') { return $false }
            if ($Text.Contains('=>')) { return $false }   # 表达式体属性/方法不是可变静态状态
            if ([regex]::IsMatch($Text, '\bstatic\s+(?:readonly|const|class|struct|interface|enum|partial|void|event|delegate)\b')) { return $false }

            # 声明头 = 首个 `=` / `;` / `{` 之前的文本；含 `(` 视为方法/元组返回/参数表，排除。
            $cut = $Text.Length
            foreach ($ch in @('=', ';', '{'))
            {
                $i = $Text.IndexOf($ch)
                if ($i -ge 0 -and $i -lt $cut) { $cut = $i }
            }
            $head = $Text.Substring(0, $cut)
            if ($head -notmatch '\bstatic\b') { return $false }
            if ($head.Contains('(')) { return $false }

            # 头尾部必须是「类型 + 标识符」，且标识符不是修饰符/关键字。
            $tail = [regex]::Match($head, '\b(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*$')
            if (-not $tail.Success) { return $false }
            $name = $tail.Groups['name'].Value
            if ($name -in @('static', 'readonly', 'volatile', 'new', 'public', 'private', 'protected', 'internal', 'unsafe', 'extern')) { return $false }
            return $true
        }

        'W04-ConfigMutation'
        {
            # C 侧组合根白名单：合法装载/组合不判。
            foreach ($d in @($Catalog.ComposeWriteWhitelistDirs))
            {
                $prefix = $d.TrimEnd('/', '\') + '/'
                if ($RelativePath -ne '' -and $RelativePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { return $false }
            }

            # 构造 + 对象初始化由 W05 管；本规则只判「已持有配置上的写」。
            if ([regex]::IsMatch($Text, '\bnew\s+[A-Za-z_]')) { return $false }

            foreach ($seg in @($Catalog.ConfigWriteReceiverSegments))
            {
                # `<seg>.<member> <op>`：op 必须是赋值类，且排除 `==` / `=>`。
                $rx = Get-ConstraintWallCachedRegex -Key ('W04:' + $seg) -Pattern ('(?<![A-Za-z0-9_])' + [regex]::Escape($seg) + '\s*\.\s*[A-Za-z_]\w*\s*(?<op>=(?![=>])|\+=|-=|\*=|/=|%=|&=|\|=|\^=|<<=|>>=|\?\?=)')
                if ($rx.IsMatch($Text)) { return $true }
            }
            return $false
        }

        'W11-ConfigDefaultFallback'
        {
            # 缺省值必须回到声明面（模板/工厂）单源；就地 `?? <literal>` 是第二真源。
            $names = @($Catalog.ConfigFieldNames)
            if ($names.Count -eq 0)
            {
                throw 'W11-ConfigDefaultFallback: 缺 ConfigFieldNames（字段索引未接入 ⇒ 不可判定，拒绝空转通过）'
            }
            $alt = ($names | Sort-Object -Unique | ForEach-Object { [regex]::Escape($_) }) -join '|'
            $segs = @($Catalog.ConfigWriteReceiverSegments) | ForEach-Object { [regex]::Escape($_) }
            if ($segs.Count -eq 0) { throw 'W11-ConfigDefaultFallback: 缺 ConfigWriteReceiverSegments（接收段未登记 ⇒ 不可判定）' }
            $rx = Get-ConstraintWallCachedRegex -Key 'W11-match' -Pattern ('(?<![A-Za-z0-9_])(?:' + ($segs -join '|') + ')\s*\.\s*(?<f>[A-Za-z_]\w*)\s*\?\?\s*(?:true|false|null|-?[0-9]+(?:\.[0-9]+)?|"[^"]*"|''[^'']*'')')
            $setKey = 'W11set:' + $alt
            if (-not $script:RuleRegexCache.ContainsKey($setKey))
            {
                $set = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
                foreach ($n in $names) { [void]$set.Add($n) }
                $script:RuleRegexCache[$setKey] = $set
            }
            $fieldSet = $script:RuleRegexCache[$setKey]
            foreach ($m in $rx.Matches($Text))
            {
                if ($fieldSet.Contains($m.Groups['f'].Value)) { return $true }
            }
            return $false
        }

        'W13-SnapshotOnly'
        {
            # 标的形态 `ConfigSnapshot<T>` 本身不是违规；只判 T 的裸持有。
            $spans = @([regex]::Matches($Text, 'ConfigSnapshot\s*<[^<>]*>') | ForEach-Object {
                    [pscustomobject]@{ Start = $_.Index; End = $_.Index + $_.Length }
                })

            foreach ($type in @($Catalog.MutableConfigTypes))
            {
                $t = [regex]::Escape($type)
                $rxDecl = Get-ConstraintWallCachedRegex -Key ('W13-decl:' + $type) -Pattern ('(?<![A-Za-z0-9_.])' + $t + '\s*(?:<[^>]*>)?\s*[?]?\s*(?:\[\s*\])?\s+[A-Za-z_][A-Za-z0-9_]*\s*(?=[=;,){])')
                foreach ($m in $rxDecl.Matches($Text))
                {
                    $insideSnapshot = $false
                    foreach ($s in $spans)
                    {
                        if ($m.Index -ge $s.Start -and $m.Index -lt $s.End) { $insideSnapshot = $true; break }
                    }
                    if ($insideSnapshot) { continue }
                    $before = $Text.Substring(0, $m.Index)
                    if ($before -match 'new\s+$') { continue }   # 构造由 W05 管
                    return $true
                }
                # 作为裸泛型实参持有（List<NodeConfig> 等）；`ConfigSnapshot<T>` 已在 spans 内排除。
                $rxGeneric = Get-ConstraintWallCachedRegex -Key ('W13-generic:' + $type) -Pattern ('<\s*' + $t + '\s*>')
                foreach ($m in $rxGeneric.Matches($Text))
                {
                    $insideSnapshot = $false
                    foreach ($s in $spans)
                    {
                        if ($m.Index -ge $s.Start -and $m.Index -lt $s.End) { $insideSnapshot = $true; break }
                    }
                    if (-not $insideSnapshot) { return $true }
                }
            }
            return $false
        }
    }

    throw "ConstraintWall: 未登记规则 '$Rule'"
}

function Get-ConstraintWallHits
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Rule,
        [hashtable]$Catalog
    )

    Import-ConstraintWallTextOps -Root $Root
    if ($null -eq $Catalog) { $Catalog = Get-ConstraintWallCatalog -Root $Root }
    $files = Get-ConstraintWallScanFiles -Root $Root -Rule $Rule

    $includeUsing = $Rule -in @('W01-ImplRef', 'W07-PlatformType')
    $hits = [System.Collections.Generic.List[object]]::new()

    foreach ($full in $files)
    {
        $rel = ConvertTo-ConstraintWallRelative -Root $Root -Path $full
        $lines = @(Get-CodeLines -Path $full -Language slash)
        foreach ($code in $lines)
        {
            $text = [string]$code.Text
            $trim = $text.Trim()
            if ($trim.StartsWith('namespace ')) { continue }
            if (-not $includeUsing -and $trim.StartsWith('using ')) { continue }
            if (Test-ConstraintWallLine -Catalog $Catalog -Rule $Rule -Text $text -RelativePath $rel)
            {
                $hits.Add([pscustomobject]@{
                        Rule = $Rule
                        File = $rel
                        Line = [int]$code.Number
                        Text = $trim
                    })
            }
        }
    }

    return $hits
}

function Get-ConstraintWallCounts
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()]$Hits
    )

    $counts = @{}
    foreach ($h in @($Hits))
    {
        $key = [string]$h.File
        if ($counts.ContainsKey($key)) { $counts[$key] = [int]$counts[$key] + 1 }
        else { $counts[$key] = 1 }
    }
    return $counts
}

function Get-ConstraintWallScanSurface
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Rule
    )

    return @(Get-ConstraintWallScanFiles -Root $Root -Rule $Rule)
}
