using System.Diagnostics.CodeAnalysis;
using DataCenter.Protocol.Roots;

namespace Operation.StateBlock.Roots;

/// <summary>
/// 取得口默认实现 —— 深层组件经本端口取根（替代静态槽）；<see cref="IRootProvider"/> 本身仍由组合根注入。
/// <see cref="Unbound"/> 为"未装配"语义：访问即 <see cref="RootMissingException"/>（fail-fast，
/// 绝不静默造根；权威: 设计文档 §12.4）。
/// </summary>
public sealed class RootProvider : IRootProvider
{
    private readonly IDataCenterRoot _root;

    public RootProvider(IDataCenterRoot root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
    }

    /// <summary>未装配的取得口（缺根 fail-fast；用于"忘了注入 provider"的装配错误显式暴露）。</summary>
    public static IRootProvider Unbound { get; } = new UnboundRootProvider();

    public IDataCenterRoot Root => _root;

    public bool TryGetRoot([NotNullWhen(true)] out IDataCenterRoot? root)
    {
        root = _root;
        return true;
    }

    private sealed class UnboundRootProvider : IRootProvider
    {
        public IDataCenterRoot Root => throw RootMissingException.Throw(nameof(RootProvider));

        public bool TryGetRoot([NotNullWhen(true)] out IDataCenterRoot? root)
        {
            root = null;
            return false;
        }
    }
}
