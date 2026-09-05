<#
.SYNOPSIS
    安装 Docker Engine CLI 版到 D:\DevEnv，配置 data-root 指向 D 盘
.DESCRIPTION
    自动检测管理员权限，非管理员则提权重启。
    下载 → 解压 → 配置 daemon.json → 注册服务 → 启动 → 验证
#>

#requires -RunAsAdministrator

param(
    [string]$TargetRoot = "D:\DevEnv"
)

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Resolve-Path (Join-Path $scriptPath "..\..\..")
$outputModule = Join-Path $projectRoot "PowerShellScript\BasicMethod\Core\Output.psm1"
if (Test-Path $outputModule) {
    Import-Module $outputModule -Force -DisableNameChecking
}

# ─── 路径 ───
$DockerDir = Join-Path $TargetRoot "DockerEngine"
$DockerDataDir = Join-Path $TargetRoot "DockerData"
$DockerConfigDir = Join-Path $env:ProgramData "Docker"
$DockerConfigPath = Join-Path $DockerConfigDir "config\daemon.json"
$ZipPath = "$env:TEMP\docker-cli.zip"
$DockerUrl = "https://download.docker.com/win/static/stable/x86_64/docker-29.5.3.zip"

# ─── 1. 下载 ───
Write-Step "1/6: 下载 Docker Engine"
if (-not (Test-Path $ZipPath) -or (Get-Item $ZipPath).Length -lt 50MB) {
    Write-Info "下载 $DockerUrl ..."
    Invoke-WebRequest -Uri $DockerUrl -OutFile $ZipPath -UseBasicParsing
}
Write-Success "下载完成: $([math]::Round((Get-Item $ZipPath).Length/1MB,1)) MB"

# ─── 2. 解压 ───
Write-Step "2/6: 解压到 $DockerDir"
if (Test-Path $DockerDir) { Remove-Item $DockerDir -Recurse -Force }
$tempDir = "$env:TEMP\docker-extract"
if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
Expand-Archive -Path $ZipPath -DestinationPath $tempDir -Force
# zip 包自带 docker/ 子目录，把文件移到根目录
if (Test-Path "$tempDir\docker") {
    Move-Item "$tempDir\docker\*" $tempDir -Force
    Remove-Item "$tempDir\docker" -Recurse -Force
}
Move-Item "$tempDir\*" $DockerDir -Force
Remove-Item $tempDir -Recurse -Force
Write-Success "解压完成"

# ─── 3. 创建数据目录 ───
Write-Step "3/6: 创建数据目录"
New-Item $DockerDataDir -ItemType Directory -Force | Out-Null
Write-Success "数据目录: $DockerDataDir"

# ─── 4. 配置 daemon.json ───
Write-Step "4/6: 配置 daemon.json"
New-Item (Split-Path $DockerConfigPath -Parent) -ItemType Directory -Force | Out-Null
@{"data-root" = $DockerDataDir} | ConvertTo-Json | Set-Content $DockerConfigPath -Force
Write-Success "data-root = $DockerDataDir"
Write-Info "配置文件: $DockerConfigPath"

# ─── 5. 注册并启动服务 ───
Write-Step "5/6: 注册 Windows 服务"
$dockerdPath = Join-Path $DockerDir "dockerd.exe"
if (-not (Test-Path $dockerdPath)) { Write-Error "未找到 dockerd.exe"; exit 1 }

sc.exe stop docker 2>$null
sc.exe delete docker 2>$null
$binPath = "`"$dockerdPath`" --run-service -H npipe:////./pipe/docker_engine -H tcp://127.0.0.1:2375"
sc.exe create docker binPath= $binPath start= auto 2>&1 | Out-Null
sc.exe config docker start= auto 2>&1 | Out-Null
Write-Success "服务已注册 (TCP 127.0.0.1:2375)"

Write-Info "启动 Docker 服务..."
sc.exe start docker 2>&1 | Out-Null
Start-Sleep -Seconds 5

# 检查服务状态
$svc = sc.exe query docker 2>&1
if ($svc -match "RUNNING") {
    Write-Success "Docker 服务运行中"
} else {
    Write-Error "服务启动失败，请手动运行: sc.exe start docker"
}

# ─── 6. 配置 PATH ───
Write-Step "6/6: 配置环境变量"
$currentPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($currentPath -notlike "*$DockerDir*") {
    [Environment]::SetEnvironmentVariable("Path", "$currentPath;$DockerDir", "User")
    Write-Success "已添加到用户 PATH"
}

# ─── 验证 ───
Write-Step "验证"
$env:Path = "$DockerDir;$env:Path"
$ver = docker version 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Success "Docker 可用!"
    docker info 2>&1 | Select-String "Root Dir|Data Space|Docker Root" | ForEach-Object { Write-Info $_ }
} else {
    Write-Error "Docker 未就绪: $ver"
}

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  Docker Engine CLI 安装完成!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  📂 安装目录: $DockerDir" -ForegroundColor Cyan
Write-Host "  📂 数据目录: $DockerDataDir" -ForegroundColor Cyan
Write-Host "  📂 配置文件: $DockerConfigPath" -ForegroundColor Cyan
Write-Host ""
Write-Host "  使用: docker version" -ForegroundColor Yellow
