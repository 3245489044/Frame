@{
    Schema = 'constraint-wall-v1'

    # D 声明面：契约（Abstractions）、配置模型（Models）与样本 I 装配中的声明模型。
    # 依据 ConstraintFramework-WallBridge.md §1.1（D / I / C 三类）与 §2.2 CW-01/02。
    DeclarationDirs = @(
        'Frame/CsharpPrj/Plate/TConfig/Models'
        'Frame/CsharpPrj/Plate/TConfig/Abstractions'
        'Frame/CsharpPrj/Operation/WorkFlowMethod/NodeProcess/Models'
    )

    # D 不得引用的 I 命名空间（CW-01）。WorkFlowMethod 用于抓「声明模型反向别名实现接口」这类自引用。
    ImplNamespacePrefixes = @(
        'System.Windows.Forms'
        'Plate.MarkdownViewer'
        'Plate.HtmlViewer'
        'FrontEndDriven'
        'WorkFlowMethod'
        'ECS'
    )

    # CW-02 的**显式禁型子集**：平台/实现类型 + 弱类型 + 可变 BCL 集合。
    # 说明：完整「公开面闭合」（未知外部类型/嵌套泛型/别名）需 Roslyn 语义，本轮标 attest，不在本键内伪装。
    DeclarationDenyTokens = @(
        'System.Windows.Forms'
        'Plate.MarkdownViewer'
        'Plate.HtmlViewer'
        'FrontEndDriven'
        'WorkFlowMethod'
        'ECS'
        'Control'
        'EntityManager'
        'Delegate'
        'dynamic'
        'object'
    )

    # 可变配置模型（CW-03/05）。新增配置模型必须在此登记，否则门禁不覆盖（已在施工记录中标为限制）。
    MutableConfigTypes = @(
        'NodeConfig'
        'GetStrConfig'
        'FuncPipelineConfig'
        'AddNodeConfig'
        'BasicConfig'
        'NextNodeConfig'
        'UIConfiguration'
        'FormConfig'
        'NavigationConfig'
        'DataBindingConfig'
        'StyleConfig'
        'DataLoadConfig'
        'BootPhase'
        'LifecycleHooks'
        'InitCommandSpec'
        'ControlConfig'
        'EventConfig'
        'EventBinding'
        'ParamConfig'
        'ReconcileConfig'
        'ViewConfig'
    )

    # 实现侧扫描面（CW-03/05/07/08/09）。本轮限定文档样本两模块，扩面须同时改基线扫描面计数。
    ImplScopeDirs = @(
        'Frame/CsharpPrj/Operation/FrontEndDriven'
        'Frame/CsharpPrj/Operation/WorkFlowMethod'
    )

    # 核心目录（CW-03）。等价路径集按文档「或等价路径集」显式登记，不做模糊匹配。
    CoreDirs = @(
        'Frame/CsharpPrj/Operation/WorkFlowMethod/NodeProcess/Execution'
        'Frame/CsharpPrj/Operation/WorkFlowMethod/GraphUsing'
        'Frame/CsharpPrj/Operation/WorkFlowMethod/Parameters'
        'Frame/CsharpPrj/Operation/WorkFlowMethod/Context'
        'Frame/CsharpPrj/Operation/WorkFlowMethod/Orchestration'
        'Frame/CsharpPrj/Operation/FrontEndDriven/ControlRender'
        'Frame/CsharpPrj/Operation/FrontEndDriven/LoadRender'
        'Frame/CsharpPrj/Operation/FrontEndDriven/Lifecycle'
        'Frame/CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline'
        'Frame/CsharpPrj/Operation/FrontEndDriven/State'
    )

    # 平台目录白名单（CW-07）。命中令牌但位于这些目录内 = 合法隔离边界。
    PlatformDirs = @(
        'Frame/CsharpPrj/Operation/FrontEndDriven/WinForms'
        'Frame/CsharpPrj/Operation/FrontEndDriven/HtmlHosting'
        'Frame/CsharpPrj/Plate/MarkdownViewer'
        'Frame/CsharpPrj/Plate/HtmlViewer'
    )

    PlatformTokens = @(
        'System.Windows.Forms'
        'Plate.MarkdownViewer'
        'Plate.HtmlViewer'
        'Control'
    )

    # CW-08 仅收明确、可静态定位的环境/全局查找调用点。
    # `.Shared` / `.Instance` 泛化面因同名成员跨类型冲突，本轮不实现（attest，见施工记录）。
    AmbientPatterns = @(
        'AppServiceProvider\.Resolve'
        'NodePortContractRegistry\.Configure'
        'NodePortContractRegistry\.Shared'
    )

    # 排除目录：生成物/历史退役/测试夹具不参与实现侧扫描。
    ExcludePathSegments = @(
        '/obj/'
        '/bin/'
        '/_deprecated/'
        '/.vs/'
    )
}
