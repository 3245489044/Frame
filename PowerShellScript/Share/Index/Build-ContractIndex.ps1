#requires -Version 5.1
<#
.SYNOPSIS
    契约索引生成器（ContractIndex）—— 「改一处，哪些落点会跟着变」由产物回答，替代人工枚举。

.DESCRIPTION
    背景（实测 2026-09-29）：同一跨层事实（键族前缀 / 词表 / 坐标）在本仓最多有 6 处可执行表示，
    横跨 3 个 git 仓（Main 超项目 / Frame / Projects 子模块）；检索面**不跨子模块**
    ⇒ 超仓 grep 对子模块内容恒假阴性（「0 命中 ≠ 不存在」）⇒ 变更前只能人工枚举 10–30 个文件。
    本件把枚举变成**产物**：每条事实 → 全部落点 + 角色分类（判据面/断言面/声明面/代码面/文档面）。

    子模块安全：**逐仓** `git -C <repo> grep`（禁超仓递归 grep，超仓 grep 不进 gitlink）。
    确定性：facts 按 Id 升序、hits 按 (repo,file,line) 升序；易变字段仅 builtAt（漂移比对时剔除）。

    模式：
      （默认）     报告：打印逐事实落点摘要，不写文件、不设退出码。
      -Update      写产物（-OutFile 可指到临时路径 ⇒ 供漂移守卫复算）。
      -Check       漂移校验：复算并与磁盘产物比对 ⇒ 不一致/缺失 → 退出码 1（fail-closed）。

.PARAMETER Root
    仓库根目录（默认由本脚本位置上溯四级推导）。

.PARAMETER OutFile
    产物路径（默认 Frame\CsharpPrj\generated\ContractIndex.json）。

.PARAMETER Update
    写产物。

.PARAMETER Check
    漂移校验（与 -Update 互斥）。

.PARAMETER Show
    打印逐事实落点明细。

.PARAMETER MaxHitsPerFact
    每条事实落点上限（默认 400；超出记 truncated=true，保证确定性与耗时上限）。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [string]$OutFile = '',
    [switch]$Update,
    [switch]$Check,
    [switch]$Show,
    [int]$MaxHitsPerFact = 400
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}
if ([string]::IsNullOrWhiteSpace($OutFile))
{
    $OutFile = Join-Path $Root 'Frame\CsharpPrj\generated\ContractIndex.json'
}
if ($Update -and $Check)
{
    throw '参数互斥：-Update 与 -Check 不可同用'
}

# ---------------- 事实登记（本表 = 唯一手写源；其余全部为产物） ----------------
$facts = @(
    @{ Id = 'key.proc';        Kind = 'KeyFamily'; Pattern = 'proc.';        Note = '过程域键族前缀' }
    @{ Id = 'key.session';     Kind = 'KeyFamily'; Pattern = 'session.';     Note = '会话消息键族前缀' }
    @{ Id = 'key.checkpoint';  Kind = 'KeyFamily'; Pattern = 'checkpoint.';  Note = 'checkpoint 键族前缀' }
    @{ Id = 'key.keymapping';  Kind = 'KeyFamily'; Pattern = 'keymapping.';  Note = 'KeyMapping 键族前缀' }
    @{ Id = 'key.relations';   Kind = 'KeyFamily'; Pattern = 'relations.';   Note = '关系键族前缀' }
    @{ Id = 'key.factid';      Kind = 'KeyFamily'; Pattern = '#R';           Note = '事实 id 前缀' }
    @{ Id = 'loc.workflow';    Kind = 'LocValue';  Pattern = 'agent_round';  Note = 'loc 受控词（单值态/下划线形）' }
    @{ Id = 'placement.process'; Kind = 'Placement'; Pattern = 'process-node'; Note = '过程域位置 id' }
    @{ Id = 'kind.toolcall';   Kind = 'Vocabulary'; Pattern = 'tool-call';   Note = '识别词表条目（过程类别）' }
)
# 口径：本表刻意只收**低噪声字面量**（键族前缀 + 词表条目 + 坐标）。
#   高频前缀（config. / history. / seg.）暂不入表：它们会命中大量「config.json / history.json 文件名」噪声，
#   待 SR-4 的「引号字面量」匹配器落地后再纳入（宁少勿噪：索引用于定位，不用于计数）。

# 仓库面：逐仓调用（超项目含 Environment/Docs/.clinerules；子模块须独立 -C 调用）
$repos = @(
    @{ Name = 'Frame';    Path = (Join-Path $Root 'Frame') }
    @{ Name = 'Projects'; Path = (Join-Path $Root 'Projects') }
    @{ Name = 'Main';     Path = $Root }
)
$pathSpecs = @('*.cs', '*.json', '*.jsonc', '*.ps1', '*.psm1', '*.ushar', '*.md', '*.ts')

# ---------------- 角色分类（落点用途，供"改这处会连带改哪些面"判断） ----------------
function Get-Role
{
    param([Parameter(Mandatory = $true)][string]$File)

    if ($File -match '(^|/)Check-[^/]*\.ps1$' -or $File -match 'GateManifests\.psd1$') { return 'Gate' }
    if ($File -match '(^|/)CentralTest/') { return 'Test' }
    if ($File -match '\.(json|jsonc|ushar|psd1)$' -or $File -match '(^|/)Config/') { return 'Declaration' }
    if ($File -match '\.(cs|ts|js)$') { return 'Code' }
    if ($File -match '\.md$') { return 'Doc' }
    return 'Other'
}

function Invoke-RepoGrep
{
    param(
        [Parameter(Mandatory = $true)][string]$RepoPath,
        [Parameter(Mandatory = $true)][string[]]$Patterns,
        [Parameter(Mandatory = $true)][string[]]$PathSpecs
    )

    if (-not (Test-Path -LiteralPath $RepoPath)) { return @() }

    # 参数序（关键）：模式选项（-e ×n）必须在 `--` **之前**；`--` 之后一律按 pathspec 解析。
    # （曾把 `--` 放在 -e 之前 ⇒ git 把 `proc.` 当 pathspec ⇒ fatal: ambiguous argument。）
    $gitArgs = @('-C', $RepoPath, 'grep', '-n', '-I', '-F')
    foreach ($p in $Patterns) { $gitArgs += @('-e', $p) }
    $gitArgs += @('--')
    $gitArgs += $PathSpecs
    # 自引用排除（确定性）：产物自身含模式串（pattern/note 字段）且 tracked ⇒ 若被扫到，
    # 每次 -Update 改写自身内容即改变后续 grep 的行号/命中数 ⇒ 产物永不收敛（-Update 后 -Check 亦失败）。
    $gitArgs += ':(exclude)**/ContractIndex.json'

    # git grep **无命中时退出码为 1**（非错误）⇒ 本地降级 EAP，仅按「fatal:/error: 文本行」判真错。
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try
    {
        $raw = @(& git @gitArgs 2>&1)
    }
    finally
    {
        $ErrorActionPreference = $prevEap
    }
    foreach ($line in $raw)
    {
        $text = $line.ToString()
        if ($text -like 'fatal:*' -or $text -like 'error:*') { throw "git grep 失败（$RepoPath）：$text" }
    }
    return @($raw | ForEach-Object { $_.ToString() })
}

# ---------------- 采集 ----------------
$patterns = @($facts | ForEach-Object { $_.Pattern })
$hitsByFact = @{}
foreach ($f in $facts) { $hitsByFact[$f.Id] = [System.Collections.Generic.List[object]]::new() }

$repoStats = [System.Collections.Generic.List[object]]::new()
foreach ($repo in $repos)
{
    $lines = @(Invoke-RepoGrep -RepoPath $repo.Path -Patterns $patterns -PathSpecs $pathSpecs)
    $repoStats.Add([pscustomobject]@{ repo = $repo.Name; lines = $lines.Count })

    foreach ($line in $lines)
    {
        $parts = $line -split ':', 3
        if ($parts.Count -lt 3) { continue }
        $file = $parts[0]
        $lineNo = 0
        if (-not [int]::TryParse($parts[1], [ref]$lineNo)) { continue }
        $text = $parts[2].Trim()
        if ($text.Length -gt 200) { $text = $text.Substring(0, 200) }
        $role = Get-Role -File $file

        foreach ($fact in $facts)
        {
            if ($text.Contains($fact.Pattern))
            {
                $hitsByFact[$fact.Id].Add([pscustomobject]@{
                    repo = $repo.Name; file = $file; line = $lineNo; role = $role; text = $text
                })
            }
        }
    }
}

# ---------------- 装配（确定性：排序 + 上限） ----------------
$roles = @('Gate', 'Test', 'Declaration', 'Code', 'Doc', 'Other')
$factNodes = [System.Collections.Generic.List[object]]::new()
$grandByRole = [ordered]@{}
foreach ($r in $roles) { $grandByRole[$r] = 0 }
$truncated = [System.Collections.Generic.List[string]]::new()

foreach ($fact in ($facts | Sort-Object Id))
{
    $hits = @($hitsByFact[$fact.Id] | Sort-Object repo, file, line)
    $isTruncated = $hits.Count -gt $MaxHitsPerFact
    if ($isTruncated)
    {
        $truncated.Add($fact.Id)
        $hits = @($hits | Select-Object -First $MaxHitsPerFact)
    }

    $byRole = [ordered]@{}
    foreach ($r in $roles) { $byRole[$r] = 0 }
    foreach ($h in $hits) { $byRole[$h.role] = $byRole[$h.role] + 1; $grandByRole[$h.role] = $grandByRole[$h.role] + 1 }

    $factNodes.Add([pscustomobject]@{
        id = $fact.Id
        kind = $fact.Kind
        pattern = $fact.Pattern
        note = $fact.Note
        total = $hits.Count
        truncated = [bool]$isTruncated
        byRole = $byRole
        hits = @($hits)
    })
}

$index = [pscustomobject]@{
    schema = 'contract-index-v1'
    sentinel = '此文件由 Frame\PowerShellScript\Share\Index\Build-ContractIndex.ps1 生成，禁止手工编辑'
    builtBy = 'Build-ContractIndex.ps1'
    builtAt = [DateTime]::UtcNow.ToString('o')
    repoAnchor = '.git'
    submoduleSafe = 'per-repo git grep（超仓 grep 不进 gitlink）'
    scan = [pscustomobject]@{ repos = @($repos | ForEach-Object { $_.Name }); pathSpecs = $pathSpecs; maxHitsPerFact = $MaxHitsPerFact }
    facts = @($factNodes)
    summary = [pscustomobject]@{
        factCount = $facts.Count
        hitCount = @($factNodes | Measure-Object -Property total -Sum).Sum
        byRole = $grandByRole
        truncatedFacts = @($truncated)
        repoLines = @($repoStats)
    }
}

# ---------------- 输出 ----------------
$json = $index | ConvertTo-Json -Depth 10

function Get-ComparableJson
{
    param([Parameter(Mandatory = $true)]$Node)
    $text = if ($Node -is [string]) { $Node } else { $Node | ConvertTo-Json -Depth 10 }
    $obj = $text | ConvertFrom-Json
    if ($null -ne $obj.PSObject.Properties['builtAt']) { $obj.PSObject.Properties.Remove('builtAt') }
    return ($obj | ConvertTo-Json -Depth 10)
}

if ($Update)
{
    $dir = Split-Path $OutFile -Parent
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($OutFile, $json, $utf8NoBom)
    Write-Host "[OK] 已生成 ContractIndex：$OutFile" -ForegroundColor Green
    Write-Host "  事实 $($index.summary.factCount) 条 / 落点合计 $($index.summary.hitCount)" -ForegroundColor Gray
    exit 0
}

if ($Check)
{
    if (-not (Test-Path -LiteralPath $OutFile))
    {
        Write-Host "[FAIL] ContractIndex 产物缺失：$OutFile" -ForegroundColor Yellow
        Write-Host "  修复命令：pwsh Frame\PowerShellScript\Share\Index\Build-ContractIndex.ps1 -Update" -ForegroundColor Gray
        exit 1
    }

    $diskText = Get-Content -LiteralPath $OutFile -Raw -Encoding UTF8
    $actual = Get-ComparableJson -Node $diskText
    $expected = Get-ComparableJson -Node $json

    if ($actual -eq $expected)
    {
        Write-Host ""
        Write-Host "[OK] ContractIndex 与复算结果一致（0 漂移）" -ForegroundColor Green
        exit 0
    }

    Write-Host "[FAIL] ContractIndex 漂移：产物 ≠ 复算结果（$OutFile）" -ForegroundColor Yellow
    Write-Host "  修复命令：pwsh Frame\PowerShellScript\Share\Index\Build-ContractIndex.ps1 -Update" -ForegroundColor Gray
    $aLines = @($actual -split "`r?`n")
    $eLines = @($expected -split "`r?`n")
    $shown = 0
    for ($i = 0; $i -lt [Math]::Max($aLines.Count, $eLines.Count) -and $shown -lt 5; $i++)
    {
        $av = if ($i -lt $aLines.Count) { $aLines[$i] } else { '<缺行>' }
        $ev = if ($i -lt $eLines.Count) { $eLines[$i] } else { '<缺行>' }
        if ($av -ne $ev)
        {
            Write-Host ("    [diff] 行 {0}" -f ($i + 1)) -ForegroundColor DarkYellow
            Write-Host ("      产物: {0}" -f $av.Trim()) -ForegroundColor DarkYellow
            Write-Host ("      复算: {0}" -f $ev.Trim()) -ForegroundColor DarkYellow
            $shown++
        }
    }
    exit 1
}

# ---------------- 报告（默认） ----------------
Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  契约索引（ContractIndex）—— 事实 → 全部落点" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ("  事实 {0} 条 / 落点合计 {1}" -f $index.summary.factCount, $index.summary.hitCount) -ForegroundColor Gray
Write-Host ""

foreach ($f in $index.facts)
{
    Write-Host ("  {0,-18} {1,-11} 落点 {2,4}  | 判据 {3} / 断言 {4} / 声明 {5} / 代码 {6} / 文档 {7}" -f `
        $f.id, $f.kind, $f.total, $f.byRole.Gate, $f.byRole.Test, $f.byRole.Declaration, $f.byRole.Code, $f.byRole.Doc)
    if ($Show)
    {
        foreach ($h in ($f.hits | Select-Object -First 30))
        {
            Write-Host ("      [{0}] {1}:{2} {3}" -f $h.role, $h.file, $h.line, $h.text) -ForegroundColor DarkGray
        }
    }
}
if ($index.summary.truncatedFacts.Count -gt 0)
{
    Write-Host ("  [截断] " + ($index.summary.truncatedFacts -join ', ') + "（提高 -MaxHitsPerFact 复查）") -ForegroundColor DarkYellow
}
Write-Host ""
Write-Host "  更新/校验：pwsh Frame\PowerShellScript\Share\Index\Build-ContractIndex.ps1 -Update|-Check" -ForegroundColor Gray
exit 0


