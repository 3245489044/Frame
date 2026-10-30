#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Plate.HtmlViewer;

namespace UiBackend.Html;

/// <summary>
/// DOM 脚本白名单执行器（通用件，唯一实现）—— 04-json-script 命令（set/get/click/realclick/sleep）
/// 对指定 htmlviewer 直接经 <see cref="HtmlViewer.ExecuteDomAsync"/> 执行；不装配运行时、不接管消息桥。
/// <para>命令解析经 <see cref="HtmlScriptBridge.ParseCommands"/>（单一真源）；命令级失败抛型（不把判据失败写成成功文本）。</para>
/// </summary>
public static class HtmlDomCommandRunner
{
    /// <summary>执行整脚本；返回逐条结果行（失败抛 <see cref="InvalidOperationException"/>，含已执行行上下文）。</summary>
    public static async Task<string> ExecuteAsync(HtmlViewer viewer, string scriptJson)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        var commands = HtmlScriptBridge.ParseCommands(scriptJson);
        if (commands.Count == 0)
            return "（脚本为空或解析失败）";

        var lines = new List<string>();
        foreach (var cmd in commands)
        {
            var action = cmd.Action?.Trim().ToLowerInvariant() ?? string.Empty;
            switch (action)
            {
                case DomOps.Set:
                {
                    var status = await viewer.ExecuteDomAsync(DomOps.Set, cmd.Target ?? string.Empty, cmd.Value)
                        .ConfigureAwait(false);
                    var text = HtmlElementNode.UnwrapString(status);
                    if (!DomOps.IsOk(text))
                        throw new InvalidOperationException($"[set] {cmd.Target} → {text}");
                    lines.Add($"[set] {cmd.Target} = {cmd.Value}");
                    break;
                }

                case DomOps.Get:
                {
                    var raw = await viewer.ExecuteDomAsync(DomOps.Get, cmd.Target ?? string.Empty, null)
                        .ConfigureAwait(false);
                    lines.Add($"[get] {cmd.Target} = {HtmlElementNode.UnwrapString(raw)}");
                    break;
                }

                case DomOps.Click:
                case DomOps.RealClick:
                {
                    var raw = await viewer.ExecuteDomAsync(action, cmd.Target ?? string.Empty, null)
                        .ConfigureAwait(false);
                    var text = HtmlElementNode.UnwrapString(raw);
                    lines.Add($"[{action}] {cmd.Target} → {text}");
                    var failed = action == DomOps.RealClick
                        ? ClickOutcomes.IsFailure(ClickOutcomes.Parse(text))
                        : !DomOps.IsOk(text);
                    if (failed)
                        throw new InvalidOperationException(string.Join(Environment.NewLine, lines));
                    break;
                }

                case "sleep":
                {
                    var ms = int.TryParse(cmd.Target ?? cmd.Value, out var parsed) && parsed > 0 ? parsed : 200;
                    await Task.Delay(ms).ConfigureAwait(false);
                    lines.Add($"[sleep] {ms}ms");
                    break;
                }

                default:
                    throw new InvalidOperationException(
                        $"[{cmd.Action}] 不支持（DOM 桥白名单：set/get/click/realclick/sleep）");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }
}
