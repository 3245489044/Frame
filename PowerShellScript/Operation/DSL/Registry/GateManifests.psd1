@{
    ShareMessagingBoundary = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '通用信息传递内核边界（Share.Messaging）：C1 领域零泄漏 / C2 零 ProjectReference / C3 禁时钟与网络 / C4 唯一状态枚举 / C5 直连禁令（Share 树禁 ITransferKeyStore·ISessionSend·SignalIntake）/ C6 三处登记齐备 / C7 正负对照 / C8 适配器面'
    }

    ContextGeometryBoundary = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '上下文几何内核边界（Share.ContextGeometry）：C1 领域零泄漏 / C2 零 ProjectReference / C3 禁时钟·网络·文件 IO / C4 段序纪律（禁按 Key 排序） / C5 三处登记齐备 / C6 正负对照'
    }

    ContextGeometryPrefixPurity = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '前缀纯度（R3）：前缀正文/规范字节/段禁含信封字段（MessageId/Timestamp/TraceId/CreatedAt）；正负对照防恒真'
    }

    NodePromptBinding = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '节点提示词绑定：模板槽 ⊆ PromptSlot 词表 / node:// domain 已声明 / 禁 SQL 旁路 / 禁新增 ContextNodeKind 用法；正负对照'
    }

    AgentModulesFrameworkLayout = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'Agent.Modules.Framework 归属唯一 / 契约不内联 / 依赖方向 / 零 Agent 件可见化；C5 正负对照（内存合成，零侧写）'
    }

    # ===== 带 -Check 开关的门禁（-Check 时退出码裁决） =====
    AgentBuildPoint = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Expect = 0
    }

    ToolCallConvergence = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '工具调用聚拢（A1–A7）：Launcher 不再装配 / 注册单点 / 客户端单实例 / 声明面单一 / 无回环回退支 / 内核纯净 / 无依赖倒置'
    }

    AgentDataSeparation = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Expect = 0
    }

    HistoryAssemblySurface = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'History 装配面：空态预装配(H1) / 档案分档+声明面(H2) / mode 注入(H3) / 视口单一入口(H4) / 端口只读(H5) / 缓存最大化分段+差分(H6) / UI 轨零 Binding(H15) / 双出口同供数(H16) / 空态可见化(H17) / 任务池状态单源(H18)'
    }

    TransferKey = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '键址传递框架 KTF：键文法/方向-kind-status 词表/6 个 transfer_key_* 单步/agent.transfer 族/ps_runtime+transfer_key 轨/powershell tool_face=full/仓库+数据根双写'
    }

    KeyspaceMirror = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '键空间镜像（E26）：仓库 agent.keyspace.json vs Data 运行时声明的 family 模板 + port 集合等价；Data 缺失 = 干净 checkout，跳过并提示'
    }

    AgentDefaultDispatch = @{
        Domain = 'Agent'
        Args   = @{}
        Tier   = 'fast'
        Expect = 0
        Note   = '默认路由（P5-A/B）：结构化意图 → 标签 → 执行腿成对；file 标签/执行腿经声明键驱动（R-07 迁移至 Frame DSL\Agent，唯一实现）'
    }

    PsSkillToolMap = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'SKILL↔ActionToTool 对账（R-05）：contracts action 集 == ActionToTool.Keys，ActionToTool.Values == SKILL.md 契约轨工具名集；正/负对照'
    }

    LlmRoutingSurface = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'LLM 路由生效面：L1 禁静默降级(catch→local) / L2 缺省声明面(禁硬编码 local 兜底) / L3 生产兜底非 local / L4 自举不覆写配置真实 provider / L5 来源可审计(RouteOrigin origin)'
    }

    AgentDecisionCoreBoundary = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Expect = 0
    }

    VmKernelReferenceBoundary = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'VM 内核引用面（VM-P6-d）：内核热路径（队列/循环/实例/路由/换代/装载/通用换代服务）零编译依赖——禁 Roslyn（Microsoft.CodeAnalysis）/ CSharpCompilation / VmCompiler；按面分层机器判据'
    }

    AgentRuntimeSlots = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '承载面 K10（S1/S2 静态可变槽）+ 取用面 S4–S8（键值唯一 / 键↔宿主 1:1 / 取用面类型化 / 声明↔形态集合相等〔B2-c〕 / 跨代句柄禁令〔B5-a〕）；-WriteManifest 为写操作，聚合器禁用'
    }

    RefreshFrameworkBoundary = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'UI 刷新通用框架红线（Strategy.Refresh）：框架内零领域串（agent/conversation/db/task/session/history）/ 零上层依赖（Agent./Plate./UiBackend./FrontEndDriven.）；正负对照 -NegativeControl'
    }

    UiSelectionExternalization = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '选择态外置守卫（框架级态外置）：C1 刷新协议禁 DOM 反查（PollAttrs 第三/四参 null）/ C2 采集端（capture 产出 uiState.select + [data-select-slot]）/ C3 消费端（HtmlBridgePayload.SelectionKeys + UiSelectionState）/ C4 历史归属解析禁伪造 default / C5 受控态 _currentHistory；负对照 -NegativeControl'
    }

    AgentProcessSurface = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '过程面单源守卫：键文法(ProcessKeys) / 事件类别(ProcessNodeKinds) / 进度接收器(SyncProgress)'
    }

    EntrySingleSource = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '入口单源守卫（写库即实现 · Agent.Core.Entry）：E-1 AgentApiPort 定义恰 1 · E-1b AgentEntryCallableTrack 恰 1 · E-2 AgentIntentMapping 恰 1 · E-3 AgentDispatcher 恰 1 · E-4 通用轨经 AgentEntryPort 转发 · P-1 正对照（intent 常量 ≥ 基线）· P-2 负对照'
    }

    IntentMappingNoLiteral = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'intent 映射字面量（B4/DUP-10）：AgentIntentMapping 的 _fnToIntent 键零 wire 字面量（改引用 WorkflowFunctionNames.*）；正/负对照'
    }

    DbToolbarDictAlloc = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '数据库页工具条字典分配（B3/RED-13）：渲染路径零逐命令 `new Dictionary{type=db}`（改走 DbCmdAttrs.For 缓存基座）；正/负对照'
    }

    RecordSingleTextForm = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '记录单一文本形态（B3/RED-06）：DetailPayloadJson 引用 = 0（载荷视图由记录态派生）· DetailRecordJson 在位；正/负对照'
    }

    GatherDiagnosticsSingleSource = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '聚拢诊断单源（B2/RED-09）：NodeGatherService 内 Diagnostics 自拼 = 0 · 渲染侧由模型字段投影（BuildGatherNote）；正/负对照'
    }

    HostAccessNoRestate = @{
        Domain = 'Agent'
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

    DataContracts = @{
        Domain = 'Data'
        Args   = @{ Check = $true }
        Expect = 0
    }

    SpecBoundary = @{
        Domain = 'Spec'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'spec 生成/手写边界（R0 基线冻结）：源码树零生成物 / 清单非空 / 正负对照；基线 Baselines\SpecBoundary.psd1'
    }

    SpecToolingContract = @{
        Domain = 'Spec'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '规格工具链契约（R2）：SYNOPSIS / 确定性 exit（Debt 棘轮）/ 无深度计数；基线 Baselines\SpecToolingContract.psd1'
    }

    SpecGeneratedBoundary = @{
        Domain = 'Spec'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'spec 生成/手写分离边界（R4 SEP）：源码树零生成物 / obj 零第二生成物（K2）/ 生成码头注释 / 源⇄产物双向 / manifest 一致性（条件判据）；基线 Baselines\SpecGeneratedBoundary.psd1；-Warn 为回退开关'
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
        Note   = '工作流↔节点域对应守卫（U4）：[Node] uses=/produces= 域 token ∈ Config\nodes\*.ushar 的 [Domain] 集（双向对账）；正/负对照防假绿；基线 Baselines\NodeFlowDomainMatch.psd1'
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
        Note   = '接线生命周期守卫（Wiring）：声明状态 ⇔ 实现事实（D/F 存在性 + C 读面探针），declared→primary→retired（本项目口径废除 dual-read 运行期双读）；注册表单源 Baselines\Wiring.psd1；正/负对照防假绿；报告模式起步（Args 空 = 不设强制；-Check 转强制）'
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

    DomainWireSingleSource = @{
        Domain = 'Node'
        Expect = 0
        Note   = '节点域接入聚拢点守卫 D1–D3（R4/R5）：派生面（instances\nodes.json 的 placements[] + placement.ushar 的 LocPlacement/LocToPlacement 行）≡ 声明真源 Config\nodes\*.json 的位置并集（漂移即红，-Write 可生成）；散弹点棘轮（per-domain 槽键常量 / 声明路径常量 / 载体工厂方法，报告模式起步，基线 Baselines\DomainWireSingleSource.psd1）；正对照（声明面 ≥ 基线位置数）+ 负对照（合成 per-domain 形态必被捕获）'
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
        Note   = '四个横切关注点框架单源 D1–D11：诊断载体（IDiagnosticsSink）/ 回退姿态（FlowPosture）/ 键寻址（KeyAddressing）/ 簿记流模板（BookkeepingFlowCatalog）/ 取参单源（WorkflowContextParams）/ 簿记流执行器构造点（BookkeepingFlowAssembly）/ 姿态收口台账（D9：RequireFlow 装配点，批次 C 后由 Baselines\AgentFrameworkSingleSource.psd1 的 PostureRequireFlowRequired 翻转生效）/ 读组装组合点（D10）/ 运行期本地流 msgId 单源（D11：统一 DiagnosticsCodes + context）；基线单源 = Baselines\AgentFrameworkSingleSource.psd1（SR-2a 已由遗留子根并入规范根）'
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

    AgentRuntimeUIConfig = @{
        Domain = 'Agent'
        Args   = @{}
        Expect = 0
        Note   = '默认校验模式；-FailOnWarning 为严格模式（默认关闭）'
    }

    HtmlViewerVocabulary = @{
        Domain = 'Usharp'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'HTML 模板键 / 类名 / region 锚零领域语义：框架只描述结构（壳/分栏/面板/列表/项/行/分组/折叠/分段/状态），语义只出现在配置键值 + Agent 数据模型 + 域样式注入；扫描面 = 框架资产 + 模板键消费点（HtmlStreamRenderer/ConversationPageRenderer）；正负对照 -NegativeControl；基线 = Baselines\HtmlViewerVocabulary.psd1'
    }

    BatchLineEndings = @{
        Domain = 'Process'
        Args   = @{}
        Expect = 0
        Note   = '默认检查模式；-Fix 为写操作，聚合器禁用'
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
        Domain = 'Usharp'
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
        Args   = @{}
        Expect = 0
        Note   = '默认检查模式 + 基线棘轮（F-P0-1e：检查 3/4/5 三类计数只减不增，增量即红；基线 = 同目录 LogMessagesBaseline.json）；-Strict / -FailOnDynamic / -StrictOrphans 为严格模式；-UpdateBaseline 为写操作，聚合器禁用'
    }

    SingleWriter = @{
        Domain = 'Data'
        Args   = @{}
        Expect = 0
        Note   = '默认检查模式；H 段 Stream 位置键单键（单行不变量：form=stream ⇒ key 模板无逐事件坐标）；-ShowDetail 显示详情'
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
        Note   = '键文法单源守卫 K1–K6：键族字面量单源（白名单）/ 零手写键切片 / 零就地键序 / 零自建比较器 + 正对照（文法定义点数 ≥ 基线、声明绑定用例在位）；基线单源 = Baselines\KeyGrammar.psd1（脚本零数值基线）；-Check 为校验模式，-ShowDetail 显示逐条实测'
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

    FrameworkActivation = @{
        Domain = 'Data'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '框架激活守卫 FA1：`new ReferenceGraphEngine` 装配点数 ≥ 基线（防图引擎退回死代码/空转）；基线 = Baselines\FrameworkActivation.psd1'
    }

    SemanticCentralization = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '语义集中守卫 SC1–SC2：收口类定义点 ≥ 基线 / 域内边类型字面量直接赋值（非豁免）≤ 基线（只减不增）；基线 = Baselines\SemanticCentralization.psd1'
    }

    CircuitSemantics = @{
        Domain = 'Agent'
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
        Note   = '初始化护栏门禁（P1）G1–G4：`throw new ArgumentNullException` / `ArgumentNullException.ThrowIfNull` 只减不增 + `PortGuard.Required` 只增不减 + 正/负对照；基线 = Baselines\InitGuards.psd1（单源，禁内联）'
    }

    # ===== 过程层结构治理（修复文档 G/H 批）=====
    BatchScope = @{
        Domain = 'Process'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '批次授权单守卫 B0–B3（过程层 P1/P2/P6）：实际改动集（git status，含子模块）⊆ 活动批 allowSet ∪ derived ∪ preexisting；denySet 面禁改；正/负对照防假绿。授权单路径单源 = Baselines\BatchScope.psd1（Docs\analysis\2026\10\10.5\2026-10-05-过程层缺陷结构治理.batches.json）；授权单缺件 ⇒ 灰度 warn'
    }

    VerifyProportionality = @{
        Domain = 'Process'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '验证成比例守卫 H0–H5（过程层 P4）：产品单工程改动成比例（验证/改动比 ≤ 1.5）∧ 映射落点 ∧ 不静默丢面 ∧ 未分类改动 fail-loud；基线 = Baselines\VerifyProportionality.psd1'
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

    # ===== 反模式族守卫（P9 落地）=====
    AntiPatternFamily = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '反模式族守卫（P9）：clinerules\formula.md §8（族=通用规则单表）C1 族唯一/件数 ≥ 基线 · C2 covered ID 形态合法且全局唯一（每条恰属一族）· C3 crit∈{macro,micro} · C4 通用规则列非空 · C5 通用规则符号可解析（回指已注册定义点）· C6 ID 集合守恒（covered ⊇ 基线，只增不减）· C7 正/负对照；基线 = Baselines\AntiPatternFamily.psd1；-WriteBaseline 为写操作，聚合器禁用'
    }

    # ===== 端口层消除（D3 僵尸脚本）=====
    DualCarrierExclusion = @{
        Domain = 'Data'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '双载体互斥门（D3）：端口块禁「impl delegate ∧ script」并存（僵尸脚本）；D1 新增即红 · D2 只减不增（可销账可见化）· D3 正/负对照（防假绿）；基线 = Baselines\DualCarrierExclusion.psd1'
    }

    # ===== VM 换代路线收敛（VM-R1 构建到新目录）=====
    GenerationLayout = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'VM-R1 代目录布局契约：gen-<n>（n ≥ 1）须含全 Agent.*.dll（计数 ≥ 就地产物面参照）+ Agent.Launcher.deps.json（ALC 解析基座=本目录）+ vm-program.json；代根 = Projects\Plug\Agent\BuildOutput\Generations；落点禁 BuildOutput\<Config>（被运行进程锁定）'
    }

    GenerationRebuildChain = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'VM-R5 换代链结构守卫：构建到新目录（代目录布局单源）→ 换 ALC（重装配桥 + 按代端口注册）→ 重装配（F 段重挂 + 装配契约）→ 触发（源码监视 + 命令触发）之结构锚点 C1–C7 在位'
    }

    # ===== 生效面断言（B′ · AI 行为护栏）=====
    EffectiveFace = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = '生效面断言（EffectiveFace）：运行时装载面指纹 == 构建产物指纹（Agent.Launcher.dll + 共享核心件；dev=就地 / locked=current→gen-<n>）· [E3] 正/负对照；基线 = Baselines\EffectiveFace.psd1；机械复算 = Frame\Invoke\Invoke-BuildEffective.ps1 -Assert'
    }

    # ===== USharp 纯化门禁（N4：V1–V6-form）=====
    UsharpDocForm = @{
        Domain = 'Usharp'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'USharp 记录实例形态（V1/V2/V3-form + V9 依赖方向）：纯三列模板与表声明 / 零兼容残留（IndexJson·KvJsonTable·KvDocumentTable·ExtractScalar）/ 规范形单源 / 存储层零 Usharp.* 依赖；基线 = Baselines\UsharpDocForm.psd1'
    }

    UsharpGetParity = @{
        Domain = 'Usharp'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'usharp_get 集中式转换层（V4/V5/V6-form）：注册单点（UsharpDataPorts.EnsureSqlFunctions）/ 下推表达式同源（UsharpRecordPaths）/ 表达式索引落点 / 单语句 CAS；基线 = Baselines\UsharpGetParity.psd1'
    }
    UsharpJoint = @{
        Domain = 'Usharp'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'USharp ↔ C# 交汇面聚拢：消费侧经唯一门面 UsharpDataPorts / 唯一 JSON 出口 UsharpValueJson（全限定散点=0）/ 零值模型内联构造（new Usharp* 棘轮只减不增）/ 门面四语义覆盖；扫描面 = 生产面 Projects\**\src\**（E-4 排除测试面）；基线 = Baselines\UsharpJoint.psd1'
    }



    # ===== PrjBridge 解耦（冻结 ABI / 只编自身）=====
    PrjBridgeDecoupling = @{
        Domain = 'Meta'
        Args   = @{ Check = $true }
        Expect = 0
        Note   = 'PrjBridge 解耦守卫（INV-PB-1）：桥产物 AssemblyRef 零 Agent.*/GameEditor.*（结构事实=元数据引用表）⇒ Agent/Engine 任意变更不迫使桥重编'
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
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '域写能力单谓词（B3/SCT-01）：AgentWriteSources.Capability 定义 = 1，旧三处判定引用 = 0；正/负对照'
    }

    SaveDispatchNoDomainSwitch = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '记录保存薄分派（B3/SCT-06）：SaveRecordAsync 体内域分支 = 0，域知识下沉 IRowSaveHandler 表；正/负对照'
    }

    DbRenderPurity = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '渲染纯化（B3/ERR-07+SCT-04）：数据库页渲染路径零写交互态（写入只在命令期）；正/负对照'
    }

    DomainResolveSingleEntry = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '域解析单入口（B3/DUP-04）：裸标签别名表定义 = 1（DomainRoute），旧入口在编排类归零；正/负对照'
    }

    RowIdentityBareKey = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '行身份裸键回退单处（B3/SCT-10）：RowSelection.Decode 仅允许出现在 DomainRoute（唯一分派点）；正/负对照'
    }

    IntentSupportsProjected = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '能力位注册表投影（B3/DUP-11）：Supports 内逐 intent case = 0，能力位由探针表投影；正/负对照'
    }


    EffectiveModeSingleSource = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '生效 mode 单源（G1 呈现裁决聚拢点）：declaredMode ?? snapshot.Mode 求值点 = 1（ConversationStageBodies.EffectiveMode）；呈现裁决单入口 = SessionPresentation（OperationModeCatalog.IsOperation 越点消费 = 0）；呈现侧内联回退 declaredMode = 0；正/负对照'
    }

    DetailReadWriteSamePort = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '详情读=写同端口（G3 行身份聚拢点）：LoadNodeDetailAsync 体内读经 AgentWriteSources.TryGetRecord（全域行端口），域单点查询 TryGetNodeAsync = 0；正/负对照'
    }

    RenderNoDomainBranch = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '详情呈现零域分支（G4 呈现投影聚拢点）：RenderNodeDetail 消费 state.DetailNote、零硬编码节注、编辑器受可编辑门约束（禁假可用）；正/负对照'
    }

    OperationRowCarriesActor = @{
        Domain = 'Agent'
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
        Note   = '键空间框架（Modules.KeySpace）：KS1 族命中 / KS1b 端口契约全集覆盖 / KS2 三态禁坍缩 / KS4 词法唯一 / KS5 域词隔离 / KS6 诊断码登记 / KS7 族注册唯一 / KS8 负对照'
    }

    DispatchTraceSchema = @{
        Domain = 'KeySpace'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '分发目标闭合（DTC）：DispatchTraceSchema 六阶段封闭 ∧ 分发/休眠遥测 msgId 已登记（未登记即静默 ⇒ 红）'
    }

    IdentityTemplatePrefix = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '身份模板声明（DTC-6）：identityTemplate 引用可解析 ∧ 花括号配对 ∧ 前缀可判定（单占位或字面量起始）'
    }

    SendDeclarationCoverage = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '发送面声明覆盖（F2）：发送入口零裸 null,null（经 SessionSendDeclaration 显式派生）∧ Agent.Ui 内 "dispatch" 字面量 = 0；正/负对照'
    }

    ReadinessSingleEntry = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '就绪/诊断单点（F4）：ReadinessFace.Of 定义 = 1 ∧ 旧 RenderReadinessHtml = 0 ∧ 恒假常量 IncludeGatherDiagnostics = 0（声明驱动 GatherDiagnosticsSwitch）；正/负对照'
    }

    UnknownVisible = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '未知可见化守卫（禁静默出口 U）：unknown_tokens.json 载体在册（unknown/dash tokens + EmbeddedResource + UnknownTokens 门面）∧ G2 身份维度判空隐藏（M1 后已归零）∧ G3 正/负对照；权威 = Docs\analysis\2026\10\10.17 设计单'
    }

    RecordDraftSingleSource = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '记录草稿单源（F5）：旧同值字段 DetailSnapshot 引用 = 0 ∧ 基线字段 DetailBaselineJson 在位（装载写一次/保存后更新/恢复读基线）；正/负对照'
    }

    TemplateAccessSingleEntry = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '模板访问单入口（F6）：除 ConversationTemplates 自身外直取 HtmlUiHost.Templates = 0 ∧ ConversationTemplates.Resolve 在位；正/负对照'
    }

    IntentParity = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'IEF 写读同源对账：history.link 写面 = graph:history_owns_task（读面绑定同一写面）∧ 后端调用注入写原语 ∧ 禁 no-op 成功 ∧ 探针单源(backend.Supports) ∧ 注册表键集=已实现 handler 集 ∧ 正/负对照'
    }

    ActionCatalogSingleSource = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '动作目录单源：运行期 CommandSystemCoordinator.RegisteredActions → 加载期 CommandLineValidator.SetRegisteredCommands 投影（FileDrivenPipelineRunner 恒注入）∧ wait-until 能力在位 ∧ 正/负对照'
    }

    IntentContract = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = 'IEF+ 执行契约守卫 C1–C6：每行 ResultKind / 写入行 ReadBack / Writes⊆Reads / PreconditionKind 有实现 / parity CaseId 不悬空 / 正负对照；基线 = Baselines\IntentContract.psd1'
    }

    EchoConstants = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '回显投影单源：写面回显禁叙事常量（Task.FromResult("已…")）∧ 必须经 OutcomeEchoProjector 输出 decision/wrote ∧ 正对照保留会话渲染映射'
    }

    ScriptNoFixedSleep = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '脚本禁固定等待：action=sleep 且 target ≥ 1000ms = 0（失败须落在强判据，禁「未跑完」）∧ wait-until 有界轮询能力在位（WaitUntilSystem 注册）∧ 正/负对照'
    }

    SignalDeclaration = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '信号声明面（S1–S6）：状态词表↔枚举 / 生命周期可达终态且终态无出边 / 三张判定表总函数形态 / 六段流闭合 / Obs required+enum / fn 常量登记'
    }

    SignalTraceSchema = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '信号 Trace（T1–T4）：六相位闭集 / Signal 运行期日志键全登记 / 关键可见化键在册 / 六段 fn 描述符正对照'
    }

    ContextGeometryConvergence = @{
        Domain = 'Agent'
        Args   = @{ Check = $true }
        Tier   = 'fast'
        Expect = 0
        Note   = '上下文几何聚拢点（K0.5/C1–C6）：白名单外零内层端口解析 / 裁剪单入口 / 命中口径单源 / Host 方法≤12 行≤400 / 正负对照'
    }

}

