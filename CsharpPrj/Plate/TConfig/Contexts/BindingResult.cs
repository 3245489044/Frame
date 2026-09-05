
using System;
using System.Collections.Generic;

namespace TConfig
{
    public class BindingResult
    {
        public List<ControlBindingInfo> BoundControls { get; set; } = new();

        public List<ControlBindingInfo> SkippedControls { get; set; } = new();

        public int BoundCount => BoundControls.Count;

        public int SkippedCount => SkippedControls.Count;

        public int ErrorCount { get; set; }

        public bool IsSuccess => ErrorCount == 0;

        public DateTime Timestamp { get; } = DateTime.UtcNow;

        public static BindingResult Success(string controlName, string eventName, string functionName)
            => new()
            {
                BoundControls = new List<ControlBindingInfo>
                {
                    new ControlBindingInfo
                    {
                        ControlName = controlName,
                        Status = "Bound"
                    }
                }
            };

        public static BindingResult Failure(string controlName, string eventName, string functionName, string errorMessage)
            => new()
            {
                ErrorCount = 1,
                SkippedControls = new List<ControlBindingInfo>
                {
                    new ControlBindingInfo
                    {
                        ControlName = controlName,
                        Status = "Failed",
                        ErrorMessage = errorMessage
                    }
                }
            };
    }
}
