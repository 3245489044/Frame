using System;
using System.IO;
using System.Text;

namespace Share.LogicComposition.Conversion.Hosting;

/// <summary>
/// 逻辑写盘原语（宿主无关）：「内容未变不写盘 + 临时文件原子覆盖 + UTF-8 无 BOM」。
/// 供 MSBuild Task / CLI 等写盘宿主复用，保证时间戳稳定（配合增量构建 Inputs/Outputs）。
/// </summary>
public static class LogicalFileWriter
{
    /// <summary>换行归一（CRLF → LF），用于「内容是否变化」的逻辑比较。</summary>
    public static string Normalize(string text) => (text ?? string.Empty).Replace("\r\n", "\n");

    /// <summary>内容相同则不写盘（返回 false）；否则写入并返回 true。</summary>
    /// <param name="target">目标路径。</param>
    /// <param name="text">待写文本。</param>
    public static bool WriteIfChanged(string target, string text)
    {
        if (string.IsNullOrEmpty(target)) throw new ArgumentException("目标路径不能为空", nameof(target));

        if (File.Exists(target) && Normalize(File.ReadAllText(target)) == Normalize(text)) return false;

        var directory = Path.GetDirectoryName(Path.GetFullPath(target));
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory!);

        // 同目录临时文件 + 原子替换：避免跨卷复制与「先删后写」造成的半成品窗口
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".write.tmp";

        try
        {
            File.WriteAllText(temporary, text ?? string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (File.Exists(target)) File.Replace(temporary, target, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else File.Move(temporary, target);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        return true;
    }
}
