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
    NodeFramework = @{
        Args   = @{}
        Path   = 'Frame\PowerShellScript\Modules\Node\Check-NodeFramework.ps1'
        Expect = 0
        Note   = '节点框架纯净性 A–H 段：语言类型不漏进位置层 / 无进程静态可变配置 / 无硬编码位置反解 / 位置层不引上层契约 / 运行态面不得声明为载体（FW-5 Error）/ 声明运行态面须有 run 身份承接点（告警）/ 实例分区零领域词与零实现依赖；-ShowDetail 显示详情'
    }

    NodeWriteBypass = @{
        Args   = @{}
        Path   = 'Frame\PowerShellScript\Modules\Node\Check-NodeWriteBypass.ps1'
        Expect = 0
        Note   = 'P6-A 委托接线架构不变式：键格式声明化 / 端口准入单源 / 节点写路径单源 / 运行态面结算单源（B/C 段为 Error）；-ShowDetail 显示详情'
    }

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

    SessionDuplication = @{
        Args   = @{}
        Expect = 0
        Note   = '会话逻辑重复消除（台账 D1–D17）：默认基线模式（拦新增重复点，未达标项可见化为[待消除]）；-Final 为终态模式（按 Target 判，批完成后启用）；-ShowDetail 显示逐条实测'
    }

    # ===== 入口框架守卫 =====
    InvokeEntrypoints = @{
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '入口框架守卫：① 禁数组 splatting 传开关 ② Args 须 hashtable ③ 入口 try/catch/exit ④ 禁硬编码绝对路径'
    }
}
