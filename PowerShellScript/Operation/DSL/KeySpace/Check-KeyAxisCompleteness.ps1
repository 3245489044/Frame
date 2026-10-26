# Check-KeyAxisCompleteness.ps1
# KB6 轴完备（薄转发）：判据实现单源 = Check-KeySpaceInvariants.ps1 -Kind axis。
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

& (Join-Path $PSScriptRoot 'Check-KeySpaceInvariants.ps1') -Root $Root -Kind axis -Check:$Check -NegativeControl:$NegativeControl
exit $LASTEXITCODE
