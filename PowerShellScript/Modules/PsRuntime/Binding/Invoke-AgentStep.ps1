# Invoke-AgentStep.ps1 — Agent 薄绑定：加载 PsRuntime + Agent 配置，暴露 Agent 入口
# 职责：配置加载 + 契约扫描 + 能力库依赖预加载（FileUtils/FileDiff），不包含业务逻辑

$bindingPath = $MyInvocation.MyCommand.Path
$psrtRoot = Split-Path -Parent (Split-Path -Parent $bindingPath)   # PowerShellScript/Modules/PsRuntime
. (Join-Path $psrtRoot "_init.ps1")

# 根基准显式化（禁止依赖旧布局推断）：ProjectRoot = 仓库根；AgentRoot = Projects\Plug\Agent；ConfigRoot 缺省 = AgentRoot\Config。
function Resolve-AgentRoots {
    param([string]$ProjectRoot, [string]$AgentRoot, [string]$ConfigRoot)
    $repo = $ProjectRoot
    if ([string]::IsNullOrWhiteSpace($repo)) {
        $probe = $psrtRoot
        while (-not [string]::IsNullOrWhiteSpace($probe)) {
            $candidateAgent = Join-Path $probe "Projects\Plug\Agent"
            $candidateFrame = Join-Path $probe "Frame\PowerShellScript"
            if ((Test-Path $candidateAgent) -and (Test-Path $candidateFrame)) { $repo = $probe; break }
            $parent = Split-Path -Parent $probe
            if ($parent -eq $probe) { break }
            $probe = $parent
        }
        if ([string]::IsNullOrWhiteSpace($repo)) {
            $frameRoot = Split-Path -Parent (Split-Path -Parent $psrtRoot)
            $repo = Split-Path -Parent $frameRoot
        }
    }
    $agent = if ($AgentRoot) { $AgentRoot } else { Join-Path $repo "Projects\Plug\Agent" }
    $cfg = if ($ConfigRoot) { $ConfigRoot } else { Join-Path $agent "Config" }
    return [PSCustomObject]@{ RepoRoot = $repo; AgentRoot = $agent; ConfigRoot = $cfg }
}

function Resolve-ConfigPath {
    param([string]$Root, [string]$AgentRoot, [string]$RelativePath)
    if ([string]::IsNullOrWhiteSpace($RelativePath)) { return $null }
    if ([System.IO.Path]::IsPathRooted($RelativePath)) { return $RelativePath }
    $candidate = Join-Path $Root $RelativePath
    if (Test-Path $candidate) { return $candidate }
    return (Join-Path $AgentRoot $RelativePath)
}

function Invoke-AgentStep {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Action,
        [Parameter(Mandatory)][AllowNull()][hashtable]$Params,
        [hashtable]$ExtensionSpec,
        [string]$ProjectRoot = '',
        [string]$AgentRoot = '',
        [string]$ConfigRoot = ''
    )
    try {
        $roots = Resolve-AgentRoots -ProjectRoot $ProjectRoot -AgentRoot $AgentRoot -ConfigRoot $ConfigRoot
        $repoRoot = $roots.RepoRoot
        $agentRoot = $roots.AgentRoot
        $configRoot = $roots.ConfigRoot

        $cfgPath = Join-Path $configRoot "ps1-runtime.json"
        if (-not (Test-Path $cfgPath)) { return New-PsResult -Status failure -Message "config missing: $cfgPath" -Errors @("config_missing") }
        $cfg = Get-Content $cfgPath -Raw -Encoding UTF8 | ConvertFrom-Json

        $root = if ([System.IO.Path]::IsPathRooted([string]$cfg.projectRoot)) { [string]$cfg.projectRoot }
                else { Join-Path $repoRoot ([string]$cfg.projectRoot) }

        # 预加载能力库依赖（read.file/write.file 依赖 FileUtils/FileDiff）
        if ($cfg.basemethodDeps) {
            foreach ($dep in $cfg.basemethodDeps) {
                $depPath = Resolve-ConfigPath -Root $root -AgentRoot $agentRoot -RelativePath ([string]$dep)
                if ($depPath -and (Test-Path $depPath)) { Import-Module $depPath -Force -ErrorAction SilentlyContinue }
            }
        }

        # 扫描契约集
        $scan = Register-PsContracts -ProjectRoot $root -ContractDirs $cfg.contractDirs
        if ($scan.status -ne "success") { return $scan }
        $cs = $scan.data.contracts

        # 拓展协议路径
        $outDir = Join-Path $configRoot "generated"
        $regPath = Resolve-ConfigPath -Root $root -AgentRoot $agentRoot -RelativePath ([string]$cfg.registry.psmRegistry)
        $usagePath = Resolve-ConfigPath -Root $root -AgentRoot $agentRoot -RelativePath ([string]$cfg.usage.store)

        # 单一配置入口：store.resource 由绑定注入解析后的存储桥配置（StorageNode 不自寻路径）
        if ($Action -match 'store\.resource$') {
            $Params["Storage"] = Resolve-StorageBridge -Cfg $cfg -Root $root -AgentRoot $agentRoot
        }

        $result = Invoke-PsAction -Action $Action -Params $Params -Contracts $cs -ExtensionSpec $ExtensionSpec `
            -OutDir $outDir -RegistryPath $regPath -UsagePath $usagePath `
            -PromotionThreshold $cfg.usage.promotionThreshold -PromotionWindowDays $cfg.usage.promotionWindowDays

        # read.file -Persist 组合（薄绑定）：读后按 Persist/auto 谓词把文件转标准资源节点（经存储桥；不再落独立 SQL ur_resources 表）
        if ($result.status -eq "success" -and $Action -match 'read\.file$') {
            $persist = $Params["Persist"]
            if ($persist -eq "on" -or $persist -eq "auto") {
                $shouldPersist = if ($persist -eq "on") { $true } else { Test-StorageAutoPersist -Cfg $cfg -Data $result.data }
                if ($shouldPersist) {
                    $node = New-FileResourceNode -Data $result.data
                    $store = Invoke-PsAction -Action "store.resource" -Params @{
                        ResourceType = $node.ResourceType
                        Id = $node.Id
                        Symbol = $node.Symbol
                        Metadata = $node.Metadata
                        Storage = (Resolve-StorageBridge -Cfg $cfg -Root $root -AgentRoot $agentRoot)
                    } -Contracts $cs -OutDir $outDir -RegistryPath $regPath -UsagePath $usagePath `
                        -PromotionThreshold $cfg.usage.promotionThreshold -PromotionWindowDays $cfg.usage.promotionWindowDays
                    $result.data["_persistStored"] = @{ status = $store.status; errors = $store.errors }
                }
            }
        }
        return $result
    } catch {
        return New-PsResult -Status failure -Message "agent step error" -Errors @($_.Exception.Message)
    }
}

# 存储桥解析：配置 host 相对项目根 → 绝对路径；未找到则在 Agent\BuildOutput 搜索（兼容 Debug/Release）
function Resolve-StorageBridge {
    param([object]$Cfg, [string]$Root, [string]$AgentRoot = '')
    try {
        if ($null -eq $Cfg.storage) { return $null }
        $hostPath = [string]$Cfg.storage.host
        if (-not [string]::IsNullOrWhiteSpace($hostPath)) {
            if (-not [System.IO.Path]::IsPathRooted($hostPath)) {
                $absolute = Join-Path $Root $hostPath
                if (Test-Path $absolute) {
                    $hostPath = $absolute
                } elseif ($AgentRoot) {
                    $agentCandidate = Join-Path $AgentRoot $hostPath
                    if (Test-Path $agentCandidate) { $hostPath = $agentCandidate }
                } else {
                    $hostPath = $absolute
                }
            }
            if (-not (Test-Path $hostPath)) {
                $searchRoot = if ($AgentRoot) { Join-Path $AgentRoot "BuildOutput" } else { Join-Path $Root "Agent\BuildOutput" }
                $found = Get-ChildItem -Path $searchRoot -Recurse -Filter "Agent.StorageBridge.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($found) { $hostPath = $found.FullName }
            }
        }
        if (-not (Test-Path $hostPath)) { return $Cfg.storage }
        return [PSCustomObject]@{
            host = $hostPath
            command = $Cfg.storage.command
            autoPersist = $Cfg.storage.autoPersist
        }
    } catch { return $null }
}

# 读后资源节点构造（file:<path> 约定收敛到绑定层，FileOps 不感知存储 schema）
function New-FileResourceNode {
    param([object]$Data)
    $path = [string]$Data.Path
    $hash = if ($Data.PSObject.Properties.Name -contains "Hash") { [string]$Data.Hash }
            else { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLower() }
    return @{
        ResourceType = "file"
        Id = "file:$path"
        Symbol = [System.IO.Path]::GetFileName($path)
        Metadata = @{
            path = $path
            totalLines = [int]$Data.TotalLines
            contentHash = $hash
        }
    }
}

# 存储 auto 谓词：扩展名 ∈ 白名单 且 文件大小 ≤ maxBytes → 自动落库（read.file -Persist auto）
function Test-StorageAutoPersist {
    param([object]$Cfg, $Data)
    try {
        $ap = $Cfg.storage.autoPersist
        if ($null -eq $ap -or -not $ap.enabled) { return $false }
        $path = [string]$Data.Path
        $ext = [System.IO.Path]::GetExtension($path).ToLowerInvariant()
        $exts = @($ap.extensions)
        $extOk = ($exts.Count -eq 0) -or ($exts -contains $ext)
        $sizeOk = $false
        $fi = Get-Item -LiteralPath $path -ErrorAction SilentlyContinue
        if ($fi) { $sizeOk = $fi.Length -le [long]$ap.maxBytes }
        return ($extOk -and $sizeOk)
    } catch { return $false }
}