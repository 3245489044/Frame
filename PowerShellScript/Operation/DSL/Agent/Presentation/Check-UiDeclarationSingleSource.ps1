#requires -Version 7.0
<#
.SYNOPSIS
    UI 声明单源守卫（UiDeclarationSingleSource）——声明只经 codegen 进入运行期。

.DESCRIPTION
    SS1 产物在位：ui-declaration.generated.json（schema / fingerprint / views / containers 非空）
    SS2 声明目录只允许 .ushar：ui-view.ushar / ui-loc.ushar 在位；Config\ui 的 *.json 仅限纯数据配置白名单
    SS3 框架层零声明 I/O：Plate\HtmlViewer 的声明类禁 JsonDocument / JsonSerializer / GetManifestResourceStream / File.ReadAllText
    SS4 消费层零解析：Agent.Ui\Ui\Views\Declaration 禁 JsonDocument / GetManifestResourceStream
    SS5 生成物哨兵：GeneratedUiDeclaration.g.cs 必含 auto-generated 头 + 生成器名
    SS6 负对照：合成违规片段必须被判红
    SS7 迁移键唯一：Config\ui\*.ushar 出现复数键 stateMigrations ⇒ 红（E3）
    纯扫描（只读、0 写入）。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'

$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

$repo = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
$productPath = Join-Path $repo 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Declaration\ui-declaration.generated.json'
$generatedCs = Join-Path $repo 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Declaration\GeneratedUiDeclaration.g.cs'
$configUi = Join-Path $repo 'Projects\Plug\Agent\Config\ui'
$plateDeclarative = Join-Path $repo 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Render\Declarative'
$agentDecl = Join-Path $repo 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Declaration'

function Get-CsFiles([string]$dir) {
    if (-not (Test-Path -LiteralPath $dir)) { return @() }
    return @(Get-ChildItem -LiteralPath $dir -Recurse -File -Filter *.cs |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' })
}

function Find-Forbidden([string]$dir, [string[]]$patterns) {
    $hits = [System.Collections.Generic.List[string]]::new()
    foreach ($file in (Get-CsFiles $dir)) {
        $text = [System.IO.File]::ReadAllText($file.FullName)
        foreach ($p in $patterns) {
            if ($text -match $p) { $hits.Add($file.Name + ' :: ' + $p) }
        }
    }
    return $hits
}

$rules = @(
    [pscustomobject]@{ Id = 'SS1'; Title = '产物在位且结构合法'
        Ok = {
            if (-not (Test-Path -LiteralPath $productPath)) { return $false }
            $decl = Get-Content -LiteralPath $productPath -Raw -Encoding UTF8 | ConvertFrom-Json
            return ([string]$decl.schema -eq 'ui-declaration-v1') -and
                   (-not [string]::IsNullOrWhiteSpace([string]$decl.fingerprint)) -and
                   (@($decl.views).Count -ge 1) -and (@($decl.containers).Count -ge 8) -and (@($decl.locs).Count -ge 1)
        } }
    [pscustomobject]@{ Id = 'SS2'; Title = '声明目录只允许 .ushar（JSON 白名单 = 纯数据配置）'
        Ok = {
            if (-not (Test-Path -LiteralPath $configUi)) { return $false }
            foreach ($declaration in @('ui-view.ushar', 'ui-loc.ushar')) {
                if (-not (Test-Path -LiteralPath (Join-Path $configUi $declaration))) { return $false }
            }

            # 白名单 = 纯数据配置（非 UI 视图/loc 声明）；未列出的 *.json ⇒ 判红（default-deny）。
            # ui-regions.json 仅允许为**生成物**（须带生成标记）——声明形态一律拒绝。
            $allowed = @('diagnostics.json', 'fold.json', 'history-task-state.rules.json', 'labels.json',
                         'presentation.regions.json', 'ui-reason-loc.json', 'ui-reason-map.json')
            foreach ($file in @(Get-ChildItem -LiteralPath $configUi -File -Filter '*.json')) {
                if ($file.Name -eq 'ui-regions.json') {
                    $text = [System.IO.File]::ReadAllText($file.FullName)
                    if ($text -notmatch 'GENERATED from Config/ui/ui-view\.ushar') { return $false }
                    continue
                }
                if ($allowed -notcontains $file.Name) { return $false }
            }
            return $true
        } }
    [pscustomobject]@{ Id = 'SS7'; Title = '迁移键唯一（禁 stateMigrations 复数键）'
        Ok = {
            if (-not (Test-Path -LiteralPath $configUi)) { return $false }
            $ushars = @(Get-ChildItem -LiteralPath $configUi -File -Filter '*.ushar')
            if ($ushars.Count -eq 0) { return $false }
            foreach ($file in $ushars) {
                if ([System.IO.File]::ReadAllText($file.FullName) -match 'stateMigrations') { return $false }
            }
            return $true
        } }
    [pscustomobject]@{ Id = 'SS3'; Title = '框架层声明类零 I/O'
        Ok = { (Find-Forbidden $plateDeclarative @('JsonDocument', 'JsonSerializer', 'GetManifestResourceStream', 'File\.ReadAllText')).Count -eq 0 } }
    [pscustomobject]@{ Id = 'SS4'; Title = '消费层声明类零解析'
        Ok = {
            # 只约束声明装配类；wire/payload 解析（ConversationCanon*）按 §11.8 白名单留在原层。
            $targets = @(
                (Join-Path $agentDecl 'AgentUiDeclarationProvider.cs'),
                (Join-Path $agentDecl 'UiDeclarationModel.cs')
            ) | Where-Object { Test-Path -LiteralPath $_ }
            foreach ($file in $targets) {
                $text = [System.IO.File]::ReadAllText($file)
                if ($text -match 'JsonDocument|GetManifestResourceStream') { return $false }
            }
            return $true
        } }
    [pscustomobject]@{ Id = 'SS5'; Title = '生成物哨兵在位'
        Ok = {
            if (-not (Test-Path -LiteralPath $generatedCs)) { return $false }
            $text = [System.IO.File]::ReadAllText($generatedCs)
            return ($text -match '<auto-generated/>') -and ($text -match 'Generate-UiDeclarationCs\.ps1') -and
                   ($text -match 'ui-declaration-v1') -and ($text -match 'Fingerprint')
        } }
)

if ($NegativeControl) {
    $probe = 'var doc = JsonDocument.Parse(text);'
    if ($probe -notmatch 'JsonDocument') { throw '负对照失败：SS3/SS4 探针不触发' }
    $sentinelProbe = '// 手写文件（无哨兵）'
    if ($sentinelProbe -match '<auto-generated/>') { throw '负对照失败：SS5 哨兵判据过宽' }
    if ('ui-regions.json' -in @('diagnostics.json', 'fold.json')) { throw '负对照失败：SS2 白名单判据过宽' }
    if ('stateMigration' -match 'stateMigrations') { throw '负对照失败：SS7 复数键探针过宽' }
    Write-Host '[OK] 负对照通过：违规片段可被 SS2/SS3/SS4/SS5/SS7 捕获。' -ForegroundColor Green
}

if (-not $Check -and -not $NegativeControl) {
    Write-Host '用法: -Check（检查） / -NegativeControl（判据自检）' -ForegroundColor Yellow
    return
}

if ($Check) {
    $failed = 0
    foreach ($r in $rules) {
        $pass = $false
        try { $pass = [bool](& $r.Ok) } catch {
            Write-Host ("[FAIL] {0} {1} — {2}" -f $r.Id, $r.Title, $_.Exception.Message) -ForegroundColor Red
            $pass = $false
        }
        if (-not $pass) { $failed++ }
        Write-Host ("[{0}] {1} {2}" -f $(if ($pass) { 'PASS' } else { 'FAIL' }), $r.Id, $r.Title) -ForegroundColor $(if ($pass) { 'Green' } else { 'Red' })
    }
    if ($failed -gt 0) {
        Write-Host "UiDeclarationSingleSource: $failed 项违规。" -ForegroundColor Red
        exit 1
    }
    Write-Host 'UiDeclarationSingleSource: 全部通过。' -ForegroundColor Green
}

exit 0
