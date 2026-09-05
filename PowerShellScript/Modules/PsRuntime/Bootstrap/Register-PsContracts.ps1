# Register-PsContracts.ps1 — 通用契约扫描器（扫描配置目录，产出契约集）
# 边界: 空目录/不存在目录跳过；ProjectRoot 为空返回 failure

function Register-PsContracts {
    param([string]$ProjectRoot, [string[]]$ContractDirs)
    try {
        # 边界: ProjectRoot 为空
        if (-not $ProjectRoot) { return New-PsResult -Status failure -Message "projectRoot empty" -Errors @("projectRoot_empty") }

        $contracts = @()
        $dirs = if ($ContractDirs) { @($ContractDirs) } else { @() }
        foreach ($dir in $dirs) {
            $abs = Join-Path $ProjectRoot $dir
            # 边界: 不存在的目录跳过
            if (-not (Test-Path $abs)) { continue }
            foreach ($f in (Get-ChildItem -Path $abs -Recurse -Include *.psm1, *.ps1 -File -ErrorAction SilentlyContinue)) {
                $cs = Get-PsContracts -ScriptPath $f.FullName
                foreach ($c in $cs) {
                    if ($c.identity.action) {
                        # 规范化 path 为绝对路径（sidecar 中可能是相对项目根的路径）
                        if ($c.identity.path -and -not [System.IO.Path]::IsPathRooted($c.identity.path)) {
                            $c.identity.path = Join-Path $ProjectRoot $c.identity.path
                        }
                        $contracts += $c
                    }
                }
            }
        }
        return New-PsResult -Status success -Message "scanned" -Data @{ count = $contracts.Count; contracts = $contracts }
    } catch {
        return New-PsResult -Status failure -Message "scan error" -Errors @($_.Exception.Message)
    }
}
