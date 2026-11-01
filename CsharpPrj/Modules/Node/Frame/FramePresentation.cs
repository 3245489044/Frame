using System;
using System.Collections.Generic;

namespace Modules.Node.Frame;

/// <summary>
/// 呈现身份裁决（**唯一实现**；域无关）：事实 → 通道 → 声明词 → 通道兜底 → 角色兜底 → 未知。
/// <para>
/// 六步顺序**不得交换**；输入全部为数据/值（类清单、事实类、通道类、角色兜底类、呈现词），
/// 框架不持有任何产品词（bubble/answer/process 由宿主声明给出）。
/// </para>
/// <para>返回 <c>Origin</c> 为结构事实串（<see cref="OriginFact"/> 等），由宿主映射为本地枚举。</para>
/// </summary>
public static class FramePresentation
{
    /// <summary>未知类标识（可见占位；不冒充过程面）。</summary>
    public const string UnknownClass = "unknown";

    /// <summary>来源：写侧事实（终答/通道）。</summary>
    public const string OriginFact = "fact";

    /// <summary>来源：声明词投影。</summary>
    public const string OriginDeclared = "declared";

    /// <summary>来源：旧数据角色兜底。</summary>
    public const string OriginLegacy = "legacy";

    /// <summary>来源：不可判定。</summary>
    public const string OriginUnknown = "unknown";

    /// <summary>裁决呈现身份。</summary>
    /// <param name="declaredClasses">已声明呈现类清单（判定“声明词”用）。</param>
    /// <param name="terminalClass">终答事实类（空 = 未声明）。</param>
    /// <param name="channelWord">本条消息的通道词（宿主枚举投影）。</param>
    /// <param name="operationChannelWord">操作面通道词（声明侧约定）。</param>
    /// <param name="operationClass">操作面类（空 = 未声明）。</param>
    /// <param name="processChannelWord">过程面通道词。</param>
    /// <param name="processClass">过程面类（空 = 未声明）。</param>
    /// <param name="roleFallbackClass">角色兜底类（宿主查表后给出；空 = 无兜底）。</param>
    /// <param name="presentationWord">写侧呈现词（可空）。</param>
    /// <param name="terminal">终答事实（<c>null</c> = 未知）。</param>
    public static (string ClassId, string Origin) Resolve(
        IEnumerable<string> declaredClasses,
        string? terminalClass,
        string? channelWord,
        string operationChannelWord,
        string? operationClass,
        string processChannelWord,
        string? processClass,
        string? roleFallbackClass,
        string? presentationWord,
        bool? terminal)
    {
        ArgumentNullException.ThrowIfNull(declaredClasses);

        // P1 写侧事实：终答（最高优先，即便通道为操作面）。
        if (terminal == true && !string.IsNullOrEmpty(terminalClass))
        {
            return (terminalClass!, OriginFact);
        }

        // P2 写侧事实：操作面。
        if (!string.IsNullOrEmpty(operationClass)
            && string.Equals(channelWord, operationChannelWord, StringComparison.OrdinalIgnoreCase))
        {
            return (operationClass!, OriginFact);
        }

        // P3 声明投影（词必须已在声明类清单登记）。
        if (!string.IsNullOrWhiteSpace(presentationWord))
        {
            var word = presentationWord.Trim().ToLowerInvariant();
            foreach (var declared in declaredClasses)
            {
                if (string.Equals(declared, word, StringComparison.Ordinal))
                {
                    return (word, OriginDeclared);
                }
            }
        }

        // P4 过程通道（旧数据；不得因角色被提升为气泡）。
        if (!string.IsNullOrEmpty(processClass)
            && string.Equals(channelWord, processChannelWord, StringComparison.OrdinalIgnoreCase))
        {
            return (processClass!, OriginFact);
        }

        // P5 旧数据角色兜底。
        if (!string.IsNullOrEmpty(roleFallbackClass))
        {
            return (roleFallbackClass!, OriginLegacy);
        }

        // P6 未知：可见，不猜。
        return (UnknownClass, OriginUnknown);
    }
}
