namespace ContractFramework.Abstractions;

/// <summary>
/// 契约执行上下文（载体集合 / 提供者 / 模式）。执行体经此取得判据载体（结构事实）。
/// </summary>
public interface IContractContext
{
    /// <summary>工作目录（相对路径解析基准；<c>null</c> = 当前目录）。</summary>
    string? WorkingDirectory { get; }

    /// <summary>来源标签（诊断用：本次装配/探测的对象名）。</summary>
    string? SourceLabel { get; }

    /// <summary>按类型取提供者（如注册表 / 模板集 / 文件缓存）。</summary>
    TProvider? GetProvider<TProvider>() where TProvider : class;

    /// <summary>按类型取提供者（非泛型）。</summary>
    object? GetProvider(Type providerType);

    /// <summary>按键取结构事实（如"已声明属性名集合"）。</summary>
    object? GetFact(string key);

    /// <summary>按键取结构事实（类型化）。</summary>
    bool TryGetFact<T>(string key, out T? value);
}

/// <summary>
/// <see cref="IContractContext"/> 默认实现（提供者注册 + 结构事实字典）。
/// </summary>
public sealed class ContractContext : IContractContext
{
    private readonly Dictionary<Type, object> _providers = [];
    private readonly Dictionary<string, object?> _facts = new(StringComparer.Ordinal);

    public ContractContext(string? workingDirectory = null, string? sourceLabel = null)
    {
        WorkingDirectory = workingDirectory;
        SourceLabel = sourceLabel;
    }

    public string? WorkingDirectory { get; }

    public string? SourceLabel { get; }

    /// <summary>注册提供者（链式）。</summary>
    public ContractContext WithProvider<TProvider>(TProvider provider) where TProvider : class
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers[typeof(TProvider)] = provider;
        return this;
    }

    /// <summary>注册结构事实（链式）。</summary>
    public ContractContext WithFact(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _facts[key] = value;
        return this;
    }

    public TProvider? GetProvider<TProvider>() where TProvider : class
        => _providers.TryGetValue(typeof(TProvider), out var value) ? value as TProvider : null;

    public object? GetProvider(Type providerType)
        => providerType is not null && _providers.TryGetValue(providerType, out var value) ? value : null;

    public object? GetFact(string key)
        => key is not null && _facts.TryGetValue(key, out var value) ? value : null;

    public bool TryGetFact<T>(string key, out T? value)
    {
        if (key is not null && _facts.TryGetValue(key, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }
}
