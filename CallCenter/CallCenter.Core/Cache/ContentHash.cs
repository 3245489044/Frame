using System;
using System.IO;

namespace CallCenter.Core.Cache;

/// <summary>文件内容指纹（SHA-256 → 十六进制）单一实现（A5 收敛）。宿主 watch/编译轨/测试共用，
/// 禁止各处各自实现「哈希→Hex」。</summary>
public static class ContentHash
{
    public static string Compute(string filePath)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var fs = File.OpenRead(filePath);
        return Convert.ToHexString(sha.ComputeHash(fs));
    }

    /// <summary>对已读入内存的字节快照计算指纹（C5：watch 先一次读入字节，指纹与解析同源该快照，避免二次打开不一致）。</summary>
    public static string Compute(byte[] data)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(data));
    }
}
