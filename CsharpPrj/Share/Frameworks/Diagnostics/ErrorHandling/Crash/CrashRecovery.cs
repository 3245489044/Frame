using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Common.Diagnostics
{
    /// <summary>
    /// 下启动自检（聚拢点）：run.alive 残留 ⇒ 上次非正常退出 ⇒ 读 crash-state.json 产恰一条 CrashSummary。
    /// 只报一次（进程内幂等）；无残留即静默返回。
    /// </summary>
    public static class CrashRecovery
    {
        private static readonly JsonSerializerOptions _opt = new() { PropertyNameCaseInsensitive = true };

        private static int _reported;

        public static CrashState? CheckAndReport(out string? summary)
        {
            summary = null;
            if (System.Threading.Interlocked.Exchange(ref _reported, 1) == 1) return null;

            var alive = LastWillWriter.AlivePath;
            if (!File.Exists(alive)) return null;

            CrashState? state = null;
            try
            {
                var statePath = LastWillWriter.StatePath;
                if (File.Exists(statePath))
                {
                    state = JsonSerializer.Deserialize<CrashState>(
                        File.ReadAllText(statePath, Encoding.UTF8), _opt);
                }
            }
            catch (Exception ex)
            {
                CrashDiag.Degrade($"CrashRecovery 读取 crash-state 失败: {ex.Message}");
            }

            summary = state == null
                ? "上次异常退出（无 crash-state 快照）: phase=?"
                : $"上次异常退出: phase={state.Phase}, detail={state.Detail}, startedUtc={state.StartedUtc:O}, updatedUtc={state.UpdatedUtc:O}, pid={state.ProcessId}, recentPhases={(state.RecentPhases.Count == 0 ? "(none)" : string.Join(" -> ", state.RecentPhases))}";

            try
            {
                CrashStore.Append(new CrashRecord
                {
                    Kind = CrashKind.Halt,
                    Phase = state?.Phase,
                    Source = "CrashRecovery",
                    Message = summary
                });

                Console.WriteLine("[CrashSummary_previous_abnormal_exit] " + summary);
                File.Delete(alive);
            }
            catch (Exception ex)
            {
                CrashDiag.Degrade($"CrashRecovery 归档失败: {ex.Message}");
            }

            return state;
        }
    }
}
