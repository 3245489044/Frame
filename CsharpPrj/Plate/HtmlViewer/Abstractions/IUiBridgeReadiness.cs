#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace UiBackend.Abstractions;

/// <summary>
/// 桥就绪查询端口（L1 通用契约）——通道实现之，供写协调器/宿主判断「文档级行为模块是否全就绪」。
/// 后端无关：HTML 由 <c>document.__bridgeModules</c> 承载，其它后端可各自映射。
/// </summary>
public interface IUiBridgeReadiness
{
    /// <summary>查询当前活 document 的桥模块就绪状态（模块位 + 版本）。</summary>
    Task<UiBridgeModuleState> GetBridgeModulesAsync(CancellationToken ct = default);
}
