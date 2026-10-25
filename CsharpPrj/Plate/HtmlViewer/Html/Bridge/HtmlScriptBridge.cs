#nullable enable

using System.Text.Json;
using Modules.Parsing.Parsing;
using Plate.JsonMethod.Serialization;

namespace UiBackend.Html;

/// <summary>脚本执行结果：逐条命令输出行 + 不支持命令计数（E12：白名单外命令不得算成功）。</summary>
public sealed record HtmlScriptResult(IReadOnlyList<string> Lines, int UnsupportedCount = 0)
{
    public bool HasUnsupported => UnsupportedCount > 0;

    public string ToText() => string.Join(Environment.NewLine, Lines);
}

/// <summary>
/// HTML 脚本桥——把 04-json-script 命令协议落到 HTML 原生元素：
/// set → DOM 写；get → DOM 读回；click → dispatchEvent 触发事件桥 → C# 函数；realclick → 真实鼠标点击（SendInput）；
/// exec → 调注册函数并写回 output（sleep 由宿主侧处理）。
/// 白名单安全：仅受控 DOM 操作，命令解析在 C# 侧，值按数据处理，不执行任意 JS。
/// </summary>
public sealed class HtmlScriptBridge
{
    /// <summary>脚本 exec 的函数调用端口（文本结果；与事件桥的类型化端口区分——脚本仅取文本叙述）。</summary>
    public delegate Task<string> HtmlScriptInvoker(string functionName, IReadOnlyDictionary<string, object?> parameters);

    private readonly IHtmlChannel _channel;
    private readonly HtmlScriptInvoker _invoker;
    private readonly IHtmlAssetProvider _assets;

    /// <summary>兼容重载（二进制稳定）：保留旧 2 参构造签名。</summary>
    public HtmlScriptBridge(IHtmlChannel channel, HtmlScriptInvoker invoker)
        : this(channel, invoker, null)
    {
    }

    public HtmlScriptBridge(IHtmlChannel channel, HtmlScriptInvoker invoker, IHtmlAssetProvider? assets)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _assets = assets ?? HtmlAssetRegistry.Default;
    }

    /// <summary>逐条执行命令，返回全部输出行；单条失败不中断后续（对齐脚本整体结果语义）。
    /// E12：不支持的命令计入 <see cref="HtmlScriptResult.UnsupportedCount"/>（可判定失败，不再静默成功）。</summary>
    public async Task<HtmlScriptResult> ExecuteAsync(IEnumerable<HtmlScriptCommand> commands, CancellationToken ct = default)
    {
        var lines = new List<string>();
        var unsupported = 0;
        foreach (var cmd in commands)
        {
            ct.ThrowIfCancellationRequested();
            var (line, isUnsupported) = await ExecuteOneAsync(cmd, ct).ConfigureAwait(false);
            lines.Add(line);
            if (isUnsupported) unsupported++;
        }
        return new HtmlScriptResult(lines, unsupported);
    }

    private async Task<(string Line, bool Unsupported)> ExecuteOneAsync(HtmlScriptCommand cmd, CancellationToken ct)
    {
        var action = cmd.Action?.Trim().ToLowerInvariant() ?? string.Empty;
        switch (action)
        {
            case DomOps.Set:
                await _channel.ExecuteAsync(DomOps.Set, cmd.Target, cmd.Value).ConfigureAwait(false);
                return ($"[set] {cmd.Target} = {cmd.Value}", false);

            case DomOps.Get:
                var raw = await _channel.ExecuteAsync(DomOps.Get, cmd.Target, null).ConfigureAwait(false);
                return ($"[get] {cmd.Target} = {HtmlElementNode.UnwrapString(raw)}", false);

            case DomOps.Click:
                // 返回 __domBridge.handle 的真实命中结果（OK/NOT_FOUND），不再无条件成功——
                // 消除「元素缺失/桥未通仍显示成功」的假阳性（对齐 realclick 的 OK/NOT_FOUND/NOT_VISIBLE 语义）。
                var clickRaw = await _channel.ExecuteAsync(DomOps.Click, cmd.Target, null).ConfigureAwait(false);
                var probe = "n/a";
                try
                {
                    // 复用资产层诊断模板（与 BridgeDiagnosisTemplate 单一来源，消除探针串漂移）；经端口取，不直连静态成员。
                    probe = HtmlElementNode.UnwrapString(
                        await _channel.ExecuteScriptAsync(_assets.GetTemplate(HtmlAssetKeys.BridgeDiagnosisTemplate), ct).ConfigureAwait(false));
                }
                catch (Exception ex)
                {
                    probe = $"err:{ex.GetType().Name}";
                }
                return ($"[click] {cmd.Target} → {HtmlElementNode.UnwrapString(clickRaw)} | probe={probe}", false);

            case DomOps.RealClick:
                // 真实鼠标点击（宿主经 Win32 SendInput 注入，isTrusted=true）；返回桥的真实命中结果（OK/NOT_FOUND/NOT_VISIBLE），
                // 避免「元素缺失仍显示成功」的假阳性，使日志可核对元素是否找到/可见/坐标解析。
                var rc = await _channel.ExecuteAsync(DomOps.RealClick, cmd.Target, null).ConfigureAwait(false);
                return ($"[realclick] {cmd.Target} → {rc}", false);

            case ScriptCommand.Exec:
                var parameters = ParseKeyValues(cmd.Value);
                var result = await _invoker(cmd.Target ?? string.Empty, parameters).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(cmd.Output))
                    await _channel.ExecuteAsync(DomOps.Set, cmd.Output, result).ConfigureAwait(false);
                return ($"[exec] {cmd.Target} → {result}", false);

            case ScriptCommand.Sleep:
                var ms = int.TryParse(cmd.Target ?? cmd.Value, out var v) ? v : 500;
                await Task.Delay(ms, ct).ConfigureAwait(false);
                return ($"[sleep] {ms}ms", false);

            default:
                // E12：白名单外命令 = 可判定失败（计入 UnsupportedCount），不再以文本行冒充成功。
                return ($"[{cmd.Action}] 不支持（HTML 桥命令白名单: set/get/click/realclick/exec/sleep）", true);
        }
    }

    /// <summary>把 04-json-script 脚本 JSON 解析为命令列表（统一经 Plate.JsonMethod 的 ScriptCommand 选项，大小写不敏感 + 宽松容错）。
    /// 兼容两种输入格式：整脚本对象（<c>{description, script, commands:[...]}</c>，自动解包 <c>.commands</c>）或裸命令数组。
    /// 并做 <c>{template}</c> 命令展开（通用层唯一实现，替代旧 Agent 侧私有 ParseScriptCommands）：
    /// <c>echo-section</c> → Action=echo, Value=title；其余模板 → Action=模板名。非法/空 → 空列表。</summary>
    public static IReadOnlyList<HtmlScriptCommand> ParseCommands(string json, Action<string, string>? diagnostics = null)
    {
        var arrJson = UnwrapCommandsArray(json);
        if (arrJson is null) return Array.Empty<HtmlScriptCommand>();
        List<HtmlScriptCommand> cmds;
        try
        {
            cmds = JsonSerializer.Deserialize<List<HtmlScriptCommand>>(arrJson, JsonSerializerFactory.Contexts.ScriptCommand)
                   ?? new List<HtmlScriptCommand>();
        }
        catch (Exception ex)
        {
            // 解析失败可见化（不再静默返回空列表）：脚本 JSON 非法 / 结构漂移须可诊断。
            diagnostics?.Invoke("ScriptBridge_parse", $"命令解析失败[{ex.GetType().Name}]: {ex.Message}");
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

    /// <summary>{template} 命令展开（D7 单一来源 = <see cref="ScriptTemplateCatalog"/>）：
    /// 已知模板 → 规范化 action/target/value；未知模板 → 保留模板名（执行期计入 UnsupportedCount，显式失败）。</summary>
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

            if (!ScriptTemplateCatalog.TryResolve(cmd.Template, out var spec))
            {
                result.Add(new HtmlScriptCommand { Action = cmd.Template, Target = cmd.Target, Value = cmd.Value });
                continue;
            }

            result.Add(new HtmlScriptCommand
            {
                Action = spec.Action,
                Target = spec.LiteralTarget ?? cmd.Target,
                Value = spec.ValueKind == TemplateValueKind.Title ? cmd.Title : cmd.Value,
                Output = cmd.Output
            });
        }
        return result;
    }

    /// <summary>解析 exec 的 key=value 参数串（D6 单一实现 = <see cref="KeyValueArguments"/>）。</summary>
    public static Dictionary<string, object?> ParseKeyValues(string? value)
        => KeyValueArguments.ParseObjects(value);
}
