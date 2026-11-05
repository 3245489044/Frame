using System.Collections.Generic;

namespace PluginHostContract;

/// <summary>
/// 插件端口注册表（通用框架面）—— <b>插件对外能力的唯一登记处</b>。
/// <para>
/// 定位：替代散落的 <c>public static { get; set; }</c> ambient 槽。写入只允许发生在插件装配阶段
/// （<see cref="IPluginPortBinder.RegisterAsync"/> 内），消费者只经 <see cref="TryGet{T}"/> 读取。
/// </para>
/// <para>
/// 隔离语义（单插件失败不影响其他插件）：端口按 <c>ownerId</c> 归属，插件停机/换代时经
/// <see cref="Unbind"/> 精确回收自身端口，绝不触碰他人。ownerId 由装配方给出（= 插件 Id），
/// 不依赖执行者叙述（判据载体=结构事实）。
/// </para>
/// </summary>
public interface IPortRegistry
{
    /// <summary>登记端口（同一 (ownerId, key) 重复登记 = 覆盖，记一次冲突计数可见化）。</summary>
    void Bind(string ownerId, string key, object? value);

    /// <summary>按端口键取实现；未登记或类型不符 ⇒ false（调用方显式降级，不抛）。</summary>
    bool TryGet<T>(string key, out T? value) where T : class;

    /// <summary>该端口键是否已登记且值非 null。</summary>
    bool Has(string key);

    /// <summary>该 owner 当前登记的端口键集合（停机回收/诊断用）。</summary>
    IReadOnlyList<string> KeysOf(string ownerId);

    /// <summary>精确回收某 owner 的全部端口（插件 Stop/换代调用）；不影响其它 owner。</summary>
    int Unbind(string ownerId);

    /// <summary>端口键 → owner 的快照（可见化：谁登记了什么，结构事实）。</summary>
    IReadOnlyDictionary<string, string> OwnerSnapshot { get; }
}
