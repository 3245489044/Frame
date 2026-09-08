namespace Strategy.Observability.Entity;

/// <summary>实体生命周期阶段（E1）：Created / Active / Completed / Failed。</summary>
public enum EntityLifecyclePhase
{
    Created,
    Active,
    Completed,
    Failed,
}

/// <summary>可观测实体契约：应用语义实体可选实现以携带身份与生命周期；内核仅读，不解释含义。</summary>
public interface IObservableEntity
{
    EntityId EntityId { get; }

    EntityLifecyclePhase Lifecycle { get; }
}
