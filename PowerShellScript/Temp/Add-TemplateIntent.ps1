# 一次性：给 AgentsRun 写入类模板插入 intent 判别式（W5 模板收敛）
# 用法: pwsh -File Add-TemplateIntent.ps1 [-WhatIf]
[CmdletBinding()]
param(
    [string]$TemplatesRoot = (Join-Path $PSScriptRoot '..\..\..\Projects\Information\DrivenConfig\Templates\AgentRun'),
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

# 模板 id → intent（只含**写入类**；读类模板不声明 intent，保持 legacy 只读路径）
$map = [ordered]@{
    'node/output.json'          = 'node.write'
    'node/record.json'          = 'node.write'
    'node/config.json'          = 'node.write'
    'node/event.json'           = 'node.write'
    'node/resource.json'        = 'node.write'
    'node/keymapping.json'      = 'node.write'
    'node/delete.json'          = 'node.delete'
    'task/enqueue.json'         = 'task.upsert'
    'task/submit.json'          = 'task.upsert'
    'task/activate.json'        = 'task.upsert'
    'task/delete.json'          = 'task.delete'
    'task/attach.json'          = 'node.link'
    'history/create.json'       = 'history.create'
    'history/delete.json'       = 'history.delete'
    'history/attachNode.json'   = 'history.link'
    'history/attachTask.json'   = 'history.link'
    'history/detachTask.json'   = 'node.unlink'
    'relations/declare.json'    = 'relation.declare'
    'relations/undeclare.json'  = 'relation.revoke'
    'graph/rebuild.json'        = 'graph.maintain'
    'taskcenter/addNode.json'   = 'scenario.apply'
    'taskcenter/createTask.json'= 'scenario.apply'
    'taskcenter/removeNode.json'= 'scenario.apply'
    'taskcenter/deleteTask.json'= 'scenario.apply'
}

$changed = 0
foreach ($rel in $map.Keys) {
    $path = Join-Path $TemplatesRoot ($rel -replace '/', '\')
    if (-not (Test-Path $path)) { Write-Warning "缺模板：$rel"; continue }
    $intent = $map[$rel]
    $text = Get-Content $path -Raw

    if ($text -match '"intent"\s*:') { Write-Host "SKIP(已有 intent): $rel"; continue }

    # 在 "_doc" 行后插入 intent（判别层紧随文档标题，符合统一键序：判别层 → 唯一名称 → 状态 → …）
    $new = [regex]::Replace(
        $text,
        '("_doc"\s*:\s*"(?:[^"\\]|\\.)*"\s*,)',
        { param($m) $m.Groups[1].Value + "`r`n  `"intent`": `"$intent`"," },
        1)
    if ($new -eq $text) { Write-Warning "未命中 _doc 行，跳过：$rel"; continue }

    if ($WhatIf) { Write-Host "[WhatIf] $rel → $intent" }
    else { Set-Content -Path $path -Value $new -NoNewline -Encoding UTF8; Write-Host "OK $rel → $intent" }
    $changed++
}
Write-Host "完成：$changed 个模板$([string]::IsNullOrEmpty($WhatIf) ? '' : '（WhatIf 未落盘）')"
