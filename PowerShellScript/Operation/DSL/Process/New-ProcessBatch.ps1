# New-ProcessBatch.ps1
# 批次授权单生成器（P7）—— 机械归纳，不做授权。人须在生成后复核 allowSet。
#   · 输入：施工单路径 + batchId + 改动集（显式 / -FromCommit / git status）；
#   · 输出：向 <plan>.batches.json 追加/更新批次并置 active；-SetManifest 同步 BatchScope 基线指向。
#   · 判据：Check-BatchScope.ps1 -Check 0 违规 且 Manifest 指向新单。
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Plan,
    [Parameter(Mandatory = $true)][string]$BatchId,
    [string[]]$Changed = @(),
    [string]$ManifestOut = '',
    [string]$FromCommit = '',
    [switch]$SetManifest,
    [switch]$Json
)

$ErrorActionPreference = 'Stop'

# 仓库根（含 Projects\ 与 Frame\）
$repoRoot = $PSScriptRoot
while ($repoRoot -and $repoRoot -ne (Split-Path $repoRoot -Parent)) {
    if ((Test-Path (Join-Path $repoRoot 'Projects')) -and (Test-Path (Join-Path $repoRoot 'Frame'))) { break }
    $repoRoot = Split-Path $repoRoot -Parent
}

function Get-SubmodulePaths {
    $mods = [System.Collections.Generic.List[string]]::new()
    $gitmodules = Join-Path $repoRoot '.gitmodules'
    if (Test-Path -LiteralPath $gitmodules) {
        foreach ($line in [IO.File]::ReadAllLines($gitmodules)) {
            $m = [regex]::Match($line, '^\s*path\s*=\s*(?<p>.+?)\s*$')
            if ($m.Success) { $mods.Add(($m.Groups['p'].Value -replace '/', '\')) }
        }
    }
    return , $mods
}

function Get-ChangedFromGit {
    $list = [System.Collections.Generic.List[string]]::new()
    $raw = @(& git -C $repoRoot -c core.quotepath=false status --porcelain 2>$null)
    foreach ($line in $raw) {
        $s = [string]$line
        if ($s.Length -lt 4) { continue }
        $p = $s.Substring(3).Trim()
        if ($p -match ' -> ') { $p = ($p -split ' -> ')[-1].Trim() }
        $list.Add(($p.Trim('"') -replace '\\', '/'))
    }
    foreach ($sub in (Get-SubmodulePaths)) {
        $subAbs = Join-Path $repoRoot $sub
        if (-not (Test-Path -LiteralPath $subAbs)) { continue }
        $raw = @(& git -C $subAbs -c core.quotepath=false status --porcelain 2>$null)
        foreach ($line in $raw) {
            $s = [string]$line
            if ($s.Length -lt 4) { continue }
            $p = $s.Substring(3).Trim()
            if ($p -match ' -> ') { $p = ($p -split ' -> ')[-1].Trim() }
            $list.Add((($sub -replace '\\', '/') + '/' + ($p.Trim('"') -replace '\\', '/')))
        }
    }
    return , $list
}

function Get-ChangedFromCommit([string]$Commit) {
    $out = [System.Collections.Generic.List[string]]::new()
    foreach ($line in @(& git -C $repoRoot show --name-only --pretty=format: $Commit 2>$null)) {
        $p = ([string]$line).Trim()
        if ($p) { $out.Add(($p -replace '\\', '/')) }
    }
    return , $out
}

function Get-AllowGlob([string]$Path) {
    $seg = @($Path -split '/')
    if ($seg.Count -ge 5 -and $seg[0] -eq 'Docs' -and $seg[1] -eq 'analysis') {
        return (($seg[0..4]) -join '/') + '/**'
    }
    if ($seg.Count -ge 2) { return (($seg[0..1]) -join '/') + '/**' }
    return $seg[0] + '/**'
}

# ---- 改动集 ----
# CLI 归一：`pwsh -File` 不拆数组/逗号（且可能带引号残留）⇒ 统一去引号 + 按逗号/空白切分。
$Changed = @($Changed |
    ForEach-Object { ([string]$_).Trim().Trim("'").Trim('"') } |
    ForEach-Object { $_ -split '[,\s]+' } |
    Where-Object { $_ })
if ($Changed.Count -gt 0) {
    $changes = @($Changed | ForEach-Object { ([string]$_ -replace '\\', '/').TrimStart('/') } | Where-Object { $_ } | Sort-Object -Unique)
} elseif ($FromCommit) {
    $changes = @(Get-ChangedFromCommit -Commit $FromCommit | Sort-Object -Unique)
} else {
    $changes = @(Get-ChangedFromGit | Sort-Object -Unique)
}
if ($changes.Count -eq 0) {
    Write-Error '改动集为空（-Changed / -FromCommit / git status 均无内容）⇒ 拒绝生成空授权单'
    exit 2
}

$allowSet = @($changes | ForEach-Object { Get-AllowGlob $_ } | Sort-Object -Unique)
if ($allowSet.Count -lt 3) {
    # 正对照下限（Check-BatchScope B3）：面过窄即补顶层目录，人须复核
    $allowSet = @($allowSet + @($changes | ForEach-Object { (@($_ -split '/')[0]) + '/**' }) | Sort-Object -Unique)
}

$planPath = if ([IO.Path]::IsPathRooted($Plan)) { $Plan } else { Join-Path $repoRoot $Plan }
if (-not (Test-Path -LiteralPath $planPath)) { Write-Error "施工单不存在：$planPath"; exit 2 }
$planRel = [IO.Path]::GetRelativePath($repoRoot, $planPath) -replace '\\', '/'
if (-not $ManifestOut) { $ManifestOut = [IO.Path]::ChangeExtension($planPath, '.batches.json') }
if (-not [IO.Path]::IsPathRooted($ManifestOut)) { $ManifestOut = Join-Path $repoRoot $ManifestOut }

$manifest = $null
if (Test-Path -LiteralPath $ManifestOut) {
    $manifest = Get-Content -LiteralPath $ManifestOut -Raw -Encoding UTF8 | ConvertFrom-Json
}
if ($null -eq $manifest) {
    $manifest = [pscustomobject]@{
        schema      = 'process-batch-manifest-v1'
        plan        = $planRel
        active      = @($BatchId)
        preexisting = @()
        derived     = @('**/BuildOutput/**', 'Data/**')
        batches     = @()
    }
}

$batches = @($manifest.batches)
$existing = $batches | Where-Object { [string]$_.batchId -eq $BatchId } | Select-Object -First 1
if ($existing) {
    $existing.status = 'active'
    $existing.allowSet = @($allowSet)
    $existing.verifyCmd = 'pwsh Frame\Invoke\Invoke-Frame.ps1 -Action invoke-verify -Changed <本批改动文件>'
} else {
    $batches = @($batches) + @([pscustomobject]@{
        batchId   = $BatchId
        title     = "自动归纳批次 $BatchId"
        status    = 'active'
        note      = "生成器机械归纳（改动 $($changes.Count) 件）；allowSet 须人工复核（生成器不做授权）。"
        allowSet  = @($allowSet)
        denySet   = @()
        verifyCmd = 'pwsh Frame\Invoke\Invoke-Frame.ps1 -Action invoke-verify -Changed <本批改动文件>'
    })
}

$manifest.active = @($BatchId)
$manifest.plan = $planRel
$manifest.batches = @($batches)
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $ManifestOut -Encoding UTF8

# ---- 同步 BatchScope 基线（-SetManifest）----
$baselineUpdated = $false
if ($SetManifest) {
    $baselinePath = Join-Path $repoRoot 'Frame\PowerShellScript\Baselines\Process\BatchScope.psd1'
    if (-not (Test-Path -LiteralPath $baselinePath)) { Write-Error "BatchScope 基线缺失：$baselinePath"; exit 2 }
    $rel = ([IO.Path]::GetRelativePath($repoRoot, $ManifestOut)) -replace '/', '\'
    $text = Get-Content -LiteralPath $baselinePath -Raw
    $pattern = "(?m)^(\s*Manifest\s*=\s*@\{\s*Value\s*=\s*')[^']*(')"
    if ($text -notmatch $pattern) { Write-Error 'BatchScope.psd1 未找到 Manifest 行（结构漂移）'; exit 2 }
    $text = [regex]::Replace($text, $pattern, { param($m) $m.Groups[1].Value + $rel + $m.Groups[2].Value }, 1)
    Set-Content -LiteralPath $baselinePath -Value $text -Encoding UTF8 -NoNewline
    $baselineUpdated = $true
}

$result = [ordered]@{
    schema          = 'process-batch-generated-v1'
    batchId         = $BatchId
    manifest        = ([IO.Path]::GetRelativePath($repoRoot, $ManifestOut) -replace '\\', '/')
    changedCount    = $changes.Count
    allowSet        = @($allowSet)
    baselineUpdated = $baselineUpdated
}
if ($Json) { $result | ConvertTo-Json -Depth 6 }
else {
    Write-Host "[BATCH] $BatchId → $($result.manifest)（改动 $($changes.Count) 件 / allowSet $($allowSet.Count) 条）"
    foreach ($a in $allowSet) { Write-Host "  - $a" -ForegroundColor DarkGray }
    if ($baselineUpdated) { Write-Host '[BATCH] BatchScope.Manifest 已切换；请复核 allowSet 后跑 Check-BatchScope -Check' -ForegroundColor Yellow }
}
exit 0

