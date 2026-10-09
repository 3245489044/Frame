using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Operation.DataStoreDriven.Graph;

/// <summary>
/// 图库（LiteDB <c>graph.db</c>）**单实例 owner 注册表**（跨代共享）—— 落点 = 默认 ALC 常驻程序集
/// <c>Operation.DataStoreDriven</c>（扩展 ALC 经 <c>ExtensionLoadContext.Load</c> 复用默认副本 ⇒ 本静态跨代同一）。
/// <para>
/// 语义：同一图库绝对路径至多一个 <see cref="ReferenceGraphCoordinator"/>（LiteDB 单写者，二次打开即文件锁）。
/// <b>换代</b>（可换扩展 ALC 重建）时新代**复用**既有协调器，禁跨代重开 / 禁跨代 dispose。
/// </para>
/// <para>生命周期：进程级（跨代不释放）；随进程退出或 <see cref="DisposeAll"/> 回收。消费方（Agent 组合根）
/// 不得把共享实例登记进单代处置链。</para>
/// </summary>
public static class ReferenceGraphStoreRegistry
{
    private static readonly ConcurrentDictionary<string, ReferenceGraphCoordinator> Items =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly object Gate = new();

    /// <summary>取或创建 path 对应的图库协调器（同一路径至多一个 owner；已释放实例不复用）。</summary>
    public static ReferenceGraphCoordinator GetOrCreate(string path, Func<string, ReferenceGraphCoordinator> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(factory);

        lock (Gate)
        {
            if (Items.TryGetValue(path, out var existing) && !existing.IsDisposed)
                return existing;

            var created = factory(path) ?? throw new InvalidOperationException("图库工厂返回 null。");
            Items[path] = created;
            return created;
        }
    }

    /// <summary>已登记的图库路径（诊断）。</summary>
    public static IReadOnlyCollection<string> Paths => Items.Keys.ToArray();

    /// <summary>进程停机释放全部图库（幂等；释放失败不抛）。</summary>
    public static void DisposeAll()
    {
        foreach (var key in Items.Keys.ToArray())
        {
            if (Items.TryRemove(key, out var coordinator))
            {
                try { coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                catch { /* 停机不抛 */ }
            }
        }
    }
}
