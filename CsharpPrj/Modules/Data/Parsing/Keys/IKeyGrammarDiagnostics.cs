namespace Modules.Parsing.Keys;

/// <summary>诊断严重度（与 Modules.Node.Diagnostics 同形，保持跨程序集语义一致）。</summary>
public enum KeyGrammarSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// 键文法诊断（码 + 严重度 + 文案 + 主体）—— 反解歧义 / 文法未注册 / 族前缀冲突一律可见化，
/// 禁静默择一（对齐 PlacementResolver 的 L-2「多解即错」口径）。
/// </summary>
public sealed record KeyGrammarDiagnostic(
    string Code,
    KeyGrammarSeverity Severity,
    string Message,
    string Subject);

/// <summary>诊断出口（宿主注入；未装配 ⇒ <see cref="NullKeyGrammarDiagnostics"/>）。</summary>
public interface IKeyGrammarDiagnostics
{
    void Report(KeyGrammarDiagnostic diagnostic);
}

/// <summary>未装配诊断出口：丢弃全部诊断（零 BCL 之外依赖，供单测与轻宿主）。</summary>
public sealed class NullKeyGrammarDiagnostics : IKeyGrammarDiagnostics
{
    public static readonly NullKeyGrammarDiagnostics Instance = new();

    private NullKeyGrammarDiagnostics()
    {
    }

    public void Report(KeyGrammarDiagnostic diagnostic)
    {
    }
}
