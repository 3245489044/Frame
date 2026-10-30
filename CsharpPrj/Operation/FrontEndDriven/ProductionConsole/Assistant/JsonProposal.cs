using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FrontEndDriven.ProductionConsole.Assistant;

/// <summary>
/// JSON 助理提案请求（目标无关）：目标 id + 目标 schema 文本 + 当前 JSON + 用户描述。
/// <para>Specs 非空 = 表单能力模式（按参数声明校验/归一）；TemplateJson 非空 = JSON 模板模式（按模板键与示例类型校验）。</para>
/// </summary>
public sealed record JsonProposalRequest
{
    /// <summary>目标 id（能力 id / 模板 id），仅用于状态绑定与提示词展示。</summary>
    public string TargetId { get; init; } = "";

    /// <summary>用户自然语言描述（唯一业务输入）。</summary>
    public string Prompt { get; init; } = "";

    /// <summary>目标 schema 文本（<see cref="JsonSchemaText"/> 产物，直接进提示词）。</summary>
    public string SchemaText { get; init; } = "";

    /// <summary>表单能力参数声明（非空 = 按 ParamSpec 校验并归一）。</summary>
    public IReadOnlyList<ParamSpec>? Specs { get; init; }

    /// <summary>JSON 模板骨架原文（非空 = 按模板顶层键/示例类型校验）。</summary>
    public string? TemplateJson { get; init; }

    /// <summary>当前编辑器/表单 JSON（提案在其上合并，未建议键保持原值）。</summary>
    public string? CurrentJson { get; init; }

    /// <summary>上次执行输出（可选；供单步修正，不自动循环）。</summary>
    public string? LastOutput { get; init; }
}

/// <summary>提案结果：单个 JSON 对象文本（补丁或完整参数对象）；来源返回 null = 不可用。</summary>
public sealed class JsonProposal
{
    public string Json { get; init; } = "";

    /// <summary>可选备注（来源侧诊断，渲染层可忽略）。</summary>
    public string? Note { get; init; }
}

/// <summary>
/// JSON 提案来源端口 —— 框架不关心「怎么得到 JSON」（Agent 按名调用 / 本地规则 / 测试桩均可），
/// 只消费一个 JSON 对象文本。返回 null 或空文本 = 不可用（由助理折叠为可见错误，不得伪装成功）。
/// </summary>
public interface IJsonProposalSource
{
    Task<JsonProposal?> ProposeAsync(JsonProposalRequest request, CancellationToken ct = default);
}
