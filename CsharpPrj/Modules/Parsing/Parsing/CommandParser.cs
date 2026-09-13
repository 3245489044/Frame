using System.Globalization;
using Modules.Parsing.Models;
using Plate.JsonMethod.Serialization;
using Share.LogicComposition.Conversion.Lexing;

namespace Modules.Parsing.Parsing;

public static class CommandParser
{
    public static Models.MetaCommand Parse(string command) => ParseCore(command, out _);

    /// <summary>
    /// 解析命令并**显式回报**格式级问题（未闭合引号 → <see cref="ErrorCode.UnclosedQuote"/>；
    /// JSON 片段非法 → <see cref="ErrorCode.SerializationError"/>）；是否升级为失败由调用方裁决。
    /// </summary>
    /// <param name="command">原始命令行。</param>
    /// <param name="parseError">out：格式级问题；<see cref="ErrorCode.None"/> 表示无。</param>
    public static Models.MetaCommand TryParse(string command, out ErrorCode parseError)
        => ParseCore(command, out parseError);

    private static Models.MetaCommand ParseCore(string command, out ErrorCode parseError)
    {
        parseError = ErrorCode.None;

        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Command cannot be null or empty", nameof(command));

        var normalized = CommandNormalizer.Normalize(command);

        if (string.IsNullOrEmpty(normalized))
            return null;

        var effectiveCommand = normalized;

        var tokenized = new CommandTokenizer(effectiveCommand).TokenizeWithDiagnostics();
        if (tokenized.HasUnterminatedQuote) parseError = ErrorCode.UnclosedQuote;

        var tokens = new List<string>(tokenized.Tokens);

        if (tokens.Count == 0)
        {
            return new Models.MetaCommand
            {
                Verb = MetaVerb.Do,
                Action = "noop",
                OriginalCommand = command,
                CommandId = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow
            };
        }

        var verbStr = tokens[0].ToLower();
        if (!Enum.TryParse<MetaVerb>(verbStr, true, out var verb))
        {
            var doCommand = ParseAsDoCommand(tokens, effectiveCommand, out var doError);
            if (parseError == ErrorCode.None) parseError = doError;
            return doCommand;
        }

        var cmd = new Models.MetaCommand
        {
            Verb = verb,
            OriginalCommand = command,
            CommandId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };

        if (tokens.Count > 1)
        {
            if (verb == MetaVerb.Do)
            {
                cmd.Action = tokens[1];
                if (tokens.Count > 2)
                {
                    ParseDoTargetAndParameters(tokens, cmd, ref parseError);
                }
            }
            else
            {
                cmd.Target = tokens[1];
            }
        }

        return cmd;
    }

    private static void ParseDoTargetAndParameters(List<string> tokens, Models.MetaCommand cmd, ref ErrorCode parseError)
    {
        var targetParts = new List<string>();
        var paramParts = new List<string>();
        bool jsonFound = false;

        for (int i = 2; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (token.StartsWith("{") || token.StartsWith("["))
            {
                var (jsonStr, endIndex) = MergeJsonTokens(tokens, i, out var jsonError);
                if (jsonError != ErrorCode.None && parseError == ErrorCode.None) parseError = jsonError;
                if (!string.IsNullOrEmpty(jsonStr))
                {
                    if (cmd.Parameters == null)
                        cmd.Parameters = new Dictionary<string, object>();
                    cmd.Parameters["_json"] = jsonStr;
                    jsonFound = true;
                    i = endIndex;
                    continue;
                }
            }

            if (cmd.Action == "set" && targetParts.Count == 0)
            {
                targetParts.Add(token);
            }
            else if (token.Contains('='))
            {
                paramParts.Add(token);
            }
            else
            {
                if (cmd.Action == "set")
                {
                    if (cmd.Parameters == null)
                        cmd.Parameters = new Dictionary<string, object>();
                    cmd.Parameters[token] = true;
                }
                else
                {
                    targetParts.Add(token);
                }
            }
        }

        if (targetParts.Count > 0)
            cmd.Target = string.Join(" ", targetParts);

        if (paramParts.Count > 0)
        {
            var parameters = ParameterParser.ParseParameters(paramParts, 0);
            if (parameters.Count > 0)
            {
                if (cmd.Parameters == null)
                    cmd.Parameters = new Dictionary<string, object>();
                foreach (var kv in parameters)
                {
                    cmd.Parameters[kv.Key] = kv.Value;
                }
            }
        }

        if (cmd.Target == null && !jsonFound && tokens.Count > 2)
        {
            cmd.Target = tokens[2];
        }
    }

    private static Models.MetaCommand ParseAsDoCommand(List<string> tokens, string originalCommand, out ErrorCode parseError)
    {
        parseError = ErrorCode.None;

        var cmd = new Models.MetaCommand
        {
            Verb = MetaVerb.Do,
            OriginalCommand = originalCommand,
            CommandId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };

        cmd.Action = tokens[0];

        if (tokens.Count > 1)
        {
            ParseDoTargetAndParameters(tokens, cmd, ref parseError);
        }

        return cmd;
    }

    public static (string mergedJson, int endIndex) MergeJsonTokens(List<string> tokens, int startIndex)
        => MergeJsonTokens(tokens, startIndex, out _);

    /// <summary>合并跨 token 的 JSON 片段（<paramref name="jsonError"/> 回报片段非法，不抛异常）。</summary>
    public static (string mergedJson, int endIndex) MergeJsonTokens(List<string> tokens, int startIndex, out ErrorCode jsonError)
    {
        jsonError = ErrorCode.None;

        if (startIndex >= tokens.Count)
            return (string.Empty, startIndex);

        var firstToken = tokens[startIndex];
        if (!firstToken.StartsWith("{") && !firstToken.StartsWith("["))
        {
            return (tokens[startIndex], startIndex);
        }

        var openChar = firstToken[0];
        var closeChar = openChar == '{' ? '}' : ']';
        int depth = 0;
        var jsonParts = new List<string>();
        int endIndex = startIndex;

        for (int i = startIndex; i < tokens.Count; i++)
        {
            var token = tokens[i];
            jsonParts.Add(token);
            endIndex = i;

            // 括号深度（跳过字符串内括号）由共享内核原语给出：与 RoslynJson 前端 / 分析器侧同一实现
            depth += TextScan.BracketDepth(token, openChar, closeChar);

            if (depth == 0)
                break;
        }

        var merged = string.Join("", jsonParts);

        if (openChar == '{' && !string.IsNullOrEmpty(merged))
        {
            if (!TryRestoreJsonObject(merged, out merged, out _)) jsonError = ErrorCode.SerializationError;
        }

        return (merged, endIndex);
    }

    /// <summary>
    /// 规范化 JSON 对象文本：**解析权威 = <c>Plate.JsonMethod</c>**
    /// （合法 → 规范化文本；非法 → 原文 + false）。
    /// 不再手写 key/value 扫描与类型猜测，避免篡改作者原文（对齐 ANTI-FAILURE-REUSE）。
    /// </summary>
    /// <remarks>
    /// 已知限制：<see cref="CommandTokenizer"/> 会剥离双引号（命令参数语义需要），故含字符串值的
    /// 内嵌 JSON 经 token 合并后已不是合法 JSON —— 此时回报 <c>false</c>/<see cref="ErrorCode.SerializationError"/>
    /// 而非猜测补引号；需要保留引号时应改由「按 token 偏移切原文」取得 JSON 片段（后续演进）。
    /// </remarks>
    /// <param name="json">待规范化的 JSON 文本。</param>
    /// <param name="restored">out：规范化文本；非法时为原文。</param>
    /// <param name="error">out：失败原因（合法/空输入为 null）。</param>
    public static bool TryRestoreJsonObject(string json, out string restored, out string? error)
    {
        restored = json ?? string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(json)) return true;

        if (JsonSerializationService.Instance.TryParseNode(json, out var node) && node is not null)
        {
            restored = node.ToJsonString();
            return true;
        }

        error = "JSON 对象片段非法";
        return false;
    }

    /// <summary>规范化 JSON 对象文本（非法即原样返回；需要错误码时用 <see cref="TryRestoreJsonObject"/>）。</summary>
    public static string RestoreJsonObject(string json)
        => TryRestoreJsonObject(json, out var restored, out _) ? restored : json;
}
