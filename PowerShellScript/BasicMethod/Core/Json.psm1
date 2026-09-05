<#
.SYNOPSIS
    JSON 配置加载函数模块 — 提供通用的 JSON 配置加载和对象转换函数

.DESCRIPTION
    从 Install-McpServer.ps1 中提取的纯通用 JSON 处理函数。
    支持"文件优先 → 回退内建"的配置加载模式。

    包含函数:
        Convert-PsCustomObjectToHashtable  — 将 PSCustomObject 数组转换为哈希表数组
        Load-JsonConfig                    — 通用 JSON 配置加载器

    用法:
        Import-Module .\BasicMethod-Json.psm1
        $configs = Load-JsonConfig -ConfigPath "config.json" -BuiltIn $defaultArray
        $hashtables = Convert-PsCustomObjectToHashtable $psObjects
#>

# ============================================
# JSON 转换函数
# ============================================

function Convert-PsCustomObjectToHashtable {
    <#
    .SYNOPSIS
        将 PSCustomObject 数组转换为哈希表数组
    .DESCRIPTION
        ConvertFrom-Json 返回的是 PSCustomObject，此函数将其递归转换为
        纯哈希表，便于后续操作（如添加/修改属性）。
    .PARAMETER InputObject
        要转换的对象（PSCustomObject 或 PSCustomObject 数组）
    .OUTPUTS
        [hashtable[]] 哈希表数组
    .EXAMPLE
        $json = '[{"name":"test","value":123}]' | ConvertFrom-Json
        $hashtables = Convert-PsCustomObjectToHashtable $json
    #>
    param(
        [Parameter(Mandatory = $true)]
        $InputObject
    )

    # 处理数组
    if ($InputObject -is [System.Collections.IEnumerable] -and $InputObject -isnot [string]) {
        $result = @()
        foreach ($item in $InputObject) {
            $result += Convert-PsCustomObjectToHashtable -InputObject $item
        }
        return $result
    }

    # 处理单个 PSCustomObject
    if ($InputObject -is [System.Management.Automation.PSCustomObject]) {
        $hash = @{}
        $InputObject.PSObject.Properties | ForEach-Object {
            $value = $_.Value
            # 递归处理嵌套对象
            if ($value -is [System.Management.Automation.PSCustomObject]) {
                $hash[$_.Name] = Convert-PsCustomObjectToHashtable -InputObject $value
            } elseif ($value -is [System.Collections.IEnumerable] -and $value -isnot [string]) {
                $arr = @()
                foreach ($v in $value) {
                    if ($v -is [System.Management.Automation.PSCustomObject]) {
                        $arr += Convert-PsCustomObjectToHashtable -InputObject $v
                    } else {
                        $arr += $v
                    }
                }
                $hash[$_.Name] = $arr
            } else {
                $hash[$_.Name] = $value
            }
        }
        return $hash
    }

    # 原始类型直接返回
    return $InputObject
}

# ============================================
# JSON 配置加载器
# ============================================

function Load-JsonConfig {
    <#
    .SYNOPSIS
        通用 JSON 配置加载器 — 优先从文件加载，失败时回退到内建数据
    .DESCRIPTION
        实现"文件优先 → 回退内建"的配置加载模式。
        支持从 JSON 文件加载配置，文件不存在或解析失败时自动回退到内建数据。
    .PARAMETER ConfigPath
        JSON 配置文件路径
    .PARAMETER BuiltIn
        内建数据（当文件不存在或解析失败时使用）
    .PARAMETER PropertyPath
        要提取的属性路径（可选），例如 "mcpServers" 表示提取根对象的 mcpServers 属性
    .PARAMETER InfoMessage
        加载成功时的提示信息模板（可选），例如 "从配置文件加载: {0}"
    .OUTPUTS
        [hashtable[]] 配置数据数组
    .EXAMPLE
        # 加载 mcp-servers-config.json，提取 mcpServers 属性
        $servers = Load-JsonConfig -ConfigPath "mcp-servers-config.json" `
            -BuiltIn $defaultServers `
            -PropertyPath "mcpServers" `
            -InfoMessage "从配置文件加载 MCP 服务器清单: {0}"
    .EXAMPLE
        # 简单加载，直接返回根对象
        $config = Load-JsonConfig -ConfigPath "appsettings.json" -BuiltIn @{}
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ConfigPath,

        [Parameter(Mandatory = $true)]
        $BuiltIn,

        [string]$PropertyPath = "",

        [string]$InfoMessage = ""
    )

    if (Test-Path $ConfigPath) {
        try {
            $raw = Get-Content $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json

            # 提取指定属性路径
            $data = if ($PropertyPath) {
                $raw.$PropertyPath
            } else {
                $raw
            }

            if ($data -and ($data -is [System.Collections.IEnumerable] -or $data.PSObject.Properties.Count -gt 0)) {
                if ($InfoMessage) {
                    Write-Host "  ℹ️  $($InfoMessage -f $ConfigPath)" -ForegroundColor Gray
                }
                return Convert-PsCustomObjectToHashtable -InputObject $data
            }
        } catch {
            Write-Host "  ⚠️  配置文件解析失败: $_" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  ℹ️  配置文件不存在: $ConfigPath，使用内建数据" -ForegroundColor Gray
    }

    # 回退到内建数据
    return $BuiltIn
}

# ============================================
# 配置值获取函数（三级回退模式）
# ============================================

function Get-ConfigValue {
    <#
    .SYNOPSIS
        三级回退配置获取 — 环境变量 → 配置文件 → 默认值
    .DESCRIPTION
        实现"环境变量覆盖 → 配置文件 → 默认值"的三级回退配置获取模式。
        优先级: 环境变量 > 配置文件 > 默认值
    .PARAMETER Key
        配置键名（用于匹配环境变量和配置文件中的键）
    .PARAMETER Config
        配置哈希表（可选，第二级回退）
    .PARAMETER Default
        默认值（可选，第三级回退）
    .PARAMETER EnvPrefix
        环境变量前缀（可选，默认无前缀）
    .PARAMETER EnvVarName
        环境变量名（可选，指定完整的环境变量名，优先级高于 Key+EnvPrefix）
    .OUTPUTS
        [object] 配置值
    .EXAMPLE
        # 三级回退: 环境变量 MCP_PORT > 配置中的 port > 默认值 8080
        $port = Get-ConfigValue -Key "port" -Config $config -Default 8080 -EnvPrefix "MCP_"
    .EXAMPLE
        # 指定完整环境变量名
        $path = Get-ConfigValue -Key "configPath" -Config $config -Default "./config.json" -EnvVarName "MCP_CONFIG_PATH"
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Key,

        [hashtable]$Config = @{},

        $Default = $null,

        [string]$EnvPrefix = "",

        [string]$EnvVarName = ""
    )

    # 第一级: 环境变量
    $envName = if ($EnvVarName) {
        $EnvVarName
    } elseif ($EnvPrefix) {
        "$EnvPrefix$Key".ToUpper()
    } else {
        $Key.ToUpper()
    }

    $envValue = [Environment]::GetEnvironmentVariable($envName)
    if ($envValue -ne $null) {
        Write-Verbose "  ℹ️  从环境变量获取配置: $envName = $envValue"
        return $envValue
    }

    # 第二级: 配置文件
    if ($Config.ContainsKey($Key)) {
        return $Config[$Key]
    }

    # 第三级: 默认值
    return $Default
}

# ============================================
# 模块导出
# ============================================

Export-ModuleMember -Function @(
    'Convert-PsCustomObjectToHashtable',
    'Load-JsonConfig',
    'Get-ConfigValue'
)
