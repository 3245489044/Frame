@{
    Schema = 'gate-baseline-v1'
    Gate   = 'ContainerSemantics'
    # 计数事实 = 源码静态计数（git 索引）；Debt = 目标未达，DueWhen 给出销账条件。
    OperationHistoryTypeDeclarations   = @{ Value = 0;   Origin = 'Derived'; DueWhen = '' }
    OperationHistoryNamespaceFiles     = @{ Value = 0;   Origin = 'Derived'; DueWhen = '' }
    FrameUsingAgent                    = @{ Value = 0;   Origin = 'Derived'; DueWhen = '' }
    FrameHistoryMentionFiles           = @{ Value = 161; Origin = 'Debt'; DueWhen = 'CF5 收口后仅剩声明面/键白名单注释' }
    AgentHistoryMentionFiles           = @{ Value = 361; Origin = 'Derived'; DueWhen = '' }
    CarrierTableLiterals               = @{ Value = 0;   Origin = 'Derived'; DueWhen = '' }
    TaskRecordFrameRefs                = @{ Value = 0;   Origin = 'Derived'; DueWhen = '' }
    PoolFrameRefs                      = @{ Value = 131; Origin = 'Derived'; DueWhen = '' }
    PoolAgentRefs                      = @{ Value = 112; Origin = 'Derived'; DueWhen = '' }
}
