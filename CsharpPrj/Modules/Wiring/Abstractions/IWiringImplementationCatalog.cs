namespace Modules.Wiring.Abstractions;

/// <summary>
/// 实现侧事实（端口漂移 D7 的唯一数据来源）。
/// <para>上层适配既有 <c>Modules.Declaration.ImplementationCatalog</c> 或函数注册表；框架只读。</para>
/// </summary>
public interface IWiringImplementationCatalog
{
    bool IsRegistered(string function);

    bool TryGetProduced(string function, out IReadOnlyList<string> produced);
}

/// <summary>空实现：无实现事实时 D7 跳过（不伪造，也不静默判绿——门禁侧另行可见化）。</summary>
public sealed class NullWiringImplementationCatalog : IWiringImplementationCatalog
{
    public static readonly NullWiringImplementationCatalog Instance = new();

    public bool IsRegistered(string function) => false;

    public bool TryGetProduced(string function, out IReadOnlyList<string> produced)
    {
        produced = Array.Empty<string>();
        return false;
    }
}
