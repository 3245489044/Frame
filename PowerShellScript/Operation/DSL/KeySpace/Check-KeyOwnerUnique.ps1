# Check-KeyOwnerUnique.ps1
# KB6 归属唯一（薄转发）：判据实现单源 = Check-KeySpaceInvariants.ps1 -Kind owner。
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

& (Join-Path $PSScriptRoot 'Check-KeySpaceInvariants.ps1') -Root $Root -Kind owner -Check:$Check -NegativeControl:$NegativeControl
exit $LASTEXITCODE
