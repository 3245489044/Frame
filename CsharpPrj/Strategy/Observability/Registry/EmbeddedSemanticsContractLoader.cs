using System.Reflection;

namespace Strategy.Observability.Registry;

/// <summary>
/// 嵌入语义契约装载装配器：把「程序集 + 资源后缀」收敛为一条装配路径，
/// 解析与注册全部复用 <see cref="ObservabilitySemanticsJson"/> / <see cref="IObservabilityContractRegistry"/>。
/// <para>语义实例（<c>*_semantics.json</c>）仍随各 Operation 工程走；本类不含任何具体语义。</para>
/// </summary>
public static class EmbeddedSemanticsContractLoader
{
    /// <summary>加载嵌入语义契约（可用外部 JSON 覆盖）。</summary>
    /// <param name="assembly">承载嵌入资源的程序集。</param>
    /// <param name="resourceSuffix">资源名后缀（如 <c>reconcile_semantics.json</c>）。</param>
    /// <param name="semanticsJson">外部覆盖 JSON；null 时读取嵌入资源。</param>
    public static IReadOnlyList<EventContract> Load(Assembly assembly, string resourceSuffix, string? semanticsJson = null)
        => ObservabilitySemanticsJson.Load(semanticsJson ?? ReadEmbedded(assembly, resourceSuffix));

    /// <summary>把嵌入语义契约并入既有注册表（重复注册交由注册表语义处理）。</summary>
    public static void RegisterInto(
        IObservabilityContractRegistry registry,
        Assembly assembly,
        string resourceSuffix,
        string? semanticsJson = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        foreach (var contract in Load(assembly, resourceSuffix, semanticsJson))
            registry.Register(contract);
    }

    /// <summary>读取程序集中以指定后缀结尾的唯一嵌入资源文本；缺失即失败（不静默返回空）。</summary>
    public static string ReadEmbedded(Assembly assembly, string resourceSuffix)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceSuffix);

        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"程序集 {assembly.GetName().Name} 缺少嵌入资源（后缀 {resourceSuffix}）");

        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"无法打开嵌入资源 {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
