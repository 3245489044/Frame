using System.Collections.Generic;
using System.Linq;
using Operation.LoopFoldMethod.Node.Assemble;
using Operation.LoopFoldMethod.Node.Model;
using Share.Composition;

namespace Operation.LoopFoldMethod.Node.Project;

/// <summary>
/// 提示词域分层结果 —— 把通用节点装配投影为 LLM 可消费的「system(头) + 有序 user 列表」。
/// 稳定前缀缓存策略载体：Head（L0 基础，稳定）→ system；Body（动态步骤）→ 首条 user；Tail（skill，多变）→ 末条 user。
/// </summary>
/// <param name="System">系统提示（头部稳定前缀，如 L0 底座）。</param>
/// <param name="UserContents">有序 user 消息（中部动态 + 尾部技能；空串段被剔除）。</param>
public sealed record PromptLayeredResult(string System, IReadOnlyList<string> UserContents);

/// <summary>
/// 提示词域投影器 —— <see cref="INodeProjector{T}"/> 的提示词实现（纯函数，零 LLM / 零 AI 可测）。
/// 把 <see cref="NodeAssembly"/>（通用节点，非提示词强相关）投影为分层提示词：
/// <list type="bullet">
///   <item><b>System</b> = Head 段（稳定 L0）拼接 —— 置对话最前，内容稳定 → 长期命中 LLM 前缀缓存；</item>
///   <item><b>UserContents</b> = [Body(动态步骤) , Tail(skill)] —— skill 多变置末条，不打断稳定前缀。</item>
/// </list>
/// 相比旧「_systemPrompt + skillBlock」把 skill 拼在 system 头部的做法，此排布最大化前缀缓存命中。
/// 排布策略（槽位）来自 <see cref="CacheStableSlotPolicy"/>，本投影器只做「节点 → 分层消息」映射。
/// </summary>
public sealed class PromptNodeProjector : INodeProjector<PromptLayeredResult>
{
    /// <summary>共享实例（无状态纯函数）。</summary>
    public static readonly PromptNodeProjector Default = new();

    /// <inheritdoc />
    public PromptLayeredResult Project(NodeAssembly assembly)
    {
        var system = Join(assembly.Head);
        var users = new List<string>(2);
        AddIfNonEmpty(users, Join(assembly.Body));
        AddIfNonEmpty(users, Join(assembly.Tail));
        return new PromptLayeredResult(system, users);
    }

    /// <summary>便捷装配：节点集 → 分层提示词（默认 <see cref="CacheStableSlotPolicy"/> 稳定前缀策略）。</summary>
    public static PromptLayeredResult Build(IEnumerable<NodeItem>? nodes, INodeSlotPolicy? policy = null)
        => Default.Project(NodeBuilder.Build(nodes, policy));

    /// <summary>
    /// 工具契约尾槽：把本步可用工具块 <paramref name="toolBlock"/> 追加到分层结果的 <b>user 尾、skill 之后</b>，
    /// 使模型在同一处看到「skill 指南 + 工具契约」（对齐格式指令高注意区）。纯函数：
    /// 空/空串 <paramref name="toolBlock"/> → 原样返回（空槽自动剔除，零回归、system/user 逐字不变）。
    /// </summary>
    public static PromptLayeredResult AppendToolTail(PromptLayeredResult layered, string? toolBlock)
    {
        if (layered is null || string.IsNullOrWhiteSpace(toolBlock))
            return layered;

        var users = new List<string>(layered.UserContents.Count + 1);
        users.AddRange(layered.UserContents);
        users.Add(toolBlock);
        return layered with { UserContents = users };
    }

    private static void AddIfNonEmpty(List<string> users, string text)
    {
        if (!string.IsNullOrWhiteSpace(text)) users.Add(text);
    }

    /// <summary>
    /// 段内拼接 —— 非 null 载荷按序以空行连接，语义委托内核 <see cref="KernelJoins.JoinAll"/>。
    /// 保真说明：既有口径为 <c>string.Join("\n\n", …)</c>（<b>不过滤空段</b>，与 <see cref="KernelJoins.BlankLine"/> 不同），
    /// 故选用 <see cref="KernelJoins.JoinAll"/> 而非 <see cref="KernelJoins.BlankLine"/>，迁移前后逐字等价。
    /// </summary>
    private static string Join(IReadOnlyList<NodeItem> band)
        => KernelJoins.JoinAll(band.Where(n => n.Payload is not null).Select(n => n.Payload!.ToString()));
}
