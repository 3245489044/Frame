@{
    Schema = 'gate-baseline-v1'
    Gate   = 'NoRelationJoin'

    # 唯一集合查询实现点（派生引擎；路径形态事实，改动须同批更新本行）。
    ImplementationPoint = @{
        Value  = 'Frame\CsharpPrj\Share\Frameworks\UniversalReference\Relations\Derivation\RelationDerivationEngine.cs'
        Origin = 'Derived'
        DueWhen = ''
    }

    # 允许的既有形态（棘轮只降不升；新增第二实现点一律判红）。
    Allow = @(
        @{ Value = 'Frame\CsharpPrj\Share\Frameworks\UniversalReference\Relations\Derivation\RelationDerivationEngine.Batch.cs'; Origin = 'Derived'; DueWhen = '' }
    )
}
