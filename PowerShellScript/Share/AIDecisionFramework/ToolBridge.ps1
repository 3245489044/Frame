# ToolBridge.ps1 - AI 决策框架工具调用桥接层（领域特定部分）
# 通用工具函数已迁移到 PowerShellScript/BasicMethod/Tooling/ToolBridge.psm1
# 本文件仅保留项目特定的 Invoke-CtxSearch

<#
.SYNOPSIS
    搜索知识库（对应 ctx_search）
.DESCRIPTION
    通过调用 CLine 的搜索能力，在本地文件系统中搜索相关内容。
    依赖项目特定的 docs/ 目录路径，因此保留在 AIDecisionFramework 中。
.PARAMETER Queries
    搜索查询数组
.PARAMETER Source
    限定搜索源
.EXAMPLE
    Invoke-CtxSearch -Queries @("决策矩阵", "认知重启税")
#>
function Invoke-CtxSearch {
    param(
        [Parameter(Mandatory)]
        [string[]]$Queries,

        [string]$Source = "",

        [int]$Limit = 3
    )

    $results = @()

    foreach ($q in $Queries) {
        # 使用 Select-String 在项目文档中搜索
        $searchResults = Get-ChildItem -Path (Resolve-Path ".\docs") -Recurse -Filter "*.md" -File |
            Select-String -Pattern $q -SimpleMatch -ErrorAction SilentlyContinue |
            Group-Object Path |
            Sort-Object Count -Descending |
            Select-Object -First $Limit

        $matches = $searchResults | ForEach-Object {
            $relativePath = $_.Name.Replace((Resolve-Path ".\").Path, "").TrimStart("\")
            [PSCustomObject]@{
                Source = $relativePath
                MatchCount = $_.Count
                Query = $q
            }
        }

        $results += [PSCustomObject]@{
            Query = $q
            TotalMatches = ($matches | Measure-Object | Select-Object -ExpandProperty Count)
            Matches = $matches
        }
    }

    return $results
}
