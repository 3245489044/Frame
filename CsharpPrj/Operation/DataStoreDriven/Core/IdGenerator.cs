using System.Threading;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 线程安全自增 id 生成器：收敛 <see cref="InMemoryNodeStore"/> / <see cref="InMemoryZoneStore"/> / <see cref="InMemorySnapshotGraphStore"/>
/// 重复的「lock 内自增」/ <c>NextId(ref seq)</c> 骨架（SingleImplementation），统一以 <see cref="Interlocked"/> 原子递增。
/// </summary>
internal static class IdGenerator
{
    public static long Next(ref long seq) => Interlocked.Increment(ref seq);
}
