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

# 2. 测试 Get-ConfigValue
$v = Get-ConfigValue 'dotnetBuilder' 'imageName'
Write-Host "Get-ConfigValue = '$v'" -ForegroundColor Green

# 3. 定义 Import-DockerBuildModule 函数（模拟脚本中的定义）
$script:DockerBuildLoaded = $false
function Import-DockerBuildModule {
    if ($script:DockerBuildLoaded) { return }
    $buildRelPath = $reg.Functions.'Test-DockerAvailable'.Path
    $buildPath = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $buildRelPath))
    Write-Host "buildPath = $buildPath" -ForegroundColor Yellow
    Import-Module $buildPath -Force -DisableNameChecking -ErrorAction Stop
    $script:DockerBuildLoaded = $true
}

# 4. 调用 Import-DockerBuildModule
Write-Host 'Calling Import-DockerBuildModule...' -ForegroundColor Yellow
try {
    Import-DockerBuildModule
    Write-Host 'DockerBuild loaded successfully' -ForegroundColor Green
    $r = Test-DockerAvailable
    Write-Host "Test-DockerAvailable = $r" -ForegroundColor Green
} catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
    Write-Host "Category: $($_.CategoryInfo)" -ForegroundColor Red
}
