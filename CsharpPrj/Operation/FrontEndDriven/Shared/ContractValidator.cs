
using TConfig;
using Common.PlaceholderFramework.SchemaValidation;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.Shared;

namespace FrontEndDriven.Shared
{
    public static class ContractValidator
    {
        private static readonly SchemaValidator _validator = new();

        public static ValidationResult Validate(UIConfiguration config, bool strictMode = true)
        {
            var result = new ValidationResult();

            if (config?.Views == null || config.Views.Count == 0)
            {
                result.Errors.Add(new ValidationError(
                    "(config)", "UIConfiguration",
                    "配置中没有 Views 定义"));
                return result;
            }

            foreach (var view in config.Views)
            {
                if (view.Controls != null)
                {
                    foreach (var controlConfig in view.Controls)
                    {
                        ValidateControl(controlConfig, result, strictMode);
                    }
                }
            }

            if (result.Warnings.Count > 0)
            {
                FrontEndDrivenLog.Log("ContractValidator_ValidationWarnings",
                    new { warnings = result.GetFormattedWarnings() });
            }

            if (result.Errors.Count > 0)
            {
                var errorMsg = result.GetFormattedErrors();
                FrontEndDrivenLog.Log("ContractValidator_ValidationErrors",
                    new { errors = errorMsg });

                if (strictMode)
                {
                    throw new InvalidOperationException(
                        $"配置契约校验失败，已阻止加载。\n{errorMsg}");
                }
            }

            return result;
        }

        private static void ValidateControl(ControlConfig controlConfig,
            ValidationResult result, bool strictMode)
        {
            if (controlConfig == null) return;

            var target = new ControlConfigTarget(controlConfig);
            var controlType = controlConfig.Type ?? "(empty)";

            if (!string.IsNullOrEmpty(controlConfig.Type))
            {
                if (!ContractRegistry.IsValidControlType(controlConfig.Type))
                {
                    var validTypes = string.Join(", ", ContractRegistry.GetRegisteredTypes().OrderBy(t => t));
                    AddError(result, target.TargetName, controlType,
                        $"未知的控件类型 '{controlConfig.Type}'。合法类型: {validTypes}", strictMode);
                }
            }

        var schema = ContractRegistry.GetContract(controlType);
        try
        {
            var childResult = _validator.Validate(target, schema, strictMode);
            result.Merge(childResult);
        }
        catch (Exception ex) when (!strictMode)
        {
            result.Warnings.Add(new ValidationError(
                controlConfig.Name ?? "(unnamed)", controlType,
                $"SchemaValidator 校验异常: {ex.Message}"));
        }
        catch (Exception ex) when (strictMode)
        {
            throw new InvalidOperationException(
                $"SchemaValidator 校验异常 (controlType={controlType}): {ex.Message}", ex);
        }

        }

        private static void AddError(ValidationResult result, string controlName, string controlType,
            string message, bool strictMode)
        {
            var error = new ValidationError(controlName, controlType, message,
                strictMode ? ValidationSeverity.Error : ValidationSeverity.Warning);

            if (strictMode)
                result.Errors.Add(error);
            else
                result.Warnings.Add(error);
        }
    }
}
