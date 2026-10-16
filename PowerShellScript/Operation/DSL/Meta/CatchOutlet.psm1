#requires -Version 5.1
<#
.SYNOPSIS
    catch 出口分类内核（「禁静默出口」的 catch 面）—— **单一实现**（TH-12）。

.DESCRIPTION
    把「catch 吞并」的判定收敛为一处，供三方共用：
      - Check-FallbackInventory.ps1（门禁棘轮：候选计数）
      - Invoke-CatchTriage.ps1（盘点：机读清单 + 分类）
      - Invoke-CatchOutletPatch.ps1（批修：按出口升级，锚点 + 哈希校验）

    分类（对齐 fallback-registry.json 的出口口径；判据载体 = catch 块体文本）：
      rethrow       块体含 throw ⇒ 出口 T（fail-fast）
      handle        块体含 handle( ⇒ 经 handle(error,Scope)
      budget        块体含 FailureBudget ⇒ 入预算账（D）
      degrade       块体含 Degrade ⇒ 显式降级 + 可见（D）
      candidate-logonly  含日志（AgentLog/ModuleLog/Log(）但无出口 ⇒ 静默吞并候选
      candidate-empty    无任何出口 / 日志 ⇒ 完全静默候选

    合法性（是否计入棘轮）= 分类 ∈ {candidate-*} ∧ ¬已登记（fallback-registry entries type=C2）。
#>

Set-StrictMode -Version Latest

$script:RxComment = [regex]'^\s*(//|/\*|\*)'
$script:CatchEngine = $null

function Get-CatchBodyHash {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Body)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Body)
        return ([System.BitConverter]::ToString($sha.ComputeHash($bytes)) -replace '-', '').ToLowerInvariant()
    } finally { $sha.Dispose() }
}

function Get-CatchOutletClass {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Body)
    if ($Body -match '\bthrow\b') { return 'rethrow' }
    if ($Body -match 'handle\s*\(') { return 'handle' }
    if ($Body -match 'FailureBudget') { return 'budget' }
    if ($Body -match '\bDegrade\b') { return 'degrade' }
    if ($Body -match 'AgentLog|ModuleLog|Log\(') { return 'candidate-logonly' }
    return 'candidate-empty'
}

<#
.SYNOPSIS
    检测 Roslyn（Microsoft.CodeAnalysis.CSharp）可用性并置引擎标志。
.DESCRIPTION
    pwsh 7 自带 Roslyn；Windows PowerShell 5.1 缺省无 ⇒ 依次尝试按候选路径加载。
    引擎可用时 catch 判定走 AST（语义级，屏蔽注释/字符串/格式漂移）；否则降级文本法。
#>
function Initialize-CatchRoslyn {
    [CmdletBinding()]
    param([string[]]$CandidateDirs = @())
    if ($null -ne ('Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree' -as [type])) { $script:CatchEngine = 'roslyn'; return $true }
    $dirs = [System.Collections.Generic.List[string]]::new()
    foreach ($d in $CandidateDirs) { if (-not [string]::IsNullOrWhiteSpace($d)) { $dirs.Add($d) } }
    if (-not [string]::IsNullOrWhiteSpace($env:CATCH_ROSLYN_DIR)) { $dirs.Add($env:CATCH_ROSLYN_DIR) }
    foreach ($d in $dirs) {
        $co = Join-Path $d 'Microsoft.CodeAnalysis.dll'
        $cs = Join-Path $d 'Microsoft.CodeAnalysis.CSharp.dll'
        if ((Test-Path -LiteralPath $co) -and (Test-Path -LiteralPath $cs)) {
            try {
                [System.Reflection.Assembly]::LoadFrom($co) | Out-Null
                [System.Reflection.Assembly]::LoadFrom($cs) | Out-Null
            } catch { continue }
            if ($null -ne ('Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree' -as [type])) { $script:CatchEngine = 'roslyn'; return $true }
        }
    }
    $script:CatchEngine = 'text'
    return $false
}

<#
.SYNOPSIS
    Roslyn AST 判定：解析源码为语法树，取全部 CatchClauseSyntax 并做语义级出口分类。
#>
function Get-CatchBlocksRoslyn {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text)
    $blocks = [System.Collections.Generic.List[object]]::new()
    if ([string]::IsNullOrWhiteSpace($Text)) { return $blocks }
    $root = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($Text).GetRoot()
    foreach ($c in $root.DescendantNodes()) {
        if (-not ($c -is [Microsoft.CodeAnalysis.CSharp.Syntax.CatchClauseSyntax])) { continue }
        $span = $c.GetLocation().GetLineSpan()
        $ex = ''
        if ($null -ne $c.Declaration -and $null -ne $c.Declaration.Identifier) { $ex = $c.Declaration.Identifier.ValueText }

        $hasThrow = $false; $hasHandle = $false; $hasBudget = $false; $hasDegrade = $false; $hasLog = $false
        foreach ($n in $c.DescendantNodes()) {
            $tn = $n.GetType().Name
            if ($tn -eq 'ThrowStatementSyntax') { $hasThrow = $true; continue }
            if ($tn -eq 'IdentifierNameSyntax') {
                if ($n.Identifier.ValueText -eq 'FailureBudget') { $hasBudget = $true }
                continue
            }
            if ($tn -eq 'InvocationExpressionSyntax') {
                $name = $n.Expression.ToString()
                if ($name -match '(^|\.)handle$') { $hasHandle = $true }
                elseif ($name -match '[Dd]egrade') { $hasDegrade = $true }
                elseif ($name -match 'Log') { $hasLog = $true }
            }
            elseif ($tn -eq 'MemberAccessExpressionSyntax') {
                if ($n.ToString() -match 'Log') { $hasLog = $true }
            }
        }
        $class = if ($hasThrow) { 'rethrow' }
            elseif ($hasHandle) { 'handle' }
            elseif ($hasBudget) { 'budget' }
            elseif ($hasDegrade) { 'degrade' }
            elseif ($hasLog) { 'candidate-logonly' }
            else { 'candidate-empty' }

        $blocks.Add([pscustomobject]@{
            StartLine = $span.StartLinePosition.Line + 1
            EndLine   = $span.EndLinePosition.Line + 1
            Exception = $ex
            Class     = $class
        })
    }
    return $blocks
}

<#
.SYNOPSIS
    提取文件内的全部 catch 块（起始行/结束行/异常类型/块体/哈希/分类）。
#>
function Get-CatchBlocks {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][AllowEmptyString()][string[]]$Lines)
    if ($null -eq $script:CatchEngine) { [void](Initialize-CatchRoslyn) }
    $blocks = [System.Collections.Generic.List[object]]::new()

    if ($script:CatchEngine -eq 'roslyn') {
        foreach ($b in (Get-CatchBlocksRoslyn -Text ($Lines -join "`n"))) {
            $body = ''
            if ($b.EndLine -ge $b.StartLine -and $b.StartLine -ge 1 -and $b.EndLine -le $Lines.Count) {
                $body = ((@($Lines[($b.StartLine - 1)..($b.EndLine - 1)] | ForEach-Object { ($_ -replace '//.*$', '') }) -join ' ') + ' ')
            }
            $blocks.Add([pscustomobject]@{
                StartLine = $b.StartLine
                EndLine   = $b.EndLine
                Header    = ($Lines[$b.StartLine - 1].Trim())
                Exception = $b.Exception
                Body      = $body
                BodyHash  = Get-CatchBodyHash -Body $body
                Class     = $b.Class
            })
        }
        return $blocks
    }

    $i = 0
    while ($i -lt $Lines.Count) {
        $line = $Lines[$i]
        if ($script:RxComment.IsMatch($line) -or ($line -notmatch '\bcatch\b')) { $i++; continue }
        $depth = 0; $started = $false
        $sb = New-Object System.Text.StringBuilder
        $j = $i
        while ($j -lt $Lines.Count) {
            $code = ($Lines[$j] -replace '//.*$', '')
            [void]$sb.Append($code).Append(' ')
            foreach ($ch in $code.ToCharArray()) {
                if ($ch -eq '{') { $depth++; $started = $true } elseif ($ch -eq '}') { $depth-- }
            }
            if ($started -and $depth -le 0) { break }
            $j++
            if (($j - $i) -gt 300) { break }
        }
        $body = $sb.ToString()
        $header = $Lines[$i].Trim()
        $ex = ''
        $m = [regex]::Match($header, 'catch\s*\(\s*(?<t>[^\)]*)\)')
        if ($m.Success) { $ex = $m.Groups['t'].Value.Trim() }
        $blocks.Add([pscustomobject]@{
            StartLine = $i + 1
            EndLine   = $j + 1
            Header    = $header
            Exception = $ex
            Body      = $body
            BodyHash  = Get-CatchBodyHash -Body $body
            Class     = Get-CatchOutletClass -Body $body
        })
        $i = $j + 1
    }
    return $blocks
}

<#
.SYNOPSIS
    分类器正/负对照（判据自证：防恒真/恒假）。返回失败项数（0 = 通过）。
#>
function Test-CatchOutletClassifier {
    [CmdletBinding()]
    param()
    $fail = 0
    if ((Get-CatchOutletClass -Body ' { throw; } ') -ne 'rethrow') { $fail++ }
    if ((Get-CatchOutletClass -Body ' { handle(ex, Scope); } ') -ne 'handle') { $fail++ }
    if ((Get-CatchOutletClass -Body ' { FailureBudget.fatal++; } ') -ne 'budget') { $fail++ }
    if ((Get-CatchOutletClass -Body ' { Degrade("x"); } ') -ne 'degrade') { $fail++ }
    if ((Get-CatchOutletClass -Body ' { AgentLog.Log("a", new { }); } ') -ne 'candidate-logonly') { $fail++ }
    if ((Get-CatchOutletClass -Body ' { } ') -ne 'candidate-empty') { $fail++ }
    # 负对照：空体不得被判为任意出口类（防恒真）
    if ((Get-CatchOutletClass -Body ' { } ') -eq 'rethrow') { $fail++ }
    # 块提取正/负对照
    $sample = @(
        'if (x)',
        '{',
        '    try { Do(); }',
        '    catch (Exception ex) { AgentLog.Log("x", new { }); }',
        '}',
        'var s = "catch me";'
    )
    $found = @(Get-CatchBlocks -Lines $sample)
    if ($found.Count -ne 1) { $fail++ }
    if ($found.Count -eq 1 -and $found[0].Class -ne 'candidate-logonly') { $fail++ }
    return $fail
}

Export-ModuleMember -Function Get-CatchBlocks, Get-CatchOutletClass, Get-CatchBodyHash, Test-CatchOutletClassifier, Initialize-CatchRoslyn, Get-CatchBlocksRoslyn
