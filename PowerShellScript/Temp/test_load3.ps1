$scriptDir = 'D:\Project\Program\WorkFlowPrj\PowerShellScript\Build'
$psmDir = Resolve-Path (Join-Path $scriptDir '..\PScriptManager')
$projectRoot = Resolve-Path (Join-Path $psmDir '..\..')
. (Join-Path $psmDir '_init.ps1')

$reg = Get-Content (Join-Path $psmDir 'psm-registry.json') -Encoding UTF8 -Raw | ConvertFrom-Json

# 1. 加载 DockerCommon
$commonRelPath = $reg.Functions.'Get-ConfigValue'.Path
$commonPath = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $commonRelPath))
Import-Module $commonPath -Force -DisableNameChecking -ErrorAction Stop
Write-Host 'DockerCommon loaded' -ForegroundColor Green

# 2. 测试 Get-ConfigValue 在全局作用域中
$v = Get-ConfigValue 'dotnetBuilder' 'imageName'
Write-Host "Get-ConfigValue = '$v'" -ForegroundColor Green

# 3. 检查 Get-ConfigValue 是否在全局作用域中
$cmd = Get-Command Get-ConfigValue -ErrorAction SilentlyContinue
Write-Host "Get-ConfigValue source: $($cmd.Source)" -ForegroundColor Yellow
Write-Host "Get-ConfigValue module: $($cmd.ModuleName)" -ForegroundColor Yellow

# 4. 模拟脚本中的 Import-DockerBuildModule
$buildRelPath = $reg.Functions.'Test-DockerAvailable'.Path
$buildPath = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $buildRelPath))
Write-Host "buildPath = $buildPath" -ForegroundColor Yellow

# 5. 在 Import-Module 之前检查 Get-ConfigValue
Write-Host "Before Import-Module DockerBuild:" -ForegroundColor Cyan
$cmd2 = Get-Command Get-ConfigValue -ErrorAction SilentlyContinue
Write-Host "  Get-ConfigValue exists: $($null -ne $cmd2)" -ForegroundColor Yellow

# 6. Import-Module DockerBuild
Write-Host "Importing DockerBuild..." -ForegroundColor Yellow
try {
    Import-Module $buildPath -Force -DisableNameChecking -ErrorAction Stop
    Write-Host 'DockerBuild loaded successfully' -ForegroundColor Green
    $r = Test-DockerAvailable
    Write-Host "Test-DockerAvailable = $r" -ForegroundColor Green
} catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
    Write-Host "Category: $($_.CategoryInfo)" -ForegroundColor Red
    Write-Host "ScriptStackTrace: $($_.ScriptStackTrace)" -ForegroundColor Red
}
