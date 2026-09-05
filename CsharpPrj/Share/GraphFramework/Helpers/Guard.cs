
#nullable enable

namespace GraphFramework.Helpers
{
    public static class Guard
    {
        public static string AgainstNullOrEmpty(string? value, string paramName)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("值不能为 null 或空", paramName);
            return value;
        }

        public static IReadOnlyCollection<T> AgainstNullOrEmpty<T>(IReadOnlyCollection<T>? value, string paramName)
        {
            if (value == null || value.Count == 0)
                throw new ArgumentException("集合不能为 null 或空", paramName);
            return value;
        }

        public static T AgainstNull<T>(T? value, string paramName) where T : class
        {
            if (value == null)
                throw new ArgumentNullException(paramName);
            return value;
        }

        public static int AgainstNonPositive(int value, string paramName)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(paramName, "值必须为正数");
            return value;
        }

        public static double AgainstNonPositive(double value, string paramName)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(paramName, "值必须为正数");
            return value;
        }

        public static int AgainstNegative(int value, string paramName)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(paramName, "值不能为负数");
            return value;
        }

        public static int AgainstOutOfRange(int value, string paramName, int min, int max)
        {
            if (value < min || value > max)
                throw new ArgumentOutOfRangeException(paramName, $"值必须在 {min} 到 {max} 之间");
            return value;
        }
    }
}
