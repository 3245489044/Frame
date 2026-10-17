using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CallCenter.Cost.Metering;

/// <summary>Windows Job Object 计量原语（kernel32 P/Invoke；net10.0-windows）。</summary>
internal static class JobObjectNative
{
    private const int JobObjectBasicAndIoAccountingInformationClass = 8;
    private const int JobObjectExtendedLimitInformationClass = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        SafeJobHandle hJob, int jobObjectInformationClass, ref JobObjectExtendedLimitInformation lpJobObjectInformation, uint cbJobObjectInformationLength);

    [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", SetLastError = true)]
    private static extern bool QueryBasicAccounting(
        SafeJobHandle hJob, int jobObjectInformationClass, out JobObjectBasicAndIoAccountingInformation lpJobObjectInformation,
        uint cbJobObjectInformationLength, IntPtr lpReturnLength);

    [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", SetLastError = true)]
    private static extern bool QueryExtendedLimit(
        SafeJobHandle hJob, int jobObjectInformationClass, out JobObjectExtendedLimitInformation lpJobObjectInformation,
        uint cbJobObjectInformationLength, IntPtr lpReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeJobHandle hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(SafeJobHandle hJob, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    public static SafeJobHandle? TryCreateKillOnClose()
    {
        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var job = new SafeJobHandle(handle);
        var info = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose,
            },
        };

        if (!SetInformationJobObject(job, JobObjectExtendedLimitInformationClass, ref info, (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
        {
            job.Dispose();
            return null;
        }

        return job;
    }

    public static bool Assign(SafeJobHandle job, IntPtr processHandle)
        => processHandle != IntPtr.Zero && AssignProcessToJobObject(job, processHandle);

    public static void Terminate(SafeJobHandle job, uint exitCode = 1)
    {
        try { TerminateJobObject(job, exitCode); } catch { /* 回收失败不阻断主流程 */ }
    }

    public static JobAccounting? Query(SafeJobHandle job)
    {
        try
        {
            if (!QueryBasicAccounting(job, JobObjectBasicAndIoAccountingInformationClass, out var basic,
                    (uint)Marshal.SizeOf<JobObjectBasicAndIoAccountingInformation>(), IntPtr.Zero))
            {
                return null;
            }

            long peakJob = 0;
            long peakWorkingSet = 0;
            if (QueryExtendedLimit(job, JobObjectExtendedLimitInformationClass, out var extended,
                    (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>(), IntPtr.Zero))
            {
                peakJob = (long)extended.PeakJobMemoryUsed.ToUInt64();
                peakWorkingSet = (long)extended.PeakProcessMemoryUsed.ToUInt64();
            }

            return new JobAccounting(
                basic.TotalUserTime,
                basic.TotalKernelTime,
                (long)basic.IoInfo.ReadTransferCount,
                (long)basic.IoInfo.WriteTransferCount,
                (int)basic.TotalProcesses,
                (int)basic.ActiveProcesses,
                peakJob,
                peakWorkingSet);
        }
        catch
        {
            return null;
        }
    }

    public sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobHandle(IntPtr handle) : base(ownsHandle: true) => SetHandle(handle);

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicAndIoAccountingInformation
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
        public IoCounters IoInfo;
    }
}

/// <summary>Job Object 计量快照（100ns 时间单位）。</summary>
internal sealed record JobAccounting(
    long UserTime100Ns,
    long KernelTime100Ns,
    long ReadBytes,
    long WriteBytes,
    int TotalProcesses,
    int ActiveProcesses,
    long PeakJobMemoryBytes,
    long PeakWorkingSetBytes)
{
    public long UserMs => UserTime100Ns / 10_000;
    public long KernelMs => KernelTime100Ns / 10_000;
}
