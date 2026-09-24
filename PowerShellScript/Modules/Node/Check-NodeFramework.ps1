#requires -Version 5.1
<#
.SYNOPSIS
    节点通用框架（Modules.Node）架构不变式门禁 —— 位置层纯净性 / 无进程静态 / 无硬编码反解。

.DESCRIPTION
    对 Frame/CsharpPrj/Modules/Node 执行四类静态判定（权威见 Modules/Node/docs/）：

      A. 语言类型不漏进位置层（P20 / K1 / K6）：本工程 csproj 与全部 *.cs 零 `Usharp` 命中；
      B. 无进程静态可变配置（K5 / P6 变体 / P14）：*.cs 零 `AsyncLocal` 用法
         （键层次化格式/前缀/后缀/分隔符必须来自声明，运行期只读）；
      C. 无硬编码位置反解（P22 / L-3）：*.cs 零 `loc =>` / `Location =>` 形式的映射字面量
         （loc → Placement 的唯一来源是声明，框架内只经 IPlacementResolver 取）；
      D. 位置层不引上层契约（D-1 案 A）：*.cs 零 `using Operation.` / `using Share.`
         （`NodeOutput`/`INodeOutputStore`(Operation) 与 `Chain<TKey,TNode>`/`UniversalMiddlewareEntry`(Share)
          由上层适配器持有，本工程契约以 JSON/BCL 为界）；
      E. （告警，不判失败）静态可变字段 / 静态属性（static 且非 readonly/const）：K5 建议一律实例级，
         静态属性须返回只读/冻结视图（原实现只查字段，静态属性漏检）。

    层序（向上引用）由 Share/Solution/Test-ProjectReferences.ps1 统一校验，本脚本不重复实现。
    CI 集成：返回码 0 = 无违规；非 0 = 存在违规（架构回归）。

.PARAMETER Root
    仓库根目录（默认由本脚本位置上溯四级推导：Frame\PowerShellScript\Modules\Node → 仓库根）。

.PARAMETER ShowDetail
    输出全部检查详情（默认仅输出违规/告警摘要）。

.EXAMPLE
    .\Check-NodeFramework.ps1
    .\Check-NodeFramework.ps1 -ShowDetail
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    # 脚本位于 Frame\PowerShellScript\Modules\Node\，上溯四级即仓库根（与 Check-SingleWriter.ps1 一致）
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()
$warnings   = [System.Collections.Generic.List[string]]::new()

$nodeDir  = Join-Path $Root 'Frame\CsharpPrj\Modules\Node'
$csproj   = Join-Path $nodeDir 'Modules.Node.csproj'

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Modules.Node 框架门禁（位置层纯净性 / 无进程静态 / 无硬编码反解）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

if (-not (Test-Path $nodeDir))
{
    Write-Host "  [失败] 缺失框架目录: $nodeDir" -ForegroundColor Red
    exit 1
}

$csFiles = @(Get-ChildItem -Path $nodeDir -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notlike '*\_deprecated\*' })

# ---------------- A. 语言类型不漏进位置层（P20） ----------------
# 判定形态为「限定名/路径」：`Proj_Usharp*` / `Usharp\` / `Usharp.<Token>`；
# 散文式提及（如规则说明里的"禁 Usharp 类型"）不算违规，避免自指误报。
$usharpTypeRegex = '(?<![A-Za-z0-9_])Usharp(\\|\.|\b[A-Z])|Proj_Usharp'

if (Test-Path $csproj)
{
    $hit = Select-String -Path $csproj -Pattern $usharpTypeRegex -ErrorAction SilentlyContinue
    if ($hit) { $violations.Add("[A/P20] Modules.Node.csproj 出现 Usharp 限定名/路径（位置层被语言绑定）") }
    elseif ($ShowDetail) { Write-Host "OK(A): csproj 零 Usharp 引用命中。" -ForegroundColor Green }
}
else
{
    $violations.Add("[A/P20] 缺失框架工程文件 Modules.Node.csproj")
}

$usharpHits = @($csFiles | Select-String -Pattern $usharpTypeRegex -ErrorAction SilentlyContinue)
if ($usharpHits.Count -gt 0)
{
    foreach ($h in $usharpHits) { $violations.Add("[A/P20] $($h.Path):$($h.LineNumber) 出现 Usharp 限定名（桥接类型只许在 Usharp 适配工程）") }
}
elseif ($ShowDetail) { Write-Host "OK(A): 源码零 Usharp 引用命中。" -ForegroundColor Green }

# ---------------- B. 无进程静态可变配置（K5/P14） ----------------
$asyncLocalHits = @($csFiles | Select-String -Pattern 'AsyncLocal' -ErrorAction SilentlyContinue)
if ($asyncLocalHits.Count -gt 0)
{
    foreach ($h in $asyncLocalHits) { $violations.Add("[B/P14] $($h.Path):$($h.LineNumber) 使用 AsyncLocal（键格式等可变配置必须声明化）") }
}
elseif ($ShowDetail) { Write-Host "OK(B): 零 AsyncLocal 用法。" -ForegroundColor Green }

# ---------------- C. 无硬编码位置反解（P22/L-3） ----------------
$locMapHits = @($csFiles | Select-String -Pattern '\bloc\s*=>|\bLocation\s*=>' -ErrorAction SilentlyContinue)
if ($locMapHits.Count -gt 0)
{
    foreach ($h in $locMapHits) { $violations.Add("[C/P22] $($h.Path):$($h.LineNumber) 出现 loc/Location 硬编码映射（反解唯一来源 = 声明）") }
}
elseif ($ShowDetail) { Write-Host "OK(C): 零 loc→carrier 硬编码映射。" -ForegroundColor Green }

# ---------------- D. 位置层不引上层契约（D-1 案 A） ----------------
$usingHits = @($csFiles | Select-String -Pattern '^\s*using\s+(Operation|HierarchyMethod|LoopFoldMethod|Share)\b' -ErrorAction SilentlyContinue)
if ($usingHits.Count -gt 0)
{
    foreach ($h in $usingHits) { $violations.Add("[D/D-1] $($h.Path):$($h.LineNumber) 直接 using 上层命名空间（契约应以 JSON/BCL 为界，适配器放在上层）") }
}
elseif ($ShowDetail) { Write-Host "OK(D): 零上层命名空间 using。" -ForegroundColor Green }

# ---------------- E. （告警）静态可变字段 / 静态暴露面（K5） ----------------
# E-1：静态字段（非 readonly/const）
$staticFieldRegex = [regex]'^\s*(?:public|private|internal|protected)\s+static\s+(?!readonly\b|const\b|partial\b|class\b)([A-Za-z_][\w<>\[\],\.\?\s]*?)\s+([A-Za-z_]\w*)\s*(?:=|;|=>)'
# E-2：静态属性（自动属性 / 表达式体属性）——字段检查之外的另一条静态暴露面（原实现只查字段，静态属性漏检）；
#      返回只读/冻结类型（IReadOnly*/ReadOnly*/Frozen*/Immutable*）即视为已隔离，不告警。
$staticPropertyRegex = [regex]'^\s*(?:public|private|internal|protected)\s+static\s+(?!readonly\b|const\b|partial\b|class\b|IReadOnly|ReadOnly|Frozen|Immutable)([A-Za-z_][\w<>\[\],\.\?\s]*?)\s+([A-Za-z_]\w*)\s*(?:\{\s*(?:get|set|init)\b|=>)'
foreach ($f in $csFiles)
{
    $lineNo = 0
    foreach ($line in [System.IO.File]::ReadAllLines($f.FullName))
    {
        $lineNo++
        if ($line -match '\(') { continue }
        # 表达式体成员（`static X Y => …`）不是字段，但静态表达式体属性仍属静态暴露面
        $arrowIndex = $line.IndexOf('=>')
        if ($arrowIndex -gt 0 -and $line[0..($arrowIndex - 1)] -notmatch '=')
        {
            if ($staticPropertyRegex.IsMatch($line))
            {
                $warnings.Add("[E2/K5] $($f.FullName):$lineNo 静态属性（静态暴露面，须返回只读/冻结视图）: $($line.Trim())")
            }

            continue
        }

        if ($staticFieldRegex.IsMatch($line))
        {
            $warnings.Add("[E/K5] $($f.FullName):$lineNo 疑似静态可变字段（应改为实例级）: $($line.Trim())")
        }
        elseif ($staticPropertyRegex.IsMatch($line))
        {
            $warnings.Add("[E2/K5] $($f.FullName):$lineNo 静态属性（静态暴露面，须返回只读/冻结视图）: $($line.Trim())")
        }
    }
}

# ---------------- 汇总 ----------------
Write-Host ""
Write-Host "  扫描源码文件: $($csFiles.Count) 个" -ForegroundColor Gray

if ($warnings.Count -gt 0)
{
    Write-Host "  [告警] $($warnings.Count) 项：" -ForegroundColor Yellow
    foreach ($w in $warnings) { Write-Host "    - $w" -ForegroundColor Yellow }
}

if ($violations.Count -gt 0)
{
    Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
    foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
    Write-Host ""
    Write-Host "  结果: 失败（架构回归）" -ForegroundColor Red
    Write-Host ""
    exit 1
}

Write-Host "  [成功] A/B/C/D 全部通过（告警 $($warnings.Count) 项）" -ForegroundColor Green
Write-Host ""
exit 0
