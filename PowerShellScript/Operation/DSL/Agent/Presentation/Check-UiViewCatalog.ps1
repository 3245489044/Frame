#requires -Version 7.0
<#
.SYNOPSIS
    视图词汇单源守卫（D01）：History/Task/Nodes 的类型键/作用域/解析只允许在 ConversationViewCatalog 声明。
.DESCRIPTION
    C1 声明在位：ConversationViewCatalog.cs 含三视图字面量（node/task/history）。
    C2 消费方全部查表：ViewScope / ConversationPageRenderer / AgentUiLibraryConversation / ConversationViewService
       均出现 ConversationViewCatalog 引用（本地再写一份映射即破坏单源）。
    C3 领域旧映射不残留：上述四文件不得出现 `MainDialogViewType.X => "node|task|history"` 形态的本地映射。
    C4 区域声明单源：Config\ui\ui-regions.json 的 kind ∈ UiRegionKind 成员、key ⊆ AgentRuntimeUI regions[].key、
       view ⊆ ConversationViewCatalog TypeKey、内容区域键 ⊆ 声明键（禁 C# 另一份区域表 / 禁配置新增未声明区域）。
    C5 负对照：合成旧映射样本必须被 C3 判据捕获；合成非法 kind 必须被 C4 判据捕获。
    C6 视图内容域：全部 views[].content 必须声明且 domain 两两不同（禁三视图共用数据身份）。
    C7 内容域登记：content.domain 必须有 [Gather] 段，且 content.domain != db-root（整库读只属数据库页）。
    C10 视图声明不复制数据语义：content 只允许 {domain, scope, fold}；出现 selection/groupBy ⇒ 判红
        （行集语义单源 = Config\nodes\node-gather.ushar 的 [Gather] 段）。
    C11 渲染层不写死呈现身份：Agent.Ui 源码不得出现 TerminalPromotion 枚举值选择 / frameSelectionId 字面量
        （策略单源 = [Gather] terminal-promotion / frame-selection）。
    纯扫描（只读、0 写入）。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

$repo = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
$agent = Join-Path $repo 'Projects\Plug\Agent\src\Agent.Ui'
$views = Join-Path $agent 'Ui\Views'
$conv = Join-Path $agent 'Core\Functions\Conversation'

function Read-Text([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { throw "缺文件: $path" }
    return Get-Content -LiteralPath $path -Raw
}

$localMapPattern = 'MainDialogViewType\.(Task|Nodes|History)\s*=>\s*"(task|node|nodes|history)"'
$catalog = Join-Path $views 'Declaration\ConversationViewCatalog.cs'
$regionsPath = Join-Path $repo 'Projects\Plug\Agent\Config\ui\ui-regions.json'
$runtimeUiPath = Join-Path $repo 'Projects\Information\DrivenConfig\JsonConfig\AgentRuntimeUI.json'
$kindBaselinePath = Join-Path $repo 'Frame\PowerShellScript\Baselines\Agent\UiRegionKind.psd1'

function Get-ConfigRegionKeys {
    $keys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    if (-not (Test-Path -LiteralPath $runtimeUiPath)) { return $keys }
    $text = Get-Content -LiteralPath $runtimeUiPath -Raw
    foreach ($m in [regex]::Matches($text, '"key"\s*:\s*"(region-[A-Za-z0-9_-]+)"')) { [void]$keys.Add($m.Groups[1].Value) }
    return $keys
}

function Get-ViewProp($obj, [string]$name) {
    if ($null -eq $obj) { return $null }
    $p = $obj.PSObject.Properties[$name]
    if ($null -eq $p) { return $null }
    return $p.Value
}

function Test-RegionDeclaration([object]$decl, $configKeys, [string]$catalogText, [string[]]$allowedKinds) {
    # 结构事实：kind 取自受控词汇快照；key 必须在配置区域键内；视图键必须在视图目录；配置内容键必须被声明覆盖
    $violations = [System.Collections.Generic.List[string]]::new()
    $declared = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $kinds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($r in @($decl.regions)) {
        if (-not $r.kind) { $violations.Add('[C4] 区域条目缺 kind'); continue }
        [void]$kinds.Add($r.kind)
        if ($allowedKinds -notcontains $r.kind) { $violations.Add("[C4] 区域 kind 不在受控词汇（UiRegionKind 基线）：$($r.kind)") }
        if (-not $r.key) { $violations.Add("[C4] 区域 $($r.kind) 缺 DOM 区域键") }
        elseif (-not $configKeys.Contains($r.key)) { $violations.Add("[C4] 区域键不在 AgentRuntimeUI regions：$($r.key)") }
        else { [void]$declared.Add($r.key) }
    }
    foreach ($v in @($decl.views)) {
        if (-not $v.view) { $violations.Add('[C4] 视图条目缺 view'); continue }
        if ([string]::IsNullOrWhiteSpace([string](Get-ViewProp $v 'typeKey'))) {
            $violations.Add("[C4] 视图条目缺 typeKey：$($v.view)")
        }
        if (@($v.regions).Count -eq 0) { $violations.Add("[C4] 视图 $($v.view) 区域集为空") }
        foreach ($k in @($v.regions)) { if (-not $kinds.Contains($k)) { $violations.Add("[C4] 视图 $($v.view) 引用未声明区域：$k") } }
    }
    $shell = @('region-seg', 'region-head', 'region-subhead')
    foreach ($k in $configKeys) {
        if ($shell -notcontains $k -and -not $declared.Contains($k)) { $violations.Add("[C4] 配置区域键未在声明面登记：$k") }
    }
    return $violations
}
$consumers = @(
    (Join-Path $views 'Declaration\ViewScope.cs'),
    (Join-Path $views 'Rendering\ConversationPageRenderer.cs'),
    (Join-Path $views 'Orchestration\ConversationViewService.cs'),
    (Join-Path $conv 'AgentUiLibraryConversation.cs')
)

# 取 [Gather] <domain> 的续行集合（到下一个 [Gather] 或文件尾；空 = 未声明）。
function Get-GatherBlock([string]$text, [string]$domain) {
    $lines = $text -split "`r?`n"
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match ('^\s*\[Gather\]\s+' + [regex]::Escape($domain) + '\s*$')) { $start = $i; break }
    }
    if ($start -lt 0) { return @() }
    $block = [System.Collections.Generic.List[string]]::new()
    for ($i = $start + 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\s*\[Gather\]\s+') { break }
        $block.Add($lines[$i])
    }
    return $block.ToArray()
}

# 投影段完备（scope=all 的全量视图 ⇒ 折叠身份 + 折叠策略 + 行上限 三者齐备）。
function Test-ProjectionDeclared([string[]]$block) {
    $text = ($block -join "`n")
    return ($text -match '(?m)^\s*identity\s+\S') -and
           ($text -match '(?m)^\s*collapse\s+(none|first|last|count)\s*$') -and
           ($text -match '(?m)^\s*window\s+\d+\s*$')
}

# 呈现类完备（半套禁止：一旦某域声明 presentation=，该域每条 shaping 都必须声明）。
function Test-PresentationDeclared([string[]]$block) {
    $shapings = @($block | Where-Object { $_ -match '^\s*shaping\s+' })
    if ($shapings.Count -eq 0) { return $true }
    $declared = @($shapings | Where-Object { $_ -match 'presentation=' })
    if ($declared.Count -eq 0) { return $true }
    return ($declared.Count -eq $shapings.Count)
}

# 视图 content 键判据：只允许 domain/scope/fold（行集语义单源 = [Gather] 段）。
function Test-ContentKeys([string[]]$names) {
    foreach ($n in $names) {
        if (@('domain', 'scope', 'fold') -notcontains $n) { return $false }
    }
    return $true
}

# 渲染层语义泄漏判据（单行）：策略枚举字面量选择 / 帧选择字面量（注释不计入）。
function Test-SemanticLeakText([string]$text) {
    if ($text -match '^\s*(//|/\*|\*)') { return $false }
    if ($text -match '\bTerminalPromotion\.(None|LastTraceInRound|EntityOnly)\b') { return $true }
    if ($text -match 'frameSelectionId\s*:\s*"') { return $true }
    return $false
}

function Test-UiSemanticLeak {
    if (-not (Test-Path -LiteralPath $agent)) { return $false }
    foreach ($f in Get-ChildItem -LiteralPath $agent -Recurse -Filter '*.cs' -File) {
        if ($f.FullName -match '\\obj\\|\\bin\\|\\BuildOutput\\') { continue }
        foreach ($line in Get-Content -LiteralPath $f.FullName) {
            if (Test-SemanticLeakText $line) { return $true }
        }
    }
    return $false
}

$rules = @(
    [pscustomobject]@{ Id = 'C1'; Title = '目录声明在位（node/task/history）'
        Ok = { $t = Read-Text $catalog; ($t -match '"node"') -and ($t -match '"task"') -and ($t -match '"history"') } }
    [pscustomobject]@{ Id = 'C2'; Title = '四个消费方均查表（ConversationViewCatalog）'
        Ok = { foreach ($f in $consumers) { if ((Read-Text $f) -notmatch 'ConversationViewCatalog') { return $false } }; return $true } }
    [pscustomobject]@{ Id = 'C3'; Title = '领域旧映射不残留（禁本地 MainDialogViewType => "type"）'
        Ok = { foreach ($f in $consumers) { if ((Read-Text $f) -match $localMapPattern) { return $false } }; return $true } }
    [pscustomobject]@{ Id = 'C4'; Title = '区域声明单源（kind/key/view/配置覆盖）'
        Ok = {
            if (-not (Test-Path -LiteralPath $regionsPath)) { return $false }
            if (-not (Test-Path -LiteralPath $kindBaselinePath)) { return $false }
            $decl = Get-Content -Raw -LiteralPath $regionsPath | ConvertFrom-Json -Depth 16
            $kinds = @((Import-PowerShellDataFile -LiteralPath $kindBaselinePath).Kinds)
            return ((Test-RegionDeclaration $decl (Get-ConfigRegionKeys) (Read-Text $catalog) $kinds).Count -eq 0)
        } }
    [pscustomobject]@{ Id = 'C6'; Title = '视图内容域声明（三视图 domain 两两不同）'
        Ok = {
            if (-not (Test-Path -LiteralPath $regionsPath)) { return $false }
            $decl = Get-Content -Raw -LiteralPath $regionsPath | ConvertFrom-Json -Depth 16
            $seen = @{}
            foreach ($v in @($decl.views)) {
                $c = $v.content
                if ($null -eq $c) { return $false }
                if ([string]::IsNullOrWhiteSpace($c.domain)) { return $false }
                $identity = "$($c.domain)"
                if ($seen.ContainsKey($identity)) { return $false }
                $seen[$identity] = $v.view
            }
            return $true
        } }
    [pscustomobject]@{ Id = 'C7'; Title = '内容域在节点声明面已登记（禁未声明域 / 禁整库域）'
        Ok = {
            if (-not (Test-Path -LiteralPath $regionsPath)) { return $false }
            $decl = Get-Content -Raw -LiteralPath $regionsPath | ConvertFrom-Json -Depth 16
            $nodesDir = Join-Path $repo 'Projects\Plug\Agent\Config\nodes'
            $gather = Read-Text (Join-Path $nodesDir 'node-gather.ushar')
            foreach ($v in @($decl.views)) {
                $c = $v.content
                if ($null -eq $c) { return $false }
                if ($c.domain -eq 'db-root') { return $false }
                if ($gather -notmatch ('(?m)^\[Gather\]\s+' + [regex]::Escape($c.domain) + '\s*$')) { return $false }
            }
            return $true
        } }
    [pscustomobject]@{ Id = 'C10'; Title = '视图声明不复制数据语义（content 只允许 domain/scope/fold）'
        Ok = {
            if (-not (Test-Path -LiteralPath $regionsPath)) { return $false }
            $decl = Get-Content -Raw -LiteralPath $regionsPath | ConvertFrom-Json -Depth 16
            foreach ($v in @($decl.views)) {
                $c = $v.content
                if ($null -eq $c) { return $false }
                if (-not (Test-ContentKeys @($c.PSObject.Properties.Name))) { return $false }
            }
            return $true
        } }
    [pscustomobject]@{ Id = 'C11'; Title = '渲染层不写死呈现身份（无 TerminalPromotion 枚举选择 / 无 frameSelectionId 字面量）'
        Ok = { return (-not (Test-UiSemanticLeak)) } }
    [pscustomobject]@{ Id = 'C8'; Title = '呈现类声明完备（每条 shaping 须 presentation=；选择按类不按 kind 集合）'
        Ok = {
            if (-not (Test-Path -LiteralPath $regionsPath)) { return $false }
            $decl = Get-Content -Raw -LiteralPath $regionsPath | ConvertFrom-Json -Depth 16
            $nodesDir = Join-Path $repo 'Projects\Plug\Agent\Config\nodes'
            $gather = Read-Text (Join-Path $nodesDir 'node-gather.ushar')
            foreach ($v in @($decl.views)) {
                $block = Get-GatherBlock $gather $v.content.domain
                if ($block.Count -eq 0) { return $false }
                if (-not (Test-PresentationDeclared $block)) { return $false }
            }
            return $true
        } }
    [pscustomobject]@{ Id = 'C9'; Title = '全量视图投影完备（scope=all ⇒ identity + collapse + window）'
        Ok = {
            if (-not (Test-Path -LiteralPath $regionsPath)) { return $false }
            $decl = Get-Content -Raw -LiteralPath $regionsPath | ConvertFrom-Json -Depth 16
            $nodesDir = Join-Path $repo 'Projects\Plug\Agent\Config\nodes'
            $gather = Read-Text (Join-Path $nodesDir 'node-gather.ushar')
            foreach ($v in @($decl.views)) {
                if ($v.content.scope -ne 'all') { continue }
                $block = Get-GatherBlock $gather $v.content.domain
                if (-not (Test-ProjectionDeclared $block)) { return $false }
            }
            return $true
        } }
    [pscustomobject]@{ Id = 'C12'; Title = '视图程序完备（锚点/面板/快照/composer/渲染模式声明闭合）'
        Ok = {
            if (-not (Test-Path -LiteralPath $regionsPath)) { return $false }
            $decl = Get-Content -Raw -LiteralPath $regionsPath | ConvertFrom-Json -Depth 16
            foreach ($v in @($decl.views)) {
                foreach ($k in @('typeKey', 'scopeKey', 'snapshot', 'sidebar', 'composer', 'actorLevel', 'board', 'sectionFilter', 'renderMode', 'gatherView')) {
                    if ([string]::IsNullOrWhiteSpace([string](Get-ViewProp $v $k))) { return $false }
                }
                $anchor = Get-ViewProp $v 'anchor'
                if ($null -eq $anchor -or @($anchor.chain).Count -eq 0) { return $false }
                $panel = Get-ViewProp $v 'panel'
                if ($null -eq $panel) { return $false }
                if ($panel.mode -ne 'none' -and [string]::IsNullOrWhiteSpace([string]$panel.domain)) { return $false }
                $chain = @($anchor.chain | ForEach-Object { [string]$_ })
                $isNodesOnly = ([string]$v.renderMode) -eq 'nodes-only'
                $notApplicable = $chain -contains 'not-applicable'
                if ($isNodesOnly -and -not $notApplicable) { return $false }
                if (-not $isNodesOnly -and $notApplicable) { return $false }
                if ([string]$v.composer -ne 'none' -and $notApplicable) { return $false }
            }
            return $true
        } }

    [pscustomobject]@{ Id = 'C13'; Title = '读路径/适配器声明闭合（read-paths 事实 + 域读适配器全覆盖）'
        Ok = {
            $readPaths = Join-Path $repo 'Projects\Plug\Agent\Config\nodes\read-paths.json'
            $gather = Join-Path $repo 'Projects\Plug\Agent\Config\nodes\node-gather.ushar'
            $adaptersPath = Join-Path $repo 'Projects\Plug\Agent\Config\nodes\node-gather-adapters.json'
            if (-not (Test-Path -LiteralPath $readPaths) -or -not (Test-Path -LiteralPath $gather) -or -not (Test-Path -LiteralPath $adaptersPath)) { return $false }

            $paths = Get-Content -Raw -LiteralPath $readPaths | ConvertFrom-Json -Depth 16
            if (@($paths.paths).Count -lt 3) { return $false }
            foreach ($p in @($paths.paths)) {
                if ([string]::IsNullOrWhiteSpace([string]$p.id) -or [string]::IsNullOrWhiteSpace([string]$p.domain)) { return $false }
                if (@($p.facts).Count -eq 0) { return $false }
                if (-not [bool]$p.anchorCapable -and [string]::IsNullOrWhiteSpace([string]$p.nonAnchorReason)) { return $false }
            }

            $gatherText = Get-Content -Raw -LiteralPath $gather
            $domains = @([regex]::Matches($gatherText, '(?m)^\[Gather\]\s+(\S+)') | ForEach-Object { $_.Groups[1].Value })
            $adapters = Get-Content -Raw -LiteralPath $adaptersPath | ConvertFrom-Json -Depth 16
            foreach ($d in $domains) {
                $entry = $adapters.domains.PSObject.Properties[$d]
                if ($null -eq $entry) { return $false }
                if ([string]::IsNullOrWhiteSpace([string]$entry.Value.adapter)) { return $false }
            }
            return $true
        } }
)

if ($NegativeControl) {
    $bad = 'MainDialogViewType.Task => "task",'
    $ok = 'MainDialogViewType.Task => ConversationViewCatalog.TypeKeyOf(MainDialogViewType.Task),'
    $caught = ($bad -match $localMapPattern) -and (-not ($ok -match $localMapPattern))
    if (-not $caught) { throw '负对照失败：C3 判据未能区分正/负样本' }

    $badDecl = [pscustomobject]@{
        regions = @([pscustomobject]@{ kind = 'Bogus'; key = 'region-main'; loc = $null })
        views   = @([pscustomobject]@{ view = 'history'; typeKey = 'history'; regions = @('Bogus') })
    }
    $goodDecl = [pscustomobject]@{
        regions = @([pscustomobject]@{ kind = 'Feed'; key = 'region-main'; loc = $null })
        views   = @([pscustomobject]@{ view = 'history'; typeKey = 'history'; regions = @('Feed') })
    }
    $negConfig = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    [void]$negConfig.Add('region-main')
    $negKinds = @('Feed')
    if ((Test-RegionDeclaration $badDecl $negConfig 'TypeKey: "history"' $negKinds).Count -eq 0) { throw '负对照失败：C4 未捕获非法 kind' }
    if ((Test-RegionDeclaration $goodDecl $negConfig 'TypeKey: "history"' $negKinds).Count -ne 0) { throw '负对照失败：C4 对合法声明误报' }

    # C6 负对照：两视图同内容域必须判红。
    $dup = @(
        [pscustomobject]@{ view = 'a'; content = [pscustomobject]@{ domain = 'text-nodes'; scope = 'all'; fold = 'panel-open' } }
        [pscustomobject]@{ view = 'b'; content = [pscustomobject]@{ domain = 'text-nodes'; scope = 'all'; fold = 'panel-open' } }
    )
    $identities = @($dup | ForEach-Object { "$($_.content.domain)" })
    if (($identities | Select-Object -Unique).Count -eq $identities.Count) { throw '负对照失败：C6 未捕获重复内容域' }

    # C10 负对照：content 携带 selection/groupBy ⇒ 必须判红；只含 domain/scope/fold 须判绿。
    if (Test-ContentKeys @('domain', 'selection')) { throw '负对照失败：C10 未捕获 content 数据语义残留' }
    if (-not (Test-ContentKeys @('domain', 'scope', 'fold'))) { throw '负对照失败：C10 对合法 content 误报' }

    # C11 负对照：策略枚举字面量必命中；注释文本不命中。
    if (-not (Test-SemanticLeakText 'new TerminalPolicy(TerminalPromotion.LastTraceInRound, set)')) { throw '负对照失败：C11 未捕获策略枚举字面量' }
    if (Test-SemanticLeakText '// TerminalPromotion.LastTraceInRound（注释）') { throw '负对照失败：C11 误判注释' }
    if (-not (Test-SemanticLeakText 'frameSelectionId: "bubble",')) { throw '负对照失败：C11 未捕获帧选择字面量' }

    # C9 负对照：缺 window 的合成全量投影必须判红；三者齐备须判绿。
    if (Test-ProjectionDeclared @('  identity text', '  collapse count')) { throw '负对照失败：C9 未捕获缺失 window 的投影声明' }
    if (-not (Test-ProjectionDeclared @('  identity text', '  collapse count', '  window 50'))) { throw '负对照失败：C9 对合法投影声明误报' }

    # C8 负对照：半套声明（部分 shaping 有 presentation）必须判红；全量声明须判绿。
    if (Test-PresentationDeclared @('  shaping a | family=generic | summary=text | presentation=bubble', '  shaping b | family=generic | summary=text')) { throw '负对照失败：C8 未捕获半套呈现类声明' }
    if (-not (Test-PresentationDeclared @('  shaping a | family=generic | summary=text | presentation=bubble'))) { throw '负对照失败：C8 对合法塑形误报' }

    Write-Host '[OK] 负对照通过：C3 旧映射 / C4 非法区域 / C6 重复内容域 / C8 缺呈现类 / C9 缺投影 / C10 数据语义残留 / C11 语义硬编码。' -ForegroundColor Green
}

if (-not $Check -and -not $NegativeControl) {
    Write-Host '用法: -Check（检查） / -NegativeControl（判据自检）' -ForegroundColor Yellow
    return
}

if ($Check) {
    $failed = 0
    foreach ($r in $rules) {
        $pass = $false
        try { $pass = [bool](& $r.Ok) } catch { $pass = $false }
        $mark = if ($pass) { 'PASS' } else { 'FAIL' }
        if (-not $pass) { $failed++ }
        Write-Host ("[{0}] {1} {2}" -f $mark, $r.Id, $r.Title) -ForegroundColor $(if ($pass) { 'Green' } else { 'Red' })
    }
    if ($failed -gt 0) {
        Write-Host "UiViewCatalog: $failed 项违规（视图词汇出现第二来源）。" -ForegroundColor Red
        exit 1
    }
    Write-Host 'UiViewCatalog: 全部通过。' -ForegroundColor Green
}

exit 0
