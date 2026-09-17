namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>
/// <c>compose</c>：分片 → 产物（重组 = <c>_meta.json</c> 成员序 + 各组分片按**组序**）。
/// 缺省 **dry-run**：把渲染文本与磁盘产物**逐字节比较**，差异即红（不落盘）；<c>--write</c> 才写。
/// </summary>
internal static class ComposeCommand
{
    /// <summary>渲染重组产物（含端口数）；不落盘。</summary>
    internal static (string Text, int PortCount) Build(LedgerView view)
    {
        using var document = LedgerDocument.FromParts(view);
        return (document.Render(), document.Ports.Count);
    }

    /// <summary>产物字节对拍（空 = 逐字节一致）。</summary>
    internal static IReadOnlyList<string> Drift(LedgerView view)
    {
        var (text, _) = Build(view);
        var drift = LedgerBytes.Drift(view, view.LedgerPath, text);
        return drift is null ? [] : [drift];
    }

    internal static int Run(LedgerView view, bool write)
    {
        var (text, portCount) = Build(view);

        if (!write)
        {
            var drift = LedgerBytes.Drift(view, view.LedgerPath, text);
            if (drift is null)
            {
                Console.WriteLine(string.Format(
                    view.Message("forgeComposeOk"), view.Groups.Count, portCount, view.LedgerPath));
                return 0;
            }

            Console.Error.WriteLine(drift);
            Console.Error.WriteLine(view.Message("forgeDryRunHint"));
            return 1;
        }

        LedgerBytes.Write(view.LedgerPath, text);
        Console.WriteLine(string.Format(view.Message("composeDone"), view.Groups.Count, view.LedgerPath, portCount));
        return 0;
    }
}
