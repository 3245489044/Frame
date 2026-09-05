using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RoslynSpace.RoslynSyntaxUtilities.Base
{
    public static class AnalysisContextExtensions
    {
        public static string GetFormattedCode(this AnalysisContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return context.Root.NormalizeWhitespace().ToFullString();
        }

        public static IEnumerable<ClassDeclarationSyntax> GetClasses(this AnalysisContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return QuerySkeleton.FindTopLevelDeclarations<ClassDeclarationSyntax>(context.Root);
        }

        public static IEnumerable<ClassDeclarationSyntax> GetClasses(this AnalysisContext context, string className = null, bool includeNested = false)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(className))
            {
                return QuerySkeleton.FindTopLevelDeclarations<ClassDeclarationSyntax>(context.Root);
            }
            return QuerySkeleton.FindTopLevelDeclarations<ClassDeclarationSyntax>(context.Root, className);
        }


        public static IEnumerable<MethodDeclarationSyntax> GetMethods(this AnalysisContext context, string className = null, string methodName = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return QuerySkeleton.FindMembersInClass<MethodDeclarationSyntax>(context.Root, className, methodName);
        }

        public static IEnumerable<PropertyDeclarationSyntax> GetProperties(this AnalysisContext context, string className = null, string propertyName = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return QuerySkeleton.FindMembersInClass<PropertyDeclarationSyntax>(context.Root, className, propertyName);
        }

        public static IEnumerable<FieldDeclarationSyntax> GetFields(this AnalysisContext context, string className = null, string fieldName = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return QuerySkeleton.FindMembersInClass<FieldDeclarationSyntax>(context.Root, className, fieldName);
        }

        public static IEnumerable<InterfaceDeclarationSyntax> GetInterfaces(this AnalysisContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return QuerySkeleton.FindTopLevelDeclarations<InterfaceDeclarationSyntax>(context.Root);
        }

        public static IEnumerable<InterfaceDeclarationSyntax> GetInterfaces(this AnalysisContext context, string interfaceName)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(interfaceName)) throw new ArgumentException("接口名不能为空", nameof(interfaceName));
            return QuerySkeleton.FindTopLevelDeclarations<InterfaceDeclarationSyntax>(context.Root, interfaceName);
        }

        public static IEnumerable<StructDeclarationSyntax> GetStructs(this AnalysisContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return QuerySkeleton.FindTopLevelDeclarations<StructDeclarationSyntax>(context.Root);
        }

        public static IEnumerable<StructDeclarationSyntax> GetStructs(this AnalysisContext context, string structName)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(structName)) throw new ArgumentException("结构体名不能为空", nameof(structName));
            return QuerySkeleton.FindTopLevelDeclarations<StructDeclarationSyntax>(context.Root, structName);
        }

        public static IEnumerable<EnumDeclarationSyntax> GetEnums(this AnalysisContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return QuerySkeleton.FindTopLevelDeclarations<EnumDeclarationSyntax>(context.Root);
        }

        public static IEnumerable<EnumDeclarationSyntax> GetEnums(this AnalysisContext context, string enumName)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(enumName)) throw new ArgumentException("枚举名不能为空", nameof(enumName));
            return QuerySkeleton.FindTopLevelDeclarations<EnumDeclarationSyntax>(context.Root, enumName);
        }

        public static IEnumerable<DelegateDeclarationSyntax> GetDelegates(this AnalysisContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return QuerySkeleton.FindTopLevelDeclarations<DelegateDeclarationSyntax>(context.Root);
        }

        public static IEnumerable<DelegateDeclarationSyntax> GetDelegates(this AnalysisContext context, string delegateName)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(delegateName)) throw new ArgumentException("委托名不能为空", nameof(delegateName));
            return QuerySkeleton.FindTopLevelDeclarations<DelegateDeclarationSyntax>(context.Root, delegateName);
        }

        public static IEnumerable<ConstructorDeclarationSyntax> GetConstructors(this AnalysisContext context, string className = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var classes = string.IsNullOrWhiteSpace(className)
                ? QuerySkeleton.FindTopLevelDeclarations<ClassDeclarationSyntax>(context.Root)
                : QuerySkeleton.FindTopLevelDeclarations<ClassDeclarationSyntax>(context.Root, className);
            return classes.SelectMany(c => c.Members.OfType<ConstructorDeclarationSyntax>());
        }

        public static IEnumerable<DestructorDeclarationSyntax> GetDestructors(this AnalysisContext context, string className = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var classes = string.IsNullOrWhiteSpace(className)
                ? QuerySkeleton.FindTopLevelDeclarations<ClassDeclarationSyntax>(context.Root)
                : QuerySkeleton.FindTopLevelDeclarations<ClassDeclarationSyntax>(context.Root, className);
            return classes.SelectMany(c => c.Members.OfType<DestructorDeclarationSyntax>());
        }

        public static IEnumerable<EventDeclarationSyntax> GetEvents(this AnalysisContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var classes = QuerySkeleton.FindTopLevelDeclarations<ClassDeclarationSyntax>(context.Root);
            return classes.SelectMany(c => c.Members.OfType<EventDeclarationSyntax>());
        }

        public static IEnumerable<NamespaceDeclarationSyntax> GetNamespaces(this AnalysisContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return context.Root.DescendantNodes().OfType<NamespaceDeclarationSyntax>();
        }


    }
}
