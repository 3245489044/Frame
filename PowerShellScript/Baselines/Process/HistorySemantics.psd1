@{
    Schema = 'gate-baseline-v1'
    Gate   = 'HistorySemantics'
    # 计数事实 = 源码静态计数（git 索引）；Debt = 目标未达，DueWhen 给出销账条件。
    # 扫描面 = Frame\CsharpPrj（RF8-1 扩容：H1 三账 / H2 / H4 全层；ContainerSemantics C1 仍只扫 Operation，分工禁双写者）。
    OperationHistoryTypeDeclarations  = @{ Value = 0;   Origin = 'Derived'; DueWhen = '' }
    ShareHistoryTypeDeclarations      = @{ Value = 0;   Origin = 'Derived'; DueWhen = '' }
    OtherFrameHistoryTypeDeclarations = @{ Value = 0;   Origin = 'Derived'; DueWhen = '' }
    FrameHistoryNamespaceFiles        = @{ Value = 0;   Origin = 'Derived'; DueWhen = '' }
    FrameUsingAgent                   = @{ Value = 0;   Origin = 'Derived'; DueWhen = '' }
    FrameAgentTypeDeclarations        = @{ Value = 0;   Origin = 'Derived'; DueWhen = '' }
    OperationHistoryMentionFiles      = @{ Value = 84;  Origin = 'Debt'; DueWhen = '白名单（键族/工具方法/持久化载荷列）外词面清零后归零' }
    ShareHistoryMentionFiles          = @{ Value = 38;  Origin = 'Debt'; DueWhen = '同上' }
    OtherFrameHistoryMentionFiles     = @{ Value = 38;  Origin = 'Debt'; DueWhen = '同上' }
    FrameHistoryMentions              = @{ Value = 160; Origin = 'Debt'; DueWhen = '同上' }
}
