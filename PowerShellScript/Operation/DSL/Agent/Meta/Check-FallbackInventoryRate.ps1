#requires -Version 7.0
<#
.SYNOPSIS
    兜底清单守卫（P3 / C7）：所有静默兜底必须登记到 FallbackInventory 基线，未登记即红。
.DESCRIPTION
    扫描 Projects\Plug\Agent\src 内三类兜底：
      template-empty  `?? ConversationTemplates.Empty`
      empty-catch     `catch { }`（含 `catch (X) { }`）
      tryload-null    `TryLoad() ??`
    判据：每个命中必须能在 Baselines\Meta\FallbackInventory.psd1 找到 (File, Pattern) 登记项。
    C1 未登记 = 0；C2 登记表非空（禁空表放行）；C3 负对照：未登记命中必判未登记、已登记命中必判已登记。
    纯扫描（只读、0 写入）。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))))
$src = Join-Path $repo 'Projects\Plug\Agent\src'
$baselinesRoot = Join-Path $repo 'Frame\PowerShellScript\Baselines'
Import-Module (Join-Path $baselinesRoot 'GateBaseline.psm1') -Force

$patterns = @(
    [pscustomobject]@{ Id = 'template-empty'; Regex = '\?\?\s*ConversationTemplates\.Empty' },
    [pscustomobject]@{ Id = 'empty-catch'; Regex = 'catch\s*(\([^)]*\))?\s*\{\s*\}' },
    [pscustomobject]@{ Id = 'tryload-null'; Regex = 'TryLoad\(\)\s*\?\?' }
)

function Get-BaselineData {
    $data = Import-GateBaseline -Name FallbackInventoryRate
    $null = $data.Measured
    return $data
}

function Get-Register {
    $data = Get-BaselineData
    return @($data.Entries | ForEach-Object {
        $parts = ([string]$_.Value) -split '\|', 2
        [pscustomobject]@{ File = $parts[0]; Pattern = $parts[1] }
    })
}

function Find-Hits {
    $hits = New-Object System.Collections.Generic.List[object]
    Get-ChildItem -LiteralPath $src -Recurse -Include *.cs -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|\\BuildOutput\\' } |
        ForEach-Object {
            $text = Get-Content -LiteralPath $_.FullName -Raw
            $relative = $_.FullName.Substring($src.Length).TrimStart('\').Replace('\', '/')
            foreach ($p in $patterns) {
                if ($text -match $p.Regex) {
                    $hits.Add([pscustomobject]@{ File = $relative; Pattern = $p.Id })
                }
            }
        }
    return $hits
}

function Test-Registered($hit, $register) {
    foreach ($entry in $register) {
        if ([string]$entry.Pattern -ne [string]$hit.Pattern) { continue }
        $file = ([string]$entry.File).Replace('\', '/')
        if ($hit.File.EndsWith($file, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

$rules = @(
    [pscustomobject]@{ Id = 'C1'; Title = '未登记兜底 = 0'
        Ok = {
            $register = Get-Register
            $unregistered = @(Find-Hits | Where-Object { -not (Test-Registered $_ $register) })
            if ($unregistered.Count -gt 0) {
                $unregistered | ForEach-Object { Write-Host ("  未登记: {0} [{1}]" -f $_.File, $_.Pattern) -ForegroundColor Red }
            }
            return $unregistered.Count -eq 0
        } }
    [pscustomobject]@{ Id = 'C2'; Title = '登记表非空（禁空表放行）'
        Ok = { @(Get-Register).Count -gt 0 } }
)

if ($NegativeControl) {
    $hit = [pscustomobject]@{ File = 'Agent.Ui/Ui/Views/X.cs'; Pattern = 'template-empty' }
    $empty = @()
    $one = @([pscustomobject]@{ File = 'Agent.Ui/Ui/Views/X.cs'; Pattern = 'template-empty' })
    if (Test-Registered $hit $empty) { throw '负对照失败：空登记表不应放行' }
    if (-not (Test-Registered $hit $one)) { throw '负对照失败：已登记命中应判已登记' }
    Write-Host '[OK] 负对照通过：未登记必红、已登记放行。' -ForegroundColor Green
}

if (-not $Check -and -not $NegativeControl) {
    Write-Host '用法: -Check（检查） / -NegativeControl（判据自检）' -ForegroundColor Yellow
    return
}

if ($Check) {
    $failed = 0
    foreach ($r in $rules) {
        $pass = $false
        try { $pass = [bool](& $r.Ok) } catch { $pass = $false }
        $mark = if ($pass) { 'PASS' } else { 'FAIL' }
        if (-not $pass) { $failed++ }
        Write-Host ("[{0}] {1} {2}" -f $mark, $r.Id, $r.Title) -ForegroundColor $(if ($pass) { 'Green' } else { 'Red' })
    }
    if ($failed -gt 0) {
        Write-Host "FallbackInventoryRate: $failed 项违规（兜底未登记）。" -ForegroundColor Red
        exit 1
    }
    Write-Host 'FallbackInventoryRate: 全部通过。' -ForegroundColor Green
}

exit 0
