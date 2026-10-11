using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node;
using Modules.Node.Placement;

namespace Modules.Node.Stream;

/// <summary>
/// 单行 Stream 域的**既有载荷读取 + 跨轮合并**（唯一复用点）：读该域 Stream 位置的既有行，经
/// <see cref="StreamPayloadMerge"/> 与本次载荷按轮合并（同轮覆盖）。运行期结算（<c>NodeDomainPort.CommitRunAsync</c>）
/// 与 standalone 追加（命令面）共用，避免两处各写一套合并（SingleImplementation）。
/// </summary>
public static class StreamPersistedMerge
{
    /// <summary>读既有行并与本次载荷合并；无既有行 / 读不可用 / 无 Stream 位置 ⇒ 本次载荷原样返回。</summary>
    /// <param name="framework">该域节点框架（提供位置/载体/编解码）。</param>
    /// <param name="positionKey">Stream 位置键（此行键）。</param>
    /// <param name="incomingJson">本次载荷 JSON。</param>
    /// <param name="ct">取消令牌。</param>
    public static async Task<string> MergeWithExistingAsync(
        NodeFramework? framework, string positionKey, string incomingJson, CancellationToken ct = default)
    {
        if (framework?.Placements is null || framework.Carriers is null)
        {
            return incomingJson;
        }

        foreach (var placement in framework.Placements.All)
        {
            if (!placement.IsReadable || placement.Form != PlacementForm.Stream)
            {
                continue;
            }

            if (!framework.Carriers.TryGet(placement.CarrierId, out var carrier) || carrier is null)
            {
                continue;
            }

            var record = await carrier.ReadAsync(positionKey, ct).ConfigureAwait(false);
            if (record is null)
            {
                continue;
            }

            var existing = framework.FormCodec.Decode(placement.Form, record.Json);
            return StreamPayloadMerge.Merge(existing, incomingJson, StreamPayloadSchema.Default);
        }

        return incomingJson;
    }
}
