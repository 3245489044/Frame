$scriptDir = 'D:\Project\Program\WorkFlowPrj\PowerShellScript\Build'
$psmDir = Resolve-Path (Join-Path $scriptDir '..\PScriptManager')
$projectRoot = Resolve-Path (Join-Path $psmDir '..\..')
. (Join-Path $psmDir '_init.ps1')
Write-Host '=== After _init ==='
Write-Host '=== Loading DockerCommon ==='
$reg = Get-Content (Join-Path $psmDir 'psm-registry.json') -Encoding UTF8 -Raw | ConvertFrom-Json
$commonRelPath = $reg.Functions.'Get-ConfigValue'.Path
$commonPath = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $commonRelPath))
Write-Host ('commonPath = ' + $commonPath)
Import-Module $commonPath -Force -DisableNameChecking -ErrorAction Stop
Write-Host '=== DockerCommon loaded ==='
$v = Get-ConfigValue 'dotnetBuilder' 'imageName'
Write-Host ('Get-ConfigValue = ' + $v)
Write-Host '=== Loading DockerBuild ==='
$buildRelPath = $reg.Functions.'Test-DockerAvailable'.Path
$buildPath = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $buildRelPath))
Write-Host ('buildPath = ' + $buildPath)
Import-Module $buildPath -Force -DisableNameChecking -ErrorAction Stop
Write-Host '=== DockerBuild loaded ==='
$r = Test-DockerAvailable
Write-Host ('Test-DockerAvailable = ' + $r)
