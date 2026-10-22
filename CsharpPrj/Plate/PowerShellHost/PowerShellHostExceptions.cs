using System;
using System.Collections.Generic;

namespace Plate.PowerShellHost;

/// <summary>
/// PowerShell 宿主失败（稳定码 + 短消息 + 有界 stderr 尾部）。
/// <para>
/// 判据载体 = 结构事实（码 / 路径 / 退出码 / stderr 倒排行）；调用方按 <see cref="Code"/> 归因，
/// 禁靠异常类型或文案猜原因。全量 stderr 只留在 <see cref="StderrTail"/>（诊断面），不进正常事件载荷。
/// </para>
/// </summary>
public class PowerShellHostException : InvalidOperationException
{
    public PowerShellHostException(
        string code,
        string message,
        string? path = null,
        IReadOnlyList<string>? stderrTail = null,
        Exception? inner = null,
        int? nativeErrorCode = null,
        string? workingDirectory = null,
        string? detail = null)
        : base(Shorten(message), inner)
    {
        Code = code;
        Path = path;
        StderrTail = stderrTail ?? Array.Empty<string>();
        NativeErrorCode = nativeErrorCode;
        WorkingDirectory = workingDirectory;
        Detail = detail;
    }

    /// <summary>稳定失败码（跨层分类器消费）。</summary>
    public string Code { get; }

    /// <summary>相关绝对路径（可空，例如配置缺失）。</summary>
    public string? Path { get; }

    /// <summary>stderr 尾部（有界；诊断/日志面，禁用作文本判据）。</summary>
    public IReadOnlyList<string> StderrTail { get; }

    /// <summary>Win32 原生错误码（启动失败时由 Win32Exception 提供；诊断面）。</summary>
    public int? NativeErrorCode { get; }

    /// <summary>失败时的工作目录（结构事实；用于区分“alias 不可启动”与“工作目录无效”）。</summary>
    public string? WorkingDirectory { get; }

    /// <summary>诊断详情（全量结构事实；只进 detailRef/日志，禁进短消息与事件载荷）。</summary>
    public string? Detail { get; }

    /// <summary>短消息上限与定位器同源（人读；全量走 StderrTail）。</summary>
    protected static string Shorten(string message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        return message.Length <= PowerShellHostLocator.ShortMessageLimit
            ? message
            : message[..PowerShellHostLocator.ShortMessageLimit] + "…";
    }
}

/// <summary>探测失败（稳定码：probe_failed / probe_timeout / executable_missing）。</summary>
public sealed class PowerShellHostProbeException : PowerShellHostException
{
    public PowerShellHostProbeException(
        string code,
        string message,
        string? path = null,
        int? exitCode = null,
        IReadOnlyList<string>? stderrTail = null,
        Exception? inner = null,
        int? nativeErrorCode = null,
        string? workingDirectory = null,
        string? detail = null)
        : base(code, message, path, stderrTail, inner, nativeErrorCode, workingDirectory, detail)
    {
        ExitCode = exitCode;
    }

    /// <summary>探测进程退出码（启动/超时失败时为 null）。</summary>
    public int? ExitCode { get; }
}

/// <summary>一次性脚本执行失败（稳定码：script_missing / script_start_failed）。</summary>
public sealed class PowerShellHostScriptException : PowerShellHostException
{
    public PowerShellHostScriptException(
        string code,
        string message,
        string? path = null,
        IReadOnlyList<string>? stderrTail = null,
        Exception? inner = null)
        : base(code, message, path, stderrTail, inner)
    {
    }
}

/// <summary>长驻会话失败（稳定码：session_start_failed / session_not_started / session_timeout / session_exited）。</summary>
public sealed class PowerShellHostSessionException : PowerShellHostException
{
    public PowerShellHostSessionException(
        string code,
        string message,
        string? path = null,
        IReadOnlyList<string>? stderrTail = null,
        Exception? inner = null)
        : base(code, message, path, stderrTail, inner)
    {
    }
}

/// <summary>稳定码常量（全仓单一来源；调用方禁手写字符串）。</summary>
public static class PowerShellHostCodes
{
    public const string ExecutableNotFoundCode = "psruntime.executable_not_found";
    public const string ExecutableMissing = "psruntime.executable_missing";
    public const string ExecutableAliasUnresolved = "psruntime.executable_alias_unresolved";
    public const string ExecutableAliasTargetMissing = "psruntime.executable_alias_target_missing";
    public const string StoreRootInaccessible = "psruntime.store_root_inaccessible";
    public const string ProbeFailed = "psruntime.probe_failed";
    public const string ProbeTimeout = "psruntime.probe_timeout";
    public const string ScriptMissing = "psruntime.script_missing";
    public const string ScriptStartFailed = "psruntime.script_start_failed";
    public const string SessionStartFailed = "psruntime.session_start_failed";
    public const string SessionNotStarted = "psruntime.session_not_started";
    public const string SessionTimeout = "psruntime.session_timeout";
    public const string SessionExited = "psruntime.session_exited";
    public const string SessionResponseUnmatched = "psruntime.session_response_unmatched";
}
