using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Plate.PowerShellHost;

/// <summary>
/// Windows App Execution Alias（IO_REPARSE_TAG_APPEXECLINK）解析：把 0 字节别名物化为真实可执行路径。
/// <para>
/// 判据载体 = reparse buffer 内的 ExecutablePath（UTF-16），不依赖目录枚举（WindowsApps 对非打包进程常被 ACL 拒绝）。
/// 本类是 AppExecLink 语义的唯一判定点；调用方不得再按“0 字节 + WindowsApps 路径”猜可用性。
/// </para>
/// </summary>
public static class WindowsAppExecLink
{
    public const uint AppExecLinkTag = 0x8000001B;

    private const uint FsctlGetReparsePoint = 0x000900A8;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileShareAll = 0x00000007;
    private const uint OpenExisting = 3;
    private const int MaxReparseBuffer = 16 * 1024;

    /// <summary>读取 alias 的真实目标；非 AppExecLink / 解析失败 ⇒ false（不抛）。</summary>
    public static bool TryReadTarget(string path, out string target)
    {
        target = string.Empty;
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path)) return false;

        IntPtr buffer = IntPtr.Zero;
        try
        {
            using var handle = CreateFileW(
                path,
                0,
                FileShareAll,
                IntPtr.Zero,
                OpenExisting,
                FileFlagOpenReparsePoint,
                IntPtr.Zero);

            if (handle.IsInvalid) return false;

            buffer = Marshal.AllocHGlobal(MaxReparseBuffer);
            if (!DeviceIoControl(
                    handle,
                    FsctlGetReparsePoint,
                    IntPtr.Zero,
                    0,
                    buffer,
                    MaxReparseBuffer,
                    out var bytesReturned,
                    IntPtr.Zero)
                || bytesReturned == 0)
            {
                return false;
            }

            var data = new byte[bytesReturned];
            Marshal.Copy(buffer, data, 0, (int)bytesReturned);
            return TryParseTarget(data, out target);
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (ArgumentException) { return false; }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>解析 reparse buffer 中的 AppExecLink 目标（公开给单测：合成 buffer 可测）。</summary>
    public static bool TryParseTarget(ReadOnlySpan<byte> buffer, out string target)
    {
        target = string.Empty;
        if (buffer.Length < 16) return false;

        var tag = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        if (tag != AppExecLinkTag) return false;

        var dataLength = BinaryPrimitives.ReadUInt16LittleEndian(buffer[4..]);
        const int dataStart = 8;
        if (dataLength < 4 || buffer.Length < dataStart + dataLength) return false;

        var end = dataStart + dataLength;
        var position = dataStart + 4; // Version (uint)

        // PackageId / EntryPoint / ExecutablePath 三个 null-terminated UTF-16 字符串。
        if (!TryReadNullTerminatedUtf16(buffer, end, ref position, out _)) return false;
        if (!TryReadNullTerminatedUtf16(buffer, end, ref position, out _)) return false;
        if (!TryReadNullTerminatedUtf16(buffer, end, ref position, out var executable)) return false;

        if (string.IsNullOrWhiteSpace(executable)) return false;
        target = executable;
        return true;
    }

    private static bool TryReadNullTerminatedUtf16(
        ReadOnlySpan<byte> buffer,
        int end,
        ref int position,
        out string value)
    {
        value = string.Empty;
        var start = position;
        while (position + 1 < end)
        {
            if (buffer[position] == 0 && buffer[position + 1] == 0)
            {
                value = Encoding.Unicode.GetString(buffer[start..position]);
                position += 2;
                return true;
            }

            position += 2;
        }

        return false;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);
}
