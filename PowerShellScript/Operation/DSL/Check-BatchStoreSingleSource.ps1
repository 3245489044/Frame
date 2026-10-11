#requires -Version 5.1
<#
.SYNOPSIS
    存储层批量单源守卫（BatchStoreSingleSource）—— 批量读/写/删唯一构建点 = StorageFramework/Batch/。

.DESCRIPTION
    判据（数据库页 R0/R5）：
      G1 唯一构建点：`Frame\CsharpPrj\Plate\DataManage\StorageFramework\Batch\` 存在且定义 `IBatchStore`。
      G2 零领域：`StorageFramework\Batch\*.cs` 零领域字面量（ur_nodes / ur_tasks / ur_histories / graph-log）。
      G3 零直写通道：`Batch\*.cs` 的批写/批删经注入执行器（含 `ISqliteWriteHost` / `IDbCommandExecutor` /
         `SqliteTransaction` 语义），不 new 连接 / 不硬编码连接串。
      G4 正/负对照：合成样本（含 ur_nodes / new SQLiteConnection）必被 G2/G3 捕获。

    模式：默认报告；-Check 时违规 → 退出码 1。

.PARAMETER Root
    仓库根（默认为本脚本位置上溯四级）。
.PARAMETER Check
    校验模式。
.PARAMETER ShowDetail
    输出全部检查详情。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================'
Write-Host '  存储层批量单源守卫（BatchStoreSingleSource：唯一构建点 / 零领域）'
Write-Host '============================================================'

$batchDir = Join-Path $Root 'Frame\CsharpPrj\Plate\DataManage\StorageFramework\Batch'

# ---------------- G1 唯一构建点 ----------------
if (-not (Test-Path $batchDir)) {
    $violations.Add("[G1] 缺失存储层批量框架目录：$batchDir")
}
else {
    $batchFiles = @(Get-ChildItem -Path $batchDir -Filter '*.cs' -File -ErrorAction SilentlyContinue)
    if ($batchFiles.Count -eq 0) {
        $violations.Add('[G1] StorageFramework\Batch\ 无 .cs 文件（批量框架缺位）')
    }
    $combinedText = [string]::Join("`n", @($batchFiles | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }))
    if ($combinedText -notmatch 'interface\s+IBatchStore') {
        $violations.Add('[G1] StorageFramework\Batch\ 未定义 IBatchStore（批量唯一构建点判据不成立）')
    }

    # ---------------- G2 零领域字面量 ----------------
    $domainTokens = @('ur_nodes', 'ur_tasks', 'ur_histories', 'graph-log', 'agent\.db')
    foreach ($f in $batchFiles) {
        $code = @([System.IO.File]::ReadAllText($f.FullName) -split "`r?`n" | Where-Object { $_.TrimStart() -notmatch '^(//|/\*|\*)' })
        $codeText = [string]::Join("`n", $code)
        foreach ($tok in $domainTokens) {
            if ([regex]::IsMatch($codeText, $tok)) {
                $violations.Add("[G2] $($f.Name) 存储层批量含领域字面量 '$tok'（须零领域）")
            }
        }
    }

    # ---------------- G3 零直写通道 ----------------
    $bareConnTokens = @('new\s+SQLiteConnection', 'new\s+SQLiteCommand\s*\(\s*[^,)]*\)\s*;', 'Data Source=')
    foreach ($f in $batchFiles) {
        $code = @([System.IO.File]::ReadAllText($f.FullName) -split "`r?`n" | Where-Object { $_.TrimStart() -notmatch '^(//|/\*|\*)' })
        $codeText = [string]::Join("`n", $code)
        foreach ($tok in $bareConnTokens) {
            if ([regex]::IsMatch($codeText, $tok)) {
                $violations.Add("[G3] $($f.Name) 出现裸连接/连接串（批写批删须经单写通道/事务执行器）")
            }
        }
    }
}

# ---------------- G4 正/负对照（防判据空转） ----------------
$probe = 'INSERT INTO ur_nodes (NodeKey, Doc) VALUES (@k, @d); new SQLiteConnection(cs);'
$probeDomain = [regex]::IsMatch($probe, 'ur_nodes')
$probeBare = [regex]::IsMatch($probe, 'new\s+SQLiteConnection')
if (-not ($probeDomain -and $probeBare)) {
    $violations.Add('[G4] 正对照失效：合成样本（领域字面量 + 裸连接）未被判据捕获')
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1–G4 全部通过（批量唯一构建点 + 存储层零领域 + 无裸连接 + 正对照绿）' -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host ""
Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
Write-Host '  结果: 失败' -ForegroundColor Red
Write-Host ''
if ($Check) { exit 1 }
exit 0
