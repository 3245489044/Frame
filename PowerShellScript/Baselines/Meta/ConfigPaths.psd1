@{
    # ===== ConfigPaths 门禁基线（单源；守卫 = Operation\DSL\Meta\Check-ConfigPaths.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'ConfigPaths'

    # 合法路径面（登记表自身 / 门禁自身 / PS 解析器 / 测试 / 生成物）。
    AllowedPathPatterns = @{
        Value = @(
            'ConfigPathCatalog\.cs$',
            'Check-ConfigPaths\.ps1$',
            'DeclarationPathResolver\.psm1$',
            '(^|/)tests?/',
            '\.g\.cs$',
            '\.generated\.'
        )
        Origin = 'Derived'
        DueWhen = ''
    }

    # 迁移期债务（只降不升；归零后必须删除条目）。Batch 2/3 收口目标。
    DebtAllowlist = @{
        Value = @(
            @{ File = 'Projects/Plug/Agent/src/Agent.Axioms/Infrastructure/RepositoryPaths.cs'; MaxHits = 1 },
            @{ File = 'Projects/Plug/Agent/src/Agent.Axioms/Llm/LlmDeclarationCatalog.cs'; MaxHits = 2 },
            @{ File = 'Projects/Plug/Agent/src/Agent.Launcher/Composition/Hosting/Llm/LlmRuntimeFactory.cs'; MaxHits = 1 },
            @{ File = 'Projects/Plug/Agent/src/Agent.Modules/Framework/History/HistoryOpsServices.cs'; MaxHits = 9 },
            @{ File = 'Projects/Plug/Agent/src/Agent.Modules/Framework/Messaging/MessageBusCatalog.cs'; MaxHits = 1 },
            @{ File = 'Projects/Plug/Agent/src/Agent.Modules/Framework/ReadIn/ReadInServices.cs'; MaxHits = 1 },
            @{ File = 'Projects/Plug/Agent/src/Agent.Modules/SubAgent/Agents/USharp/USharpSubAgentTools.cs'; MaxHits = 1 },
            @{ File = 'Projects/Plug/Agent/src/Agent.Ui/Core/Functions/HistoryOps/AgentUiLibraryHistoryOps.cs'; MaxHits = 1 },
            @{ File = 'Projects/Plug/Agent/src/Agent.Ui/Core/Host/AgentUiWiring.cs'; MaxHits = 1 },
            @{ File = 'Projects/Plug/Agent/PowerShellScript/Check-AgenticWorkflow.ps1'; MaxHits = 5 },
            @{ File = 'Projects/Plug/Agent/PowerShellScript/Check-NodeTaxonomy.ps1'; MaxHits = 1 },
            @{ File = 'Projects/Plug/Agent/PowerShellScript/Migrate-AgentHistoryRoots.ps1'; MaxHits = 1 }
        )
        Origin = 'Debt'
        DueWhen = 'Batch 2 消除 MessageBusCatalog / SkillsConfigRelPath；Batch 3 归零'
    }
}
