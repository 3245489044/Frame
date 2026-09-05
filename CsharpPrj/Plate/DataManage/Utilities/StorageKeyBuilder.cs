#nullable enable

using System;

namespace DataManage.Utilities
{
    public static class StorageKeyBuilder
    {
        public static string BuildWorkflowKey(string workflowId)
        {
            return BuildKey("workflow_", workflowId);
        }

        public static string BuildBranchKey(string branchId)
        {
            return BuildKey("branch_", branchId);
        }

        public static string BuildActionKey(string actionId)
        {
            return BuildKey("action_", actionId);
        }

        public static string BuildResourceKey(string resourceType, string resourceId)
        {
            return $"{resourceType}_{resourceId}";
        }

        public static string BuildKey(string prefix, string id)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return id?.Trim() ?? string.Empty;
            }

            var trimmedId = id?.Trim() ?? string.Empty;
            if (trimmedId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmedId;
            }

            return $"{prefix}{trimmedId}";
        }

        public static string ExtractId(string prefix, string key)
        {
            if (string.IsNullOrEmpty(prefix) || string.IsNullOrEmpty(key))
            {
                return key ?? string.Empty;
            }

            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return key.Substring(prefix.Length);
            }

            return key;
        }

        public static bool HasPrefix(string prefix, string key)
        {
            if (string.IsNullOrEmpty(prefix) || string.IsNullOrEmpty(key))
            {
                return false;
            }

            return key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizeKey(string prefix, string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            var trimmedKey = key.Trim();
            if (string.IsNullOrEmpty(trimmedKey))
            {
                return string.Empty;
            }

            return BuildKey(prefix, trimmedKey);
        }
    }
}