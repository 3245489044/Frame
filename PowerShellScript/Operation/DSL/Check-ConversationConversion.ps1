#requires -Version 5.1
<#
.SYNOPSIS
    对话形态转换聚拢点（CV 族）架构不变式审计 —— 对齐 Agent.Session\Conversion 的 CV-I1/CV-I2/CV-I4 判据。

.DESCRIPTION
    收敛目标（散点治理）：同一「对话/历史」语义的多种形态（规范消息 / 会话日志 / 历史 JSON / 字典数组 /
    节点产出 / 分层步骤 / 中立外部消息）只允许经「一处注册、一处调用」转换：
      ConversationConversionHub（单一注册表 + 单一入口）+ ConversationConversionDefaults（内建注册）。
    检查项（命中即违规）：
      A. CV-I4 形态判定不得用字符串嗅探：会话相关源码的**代码行**不得出现 StartsWith("[")（注释行豁免）；
      B. CV-I2 wire 渲染不得散落：ConversationAgentWireRegistry.Resolve 的生产消费点仅允许
         ConversationWireJsonCodec.cs（聚拢点内建转换器）+ ConversationPipeline.cs（IoC 注入默认值 seam）；
      C. CV-I1 每对 (From,To) 恰一实现：生产源码不得用 Hub.Register( 严格注册（应经 RegisterOnce 幂等装配）；
      D. 双包装不得回归：CanonicalContext 已退役（规范上下文包装唯一 = Agent.Session.StandardContext），
         生产源码不得再出现该类型名；
      E. （告警，不判失败）聚拢点尚无跨工程消费（Conversation/ 目录外无人调用 Hub）→ 框架空转风险。
    CI 集成：返回码 0 = 无违规；非 0 = 存在违规（形态转换散点回归）。

.PARAMETER Root
    扫描根目录（默认仓库根，相对本脚本上溯四级）。

.PARAMETER ShowDetail
    输出全部命中明细（默认仅输出违规摘要与计数）。

.EXAMPLE
    .\Check-ConversationConversion.ps1
    .\Check-ConversationConversion.ps1 -Root d:\Project\Main -ShowDetail
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    # 脚本位于 Frame\PowerShellScript\Operation\DSL\，上溯四级即仓库根（与 Check-SingleWriter.ps1 一致）
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}
$violations = [System.Collections.Generic.List[string]]::new()
$warnings   = [System.Collections.Generic.List[string]]::new()

$agentSrc = Join-Path $Root 'Projects\Plug\Agent\src'
if (-not (Test-Path $agentSrc))
{
    Write-Host "[CV] 未找到 Agent 源码目录：$agentSrc（跳过）"
    exit 0
}

$csFiles = Get-ChildItem -Path $agentSrc -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notlike '*\_deprecated\*' }

# 代码行（剔除 /// 与 // 注释行：注释中允许提及被禁模式，便于说明「已退役/已替代」）
function Get-CodeLines([string]$Path)
{
    Get-Content -LiteralPath $Path | Where-Object {
        $t = $_.TrimStart()
        -not ($t.StartsWith('//') -or $t.StartsWith('*') -or $t.StartsWith('/*'))
    }
}

# A. CV-I4：会话相关源码不得用前缀嗅探判形态。
$conversationFiles = $csFiles | Where-Object { $_.Name -match 'Conversation|Cline|Answer' }
foreach ($f in $conversationFiles)
{
    foreach ($line in Get-CodeLines $f.FullName)
    {
        if ($line -match 'StartsWith\("\["')
        {
            $violations.Add("CV-I4 形态嗅探：$($f.Name) 用 StartsWith(""["") 判形态；应经 ConversationPayloadForms / ConversationConversionHub.CanConvert")
        }
    }
}

# B. CV-I2：wire 注册表直连仅允许聚拢点内建转换器与 IoC 默认值 seam。
$wireAllow = @('ConversationWireJsonCodec.cs', 'ConversationPipeline.cs')
foreach ($f in $csFiles)
{
    foreach ($line in Get-CodeLines $f.FullName)
    {
        if ($line -match 'ConversationAgentWireRegistry\.Resolve' -and $wireAllow -notcontains $f.Name)
        {
            $violations.Add("CV-I2 wire 直连散点：$($f.Name) 直接 Resolve wire；应经 ConversationConversionHub（Messages → WireJson）")
        }
    }
}

# C. CV-I1：严格注册（非幂等）不得出现在生产源码（Hub 自身除外）。
foreach ($f in $csFiles)
{
    if ($f.Name -eq 'ConversationConversionHub.cs') { continue }
    foreach ($line in Get-CodeLines $f.FullName)
    {
        if ($line -match 'ConversationConversionHub\.Register\(')
        {
            $violations.Add("CV-I1 非幂等注册：$($f.Name) 使用 Hub.Register(；装配须用 RegisterOnce（幂等、身份冲突 fail-fast）")
        }
    }
}

# D. 双包装不得回归（CanonicalContext 已退役）。
foreach ($f in $csFiles)
{
    foreach ($line in Get-CodeLines $f.FullName)
    {
        if ($line -match '\bCanonicalContext\b')
        {
            $violations.Add("双包装回归：$($f.Name) 出现已退役的 CanonicalContext；规范上下文包装唯一 = Agent.Session.StandardContext")
        }
    }
}

# E. 告警：聚拢点是否有 Conversion/ 目录外的消费方（框架空转风险）。
$hubConsumers = @()
foreach ($f in $csFiles)
{
    if ($f.FullName -match '\\Agent\.Session\\Conversion\\') { continue }
    if (Select-String -Path $f.FullName -Pattern 'ConversationConversionHub\.' -ErrorAction SilentlyContinue)
    {
        $hubConsumers += $f.Name
    }
}
if ($hubConsumers.Count -eq 0)
{
    $warnings.Add("CV 聚拢点暂无 Conversion/ 目录外消费方（框架空转风险）")
}

if ($ShowDetail)
{
    Write-Host "[CV] 扫描文件：$($csFiles.Count) 个 .cs（会话相关 $($conversationFiles.Count)）"
    Write-Host "[CV] 聚拢点消费方：$($hubConsumers -join ', ')"
    foreach ($w in $warnings) { Write-Host "[CV][警告] $w" }
}

if ($violations.Count -gt 0)
{
    Write-Host "[CV] 违规 $($violations.Count) 项："
    foreach ($v in $violations) { Write-Host "  - $v" }
    if ($warnings.Count -gt 0)
    {
        foreach ($w in $warnings) { Write-Host "[CV][警告] $w" }
    }
    exit 1
}

Write-Host "[CV] 通过：形态转换无散点（CV-I1/CV-I2/CV-I4 全绿；告警 $($warnings.Count) 项）"
if ($warnings.Count -gt 0 -and -not $ShowDetail)
{
    foreach ($w in $warnings) { Write-Host "[CV][警告] $w" }
}
exit 0
