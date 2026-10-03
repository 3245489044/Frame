# Check-AnalysisDocGrounding.ps1
# 分析文档「根因」对账门禁（G5 · R5）—— 只对**显式 opt-in**（marker）的文档生效，
# 已存在的大量历史分析文档不在判据面内（避免一次性全量红）。新写作须显式 opt-in 并守法。
#
# opt-in marker（任一声明即纳入判据面）：
#   - 正文含 HTML 注释 `<!-- grounding: tri-state -->`
#   - 或 `---meta` 块 / 行内元数据含 `grounding = "tri-state"` / `grounding:tri-state`
#
# 判据：
#   ① 三态标注（治「把建议机制写成已知根因」）：含 opt-in marker 的文档，其每个「根因」章节内，
#      内容行（bullet 行 / 表格数据行）须各带〔事实|建议|待核实〕之一；未标注即违规。
#   ② 路径对账（治「文档-代码不一致」）：文档内全部反引号中的**仓库相对路径** token
#      （起点 ∈ {Frame, Environment, Projects, Docs, .clinerules}）须在磁盘存在；不存在即违规。
#
# 模式：-Check 违规 exit 1；默认报告模式（不设退出码）。
# 依赖方向（硬约束）：本件只读仓库；零侧写。

param(
    [switch]$Check,
    [string]$DocsRoot = '',
    [string]$RepoRoot = ''
)

$ErrorActionPreference = 'Stop'

# 定位仓库根（含 Projects\ 与 Frame\）—— 相对 $PSScriptRoot 上溯，禁硬编码绝对路径
if (-not $RepoRoot) {
    $probe = $PSScriptRoot
    while ($probe -and $probe -ne (Split-Path $probe -Parent)) {
        if ((Test-Path (Join-Path $probe 'Projects')) -and (Test-Path (Join-Path $probe 'Frame'))) { break }
        $probe = Split-Path $probe -Parent
    }
    $RepoRoot = $probe
}
if (-not $DocsRoot) { $DocsRoot = Join-Path $RepoRoot 'Docs\analysis' }

$violations = [System.Collections.Generic.List[string]]::new()
$optedIn = New-Object System.Collections.Generic.List[string]

$stateTokens = @('事实', '建议', '待核实')
$pathRoots = @('Frame', 'Environment', 'Projects', 'Docs', '.clinerules')
$pathExts = @('.ps1', '.psm1', '.psd1', '.cs', '.md', '.json', '.jsonc', '.targets', '.props', '.csproj', '.specs', '.ushar', '.sln', '.slnx')

if (-not (Test-Path -LiteralPath $DocsRoot)) {
    Write-Host "=== 分析文档根因对账门禁 ==="
    Write-Host "  [OK] 文档根不存在，跳过：$DocsRoot"
    exit 0
}

$docs = @(Get-ChildItem -Path $DocsRoot -Recurse -Filter '*.md' -File -ErrorAction SilentlyContinue)

foreach ($doc in $docs) {
    $raw = Get-Content -LiteralPath $doc.FullName -Raw
    if ($raw -notmatch '<!--\s*grounding\s*:\s*tri-state\s*-->' -and $raw -notmatch 'grounding\s*[:=]\s*"tri-state"') { continue }
    $optedIn.Add($doc.FullName)
    $lines = Get-Content -LiteralPath $doc.FullName
    $rel = [IO.Path]::GetRelativePath($RepoRoot, $doc.FullName)

    # ---- 判据 ①：含 marker 的文档，「根因」章节的内容行须三态标注 ----
    $inRootCauseSection = $false
    $headingLevel = 0
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $trimmed = $line.Trim()

        # 标题：判「根因」章节的开合（同级/更高级标题关闭）
        $hm = [regex]::Match($line, '^(#+)\s*(.+)$')
        if ($hm.Success) {
            $level = $hm.Groups[1].Value.Length
            $title = $hm.Groups[2].Value
            if ($title -match '根因') { $inRootCauseSection = $true; $headingLevel = $level; continue }
            if ($inRootCauseSection -and $level -le $headingLevel) { $inRootCauseSection = $false }
            continue
        }
        if (-not $inRootCauseSection) { continue }
        if ([string]::IsNullOrWhiteSpace($trimmed)) { continue }

        # 表格分隔行 / 表头行：跳过
        if ($trimmed -match '^\|[\s:|-]+\|$') { continue }

        $isBullet = $trimmed -match '^[-*]\s+\S'
        $isTableRow = $trimmed -match '^\|'
        if (-not ($isBullet -or $isTableRow)) { continue }

        # 表头行（含「态」/「证据」列名，或无三态但为表头）跳过：以「是否含三态列名」判
        if ($isTableRow -and $trimmed -match '\|\s*(态|证据态|标注|grounding)\s*\|') { continue }

        $tagged = $false
        foreach ($t in $stateTokens) { if ($line.Contains($t)) { $tagged = $true; break } }
        if (-not $tagged) {
            $violations.Add("[${rel}:$($i+1)] 根因章节内容行缺三态标注（须含 事实/建议/待核实 之一）：$trimmed")
        }
    }

    # ---- 判据 ②：反引号内仓库相对路径 token 须存在 ----
    foreach ($m in [regex]::Matches($raw, '`([^`\r\n]+)`')) {
        $tok = $m.Groups[1].Value.Trim()
        if ($tok -match '[*<>]') { continue }                       # 通配/占位 token 跳过
        $seg = ($tok -split '[\\/]')[0]
        if ($pathRoots -notcontains $seg) { continue }              # 仅核仓库起点路径
        if (-not ($pathExts | Where-Object { $tok.EndsWith($_) })) { continue }
        $full = Join-Path $RepoRoot ($tok -replace '/', '\')
        if (-not (Test-Path -LiteralPath $full)) {
            $violations.Add("[${rel}] 路径对账失败（不存在）：$tok")
        }
    }
}

# ---- 报告 ----
Write-Host "=== 分析文档根因对账门禁（G5） ==="
Write-Host ("opt-in 文档数: {0}（marker: grounding:tri-state）" -f $optedIn.Count)
Write-Host ("违规数: {0}" -f $violations.Count)

if ($violations.Count -gt 0) {
    Write-Host "--- 违规清单 ---"
    foreach ($v in $violations) { Write-Host "  [FAIL] $v" }
    if ($Check) {
        Write-Host "校验失败：分析文档根因对账发现 $($violations.Count) 项违规"
        exit 1
    }
    exit 0
}

Write-Host "  [OK] 分析文档根因对账通过"
exit 0
