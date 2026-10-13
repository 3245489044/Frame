using System;
using System.IO;

namespace Common.Diagnostics
{
    /// <summary>
    /// 裸 dump 兜底策略：配置 WER LocalDumps / .NET 迷你转储环境变量（仅当未显式设置时）。
    /// 覆盖进程级不可捕获终止（原生崩溃/栈溢出/FailFast）的离线取证。
    /// </summary>
    public static class FaultDumpPolicy
    {
        public static void Apply(string? dumpFolder = null)
        {
            try
            {
                var dir = dumpFolder ?? Path.Combine(CrashStore.DirectoryPath, "dumps");
                System.IO.Directory.CreateDirectory(dir);

                SetIfEmpty("DOTNET_DbgEnableMiniDump", "1");
                SetIfEmpty("DOTNET_DbgMiniDumpType", "4");
                SetIfEmpty("DOTNET_DbgMiniDumpName", Path.Combine(dir, "dotnet_%e_%p.dmp"));
                SetIfEmpty("DOTNET_CreateDumpDiagnostics", "1");
            }
            catch (Exception ex)
            {
                CrashDiag.Degrade($"FaultDumpPolicy 配置失败: {ex.Message}");
            }
        }

        private static void SetIfEmpty(string name, string value)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
