#nullable enable

using System;
using System.Collections.Generic;

namespace Modules.Gather;

/// <summary>
/// 聚拢点登记（**装配聚拢的单行事实**）：一个可独立装配/观测/回退的功能切片在 Gather 上的投影。
/// <para>
/// 与 <see cref="GatherViewSpec"/> 的关系：视图声明「要什么、按什么顺序」；聚拢点声明「谁来实现、何时装配」。
/// 视图语义不变（<c>Config\gather\gather-views.ushar</c> 仍是唯一真源），本类型只承载装配映射。
/// </para>
/// <para>
/// 工厂委托（而非反射 <c>Type</c>）：模块构造期依赖（服务/上下文/载体）在编译期可见，
/// 避免 <c>ActivatorUtilities</c> 把依赖关系藏到运行期。
/// </para>
/// </summary>
/// <param name="PointId">稳定身份（<c>&lt;owner&gt;.&lt;feature&gt;</c>，如 <c>ui.conversation-canon</c>）。</param>
/// <param name="Views">该点负责装配的视图键（1..N；载体族可一次登记多个派生视图）。</param>
/// <param name="Owner">归属工程（依赖方向门禁归因；当前恒 <c>Agent.Ui</c>）。</param>
/// <param name="GateKey">装配门控键（<c>null</c> = 常开；须在宿主开关表内登记）。</param>
/// <param name="Create">装配工厂（<see cref="object"/> = 宿主上下文，由工厂自行解释；<c>null</c> = 仅声明/校验用）。</param>
public sealed record GatherPointRegistration(
    string PointId,
    IReadOnlyList<string> Views,
    string Owner,
    string? GateKey,
    Func<GatherViewSpec, object?, IGatherModule>? Create)
{
    /// <summary>门控是否放行（<c>GateKey</c> 为空 ⇒ 常开；无门控谓词 ⇒ 视为放行）。</summary>
    /// <param name="isGateEnabled">门控键 → 是否启用（宿主注入）。</param>
    public bool IsGateOpen(Func<string, bool>? isGateEnabled)
        => string.IsNullOrWhiteSpace(GateKey) || (isGateEnabled?.Invoke(GateKey!) ?? true);
}

/// <summary>聚拢点目录问题（装配期判据；空 = 闭合）。</summary>
/// <param name="Code">规则号（GP1–GP6，与门禁脚本同一口径）。</param>
/// <param name="PointId">问题归属点（目录级问题用 <c>*</c>）。</param>
/// <param name="Detail">判据正文（禁仅给「失败」）。</param>
public sealed record GatherPointIssue(string Code, string PointId, string Detail);

/// <summary>一次聚拢点装配结果（点 + 已解析视图 + 已构造模块；三件同源，禁分别再查）。</summary>
public sealed record GatherPointAssembly(
    GatherPointRegistration Point,
    GatherViewSpec View,
    IGatherModule Module);
