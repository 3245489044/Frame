using System.Threading;
using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Rules;

namespace Modules.KeySpace.Runtime;

/// <summary>
/// 坐标解析宿主（组合根注入；框架内只读）。
/// <para>
/// 未注入 ⇒ <see cref="Current"/> = null：消费方须 fail-fast（或经显式开关走 legacy），**禁静默回退 raw**。
/// </para>
/// </summary>
public static class CoordinateHost
{
    private static ICoordinateResolver? _resolver;

    /// <summary>已装配的解析器（未装配 ⇒ null）。</summary>
    public static ICoordinateResolver? Current => Volatile.Read(ref _resolver);

    /// <summary>以声明构造并装配（重复调用覆盖；幂等）。</summary>
    public static ICoordinateResolver Configure(KeySpaceDeclaration declaration)
    {
        var resolver = new CoordinateResolver(declaration);
        Volatile.Write(ref _resolver, resolver);
        return resolver;
    }

    /// <summary>显式清空（测试/回退用；生产不调用）。</summary>
    public static void Reset() => Volatile.Write(ref _resolver, null);
}
