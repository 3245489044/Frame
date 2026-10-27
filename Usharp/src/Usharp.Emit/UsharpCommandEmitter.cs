using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Usharp.Semantics;
using Usharp.Syntax;

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
            if (command.Channel is { } channel) item["channel"] = UsharpJson.Tail(channel);
            if (command.DefaultOp is { } def) item["default"] = def;
            if (command.FallbackOp is { } fallback) item["fallback"] = fallback;
            if (command.Ports.Count > 0) item["ports"] = UsharpJson.Array(command.Ports.Select(UsharpJson.Tail).ToArray());

            var ops = new JsonArray();
            foreach (var op in command.Ops)
            {
                var entry = new JsonObject { ["op"] = op.Op };
                if (op.Intent is { } intent) entry["intent"] = intent;
                if (op.Handler is { } handler) entry["handler"] = handler;
                if (op.Projector is { } projector) entry["projector"] = projector;
                if (op.Resolver is { } resolver) entry["resolver"] = resolver;
                if (op.Model is { } model) entry["model"] = model;
                if (op.Ports.Count > 0) entry["ports"] = UsharpJson.Array(op.Ports.Select(UsharpJson.Tail).ToArray());
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
                    ["chain"] = UsharpJson.Array(key.Chain),
                });
            }
            item["keys"] = keys;
            commands.Add(item);
        }

        var readModels = new JsonArray();
        compilation.TryGetFacet<IReadOnlyList<UsharpSegmentDecl>>(out var segments);
        foreach (var model in compilation.ReadModels)
        {
            var item = new JsonObject { ["model"] = model.ModelId };
            if (model.Sources.Count > 0) item["sources"] = UsharpJson.Array(model.Sources.ToArray());
            if (!string.IsNullOrWhiteSpace(model.Version)) item["version"] = model.Version;
            if (model.TtlMs is { } ttl) item["ttl"] = ttl;
            if (model.Projector is { } projector) item["projector"] = projector;
            if (model.Args.Count > 0) item["args"] = UsharpJson.Array(model.Args.ToArray());
            if (model.Fields.Count > 0) item["fields"] = EmitFields(model, segments);
            readModels.Add(item);
        }

        var root = new JsonObject
        {
            ["schema"] = "ui-commands-v2",
            ["commands"] = commands,
            // 读模型声明段：`[ReadModel]` 声明 →（模型 id / 源键 / 版本源 / TTL / 投影器 / 参数名表）。
            ["readModels"] = readModels,
        };
        return root.ToJsonString();
    }

    /// <summary>
    /// <c>fields=</c> → 解析后的字段表（<c>name/type/order/required/from</c>）。
    /// <para>序 = <c>fields=</c> 的**列出顺序**（位置即语义，无隐藏 tiebreak）；<c>from</c> = 段键（可回溯声明面）。</para>
    /// </summary>
    private static JsonArray EmitFields(
        UsharpReadModelDecl model,
        IReadOnlyList<UsharpSegmentDecl>? segments)
    {
        var byKey = new Dictionary<string, UsharpSegmentDecl>(StringComparer.Ordinal);
        if (segments is not null)
            foreach (var segment in segments) byKey[segment.Key] = segment;

        var fields = new JsonArray();
        foreach (var key in model.Fields)
        {
            if (!byKey.TryGetValue(key, out var segment)) continue; // 悬空引用由闸A US7711 拦（此处不伪造）
            fields.Add(new JsonObject
            {
                ["name"] = segment.Field,
                ["type"] = segment.Type,
                ["order"] = segment.Order,
                ["required"] = string.Equals(segment.Requirement, "required", StringComparison.OrdinalIgnoreCase),
                ["from"] = segment.Key,
            });
        }
        return fields;
    }
}
