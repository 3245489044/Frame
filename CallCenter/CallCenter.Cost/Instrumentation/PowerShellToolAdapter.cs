using System.Security.Cryptography;
using System.Text;
using CallCenter.Cost.Metering;

namespace CallCenter.Cost.Instrumentation;

/// <summary>PS 脚本 → ToolInvocation 映射（脚本即工具，Medium=PowerShell）。</summary>
public static class PowerShellToolAdapter
{
    /// <summary>
    /// 路径归一：<c>Frame/Invoke/Invoke-Verify.ps1</c> → <c>Frame.Invoke.Verify</c>；
    /// 任意目录 <c>&lt;name&gt;.ps1</c> → 相对路径点号化。
    /// </summary>
    public static string NormalizeToolName(string scriptPath)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            return "unknown";
        }

        var normalized = scriptPath.Replace('\\', '/').Trim();
        var frameIndex = normalized.IndexOf("Frame/", StringComparison.OrdinalIgnoreCase);
        if (frameIndex >= 0)
        {
            normalized = normalized[frameIndex..];
        }

        var withoutExtension = normalized.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^4]
            : normalized;
        var parts = withoutExtension
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Replace('-', '.'))
            .ToList();

        // Frame.Invoke.Invoke.Verify → Frame.Invoke.Verify（去掉重复的 Invoke 段）
        if (parts.Count >= 3
            && string.Equals(parts[^2], "Invoke", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[^3], "Invoke", StringComparison.OrdinalIgnoreCase))
        {
            parts.RemoveAt(parts.Count - 2);
        }

        return string.Join('.', parts);
    }

    /// <summary>参数摘要（只存 hash，不落明文）。</summary>
    public static string HashArguments(IEnumerable<string>? args)
    {
        var text = args is null ? string.Empty : string.Join("\u001f", args);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>构造 PS 工具调用请求。</summary>
    public static ToolInvocationRequest CreateRequest(
        string scriptPath,
        IReadOnlyList<string>? args = null,
        string origin = ToolOrigins.FrameInvoke,
        string? taskKey = null,
        string? roundId = null,
        string? traceId = null,
        string? logicalCallId = null)
    {
        var name = NormalizeToolName(scriptPath);
        var argsHash = HashArguments(args);
        return new ToolInvocationRequest(
            name,
            ToolMediums.PowerShell,
            origin,
            new InstanceRef("tool", $"{name}|{argsHash}"),
            ArgsHash: argsHash,
            TaskKey: taskKey,
            RoundId: roundId,
            TraceId: traceId,
            LogicalCallId: logicalCallId);
    }
}
