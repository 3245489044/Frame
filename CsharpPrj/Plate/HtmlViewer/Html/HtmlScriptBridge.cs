#nullable enable

using System.Text.Json;
using Plate.JsonMethod.Serialization;

namespace UiBackend.Html;

/// <summary>脚本执行结果：逐条命令输出行。</summary>
public sealed record HtmlScriptResult(IReadOnlyList<string> Lines)
{
    public string ToText() => string.Join(Environment.NewLine, Lines);
}

/// <summary>
/// HTML 脚本桥——把 04-json-script 命令协议（exec/set/click/get）落到 HTML 原生元素：
/// set → DOM 写；get → DOM 读回；click → dispatchEvent 触发事件桥 → C# 函数；exec → 调注册函数并写回 output。
/// 白名单安全：仅受控 DOM 操作，命令解析在 C# 侧，值按数据处理，不执行任意 JS。
/// </summary>
public sealed class HtmlScriptBridge
{
    private readonly IHtmlChannel _channel;
    private readonly HtmlEventBridge.HtmlFunctionInvoker _invoker;

    public HtmlScriptBridge(IHtmlChannel channel, HtmlEventBridge.HtmlFunctionInvoker invoker)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
    }

    /// <summary>逐条执行命令，返回全部输出行；单条失败不中断后续（对齐脚本整体结果语义）。</summary>
    public async Task<HtmlScriptResult> ExecuteAsync(IEnumerable<HtmlScriptCommand> commands, CancellationToken ct = default)
    {
        var lines = new List<string>();
        foreach (var cmd in commands)
        {
            ct.ThrowIfCancellationRequested();
            lines.Add(await ExecuteOneAsync(cmd, ct).ConfigureAwait(false));
        }
        return new HtmlScriptResult(lines);
    }

    private async Task<string> ExecuteOneAsync(HtmlScriptCommand cmd, CancellationToken ct)
    {
        var action = cmd.Action?.Trim().ToLowerInvariant() ?? string.Empty;
        switch (action)
        {
            case DomOps.Set:
                await _channel.ExecuteAsync(DomOps.Set, cmd.Target, cmd.Value).ConfigureAwait(false);
                return $"[set] {cmd.Target} = {cmd.Value}";

            case DomOps.Get:
                var raw = await _channel.ExecuteAsync(DomOps.Get, cmd.Target, null).ConfigureAwait(false);
                return $"[get] {cmd.Target} = {HtmlElementNode.UnwrapString(raw)}";

            case DomOps.Click:
                // 返回 __domBridge.handle 的真实命中结果（OK/NOT_FOUND），不再无条件成功——
                // 消除「元素缺失/桥未通仍显示成功」的假阳性（对齐 realclick 的 OK/NOT_FOUND/NOT_VISIBLE 语义）。
                var clickRaw = await _channel.ExecuteAsync(DomOps.Click, cmd.Target, null).ConfigureAwait(false);
                var probe = "n/a";
                try { probe = HtmlElementNode.UnwrapString(await _channel.ExecuteScriptAsync("JSON.stringify({installed:!!document.__domBridgeInstalled,chrome:!!(window.chrome&&window.chrome.webview),ready:document.readyState})", ct).ConfigureAwait(false)); } catch { }
                return $"[click] {cmd.Target} → {HtmlElementNode.UnwrapString(clickRaw)} | probe={probe}";

            case DomOps.RealClick:
                // 真实鼠标点击（宿主经 Win32 SendInput 注入，isTrusted=true）；返回桥的真实命中结果（OK/NOT_FOUND/NOT_VISIBLE），
                // 避免「元素缺失仍显示成功」的假阳性，使日志可核对元素是否找到/可见/坐标解析。
                var rc = await _channel.ExecuteAsync(DomOps.RealClick, cmd.Target, null).ConfigureAwait(false);
                return $"[realclick] {cmd.Target} → {rc}";

            case ScriptCommand.Exec:
                var parameters = ParseKeyValues(cmd.Value);
                var result = await _invoker(cmd.Target ?? string.Empty, parameters).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(cmd.Output))
                    await _channel.ExecuteAsync(DomOps.Set, cmd.Output, result).ConfigureAwait(false);
                return $"[exec] {cmd.Target} → {result}";

            case ScriptCommand.Sleep:
                var ms = int.TryParse(cmd.Target ?? cmd.Value, out var v) ? v : 500;
                await Task.Delay(ms, ct).ConfigureAwait(false);
                return $"[sleep] {ms}ms";

            default:
                return $"[{cmd.Action}] 不支持（HTML 桥命令白名单: set/get/click/exec/sleep）";
        }
    }

    /// <summary>把 04-json-script 脚本 JSON 解析为命令列表（统一经 Plate.JsonMethod 的 ScriptCommand 选项，大小写不敏感 + 宽松容错）。
    /// 兼容两种输入格式：整脚本对象（<c>{description, script, commands:[...]}</c>，自动解包 <c>.commands</c>）或裸命令数组。
    /// 并做 <c>{template}</c> 命令展开（通用层唯一实现，替代旧 Agent 侧私有 ParseScriptCommands）：
    /// <c>echo-section</c> → Action=echo, Value=title；其余模板 → Action=模板名。非法/空 → 空列表。</summary>
    public static IReadOnlyList<HtmlScriptCommand> ParseCommands(string json)
    {
        var arrJson = UnwrapCommandsArray(json);
        if (arrJson is null) return Array.Empty<HtmlScriptCommand>();
        List<HtmlScriptCommand> cmds;
        try
        {
            cmds = JsonSerializer.Deserialize<List<HtmlScriptCommand>>(arrJson, JsonSerializerFactory.Contexts.ScriptCommand)
                   ?? new List<HtmlScriptCommand>();
        }
        catch
        {
            return Array.Empty<HtmlScriptCommand>();
        }
        return ExpandTemplates(cmds);
    }

    /// <summary>提取命令数组原始 JSON：输入为裸数组原样返回；为 <c>{commands:[...]}</c> 对象时返回其 <c>commands</c> 原始文本；非法 → null。</summary>
    private static string? UnwrapCommandsArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                return json;
            if (doc.RootElement.TryGetProperty("commands", out var arr) && arr.ValueKind == JsonValueKind.Array)
                return arr.GetRawText();
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>{template} 命令展开：echo-section → echo 命令（Value=title）；其余模板命令名 → 转为其 action 名。</summary>
    private static IReadOnlyList<HtmlScriptCommand> ExpandTemplates(List<HtmlScriptCommand> cmds)
    {
        var result = new List<HtmlScriptCommand>(cmds.Count);
        foreach (var cmd in cmds)
        {
            if (string.IsNullOrWhiteSpace(cmd.Template))
            {
                result.Add(cmd);
                continue;
            }
            if (string.Equals(cmd.Template, "echo-section", StringComparison.OrdinalIgnoreCase))
                result.Add(new HtmlScriptCommand { Action = "echo", Value = cmd.Title ?? cmd.Template });
            else
                result.Add(new HtmlScriptCommand { Action = cmd.Template, Target = cmd.Target });
        }
        return result;
    }

    /// <summary>解析 exec 的 key=value 参数串（空格分隔，值含空格用引号包裹）→ 命名参数字典。</summary>
    public static Dictionary<string, object?> ParseKeyValues(string? value)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value)) return dict;

        foreach (var token in Tokenize(value))
        {
            var eq = token.IndexOf('=');
            if (eq <= 0) continue;
            var key = token[..eq].Trim();
            var val = token[(eq + 1)..].Trim().Trim('"', '\'');
            if (key.Length > 0)
                dict[key] = val;
        }
        return dict;
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuote = false;
        foreach (var ch in value)
        {
            if (ch == '"')
            {
                inQuote = !inQuote;
                current.Append(ch);
            }
            else if (ch == ' ' && !inQuote)
            {
                if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
            }
            else
            {
                current.Append(ch);
            }
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }
}
