using System;
using Share.LogicComposition.Conversion.Models;

namespace Share.LogicComposition.Conversion.Hosting;

/// <summary>
/// 宿主无关的日志条目：把转换诊断摊平为「文件/行/列/码/消息/严重度」，
/// 由具体宿主（MSBuild Task、CLI、控制台）自行落库——框架不绑定任何宿主程序集。
/// </summary>
public readonly struct BuildLogEntry
{
    /// <summary>构造日志条目。</summary>
    public BuildLogEntry(string file, int line, int column, string code, string message, bool isError)
    {
        File = file;
        Line = line;
        Column = column;
        Code = code;
        Message = message;
        IsError = isError;
    }

    /// <summary>文件路径。</summary>
    public string File { get; }

    /// <summary>行号（0 = 未知）。</summary>
    public int Line { get; }

    /// <summary>列号（0 = 未知）。</summary>
    public int Column { get; }

    /// <summary>稳定错误码。</summary>
    public string Code { get; }

    /// <summary>人类可读消息。</summary>
    public string Message { get; }

    /// <summary>是否 Error 级（false = Warning 级）。</summary>
    public bool IsError { get; }
}

/// <summary>转换诊断 → 宿主无关日志条目的换算（位置缺失时回落到源文件路径）。</summary>
public static class ConversionDiagnosticLogMap
{
    /// <summary>换算单条诊断。</summary>
    /// <param name="diagnostic">转换诊断。</param>
    /// <param name="sourcePath">源文件路径（诊断无位置时的回落目标）。</param>
    public static BuildLogEntry ToLogEntry(ConversionDiagnostic diagnostic, string sourcePath)
    {
        if (diagnostic is null) throw new ArgumentNullException(nameof(diagnostic));

        var file = diagnostic.Span.HasPosition ? diagnostic.Span.File! : sourcePath;
        var line = diagnostic.Span.HasPosition ? diagnostic.Span.Line : 0;
        var column = diagnostic.Span.HasPosition ? diagnostic.Span.Column : 0;

        return new BuildLogEntry(
            file,
            line,
            column,
            diagnostic.Code,
            diagnostic.Message,
            diagnostic.Severity == ConversionSeverity.Error);
    }
}
