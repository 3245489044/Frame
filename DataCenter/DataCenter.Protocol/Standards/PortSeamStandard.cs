namespace DataCenter.Protocol.Standards;

/// <summary>
/// 端口**实现注入（seam）**声明（F2-d 一期纯契约）：把「声明面无法表达、必须由宿主/测试侧注入实现」的
/// 端口逐条登记为**一等公民**——有名字、有实参、有理由，可校验、可登记、可判额度。
/// <para>
/// 分工（D-2 裁决）：**契约入标准层**（本件，纯契约零执行）；**实现入执行面**
/// （<c>Strategy.Observability.Ports</c> 的 provider 与注册表）；宿主/测试侧只实现 provider 并提交 seam 表。
/// </para>
/// <para>
/// 纪律：seam **不是垃圾桶** —— 每条须有 <see cref="Reason"/>（缺理由即 <c>C15</c> 违规）；
/// 额度冻结（seam 条目数只减不增），未声明 seam 的端口一律走声明面（scripted / composed / reflective）。
/// </para>
/// </summary>
/// <param name="Seam">seam 名（不透明名；由执行面注册表解析，未注册即 fail-fast）。</param>
/// <param name="Args">seam 实参声明（原样 JSON：由 provider 自行解释，标准层不解释内部结构）。</param>
/// <param name="Reason">保留理由（必需：说明为何声明面无法表达；缺即违规）。</param>
public sealed record PortSeamStandard(
    string Seam,
    IReadOnlyList<System.Text.Json.JsonElement>? Args = null,
    string? Reason = null);
