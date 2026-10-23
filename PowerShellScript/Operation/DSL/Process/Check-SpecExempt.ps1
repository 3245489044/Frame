<#
.SYNOPSIS
    spec-exempt 登记与到期检查（D-06：豁免必须有闭合）。
.DESCRIPTION
    框架允许 `// spec-exempt:` 豁免，但无到期检查 ⇒ 不一致被永久合法化。
    本件把豁免变成可判据的结构事实：每条豁免必须携带 owner=<归属> 与 expires=<yyyy-MM-dd>，
    到期未收敛即 FAIL（-Check 时退出码 1）。
    正/负对照：内置自检（合规/缺 owner/缺 expires/已到期）在扫描前执行，自检失败即 FAIL（防判据空转）。
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-RepoRoot {
    (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

function Test-ExemptionLine {
    param([string]$Text, [datetime]$Today)
    $m = [regex]::Match($Text, '^\s*//\s*spec-exempt:\s*(?<body>.*)$')
    if (-not $m.Success) { return 'not-an-exemption' }
    $body = $m.Groups['body'].Value
    $owner = [regex]::Match($body, 'owner=(?<v>[A-Za-z0-9_.-]+)')
    $expires = [regex]::Match($body, 'expires=(?<v>\d{4}-\d{2}-\d{2})')
    if (-not $owner.Success) { return 'missing-owner' }
    if (-not $expires.Success) { return 'missing-expires' }
    $due = [datetime]::MinValue
    if (-not [datetime]::TryParseExact($expires.Groups['v'].Value, 'yyyy-MM-dd', $null,
            [System.Globalization.DateTimeStyles]::None, [ref]$due)) { return 'bad-expires' }
    if ($due -lt $Today) { return "expired($($expires.Groups['v'].Value))" }
    return $null
}

function Invoke-SelfTest {
    $today = [datetime]'2026-01-01'
    $cases = @(
        @{ Text = '// spec-exempt: migration owner=agent-core expires=2026-12-31'; Expect = $null },
        @{ Text = '// spec-exempt: migration expires=2026-12-31'; Expect = 'missing-owner' },
        @{ Text = '// spec-exempt: migration owner=agent-core'; Expect = 'missing-expires' },
        @{ Text = '// spec-exempt: migration owner=agent-core expires=2025-12-31'; Expect = 'expired(2025-12-31)' }
    )
    foreach ($case in $cases) {
        $actual = Test-ExemptionLine -Text $case.Text -Today $today
        if ($actual -ne $case.Expect) {
            return "自检失败: [$($case.Text)] 期望 '$($case.Expect)' 实际 '$actual'"
        }
    }
    return $null
}

$selfTest = Invoke-SelfTest
if ($selfTest) {
    Write-Host "[SPEC-EXEMPT][FAIL] $selfTest" -ForegroundColor Red
    if ($Check) { exit 1 }
    return
}

$root = Get-RepoRoot
$scanRoot = Join-Path $root 'Environment\CentralTest'
$today = (Get-Date).Date
$violations = New-Object System.Collections.Generic.List[string]
$count = 0

# 存量豁免登记册（D-06）：未 inline 声明 owner/expires 的既有豁免必须在册；在册同样须未到期。
$baselinePath = Join-Path $root 'Frame\PowerShellScript\Baselines\Process\_SpecExemptions.psd1'
$registry = @{}
if (Test-Path -LiteralPath $baselinePath) {
    $data = Import-PowerShellDataFile -LiteralPath $baselinePath
    if ($data.ContainsKey('Registry')) { $registry = $data.Registry }
}

if (Test-Path -LiteralPath $scanRoot) {
    $files = Get-ChildItem -LiteralPath $scanRoot -Recurse -Filter '*.cs' -File |
        Where-Object { $_.FullName -notmatch '\\(BuildOutput|obj|bin|_deprecated)\\' }

    foreach ($file in $files) {
        $rel = [System.IO.Path]::GetRelativePath($root, $file.FullName)
        $lineNo = 0
        foreach ($line in [System.IO.File]::ReadLines($file.FullName)) {
            $lineNo++
            if ($line -notmatch '^\s*//\s*spec-exempt:') { continue }
            $count++
            $reason = Test-ExemptionLine -Text $line -Today $today
            if ($null -eq $reason) { continue }

            if ($reason -eq 'missing-owner') {
                $entry = $registry[$rel]
                if ($null -eq $entry) {
                    $violations.Add("${rel}:${lineNo} => unregistered（须 inline owner/expires 或登记 Baselines\\Process\\_SpecExemptions.psd1）")
                    continue
                }
                $expires = [string]$entry.Expires
                $due = [datetime]::MinValue
                if (-not [datetime]::TryParseExact($expires, 'yyyy-MM-dd', $null,
                        [System.Globalization.DateTimeStyles]::None, [ref]$due)) {
                    $violations.Add("${rel}:${lineNo} => baseline-bad-expires($expires)")
                }
                elseif ($due -lt $today) {
                    $violations.Add("${rel}:${lineNo} => expired($expires, owner=$($entry.Owner))")
                }
                continue
            }

            $violations.Add("${rel}:${lineNo} => $reason")
        }
    }
}

if ($violations.Count -gt 0) {
    foreach ($v in $violations) { Write-Host "[SPEC-EXEMPT][FAIL] $v" -ForegroundColor Red }
    Write-Host "[SPEC-EXEMPT] 违例 $($violations.Count) / 豁免 $count（须 owner=<归属> expires=<yyyy-MM-dd>，到期即收敛）"
    if ($Check) { exit 1 }
    return
}

Write-Host "[SPEC-EXEMPT][PASS] 豁免 $count 条，全部已登记且未到期"
if ($Check) { exit 0 }
