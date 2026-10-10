@{
    # ===== Wiring 接线注册表（单一生产者；守卫 = Modules\Node\Check-Wiring.ps1）=====
    # 语义（《统一节点框架-接线本质与彻底重构-方案》§2/§4）：接线单元 W = ⟨D,C,F,φ,r⟩；
    #   生命周期 = declared → primary → retired（本项目口径：**不做 dual-read 运行期双读**，
    #   φ「两源同构」由既有等价门离线承担）。
    #   状态 State 由门禁按结构事实核对（D/F 存在性 + C 读面探针），非人工漂移；报告模式起步。
    # 每条事实必带 Origin/DueWhen（四分事实见 Baselines\README.md）。
    Schema = 'wiring-registry-v1'
    Gate   = 'Wiring'
    Notes  = 'Plane=Logical|Physical；State=declared|primary|retired；CProbeFile/Pattern/Expect 为 C 读面结构探针（Expect∈{read,notRead}）。'

    # 正对照下限：注册单元数须 ≥ 此值（注册表被掏空 ⇒ 判据无标的假绿）
    UnitFloor = @{ Value = 10; Origin = 'Verdict'; DueWhen = '接线闭项（全 retired）后按剩余单元重裁' }

    # 接线单元（Id 唯一；每行 = 一个 W=⟨D,C,F,φ,r⟩；空串 = 该分量不适用）
    Units = @(
        @{
            Value = 'U1.DomainSpec'; Origin = 'Verdict'; DueWhen = 'JSON 装载删除 ∧ 等价门 10/10 恒绿后翻 retired'
            Plane = 'Logical'; Layer = 'L1'; State = 'retired'
            D = 'Projects\Plug\Agent\Config\nodes\conversation.ushar'
            C = 'Agent.Modules.NodeSpec.AgentNodeSpec.TryLoadDeclared'
            F = ''
            Phi = 'Check-NodeDeclSingleSource'; E = 'Check-BakeFramework+DeployFingerprint'
            CProbeFile = 'Projects\Plug\Agent\src\Agent.Modules\NodeSpec\AgentNodeSpec.cs'
            CProbePattern = 'UsharpNodeSpecSource'; CProbeExpect = 'read'
            Retire = 'JSON 装载删除 ∧ 等价门 10/10 恒绿'
        }
        @{
            Value = 'U2.Gather'; Origin = 'Verdict'; DueWhen = '语言面转正（useLanguage 开关删除）后翻 primary→retired'
            Plane = 'Logical'; Layer = 'L1'; State = 'retired'
            D = 'Projects\Plug\Agent\Config\nodes\node-gather.ushar'
            C = 'Agent.Modules.Gather.NodeGatherHosting.LoadSpec'
            F = ''
            Phi = 'Check-NodeGather'; E = 'Check-BakeFramework'
            CProbeFile = 'Projects\Plug\Agent\src\Agent.Modules\Gather\NodeGatherHosting.cs'
            CProbePattern = 'NodeGatherDeclaration\.TryLoad'; CProbeExpect = 'read'
            Retire = 'node-gather-*.json 删除 ∧ gather 等价门恒绿'
        }
        @{
            Value = 'U3.Vocabulary'; Origin = 'Verdict'; DueWhen = 'C# 字面量白名单归零后翻 retired'
            Plane = 'Logical'; Layer = 'L2'; State = 'primary'
            D = 'Projects\Plug\Agent\Config\nodes\process.ushar'
            C = 'Agent.Modules.NodeSpec.AgentNodeDomains#SelectionVocabularyAdapter'
            F = 'Frame\CsharpPrj\Modules\Node\Spec\NodeSelection.cs'
            Phi = 'Check-NodePortPurity'; E = 'Check-BakeFramework'
            CProbeFile = 'Projects\Plug\Agent\src\Agent.Modules\NodeSpec\AgentNodeDomains.cs'
            CProbePattern = 'SelectionVocabularyAdapter'; CProbeExpect = 'read'
            Retire = 'C# 字面量归零 ∧ 端口非空装配恒真'
        }
        @{
            Value = 'U4.RefVocabulary'; Origin = 'Verdict'; DueWhen = 'F5.3 内嵌产物退役后翻 retired'
            Plane = 'Logical'; Layer = 'L2'; State = 'primary'
            D = 'Projects\Plug\Agent\Config\nodes\reference.ushar'
            C = 'Agent.Launcher.ReferenceVocabularyProvider.Resolve'
            F = 'Frame\CsharpPrj\Operation\DataStoreDriven\ReferenceSchema\reference-schema.generated.json'
            Phi = 'Check-ReferenceVocabulary'; E = 'Check-BakeFramework+DeployFingerprint'
            CProbeFile = 'Projects\Plug\Agent\src\Agent.Launcher\Composition\Hosting\ReferenceVocabularyProvider.cs'
            CProbePattern = 'DeclaredReferenceVocabulary'; CProbeExpect = 'read'
            Retire = '内嵌产物退役 ∧ 边集 ≡ 声明集（双向）'
        }
        @{
            Value = 'U5.HierarchyRefs'; Origin = 'Verdict'; DueWhen = '无常驻回退面（恒 primary）'
            Plane = 'Logical'; Layer = 'L2'; State = 'primary'
            D = 'Projects\Plug\Agent\Config\nodes\reference.ushar'
            C = 'Operation.DataStoreDriven.ReferenceGraphCoordinator'
            F = ''
            Phi = 'Check-NodeHierarchyRefs'; E = 'Check-BakeFramework'
            CProbeFile = ''; CProbePattern = ''; CProbeExpect = ''
            Retire = '无回退面；归属族常量集 ⊆ 声明集恒真'
        }
        @{
            Value = 'U6.Workflows'; Origin = 'Verdict'; DueWhen = 'C# 内置兜底退役后翻 retired'
            Plane = 'Logical'; Layer = 'L4'; State = 'primary'
            D = 'Projects\Plug\Agent\Config\workflows'
            C = 'Agent.Core.Nodes.Loading.RoundSpecLoader'
            F = ''
            Phi = 'Check-AgentOrchestrationFlow'; E = 'Check-BakeFramework'
            CProbeFile = ''; CProbePattern = ''; CProbeExpect = ''
            Retire = 'C# 内置兜底删除 ∧ DeclaredFlowCount 基线稳定'
        }
        @{
            Value = 'U7.ReadIn'; Origin = 'Verdict'; DueWhen = '恒 primary（无回退面）'
            Plane = 'Logical'; Layer = 'L4'; State = 'primary'
            D = 'Projects\Plug\Agent\Config\workflows\read-in.ushar'
            C = 'Agent.Modules.ReadIn.ReadInServices'
            F = ''
            Phi = 'Check-AgentOrchestrationFlow'; E = 'Check-BakeFramework'
            CProbeFile = ''; CProbePattern = ''; CProbeExpect = ''
            Retire = '常开注册 + 启动校验恒真（无回退面）'
        }
        @{
            Value = 'U8.ToolFace'; Origin = 'Verdict'; DueWhen = 'tool-face.json 删除后翻 retired'
            Plane = 'Logical'; Layer = 'L3'; State = 'primary'
            D = 'Projects\Plug\Agent\Config\tool-face.ushar'
            C = 'Agent.Launcher.ToolFaceAssembler.ReadFacePlan'
            F = 'Projects\Plug\Agent\Config\tool-face.json'
            Phi = 'Check-ToolFaceDeclaration'; E = 'Check-ToolFaceDeclaration'
            CProbeFile = 'Projects\Plug\Agent\src\Agent.Launcher\Composition\Hosting\ToolFaceAssembler.cs'
            CProbePattern = 'ToolFaceDeclaration\.Load'; CProbeExpect = 'read'
            Retire = 'tool-face.json 消费面删除 ∧ 两面轨逐项一致'
        }
        @{
            Value = 'U9.NodeCache'; Origin = 'Verdict'; DueWhen = '旧读面删除后翻 retired'
            Plane = 'Logical'; Layer = 'L5'; State = 'primary'
            D = 'Projects\Plug\Agent\Config\workflows\context-projection.ushar'
            C = 'Agent.Launcher.ConversationHosting#contextCache'
            F = ''
            Phi = ''; E = '派生缓存（无物理 E）'
            CProbeFile = 'Projects\Plug\Agent\src\Agent.Launcher\Composition\Hosting\ConversationHosting.cs'
            CProbePattern = 'contextCache'; CProbeExpect = 'read'
            Retire = '开关删除（恒开）∧ 旧读面删除'
        }
        @{
            Value = 'F5.Bridges'; Origin = 'Verdict'; DueWhen = '烘焙 + 部署副本刷新（三方指纹一致）后翻 primary'
            Plane = 'Physical'; Layer = 'L1'; State = 'declared'
            D = 'Frame\Usharp\src\Usharp.Adapters.Node'
            C = 'Agent 宿主 Default ALC 解析探针'
            F = ''
            Phi = 'Check-BakeFramework'; E = 'DeployFingerprint'
            CProbeFile = ''; CProbePattern = ''; CProbeExpect = ''
            Retire = '三方指纹一致 ∧ 部署副本刷新'
        }
    )
}
