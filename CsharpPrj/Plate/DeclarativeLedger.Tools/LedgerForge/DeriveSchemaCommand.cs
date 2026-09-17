using DataCenter.Standards;

namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>
/// <c>derive-schema</c>：词表 → 结构门（schema <c>enum</c> / 对象键集）的**统一 CLI 入口**
/// —— 零算法重写：唯一实现 = 标准层 <c>SchemaEnumSynchronizer</c>（本件只做接线与报告）。
/// <para>
/// <c>--verify</c>（缺省）：只读对账，差异非空即红（exit 1）；<c>--sync</c>：幂等写回（空 = 已一致）。
/// schema 目录缺省自程序集位置**向上定位** <c>Frame\DataCenter\schemas</c>（找不到即抛，不猜路径）。
/// </para>
/// </summary>
internal static class DeriveSchemaCommand
{
    internal static int Run(LedgerView view, string? schemasDirectory, bool sync)
    {
        var directory = schemasDirectory is { Length: > 0 } ? Path.GetFullPath(schemasDirectory) : Locate();
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException(string.Format(view.Message("forgeDirectoryMissing"), directory));

        if (!sync)
        {
            var diffs = SchemaEnumSynchronizer.Verify(directory);
            if (diffs.Count == 0)
            {
                Console.WriteLine(string.Format(view.Message("schemaVerifyOk"), directory,
                    SchemaEnumSynchronizer.EnumBindings.Count, SchemaEnumSynchronizer.PropertiesKeyBindings.Count));
                return 0;
            }

            Console.Error.WriteLine(string.Format(view.Message("schemaVerifyFail"), diffs.Count, directory));
            foreach (var diff in diffs) Console.Error.WriteLine("  " + diff);
            return 1;
        }

        var changes = SchemaEnumSynchronizer.Sync(directory);
        if (changes.Count == 0)
        {
            Console.WriteLine(string.Format(view.Message("schemaSyncNoChange"), directory));
            return 0;
        }

        Console.WriteLine(string.Format(view.Message("schemaSyncChanged"), changes.Count, directory));
        foreach (var change in changes) Console.WriteLine("  " + change);
        return 0;
    }

    /// <summary>schema 目录（自程序集位置向上定位；找不到即抛）。</summary>
    private static string Locate()
    {
        const string Relative = @"Frame\DataCenter\schemas";
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, Relative);
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"未能自 {AppContext.BaseDirectory} 向上定位 schema 目录（未找到 {Relative}）；请用 --schemas 显式指定");
    }
}
