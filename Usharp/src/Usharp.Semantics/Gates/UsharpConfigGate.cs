using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 闸A：<c>[Config]</c> 续行结构闭包（C1–C2）。
/// <para>
/// 判据载体 = 声明面结构事实（条目键重复 / 未解析续行）；值语义仍由装载侧裁决（本闸不推断类型）。
/// 补齐「<c>[Config]</c> 无闸」缺口：注释已由 <see cref="UsharpContinuation"/> 统一跳过，
/// 其余形态错误必须在此可见化。
/// </para>
/// </summary>
internal sealed class ConfigGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.config";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        foreach (var config in model.Configs)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in config.Entries)
            {
                if (!seen.Add(entry.Key))
                {
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.AxisDup,
                        $"[Config] {config.Path}: 条目键 '{entry.Key}' 重复声明（禁后者静默覆盖）",
                        config.SourceFile, config.LineNumber));
                }
            }

            foreach (var line in config.UnparsedLines)
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DocumentBodyLineUnparsed,
                    $"[Config] {config.Path}: 无法解析的续行 '{line}'（须为 key = value）",
                    config.SourceFile, config.LineNumber));
            }
        }
    }
}
