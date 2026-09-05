using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using JsonUIBeautifier.Models;
using JsonUIBeautifier.Analyzer;
using JsonUIBeautifier.Beautifier;
using JsonUIBeautifier.Refactor;
using JsonUIBeautifier.Stylizer;
using JsonUIBeautifier.Stylizer.Presets;

namespace JsonUIBeautifier.Pipeline;

public class JsonBeautifierPipeline
{
    private readonly BeautifierConfig _config;

    public JsonBeautifierPipeline(BeautifierConfig? config = null)
    {
        _config = config ?? new BeautifierConfig();
    }

    public BeautifyResult Process(string json)
    {
        var result = new BeautifyResult
        {
            OriginalJson = json
        };

        try
        {
            var root = JObject.Parse(json);
            var report = result.Report;

            var structureAnalyzer = new StructureAnalyzer();
            var styleAnalyzer = new StyleAnalyzer();
            var layoutAnalyzer = new LayoutAnalyzer();

            var structure = structureAnalyzer.Analyze(root);
            var colorUsages = styleAnalyzer.AnalyzeColors(root);
            var layoutQuality = layoutAnalyzer.AnalyzeLayoutQuality(root);
            var spacingConsistency = layoutAnalyzer.AnalyzeSpacingConsistency(root);
            var patterns = layoutAnalyzer.RecognizePatterns(root);
            var overlaps = layoutAnalyzer.DetectOverlaps(root);

            report.Statistics.ControlsCount = structure.TotalControls;
            report.Statistics.ColorsFound = colorUsages.Count;
            report.Score.LayoutQuality = layoutQuality;
            report.Score.ColorConsistency = styleAnalyzer.EvaluateColorConsistency(colorUsages);
            report.Score.SpacingConsistency = spacingConsistency;
            report.Score.StructureClarity = structureAnalyzer.EvaluateClarity(structure);

            if (overlaps.Count > 0)
                report.Suggestions.Add($"发现 {overlaps.Count} 处布局重叠，建议手动调整");
            if (colorUsages.Count > 5)
                report.Suggestions.Add($"使用了 {colorUsages.Count} 种颜色，建议统一到主题色板");
            if (patterns.Count > 0)
                report.Suggestions.Add($"识别到 {patterns.Count} 个布局模式，已自动标注");

            if (_config.EnableLayoutOptimization)
            {
                var layoutOptimizer = new LayoutOptimizer(_config);
                var layoutChanges = layoutOptimizer.Optimize(root);
                report.Changes.AddRange(layoutChanges);
                report.Statistics.LayoutsOptimized = layoutChanges.Count;
            }

            if (_config.EnableColorHarmonization)
            {
                var colorHarmonizer = new ColorHarmonizer(_config);
                var (colorChanges, primaryColor) = colorHarmonizer.Harmonize(root);
                report.Changes.AddRange(colorChanges);
                report.Statistics.ColorsUnified = colorChanges.Count;
            }

            if (_config.EnableSpacingNormalization)
            {
                var spacingNormalizer = new SpacingNormalizer(_config);
                var spacingChanges = spacingNormalizer.Normalize(root);
                report.Changes.AddRange(spacingChanges);
                report.Statistics.SpacingsNormalized = spacingChanges.Count;
            }

            if (_config.EnableStyleDeduplication)
            {
                var styleUnifier = new StyleUnifier(_config);
                var styleChanges = styleUnifier.Unify(root);
                report.Changes.AddRange(styleChanges);
                report.Statistics.StylesDeduplicated = styleChanges.Count;
            }


            if (_config.EnableThemeExtraction)
            {
                var themeExtractor = new ThemeExtractor(_config);
                var (themeChanges, theme) = themeExtractor.Extract(root);
                report.Changes.AddRange(themeChanges);
                report.Statistics.ThemesExtracted = themeChanges.Count;
            }

            if (_config.EnableStyleDeduplication)
            {
                var styleDeduplicator = new StyleDeduplicator(_config);
                var dedupChanges = styleDeduplicator.Deduplicate(root);
                report.Changes.AddRange(dedupChanges);
                report.Statistics.StylesDeduplicated += dedupChanges.Count;
            }

            var schemaUpgrader = new SchemaUpgrader(_config);
            var schemaChanges = schemaUpgrader.Upgrade(root);
            report.Changes.AddRange(schemaChanges);
            report.Statistics.StrategiesInferred = schemaChanges.Count;

            if (_config.EnableStylization)
            {
                try
                {
                    Stylizer.Stylizer stylizer;

                    if (!string.IsNullOrEmpty(_config.CustomStyleJson))
                    {
                        stylizer = new Stylizer.Stylizer(_config.CustomStyleJson, isJson: true);
                    }
                    else if (!string.IsNullOrEmpty(_config.ActiveStyle))
                    {
                        stylizer = new Stylizer.Stylizer(_config.ActiveStyle);
                    }
                    else
                    {
                        stylizer = new Stylizer.Stylizer("modern-light");
                    }

                    var styleResult = stylizer.Apply(root);
                    report.Changes.AddRange(styleResult.Changes);
                    report.Statistics.StylizationChanges = styleResult.TotalChanges;
                    report.Statistics.AppliedStyle = styleResult.StyleName;

                    report.Suggestions.Add($"应用风格 [{styleResult.StyleName}]: {styleResult.StyleDescription}，共 {styleResult.TotalChanges} 项变更");
                }
                catch (Exception ex)
                {
                    report.Suggestions.Add($"风格化失败: {ex.Message}");
                }
            }

            var formatting = _config.OutputFormat == "compact"
                ? Formatting.None
                : Formatting.Indented;

            result.BeautifiedJson = root.ToString(formatting);
        }
        catch (JsonReaderException ex)
        {
            result.Report.Suggestions.Add($"JSON 解析错误: {ex.Message}");
            result.BeautifiedJson = json;
        }
        catch (Exception ex)
        {
            result.Report.Suggestions.Add($"处理异常: {ex.Message}");
            result.BeautifiedJson = json;
        }

        return result;
    }

    public BeautifyResult ProcessFile(string inputPath, string? outputPath = null)
    {
        var json = File.ReadAllText(inputPath);
        var result = Process(json);

        result.Report.InputFile = inputPath;

        if (outputPath != null)
        {
            File.WriteAllText(outputPath, result.BeautifiedJson);
            result.Report.OutputFile = outputPath;
        }

        return result;
    }

    public BeautifyReport AnalyzeOnly(string json)
    {
        var result = Process(json);
        return result.Report;
    }

    public BeautifyReport AnalyzeFileOnly(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var report = AnalyzeOnly(json);
        report.InputFile = filePath;
        return report;
    }

    public BeautifierConfig Config => _config;
}
