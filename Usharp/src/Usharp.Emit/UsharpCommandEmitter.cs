using System.Text.Json.Nodes;
using Usharp.Semantics;

namespace Usharp.Emit;

/// <summary><c>[Command]</c> → UI 连接点目录 JSON（<c>ui-commands-v1</c>，供 Agent 侧 UiCommandCatalog 读取）。</summary>
public static class UsharpCommandEmitter
{
    /// <summary>发射全部命令声明（声明序）。</summary>
    public static string Emit(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var commands = new JsonArray();
        foreach (var command in compilation.Commands)
        {
            var item = new JsonObject { ["action"] = command.Action };
            if (command.Channel is { } channel) item["channel"] = Tail(channel);
            if (command.DefaultOp is { } def) item["default"] = def;
            if (command.FallbackOp is { } fallback) item["fallback"] = fallback;
            if (command.Ports.Count > 0) item["ports"] = Array(command.Ports.Select(Tail).ToArray());

            var ops = new JsonArray();
            foreach (var op in command.Ops)
            {
                var entry = new JsonObject { ["op"] = op.Op };
                if (op.Intent is { } intent) entry["intent"] = intent;
                if (op.Handler is { } handler) entry["handler"] = handler;
                if (op.Projector is { } projector) entry["projector"] = projector;
                if (op.Resolver is { } resolver) entry["resolver"] = resolver;
                if (op.Ports.Count > 0) entry["ports"] = Array(op.Ports.Select(Tail).ToArray());
                ops.Add(entry);
            }
            item["ops"] = ops;

            var keys = new JsonArray();
            foreach (var key in command.Keys)
            {
                keys.Add(new JsonObject
                {
                    ["op"] = key.Op,
                    ["name"] = key.Name,
                    ["family"] = key.Family,
                    ["surface"] = key.Surface,
                    ["required"] = key.Required,
                    ["chain"] = Array(key.Chain),
                });
            }
            item["keys"] = keys;
            commands.Add(item);
        }

        var root = new JsonObject
        {
            ["schema"] = "ui-commands-v1",
            ["commands"] = commands,
        };
        return root.ToJsonString();
    }

    private static JsonArray Array(IReadOnlyList<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values) array.Add(value);
        return array;
    }

    private static string Tail(string value)
    {
        var dot = value.LastIndexOf('.');
        return dot >= 0 && dot < value.Length - 1 ? value[(dot + 1)..] : value;
    }
}
