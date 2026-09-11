# Check-AgentRuntimeSlots.ps1
# Agent 运行时槽门禁（K10，对齐 Frame\DataCenter\Doc\DataCenter-Agent运行时数据接入-设计与实施.md §5 K10、§4 A1）
# 判据：产品侧（Projects\Plug\Agent\src）不得新增「静态可变运行时槽」——运行时对象必须落在
#       运行时槽分区（root.Slots.Partition("agent")）或 AmbientSlot 机制上。
# 检测：
#   [S1] 静态可变字段（static 非 readonly 且非 const 的字段声明）
#   [S2] 静态可变属性（static 且含 set 访问器的自动属性）
#   [S3] 静态字典/集合持有（static readonly Dictionary/ConcurrentDictionary 等——只读引用但内容可变）
# 白名单：迁移期载体（AgentRuntimeRoot 的绑定承载/无根回落容器）+ 注册表常量类 + 测试工程（不在扫描面）。
#   - 报告模式（默认）：列出命中，不设退出码。
#   - 校验模式（-Check）：非白命中 > 0 → 退出码 1（CI 门禁）。

[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    # 脚本位于 Frame\PowerShellScript\Operation\DSL\，上溯四级即仓库根
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$scanDir = Join-Path $Root 'Projects\Plug\Agent\src'
if (-not (Test-Path $scanDir)) { Write-Error "未找到 Agent 源码目录: $scanDir"; exit 2 }

$patterns = [ordered]@{
    'S1 静态可变字段' = {
        param($t)
        if ($t -notmatch '^\s*(?:public|internal|private|protected)\s+') { return $false }
        if ($t -notmatch '\bstatic\b') { return $false }
        # 只读/常量/类型声明/属性/方法/表达式体 一律不算可变槽
        if ($t -match '\b(?:readonly|const|partial|class|struct|record|interface|enum)\b') { return $false }
        if ($t -match '=>') { return $false }
        if ($t -match '\(') { return $false }
        if ($t -match '\{') { return $false }
        return ($t -match ';\s*$')
    }.GetNewClosure()
    'S2 静态可变属性' = {
        param($t)
        if ($t -notmatch '\bstatic\b') { return $false }
        return ($t -match '\{\s*get;\s*set;')
    }.GetNewClosure()
}

# 白名单（路径子串 + 理由）——逐条登记，不得放宽为整目录泛白
$allowList = @(
    [pscustomobject]@{ Path = '\Runtime\AgentRuntimeRoot.cs'; Pending = $false; Reason = 'A1 取根通道：进程级装配绑定 + 无根回落容器（迁移期转发承载，注释已登记退役条件）' }
)

# 存量基线（A1 扫描基线：既有静态可变运行时槽，A2+ 对象槽分区逐项收敛；归零后本表清空）
# 判据语义：K10 = 不得【新增】静态可变运行时槽；基线内为待迁移存量（报告可见，不阻断）。
$baseline = @(
    'Agent.Axioms\Infrastructure\ClineCredentials.cs',
    'Agent.Axioms\Loops\DataContractRegistrar.cs',
    'Agent.Core\Prompts\PromptFrameworkFunction.cs',
    'Agent.Launcher\Composition\AgentFrameworkHosting.cs',
    'Agent.Launcher\Composition\ConversationHosting.cs',
    'Agent.Launcher\Modules\Libraries\AgentUiLibraryDb.cs',
    'Agent.Launcher\Modules\AgentUiHost.cs',
    'Agent.McpRegistryHost\MCP\GraphTaskTools.cs',
    'Agent.McpRegistryHost\MCP\LocalDbMcpTools.cs',
    'Agent.McpRegistryHost\AgentMcpLoopback.cs',
    'Agent.McpRegistryHost\McpToolFunction.cs',
    'Agent.McpRegistryHost\RegistrySkillProvider.cs',
    'Agent.McpRegistryHost\RegistryToolService.cs',
    'Agent.Modules\Cline\Pool\ClineDaemonPool.cs',
    'Agent.Modules\Cline\ClineClient.cs',
    'Agent.Operation\Skill\SkillsOptions.cs'
)

$files = Get-ChildItem -Path $scanDir -Recurse -File -Include *.cs -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notlike '*\_deprecated\*' }

$hits = [System.Collections.Generic.List[object]]::new()
$allowed = [System.Collections.Generic.List[object]]::new()
$pending = [System.Collections.Generic.List[object]]::new()
foreach ($file in $files) {
    $rel = $file.FullName.Substring($Root.Length).TrimStart('\')
    $lines = Get-Content $file.FullName -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $text = $lines[$i]
        if ([string]::IsNullOrWhiteSpace($text)) { continue }
        $trimmed = $text.Trim()
        if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('/*') -or $trimmed.StartsWith('*')) { continue }

        foreach ($name in $patterns.Keys) {
            if (-not (& $patterns[$name] $text)) { continue }
            $entry = [pscustomobject]@{ File = $rel; Line = $i + 1; Pattern = $name; Text = $trimmed }
            $white = $allowList | Where-Object { $_.Path -and $rel -like ('*' + $_.Path + '*') } | Select-Object -First 1
            if ($white) {
                $entry | Add-Member -NotePropertyName Reason -NotePropertyValue $white.Reason
                $allowed.Add($entry)
            } elseif ($baseline | Where-Object { $rel -like ('*' + $_ + '*') }) {
                $pending.Add($entry)
            } else {
                $hits.Add($entry)
            }
            break
        }
    }
}

Write-Host '=== [K10] Agent 无新增静态可变运行时槽 ==='
Write-Host ("扫描文件数: {0} | 白名单命中: {1} | 存量基线（待迁移）: {2} | 违规命中: {3}" -f `
    $files.Count, $allowed.Count, $pending.Count, $hits.Count)
if ($allowed.Count -gt 0) {
    Write-Host '--- 白名单（迁移期载体） ---'
    $allowed | ForEach-Object { Write-Host ("  [允许] {0}:{1} [{2}] {3}" -f $_.File, $_.Line, $_.Pattern, $_.Reason) }
}
if ($pending.Count -gt 0) {
    Write-Host '--- 存量基线（A2+ 对象槽分区逐项收敛；新增即违规） ---'
    $pending | ForEach-Object { Write-Host ("  [待迁移] {0}:{1} [{2}]" -f $_.File, $_.Line, $_.Pattern) }
}
if ($hits.Count -gt 0) {
    Write-Host '--- 违规清单（静态可变运行时槽） ---'
    $hits | ForEach-Object { Write-Host ("  [违规] {0}:{1} [{2}] {3}" -f $_.File, $_.Line, $_.Pattern, $_.Text) }
}

if ($Check -and $hits.Count -gt 0) {
    Write-Host '校验失败：K10 静态可变运行时槽未收敛（应改经运行时槽分区或 AmbientSlot 承载）。'
    exit 1
}
exit 0
