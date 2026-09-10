
using System.Text;
using System.Text.Json.Nodes;
using Modules.Parsing.Parsing;
using Common.PlaceholderFramework.TemplateExpansion.Abstractions;

namespace Common.PlaceholderFramework.TemplateExpansion.Diagnostics;

public class TemplateTreeNode
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = "composite";

    public List<TemplateTreeNode> Children { get; set; } = new();

    public bool IsCircular { get; set; }

    public bool IsMissing { get; set; }

    public TemplateMetadata? Metadata { get; set; }
}

public class TemplateTreeAdapter
{
    private readonly ITemplateStorage _storage;

    public TemplateTreeAdapter(ITemplateStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public List<TemplateTreeNode> BuildFullTree()
    {
        var roots = new List<TemplateTreeNode>();
        var visited = new HashSet<string>();

        foreach (var name in _storage.GetCompositeNames())
        {
            if (!visited.Contains(name))
            {
                var node = BuildNode(name, visited, new HashSet<string>());
                if (node != null)
                    roots.Add(node);
            }
        }

        return roots;
    }

    public TemplateTreeNode? BuildTree(string rootName)
    {
        return BuildNode(rootName, new HashSet<string>(), new HashSet<string>());
    }

    public string ToMermaid()
    {
        var sb = new StringBuilder();
        sb.AppendLine("graph TD");
        var edges = new List<string>();
        var visited = new HashSet<string>();

        foreach (var name in _storage.GetCompositeNames())
        {
            CollectEdges(name, edges, visited);
        }

        foreach (var edge in edges.Distinct())
        {
            sb.AppendLine($"    {edge}");
        }

        return sb.ToString();
    }

    public string ToIndentedText()
    {
        var sb = new StringBuilder();
        var roots = BuildFullTree();

        foreach (var root in roots)
        {
            PrintNode(root, "", sb, true);
        }

        return sb.ToString();
    }

    public JsonObject ToJson()
    {
        var roots = BuildFullTree();
        var result = new JsonObject();

        foreach (var root in roots)
        {
            result[root.Name] = NodeToJson(root);
        }

        return result;
    }

    private TemplateTreeNode? BuildNode(string name, HashSet<string> allVisited, HashSet<string> chainVisited)
    {
        if (chainVisited.Contains(name))
        {
            return new TemplateTreeNode
            {
                Name = name,
                IsCircular = true
            };
        }

        if (!_storage.HasComposite(name))
        {
            return new TemplateTreeNode
            {
                Name = name,
                IsMissing = true
            };
        }

        allVisited.Add(name);
        chainVisited.Add(name);

        var composite = _storage.GetComposite(name);
        var node = new TemplateTreeNode
        {
            Name = name,
            Type = "composite",
            Metadata = _storage.GetMetadata(name)
        };

        if (composite != null)
        {
            if (composite.TryGetPropertyValue("extends", out var extendsNode) &&
                extendsNode is JsonValue extendsVal &&
                extendsVal.TryGetValue<string>(out var extendsName))
            {
                var resolvedExtends = PrefixResolver.ResolveName(extendsName, PrefixResolver.Extends);

                var child = BuildNode(resolvedExtends, allVisited, new HashSet<string>(chainVisited));
                if (child != null)
                    node.Children.Add(child);
            }

            CollectTemplateRefs(composite, allVisited, chainVisited, node);
        }

        chainVisited.Remove(name);
        return node;
    }

    private void CollectTemplateRefs(JsonObject obj, HashSet<string> allVisited, HashSet<string> chainVisited, TemplateTreeNode parent)
    {
        foreach (var kv in obj)
        {
            if (kv.Value is JsonObject childObj)
            {
                if (childObj.TryGetPropertyValue("template", out var templateNode) &&
                    templateNode is JsonValue templateVal &&
                    templateVal.TryGetValue<string>(out var templateName))
                {
                    var resolvedName = PrefixResolver.ResolveName(templateName, PrefixResolver.Template);

                    var child = BuildNode(resolvedName, allVisited, new HashSet<string>(chainVisited));
                    if (child != null)
                        parent.Children.Add(child);
                }
                else
                {
                    CollectTemplateRefs(childObj, allVisited, chainVisited, parent);
                }
            }
            else if (kv.Value is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    if (item is JsonObject arrObj)
                    {
                        if (arrObj.TryGetPropertyValue("template", out var templateNode) &&
                            templateNode is JsonValue templateVal &&
                            templateVal.TryGetValue<string>(out var templateName))
                        {
                            var resolvedName = PrefixResolver.ResolveName(templateName, PrefixResolver.Template);

                            var child = BuildNode(resolvedName, allVisited, new HashSet<string>(chainVisited));
                            if (child != null)
                                parent.Children.Add(child);
                        }
                        else
                        {
                            CollectTemplateRefs(arrObj, allVisited, chainVisited, parent);
                        }
                    }
                }
            }
        }
    }

    private void CollectEdges(string name, List<string> edges, HashSet<string> visited)
    {
        if (visited.Contains(name)) return;
        visited.Add(name);

        var composite = _storage.GetComposite(name);
        if (composite == null) return;

        if (composite.TryGetPropertyValue("extends", out var extendsNode) &&
            extendsNode is JsonValue extendsVal &&
            extendsVal.TryGetValue<string>(out var extendsName))
        {
            var resolvedExtends = PrefixResolver.ResolveName(extendsName, PrefixResolver.Extends);

            edges.Add($"{SanitizeId(name)} -->|extends| {SanitizeId(resolvedExtends)}");
            CollectEdges(resolvedExtends, edges, visited);
        }

        CollectTemplateEdges(name, composite, edges, visited);
    }

    private void CollectTemplateEdges(string parentName, JsonObject obj, List<string> edges, HashSet<string> visited)
    {
        foreach (var kv in obj)
        {
            if (kv.Value is JsonObject childObj)
            {
                if (childObj.TryGetPropertyValue("template", out var templateNode) &&
                    templateNode is JsonValue templateVal &&
                    templateVal.TryGetValue<string>(out var templateName))
                {
                    var resolvedName = PrefixResolver.ResolveName(templateName, PrefixResolver.Template);

                    edges.Add($"{SanitizeId(parentName)} -->|{kv.Key}| {SanitizeId(resolvedName)}");
                    CollectEdges(resolvedName, edges, visited);
                }
                else
                {
                    CollectTemplateEdges(parentName, childObj, edges, visited);
                }
            }
            else if (kv.Value is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    if (item is JsonObject arrObj)
                    {
                        if (arrObj.TryGetPropertyValue("template", out var templateNode) &&
                            templateNode is JsonValue templateVal &&
                            templateVal.TryGetValue<string>(out var templateName))
                        {
                            var resolvedName = PrefixResolver.ResolveName(templateName, PrefixResolver.Template);

                            edges.Add($"{SanitizeId(parentName)} -->|{kv.Key}| {SanitizeId(resolvedName)}");
                            CollectEdges(resolvedName, edges, visited);
                        }
                        else
                        {
                            CollectTemplateEdges(parentName, arrObj, edges, visited);
                        }
                    }
                }
            }
        }
    }

    private static void PrintNode(TemplateTreeNode node, string indent, StringBuilder sb, bool isLast)
    {
        var marker = isLast ? "└── " : "├── ";
        var suffix = node.IsCircular ? " [CIRCULAR]" :
                     node.IsMissing ? " [MISSING]" : "";

        sb.AppendLine($"{indent}{marker}{node.Name}{suffix}");

        var childIndent = indent + (isLast ? "    " : "│   ");
        for (int i = 0; i < node.Children.Count; i++)
        {
            PrintNode(node.Children[i], childIndent, sb, i == node.Children.Count - 1);
        }
    }

    private static JsonObject NodeToJson(TemplateTreeNode node)
    {
        var obj = new JsonObject
        {
            ["name"] = node.Name,
            ["type"] = node.Type
        };

        if (node.IsCircular)
            obj["circular"] = true;
        if (node.IsMissing)
            obj["missing"] = true;
        if (node.Metadata != null)
        {
            obj["hasParameters"] = node.Metadata.HasParameters;
            obj["hasDefaults"] = node.Metadata.HasDefaults;
            obj["isMultiObject"] = node.Metadata.IsMultiObject;
        }

        if (node.Children.Count > 0)
        {
            var children = new JsonObject();
            foreach (var child in node.Children)
            {
                children[child.Name] = NodeToJson(child);
            }
            obj["children"] = children;
        }

        return obj;
    }

    private static string SanitizeId(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
            else
                sb.Append('_');
        }
        return sb.ToString();
    }
}
