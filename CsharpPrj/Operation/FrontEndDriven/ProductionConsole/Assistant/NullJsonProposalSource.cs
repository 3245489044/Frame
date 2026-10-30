using System.Threading;
using System.Threading.Tasks;

namespace FrontEndDriven.ProductionConsole.Assistant;

/// <summary>
/// 空提案来源（框架默认）：宿主未装配领域来源时**确定性返回 null**（不静默编造参数）。
/// <para>领域来源（如 Agent 单步对话）由插件实现 <see cref="IJsonProposalSource"/> 并
/// 经 <see cref="JsonAssistRuntime.Configure"/> 装配；框架层不持有任何 Agent 函数名/提示词/会话名。</para>
/// </summary>
public sealed class NullJsonProposalSource : IJsonProposalSource
{
    public static readonly NullJsonProposalSource Instance = new();

    public Task<JsonProposal?> ProposeAsync(JsonProposalRequest request, CancellationToken ct = default)
        => Task.FromResult<JsonProposal?>(null);
}

/// <summary>
/// JSON 助理运行时槽（宿主装配期注入来源；未注入时为空来源 ⇒ 不产出提案）。
/// <para>唯一可变状态 = 来源与助理实例，宿主装配一次。</para>
/// </summary>
public static class JsonAssistRuntime
{
    private static readonly object Gate = new();
    private static IJsonProposalSource? _source;
    private static JsonStepAssistant? _assistant;

    /// <summary>宿主装配期覆盖来源（null = 回到空来源）。</summary>
    public static void Configure(IJsonProposalSource? source)
    {
        lock (Gate)
        {
            _source = source;
            _assistant = null;
        }
    }

    /// <summary>当前助理实例（缺省空来源惰性创建；测试可直接 Configure 注入桩）。</summary>
    public static JsonStepAssistant Assistant
    {
        get
        {
            lock (Gate)
            {
                _source ??= NullJsonProposalSource.Instance;
                return _assistant ??= new JsonStepAssistant(_source);
            }
        }
    }
}