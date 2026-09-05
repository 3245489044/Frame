<#
端到端：分析文件夹 → 标准存储节点（PsRuntimeBinding 标准化动作 + 单一 C# 写入口）
流程：resolve.path → list.dir → read.file(-Persist on，自动转 file:<path> 节点) → store.resource(analysis:<folder>)
校验：存储桥 Agent.StorageBridge.exe get-resource 确认 analysis + file 节点已写入 ur_resources。
幂等：节点 id 稳定（upsert），可重复运行不残留重复。
对齐：FileDriven/DrivenConfig/Scripts/Agent/agent_task_node_loop_test.json（同思路：确定性动作 + 校验）。
注：PsRuntime 动作（list.dir/store.resource/read.file-Persist）为 PS 侧确定性动作，经 Invoke-AgentStep 调用，
故以 PowerShell 脚本承载（JSON 脚本仅驱动 AgentUi_* GUI 函数，无法直达 PsRuntime）。
#>
[CmdletBinding()]
param(
    [string]$Folder = "PowerShellScript\BasicMethod\FileOps",
    [string]$ValueFile = "PowerShellScript\BasicMethod\FileOps\FileAccess.psm1"
)
$ErrorActionPreference = "Stop"

$root = (Resolve-Path "$PSScriptRoot\..\..\..\..").Path   # WorkFlowPrj 根
. (Join-Path "$root\PowerShellScript\PsRuntime\Binding" "Invoke-AgentStep.ps1")
$bridgeExe = Join-Path "$root\Agent\BuildOutput\Debug" "Agent.StorageBridge.exe"

function Invoke-Step([string]$Action, $Params) {
    $r = Invoke-AgentStep -Action $Action -Params $Params
    if ($r.status -ne "success") { throw "$Action failed: $($r.errors -join ',')" }
    return $r
}

Write-Host "=== [1] resolve.path 定位文件夹 ==="
$r = Invoke-Step "resolve.path" @{ RelativePath = $Folder }
$resolved = if ($r.data -is [hashtable] -and $r.data.ContainsKey("ResolvedPath")) { $r.data.ResolvedPath }
            else { $r.data.payload }
Write-Host "resolved: $resolved"

Write-Host "=== [2] list.dir 枚举（Recurse）==="
$r = Invoke-Step "list.dir" @{ Path = $resolved; Recurse = $true }
$totalFiles = $r.data.TotalFiles
Write-Host "files=$totalFiles dirs=$($r.data.TotalDirs)"

Write-Host "=== [3] read.file -Persist on（自动转 file:<path> 节点）==="
$r = Invoke-Step "read.file" @{ Path = $ValueFile; Persist = "on" }
$fileNodeId = "file:$($r.data.Path)"
Write-Host "file node: $fileNodeId (persistStored=$($r.data._persistStored.status))"
if ($r.data._persistStored.status -ne "success") { throw "read.file 落库失败" }

Write-Host "=== [4] store.resource 写 analysis:<folder> 节点 ==="
$folderName = Split-Path $Folder -Leaf
$analysisId = "Storage\e2e_$folderName"
$meta = @{ folder = $resolved; totalFiles = $totalFiles; analyzedAt = (Get-Date).ToString("o") }
$r = Invoke-Step "store.resource" @{
    ResourceType = "analysis"
    Id = $analysisId
    Symbol = "analysis:$analysisId"
    Metadata = $meta
}
Write-Host "analysis node: analysis:$analysisId (upserted=$($r.data.Upserted))"

Write-Host "=== [5] 存储桥 get-resource 校验（无悬空）==="
foreach ($spec in @(
    @{ ResourceType = "analysis"; Id = $analysisId },
    @{ ResourceType = "file"; Id = $fileNodeId }
)) {
    $g = (& $bridgeExe get-resource --resourceType $spec.ResourceType --id $spec.Id | Out-String).Trim() | ConvertFrom-Json
    if ($g.data.Found) { Write-Host "  OK  $($spec.ResourceType):$($spec.Id)" }
    else { throw "未找到节点 $($spec.ResourceType):$($spec.Id)" }
}

Write-Host "=== 端到端通过：analysis + file 节点已写入 ur_resources ==="