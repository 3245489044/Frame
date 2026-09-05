using Newtonsoft.Json.Linq;
using JsonUIBeautifier.Models;
using JsonUIBeautifier.Utils;

namespace JsonUIBeautifier.Analyzer;

public class StructureAnalyzer
{
    public StructureAnalysis Analyze(JObject root)
    {
        var analysis = new StructureAnalysis();
        var allControls = JsonNavigator.GetAllControls(root).ToList();

        analysis.TotalControls = allControls.Count;
        analysis.MaxDepth = CalculateMaxDepth(root);

        foreach (var (_, token) in allControls)
        {
            var type = JsonNavigator.GetControlType(token);
            if (!analysis.ControlTypes.Contains(type))
                analysis.ControlTypes.Add(type);

            if (!analysis.TypeDistribution.ContainsKey(type))
                analysis.TypeDistribution[type] = 0;
            analysis.TypeDistribution[type]++;

            if (token["controls"] is JArray arr && arr.HasValues)
                analysis.ContainerCount++;
            else
                analysis.LeafControlCount++;
        }

        return analysis;
    }

    private int CalculateMaxDepth(JObject root)
    {
        int maxDepth = 0;
        var views = JsonNavigator.GetViews(root);
        if (views == null) return 0;

        foreach (var view in views)
        {
            var controls = view["controls"] as JArray;
            if (controls != null)
            {
                foreach (var control in controls)
                {
                    maxDepth = Math.Max(maxDepth, CalculateDepth(control, 1));
                }
            }
        }

        return maxDepth;
    }

    private int CalculateDepth(JToken token, int currentDepth)
    {
        int maxDepth = currentDepth;
        if (token["controls"] is JArray controls)
        {
            foreach (var child in controls)
            {
                maxDepth = Math.Max(maxDepth, CalculateDepth(child, currentDepth + 1));
            }
        }
        return maxDepth;
    }

    public double EvaluateClarity(StructureAnalysis analysis)
    {
        double score = 10.0;

        if (analysis.MaxDepth > 4) score -= 2.0;
        else if (analysis.MaxDepth > 3) score -= 1.0;

        if (analysis.TotalControls > 0)
        {
            var containerRatio = (double)analysis.ContainerCount / analysis.TotalControls;
            if (containerRatio > 0.5) score -= 1.0;
        }

        if (analysis.ControlTypes.Count < 2) score -= 1.0;

        return Math.Round(Math.Max(0, score), 1);
    }
}
