
namespace Share.StateManagement
{
    public class RetryPolicy
    {
        public int MaxRetryCount { get; set; } = 3;

        public int RetryDelayMs { get; set; } = 1000;

        public double RetryDelayMultiplier { get; set; } = 2.0;

        public int GetDelayMs(int retryCount)
        {
            // E9：钳制指数溢出到 int.MaxValue，且保证非负（配置异常时不产生负延时）。
            double ms = RetryDelayMs * Math.Pow(RetryDelayMultiplier, Math.Max(0, retryCount));
            if (ms > int.MaxValue) return int.MaxValue;
            return ms < 0 ? 0 : (int)ms;
        }
    }
}
