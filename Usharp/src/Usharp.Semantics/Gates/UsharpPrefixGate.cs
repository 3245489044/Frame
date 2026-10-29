using System.Text.RegularExpressions;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;

namespace Usharp.Semantics;

/// <summary>
/// 核心闸（常驻）：声明行前缀**可见性**判据 —— 源文本中形如 <c>^\[Name\]</c> 的行，若不在
/// 框架核心表、也不在任何已装载语法包的前缀集内 ⇒ <c>US0013</c>。
/// <para>
/// 目的（I2 可验证）：未装载某领域包时，此前是「前缀不识别 ⇒ 声明静默消失」；本规则把它变成
/// <b>明确报错</b>（比编译失败之后的下游怪象更早、更准）。
/// </para>
/// <para>与领域无关、常驻：由 <see cref="UsharpValidator"/> 固定在规则序首位执行（根因先于后果）。</para>
/// </summary>
internal sealed class UnregisteredPrefixGateRule : IGateRule<UsharpGateModel>
{
    /// <summary>声明行前缀形态：<c>[</c> + 字母开头 + 字母/数字/<c>_</c>/<c>-</c> + <c>]</c>（不含 <c>[CR: …]</c> 等内联引用）。</summary>
    private static readonly Regex DeclarationPrefix = new(
        @"^\[[A-Za-z][A-Za-z0-9_-]*\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Name => "usharp.prefix";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        var scope = model.PrefixScope;
        if (scope is null) return;

        foreach (var source in model.Sources)
        {
            if (string.IsNullOrEmpty(source.Text)) continue;

            var lines = source.Text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            var inFence = false;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // fenced 块（```…```）由解析器整体跳过，不做前缀识别（避免把代码样例误判为声明）。
                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    continue;
                }
                if (inFence || line.Length == 0 || line[0] != '[') continue;

                var match = DeclarationPrefix.Match(line);
                if (!match.Success) continue;

                var prefix = match.Value;
                if (scope.Find(prefix) is not null) continue;

                sink.Report(UsharpGateDiagnostics.Error(
                    UsharpDiagnosticCatalog.UnregisteredPrefix,
                    $"{prefix}: 未在任何已装载领域包注册（未装载 ⇒ 不识别；装载提供该前缀的领域包后重编）",
                    source.Path, i + 1));
            }
        }
    }
}
