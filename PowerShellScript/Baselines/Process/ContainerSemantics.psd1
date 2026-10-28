@{
    Schema = 'gate-baseline-v1'
    Gate   = 'ContainerSemantics'
    # 计数事实 = 源码静态计数（git 索引）；Debt = 目标未达，DueWhen 给出销账条件。
    OperationHistoryTypeDeclarations   = @{ Value = 0;   Origin = 'Derived'; DueWhen = '' }
    OperationHistoryNamespaceFiles     = @{ Value = 0;   Origin = 'Derived'; DueWhen = '' }
    FrameUsingAgent                    = @{ Value = 0;   Origin = 'Derived'; DueWhen = '' }
    FrameHistoryMentionFiles           = @{ Value = 163; Origin = 'Debt'; DueWhen = 'CF5 收口后仅剩声明面/键白名单注释' }
    AgentHistoryMentionFiles           = @{ Value = 358; Origin = 'Derived'; DueWhen = '' }
    CarrierTableLiterals               = @{ Value = 17;  Origin = 'Debt'; DueWhen = 'CF4 表名参数化完成时收敛到 0' }
    TaskRecordFrameRefs                = @{ Value = 0;   Origin = 'Derived'; DueWhen = '' }
    PoolFrameRefs                      = @{ Value = 133; Origin = 'Derived'; DueWhen = '' }
    PoolAgentRefs                      = @{ Value = 110; Origin = 'Derived'; DueWhen = '' }
}
