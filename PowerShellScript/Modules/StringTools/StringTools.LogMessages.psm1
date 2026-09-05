<#
.SYNOPSIS
    *_log_messages.json 的 msgId 正则与文本操作模块（单一来源）

.DESCRIPTION
    承载日志消息 msgId 的源码精确解析（Roslyn）、正则回退、引用判定与块删除逻辑，
    供 Check-LogMessages.ps1 与 Remove-RedundantLogMessages.ps1 共用，
    消除两脚本在「msgId 被引用」口径上的漂移（对齐 Check-ClinerulesReferences 把文法委托给 StringTools 的范式）。

    精确解析：在 PowerShell 7（.NET Core/.NET 5+）下，进程内加载 RoslynMethod.Core
    并以 Roslyn 语法树精确定位日志调用与首参表达式类型，替代朴素字符串匹配——
      检查3 动态反模式  = InvocationExpressionSyntax 首参为 InterpolatedStringExpressionSyntax（%任意插值拼进 msgId）。
      检查4 显式调用形  = 首参为字符串字面量（LiteralExpressionSyntax / StringLiteralExpression）→ 提取 ValueText。
    PS 5.1（Desktop/.NET Framework）无法加载 net10 程序集时自动回退到正则（口径同旧版）。

    口径说明（两类意图，必须保持一致）：
      1) 动态反模式（检查3）：.Log($"...{...}...") / ConfigFileLoaderLog($"...") —— 禁止插值拼 msgId。
      2) 显式调用形（检查4，窄）：.Log("msgId") 首参字面量 —— 用于「代码引用但未注册」告警，避免误报变量/方法名。
      3) 标识符样 token（Remove 保留判定，宽）：引号内 类_方法_ 形态 —— 用于「被代码引用则保留」，宁可宽不可误删。

    依赖：默认无；启用精确解析需环境含 RoslynMethod.Core/ Microsoft.CodeAnalysis 程序集
    （可用 $env:RMC_DLL_DIR 指定其所在目录；PowerShell 7 下自动探测仓库内 BuildOutput）。
    PS 5.1 下仅使用 .NET 正则与字符串 API，独立可加载。

.NOTES
    注册：Import-Modules.ps1 的 $functionModuleMap 已登记本级函数；脚本以 `# @requires` 按需加载。
    新增内部函数（非导出）：Find-RoslynLogAnalyzerType（懒加载缓存 Roslyn 解析器类型）。
#>

# ============================================
#  msgId 正则会（单一来源：以上三类口径集中定义）
# ============================================
$script:MsgIdRe = @{
    # 动态反模式：X.Log($"<tt_ident>{插值}..."）—— <tt_ident> 即标识符风格前缀，紧跟 {..} 插值
    DynamicCall   = '\.Log\(\s*\$"[A-Za-z_][A-Za-z0-9_]*\{[^}]*\}[^"]*"'
    DynamicLoader = 'ConfigFileLoaderLog\(\s*\$"[A-Za-z_][A-Za-z0-9_]*\{[^}]*\}[^"]*"'
    # 显式调用形：.Log("msgId"（检查4 用；捕获组1 = msgId）
    LiteralCall   = '\.Log\(\s*"([A-Za-z0-9_]+)"'
    # 标识符样 token：引号内「分段大写标识符」（Remove 保留判定用；捕获组1 = 候选 msgId）
    IdentLike     = '["]((?:[A-Z][A-Za-z0-9_]*_)+[A-Z][A-Za-z0-9_]*)["]'
}

# ============================================
#  Roslyn 精确解析（懒加载；PS 5.1 自动回退正则）
# ============================================
$script:logAnalyzerType = $null            # 解析器类型（加载成功后缓存）
$script:logAnalyzerReady = -1              # -1 未尝试 / 0 不可用 / 1 可用
$script:logAnalyzerDllDir = ''             # 解析用程序集目录

<#
.SYNOPSIS
    探测 Roslyn 程序集目录（优先级：环境变量 RMC_DLL_DIR > 仓库内常见 BuildOutput）
#>
function Find-RoslynLogAnalyzerDllDir {
    if ($script:logAnalyzerDllDir) { return $script:logAnalyzerDllDir }
    $candidates = @()
    if ($env:RMC_DLL_DIR -and (Test-Path $env:RMC_DLL_DIR)) { $candidates += $env:RMC_DLL_DIR }
    # 从脚本所在模块向上定位仓库根（含 CsharpPrj 的父级）
    $p = $PSScriptRoot
    while ($p -and -not (Test-Path (Join-Path $p 'CsharpPrj'))) { $p = Split-Path $p -Parent }
    if ($p) {
        $patterns = @(
            (Join-Path $p 'CentralTest\BuildOutput\Debug\net10.0-windows'),
            (Join-Path $p 'Projects\Agent\BuildOutput\Debug'),
            (Join-Path $p 'CsharpPrj\BuildOutput'),
            (Join-Path $p 'Projects\Engine\BuildOutput\Debug')
        )
        $candidates += $patterns
    }
    foreach ($d in $candidates) {
        if ($d -and (Test-Path $d) -and (Test-Path (Join-Path $d 'RoslynMethod.Core.dll')) -and
            (Test-Path (Join-Path $d 'Microsoft.CodeAnalysis.dll')) -and
            (Test-Path (Join-Path $d 'Microsoft.CodeAnalysis.CSharp.dll'))) {
            $script:logAnalyzerDllDir = $d
            return $d
        }
    }
    return ''
}

<#
.SYNOPSIS
    懒加载并缓存 Roslyn 日志解析器类型；不可用时返回 $null（调用方回退正则）。
.DESCRIPTION
    - 仅在 PSEdition=Core（PowerShell 7/.NET）时尝试；PS5.1(.NET Framework) 直接返回 $null。
    - 依次 Add-Type 引用 RoslynMethod.Core / Microsoft.CodeAnalysis / Microsoft.CodeAnalysis.CSharp，
      再内联编译一个 C# 静态类 LogMsgAnalyzer，其 FindLogCalls(code, file) 返回精确命中（含行号）。
    - 首次调用做完整尝试并缓存结果；任何失败置为不可用，避免反复抛错。
#>
function Find-RoslynLogAnalyzerType {
    if ($script:logAnalyzerReady -ge 0) {
        if ($script:logAnalyzerReady -eq 1) { return $script:logAnalyzerType }
        return $null
    }
    $script:logAnalyzerReady = 0   # 先置不可用，防止递归/反复
    try {
        if ($PSVersionTable.PSEdition -ne 'Core') { return $null }
        $dir = Find-RoslynLogAnalyzerDllDir
        if (-not $dir) { return $null }
        $core = Join-Path $dir 'RoslynMethod.Core.dll'
        $mca  = Join-Path $dir 'Microsoft.CodeAnalysis.dll'
        $mcca = Join-Path $dir 'Microsoft.CodeAnalysis.CSharp.dll'
        # 先解析引用，保证内联编译返回的类型运行时能解析到依赖（顺序：先 Compilation，再 CSharp，最后 Core）
        foreach ($a in @($mca, $mcca, $core)) {
            [void][System.Reflection.Assembly]::LoadFrom($a)
        }

        $src = @'
using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynSyntaxUtilities.Base;

public sealed class LogCallHit
{
    public string Kind;    // dynamic | literal
    public string File;
    public string Value;   // literal: 解码后的 msgId；dynamic: 标注用（可为空）
    public int Line;       // 1-based
    public int Column;     // 1-based
}

public static class LogMsgAnalyzer
{
    // 判定成员/直接调用是否属于日志门面（接收者或方法名按 Log 结尾，或直接被 ConfigFileLoaderLog 调用）
    private static bool IsLogReceiver(string receiverName)
    {
        if (string.IsNullOrEmpty(receiverName)) return false;
        // 直接静态调用 ConfigFileLoaderLog(...)
        if (receiverName == "ConfigFileLoaderLog") return true;
        // FsLog / UtilitiesLog / ModuleLog ... 与 generic .Log / .ConfigFileLoaderLog 调用
        return receiverName.EndsWith("Log", StringComparison.Ordinal)
            || receiverName == "Log"
            || receiverName == "ConfigFileLoaderLog";
    }

    public static List<LogCallHit> FindLogCalls(string code, string file)
    {
        var result = new List<LogCallHit>();
        if (string.IsNullOrWhiteSpace(code)) return result;

        if (!RoslynAnalyzer.TryInitialize(code, out var ctx) || ctx == null)
            return result;

        foreach (var node in ctx.Root.DescendantNodes())
        {
            if (node is not InvocationExpressionSyntax inv) continue;

            // 提取调用名（成员访问的右端名，或直接标识符）
            string callName = null;
            string receiver = null;
            var expr = inv.Expression;
            if (expr is MemberAccessExpressionSyntax ma)
            {
                receiver = ma.Expression.ToString();
                callName = ma.Name.Identifier.ValueText;
            }
            else if (expr is IdentifierNameSyntax iName)
            {
                receiver = iName.Identifier.ValueText;
                callName = iName.Identifier.ValueText;
            }
            else if (expr is GenericNameSyntax g)
            {
                callName = g.Identifier.ValueText;
                receiver = inv.Expression.ToString();
            }
            if (string.IsNullOrEmpty(callName)) continue;
            if (!IsLogReceiver(receiver)) continue;

            var args = inv.ArgumentList;
            if (args.Arguments.Count == 0) continue;
            var firstArg = args.Arguments[0];
            var pos = inv.GetLocation().GetLineSpan().StartLinePosition;
            var line = pos.Line + 1;
            var column = pos.Character + 1;

            // 首参表达式类型分流
            var litSpan = firstArg.GetLocation().GetLineSpan();
            var aLine = litSpan.StartLinePosition.Line + 1;
            var aCol = litSpan.StartLinePosition.Character + 1;

            if (firstArg.Expression is InterpolatedStringExpressionSyntax)
            {
                result.Add(new LogCallHit { Kind = "dynamic", File = file, Line = aLine, Column = aCol });
            }
            else if (firstArg.Expression is LiteralExpressionSyntax lit &&
                     lit.IsKind(SyntaxKind.StringLiteralExpression) &&
                     lit.Token.ValueText.Length > 0)
            {
                result.Add(new LogCallHit { Kind = "literal", File = file, Value = lit.Token.ValueText, Line = aLine, Column = aCol });
            }
        }
        return result;
    }
}
'@
        $refs = [string[]]@($mca, $mcca, $core)
        $type = Add-Type -TypeDefinition $src -ReferencedAssemblies $refs -ErrorAction Stop
        $script:logAnalyzerType = $type
        $script:logAnalyzerReady = 1
        return $type
    }
    catch {
        $script:logAnalyzerReady = 0
        return $null
    }
}

# ============================================
#  动态 msgId（检查3）
# ============================================
<#
.SYNOPSIS
    提取单份 C# 文本中的动态 msgId 反模式（字符串插值拼进 msgId）
.DESCRIPTION
    优先 Roslyn 精确解析（首参为 InterpolatedStringExpressionSyntax），
    PS5.1/不可用时回退正则（旧口径）。输出 { File, Text, Line, Column }。
#>
function Get-MsgIdDynamicMatches {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [AllowEmptyString()][string]$Text,

        [Parameter(Position = 1)]
        [string]$File = ''
    )
    $found = @()
    $ana = Find-RoslynLogAnalyzerType
    if ($ana) {
        $hits = $ana::FindLogCalls($Text, $File)
        foreach ($h in $hits) {
            if ($h.Kind -eq 'dynamic') {
                $found += [pscustomobject]@{ File = $File; Text = '动态 msgId'; Line = $h.Line; Column = $h.Column }
            }
        }
        return $found
    }
    foreach ($pat in @($script:MsgIdRe.DynamicCall, $script:MsgIdRe.DynamicLoader)) {
        foreach ($m in [regex]::Matches($Text, $pat)) {
            $found += [pscustomobject]@{ File = $File; Text = $m.Value; Line = 0; Column = 0 }
        }
    }
    return $found
}

# ============================================
#  显式调用形 msgId（检查4）
# ============================================
<#
.SYNOPSIS
    提取单份 C# 文本中的显式调用字面量 msgId（.Log("msgId" 首参）
.DESCRIPTION
    优先 Roslyn 精确解析（首参为字符串字面量 LiteralExpressionSyntax，取解码值），
    PS5.1/不可用时回退正则（旧口径）。输出 { File, Id, Line, Column }。
#>
function Get-MsgIdLiteralCallMatches {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [AllowEmptyString()][string]$Text,

        [Parameter(Position = 1)]
        [string]$File = ''
    )
    $found = @()
    $ana = Find-RoslynLogAnalyzerType
    if ($ana) {
        $hits = $ana::FindLogCalls($Text, $File)
        foreach ($h in $hits) {
            if ($h.Kind -eq 'literal' -and $h.Value) {
                $found += [pscustomobject]@{ File = $File; Id = $h.Value; Line = $h.Line; Column = $h.Column }
            }
        }
        return $found
    }
    foreach ($m in [regex]::Matches($Text, $script:MsgIdRe.LiteralCall)) {
        $found += [pscustomobject]@{ File = $File; Id = $m.Groups[1].Value; Line = 0; Column = 0 }
    }
    return $found
}

# ============================================
#  源码引用索引（Remove 保留判定用）
# ============================================
<#
.SYNOPSIS
    从一组 C# 文件构建源码引用索引：全文拼接 + 标识符样 msgId 候选集
.DESCRIPTION
    同时返回 { Blob, Ids }：
      Blob = 全部 C# 文本拼接（供 Contains('"'+cand+'"') 精确命中）
      Ids  = 正则命中的标识符样 msgId 候选集（供快速 Contains 判定）
#>
function Get-MsgIdSourceIndex {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [AllowNull()][System.IO.FileInfo[]]$Files
    )
    $blob = [System.Text.StringBuilder]::new()
    $ids  = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($f in $Files) {
        $text = Get-Content -LiteralPath $f.FullName -Raw -ErrorAction SilentlyContinue
        if ($null -eq $text) { $text = '' }
        [void]$blob.Append($text).Append("`n")
        foreach ($m in [regex]::Matches($text, $script:MsgIdRe.IdentLike)) {
            [void]$ids.Add($m.Groups[1].Value)
        }
    }
    return [pscustomobject]@{
        Blob = $blob.ToString()
        Ids  = $ids
    }
}

# ============================================
#  keyPrefix 感知的引用判定
# ============================================
<#
.SYNOPSIS
    对单个 msgId 生成候选键：原键 / 去任一前缀 / 加任一前缀
.DESCRIPTION
    日志基础设施按 keyPrefix（如 fde:/exec:/fs:）拆分调用，JSON 键名可能是
    「keyPrefix+msgId」或「无前缀 msgId」两种形态，而代码只写无前缀 msgId。
    任一候选命中源码即视为被引用，避免误删带前缀键。
#>
function Get-MsgIdCandidates {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string]$Id,

        [Parameter(Position = 1)]
        [string[]]$Prefixes
    )
    $set = [System.Collections.Generic.HashSet[string]]::new()
    [void]$set.Add($Id)
    foreach ($p in $Prefixes) {
        if ([string]::IsNullOrWhiteSpace($p)) { continue }
        if ($Id.StartsWith($p) -and $Id.Length -gt $p.Length) {
            [void]$set.Add($Id.Substring($p.Length))     # 去掉前缀（匹配 fde:X 形式）
        } elseif (-not $Id.StartsWith($p)) {
            [void]$set.Add($p + $Id)                     # 加上前缀（匹配代码含前缀的情形）
        }
    }
    return @($set)
}

<#
.SYNOPSIS
    判定一个 msgId 是否被源码引用（任一候选以「带引号完整串」出现在源码即 true）
#>
function Test-MsgIdReferenced {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string]$Id,

        [Parameter(Mandatory = $true, Position = 1)]
        [string]$AllCsText,

        [Parameter(Position = 2)]
        [string[]]$Prefixes,

        [Parameter(Position = 3)]
        $ReferencedSet
    )
    foreach ($cand in (Get-MsgIdCandidates -Id $Id -Prefixes $Prefixes)) {
        if ($AllCsText.Contains('"' + $cand + '"')) { return $true }
        if ($ReferencedSet -and $ReferencedSet.Contains($cand)) { return $true }
    }
    return $false
}

# ============================================
#  keyPrefix 白名单索引（Check-5 / Remove 共用，单一来源）
# ============================================
<#
.SYNOPSIS
    扫描 framework.config.json 收集日志 keyPrefix 白名单与「JSON 文件 → keyPrefix」映射
.DESCRIPTION
    日志基础设施按 keyPrefix（如 agent:/fde:/exec:）拆分调用，JSON 键名可能为「keyPrefix+msgId」
    或无前缀 msgId，而代码只写无前缀 msgId。引用/孤儿判定需按文件构造候选，避免误判带前缀键。
    返回 { All = HashSet[全库 keyPrefix]，ByFile = @{ <*_log_messages.json 绝对路径> = HashSet[keyPrefix] } }。
    未命中映射的文件回退用 All（与 Remove-RedundantLogMessages.ps1 既有口径一致）。
#>
function Get-MsgIdKeyPrefixIndex {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string]$Root,

        [Parameter(Position = 1)]
        [string[]]$ConfigFiles
    )
    $all    = [System.Collections.Generic.HashSet[string]]::new()
    $byFile = @{}
    foreach ($cfg in $ConfigFiles) {
        if ([string]::IsNullOrWhiteSpace($cfg)) { continue }
        try { $cfgJson = Get-Content -LiteralPath $cfg -Raw | ConvertFrom-Json } catch { continue }
        if ($null -eq $cfgJson -or $null -eq $cfgJson.logging -or $null -eq $cfgJson.logging.modules) { continue }
        foreach ($m in $cfgJson.logging.modules) {
            if ($null -eq $m -or [string]::IsNullOrWhiteSpace($m.keyPrefix)) { continue }
            [void]$all.Add($m.keyPrefix)
            if ([string]::IsNullOrWhiteSpace($m.path)) { continue }
            try {
                $rel = ([string]$m.path).TrimStart('\\', '/')
                $abs = if ([System.IO.Path]::IsPathRooted($rel)) { $rel } else { Join-Path $Root $rel }
                $full = [System.IO.Path]::GetFullPath($abs)
                if (-not $byFile.ContainsKey($full)) { $byFile[$full] = [System.Collections.Generic.HashSet[string]]::new() }
                [void]$byFile[$full].Add($m.keyPrefix)
            } catch { }
        }
    }
    return [pscustomobject]@{ All = $all; ByFile = $byFile }
}


# ============================================
#  文本级块删除
# ============================================
<#
.SYNOPSIS
    从 JSON 原文中删除一组 msgId 对象块，保留原格式/缩进/模板
.DESCRIPTION
    通过「字符串感知的括号计数」定位每个 msgId 对象的精确边界后删除，
    保留原文件格式（不依赖统一缩进、不重序列化）。模板中的 {param} 在字符串内不计。
#>
function Remove-MsgBlock {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string]$Text,

        [Parameter(Mandatory = $true, Position = 1)]
        [string[]]$IdList
    )
    foreach ($id in $IdList) {
        $needle = '"' + $id + '":'
        $si = $Text.IndexOf($needle, [System.StringComparison]::Ordinal)
        if ($si -lt 0) { continue }

        # 块起始 = 含 key 的行首
        $ls = $si
        while ($ls -gt 0 -and $Text[$ls - 1] -ne "`n" -and $Text[$ls - 1] -ne "`r") { $ls-- }

        # 从 needle 后找首个「结构级」'{'（跳过字符串内内容）
        $i = $si; $inStr = $false; $open = -1
        while ($i -lt $Text.Length) {
            $c = $Text[$i]
            if ($inStr) {
                if ($c -eq '\') { $i += 2; continue }
                if ($c -eq '"') { $inStr = $false }
            } else {
                if ($c -eq '"') { $inStr = $true }
                elseif ($c -eq '{') { $open = $i; break }
            }
            $i++
        }

        # 括号计数到匹配的 '}'（模板中的 {param} 在字符串内，不计）
        $depth = 1; $i = $open + 1; $inStr = $false; $close = $open
        while ($i -lt $Text.Length) {
            $c = $Text[$i]
            if ($inStr) {
                if ($c -eq '\') { $i += 2; continue }
                if ($c -eq '"') { $inStr = $false }
            } else {
                if ($c -eq '"') { $inStr = $true }
                elseif ($c -eq '{') { $depth++ }
                elseif ($c -eq '}') {
                    $depth--
                    if ($depth -eq 0) { $close = $i; break }
                }
            }
            $i++
        }

        # 块终止符：可选 ','，再吞掉行尾换行
        $k = $close + 1
        while ($k -lt $Text.Length -and ($Text[$k] -eq ' ' -or $Text[$k] -eq "`t")) { $k++ }
        if ($k -lt $Text.Length -and $Text[$k] -eq ',') { $k++ }
        $e = $k
        while ($e -lt $Text.Length -and ($Text[$e] -eq "`r" -or $Text[$e] -eq "`n")) { $e++ }
        $blockEnd = $e

        # 删除块
        $Text = $Text.Substring(0, $ls) + $Text.Substring($blockEnd)

        # 若该块是对象末项（其后紧跟 '}'），移除前一成员的尾随逗号
        $nx = $ls
        while ($nx -lt $Text.Length -and ($Text[$nx] -eq ' ' -or $Text[$nx] -eq "`t")) { $nx++ }
        if ($nx -lt $Text.Length -and $Text[$nx] -eq '}') {
            $b = $ls - 1
            while ($b -gt 0 -and ($Text[$b] -eq ' ' -or $Text[$b] -eq "`t" -or $Text[$b] -eq "`r" -or $Text[$b] -eq "`n")) { $b-- }
            if ($b -ge 0 -and $Text[$b] -eq ',') { $Text = $Text.Substring(0, $b) + $Text.Substring($b + 1) }
        }
    }
    return $Text
}

# ============================================
#  导出公共函数
# ============================================
Export-ModuleMember -Function Get-MsgIdDynamicMatches,
                              Get-MsgIdLiteralCallMatches,
                              Get-MsgIdSourceIndex,
                              Get-MsgIdCandidates,
                              Get-MsgIdKeyPrefixIndex,
                              Test-MsgIdReferenced,
                              Remove-MsgBlock