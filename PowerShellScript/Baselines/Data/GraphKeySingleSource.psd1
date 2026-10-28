@{
    # ===== 图边键单源基线（纯数据单源；守卫 = Operation\DSL\Data\Check-GraphKeySingleSource.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'GraphKeySingleSource'
    Source = 'Frame\PowerShellScript\Operation\DSL\Data\Check-GraphKeySingleSource.ps1'

    # 手拼边键字面量（"::"）容差（0 = 唯一实现 = DeclaredGraphKeyGrammar）。
    ManualKeyConcatTolerance = @{
        Value   = 0
        Origin  = 'Derived'
        DueWhen = '保持 0；新增键形态只改 GraphKeyGrammar'
    }

    # 文法实现点（1 = DeclaredGraphKeyGrammar）。
    GrammarDefinitionCount = @{
        Value   = 1
        Origin  = 'Derived'
        DueWhen = '保持 1（禁第二套键实现）'
    }
}
