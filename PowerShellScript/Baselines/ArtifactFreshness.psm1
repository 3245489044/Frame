#requires -Version 5.1
<#
.SYNOPSIS
    产物新鲜度判据族（单源）—— 「源 → 产物」新鲜度比较的唯一实现（B6/SCT-08）。

.DESCRIPTION
    把原先两处各自实现的新鲜度比较（dll 烘焙 / 配置产物）收敛为**一族两口径**：
      - 内容指纹：源文件与产物文件 SHA256 逐字比对（Fingerprint）。
      - 结构包含：源文本中的引用（事件键 / 绑定引用）须在产物文本中存在（Contain）。
    消费方：Assert-BakeFreshness.ps1（指纹）/ Check-AgentRuntimeUIConfigFreshness.ps1（包含），
    二者退为对本族的薄调用（门禁 FreshnessFamilySingleSource：比较实现 = 1 处）。
#>

Set-StrictMode -Version Latest

# ---- 口径 1：内容指纹（SHA256 逐字） ----
function Get-ArtifactContentFingerprint {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Test-ArtifactFingerprintFresh {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$ArtifactPath
    )

    $s = Get-ArtifactContentFingerprint -Path $SourcePath
    $a = Get-ArtifactContentFingerprint -Path $ArtifactPath
    if ($null -eq $s -or $null -eq $a) { return $false }
    return ($s -eq $a)
}

# ---- 口径 2：结构包含（源引用 ⊆ 产物） ----
function Get-ArtifactContainmentMisses {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$SourceText,
        [Parameter(Mandatory = $true)][string]$ProductText
    )

    $misses = [System.Collections.Generic.List[string]]::new()
    foreach ($m in [regex]::Matches($SourceText, '"(\w+)"\s*:\s*"AgentUi_[^"]*"')) {
        if ($ProductText -notmatch [regex]::Escape($m.Value)) { $misses.Add('事件/函数引用缺失：' + $m.Value) }
    }
    foreach ($m in [regex]::Matches($SourceText, '"(?:source|target|output)"\s*:\s*"([^"]+)"')) {
        $value = $m.Groups[1].Value
        if ($ProductText -notmatch ('"' + [regex]::Escape($value) + '"')) { $misses.Add("绑定引用缺失：$value") }
    }
    return $misses
}

# ---- 入口（按口径分派；两条既有判据的一行配置调用面） ----
function Test-ArtifactFreshness {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][ValidateSet('Fingerprint', 'Contain')][string]$Mode,
        [string]$SourceText,
        [string]$ProductText,
        [string]$SourcePath,
        [string]$ArtifactPath
    )

    switch ($Mode) {
        'Fingerprint' { return (Test-ArtifactFingerprintFresh -SourcePath $SourcePath -ArtifactPath $ArtifactPath) }
        'Contain' {
            $misses = Get-ArtifactContainmentMisses -SourceText $SourceText -ProductText $ProductText
            return ($misses.Count -eq 0)
        }
    }
}

Export-ModuleMember -Function Get-ArtifactContentFingerprint, Test-ArtifactFingerprintFresh, Get-ArtifactContainmentMisses, Test-ArtifactFreshness
