#requires -Version 5.1
<#
.SYNOPSIS
    GateClosure —— 清单闭合原语（纯集合运算；零解析、零 IO、零裁决语义）。

.DESCRIPTION
    收敛各门禁内联的「集合比对 + 豁免 + 缺口清单 + 严格分档」四件套（口径漂移源）：
    本件只吃三个集合、吐一个结论，不做任何文本解析（解析归各门禁，防"万能解析器"）。

    输出语义：
      Missing        = Declared − Implemented − Exempt   声明了但没实现（悬空）
      Orphans        = Implemented − Declared − Exempt   实现了但没声明（孤儿；只报告，不判红）
      Coverage       = |Declared ∩ Implemented| / |Declared|（Declared=∅ ⇒ $null = ⊥）
      WellFormed     = Declared 非空（空集不得判 pass：不可判定 ≠ 通过）
      Verdict        = 'undetermined'（WellFormed=false）| 'fail'（Missing 非空）| 'pass'

    Orphans 不参与 Verdict：动态工具面 / 晚注册实现是合法孤儿，判红会把闭合判据变成噪声源。

.EXAMPLE
    $r = Test-ClosureSet -Declared @('a','b') -Implemented @('a') -Label 'DC2'
    # $r.Missing = @('b'); $r.Coverage = 0.5; $r.Verdict = 'fail'
#>
Set-StrictMode -Version Latest

function Test-ClosureSet {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Declared,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Implemented,
        [AllowEmptyCollection()][string[]]$Exempt = @(),
        [string]$Label = 'closure'
    )

    $normalize = {
        param([AllowEmptyCollection()][string[]]$Items)
        $set = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($item in @($Items)) {
            if ($null -eq $item) { continue }
            $text = ([string]$item).Trim()
            if ($text.Length -eq 0) { continue }
            [void]$set.Add($text)
        }
        return $set
    }

    $declaredSet = & $normalize -Items $Declared
    $implementedSet = & $normalize -Items $Implemented
    $exemptSet = & $normalize -Items $Exempt

    $missing = [System.Collections.Generic.List[string]]::new()
    foreach ($key in $declaredSet) {
        if ($exemptSet.Contains($key)) { continue }
        if (-not $implementedSet.Contains($key)) { $missing.Add($key) }
    }

    $orphans = [System.Collections.Generic.List[string]]::new()
    foreach ($key in $implementedSet) {
        if ($exemptSet.Contains($key)) { continue }
        if (-not $declaredSet.Contains($key)) { $orphans.Add($key) }
    }

    $declaredCount = $declaredSet.Count
    $matchedCount = $declaredCount - $missing.Count
    $coverage = if ($declaredCount -eq 0) {
        $null
    } else {
        [math]::Round(($matchedCount / [double]$declaredCount), 4)
    }
    $wellFormed = $declaredCount -gt 0
    $verdict = if (-not $wellFormed) { 'undetermined' } elseif ($missing.Count -gt 0) { 'fail' } else { 'pass' }

    $missingArray = $missing.ToArray()
    $orphanArray = $orphans.ToArray()
    [System.Array]::Sort($missingArray, [System.StringComparer]::Ordinal)
    [System.Array]::Sort($orphanArray, [System.StringComparer]::Ordinal)

    return [pscustomobject]@{
        Label            = $Label
        Missing          = $missingArray
        Orphans          = $orphanArray
        DeclaredCount    = $declaredCount
        ImplementedCount = $implementedSet.Count
        MatchedCount     = $matchedCount
        Coverage         = $coverage
        WellFormed       = $wellFormed
        Verdict          = $verdict
    }
}

Export-ModuleMember -Function @('Test-ClosureSet')
