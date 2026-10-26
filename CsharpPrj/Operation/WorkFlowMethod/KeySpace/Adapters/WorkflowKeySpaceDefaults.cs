using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Rules;
using Modules.KeySpace.Runtime;
using WorkFlowMethod.Skeletons.NodeProcess.Execution;

namespace WorkFlowMethod.KeySpace.Adapters;

/// <summary>
/// WFM 键空间组合根默认件：声明单源 = <see cref="NodePortContractRegistry.Shared"/>，
/// 分类/判等/规划/绑定全部由框架唯一实现承担（层 4 不再 new 第二套规则）。
/// </summary>
public static class WorkflowKeySpaceDefaults
{
    public static IKeySpaceDeclaration Declaration => NodePortContractRegistry.Shared.Declaration;

    public static IKeyEquivalence Equivalence => new KeyEquivalence(Declaration);

    public static KeySpaceFacade CreateFacade()
    {
        var declaration = Declaration as KeySpaceDeclaration
                          ?? KeySpaceDeclaration.Empty(Declaration.Id);
        return new KeySpaceFacade(
            declaration,
            new GrammarKeyClassifier(declaration, WorkflowKeyDiagnosticsSink.Instance),
            new KeyEquivalence(declaration),
            new KeySetPlanner(),
            new KeyBinder(),
            WorkflowKeyDiagnosticsSink.Instance);
    }
}
