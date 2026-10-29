# 声明面字段袋解析（共享单源）—— 供 Check-KeyIdentity / Check-KeyPolicy 等门禁复用。
# 口径：`|` 分段，段内首个 `=` 切 key/value；空 key 或空段忽略（与 Usharp 声明同形）。

function Get-DslFieldBag {
    param([string]$Line)

    $map = @{}
    foreach ($seg in ($Line -split '\|')) {
        $t = $seg.Trim()
        $eq = $t.IndexOf('=')
        if ($eq -gt 0) { $map[$t.Substring(0, $eq).Trim()] = $t.Substring($eq + 1).Trim() }
    }
    return $map
}

# 形状契约必填项（单一清单；与 [AnswerShape]/[WindowShape] 声明对齐）。
function Get-DslShapeRequired {
    param([string]$Kind)

    if ($Kind -eq 'Answer') { return @('container','round','text','terminal','default') }
    if ($Kind -eq 'Window') { return @('parent','member','terminal','default') }
    return @()
}

Export-ModuleMember -Function Get-DslFieldBag, Get-DslShapeRequired
