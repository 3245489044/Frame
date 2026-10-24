@{
    # ===== 渲染层存储解耦基线（纯数据单源；守卫 = Operation\DSL\Data\Check-RenderLayerStoreFree.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'RenderLayerStoreFree'
    Source = 'Frame\PowerShellScript\Operation\DSL\Data\Check-RenderLayerStoreFree.ps1'

    # RL1 渲染层（Agent.Ui\Ui\Views\**）存储端口引用数：期望 0（只接收模型）。
    StorePortReferences = @{
        Value   = 0
        Origin  = 'Derived'
        DueWhen = '保持 0；引入即回退到投影层（禁在 Views 内读库）'
    }
}
