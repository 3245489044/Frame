using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Common.Diagnostics
{
    /// <summary>
    /// 进程遗嘱（聚拢点）：启动写 run.alive 存活标记；相位变更覆盖写 crash-state.json（fsync）；
    /// 正常收尾删标记。不可捕获终止后，下次启动据残留标记自证（见 CrashRecovery）。
    /// 健康路径零日志；写失败降级可见、不反噬主链。
    /// </summary>
    public static class LastWillWriter
    {
        private static readonly object _gate = new();

        private static readonly JsonSerializerOptions _opt = new() { WriteIndented = false };

        private static CrashState? _state;

        private static bool _started;

        public static string DirectoryPath => CrashStore.DirectoryPath;

        public static string AlivePath => Path.Combine(DirectoryPath, "run.alive");

        public static string StatePath => Path.Combine(DirectoryPath, "crash-state.json");

        public static void Begin()
        {
            lock (_gate)
            {
                _state = new CrashState();
                _started = true;
                WriteFile(AlivePath, $"{_state.ProcessId};{_state.StartedUtc:O}");
                WriteState();
            }
        }

        public static void Mark(string phase, string? detail = null)
        {
            lock (_gate)
            {
                if (!_started || _state == null) return;
                _state.Phase = phase;
                _state.Detail = detail;
                _state.UpdatedUtc = DateTime.UtcNow;
                WriteState();
            }
        }

        public static void Complete()
        {
            lock (_gate)
            {
                if (!_started) return;
                _started = false;
                _state = null;
                TryDelete(AlivePath);
            }
        }

        private static void WriteState()
        {
            if (_state == null) return;
            WriteFile(StatePath, JsonSerializer.Serialize(_state, _opt));
        }

        private static void WriteFile(string path, string content)
        {
            try
            {
                System.IO.Directory.CreateDirectory(DirectoryPath);
                var bytes = Encoding.UTF8.GetBytes(content);
                using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            catch (Exception ex)
            {
                CrashDiag.Degrade($"LastWillWriter 写 {Path.GetFileName(path)} 失败: {ex.Message}");
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                CrashDiag.Degrade($"LastWillWriter 删除 {Path.GetFileName(path)} 失败: {ex.Message}");
            }
        }
    }
}
