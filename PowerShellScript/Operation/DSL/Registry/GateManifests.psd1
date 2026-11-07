@{
    KeyDomainStrict = @{
        Domain = 'KeySpace'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '键域严格化（K-01）：KD1 构造收口（KeyRef/KeyDomainId 只允许在 KeySpace 内 new） / KD2 域词隔离 / KD3 域注册表有产品侧消费方 / KD4 负对照'
    }

    KeyCorpusGolden = @{
        Domain = 'KeySpace'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'KB1 golden 键语料（阻断）：结构/source 枚举/族×面覆盖/边界样本齐备；负对照缺族必红'
    }

    KeyRoundTrip = @{
        Domain = 'KeySpace'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'KB1 往返（阻断）：每族可往返条目在册 + KeyRoundTripTests 存在且调用 Render/TryParse；负对照空语料必红'
    }

    KeyOpsClosedSet = @{
        Domain = 'KeySpace'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'KB2 固有函数闭集（棘轮）：键上下文拼接/插值/前缀判据/切片只经 KeyOps；基线只降不升'
    }

    KeyDeclSingleSource = @{
        Domain = 'KeySpace'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'KB2 键形字面量单源（棘轮）：域前缀/模板只允许目录+模板常量+声明装载器；基线只降不升'
    }

    KeyParseSingleSource = @{
        Domain = 'KeySpace'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'KPK1 解析单源（阻断）：域键类内禁 StartsWith/IndexOf/Substring/Split/数值解析，解析只经 KeyOps.TryParse*'
    }

    KeyDeclNoLogic = @{
        Domain = 'KeySpace'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'KP6 声明无逻辑（阻断）：DomainKeyFamilies 仅 id 常量与只读转发，禁模板/面/形态构造'
    }

    KeyParseAmbiguity = @{
        Domain = 'KeySpace'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'KPK1 歧义阻断在册（阻断）：特异性/歧义/轴词表/必填轴/作用域用例齐备；行为由 KeySpace.Tests 承担'
    }

    KeyAxisCompleteness = @{
        Domain = 'KeySpace'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'KB6 轴完备：规范模板有轴，面模板占位符 ⊆ 规范轴；负对照必红'
    }

    KeySurfaceDistinguishable = @{
        Domain = 'KeySpace'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'KB6 面可区分：同族各面模板形状两两不同；负对照必红'
    }

    KeyOwnerUnique = @{
        Domain = 'KeySpace'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'KB6 归属唯一：族 id 唯一 + 规范模板形状不重复；负对照必红'
    }

    KeyIdentity = @{
        Domain = 'KeySpace'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '键身份语义层（J2/J3/J4/J9）：[Entity]/[KeyLexicon]/[AnswerShape]/[WindowShape]/[KeyPolicy]/[KeyBridge] 结构闭包 + 权威面唯一 + 执行端零泄漏；负对照必红'
    }

    KeyPolicy = @{
        Domain = 'KeySpace'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'data'
        Expect = 0
        Note   = '键写侧策略（J5/J7 声明面）：终结实体 cardinality=one-per-turn ∧ 单写者 ∧ window_single_parent 不变量在册 ∧ 形状 default 非空；负对照必红'
    }

    PromptCacheStability = @{
        Domain = 'Agent'
        Family = 'Declaration'
        Form   = 'SingleSource'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '提示词单源与缓存稳定（B6）：C1 段声明面在册且无第二张别名表 / C2 required_prompt_ports ⊆ cline_execute.inports（结构事实） / C3 UI 无节点端口反读（快照单源） / C4 指纹基线（report 级） / C5 负对照'
    }

    WireFabric = @{
        Domain = 'KeySpace'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '端口接盒框架（WireFabric）：C0 登记在位 / C1 依赖面仅 KeySpace+Parsing.Keys / C2 框架文件齐 / C3 测试别名在册 / C4 引用数非空正对照 / C5 第三个引用必红的负对照'
    }

    DeclarationClosure = @{
        Domain = 'KeySpace'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '声明→绑定闭合（K-02）：DC1 声明 fn 必须已注册 / DC2 声明 outports ⊆ 实现产出键（集合闭合 = GateClosure；输出 coverage=）/ DC2s Strict 越档（Coverage<1.0 判红）/ DC3 冻结产物含指纹 / DC4 负对照（含空声明⊥）/ DC5 豁免册单源'
    }

    ExecutionOwnership = @{
        Domain = 'KeySpace'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '执行归属（K-03）：EO1 建成员不驱动 / EO2 驱动必须 TryAcquire / EO3 归属内核在位且要素齐备 / EO4 负对照'
    }

    OrchestrationKernel = @{
        Domain = 'Process'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '编排内核单一性（Block 单内核 / Pipeline 适配 / Circuit 描述）：C1 Block 零引用 / C2 BasicMethod 禁反向依赖 / C3 执行循环与重试唯一 / C4 结果类型唯一 / C5 Pipeline 委托内核 / C6 Circuit 走 BlockPipeline / C7 正负对照'
    }

    ShareMessagingBoundary = @{
        Domain = 'Agent'
        Family = 'Boundary'
        Form   = 'Boundary'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '通用信息传递内核边界（Share.Messaging）：C1 领域零泄漏 / C2 零 ProjectReference / C3 禁时钟与网络 / C4 唯一状态枚举 / C5 直连禁令（Share 树禁 ITransferKeyStore·ISessionSend·SignalIntake）/ C6 三处登记齐备 / C7 正负对照 / C8 适配器面'
    }

    ContextGeometryBoundary = @{
        Domain = 'Agent'
        Family = 'Boundary'
        Form   = 'Boundary'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '上下文几何内核边界（Share.ContextGeometry）：C1 领域零泄漏 / C2 零 ProjectReference / C3 禁时钟·网络·文件 IO / C4 段序纪律（禁按 Key 排序） / C5 三处登记齐备 / C6 正负对照'
    }

    ContextGeometryPrefixPurity = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'Boundary'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '前缀纯度（R3）：前缀正文/规范字节/段禁含信封字段（MessageId/Timestamp/TraceId/CreatedAt）；正负对照防恒真'
    }

    NodePromptBinding = @{
        Domain = 'Agent'
        Family = 'Declaration'
        Form   = 'Closure'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '节点提示词绑定：模板槽 ⊆ PromptSlot 词表 / node:// domain 已声明 / 禁 SQL 旁路 / 禁新增 ContextNodeKind 用法；正负对照'
    }

    EndpointKeyConsistency = @{
        Domain = 'Data'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '端点键一致性（node/task 加载失败）：G1 history 源 ∈ 历史根派生端点 / G2 cross_db_link task 源 ∈ 树可达成员 / G3 端点 Id 不动点 / G4 session 源不动点；基线棘轮只降不升 + 负对照'
    }

    AgentModulesFrameworkLayout = @{
        Domain = 'Agent'
        Family = 'Boundary'
        Form   = 'Boundary'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'Agent.Modules.Framework 归属唯一 / 契约不内联 / 依赖方向 / 零 Agent 件可见化；C5 正负对照（内存合成，零侧写）'
    }

    AssemblyVerdictClosure = @{
        Domain = 'Node'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '装配裁决闭门：AV1 位置对账未解析必红 / AV2 就绪覆盖显式声明 / AV3 指标口径与节点域一致 / AV4 调用面 Shadow 可追溯或可阻塞 / AV5 边准入声明为空不可默认可过 / AV6 声明孤儿默认姿态可判 / AV7 部署存在≠被引用 / AV8 聚拢健康与封存内容判据 / AV9 VM 占位不得据此判过；九组正负对照 + 存量逐条豁免'
    }

    # ===== 带 -Check 开关的门禁（-Check 时退出码裁决） =====
    RelationPlan = @{
        Domain = 'Node'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '关系声明面单源：E1 [Relation] 存在 / E2 edge ⊆ [Edge] / E3 归属边恰一 ensure / E4 hierarchy contains 对账 / E5 负对照 / E6 intent+exists 显式 / E7 C# 归属边字面量 ≤ 基线（负对照）'
    }

    GraphPortSurface = @{
        Domain = 'Node'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '对外端口单文件：PS1 端口命名空间文件外 public = 0（白名单对账）/ PS2 文件内无方法体·new·IO / PS3 Agent.* 不引用实现类 / PS4 变更分类 + 迁移说明 / PS5 类型名无域词 / PS6 子命名空间受限 / PS7 负对照'
    }

    GraphDeclarationDrift = @{
        Domain = 'Node'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '声明面漂移：GD1 产品 depends_on 闭合 + 面字段 ⊆ 产品出字段 / GD2 面须有宿主视图层 + owner·channel 双维肯定式白名单 / GD3 视图内容域须有同名 [Gather] 段（缺口仅在基线棘轮在册，只降不升）/ GD5 derives= 血缘闭合（≥1 上游 + 每个字段对逐对闭合）/ GD4 负对照'
    }

    DeclaredFlowConsumers = @{
        Domain = 'Declarations'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'UPC D-F1 声明-消费：每个 [Flow] 入口方法（受控映射表）在生产源码有调用点 ≥1，或行内声明 Dead=<reason>；负对照零调用样本必红'
    }

    FactProducerSingle = @{
        Domain = 'Declarations'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'UPC D-F2 事实生产者单一：登记事实的 producer 类型定义数=1（MemberWindowReader / TaskRuntimeReportProvider / PortContextAssembler）；负对照重复定义必红'
    }

    EmptyReasonLiveness = @{
        Domain = 'Declarations'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'UPC D-F3 空因活性：每个 PortContextEmptyCodes 受控码有生产点，且三出口存在 EmptyCode/EmptyReason 透传；负对照丢弃 code 必红'
    }

    PortContextShapeEquality = @{
        Domain = 'Declarations'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'UPC D-F5 三出口同形：IPortContextConsumer 实现=3 且均调用 PortContextProjection.Project(context)，Consumer 类体无字段裁剪；负对照单侧裁剪必红'
    }

    SyntheticVisible = @{
        Domain = 'Declarations'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'UPC D-F7 合成壳可见：合成分支调用 AsSynthetic + SyntheticOnly，且不得 rowsEmptyReason=null；卡片模型含 ContextSynthetic；负对照清空因必红'
    }

    FactFieldCoverage = @{
        Domain = 'Declarations'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'UPC D-F4 事实字段位：face.fields 与 product.out 集合全等 + empty_reason/synthetic/window_digest 必有字段位 + read-paths 数值 == HistoryPortContextSpec 常量；负对照面字段缺位必红'
    }

    NoRoleAttribution = @{
        Domain = 'Node'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '归属判据：C# 中 Role ==/!= ConversationRole.* 归属判定 = 0（面选择走通道白名单）；负对照必红'
    }

    FaceWhitelist = @{
        Domain = 'Node'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '面白名单：每个 [Face] 的 product/owner/channel 齐备 + product 已声明 + fields ⊆ out；负对照缺 channel/悬空 product 必红'
    }

    EdgeSingleWriter = @{
        Domain = 'Node'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '边写单原语（C1）：HierarchyRef 归属边映射完备 / 每条业务边恰 1 写者 / 写者坐标可解析 / operation 与 edge 对账；负对照双写者必红'
    }

    NoRelationLiteral = @{
        Domain = 'Node'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '关系边名字面量（C2）：C# 边名字面量 = 0（适配器白名单在基线 Allow）；边名集合来自 [Ref] edge= 单源；负对照必红'
    }

    NoRelationJoin = @{
        Domain = 'Node'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '集合查询实现点（C3）：唯一实现点存在且第二自 join 实现点 = 0（基线 Allow 棘轮）；负对照必红'
    }

    UsharpFirst = @{
        Domain = 'Node'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '声明优先（C17）：字符串规则表 switch / 规则表字典 / 判定魔数 / 同名常量异值 均在基线棘轮内（只降不升）；负对照必红'
    }

    TopologyLineage = @{
        Domain = 'Node'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '拓扑血缘（C6）：derives= 字段对形态/端点闭合/边键唯一 + 生产调用点（DeclarativeTopologyLineage ∧ PlanFromField）≥1；负对照必红'
    }

    EdgeStamp = @{
        Domain = 'Node'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '边盖章（C8）：回填报告 schema/计数齐备且 origin_flow 为空 = 0（报告缺失 ⇒ 判据不可判定，fail-closed）；负对照必红'
    }

    WriteSurface = @{
        Domain = 'Node'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '关系写面清单（RF6 判红态）：C1 清单↔声明 / C2 状态谎报（migrated 仍直写；数据流残留须与 C4 白名单双向一致）/ C3 pending 棘轮（当前=0）/ C4 写入口唯一 / C5 负对照'
    }

    # U4 判红态（G1/G2/G3 已全绿；回退即红）
    RelationGather = @{
        Domain = 'Node'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '关系聚拢点：G1 聚拢点实现=1 且调用点直连写=0 / G2 层向（UR 不得 using Operation./Modules.Node/LiteDB/Plate.DslMethod；Modules.GraphProcessing|Pipeline 豁免）/ G3 派生单源（ContainerReferenceGrounding 必须委托 [Derive] HistoryNodes）/ G4 负对照'
    }

    NodeEdgeIntegrity = @{
        Domain = 'Node'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '归属边跟随落库事实（I2）：C1 node_refs 声明 / C2 旧产出枚举生产调用点=0 / C3 实例键禁截断 / C4 实例键→裸键兼容读 / C5 PersistResult 单源 / C6 边写节点存在断言 / N1-N2 负对照 / D1 可选 DB 幻影边审计'
    }

    AgentBuildPoint = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'SingleSource'
        Args   = @{ Check = $true }
        Expect = 0
    }

    ToolCallConvergence = @{
        Domain = 'Agent'
        Family = 'Contract'
        Form   = 'SingleSource'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '工具调用聚拢（A1–A7）：Launcher 不再装配 / 注册单点 / 客户端单实例 / 声明面单一 / 无回环回退支 / 内核纯净 / 无依赖倒置'
    }

    AgentDataSeparation = @{
        Domain = 'Agent'
        Family = 'Boundary'
        Form   = 'Consistency'
        Args   = @{ Check = $true }
        Expect = 0
    }

    HistoryAssemblySurface = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'Consistency'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'History 装配面：空态预装配(H1) / 档案分档+声明面(H2) / mode 注入(H3) / 视口单一入口(H4) / 端口只读(H5) / 缓存最大化分段+差分(H6) / UI 轨零 Binding(H15) / 双出口同供数(H16) / 空态可见化(H17) / 任务池状态单源(H18)'
    }

    TransferKey = @{
        Domain = 'Agent'
        Family = 'Contract'
        Form   = 'Closure'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '键址传递框架 KTF：键文法/方向-kind-status 词表/6 个 transfer_key_* 单步/agent.transfer 族/ps_runtime+transfer_key 轨/powershell tool_face=full/仓库+数据根双写'
    }

    KeyspaceMirror = @{
        Domain = 'Agent'
        Family = 'Contract'
        Form   = 'Drift'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '键空间镜像（E26）：仓库 agent.keyspace.json vs Data 运行时声明的 family 模板 + port 集合等价；Data 缺失 = 干净 checkout，跳过并提示'
    }

    AgentDefaultDispatch = @{
        Domain = 'Agent'
        Family = 'Declaration'
        Form   = 'Closure'
        Args   = @{}
        Tier   = 'fast'
        Expect = 0
        Note   = '默认路由（P5-A/B）：结构化意图 → 标签 → 执行腿成对；file 标签/执行腿经声明键驱动（R-07 迁移至 Frame DSL\Agent，唯一实现）'
    }

    PsSkillToolMap = @{
        Domain = 'Agent'
        Family = 'Contract'
        Form   = 'Closure'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'SKILL↔ActionToTool 对账（R-05）：contracts action 集 == ActionToTool.Keys，ActionToTool.Values == SKILL.md 契约轨工具名集；正/负对照'
    }

    LlmRoutingSurface = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'Boundary'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'LLM 路由生效面：L1 禁静默降级(catch→local) / L2 缺省声明面(禁硬编码 local 兜底) / L3 生产兜底非 local / L4 自举不覆写配置真实 provider / L5 来源可审计(RouteOrigin origin)'
    }

    AgentDecisionCoreBoundary = @{
        Domain = 'Agent'
        Family = 'Boundary'
        Form   = 'Boundary'
        Args   = @{ Check = $true }
        Expect = 0
    }

    VmKernelReferenceBoundary = @{
        Domain = 'Process'
        Family = 'Boundary'
        Form   = 'Boundary'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'VM 内核引用面（VM-P6-d）：内核热路径（队列/循环/实例/路由/换代/装载/通用换代服务）零编译依赖——禁 Roslyn（Microsoft.CodeAnalysis）/ CSharpCompilation / VmCompiler；按面分层机器判据'
    }

    AgentRuntimeSlots = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'Boundary'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '承载面 K10（S1/S2 静态可变槽）+ 取用面 S4–S8（键值唯一 / 键↔宿主 1:1 / 取用面类型化 / 声明↔形态集合相等〔B2-c〕 / 跨代句柄禁令〔B5-a〕）；-WriteManifest 为写操作，聚合器禁用'
    }

    RefreshFrameworkBoundary = @{
        Domain = 'Agent'
        Family = 'Boundary'
        Form   = 'Boundary'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'UI 刷新通用框架红线（Strategy.Refresh）：框架内零领域串（agent/conversation/db/task/session/history）/ 零上层依赖（Agent./Plate./UiBackend./FrontEndDriven.）；正负对照 -NegativeControl'
    }

    UiSelectionExternalization = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'Consistency'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '选择态外置守卫（框架级态外置）：C1 刷新协议禁 DOM 反查（PollAttrs 第三/四参 null）/ C2 采集端（capture 产出 uiState.select + [data-select-slot]）/ C3 消费端（HtmlBridgePayload.SelectionKeys + UiSelectionState）/ C4 历史归属解析禁伪造 default / C5 选中态受控于 UiSelectionStore；负对照 -NegativeControl'
    }

    RenderCoverage = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'SingleSource'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '字段覆盖率三表联立：frame-kinds:fields × fields.generated × surface-map.generated（缺一即红）'
    }

    CarrierNeutral = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'SingleSource'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'Composer/Projector 零载体字面量（载体词只允许在 surface-map 声明面）'
    }

    SurfaceAssets = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'Consistency'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
    }

    SurfaceCoverage = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'Consistency'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '视图/容器/区域闭合：owner=view / role·slot·write 齐全 / 映射存在 / 容器列入所属视图 / region∈ui-regions kind / 每 kind 有容器引用；负对照脏映射必红'
    }

    SurfaceMappingCoverage = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'Consistency'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '映射/载体/槽位闭合：每映射 html.tag·winforms.control·text 键齐全 / 每容器载体有结构 / 每 slot 至少一个 bind≠none 且 prop 非空；负对照删 tag 必红'
    }

    SurfaceParity = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'Consistency'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '载体能力闭合：每图元每载体 caps 三态齐全 / 每被层引用图元至少一载体 supported / profile 材质层可落地 / unsupported 载体仍有结构；负对照全 unsupported 必红'
    }

    SurfaceSourceSingle = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'SingleSource'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '表面源码零具体值：Surface/Skin/Paint/Capability 面不得出现 hex 色值与 \d+px 字面量（声明面与生成物豁免）；负对照合成 #ffffff 必红'
    }

    SurfaceProgram = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'Consistency'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '表面程序结构闭合（surface-v1）：容器 view=owner / write 受控 / 映射存在 / 视图→容器闭合 / 无孤儿映射 / 外观闭合（层→图元、材质→层+fallback、Profile→材质、caps 三态、每图元有落地载体）；负对照删映射必红'
    }

    ViewFingerprint = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'SingleSource'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '三视图指纹两两不相交（七元组 + badge/accent）；history 禁 l0-only 截断'
    }

    UiProgramCoverage = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'SingleSource'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'UI 个性化单一声明程序（ui-v1）：槽位→token 闭合、每载体元素齐全、皮肤链无环、单元无零样式；负对照未知 token 必红'
    }

    UiCarrierParity = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'SingleSource'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '三载体声明一致性：carriers 单源 = 单元 carriers = surface-map 角色载体集；负对照缺载体必红'
    }

    UiProjectionCapability = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'SingleSource'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '投影能力矩阵：caps 键文法(channel_primitive) / property 通道须声明 property_fill / property_fill 必须 supported-opaque；负对照 property_fill=supported 必红'
    }

    UiMaterialPropertyWriter = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'SingleSource'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '材质属性写点唯一（扫描面=FrontEndDriven 全子树）：材质写点=WinFormsPropertyProjector.cs 恰 1；声明属性写点仅 WinFormsControlAdapter.cs 与 WinFormsSurfaceBackend 的 descriptor 字段；负对照未白名单写点必红'
    }

    PublicApiAdditive = @{
        Domain = 'Agent'
        Family = 'Boundary'
        Form   = 'SingleSource'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '公共 API 只允许加法：签名基线 Baselines\PublicApi.json 的条目不得删除/改签名（P2）；写开关 -UpdateBaseline 不在聚合器使用；负对照基线多余签名必红'
    }

    UiViewCatalog = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'SingleSource'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '视图词汇单源（D01）：C1 目录声在位 / C2 四个消费方均查表 / C3 领域禁本地 MainDialogViewType => "type" 映射；负对照旧映射必红'
    }

    # ===== 读路径 / 声明面对账 / 视图黄金样本（P1/P2/P6）=====
    ReadFactCoverage = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'Drift'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '读路径事实覆盖（R1–R5）：read-paths.json 每条路径 facts 非空 / 域 ⊆ [Gather] / anchorCapable=false 须给理由 / session-message 必声明 terminal+archived'
    }

    ViewContract = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'SingleSource'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '视图读面契约（P1 起）：C1 ViewCompiler 定义点=1 / C2 ViewSnapshot 定义点=1 / C3 契约件齐备 / C4 View 平台层零 Agent 依赖 / C5 装配器零数据源字面量 / C6（-Stage p2）UI 不直取端口 / C8 黄金样本四态覆盖+唯一 id；双定义·using Agent.·smsg 字面量负对照必红'
    }

    DeclarationParity = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'Drift'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '声明面对账（D1–D6）：[Gather] 全覆盖 read-adapter / 适配器闭集 / stream-frame 键族耦合 / 视图内容域与读路径域 ⊆ [Gather] / 渲染层无 view== 分支'
    }

    ViewGolden = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'Drift'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '视图黄金样本（G1–G4）：三视图各 ≥2 场景，每场景 input/expected.snapshot/expected.dom 齐备且 snapshot.view 与目录一致'
    }

    UiNoViewBranch = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'Boundary'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '渲染层零视图分支（P2/B2）：C1 switch(viewType)/if(viewType==) = 0 / C2 视图差异在目录 Regions / C3 渲染器按 model.Regions 单点拼接；负对照视图分支必红、区域分派不误报'
    }

    UiRegionOwnership = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'SingleSource'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '区域归属（P1）：C1 nodes 不再前置 runtime / C2 nodes 不承载 feed·board·prompt / C3 五区域各恰一个生产者 case；负对照 nodes 越界必红'
    }
    ViewProgramConsistency = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'Consistency'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '视图程序一致性（VSC-A1）：C1 产物在位且结构合法 / C2 产物 ≡ 声明重生成（逐键）/ C3 读取面 = 嵌入产物单源；负对照容器键篡改必红'
    }

    ViewContainerOwnership = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'SingleSource'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '视图容器所有权（VSC-1）：C1 容器非空 / C2 ownerView == viewKey / C3 视图内容器键唯一 / C4 流容器≤1 / C5 框架源码零 UiRegionKind；负对照跨视图 owner 必红'
    }

    NoClearWrite = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'Boundary'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '无清空写（VSC-3）：C1 SetView 下线 / C2 _pendingStreamClear 下线 / C3-C4 无 Trim(0)/空串清空目标；负对照清空写点必红'
    }



    UiTextSafe = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'Boundary'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '文本安全（P4/C4）：C1 HtmlText 单源在位（非标量 ⇒ ⊘）/ C2 HtmlSectionItem 经 HtmlText / C3 Views 内禁对象字段直拼；负对照 details.Add(row.Payload) 必红'
    }

    UiLocDeclaration = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'SingleSource'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'UI 位置反解（loc 反转）：C1 闸A 编译 0 错 / C2 spec 键集 ≡ 声明坐标集（G1/G3，无生成物）/ C3 C# 引用 loc 闭合 / C4 Views 中文字面量棘轮只降不升；负对照=未声明 loc 与缺行声明必红'
    }

    FallbackInventoryRate = @{
        Domain = 'Agent'
        Family = 'Meta'
        Form   = 'Ratchet'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '兜底清册（P3/C7）：C1 未登记兜底 = 0（template-empty/empty-catch/tryload-null）/ C2 登记表非空；负对照未登记必红'
    }

    UiReadOutcome = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'SingleSource'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '读取代数单源（D03/D05）：C1 框架 UiReadState 四态 + UiReadSpec 在位 / C2 领域消费 UiReadGateway / C3 旧 TaskConversation* 三态不残留；负对照旧枚举必红'
    }

    UiRenderEntry = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'SingleSource'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '渲染入口单源（D10）：C1 RenderOutput 构造点收敛（契约外=0）/ C2 CVS 经 RenderOutputCoordinator.BuildAsync / C3 领域禁自建 RefreshTraceId.New；负对照领域直构必红'
    }

    UiAnchorWrites = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'SingleSource'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '选择态单写者（E04）：C1 SetCurrentHistory/SetCurrentTask 只允许在 ConversationViewService.cs / C2 BuildViewOutputAsync 具备 rememberTask 申报面；负对照 handler 直写必红'
    }

    AgentProcessSurface = @{
        Domain = 'Agent'
        Family = 'Contract'
        Form   = 'SingleSource'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '过程面单源守卫：键文法(ProcessKeys) / 事件类别(ProcessNodeKinds) / 进度接收器(SyncProgress)'
    }

    EntrySingleSource = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'SingleSource'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '入口单源守卫（写库即实现 · Agent.Core.Entry）：E-1 AgentApiPort 定义恰 1 · E-1b AgentEntryCallableTrack 恰 1 · E-2 AgentIntentMapping 恰 1 · E-3 AgentDispatcher 恰 1 · E-4 通用轨经 AgentEntryPort 转发 · P-1 正对照（intent 常量 ≥ 基线）· P-2 负对照'
    }

    IntentMappingNoLiteral = @{
        Domain = 'Agent'
        Family = 'Contract'
        Form   = 'Boundary'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'intent 映射字面量（B4/DUP-10）：AgentIntentMapping 的 _fnToIntent 键零 wire 字面量（改引用 WorkflowFunctionNames.*）；正/负对照'
    }

    DbToolbarDictAlloc = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'Ratchet'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '数据库页工具条字典分配（B3/RED-13）：渲染路径零逐命令 `new Dictionary{type=db}`（改走 DbCmdAttrs.For 缓存基座）；正/负对照'
    }

    RecordSingleTextForm = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'Boundary'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '记录单一文本形态（B3/RED-06）：DetailPayloadJson 引用 = 0（载荷视图由记录态派生）· DetailRecordJson 在位；正/负对照'
    }

    GatherDiagnosticsSingleSource = @{
        Domain = 'Agent'
        Family = 'Observability'
        Form   = 'SingleSource'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '聚拢诊断单源（B2/RED-09）：NodeGatherService 内 Diagnostics 自拼 = 0 · 渲染侧由模型字段投影（BuildGatherNote）；正/负对照'
    }

    HostAccessNoRestate = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'SingleSource'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '宿主访问面零重抄（B5/RED-02）：实现类内具名端口成员 = 0（成员为接口默认实现）· Read(HostPort) 恰 1 · 接线单源 = UiHostAccessCircuit 电路配方 · HostPort 端口表 + IHostAccess；正/负对照'
    }

    DataCenterGenericity = @{
        Domain = 'Data'
        Args   = @{ Check = $true }
        Expect = 0
    }

    GraphSingleSource = @{
        Domain = 'Data'
        Args   = @{ Check = $true }
        Expect = 0
    }

    EdgeAsVertex = @{
        Domain = 'Data'
        Args   = @{ Check = $true }
        Expect = 0
    }

    FieldLineageClosure = @{
        Domain = 'Data'
        Args   = @{ Check = $true }
        Expect = 0
    }

    ProductIdempotency = @{
        Domain = 'Data'
        Args   = @{ Check = $true }
        Expect = 0
    }

    DataContracts = @{
        Domain = 'Data'
        Args   = @{ Check = $true }
        Expect = 0
    }

    SpecBoundary = @{
        Domain = 'Spec'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'spec 生成/手写边界（R0 基线冻结）：源码树零生成物 / 清单非空 / 正负对照；基线 Baselines\Spec\SpecBoundary.psd1'
    }

    SpecToolingContract = @{
        Domain = 'Spec'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '规格工具链契约（R2）：SYNOPSIS / 确定性 exit（Debt 棘轮）/ 无深度计数；基线 Baselines\Spec\SpecToolingContract.psd1'
    }

    SpecGeneratedBoundary = @{
        Domain = 'Spec'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'spec 生成/手写分离边界（R4 SEP）：源码树零生成物 / obj 零第二生成物（K2）/ 生成码头注释 / 源⇄产物双向 / manifest 一致性（条件判据）；基线 Baselines\Spec\SpecGeneratedBoundary.psd1；-Warn 为回退开关'
    }

    SpecPortLedger = @{
        Domain = 'Spec'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'spec 端口台账漂移守卫（R3）：用例 given ports 引用键 ⊆ 端口台账（Standards\ports\*.specs 的 port <键>）∧ 内联 chain 端口 impl.productFields ⊇ out.fields ∧ 正/负对照；治簇 A「绑定未登记端口仅运行期可见」'
    }

    DeclaredBundle = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '冻结声明产物守卫（G5）：门禁判据迁移到编译产物结构事实——manifest 三件在位 / schema 哨兵 / 非空正对照 / 组装器零词表字面量（J7）+ 正负对照'
    }

    OperationLayerIsolation = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'N-1 Operation 同层引用隔离（方案 §2.3 G-1）：棘轮基线 18 条只减不增 + 目标对 WorkFlowMethod<->DataStoreDriven 恒 0；层号真源 modules.json roots.layer；D3/D4 正负对照'
    }

    SpecOnly = @{
        Domain = 'Spec'
        Args   = @{ Root = @('AgentTest', 'UsharpTests'); Baseline = 'Tools\Gate\baselines\SpecOnlyGate.baseline.json' }
        Expect = 0
        Note   = '测试载体单一化（AT2001 工具期对拍）：已启用范围（UsharpTests 根级 / Agent.Core.Tests 工程级）内未豁免的 [TestMethod] 即 FAIL；棘轮基线只减不增（新增手写测试 / 向已豁免文件追加）'
    }

    NodeFlowDomainMatch = @{
        Domain = 'Node'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '工作流↔节点域对应守卫（U4）：[Node] uses=/produces= 域 token ∈ Config\nodes\*.ushar 的 [Domain] 集（双向对账）；正/负对照防假绿；基线 Baselines\Node\NodeFlowDomainMatch.psd1'
    }

    DbPageGatherWiring = @{
        Domain = 'Node'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '数据库页读侧统一守卫（R1/R5）：数据库页读路径零存储域（ur_* / SELECT * / CellMaxLength）· NodePanelModel 为唯一读侧 DTO · 入口函数已注册 · 正/负对照'
    }

    BatchStoreSingleSource = @{
        Domain = 'Data'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '存储层批量单源守卫（R0/R5）：批量读/写/删唯一构建点 = StorageFramework\Batch\（IBatchStore）· 存储层批量零领域字面量 · 批写批删无裸连接 · 正/负对照'
    }

    TableWriteDeclaration = @{
        Domain = 'Data'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '表写声明完备性（行写通用化 S5/S7）：每个 [Table] 须含 write: 段（fail-closed）；不可写须附 reason=；行端口注册表单源（TryGetRecord + 四存储键）；正/负对照'
    }

    SqliteTablePolicySingleSource = @{
        Domain = 'Data'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'Sqlite 白名单/封顶单源（B1/DUP-01）：`DefaultLimit=200`/`MaxLimit=1000` 字面量各恰 1 · `IsUserTable` 定义恰 1（SqliteTablePolicy）；正/负对照'
    }

    TableConvergenceSingleEntry = @{
        Domain = 'Data'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '表结构收敛单入口（B1/DUP-08）：MigrateToPure/ResetToPureIfNeeded 降级 internal · 外部调用点 = 0（经 EnsureConverged）；正/负对照'
    }


    AgentRuntimeUIConfigFreshness = @{
        Domain = 'Node'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'AgentRuntimeUI 配置新鲜度（A1-b）：源（Information 资源仓 JsonConfig）→ 产物（FileDriven 宿主 Configs）**结构包含**判据（事件键 + 绑定引用；容忍产物独有派生键 themeSpec/templatePacks）+ JSON 形态可解析 + 正/负对照；治「改了配置但产物滞后 ⇒ 运行不生效」静默'
    }

    ConversationReadSingleSource = @{
        Domain = 'Data'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '会话页读面单一真源守卫：读面零合成（禁兜底造 default 容器／禁合并映射残留 · 已销账 0）·「列历史」实现点 = 1 · 读面 SessionKeys.Default 载体只减不增 · 正/负对照'
    }

    GateRegistry = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '门禁注册表对账（P4）：formula.md [Gate] 声明 ⊇ 已登记运行期实现；登记项 Impl 文件在位且含符号（G1 悬空/G2 漂移/G3 未声明登记 → 失败）；G4 已声明无实现（行为门）仅可见化'
    }


    # ===== 默认即校验的门禁（无 -Check 开关，默认即输出+退出码） =====
    NodeFramework = @{
        Domain = 'Node'
        Args   = @{}
        Expect = 0
        Note   = '节点框架纯净性 A–H 段 + S 段（S-1：Form=Stream ⇒ 身份模板单键）：语言类型不漏进位置层 / 无进程静态可变配置 / 无硬编码位置反解 / 位置层不引上层契约 / 运行态面不得声明为载体（FW-5 Error）/ 声明运行态面须有 run 身份承接点（告警）/ 实例分区零领域词与零实现依赖 / Stream 声明面单键身份（与 NodeSpecValidator spec.stream-identity-multi-row 对偶）；-ShowDetail 显示详情'
    }

    NodeIo = @{
        Domain = 'Node'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '节点文档内核（Share.NodeIo）：语言/实现隔离（零 Usharp/INodeStore/Sqlite/上层 using）/ 零领域 switch / 位置反解单源（仅 NodeScopeResolver）/ 载体原语收口（仅 NodeDocumentPort* 与 NodeStreamSession）/ 负对照必红'
    }

    ToolFaceDeclaration = @{
        Domain = 'Usharp'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '工具面/MCP 装配声明守卫 D1–D5（P0/P2 建面）：Config\tool-face.json 三段齐备 / 执行端与内部面（cline/answer）不外泄 / face.tracks 非空 / 正对照（faces 数 ≥ 基线）+ 负对照（合成 cline.expose=true 必须被同一判据命中）；U6 转强制（Args Check = 校验模式，违规 → 退出码 1）'
    }

    NodeDeclSingleSource = @{
        Domain = 'Node'
        Args   = @{}
        Expect = 0
        Note   = '节点域声明单一来源桥接线守卫 C1–C5（X1/W5 建面）：FrameBinaryRefs.props 登记 Usharp.Adapters.Node（否则语言桥不可见）/ UsharpNodeSpecSource 实现 ISpecSource / 域定义面 JSON 存量缺口报告口径（U1 目标清零）/ 正对照（注册行数 ≥ 基线）+ 负对照（合成缺失注册必须被同一判据命中）；报告模式起步（Args 空 = 不设强制；-Check 转强制）'
    }

    NodeGather = @{
        Domain = 'Node'
        Args   = @{}
        Expect = 0
        Note   = '节点聚拢通用框架 G1–G3：声明闭合（node-gather-*.json 的 kind-shaping.family ⊆ families + 有 families 须留 "*" 默认塑形）/ 零领域（Gather 层零上层 using Operation·Share·Agent·Usharp 与零域键字面量 process·task-node·conversation·segment·message）/ 接线唯一（INodeGatherService 注册点恰 1 = NodeGatherHosting.TryAttachAll；框架层不消费 INodeDomainPort）'
    }

    Wiring = @{
        Domain = 'Node'
        Args   = @{}
        Expect = 0
        Note   = '接线生命周期守卫（Wiring）：声明状态 ⇔ 实现事实（D/F 存在性 + C 读面探针），declared→primary→retired（本项目口径废除 dual-read 运行期双读）；注册表单源 Baselines\Node\Wiring.psd1；正/负对照防假绿；报告模式起步（Args 空 = 不设强制；-Check 转强制）'
    }

    ObservabilitySurface = @{
        Domain = 'Node'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '可见化观测面守卫 C1–C8（F-P0-1c/1d/1i / C-5）：名册在册且已登记 EmbeddedResource / 事件数 ≥ 基线(46) / AgentLog 面关键三键在册（正对照）/ 乱造键不在册（负对照）/ 名册 ∩ 语义台账 = ∅（第二作者）/ 双门面发射面成对（AgentTrace.Visibility + AgentLog 镜像 + ModuleLog 镜像）/ ModuleLog 面（Agent.Modules 10 调用点）关键键在册 / 运行期读面（C8 零落盘修订：连实时出口 agent-observe-<pid> 采样，断言"可连 ∧ 关键键可检索"；Agent 未运行 ⇒ ⊥ 报告态不违规，-VerifyDeploy 转违规，-PipeName/-WindowSec/-Backlog 控窗口）'
    }

    NodeWriteBypass = @{
        Domain = 'Node'
        Args   = @{}
        Expect = 0
        Note   = 'P6-A 委托接线架构不变式：键格式声明化 / 端口准入单源 / 节点写路径单源 / 运行态面结算单源 / I 段 Stream 结算恰 1 次写（B/C/I 段为 Error；I 段防退回逐帧写）；-ShowDetail 显示详情'
    }

    MountSingleWriter = @{
        Domain = 'Node'
        Args   = @{ Check = $true; NegativeControl = $true }
        Expect = 0
        Note   = 'UMF 唯一写入口：history_owns_node 写路径=0 / ScopeFramework 不再产出 node_to_node 归属 / Agent 侧裸写 task→node 归属=0；负对照必红'
    }

    MountDeclaration = @{
        Domain = 'Node'
        Args   = @{ Check = $true; NegativeControl = $true }
        Expect = 0
        Note   = 'UMF 声明对账：每个绑定引用真实 [Relation] / 每个 ensure operation 至少一个绑定 / Stage·Family·Posture 闭集词表；负对照必红'
    }

    MountRead = @{
        Domain = 'Node'
        Args   = @{ Check = $true; NegativeControl = $true }
        Expect = 0
        Note   = 'UMF 唯一读入口：UI/Session 直连图查询点数 ≤ 基线（只减不增）/ 负对照必红；基线 Baselines\Node\MountRead.psd1'
    }

    MountCallSites = @{
        Domain = 'Node'
        Args   = @{ Check = $true; NegativeControl = $true }
        Expect = 0
        Note   = 'UMF 调用点棘轮 + 批量语义（循环内 MountTarget/ApplyAsync 禁）+ 仲裁窗口（MountScope.CorrelationId 必填）；基线 Baselines\Node\MountCallSites.psd1'
    }

    MountFailurePosture = @{
        Domain = 'Node'
        Args   = @{ Check = $true; NegativeControl = $true }
        Expect = 0
        Note   = 'UMF 失败姿态：门面判空走旧写的三元回退只减不增 / 生产禁用 MountBindingCatalog.Default；基线 Baselines\Node\MountFailurePosture.psd1'
    }

    DomainWireSingleSource = @{
        Domain = 'Node'
        Expect = 0
        Note   = '节点域接入聚拢点守卫 D1–D3（R4/R5）：派生面（instances\nodes.json 的 placements[] + placement.ushar 的 LocPlacement/LocToPlacement 行）≡ 声明真源 Config\nodes\*.json 的位置并集（漂移即红；生成分支已拆到 Frame 侧产品生成器 New-NodePlacementDeclaration.ps1，迁入件只做只读对账）；散弹点棘轮（per-domain 槽键常量 / 声明路径常量 / 载体工厂方法，报告模式起步，基线 Baselines\Node\DomainWireSingleSource.psd1）；正对照（声明面 ≥ 基线位置数）+ 负对照（合成 per-domain 形态必被捕获）'
    }
    TextClipSingleSource = @{
        Domain = 'Node'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '文本截断单源（B1/DUP-02）：Modules/Node 截断字面量恰 1（TextClipPolicy）· DeclaredShaper 经 TextClipPolicy 取值 · StorageFramework 截断 ≤1；正/负对照'
    }

    FreshnessFamilySingleSource = @{
        Domain = 'Node'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '产物新鲜度判据族单源（B6/SCT-08）：包含口径/指纹口径实现仅 Baselines\ArtifactFreshness.psm1 · 配置守卫经族函数委托；正/负对照'
    }

    StoreReadPortSingleTable = @{
        Domain = 'Node'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '存储读端口单表（B1/RED-10）：IStoreTableReadPort 无 ListTables · ReadRows(int limit) 为唯一读方法 · ListTables 调用点 = 0 · StoreTableRow.Collection 在位；正/负对照'
    }

    NodeLocPlacement = @{
        Domain = 'Node'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '节点位置反解声明守卫 C1–C5（U12：位置 = loc 反解，不手写声明）：闸A 编译 0 错 / 反解域完备（位置 id ∪ Loc 词表，双向无游离）/ 反解唯一（每位置恰一行）/ 正对照（位置数 ≥ 基线）/ 负对照（U12 §13.2 原样形态必须报 US1003 ∧ US1009）'
    }

    NodeReadSingleSource = @{
        Domain = 'Node'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '节点读面单源：task-node 读轴声明闭合（read-scope/source/edge/normalize/order）/ 端点归一生产调用点 ≤ 基线（唯一实现 NodeKeySetResolver）/ 框架读面零自写剥离 / Modules.Node 零上层依赖 / ReadScope.Keys 与 KeyPrefix 互斥；负对照=删 read-source 必须判红'
    }

    NodeKeyAddress = @{
        Domain = 'Node'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '节点键地址守卫 A1–A7（批次 1）：位置键字段 ⊆ 身份坐标 ∪ 位置 local / local 自洽 / 身份坐标声明齐备 / 同域键模板可区分 / 单写者多落点提示 / 实例位置 id 一致 / 正对照（位置数 ≥ 8 基线）'
    }

    NodeLocConsistency = @{
        Domain = 'Node'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '节点 loc 单源一致性守卫 C1–C4（HV-1 / U12 L-7 / L-6）：loc 字面量只允许出现在定义清单（新增即违规）/ 回退债务清单只减不增（列示现存 4 件）/ ReadScope 构造点须带 Loc（读侧位置来源 = 反解，禁绕过）/ 正对照（单源定义 + 声明面 [Space] Loc + ReadScope 构造点数 ≥ 基线）'
    }

    NodePortPurity = @{
        Domain = 'Node'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '节点端口纯净性守卫 C1–C5（CT-U3 / P20 / §2.2）：位置层可执行引用零 Usharp（csproj 引用行 + 源码去注释后）/ 位置层不引 HierarchyMethod·UniversalReference（K6）/ 引 Modules.Node 的 Usharp 工程恰为白名单 {Usharp.Adapters.Node} / 正对照（桥接工程存在 ∧ 引用 Modules.Node ∧ 实现 ISpecSource）/ C5 读面三态收口（F-4：三态词面 OkButEmpty·Unavailable·IsUnavailable 在位 ∧ 读面适配器消费 IsUnavailable；附负对照）'
    }

    NodeHierarchyRefs = @{
        Domain = 'Node'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '节点层级引用声明守卫 E1–E5（W1：归属边事实只在声明面）：闸A 0 错 / 边类型集合双向对账（声明 ⇄ 唯一字面量源 ReferenceTypes 实测用到集；CrossDbReferenceTypes 已退化为别名门面）/ 退役常量写用法债务只减不增（HistoryOwnsNode）/ [Ref] 字段白名单（禁未注册字段：静默忽略 ⇒ 假声明）/ 正对照 + 负对照（`loc=` 必须被字段判据命中，且闸A 对其 0 错）'
    }

    ReferenceVocabulary = @{
        Domain = 'Node'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '引用关系词汇守卫 A1（C1–C4）：边类型/端点类型字面量只住唯一源 ReferenceTypes.cs（其余常量类只做别名转发）/ 零依赖层豁免（ScopeFramework·DeclarativeDataflow，只减不增）/ 正对照（单源命中 ≥ 基线）/ 负对照（合成字面量必须被同一判据命中）'
    }

    ReferenceExclusion = @{
        Domain = 'Node'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '引用框架禁引守卫 B5（C1–C3）：UR 只引「词表 DSL + 图算法只读服务 + 资源模型」—— WorkflowExecutor / IWorkflowExecutor / IUniversalReferenceManager（Agent 面）/ Reference.Relationship / BranchManagement 在两扫描面 0 命中；正对照（适配器面须命中 IUniversalReferenceManager）+ 负对照（合成禁引必报）'
    }

    NodeCollectPort = @{
        Domain = 'Node'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '节点收集式输入端口守卫 C1–C5（inports=nodes.*）：保留 token 白名单 / 单次性 / 出参唯一 / 正对照（收集端口声明数≥基线且 fn∈投影类白名单）/ 负对照（合成 `inports=node.*` 必须报 C1）；运行时 counterpart = WorkflowFunctionRefValidator 的 C1–C3'
    }

    AgentOrchestrationFlow = @{
        Domain = 'Node'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'Agent 编排归流 E1–E13（声明真源/单步体/静默基线/入参三方对账/调用点与命名空间取值点基线/fn 常量双源对账）'
    }

    AgentFrameworkSingleSource = @{
        Domain = 'Node'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '四个横切关注点框架单源 D1–D11：诊断载体（IDiagnosticsSink）/ 回退姿态（FlowPosture）/ 键寻址（KeyAddressing）/ 簿记流模板（BookkeepingFlowCatalog）/ 取参单源（WorkflowContextParams）/ 簿记流执行器构造点（BookkeepingFlowAssembly）/ 姿态收口台账（D9：RequireFlow 装配点，批次 C 后由 Baselines\Node\AgentFrameworkSingleSource.psd1 的 PostureRequireFlowRequired 翻转生效）/ 读组装组合点（D10）/ 运行期本地流 msgId 单源（D11：统一 DiagnosticsCodes + context）；基线单源 = Baselines\Node\AgentFrameworkSingleSource.psd1（SR-2a 已由遗留子根并入规范根）'
    }

    ClinerulesReferences = @{
        Domain = 'Declarations'
        Args   = @{}
        Expect = 0
        Note   = '默认即校验；-UpdateIndex 为写操作，聚合器禁用'
    }

    BakeFramework = @{
        Domain = 'Build'
        Args   = @{}
        Expect = 0
        Note   = 'CsharpPrj 权威烘焙框架治理 1–6 + 共享边界程序集 7–10（权威副本在册 / 副本外溢=版本偏斜面 / 不源码重编译 / props⇄C# 清单对账）+ 烘焙新鲜度；-Configuration 切换配置，默认 Debug'
    }

    BuildTargetMap = @{
        Domain = 'Build'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '共享映射自洽：Schema / psd1 规则 + props 构建图（目标/插件/profile/分发源存在与引用可解析）/ 工程覆盖 / 正负对照'
    }

    DistributionIncrement = @{
        Domain = 'Build'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '分发单次性守卫：一次 dotnet msbuild / 无嵌套 build·publish / manifest 增量计数 / Unmapped fail-loud / 数据根保留 / 正负对照'
    }

    BuildFrameworkModules = @{
        Domain = 'Build'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '模块归属/依赖方向/单一实现/体量守卫（C1–C8）：文件归属唯一 · .psm1 落点 · M5 禁反向依赖 Entry · M4 禁依赖 M5 · Entry 禁 dotnet publish · 同名函数恰一处 · 正/负对照'
    }
    BinaryContract = @{
        Domain = 'Build'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '二进制契约漂移（BCD）判定：面（探针目录有序集合）+ P1 符号闭包（TypeRef/MemberRef 须在目标程序集元数据解析，含 TypeForwardedTo）+ P2 依赖指纹（旁车 <asm>.contract.json）+ P3 内容级单副本（同名不同内容=偏斜面）+ 正/负对照；基线 = Baselines\Build\BinaryContract.psd1'
    }



    AgentRuntimeUIConfig = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'Consistency'
        Args   = @{}
        Expect = 0
        Note   = '默认校验模式；-FailOnWarning 为严格模式（默认关闭）'
    }

    HtmlViewerVocabulary = @{
        Domain = 'Usharp'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'HTML 模板键 / 类名 / region 锚零领域语义：框架只描述结构（壳/分栏/面板/列表/项/行/分组/折叠/分段/状态），语义只出现在配置键值 + Agent 数据模型 + 域样式注入；扫描面 = 框架资产 + 模板键消费点（HtmlStreamRenderer/ConversationPageRenderer）；正负对照 -NegativeControl；基线 = Baselines\Usharp\HtmlViewerVocabulary.psd1'
    }

    BatchLineEndings = @{
        Domain = 'Process'
        Args   = @{}
        Expect = 0
        Note   = '默认检查模式；-Fix 为写操作，聚合器禁用'
    }

    DeclarativeSurface = @{
        Domain = 'Usharp'
        Args   = @{ Check = $true; NegativeControl = $true }
        Expect = 0
        Tier   = 'fast'
        Note   = '呈现策略声明化：模板键/折叠词/面序/兜底模板/必需键/诊断码/标签只在声明面（Config\ui\*.json）；生产代码策略字面量 = 0；诊断码须在 diagnostics.json 在册；read-paths 事实与 FrameFacts 奇偶；基线 = Baselines\Usharp\DeclarativeSurface.psd1'
    }

    ConversationConversion = @{
        Domain = 'Data'
        Args   = @{}
        Expect = 0
        Note   = '默认检查模式；-ShowDetail 显示详情'
    }

    FsSinkEntry = @{
        Domain = 'Usharp'
        Args   = @{}
        Expect = 0
        Note   = '默认检查模式；-ShowDetail / -StrictDbResidue'
    }

    AgentStorageIsolation = @{
        Domain = 'Agent'
        Family = 'Boundary'
        Form   = 'Boundary'
        Args   = @{}
        Tier   = 'fast'
        Expect = 0
        Note   = 'AgentStorage 零落盘/零 SQLite 门禁（worker 根无 *.db*；strict 模式需 -ProbeReport + sessionServiceCalls>0）'
    }

    GateFileIndex = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '门禁源文件索引棘轮（R1）：递归 Get-ChildItem 只允许在册债务；源文件枚举统一走 Baselines\GateFileIndex.psm1；正/负对照'
    }

    LogMessages = @{
        Domain = 'Declarations'
        Args   = @{ StrictChanged = $true; FailOnDynamic = $true }
        Expect = 0
        Note   = '基线棘轮（检查 3/4/5 计数只减不增）+ 增量红线（-StrictChanged：仅对 git 改动文件的未注册引用判红）；-Strict/-StrictOrphans 仍为全量严格模式；-UpdateBaseline 为写操作，聚合器禁用'
    }

    RuntimeMirror = @{
        Domain = 'Declarations'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '运行期镜像一致性（F-09）：源 *_log_messages.json（FileDriven/Frame.CsharpPrj/Plug.Agent）与 DrivenConfig 树 → Runtime/Config 副本 SHA256 相等；未构建 Runtime ⇒ 不适用 PASS；哈希判据正/负对照'
    }

    SingleWriter = @{
        Domain = 'Data'
        Args   = @{}
        Expect = 0
        Note   = '默认检查模式；H 段 Stream 位置键单键（单行不变量：form=stream ⇒ key 模板无逐事件坐标）；-ShowDetail 显示详情'
    }

    RoundFailureEnvelope = @{
        Domain = 'Agent'
        Family = 'Observability'
        Form   = 'Consistency'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '失败信封与单轮 trace（R1/R3/R7）：R1 契约 / R2 载荷+序列化 / R3 Failure 单写点 / R4 Round_trace 单发点+登记 / R5 Faulted 写入白名单 / P1 正对照 + N1 负对照'
    }

    FactStandard = @{
        Domain = 'Data'
        Args   = @{}
        Expect = 0
        Note   = '会话事实标准 C1–C4 + 声明面闸A；-Strict 为严格模式（C3 旧标识残留判失败），-ShowDetail 显示详情'
    }

    SessionDuplication = @{
        Domain = 'Data'
        Args   = @{}
        Expect = 0
        Note   = '会话逻辑重复消除（台账 D1–D17）：默认基线模式（拦新增重复点，未达标项可见化为[待消除]）；-Final 为终态模式（按 Target 判，批完成后启用）；-ShowDetail 显示逐条实测'
    }

    KeyGrammar = @{
        Domain = 'Parsing'
        Args   = @{}
        Expect = 0
        Note   = '键文法单源守卫 K1–K6：键族字面量单源（白名单）/ 零手写键切片 / 零就地键序 / 零自建比较器 + 正对照（文法定义点数 ≥ 基线、声明绑定用例在位）；基线单源 = Baselines\Parsing\KeyGrammar.psd1（脚本零数值基线）；-Check 为校验模式，-ShowDetail 显示逐条实测'
    }

    GateBaselines = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '门禁基线守卫 U1–U10：内联数值基线清册（未在册即拦新增）/ 外置基线类型（Origin ∈ Derived|Debt|Verdict；Debt/Verdict 必带 DueWhen）/ 正负对照（防判据空转）/ 接线一致性（U7：psd1 ↔ 同名消费脚本）/ 命名唯一性·禁第二基线根（U8）/ 事实行类型强制（U9）/ 声明面路径存在性（U10）；基线单源 = Frame\PowerShellScript\Baselines\'
    }

    GeneratedArtifactsDrift = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '生成物漂移守卫 D1–D3：产物存在 / 哨兵在位（手改即失守）/ 复算等价（跑生成器到 temp 逐字比对）；登记表 = Baselines\_generated-artifacts.psd1（含 ContractIndex 与本仓既有 2 个生成物）；新增生成物不登记 = 无人看守产物'
    }

    SolutionUmbrella = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '伞工程解决方案守卫 S1–S6：伞集合由 modules.json roots 推导（根目录直属 csproj ∧ 该根无独立 sln）/ 叶子不得直挂 MainProj.sln / 禁 Solution Folder 与 NestedProjects / 悬挂与重复登记 / 配置行双向一致；含正负对照（叶子直挂与文件夹必被捕获）'
    }

    ProjectReferences = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '项目引用形态守卫 P1–P3：扫描面 = Frame + Projects 全部 csproj（与映射源 modules.json 解耦）/ 跨子树指向 CsharpPrj 必须 $(Proj_<项目名>) 且悬空 = 0 / 正对照（扫描数 ≥ 基线，防解析面失效）；唯一实现 = Share\Solution\Test-ProjectReferences.ps1'
    }

    TestBuildFramework = @{
        Domain = 'Meta'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '测试构建通用框架守卫 T1–T4：输出拓扑单源（测试工程禁本地 OutputPath）/ 产品面单源（禁 Reference 直连权威目录与 CopyProductClosure）/ 兼容入口薄转发 / 保留策略（published ≤1、无 dmp）；负对照合成 OutputPath 必红'
    }

    # ===== 测试布局对齐（TestLayout · 文件级投影）=====
    TestFileOwned = @{
        Domain = 'TestLayout'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'FA2 测试侧可达性：载体文件（测试特性/命名）必须投影到 ≥1 源码文件（L1 词干 / L2 声明类型 / L3 covers）；新增即红，存量棘轮（_TestLayoutReachability.psd1）；Exempt 车道不参与；不要求 1:1'
    }

    TestSourceReachable = @{
        Domain = 'TestLayout'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'FA3 源码侧可达性：可测源码文件应被 ≥1 测试投影（生成/纯接口/纯枚举自动豁免）；新增即红，存量棘轮；不承诺「每个 .cs 都有 XxxTests.cs」（禁诱导空测试）'
    }

    TestIncrementGuard = @{
        Domain = 'TestLayout'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'TA6 增量守卫：只看 .csproj 增/改名（D 不判）；测试工程须 ProjectMap 命中 ∨ Exempt 车道 ∨ Obligation 豁免，源码工程须有测试投影 ∨ 豁免；git 增量取不到 ⇒ 不可判定即红（fail-closed）'
    }

    TestPathMirror = @{
        Domain = 'TestLayout'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'FA1 路径镜像：Strict/SpecOnly 测试工程须可被 L0 规则定位 ∨ 命名变体 ∨ 辅助工程；集合棘轮（_TestLayoutStructure.psd1 的 PathMirrorUnmapped.Allowed），集合外新增即红、只减不增'
    }

    TestNamingVariant = @{
        Domain = 'TestLayout'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'FA5 命名变体预算：Strict/SpecOnly 测试工程目录叶/工程名须 `.Tests`/`.SpecTests` 结尾 ∨ 命中已登记变体；变体命中数 ≤ Budget（预算缺失即红），未登记形态即红'
    }

    TestLayoutDebt = @{
        Domain = 'TestLayout'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'TD1 债务到期巡检：扫描 reachability/structure 基线事实行 + 账本 Overrides/NamingVariants 的 DueWhen；到期未清即红，无日期 ⇒ attest（不可判定，不伪装为通过）'
    }

    TestSpecsSource = @{
        Domain = 'TestLayout'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'FA6 spec 源有效性：`.specs` 的 `migrate source` 必须指向存在的文件/目录；悬空集合棘轮（_TestLayoutStructure.psd1 的 SpecsMigrateSourceDangling.Allowed），集合外新增即红、只减不增；正负对照常跑'
    }

    TestLayoutOverride = @{
        Domain = 'TestLayout'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'FA4 例外有效性：Kind ∈ {Edge,Waiver}（Edge: From(tst)/To(src) 存在且 Applied=true；Waiver: Scope ∈ 能力集且 Target 存在）；Origin ∈ 能力集，Debt 必带 Owner+DueWhen；Granularity 无判据消费 ⇒ 仅 attest 形状'
    }

    PsCapabilityFileOnly = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '能力库文件化守卫：BasicMethod\Capability 下 .psm1/.ps1 必须同名 .contracts.json / 能力体禁内联执行（Invoke-Expression/scriptblock::Create/iex）/ 契约 function 必须出现在实现文件 / action 须 verb.object / 正负对照'
    }

    FrameworkActivation = @{
        Domain = 'Data'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '框架激活守卫 FA1：`new ReferenceGraphEngine` 装配点数 ≥ 基线（防图引擎退回死代码/空转）；基线 = Baselines\Data\FrameworkActivation.psd1'
    }

    SemanticCentralization = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '语义集中守卫 SC1–SC2：收口类定义点 ≥ 基线 / 域内边类型字面量直接赋值（非豁免）≤ 基线（只减不增）；基线 = Baselines\Meta\SemanticCentralization.psd1'
    }

    UiSingleWrite = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'UI 写落地单写者（U1 白名单外 SetHtmlAsync( 调用 = 0 / U2 白名单正对照 / U3 负对照）；基线 = Baselines\Meta\UiSingleWrite.psd1'
    }

    UiIncremental = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'UI 增量契约（I1 日志 sink 具备 UiWritePlan.Append( + RenderBlock( / I2 禁回退 SetHtmlAsync( / I3 整页 Render 调用面收口 / I4 负对照）；基线 = Baselines\Meta\UiIncremental.psd1'
    }

    UiBudget = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'UI 流原语闭合（B1 契约 / B2 协调器分支 / B3 DomOps 原语 / B4 JS 落地 / B5 负对照）；帧预算阈值随后续批扩项；基线 = Baselines\Meta\UiBudget.psd1'
    }

    SectionWrite = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '写入单位 = 节（S1 基线层入面 / S2 ISurfaceWriter.SubmitAsync + 预算 + 批量脚本 / S3 UseSectionWriter + SectionPlanAdapter / S4 负对照）；基线 = Baselines\Meta\SectionWrite.psd1'
    }

    WriteBudgetNodes = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '预算主维度 = 节点数（B1 基线入面 / B2 选项+策略 / B3 写入口消费 MaxNodesPerBeat / B4 负对照）；基线 = Baselines\Meta\WriteBudgetNodes.psd1'
    }

    BulkThreshold = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '双原语（K1 基线入面 / K2 changeRatio + BulkReplace / K3 JS bulk 原语 / K4 负对照）；基线 = Baselines\Meta\BulkThreshold.psd1'
    }

    UiTelemetryFields = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '节/拍遥测字段（T1 基线入面 / T2 nodes/bytes/jsMs/outcome + batchNodes/lane/queueMs/droppedSections / T3 指标字段 / T4 msgId 注册 / T5 负对照）；基线 = Baselines\Meta\UiTelemetryFields.psd1'
    }

    UiThreadAffinity = @{
        Domain = 'Meta'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'WebView2 线程亲和（U1 续体逃逸 ConfigureAwait(false) = 0 / U2 IWebViewCore 端口守卫 ≥ 基线且带成员名 / U3 sink 先置退避后记诊断且诊断唯一入口 LogSelfDiagnostic / U4 UiThreadMarshaler 定义恰 1 / U5 正负对照 / U6 首帧退避按失败次数 + 固定 1s 字面量 = 0 / U7 失败消息带 threadId / L1-L2 活性自检）；基线 = Baselines\Meta\UiThreadAffinity.psd1'
    }

    RefreshFetch = @{
        Domain = 'Meta'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'UI 刷新取数（R1 消息边界只投递 / R2 按需投影 member_keys / R3 树版本快照 / R4 批量取数 / R5 禁同步等待 / N1 负对照）'
    }


    CircuitSemantics = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'SingleSource'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '电路语义守卫 CS1–CS10：唯一宿主接线 / 禁平行引用抽象定义（负对照=0）/ 引用符号在册 / 轮骨架唯一接线 / 装配域唯一接线（CS5：编排唯一=AssemblyPhaseRunner、配方唯一=框架 DSL、执行外壳唯一，含 RecipeAssembly 正对照）/ CS6 接入面编译入口唯一（负对照=0）/ CS7 接入面禁桩件体（负对照=0）/ CS8 缓存键内容指纹（正对照）/ CS9 编排动作分派配方化（负对照=0：禁 switch(operation.Op) 与漂移兜底；CS9-d 正对照：配方驱动在册）/ CS10 区域子计划编译入
口唯一（负对照=0：CircuitCompiler 之外禁重排配方域+电路域校验；CS10-d 正对照：区域子计划注册表在册）/ CS10-e 区域镜像单一来源（负对照=0：投影/符号定义处之外引用 LoopSymbol） / CS23 递归执行化在册（正对照：RegionNodeBase 执行面 + 能力表 Recursion/BaseCase + 投影 RecursionSymbol 接线） / CS-P1 编译入口唯一（负对照=0：DataCenter/接入面禁自建重排 配方校验→电路校验；正对照=接入面经 CircuitCompiler.TryCompile）/ CS-P2 接入面禁平行执行外壳（负对照=0：new CircuitEngine( / CircuitAssemblyRunner.RunAsync(）/ CS-P3 框架零领域类型（负对照=0：CircuitFramework 内禁 DataCenter./PortStandard/PortImplStandard/PortInstr/PortImplVocabulary；正对照=框架源码在册）'
    }

    RecognizerVocabulary = @{
        Domain = 'Parsing'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '识别词表守卫 R1–R11：声明面（Config\recognizers.json）JSON 合法且结构齐备 / kind 唯一且含兜底类 / 规则 kind ∈ 词表 / match 仅结构谓词 / TS 侧零词表副本（W-2）/ 负对照（防假绿）/ 正对照基线 / R8 C# 镜像对账（到期：缺失即违规）/ R9 live∩snapshot 成节点类别 = ∅ / R10 聚拢声明品类闭合 / R11 双向对拍（镜像字面量集 ≡ 声明面；编译视图须经 RecognizedKinds.All）'
    }

    # ===== 入口框架守卫 =====
    AgentSpec = @{
        Domain = 'Agent'
        Family = 'Declaration'
        Form   = 'Closure'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'agent 识别声明守卫 A1–A8：每个 agent 目录齐备（recognizers.json + round-shape.ushar）/ default 非空且 ∈ kinds / match 仅结构谓词 / 禁过程语义键 / 映射闭合（conversation-canon）/ 正负对照（缺 default、含 step 键必红）'
    }

    RoundShape = @{
        Domain = 'Agent'
        Family = 'Declaration'
        Form   = 'Closure'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '轮次形状守卫 S1–S4（A-2 / P2-4）：样本轮次经同一 TS 执行器重放后须满足 required / terminal=exactly(n) / pairings；负例（missing-*）必红；完整性账 emitted+countedOnly+unrecognized=total'
    }

    CanonicalForm = @{
        Domain = 'Agent'
        Family = 'Contract'
        Form   = 'Closure'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'CanonicalForm 结构守卫 C1–C5（P3-5）：映射单源 conversation-canon（unmapped 末路在位）/ 类型闭合（未映射 type 必拒、禁 Default 冒充）/ 边闭合（无悬空边）/ 属性闭合（受控键）/ 负对照（type=Foo 必红）'
    }

    CanonReplay = @{
        Domain = 'Agent'
        Family = 'Meta'
        Form   = 'Consistency'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '重放对拍守卫 R1–R3（P2-4 / A-3 / A-5）：同一输入两次重放 hits 键集逐项相同（幂等）/ 完整性账平 / 人为删帧后差集非空（防对拍恒绿）'
    }

    AnalysisDocGrounding = @{
        Domain = 'Process'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '分析文档根因对账（G5/R5）：opt-in（marker: grounding:tri-state）文档的「根因」章节内容行须三态标注（事实/建议/待核实）+ 反引号仓库相对路径 token 须在磁盘存在'
    }

    InvokeEntrypoints = @{
        Domain = 'Process'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '入口框架守卫：① 禁数组 splatting 传开关 ② Args 须 hashtable ③ 入口 try/catch/exit ④ 禁硬编码绝对路径'
    }

    # ===== 验证单入口守卫（G8）=====
    VerifySingleShot = @{
        Domain = 'Process'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '验证单入口守卫（G8）：① Frame\Invoke\*.ps1 禁原生 dotnet test/build（薄转发）② 唯一验证命令接线（axioms.md 声明 VERIFY_SINGLE_ENTRY ∧ 指向已存在入口 ∧ project.md §1.10 登记）③ 正/负对照（防判据空转）④ 默认禁全量接线（Baselines\_verify-scope.psd1 FullRunRequired ∧ VerifyScope.psm1 Resolve-FullRunGrant ∧ 两执行体 [VERIFY-SCOPE-GATE] 裁决 + 负对照）；P2 唯一命令纪律；运行面由 §1.10 相位白名单 + [批次声明] 自审承担'
    }

    FallbackInventory = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '框架回退登记守卫 F1–F6：?? new 白名单（组合根 + Null* 工厂）/ catch 吞并 / legacy 棘轮 / 回退可见化 / [Obsolete] dep / 灰发开关登记；基线 = 同目录 fallback-baseline.json（只减不增）；-UpdateBaseline 为写操作，聚合器禁用'
    }

    InitGuards = @{
        Domain = 'Declarations'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '初始化护栏门禁（P1）G1–G4：`throw new ArgumentNullException` / `ArgumentNullException.ThrowIfNull` 只减不增 + `PortGuard.Required` 只增不减 + 正/负对照；基线 = Baselines\Declarations\InitGuards.psd1（单源，禁内联）'
    }

    # ===== 过程层结构治理（修复文档 G/H 批）=====
    RenderNoStringConcat = @{
        Domain = 'Process'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '渲染桥零字符串拼接（N1 HTML 标签字面量 / N2 StringBuilder / N3 拼接式字符串 / N4 string.Concat / N5 领域中文）：渲染只产节点，序列化唯一出口 = FragmentSerializer；负对照 -NegativeControl'
    }

    FragmentBoundary = @{
        Domain = 'Process'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '渲染产物边界（节点树单一形态）：B1 UiNodeHtml.ToHtml 调用点 = 1（FragmentSerializer）/ B2 渲染链气泡段无「造串→解串」（Parse 仅两处登记边界）/ B3 会话模板无 |raw；负对照 -NegativeControl'
    }

    FrameFactCoverage = @{
        Domain = 'Process'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '帧事实覆盖率（P4b 前置）：canon/productions/observability 三写入者的帧字段构造方法须含 terminal 事实键；message-stream/observability 写入站点集合须等于登记集合；负对照缺事实键必红'
    }

    AnswerIdentity = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'SingleSource'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '终答事实可达性（治“有事实、无答案”假绿）：A1 answer-facts 声明闭合 / A2 单写者含 terminal=true 分支 / A3 true 写入恰 1 处 / A4 端点接线 / A5 MarkFact 单点；常量 false 负对照必红'
    }

    FrameIdentity = @{
        Domain = 'Agent'
        Family = 'Declaration'
        Form   = 'SingleSource'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '身份模板口径单值：I1 多行 identity 形状唯一 / I2 声明↔ProcessKeys 双向绑定 / I3 ordinal 口径单值 / I4 占位符登记；段序错/双口径负对照必红'
    }

    CallGovernance = @{
        Domain = 'Process'
        Family = 'CallGovernance'
        Form   = 'Boundary'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '调用治理执行体（治“声明层无执行体”）：G1 策略声明闭合 / G2 原语零运行 / G3 入口接线(exit 3) / G4 归属查表只读 / G5 入参 fail-fast / G6 公理可回溯；缺码·含 dotnet·含写盘负对照必红'
    }

    FrameSchema = @{
        Domain = 'Agent'
        Family = 'Declaration'
        Form   = 'SingleSource'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '帧语义表（C 阶段编译视图）：F1 kind 闭合≡RecognizedKinds / F2 shaping parity / F3 呈现词合法 / F4 anchor=always⇒bubble / F5 ordinal 单值 / F6 禁 answer 列（单源=answer-facts） / F7 subject 预留轴 / F8 受控词；重复·非法呈现·缺 subject 负对照必红'
    }

    AgentSemanticsLeak = @{
        Domain = 'Process'
        Args   = @{ Check = $true; NegativeControl = $true }
        Expect = 0
        Note   = 'Agent 领域语义泄漏（Frame 不得持有 Agent 产品语义）：A1 AgentUi_ 函数名 / A2 Agent 交互命令词 / A3 终答语义旧类型名 / A4 using Agent. = 0；负对照 -NegativeControl'
    }

    BatchScope = @{
        Domain = 'Process'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '批次授权单守卫 B0–B3（过程层 P1/P2/P6）：实际改动集（git status，含子模块）⊆ 活动批 allowSet ∪ derived ∪ preexisting；denySet 面禁改；正/负对照防假绿。授权单路径单源 = Baselines\Process\BatchScope.psd1（Docs\analysis\2026\10\10.5\2026-10-05-过程层缺陷结构治理.batches.json）；授权单缺件 ⇒ 灰度 warn'
    }

    VerifyProportionality = @{
        Domain = 'Process'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '验证成比例守卫 H0–H5（过程层 P4）：产品单工程改动成比例（验证/改动比 ≤ 1.5）∧ 映射落点 ∧ 不静默丢面 ∧ 未分类改动 fail-loud；基线 = Baselines\Process\VerifyProportionality.psd1'
    }

    SpecExempt = @{
        Domain = 'Process'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'spec-exempt 登记与到期闭合（D-06）：每条豁免须 owner=<归属> + expires=<yyyy-MM-dd>；到期未收敛 FAIL；内置正/负对照防判据空转'
    }

    # ===== 运行/验证前置守卫（框架级缺陷：收敛 / 前置开销 / 替身漂移）=====
    RunConvergence = @{
        Domain = 'Process'
        Args   = @{ Check = $true }
        Tier   = 'full'
        Expect = 0
        Note   = '运行收敛守卫 C0–C3（过程层 P6）：-Class 精确类名须收敛为恰 1 单元 / 未命中 fail-fast（禁静默全工程扫描白跑）/ 同工程多目标归并为 1 单元；正负对照防假绿'
    }

    PreflightCost = @{
        Domain = 'Process'
        Args   = @{ Check = $true }
        Tier   = 'full'
        Expect = 0
        Note   = '前置开销守卫 P0–P4（因4）：事实索引单源（*.specs 独立扫描 ≤ 1 ∧ 消费 Get-TestInventory）/ 对齐指纹增量（Get-ContentFingerprint -CachePath 接线）/ bake 新鲜度短路（Test-CsharpBakeFresh）/ 锁分层（Test-BuildGateBusy 探针 + Invoke-Verify 构建相位后释放门）'
    }

    TestDoubleParity = @{
        Domain = 'Process'
        Args   = @{ Check = $true }
        Tier   = 'full'
        Expect = 0
        Note   = '测试替身一致性守卫 D0–D3（因6）：测试侧 `new <Type>{ Member=… }` 引用的成员须 ∈ 产品类型成员面（含 record 主构造）；含继承子句的类型跳过（禁猜测，防假红）；标的 < 基线下限即红'
    }

    # ===== 降维门禁（声明式与工作流即降维）=====
    DimensionReduction = @{
        Domain = 'Process'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Carrier = '源码静态计数（git 索引枚举）：形态转换点数/形态走私点/写原语数/账本写者数/取证文件数'
        Note   = '降维门禁（D1–D6）：C0 登记载体自审（Carrier/Note 不含语义豁免词）/ C1–C4 计数棘轮（只降不升）/ C5 取证下限（ADR-6 降维不得删信息）/ C6 声明面完备（rules 须声明集合级 default、条目禁步骤键）/ C7 正对照（符号清单被掏空即判「判据无标的」）/ C8 负对照 ADR-1..ADR-6 / C9 -Target 列真实目标差距；基线 = Baselines\Process\DimensionReduction.psd1；-WriteBaseline 为写操作，聚合器禁用'
    }

    # ===== 调用面契约门禁（P0 框架：命令声明面）=====
    CommandSpecCoverage = @{
        Domain = 'Process'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Carrier = '源码结构事实：CommandSystemBase 注册动作集（base(entityManager,"x")）vs CommandSpecCatalog 声明集'
        Note   = '命令注册面与声明面一一对应；注册未声明/声明未注册均判红；含正/负对照（声明表掏空即判无标的）'
    }

    LogTemplateBinding = @{
        Domain = 'Process'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Carrier = '源码结构事实：*_log_messages.json 的 msgId/占位符 vs .Log("id", new { ... }) 键集'
        Note   = '日志模板与调用点绑定门禁：未登记 msgId / 漏绑定占位符（字面量 {x} 输出）；基线 Baselines\Process\LogTemplateBinding.psd1（只降不升）'
    }

    # ===== 读模型单源门禁（读模型框架 RM-A）=====
    ReadModelSingleSource = @{
        Domain = 'Process'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Carrier = '生成目录 JSON 结构（op.model ↔ readModels[].model）+ 源码静态计数（IReadModelProjector 实现数 / 视图缓存形态）'
        Note   = '读模型单源 R1–R5：op.model 声明闭合（悬空/死声明判红）/ 视图侧缓存形态棘轮（只降不升）/ 投影实现数=声明数 / 负对照三样本必红 / 版本源词表 + versionProbe 非空；基线 = Baselines\Process\ReadModelSingleSource.psd1；判据口径 = op 级 model=（channel=ReadModel 为命令通道枚举，不参与）'
    }

    # ===== UI 单入口门禁（连接点重构 R7）=====
    UiSingleEntry = @{
        Domain = 'Process'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Carrier = '源码静态计数（Core/Functions + Ui/Views 静态槽直取点）+ 生成目录 handler 解析'
        Note   = 'UI 单入口：C0 登记 / C1-C3 静态槽直取计数棘轮（只降不升）/ C4 目录 handler=LegacyRoute=0 / C5 handler 词表闭合 / C6 负对照 / C7 正对照；基线 = Baselines\Process\UiSingleEntry.psd1'
    }

    # ===== 反模式族守卫（P9 落地）=====
    AntiPatternFamily = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '反模式族守卫（P9）：clinerules\formula.md §8（族=通用规则单表）C1 族唯一/件数 ≥ 基线 · C2 covered ID 形态合法且全局唯一（每条恰属一族）· C3 crit∈{macro,micro} · C4 通用规则列非空 · C5 通用规则符号可解析（回指已注册定义点）· C6 ID 集合守恒（covered ⊇ 基线，只增不减）· C7 正/负对照；基线 = Baselines\Meta\AntiPatternFamily.psd1；-WriteBaseline 为写操作，聚合器禁用'
    }

    # ===== 端口层消除（D3 僵尸脚本）=====
    DualCarrierExclusion = @{
        Domain = 'Data'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '双载体互斥门（D3）：端口块禁「impl delegate ∧ script」并存（僵尸脚本）；D1 新增即红 · D2 只减不增（可销账可见化）· D3 正/负对照（防假绿）；基线 = Baselines\Data\DualCarrierExclusion.psd1'
    }

    # ===== VM 换代路线收敛（VM-R1 构建到新目录）=====
    GenerationLayout = @{
        Domain = 'Agent'
        Family = 'Meta'
        Form   = 'Consistency'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'VM-R1 代目录布局契约：gen-<n>（n ≥ 1）须含全 Agent.*.dll（计数 ≥ 就地产物面参照）+ Agent.Launcher.deps.json（ALC 解析基座=本目录）+ vm-program.json；代根 = Projects\Plug\Agent\BuildOutput\Generations；落点禁 BuildOutput\<Config>（被运行进程锁定）'
    }

    GenerationRebuildChain = @{
        Domain = 'Agent'
        Family = 'Meta'
        Form   = 'Consistency'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'VM-R5 换代链结构守卫：构建到新目录（代目录布局单源）→ 换 ALC（重装配桥 + 按代端口注册）→ 重装配（F 段重挂 + 装配契约）→ 触发（源码监视 + 命令触发）之结构锚点 C1–C7 在位'
    }

    # ===== 生效面断言（B′ · AI 行为护栏）=====
    EffectiveFace = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'Drift'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '生效面断言（EffectiveFace）：运行时装载面指纹 == 构建产物指纹（Agent.Launcher.dll + 共享核心件；dev=就地 / locked=current→gen-<n>）· [E3] 正/负对照；基线 = Baselines\Agent\EffectiveFace.psd1；机械复算 = Frame\Invoke\Invoke-BuildEffective.ps1 -Assert'
    }

    # ===== USharp 纯化门禁（N4：V1–V6-form）=====
    UsharpDocForm = @{
        Domain = 'Usharp'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'USharp 记录实例形态（V1/V2/V3-form + V9 依赖方向）：纯三列模板与表声明 / 零兼容残留（IndexJson·KvJsonTable·KvDocumentTable·ExtractScalar）/ 规范形单源 / 存储层零 Usharp.* 依赖；基线 = Baselines\Usharp\UsharpDocForm.psd1'
    }

    UsharpGetParity = @{
        Domain = 'Usharp'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'usharp_get 集中式转换层（V4/V5/V6-form）：注册单点（UsharpDataPorts.EnsureSqlFunctions）/ 下推表达式同源（UsharpRecordPaths）/ 表达式索引落点 / 单语句 CAS；基线 = Baselines\Usharp\UsharpGetParity.psd1'
    }
    UsharpJoint = @{
        Domain = 'Usharp'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'USharp ↔ C# 交汇面聚拢：消费侧经唯一门面 UsharpDataPorts / 唯一 JSON 出口 UsharpValueJson（全限定散点=0）/ 零值模型内联构造（new Usharp* 棘轮只减不增）/ 门面四语义覆盖；扫描面 = 生产面 Projects\**\src\**（E-4 排除测试面）；基线 = Baselines\Usharp\UsharpJoint.psd1'
    }



    # ===== PrjBridge 解耦（冻结 ABI / 只编自身）=====
    PrjBridgeDecoupling = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'PrjBridge 解耦守卫（INV-PB-1）：桥产物 AssemblyRef 零 Agent.*/GameEditor.*（结构事实=元数据引用表）⇒ Agent/Engine 任意变更不迫使桥重编'
    }

    # ===== Card 通用契约层边界（CB-1..CB-4）=====
    CardBoundary = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'Card 中性契约边界：CB-1 契约零依赖 / CB-2 适配器单边引用（Agent→Contract，Engine→Contract+Scene，禁互引/禁 Frame）/ CB-3 契约公开面无委托与句柄 / CB-4 契约副本唯一（Shared\CsharpPrj）；自带 Good/Bad 自证 + -NegativeControl'
    }

    # ===== 契约框架（ContractFramework：契约身份/注册/对账/执行/取证）=====
    ContractRegistry = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '契约注册表对账（G0 元契约/G1 悬空/G2 漂移/G3 未声明/G4 可见化 + 正负对照）：声明索引单源 = Frame\CsharpPrj\contracts.index.json + 各 contracts.json（结构事实）；与 C# ContractCatalog 同读一份文件'
    }

    # ===== 门禁分类（P0 逻辑模块化：Domain 分类单源）=====
    GateTaxonomy = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '门禁分类守卫 T1–T5（清单 ⇄ 索引 ⇄ 脚本 三方对账）：Domain 合法/落点在位/DSL Check-*.ps1 归属可达（含伴随白名单）/index.generated.md 复算等价/正负对照；分类单源 = GateManifests.psd1 的 Domain 字段；索引 = index.generated.md（Build-GateIndex.ps1 生成）'
    }
    # ===== 框架层设计缺陷彻底优化（B2/B3/B4：读=写同物 / 渲染纯化 / 单谓词 / 单入口）=====
    StoreReadPayloadRecordForm = @{
        Domain = 'Node'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '读端口记录体形态（B2/ERR-06）：载荷 = 记录体 JSON（IDocumentCodec 解码投影），禁列字典序列化；正/负对照'
    }

    GatherFieldMapping = @{
        Domain = 'Node'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '聚拢字段映射单源（B2/SCT-03）：存储行字段键名收敛 StoreRowField，适配器零字面量；正/负对照'
    }

    DomainRosterSingleSource = @{
        Domain = 'Node'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '域族分离（B4/SCT-09）：存储域集合单源 = DbStoreKeys.All + 节点域表显式声明域族分离；正/负对照'
    }

    WriteCapabilitySinglePredicate = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'SingleSource'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '域写能力单谓词（B3/SCT-01）：AgentWriteSources.Capability 定义 = 1，旧三处判定引用 = 0；正/负对照'
    }

    SaveDispatchNoDomainSwitch = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'Boundary'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '记录保存薄分派（B3/SCT-06）：SaveRecordAsync 体内域分支 = 0，域知识下沉 IRowSaveHandler 表；正/负对照'
    }

    PresentationContract = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'Boundary'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '呈现契约（B1）：模板无 role 条件/role=tool；呈现类模板键在位；过程面不引用 bubbleRow；锚点裁决经 PresentationResolver；正/负对照'
    }

    FoldState = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'SingleSource'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '折叠协议（B2）：模板 open 字面量=0；details 全带 data-fold-key；模板出口产元素节点；先绑定后指纹；stream 序列化绑定节点；JS 捕获 fold key；正/负对照'
    }

    DbRenderPurity = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'Boundary'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '渲染纯化（B3/ERR-07+SCT-04）：数据库页渲染路径零写交互态（写入只在命令期）；正/负对照'
    }

    TaskHistorySingleEntry = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'SingleSource'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '归拢写边单入口（DUP-01/ERR-01..03 防回退）：Attach/DetachTaskCommand 构造点唯一于 TaskHistoryCommandPort.cs；归拢写面禁 UserRequested（统一 SystemExplicit）；正/负对照'
    }

    DomainResolveSingleEntry = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'SingleSource'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '域解析单入口（B3/DUP-04）：裸标签别名表定义 = 1（DomainRoute），旧入口在编排类归零；正/负对照'
    }

    RowIdentityBareKey = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'Ratchet'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '行身份裸键回退单处（B3/SCT-10）：RowSelection.Decode 仅允许出现在 DomainRoute（唯一分派点）；正/负对照'
    }

    IntentSupportsProjected = @{
        Domain = 'Agent'
        Family = 'Contract'
        Form   = 'SingleSource'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '能力位注册表投影（B3/DUP-11）：Supports 内逐 intent case = 0，能力位由探针表投影；正/负对照'
    }


    EffectiveModeSingleSource = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'SingleSource'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '生效 mode 单源（G1 呈现裁决聚拢点）：declaredMode ?? snapshot.Mode 求值点 = 1（ConversationStageBodies.EffectiveMode）；呈现裁决单入口 = SessionPresentation（OperationModeCatalog.IsOperation 越点消费 = 0）；呈现侧内联回退 declaredMode = 0；正/负对照'
    }

    DetailReadWriteSamePort = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'SingleSource'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '详情读=写同端口（G3 行身份聚拢点）：LoadNodeDetailAsync 体内读经 AgentWriteSources.TryGetRecord（全域行端口），域单点查询 TryGetNodeAsync = 0；正/负对照'
    }

    RenderNoDomainBranch = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'Boundary'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '详情呈现零域分支（G4 呈现投影聚拢点）：RenderNodeDetail 消费 state.DetailNote、零硬编码节注、编辑器受可编辑门约束（禁假可用）；正/负对照'
    }

    OperationRowCarriesActor = @{
        Domain = 'Agent'
        Family = 'Observability'
        Form   = 'Consistency'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '操作行承载执行体（G5 操作流承载模型 + F3 分发目标集）：RenderOperations 行模型含 subagent/targets 键 ∧ 框架 actionPanel 模板含 {subagent}/{?targets}{targets} ∧ 零 Role 兜底（承载单源 ToOperationRow）；正/负对照'
    }

    # ===== 呈现链聚拢点 · 剩余未完成项 通用框架化（F2–F6）=====
    KeySpaceCoverage = @{
        Domain = 'KeySpace'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '键空间框架（Modules.KeySpace）：KS1 族命中 / KS1b 端口契约全集覆盖（集合闭合 = GateClosure；输出 coverage=）/ KS2 三态禁坍缩 / KS4 词法唯一 / KS5 域词隔离 / KS6 诊断码登记 / KS7 族注册唯一 / KS8 负对照'
    }

    WorkFlowMethodKeySemantics = @{
        Domain = 'KeySpace'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '层 4 键语义：键目录零自建 Regex/Split、Output 零 Values.First 兜底、管线零同步等待；负对照'
    }

    DispatchTraceSchema = @{
        Domain = 'KeySpace'
        Family = 'Observability'
        Form   = 'Closure'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '分发目标闭合（DTC）：DispatchTraceSchema 六阶段封闭 ∧ 分发/休眠遥测 msgId 已登记（未登记即静默 ⇒ 红）'
    }

    IdentityTemplatePrefix = @{
        Domain = 'Agent'
        Family = 'Declaration'
        Form   = 'Closure'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '身份模板声明（DTC-6）：identityTemplate 引用可解析 ∧ 花括号配对 ∧ 前缀可判定（单占位或字面量起始）'
    }

    SendDeclarationCoverage = @{
        Domain = 'Agent'
        Family = 'Declaration'
        Form   = 'Closure'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '发送面声明覆盖（F2）：发送入口零裸 null,null（经 SessionSendDeclaration 显式派生）∧ Agent.Ui 内 "dispatch" 字面量 = 0；正/负对照'
    }

    ReadinessSingleEntry = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'SingleSource'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '就绪/诊断单点（F4）：ReadinessFace.Of 定义 = 1 ∧ 旧 RenderReadinessHtml = 0 ∧ 恒假常量 IncludeGatherDiagnostics = 0（声明驱动 GatherDiagnosticsSwitch）；正/负对照'
    }

    UnknownVisible = @{
        Domain = 'Agent'
        Family = 'Observability'
        Form   = 'Boundary'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '未知可见化守卫（禁静默出口 U）：unknown_tokens.json 载体在册（unknown/dash tokens + EmbeddedResource + UnknownTokens 门面）∧ G2 身份维度判空隐藏（M1 后已归零）∧ G3 正/负对照；权威 = Docs\analysis\2026\10\10.17 设计单'
    }

    RecordDraftSingleSource = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'SingleSource'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '记录草稿单源（F5）：旧同值字段 DetailSnapshot 引用 = 0 ∧ 基线字段 DetailBaselineJson 在位（装载写一次/保存后更新/恢复读基线）；正/负对照'
    }

    TemplateAccessSingleEntry = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'SingleSource'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '模板访问单入口（F6）：除 ConversationTemplates 自身外直取 HtmlUiHost.Templates = 0 ∧ ConversationTemplates.Resolve 在位；正/负对照'
    }

    IntentParity = @{
        Domain = 'Agent'
        Family = 'Contract'
        Form   = 'Consistency'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'IEF 写读同源对账：history.link 写面 = graph:history_owns_task（读面绑定同一写面）∧ 后端调用注入写原语 ∧ 禁 no-op 成功 ∧ 探针单源(backend.Supports) ∧ 注册表键集=已实现 handler 集 ∧ 正/负对照'
    }

    ActionCatalogSingleSource = @{
        Domain = 'Process'
        Family = 'Contract'
        Form   = 'SingleSource'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '动作目录单源：运行期 CommandSystemCoordinator.RegisteredActions → 加载期 CommandLineValidator.SetRegisteredCommands 投影（FileDrivenPipelineRunner 恒注入）∧ wait-until 能力在位 ∧ 正/负对照'
    }

    PowerShellHostSingleSource = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'SingleSource'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'PowerShell 宿主单源（B10）：Plate\PowerShellHost 唯一实现（物化解析/探测/一次性脚本/长驻会话/通用配置）∧ 0 字节 alias 必须物化为真实目标 ∧ Agent/FileDriven 只调用（RunFileAsync/Locate）∧ 非单源面无 PS ProcessStartInfo ∧ "-File" 字面量仅限 Runner/Session/Probe ∧ reparse/WindowsApps 语义仅限解析器 ∧ 能力失败保留稳定码 ∧ PsRuntime-Host 必须经 plain payload 且 ConvertTo-Json 深度 ≤8（A6–A12）'
    }

    IntentContract = @{
        Domain = 'Agent'
        Family = 'Contract'
        Form   = 'Closure'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'IEF+ 执行契约守卫 C1–C6：每行 ResultKind / 写入行 ReadBack / Writes⊆Reads / PreconditionKind 有实现 / parity CaseId 不悬空 / 正负对照；基线 = Baselines\Agent\IntentContract.psd1'
    }

    EchoConstants = @{
        Domain = 'Agent'
        Family = 'Observability'
        Form   = 'SingleSource'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '回显投影单源：写面回显禁叙事常量（Task.FromResult("已…")）∧ 必须经 OutcomeEchoProjector 输出 decision/wrote ∧ 正对照保留会话渲染映射'
    }

    ScriptNoFixedSleep = @{
        Domain = 'Meta'
        Family = 'Meta'
        Form   = 'Ratchet'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '脚本禁固定等待：action=sleep 且 target ≥ 1000ms = 0（失败须落在强判据，禁「未跑完」）∧ wait-until 有界轮询能力在位（WaitUntilSystem 注册）∧ 正/负对照'
    }

    SignalDeclaration = @{
        Domain = 'Agent'
        Family = 'Declaration'
        Form   = 'Closure'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '信号声明面（S1–S6）：状态词表↔枚举 / 生命周期可达终态且终态无出边 / 三张判定表总函数形态 / 六段流闭合 / Obs required+enum / fn 常量登记'
    }

    SignalTraceSchema = @{
        Domain = 'Agent'
        Family = 'Observability'
        Form   = 'Closure'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '信号 Trace（T1–T4）：六相位闭集 / Signal 运行期日志键全登记 / 关键可见化键在册 / 六段 fn 描述符正对照'
    }

    ContextGeometryConvergence = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'SingleSource'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '上下文几何聚拢点（K0.5/C1–C6）：白名单外零内层端口解析 / 裁剪单入口 / 命中口径单源 / Host 方法≤12 行≤400 / 正负对照'
    }

    LaneCatalog = @{
        Domain = 'Agent'
        Family = 'Declaration'
        Form   = 'Closure'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '车道目录（L1–L6）：声明非空 / executor 必填 / fallback 可达 / workflow 存在且拒绝运行期 plan / 降级车道须 allow_tools 且不被意图命中 / PS 域 workflow+plan=false / 正负对照'
    }

    ExecutionLaneGate = @{
        Domain = 'Agent'
        Family = 'Contract'
        Form   = 'Closure'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '执行车道门（N1–N4）：workflow 与单步 llm 互斥 / 声明流拒绝运行期 plan / default 车道 deny run_commands / 负对照'
    }

    PromptSegments = @{
        Domain = 'Agent'
        Family = 'Declaration'
        Form   = 'SingleSource'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '提示词段装配（P1–P4）：会话链不以短常量作 L0 / 种子 System 与 L0 段集同源 / 执行体步 system 可达 / 负对照'
    }

    DelegateEntry = @{
        Domain = 'Agent'
        Family = 'Contract'
        Form   = 'Closure'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '委派入口（D1–D5）：tool-face 声明轨/面→轨/模型暴露 / TrackFactories 恰 1 处 / ToolCatalogCore 可见 / ITaskAwaitPort 同步等待 / 正负对照'
    }

    WorkflowRegistration = @{
        Domain = 'Agent'
        Family = 'Declaration'
        Form   = 'Closure'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '工作流登记（W1–W6）：WorkflowSpecFiles ≡ DeclaredFlowIds.Map ≡ WorkflowRegistrationCatalog.All / 流号一致 / 声明文件与 [Flow] 在位 / 未登记声明面须显式豁免 / 正负对照'
    }

    # ===== 执行体观察面（Actor Observation：K-2/K-3/K-4）=====
    ActorBindingSingleWriter = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'SingleSource'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '执行体身份单写（K-2）：AgentStatus.TryAdd 仅允许 ActorBinding/SnapshotMergePolicy/JsonConverter 三处（身份/合并回填/反序列化）· 裸改写=0 · ActorIdentity.Resolve 定义恰 1 · ReadActorId 读点带 preferred；正/负对照'
    }

    ActorObservationSurface = @{
        Domain = 'Agent'
        Family = 'Observability'
        Form   = 'Consistency'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '执行体观测面（P5′-P7）：合同/调用投影/合并/Hub/视图投影/调用台账/结果账/事实源在位 · UI 视图层零直读 · 单一入口 · 全局 scope · 层级适用性（ActorApplicability.For）被消费 · 身份列只取 Id · 执行体面零控制 · 声明不产行顺序判据（S7b）· 提示词快照读取单点（S11）· 阶段词表 + Normalize 同义词映射对拍（S12）；正/负对照经真实分类函数'
    }

    ActorObservationEmptyReason = @{
        Domain = 'Agent'
        Family = 'Observability'
        Form   = 'Boundary'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '执行体观测空态/结果可见（NoSilentOutlet）：EmptyText/NotAttachedText · 声明/任务/全局 scope/无调用归因键（单一常量源 ActorResultReasons）· 未登记调用结构码 · 基础提示词折叠区（PromptSurface/actor-base-prompt）· 能力目录已退役（无 actor-dir/ActorDirectory）· 结果通道（Node/Ktf/None+原因）唯一写点在位 · 结果账非法输入拒绝不抛（E9）'
    }

    ActorInvocationSingleSource = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'SingleSource'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '调用登记单源（P5′/P8′）：ActorInvocationLedger.Record 生产写点恰 1（AgentTaskPool 活入队单点）· ResultObservationLedger.Record 写点恰 1（SubAgentResultPublisher）· 行必含 InvocationId · 同 TaskKey 幂等（TryAdd）；正/负对照经真实扫描函数'
    }

    JudgmentPoints = @{
        Domain = 'Process'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '判定点单源（K1–K9）：Judgment/ 内核在位 · Rules 文件 >= 5 · MissingPorts 定义点 = 1（薄转发）· RoundFailureException 抛点 = 2（执行失败 + Apply）；负对照防恒真'
    }

    JudgmentParity = @{
        Domain = 'Process'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '判定口径对拍（K2/K4/K6）：RoundContractJudge 转发 JudgmentEngine.Judge 且无 Except 判定体 · PromptPreflight 接 AliasCoverageRule 且无本地 IsNullOrWhiteSpace · 对拍测试在位；负对照防恒真'
    }

    DeclarationCapability = @{
        Domain = 'Process'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '声明能力闭合（K5/K7）：nullable=/conditional= 端口必须 ⊆ outports；负对照：未知能力端口必须判违规'
    }

    TextKernelPurity = @{
        Domain = 'Process'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '通用文本代数纯度：零领域常量 · 裸判空仅语义承担位 · TextState/Apply/Compose/From 四单源；负对照防恒真'
    }

    StorageWriteEntries = @{
        Domain = 'Data'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '双库写入入口收敛：图库 owner 创建点 = 1 · 连接工厂/直构节点库/载荷写 ≤ 基线（结构事实：调用点计数）'
    }

    ReadIdentitySingleSource = @{
        Domain = 'Data'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '行身份单源：存储键→表映射 = 1 · 旧标签表 = 1 · 域注册面 ≤ 基线 · 节点域单点读 ≤ 基线'
    }

    RenderLayerStoreFree = @{
        Domain = 'Data'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '渲染层只接收模型：Agent.Ui\Ui\Views\** 内存储端口引用 = 0'
    }

    GraphPathSingleSource = @{
        Domain = 'Data'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '图库路径单源：路径解析/文件名常量 = 1 · 启动指纹发射点 = 1 · 两库同目录断言 ≥ 1'
    }

    GraphDeclarationParity = @{
        Domain = 'Data'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '图边声明完整性：必需边五字段齐备（identity/tombstone/unique/revive/cascade）+ 受控词 + 规则动作受控；负对照删字段/越界动作必红；基线 Baselines\Data\GraphDeclarationParity.psd1'
    }

    RelationGraphParity = @{
        Domain = 'Data'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '关系图三方对账：ensure 关系的 edge 已声明 + 每条 ensure 有 [Mount] 绑定 + Mount 反向不悬空 + stage/family/posture/order 受控 + Derive via 边已声明；负对照删 [Mount]/换未声明边必红；基线 Baselines\Data\RelationGraphParity.psd1'
    }

    GraphTombstoneRead = @{
        Domain = 'Data'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '图边墓碑读取点 ≥1（删除有约束力）：写前 Admit 调用点 ≥1 + EdgeTombstoneExists 读取点 ≥1 + 墓碑键构造/读取同源；负对照移除读取点必红'
    }

    GraphKeySingleSource = @{
        Domain = 'Data'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '图边键单源：手拼 "::" = 0（唯一实现 = DeclaredGraphKeyGrammar）+ 文法实现点 = 1；负对照合成拼键必红；基线 Baselines\Data\GraphKeySingleSource.psd1'
    }

    GraphIndexDeclaration = @{
        Domain = 'Data'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '图库索引声明（棘轮）：EnsureIndex 计数 ≤ 基线（只降不升）+ 边身份唯一索引在位；负对照新增索引必红；基线 Baselines\Data\GraphIndexDeclaration.psd1'
    }

    DataStoreDomainBoundary = @{
        Domain = 'Data'
        Args   = @{}
        Tier   = 'fast'
        Expect = 0
        Note   = 'DataStoreDriven 领域边界（控制反转）：P1 核心零域 import / P2 核心零域工程引用 / P3 禁反向引用 Domains / P4 核心零领域字面量 / P5 宿主零具体介质 using / P6 正对照 / P7 同层引用棘轮 ≤19 且 WorkFlowMethod↔DataStoreDriven=0、HierarchyMethod/LoopFoldMethod→Domains=0；默认报告、-Check 拒绝'
    }


    SelectionContract = @{
        Domain = 'Data'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '选择态契约（作用域×槽）：C1 切换器声明 view 槽 / C2 锚回退链唯一实现 / C3 领域消费点=2（会话页+DB页，证明通用）/ C4 导航禁归一化占位键 / C5 负对照'
    }


    UiWriteFidelity = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'Consistency'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '写面保真（F-1/F-8）：G1 片段根 Bulk 透传子节点 / G2 解析实体解码 / G3 页面属性同步 / G4 往返用例 / G5 负对照'
    }

    UiTemplateSet = @{
        Domain = 'Agent'
        Family = 'Presentation'
        Form   = 'Closure'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '模板集完整性（F-3/I-2）：T1 必需键齐备 / T2 覆盖键闭合 / T3 装配期合并结构 / T4 解析入口防御合并 / T5 负对照'
    }

    KeySurfaceSingleSource = @{
        Domain = 'KeySpace'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'GP-1 键面单源（棘轮）：候选行键表 0 / 行键解析实现 1（键面投影 + 声明 readOnly）/ 前缀拼串 0'
    }

    EntityExistenceSingleSource = @{
        Domain = 'Process'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'GP-2 实体存在性单源：直读探针 0 / 实现 2（未装配桩 + 注册表）/ 命令层重复前置检查 0 / 未映射码 1'
    }

    ContainerSemantics = @{
        Domain = 'Process'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '容器族语义：Operation 内 *History* 类型 0（Frame 全量账归 HistorySemantics H1，分工禁双写者）/ Frame using Agent 0 / History 命名空间 0 / 底座引用不下降（135/106）/ 载体表名与记录类型棘轮'
    }

    HistorySemantics = @{
        Domain = 'Process'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'history 语义收拢（H6/RF8）：扫描面 = Frame\CsharpPrj；H1a Operation / H1b Share / H1c 其余层 *History* 类型声明 0（正则含 struct，防 record struct 逃逸）/ H2 History 命名空间 0 / H3 Frame using Agent 0 / H4 Frame Agent* 类型 0 / H5a-d 提及文件分区棘轮 / NC 正负对照（含 Share 扩面与排除规则）/ -List 逐条列命中'
    }

    # ===== 聚拢框架（Gather-as-Pipeline，G0–G2）=====
    GatherSpec = @{
        Domain = 'Agent'
        Family = 'Declaration'
        Form   = 'Closure'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '聚拢声明守卫 S1–S7：Config\gather\*.json 合格性（schema / stages / needs 闭合 / deps 无环 / produces crit 与 macro recompute）/ 禁过程语义键（step/order/sequence/then/next）/ 正负对照（缺 need、成环、step 键三类样本必红）'
    }

    GatherPoints = @{
        Domain = 'Agent'
        Family = 'Declaration'
        Form   = 'Closure'
        Args   = @{ Check = $true; ShowDetail = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '聚拢点装配守卫 GP1–GP7：PointId 唯一/视图已声明/IGatherModule 全覆盖/单表外禁直接 new/Owner/门控已登记/负对照'
    }

    GatherClosure = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'Closure'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '聚拢闭合守卫 C1–C5：读/写直呼清单（file:line + 符号）不得超基线（Baselines\Process\GatherClosure.psd1，只降不升）/ 允许集 = producer 实现文件 + 契约定义面 / C5 出口闭合（Emit 字面量产物名 ∈ 声明面 produces，常量引用放行）/ 负对照（合成直呼、未声明产物名必被捕获）'
    }

    GatherDecl = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'Boundary'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '聚拢声明约束 G1–G4：Modules\Gather 公开类型 ⊆ 白名单（新增公开面须登记）/ IGatherModule 实现只允许 *GatherModule*.cs / new GatherHub 只允许 Modules\Gather 与 UiViewProjection.cs / 负对照（未登记公开类型、非法实现点必红）'
    }


    # ===== 任务池并发语义 / 成本门禁复用（S4）=====
    TaskConcurrencySemantics = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'Closure'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'C1 组 API 定义唯一 / C2 无跨组聚合 / C3 嵌套组校验 / C4 容量门先于 Running / C5 L0 三端口唯一 / C6 领域零内联并发 / C7 负对照'
    }

    ConcurrencyLayering = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'Closure'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'B5 分层守卫：L1 通用并发内核零 Frame 引用 / L4 任务编排零 Agent 引用 / Agent 侧不得复活已迁移类型 / 负对照'
    }

    CostGateReuse = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'Closure'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'D1 pricing.json 解析仅 CostConfigLoader / D2 成本比较唯一 / D3 消费 IPriceTable / D4 声明策略单源 / D5 复用 ICostStore·ICostQuery / D6 负对照'
    }

    # ===== 并发策略声明单源（spec → 生成物 → 配置值面）=====
    ConcurrencyOptionsSingleSource = @{
        Domain  = 'Agent'
        Family  = 'Declaration'
        Form    = 'SingleSource'
        Args    = @{ Check = $true; NegativeControl = $true }
        Tier    = 'fast'
        Expect  = 0
        Carrier = '声明键集/生成属性集/schema 属性集/配置根键集 四面对拍 + 源码字面量计数 + 消费方符号存在性'
        Note    = 'C1 spec ⇄ agent-config.ushar 键集；C2 spec ⇄ Generated\ConcurrencyOptions.g.cs ⇄ agent-config.schema；C3 权重/成员上限/静默钳制零回填；C4 UnitTokensPerNode 单声明且 Copy()=0；C5 声明键消费方可检索；负对照 C1/C3/C4/C5 必红'
    }

    ContractSingleSource = @{
        Domain = 'Agent'
        Family = 'Runtime'
        Form   = 'Closure'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'C1 摘要直调=0（阻断）/ C2 嵌入资源直调=0（阻断）/ C3 TryGetProperty 直调≤37（棘轮只降不升）/ C4 单键域拼接=0（阻断）/ C5 诊断归因单表（无 ||）/ C6 负对照'
    }

    # ===== 启动面：装配闭包 / 布局 / 构建闭包 / 冒烟 / 入口（框架级，2026-11-05）=====
    AssemblyClosure = @{
        Domain  = 'Meta'
        Family  = 'Closure'
        Form    = 'Closure'
        Args    = @{ Check = $true }
        Tier    = 'fast'
        Expect  = 0
        Carrier = 'assembly-closure.json ∪ deps.json ∪ 文件系统（A1 根/清单、A2 资产闭包、A3 归属唯一、A4 正对照）'
        Note    = '装载组三段闭包；缺件即红并列出资产/清单；实现单源 Share\AssemblyClosure\AssemblyClosure.psm1'
    }

    BuildLayout = @{
        Domain  = 'Meta'
        Family  = 'Build'
        Form    = 'Closure'
        Args    = @{ Check = $true }
        Tier    = 'fast'
        Expect  = 0
        Carrier = 'props 文本结构（<OutputPath> 含 $(Configuration) / 生成件 Import）+ 文件系统（扁平权威层=0）'
        Note    = 'L1 配置层必备 / L2 自包含 props 必须 Import 生成件 / L3 扁平层允许 0 / L4 正对照'
    }

    BuildClosure = @{
        Domain  = 'Meta'
        Family  = 'Build'
        Form    = 'Closure'
        Args    = @{ Check = $true }
        Tier    = 'fast'
        Expect  = 0
        Carrier = 'BuildTargetMap.props 的 BuildProfiles.launch ∪ assembly-closure.json ∪ 脚本文本结构'
        Note    = 'K1 launch ⊇ required 装配组 buildTarget / K2 禁内联产品清单 / K3 清单就绪 / K4 正对照'
    }

    LaunchSmoke = @{
        Domain  = 'Meta'
        Family  = 'Launch'
        Form    = 'Closure'
        Args    = @{ Check = $true }
        Tier    = 'fast'
        Expect  = 0
        Carrier = '进程生命周期（超时/退出）+ %TEMP% 诊断文件集合（StartupFailure / FunctionLibFallback）'
        Note    = 'macro 判据「能启动」：只判 bootstrap 层，不判引擎业务失败；缺失启动器 ⇒ fail-fast'
    }

    LaunchEntrypoint = @{
        Domain  = 'Meta'
        Family  = 'Launch'
        Form    = 'SingleSource'
        Args    = @{ Check = $true }
        Tier    = 'fast'
        Expect  = 0
        Carrier = 'EnableMent\*.bat 文本结构（去注释有效行数 + 禁用 token 集）'
        Note    = 'K1 入口零逻辑：有效行 ≤ 基线、禁 if/set/pause/chcp/call/for、须引用唯一入口并显式声明 -Product'
    }

    LaunchContract = @{
        Domain  = 'Meta'
        Family  = 'Launch'
        Form    = 'SingleSource'
        Args    = @{ Check = $true }
        Tier    = 'fast'
        Expect  = 0
        Carrier = 'Launchers\*.launch.psd1 表项 + EnableMent\*.bat 集合（双向覆盖）'
        Note    = 'K2 契约完备：每产品恰一份契约、字段完备、Entries 与入口双向映射、禁装载面路径字面'
    }

    LaunchExitCodes = @{
        Domain  = 'Meta'
        Family  = 'Launch'
        Form    = 'Consistency'
        Args    = @{ Check = $true }
        Tier    = 'fast'
        Expect  = 0
        Carrier = '契约 ExitCodes 表 × 执行体常量退出点（源码静态计数）'
        Note    = 'K3 退出码可证：禁常量 exit 0；常量码 ⊆ 契约码；契约非零码 ⊆ 常量码；须有 WaitForExit + 进程退出码返回'
    }

    LaunchTrace = @{
        Domain  = 'Meta'
        Family  = 'Launch'
        Form    = 'Consistency'
        Args    = @{ Check = $true }
        Tier    = 'fast'
        Expect  = 0
        Carrier = '契约 Trace 表 + 实跑 --diagnose 新增日志内容标记'
        Note    = 'K4 失败可复算：Trace.Dir/Tee/KeepDays 在册，诊断实跑必落日志且含契约哈希/阶段/判定证据'
    }

    DeclarationLayout = @{
        Domain = 'Meta'
        Args   = @{ Check = $true; NegativeControl = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '声明面布局守卫 D1–D7：role/dirs 存在 / bundle 角色已声明 / 磁盘 .ushar 全覆盖 / role-basename 逻辑名唯一 / 命名策略（model·prompt·policy·toolface=role-basename，agents=full-path）/ JSON ⇔ BuildDefault 三方等价 / 正负对照'
    }

    ConfigPaths = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '配置路径防回潮 P1–P4：C# 消费代码禁 @"Config\..." 与 Path.Combine("Config",...)；Agent 模块 PS 禁 Join-Path Config 硬编码；债务棘轮只降不升（Baselines\Meta\ConfigPaths.psd1）；正负对照'
    }

    # ── Constraint 域：约束墙（Wall）侧 CF-W-*（Docs\analysis\2026\11\11.6\constraint-framework）──
    'CF-W-01-DeclarationNoImplRef' = @{
        Domain  = 'Constraint'
        Args    = @{ Check = $true }
        Expect  = 0
        Carrier = 'D 声明面 *.cs 的 using/限定名（去注释）'
        Note    = 'CW-01 声明包不依赖实现：D 不得 using 实现命名空间；基线棘轮 Baselines\Constraint\CF-W-01-DeclarationNoImplRef.psd1；正负对照'
    }

    'CF-W-02-DeclarationSurfaceClosed' = @{
        Domain  = 'Constraint'
        Args    = @{ Check = $true }
        Expect  = 0
        Carrier = 'D 声明面 *.cs 的显式禁型 token（去注释）'
        Note    = 'CW-02 声明面禁型闭合（显式子集：平台/实现/object/Delegate/dynamic）；基线棘轮 Baselines\Constraint\CF-W-02-DeclarationSurfaceClosed.psd1；完整闭合标 attest；正负对照'
    }

    'CF-W-03-ImplNoMutableConfigRef' = @{
        Domain  = 'Constraint'
        Args    = @{ Check = $true }
        Expect  = 0
        Carrier = '核心目录 *.cs 的显式配置类型声明（去注释）'
        Note    = 'CW-03 核心目录零可变配置引用：字段/参数/局部/属性显式类型命中；基线棘轮 Baselines\Constraint\CF-W-03-ImplNoMutableConfigRef.psd1；正负对照'
    }

    'CF-W-04-NoConfigMutation' = @{
        Domain  = 'Constraint'
        Args    = @{ Check = $true }
        Expect  = 0
        Carrier = '实现侧 *.cs 的 `<配置接收段>.<成员> <赋值类 op>`（去注释；配置段子集）'
        Note    = 'CW-04 实现侧零配置写（可静态确认的接收段子集）；binding.Set/Merge/反射回填仍 attest；基线棘轮 Baselines\Constraint\CF-W-04-NoConfigMutation.psd1；正负对照'
    }

    'CF-W-05-NoConfigConstruction' = @{
        Domain  = 'Constraint'
        Args    = @{ Check = $true }
        Expect  = 0
        Carrier = '实现侧 *.cs 的 new/Deserialize<MutableConfigType>（去注释）'
        Note    = 'CW-05 实现侧零配置构造：显式 new/反序列化命中；基线棘轮 Baselines\Constraint\CF-W-05-NoConfigConstruction.psd1；目标态归零；正负对照'
    }

    'CF-W-07-PlatformTypeQuarantine' = @{
        Domain  = 'Constraint'
        Args    = @{ Check = $true }
        Expect  = 0
        Carrier = '实现侧 *.cs 的路径白名单 × 平台类型 token'
        Note    = 'CW-07 平台类型隔离：WinForms/HtmlHosting/Viewer 边界外禁平台类型；基线棘轮 Baselines\Constraint\CF-W-07-PlatformTypeQuarantine.psd1；正负对照'
    }

    'CF-W-08-NoAmbientLookup' = @{
        Domain  = 'Constraint'
        Args    = @{ Check = $true }
        Expect  = 0
        Carrier = '实现侧 *.cs 的 AppServiceProvider.Resolve / NodePortContractRegistry 明确调用点'
        Note    = 'CW-08 零环境查找（明确调用点面）：.Shared/.Instance 泛化面标 attest；基线棘轮 Baselines\Constraint\CF-W-08-NoAmbientLookup.psd1；正负对照'
    }

    'CF-W-09-NoStaticMutableState' = @{
        Domain  = 'Constraint'
        Args    = @{ Check = $true }
        Expect  = 0
        Carrier = '实现侧 *.cs 的 static 字段/自动属性声明头（去注释）'
        Note    = 'CW-09 零静态可变状态：static 非 readonly/const 字段与可变静态属性；基线棘轮 Baselines\Constraint\CF-W-09-NoStaticMutableState.psd1；正负对照'
    }

    'CF-W-10-NoZombieConfigField' = @{
        Domain  = 'Constraint'
        Args    = @{ Check = $true }
        Expect  = 0
        Carrier = '声明面配置字段 × 实现侧读索引（ConstraintFieldIndex，去注释；唯一名可判子集）'
        Note    = 'CW-10 配置字段无僵尸：可判子集（唯一名）零读取字段进 ZooBaseline；同名冲突/运行期消费仍 attest；基线棘轮 Baselines\Constraint\CF-W-10-NoZombieConfigField.psd1；正负对照'
    }

    'CF-W-11-ConfigDefaultSingleSource' = @{
        Domain  = 'Constraint'
        Args    = @{ Check = $true }
        Expect  = 0
        Carrier = '实现侧 *.cs 的 `<配置接收段>.<字段> ?? <literal>`（去注释）'
        Note    = 'CW-11 配置缺省单源（可静态确认子集）：就地字面量兜底 = 第二真源；缺省模板静态表仍 attest；基线棘轮 Baselines\Constraint\CF-W-11-ConfigDefaultSingleSource.psd1；正负对照'
    }

    'CF-W-12-SemanticReadClassified' = @{
        Domain  = 'Constraint'
        Args    = @{ Check = $true }
        Expect  = 0
        Carrier = '字段分级表 × 实现侧语义字段读取点（ConstraintFieldIndex，去注释）'
        Note    = 'CW-12 语义/呈现分级：分级表↔声明面互证；语义读取只允许 Adapter 白名单（本轮为空）；基线棘轮 Baselines\Constraint\CF-W-12-SemanticReadClassified.psd1；正负对照'
    }

    'CF-W-13-SnapshotOnly' = @{
        Domain  = 'Constraint'
        Args    = @{ Check = $true }
        Expect  = 0
        Carrier = '实现侧 *.cs 对 MutableConfigTypes 的裸持有（字段/参数/泛型实参，去注释）'
        Note    = 'CW-13 配置只以 ConfigSnapshot<T> 持有（可静态确认的显式类型子集）；var/接口推断与深拷贝不可变仍 attest；基线棘轮 Baselines\Constraint\CF-W-13-SnapshotOnly.psd1；正负对照'
    }

    'CF-W-14-FieldUsageIndex' = @{
        Domain  = 'Constraint'
        Args    = @{ Check = $true }
        Expect  = 0
        Carrier = 'ConstraintIndex 产物（BuildOutput\Debug\ConstraintIndex\FieldUsageIndex.json）的 schema/新鲜度/非空/引用集/未解析接收者'
        Note    = '语义字段使用索引自证：Trees/Fields/Accesses 非空；排除装配不得进入引用集；UnresolvedReceivers 棘轮；基线棘轮 Baselines\Constraint\CF-W-14-FieldUsageIndex.psd1；正负对照'
    }

}

