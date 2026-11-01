#requires -Version 5.1
<#
.SYNOPSIS
    呈现契约守卫（PresentationContract）—— 呈现身份只由声明面决定，模板/渲染器不得二次推断。

.DESCRIPTION
    判据（载体 = 源码/模板字面量计数；扫描面 = Frame 默认模板 + Agent 呈现声明）：
      P1 模板无角色条件：`{?role=...}` 与 `role=tool` 命中 = 0（模板不得用角色决定样式/形态）。
      P2 声明完整：presentation.json 每个呈现类的 template 键必须在模板集中存在。
      P3 过程面不入气泡：surface=process 的类不得引用 bubbleRow。
      P4 裁决单一：AttachPlanner 与 ConversationProjectionContext 均经 PresentationResolver.Resolve（≥2 消费点）。
      P5 行模板齐备：bubbleRow / processRow / noticeRow / operationRow 均在默认模板集。
      P6 机制下沉：Modules/Node/Frame/FramePresentation.cs 存在（呈现裁决唯一实现）。
      P7 无第二裁决：PresentationResolver 委托 FramePresentation.Resolve，且不含 terminal == true 分支。
      NC 正/负对照：合成 `{?role=tool}` 必被 P1 捕获；普通条件 `{?presentationUnknown}` 不命中。
    模式：默认报告；-Check 违规 → 退出码 1。只读、0 写。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'

function Find-RepoRoot {
    $d = $PSScriptRoot
    while ($d -and -not (Test-Path (Join-Path $d 'Frame\CsharpPrj'))) { $d = Split-Path $d -Parent }
    if ($d) { return $d }
    throw '[PresentationContract] 无法定位仓库根（上溯未找到含 Frame\CsharpPrj 的目录）'
}

$root = Find-RepoRoot
$templatesPath = Join-Path $root 'Frame\CsharpPrj\Plate\HtmlViewer\Assets\templates.default.json'
$presentationPath = Join-Path $root 'Projects\Plug\Agent\Config\nodes\presentation.json'
$plannerPath = Join-Path $root 'Frame\CsharpPrj\Share\Frameworks\Conversation\Rendering\AttachPlanner.cs'
$contextPath = Join-Path $root 'Frame\CsharpPrj\Operation\FrontEndDriven\HtmlViewerBridge\Projection\ConversationProjectionContext.cs'

$violations = [System.Collections.Generic.List[string]]::new()
function Add-Violation([string]$id, [string]$msg) { $script:violations.Add("$id $msg") }
function Read-Text([string]$path) { if (-not (Test-Path -LiteralPath $path)) { throw "缺文件: $path" }; Get-Content -LiteralPath $path -Raw }

$tplText = Read-Text $templatesPath
$templates = $tplText | ConvertFrom-Json
$presentation = Read-Text $presentationPath | ConvertFrom-Json

# P1 模板无角色条件
$roleCondition = '\{\?\s*role\s*='
$roleHits = [regex]::Matches($tplText, $roleCondition).Count
$roleToken = [regex]::Matches($tplText, 'role\s*=\s*\\?"?tool').Count
Write-Host ("[PresentationContract] P1 role-condition={0} role=tool={1}" -f $roleHits, $roleToken)
if ($roleHits -gt 0) { Add-Violation 'P1' ("模板含角色条件 {0} 处（应由呈现类决定形态）" -f $roleHits) }
if ($roleToken -gt 0) { Add-Violation 'P1' ("模板含 role=tool 映射 {0} 处" -f $roleToken) }

# P2/P3 声明类 → 模板
$templateKeys = @($templates.PSObject.Properties.Name)
$classCount = 0
foreach ($prop in $presentation.classes.PSObject.Properties) {
    $classCount++
    $id = $prop.Name
    $spec = $prop.Value
    if ([string]::IsNullOrWhiteSpace([string]$spec.template)) {
        Add-Violation 'P2' ("呈现类 '{0}' 缺 template" -f $id)
        continue
    }
    if ($templateKeys -notcontains $spec.template) {
        Add-Violation 'P2' ("呈现类 '{0}' 引用未声明模板 '{1}'" -f $id, $spec.template)
    }
    if ($spec.surface -eq 'process' -and $spec.template -eq 'bubbleRow') {
        Add-Violation 'P3' ("过程类 '{0}' 引用气泡模板 bubbleRow" -f $id)
    }
}
if ($classCount -eq 0) { Add-Violation 'P2' '呈现声明为空（判据无标的）' }
Write-Host ("[PresentationContract] P2/P3 classes={0} templates={1}" -f $classCount, $templateKeys.Count)

# P4 裁决单一（两个消费点均经 resolver）
$plannerText = Read-Text $plannerPath
$contextText = Read-Text $contextPath
$plannerUses = $plannerText -match 'PresentationResolver\.Resolve'
$contextUses = $contextText -match 'PresentationResolver\.Resolve'
Write-Host ("[PresentationContract] P4 planner={0} context={1}" -f $plannerUses, $contextUses)
if (-not $plannerUses) { Add-Violation 'P4' 'AttachPlanner 未经 PresentationResolver.Resolve（出现第二套锚点判定）' }
if (-not $contextUses) { Add-Violation 'P4' 'ConversationProjectionContext 未经 PresentationResolver.Resolve' }

# P6/P7 呈现裁决单一实现（机制下沉 Modules.Node.Frame；Share 只做领域绑定）
$framePresentationPath = Join-Path $root 'Frame\CsharpPrj\Modules\Node\Frame\FramePresentation.cs'
$resolverPath = Join-Path $root 'Frame\CsharpPrj\Share\Frameworks\Conversation\Presentation\PresentationResolver.cs'
$framePresentationText = if (Test-Path -LiteralPath $framePresentationPath) { Get-Content -LiteralPath $framePresentationPath -Raw } else { $null }
$resolverText = if (Test-Path -LiteralPath $resolverPath) { Get-Content -LiteralPath $resolverPath -Raw } else { $null }
$mechanismOk = ($framePresentationText -match 'public static class FramePresentation') -and ($framePresentationText -match 'Resolve\(')
$resolverDelegates = ($resolverText -match 'FramePresentation\.Resolve')
$resolverHasOwnDecision = ($resolverText -match 'terminal\s*==\s*true')
if (-not $mechanismOk) { Add-Violation 'P6' '缺 Modules/Node/Frame/FramePresentation.cs（呈现裁决机制未下沉框架）' }
if (-not $resolverDelegates) { Add-Violation 'P7' 'PresentationResolver 未经 FramePresentation.Resolve（出现第二套裁决）' }
if ($resolverHasOwnDecision) { Add-Violation 'P7' 'PresentationResolver 仍含 terminal == true 裁决分支（第二实现）' }
Write-Host ("[PresentationContract] P6/P7 mechanism={0} delegates={1} secondDecision={2}" -f $mechanismOk, $resolverDelegates, $resolverHasOwnDecision)

# P5 行模板齐备
foreach ($key in @('bubbleRow', 'processRow', 'noticeRow', 'operationRow')) {
    if ($templateKeys -notcontains $key) { Add-Violation 'P5' ("缺行模板 '{0}'" -f $key) }
}

if ($NegativeControl) {
    $pos = [regex]::IsMatch('<div class="block {?role=tool}assistant{/}">', $roleCondition)
    $neg = [regex]::IsMatch('<span>{?presentationUnknown}未知{/}</span>', $roleCondition)
    if (-not $pos) { Add-Violation 'NC' '正对照未命中：合成 {?role=tool} 未被 P1 捕获' }
    if ($neg) { Add-Violation 'NC' '负对照误命中：普通呈现条件被判为角色条件' }
    Write-Host ("[PresentationContract] NC pos={0} neg={1}" -f $pos, $neg)
}

if ($violations.Count -gt 0) {
    foreach ($v in $violations) { Write-Host ("[VIOLATION] {0}" -f $v) }
    if ($Check) { exit 1 }
}
else {
    Write-Host '[PresentationContract] 通过（0 违规）'
    if ($Check) { exit 0 }
}
