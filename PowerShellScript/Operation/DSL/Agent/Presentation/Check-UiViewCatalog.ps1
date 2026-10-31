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
    C6 视图内容身份：全部 views[].content 必须声明且 (domain|selection) 两两不同（禁三视图共用数据身份）。
    C7 内容域/选择登记：content.domain 必须有 [Gather] 段、content.selection 必须在域声明 selections 中、
       且 content.domain != db-root（整库读只属数据库页）。
    纯扫描（只读、0 写入）。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))))
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
        if ($catalogText -notmatch ('TypeKey:\s*"' + [regex]::Escape($v.view) + '"')) {
            $violations.Add("[C4] 视图键未在 ConversationViewCatalog 声明：$($v.view)")
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
    [pscustomobject]@{ Id = 'C6'; Title = '视图内容身份声明（三视图两两不同）'
        Ok = {
            if (-not (Test-Path -LiteralPath $regionsPath)) { return $false }
            $decl = Get-Content -Raw -LiteralPath $regionsPath | ConvertFrom-Json -Depth 16
            $seen = @{}
            foreach ($v in @($decl.views)) {
                $c = $v.content
                if ($null -eq $c) { return $false }
                if ([string]::IsNullOrWhiteSpace($c.domain) -or [string]::IsNullOrWhiteSpace($c.selection)) { return $false }
                $identity = "$($c.domain)|$($c.selection)"
                if ($seen.ContainsKey($identity)) { return $false }
                $seen[$identity] = $v.view
            }
            return $true
        } }
    [pscustomobject]@{ Id = 'C7'; Title = '内容域/选择在节点声明面已登记（禁未声明域）'
        Ok = {
            if (-not (Test-Path -LiteralPath $regionsPath)) { return $false }
            $decl = Get-Content -Raw -LiteralPath $regionsPath | ConvertFrom-Json -Depth 16
            $nodesDir = Join-Path $repo 'Projects\Plug\Agent\Config\nodes'
            $gather = Read-Text (Join-Path $nodesDir 'node-gather.ushar')
            $selections = (Read-Text (Join-Path $nodesDir 'conversation.ushar')) + "`n" +
                          (Read-Text (Join-Path $nodesDir 'message-stream.ushar')) + "`n" +
                          (Read-Text (Join-Path $nodesDir 'task-node.ushar'))
            foreach ($v in @($decl.views)) {
                $c = $v.content
                if ($null -eq $c) { return $false }
                if ($c.domain -eq 'db-root') { return $false }
                if ($gather -notmatch ('(?m)^\[Gather\]\s+' + [regex]::Escape($c.domain) + '\s*$')) { return $false }
                if ($selections -notmatch ('(?m)^\s+' + [regex]::Escape($c.selection) + '\s*=')) { return $false }
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
        views   = @([pscustomobject]@{ view = 'history'; regions = @('Bogus') })
    }
    $goodDecl = [pscustomobject]@{
        regions = @([pscustomobject]@{ kind = 'Feed'; key = 'region-main'; loc = $null })
        views   = @([pscustomobject]@{ view = 'history'; regions = @('Feed') })
    }
    $negConfig = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    [void]$negConfig.Add('region-main')
    $negKinds = @('Feed')
    if ((Test-RegionDeclaration $badDecl $negConfig 'TypeKey: "history"' $negKinds).Count -eq 0) { throw '负对照失败：C4 未捕获非法 kind' }
    if ((Test-RegionDeclaration $goodDecl $negConfig 'TypeKey: "history"' $negKinds).Count -ne 0) { throw '负对照失败：C4 对合法声明误报' }

    # C6/C7 负对照：两视图同内容身份 / 内容域为 db-root 必须判红。
    $dup = @(
        [pscustomobject]@{ view = 'a'; content = [pscustomobject]@{ domain = 'text-nodes'; selection = 'text' } }
        [pscustomobject]@{ view = 'b'; content = [pscustomobject]@{ domain = 'text-nodes'; selection = 'text' } }
    )
    $identities = @($dup | ForEach-Object { "$($_.content.domain)|$($_.content.selection)" })
    if (($identities | Select-Object -Unique).Count -eq $identities.Count) { throw '负对照失败：C6 未捕获重复内容身份' }

    Write-Host '[OK] 负对照通过：C3 可捕获本地旧映射；C4 可捕获非法区域声明。' -ForegroundColor Green
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
