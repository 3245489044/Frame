
namespace ParameterResolver.Validation.Diagnostics;

public enum Severity
{
    Info = 0,

    Warning = 1,

    Error = 2
}

public static class SeverityExtensions
{
    public static bool IsBlocking(this Severity severity, bool strictMode)
        => strictMode && severity == Severity.Error;

    public static bool ShouldLog(this Severity severity)
        => severity >= Severity.Warning;

    public static string ToDisplayString(this Severity severity)
        => severity switch
        {
            Severity.Info => "信息",
            Severity.Warning => "警告",
            Severity.Error => "错误",
            _ => "未知"
        };
}
