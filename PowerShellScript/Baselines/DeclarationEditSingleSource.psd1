@{
    # ===== DeclarationEditSingleSource 门禁基线（单源；守卫 = Operation\DSL\Check-DeclarationEditSingleSource.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'DeclarationEditSingleSource'

    # 正对照基线：IDeclarationEditor 实现数 ≥ 基线
    MinEditorImplementations = @{ Value = 1; Origin = 'Derived'; DueWhen = '可由 UsharpDeclarationEditor.cs 的 `: IDeclarationEditor` 复算' }

    # 直写白名单（层内写盘唯一合法点 + 已登记债务）
    #   FileDeclarationStore.cs = 声明库文件实现（唯一合法写盘点）
    #   AgentConfig.cs = UI 端 agent-config.ushar 持久化（既有直写点，待迁 IDeclarationStore；登记为 Debt）
    WriteWhitelist = @{ Value = @('FileDeclarationStore.cs', 'AgentConfig.cs'); Origin = 'Debt'; DueWhen = 'AgentConfig 的 agent-config.ushar 直写迁至 IDeclarationStore.PutAsync 后移除该项' }
}
