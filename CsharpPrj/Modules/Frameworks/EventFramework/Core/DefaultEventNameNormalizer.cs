
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Modules.EventFramework.Abstractions;

namespace Modules.EventFramework.Core;

public class DefaultEventNameNormalizer : IEventNameNormalizer
{
    private readonly ConcurrentDictionary<string, string> _aliasMappings = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> KnownWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "up", "in", "on", "to", "of", "is", "at",
        "key", "end", "tip", "row", "tab", "top", "off", "set", "get",
        "back", "cell", "click", "down", "drag", "drop", "edit", "font",
        "form", "fore", "give", "help", "hide", "hold", "info", "item",
        "last", "left", "list", "load", "menu", "mode", "move", "node",
        "page", "paint", "post", "prep", "redo", "refs", "root", "save",
        "show", "size", "sort", "text", "tick", "tree", "undo", "view",
        "wheel",
        "after", "align", "begin", "bound", "close", "color", "defer",
        "double", "enter", "error", "focus", "frame", "hover", "image",
        "index", "label", "layer", "leave", "mouse", "paint", "panel",
        "point", "press", "query", "reset", "resize", "scale", "scope",
        "shift", "split", "stack", "state", "style", "value", "width",
        "action", "active", "cancel", "change", "closed", "closing",
        "column", "commit", "common", "config", "cursor", "custom",
        "delete", "design", "enable", "expand", "filter", "format",
        "header", "height", "layout", "margin", "marker", "matrix",
        "merged", "method", "metric", "minmax", "object", "offset",
        "option", "output", "parent", "parser", "picker", "pinned",
        "pushed", "record", "region", "remove", "render", "repeat",
        "scroll", "search", "select", "shared", "signal", "source",
        "status", "string", "submit", "target", "thread", "toggle",
        "update", "upload", "visual", "weight",
        "changed", "checked", "clicked", "collapse", "command",
        "compare", "connect", "context", "control", "convert",
        "created", "current", "deleted", "destroy", "deselect",
        "enabled", "expired", "extract", "focused", "handler",
        "import", "initial", "insert", "invalid", "manager",
        "mapping", "message", "minimum", "navigate", "notify",
        "options", "padding", "pressed", "process", "profile",
        "project", "reorder", "request", "resolve", "restore",
        "section", "segment", "service", "session", "setting",
        "started", "stopped", "support", "swapped", "symbols",
        "timeout", "tracker", "updated", "utility", "version",
        "visible",
        "activate", "advanced", "argument", "balanced", "behavior",
        "bindings", "complete", "continue", "delegate", "disabled",
        "document", "dragdrop", "executed", "finished", "floating",
        "fontsize", "formated", "handlers", "instance", "interval",
        "modified", "observer", "pipeline", "position", "property",
        "received", "released", "selected", "selector", "template",
        "tracking", "unshared", "validated", "validates",
        "activated", "animation", "arguments", "attribute", "cancelled",
        "component", "container", "converter", "databound", "debugging",
        "declared", "direction", "executing", "execution", "extension",
        "framework", "generated", "highlight", "interface", "iteration",
        "lifecycle", "listeners", "maximized", "minimized", "namespace",
        "operation", "parameter", "reference", "reflected", "released",
        "rendering", "resolving", "selection", "sensitive", "separator",
        "serialize", "shortcuts", "signature", "structure", "synchron",
        "validates",
        "balloontip", "bindingcontext", "causesvalidation",
        "databinding", "formatstring", "formattingenabled",
        "querycontinue", "selectionchange", "selectionchangecommitted",
    };

    public DefaultEventNameNormalizer()
    {
        _aliasMappings["closing"] = "FormClosing";
        _aliasMappings["closed"] = "FormClosed";
        _aliasMappings["buttonclick"] = "Click";
    }

    public void RegisterMapping(string lowerCaseName, string pascalCaseName)
    {
        if (string.IsNullOrWhiteSpace(lowerCaseName))
            throw new ArgumentException("事件名不能为空", nameof(lowerCaseName));
        if (string.IsNullOrWhiteSpace(pascalCaseName))
            throw new ArgumentException("PascalCase 事件名不能为空", nameof(pascalCaseName));

        _aliasMappings[lowerCaseName] = pascalCaseName;
    }

    public void RegisterMappings(IEnumerable<KeyValuePair<string, string>> mappings)
    {
        if (mappings == null)
            throw new ArgumentNullException(nameof(mappings));

        foreach (var kv in mappings)
        {
            _aliasMappings[kv.Key] = kv.Value;
        }
    }

    public string ToPascalCase(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            return eventName;

        if (_aliasMappings.TryGetValue(eventName, out var aliasName))
            return aliasName;

        if (char.IsUpper(eventName[0]))
            return eventName;

        return ConvertToPascalCase(eventName);
    }

    public IReadOnlyDictionary<string, string> GetKnownMappings()
    {
        return new Dictionary<string, string>(_aliasMappings, StringComparer.OrdinalIgnoreCase);
    }

    public bool UnregisterMapping(string lowerCaseName)
    {
        return _aliasMappings.TryRemove(lowerCaseName, out _);
    }

    private static string ConvertToPascalCase(string eventName)
    {
        if (string.IsNullOrEmpty(eventName))
            return eventName;

        var words = SplitWords(eventName);
        if (words.Count == 0)
            return eventName;

        return string.Concat(words.Select(w => char.ToUpper(w[0]) + w.Substring(1).ToLower()));
    }

    private static List<string> SplitWords(string input)
    {
        var result = new List<string>();
        int i = 0;
        while (i < input.Length)
        {
            bool matched = false;
            int maxLen = Math.Min(input.Length - i, 30);
            for (int len = maxLen; len >= 1; len--)
            {
                var candidate = input.Substring(i, len);
                if (KnownWords.Contains(candidate))
                {
                    result.Add(candidate);
                    i += len;
                    matched = true;
                    break;
                }
            }
            if (!matched)
            {
                result.Add(input[i].ToString());
                i++;
            }
        }
        return result;
    }
}
