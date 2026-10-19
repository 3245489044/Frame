using Share.Conversation.Aggregates;

namespace Operation.HierarchyMethod.History;

/// <summary>框架独立运行时的缺省分类器：所有非空旧行都视为历史根（Agent 侧会注册带派生前缀规则的实现）。</summary>
public sealed class NullLegacyHistoryClassifier : ILegacyHistoryClassifier
{
    public static NullLegacyHistoryClassifier Instance { get; } = new();

    public bool IsLegacyRoot(string key) => !string.IsNullOrWhiteSpace(key);

    public SessionRole ResolveRole(string key) => SessionRole.HistoryRoot;
}
