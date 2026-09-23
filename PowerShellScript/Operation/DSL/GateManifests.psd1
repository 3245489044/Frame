@{
    # ===== 带 -Check 开关的门禁（-Check 时退出码裁决） =====
    AgentBuildPoint = @{
        Args   = @{ Check = $true }
        Expect = 0
    }

    AgentDataSeparation = @{
        Args   = @{ Check = $true }
        Expect = 0
    }

    AgentDecisionCoreBoundary = @{
        Args   = @{ Check = $true }
        Expect = 0
    }

    AgentRuntimeSlots = @{
        Args   = @{ Check = $true }
        Expect = 0
    }

    DataCenterGenericity = @{
        Args   = @{ Check = $true }
        Expect = 0
    }

    DataContracts = @{
        Args   = @{ Check = $true }
        Expect = 0
    }

    # ===== 默认即校验的门禁（无 -Check 开关，默认即输出+退出码） =====
    ClinerulesReferences = @{
        Args   = @()
        Expect = 0
        Note   = '默认即校验；-UpdateIndex 为写操作，聚合器禁用'
    }

    AgentRuntimeUIConfig = @{
        Args   = @()
        Expect = 0
        Note   = '默认校验模式；-FailOnWarning 为严格模式（默认关闭）'
    }

    BatchLineEndings = @{
        Args   = @()
        Expect = 0
        Note   = '默认检查模式；-Fix 为写操作，聚合器禁用'
    }

    ConversationConversion = @{
        Args   = @()
        Expect = 0
        Note   = '默认检查模式；-ShowDetail 显示详情'
    }

    FsSinkEntry = @{
        Args   = @()
        Expect = 0
        Note   = '默认检查模式；-ShowDetail / -StrictDbResidue'
    }

    LogMessages = @{
        Args   = @()
        Expect = 0
        Note   = '默认检查模式；-Strict / -FailOnDynamic / -StrictOrphans 为严格模式'
    }

    SingleWriter = @{
        Args   = @()
        Expect = 0
        Note   = '默认检查模式；-ShowDetail 显示详情'
    }
}
