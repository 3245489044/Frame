using System;
using System.Collections.Concurrent;

namespace Modules.Threading
{
    /// <summary>
    /// 进程级「单一确定性写者」注册/断言（原 Operation.HierarchyMethod.Center.SingleWriteGuard 的通用归属）。
    /// 确定性核心写目标只允许经唯一注册的写路径进入；平行实现/越权写在此 fail-fast（StateBus 治理 P1）。
    /// 同一写目标至多一个确定性写路径；注册一次后，第二个不同写者尝试注册即抛错。
    /// </summary>
    public static class SingleWriterRegistry
    {
        private static readonly ConcurrentDictionary<string, string> Owners = new(StringComparer.Ordinal);

        /// <summary>注册某写目标的唯一写者。已有不同写者 → 抛 <see cref="InvalidOperationException"/>（越权写 fail-fast，不静默分叉）。</summary>
        public static void AssertSingleWriter(string target, string writer)
        {
            if (string.IsNullOrWhiteSpace(target)) throw new ArgumentNullException(nameof(target));
            if (string.IsNullOrWhiteSpace(writer)) throw new ArgumentNullException(nameof(writer));
            var current = Owners.GetOrAdd(target, writer);
            if (!string.Equals(current, writer, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"单一写者违反: 写目标 '{target}' 已注册写者 '{current}'，'{writer}' 尝试平行注册（确定性核心仅单一确定性写入；如为合法迁移请先 Release 旧写者）");
            }
        }

        /// <summary>释放写目标（供测试重置 / 组合根重建）。仅注册写者可释放；未注册/非本写者 no-op。</summary>
        public static void Release(string target, string writer)
        {
            if (Owners.TryGetValue(target, out var current) &&
                string.Equals(current, writer, StringComparison.Ordinal))
            {
                Owners.TryRemove(target, out _);
            }
        }

        /// <summary>当前注册的写者快照（诊断/测试）。</summary>
        public static IReadOnlyDictionary<string, string> Snapshot() => new Dictionary<string, string>(Owners);
    }
}
