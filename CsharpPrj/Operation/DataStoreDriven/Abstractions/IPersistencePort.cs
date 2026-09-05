using System.Threading;
using System.Threading.Tasks;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 持久化端口（框架决定落库的抽象）：在循环边界把某 session 的内存快照序列化到长期库，或从长期库重放重建。
/// Agent 只消费此抽象；落库时机由框架（DataStoreEngine）在 commit / checkpoint 边界决定，AI 不直接触发数据库写入。
/// </summary>
public interface IPersistencePort
{
    Task PersistAsync(string sessionId, string snapshotJson, CancellationToken ct = default);

    Task<string?> LoadAsync(string sessionId, CancellationToken ct = default);
}
