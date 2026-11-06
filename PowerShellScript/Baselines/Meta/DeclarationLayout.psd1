@{
    # ===== DeclarationLayout 门禁基线（单源；守卫 = Operation\DSL\Meta\Check-DeclarationLayout.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'DeclarationLayout'

    # 根级控制面白名单（根级仅允许启动入口 / 布局真源 / schema 与生成物清单）。
    RootControlFiles = @{
        Value = @('agent-config.ushar')
        Origin = 'Derived'
        DueWhen = ''
    }

    # 迁移期根级声明（Batch 2 完成后已清空；根级只剩控制面）。
    MigrationRootFiles = @{
        Value = @()
        Origin = 'Debt'
        DueWhen = ''
    }
}
