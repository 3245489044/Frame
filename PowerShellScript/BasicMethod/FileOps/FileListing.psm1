<#
.SYNOPSIS
    枚举文件夹内容（文件 + 子目录，支持递归/通配）—— list.dir 确定性动作

.DESCRIPTION
    标准存储分析流程的确定性动作（零 LLM）：枚举文件夹的文件与子目录，返回结构化结果。
    依赖 BasicMethod-FileUtils.psm1（_ResolvePath / _GetRelativePath）。

.PARAMETER Path
    目录路径（相对项目根或绝对路径）
.PARAMETER Recurse
    是否递归子目录
.PARAMETER Pattern
    文件通配模式（如 *.cs；默认 *）
.EXAMPLE
    List-Dir -Path "PowerShellScript\BasicMethod" -Recurse
    List-Dir -Path "Storage\xxx" -Pattern "*.cs"
#>
function List-Dir {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [switch]$Recurse,

        [string]$Pattern = "*"
    )

    try {
        $resolvedPath = _ResolvePath -Path $Path

        if (-not (Test-Path -LiteralPath $resolvedPath -PathType Container)) {
            return New-PsResult -Status failure -Message "directory not found: $resolvedPath" -Errors @("dir_not_found")
        }

        $getParams = @{ LiteralPath = $resolvedPath; ErrorAction = "SilentlyContinue" }
        if ($Recurse) { $getParams["Recurse"] = $true }

        # 子目录（不参与 Pattern 过滤）
        $dirItems = @(
            Get-ChildItem @getParams -Directory -ErrorAction SilentlyContinue |
                ForEach-Object {
                    [PSCustomObject]@{
                        FullName = $_.FullName
                        Name = $_.Name
                    }
                }
        )

        # 文件（先枚举再按 -like 通配过滤，避免 -Filter 与 -LiteralPath 组合的 provider 过滤坑）
        $allFiles = @(Get-ChildItem @getParams -File -ErrorAction SilentlyContinue)
        $fileItems = @(
            $allFiles | Where-Object { $_.Name -like $Pattern } | ForEach-Object {
                [PSCustomObject]@{
                    FullName = $_.FullName
                    Name = $_.Name
                    Length = $_.Length
                    LastWriteTime = $_.LastWriteTime
                    RelativePath = _GetRelativePath -Path $_.FullName
                }
            }
        )

        return @{
            Path = $resolvedPath
            Recurse = $Recurse.IsPresent
            Pattern = $Pattern
            Files = $fileItems
            Dirs = $dirItems
            TotalFiles = $fileItems.Count
            TotalDirs = $dirItems.Count
        }
    } catch {
        return New-PsResult -Status failure -Message "list.dir error" -Errors @("execution_error", $_.Exception.Message)
    }
}

Export-ModuleMember -Function "List-Dir"