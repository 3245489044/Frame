# New-KeySpacePortContract.ps1
# 键空间端口契约生成器（声明面 → JSON）：唯一判据 = 工作流 [Node] inports=/outports=。
# 默认只打印差集；-Write 才合并写回（新增默认 Optional；已有条目保持不变）。
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Write
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$declarationPath = Join-Path $Root 'Projects\Plug\Agent\Config\keyspaces\agent.keyspace.json'
$workflowDir = Join-Path $Root 'Projects\Plug\Agent\Config\workflows'
if (-not (Test-Path -LiteralPath $declarationPath)) { throw "声明文件不存在：$declarationPath" }
if (-not (Test-Path -LiteralPath $workflowDir)) { throw "工作流目录不存在：$workflowDir" }

$json = Get-Content -LiteralPath $declarationPath -Raw | ConvertFrom-Json

# 族：agent.port 是端口契约的默认归属（模板 {port}，无前缀）。
$families = @($json.families)
if (-not ($families | Where-Object { [string]$_.id -eq 'agent.port' })) {
    $families += [pscustomobject]@{
        id           = 'agent.port'
        template     = '{port}'
        familyPrefix = ''
        relation     = 'Member'
        cardinality  = 'Many'
        aliases      = @()
    }
}

$existing = @{}
$ports = @($json.ports)
foreach ($port in $ports) {
    $existing["$([string]$port.direction)|$([string]$port.name)"] = $port
}

$generated = [System.Collections.Generic.List[object]]::new()
foreach ($file in @(Get-ChildItem -LiteralPath $workflowDir -Filter *.ushar -File)) {
    foreach ($line in Get-Content -LiteralPath $file.FullName) {
        $trimmed = $line.Trim()
        if (-not $trimmed.StartsWith('[Node]')) { continue }

        foreach ($spec in @(@{ Marker = 'inports='; Direction = 'In' }, @{ Marker = 'outports='; Direction = 'Out' })) {
            foreach ($match in [regex]::Matches($trimmed, [regex]::Escape($spec.Marker) + '([^|]+)')) {
                foreach ($token in ($match.Groups[1].Value -split ',')) {
                    $name = $token.Trim()
                    if ($name.Length -eq 0) { continue }

                    $key = "$($spec.Direction)|$name"
                    if ($existing.ContainsKey($key)) { continue }
                    $existing[$key] = $null

                    $generated.Add([pscustomobject]@{
                        name        = $name
                        direction   = $spec.Direction
                        familyId    = 'agent.port'
                        required    = $false
                        defaultValue = $null
                        cardinality = 'One'
                    })
                }
            }
        }
    }
}

Write-Host ("端口契约：已有={0} 新增={1} 生成后总量={2}" -f $ports.Count, $generated.Count, ($ports.Count + $generated.Count))
foreach ($port in $generated) {
    Write-Host ("  + [{0}] {1}" -f $port.direction, $port.name)
}

if (-not $Write) {
    Write-Host '报告模式：加 -Write 合并写回 agent.keyspace.json'
    return
}

$merged = @($ports) + @($generated)
$merged = @($merged | Sort-Object @{ Expression = { [string]$_.direction } }, @{ Expression = { [string]$_.name } })

# 保序输出：id → families → ports → policies（与既有文件结构一致）。
$result = [ordered]@{
    id       = [string]$json.id
    families = $families
    ports    = $merged
    policies = $json.policies
}

$result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $declarationPath -Encoding utf8
Write-Host ("已写回：{0}" -f $declarationPath)
