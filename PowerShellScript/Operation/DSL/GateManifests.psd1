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

    VmKernelReferenceBoundary = @{
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'VM 内核引用面（VM-P6-d）：内核热路径（队列/循环/实例/路由/换代/装载/通用换代服务）零编译依赖——禁 Roslyn（Microsoft.CodeAnalysis）/ CSharpCompilation / VmCompiler；按面分层机器判据'
    }

    AgentRuntimeSlots = @{
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '承载面 K10（S1/S2 静态可变槽）+ 取用面 S4–S8（键值唯一 / 键↔宿主 1:1 / 取用面类型化 / 声明↔形态集合相等〔B2-c〕 / 跨代句柄禁令〔B5-a〕）；-WriteManifest 为写操作，聚合器禁用'
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

    SpecBoundary = @{
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'spec 生成/手写边界（R0 基线冻结）：源码树零生成物 / 清单非空 / 正负对照；基线 Baselines\SpecBoundary.psd1'
    }

    SpecToolingContract = @{
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '规格工具链契约（R2）：SYNOPSIS / 确定性 exit（Debt 棘轮）/ 无深度计数；基线 Baselines\SpecToolingContract.psd1'
    }

    SpecGeneratedBoundary = @{
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'spec 生成/手写分离边界（R4 SEP）：源码树零生成物 / obj 零第二生成物（K2）/ 生成码头注释 / 源⇄产物双向 / manifest 一致性（条件判据）；基线 Baselines\SpecGeneratedBoundary.psd1；-Warn 为回退开关'
    }

    OperationLayerIsolation = @{
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'N-1 Operation 同层引用隔离（方案 §2.3 G-1）：棘轮基线 18 条只减不增 + 目标对 WorkFlowMethod<->DataStoreDriven 恒 0；层号真源 modules.json roots.layer；D3/D4 正负对照'
    }

    SpecOnly = @{
        Args   = @{ Root = @('AgentTest', 'UsharpTests'); Baseline = 'Tools\Gate\baselines\SpecOnlyGate.baseline.json' }
        Path   = 'Environment\CentralTest\Tools\Gate\Test-SpecOnlyGate.ps1'
        Expect = 0
        Note   = '测试载体单一化（AT2001 工具期对拍）：已启用范围（UsharpTests 根级 / Agent.Core.Tests 工程级）内未豁免的 [TestMethod] 即 FAIL；棘轮基线只减不增（新增手写测试 / 向已豁免文件追加）'
    }


    # ===== 默认即校验的门禁（无 -Check 开关，默认即输出+退出码） =====
    NodeFramework = @{
        Args   = @{}
        Path   = 'Frame\PowerShellScript\Modules\Node\Check-NodeFramework.ps1'
        Expect = 0
        Note   = '节点框架纯净性 A–H 段 + S 段（S-1：Form=Stream ⇒ 身份模板单键）：语言类型不漏进位置层 / 无进程静态可变配置 / 无硬编码位置反解 / 位置层不引上层契约 / 运行态面不得声明为载体（FW-5 Error）/ 声明运行态面须有 run 身份承接点（告警）/ 实例分区零领域词与零实现依赖 / Stream 声明面单键身份（与 NodeSpecValidator spec.stream-identity-multi-row 对偶）；-ShowDetail 显示详情'
    }

    NodeGather = @{
        Args   = @{}
        Path   = 'Frame\PowerShellScript\Modules\Node\Check-NodeGather.ps1'
        Expect = 0
        Note   = '节点聚拢通用框架 G1–G3：声明闭合（node-gather-*.json 的 kind-shaping.family ⊆ families + 有 families 须留 "*" 默认塑形）/ 零领域（Gather 层零上层 using Operation·Share·Agent·Usharp 与零域键字面量 process·task-node·conversation·segment·message）/ 接线唯一（INodeGatherService 注册点恰 1 = NodeGatherHosting.TryAttachAll；框架层不消费 INodeDomainPort）'
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
        Note   = 'P6-A 委托接线架构不变式：键格式声明化 / 端口准入单源 / 节点写路径单源 / 运行态面结算单源 / I 段 Stream 结算恰 1 次写（B/C/I 段为 Error；I 段防退回逐帧写）；-ShowDetail 显示详情'
    }

    DomainWireSingleSource = @{
        Expect = 0
        Note   = '节点域接入聚拢点守卫 D1–D3（R4/R5）：派生面（instances\nodes.json 的 placements[] + placement.ushar 的 LocPlacement/LocToPlacement 行）≡ 声明真源 Config\nodes\*.json 的位置并集（漂移即红，-Write 可生成）；散弹点棘轮（per-domain 槽键常量 / 声明路径常量 / 载体工厂方法，报告模式起步，基线 Baselines\DomainWireSingleSource.psd1）；正对照（声明面 ≥ 基线位置数）+ 负对照（合成 per-domain 形态必被捕获）'
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
        Note   = '节点端口纯净性守卫 C1–C5（CT-U3 / P20 / §2.2）：位置层可执行引用零 Usharp（csproj 引用行 + 源码去注释后）/ 位置层不引 HierarchyMethod·UniversalReference（K6）/ 引 Modules.Node 的 Usharp 工程恰为白名单 {Usharp.Adapters.Node} / 正对照（桥接工程存在 ∧ 引用 Modules.Node ∧ 实现 ISpecSource）/ C5 读面三态收口（F-4：三态词面 OkButEmpty·Unavailable·IsUnavailable 在位 ∧ 读面适配器消费 IsUnavailable；附负对照）'
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
        Note   = '四个横切关注点框架单源 D1–D11：诊断载体（IDiagnosticsSink）/ 回退姿态（FlowPosture）/ 键寻址（KeyAddressing）/ 簿记流模板（BookkeepingFlowCatalog）/ 取参单源（WorkflowContextParams）/ 簿记流执行器构造点（BookkeepingFlowAssembly）/ 姿态收口台账（D9：RequireFlow 装配点，批次 C 后由 Baselines\AgentFrameworkSingleSource.psd1 的 PostureRequireFlowRequired 翻转生效）/ 读组装组合点（D10）/ 运行期本地流 msgId 单源（D11：统一 DiagnosticsCodes + context）；基线单源 = Baselines\AgentFrameworkSingleSource.psd1（SR-2a 已由遗留子根并入规范根）'
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
        Note   = '默认检查模式；H 段 Stream 位置键单键（单行不变量：form=stream ⇒ key 模板无逐事件坐标）；-ShowDetail 显示详情'
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
        Note   = '电路语义守卫 CS1–CS10：唯一宿主接线 / 禁平行引用抽象定义（负对照=0）/ 引用符号在册 / 轮骨架唯一接线 / 装配域唯一接线（CS5：编排唯一=AssemblyPhaseRunner、配方唯一=框架 DSL、执行外壳唯一，含 RecipeAssembly 正对照）/ CS6 接入面编译入口唯一（负对照=0）/ CS7 接入面禁桩件体（负对照=0）/ CS8 缓存键内容指纹（正对照）/ CS9 编排动作分派配方化（负对照=0：禁 switch(operation.Op) 与漂移兜底；CS9-d 正对照：配方驱动在册）/ CS10 区域子计划编译入
口唯一（负对照=0：CircuitCompiler 之外禁重排配方域+电路域校验；CS10-d 正对照：区域子计划注册表在册）/ CS10-e 区域镜像单一来源（负对照=0：投影/符号定义处之外引用 LoopSymbol） / CS23 递归执行化在册（正对照：RegionNodeBase 执行面 + 能力表 Recursion/BaseCase + 投影 RecursionSymbol 接线） / CS-P1 编译入口唯一（负对照=0：DataCenter/接入面禁自建重排 配方校验→电路校验；正对照=接入面经 CircuitCompiler.TryCompile）/ CS-P2 接入面禁平行执行外壳（负对照=0：new CircuitEngine( / CircuitAssemblyRunner.RunAsync(）/ CS-P3 框架零领域类型（负对照=0：CircuitFramework 内禁 DataCenter./PortStandard/PortImplStandard/PortInstr/PortImplVocabulary；正对照=框架源码在册）'
    }

    RecognizerVocabulary = @{
        Args   = @{ Check = $true }
        Path   = 'Frame\PowerShellScript\Modules\Parsing\Check-RecognizerVocabulary.ps1'
        Expect = 0
        Note   = '识别词表守卫 R1–R11：声明面（Config\recognizers.json）JSON 合法且结构齐备 / kind 唯一且含兜底类 / 规则 kind ∈ 词表 / match 仅结构谓词 / TS 侧零词表副本（W-2）/ 负对照（防假绿）/ 正对照基线 / R8 C# 镜像对账（到期：缺失即违规）/ R9 live∩snapshot 成节点类别 = ∅ / R10 聚拢声明品类闭合 / R11 双向对拍（镜像字面量集 ≡ 声明面；编译视图须经 RecognizedKinds.All）'
    }

    # ===== 入口框架守卫 =====
    AnalysisDocGrounding = @{
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '分析文档根因对账（G5/R5）：opt-in（marker: grounding:tri-state）文档的「根因」章节内容行须三态标注（事实/建议/待核实）+ 反引号仓库相对路径 token 须在磁盘存在'
    }

    InvokeEntrypoints = @{
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '入口框架守卫：① 禁数组 splatting 传开关 ② Args 须 hashtable ③ 入口 try/catch/exit ④ 禁硬编码绝对路径'
    }

    # ===== 验证单入口守卫（G8）=====
    VerifySingleShot = @{
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '验证单入口守卫（G8）：① Frame\Invoke\*.ps1 禁原生 dotnet test/build（薄转发）② 唯一验证命令接线（axioms.md 声明 VERIFY_SINGLE_ENTRY ∧ 指向已存在入口 ∧ project.md §1.10 登记）③ 正/负对照（防判据空转）；P2 唯一命令纪律；运行面由 §1.10 相位白名单 + [批次声明] 自审承担'
    }

    FallbackInventory = @{
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '框架回退登记守卫 F1–F6：?? new 白名单（组合根 + Null* 工厂）/ catch 吞并 / legacy 棘轮 / 回退可见化 / [Obsolete] dep / 灰发开关登记；基线 = 同目录 fallback-baseline.json（只减不增）；-UpdateBaseline 为写操作，聚合器禁用'
    }

    InitGuards = @{
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '初始化护栏门禁（P1）G1–G4：`throw new ArgumentNullException` / `ArgumentNullException.ThrowIfNull` 只减不增 + `PortGuard.Required` 只增不减 + 正/负对照；基线 = Baselines\InitGuards.psd1（单源，禁内联）'
    }

    # ===== 过程层结构治理（修复文档 G/H 批）=====
    BatchScope = @{
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '批次授权单守卫 B0–B3（过程层 P1/P2/P6）：实际改动集（git status，含子模块）⊆ 活动批 allowSet ∪ derived ∪ preexisting；denySet 面禁改；正/负对照防假绿。授权单路径单源 = Baselines\BatchScope.psd1（Docs\analysis\2026\10\10.5\2026-10-05-过程层缺陷结构治理.batches.json）；授权单缺件 ⇒ 灰度 warn'
    }

    VerifyProportionality = @{
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '验证成比例守卫 H0–H5（过程层 P4）：产品单工程改动成比例（验证/改动比 ≤ 1.5）∧ 映射落点 ∧ 不静默丢面 ∧ 未分类改动 fail-loud；基线 = Baselines\VerifyProportionality.psd1'
    }

    # ===== 反模式族守卫（P9 落地）=====
    AntiPatternFamily = @{
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '反模式族守卫（P9）：clinerules\formula.md §8（族=通用规则单表）C1 族唯一/件数 ≥ 基线 · C2 covered ID 形态合法且全局唯一（每条恰属一族）· C3 crit∈{macro,micro} · C4 通用规则列非空 · C5 通用规则符号可解析（回指已注册定义点）· C6 ID 集合守恒（covered ⊇ 基线，只增不减）· C7 正/负对照；基线 = Baselines\AntiPatternFamily.psd1；-WriteBaseline 为写操作，聚合器禁用'
    }

    # ===== 端口层消除（D3 僵尸脚本）=====
    DualCarrierExclusion = @{
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '双载体互斥门（D3）：端口块禁「impl delegate ∧ script」并存（僵尸脚本）；D1 新增即红 · D2 只减不增（可销账可见化）· D3 正/负对照（防假绿）；基线 = Baselines\DualCarrierExclusion.psd1'
    }

    # ===== VM 换代路线收敛（VM-R1 构建到新目录）=====
    GenerationLayout = @{
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'VM-R1 代目录布局契约：gen-<n>（n ≥ 1）须含全 Agent.*.dll（计数 ≥ 就地产物面参照）+ Agent.Launcher.deps.json（ALC 解析基座=本目录）+ vm-program.json；代根 = Projects\Plug\Agent\BuildOutput\Generations；落点禁 BuildOutput\<Config>（被运行进程锁定）'
    }

}
