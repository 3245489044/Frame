# Import-Modules.ps1 - IoC 模块加载器
# 支持两种模式：
#   模式 A（新）：按需加载 — 脚本头部声明 @requires，加载器按需提供
#   模式 B（旧）：全量加载 — 无 @requires 时回退到原有的全量加载逻辑
#
# ★ 使用方式（按需加载）：
#   在脚本头部添加 @requires 注释：
#   <#
#   .SYNOPSIS
#       脚本描述
#   # @requires Write-Header, Write-Success, Confirm-Action
#   #>
#   . (Join-Path $PSScriptRoot "..\Modules\Import-Modules.ps1")
#
# ★ 使用方式（全量加载，兼容旧脚本）：
#   . (Join-Path $PSScriptRoot "..\Modules\Import-Modules.ps1")
# ============================================================
# 函数→模块映射表（IoC 核心）— 编译产物
# 由 DSL\Build-ModuleIndex.ps1 从源 .psm1 Export-ModuleMember 扫描生成，禁止手改
# 新增/重命名/删除函数后，重新运行：.\\DSL\\Build-ModuleIndex.ps1
# <!-- PROTO_AUTO:functionModuleMap:BEGIN -->
# ============================================================
$functionModuleMap = @{

    # ===== BasicMethod\Core =====
    'Get-FileEncoding'     = 'BasicMethod/Core/FileUtils.psm1'
    'Get-PSEncoding'       = 'BasicMethod/Core/FileUtils.psm1'

    # ===== BasicMethod\Core =====
    'Convert-PsCustomObjectToHashtable'     = 'BasicMethod/Core/Json.psm1'
    'Get-ConfigValue'                       = 'BasicMethod/Core/Json.psm1'
    'Load-JsonConfig'                       = 'BasicMethod/Core/Json.psm1'

    # ===== BasicMethod\Core =====
    'Test-HttpEndpoint'     = 'BasicMethod/Core/Network.psm1'
    'Test-PortPing'         = 'BasicMethod/Core/Network.psm1'
    'Test-TcpPort'          = 'BasicMethod/Core/Network.psm1'

    # ===== BasicMethod\Core =====
    'Get-StatusIcon'        = 'BasicMethod/Core/Output.psm1'
    'Pause-Menu'            = 'BasicMethod/Core/Output.psm1'
    'Write-Banner'          = 'BasicMethod/Core/Output.psm1'
    'Write-Hint'            = 'BasicMethod/Core/Output.psm1'
    'Write-MenuHeader'      = 'BasicMethod/Core/Output.psm1'
    'Write-MenuItem'        = 'BasicMethod/Core/Output.psm1'
    'Write-Result'          = 'BasicMethod/Core/Output.psm1'
    'Write-ResultTable'     = 'BasicMethod/Core/Output.psm1'
    'Write-Section'         = 'BasicMethod/Core/Output.psm1'
    'Write-Step'            = 'BasicMethod/Core/Output.psm1'
    'Write-StepTitle'       = 'BasicMethod/Core/Output.psm1'
    'Write-SubSection'      = 'BasicMethod/Core/Output.psm1'
    'Write-Warn'            = 'BasicMethod/Core/Output.psm1'
    'Write-WhatIf'          = 'BasicMethod/Core/Output.psm1'

    # ===== BasicMethod\Core =====
    'Convert-ContainerPathToHost'     = 'BasicMethod/Core/Path.psm1'
    'Convert-HostPathToContainer'     = 'BasicMethod/Core/Path.psm1'
    'Normalize-Path'                  = 'BasicMethod/Core/Path.psm1'
    'Resolve-ProjectPath'             = 'BasicMethod/Core/Path.psm1'
    'Test-IsContainerPath'            = 'BasicMethod/Core/Path.psm1'

    # ===== BasicMethod\Core =====
    'Import-ModuleByPath'        = 'BasicMethod/Core/Utils.psm1'
    'Initialize-BasicMethod'     = 'BasicMethod/Core/Utils.psm1'
    'Test-AdminPrivilege'        = 'BasicMethod/Core/Utils.psm1'

    # ===== BasicMethod\DevOps =====
    'Build-DockerImage'            = 'BasicMethod/DevOps/Docker.psm1'
    'Copy-FileToContainer'         = 'BasicMethod/DevOps/Docker.psm1'
    'Get-ContainerHealth'          = 'BasicMethod/DevOps/Docker.psm1'
    'Get-ContainerInfo'            = 'BasicMethod/DevOps/Docker.psm1'
    'Get-ContainerLogs'            = 'BasicMethod/DevOps/Docker.psm1'
    'Get-ContainerState'           = 'BasicMethod/DevOps/Docker.psm1'
    'Get-ContainerUptime'          = 'BasicMethod/DevOps/Docker.psm1'
    'Get-DockerImageInfo'          = 'BasicMethod/DevOps/Docker.psm1'
    'Get-DockerVersion'            = 'BasicMethod/DevOps/Docker.psm1'
    'Get-DockerVolumeArgs'         = 'BasicMethod/DevOps/Docker.psm1'
    'Invoke-ContainerScript'       = 'BasicMethod/DevOps/Docker.psm1'
    'Invoke-DockerCommand'         = 'BasicMethod/DevOps/Docker.psm1'
    'New-DockerContainer'          = 'BasicMethod/DevOps/Docker.psm1'
    'Remove-ContainerFile'         = 'BasicMethod/DevOps/Docker.psm1'
    'Remove-DockerContainer'       = 'BasicMethod/DevOps/Docker.psm1'
    'Test-ContainerExists'         = 'BasicMethod/DevOps/Docker.psm1'
    'Test-ContainerRunning'        = 'BasicMethod/DevOps/Docker.psm1'
    'Test-DockerAvailable'         = 'BasicMethod/DevOps/Docker.psm1'
    'Test-DockerEngineRunning'     = 'BasicMethod/DevOps/Docker.psm1'
    'Test-DockerLinuxMode'         = 'BasicMethod/DevOps/Docker.psm1'
    'Test-DockerVolumeMount'       = 'BasicMethod/DevOps/Docker.psm1'
    'Test-ImageExists'             = 'BasicMethod/DevOps/Docker.psm1'
    'Test-PortHttpReachable'       = 'BasicMethod/DevOps/Docker.psm1'
    'Test-PortInContainer'         = 'BasicMethod/DevOps/Docker.psm1'
    'Wait-ContainerReady'          = 'BasicMethod/DevOps/Docker.psm1'

    # ===== BasicMethod\DevOps =====
    'Invoke-GitCloneAndBuild'     = 'BasicMethod/DevOps/GitClone.psm1'

    # ===== BasicMethod\DevOps =====
    'Find-GitHubReleaseAsset'        = 'BasicMethod/DevOps/GitHubRelease.psm1'
    'Get-GitHubLatestRelease'        = 'BasicMethod/DevOps/GitHubRelease.psm1'
    'Install-GitHubRelease'          = 'BasicMethod/DevOps/GitHubRelease.psm1'
    'Install-GitHubReleaseToDir'     = 'BasicMethod/DevOps/GitHubRelease.psm1'

    # ===== BasicMethod\DevOps =====
    'Get-MCPServiceList'        = 'BasicMethod/DevOps/MCP.psm1'
    'Get-MCPServicePortMap'     = 'BasicMethod/DevOps/MCP.psm1'
    'Test-McpEndpoint'          = 'BasicMethod/DevOps/MCP.psm1'

    # ===== BasicMethod\DevOps =====
    'Get-NpmPackageVersion'        = 'BasicMethod/DevOps/PackageManager.psm1'
    'Get-PipPackageVersion'        = 'BasicMethod/DevOps/PackageManager.psm1'
    'Invoke-NpmInstall'            = 'BasicMethod/DevOps/PackageManager.psm1'
    'Invoke-NpmUninstall'          = 'BasicMethod/DevOps/PackageManager.psm1'
    'Invoke-NpmUpdate'             = 'BasicMethod/DevOps/PackageManager.psm1'
    'Invoke-PipInstall'            = 'BasicMethod/DevOps/PackageManager.psm1'
    'Invoke-PipUninstall'          = 'BasicMethod/DevOps/PackageManager.psm1'
    'Invoke-PipUpdate'             = 'BasicMethod/DevOps/PackageManager.psm1'
    'Test-NpmPackageInstalled'     = 'BasicMethod/DevOps/PackageManager.psm1'
    'Test-PipPackageInstalled'     = 'BasicMethod/DevOps/PackageManager.psm1'

    # ===== BasicMethod\FileOps =====
    'Clear-DownloadCache'     = 'BasicMethod/FileOps/Download.psm1'
    'Get-FileHashValue'       = 'BasicMethod/FileOps/Download.psm1'
    'Invoke-FileDownload'     = 'BasicMethod/FileOps/Download.psm1'

    # ===== BasicMethod\FileOps =====
    'Analyze-File'       = 'BasicMethod/FileOps/FileAccess.psm1'
    'List-Directory'     = 'BasicMethod/FileOps/FileAccess.psm1'
    'Read-File'          = 'BasicMethod/FileOps/FileAccess.psm1'
    'Replace-InFile'     = 'BasicMethod/FileOps/FileAccess.psm1'
    'Search-Files'       = 'BasicMethod/FileOps/FileAccess.psm1'
    'Write-File'         = 'BasicMethod/FileOps/FileAccess.psm1'

    # ===== BasicMethod\FileOps =====
    'BatchRead-File'                 = 'BasicMethod/FileOps/FileBatch.psm1'
    'BatchReplace-InFile'            = 'BasicMethod/FileOps/FileBatch.psm1'
    'BatchWrite-File'                = 'BasicMethod/FileOps/FileBatch.psm1'
    'Invoke-ContextAwareReplace'     = 'BasicMethod/FileOps/FileBatch.psm1'

    # ===== BasicMethod\FileOps =====
    'Confirm-Replacement'       = 'BasicMethod/FileOps/FileDiff.psm1'
    'ConvertTo-SearchBlock'     = 'BasicMethod/FileOps/FileDiff.psm1'
    'Restore-File'              = 'BasicMethod/FileOps/FileDiff.psm1'
    'Show-FileDiff'             = 'BasicMethod/FileOps/FileDiff.psm1'

    # ===== BasicMethod\FileOps =====
    'List-Dir'     = 'BasicMethod/FileOps/FileListing.psm1'

    # ===== BasicMethod\Storage =====
    'Store-Resource'     = 'BasicMethod/Storage/StorageNode.psm1'

    # ===== BasicMethod\Tooling =====
    'Export-DiagnosticJson'       = 'BasicMethod/Tooling/Diagnostic.psm1'
    'New-DiagnosticContext'       = 'BasicMethod/Tooling/Diagnostic.psm1'
    'Save-DiagnosticReport'       = 'BasicMethod/Tooling/Diagnostic.psm1'
    'Show-DiagnosticSummary'      = 'BasicMethod/Tooling/Diagnostic.psm1'
    'Start-ContinuousMonitor'     = 'BasicMethod/Tooling/Diagnostic.psm1'
    'Write-Diagnostic'            = 'BasicMethod/Tooling/Diagnostic.psm1'

    # ===== BasicMethod\Tooling =====
    'Get-GitStatus'                 = 'BasicMethod/Tooling/ToolBridge.psm1'
    'Invoke-CliCommand'             = 'BasicMethod/Tooling/ToolBridge.psm1'
    'Invoke-CtxBatchExecute'        = 'BasicMethod/Tooling/ToolBridge.psm1'
    'Invoke-CtxExecute'             = 'BasicMethod/Tooling/ToolBridge.psm1'
    'Invoke-CtxExecuteFile'         = 'BasicMethod/Tooling/ToolBridge.psm1'
    'Invoke-McpRequest'             = 'BasicMethod/Tooling/ToolBridge.psm1'
    'Test-McpServiceInitialize'     = 'BasicMethod/Tooling/ToolBridge.psm1'
    'Test-McpServiceToolCall'       = 'BasicMethod/Tooling/ToolBridge.psm1'
    'Test-McpServiceToolsList'      = 'BasicMethod/Tooling/ToolBridge.psm1'

    # ===== Md =====
    'Invoke-MarkdownLintFix'     = 'Md/MarkdownLintFix.psm1'

    # ===== Modules =====
    'Check-OutputDirectoryIntegrity'          = 'Modules/BuildOutputCleaner.psm1'
    'Check-TargetFrameworkSubdirectories'     = 'Modules/BuildOutputCleaner.psm1'
    'Get-ObsoleteBuildOutputDirectories'      = 'Modules/BuildOutputCleaner.psm1'
    'Show-IntegrityCheckResult'               = 'Modules/BuildOutputCleaner.psm1'

    # ===== Modules\Core =====
    'Get-BatToolAliases'      = 'Modules/Core/BatToolConfig.psm1'
    'Show-BatToolAliases'     = 'Modules/Core/BatToolConfig.psm1'

    # ===== Modules\Core =====
    'Confirm-Action'               = 'Modules/Core/BuildHelpers.psm1'
    'Format-BuildOutput'           = 'Modules/Core/BuildHelpers.psm1'
    'Invoke-Step'                  = 'Modules/Core/BuildHelpers.psm1'
    'Invoke-VerificationCheck'     = 'Modules/Core/BuildHelpers.psm1'
    'Test-CommandExists'           = 'Modules/Core/BuildHelpers.psm1'

    # ===== Modules\Core =====
    'Backup-File'                    = 'Modules/Core/FileSystemHelpers.psm1'
    'Get-BinObjDirectories'          = 'Modules/Core/FileSystemHelpers.psm1'
    'Get-ChildDirectoriesByName'     = 'Modules/Core/FileSystemHelpers.psm1'
    'Get-CsprojFiles'                = 'Modules/Core/FileSystemHelpers.psm1'
    'Get-DirectorySizeMB'            = 'Modules/Core/FileSystemHelpers.psm1'
    'Remove-DirectoryWithLog'        = 'Modules/Core/FileSystemHelpers.psm1'
    'Test-ProjectReferences'         = 'Modules/Core/FileSystemHelpers.psm1'

    # ===== Modules\Core =====
    'Confirm-And-DeleteDirectories'     = 'Modules/Core/InteractiveHelpers.psm1'
    'Confirm-And-FixProjects'           = 'Modules/Core/InteractiveHelpers.psm1'
    'Group-ByFilePath'                  = 'Modules/Core/InteractiveHelpers.psm1'

    # ===== Modules\Core =====
    'Show-FixSummary'     = 'Modules/Core/OutputHelpers.psm1'
    'Write-Diff'          = 'Modules/Core/OutputHelpers.psm1'
    'Write-Error'         = 'Modules/Core/OutputHelpers.psm1'
    'Write-Header'        = 'Modules/Core/OutputHelpers.psm1'
    'Write-Info'          = 'Modules/Core/OutputHelpers.psm1'
    'Write-SubHeader'     = 'Modules/Core/OutputHelpers.psm1'
    'Write-Success'       = 'Modules/Core/OutputHelpers.psm1'
    'Write-Warning'       = 'Modules/Core/OutputHelpers.psm1'

    # ===== Modules\Core =====
    'Get-GlobalTargetFramework'         = 'Modules/Core/ProjectConfig.psm1'
    'Get-InvalidTfmMap'                 = 'Modules/Core/ProjectConfig.psm1'
    'Get-ProjectConfig'                 = 'Modules/Core/ProjectConfig.psm1'
    'Get-RedundantPlatforms'            = 'Modules/Core/ProjectConfig.psm1'
    'Get-WinFormsDependentProjects'     = 'Modules/Core/ProjectConfig.psm1'

    # ===== Modules\Core =====
    'Get-CsharpProjectRoot'             = 'Modules/Core/ProjectPaths.psm1'
    'Get-LauncherOutputDir'             = 'Modules/Core/ProjectPaths.psm1'
    'Get-PathAliases'                   = 'Modules/Core/ProjectPaths.psm1'
    'Get-ProjectDirectories'            = 'Modules/Core/ProjectPaths.psm1'
    'Get-ProjectRoot'                   = 'Modules/Core/ProjectPaths.psm1'
    'Reset-ProjectRootCache'            = 'Modules/Core/ProjectPaths.psm1'
    'Set-AliasEnvironmentFromPaths'     = 'Modules/Core/ProjectPaths.psm1'

    # ===== Modules\Core =====
    'Add-XmlElementAfterSibling'      = 'Modules/Core/XmlHelpers.psm1'
    'Get-CsprojPropertyValues'        = 'Modules/Core/XmlHelpers.psm1'
    'Get-MsbuildNamespaceManager'     = 'Modules/Core/XmlHelpers.psm1'
    'Read-XmlFile'                    = 'Modules/Core/XmlHelpers.psm1'
    'Remove-XmlElementLines'          = 'Modules/Core/XmlHelpers.psm1'
    'Select-MsbuildNodes'             = 'Modules/Core/XmlHelpers.psm1'
    'Test-ContentHasElement'          = 'Modules/Core/XmlHelpers.psm1'
    'Test-XmlElementExists'           = 'Modules/Core/XmlHelpers.psm1'
    'Update-XmlElementValue'          = 'Modules/Core/XmlHelpers.psm1'

    # ===== Modules\Csproj =====
    'Get-ProjectLocalBuildOutput'          = 'Modules/Csproj/CsprojDetection.psm1'
    'Get-TargetFrameworkConfiguration'     = 'Modules/Csproj/CsprojDetection.psm1'

    # ===== Modules\Csproj =====
    'Fix-CustomOutputPath'                     = 'Modules/Csproj/CsprojFixActions.psm1'
    'Fix-InvalidTargetFramework'               = 'Modules/Csproj/CsprojFixActions.psm1'
    'Fix-MissingUseWindowsForms'               = 'Modules/Csproj/CsprojFixActions.psm1'
    'Fix-RedundantTargetFramework'             = 'Modules/Csproj/CsprojFixActions.psm1'
    'Fix-SolutionDirInDirectoryBuildProps'     = 'Modules/Csproj/CsprojFixActions.psm1'

    # ===== Modules\Csproj =====
    'Initialize-NewProject'     = 'Modules/Csproj/CsprojFixEngine.psm1'
    'Invoke-CsprojRepair'       = 'Modules/Csproj/CsprojFixEngine.psm1'

    # ===== Modules\Docker =====
    'Ensure-DockerImage'         = 'Modules/Docker/DockerBuild.psm1'
    'Enter-DockerContainer'      = 'Modules/Docker/DockerBuild.psm1'
    'Get-DockerBuildStatus'      = 'Modules/Docker/DockerBuild.psm1'
    'Invoke-DotnetCommand'       = 'Modules/Docker/DockerBuild.psm1'
    'Test-DockerImageExists'     = 'Modules/Docker/DockerBuild.psm1'

    # ===== Modules\Docker =====
    'Get-BackupDir'                = 'Modules/Docker/DockerCommon.psm1'
    'Get-ClineConfigPath'          = 'Modules/Docker/DockerCommon.psm1'
    'Get-ComposeFile'              = 'Modules/Docker/DockerCommon.psm1'
    'Get-ContainerName'            = 'Modules/Docker/DockerCommon.psm1'
    'Get-DockerConfigDir'          = 'Modules/Docker/DockerCommon.psm1'
    'Get-DockerDataDir'            = 'Modules/Docker/DockerCommon.psm1'
    'Get-DockerLogDir'             = 'Modules/Docker/DockerCommon.psm1'
    'Get-DockerServiceName'        = 'Modules/Docker/DockerCommon.psm1'
    'Get-ImageName'                = 'Modules/Docker/DockerCommon.psm1'
    'Get-McpServers'               = 'Modules/Docker/DockerCommon.psm1'
    'Get-PortEnd'                  = 'Modules/Docker/DockerCommon.psm1'
    'Get-PortStart'                = 'Modules/Docker/DockerCommon.psm1'
    'Import-DockerBuildModule'     = 'Modules/Docker/DockerCommon.psm1'

    # ===== Modules\Docker =====
    'Get-DockerLogs'              = 'Modules/Docker/DockerManagement.psm1'
    'Get-DockerStats'             = 'Modules/Docker/DockerManagement.psm1'
    'Get-DockerStatus'            = 'Modules/Docker/DockerManagement.psm1'
    'Repair-DockerEngine'         = 'Modules/Docker/DockerManagement.psm1'
    'Restart-DockerContainer'     = 'Modules/Docker/DockerManagement.psm1'
    'Start-DockerContainer'       = 'Modules/Docker/DockerManagement.psm1'
    'Stop-DockerContainer'        = 'Modules/Docker/DockerManagement.psm1'
    'Test-DockerEnvironment'      = 'Modules/Docker/DockerManagement.psm1'
    'Test-McpServices'            = 'Modules/Docker/DockerManagement.psm1'
    'Update-DockerImage'          = 'Modules/Docker/DockerManagement.psm1'

    # ===== Modules\Docker =====
    'Add-McpServerConfig'          = 'Modules/Docker/McpConfiguration.psm1'
    'Backup-ClineMcpConfig'        = 'Modules/Docker/McpConfiguration.psm1'
    'Disable-McpServer'            = 'Modules/Docker/McpConfiguration.psm1'
    'Enable-McpServer'             = 'Modules/Docker/McpConfiguration.psm1'
    'Get-ClineMcpBackupList'       = 'Modules/Docker/McpConfiguration.psm1'
    'Get-ClineMcpConfig'           = 'Modules/Docker/McpConfiguration.psm1'
    'Remove-McpServerConfig'       = 'Modules/Docker/McpConfiguration.psm1'
    'Restore-ClineMcpConfig'       = 'Modules/Docker/McpConfiguration.psm1'
    'Set-ClineMcpConfig'           = 'Modules/Docker/McpConfiguration.psm1'
    'Show-ClineMcpConfig'          = 'Modules/Docker/McpConfiguration.psm1'
    'Sync-McpConfigFromDocker'     = 'Modules/Docker/McpConfiguration.psm1'
    'Test-ClineMcpConfig'          = 'Modules/Docker/McpConfiguration.psm1'

    # ===== Modules\Docker =====
    'Get-McpServerList'           = 'Modules/Docker/McpInstallation.psm1'
    'Get-McpServerVersion'        = 'Modules/Docker/McpInstallation.psm1'
    'Install-AllMcpServers'       = 'Modules/Docker/McpInstallation.psm1'
    'Install-McpServer'           = 'Modules/Docker/McpInstallation.psm1'
    'Show-McpServerStatus'        = 'Modules/Docker/McpInstallation.psm1'
    'Test-McpServerInstalled'     = 'Modules/Docker/McpInstallation.psm1'
    'Uninstall-McpServer'         = 'Modules/Docker/McpInstallation.psm1'
    'Update-AllMcpServers'        = 'Modules/Docker/McpInstallation.psm1'
    'Update-McpServer'            = 'Modules/Docker/McpInstallation.psm1'

    # ===== Modules\StringTools =====
    'ConvertFrom-CrReference'           = 'Modules/StringTools/StringTools.Clinerules.psm1'
    'ConvertFrom-LegacyCrReference'     = 'Modules/StringTools/StringTools.Clinerules.psm1'
    'Get-CrReferenceTokens'             = 'Modules/StringTools/StringTools.Clinerules.psm1'
    'Resolve-DefinitionForm'            = 'Modules/StringTools/StringTools.Clinerules.psm1'

    # ===== Modules\StringTools =====
    'Convert-NameCase'                   = 'Modules/StringTools/StringTools.Core.psm1'
    'ConvertTo-CompoundKey'              = 'Modules/StringTools/StringTools.Core.psm1'
    'Get-FirstMatchingPrefix'            = 'Modules/StringTools/StringTools.Core.psm1'
    'Get-NameHead'                       = 'Modules/StringTools/StringTools.Core.psm1'
    'Invoke-BracketAwareReplace'         = 'Modules/StringTools/StringTools.Core.psm1'
    'Invoke-BracketAwareSplit'           = 'Modules/StringTools/StringTools.Core.psm1'
    'Invoke-FilterLines'                 = 'Modules/StringTools/StringTools.Core.psm1'
    'Invoke-RegexExtract'                = 'Modules/StringTools/StringTools.Core.psm1'
    'Invoke-RegexExtractToHashtable'     = 'Modules/StringTools/StringTools.Core.psm1'
    'Invoke-RegexReplace'                = 'Modules/StringTools/StringTools.Core.psm1'
    'Invoke-RemoveInsideSymbols'         = 'Modules/StringTools/StringTools.Core.psm1'
    'Invoke-ReplacePlaceholder'          = 'Modules/StringTools/StringTools.Core.psm1'
    'Invoke-SimpleReplace'               = 'Modules/StringTools/StringTools.Core.psm1'
    'Invoke-TemplateReplace'             = 'Modules/StringTools/StringTools.Core.psm1'
    'Test-ValidIdentifier'               = 'Modules/StringTools/StringTools.Core.psm1'

    # ===== Modules\StringTools =====
    'Backup-FileWithTimestamp'     = 'Modules/StringTools/StringTools.FileBatch.psm1'
    'Get-FileList'                 = 'Modules/StringTools/StringTools.FileBatch.psm1'
    'Invoke-FileRegexExtract'      = 'Modules/StringTools/StringTools.FileBatch.psm1'
    'Invoke-FileReplace'           = 'Modules/StringTools/StringTools.FileBatch.psm1'
    'Invoke-FileTransform'         = 'Modules/StringTools/StringTools.FileBatch.psm1'

    # ===== Modules\StringTools =====
    'Get-MsgIdCandidates'             = 'Modules/StringTools/StringTools.LogMessages.psm1'
    'Get-MsgIdDynamicMatches'         = 'Modules/StringTools/StringTools.LogMessages.psm1'
    'Get-MsgIdLiteralCallMatches'     = 'Modules/StringTools/StringTools.LogMessages.psm1'
    'Get-MsgIdSourceIndex'            = 'Modules/StringTools/StringTools.LogMessages.psm1'
    'Remove-MsgBlock'                 = 'Modules/StringTools/StringTools.LogMessages.psm1'
    'Test-MsgIdReferenced'            = 'Modules/StringTools/StringTools.LogMessages.psm1'

    # ===== Modules\StringTools =====
    'Get-BacktickTokens'             = 'Modules/StringTools/StringTools.Markdown.psm1'
    'Get-MarkdownFirstCell'          = 'Modules/StringTools/StringTools.Markdown.psm1'
    'Test-CodeFenceLine'             = 'Modules/StringTools/StringTools.Markdown.psm1'
    'Test-MarkdownHeadingLine'       = 'Modules/StringTools/StringTools.Markdown.psm1'
    'Test-MarkdownSectionHeader'     = 'Modules/StringTools/StringTools.Markdown.psm1'
    'Test-MarkdownTableLine'         = 'Modules/StringTools/StringTools.Markdown.psm1'

    # ===== Modules\StringTools =====
    'ConvertFrom-DotnetListOutput'     = 'Modules/StringTools/StringTools.StructuredText.psm1'
    'ConvertFrom-KeyValueText'         = 'Modules/StringTools/StringTools.StructuredText.psm1'
    'ConvertFrom-SlnFile'              = 'Modules/StringTools/StringTools.StructuredText.psm1'
    'Get-SlnProjectGuidMap'            = 'Modules/StringTools/StringTools.StructuredText.psm1'
    'Get-SlnProjectPathMap'            = 'Modules/StringTools/StringTools.StructuredText.psm1'

    # ===== Path\Core =====
    'Get-EnvironmentVariable'      = 'Path/Core/PathEnvManager.psm1'
    'Get-PathEntryExists'          = 'Path/Core/PathEnvManager.psm1'
    'Remove-PathEntry'             = 'Path/Core/PathEnvManager.psm1'
    'Set-EnvironmentVariables'     = 'Path/Core/PathEnvManager.psm1'
    'Set-PathEntries'              = 'Path/Core/PathEnvManager.psm1'

    # ===== Path\Core =====
    'Get-ScriptDirectory'     = 'Path/Core/ScriptContext.psm1'
    'Resolve-SiblingPath'     = 'Path/Core/ScriptContext.psm1'

    # ===== Path\Project =====
    'Clear-FrameworkPaths'       = 'Path/Project/FrameworkPaths.psm1'
    'Get-AllFrameworkPaths'      = 'Path/Project/FrameworkPaths.psm1'
    'Get-FrameworkDataPath'      = 'Path/Project/FrameworkPaths.psm1'
    'Get-FrameworkPath'          = 'Path/Project/FrameworkPaths.psm1'
    'Register-FrameworkPath'     = 'Path/Project/FrameworkPaths.psm1'

    # ===== PScriptManager\Core =====
    'Add-CategoryIfNotExists'      = 'PScriptManager/Core/ConfigGenerator.psm1'
    'ConvertTo-ToolEntry'          = 'PScriptManager/Core/ConfigGenerator.psm1'
    'Load-LauncherConfig'          = 'PScriptManager/Core/ConfigGenerator.psm1'
    'Register-ScriptsToConfig'     = 'PScriptManager/Core/ConfigGenerator.psm1'
    'Save-LauncherConfig'          = 'PScriptManager/Core/ConfigGenerator.psm1'

    # ===== PScriptManager\Core =====
    'New-ModuleManifest'     = 'PScriptManager/Core/ManifestManager.psm1'

    # ===== PScriptManager\Core =====
    'Get-ScriptMetadata'        = 'PScriptManager/Core/ScriptScanner.psm1'
    'Get-ScriptSummary'         = 'PScriptManager/Core/ScriptScanner.psm1'
    'Invoke-ScriptScan'         = 'PScriptManager/Core/ScriptScanner.psm1'
    'Parse-BlockComment'        = 'PScriptManager/Core/ScriptScanner.psm1'
    'Resolve-ScriptPath'        = 'PScriptManager/Core/ScriptScanner.psm1'
    'Show-ScriptScanResult'     = 'PScriptManager/Core/ScriptScanner.psm1'

    # ===== PScriptManager =====
    'Get-PScriptManagerInfo'        = 'PScriptManager/PScriptManager.psm1'
    'Get-PScriptManagerVersion'     = 'PScriptManager/PScriptManager.psm1'

    # ===== PsRuntime\Capability =====
    'Normalize-PsAction'         = 'PsRuntime/Capability/CapabilityResolver.psm1'
    'Register-PsActionAlias'     = 'PsRuntime/Capability/CapabilityResolver.psm1'
    'Resolve-PsCapability'       = 'PsRuntime/Capability/CapabilityResolver.psm1'

    # ===== PsRuntime\Contract =====
    'Get-PsContracts'      = 'PsRuntime/Contract/ContractParser.psm1'
    'Get-PsScriptTags'     = 'PsRuntime/Contract/ContractParser.psm1'

    # ===== PsRuntime\Contract =====
    'Get-PsContractSchema'     = 'PsRuntime/Contract/ContractSchema.psm1'
    'Test-PsContract'          = 'PsRuntime/Contract/ContractSchema.psm1'

    # ===== PsRuntime\Contract =====
    'Test-PsContractEnv'        = 'PsRuntime/Contract/ContractValidator.psm1'
    'Test-PsContractParams'     = 'PsRuntime/Contract/ContractValidator.psm1'

    # ===== PsRuntime\Extension =====
    'Register-PsContract'     = 'PsRuntime/Extension/ContractRegistrar.psm1'

    # ===== PsRuntime\Extension =====
    'New-PsContractScript'     = 'PsRuntime/Extension/ContractScaffold.psm1'

    # ===== PsRuntime\Extension =====
    'Get-PsContractPromotion'     = 'PsRuntime/Extension/UsageTracker.psm1'
    'Get-PsContractUsage'         = 'PsRuntime/Extension/UsageTracker.psm1'
    'Update-PsContractUsage'      = 'PsRuntime/Extension/UsageTracker.psm1'

    # ===== PsRuntime\Gate =====
    'Test-PsCanHandle'     = 'PsRuntime/Gate/CanHandler.psm1'

    # ===== PsRuntime\Runtime =====
    'ConvertTo-PsResult'     = 'PsRuntime/Runtime/OutputSchema.psm1'
    'New-PsResult'           = 'PsRuntime/Runtime/OutputSchema.psm1'

    # ===== Tools\modules =====
    'Confirm-Prompt'      = 'Tools/modules/Confirm-Action.psm1'
    'Pause-Message'       = 'Tools/modules/Confirm-Action.psm1'
    'Read-MenuChoice'     = 'Tools/modules/Confirm-Action.psm1'

    # ===== Tools\modules =====
    'ConvertTo-Hashtable'          = 'Tools/modules/Convert-Format.psm1'
    'ConvertTo-PSCustomObject'     = 'Tools/modules/Convert-Format.psm1'
    'Format-Size'                  = 'Tools/modules/Convert-Format.psm1'
    'Get-DirSize'                  = 'Tools/modules/Convert-Format.psm1'
    'Get-HumanSize'                = 'Tools/modules/Convert-Format.psm1'
    'Get-RelativePath'             = 'Tools/modules/Convert-Format.psm1'
    'Resolve-EnvPath'              = 'Tools/modules/Convert-Format.psm1'
    'Resolve-PathTemplate'         = 'Tools/modules/Convert-Format.psm1'

    # ===== Tools\modules =====
    'Find-VscodeCmd'            = 'Tools/modules/Test-Environment.psm1'
    'Test-DockerRunning'        = 'Tools/modules/Test-Environment.psm1'
    'Test-WslCommand'           = 'Tools/modules/Test-Environment.psm1'
    'Test-WslDockerDesktop'     = 'Tools/modules/Test-Environment.psm1'

    # ===== Tools\modules =====
    'Get-DiagnosticResultsText'     = 'Tools/modules/Write-Diagnostic.psm1'
    'Initialize-Diagnostic'         = 'Tools/modules/Write-Diagnostic.psm1'

    # ===== Tools\modules =====
    'Write-Color'     = 'Tools/modules/Write-Format.psm1'
}
# <!-- PROTO_AUTO:functionModuleMap:END -->
# ============================================================
# 按需加载函数
# ============================================================

function Invoke-RequirementBasedLoad {
    <#
    .SYNOPSIS
        基于 @requires 声明按需加载模块
    .DESCRIPTION
        解析调用脚本头部的 @requires 注释，查找函数→模块映射表，
        仅加载尚未加载的模块。
    #>
    param(
        # 调用脚本的完整路径（自动检测）
        [string]$CallerPath = $null
    )

    if (-not $CallerPath) {
        $CallerPath = Get-PSCallStack | Select-Object -Skip 1 -First 1 |
            ForEach-Object { $_.ScriptName }
        if (-not $CallerPath -or $CallerPath -match 'Import-Modules\.ps1') {
            # 尝试从调用堆栈更上层获取
            $CallerPath = Get-PSCallStack | Select-Object -Skip 2 -First 1 |
                ForEach-Object { $_.ScriptName }
        }
        if (-not $CallerPath) { return }
    }

    # 读取脚本头部，提取 @requires 行
    $header = Get-Content $CallerPath -TotalCount 20 -Encoding UTF8 -ErrorAction SilentlyContinue
    if (-not $header) { return }

    $requiresLine = $header | Where-Object { $_ -match '#\s*@requires\s+(.+)$' }

    if (-not $requiresLine) { return }  # 没有声明需求，跳过

    # 解析函数名列表
    $requiredFunctions = @()
    foreach ($line in $requiresLine) {
        if ($line -match '#\s*@requires\s+(.+)$') {
            $requiredFunctions += $Matches[1] -split ',' | ForEach-Object { $_.Trim() }
        }
    }

    if ($requiredFunctions.Count -eq 0) { return }

    Write-Host "  [按需加载] 检测到 @requires 声明: $CallerPath" -ForegroundColor DarkGray

    # 按需加载模块
    $loadedModules = @{}
    foreach ($funcName in $requiredFunctions) {
        if (-not $functionModuleMap.ContainsKey($funcName)) {
            Write-Warning "[Import-Modules] 未找到函数 '$funcName' 的模块映射"
            continue
        }

        $moduleRelPath = $functionModuleMap[$funcName]
        if ($loadedModules.ContainsKey($moduleRelPath)) { continue }

        # 计算模块完整路径
        $scriptRoot = Split-Path -Parent $PSScriptRoot
        $moduleFullPath = Join-Path $scriptRoot $moduleRelPath

        if (-not (Test-Path $moduleFullPath)) {
            Write-Warning "[Import-Modules] 模块文件不存在: $moduleRelPath"
            continue
        }

        # 检查是否已加载
        $moduleName = [System.IO.Path]::GetFileNameWithoutExtension($moduleRelPath)
        $existing = Get-Module -Name $moduleName -ErrorAction SilentlyContinue
        if ($existing) {
            $loadedModules[$moduleRelPath] = $true
            continue
        }

        # 加载模块
        try {
            Import-Module $moduleFullPath -Force -DisableNameChecking -ErrorAction Stop
            $loadedModules[$moduleRelPath] = $true
            Write-Host "  [按需加载] $moduleRelPath" -ForegroundColor DarkGray
        } catch {
            Write-Warning "[Import-Modules] 模块加载失败: $moduleRelPath - $_"
        }
    }
}

# ============================================================
# 主入口：检测调用脚本是否有 @requires，决定加载模式
# ============================================================

# 检测调用者是否有 @requires 声明
$callerPath = Get-PSCallStack | Select-Object -Skip 1 -First 1 |
    ForEach-Object { $_.ScriptName }

if ($callerPath -and (Test-Path $callerPath -ErrorAction SilentlyContinue)) {
    $header = Get-Content $callerPath -TotalCount 20 -Encoding UTF8 -ErrorAction SilentlyContinue
    $hasRequires = $header | Where-Object { $_ -match '#\s*@requires\s+' }

    if ($hasRequires) {
        # 模式 A：按需加载
        Invoke-RequirementBasedLoad -CallerPath $callerPath
        return  # 不执行全量加载
    }
}

# 模式 B：全量加载（兼容旧脚本，无 @requires 时回退）
Write-Host "  [全量加载] 未检测到 @requires 声明，执行全量加载" -ForegroundColor DarkGray

$modulesDir = $PSScriptRoot

# 按依赖顺序加载模块
$moduleOrder = @(
    # Core 基础工具
    'Core\XmlHelpers.psm1',
    'Core\FileSystemHelpers.psm1',
    'Core\InteractiveHelpers.psm1',
    'Core\OutputHelpers.psm1',
    'Core\ProjectPaths.psm1',
    'Core\ProjectConfig.psm1',
    'Core\BuildHelpers.psm1',

    # Csproj 修复引擎
    'Csproj\CsprojFixEngine.psm1',

    # BuildOutput 清理
    'BuildOutputCleaner.psm1',

    # StringTools 字符串处理工具
    'StringTools\StringTools.Core.psm1',
    'StringTools\StringTools.FileBatch.psm1',
    'StringTools\StringTools.StructuredText.psm1',
    'StringTools\StringTools.Markdown.psm1',

    # FileDriven 测试工具
    'FileDriven\FileDrivenTestUtils.ps1',
    'FileDriven\ScriptRunner.ps1',

    # Tools 公共模块
    '..\Tools\modules\Write-Format.psm1',
    '..\Tools\modules\Write-Diagnostic.psm1',
    '..\Tools\modules\Test-Environment.psm1',
    '..\Tools\modules\Confirm-Action.psm1',
    '..\Tools\modules\Convert-Format.psm1'
)

# 支持调用方通过 $SkipModules 跳过特定模块
if (-not (Get-Variable -Name 'SkipModules' -Scope Global -ErrorAction SilentlyContinue)) {
    $global:SkipModules = @{}
}

foreach ($moduleName in $moduleOrder) {
    if ($global:SkipModules.ContainsKey($moduleName)) {
        Write-Host "  [已跳过] $moduleName" -ForegroundColor DarkGray
        continue
    }

    $modulePath = Join-Path $modulesDir $moduleName
    if (Test-Path $modulePath) {
        $extension = [System.IO.Path]::GetExtension($moduleName)
        if ($extension -eq '.psm1') {
            Remove-Module -Name ([System.IO.Path]::GetFileNameWithoutExtension($moduleName)) -ErrorAction SilentlyContinue
            Import-Module -Name $modulePath -Force -DisableNameChecking
        } else {
            . $modulePath
        }
        Write-Host "  [已加载] $moduleName" -ForegroundColor DarkGray
    } else {
        Write-Host "  [警告] 模块文件不存在: $modulePath" -ForegroundColor Yellow
    }
}
  
