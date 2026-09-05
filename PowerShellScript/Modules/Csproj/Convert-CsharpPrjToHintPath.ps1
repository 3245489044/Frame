<#
.SYNOPSIS
    CsharpPrj 工程引用 → 二进制 HintPath 引用 生成器（案A：让 Agent/Engine/Produce 等不再直接编译 CsharpPrj）

.DESCRIPTION
    扫描指定产品目录下的所有 .csproj，找出其中指向 CsharpPrj 的 ProjectReference，
    将其替换为指向 CsharpPrj 唯一权威烘焙目录的 <Reference HintPath="$(CsharpBuildOutputDir)\X.dll" />。

    原理：
        - CsharpPrj 项目本就只编译到自己的权威目录 CsharpPrj\BuildOutput\...（方案B 已锚定）。
        - 产品项目把 ProjectReference 换成 Reference，则：
              * 编译期类型仍从权威 dll 解析（Agent 的 using CsharpPrj.* 依旧可用）
              * 不再把 CsharpPrj 拉进产品构建图，避免重复编译
        - HintPath 使用 $(CsharpBuildOutputDir) 属性，构建时由 MSBuild 展开。

    安全策略：
        - 默认仅 DryRun 预览，不写任何文件。
        - 加 -Apply 才实际改写 csproj。
        - 改写前自动备份原文件（.before-csharpprj.<时间戳>.bak），可用 -Restore 回滚。

    用法:
        .\Convert-CsharpPrjToHintPath.ps1                          # DryRun 预览当前目录
        .\Convert-CsharpPrjToHintPath.ps1 -Path Projects\Agent     # DryRun 预览指定产品
        .\Convert-CsharpPrjToHintPath.ps1 -Path Projects\Agent -Apply -Backup   # 实际改写并备份
        .\Convert-CsharpPrjToHintPath.ps1 -Path Projects\Agent -Restore          # 回滚改动
        .\Convert-CsharpPrjToHintPath.ps1 -ReportFile plan.json    # 导出替换计划

    示例:
        .\Convert-CsharpPrjToHintPath.ps1 -Path Projects\Agent -Apply
#>

param(
    # 目标产品目录或 .csproj 文件（默认当前目录）
    [string]$Path = ".",

    # CsharpPrj 根目录（默认从仓库根自动推导）
    [string]$CsharpRoot = "",

    # 产品侧应定义、且指向 CsharpPrj 权威输出目录的 MSBuild 属性名（写入 HintPath）
    [string]$OutputProp = "CsharpBuildOutputDir",

    # 是否实际改写文件
    [switch]$Apply,

    # 改写前是否备份原文件（默认 true，配合 -Apply 生效）
    [switch]$NoBackup,

    # 是否回滚上一次改动
    [switch]$Restore,

    # 导出替换计划到 JSON 文件
    [string]$ReportFile = ""
)

# ============================================
#  辅助函数
# ============================================

function Write-Part {
    param([string]$Text, [string]$Color = "White")
    Write-Host $Text -ForegroundColor $Color
}
function Write-Ok   { param([string]$T) Write-Host $T -ForegroundColor Green }
function Write-Info { param([string]$T) Write-Host $T -ForegroundColor Cyan }
function Write-Warn { param([string]$T) Write-Host $T -ForegroundColor Yellow }
function Write-Err  { param([string]$T) Write-Host $T -ForegroundColor Red }

function Get-NormalizedPath {
    param([string]$P, [string]$BaseDir = "")
    if ([string]::IsNullOrWhiteSpace($P)) { return "" }
    if ([System.IO.Path]::IsPathRooted($P)) { return [System.IO.Path]::GetFullPath($P) }
    if (-not [string]::IsNullOrWhiteSpace($BaseDir)) {
        return [System.IO.Path]::GetFullPath((Join-Path $BaseDir $P))
    }
    return $P
}

function Import-PropsProperties {
    <#
    .SYNOPSIS
        解析 .props 文件，收集所有 <PropertyGroup> 内 <Key>Value</Key> 的属性表
    .DESCRIPTION
        返回 Hashtable：Key -> Value（Value 为原始文本，可能含 $(...) 待展开）
    #>
    param([string[]]$Files)
    $map = @{}
    foreach ($f in $Files) {
        if (-not (Test-Path $f)) { continue }
        $raw = Get-Content $f -Raw -ErrorAction SilentlyContinue
        if ([string]::IsNullOrWhiteSpace($raw)) { continue }
        $rx = [regex]'<\s*([A-Za-z_][\w\.]*)\s*>([^<]*)</\s*\1\s*>'
        foreach ($m in $rx.Matches($raw)) {
            $key = $m.Groups[1].Value
            $val = $m.Groups[2].Value.Trim()
            if ([string]::IsNullOrWhiteSpace($key)) { continue }
            $map[$key] = $val
        }
    }
    return $map
}

function Expand-Properties {
    <#
    .SYNOPSIS
        循环展开字符串中的 $(Property) 引用（基于属性表，最多迭代若干轮防环）
    #>
    param([string]$Text, [hashtable]$Map, [int]$MaxIter = 25)
    $result = $Text
    for ($i = 0; $i -lt $MaxIter; $i++) {
        $expanded = $result
        $rx = [regex]'[^$]?[*@]?\$\(([\w\.]+)\)'
        # 仅处理 $(...) 形式
        $m = [regex]::Matches($result, '\$\(([\w\.]+)\)')
        if ($m.Count -eq 0) { break }
        foreach ($mm in $m) {
            $key = $mm.Groups[1].Value
            if ($Map.ContainsKey($key)) {
                $expanded = $expanded.Replace($mm.Value, $Map[$key])
            }
        }
        if ($expanded -eq $result) { break }
        $result = $expanded
    }
    # 清理属性残留：未解析的 $(...) 保留原样
    return $result
}

function Find-CsprojFiles {
    <#
    .SYNOPSIS
        收集目标下的 .csproj 文件（排除 obj/bin/BuildOutput 等产物目录）
    #>
    param([string]$TargetPath)
    $existing = Resolve-Path $TargetPath -ErrorAction SilentlyContinue
    if (-not $existing) {
        Write-Err "路径不存在: $TargetPath"
        return @()
    }
    $full = $existing.Path
    if (Test-Path $full -PathType Leaf) {
        if ($full -like "*.csproj") { return @($full) }
        return @()
    }
    $csproj = @()
    Get-ChildItem -Path $full -Filter "*.csproj" -File -Recurse |
        Where-Object {
            $_.FullName -notmatch '\\(obj|bin|BuildOutput|\.vs)\\' -and
            $_.FullName -notmatch '\\backups?\\'
        } |
        ForEach-Object { $csproj += $_.FullName }
    if ($csproj.Count -eq 0) {
        Write-Warn "目录中未找到 .csproj 文件: $full"
    }
    return @($csproj | Sort-Object)
}

function Read-CsprojDoc {
    param([string]$FilePath)
    if (-not (Test-Path $FilePath)) { return $null }
    try {
        $doc = New-Object System.Xml.XmlDocument
        $doc.PreserveWhitespace = $true
        $doc.Load($FilePath)
        return $doc
    }
    catch {
        Write-Err "无法解析 XML: $FilePath ($_)"
        return $null
    }
}

function Get-AssemblyName {
    <#
    .SYNOPSIS
        取 csproj 的程序集名（无显式 <AssemblyName> 时 = 文件名）
    #>
    param([string]$CsprojPath)
    $fileName = [System.IO.Path]::GetFileNameWithoutExtension($CsprojPath)
    $doc = Read-CsprojDoc $CsprojPath
    if ($doc -ne $null) {
        $node = $doc.SelectSingleNode("//PropertyGroup/AssemblyName")
        if ($node -ne $null -and -not [string]::IsNullOrWhiteSpace($node.InnerText)) {
            return $node.InnerText.Trim()
        }
    }
    return $fileName
}

function Is-UnderDir {
    param([string]$Child, [string]$Parent)
    $c = Get-NormalizedPath $Child
    $p = Get-NormalizedPath $Parent.TrimEnd('\')
    return $c.StartsWith($p + '\', [System.StringComparison]::OrdinalIgnoreCase)
}

# ============================================
#  解析路径与属性
# ============================================

# 解析仓库根：向上找到「以 CsharpPrj 作为同级子目录」的那一级，视为仓库根
# （不能用「第一个 Directory.Build.props」，因为各产品(Agent/Engine…)自带 props，会误判）
$resolvedTarget = (Resolve-Path $Path -ErrorAction SilentlyContinue)
if (-not $resolvedTarget) { Write-Err "路径不存在: $Path"; exit 1 }
$targetFull = [System.IO.Path]::GetFullPath($resolvedTarget.Path)
$cur = if (Test-Path $targetFull -PathType Leaf) { Split-Path $targetFull -Parent } else { $targetFull }

$repoRoot = ""
$walk = $cur
while ($walk) {
    if (Test-Path (Join-Path $walk "CsharpPrj") ) {
        $repoRoot = $walk
        break
    }
    $parent = Split-Path $walk -Parent
    if ($parent -eq $walk) { break }
    $walk = $parent
}

if ([string]::IsNullOrWhiteSpace($CsharpRoot)) {
    if (-not [string]::IsNullOrWhiteSpace($repoRoot)) {
        $CsharpRoot = Join-Path $repoRoot "CsharpPrj"
    }
    else {
        $CsharpRoot = Join-Path $cur "CsharpPrj"
    }
}
$CsharpRootFull = Get-NormalizedPath $CsharpRoot

# 收集属性表：仓库根 props + 目标产品 props
$propFiles = @()
if (-not [string]::IsNullOrWhiteSpace($repoRoot)) {
    $propFiles += Join-Path $repoRoot "Directory.Build.props"
}
if (-not (Test-Path $targetFull -PathType Leaf)) {
    $prodProps = Join-Path $targetFull "Directory.Build.props"
    if (Test-Path $prodProps) { $propFiles += $prodProps }
}
$propMap = Import-PropsProperties -Files $propFiles

# 覆盖注入已知的 Csharp 根属性别名（确保 ProjectReference 的 $(...) 能被解析为可验证的真实路径）
$propMap["CsharpProjectRoot"] = $CsharpRootFull
$propMap["CsharpRootDir"]     = $CsharpRootFull + '\'
$propMap["CsharpRoot"]        = $CsharpRootFull
if (-not [string]::IsNullOrWhiteSpace($repoRoot)) {
    $propMap["RepoRoot"] = $repoRoot + '\'
}

# ============================================
#  扫描并生成替换计划（默认 DryRun）
# ============================================

$csprojFiles = Find-CsprojFiles -TargetPath $targetFull
if ($csprojFiles.Count -eq 0) { exit 0 }

$plan = @()   # 每项：{ Csproj, BackupPath, Reference, OldNodeXml, NewNodeXml }
$ownReference = @{}  # CsharpPrj csproj 的 AssemblyName 缓存

foreach ($csproj in $csprojFiles) {
    $doc = Read-CsprojDoc $csproj
    if ($doc -eq $null) { continue }
    $csprojDir = Split-Path $csproj -Parent

    $projectRefs = $doc.SelectNodes("//ProjectReference")
    foreach ($node in $projectRefs) {
        $include = $node.GetAttribute("Include")
        if ([string]::IsNullOrWhiteSpace($include)) { continue }

        # 展开属性得到真正的目标路径
        $expInc = Expand-Properties -Text $include -Map $propMap
        $resolved = Get-NormalizedPath -P $expInc -BaseDir $csprojDir
        # 若展开后仍含未解析属性，尝试直接拼 csprojDir
        if (-not [System.IO.Path]::IsPathRooted($resolved)) { $resolved = $csprojDir }

        # 是否指向 CsharpPrj？
        if (-not (Is-UnderDir -Child $resolved -Parent $CsharpRootFull)) { continue }
        if (-not (Test-Path $resolved)) {
            Write-Warn "  跳过(目标 csproj 不存在): $include"
            continue
        }

        $asmName = Get-AssemblyName $resolved
        # 生成 MSBuild 字面文本: $(CsharpBuildOutputDir)\X.dll（用拼接避免 PS 把 $() 当子表达式）
        $hintPathExpr = [string]::Concat('$(', $OutputProp, ')\', $asmName, '.dll')

        $plan += [PSCustomObject]@{
            Csproj       = (Get-NormalizedPath $csproj)
            Reference    = $resolved
            AssemblyName = $asmName
            HintPath     = $hintPathExpr
            OldNodeXml   = $node.OuterXml
        }
    }
}

$grouped = $plan | Group-Object Csproj

Write-Part ""
Write-Part "============================================" Cyan
Write-Part "  CsharpPrj 引用 → HintPath 生成器（DryRun 预览）" Cyan
Write-Part "============================================" Cyan
Write-Info "仓库根      : $repoRoot"
Write-Info "CsharpPrj 根: $CsharpRootFull"
Write-Info "扫描目标    : $targetFull"
Write-Info "输出属性    : $OutputProp"
Write-Info "命中 csproj : $($grouped.Count) 个 / 引用 $($plan.Count) 处"
Write-Part ""

if ($plan.Count -eq 0) {
    Write-Ok "未发现指向 CsharpPrj 的 ProjectReference，无需改动。"
}
else {
    foreach ($g in $grouped) {
        Write-Part "● $($g.Name)" Yellow
        foreach ($item in $g.Group) {
            Write-Part "    - $($item.AssemblyName).dll  ←  $(Split-Path $item.Reference -Leaf)" Gray
            Write-Part "        HintPath: $($item.HintPath)" DarkGray
        }
    }
    Write-Part ""
    Write-Warn "以上为预览，尚未写文件。"
    Write-Warn "如 CsharpPrj 产物（$CsharpRootFull）尚未编译，请先 dotnet build 对应配置，再执行写入。"
}

# ============================================
#  Apply：实际改写 csproj
# ============================================
if ($Apply) {
    if ($plan.Count -eq 0) {
        Write-Info "无可应用改动。"
        exit 0
    }

    # 确实改写：重读每个 csproj，把命中节点改为 <Reference>
    foreach ($g in $grouped) {
        $csproj = $g.Name
        $doc = Read-CsprojDoc $csproj
        if ($doc -eq $null) { continue }

        # 备份
        $backupPath = ""
        if (-not $NoBackup) {
            $ts = Get-Date -Format "yyyyMMdd_HHmmss"
            $backupPath = "$csproj.before-csharpprj.$ts.bak"
            Copy-Item $csproj $backupPath -Force
        }

        $changed = 0
        $refNodes = $doc.SelectNodes("//ProjectReference")
        foreach ($node in $refNodes) {
            $include = $node.GetAttribute("Include")
            if ([string]::IsNullOrWhiteSpace($include)) { continue }
            $expInc = Expand-Properties -Text $include -Map $propMap
            $resolved = Get-NormalizedPath -P $expInc -BaseDir (Split-Path $csproj -Parent)
            if (-not (Is-UnderDir -Child $resolved -Parent $CsharpRootFull)) { continue }

            $asmName = Get-AssemblyName $resolved
            $newNode = $doc.CreateElement("Reference")
            $newNode.SetAttribute("Include", $asmName)
            $newNode.SetAttribute("HintPath", [string]::Concat('$(', $OutputProp, ')\', $asmName, '.dll'))
            $newNode.SetAttribute("Private", "true")
            $node.ParentNode.ReplaceChild($newNode, $node) | Out-Null
            $changed++
        }

        if ($changed -gt 0) {
            $doc.Save($csproj)
            Write-Ok "已改写: $csproj ($changed 处)"

            # 提示：产品需在 Directory.Build.props 定义 $(OutputProp)
            Write-Warn "    → 请确保产品 Directory.Build.props 已定义: <$OutputProp> ... </$OutputProp>（指向 $CsharpRootFull 的权威输出目录）"
        }
    }

    if (-not $NoBackup) {
        Write-Info "备份文件：*.before-csharpprj.*.bak（如需要回滚，请用 -Restore）"
    }
}

# ============================================
#  Restore：回滚最后一次改动
# ============================================
if ($Restore) {
    $bakFiles = Get-ChildItem -Path (Split-Path $targetFull -Parent) -Filter "*.before-csharpprj.*.bak" -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\' }
    if ($bakFiles.Count -eq 0) {
        Write-Info "未找到备份文件，无法回滚。"
    }
    else {
        foreach ($bak in $bakFiles) {
            $orig = $bak.FullName -replace '\.before-csharpprj\.[^.]+\.bak$', ''
            if (Test-Path $orig) {
                Copy-Item $bak.FullName $orig -Force
                Write-Ok "已回滚: $orig"
            }
        }
        Write-Info "回滚完成。如需清理备份文件可手动删除 *.before-csharpprj.*.bak。"
    }
}

# 导出计划
if (-not [string]::IsNullOrWhiteSpace($ReportFile)) {
    $report = @{
        repoRoot       = $repoRoot
        csharpRoot     = $CsharpRootFull
        target         = $targetFull
        outputProp     = $OutputProp
        count          = $plan.Count
        plan           = $plan | ForEach-Object {
            @{
                csproj       = $_.Csproj
                reference    = $_.Reference
                assemblyName = $_.AssemblyName
                hintPath     = $_.HintPath
            }
        }
    }
    $dir = Split-Path $ReportFile -Parent
    if (-not [string]::IsNullOrWhiteSpace($dir) -and -not (Test-Path $dir)) {
        New-Item -Path $dir -ItemType Directory -Force | Out-Null
    }
    ($report | ConvertTo-Json -Depth 6) | Out-File -FilePath $ReportFile -Encoding utf8
    Write-Info "计划已导出: $ReportFile"
}