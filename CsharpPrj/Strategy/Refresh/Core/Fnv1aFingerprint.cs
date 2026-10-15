using System.Text;

using Strategy.Refresh.Ports;

namespace Strategy.Refresh.Core
{
    /// <summary>
    /// <see cref="IFingerprint"/> 的简单默认实现：FNV-1a 64 位（对 UTF-8 字节）。
    /// <para>刻意不用 <c>string.GetHashCode</c>——后者随进程随机化、跨进程不稳定，无法作为可复算去重指纹。</para>
    /// </summary>
    public sealed class Fnv1aFingerprint : IFingerprint
    {
        /// <summary>指纹单源实例（无状态、进程内可安全共享）——全库**唯一** `new` 处；
        /// 领域与写落地一律引用本实例，消除多处独立实现/实例。</summary>
        public static readonly IFingerprint Default = new Fnv1aFingerprint();

        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        /// <inheritdoc />
        public string Of(string canonical)
        {
            var bytes = Encoding.UTF8.GetBytes(canonical ?? string.Empty);
            var hash = OffsetBasis;
            for (var i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= Prime;
            }
            return hash.ToString("x16");
        }
    }
}

