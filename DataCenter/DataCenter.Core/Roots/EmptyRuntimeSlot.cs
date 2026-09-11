using DataCenter.Protocol.Roots;

namespace Operation.StateBlock.Roots;

/// <summary>
/// 只读空槽分区宿主（默认实现）—— 供"未装配运行时槽"的宿主使用（如既有测试 / 单进程内嵌 Core）：
/// 取值一律未命中（<see cref="KeyNotFoundException"/>），注册一律 fail-fast（无槽可注册）。
/// 语义：槽是宿主装配层的产物，未装配 ≠ 静默容忍；但也不阻塞根实例化（槽缺席不影响状态面）。
/// </summary>
public sealed class EmptyRuntimeSlot : IRuntimeSlot
{
    /// <summary>共享只读实例（无状态）。</summary>
    public static IRuntimeSlot Instance { get; } = new EmptyRuntimeSlot();

    private EmptyRuntimeSlot()
    {
    }

    public IReadOnlyCollection<string> Domains => Array.Empty<string>();

    public IRuntimeSlotPartition Partition(string domainKey) => new EmptyPartition(domainKey ?? string.Empty);

    private sealed class EmptyPartition(string domainKey) : IRuntimeSlotPartition
    {
        public string DomainKey { get; } = domainKey;

        public IReadOnlyCollection<string> Keys => Array.Empty<string>();

        public void Register(string key, object value)
            => throw new InvalidOperationException(
                $"运行时槽未装配：无法向分区 '{DomainKey}' 注册 '{key}'（槽由宿主装配层创建并注入，禁止就地自建第二根）。");

        public bool TryGet<T>(string key, out T? value) where T : class
        {
            value = null;
            return false;
        }

        public T Get<T>(string key) where T : class
            => throw new KeyNotFoundException($"运行时槽未装配：分区 '{DomainKey}' 中不存在 '{key}'。");

        public object Get(string key)
            => throw new KeyNotFoundException($"运行时槽未装配：分区 '{DomainKey}' 中不存在 '{key}'。");
    }
}
