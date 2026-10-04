# StringTools.Clinerules.psm1 - clinerules DSL 文法解析模块  
# 提供定义点形态判定（Resolve-DefinitionForm）与 [CR] 引用解析（严格/旧形态）  
#  
# 依赖：StringTools.Core.psm1（Get-FirstMatchingPrefix, Test-ValidIdentifier）  
#  
# 文法权威：[CR: dsl:Sec.4]（clinerules/dsl.md）；本模块承载 dsl.md §4.1/§4.3 引用文法  
# 与定义点前缀 → STYP 语义类型映射（dsl.md §4.5）  
  
# ============================================  
#  私有数据（dsl.md §4.5 语义类型映射 / §4.4-4.6 枚举对齐）  
# ============================================  
  
# 定义点前缀 → STYP 映射（与 dsl.md §4.5 语义类型对齐）  
$script:PrefixStypMap = @{  
    '[AX-CORE]' = 'AX-CORE'; '[AX-META]' = 'AX-META'; '[AX-FRAME]' = 'AX-FRAME'  
    '[BAX-IDI]' = 'BAX-IDI'; '[BAX-TIME]' = 'BAX-TIME'; '[BAX-C]' = 'BAX-C'  
    '[BAX-D]'   = 'BAX-D';    '[BAX-X]'   = 'BAX-X';    '[BAX]'     = 'BAX'  
    '[AX]'      = 'AX';       '[Def]'     = 'Def';      '[Type]'    = 'Type'  
    '[StateVar]'= 'StateVar'; '[Profile]' = 'Profile';  '[Pattern]' = 'Pattern'  
    '[Gate]'    = 'Gate';     '[SM]'      = 'SM';       '[SEP]'     = 'SEP'  
    '[DR]'      = 'DR';       '[LO]'      = 'LO';       '[QA]'      = 'SM'  
    '[CD]'      = 'Gate';     '[CM]'      = 'Gate';     '[CC]'      = 'Gate'  
    '[CS]'      = 'Gate';     '[TD]'      = 'Gate';     '[Attention]' = 'Gate'  
    '[Strategy]' = 'Strategy'  
}  
  
# 前缀判定顺序：长前缀优先（[AX-CORE] 先于 [AX]）  
$script:PrefixOrder = @('[AX-CORE]','[AX-META]','[AX-FRAME]','[BAX-IDI]','[BAX-TIME]',  
                  '[BAX-C]','[BAX-D]','[BAX-X]','[BAX]','[AX]','[Def]','[Type]',  
                  '[StateVar]','[Profile]','[Pattern]','[Gate]','[SM]','[SEP]',  
                  '[DR]','[LO]','[QA]','[CD]','[CM]','[CC]','[CS]','[TD]','[Attention]',  
                  '[Strategy]')  
  
# 私有定义前缀：需行内含冒号才视为定义头（防内部公式行误判）；Def/Type 有专属分隔符正则, 豁免  
$script:PrivatePrefixes = @('[AX]','[BAX]','[AX-CORE]','[AX-META]','[AX-FRAME]',  
                      '[BAX-IDI]','[BAX-TIME]','[BAX-C]','[BAX-D]','[BAX-X]',  
                      '[Def]','[Type]','[LO]','[Attention]','[QI]')  
$script:NoColonPrefixes = @('[Def]','[Type]')   # 专属正则已保证分隔符, 不再要求冒号  
  
# 定义行语料黑名单（文档语料中的示例/说明行非定义头）  
$script:BlacklistPattern = '参考|模板|语法|示例|Prefix|`\[\w+\]`|区块导航|权威清单'  
  
# ============================================  
#  定义点形态解析  
# ============================================  
function Resolve-DefinitionForm {  
    [CmdletBinding()]  
    param(  
        [Parameter(Mandatory = $true, Position = 0)]  
        [AllowEmptyString()][string]$Line  
    )  
  
    if ([string]::IsNullOrEmpty($Line)) { return $null }

    # 权威清单行过滤: [AX-CORE] L0: BAX-IDI,...  
    if ($Line -match '^\[[A-Za-z-]+\]\s+(L[0-9.]+)\s*:') { return $null }  
  
    # 形态4（前置, 不依赖 prefix 检测）: ## TH-n: / # TH-n:  
    if ($Line -match '^#{1,6}\s+(TH-\d+)\s*:') {  
        return [PSCustomObject]@{  
            Prefix = ''; Styp = 'TH'; Name = $Matches[1]  
            Names = @($Matches[1]); Dep = $false; Intent = 'deriv'; Form = 4  
        }  
    }  
  
    $body = $Line -replace '^\s*',''  
    $name = $null; $styp = $null; $prefix = $null; $form = 0  
    $intent = 'def'  
  
    # 形态2 前置: % NAME= (配置参数, 无 [前缀])  
    if ($body -match '^%\s*([A-Za-z_][A-Za-z0-9_-]*)\s*=') {  
        $name = $Matches[1]; $styp = 'Param'; $form = 2  
    }  
    # 形态5 前置: $ symbol = 含义 或 $ symbol(...) = (符号速记, 等号必选; 无 [前缀])  
    elseif ($body -match '^\$\s*([A-Za-z_][A-Za-z0-9_]*)\s*(?:\([^)]*\))?\s*=') {  
        $name = $Matches[1]; $styp = 'Symbol'; $form = 5  
    }  
    else {  
        # 前缀判定（长前缀优先）+ 语料黑名单过滤（基于原始行, 防示例行误判）  
        $prefix = Get-FirstMatchingPrefix -Text $body -Prefixes $script:PrefixOrder  
        if (-not $prefix -or $Line -match $script:BlacklistPattern) { return $null }  
        $styp = $script:PrefixStypMap[$prefix]  
        # 形态0a: [Def] 含点符号/必须带分隔符（op.needsSync / CallKey / meltdown — 分隔符前置判定）  
        if ($prefix -eq '[Def]' -and $body -match '^\[Def\]\s*([A-Za-z_][A-Za-z0-9_.]*?)\s*(?=[\(=:：↔])') {  
            $name = $Matches[1]; $form = 0  
        }  
        # 形态0b: [Type] Name = 含义  
        elseif ($prefix -eq '[Type]' -and $body -match '^\[Type\]\s*([A-Za-z_][A-Za-z0-9_]*?)\s*[=:：]') {  
            $name = $Matches[1]; $form = 0  
        }  
        # 形态1: [Prefix] Name: — [Def] 需行内含 =/: 分隔符（state1 ↝ 等 Unicode 公式行排除）  
        # 名称允许点号（如 [CM] OnError.shared-branch）→ 编译为独立子谓词符号, 避免与 OnError 合并  
        elseif ($body -match '^\[[A-Za-z-]+\]\s*([A-Za-z_][A-Za-z0-9_.-]*)') {  
            $m1 = $Matches[1]  
            if (-not ($prefix -eq '[Def]' -and $body -notmatch '[=:：]')) {  
                $name = $m1; $form = 1  
            }  
        }  
        # 形态3: [Gate]/[SM] 多行式 Name:  
        # 形态1: [Prefix] Name: — [Def] 需行内含 =/: 分隔符（state1 ↝ 等 Unicode 公式行排除）  
        # 名称允许点号（如 [CM] OnError.shared-branch）→ 编译为独立子谓词符号, 避免与 OnError 合并  
        elseif ($body -match '^\[[A-Za-z-]+\]\s*([A-Za-z_][A-Za-z0-9_.-]*)') {  
            $m1 = $Matches[1]  
            if (-not ($prefix -eq '[Def]' -and $body -notmatch '[=:：]')) {  
                $name = $m1; $form = 1  
            }  
        }  
        # 形态3: [Gate]/[SM] 多行式 Name:  
        elseif ($body -match '^\[(Gate|SM|CD|QA|CM|CC|CS)\]\s+([A-Za-z_][A-Za-z0-9_-]*)') {  
            $name = $Matches[2]; $form = 3  
        }  
    }  
    # 防御校验: 捕获名须为合法标识符（形态正则已保证, 防正则漂移）  
    if (-not $name -or -not (Test-ValidIdentifier -Text $name -AllowDot)) { return $null }  
  
    # 多符号行收集: BAX 族行内 Name 段（冒号后须空白, 排除 irr:/comb: 等元数据键）;  
    $multiNames = New-Object System.Collections.Generic.List[string]  
    [void]$multiNames.Add($name)  
    if ($prefix -in @('[BAX]','[BAX-IDI]','[BAX-TIME]','[BAX-C]','[BAX-D]','[BAX-X]')) {  
        foreach ($m in [regex]::Matches($body, '(?:\||^)\s*([A-Za-z_][A-Za-z0-9_.-]*)\s*:\s')) {  
            [void]$multiNames.Add($m.Groups[1].Value)  
        }  
    }  
    elseif ($prefix -eq '[Def]' -and $body -match '^\[Def\]\s*([A-Za-z_][A-Za-z0-9_.-]*(?:/[A-Za-z_][A-Za-z0-9_.-]*)+)') {  
        $multiNames.Clear()  
        foreach ($n in ($Matches[1] -split '/')) { [void]$multiNames.Add($n) }  
    }  
  
    # 逐条过滤: 去重 + 长度过滤（Def/Symbol/Type 单字符名非法）+ 私有前缀冒号过滤（Def/Type 豁免）  
    $names = @()  
    foreach ($nm in ($multiNames | Select-Object -Unique)) {  
        if ($nm.Length -le 1 -and $styp -in @('Def','Symbol','Type')) { continue }  
        if ($prefix -in $script:PrivatePrefixes -and $prefix -notin $script:NoColonPrefixes -and $body -notmatch ':(\s|$)|：') { continue }  
        $names += $nm  
    }  
    if ($names.Count -eq 0) { return $null }  
  
    # dep 标注（定义处 dep:replaced-by(X) → 编译出 #dep）  
    $dep = $Line -match 'dep\s*:\s*\x22replaced-by|dep\s*=\s*\x22replaced-by'  
  
    return [PSCustomObject]@{  
        Prefix = $prefix; Styp = $styp; Name = $name  
        Names = $names; Dep = $dep; Intent = $intent; Form = $form  
    }  
}  
  
# ============================================  
#  [CR] 引用解析  
# ============================================  
  
function Get-CrReferenceTokens {  
    [CmdletBinding()]  
    param(  
        [Parameter(Mandatory = $true, Position = 0)]  
        [AllowEmptyString()][string]$Line  
    )  
    $ms = [regex]::Matches($Line, '\[CR[:\s]+\s*([^\]]+?)\s*\]|«([^»]+?)»')  
    $tokens = @()  
    foreach ($m in $ms) {  
        if ($m.Groups[1].Success) { $inner = $m.Groups[1].Value.Trim() }
        else {
            $inner = $m.Groups[2].Value.Trim()
            if ($inner -notmatch '@(def|deriv|inst|consume|align|see)$') { $inner = "$inner@consume" }
        }
        $tokens += [PSCustomObject]@{ Raw = $m.Value; Inner = $inner }  
    }  
    return $tokens  
}  
  
function ConvertFrom-CrReference {  
    [CmdletBinding()]  
    param(  
        [Parameter(Mandatory = $true, Position = 0)]  
        [string]$Token  
    )  
    $strict = '^([a-z]+):([A-Za-z-]+)\.([A-Za-z0-9_][A-Za-z0-9_.-]*?)(?:@(def|deriv|inst|consume|align|see))?$'  
    if ($Token -match $strict) {  
        return [PSCustomObject]@{  
            Lib = $Matches[1]; Styp = $Matches[2]; Name = $Matches[3]  
            Intent = if ($Matches[4]) { $Matches[4] } else { 'def' }  
        }  
    }  
    return $null  
}  
  
function ConvertFrom-LegacyCrReference {  
    [CmdletBinding()]  
    param(  
        [Parameter(Mandatory = $true, Position = 0)]  
        [string]$Token  
    )  
    $legacy1 = '^(?:([a-z]+)\.md|([a-z]+))[:\s]+([A-Za-z_][A-Za-z0-9_.-]*)'  
    if ($Token -match $legacy1) {  
        return [PSCustomObject]@{  
            Lib = if ($Matches[1]) { $Matches[1] } else { $Matches[2] }  
            Name = $Matches[3]  
        }  
    }  
    return $null  
}  
  
# ============================================  
#  导出公共函数  
# ============================================  
  
Export-ModuleMember -Function Resolve-DefinitionForm,  
                              Get-CrReferenceTokens,  
                              ConvertFrom-CrReference,  
                              ConvertFrom-LegacyCrReference  
