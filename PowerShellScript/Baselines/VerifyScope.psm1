#requires -Version 5.1
<#
VerifyScope.psm1 —— 验证面授权裁决原语（注册表装载 + 授权来源解析；无策略分支）

设计：
  · 注册表单源 = Baselines\_verify-scope.psd1（纯数据；`_` 前缀 ⇒ 不进入 GateBaselines 单件判据）；
  · 授权来源优先级 = -GrantFile（显式授权单）> 批次授权单 verify 字段 > 宿主令牌（结构事实，可审计）；
  · 消费方 = Environment\CentralTest\Internal\Invoke-Verify.ps1 ∧ Invoke-AgentRegression.ps1
    （唯一实现；禁消费方各自手写一套解析 ⇒ 对齐 Strategy.SingleImplementation）。

用法（消费方）：
    Import-Module (Join-Path $repoRoot 'Frame\PowerShellScript\Baselines\VerifyScope.psm1') -Force
    $grant = Resolve-FullRunGrant -RepoRoot $repoRoot -GrantFile $GrantFile
#>

$script:BaselinesRoot = $PSScriptRoot

<#
.SYNOPSIS
    装载验证面注册表（缺文件即抛：禁隐式空策略导致默认放行）。
#>
function Get-VerifyScopeRegistry
{
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    $path = Join-Path $RepoRoot 'Frame\PowerShellScript\Baselines\_verify-scope.psd1'
    if (-not (Test-Path -LiteralPath $path))
    {
        throw "验证面注册表缺失：$path（禁隐式空策略；约定见 Baselines\_verify-scope.psd1）"
    }
    return Import-PowerShellDataFile -LiteralPath $path
}

<#
.SYNOPSIS
    全量测试默认禁止开关（注册表事实）。
#>
function Test-FullRunRequired
{
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    $reg = Get-VerifyScopeRegistry -RepoRoot $RepoRoot
    return [bool]$reg.FullRunRequired.Value
}

<#
.SYNOPSIS
    取宿主令牌通道名（注册表声明；缺声明回退缺省名）。
#>
function Get-FullRunTokenEnvVar
{
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    $fallback = 'WF_VERIFY_FULL_GRANT'
    try { $reg = Get-VerifyScopeRegistry -RepoRoot $RepoRoot } catch { return $fallback }
    if ($reg.ContainsKey('GrantTokenEnvVar') -and $reg.GrantTokenEnvVar.Value) { return [string]$reg.GrantTokenEnvVar.Value }
    return $fallback
}

<#
.SYNOPSIS
    解析全量面授权来源（三者优先级：-GrantFile > 批次授权单 verify > 宿主令牌）。
.OUTPUTS
    命中 = @{ Source='<可审计来源>'; Matrix='<可选矩阵名>' }；未授权 = $null。
#>
function Resolve-FullRunGrant
{
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [string]$GrantFile = ''
    )

    # ① 显式授权单（JSON：{ "verify": { "full": true, "matrix": "<可选>", "reason": "..." } }）
    if (-not [string]::IsNullOrWhiteSpace($GrantFile))
    {
        if (-not (Test-Path -LiteralPath $GrantFile)) { return $null }
        try { $doc = ConvertFrom-Json (Get-Content -LiteralPath $GrantFile -Raw -Encoding UTF8) } catch { return $null }
        $v = $doc.verify
        if ($null -ne $v -and $v.full -eq $true)
        {
            return @{ Source = "授权单:$GrantFile"; Matrix = [string]$v.matrix }
        }
        return $null
    }

    # ② 活动批次授权单（单源指针 = Baselines\BatchScope.psd1 的 Manifest 事实）
    $batchBaseline = Join-Path $RepoRoot 'Frame\PowerShellScript\Baselines\BatchScope.psd1'
    if (Test-Path -LiteralPath $batchBaseline)
    {
        $b = Import-PowerShellDataFile -LiteralPath $batchBaseline
        $mfRel = [string]$b.Manifest.Value
        if (-not [string]::IsNullOrWhiteSpace($mfRel))
        {
            $mfPath = Join-Path $RepoRoot $mfRel
            if (Test-Path -LiteralPath $mfPath)
            {
                try { $mf = ConvertFrom-Json (Get-Content -LiteralPath $mfPath -Raw -Encoding UTF8) } catch { $mf = $null }
                if ($null -ne $mf)
                {
                    foreach ($bid in @($mf.active))
                    {
                        $batch = @($mf.batches | Where-Object { $_.batchId -eq $bid }) | Select-Object -First 1
                        if ($null -eq $batch) { continue }
                        if (@($batch.PSObject.Properties.Name) -notcontains 'verify') { continue }
                        if ($batch.verify.full -eq $true)
                        {
                            return @{ Source = "批次授权单:$mfRel [批 $bid]"; Matrix = [string]$batch.verify.matrix }
                        }
                    }
                }
            }
        }
    }

    # ③ 宿主令牌（由宿主进程注入；本模块不自置）
    $envName = Get-FullRunTokenEnvVar -RepoRoot $RepoRoot
    $tok = [Environment]::GetEnvironmentVariable($envName)
    if (-not [string]::IsNullOrWhiteSpace($tok)) { return @{ Source = "宿主令牌:$envName" } }

    return $null
}

Export-ModuleMember -Function Get-VerifyScopeRegistry, Test-FullRunRequired, Get-FullRunTokenEnvVar, Resolve-FullRunGrant
