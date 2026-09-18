using System.Text.Json;
using System.Text.Json.Serialization;
using Share.StateManagement;
using Operation.StateBlock.Layering;

namespace Operation.StateBlock.Decision;

/// <summary>
/// 决策中心门面 —— 轮决策记录（<see cref="DecisionRecord"/>）落 Center Session 层（scope=任务）+ 最近决策投影。
/// 决策与确定性核心同为状态的一等公民：可审计、可回放；订阅广播（StateSubscriptionHub / IPublishDispatcher）供后续
/// UI / ur_outputs 消费。语义权威仍 Share.TaskStatusPool.Loops（R7），本门面只做状态落地与投影。
/// 对应文档 2026-09-04 StateHoldDriven §3.3 DecisionCenter / §5.1（替代 lastDecision 闭包）。
/// </summary>
public sealed class DecisionCenter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        // 枚举以字符串落库 —— 与 JsonValueCodec「枚举取名字」及 TS 侧字符串枚举的跨语言契约一致
        // （默认数字形态会使 LoopDecision/EvalOutcome/ErrorClass 在跨语言消费侧不可读）。
        // JsonStringEnumConverter 反序列化仍接受数字，旧数字载荷可正常回放（向后兼容）。
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly StateLayerScheme _scheme;
    private readonly LayeredStoreClient _client;

    public DecisionCenter(IStateCenter center)
    {
        ArgumentNullException.ThrowIfNull(center);
        _scheme = LoopKeys.BuildScheme();
        _client = new LayeredStoreClient(center, _scheme);
    }

    /// <summary>写轮决策记录到 Center Session 层（scope=任务）。</summary>
    public void Record(string scope, DecisionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _client.Write(_scheme.Session(scope, LoopKeys.DecisionLatest), JsonSerializer.Serialize(record, Json));
    }

    /// <summary>读最近决策记录投影；无记录返回 null（替代 lastDecision 闭包）。</summary>
    public DecisionRecord? Recent(string scope)
    {
        var json = _client.Read<string>(_scheme.Session(scope, LoopKeys.DecisionLatest));
        if (string.IsNullOrWhiteSpace(json)) return null;
        // 载荷不可读（损坏 / 结构演进 / 类型不受支持）→ 视为「无记录」，不外溢异常（F-46 异常面收窄）
        try { return JsonSerializer.Deserialize<DecisionRecord>(json, Json); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException) { return null; }
    }
}
