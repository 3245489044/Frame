
using System;

namespace PathManager
{
    public static class WorkingDirectoryManager
    {
        public static bool SetToProjectRoot(Action<string, object?>? logAction = null)
        {
            try
            {
                var projectRoot = ProjectPathManager.FindProjectRoot();
                Environment.CurrentDirectory = projectRoot;
                return true;
            }
            catch (Exception ex)
            {
                logAction?.Invoke("Executor_SetWorkingDirectory_error",
                    new { errorMessage = ex.Message });
                return false;
            }
        }
    }
}
