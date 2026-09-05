using System.Text.Json;

namespace Modules.Common.Utilities
{
    using System;
    using System.Collections.Generic;

    public static class BasicValidator
    {
        public static void ValidateNotNull(object value, string paramName)
        {
            if (value == null)
                throw new ArgumentNullException(paramName, $"{paramName} 不能为空");
        }

        public static void ValidateListString(List<string> extras, int expectedCount)
        {
            if (extras == null)
                throw new ArgumentNullException(nameof(extras));

            if (extras.Count < expectedCount)
                throw new ArgumentException($"Expected at least {expectedCount} extras, but got {extras.Count}");
        }

        public static void ValidateNotNullOrEmpty(string value, string paramName)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException($"{paramName} 不能为空或空字符串", paramName);
        }

        public static void ValidateGreaterThanZero(int value, string paramName)
        {
            if (value <= 0)
                throw new ArgumentException($"{paramName} 必须大于0", paramName);
        }

        public static void ValidateGreaterThanOrEqual(int value, int minValue, string paramName)
        {
            if (value < minValue)
                throw new ArgumentException($"{paramName} 必须大于或等于 {minValue}", paramName);
        }

        public static void ValidateJsonString(string json, string paramName)
        {
            ValidateNotNullOrEmpty(json, paramName);
            try
            {
                using var doc = JsonDocument.Parse(json);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"{paramName} 不是有效的JSON字符串: {ex.Message}", paramName);
            }
        }
    }
}
