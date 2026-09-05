<#
.SYNOPSIS
    ComfyUI 漫画套件 - 分批下载/安装脚本（无 UI，纯控制台）
.DESCRIPTION
    复用 Install Framework 的 Manifest + Provider 模式：
      - 读取 comfyui-manifest.json
      - 自动探测已安装项（skip-if-exists）
      - 按 phase 分批执行（Phase1 环境/框架，Phase2 节点，Phase3 模型）
      - Provider: venv / pip / pip-req / git-clone / http-file
      - 支持断点续传、强制重装、单 phase 执行、只预览

    用法:
      powershell -ExecutionPolicy Bypass -File .\comfyui-download.ps1 -Manifest .\comfyui-manifest.json
      powershell -ExecutionPolicy Bypass -File .\comfyui-download.ps1 -Phase 1
      powershell -ExecutionPolicy Bypass -File .\comfyui-download.ps1 -Name ipadapter-sdxl-model -Force
.PARAMETER Manifest
    manifest JSON 路径（默认与脚本同目录）
.PARAMETER Phase
    只执行某个 phase（默认全部）
.PARAMETER Name
    只安装单个 item
.PARAMETER Force
    强制重装（跳过已安装检查）
.PARAMETER WhatIf
    只预览，不实际执行
#>
[CmdletBinding()]
param(
    [string]$Manifest = "",
    [int]$Phase = 0,
    [string]$Name = "",
    [switch]$Force,
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Manifest) { $Manifest = Join-Path $scriptDir "comfyui-manifest.json" }

# ============================================================
# 输出辅助（纯文本，无 UI）
# ============================================================
function Write-Step { param([string]$m) Write-Host "  == $m" -ForegroundColor Cyan }
function Write-Ok   { param([string]$m) Write-Host "  [OK] $m" -ForegroundColor Green }
function Write-Warn { param([string]$m) Write-Host "  [!!] $m" -ForegroundColor Yellow }
function Write-Err  { param([string]$m) Write-Host "  [XX] $m" -ForegroundColor Red }

# ============================================================
# 执行器：直接调用可执行文件，返回退出码
#   避免「嵌套 powershell -Command 字符串拼接」带来的
#   引号/$null 展开/重定向等脆弱问题。
# ============================================================
function Invoke-Exe {
    param(
        [string]$FilePath,
        [string[]]$ArgList
    )
    if ($WhatIf) {
        Write-Host "    (WhatIf) $FilePath $($ArgList -join ' ')" -ForegroundColor DarkGray
        return 0
    }
    try {
        # Out-Host：原生命令输出直接进控制台，避免污染函数返回值(退出码)
        & $FilePath @ArgList | Out-Host
    } catch {
        Write-Host "    (执行出错) $_" -ForegroundColor Red
        return 1
    }
    return $LASTEXITCODE
}

function Get-VenvPython {
    param([string]$VenvDir)
    return Join-Path $VenvDir "Scripts\python.exe"
}

# ============================================================
# 已安装探测
# ============================================================
function Test-Installed {
    param([object]$Item)
    $t = $Item.type
    $dir = $Item.installDir

    switch ($t) {
        "venv" {
            return Test-Path (Get-VenvPython -VenvDir $dir)
        }
        "pip" {
            $py = Get-VenvPython -VenvDir $dir
            if (-not $py -or -not (Test-Path $py)) { return $false }
            foreach ($pkg in ($Item.source -split " ")) {
                if ($pkg -eq "torch") { continue }  # torch 单独探测
                Invoke-Exe $py @("-m", "pip", "show", $pkg) > $null
                if ($LASTEXITCODE -ne 0) { return $false }
            }
            # torch 探测 CUDA 可用性
            Invoke-Exe $py @("-c", "import torch;print('ok')") > $null
            return ($LASTEXITCODE -eq 0)
        }
        "pip-req" {
            $py = Get-VenvPython -VenvDir $dir
            return (Test-Path $py)
        }
        "git-clone" {
            if (-not (Test-Path $dir)) { return $false }
            foreach ($f in ($Item.requiredFiles | ForEach-Object { Join-Path $dir $_ })) {
                if (-not (Test-Path $f)) { return $false }
            }
            return $true
        }
        "http-file" {
            $target = Join-Path $dir $Item.fileName
            return (Test-Path $target)
        }
        default { return $false }
    }
}

# ============================================================
# 版本获取（git 仓库取 HEAD）
# ============================================================
function Get-Version {
    param([object]$Item)
    if ($Item.type -eq "git-clone" -and (Test-Path $Item.installDir)) {
        $v = Invoke-Exe "git" @("rev-parse", "--short", "HEAD") | Out-String
        if ($LASTEXITCODE -eq 0 -and $v) { return ("commit " + $v.Trim()) }
    }
    return $null
}

# ============================================================
# 安装实现（各 Provider）
# ============================================================
function Invoke-Install {
    param([object]$Item)
    $t = $Item.type
    $dir = $Item.installDir
    $venvPy = Get-VenvPython -VenvDir $dir

    try {
        switch ($t) {
            "venv" {
                Write-Step "创建 venv: $dir"
                $parent = Split-Path $dir -Parent
                if (-not (Test-Path $parent)) {
                    New-Item -ItemType Directory -Force -Path $parent | Out-Null
                }
                $ec = Invoke-Exe "python" @("-m", "venv", "$dir")
                if ($ec -ne 0) { throw "venv 创建失败 (exit $ec)" }
                Write-Ok "venv 就绪"
            }
            "pip" {
                $pkgs = @($Item.source -split " " | Where-Object { $_ })
                $extra = @($Item.pipExtra -split " " | Where-Object { $_ })
                if (-not (Test-Path $venvPy)) { throw "venv 不存在: $venvPy" }
                Write-Step "pip 安装: $($pkgs -join ' ') $($extra -join ' ')"
                $ec = Invoke-Exe $venvPy @("-m", "pip", "install", "--upgrade", "pip")
                if ($ec -ne 0) { throw "pip 升级失败 (exit $ec)" }
                $installArgs = @("-m", "pip", "install") + $extra + $pkgs
                $ec = Invoke-Exe $venvPy $installArgs
                if ($ec -ne 0) { throw "pip 安装失败 (exit $ec)" }
                Write-Ok "已安装: $($pkgs -join ' ')"
            }
            "pip-req" {
                $req = $Item.source
                if (-not (Test-Path $venvPy)) { throw "venv 不存在: $venvPy" }
                Write-Step "pip 安装依赖: $req"
                $ec = Invoke-Exe $venvPy @("-m", "pip", "install", "-r", $req)
                if ($ec -ne 0) { throw "requirements 安装失败 (exit $ec)" }
                Write-Ok "依赖已安装"
            }
            "git-clone" {
                $repo = $Item.source
                $branch = if ($Item.branch) { $Item.branch } else { "main" }
                Write-Step "git clone: $repo"
                if (Test-Path $dir) {
                    Remove-Item $dir -Recurse -Force
                }
                $parent = Split-Path $dir -Parent
                if (-not (Test-Path $parent)) {
                    New-Item -ItemType Directory -Force -Path $parent | Out-Null
                }
                $ec = Invoke-Exe "git" @("clone", "--depth", "1", "--branch", "$branch", $repo, $dir)
                if ($ec -ne 0) { throw "git clone 失败 (exit $ec)" }
                Write-Ok "克隆完成: $($Item.name)"
            }
            "http-file" {
                $url = $Item.source
                $file = $Item.fileName
                if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
                $target = Join-Path $dir $file
                $tmp = "$target.part"
                Write-Step "下载: $url"
                # 断点续传 curl.exe（Windows 自带）
                #   -C -            从已有 .part 续传
                #   --connect-timeout 连接超时
                #   --speed-limit/--speed-time 速度低于阈值即中断，交由失败重试循环续传
                $maxRetry = 8
                $try = 0
                while ($true) {
                    $try++
                    $ec = Invoke-Exe "curl.exe" @(
                        "-L", "--continue-at", "-",
                        "--connect-timeout", "20",
                        "--speed-limit", "1024", "--speed-time", "60",
                        "-o", $tmp, $url
                    )
                    if ($ec -eq 0) { break }
                    if ($try -ge $maxRetry) { throw "下载失败(已重试 $maxRetry 次) exit $ec" }
                    $gotMB = if (Test-Path $tmp) { [math]::Round((Get-Item $tmp).Length / 1MB, 1) } else { 0 }
                    Write-Warn "下载中断(exit=$ec)，已获取 $gotMB MB，${try}/$maxRetry 续传重试..."
                    Start-Sleep -Seconds 3
                }
                Move-Item $tmp $target -Force
                $size = [math]::Round((Get-Item $target).Length / 1MB, 1)
                Write-Ok "下载完成: $file ($size MB)"
            }
            default { throw "未知 Provider 类型: $t" }
        }
        return $true
    } catch {
        Write-Err "安装失败: $_"
        return $false
    }
}

# ============================================================
# 主流程
# ============================================================
Write-Host ""
Write-Host "╔════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  ComfyUI 漫画套件 - 分批下载/安装          ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $Manifest)) {
    Write-Err "manifest 不存在: $Manifest"
    exit 1
}

# $cfg: parsed manifest object; $Manifest keeps the file path
$cfg = [System.IO.File]::ReadAllText($Manifest, [System.Text.Encoding]::UTF8) | ConvertFrom-Json

# 过滤项目
$items = @($cfg.items)
if ($Phase -gt 0) { $items = @($items | Where-Object { $_.phase -eq $Phase }) }
if ($Name) { $items = @($items | Where-Object { $_.name -eq $Name }) }

if ($items.Count -eq 0) {
    Write-Warn "没有匹配的项目（Phase=$Phase Name=$Name）"
    exit 0
}

Write-Host "待安装项目: $($items.Count) 个  目标目录: $($cfg.comfyRoot)" -ForegroundColor Gray
Write-Host ""

$result = @{ Success = 0; Failed = 0; Skipped = 0 }
foreach ($item in $items) {
    $label = "[Phase$($item.phase)] $($item.name) ($($item.type))"
    if (Test-Installed -Item $item) {
        if (-not $Force) {
            Write-Warn "$label 已安装，跳过（用 -Force 重装）"
            $result.Skipped++
            continue
        }
        Write-Host "  (Force) $label 重新安装" -ForegroundColor Magenta
    } else {
        Write-Host "  → $label" -ForegroundColor White
    }

    if (Invoke-Install -Item $item) {
        $result.Success++
    } else {
        $result.Failed++
    }
    Write-Host ""
}

# 摘要
Write-Host ""
Write-Host "===== 摘要 =====" -ForegroundColor Yellow
Write-Host "  成功: $($result.Success)" -ForegroundColor Green
Write-Host "  跳过: $($result.Skipped)" -ForegroundColor Yellow
Write-Host "  失败: $($result.Failed)" -ForegroundColor Red
Write-Host ""

if ($result.Failed -gt 0) { exit 1 }
exit 0