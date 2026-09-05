using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CodeAnalyzer.Core.Models;

namespace CodeAnalyzer.Analysis.Services
{
    public static class StatementAnalyzer
    {
        private static readonly Type[] BlockTypes = new Type[]
        {
            typeof(IfStatementSyntax),
            typeof(ForStatementSyntax),
            typeof(WhileStatementSyntax),
            typeof(DoStatementSyntax),
            typeof(SwitchStatementSyntax)
        };

        #region 主入口方法

        public static List<StatementParameter> AnalyzeCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("代码不能为空", nameof(code));

            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetCompilationUnitRoot();
            var allClasses = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();

            if (!allClasses.Any())
                return new List<StatementParameter>();

            var result = new List<StatementParameter>();

            foreach (var classDecl in allClasses)
            {
                result.AddRange(ProcessClassMethods(classDecl));
            }

            return result;
        }

        public static List<StatementParameter> ProcessClassMethods(
            ClassDeclarationSyntax classDecl,
            Action<MethodDeclarationSyntax, List<(string Column1, object Column2)>> processMethod = null)
        {
            if (classDecl == null)
                throw new ArgumentNullException(nameof(classDecl));

            var result = new List<StatementParameter>();
            string className = classDecl.Identifier.ValueText;

            var classFields = new List<(string Column1, object Column2)>();
            CollectFieldDeclarations(classDecl, classFields);

            var allMethods = classDecl.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();

            if (!allMethods.Any())
                return result;

            foreach (var methodDecl in allMethods)
            {
                string methodName = methodDecl.Identifier.ValueText;
                var methodData = new List<(string Column1, object Column2)>();

                methodData.AddRange(classFields);

                ProcessMethodParameters(methodDecl.ParameterList.Parameters, methodData);

                if (methodDecl.Body != null)
                {
                    ProcessMethodBody(methodDecl.Body.Statements, methodData);
                }
                else if (methodDecl.ExpressionBody != null)
                {
                    methodData.Add(("ExpressionBody", methodDecl.ExpressionBody.ToString()));
                }

                processMethod?.Invoke(methodDecl, methodData);

                result.Add(new StatementParameter
                {
                    ClassName = className,
                    MethodName = methodName,
                    Data = methodData
                });
            }

            return result;
        }

        #endregion

        #region 辅助方法

        public static string SanitizeCodeString(SyntaxNode node)
        {
            if (node == null) return string.Empty;

            return System.Text.RegularExpressions.Regex.Replace(
                node.ToFullString()
                    .Replace("public", "")
                    .Replace(";", ""),
                @"\s+", " ").Trim();
        }

        public static void AddData(List<(string Column1, object Column2)> targetList, string column1, object column2)
        {
            if (column1 != null && column2 != null)
            {
                targetList.Add((column1, column2));
            }
        }

        #endregion

        #region 泛型类型处理

        public static void ProcessGenericTypeArguments(List<(string Column1, object Column2)> func, GenericNameSyntax genericType)
        {
            if (genericType?.TypeArgumentList?.Arguments.Count > 0)
            {
                var arguments = genericType.TypeArgumentList.Arguments.ToList();

                for (int i = 0; i < arguments.Count - 1; i++)
                {
                    AddData(func, "FuncArgument", arguments[i].ToString());
                }

                if (arguments.Count > 0)
                {
                    AddData(func, "FuncResultArgument", arguments.Last().ToString());
                }
            }
        }

        public static void ProcessFuncParameter(ParameterSyntax parameter, List<(string Column1, object Column2)> data)
        {
            if (parameter == null) return;

            List<(string Column1, object Column2)> func = new();
            string parameterName = parameter.Identifier.Text;

            AddData(func, "FuncName", parameterName);

            if (parameter.Type is GenericNameSyntax genericType)
            {
                ProcessGenericTypeArguments(func, genericType);
                AddData(func, "LambdaExpression", $"{parameterName}_strparameter");
                AddData(data, "methodparams", $"string {parameterName}_strparameter");
            }

            AddData(data, "Func", func);
        }

        public static void VisitFuncDeclarations(VariableDeclarationSyntax variableDeclaration, GenericNameSyntax genericType, List<(string Column1, object Column2)> data)
        {
            if (variableDeclaration == null || genericType == null) return;

            List<(string Column1, object Column2)> func = new();
            var variableNames = variableDeclaration.Variables.Select(v => v.Identifier.Text).ToList();

            foreach (var name in variableNames)
            {
                AddData(func, "FuncName", name);
            }

            ProcessGenericTypeArguments(func, genericType);

            foreach (var declarator in variableDeclaration.Variables)
            {
                if (declarator.Initializer is EqualsValueClauseSyntax initializer &&
                    initializer.Value is LambdaExpressionSyntax lambda)
                {
                    string lambdaBody = string.Empty;

                    if (lambda.Body is ExpressionSyntax expressionBody)
                    {
                        lambdaBody = expressionBody.ToFullString();
                    }
                    else if (lambda.Body is BlockSyntax blockBody)
                    {
                        lambdaBody = string.Join(Environment.NewLine, blockBody.Statements.Select(s => s.ToFullString()));
                    }

                    if (!string.IsNullOrEmpty(lambdaBody))
                    {
                        AddData(func, "LambdaExpression", lambdaBody);
                    }
                }
            }

            AddData(data, "Func", func);
        }

        #endregion

        #region 方法体处理

        public static void ProcessMethodBody(SyntaxList<StatementSyntax> statements, List<(string Column1, object Column2)> data)
        {
            foreach (var stmt in statements)
            {
                if (stmt == null) continue;
                ProcessStatement(stmt, data);
            }
        }

        public static void ProcessStatement(StatementSyntax stmt, List<(string Column1, object Column2)> data)
        {
            if (stmt == null) return;

            switch (stmt)
            {
                case LocalDeclarationStatementSyntax localDecl:
                    HandleLocalDeclaration(localDecl, data);
                    break;

                case ExpressionStatementSyntax exprStmt:
                    HandleExpressionStatement(exprStmt, data);
                    break;

                case BlockSyntax block:
                    ProcessBlockStatements(block.Statements, data);
                    break;
                default:
                    string originalCode = stmt.ToString().Trim();
                    AddData(data, "expressionstatementsyntaxs", originalCode);
                    break;
            }
        }

        public static void ProcessBlockStatements(SyntaxList<StatementSyntax> blockStatements, List<(string Column1, object Column2)> data)
        {
            foreach (var blkstmt in blockStatements)
            {
                if (blkstmt == null) continue;

                bool isBlockType = BlockTypes.Any(blockType => blockType.IsInstanceOfType(blkstmt));

                if (!isBlockType)
                {
                    ProcessStatement(blkstmt, data);
                }
                else
                {
                    AddData(data, "expressionstatementsyntaxs", blkstmt.ToString().Trim());
                }
            }
        }

        public static void HandleLocalDeclaration(LocalDeclarationStatementSyntax localDecl, List<(string Column1, object Column2)> data)
        {
            if (localDecl?.Declaration == null) return;

            var variableDeclaration = localDecl.Declaration;

            if (variableDeclaration.Type is GenericNameSyntax genericType &&
                genericType.Identifier.Text == "Func")
            {
                VisitFuncDeclarations(variableDeclaration, genericType, data);
            }
            else
            {
                string fullDeclaration = localDecl.ToString().Trim();

                if (fullDeclaration.EndsWith(";"))
                {
                    fullDeclaration = fullDeclaration.Substring(0, fullDeclaration.Length - 1).Trim();
                }

                AddData(data, "localdeclarationstatementsyntaxs", fullDeclaration);
            }
        }

        public static void AddVariableDeclarations(VariableDeclarationSyntax variableDeclaration, List<(string Column1, object Column2)> data)
        {
            if (variableDeclaration == null) return;

            foreach (VariableDeclaratorSyntax declarator in variableDeclaration.Variables)
            {
                if (declarator == null) continue;

                string declarationText = declarator.Initializer != null
                    ? $"{variableDeclaration.Type} {declarator.Identifier} = {declarator.Initializer.Value}"
                    : $"{variableDeclaration.Type} {declarator.Identifier}";

                AddData(data, "localdeclarationstatementsyntaxs", declarationText);
            }
        }

        public static void HandleExpressionStatement(ExpressionStatementSyntax exprStmt, List<(string Column1, object Column2)> data)
        {
            if (exprStmt?.Expression == null) return;

            var expression = exprStmt.Expression;

            if (expression is AssignmentExpressionSyntax assignment)
            {
                HandleAssignment(assignment, data);
            }
            else
            {
                string originalCode = exprStmt.ToString().Trim();
                AddData(data, "expressionstatementsyntaxs", originalCode);
            }
        }

        public static void HandleAssignment(AssignmentExpressionSyntax assignment, List<(string Column1, object Column2)> data)
        {
            if (assignment?.Right == null) return;

            string assignmentCode = assignment.Parent?.ToString().Trim() ?? assignment.ToString().Trim();

            switch (assignment.Right)
            {
                case LiteralExpressionSyntax literal:
                    AddData(data, "literalExpressionSyntax", assignmentCode);
                    break;
                default:
                    AddData(data, "identifierNameSyntax", assignmentCode);
                    break;
            }
        }

        #endregion

        #region 参数和字段处理

        public static void ProcessMethodParameters(SeparatedSyntaxList<ParameterSyntax> parameters, List<(string Column1, object Column2)> data)
        {
            foreach (ParameterSyntax param in parameters)
            {
                if (param == null) continue;

                var sanitized = SanitizeCodeString(param);

                if (sanitized.Contains("Func"))
                {
                    ProcessFuncParameter(param, data);
                    continue;
                }

                AddData(data, "methodparams", sanitized);
            }
        }

        public static void CollectFieldDeclarations(ClassDeclarationSyntax classDecl, List<(string Column1, object Column2)> data)
        {
            if (classDecl != null)
            {
                var fields = classDecl.DescendantNodes().OfType<FieldDeclarationSyntax>();
                foreach (var field in fields)
                {
                    string fieldText = SanitizeCodeString(field);
                    AddData(data, "fielddeclarationsyntax", fieldText);
                }
            }
        }

        #endregion
    }

    public class StatementParameter
    {
        public string ClassName { get; set; }
        public string MethodName { get; set; }
        public List<(string Column1, object Column2)> Data { get; set; } = new List<(string Column1, object Column2)>();
    }
}
