using System.Text.Json;
using BasicMethod.Declarative.Ledger;

namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>
/// <c>set-impl</c>：把「端口键 → 新 impl 对象」的数据文件合并进台账 —— **只改 <c>ports[&lt;键&gt;].impl</c>
/// 子对象**（<see cref="LedgerTextEditor"/> 花括号配平定位），其余字节**逐字保留**
/// （不重排 / 不重序列化整份文件 ⇒ diff 只落在被改键上）。
/// <para>
/// 新 impl 由数据文件给出（紧凑 JSON 亦可）：按台账风格重排为 6 空格内缩进（= <c>PsJsonText</c> 续行前缀口径）。
/// 缺省 **dry-run**（只报告将改写哪些键，不落盘）；<c>--write</c> 才写。同输入二次运行**零变更**（幂等）。
/// </para>
/// </summary>
internal static class SetImplCommand
{
    /// <summary>规划改写（含全部键存在性校验）；不落盘。</summary>
    internal static (string Text, IReadOnlyList<string> Changed) Plan(LedgerView view, string implPath)
    {
        if (!File.Exists(implPath))
            throw new FileNotFoundException(string.Format(view.Message("forgeFileMissing"), implPath));
        if (!File.Exists(view.LedgerPath))
            throw new FileNotFoundException(string.Format(view.Message("forgeFileMissing"), view.LedgerPath));

        using var declared = JsonDocument.Parse(File.ReadAllText(implPath));
        if (declared.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException(string.Format(view.Message("implDeclaredShape"), implPath));

        var text = File.ReadAllText(view.LedgerPath);
        var lineBreak = text.Contains(PsJsonText.LineBreak, StringComparison.Ordinal) ? PsJsonText.LineBreak : "\n";
        var changed = new List<string>();

        foreach (var property in declared.RootElement.EnumerateObject())
        {
            var (status, _, _) = LedgerTextEditor.LocateMemberObject(text, property.Name, "impl");
            if (status != LedgerLocateStatus.Found)
                throw new InvalidOperationException(string.Format(
                    view.Message(StatusMessageKey(status)), property.Name));

            var block = PsJsonText.Write(property.Value, "      ");
            if (!string.Equals(lineBreak, PsJsonText.LineBreak, StringComparison.Ordinal))
                block = block.Replace(PsJsonText.LineBreak, lineBreak, StringComparison.Ordinal);

            text = LedgerTextEditor.ReplaceMemberObject(text, property.Name, "impl", block, out var dirty);
            if (dirty) changed.Add(property.Name);
        }

        return (text, changed);
    }

    internal static int Run(LedgerView view, string implPath, bool write)
    {
        var (text, changed) = Plan(view, implPath);

        if (!write)
        {
            Console.WriteLine(string.Format(view.Message("implPlan"), changed.Count, string.Join(", ", changed)));
            if (changed.Count != 0) Console.Error.WriteLine(view.Message("forgeDryRunHint"));
            return 0;
        }

        if (changed.Count == 0)
        {
            Console.WriteLine(view.Message("implNoChange"));
            return 0;
        }

        LedgerBytes.Write(view.LedgerPath, text);
        Console.WriteLine(string.Format(view.Message("implChanged"), changed.Count, string.Join(", ", changed)));
        return 0;
    }

    /// <summary>定位状态 → 文案键（文案单源：状态机在机制层，文案在数据视图）。</summary>
    private static string StatusMessageKey(LedgerLocateStatus status) => status switch
    {
        LedgerLocateStatus.KeyMissing => "implKeyMissing",
        LedgerLocateStatus.MemberMissing => "implSectionMissing",
        LedgerLocateStatus.BodyMissing => "implBodyMissing",
        _ => "implUnbalanced",
    };
}
