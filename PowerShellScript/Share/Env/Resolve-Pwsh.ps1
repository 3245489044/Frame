#Requires -Version 5.1
<#
.SYNOPSIS
    Locate a runnable PowerShell 7+ (pwsh) and print its absolute path.

.DESCRIPTION
    Framework-level pwsh bootstrap. Called by batch entries (EnableMent UI
    launcher, Build-Distribution) through Windows PowerShell 5.1 (always
    present), so the entries no longer depend on pwsh being on PATH.

    Resolution order:
      1. WF_PWSH environment variable (portable/custom install)
      2. pwsh on PATH
      3. MSI install location (%ProgramFiles%\PowerShell\7)
      4. Store app execution alias (%LOCALAPPDATA%\Microsoft\WindowsApps)
      5. Store package folder (Microsoft.PowerShell_*)

    Exit codes: 0 = found (path on stdout); 2 = not found / not runnable.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Frame\PowerShellScript\Share\Env\Resolve-Pwsh.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Test-PwshCandidate {
    param([string]$Candidate)

    if ([string]::IsNullOrWhiteSpace($Candidate)) { return $false }
    if (-not (Test-Path -LiteralPath $Candidate -PathType Leaf)) { return $false }

    try {
        $raw = & $Candidate -NoProfile -NonInteractive -ExecutionPolicy Bypass `
            -Command '$PSVersionTable.PSVersion.Major' 2>$null
        if ($LASTEXITCODE -ne 0) { return $false }
        $major = 0
        $first = @($raw) | Select-Object -First 1
        if (-not [int]::TryParse([string]$first, [ref]$major)) { return $false }
        return ($major -ge 7)
    }
    catch {
        return $false
    }
}

$candidates = New-Object System.Collections.Generic.List[string]

if (-not [string]::IsNullOrWhiteSpace($env:WF_PWSH)) {
    $candidates.Add($env:WF_PWSH.Trim('"'))
}

$command = Get-Command -Name 'pwsh' -ErrorAction SilentlyContinue
if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace($command.Source)) {
    $candidates.Add($command.Source)
}

if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
    $candidates.Add((Join-Path $env:ProgramFiles 'PowerShell\7\pwsh.exe'))
}
if (-not [string]::IsNullOrWhiteSpace($env:ProgramW6432)) {
    $candidates.Add((Join-Path $env:ProgramW6432 'PowerShell\7\pwsh.exe'))
}

if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
    $windowsApps = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps'
    $candidates.Add((Join-Path $windowsApps 'pwsh.exe'))
    if (Test-Path -LiteralPath $windowsApps -PathType Container) {
        foreach ($dir in @(Get-ChildItem -LiteralPath $windowsApps -Directory -Filter 'Microsoft.PowerShell_*' -ErrorAction SilentlyContinue)) {
            $candidates.Add((Join-Path $dir.FullName 'pwsh.exe'))
        }
    }
}

$seen = @{}
foreach ($candidate in $candidates) {
    if ([string]::IsNullOrWhiteSpace($candidate)) { continue }

    $full = $null
    try { $full = [IO.Path]::GetFullPath($candidate) } catch { $full = $null }
    if ([string]::IsNullOrWhiteSpace($full)) { continue }
    if ($seen.ContainsKey($full)) { continue }
    $seen[$full] = $true

    if (Test-PwshCandidate -Candidate $full) {
        Write-Output $full
        exit 0
    }
}

[Console]::Error.WriteLine('[ERROR] PowerShell 7+ (pwsh) not found or not runnable.')
[Console]::Error.WriteLine('        Install: winget install --id Microsoft.PowerShell --source winget')
[Console]::Error.WriteLine('        Or add %LOCALAPPDATA%\Microsoft\WindowsApps to the user PATH, then reopen the terminal.')
[Console]::Error.WriteLine('        Portable: set WF_PWSH to the absolute path of pwsh.exe')
exit 2
