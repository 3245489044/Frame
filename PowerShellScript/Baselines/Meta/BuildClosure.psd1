@{
    # ===== BuildClosure 门禁基线（单源；守卫 = Operation\DSL\Meta\Check-BuildClosure.ps1）=====
    # 语义：启动必需闭包（BuildProfiles.launch）与装配组声明同源；禁脚本内联产品清单。
    Schema = 'gate-baseline-v1'
    Gate   = 'BuildClosure'

    ProfileName = @{ Value = 'launch'; Origin = 'Verdict'; DueWhen = 'launch 档名改由构建清单派生后重裁' }
    MapPropsRelative = @{ Value = 'Projects\BuildFramework\Graph\props\BuildTargetMap.props'; Origin = 'Derived'; DueWhen = '' }

    # K2：允许出现“产品清单常量”的文件（应为空；迁移期如需例外须显式登记 + DueWhen）
    AllowedInlineClosureFiles = @{ Value = @(); Origin = 'Verdict'; DueWhen = '新增产品清单常量例外时重裁（当前为空表）' }

    MinLaunchTargets = @{ Value = 8; Origin = 'Derived'; DueWhen = '' }
}
