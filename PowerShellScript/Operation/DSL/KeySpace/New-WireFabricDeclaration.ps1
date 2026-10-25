#requires -Version 5.1
<#
.SYNOPSIS
    从 UI 声明（AgentRuntimeUI.json / ProducePanel.json）生成接线声明（唯一源 → 生成物）。

.DESCRIPTION
    判据载体 = UI 配置中的 events / eventTargets（结构事实），生成物含 sourceHash（源指纹）。
    输出：Projects\Plug\Agent\Config\wiring\ui-all.wiring.json
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [string]$Output = 'Projects\Plug\Agent\Config\wiring\ui-all.wiring.json'
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$uiFiles = @(
    'Projects\Information\DrivenConfig\JsonConfig\AgentRuntimeUI.json',
    'Projects\Information\DrivenConfig\JsonConfig\ProducePanel.json'
)

function Get-Sha256Hex([byte[]]$Bytes) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes)) -replace '-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Get-WireKey([string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Name)) { return 'unknown' }
    return ($Name.ToLowerInvariant() -replace '[^a-z0-9]', '_')
}

$buffer = [System.Collections.Generic.List[byte]]::new()
foreach ($rel in $uiFiles) {
    $full = Join-Path $Root $rel
    if (-not (Test-Path -LiteralPath $full)) { throw "UI 配置缺失: $rel" }
    $buffer.AddRange([System.IO.File]::ReadAllBytes($full))
}
$sourceHash = Get-Sha256Hex $buffer.ToArray()

$hostEvents = [System.Collections.Generic.List[object]]::new()
$pageTargets = [System.Collections.Generic.List[object]]::new()

function Walk-Node($node, [string]$path) {
    if ($null -eq $node) { return }

    if ($node -is [System.Collections.IDictionary]) {
        if ($path -notmatch '/eventTargets/' -and $node.Contains('events')) {
            $events = $node['events']
            if ($events -is [System.Collections.IDictionary]) {
                foreach ($eventName in @($events.Keys)) {
                    if ($eventName -eq 'bindings') { continue }
                    $functions = @($events[$eventName])
                    if ($functions.Count -gt 0) {
                        $controlName = ''
                        if ($node.Contains('name')) { $controlName = [string]$node['name'] }
                        $hostEvents.Add([pscustomobject]@{ Path = $path; Event = [string]$eventName; Function = [string]$functions[0]; Name = $controlName })
                    }
                }
            }
        }

        if ($node.Contains('eventTargets') -and $node['eventTargets'] -is [System.Collections.IDictionary]) {
            foreach ($targetName in @($node['eventTargets'].Keys)) {
                $target = $node['eventTargets'][$targetName]
                $function = ''
                if ($target -is [System.Collections.IDictionary] -and $target.Contains('events')) {
                    $tEvents = $target['events']
                    if ($tEvents -is [System.Collections.IDictionary]) {
                        foreach ($eventName in @($tEvents.Keys)) {
                            if ($eventName -eq 'bindings') { continue }
                            $list = @($tEvents[$eventName])
                            if ($list.Count -gt 0) { $function = [string]$list[0]; break }
                        }
                    }
                }
                $pageTargets.Add([pscustomobject]@{ Name = [string]$targetName; Function = $function })
            }
        }

        foreach ($key in @($node.Keys)) { Walk-Node $node[$key] "$path/$key" }
    }
    elseif ($node -is [System.Collections.IEnumerable] -and $node -isnot [string]) {
        $i = 0
        foreach ($item in $node) { Walk-Node $item "$path[$i]"; $i++ }
    }
}

foreach ($rel in $uiFiles) {
    $json = Get-Content -LiteralPath (Join-Path $Root $rel) -Raw | ConvertFrom-Json -AsHashtable
    Walk-Node $json ([System.IO.Path]::GetFileName($rel))
}

$forms = @(
    @{ id = 'Text'; clr = 'System.String'; sealed = $true },
    @{ id = 'PageEvent'; sealed = $true },
    @{ id = 'HostMessage'; sealed = $true },
    @{ id = 'RenderOutput'; sealed = $true }
)

$boxes = [System.Collections.Generic.List[object]]::new()
$wires = [System.Collections.Generic.List[object]]::new()
$wireIds = [System.Collections.Generic.List[string]]::new()

function Add-Wire([string]$id, [string]$from, [string]$to, [string]$adapt = $null, [string]$judge = $null, [string]$merge = $null) {
    $w = @{ id = $id; from = $from; to = $to; onMissing = 'report' }
    if ($adapt) { $w.adapt = $adapt }
    if ($judge) { $w.judge = $judge }
    if ($merge) { $w.merge = $merge }
    $wires.Add($w)
    $wireIds.Add($id)
}

# 唯一写回入口（terminal，多写者显式 merge=concat）
$boxes.Add(@{
    id = 'ui.render.writer'; impl = 'render:writer'; terminal = $true
    ports = @(@{ name = 'result'; dir = 'in'; form = 'RenderOutput'; required = $true; card = -1 })
})

# 宿主控件事件入口
$index = 0
$hostKeys = @{}
foreach ($evt in $hostEvents) {
    $index++
    $baseKey = if ([string]::IsNullOrWhiteSpace($evt.Name)) { "h$index" } else { Get-WireKey $evt.Name }
    if ($hostKeys.ContainsKey($baseKey)) { $hostKeys[$baseKey] = $hostKeys[$baseKey] + 1; $key = "$baseKey$($hostKeys[$baseKey])" }
    else { $hostKeys[$baseKey] = 1; $key = $baseKey }
    $sourceId = "ui.host_event.$key"
    $handlerId = "ui.handler.$key"
    $boxes.Add(@{ id = $sourceId; impl = 'source:host'; out = @('event:HostMessage') })
    $boxes.Add(@{
        id = $handlerId; impl = ("fn:" + $evt.Function)
        ports = @(
            @{ name = 'input'; dir = 'in'; form = 'HostMessage'; required = $true; card = 1 },
            @{ name = 'result'; dir = 'out'; form = 'RenderOutput'; card = 1 }
        )
    })
    Add-Wire "wi_host_event_$key" "$sourceId.event" "$handlerId.input"
    Add-Wire "wi_host_result_$key" "$handlerId.result" 'ui.render.writer.result' -merge 'concat'
}

# 会话命令面（声明唯一源）：命令类只经 host message 源，页面事件只做投影（禁双派发）。
$commands = @('selectType','composerSend','showAiPayload','refresh','selectTask','openTaskView','selectHistoryContainer','selectHistory','createHistory')
$commandKeys = @{}

# 会话页内部事件目标（page 域）
foreach ($target in $pageTargets) {
    $isCommand = $commands -contains $target.Name
    if ($isCommand -and $target.Name -ine 'composerSend') { continue }  # 命令类只经 host message 源
    $key = Get-WireKey $target.Name
    $sourceId = "ui.page_event.$key"
    $handlerId = "ui.page_handler.$key"
    $boxes.Add(@{ id = $sourceId; impl = 'source:page'; out = @('event:PageEvent') })
    if ($isCommand) { continue }  # composerSend：仅保留输入采集源端口，不建 page handler（禁双派发）
    $boxes.Add(@{
        id = $handlerId; impl = ("fn:" + $target.Function)
        ports = @(
            @{ name = 'input'; dir = 'in'; form = 'PageEvent'; required = $true; card = 1 },
            @{ name = 'result'; dir = 'out'; form = 'RenderOutput'; card = 1 }
        )
    })
    Add-Wire "wi_page_$key" "$sourceId.event" "$handlerId.input"
    Add-Wire "wi_page_result_$key" "$handlerId.result" 'ui.render.writer.result' -merge 'concat'
}

# 会话命令面（每个 cmd 恰一条源端口 + 一条 wire；禁共享源 + judge 分派）
foreach ($cmd in $commands) {
    $key = Get-WireKey $cmd
    $commandKeys[$cmd] = $key
    $sourceId = "ui.command_source.$key"
    $boxId = "ui.command.$key"
    $ports = @(
        @{ name = 'input'; dir = 'in'; form = 'HostMessage'; required = $true; card = 1 },
        @{ name = 'result'; dir = 'out'; form = 'RenderOutput'; card = 1 }
    )
    if ($key -eq 'composersend') {
        # 值槽：composer_send.draft（required Text）由输入采集链供给，空值在接线层即 missing-required。
        $ports = @(
            @{ name = 'input'; dir = 'in'; form = 'HostMessage'; required = $true; card = 1 },
            @{ name = 'draft'; dir = 'in'; form = 'Text'; required = $true; card = 1 },
            @{ name = 'result'; dir = 'out'; form = 'RenderOutput'; card = 1 }
        )
    }
    $boxes.Add(@{ id = $sourceId; impl = 'source:command'; out = @('event:HostMessage') })
    $boxes.Add(@{
        id = $boxId; impl = ("cmd:" + $cmd)
        ports = $ports
    })
    Add-Wire "wi_cmd_$key" "$sourceId.event" "$boxId.input"
    Add-Wire "wi_cmd_result_$key" "$boxId.result" 'ui.render.writer.result' -merge 'concat'
}

# 输入态：页面 composerSend 事件 → 文本适配 → 输入投影 → 状态存储
$composerKey = $null
foreach ($target in $pageTargets) {
    if ($target.Name -ieq 'composerSend') { $composerKey = Get-WireKey $target.Name; break }
}

$adapters = @(@{ id = 'event_to_text'; from = 'PageEvent'; to = 'Text'; impl = 'ui.input.project' })
$boxes.Add(@{
    id = 'ui.composer.project'; impl = 'composer:project'
    ports = @(
        @{ name = 'value'; dir = 'in'; form = 'Text'; required = $true; card = 1 },
        @{ name = 'draft'; dir = 'out'; form = 'Text'; card = 1 }
    )
})
$boxes.Add(@{
    id = 'ui.state.store'; impl = 'state:store'; terminal = $true
    ports = @(@{ name = 'snapshot'; dir = 'in'; form = 'Text'; required = $true; card = 1 })
})
Add-Wire 'wi_draft_store' 'ui.composer.project.draft' 'ui.state.store.snapshot'

if ($composerKey) {
    Add-Wire 'wi_composer_input' "ui.page_event.$composerKey.event" 'ui.composer.project.value' -adapt 'event_to_text' -judge 'cmd==composerSend'
}

# 值槽接线：输入采集事件 → composer_send.draft（required Text，空值在接线层即 missing-required）
$composerCommand = $commandKeys['composerSend']
if ($composerCommand) {
    Add-Wire 'wi_composer_draft' "ui.page_event.$composerKey.event" "ui.command.$composerCommand.draft" -adapt 'event_to_text' -judge 'cmd==composerSend'
}

# 覆盖率自证（生成器单源；门禁 C9 只比对声明内数字，不复制解析器）
$entryCount = $hostEvents.Count + $commands.Count
foreach ($target in $pageTargets) { if ($commands -notcontains $target.Name) { $entryCount++ } }
$coveredCount = @($wireIds | Where-Object {
    ($_ -like 'wi_host_event_*') -or
    (($_ -like 'wi_page_*') -and ($_ -notlike 'wi_page_result_*')) -or
    (($_ -like 'wi_cmd_*') -and ($_ -notlike 'wi_cmd_result_*'))
}).Count

$declaration = [ordered]@{
    '$schema' = 'wiring-declaration/v1'
    sourceId = 'agent.ui.generated'
    sourceHash = $sourceHash
    sourceRefs = $uiFiles
    forms = $forms
    boxes = $boxes
    wires = $wires
    adapters = $adapters
    profiles = @(@{ id = 'ui.all'; enable = $wireIds; scope = 'ui' })
    coverage = @{ entries = $entryCount; covered = $coveredCount }
}

$outPath = Join-Path $Root $Output
$dir = Split-Path -Parent $outPath
if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
$declaration | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $outPath -Encoding UTF8

Write-Host ("接线声明已生成: {0} (wires={1}, boxes={2}, sourceHash={3})" -f $Output, $wires.Count, $boxes.Count, $sourceHash.Substring(0, 12))

