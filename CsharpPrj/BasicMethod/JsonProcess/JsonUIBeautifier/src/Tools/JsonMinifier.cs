using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace JsonUIBeautifier.Tools;

public static class JsonMinifier
{
    public static string Minify(string json)
    {
        try
        {
            var obj = JToken.Parse(json);
            return obj.ToString(Formatting.None);
        }
        catch
        {
            return json;
        }
    }

    public static string PrettyPrint(string json)
    {
        try
        {
            var obj = JToken.Parse(json);
            return obj.ToString(Formatting.Indented);
        }
        catch
        {
            return json;
        }
    }

    public static void MinifyFile(string inputPath, string? outputPath = null)
    {
        outputPath ??= inputPath;
        var json = File.ReadAllText(inputPath);
        File.WriteAllText(outputPath, Minify(json));
    }

    public static void PrettyPrintFile(string inputPath, string? outputPath = null)
    {
        outputPath ??= inputPath;
        var json = File.ReadAllText(inputPath);
        File.WriteAllText(outputPath, PrettyPrint(json));
    }
}
