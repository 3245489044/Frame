@{
    Schema = 'gate-baseline-v1'
    Gate = 'CF-W-10-NoZombieConfigField'
    Source = 'Environment\CentralTest\Tools\Gate\Constraint\Check-CF-W-10-NoZombieConfigField.ps1'

    ScanFiles = @{ Value = 362; Origin = 'Derived'; DueWhen = '' }

    # 棘轮：可判子集（唯一名）内的僵尸配置字段；同名冲突字段留在 attest。
    FileCounts = @{
        Value = @{
            'BasicConfig.Show' = 1
            'BasicConfig.SwitchReturn' = 1
            'ControlConfig.AcceptsReturn' = 1
            'ControlConfig.Backend' = 1
            'ControlConfig.DataBinding' = 1
            'ControlConfig.FixedPanel' = 1
            'ControlConfig.IsSplitterFixed' = 1
            'ControlConfig.ListViewColumns' = 1
            'ControlConfig.MaxLength' = 1
            'ControlConfig.Placeholder' = 1
            'ControlConfig.StackLayout' = 1
            'ControlConfig.ToolTip' = 1
            'ControlConfig.ViewIndex' = 1
            'ControlConfig.Writeback' = 1
            'DataBindingConfig.DataMember' = 1
            'DataBindingConfig.Format' = 1
            'DataBindingConfig.FormatString' = 1
            'DataBindingConfig.TwoWay' = 1
            'DataBindingConfig.UpdateMode' = 1
            'EventBinding.OutputKey' = 1
            'EventBinding.Reconcile' = 1
            'FormConfig.IconPath' = 1
            'FormConfig.MaximizeBox' = 1
            'FormConfig.MinimizeBox' = 1
            'FormConfig.ShowIcon' = 1
            'FormConfig.ShowInTaskbar' = 1
            'FormConfig.StartPosition' = 1
            'FormConfig.WindowState' = 1
            'LifecycleHooks.OnAfterDestroy' = 1
            'LifecycleHooks.OnBeforeDestroy' = 1
            'NavigationConfig.Buttons' = 1
            'NavigationConfig.DefaultView' = 1
            'NavigationConfig.NavigationBarPosition' = 1
            'NavigationConfig.Position' = 1
            'NavigationConfig.ShowNavigationBar' = 1
            'NextNodeConfig.AfterSymbol' = 1
            'NextNodeConfig.BeforeSymbol' = 1
            'NextNodeConfig.SymbolMatchMode' = 1
            'ParamConfig.IsOptional' = 1
            'ReconcileConfig.Cases' = 1
            'ReconcileConfig.Signals' = 1
            'ReconcileConfig.When' = 1
            'StyleConfig.FontName' = 1
            'StyleConfig.FontStyle' = 1
            'StyleConfig.StyleReference' = 1
            'UIConfiguration.DataBindings' = 1
            'UIConfiguration.DataLoad' = 1
            'UIConfiguration.Extends' = 1
            'UIConfiguration.InitCommandMode' = 1
            'UIConfiguration.Intents' = 1
            'UIConfiguration.Navigation' = 1
            'UIConfiguration.Styles' = 1
        }
        Origin = 'Debt'
        DueWhen = 'CW-10 僵尸字段逐项销账（接入实现读取或标记 @deprecated）'
    }
}

