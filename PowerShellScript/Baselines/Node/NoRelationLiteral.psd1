@{
    Schema = 'gate-baseline-v1'
    Gate   = 'NoRelationLiteral'

    # 关系边名集合的补充项（reference.ushar [Ref] 之外的受控边；来源 = 声明面事实）。
    ExtraEdges = @(
        @{ Value = 'node_to_node';        Origin = 'Derived'; DueWhen = '' }
        @{ Value = 'keymapping_to_node';  Origin = 'Derived'; DueWhen = '' }
    )

    # 适配器白名单：边名词表定义 / 单源常量持有 / 受控特判（棘轮只降不升）。
    Allow = @(
        @{ Value = 'Frame\CsharpPrj\Share\Frameworks\UniversalReference\Reference\Models\ReferenceEdgeTypes.cs'; Origin = 'Derived'; DueWhen = '' }
        @{ Value = 'Frame\CsharpPrj\Share\Frameworks\UniversalReference\Relations\Gather\Adapters\DomainRelationSurface.cs'; Origin = 'Derived'; DueWhen = '' }
        @{ Value = 'Frame\CsharpPrj\Share\Frameworks\UniversalReference\Relations\Kernel\RelationOperationResolver.cs'; Origin = 'Derived'; DueWhen = '' }
    )
}
