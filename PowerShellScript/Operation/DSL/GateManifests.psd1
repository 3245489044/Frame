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

    OperationLayerIsolation = @{
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'N-1 Operation 同层引用隔离（方案 §2.3 G-1）：棘轮基线 18 条只减不增 + 目标对 WorkFlowMethod<->DataStoreDriven 恒 0；层号真源 modules.json roots.layer；D3/D4 正负对照'
    }

    # ===== 默认即校验的门禁（无 -Check 开关，默认即输出+退出码） =====
    NodeFramework = @{
        Args   = @{}
        Path   = 'Frame\PowerShellScript\Modules\Node\Check-NodeFramework.ps1'
        Expect = 0
        Note   = '节点框架纯净性 A–H 段：语言类型不漏进位置层 / 无进程静态可变配置 / 无硬编码位置反解 / 位置层不引上层契约 / 运行态面不得声明为载体（FW-5 Error）/ 声明运行态面须有 run 身份承接点（告警）/ 实例分区零领域词与零实现依赖；-ShowDetail 显示详情'
    }

    ObservabilitySurface = @{
        Args   = @{ Check = $true }
        Path   = 'Frame\PowerShellScript\Modules\Node\Check-ObservabilitySurface.ps1'
        Expect = 0
        Note   = '可见化观测面守卫 C1–C8（F-P0-1c/1d/1i / C-5）：名册在册且已登记 EmbeddedResource / 事件数 ≥ 基线(46) / AgentLog 面关键三键在册（正对照）/ 乱造键不在册（负对照）/ 名册 ∩ 语义台账 = ∅（第二作者）/ 双门面发射面成对（AgentTrace.Visibility + AgentLog 镜像 + ModuleLog 镜像）/ ModuleLog 面（Agent.Modules 10 调用点）关键键在册 / 部署态事件区读面（C8：直读 events.jsonl 断言"存在 ∧ 关键键可检索"；默认报告态，-VerifyDeploy 转违规，-EventStorePath 供正负对照）'
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
        Note   = '节点层级引用声明守卫 E1–E5（W1：归属边事实只在声明面）：闸A 0 错 / 边类型集合双向对账（声明 ⇄ 唯一字面量源 ReferenceTypes 实测用到集；CrossDbReferenceTypes 已退化为别名门面）/ 退役常量写用法债务只减不增（HistoryOwnsNode）/ [Ref] 字段白名单（禁未注册字段：静默忽略 ⇒ 假声明）/ 正对照 + 负对照（`loc=` 必须被字段判据命中，且闸A 对其 0 错）'
    }

    ReferenceVocabulary = @{
        Args   = @{ Check = $true }
        Path   = 'Frame\PowerShellScript\Modules\Node\Check-ReferenceVocabulary.ps1'
        Expect = 0
        Note   = '引用关系词汇守卫 A1（C1–C4）：边类型/端点类型字面量只住唯一源 ReferenceTypes.cs（其余常量类只做别名转发）/ 零依赖层豁免（ScopeFramework·DeclarativeDataflow，只减不增）/ 正对照（单源命中 ≥ 基线）/ 负对照（合成字面量必须被同一判据命中）'
    }

    ReferenceExclusion = @{
        Args   = @{ Check = $true }
        Path   = 'Frame\PowerShellScript\Modules\Node\Check-ReferenceExclusion.ps1'
        Expect = 0
        Note   = '引用框架禁引守卫 B5（C1–C3）：UR 只引「词表 DSL + 图算法只读服务 + 资源模型」—— WorkflowExecutor / IWorkflowExecutor / IUniversalReferenceManager（Agent 面）/ Reference.Relationship / BranchManagement 在两扫描面 0 命中；正对照（适配器面须命中 IUniversalReferenceManager）+ 负对照（合成禁引必报）'
    }

    NodeCollectPort = @{
        Args   = @{ Check = $true }
        Path   = 'Frame\PowerShellScript\Modules\Node\Check-NodeCollectPort.ps1'
        Expect = 0
        Note   = '节点收集式输入端口守卫 C1–C5（inports=nodes.*）：保留 token 白名单 / 单次性 / 出参唯一 / 正对照（收集端口声明数≥基线且 fn∈投影类白名单）/ 负对照（合成 `inports=node.*` 必须报 C1）；运行时 counterpart = WorkflowFunctionRefValidator 的 C1–C3'
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
        Note   = '四个横切关注点框架单源 D1–D10：诊断载体（IDiagnosticsSink）/ 回退姿态（FlowPosture）/ 键寻址（KeyAddressing）/ 簿记流模板（BookkeepingFlowCatalog）/ 取参单源（WorkflowContextParams）/ 簿记流执行器构造点（BookkeepingFlowAssembly）/ 姿态收口台账（D9：RequireFlow 装配点，批次 C 后由 Baselines\AgentFrameworkSingleSource.psd1 的 PostureRequireFlowRequired 翻转生效）/ 读组装组合点（D10）；基线单源 = Baselines\AgentFrameworkSingleSource.psd1（SR-2a 已由遗留子根并入规范根）'
    }

    ClinerulesReferences = @{
        Args   = @{}
        Expect = 0
        Note   = '默认即校验；-UpdateIndex 为写操作，聚合器禁用'
    }

    BakeFramework = @{
        Args   = @{}
        Path   = 'Projects\BuildFramework\Check-BakeFramework.ps1'
        Expect = 0
        Note   = 'CsharpPrj 权威烘焙框架治理 1–6 + 共享边界程序集 7–10（权威副本在册 / 副本外溢=版本偏斜面 / 不源码重编译 / props⇄C# 清单对账）+ 烘焙新鲜度；-Configuration 切换配置，默认 Debug'
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
        Note   = '默认检查模式 + 基线棘轮（F-P0-1e：检查 3/4/5 三类计数只减不增，增量即红；基线 = 同目录 LogMessagesBaseline.json）；-Strict / -FailOnDynamic / -StrictOrphans 为严格模式；-UpdateBaseline 为写操作，聚合器禁用'
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
        Note   = '键文法单源守卫 K1–K6：键族字面量单源（白名单）/ 零手写键切片 / 零就地键序 / 零自建比较器 + 正对照（文法定义点数 ≥ 基线、声明绑定用例在位）；基线单源 = Baselines\KeyGrammar.psd1（脚本零数值基线）；-Check 为校验模式，-ShowDetail 显示逐条实测'
    }

    GateBaselines = @{
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '门禁基线守卫 U1–U10：内联数值基线清册（未在册即拦新增）/ 外置基线类型（Origin ∈ Derived|Debt|Verdict；Debt/Verdict 必带 DueWhen）/ 正负对照（防判据空转）/ 接线一致性（U7：psd1 ↔ 同名消费脚本）/ 命名唯一性·禁第二基线根（U8）/ 事实行类型强制（U9）/ 声明面路径存在性（U10）；基线单源 = Frame\PowerShellScript\Baselines\'
    }

    GeneratedArtifactsDrift = @{
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '生成物漂移守卫 D1–D3：产物存在 / 哨兵在位（手改即失守）/ 复算等价（跑生成器到 temp 逐字比对）；登记表 = Baselines\_generated-artifacts.psd1（含 ContractIndex 与本仓既有 2 个生成物）；新增生成物不登记 = 无人看守产物'
    }

    FrameworkActivation = @{
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '框架激活守卫 FA1：`new ReferenceGraphEngine` 装配点数 ≥ 基线（防图引擎退回死代码/空转）；基线 = Baselines\FrameworkActivation.psd1'
    }

    SemanticCentralization = @{
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '语义集中守卫 SC1–SC2：收口类定义点 ≥ 基线 / 域内边类型字面量直接赋值（非豁免）≤ 基线（只减不增）；基线 = Baselines\SemanticCentralization.psd1'
    }

    CircuitSemantics = @{
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '电路语义守卫 CS1–CS8：唯一宿主接线 / 禁平行引用抽象定义（负对照=0）/ 引用符号在册 / 轮骨架唯一接线 / 装配域唯一接线（CS5：编排唯一=AssemblyPhaseRunner、配方唯一=框架 DSL、执行外壳唯一，含 RecipeAssembly 正对照）/ CS6 接入面编译入口唯一（负对照=0）/ CS7 接入面禁桩件体（负对照=0）/ CS8 缓存键内容指纹（正对照）'
    }

    RecognizerVocabulary = @{
        Args   = @{ Check = $true }
        Path   = 'Frame\PowerShellScript\Modules\Parsing\Check-RecognizerVocabulary.ps1'
        Expect = 0
        Note   = '识别词表守卫 R1–R8：声明面（Config\recognizers.json）JSON 合法且结构齐备 / kind 唯一且含兜底类 / 规则 kind ∈ 词表 / match 仅结构谓词 / TS 侧零词表副本（W-2）/ 负对照（防假绿）/ 正对照基线 / C# 镜像对账（K3 收口前记 pending）'
    }

    # ===== 入口框架守卫 =====
    InvokeEntrypoints = @{
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '入口框架守卫：① 禁数组 splatting 传开关 ② Args 须 hashtable ③ 入口 try/catch/exit ④ 禁硬编码绝对路径'
    }
}
