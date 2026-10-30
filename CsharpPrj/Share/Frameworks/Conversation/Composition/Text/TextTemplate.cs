#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace Share.Composition.Text;

/// <summary>
/// 提示词模板填充内核（Frame 通用层，Template target）。
/// <para>
/// 只负责“模板串 + 槽值 → 文本 + 槽状态 + 哈希”，不解析 JSON、不查节点、不调 LLM；
/// 槽来源（节点切片/派生/参数/技能正文）全部由领域侧提供。
/// </para>
/// <para>
/// 两阶段语义由一次扫描实现：<see cref="TextTemplateRequest.Scalars"/> 与
/// <see cref="TextTemplateRequest.Blocks"/> 的 token 按出现位置解析，已填入的值不会再被识别，
/// 因此节点文本里的 JSON 大括号或 <c>&lt;&lt;...&gt;&gt;</c> 不会被二次解析。
/// </para>
/// </summary>
public static class TextTemplate
{
    private const string DefaultUnknownPlaceholder = "«unknown»";
    private const string TruncatedSuffix = "…(truncated)";

    private static readonly IReadOnlyDictionary<string, TemplateSlotValue> EmptySlots =
        new Dictionary<string, TemplateSlotValue>(StringComparer.Ordinal);

    /// <summary>
    /// 提取模板槽名（按定界符 + 槽名规则；首次出现序、去重）。
    /// <para>非法 token（如 JSON 对象/纯数字）不作为槽，调用方无需二次过滤。</para>
    /// </summary>
    public static IReadOnlyList<string> ExtractSlots(
        string? template,
        TemplateDelimiters delimiters,
        TemplateSlotNameRules? rules = null)
    {
        ArgumentNullException.ThrowIfNull(delimiters);
        delimiters.EnsureValid();
        var effectiveRules = rules ?? TemplateSlotNameRules.Identifier;

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (template is null || template.Length == 0) return result;

        var index = 0;
        while (index < template.Length)
        {
            var start = template.IndexOf(delimiters.Prefix, index, StringComparison.Ordinal);
            if (start < 0) break;

            var contentStart = start + delimiters.Prefix.Length;
            var end = template.IndexOf(delimiters.Suffix, contentStart, StringComparison.Ordinal);
            if (end < 0) break;

            var token = template.Substring(contentStart, end - contentStart);
            var next = end + delimiters.Suffix.Length;
            if (effectiveRules.IsValid(token) && seen.Add(token))
                result.Add(token);

            // 非法 token 也跳过整个匹配段，避免 {{x}} 被 {x} 的嵌套前缀二次匹配。
            index = next;
        }

        return result;
    }

    /// <summary>执行一次模板填充。</summary>
    public static TextTemplateResult Render(TextTemplateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var template = request.Template ?? string.Empty;
        var scalars = request.Scalars ?? EmptySlots;
        var blocks = request.Blocks;
        var scalarDelimiters = (request.ScalarDelimiters ?? TemplateDelimiters.Brace).EnsureValid();
        var blockDelimiters = (request.BlockDelimiters ?? TemplateDelimiters.Angle).EnsureValid();
        var rules = request.Rules ?? TemplateSlotNameRules.Identifier;
        var placeholder = request.UnknownPlaceholder ?? DefaultUnknownPlaceholder;

        if (blocks is not null && scalarDelimiters.Prefix == blockDelimiters.Prefix)
            throw new ArgumentException(
                "标量定界符与块定界符的前缀不得相同（无法判定 token 归属）。",
                nameof(request));

        var slots = new List<TemplateSlotValue>();
        var slotSeen = new HashSet<string>(StringComparer.Ordinal);
        var unknown = new List<string>();
        var unknownSeen = new HashSet<string>(StringComparer.Ordinal);
        var blocked = false;
        var degraded = false;

        var sb = new StringBuilder(template.Length);
        var index = 0;

        while (index < template.Length)
        {
            var scalarStart = template.IndexOf(scalarDelimiters.Prefix, index, StringComparison.Ordinal);
            var blockStart = blocks is null
                ? -1
                : template.IndexOf(blockDelimiters.Prefix, index, StringComparison.Ordinal);

            if (scalarStart < 0 && blockStart < 0)
            {
                sb.Append(template, index, template.Length - index);
                break;
            }

            var useBlock = blockStart >= 0
                           && (scalarStart < 0
                               || blockStart < scalarStart
                               || (blockStart == scalarStart
                                   && blockDelimiters.Prefix.Length > scalarDelimiters.Prefix.Length));

            var start = useBlock ? blockStart : scalarStart;
            var delimiters = useBlock ? blockDelimiters : scalarDelimiters;
            var map = useBlock ? blocks! : scalars;

            sb.Append(template, index, start - index);

            var contentStart = start + delimiters.Prefix.Length;
            var end = template.IndexOf(delimiters.Suffix, contentStart, StringComparison.Ordinal);
            if (end < 0)
            {
                sb.Append(template, start, template.Length - start);
                break;
            }

            var token = template.Substring(contentStart, end - contentStart);
            var next = end + delimiters.Suffix.Length;

            if (!rules.IsValid(token))
            {
                // 非法 token 原样保留（JSON 对象/纯数字等），不参与槽状态。
                sb.Append(template, start, next - start);
                index = next;
                continue;
            }

            var resolution = Resolve(token, template.Substring(start, next - start), map, request.Missing, placeholder);
            sb.Append(resolution.Text);

            if (slotSeen.Add(token))
                slots.Add(resolution.Slot);

            if (resolution.Unknown && unknownSeen.Add(token))
                unknown.Add(token);

            if (resolution.Blocked)
                blocked = true;
            else if (resolution.Unknown)
                degraded = true;

            index = next;
        }

        var text = sb.ToString();
        var truncated = false;
        if (request.MaxChars > 0 && text.Length > request.MaxChars)
        {
            text = text.Substring(0, request.MaxChars) + TruncatedSuffix;
            truncated = true;
            degraded = true;
        }

        var status = blocked
            ? TextVerdictStatus.Blocked
            : degraded
                ? TextVerdictStatus.Degraded
                : TextVerdictStatus.Complete;

        var hash = ComputeHash(request, scalarDelimiters, blocks is null ? null : blockDelimiters, slots);
        return new TextTemplateResult(text, hash, status, slots, unknown, truncated);
    }

    private static Resolution Resolve(
        string token,
        string originalSegment,
        IReadOnlyDictionary<string, TemplateSlotValue> map,
        TemplateMissingPolicy policy,
        string placeholder)
    {
        if (map.TryGetValue(token, out var value))
        {
            var normalized = value with { Slot = token };

            switch (value.State)
            {
                case TextState.Known:
                    return new Resolution(value.Text ?? string.Empty, normalized, Unknown: false, Blocked: false);

                case TextState.Empty:
                    return new Resolution(string.Empty, normalized with { Text = string.Empty }, Unknown: false,
                        Blocked: policy == TemplateMissingPolicy.FailFast);

                case TextState.NotApplicable:
                    return new Resolution(string.Empty, normalized with { Text = string.Empty }, Unknown: false,
                        Blocked: false);

                case TextState.Unknown:
                default:
                    return MissingOrUnknown(originalSegment, policy, placeholder,
                        normalized with { Text = null });
            }
        }

        return MissingOrUnknown(
            originalSegment,
            policy,
            placeholder,
            new TemplateSlotValue(token, TextState.Unknown, null, "missing", "missing"));
    }

    private static Resolution MissingOrUnknown(
        string originalSegment,
        TemplateMissingPolicy policy,
        string placeholder,
        TemplateSlotValue slot)
        => policy switch
        {
            TemplateMissingPolicy.Keep => new Resolution(originalSegment, slot, Unknown: true, Blocked: false),
            TemplateMissingPolicy.Omit => new Resolution(string.Empty, slot, Unknown: true, Blocked: false),
            TemplateMissingPolicy.Unknown => new Resolution(placeholder, slot, Unknown: true, Blocked: false),
            _ => new Resolution(placeholder, slot, Unknown: true, Blocked: true),
        };

    private static string ComputeHash(
        TextTemplateRequest request,
        TemplateDelimiters scalarDelimiters,
        TemplateDelimiters? blockDelimiters,
        IReadOnlyList<TemplateSlotValue> slots)
    {
        var sb = new StringBuilder();
        sb.Append(request.Template ?? string.Empty).Append('\n')
          .Append(scalarDelimiters.Prefix).Append('|').Append(scalarDelimiters.Suffix).Append('|')
          .Append(blockDelimiters?.Prefix ?? string.Empty).Append('|')
          .Append(blockDelimiters?.Suffix ?? string.Empty).Append('|')
          .Append(request.Missing).Append('|')
          .Append(request.MaxChars).Append('|')
          .Append(request.UnknownPlaceholder ?? DefaultUnknownPlaceholder)
          .Append('\n');

        foreach (var slot in slots)
        {
            sb.Append(slot.Slot).Append('|')
              .Append(slot.State).Append('|')
              .Append(TextDigest.Hash(slot.Text)).Append('|')
              .Append(slot.Source).Append(';');
        }

        return TextDigest.Hash(sb.ToString());
    }

    /// <summary>单槽解析结果（文本 + 槽值 + 是否未知/阻断）。</summary>
    private readonly record struct Resolution(
        string Text,
        TemplateSlotValue Slot,
        bool Unknown,
        bool Blocked);
}
