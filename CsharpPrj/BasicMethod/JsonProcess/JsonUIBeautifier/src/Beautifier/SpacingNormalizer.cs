using Newtonsoft.Json.Linq;
using JsonUIBeautifier.Models;
using JsonUIBeautifier.Utils;

namespace JsonUIBeautifier.Beautifier;

public class SpacingNormalizer
{
    private readonly BeautifierConfig _config;

    public SpacingNormalizer(BeautifierConfig config)
    {
        _config = config;
    }

    public List<BeautifyChange> Normalize(JObject root)
    {
        var changes = new List<BeautifyChange>();

        foreach (var (path, container) in JsonNavigator.GetAllContainers(root))
        {
            var controls = container["controls"] as JArray;
            if (controls == null || controls.Count < 2) continue;

            var childLayouts = new List<(JToken Token, double L, double T, double W, double H)>();
            foreach (var control in controls)
            {
                var (l, t, w, h) = JsonNavigator.GetLayout(control);
                if (l.HasValue && t.HasValue && w.HasValue && h.HasValue)
                    childLayouts.Add((control, l.Value, t.Value, w.Value, h.Value));
            }

            if (childLayouts.Count < 2) continue;

            changes.AddRange(NormalizeDirectionalSpacing(
                childLayouts, path,
                sortKey: l => l.T,
                getPosition: l => l.T,
                getSize: l => l.H,
                inferSpacing: LayoutMath.InferVerticalSpacing,
                dimensionName: "垂直", positionName: "top"));

            changes.AddRange(NormalizeDirectionalSpacing(
                childLayouts, path,
                sortKey: l => l.L,
                getPosition: l => l.L,
                getSize: l => l.W,
                inferSpacing: LayoutMath.InferHorizontalSpacing,
                dimensionName: "水平", positionName: "left"));
        }

        return changes;
    }

    private List<BeautifyChange> NormalizeDirectionalSpacing(
        List<(JToken Token, double L, double T, double W, double H)> layouts,
        string containerPath,
        Func<(JToken Token, double L, double T, double W, double H), double> sortKey,
        Func<(JToken Token, double L, double T, double W, double H), double> getPosition,
        Func<(JToken Token, double L, double T, double W, double H), double> getSize,
        Func<List<double>, List<double>, double> inferSpacing,
        string dimensionName, string positionName)
    {
        var changes = new List<BeautifyChange>();

        var sorted = layouts.OrderBy(sortKey).ToList();

        var positions = sorted.Select(getPosition).ToList();
        var sizes = sorted.Select(getSize).ToList();
        var standardSpacing = inferSpacing(positions, sizes);

        for (int i = 1; i < sorted.Count; i++)
        {
            var expectedPos = getPosition(sorted[i - 1]) + getSize(sorted[i - 1]) + standardSpacing;
            var actualPos = getPosition(sorted[i]);

            if (Math.Abs(expectedPos - actualPos) > 0.005)
            {
                var controlName = JsonNavigator.GetControlName(sorted[i].Token);
                var targetPath = $"{containerPath}.controls[{controlName}]";

                if (positionName == "top")
                    JsonNavigator.SetLayout(sorted[i].Token, sorted[i].L, expectedPos, sorted[i].W, sorted[i].H);
                else
                    JsonNavigator.SetLayout(sorted[i].Token, expectedPos, sorted[i].T, sorted[i].W, sorted[i].H);

                changes.Add(new BeautifyChange
                {
                    Type = "spacing_normalized",
                    Target = targetPath,
                    Before = $"{positionName}:{actualPos:F3}",
                    After = $"{positionName}:{expectedPos:F3}",
                    Description = $"{dimensionName}间距标准化为 {standardSpacing:F3}"
                });
            }
        }

        return changes;
    }
}


