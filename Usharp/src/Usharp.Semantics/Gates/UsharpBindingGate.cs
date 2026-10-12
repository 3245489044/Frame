using Plate.DslMethod;
using Plate.DslMethod.Model;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 规则 ⑥：控制流绑定段重复键（<c>in=</c>/<c>out=</c>/<c>routes=</c>）。
/// 同一键重复出现会被解析层按「后值覆盖」静默吞掉（声明笔误不可见）⇒ 本规则读原始字段文本可见化（E4）。
/// 严重度 = Warning：既有声明不改行为，仅暴露静默覆盖（对齐 US5005「同键抖动可见化」先例）。
/// </summary>
internal sealed class BindingDuplicateGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.binding.dup";

    private static readonly string[] BindingFields =
    {
        UsharpLanguage.FieldInBindings, UsharpLanguage.FieldOutBindings, UsharpLanguage.FieldRoutes,
    };

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        foreach (var doc in model.Documents)
            foreach (var decl in doc.Declarations)
            {
                if (decl.Kind != DslSymbolKind.Call && decl.Kind != DslSymbolKind.Region && decl.Kind != DslSymbolKind.Switch)
                    continue;

                foreach (var field in BindingFields)
                {
                    var duplicate = FirstDuplicateKey(decl.Field(field));
                    if (duplicate is null) continue;
                    sink.Report(UsharpGateDiagnostics.Warn(UsharpCodes.BindingKeyDuplicate,
                        $"{decl.Prefix} {decl.Name}: {field}= 绑定键 '{duplicate}' 重复（同键多值静默覆盖；须唯一）",
                        decl.SourceFile, decl.LineNumber));
                }
            }
    }

    /// <summary>返回首个重复出现的绑定键（无重复 / 无 <c>=</c> ⇒ null）。</summary>
    private static string? FirstDuplicateKey(string? raw)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var segment in UsharpLanguage.SplitSegments(raw))
        {
            var eq = segment.IndexOf('=');
            if (eq <= 0) continue;
            var key = segment[..eq].Trim();
            if (key.Length == 0) continue;
            if (!seen.Add(key)) return key;
        }
        return null;
    }
}
