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
        @{ Value = 'Frame\CsharpPrj\Modules\Wiring\Runtime\IWirePortStore.cs'; Origin = 'Derived'; DueWhen = '' }
        @{ Value = 'Frame\CsharpPrj\Modules\Wiring\Runtime\IWireHandlerTable.cs'; Origin = 'Derived'; DueWhen = '' }
        @{ Value = 'Frame\CsharpPrj\Modules\Wiring\Runtime\WireDeliveryMode.cs'; Origin = 'Derived'; DueWhen = '' }
        @{ Value = 'Frame\CsharpPrj\Modules\Wiring\Runtime\IWireLedger.cs'; Origin = 'Derived'; DueWhen = '' }
        @{ Value = 'Frame\CsharpPrj\Modules\Wiring\Diagnostics\WiringReasonCodes.cs'; Origin = 'Derived'; DueWhen = '' }
        @{ Value = 'Environment\CentralTest\CSharpTest\Modules.Tests\Wiring.Tests\Wiring.Tests.csproj'; Origin = 'Derived'; DueWhen = '' }
    )

    # 测试别名（TestTargets.psd1 单源）
    TestAlias = @{ Value = 'wiring'; Origin = 'Derived'; DueWhen = '' }

    # 首批接线声明（W5）：UI 全量入口；WireFloor = 接线规模下限（被掏空 ⇒ 判据无标的）
    WiringDeclaration = @{ Value = 'Projects\Plug\Agent\Config\wiring\ui-all.wiring.json'; Origin = 'Derived'; DueWhen = '' }
    WiringDeclarationWireFloor = @{ Value = 49; Origin = 'Verdict'; DueWhen = '接入更多链后按实际重裁（只升不降）' }

    # 声明唯一源（生成器输入）：sourceHash 必须与这些文件的组合指纹一致（C8）
    WiringSources = @(
        @{ Value = 'Projects\Information\DrivenConfig\JsonConfig\AgentRuntimeUI.json'; Origin = 'Derived'; DueWhen = '' }
        @{ Value = 'Projects\Information\DrivenConfig\JsonConfig\ProducePanel.json'; Origin = 'Derived'; DueWhen = '' }
    )

    # ===== C9–C15（R7）=====
    CoverageFloor = @{ Value = 100; Origin = 'Verdict'; DueWhen = 'UI 入口覆盖率 100%（入口集合 = events ∪ 非命令 eventTargets ∪ cmd）' }
    CommandSourceMax = @{ Value = 1; Origin = 'Verdict'; DueWhen = '同一 cmd 的源端口数上限（禁共享 ui.command_source + judge）' }
    ForbiddenFormClr = @{ Value = 'System.Object'; Origin = 'Verdict'; DueWhen = 'Form 禁 System.Object 兜底（C12）' }
    ParseCountMax = @{ Value = 1; Origin = 'Verdict'; DueWhen = '事件链 JsonDocument.Parse 上限（C10）' }
    ParseFiles = @(
        @{ Value = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Bridge\HtmlEventBridge.cs'; Origin = 'Derived'; DueWhen = '' }
        @{ Value = 'Projects\Plug\Agent\src\Agent.Ui\Core\Host\AgentUiWiring.cs'; Origin = 'Derived'; DueWhen = '' }
    )
    LedgerWriterFile = @{ Value = 'Frame\CsharpPrj\Modules\Wiring\Runtime\Junction.cs'; Origin = 'Derived'; DueWhen = 'I2 单写者：ledger.Record 只允许 Junction 调用（C13 静态面）' }
}
