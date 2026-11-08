using FrontEndDriven.Shared;
using TConfig;

namespace FrontEndDriven
{
    public static class FrontEndDrivenConstants
    {
        public const int DefaultFormWidth = 1400;

        public const int DefaultFormHeight = 900;

        public const int DefaultFormMinWidth = 800;

        public const int DefaultFormMinHeight = 600;

        /// <summary>默认字体族（WinForms 门面默认）。唯一来源 = <see cref="StyleConfig"/>，StyleSystem 与 Collect/Canonical 输出共用。</summary>
        public const string DefaultFontFamily = StyleConfig.DefaultFontFamily;

        /// <summary>默认字体尺寸（pt）。</summary>
        public const float DefaultFontSize = StyleConfig.DefaultFontSize;

        public static HashSet<string> ContainerTypes => ControlTypeRegistry.ContainerTypes;

        public static readonly HashSet<string> DockAllowedValues = new(StringComparer.OrdinalIgnoreCase)
            { "none", "top", "bottom", "left", "right", "fill" };

        public static readonly HashSet<string> AnchorAllowedValues = new(StringComparer.OrdinalIgnoreCase)
            { "top", "left", "right", "bottom",
              "top, left", "top, right", "bottom, left", "bottom, right",
              "top, bottom, left, right" };

        /// <summary>
        /// hiddenSemantics 声明合法值（单一真源）：schema 校验（ContractBuilder）与解析
        /// （ConfigDrivenControlFactory.TryParseVisibilityIntent）共用，新增语义值在此登记并同步 switch 映射，
        /// 消除「校验词表 vs 解析词表」各自维护的漂移。
        /// </summary>
        public static readonly HashSet<string> HiddenSemanticsAllowedValues = new(StringComparer.OrdinalIgnoreCase)
            { "expect", "expect-visible", "semantic", "semantic-hidden", "deferred", "deferred-hidden" };
    }
}