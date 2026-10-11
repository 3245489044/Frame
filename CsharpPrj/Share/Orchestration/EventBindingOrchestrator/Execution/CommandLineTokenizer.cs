
#nullable enable
using System.Collections.Generic;
using System.Text;

namespace Share.EventBindingOrchestrator.Execution;

/// <summary>
/// 命令行分词（唯一实现 · D2 收敛）：空白分隔；双引号包裹为单 token（去引号，支持含空格值）。
/// 供命令条（UIToolbox）与 exec DSL（InitCommandExecutor）共用，消除两套分词差异。
/// </summary>
public static class CommandLineTokenizer
{
    /// <summary>按引号感知规则分词；引号外空白作分隔，引号内原样保留（含空白）。</summary>
    public static List<string> Tokenize(string? commandLine)
    {
        var tokens = new List<string>();
        if (string.IsNullOrEmpty(commandLine))
            return tokens;

        var sb = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in commandLine)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(ch))
            {
                if (sb.Length > 0)
                {
                    tokens.Add(sb.ToString());
                    sb.Clear();
                }
                continue;
            }

            sb.Append(ch);
        }

        if (sb.Length > 0)
            tokens.Add(sb.ToString());

        return tokens;
    }
}
