using System.Threading;
using Modules.KeySpace.Rules;

namespace Modules.KeySpace.Runtime;

/// <summary>
/// 键空间声明宿主（组合根注入；框架内只读）：供运行期契约投影与绑定消费。
/// <para>未注入 ⇒ null（调用方回落既有路径，零行为变化）。</para>
/// </summary>
public static class KeySpaceHost
{
    private static KeySpaceDeclaration? _declaration;

    public static KeySpaceDeclaration? Declaration => Volatile.Read(ref _declaration);

    public static void Configure(KeySpaceDeclaration? declaration)
        => Volatile.Write(ref _declaration, declaration);
}
