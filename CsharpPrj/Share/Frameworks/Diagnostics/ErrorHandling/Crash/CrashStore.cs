using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Common.Diagnostics
{
    /// <summary>
    /// 取证落面唯一写入口（聚拢点）：append-only NDJSON + 同步 fsync。
    /// 独立 crash/ 目录，不与被折叠的滚动日志共用落面；写失败降级可见、不反噬主链。
    /// </summary>
    public static class CrashStore
    {
        private static readonly object _gate = new();

        private static readonly JsonSerializerOptions _opt = new() { WriteIndented = false };

        private static string? _dir;

        public static string DirectoryPath
        {
            get => _dir ??= ResolveDefaultDir();
            set => _dir = value;
        }

        private static string ResolveDefaultDir()
        {
            var overridden = Environment.GetEnvironmentVariable("CRASH_EVIDENCE_DIR");
            return string.IsNullOrWhiteSpace(overridden)
                ? Path.Combine(Environment.CurrentDirectory, "crash")
                : overridden;
        }

        public static string FilePath => Path.Combine(DirectoryPath, "crash.jsonl");

        public static void Append(CrashRecord record)
        {
            if (record == null) return;

            try
            {
                lock (_gate)
                {
                    System.IO.Directory.CreateDirectory(DirectoryPath);
                    var line = JsonSerializer.Serialize(record, _opt) + Environment.NewLine;
                    var bytes = Encoding.UTF8.GetBytes(line);
                    using var fs = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);
                }
            }
            catch (Exception ex)
            {
                CrashDiag.Degrade($"CrashStore 写入失败（主链不受影响）: {ex.Message}");
            }
        }
    }
}
