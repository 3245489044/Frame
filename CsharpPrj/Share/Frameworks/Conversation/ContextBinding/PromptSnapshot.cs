using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Share.Composition;

namespace Share.Conversation.ContextBinding;

/// <summary>PromptSnapshot 的持久化 DTO（仅存储段字段；Bindings/System 为派生，反序列化时经 Builder 重建）。</summary>
internal sealed record PromptSnapshotData(
    string L0, string SkillBlock, string L1, string L2, IReadOnlyList<string> SelectedSkills,
    string? ToolBlock = null, IReadOnlyList<string>? SelectedTools = null, string? Mode = null,
    IReadOnlyList<PromptBand>? L0Bands = null, string? SubAgent = null, string? ResourceBlock = null,
    string? TaskRuntime = null,
    int SchemaVersion = PromptSnapshot.CurrentSchemaVersion);

/// <summary>
/// 写侧快照 —— provide_prompt 装配后捕获的真实 system 值（L0 / 填槽 L1 / L2 / 本轮选中 SkillBlock），
/// 供 UI 头提示层与 LLM system 共用同一份（消除「UI 用模板/重算近似」的 G1 缺口，满足 I3 哈希一致）。
/// 落 state.Data["__prompt_snapshot"]（按轮滚动，仅存本轮，防无限增长）。
/// </summary>
public sealed class PromptSnapshot
{
    /// <summary>当前快照 schema 版本（改字段必须先升版本 + 保 Legacy 读取路径）。</summary>
    public const int CurrentSchemaVersion = 3;

    /// <summary>快照 schema 版本（旧归档 &lt; 当前 ⇒ <see cref="IsLegacy"/>）。</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>是否旧版本归档（按 Legacy 读取路径解释，UI 可见化时必须标注）。</summary>
    public bool IsLegacy => SchemaVersion < CurrentSchemaVersion;

    public string L0 { get; init; } = string.Empty;
    public string SkillBlock { get; init; } = string.Empty;
    public string L1 { get; init; } = string.Empty;
    public string L2 { get; init; } = string.Empty;
    public IReadOnlyList<string> SelectedSkills { get; init; } = Array.Empty<string>();

    /// <summary>L0 分段（按 DSL 承载技能/文件拆分，保持 band 原序）：技能名 + 正文；
    /// 供 UI 头提示层逐段渲染（一文件一气泡）。空 = 未分段（回退单一 L0；UI 显示一条 prompt:l0）。</summary>
    public IReadOnlyList<PromptBand> L0Bands { get; init; } = Array.Empty<PromptBand>();

    /// <summary>工具面（MCP）契约块 —— 本步可用工具（工具名 + 描述）文本；空 = 无工具面段（头提示层跳过）。</summary>
    public string ToolBlock { get; init; } = string.Empty;

    /// <summary>工具面工具名集（与 <see cref="ToolBlock"/> 同源，供比对/诊断）。</summary>
    public IReadOnlyList<string> SelectedTools { get; init; } = Array.Empty<string>();

    /// <summary>模式声明段（注入提示词装配声明；如 <c>reply</c>/<c>task.create</c>）——
    /// History 状态响应智能体（HSRA）读此段决定本轮行为集合（零重算，UI ≡ LLM）；空 = 未声明（→ 缺省模式）。</summary>
    public string Mode { get; init; } = string.Empty;

    /// <summary>子智能体标签声明段（与 <see cref="Mode"/> 同构）—— 供 UI/诊断面显示本轮装配的子智能体；
    /// 种子来源走会话声明面（<c>SessionDeclaration</c>），本段非种子依据。空 = 未声明。</summary>
    public string SubAgent { get; init; } = string.Empty;

    /// <summary>运行时资源段（自动发现 · 按需加载块；索引档文本）—— 消费方 = 运行时资源发现链。
    /// 空 = 无资源段（头提示层跳过，零回归）。</summary>
    public string ResourceBlock { get; init; } = string.Empty;

    /// <summary>挂载成员运行态段（容器上下文末尾；逐拍变化，CacheBand.B6）—— 消费方 = 容器（history）LLM 上下文。
    /// 空 = 无挂载/未装配（不伪造）；旧快照（schema &lt; 3）恒空并按 Legacy 解释。</summary>
    public string TaskRuntime { get; init; } = string.Empty;

    /// <summary>逐段绑定（prompt.l0/l1/l2 / skill.block），供 UI 与 LLM 双向比对。</summary>
    public IReadOnlyList<MessageBinding> Bindings { get; init; } = Array.Empty<MessageBinding>();

    /// <summary>system 槽位全文 = L0 + SkillBlock（与 <c>PromptText.Assemble(L0, SkillBlock)</c> 逐字一致）。</summary>
    public string System => TextComposer.JoinNonEmpty(new[] { L0, SkillBlock });

    /// <summary>
    /// 缓存纪元前缀指纹（PrefixFingerprint）= Hash(B0 段哈希 ∥ B1 段哈希)（逐段 SHA256 聚合，段内保序）。
    /// 同一 epoch 内 B0+B1 字节稳定 ⇒ 前缀缓存可复用（判据 J2/J4；权威: 顺序框架设计 §5.1）。
    /// </summary>
    public string PrefixFingerprint => PromptSnapshotBuilder.Hash(string.Join("|",
        Bindings
            .Where(b => CacheBandRules.IsPrefix(PromptSegmentCatalog.BandFor(b.Key)))
            .Select(b => b.TextHash)));

    public MessageBinding? BindingFor(AnchorKey key)
        => Bindings.FirstOrDefault(b => b.Key == key);

    /// <summary>序列化为 JSON（落 __prompt_snapshot 归档；仅存段字段，Bindings/System 反序列化时重建）。</summary>
    public string ToJson()
        => JsonSerializer.Serialize(new PromptSnapshotData(
            L0, SkillBlock, L1, L2, SelectedSkills, ToolBlock, SelectedTools, Mode, L0Bands, SubAgent, ResourceBlock,
            TaskRuntime, SchemaVersion));

    /// <summary>从 JSON 反序列化并重建 Bindings；null/空/非法 → null（无快照）。</summary>
    public static PromptSnapshot? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var d = JsonSerializer.Deserialize<PromptSnapshotData>(json);
            if (d is null) return null;
            // 旧归档（无 SchemaVersion 字段）按 Legacy 读取并显式标记来源，不静默当新版。
            var declaredVersion = HasSchemaVersion(json) ? d.SchemaVersion : 1;
            var snapshot = PromptSnapshotBuilder.Build(
                d.L0 ?? string.Empty, d.SkillBlock ?? string.Empty,
                d.L1 ?? string.Empty, d.L2 ?? string.Empty, d.SelectedSkills,
                d.ToolBlock, d.SelectedTools, d.Mode, d.L0Bands, d.SubAgent, resourceBlock: d.ResourceBlock,
                taskRuntime: d.TaskRuntime,
                schemaVersion: declaredVersion <= 0 ? 1 : declaredVersion);
            return snapshot;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>旧归档缺 SchemaVersion 字段 ⇒ 按 Legacy(1) 解释（禁把"未声明"当最新版）。</summary>
    private static bool HasSchemaVersion(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty(nameof(PromptSnapshotData.SchemaVersion), out var value)
                   && value.ValueKind == JsonValueKind.Number;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>空快照（未捕获时的兜底；BindingFor 返回 null，校验视为无 system 段）。</summary>
    public static PromptSnapshot Empty => new();
}
