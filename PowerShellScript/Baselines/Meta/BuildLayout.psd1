@{
    # ===== BuildLayout 门禁基线（单源；守卫 = Operation\DSL\Meta\Check-BuildLayout.ps1）=====
    # 语义：产物落点必须带配置层；自包含 props 必须 Import 生成件（求值期 Configuration 非空）；
    #       禁扁平权威层（= 事故形态）。
    Schema = 'gate-baseline-v1'
    Gate   = 'BuildLayout'

    # L1：任何 <OutputPath> 的取值必须含 $(Configuration)
    RequireConfigurationSegment = @{ Value = $true; Origin = 'Verdict'; DueWhen = '所有 OutputPath 声明均含 $(Configuration) 后重裁' }

    # L2：自包含 props（不 Import 上游 props）必须 Import 生成件
    SelfContainedProps = @{ Value = @(
        'Projects\Plug\Engine\Directory.Build.props'
        'Projects\Plug\DataProcess\Directory.Build.props'
    ); Origin = 'Derived'; DueWhen = '' }

    GeneratedPropsRelative = @{ Value = 'Frame\generated\BuildLayout.generated.props'; Origin = 'Derived'; DueWhen = '' }

    # L3：扁平权威层（<product>\BuildOutput\*.dll 直挂）允许数 = 0
    AllowFlatAuthorityFiles = @{ Value = 0; Origin = 'Verdict'; DueWhen = '扁平权威层恒为 0 后由 L3 直接断言，届时重裁' }

    # L4：正对照 —— 扫描到的 Directory.Build.props 数下限
    MinPropsScanned = @{ Value = 12; Origin = 'Derived'; DueWhen = '' }
}
