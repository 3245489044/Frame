using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Plate.PowerShellHost;

/// <summary>
/// PowerShell 子进程环境快照（单一来源）：只保留系统必需变量，隔离宿主定制/污染。
/// <para>探测、一次性脚本、长驻会话共用，避免某一宿主环境（PATH/PSModulePath 等）导致启动差异。</para>
/// </summary>
public static class PowerShellHostEnvironment
{
    public static readonly IReadOnlyList<string> DefaultAllowList = new[]
    {
        "PATH", "PATHEXT", "SystemRoot", "SystemDrive", "windir", "TEMP", "TMP",
        "COMSPEC", "USERPROFILE", "HOME", "HOMEDRIVE", "HOMEPATH",
        "PSModulePath", "DOTNET_ROOT", "PROCESSOR_ARCHITECTURE", "NUMBER_OF_PROCESSORS",
        "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "LOCALAPPDATA", "APPDATA"
    };

    /// <summary>清空继承环境并写入白名单；<paramref name="overrides"/> 最后覆盖。</summary>
    public static void Apply(
        ProcessStartInfo startInfo,
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        if (startInfo is null) throw new ArgumentNullException(nameof(startInfo));

        startInfo.Environment.Clear();
        foreach (var name in DefaultAllowList)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
                startInfo.Environment[name] = value;
        }

        if (overrides is null) return;
        foreach (var pair in overrides)
            startInfo.Environment[pair.Key] = pair.Value;
    }
}
