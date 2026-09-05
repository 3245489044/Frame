
namespace Modules.Framework.Abstractions
{
    #region 配置加载器

    public interface IConfigLoader<TConfig> where TConfig : class
    {
        TConfig? Load(string source);

        TConfig? LoadFromFile(string filePath);

        TConfig? LoadFromString(string content);

        TConfig? LoadMerged(IEnumerable<string> sources);
    }

    #endregion

    #region 配置合并器

    public interface IConfigMerger<TConfig> where TConfig : class
    {
        void Merge(TConfig target, TConfig source, bool overwrite = true);
    }

    #endregion

    #region 配置构建器

    public interface IConfigBuilder<TConfig> where TConfig : class, new()
    {
        IConfigBuilder<TConfig> LoadFromFile(string filePath);

        IConfigBuilder<TConfig> LoadFromString(string content);

        IConfigBuilder<TConfig> Merge(TConfig config);

        IConfigBuilder<TConfig> WithDefaults(TConfig defaults);

        IConfigBuilder<TConfig> Validate();

        TConfig Build();
    }

    #endregion

    #region 配置验证

    public class ConfigValidationResult
    {
        public bool IsValid { get; set; } = true;

        public List<string> Errors { get; } = new();

        public List<string> Warnings { get; } = new();

        public void AddError(string error)
        {
            Errors.Add(error);
            IsValid = false;
        }

        public void AddWarning(string warning)
        {
            Warnings.Add(warning);
        }

        public void Merge(ConfigValidationResult other)
        {
            if (other == null) return;
            Errors.AddRange(other.Errors);
            Warnings.AddRange(other.Warnings);
            if (!other.IsValid) IsValid = false;
        }
    }

    public interface IConfigValidator<TConfig> where TConfig : class
    {
        ConfigValidationResult Validate(TConfig config);
    }

    #endregion
}
