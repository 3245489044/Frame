@{
    Schema = 'gate-baseline-v1'
    Gate   = 'ContainerSemantics'
    # 计数事实 = 源码静态计数（git 索引）；Debt = 目标未达，DueWhen 给出销账条件。
    OperationHistoryTypeDeclarations   = @{ Value = 0;   Origin = 'Derived'; DueWhen = '' }
    OperationHistoryNamespaceFiles     = @{ Value = 0;   Origin = 'Derived'; DueWhen = '' }
    FrameUsingAgent                    = @{ Value = 0;   Origin = 'Derived'; DueWhen = '' }
    FrameHistoryMentionFiles           = @{ Value = 160; Origin = 'Debt'; DueWhen = 'RF8 正名后仅剩声明面/键白名单/载荷列注释（同 HistorySemantics H5d）' }
    AgentHistoryMentionFiles           = @{ Value = 360; Origin = 'Derived'; DueWhen = '' }
    CarrierTableLiterals               = @{ Value = 0;   Origin = 'Derived'; DueWhen = '' }
    TaskRecordFrameRefs                = @{ Value = 0;   Origin = 'Derived'; DueWhen = '' }
    PoolFrameRefs                      = @{ Value = 131; Origin = 'Derived'; DueWhen = '' }
    PoolAgentRefs                      = @{ Value = 112; Origin = 'Derived'; DueWhen = '' }
}
