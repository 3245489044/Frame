using System;

namespace Share.Composition.AssemblyFramework.DeclarationReload;

/// <summary>
/// 进程级声明缓存（<see cref="Lazy{T}"/> 单次装载 + 失效重建），并在构造时**自我登记**进
/// <see cref="DeclarationReloadRegistry"/> —— 消灭「新增装载器忘记登记 ⇒ 热装载静默失效」的漂移面。
/// <para>
/// 原形态在 7 个装载器内逐份手写（<c>private static Lazy&lt;T&gt; _loaded = new(Load, isThreadSafe: true)</c>
/// + <c>public static void Reset() =&gt; _loaded = new Lazy&lt;T&gt;(Load, isThreadSafe: true)</c>），
/// 且 <c>Reset</c> 需在 <c>*DeclarationReloadRegistrations</c> 里逐条手工登记；模板与登记各有一份漂移面。
/// </para>
/// <para>
/// 口径：每个装载器**一个静态实例**（同名重复构造会被注册表按名忽略，第二个实例的 <see cref="Reset"/>
/// 将不会生效），名称取 <c>nameof(装载器类型)</c> 以保持既有登记名不变（诊断清单/门禁按名消费）。
/// </para>
/// <para>
/// 时机纪律：登记发生在实例构造时（= 宿主类型静态构造，由组合根装配相位显式触达后发生），
/// 写入目标是进程级 <see cref="DeclarationReloadRegistry.Default"/>（非承载容器），故不违反
/// 「禁加载期写承载容器」；见 <c>Agent.Core.Nodes.DeclarationReloadRegistrations</c> 的相位说明。
/// </para>
/// </summary>
public sealed class DeclarationCache<T>
{
    private readonly string _name;
    private readonly Func<T> _loader;
    private readonly Action? _onReset;
    private Lazy<T> _slot;

    /// <summary>建立一个进程级声明缓存并登记其失效委托（幂等：同名只保留首个）。</summary>
    /// <param name="name">参与者名（注册表去重键；取 <c>nameof(装载器类型)</c>）。</param>
    /// <param name="loader">装载委托（进程级缓存，热路径零编译）。</param>
    /// <param name="onReset">失效副作用（可选；如清理与缓存同源的派生表）。</param>
    public DeclarationCache(string name, Func<T> loader, Action? onReset = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("声明缓存参与者名不得为空。", nameof(name));
        _name = name.Trim();
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _onReset = onReset;
        _slot = new Lazy<T>(_loader, isThreadSafe: true);
        DeclarationReloadRegistry.Default.Register(_name, Reset);
    }

    /// <summary>参与者名（注册表去重键）。</summary>
    public string Name => _name;

    /// <summary>当前生效值（首次取用触发装载；失效后下次取用重新装载）。</summary>
    public T Value => _slot.Value;

    /// <summary>失效进程级缓存（热装载：重建 <see cref="Lazy{T}"/>，并执行失效副作用）。</summary>
    public void Reset()
    {
        _slot = new Lazy<T>(_loader, isThreadSafe: true);
        _onReset?.Invoke();
    }
}
