@{
    # 门禁注册表（P4）：key = 运行期门实现符号；值 = { Declared, Carrier, Impl, Layer }
    #   Declared = .clinerules/formula.md 声明门符号（空 = 无对应声明门，属公理/契约落地）
    #   Carrier  = 判据载体（结构事实，禁执行者叙述；权威: axioms SEP.JudgmentCarrierStructural）
    #   Impl     = 仓库相对路径（'/' 分隔）
    #   Layer    = runtime（运行期 C# 门）
    # 对账脚本: Check-GateRegistry.ps1；权威声明面: .clinerules/formula.md
    CriterionGate = @{
        Declared = 'MacroGate'
        Carrier  = 'axis/granularity/recomputeCmd（判据声明结构事实）'
        Impl     = 'Projects/Plug/Agent/src/Agent.Modules/Response/StateGovernance/CriterionGate.cs'
        Layer    = 'runtime'
    }

    ReturnEnvelopeGate = @{
        Declared = ''
        Carrier  = 'fenced json 信封 signal∈{DONE,RETRY,ERROR}（内容结构事实）'
        Impl     = 'Projects/Plug/Agent/src/Agent.Modules/Response/StateGovernance/ReturnEnvelopeGate.cs'
        Layer    = 'runtime'
    }

    ContextEconomy = @{
        Declared = 'FormatReassert'
        Carrier  = 'token 估算（文本长度/CJK 计数字符结构事实）'
        Impl     = 'Projects/Plug/Agent/src/Agent.Modules/Response/StateGovernance/Kernel/ContextEconomy.cs'
        Layer    = 'runtime'
    }

    LoopGovernor = @{
        Declared = 'StagnationGate'
        Carrier  = 'noGain≥STAGNATION_LIMIT（计数结构事实）'
        Impl     = 'Projects/Plug/Agent/src/Agent.Modules/Response/StateGovernance/LoopGovernor.cs'
        Layer    = 'runtime'
    }

    # 契约执行门（ContractFramework 契约框架的单一执行入口 + 单一抛出点；声明白名单 = contracts.json）
    ContractGate = @{
        Declared = ''
        Carrier  = 'contracts.json 五元组（Id/Semantics/Carrier/Tier/Effect）+ Evidence（声明面结构事实）'
        Impl     = 'Frame/CsharpPrj/Modules/ContractFramework/Gate/ContractGate.cs'
        Layer    = 'runtime'
    }
}
