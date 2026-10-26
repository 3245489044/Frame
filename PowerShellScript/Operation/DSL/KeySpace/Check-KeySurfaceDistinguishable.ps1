# Check-KeySurfaceDistinguishable.ps1
# KB6 面可区分（薄转发）：判据实现单源 = Check-KeySpaceInvariants.ps1 -Kind surface。
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

& (Join-Path $PSScriptRoot 'Check-KeySpaceInvariants.ps1') -Root $Root -Kind surface -Check:$Check -NegativeControl:$NegativeControl
exit $LASTEXITCODE
