using Newtonsoft.Json.Linq;
using JsonUIBeautifier.Models;
using JsonUIBeautifier.Utils;

namespace JsonUIBeautifier.Refactor;

public class SchemaUpgrader
{
    private readonly BeautifierConfig _config;

    public SchemaUpgrader(BeautifierConfig config)
    {
        _config = config;
    }

    public List<BeautifyChange> Upgrade(JObject root)
    {
        var changes = new List<BeautifyChange>();

        if (root["form"] == null)
        {
            root["form"] = new JObject
            {
                ["title"] = "Application",
                ["width"] = 800,
                ["height"] = 600
            };
            changes.Add(new BeautifyChange
            {
                Type = "schema_upgraded",
                Target = "form",
                Description = "添加缺失的 form 配置"
            });
        }

        if (root["views"] == null)
        {
            root["views"] = new JArray();
            changes.Add(new BeautifyChange
            {
                Type = "schema_upgraded",
                Target = "views",
                Description = "添加缺失的 views 配置"
            });
        }

        var nameCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, token) in JsonNavigator.GetAllControls(root))
        {
            if (token["name"] == null || string.IsNullOrEmpty(token["name"]?.ToString()))
            {
                var type = JsonNavigator.GetControlType(token);
                if (!nameCounters.ContainsKey(type))
                    nameCounters[type] = 0;
                token["name"] = $"{type}_{nameCounters[type]++}";
                changes.Add(new BeautifyChange
                {
                    Type = "schema_upgraded",
                    Target = path,
                    Description = $"添加缺失的 name 字段"
                });
            }


            if (token["layout"] == null)
            {
                token["layout"] = new JObject
                {
                    ["left"] = 0.0,
                    ["top"] = 0.0,
                    ["width"] = 0.1,
                    ["height"] = 0.1
                };
                changes.Add(new BeautifyChange
                {
                    Type = "schema_upgraded",
                    Target = $"{path}.layout",
                    Description = "添加缺失的 layout 配置"
                });
            }
        }

        if (_config.EnableLayoutStrategyInference)
        {
            var analyzer = new Analyzer.LayoutAnalyzer();
            var patterns = analyzer.RecognizePatterns(root);

            foreach (var pattern in patterns)
            {
                foreach (var (path, container) in JsonNavigator.GetAllContainers(root))
                {
                    if (path == pattern.ContainerPath)
                    {
                        container["layoutStrategy"] = pattern.Type.ToString().ToLowerInvariant();
                        changes.Add(new BeautifyChange
                        {
                            Type = "strategy_inferred",
                            Target = $"{path}.layoutStrategy",
                            After = pattern.Type.ToString(),
                            Description = $"推断布局策略: {pattern.Type} (置信度: {pattern.Confidence:P0})"
                        });
                        break;
                    }
                }
            }
        }

        return changes;
    }
}
