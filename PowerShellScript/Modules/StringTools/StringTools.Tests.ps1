# StringTools.Tests.ps1 - StringTools 模块 Pester 测试
# 运行方式: Invoke-Pester .\StringTools.Tests.ps1

BeforeAll {
    # 加载被测试模块
    $modulePath = Join-Path $PSScriptRoot "StringTools.Core.psm1"
    Import-Module $modulePath -Force -ErrorAction Stop

    $batchModulePath = Join-Path $PSScriptRoot "StringTools.FileBatch.psm1"
    Import-Module $batchModulePath -Force -ErrorAction Stop

    $structuredModulePath = Join-Path $PSScriptRoot "StringTools.StructuredText.psm1"
    Import-Module $structuredModulePath -Force -ErrorAction Stop

    $markdownModulePath = Join-Path $PSScriptRoot "StringTools.Markdown.psm1"
    $clinerulesModulePath = Join-Path $PSScriptRoot 'StringTools.Clinerules.psm1'
    Import-Module $clinerulesModulePath -Force -ErrorAction Stop
    Import-Module $markdownModulePath -Force -ErrorAction Stop

    # FileBatch.Backup-FileWithTimestamp 委托 Core 的 Backup-File；生产路径由 Import-Modules.ps1 全量加载，此处补齐依赖
    $coreHelpersPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'Core\FileSystemHelpers.psm1'
    if (Test-Path -LiteralPath $coreHelpersPath) { Import-Module $coreHelpersPath -Force -DisableNameChecking -ErrorAction Stop }
}

# ============================================
#  StringTools.Core 测试
# ============================================

Describe 'Invoke-SimpleReplace' {
    It '应执行简单文本替换' {
        $result = Invoke-SimpleReplace -InputObject "Hello World" -OldValue "World" -NewValue "PowerShell"
        $result | Should -Be "Hello PowerShell"
    }

    It '应支持管道输入' {
        $result = "Hello World" | Invoke-SimpleReplace -OldValue "World" -NewValue "PowerShell"
        $result | Should -Be "Hello PowerShell"
    }

    It '应支持多规则替换' {
        $result = Invoke-SimpleReplace -InputObject "A B C" -Rules @(
            @{ Old = "A"; New = "X" },
            @{ Old = "B"; New = "Y" }
        )
        $result | Should -Be "X Y C"
    }

    It '应区分大小写' {
        $result = Invoke-SimpleReplace -InputObject "Hello hello" -OldValue "hello" -NewValue "Hi" -CaseSensitive
        $result | Should -Be "Hello Hi"
    }
}

Describe 'Invoke-RegexExtract' {
    It '应提取正则匹配内容' {
        $result = Invoke-RegexExtract -InputObject "Version 1.0.0 and 2.0.0" -Pattern "(\d+\.\d+\.\d+)"
        $result.Count | Should -Be 2
        $result[0] | Should -Be "1.0.0"
        $result[1] | Should -Be "2.0.0"
    }

    It '应支持命名捕获组' {
        $result = Invoke-RegexExtract -InputObject "<Version>1.0.0</Version>" -Pattern "<Version>(?<ver>.*?)</Version>" -GroupName "ver"
        $result | Should -Be "1.0.0"
    }

    It '应支持索引捕获组' {
        $result = Invoke-RegexExtract -InputObject "key=value" -Pattern "(\w+)=(\w+)" -GroupName 2
        $result | Should -Be "value"
    }

    It '应支持只返回第一个匹配' {
        $result = Invoke-RegexExtract -InputObject "A1 B2 C3" -Pattern "\w\d" -FirstMatch
        $result | Should -Be "A1"
    }
}

Describe 'Invoke-RegexExtractToHashtable' {
    It '应将命名捕获组转为哈希表' {
        $result = Invoke-RegexExtractToHashtable -InputObject "name=John;age=30" -Pattern "(?<key>\w+)=(?<value>\w+)"
        $result.key | Should -Be "name"
        $result.value | Should -Be "John"
    }
}

Describe 'Invoke-RegexReplace' {
    It '应执行正则替换' {
        $result = Invoke-RegexReplace -InputObject "Hello 123 World" -Pattern "\d+" -Replacement "NUM"
        $result | Should -Be "Hello NUM World"
    }

    It '应支持 ScriptBlock 回调替换' {
        $result = Invoke-RegexReplace -InputObject "a=1 b=2" -Pattern "(\w+)=(\w+)" -ScriptBlock {
            param($match)
            return "$($match.Groups[2].Value)=$($match.Groups[1].Value)"
        }
        $result | Should -Be "1=a 2=b"
    }

    It '应支持限制替换次数' {
        $result = Invoke-RegexReplace -InputObject "A A A A" -Pattern "A" -Replacement "B" -Count 2
        $result | Should -Be "B B A A"
    }
}

Describe 'Invoke-BracketAwareSplit' {
    It '应跳过括号内的分隔符' {
        $result = Invoke-BracketAwareSplit -InputObject "a, b, {c, d}, e"
        $result.Count | Should -Be 4
        $result[0] | Should -Be "a"
        $result[2] | Should -Be "{c, d}"
    }

    It '应跳过引号内的分隔符' {
        $result = Invoke-BracketAwareSplit -InputObject "a, 'b, c', d"
        $result.Count | Should -Be 3
        $result[1] | Should -Be "'b, c'"
    }

    It '应处理空条目' {
        $result = Invoke-BracketAwareSplit -InputObject "a,,b" -RemoveEmptyEntries
        $result.Count | Should -Be 2
    }
}

Describe 'Invoke-BracketAwareReplace' {
    It '应只替换括号外的内容' {
        $result = Invoke-BracketAwareReplace -InputObject "a, {b, c}" -Pattern "," -Replacement ";"
        $result | Should -Be "a; {b, c}"
    }
}

Describe 'Invoke-RemoveInsideSymbols' {
    It '应移除花括号内的内容' {
        $result = Invoke-RemoveInsideSymbols -InputObject "a {b c} d" -OpenSymbol "{" -CloseSymbol "}"
        $result | Should -Be "a  d"
    }

    It '应保留符号（不包含符号时）' {
        $result = Invoke-RemoveInsideSymbols -InputObject "a {b} d" -OpenSymbol "{" -CloseSymbol "}" -IncludeSymbols:$false
        $result | Should -Be "a  d"
    }
}

Describe 'Convert-NameCase' {
    It '应将 snake_case 转为 PascalCase' {
        $result = Convert-NameCase -InputObject "hello_world" -ToCase "Pascal"
        $result | Should -Be "HelloWorld"
    }

    It '应将 PascalCase 转为 snake_case' {
        $result = Convert-NameCase -InputObject "HelloWorld" -ToCase "Snake"
        $result | Should -Be "hello_world"
    }

    It '应将 PascalCase 转为 camelCase' {
        $result = Convert-NameCase -InputObject "HelloWorld" -ToCase "Camel"
        $result | Should -Be "helloWorld"
    }

    It '应将 PascalCase 转为 kebab-case' {
        $result = Convert-NameCase -InputObject "HelloWorld" -ToCase "Kebab"
        $result | Should -Be "hello-world"
    }

    It '应自动检测输入格式' {
        $result = Convert-NameCase -InputObject "my_variable_name" -ToCase "Pascal"
        $result | Should -Be "MyVariableName"
    }
}

Describe 'Invoke-ReplacePlaceholder' {
    It '应替换简单占位符' {
        $result = Invoke-ReplacePlaceholder -InputObject "Hello {name}!" -Parameters @{ name = "World" }
        $result | Should -Be "Hello World!"
    }

    It '应支持默认值' {
        $result = Invoke-ReplacePlaceholder -InputObject "{name|Guest}!" -Parameters @{}
        $result | Should -Be "Guest!"
    }

    It '应支持多个占位符' {
        $result = Invoke-ReplacePlaceholder -InputObject "{a} {b}" -Parameters @{ a = "Hello"; b = "World" }
        $result | Should -Be "Hello World"
    }
}

Describe 'Invoke-FilterLines' {
    It '应跳过指定前缀的行' {
        $content = "using System;`r`nnamespace Test`r`n{`r`n// comment`r`nclass Foo {}`r`n}"
        $result = Invoke-FilterLines -InputObject $content -SkipLines @("using ", "// ")
        $result.Count | Should -Be 4
        $result[0] | Should -Be "namespace Test"
        $result[1] | Should -Be "{"
        $result[2] | Should -Be "class Foo {}"
        $result[3] | Should -Be "}"
    }

    It '应支持正则模式过滤' {
        $content = "line1`n#comment`nline2"
        $result = Invoke-FilterLines -InputObject $content -SkipPattern "^#"
        $result.Count | Should -Be 2
        $result[0] | Should -Be "line1"
        $result[1] | Should -Be "line2"
    }
}

Describe 'Invoke-TemplateReplace' {
    It '应替换占位符' {
        $result = Invoke-TemplateReplace -InputObject "Hello @@NAME@@" -Parameters @{ NAME = "World" }
        $result | Should -Be "Hello World"
    }

    It '应支持多个占位符' {
        $result = Invoke-TemplateReplace -InputObject "@@A@@-@@B@@" -Parameters @{ A = "1"; B = "2" }
        $result | Should -Be "1-2"
    }

    It '未提供的占位符应保持原样' {
        $result = Invoke-TemplateReplace -InputObject "Value: @@KNOWN@@ / @@UNKNOWN@@" -Parameters @{ KNOWN = "ok" }
        $result | Should -Be "Value: ok / @@UNKNOWN@@"
    }

    It '应支持自定义前后缀' {
        $result = Invoke-TemplateReplace -InputObject "Hi {name}" -Parameters @{ name = "Bob" } -Prefix '{' -Suffix '}'
        $result | Should -Be "Hi Bob"
    }
}

Describe 'ConvertTo-CompoundKey' {
    It '应按位置分隔符连接分段' {
        $result = ConvertTo-CompoundKey -Segments @('axioms','AX','StateSnapshot') -Separators @(':','.')
        $result | Should -Be 'axioms:AX.StateSnapshot'
    }

    It '单段应原样返回' {
        $result = ConvertTo-CompoundKey -Segments @('only') -Separators @(':')
        $result | Should -Be 'only'
    }

    It '分隔符不足时应复用最后一个' {
        $result = ConvertTo-CompoundKey -Segments @('a','b','c','d') -Separators @(':', '.')
        $result | Should -Be 'a:b.c.d'
    }
}

Describe 'Test-MarkdownTableLine' {
    It '应识别表格行' {
        Test-MarkdownTableLine -InputObject "| 类型 | 说明 |" | Should -Be $true
        Test-MarkdownTableLine -InputObject "   | a | b |" | Should -Be $true
    }

    It '应拒绝非表格行' {
        Test-MarkdownTableLine -InputObject "# 标题" | Should -Be $false
        Test-MarkdownTableLine -InputObject "普通文本" | Should -Be $false
    }
}

Describe 'Test-CodeFenceLine' {
    It '应识别代码围栏行' {
        Test-CodeFenceLine -InputObject '```powershell' | Should -Be $true
        Test-CodeFenceLine -InputObject '~~~' | Should -Be $true
    }

    It '应拒绝非围栏行' {
        Test-CodeFenceLine -InputObject '# 标题' | Should -Be $false
        Test-CodeFenceLine -InputObject '`inline`' | Should -Be $false
    }
}

Describe 'Get-BacktickTokens' {
    It '应提取反引号包裹的 token' {
        $result = Get-BacktickTokens -InputObject '`A` 与 `B` / `C`'
        $result.Count | Should -Be 3
        $result[0] | Should -Be 'A'
        $result[1] | Should -Be 'B'
        $result[2] | Should -Be 'C'
    }

    It '应去除 token 两端空白' {
        $result = Get-BacktickTokens -InputObject '` A `'
        $result.Count | Should -Be 1
        $result[0] | Should -Be 'A'
    }

    It '无 token 时返回空数组' {
        $result = Get-BacktickTokens -InputObject 'no tokens here'
        $result.Count | Should -Be 0
    }
}

# ============================================
#  StringTools.FileBatch 测试
# ============================================

Describe 'Get-FileList' {
    It '应返回指定模式的文件列表' {
        $files = Get-FileList -Path $PSScriptRoot -Include "*.psm1"
        $files.Count | Should -BeGreaterThan 0
        ($files | Where-Object { $_.Name -match '\.psm1$' }).Count | Should -Be $files.Count
    }
}

Describe 'Backup-FileWithTimestamp' {
    It '应创建带时间戳的备份' {
        $testFile = Join-Path $TestDrive "test.txt"
        "test content" | Set-Content -Path $testFile

        $backupPath = Backup-FileWithTimestamp -FilePath $testFile
        $backupPath | Should -Not -Be $null
        Test-Path $backupPath | Should -Be $true
    }
}

# ============================================
#  StringTools.StructuredText 测试
# ============================================

Describe 'ConvertFrom-KeyValueText' {
    It '应解析键值对文本' {
        $text = "name=John`nage=30`n# comment"
        $result = ConvertFrom-KeyValueText -InputObject $text
        $result["name"] | Should -Be "John"
        $result["age"] | Should -Be "30"
    }

    It '应支持自定义分隔符' {
        $text = "name:John`nage:30"
        $result = ConvertFrom-KeyValueText -InputObject $text -Delimiter ":"
        $result["name"] | Should -Be "John"
    }

    It '应跳过注释行' {
        $text = "# comment`nkey=value"
        $result = ConvertFrom-KeyValueText -InputObject $text
        $result.Count | Should -Be 1
    }
}

Describe 'ConvertFrom-DotnetListOutput' {
    It '应解析 dotnet list 输出格式' {
        $sampleOutput = @'
Project `TestProject`
> Top-level Package      Requested   Resolved
> --------------         ---------   --------
> Newtonsoft.Json        13.0.1      13.0.3
'@
        $result = ConvertFrom-DotnetListOutput -InputObject $sampleOutput
        $result.Count | Should -Be 1
        $result[0].ProjectName | Should -Be "TestProject"
        $result[0].PackageName | Should -Be "Newtonsoft.Json"
    }
}

Write-Host "`n所有测试完成！" -ForegroundColor Green
  
  
Describe 'Test-ValidIdentifier' {  
    It '应识别合法标识符' {  
        Test-ValidIdentifier -Text 'StateSnapshot' | Should -Be $true  
        Test-ValidIdentifier -Text 'op.needsSync' -AllowDot | Should -Be $true  
    }  
  
    It '应拒绝数字开头与含点' {  
        Test-ValidIdentifier -Text '123abc' | Should -Be $false  
        Test-ValidIdentifier -Text 'a.b' | Should -Be $false  
    }  
}  
  
Describe 'Get-NameHead' {  
    It '应取点链首段' {  
        Get-NameHead -Name 'AX.StateSnapshot.deterministic' | Should -Be 'AX'  
        Get-NameHead -Name 'TH-1' | Should -Be 'TH-1'  
    }  
}  
  
Describe 'Get-FirstMatchingPrefix' {  
    It '应长前缀优先' {  
        Get-FirstMatchingPrefix -Text '[AX-CORE] x' -Prefixes @('[AX]','[AX-CORE]') | Should -Be '[AX-CORE]'  
    }  
  
    It '无匹配应返回 $null' {  
        Get-FirstMatchingPrefix -Text 'plain' -Prefixes @('[AX]') | Should -Be $null  
    }  
}  
  
Describe 'Get-MarkdownFirstCell' {  
    It '应提取首单元格' {  
        Get-MarkdownFirstCell -Line '| A | B |' | Should -Be 'A'  
    }  
  
    It '非表格行返回 $null' {  
        Get-MarkdownFirstCell -Line '# 标题' | Should -Be $null  
    }  
}  
  
Describe 'Test-MarkdownHeadingLine' {  
    It '应识别标题行' {  
        Test-MarkdownHeadingLine -Line '## 一、' | Should -Be $true  
        Test-MarkdownHeadingLine -Line '### 1.1' -MinLevel 2 | Should -Be $true  
    }  
  
    It '应拒绝普通行' {  
        Test-MarkdownHeadingLine -Line '普通文本' | Should -Be $false  
    }  
}  
  
Describe 'Test-MarkdownSectionHeader' {  
    It '应匹配指定节号' {  
        Test-MarkdownSectionHeader -Line '### 4.4 注册库' -Number '4.4' | Should -Be $true  
        Test-MarkdownSectionHeader -Line '### 4.5 x' -Number '4.4' | Should -Be $false  
    }  
}  
  
Describe 'Resolve-DefinitionForm' {  
    It '应解析 AX 公理行' {  
        $d = Resolve-DefinitionForm -Line '[AX] StateSnapshot: x'  
        $d.Styp | Should -Be 'AX'  
        $d.Name | Should -Be 'StateSnapshot'  
    }  
  
    It '应解析 TH-n 与 Param' {  
        $d = Resolve-DefinitionForm -Line '## TH-1: 跨域'  
        $d.Styp | Should -Be 'TH'  
        $d.Intent | Should -Be 'deriv'  
        $d = Resolve-DefinitionForm -Line '% MAX_PROGRESS=int:3'  
        $d.Name | Should -Be 'MAX_PROGRESS'  
    }  
  
    It '表格行与非定义行返回 $null' {  
        Resolve-DefinitionForm -Line '| 类型 | 说明 |' | Should -Be $null  
        Resolve-DefinitionForm -Line '普通文本' | Should -Be $null  
    }  
}  
  
Describe 'Get-CrReferenceTokens' {  
    It '应提取多个引用' {  
        $t = Get-CrReferenceTokens -Line '见 [CR: axioms:AX.StateSnapshot] 与 [CR axioms.md:X]'  
        $t.Count | Should -Be 2  
        $t[0].Inner | Should -Be 'axioms:AX.StateSnapshot'  
    }  
}  
  
Describe 'ConvertFrom-CrReference' {  
    It '应解析严格形态' {  
        $c = ConvertFrom-CrReference -Token 'axioms:AX.StateSnapshot@consume'  
        $c.Lib | Should -Be 'axioms'  
        $c.Name | Should -Be 'StateSnapshot'  
        $c.Intent | Should -Be 'consume'  
    }  
  
    It '应支持含点 Name 与 Sec 章节' {  
        $c = ConvertFrom-CrReference -Token 'axioms:Def.op.needsSync'  
        $c.Name | Should -Be 'op.needsSync'  
        $c = ConvertFrom-CrReference -Token 'dsl:Sec.4.5@see'  
        $c.Styp | Should -Be 'Sec'  
    }  
  
    It '旧形态应返回 $null' {  
        ConvertFrom-CrReference -Token 'axioms.md:AttentionWeighting' | Should -Be $null  
    }  
}  
  
Describe 'ConvertFrom-LegacyCrReference' {  
    It '应解析旧形态' {  
        $l = ConvertFrom-LegacyCrReference -Token 'axioms.md:AttentionWeighting'  
        $l.Lib | Should -Be 'axioms'  
        $l.Name | Should -Be 'AttentionWeighting'  
    }  
}  

# ============================================
#  StringTools.LogMessages 测试（IdentLike 正则 / Get-MsgIdSourceIndex）
# ============================================
Describe 'StringTools.LogMessages.Get-MsgIdSourceIndex' {
    BeforeAll {
        $__lmPath = Join-Path $PSScriptRoot 'StringTools.LogMessages.psm1'
        Import-Module $__lmPath -Force -DisableNameChecking -ErrorAction Stop
        $script:__lmTmp = [System.IO.Path]::GetTempFileName()
    }
    AfterAll {
        if ($script:__lmTmp -and (Test-Path -LiteralPath $script:__lmTmp)) { Remove-Item -LiteralPath $script:__lmTmp -Force }
    }

    It 'IdentLike 正则配平（Get-MsgIdSourceIndex 不抛 "Too many )s"）' {
        $code = 'AgentLog.Log("WinFormApplier_Apply_start"); ConfigFileLoaderLog("Foo_Bar_baz");'
        Set-Content -LiteralPath $script:__lmTmp -Value $code -Encoding UTF8
        { $null = Get-MsgIdSourceIndex -Files ([System.IO.FileInfo]$script:__lmTmp) } | Should -Not -Throw
    }

    It '应提取分段标识符样候选 msgId（下划线分割 ≥2 段）' {
        $code = 'AgentLog.Log("WinFormApplier_Apply_start"); AgentLog.Log("Foo_Bar_baz");'
        Set-Content -LiteralPath $script:__lmTmp -Value $code -Encoding UTF8
        $ids = @((Get-MsgIdSourceIndex -Files ([System.IO.FileInfo]$script:__lmTmp)).Ids)
        $ids | Should -Contain 'WinFormApplier_Apply_start'
        $ids | Should -Contain 'Foo_Bar_baz'
    }

    It '单段无下划线单词不应作为标识符样候选捕获' {
        $code = 'var s = "justAWord";'
        Set-Content -LiteralPath $script:__lmTmp -Value $code -Encoding UTF8
        $ids = @((Get-MsgIdSourceIndex -Files ([System.IO.FileInfo]$script:__lmTmp)).Ids)
        $ids | Should -Not -Contain 'justAWord'
    }
}

# ============================================
#  追加能力测试（转义感知切分 / 数值字面量 / 空白压缩 / 标识符参数化 / 行首前缀容错）
# ============================================

Describe 'Invoke-BracketAwareSplit -EscapeChar' {
    It '应跳过被转义的分隔符' {
        $result = Invoke-BracketAwareSplit -InputObject '"a\,b", c' -EscapeChar '\'
        $result.Count | Should -Be 2
        $result[0] | Should -Be '"a\,b"'
        $result[1] | Should -Be 'c'
    }

    It '被转义的引号不应终止字符串' {
        $result = Invoke-BracketAwareSplit -InputObject '"a\"b, c", d' -EscapeChar '\'
        $result.Count | Should -Be 2
        $result[0] | Should -Be '"a\"b, c"'
    }

    It '未启用转义时保持既有行为' {
        $result = Invoke-BracketAwareSplit -InputObject 'a, b, {c, d}, e'
        $result.Count | Should -Be 4
        $result[2] | Should -Be '{c, d}'
    }
}

Describe 'Test-NumericLiteral' {
    It '应识别整型/浮点/指数形式' {
        Test-NumericLiteral -Text '-12' | Should -Be $true
        Test-NumericLiteral -Text '1.5' | Should -Be $true
        Test-NumericLiteral -Text '.5' | Should -Be $true
        Test-NumericLiteral -Text '1.5e-3' | Should -Be $true
    }

    It 'IntegerOnly 应仅接受整数' {
        Test-NumericLiteral -Text '-12' -IntegerOnly | Should -Be $true
        Test-NumericLiteral -Text '1.5' -IntegerOnly | Should -Be $false
    }

    It '应拒绝非法形式' {
        Test-NumericLiteral -Text '1e' | Should -Be $false
        Test-NumericLiteral -Text '' | Should -Be $false
        Test-NumericLiteral -Text 'abc' | Should -Be $false
    }
}

Describe 'Get-CompactText' {
    It '应移除全部空白字符' {
        Get-CompactText -Text "a + ( b )`n`tc" | Should -Be 'a+(b)c'
    }

    It '$null 应返回空串' {
        Get-CompactText -Text $null | Should -Be ''
    }
}

Describe 'Test-ValidIdentifier -AllowLeadingDigit' {
    It '应接受数字开头的规格标识符（配合 -AllowDot）' {
        Test-ValidIdentifier -Text 'a1.b-c' -AllowDot -AllowLeadingDigit | Should -Be $true
        Test-ValidIdentifier -Text '1case' -AllowLeadingDigit | Should -Be $true
    }

    It '默认仍拒绝数字开头' {
        Test-ValidIdentifier -Text '1abc' | Should -Be $false
    }

    It '应接受点号（-AllowDot）与短横线' {
        Test-ValidIdentifier -Text 'op.needsSync' -AllowDot | Should -Be $true
    }
}

Describe 'Get-FirstMatchingPrefix 行首前缀' {
    It '空串应返回 $null 而不抛参数绑定错误' {
        $result = Get-FirstMatchingPrefix -Text '' -Prefixes @('then')
        $result | Should -Be $null
    }

    It '缩进行首前缀应命中' {
        Get-FirstMatchingPrefix -Text '  then path x eq 1' -Prefixes @('then') | Should -Be 'then'
    }

    It '行内出现的词不应误判为首前缀' {
        Get-FirstMatchingPrefix -Text '  title then' -Prefixes @('then') | Should -Be $null
    }
}

