
namespace Share.LogicComposition.Conversion.Models
{
    /// <summary>转换诊断严重度（仅 Error 使转换失败）。</summary>
    public enum ConversionSeverity
    {
        /// <summary>提示（不影响产物可用性）。</summary>
        Info,

        /// <summary>警告（产物可用，但存在待澄清的缺省/降级）。</summary>
        Warning,

        /// <summary>错误（产物不可信；调用方应中止提交）。</summary>
        Error
    }
}
