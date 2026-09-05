<#
.SYNOPSIS
    PowerShell 测试框架 - 通用测试辅助模块

.DESCRIPTION
    提供 Pester 测试中通用的辅助函数：
    - 加载被测试脚本
    - 创建测试配置
    - 模拟文件系统
    - 测试输出捕获

.NOTES
    此模块需要在 Pester 5.x 环境中使用。
    通过 Import-Module 或 using module 加载。
#>

# ============================================
#  脚本加载
# ============================================

function Import-TestScript {
    <#
    .SYNOPSIS
        通过点引入（dot-source）方式加载被测试的 PowerShell 脚本

    .PARAMETER RelativePath
        相对于项目根目录的脚本路径，如 "Script\Modules\FileDriven\FileDrivenTestUtils.ps1"

    .PARAMETER ProjectRoot
        项目根目录，默认为自动检测

    .EXAMPLE
        Import-TestScript -RelativePath "Script\Modules\FileDriven\FileDrivenTestUtils.ps1"
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$RelativePath,

        [Parameter(Mandatory=$false)]
        [string]$ProjectRoot = ""
    )

    if (-not $ProjectRoot) {
        $ProjectRoot = Get-ProjectRoot
    }

    $fullPath = Join-Path $ProjectRoot $RelativePath
    if (-not (Test-Path $fullPath)) {
        throw "测试脚本不存在: $fullPath"
    }

    . $fullPath
    return $fullPath
}

function Get-ProjectRoot {
    <#
    .SYNOPSIS
        自动检测项目根目录

    .DESCRIPTION
        从当前脚本位置向上查找，直到找到 WorkFlowPrj.sln 文件

    .OUTPUTS
        string: 项目根目录路径
    #>
    $currentDir = $PSScriptRoot
    if (-not $currentDir) {
        $currentDir = Get-Location
    }

    $dir = $currentDir
    while ($dir) {
        if (Test-Path (Join-Path $dir "WorkFlowPrj.sln")) {
            return $dir
        }
        $dir = Split-Path $dir -Parent
    }

    # 如果找不到，尝试从已知位置推断
    $testPath = "d:\Project\Program\WorkFlowPrj"
    if (Test-Path $testPath) {
        return $testPath
    }

    throw "无法自动检测项目根目录，请手动指定 ProjectRoot 参数"
}

# ============================================
#  测试配置创建
# ============================================

function New-TestConfig {
    <#
    .SYNOPSIS
        创建测试用的 FileDriven 配置哈希表

    .PARAMETER ProjectRoot
        项目根目录（可选，默认自动检测）

    .PARAMETER Overrides
        要覆盖的配置项（可选）

    .EXAMPLE
        $config = New-TestConfig
        $config = New-TestConfig -Overrides @{ "ProjectRoot" = "D:\test" }
    #>
    param(
        [Parameter(Mandatory=$false)]
        [string]$ProjectRoot = "",

        [Parameter(Mandatory=$false)]
        [hashtable]$Overrides = @{}
    )

    if (-not $ProjectRoot) {
        $ProjectRoot = Get-ProjectRoot
    }

    $config = @{
        ProjectRoot       = $ProjectRoot
        FileDrivenDir     = Join-Path $ProjectRoot "FileDriven"
        ScriptsDir        = Join-Path $ProjectRoot "FileDriven\DrivenConfig\Scripts"
        Aliases           = @{
            "Output"    = Join-Path $ProjectRoot "BuildOutput\Debug"
            "Config"    = Join-Path $ProjectRoot "FileDriven\DrivenConfig"
            "Logs"      = Join-Path $ProjectRoot "BuildOutput\Logs"
        }
        BatToolConfigPath = Join-Path $ProjectRoot "BatTool\Tools\Utils\BatToolConfig.bat"
    }

    # 应用覆盖
    foreach ($key in $Overrides.Keys) {
        $config[$key] = $Overrides[$key]
    }

    return $config
}

# ============================================
#  模拟文件系统
# ============================================

function New-TestFile {
    <#
    .SYNOPSIS
        在 TestDrive 中创建测试文件

    .PARAMETER RelativePath
        相对路径（相对于 TestDrive）

    .PARAMETER Content
        文件内容

    .PARAMETER Encoding
        编码，默认为 UTF8

    .EXAMPLE
        New-TestFile -RelativePath "test.json" -Content "{ `"name`": `"test`" }"
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$RelativePath,

        [Parameter(Mandatory=$false)]
        [string]$Content = "",

        [Parameter(Mandatory=$false)]
        [string]$Encoding = "UTF8"
    )

    $fullPath = Join-Path $TestDrive $RelativePath
    $parentDir = Split-Path $fullPath -Parent

    if (-not (Test-Path $parentDir)) {
        New-Item -Path $parentDir -ItemType Directory -Force | Out-Null
    }

    if ($Content) {
        Set-Content -Path $fullPath -Value $Content -Encoding $Encoding
    } else {
        New-Item -Path $fullPath -ItemType File -Force | Out-Null
    }

    return $fullPath
}

function New-TestDirectory {
    <#
    .SYNOPSIS
        在 TestDrive 中创建测试目录

    .PARAMETER RelativePath
        相对路径（相对于 TestDrive）

    .EXAMPLE
        New-TestDirectory -RelativePath "SubDir\Nested"
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$RelativePath
    )

    $fullPath = Join-Path $TestDrive $RelativePath
    New-Item -Path $fullPath -ItemType Directory -Force | Out-Null
    return $fullPath
}

# ============================================
#  测试输出捕获
# ============================================

function Invoke-WithOutputCapture {
    <#
    .SYNOPSIS
        执行脚本块并捕获所有输出流

    .PARAMETER ScriptBlock
        要执行的脚本块

    .PARAMETER ArgumentList
        参数列表

    .OUTPUTS
        Hashtable，包含 Output（stdout）、Warning、Error、Verbose、Debug 流
    #>
    param(
        [Parameter(Mandatory=$true)]
        [scriptblock]$ScriptBlock,

        [Parameter(Mandatory=$false)]
        [object[]]$ArgumentList = @()
    )

    $result = @{
        Output  = @()
        Warning = @()
        Error   = @()
        Verbose = @()
        Debug   = @()
    }

    try {
        $output = & $ScriptBlock @ArgumentList 2>&1

        foreach ($line in $output) {
            if ($line -is [System.Management.Automation.WarningRecord]) {
                $result.Warning += $line.Message
            } elseif ($line -is [System.Management.Automation.ErrorRecord]) {
                $result.Error += $line.Exception.Message
            } elseif ($line -is [System.Management.Automation.VerboseRecord]) {
                $result.Verbose += $line.Message
            } elseif ($line -is [System.Management.Automation.DebugRecord]) {
                $result.Debug += $line.Message
            } else {
                $result.Output += $line
            }
        }
    } catch {
        $result.Error += $_.Exception.Message
    }

    return $result
}

# ============================================
#  Mock 辅助
# ============================================

function Get-MockParameterFilter {
    <#
    .SYNOPSIS
        生成 Mock 的 ParameterFilter 脚本块

    .PARAMETER Parameters
        参数字典，如 @{ Path = "C:\test"; Recurse = $true }

    .EXAMPLE
        Mock Get-ChildItem -ParameterFilter (Get-MockParameterFilter @{ Path = "C:\test" })
    #>
    param(
        [Parameter(Mandatory=$true)]
        [hashtable]$Parameters
    )

    $filterParts = @()
    foreach ($key in $Parameters.Keys) {
        $value = $Parameters[$key]
        if ($value -is [string]) {
            $filterParts += "`$`$key -eq '$value'"
        } elseif ($value -is [bool]) {
            $filterParts += "`$`$key -eq `$$value"
        } else {
            $filterParts += "`$`$key -eq $value"
        }
    }

    $filterExpr = $filterParts -join " -and "
    return [scriptblock]::Create("param(`$$($Parameters.Keys -join ', `$'))`n    return ($filterExpr)")
}

# ============================================
#  断言辅助
# ============================================

function Should-BeEmpty {
    <#
    .SYNOPSIS
        验证值为空（$null 或空字符串）

    .PARAMETER ActualValue
        要验证的值
    #>
    param($ActualValue)
    $ActualValue | Should -BeNullOrEmpty
}

function Should-Contain {
    <#
    .SYNOPSIS
        验证字符串包含子串

    .PARAMETER ActualValue
        要验证的字符串

    .PARAMETER ExpectedSubstring
        期望包含的子串
    #>
    param($ActualValue, [string]$ExpectedSubstring)
    $ActualValue | Should -Match [regex]::Escape($ExpectedSubstring)
}

function Should-HaveCount {
    <#
    .SYNOPSIS
        验证集合具有指定数量的元素

    .PARAMETER ActualValue
        要验证的集合

    .PARAMETER ExpectedCount
        期望的元素数量
    #>
    param($ActualValue, [int]$ExpectedCount)
    @($ActualValue).Count | Should -Be $ExpectedCount
}

# ============================================
#  BatTool 测试辅助
# ============================================

function New-BatToolConfig {
    <#
    .SYNOPSIS
        创建 BatTool 测试用的配置哈希表

    .PARAMETER ProjectRoot
        项目根目录（可选，默认自动检测）

    .PARAMETER Overrides
        要覆盖的配置项（可选）

    .EXAMPLE
        $config = New-BatToolConfig
        $config = New-BatToolConfig -Overrides @{ "BatToolDir" = "D:\custom\BatTool" }
    #>
    param(
        [Parameter(Mandatory=$false)]
        [string]$ProjectRoot = "",

        [Parameter(Mandatory=$false)]
        [hashtable]$Overrides = @{}
    )

    if (-not $ProjectRoot) {
        $ProjectRoot = Get-ProjectRoot
    }

    $config = @{
        ProjectRoot          = $ProjectRoot
        BatToolDir           = Join-Path $ProjectRoot "BatTool"
        ToolsDir             = Join-Path $ProjectRoot "BatTool\Tools"
        UtilsDir             = Join-Path $ProjectRoot "BatTool\Tools\Utils"
        BatToolConfigPath    = Join-Path $ProjectRoot "BatTool\Tools\Utils\BatToolConfig.bat"
        EntriesConfigPath    = Join-Path $ProjectRoot "vscode-plugin-framework\config\entries.config.json"
        RegisterEntryScript  = Join-Path $ProjectRoot "BatTool\Tools\register_entry.ps1"
        SyncEntriesScript    = Join-Path $ProjectRoot "BatTool\Tools\sync_vscode_entries.ps1"
        FixCodeRunnerScript  = Join-Path $ProjectRoot "BatTool\Tools\fix_coderunner_config.ps1"
    }

    # 应用覆盖
    foreach ($key in $Overrides.Keys) {
        $config[$key] = $Overrides[$key]
    }

    return $config
}

function New-EntriesConfigJson {
    <#
    .SYNOPSIS
        创建模拟的 entries.config.json 内容

    .PARAMETER Entries
        条目数组（可选，默认为空数组）

    .EXAMPLE
        $json = New-EntriesConfigJson
        $json = New-EntriesConfigJson -Entries @(
            @{ name = "测试"; target = "test.bat"; workDir = "C:\test" }
        )
    #>
    param(
        [Parameter(Mandatory=$false)]
        [array]$Entries = @()
    )

    $config = @{
        entries = @()
    }

    foreach ($entry in $Entries) {
        $config.entries += @{
            name        = if ($entry.ContainsKey("name")) { $entry.name } else { "默认条目" }
            description = if ($entry.ContainsKey("description")) { $entry.description } else { "BatTool 测试条目" }
            type        = if ($entry.ContainsKey("type")) { $entry.type } else { "bat" }
            target      = if ($entry.ContainsKey("target")) { $entry.target } else { "test.bat" }
            workDir     = if ($entry.ContainsKey("workDir")) { $entry.workDir } else { "C:\test" }
            plugin      = if ($entry.ContainsKey("plugin")) { $entry.plugin } else { "code-runner" }
            newWindow   = if ($entry.ContainsKey("newWindow")) { $entry.newWindow } else { $true }
        }
    }

    return $config | ConvertTo-Json -Depth 10
}

function New-VsCodeSettingsJson {
    <#
    .SYNOPSIS
        创建模拟的 VS Code settings.json 内容

    .PARAMETER Overrides
        要覆盖或添加的配置项

    .EXAMPLE
        $json = New-VsCodeSettingsJson
        $json = New-VsCodeSettingsJson -Overrides @{
            "editor.fontSize" = 16
        }
    #>
    param(
        [Parameter(Mandatory=$false)]
        [hashtable]$Overrides = @{}
    )

    $settings = @{
        "editor.fontSize"          = 14
        "files.autoSave"           = "onFocusChange"
        "workbench.colorTheme"     = "Default Dark+"
        "code-runner.runInTerminal" = $false
        "code-runner.executorMap"  = @{
            "python"       = "python -u $fullFileName"
            "javascript"   = "node $fullFileName"
            "bat"          = 'start "Code Runner" cmd /c "$fullFileName"'
        }
    }

    # 应用覆盖
    foreach ($key in $Overrides.Keys) {
        $settings[$key] = $Overrides[$key]
    }

    return $settings | ConvertTo-Json -Depth 10
}

Export-ModuleMember -Function Import-TestScript, Get-ProjectRoot, New-TestConfig,
    New-TestFile, New-TestDirectory,
    Invoke-WithOutputCapture,
    Get-MockParameterFilter,
    Should-BeEmpty, Should-Contain, Should-HaveCount,
    New-BatToolConfig, New-EntriesConfigJson, New-VsCodeSettingsJson
