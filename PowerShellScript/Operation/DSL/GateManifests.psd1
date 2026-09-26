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
        Note   = '承载面 K10（S1/S2 静态可变槽）+ 取用面 S4–S6（键值唯一 / 键↔宿主 1:1 / 取用面类型化）；-WriteManifest 为写操作，聚合器禁用'
    }

    AgentProcessSurface = @{
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '过程面单源守卫：键文法(ProcessKeys) / 事件类别(ProcessNodeKinds) / 进度接收器(SyncProgress)'
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
        Args   = @{}
        Expect = 0
        Note   = '默认即校验；-UpdateIndex 为写操作，聚合器禁用'
    }

    AgentRuntimeUIConfig = @{
        Args   = @{}
        Expect = 0
        Note   = '默认校验模式；-FailOnWarning 为严格模式（默认关闭）'
    }

    BatchLineEndings = @{
        Args   = @{}
        Expect = 0
        Note   = '默认检查模式；-Fix 为写操作，聚合器禁用'
    }

    ConversationConversion = @{
        Args   = @{}
        Expect = 0
        Note   = '默认检查模式；-ShowDetail 显示详情'
    }

    FsSinkEntry = @{
        Args   = @{}
        Expect = 0
        Note   = '默认检查模式；-ShowDetail / -StrictDbResidue'
    }

    LogMessages = @{
        Args   = @{}
        Expect = 0
        Note   = '默认检查模式；-Strict / -FailOnDynamic / -StrictOrphans 为严格模式'
    }

    SingleWriter = @{
        Args   = @{}
        Expect = 0
        Note   = '默认检查模式；-ShowDetail 显示详情'
    }

    FactStandard = @{
        Args   = @{}
        Expect = 0
        Note   = '会话事实标准 C1–C4 + 声明面闸A；-Strict 为严格模式（C3 旧标识残留判失败），-ShowDetail 显示详情'
    }

    # ===== 入口框架守卫 =====
    InvokeEntrypoints = @{
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '入口框架守卫：① 禁数组 splatting 传开关 ② Args 须 hashtable ③ 入口 try/catch/exit ④ 禁硬编码绝对路径'
    }
}
