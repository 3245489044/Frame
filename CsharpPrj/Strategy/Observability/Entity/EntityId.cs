namespace Strategy.Observability.Entity;

/// <summary>
/// 实体身份（E1）：不绑定任何应用语义，用「作用域键 + 实体类型名 + 序号」三元组表达。
/// 内核只负责 分配/递增/层级折叠（ScopeId = parent + suffix 可聚簇），不解释实体含义。
/// </summary>
public readonly record struct EntityId(string ScopeId, string EntityType, int Seq)
{
    /// <summary>紧凑比较键（作用域聚簇友好）。</summary>
    public string Key => $"{ScopeId}\u001f{EntityType}\u001f{Seq}";

    /// <summary>构造父作用域派生出的子作用域实体身份（层级折叠：scope = parent + suffix）。</summary>
    public static EntityId Derive(string parentScopeId, string suffix, string entityType, int seq)
        => new($"{parentScopeId}{suffix}", entityType, seq);

    public override string ToString() => Key;
}

/// <summary>实体身份分配源：同 ScopeId+EntityType 内序号单调递增（E1 原子自增）。</summary>
public interface IEntitySequenceSource
{
    EntityId New(string scopeId, string entityType);
}
