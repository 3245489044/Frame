@{
    RootModule        = 'DockerManagement.psm1'
    ModuleVersion     = '1.0.0'
    GUID              = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890'
    Author            = 'WorkFlowPrj'
    CompanyName       = 'WorkFlowPrj'
    Copyright         = '(c) WorkFlowPrj. All rights reserved.'
    Description       = 'Docker 容器生命周期管理 + MCP 服务安装管理 + CLine 配置管理'

    # 嵌套模块（按依赖顺序：DockerCommon 在最前）
    NestedModules = @(
        'DockerCommon.psm1'
        'DockerBuild.psm1'
        'McpInstallation.psm1'
        'McpConfiguration.psm1'
    )

    # 导出的别名

    AliasesToExport = @()

    # 私有函数（不导出）
    PrivateData = @{
        PSData = @{
            Tags         = @('Docker', 'MCP', 'CLine', 'Container', 'DevOps')
            ProjectUri   = 'https://github.com/3245489044/ZhiLianStorage'
            LicenseUri   = 'https://github.com/3245489044/ZhiLianStorage/blob/main/LICENSE'
        }
    }
}
