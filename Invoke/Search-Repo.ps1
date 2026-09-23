<#
.SYNOPSIS
    【入口】多仓多模式只读检索
.DESCRIPTION
    Frame\Invoke 统一入口 —— 转发到 Frame\PowerShellScript\Share\Search\Search-Repo.ps1
    替代手写 Get-ChildItem | Select-String 的递归搜索。

    默认字面量匹配，多 pattern = OR，分仓输出。
.PARAMETER Pattern
    搜索模式（多值 = OR，逗号分隔）。
.PARAMETER PathSpec
    文件过滤（如 '*.cs','*.json'），默认多格式覆盖。
.PARAMETER Repo
    仓库列表，默认 Frame, Projects。
.PARAMETER Literal
    字面量匹配（默认开启）。
.PARAMETER Regex
    正则匹配（与 Literal 互斥）。
.PARAMETER Untracked
    包含未跟踪文件。
.PARAMETER List
    只列命中文件。
.PARAMETER MaxFiles
    最大显示条数，默认 200。
.PARAMETER FailOnEmpty
    无命中时退出码 1。
.EXAMPLE
    .\Search-Repo.ps1 -Pattern data-poll-cmd -PathSpec '*.cs' -Repo Frame
#>
param(
    [Parameter(Mandatory = $true)][string[]]$Pattern,
    [string[]]$PathSpec,
    [string[]]$Repo,
    [switch]$Literal,
    [switch]$Regex,
    [switch]$Untracked,
    [switch]$List,
    [int]$MaxFiles = 200,
    [switch]$FailOnEmpty
)

$target = Join-Path $PSScriptRoot "..\PowerShellScript\Share\Search\Search-Repo.ps1"

# 使用哈希表 splatting 确保类型正确
$params = @{
    Pattern = $Pattern
}
if ($PSBoundParameters.ContainsKey('PathSpec')) { $params['PathSpec'] = $PathSpec }
if ($PSBoundParameters.ContainsKey('Repo'))     { $params['Repo'] = $Repo }
if ($Literal)    { $params['Literal'] = $true }
if ($Regex)      { $params['Regex'] = $true }
if ($Untracked)  { $params['Untracked'] = $true }
if ($List)       { $params['List'] = $true }
if ($PSBoundParameters.ContainsKey('MaxFiles')) { $params['MaxFiles'] = $MaxFiles }
if ($FailOnEmpty){ $params['FailOnEmpty'] = $true }

& $target @params
exit $LASTEXITCODE
