@{
    Schema = 'constraint-field-classification-v1'

    # 字段分级表（CW-12 的前置件）。
    # 真源分工（刻意避免第二份真源）：
    #   · 「有哪些字段」的唯一真源 = C# 声明面（ConstraintWall.settings.psd1 的 DeclarationDirs）；
    #     本表不复制字段清单，只登记「类型 → 类别」与少数「字段 → 语义/派生」判定。
    #   · 「字段属于哪一类」的唯一真源 = 本表；覆盖关系由门禁互证（声明有、表无 ⇒ 违规；表有、声明无 ⇒ 违规）。
    # 对齐既有声明面：
    #   · TConfig 声明：TypeKinds 覆盖 ConstraintWall.settings.psd1 的全部 MutableConfigTypes（Kind=Config）。
    #   · [Space] 键：是值空间坐标（`S`/`x` 等），不是配置字段的所有者；只有显式登记的
    #     SpaceKeyToConfigField 才把某个键映射到配置字段，缺省为空 ⇒ 不构成第二真源。
    #   · ushar 声明：是声明资产（[Gather]/[Edge]/[Table]），不拥有配置字段；本表不读取、不复制其声明。

    # 类型级类别：Config = 配置字段；Runtime = 运行期状态字段；Derived = 由配置/布局派生的字段。
    # Config 类型必须与 MutableConfigTypes 一一对应（门禁互证），此处不重复登记配置类型。
    TypeKinds = @{
        # 运行期：由交互/句柄/标签在运行时产生，非配置输入。
        'ControlStateComponent'         = 'Runtime'
        'ControlStaticDataComponent'    = 'Runtime'
        'ControlTagComponent'           = 'Runtime'
        'ControlTypeComponent'          = 'Runtime'
        'EventBindingComponent'         = 'Runtime'
        'EventBindingsComponent'        = 'Runtime'
        'WinFormsHandleComponent'       = 'Runtime'

        # 派生：由配置 + 布局计算得出，可重算，不得回写配置。
        'ColorStyleComponent'           = 'Derived'
        'ComputedLayoutComponent'       = 'Derived'
        'ControlBehaviorComponent'      = 'Derived'
        'FontStyleComponent'            = 'Derived'
        'LayoutIntentComponent'         = 'Derived'
        'MergedStyleComponent'          = 'Derived'
        'PixelLayoutComponent'          = 'Derived'
        'ProportionalLayoutComponent'   = 'Derived'
        'SplitterRatioComponent'        = 'Derived'
        'StyleRefComponent'             = 'Derived'
    }

    # 语义字段：决定结构/流程/路由/身份/绑定的配置字段；I 侧只允许 Adapter 读取。
    # 未列入者默认为 Presentation（呈现字段）。条目必须能在声明面找到，否则门禁报「幻影条目」。
    SemanticFields = @(
        'NodeConfig.Id'
        'NodeConfig.Symbol'
        'NodeConfig.NextNodeId'
        'NodeConfig.basicConfig'
        'NodeConfig.getStrConfig'
        'NodeConfig.funcPipelineConfig'
        'NodeConfig.addNodeConfig'
        'NodeConfig.nextNodeConfig'
        'BasicConfig.Callkey'
        'BasicConfig.DirectString'
        'BasicConfig.SwitchReturn'
        'NextNodeConfig.HandleNextNodeRun'
        'NextNodeConfig.NextNodeKey'
        'NextNodeConfig.MapKeyConfig'
        'NextNodeConfig.ConnectChainInfoKey'
        'NextNodeConfig.ConnectChainInfoConfig'
        'NextNodeConfig.UpdateChain'
        'NextNodeConfig.BeforeNodeCallkey'
        'NextNodeConfig.AfterNodeCallkey'
        'NextNodeConfig.BeforeSymbol'
        'NextNodeConfig.AfterSymbol'
        'NextNodeConfig.SymbolMatchMode'
        'FuncPipelineConfig.FuncName'
        'FuncPipelineConfig.ParameterKeys'
        'FuncPipelineConfig.ResultKeys'
        'FuncPipelineConfig.ProcessSymbol'
        'FuncPipelineConfig.Params'
        'FuncPipelineConfig.ParameterType'
        'AddNodeConfig.AddConfigAddKey'
        'AddNodeConfig.AddAICallCellInvoke'
        'AddNodeConfig.AddAutoCreateRoot'
        'AddNodeConfig.AddParameters'
        'GetStrConfig.GetConfigGetstring'
        'GetStrConfig.GetConfigLabel'
        'GetStrConfig.GetConfigQuerySteps'
        'GetStrConfig.GetConfigSymbol'
        'UIConfiguration.Components'
        'UIConfiguration.Views'
        'UIConfiguration.Templates'
        'UIConfiguration.Intents'
        'UIConfiguration.Lifecycle'
        'UIConfiguration.Boot'
        'UIConfiguration.InitCommands'
        'UIConfiguration.InitCommandSpecs'
        'UIConfiguration.InitCommandMode'
        'UIConfiguration.Form'
        'UIConfiguration.Navigation'
        'UIConfiguration.Labels'
        'UIConfiguration.Formatters'
        'UIConfiguration.Extends'
        'UIConfiguration.Styles'
        'UIConfiguration.DataBindings'
        'UIConfiguration.DataLoad'
        'ViewConfig.Name'
        'ViewConfig.Type'
        'ViewConfig.Controls'
        'ViewConfig.Layout'
        'ViewConfig.DataSource'
        'ViewConfig.Events'
        'ControlConfig.Name'
        'ControlConfig.Type'
        'ControlConfig.Container'
        'ControlConfig.View'
        'ControlConfig.ViewIndex'
        'ControlConfig.Backend'
        'ControlConfig.Writeback'
        'ControlConfig.HiddenSemantics'
        'ControlConfig.Orientation'
        'ControlConfig.Items'
        'ControlConfig.Columns'
        'ControlConfig.Tabs'
        'ControlConfig.ListViewColumns'
        'ControlConfig.Panel1Controls'
        'ControlConfig.Panel2Controls'
        'ControlConfig.DataBinding'
        'ControlConfig.Layout'
        'ControlConfig.Style'
        'ControlConfig.ExtensionData'
        'FormConfig.Title'
        'NavigationConfig.DefaultView'
        'NavigationConfig.Buttons'
        'NavigationConfig.Position'
        'NavigationConfig.NavigationBarPosition'
        'NavigationConfig.ShowNavigationBar'
        'StyleConfig.StyleReference'
        'DataBindingConfig.Target'
        'DataBindingConfig.Property'
        'DataBindingConfig.Source'
        'DataBindingConfig.DataMember'
        'DataBindingConfig.Format'
        'DataBindingConfig.FormatString'
        'DataBindingConfig.TwoWay'
        'DataBindingConfig.UpdateMode'
        'DataLoadConfig.Source'
        'BootPhase.Host'
        'BootPhase.Scope'
        'BootPhase.Load'
        'LifecycleHooks.OnBeforeLoad'
        'LifecycleHooks.OnAfterLoad'
        'LifecycleHooks.OnBeforeDestroy'
        'LifecycleHooks.OnAfterDestroy'
        'InitCommandSpec.Exec'
        'InitCommandSpec.Output'
        'InitCommandSpec.Requires'
        'EventConfig.Bindings'
        'EventConfig.EventsDictionary'
        'EventBinding.FunctionName'
        'EventBinding.FunctionTimeoutMs'
        'EventBinding.Workflows'
        'EventBinding.Reconcile'
        'EventBinding.OutputKey'
        'EventBinding.OutputMapping'
        'EventBinding.OutputMode'
        'EventBinding.OutputTarget'
        'EventBinding.Parameters'
        'ParamConfig.DefaultValue'
        'ParamConfig.IsOptional'
        'ReconcileConfig.When'
        'ReconcileConfig.Cases'
        'ReconcileConfig.Signals'
        'ReconcileConfig.Mode'
        'ReconcileConfig.Order'
    )

    # 派生字段：配置模型上的「由其他配置字段计算得出」的只读标记（不是独立输入）。
    DerivedFields = @(
        'FormConfig.IsWidthSet'
        'FormConfig.IsHeightSet'
        'FormConfig.IsMaximizeBoxSet'
        'FormConfig.IsMinimizeBoxSet'
        'FormConfig.IsWindowStateSet'
    )

    # [Space] 键 → 配置字段 的显式映射。缺省为空：Space 键不自动成为配置字段真源。
    SpaceKeyToConfigField = @{}

    # 判定说明（写入 -ShowDetail / 施工记录，不作机器门禁输入）。
    Notes = @(
        'Kind=Config 的字段必须出现在 C# 声明面；Semantic/派生判定只允许引用已声明字段。'
        'Presentation 为默认值，不在表中重复登记。'
        'Runtime/Derived 类型的字段不参与 CW-12 的「配置字段语义读取」判定。'
    )
}
