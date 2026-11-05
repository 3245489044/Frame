namespace Modules.Registry.AutoRegister;

/// <summary>
/// <see cref="IAssemblySourceProvider"/> 的运行时定位器 / 默认空实现。
/// 组合根可经 <see cref="Set"/> 注入一次带布局根的实例（如由 Route 布局 catalog 派生后包装实现）；
/// 未注入时恒空（<see cref="Default"/>），让 AutoRegister/各 *Register 库在默认环境下行为不变。
/// </summary>
public static class AssemblySourceProvider
{
    private static readonly IAssemblySourceProvider _default = new EmptyProvider();
    private static readonly object _gate = new();
    private static IAssemblySourceProvider _current = _default;

    /// <summary>空实现（无注入根，退化为运行目录 + 当前目录旧语义）。注：用 readonly 单例字段承载，
    /// 且置于 <see cref="_current"/> 之前初始化，避免 C# 静态成员按文本序初始化导致 "Default 读取时尚未赋值 → null" 的 NRE。</summary>
    public static IAssemblySourceProvider Default => _default;

    /// <summary>当前生效的提供器（默认空）。</summary>
    public static IAssemblySourceProvider Current
    {
        get { lock (_gate) return _current; }
    }

    /// <summary>注入提供器（幂等安全；组合根在启动装配早期调用一次）。传 <c>null</c> 恢复默认空。</summary>
    public static void Set(IAssemblySourceProvider? provider)
    {
        lock (_gate)
        {
            _current = provider ?? Default;
        }
    }

    private sealed class EmptyProvider : IAssemblySourceProvider
    {
        public IReadOnlyList<string> AssemblySearchDirectories => Array.Empty<string>();
        public IReadOnlyList<string> LayoutRoots => Array.Empty<string>();
    }
}
