using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Graph;

namespace Operation.DataStoreDriven.Abstractions
{
    /// <summary>重新同步结果：清除的墓碑数 + seed 新增声明数（可见化「会恢复用户已删关系」）。</summary>
    public sealed record GraphResyncResult(int ClearedTombstones, int SeededDeclarations);

    /// <summary>
    /// 图库维护端口（seed / 重建 / 墓碑管理）：仅宿主维护命令消费；常规读写走协调器与守卫。
    /// </summary>
    public interface IGraphMaintenanceStore
    {
        /// <summary>按已注入的声明源 seed（不清墓碑）。返回新增声明数。</summary>
        Task<int> SeedAsync(CancellationToken ct = default);

        /// <summary>清除墓碑后重新 seed（**会恢复配置中仍存在、但用户已删除的关系**）。</summary>
        Task<GraphResyncResult> ResyncAsync(CancellationToken ct = default);

        /// <summary>列举墓碑（kind 为空 ⇒ 全部）。</summary>
        Task<IReadOnlyList<GraphTombstoneDoc>> ListTombstonesAsync(string? kind = null, CancellationToken ct = default);

        /// <summary>清除墓碑（kind 为空 ⇒ 全部）；返回清除条数。</summary>
        Task<int> ClearTombstonesAsync(string? kind = null, CancellationToken ct = default);
    }
}
