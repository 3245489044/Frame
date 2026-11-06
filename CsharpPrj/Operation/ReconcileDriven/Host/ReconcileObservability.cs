using Strategy.Observability.Registry;

namespace Operation.ReconcileDriven.Host;

/// <summary>
/// 对账语义观测组合根：装配本模块的语义契约（reconcile_semantics.json，嵌入资源）进通用内核。
/// 机制与语义分离：本类只注入语义实例，不改内核（Strategy.Observability）。
/// </summary>
public static class ReconcileObservability
{
    /// <summary>加载本模块语义契约（嵌入资源；可覆盖为外部实例）。</summary>
    public static IReadOnlyList<EventContract> LoadContracts(string? semanticsJson = null)
        => EmbeddedSemanticsContractLoader.Load(
            typeof(ReconcileObservability).Assembly, ResourceSuffix, semanticsJson);

    /// <summary>把本模块语义契约并入既有注册表（Agent 同进程时经此合并；重复注册静默忽略）。</summary>
    public static void RegisterInto(IObservabilityContractRegistry registry, string? semanticsJson = null)
        => EmbeddedSemanticsContractLoader.RegisterInto(
            registry, typeof(ReconcileObservability).Assembly, ResourceSuffix, semanticsJson);

    /// <summary>构造本模块语义词表（含对账事件 + 可选外部契约，如 Agent 语义）。</summary>
    public static Gates.ReconcileVocabulary BuildVocabulary(IEnumerable<EventContract>? extraContracts = null)
    {
        var kinds = new List<string>(LoadContracts().Select(c => c.EventKind));
        if (extraContracts != null)
            kinds.AddRange(extraContracts.Select(c => c.EventKind));
        return new Gates.ReconcileVocabulary(kinds);
    }

    /// <summary>读取嵌入的对账语义契约 JSON。</summary>
    public static string ReadEmbeddedSemantics()
        => EmbeddedSemanticsContractLoader.ReadEmbedded(
            typeof(ReconcileObservability).Assembly, ResourceSuffix);

    private const string ResourceSuffix = "reconcile_semantics.json";
}
