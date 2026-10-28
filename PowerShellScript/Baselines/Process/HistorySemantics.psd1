@{
    Schema = 'gate-baseline-v1'
    Gate   = 'HistorySemantics'
    # 计数事实 = 源码静态计数（git 索引）；Debt = 目标未达，DueWhen 给出销账条件。
    HistoryTypeDeclarations   = @{ Value = 0;  Origin = 'Derived'; DueWhen = '' }
    HistoryNamespaceFiles     = @{ Value = 0;  Origin = 'Derived'; DueWhen = '' }
    FrameUsingAgent           = @{ Value = 0;  Origin = 'Derived'; DueWhen = '' }
    FrameAgentTypeDeclarations = @{ Value = 0; Origin = 'Derived'; DueWhen = '' }
    OperationHistoryMentionFiles = @{ Value = 85; Origin = 'Debt'; DueWhen = '正名收口（History 目录/标识符迁 Conversation/Container）完成后归零' }
}
