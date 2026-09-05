using Microsoft.CodeAnalysis;
using RoslynSpace.RoslynSyntaxUtilities.Core.Utilities;
using System.Collections.Generic;

namespace RoslynSpace.RoslynSyntaxUtilities.Base
{
    public static class UsingSkeleton
    {
        #region 提取

        public static List<string> ExtractUsings(string sourceCode)
            => UsingManager.ExtractUsings(sourceCode);

        public static List<string> ExtractUsings(SyntaxNode root)
            => UsingManager.ExtractUsings(root);

        public static List<string> ExtractUsingNames(string sourceCode)
            => UsingManager.ExtractUsingNames(sourceCode);

        public static List<string> ExtractUsingNames(SyntaxNode root)
            => UsingManager.ExtractUsingNames(root);

        #endregion

        #region 添加普通 Using

        public static string AddMissingUsings(string code)
            => UsingManager.AddMissingUsings(code);

        public static string AddUsings(string code, params string[] namespaces)
            => UsingManager.AddUsings(code, namespaces);

        #endregion

        #region 添加静态 Using

        public static string AddStaticUsings(string code, params string[] staticTypes)
            => UsingManager.AddStaticUsings(code, staticTypes);

        public static string AddStaticClassReferences(string sourceCode)
            => UsingManager.AddStaticClassReferences(sourceCode);

        #endregion

        #region 移除 Using

        public static string RemoveUsings(string code, params string[] namespaces)
            => UsingManager.RemoveUsings(code, namespaces);

        public static string RemoveUnusedUsings(string code)
            => UsingManager.RemoveUnusedUsings(code);

        #endregion

        #region 合并 Using

        public static string MergeUsings(string targetCode, string sourceCode)
            => UsingManager.MergeUsings(targetCode, sourceCode);

        #endregion

        #region 确保 System 命名空间

        public static string EnsureUsingSystem(string sourceCode)
            => UsingManager.EnsureUsingSystem(sourceCode);

        public static string AddAllReferences(string sourceCode)
            => UsingManager.AddAllReferences(sourceCode);

        #endregion
    }
}
