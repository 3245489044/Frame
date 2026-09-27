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

    NodeLocPlacement = @{
        Args   = @{ Check = $true }
        Path   = 'Frame\PowerShellScript\Modules\Node\Check-NodeLocPlacement.ps1'
        Expect = 0
        Note   = '节点位置反解声明守卫 C1–C5（U12：位置 = loc 反解，不手写声明）：闸A 编译 0 错 / 反解域完备（位置 id ∪ Loc 词表，双向无游离）/ 反解唯一（每位置恰一行）/ 正对照（位置数 ≥ 基线）/ 负对照（U12 §13.2 原样形态必须报 US1003 ∧ US1009）'
    }

    NodeKeyAddress = @{
        Args   = @{ Check = $true }
        Path   = 'Frame\PowerShellScript\Modules\Node\Check-NodeKeyAddress.ps1'
        Expect = 0
        Note   = '节点键地址守卫 A1–A7（批次 1）：位置键字段 ⊆ 身份坐标 ∪ 位置 local / local 自洽 / 身份坐标声明齐备 / 同域键模板可区分 / 单写者多落点提示 / 实例位置 id 一致 / 正对照（位置数 ≥ 8 基线）'
    }

    NodeLocConsistency = @{
        Args   = @{ Check = $true }
        Path   = 'Frame\PowerShellScript\Modules\Node\Check-NodeLocConsistency.ps1'
        Expect = 0
        Note   = '节点 loc 单源一致性守卫 C1–C4（HV-1 / U12 L-7 / L-6）：loc 字面量只允许出现在定义清单（新增即违规）/ 回退债务清单只减不增（列示现存 4 件）/ ReadScope 构造点须带 Loc（读侧位置来源 = 反解，禁绕过）/ 正对照（单源定义 + 声明面 [Space] Loc + ReadScope 构造点数 ≥ 基线）'
    }

    NodePortPurity = @{
        Args   = @{ Check = $true }
        Path   = 'Frame\PowerShellScript\Modules\Node\Check-NodePortPurity.ps1'
        Expect = 0
        Note   = '节点端口纯净性守卫 C1–C4（CT-U3 / P20 / §2.2）：位置层可执行引用零 Usharp（csproj 引用行 + 源码去注释后）/ 位置层不引 HierarchyMethod·UniversalReference（K6）/ 引 Modules.Node 的 Usharp 工程恰为白名单 {Usharp.Adapters.Node} / 正对照（桥接工程存在 ∧ 引用 Modules.Node ∧ 实现 ISpecSource）'
    }

    NodeHierarchyRefs = @{
        Args   = @{ Check = $true }
        Path   = 'Frame\PowerShellScript\Modules\Node\Check-NodeHierarchyRefs.ps1'
        Expect = 0
        Note   = '节点层级引用声明守卫 E1–E5（W1：归属边事实只在声明面）：闸A 0 错 / 边类型集合双向对账（声明 ⇄ CrossDbReferenceTypes 实测用到集）/ 退役常量写用法债务只减不增（HistoryOwnsNode）/ [Ref] 字段白名单（禁未注册字段：静默忽略 ⇒ 假声明）/ 正对照 + 负对照（`loc=` 必须被字段判据命中，且闸A 对其 0 错）'
    }

    AgentOrchestrationFlow = @{
        Args   = @{ Check = $true }
        Path   = 'Frame\PowerShellScript\Modules\Node\Check-AgentOrchestrationFlow.ps1'
        Expect = 0
        Note   = 'Agent 编排归流 E1–E12（声明真源/单步体/静默基线/入参三方对账/调用点与命名空间取值点基线）'
    }

    AgentFrameworkSingleSource = @{
        Args   = @{ Check = $true }
        Path   = 'Frame\PowerShellScript\Modules\Node\Check-AgentFrameworkSingleSource.ps1'
        Expect = 0
        Note   = '四个横切关注点框架单源 D1–D8：诊断载体（IDiagnosticsSink）/ 回退姿态（FlowPosture）/ 键寻址（KeyAddressing）/ 簿记流模板（BookkeepingFlowCatalog）/ 取参单源（WorkflowContextParams）/ 簿记流执行器构造点（BookkeepingFlowAssembly）；基线外置 Baselines\AgentFramework.psd1'
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

    KeyGrammar = @{
        Args   = @{}
        Path   = 'Frame\PowerShellScript\Modules\Parsing\Check-KeyGrammar.ps1'
        Expect = 0
        Note   = '键文法单源守卫 K1–K6：键族字面量单源（白名单）/ 零手写键切片 / 零就地键序 / 零自建比较器 + 正对照（文法定义点数 ≥ 基线、声明绑定用例在位）；-Check 为校验模式，-ShowDetail 显示逐条实测'
    }

    # ===== 入口框架守卫 =====
    InvokeEntrypoints = @{
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '入口框架守卫：① 禁数组 splatting 传开关 ② Args 须 hashtable ③ 入口 try/catch/exit ④ 禁硬编码绝对路径'
    }
}
