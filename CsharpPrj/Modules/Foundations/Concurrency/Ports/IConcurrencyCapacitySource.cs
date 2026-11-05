namespace Modules.Concurrency;

/// <summary>
/// 并发容量来源（中性形态）：宿主任务池适配实现。
/// <para>声明上限 + 实时可用 slot；门禁与池读同一实例，禁止两处配置。</para>
/// </summary>
public interface IConcurrencyCapacitySource
{
    /// <summary>声明上限。</summary>
    int MaxConcurrency { get; }

    /// <summary>实时可用 slot。</summary>
    int AvailableSlots { get; }
}
