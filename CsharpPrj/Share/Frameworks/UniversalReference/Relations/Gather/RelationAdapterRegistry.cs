namespace UniversalWorkflowFramework.Relations;

/// <summary>
/// 端点适配注册表：端点类型 → provision 委托（建端点镜像）。
/// <para>域差异只能是数据（注册一行），禁 <c>if (task/history/session)</c> 分支。</para>
/// </summary>
public sealed class RelationAdapterRegistry
{
    private readonly Dictionary<string, Func<string, CancellationToken, Task>> _provisions = new(StringComparer.Ordinal);

    /// <summary>注册端点镜像委托（同类型重复注册 ⇒ 抛：禁静默覆盖）。</summary>
    public void Register(string entityType, Func<string, CancellationToken, Task> provision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentNullException.ThrowIfNull(provision);
        if (!_provisions.TryAdd(entityType, provision))
            throw new InvalidOperationException($"端点 provision 重复注册：{entityType}（单一实现原则）");
    }

    /// <summary>取端点镜像委托（未注册 ⇒ null；调用方据此跳过并可见化）。</summary>
    public Func<string, CancellationToken, Task>? TryGet(string entityType)
        => !string.IsNullOrWhiteSpace(entityType) && _provisions.TryGetValue(entityType, out var found) ? found : null;

    /// <summary>已注册的端点类型数（结构计数，门禁判据）。</summary>
    public int Count => _provisions.Count;
}
