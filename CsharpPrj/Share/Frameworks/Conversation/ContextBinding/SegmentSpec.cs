#nullable enable

using System;

namespace Share.Conversation.ContextBinding;

/// <summary>
/// 段装配输入（写侧产段时的原始值）——<see cref="SegmentSpec.Produce"/> 的单一消费形参，
/// 使快照构建无需按段名散写取值逻辑。
/// </summary>
public sealed record SegmentBuildInputs(
    string L0,
    string SkillBlock,
    string L1,
    string L2,
    string? ToolBlock,
    string? Mode,
    string? SubAgent);

/// <summary>
/// 提示词段声明（SegmentSpec）—— 段的**单一真相**：键 + 展示 stage + 写侧取值（<see cref="Produce"/>）
/// + 读侧取值（<see cref="Text"/>）+ 档案开关名（<see cref="Toggle"/>）。
/// <para>快照构建（发射 binding）与段投影（UI/LLM 头提示层）**均由此驱动**，消除
/// 「新增段须改多处、漏改即投影缺口（如 prompt.mode）」。</para>
/// <para>纯声明（零领域依赖），权威注册表见 <see cref="PromptSegmentCatalog"/>。</para>
/// </summary>
/// <param name="Key">段锚点键（全局唯一，R4 校验）。</param>
/// <param name="UiStage">投影为头提示层气泡的 stage（前缀 <c>prompt:</c> ⇒ 折叠气泡）。</param>
/// <param name="Produce">写侧取值（装配输入 → 段文本；空/ null = 不发射 binding）。</param>
/// <param name="Text">读侧取值（快照 → 段文本；投影唯一依据）。</param>
/// <param name="Toggle">档案开关名（可选；null = 恒启用）。</param>
/// <param name="Band">缓存带（顺序单一权威；默认 B5 = 本轮易变，禁入前缀）。</param>
/// <param name="Target">投喂目标槽位（System 前缀 / User 有序列表）。</param>
public sealed record SegmentSpec(
    AnchorKey Key,
    string UiStage,
    Func<SegmentBuildInputs, string?> Produce,
    Func<PromptSnapshot, string> Text,
    string? Toggle = null,
    CacheBand Band = CacheBand.B5,
    SegmentTarget Target = SegmentTarget.System);
