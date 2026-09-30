@{
    # ===== 语义集中门禁基线（纯数据单源；守卫 = Operation\DSL\Check-SemanticCentralization.ps1）=====
    # 防止边类型词汇退回各域自实现（B1 收口后唯一字面量源 = 框架 ReferenceEdgeTypes）。
    # 2026-09-30 B1 收口：DataStoreDriven ReferenceTypes 降为别名门面。
    Schema = 'gate-baseline-v1'
    Gate   = 'SemanticCentralization'
    Source = 'Frame\PowerShellScript\Operation\DSL\Check-SemanticCentralization.ps1'

    # 零依赖层豁免白名单（零项目引用纯逻辑框架，本地字面量按层内自治常量保留；
    #   与 Check-ReferenceVocabulary 豁免口径一致）。
    ExemptLayers = @{
        Value  = @(
            'Modules\DeclarativeDataflow\'
            'Share\Foundations\ScopeFramework\'
        )
        Origin  = 'Derived'
        DueWhen = ''
    }

    # 唯一字面量源（框架本体）。
    CanonicalSource = @{
        Value  = 'Frame\CsharpPrj\Share\Frameworks\UniversalReference\Reference\Models\ReferenceEdgeTypes.cs'
        Origin = 'Derived'
        DueWhen = ''
    }

    # SC1 收口类下限：`class ReferenceEdgeTypes` 定义点数（防单源被删/未注册而门禁恒绿）。
    ReferenceEdgeTypesDefs = @{
        Value  = 1
        Origin = 'Derived'
        DueWhen = '收口类拆分或合并时同步调整'
    }

    # SC2 域内字面量直接赋值上限：非豁免域、非唯一源内，边类型字面量直接赋值（`= "cross_db_link"` 等）的文件数。
    # 只减不增：B1 后各域须别名转发，新增直接赋值即违规。
    EdgeTypeLiteralSites = @{
        Value  = 0
        Origin = 'Derived'
        DueWhen = ''
    }

    # 被扫描的边类型字面量（独特小写带下划线，避免与方法名/配置键混淆）。
    ScannedLiterals = @{
        Value  = @(
            'cross_db_link'
            'session_owns'
            'task_owns_task'
            'history_owns_task'
            'history_owns_node'
            'node_to_node'
            'keymapping_to_node'
        )
        Origin  = 'Derived'
        DueWhen = ''
    }
}
