using System;

namespace FrameworkAbstractions.WinFormsAbstractions;

/// <summary>
/// 输出目标写者（单一实现，E10/D10 收敛）：命令层（exec/get）与交互层（事件函数落地）
/// 对 `output` 目标的所有文本回写都经此一处，禁止各自直呼 <see cref="IEcsStateAccessor"/> 的写方法。
/// 写语义由目标声明（outputMode）决定，不由调用点各自约定。
/// </summary>
public static class OutputTargetWriter
{
    /// <summary>
    /// 回写文本到输出目标。空值不写（空文档会清空 HtmlViewer 页面）。
    /// </summary>
    /// <param name="accessor">ECS 状态访问器（未装配 → 不写，返回 false）。</param>
    /// <param name="target">输出目标控件名。</param>
    /// <param name="text">待写文本；null/空白 → 不写。</param>
    /// <param name="outputMode">写语义（append / replace）；null = replace。</param>
    /// <returns>是否实际写入。</returns>
    public static bool Write(IEcsStateAccessor? accessor, string? target, string? text, string? outputMode = null)
    {
        if (accessor is null || string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(text))
            return false;

        return string.Equals(outputMode, TConfig.OutputWriteModes.Append, StringComparison.OrdinalIgnoreCase)
            ? accessor.AppendValue(target, text)
            : accessor.SetValue(target, text);
    }
}
