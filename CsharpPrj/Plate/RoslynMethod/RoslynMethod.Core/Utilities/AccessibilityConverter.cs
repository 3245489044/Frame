using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;
using System.Reflection;

namespace RoslynSpace.RoslynSyntaxUtilities.Core.Utilities
{
    public static class AccessibilityConverter
    {
        #region 从 SyntaxTokenList 提取（Roslyn 语法令牌）

        public static string GetAccessibilityString(SyntaxTokenList modifiers)
        {
            if (modifiers.Any(SyntaxKind.PublicKeyword)) return "public";
            if (modifiers.Any(SyntaxKind.PrivateKeyword)) return "private";
            if (modifiers.Any(SyntaxKind.ProtectedKeyword))
                return modifiers.Any(SyntaxKind.InternalKeyword) ? "protected internal" : "protected";
            if (modifiers.Any(SyntaxKind.InternalKeyword)) return "internal";
            if (modifiers.Any(SyntaxKind.FileKeyword)) return "file";
            return "private";
        }

        public static Microsoft.CodeAnalysis.Accessibility GetAccessibility(SyntaxTokenList modifiers)
        {
            if (modifiers.Any(SyntaxKind.PublicKeyword)) return Microsoft.CodeAnalysis.Accessibility.Public;
            if (modifiers.Any(SyntaxKind.PrivateKeyword)) return Microsoft.CodeAnalysis.Accessibility.Private;
            if (modifiers.Any(SyntaxKind.ProtectedKeyword)) return Microsoft.CodeAnalysis.Accessibility.Protected;
            if (modifiers.Any(SyntaxKind.InternalKeyword)) return Microsoft.CodeAnalysis.Accessibility.Internal;
            return Microsoft.CodeAnalysis.Accessibility.Internal;
        }

        #endregion

        #region 从 SyntaxNode 提取

        public static string GetAccessibilityString(SyntaxNode node)
        {
            var modifiers = GetModifiers(node);
            return GetAccessibilityString(modifiers);
        }

        public static Microsoft.CodeAnalysis.Accessibility GetAccessibility(SyntaxNode node)
        {
            var modifiers = GetModifiers(node);
            return GetAccessibility(modifiers);
        }

        public static SyntaxTokenList GetModifiers(SyntaxNode node)
        {
            return node switch
            {
                MethodDeclarationSyntax method => method.Modifiers,
                ConstructorDeclarationSyntax constructor => constructor.Modifiers,
                PropertyDeclarationSyntax property => property.Modifiers,
                FieldDeclarationSyntax field => field.Modifiers,
                ClassDeclarationSyntax classDecl => classDecl.Modifiers,
                StructDeclarationSyntax structDecl => structDecl.Modifiers,
                InterfaceDeclarationSyntax interfaceDecl => interfaceDecl.Modifiers,
                EventDeclarationSyntax eventDecl => eventDecl.Modifiers,
                DelegateDeclarationSyntax delegateDecl => delegateDecl.Modifiers,
                _ => new SyntaxTokenList()
            };
        }

        #endregion

        #region 从 System.Reflection.MethodInfo 提取

        public static string GetAccessibilityString(MethodInfo method)
        {
            if (method == null) return "private";

            if (method.IsPublic) return "public";
            if (method.IsPrivate) return "private";
            if (method.IsFamily) return "protected";
            if (method.IsAssembly) return "internal";
            if (method.IsFamilyOrAssembly) return "protected internal";
            if (method.IsFamilyAndAssembly) return "private protected";

            return "private";
        }

        public static string GetAccessibilityString(Type type)
        {
            if (type == null) return "internal";

            if (type.IsPublic) return "public";
            if (type.IsNestedPublic) return "public";
            if (type.IsNestedPrivate) return "private";
            if (type.IsNestedFamily) return "protected";
            if (type.IsNestedAssembly) return "internal";
            if (type.IsNestedFamORAssem) return "protected internal";
            if (type.IsNestedFamANDAssem) return "private protected";

            return "internal";
        }

        #endregion

        #region 辅助方法

        public static bool IsStatic(SyntaxTokenList modifiers)
            => modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));

        public static bool IsVirtual(SyntaxTokenList modifiers)
            => modifiers.Any(m => m.IsKind(SyntaxKind.VirtualKeyword));

        public static bool IsAbstract(SyntaxTokenList modifiers)
            => modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword));

        public static bool IsReadOnly(SyntaxTokenList modifiers)
            => modifiers.Any(m => m.IsKind(SyntaxKind.ReadOnlyKeyword));

        public static bool IsConst(SyntaxTokenList modifiers)
            => modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword));

        #endregion
    }
}
