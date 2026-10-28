using System.Text.Encodings.Web;
using System.Text.Json;

namespace Operation.HierarchyMethod.History.Conversation;

/// <summary>
/// 历史载荷 JSON 选项**单一来源**（DUP-3）：段载荷 / 消息载荷 / 入史载荷的序列化共用同一口径
/// （不转义非 ASCII，中文载荷可直读），消除多份私有 <c>JsonSerializerOptions</c> 副本的漂移面。
/// <para>消费方：切分器段载荷 / 追加服务追加载荷 / 入史消息载荷。</para>
/// </summary>
public static class HistoryPayloadJson
{
    /// <summary>载荷 JSON 选项（唯一实例；口径权威 = 本处）。</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
