@{
    # 关系入口面清单（网格，非数值基线；数值基线见 RelationEntrySurface.baseline.json）
    Surface = 'relation-entry'
    Schema  = 'relation-entry-v1'
    Entries = @(
        @{ Id = 'W1'; File = 'Agent.Modules/Framework/RoundAttribution/RoundAttributionOpsFunctions.cs'; Target = 'IRelationGatherPoint'; Status = 'pending'; Wave = 'U3' }
        @{ Id = 'W2'; File = 'Frame/CsharpPrj/Operation/DataStoreDriven/Domains/Hierarchy/TaskReferenceGrounding.cs'; Target = 'IRelationGatherPoint'; Status = 'pending'; Wave = 'U3' }
        @{ Id = 'W3'; File = 'Frame/CsharpPrj/Operation/LoopFoldMethod/Reference/Gateways/NodeRelationGateway.cs'; Target = 'IRelationGatherPoint'; Status = 'partial'; Wave = 'U3' }
        @{ Id = 'W4'; File = 'Frame/CsharpPrj/Operation/LoopFoldMethod/Reference/Gateways/NodeRelationGateway.cs'; Target = '(deleted ownership derivation)'; Status = 'migrated'; Wave = 'U3' }
        @{ Id = 'W5'; File = 'Frame/CsharpPrj/Operation/HierarchyMethod/Nodes/TaskGraph/TaskGraphService.cs'; Target = 'IRelationGatherPoint'; Status = 'pending'; Wave = 'U3' }
        @{ Id = 'W6'; File = 'Projects/Plug/Agent/src/Agent.Ui/Core/Functions/History/AgentUiLibraryHistory.cs'; Target = 'arity=many batch'; Status = 'pending'; Wave = 'U3' }
        @{ Id = 'W7'; File = 'Projects/Plug/Agent/src/Agent.Modules/Framework/Relations/GraphRebuildService.cs'; Target = 'IRelationGatherPoint'; Status = 'pending'; Wave = 'U4' }
        @{ Id = 'W8'; File = 'Frame/PowerShellScript/Share/Data/Invoke-GraphRebuild.ps1'; Target = 'IRelationGatherPoint'; Status = 'pending'; Wave = 'U4' }
        @{ Id = 'K1'; File = 'UniversalReference/Relations/Kernel/DeclaredRelationKernel.cs'; Target = '唯一写核（UR）'; Status = 'migrated'; Wave = 'U2' }
        @{ Id = 'G1'; File = 'UniversalReference/Relations/Gather/RelationGatherPoint.cs'; Target = '唯一求值入口'; Status = 'migrated'; Wave = 'U3' }
    )
}
