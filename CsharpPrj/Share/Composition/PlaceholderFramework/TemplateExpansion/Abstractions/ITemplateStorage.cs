
using System.Text.Json.Nodes;

namespace Common.PlaceholderFramework.TemplateExpansion.Abstractions;

public class TemplateMetadata
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string? SourceFile { get; set; }

    public string? Description { get; set; }

    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    public bool HasParameters { get; set; }

    public bool HasDefaults { get; set; }

    public bool HasApplyTo { get; set; }

    public bool IsMultiObject { get; set; }
}

public interface ITemplateStorage
{
    bool IsInitialized { get; }

    void Initialize(string stylesPath, string compositesPath);

    void Initialize(IEnumerable<(string stylesPath, string compositesPath)> paths);

    JsonObject? GetStyle(string name);

    JsonObject? GetComposite(string name);

    bool HasStyle(string name);

    bool HasComposite(string name);

    IEnumerable<string> GetStyleNames();

    IEnumerable<string> GetCompositeNames();

    TemplateMetadata? GetMetadata(string name);

    IEnumerable<string> SearchTemplates(string keyword);

    void RefreshIfNeeded();

    void RegisterComposite(string name, JsonObject definition);

    void RegisterStyle(string name, JsonObject definition);

    void UnregisterComposite(string name);

    void UnregisterStyle(string name);

    #region 批量操作

    void RegisterComposites(Dictionary<string, JsonObject> definitions);

    void RegisterStyles(Dictionary<string, JsonObject> definitions);

    void UnregisterComposites(IEnumerable<string> names);

    void UnregisterStyles(IEnumerable<string> names);

    void ClearComposites();

    void ClearStyles();

    #endregion

    string? StylesPath { get; }

    string? CompositesPath { get; }
}
