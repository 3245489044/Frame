using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using JsonUIBeautifier.Models;
using JsonUIBeautifier.Stylizer.Models;
using JsonUIBeautifier.Stylizer.Presets;
using JsonUIBeautifier.Stylizer.Applicators;

namespace JsonUIBeautifier.Stylizer;

public class Stylizer
{
    private readonly StyleDefinition _style;

    public Stylizer(string presetName)
    {
        _style = Presets.StylePresets.GetPreset(presetName)
            ?? throw new ArgumentException($"未知的风格预设: {presetName}。可用预设: {string.Join(", ", Presets.StylePresets.GetPresetNames())}", nameof(presetName));
    }

    public Stylizer(StyleDefinition style)
    {
        _style = style ?? throw new ArgumentNullException(nameof(style));
    }

    public Stylizer(string styleJson, bool isJson)
    {
        if (!isJson) throw new ArgumentException("参数 isJson 必须为 true", nameof(isJson));
        _style = JsonConvert.DeserializeObject<StyleDefinition>(styleJson)
            ?? throw new ArgumentException("无法解析风格 JSON", nameof(styleJson));
    }

    public StyleDefinition Style => _style;

    public StylizerResult Apply(JObject root)
    {
        var result = new StylizerResult();
        var changes = new List<BeautifyChange>();

        var colorApp = new ColorApplicator(_style);
        changes.AddRange(colorApp.ApplyPalette(root));

        var typographyApp = new TypographyApplicator(_style);
        changes.AddRange(typographyApp.ApplyTypography(root));

        var spacingApp = new SpacingApplicator(_style);
        changes.AddRange(spacingApp.ApplySpacing(root));

        var borderApp = new BorderApplicator(_style);
        changes.AddRange(borderApp.ApplyBorders(root));

        result.Changes = changes;
        result.StyleName = _style.Name;
        result.StyleDescription = _style.Description;
        return result;
    }
}

public class StylizerResult
{
    public List<BeautifyChange> Changes { get; set; } = new();

    public string StyleName { get; set; } = "";

    public string StyleDescription { get; set; } = "";

    public int TotalChanges => Changes.Count;
}
