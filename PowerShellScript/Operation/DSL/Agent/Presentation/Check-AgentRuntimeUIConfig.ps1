#requires -Version 5.1
<#
.SYNOPSIS
    JSON 驱动 UI 配置自检器 —— 布局重叠 + 绑定链一致性（编写期回归防护）。

.DESCRIPTION
    校验 JsonConfig/*.json 形式的 UI 配置（views/controls/layout/bindings 契约）：
      检查1 布局重叠  同父兄弟控件（排除 TabControl/TabPage 固有堆叠）归一化包围盒相交 → Warning
      检查2 绑定悬空  bindings.source / bindings.output 指向不存在的控件名           → Error
      检查3 函数未注册 events.click 函数名不在已知注册函数清单                      → Warning

    用途：把「手写归一化坐标漂移 → 静默重叠」「绑定源/输出改名 → 静默断链」这两类
    AgentRuntimeUI.json 历史缺陷，转化为编写期可观测报告，防止回归。
    对齐既有 PowerShellScript/DSL/Declarations/Check-ClinerulesReferences.ps1 模式（独立脚本、无运行时副作用）。

.PARAMETER ConfigPath
    待校验的 UI 配置 JSON（默认 Projects/Information/DrivenConfig/JsonConfig/AgentRuntimeUI.json ——
    FileDriven 的 DrivenConfig 资源已整体迁入 Information 资源仓，见 Modules\FileDriven\FileDrivenTestUtils.ps1）。

.PARAMETER WorkFlowDir
    已知注册函数（工作流名）来源目录（默认 Projects/Information/DrivenConfig/WorkFlow），
    其下 *.json 的 workflowName 字段视为已注册函数。

.PARAMETER FailOnWarning
    指定后 Warning 也导致非零退出码（用于 CI 严格模式）。

.EXAMPLE
    ./Check-AgentRuntimeUIConfig.ps1
    ./Check-AgentRuntimeUIConfig.ps1 -ConfigPath .\MathFunctions.json -FailOnWarning
#>
[CmdletBinding()]
param(
    # FileDriven 的 DrivenConfig 资源已整体迁入 Information 资源仓（见 Modules\FileDriven\FileDrivenTestUtils.ps1）；
    # 仓库超根由门禁运行时解析（与脚本所在目录深度无关）
    [string]$ConfigPath = '',
    [string]$WorkFlowDir = '',
    # 框架默认模板（判据载体：配置 templates 覆盖键必须存在于本键集，防「默认/覆盖」两份声明漂移）
    [string]$DefaultTemplatesPath = '',
    [switch]$FailOnWarning
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking


# 参数默认值改由仓库超根派生（参数绑定期 $PSScriptRoot 不可用；与目录深度无关）
$Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ConfigPath)) { $ConfigPath = Join-Path $Root 'Projects\Information\DrivenConfig\JsonConfig\AgentRuntimeUI.json' }
if ([string]::IsNullOrWhiteSpace($WorkFlowDir)) { $WorkFlowDir = Join-Path $Root 'Projects\Information\DrivenConfig\WorkFlow' }
if ([string]::IsNullOrWhiteSpace($DefaultTemplatesPath)) { $DefaultTemplatesPath = Join-Path $Root 'Frame\CsharpPrj\Plate\HtmlViewer\Assets\templates.default.json' }

$ExitCode = 0
$Errors = [System.Collections.Generic.List[string]]::new()
$Warnings = [System.Collections.Generic.List[string]]::new()

function Add-Error([string]$m) { $script:Errors.Add($m); $script:ExitCode = 1 }
function Add-Warning([string]$m) { $script:Warnings.Add($m) }

# StrictMode 安全取属性（对象为 null 或属性不存在 → 返回 $null，不抛错）
function Get-Prop($obj, [string]$name) {
    if ($null -eq $obj) { return $null }
    $p = $obj.PSObject.Properties[$name]
    if ($null -eq $p) { return $null }
    return $p.Value
}


# ---- 检查0：JSON 可解析 ----
if (-not (Test-Path -LiteralPath $ConfigPath)) {
    Write-Error "配置不存在: $ConfigPath"
    exit 2
}
try {
    $config = Get-Content -Raw -LiteralPath $ConfigPath | ConvertFrom-Json -Depth 64
}
catch {
    Write-Error "配置 JSON 解析失败: $($_.Exception.Message)"
    exit 2
}

# ---- 收集全部控件名（递归） ----
# 作用域 = **组合后 UI**，不只本文件：绑定的 output/source 按名解析发生在 MainLayout 组合后的控件树上，
# 跨文件片段绑定是合法契约（实证：MainLayout.json 的 outputTextBox 显式注释「绑定 output=outputTextBox 保持兼容」）。
# 故判据面 = 本文件 views/templates + **同目录全部 JsonConfig 片段**，否则跨文件绑定被误报为「悬空」。
$controlNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
function Collect-Names([array]$nodes) {
    foreach ($c in $nodes) {
        $name = Get-Prop $c 'name'
        if ($name) { [void]$controlNames.Add([string]$name) }
        $children = Get-Prop $c 'controls'
        if ($children) { Collect-Names $children }
    }
}
function Collect-ConfigNames($cfg) {
    if ($null -eq $cfg) { return }
    foreach ($view in @(Get-Prop $cfg 'views')) {
        $vc = if ($null -ne $view) { Get-Prop $view 'controls' } else { $null }
        if ($vc) { Collect-Names $vc }
    }
    $tpl = Get-Prop $cfg 'templates'
    if ($tpl -and $tpl.PSObject) {
        foreach ($t in $tpl.PSObject.Properties) {
            $tn = Get-Prop $t.Value 'controls'
            if ($tn) { Collect-Names $tn }
        }
    }
}
Collect-ConfigNames $config
$configDir = Split-Path -Parent $ConfigPath
$configLeaf = Split-Path -Leaf $ConfigPath
foreach ($sib in @(Get-ChildItem -LiteralPath $configDir -Filter *.json -File -ErrorAction SilentlyContinue)) {
    if ($sib.Name -eq $configLeaf) { continue }
    try { Collect-ConfigNames (Get-Content -Raw -LiteralPath $sib.FullName | ConvertFrom-Json -Depth 64) }
    catch { Add-Warning "片段解析失败（未纳入控件名集合）: $($sib.Name) — $($_.Exception.Message)" }
}

# ---- 收集页面区域声明键（htmlPage.regions[].key）：region:<key> 的合法域 ----
$regionKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
function Collect-RegionKeys($node) {
    if ($null -eq $node) { return }
    if ($node -is [System.Collections.IEnumerable] -and $node -isnot [string]) {
        foreach ($item in $node) { Collect-RegionKeys $item }
        return
    }
    if ($node -isnot [pscustomobject]) { return }
    $regions = Get-Prop $node 'regions'
    if ($regions) {
        foreach ($r in @($regions)) {
            $k = Get-Prop $r 'key'
            if ($k) { [void]$regionKeys.Add([string]$k) }
        }
    }
    foreach ($p in $node.PSObject.Properties) {
        if ($p.Name -eq 'regions') { continue }
        Collect-RegionKeys $p.Value
    }
}
Collect-RegionKeys $config

# 输出目标合法性（单一判据）：region:<key> → 区域声明集；host:<name> → 控件集去前缀；裸名 → 控件集。
function Test-ValidOutput([string]$output) {
    if ([string]::IsNullOrWhiteSpace($output)) { return $true }
    if ($output.StartsWith('region:', [System.StringComparison]::OrdinalIgnoreCase)) {
        return $regionKeys.Contains($output.Substring(7).Trim())
    }
    if ($output.StartsWith('host:', [System.StringComparison]::OrdinalIgnoreCase)) {
        return $controlNames.Contains($output.Substring(5).Trim())
    }
    return $controlNames.Contains($output)
}

# ---- 检查1：布局重叠（兄弟级，排除 TabControl/TabPage 固有堆叠） ----
$overlapEpsilon = 1e-4   # 亚像素容差，避免浮点相邻误报
function Test-Overlap($a, $b) {
    $aL = [double]$a.layout.left;  $aT = [double]$a.layout.top
    $aW = [double]$a.layout.width; $aH = [double]$a.layout.height
    $bL = [double]$b.layout.left;  $bT = [double]$b.layout.top
    $bW = [double]$b.layout.width; $bH = [double]$b.layout.height
    $aR = $aL + $aW; $aB = $aT + $aH
    $bR = $bL + $bW; $bB = $bT + $bH
    $ovx = ($aR - $bL) -gt $overlapEpsilon -and ($bR - $aL) -gt $overlapEpsilon
    $ovy = ($aB - $bT) -gt $overlapEpsilon -and ($bB - $aT) -gt $overlapEpsilon
    return ($ovx -and $ovy)
}

function Check-Container([array]$nodes, [string]$ctx, [bool]$skipOverlap) {
    if (-not $skipOverlap -and $nodes.Count -ge 2) {
        for ($i = 0; $i -lt $nodes.Count; $i++) {
            for ($k = $i + 1; $k -lt $nodes.Count; $k++) {
                $a = $nodes[$i]; $b = $nodes[$k]
                $al = Get-Prop $a 'layout'; $bl = Get-Prop $b 'layout'
                if ($al -and $bl -and (Test-Overlap $a $b)) {
                    Add-Warning "布局重叠 [$ctx]: $(Get-Prop $a 'name') x $(Get-Prop $b 'name')"
                }
            }
        }
    }
    foreach ($n in $nodes) {
        $children = Get-Prop $n 'controls'
        if ($children) {
            # TabControl 的直系子节点是 TabPage（固有堆叠），跳过其相互重叠；其内部仍正常递归
            $childSkip = ((Get-Prop $n 'type') -eq 'tabcontrol')
            Check-Container $children (Get-Prop $n 'name') $childSkip
        }
    }
}
foreach ($view in $config.views) {
    $vc = Get-Prop $view 'controls'
    if ($vc) { Check-Container $vc $view.name $false }
}


# ---- 检查2：绑定源 / 输出控件名悬空 ----
function Check-Bindings($nodes) {
    foreach ($c in $nodes) {
        $events = Get-Prop $c 'events'
        if ($events) {
            $bind = Get-Prop $events 'bindings'
            if ($bind) {
                $output = Get-Prop $bind 'output'
                if ($output -and -not (Test-ValidOutput ([string]$output))) {
                    Add-Error "绑定输出悬空: $(Get-Prop $c 'name') → output=$output（控件/区域均未声明）"
                }
                $parameters = Get-Prop $bind 'parameters'
                if ($parameters) {
                    foreach ($p in $parameters) {
                        $sourceType = Get-Prop $p 'sourceType'
                        $source = Get-Prop $p 'source'
                        if ($sourceType -eq 'control' -and $source -and -not $controlNames.Contains([string]$source)) {
                            Add-Error "绑定参数源悬空: $(Get-Prop $c 'name') → source=$source"
                        }
                    }
                }
            }
        }
        $children = Get-Prop $c 'controls'
        if ($children) { Check-Bindings $children }
    }
}
foreach ($view in $config.views) {
    $vc = Get-Prop $view 'controls'
    if ($vc) { Check-Bindings $vc }
}

# ---- 检查2b：页面级事件目标（htmlPage.eventTargets）——绑定目标 + 工作流节点结构 ----
$validStepTypes = @('Function', 'Condition', 'Loop')
function Check-WorkflowNode([string]$workflowName, $node, [string]$path) {
    if ($null -eq $node) { return }
    $id = Get-Prop $node 'id'
    $fn = Get-Prop $node 'functionName'
    $legacyFn = Get-Prop $node 'function'
    $children = Get-Prop $node 'children'
    $elseChildren = Get-Prop $node 'elseChildren'
    if ($legacyFn -and -not $fn) {
        Add-Error "工作流节点键名非规范: $path `(workflow=$workflowName node=$id`)，'function' 应为 'functionName'"
    }
    if (-not $fn -and -not $legacyFn -and -not $children -and -not $elseChildren) {
        Add-Error "工作流节点缺少 functionName 且无子节点: $path `(workflow=$workflowName node=$id`)"
    }
    if (-not $id) { Add-Warning "工作流节点缺少 id: $path (workflow=$workflowName)" }
    $stepType = Get-Prop $node 'stepType'
    if ($stepType -and ($validStepTypes -notcontains [string]$stepType)) {
        Add-Error "工作流节点 stepType 非法: $path → $stepType（允许 Function/Condition/Loop）"
    }
    foreach ($c in @($children)) { Check-WorkflowNode $workflowName $c "$path.children" }
    foreach ($c in @($elseChildren)) { Check-WorkflowNode $workflowName $c "$path.elseChildren" }
}
function Check-EventTarget([string]$targetName, $target) {
    if ($null -eq $target) { return }
    $events = Get-Prop $target 'events'
    if (-not $events) { return }
    $bind = Get-Prop $events 'bindings'
    if (-not $bind) { return }
    $output = Get-Prop $bind 'output'
    if ($output -and -not (Test-ValidOutput ([string]$output))) {
        Add-Error "事件目标输出悬空: $targetName → output=$output（控件/区域均未声明）"
    }
    foreach ($p in @(Get-Prop $bind 'parameters')) {
        $sourceType = Get-Prop $p 'sourceType'
        $source = Get-Prop $p 'source'
        if ($sourceType -eq 'control' -and $source -and -not $controlNames.Contains([string]$source)) {
            Add-Error "事件目标参数源悬空: $targetName → source=$source"
        }
    }
    $click = Get-Prop $events 'click'
    $workflows = Get-Prop $bind 'workflows'
    if ($workflows -and $workflows.PSObject) {
        if ($click -and -not $workflows.PSObject.Properties[$click]) {
            Add-Warning "事件目标 click 意图无同名工作流: $targetName → click=$click"
        }
        foreach ($wf in $workflows.PSObject.Properties) {
            $nodes = Get-Prop $wf.Value 'nodes'
            if (-not $nodes -or @($nodes).Count -eq 0) {
                Add-Error "工作流无节点: workflow=$($wf.Name) target=$targetName"
                continue
            }
            $i = 0
            foreach ($n in @($nodes)) {
                Check-WorkflowNode ([string]$wf.Name) $n "eventTargets.$targetName.workflows.$($wf.Name).nodes[$i]"
                $i++
            }
        }
    }
}
function Walk-EventTargets($nodes) {
    foreach ($c in $nodes) {
        $page = Get-Prop $c 'htmlPage'
        if ($page) {
            $targets = Get-Prop $page 'eventTargets'
            if ($targets -and $targets.PSObject) {
                foreach ($t in $targets.PSObject.Properties) { Check-EventTarget $t.Name $t.Value }
            }
        }
        $children = Get-Prop $c 'controls'
        if ($children) { Walk-EventTargets $children }
    }
}
foreach ($view in $config.views) {
    $vc = Get-Prop $view 'controls'
    if ($vc) { Walk-EventTargets $vc }
}

# ---- 检查3：click / initCommands 函数名注册（前缀库 + 工作流名清单） ----
$knownFunctions = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
function Test-KnownPrefix([string]$name) {
    return $name -like 'Ui_*' -or $name -like 'AgentUi_*'
}
# 工作流函数名（WorkFlowDir 下各 *.json 的 workflowName）视为已注册
if (Test-Path -LiteralPath $WorkFlowDir) {
    Get-ChildItem -LiteralPath $WorkFlowDir -Filter '*.json' -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $wf = Get-Content -Raw -LiteralPath $_.FullName | ConvertFrom-Json -Depth 32
            if ($wf.workflowName) { [void]$knownFunctions.Add([string]$wf.workflowName) }
        }
        catch { }
    }
}
function Check-ClickFunctions($nodes) {
    foreach ($c in $nodes) {
        $events = Get-Prop $c 'events'
        if ($events) {
            $click = Get-Prop $events 'click'
            if ($click) {
                foreach ($fn in @($click)) {
                    if ($knownFunctions.Contains([string]$fn) -or (Test-KnownPrefix ([string]$fn))) {
                        continue
                    }
                    Add-Warning "click 函数未在已知注册清单: $(Get-Prop $c 'name') → $fn（若为合法注册函数请加入 allowlist）"
                }
            }
        }
        $children = Get-Prop $c 'controls'
        if ($children) { Check-ClickFunctions $children }
    }
}
foreach ($view in $config.views) {
    $vc = Get-Prop $view 'controls'
    if ($vc) { Check-ClickFunctions $vc }
}

if ($config.initCommands) {
    foreach ($cmd in $config.initCommands) {
        if ($cmd -match 'exec\s+([A-Za-z0-9_]+)') {
            $fn = $Matches[1]
            if (-not $knownFunctions.Contains($fn) -and -not (Test-KnownPrefix $fn)) {
                Add-Warning "initCommands 函数未在已知注册清单: $fn"
            }
        }
    }
}

# ---- 检查4：模板同父键冲突（键控对齐身份键须同父唯一；身份键/具名锚=data-key/data-name/id） ----
$alignAttrs = @('data-key', 'data-name', 'id')
$voidTags = @('area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link', 'meta', 'param', 'source', 'track', 'wbr')
$tagRe = [regex]'<(/?)([a-zA-Z][\w:.-]*)((?:"[^"]*"|''[^'']*''|[^>"''])*)>'

function Get-AlignKey([string]$attrs) {
    foreach ($a in $alignAttrs) {
        $m = [regex]::Match($attrs, '(?i)(?:^|\s)' + [regex]::Escape($a) + '\s*=\s*(?:"([^"]*)"|''([^'']*)''|([^\s>]+))')
        if ($m.Success) {
            $v = if ($m.Groups[1].Success) { $m.Groups[1].Value }
                 elseif ($m.Groups[2].Success) { $m.Groups[2].Value }
                 else { $m.Groups[3].Value }
            if ($v) { return ($a + '=' + $v) }
        }
    }
    return $null
}

function Check-TemplateKeys([string]$templateName, [string]$html) {
    if ([string]::IsNullOrEmpty($html)) { return }
    $stack = [System.Collections.Generic.List[object]]::new()
    $stack.Add([pscustomobject]@{ Tag = '(root)'; Keys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal) })
    foreach ($m in $tagRe.Matches($html)) {
        $isClose = ($m.Groups[1].Value -eq '/')
        $tag = $m.Groups[2].Value.ToLowerInvariant()
        $attrs = $m.Groups[3].Value
        if ($isClose) {
            for ($i = $stack.Count - 1; $i -ge 1; $i--) {
                if ($stack[$i].Tag -eq $tag) { $stack.RemoveRange($i, $stack.Count - $i); break }
            }
            continue
        }
        $selfClose = $attrs.TrimEnd().EndsWith('/')
        $key = Get-AlignKey $attrs
        if ($key) {
            $parent = $stack[$stack.Count - 1]
            if (-not $parent.Keys.Add($key)) {
                Add-Error "模板同父键冲突: $templateName → $key（父级 <$($parent.Tag)> 下身份键必须唯一）"
            }
        }
        if (-not $selfClose -and -not ($voidTags -contains $tag)) {
            $stack.Add([pscustomobject]@{ Tag = $tag; Keys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal) })
        }
    }
}

$tplObj = Get-Prop $config 'templates'
if ($tplObj -and $tplObj.PSObject) {
    foreach ($t in $tplObj.PSObject.Properties) {
        if ($t.Value -is [string]) { Check-TemplateKeys $t.Name ([string]$t.Value) }
    }
}

# ---- 检查5：事件路由声明（data-cmd="event:*" ⇒ 必须 data-route；路由须落到同页 eventTargets 的同名事件） ----
# 契约：data-name = 唯一寻址键（realclick/断言/DOM 读写），data-route = 静态路由键（eventTargets 命中）。
$eventTargetEvents   = @{}   # 目标名 → HashSet[事件名]
$eventTargetClickFn  = @{}   # 目标名 → click 函数名清单
$eventTargetWorkflow = @{}   # 目标名 → HashSet[本目标声明的 workflow 名]

function Get-AttrValue([string]$attrs, [string]$attributeName) {
    $m = [regex]::Match($attrs, '(?i)(?:^|\s)' + [regex]::Escape($attributeName) + '\s*=\s*(?:"([^"]*)"|''([^'']*)''|([^\s>]+))')
    if (-not $m.Success) { return $null }
    if ($m.Groups[1].Success) { return $m.Groups[1].Value }
    if ($m.Groups[2].Success) { return $m.Groups[2].Value }
    return $m.Groups[3].Value
}

function Collect-EventTargetIndex($nodes) {
    foreach ($c in $nodes) {
        $page = Get-Prop $c 'htmlPage'
        if ($page) {
            $targets = Get-Prop $page 'eventTargets'
            if ($targets -and $targets.PSObject) {
                foreach ($t in $targets.PSObject.Properties) {
                    $declaredName = Get-Prop $t.Value 'name'
                    $name = if ([string]::IsNullOrWhiteSpace([string]$declaredName)) { $t.Name } else { [string]$declaredName }

                    $events = Get-Prop $t.Value 'events'
                    $eventNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
                    $clickFns = @()
                    $workflowNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
                    if ($events -and $events.PSObject) {
                        foreach ($ev in $events.PSObject.Properties) {
                            if ($ev.Name -eq 'bindings') { continue }
                            [void]$eventNames.Add($ev.Name)
                            if ($ev.Name -eq 'click') { $clickFns = @($ev.Value) }
                        }
                        $bind = Get-Prop $events 'bindings'
                        $wfs = Get-Prop $bind 'workflows'
                        if ($wfs -and $wfs.PSObject) {
                            foreach ($wf in $wfs.PSObject.Properties) { [void]$workflowNames.Add($wf.Name) }
                        }
                    }

                    $eventTargetEvents[$name] = $eventNames
                    $eventTargetClickFn[$name] = $clickFns
                    $eventTargetWorkflow[$name] = $workflowNames
                }
            }
        }
        $children = Get-Prop $c 'controls'
        if ($children) { Collect-EventTargetIndex $children }
    }
}

function Test-EventRouteDeclared([string]$route, [string]$eventName) {
    foreach ($name in $eventTargetEvents.Keys) {
        $hit = $false
        $open = $route.IndexOf('{')
        if ($open -lt 0) {
            $hit = [string]::Equals($route, $name, [System.StringComparison]::OrdinalIgnoreCase)
        }
        else {
            # 模板路由（如 viewType-{type}）：按模板前缀匹配目标名
            $prefix = $route.Substring(0, $open)
            $hit = $name.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
        }
        if ($hit -and $eventTargetEvents[$name].Contains($eventName)) { return $true }
    }
    return $false
}

function Check-EventTemplates([string]$source, $templates) {
    if (-not $templates -or -not $templates.PSObject) { return }
    foreach ($t in $templates.PSObject.Properties) {
        if ($t.Value -isnot [string]) { continue }
        foreach ($m in $tagRe.Matches([string]$t.Value)) {
            if ($m.Groups[1].Value -eq '/') { continue }
            $attrs = $m.Groups[3].Value
            $cmd = Get-AttrValue $attrs 'data-cmd'
            if (-not $cmd -or -not $cmd.StartsWith('event:', [System.StringComparison]::OrdinalIgnoreCase)) { continue }

            $eventName = $cmd.Substring(6)
            $route = Get-AttrValue $attrs 'data-route'
            $domName = Get-AttrValue $attrs 'data-name'
            if ([string]::IsNullOrWhiteSpace($route)) {
                # 允许「与 eventTargets 同名」的静态寻址键（无模板占位），其余必须显式 data-route。
                if (-not [string]::IsNullOrWhiteSpace($domName) -and $domName.IndexOf('{') -lt 0 -and (Test-EventRouteDeclared $domName $eventName)) { continue }
                Add-Error "事件元素缺 data-route（路由键与寻址键须分离）: $source → template=$($t.Name), data-name=$domName, data-cmd=$cmd"
                continue
            }
            if (-not (Test-EventRouteDeclared $route $eventName)) {
                Add-Error "事件元素路由不可解析: $source → template=$($t.Name), data-route=$route, event=$eventName（须命中 eventTargets 键且该目标声明同名事件）"
            }
        }
    }
}

foreach ($view in $config.views) {
    $vc = Get-Prop $view 'controls'
    if ($vc) { Collect-EventTargetIndex $vc }
}
Check-EventTemplates '配置 templates' (Get-Prop $config 'templates')

# ---- 检查5b：data-payload 的 JSON 花括号必须实体转义（R3 根因：{...} 被模板引擎当绑定吞掉） ----
function Check-PayloadEncoding([string]$source, $templates) {
    if (-not $templates -or -not $templates.PSObject) { return }
    foreach ($t in $templates.PSObject.Properties) {
        if ($t.Value -isnot [string]) { continue }
        foreach ($m in $tagRe.Matches([string]$t.Value)) {
            if ($m.Groups[1].Value -eq '/') { continue }
            $payload = Get-AttrValue $m.Groups[3].Value 'data-payload'
            if ([string]::IsNullOrWhiteSpace($payload)) { continue }
            if ($payload.TrimStart().StartsWith('{')) {
                Add-Error "data-payload 含未转义 JSON 花括号（模板引擎会按 {...} 绑定吞掉该段）: $source → template=$($t.Name)；须写 &#123; … &#125;"
            }
        }
    }
}
Check-PayloadEncoding '配置 templates' (Get-Prop $config 'templates')
if (Test-Path -LiteralPath $DefaultTemplatesPath) {
    try { Check-PayloadEncoding '框架默认模板' (Get-Content -Raw -LiteralPath $DefaultTemplatesPath | ConvertFrom-Json -Depth 16) }
    catch { }
}

# ---- 检查6：配置模板覆盖键 ⊆ 框架默认键集（D9：同一交互契约两份声明禁漂移） ----
$defaultTemplateKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
if (Test-Path -LiteralPath $DefaultTemplatesPath) {
    try {
        $defaultTpl = Get-Content -Raw -LiteralPath $DefaultTemplatesPath | ConvertFrom-Json -Depth 16
        foreach ($p in $defaultTpl.PSObject.Properties) {
            if ($p.Value -is [string]) { [void]$defaultTemplateKeys.Add($p.Name) }
        }
    }
    catch { Add-Warning "框架默认模板解析失败（判据不可判）: $($_.Exception.Message)" }
}
else { Add-Warning "框架默认模板缺件（判据不可判）: $DefaultTemplatesPath" }
if ($defaultTemplateKeys.Count -gt 0 -and $tplObj -and $tplObj.PSObject) {
    foreach ($t in $tplObj.PSObject.Properties) {
        if ($t.Value -isnot [string]) { continue }
        if (-not $defaultTemplateKeys.Contains($t.Name)) {
            Add-Error "模板覆盖键不存在于框架默认键集: $($t.Name)（新增键须先入框架默认模板）"
        }
    }
}

# ---- 检查7：eventTargets 事件函数可达性（本目标声明 ∨ 他处声明（全局复用）∨ 已知注册） ----
$declaredWorkflowAnywhere = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($set in $eventTargetWorkflow.Values) { foreach ($n in $set) { [void]$declaredWorkflowAnywhere.Add($n) } }
foreach ($name in $eventTargetClickFn.Keys) {
    foreach ($fn in @($eventTargetClickFn[$name])) {
        if ([string]::IsNullOrWhiteSpace([string]$fn)) { continue }
        if ($eventTargetWorkflow[$name].Contains([string]$fn)) { continue }
        if ($declaredWorkflowAnywhere.Contains([string]$fn)) { continue }
        if ($knownFunctions.Contains([string]$fn) -or (Test-KnownPrefix ([string]$fn))) { continue }
        Add-Warning "事件目标 click 函数不可达: $name → $fn（本目标未声明 workflow，且非已知注册函数）"
    }
}

# ---- 检查8：容器声明闭合（生成物容器集 ⊆ 页面 regions 声明；壳槽恒在） ----
$viewProgramsPath = Join-Path $Root 'Projects\Plug\Agent\Config\ui\view-programs.generated.json'
$shellRegionKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
if (Test-Path -LiteralPath $viewProgramsPath) {
    $viewPrograms = $null
    try { $viewPrograms = Get-Content -LiteralPath $viewProgramsPath -Raw | ConvertFrom-Json }
    catch { Add-Error '检查8 判据不可判：view-programs.generated.json 解析失败' }
    if ($viewPrograms) {
        $shellSlots = @('region-seg', 'region-head', 'region-subhead')
        foreach ($view in @($viewPrograms.views)) {
            $viewKey = [string]$view.viewKey
            $keys = @($view.containers | ForEach-Object { [string]$_.key })
            if ($keys.Count -eq 0) { Add-Error "检查8 视图 '$viewKey' 无容器声明"; continue }
            foreach ($slot in $shellSlots) {
                if ($keys -notcontains $slot) { Add-Error "检查8 视图 '$viewKey' 缺壳槽 $slot" }
            }
            foreach ($k in $keys) { [void]$shellRegionKeys.Add($k) }
        }
        if ($shellRegionKeys.Count -lt 8) {
            Add-Error "检查8 判据不可判：生成物容器集仅 $($shellRegionKeys.Count) 个（应 >= 8）"
        }
    }
}
else { Add-Error "检查8 判据不可判：缺件 $viewProgramsPath" }

function Collect-DeclaredRegions($node, $out) {
    if ($null -eq $node) { return }
    $regions = Get-Prop $node 'regions'
    if ($regions) {
        foreach ($r in @($regions)) {
            $k = Get-Prop $r 'key'
            if ($k) { [void]$out.Add([string]$k) }
        }
    }
    foreach ($prop in @('controls', 'views')) {
        $items = Get-Prop $node $prop
        if ($items) { foreach ($c in @($items)) { Collect-DeclaredRegions $c $out } }
    }
    $htmlPage = Get-Prop $node 'htmlPage'
    if ($htmlPage) { Collect-DeclaredRegions $htmlPage $out }
}

$declaredRegions = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
Collect-DeclaredRegions $config $declaredRegions
if ($declaredRegions.Count -gt 0 -and $shellRegionKeys.Count -gt 0) {
    foreach ($k in $shellRegionKeys) {
        if (-not $declaredRegions.Contains($k)) {
            Add-Error "检查8 区域声明不闭合（shell 产出但页面 regions 未声明）: $k"
        }
    }
    $neg = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    [void]$neg.Add('region-seg')
    $negMissing = @($shellRegionKeys | Where-Object { -not $neg.Contains($_) })
    if ($negMissing.Count -eq 0) { Add-Error '检查8 负对照失效：缺失未被判据捕获' }
}

# ---- 检查9：页面声明单源（ui-regions 区域键 ⊆ AgentRuntimeUI.htmlPage.regions；视图区域集闭合） ----
$uiRegionsPath9 = Join-Path $Root 'Projects\Plug\Agent\Config\ui\ui-regions.json'
if (Test-Path -LiteralPath $uiRegionsPath9) {
    $uiDecl = Get-Content -LiteralPath $uiRegionsPath9 -Raw | ConvertFrom-Json -Depth 16
    $uiKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($r9 in @($uiDecl.regions)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$r9.key)) { [void]$uiKeys.Add([string]$r9.key) }
    }
    foreach ($k9 in $uiKeys) {
        if (-not $declaredRegions.Contains($k9)) {
            Add-Error "检查9 区域声明不闭合（ui-regions 引用 region 未在页面 htmlPage.regions 声明）: $k9"
        }
    }

    $kinds9 = @{}
    foreach ($r9 in @($uiDecl.regions)) { $kinds9[[string]$r9.kind] = [string]$r9.key }
    foreach ($v9 in @($uiDecl.views)) {
        foreach ($kind9 in @($v9.regions)) {
            if (-not $kinds9.ContainsKey([string]$kind9)) {
                Add-Error "检查9 视图 $($v9.view) 引用未声明区域 kind: $kind9"
            }
        }
    }
} else {
    Add-Warning "检查9 判据不可判：缺件 $uiRegionsPath9"
}

# ---- 报告 ----
Write-Host "=== Check-AgentRuntimeUIConfig ===" -ForegroundColor Cyan
Write-Host ("配置: {0}" -f $ConfigPath)
Write-Host ("控件总数: {0}" -f $controlNames.Count)
Write-Host ("已知注册函数(工作流): {0}" -f $knownFunctions.Count)
Write-Host "--- 布局重叠 / 函数未注册 (Warning) ---" -ForegroundColor Yellow
if ($Warnings.Count -eq 0) { Write-Host "  (无)" }
else { $Warnings | ForEach-Object { Write-Host ("  [!] " + $_) -ForegroundColor Yellow } }
Write-Host "--- 绑定悬空 (Error) ---" -ForegroundColor Red
if ($Errors.Count -eq 0) { Write-Host "  (无)" }
else { $Errors | ForEach-Object { Write-Host ("  [X] " + $_) -ForegroundColor Red } }

if ($Errors.Count -eq 0 -and ($Warnings.Count -eq 0 -or -not $FailOnWarning)) {
    Write-Host "结果: 通过" -ForegroundColor Green
}
else {
    Write-Host "结果: 存在问题" -ForegroundColor Red
}

exit $ExitCode
