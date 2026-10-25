@{
    # ===== WireFabric 门禁基线（单一生产者；守卫 = Operation\DSL\KeySpace\Check-WireFabric.ps1）=====
    # 语义：框架在位 + 依赖面受控 + 测试在册（结构事实；与 Node\Wiring 的接线生命周期守卫无关）。
    Schema = 'gate-baseline-v1'
    Gate   = 'WireFabric'

    # 框架工程（判据载体：文件存在 + ProjectReference 集合）
    FrameworkProject = @{ Value = 'Frame\CsharpPrj\Modules\Wiring\Modules.Wiring.csproj'; Origin = 'Derived'; DueWhen = '' }

    # 允许的依赖面（第三个引用出现即红；新增须在此登记理由）
    AllowedReferences = @(
        @{ Value = 'Proj_KeySpace'; Origin = 'Verdict'; DueWhen = '声明模型复用（DeclarationKind/PortBinding/ImplementationCatalog/ClosureValidator）' }
        @{ Value = 'Proj_Parsing_Keys'; Origin = 'Verdict'; DueWhen = '键坐标文法内核（BCL-only，与 KeySpace 同纪律）' }
    )

    # 在位清单（模型 / 编译 / 运行期 / 诊断各至少一件）
    RequiredFiles = @(
        @{ Value = 'Frame\CsharpPrj\Modules\Wiring\Model\WiringCatalog.cs'; Origin = 'Derived'; DueWhen = '' }
        @{ Value = 'Frame\CsharpPrj\Modules\Wiring\Model\WireSpec.cs'; Origin = 'Derived'; DueWhen = '' }
        @{ Value = 'Frame\CsharpPrj\Modules\Wiring\Abstractions\IWiringDeclarationSource.cs'; Origin = 'Derived'; DueWhen = '' }
        @{ Value = 'Frame\CsharpPrj\Modules\Wiring\Declaration\JsonWiringDeclarationSource.cs'; Origin = 'Derived'; DueWhen = '' }
        @{ Value = 'Frame\CsharpPrj\Modules\Wiring\Compile\WiringValidator.cs'; Origin = 'Derived'; DueWhen = '' }
        @{ Value = 'Frame\CsharpPrj\Modules\Wiring\Compile\WiringPlanner.cs'; Origin = 'Derived'; DueWhen = '' }
        @{ Value = 'Frame\CsharpPrj\Modules\Wiring\Runtime\Junction.cs'; Origin = 'Derived'; DueWhen = '' }
        @{ Value = 'Frame\CsharpPrj\Modules\Wiring\Runtime\IWireLedger.cs'; Origin = 'Derived'; DueWhen = '' }
        @{ Value = 'Frame\CsharpPrj\Modules\Wiring\Diagnostics\WiringReasonCodes.cs'; Origin = 'Derived'; DueWhen = '' }
        @{ Value = 'Environment\CentralTest\CSharpTest\Modules.Tests\Wiring.Tests\Wiring.Tests.csproj'; Origin = 'Derived'; DueWhen = '' }
    )

    # 测试别名（TestTargets.psd1 单源）
    TestAlias = @{ Value = 'wiring'; Origin = 'Derived'; DueWhen = '' }

    # 首批接线声明（W5）：UI 发送链；WireFloor = 接线规模下限（被掏空 ⇒ 判据无标的）
    WiringDeclaration = @{ Value = 'Projects\Plug\Agent\Config\wiring\ui-send.wiring.json'; Origin = 'Derived'; DueWhen = '' }
    WiringDeclarationWireFloor = @{ Value = 2; Origin = 'Verdict'; DueWhen = '接入更多链后按实际重裁（只升不降）' }
}
