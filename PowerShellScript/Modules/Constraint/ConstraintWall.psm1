# ConstraintWall.psm1
# 约束墙（Wall）侧判据的**单一实现**：门禁脚本与正负对照共用同一谓词，避免「控制样本」与「扫描面」两套判据。
# 依赖既有框架：Get-CodeLines（TextOps.psm1）做行级枚举并剥离注释；不在本件重造文本扫描。

$script:CatalogCache = @{}

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
        'W05-ConfigConstruction' { $catalog.ImplScopeDirs }
        'W07-PlatformType' { $catalog.ImplScopeDirs }
        'W08-AmbientLookup' { $catalog.ImplScopeDirs }
        'W09-StaticMutableField' { $catalog.ImplScopeDirs }
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

    return @($files | Sort-Object -Unique)
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
    }

    throw "ConstraintWall: 未登记规则 '$Rule'"
}

function Get-ConstraintWallHits
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Rule
    )

    Import-ConstraintWallTextOps -Root $Root
    $catalog = Get-ConstraintWallCatalog -Root $Root
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
            if (Test-ConstraintWallLine -Catalog $catalog -Rule $Rule -Text $text -RelativePath $rel)
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
