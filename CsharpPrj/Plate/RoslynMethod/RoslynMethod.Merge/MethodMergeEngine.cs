using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using System;
using System.Collections.Generic;
using System.Linq;
namespace RoslynSpace.RoslynCompilationIntegration
{
    public static class EnhancedMethodMergeEngine
    {
        private static double _statementSimilarityThreshold = 0.85;
        private const double SIMILARITY_HIGH_THRESHOLD = 0.8;
        private const double SIMILARITY_MEDIUM_THRESHOLD = 0.6;
        private const double SIMILARITY_LOW_THRESHOLD = 0.5;

        public static double StatementSimilarityThreshold
        {
            get => _statementSimilarityThreshold;
            set => _statementSimilarityThreshold = Math.Clamp(value, 0.0, 1.0);
        }

        private static double _blockSimilarityThreshold = 0.8;

        public static double BlockSimilarityThreshold
        {
            get => _blockSimilarityThreshold;
            set => _blockSimilarityThreshold = Math.Clamp(value, 0.0, 1.0);
        }


        public enum MergeStrategy { Conservative, Aggressive, Intelligent, ThreeWay }
        public enum MergeStatus { FullyMerged, PartiallyMerged, HasConflicts, NoChanges, Added, Removed }

        private enum SequenceOperationType { Equal, Insert, Delete, Replace }

        private class SequenceOperation
        {
            public SequenceOperationType Operation { get; set; }
            public StatementSyntax FromA { get; set; }
            public StatementSyntax FromB { get; set; }
        }

        private class StatementSequenceMatcher : IDisposable
        {
            private readonly List<StatementSyntax> _sequenceA, _sequenceB;
            private readonly Func<StatementSyntax, StatementSyntax, bool> _equalityComparer;
            private int[,] _dpTable;

            public StatementSequenceMatcher(List<StatementSyntax> a, List<StatementSyntax> b, Func<StatementSyntax, StatementSyntax, bool> equalityComparer)
            {
                _sequenceA = a ?? throw new ArgumentNullException(nameof(a));
                _sequenceB = b ?? throw new ArgumentNullException(nameof(b));
                _equalityComparer = equalityComparer ?? throw new ArgumentNullException(nameof(equalityComparer));
            }

            public List<SequenceOperation> GetOperations()
            {
                var operations = new List<SequenceOperation>();
                ComputeDPTable();

                int i = _sequenceA.Count, j = _sequenceB.Count;
                while (i > 0 || j > 0)
                {
                    if (i > 0 && j > 0 && _equalityComparer(_sequenceA[i - 1], _sequenceB[j - 1]))
                    {
                        operations.Insert(0, new SequenceOperation { Operation = SequenceOperationType.Equal, FromA = _sequenceA[i - 1], FromB = _sequenceB[j - 1] });
                        i--; j--;
                    }
                    else if (i > 0 && j > 0)
                    {
                        operations.Insert(0, new SequenceOperation { Operation = SequenceOperationType.Replace, FromA = _sequenceA[i - 1], FromB = _sequenceB[j - 1] });
                        i--; j--;
                    }
                    else if (j > 0)
                    {
                        operations.Insert(0, new SequenceOperation { Operation = SequenceOperationType.Insert, FromB = _sequenceB[j - 1] });
                        j--;
                    }
                    else
                    {
                        operations.Insert(0, new SequenceOperation { Operation = SequenceOperationType.Delete, FromA = _sequenceA[i - 1] });
                        i--;
                    }
                }
                return operations;
            }

            private void ComputeDPTable()
            {
                int m = _sequenceA.Count, n = _sequenceB.Count;
                _dpTable = new int[m + 1, n + 1];
                for (int i = 1; i <= m; i++)
                    for (int j = 1; j <= n; j++)
                        _dpTable[i, j] = _equalityComparer(_sequenceA[i - 1], _sequenceB[j - 1])
                            ? _dpTable[i - 1, j - 1] + 1
                            : Math.Max(_dpTable[i - 1, j], _dpTable[i, j - 1]);
            }

            public void Dispose() => _dpTable = null;
        }

        public class MethodMergeDetails
        {
            public string MethodName { get; set; }
            public string ClassName { get; set; }
            public string FullClassName { get; set; }
            public MergeStatus Status { get; set; }
            public List<StatementMerge> StatementMerges { get; set; } = new();
            public List<string> Conflicts { get; set; } = new();
            public double SimilarityScore { get; set; }
            public MethodDeclarationSyntax MergedMethod { get; set; }
        }

        public class StatementMerge
        {
            public StatementSyntax Original { get; set; }
            public StatementSyntax Modified { get; set; }
            public StatementSyntax Merged { get; set; }
            public MergeStatus Status { get; set; }
            public double Similarity { get; set; }
        }

        public static EnhancedMergeResult MergeMethodsIntelligently(
            SyntaxTree originalTree, SyntaxTree modifiedTree, MergeStrategy strategy = MergeStrategy.Intelligent)
        {
            if (originalTree == null || modifiedTree == null)
                throw new ArgumentNullException(originalTree == null ? nameof(originalTree) : nameof(modifiedTree));

            try
            {
                var originalRoot = originalTree.GetRoot();
                var modifiedRoot = modifiedTree.GetRoot();
                var originalMethodsByClass = GroupMethodsByClass(originalRoot);
                var modifiedMethodsByClass = GroupMethodsByClass(modifiedRoot);
                var classDeclarationCache = ClassNameResolver.BuildClassCache(originalRoot, modifiedRoot);
                var mergeDetails = new List<MethodMergeDetails>();
                var allMergedMethods = new List<MethodDeclarationSyntax>();
                int totalConflicts = 0;

                foreach (var fullClassName in originalMethodsByClass.Keys.Intersect(modifiedMethodsByClass.Keys))
                {
                    var originalMethods = originalMethodsByClass[fullClassName];
                    var modifiedMethods = modifiedMethodsByClass[fullClassName];
                    var classDecl = classDeclarationCache.TryGetValue(fullClassName, out var cached)
                        ? cached
                        : ClassNameResolver.FindClassByFullName(originalRoot, fullClassName)
                          ?? ClassNameResolver.FindClassByFullName(modifiedRoot, fullClassName);

                    foreach (var signature in originalMethods.Keys.Intersect(modifiedMethods.Keys))
                    {
                        var details = MergeSingleMethod(
                            originalMethods[signature],
                            modifiedMethods[signature],
                            strategy,
                            classDecl);
                        mergeDetails.Add(details);
                        if (details.Status == MergeStatus.HasConflicts || details.Status == MergeStatus.PartiallyMerged)
                            totalConflicts += details.Conflicts.Count;
                        allMergedMethods.Add(details.MergedMethod);
                    }

                    foreach (var signature in modifiedMethods.Keys.Except(originalMethods.Keys))
                    {
                        var method = modifiedMethods[signature];
                        allMergedMethods.Add(method);
                        mergeDetails.Add(new MethodMergeDetails
                        {
                            MethodName = method.Identifier.Text,
                            ClassName = ClassNameResolver.GetSimpleClassName(fullClassName),
                            FullClassName = fullClassName,
                            Status = MergeStatus.Added,
                            MergedMethod = method
                        });
                    }

                    foreach (var signature in originalMethods.Keys.Except(modifiedMethods.Keys))
                    {
                        var method = originalMethods[signature];
                        allMergedMethods.Add(method);
                        mergeDetails.Add(new MethodMergeDetails
                        {
                            MethodName = method.Identifier.Text,
                            ClassName = ClassNameResolver.GetSimpleClassName(fullClassName),
                            FullClassName = fullClassName,
                            Status = MergeStatus.Removed,
                            MergedMethod = method
                        });
                    }
                }

                foreach (var fullClassName in modifiedMethodsByClass.Keys.Except(originalMethodsByClass.Keys))
                {
                    var methods = modifiedMethodsByClass[fullClassName];
                    var classDecl = classDeclarationCache.TryGetValue(fullClassName, out var cached)
                        ? cached
                        : ClassNameResolver.FindClassByFullName(modifiedRoot, fullClassName);

                    foreach (var method in methods.Values)
                    {
                        allMergedMethods.Add(method);
                        mergeDetails.Add(new MethodMergeDetails
                        {
                            MethodName = method.Identifier.Text,
                            ClassName = ClassNameResolver.GetSimpleClassName(fullClassName),
                            FullClassName = fullClassName,
                            Status = MergeStatus.Added,
                            MergedMethod = method
                        });
                    }
                }

                return new EnhancedMergeResult
                {
                    Success = true,
                    MergedCode = BuildFinalCode(originalRoot, modifiedRoot, allMergedMethods, classDeclarationCache),
                    MethodDetails = mergeDetails,
                    TotalConflicts = totalConflicts,
                    HasConflicts = totalConflicts > 0,
                    MergeStrategy = strategy
                };
            }
            catch (Exception ex)
            {
                return new EnhancedMergeResult
                {
                    Success = false,
                    ErrorMessage = $"合并失败: {ex.Message}{Environment.NewLine}堆栈跟踪: {ex.StackTrace}",
                    HasConflicts = true,
                    TotalConflicts = 1
                };
            }
        }

        private static Dictionary<string, Dictionary<string, MethodDeclarationSyntax>> GroupMethodsByClass(SyntaxNode root)
        {
            var result = new Dictionary<string, Dictionary<string, MethodDeclarationSyntax>>();
            var classDeclarations = QuerySkeleton.FindTopLevelDeclarations<ClassDeclarationSyntax>(root);

            foreach (var classDecl in classDeclarations)
            {
                var fullClassName = ClassNameResolver.GetFullClassName(classDecl);
                var methods = QuerySkeleton.FindMembersInClass<MethodDeclarationSyntax>(root, classDecl.Identifier.Text);
                var methodDict = new Dictionary<string, MethodDeclarationSyntax>();

                foreach (var method in methods)
                {
                    var signature = MethodSignatureBuilder.GetMethodSignature(method, fullClassName);
                    if (!methodDict.ContainsKey(signature))
                        methodDict[signature] = method;
                }

                if (methodDict.Any())
                    result[fullClassName] = methodDict;
            }

            return result;
        }

        private static MethodMergeDetails MergeSingleMethod(
            MethodDeclarationSyntax originalMethod, MethodDeclarationSyntax modifiedMethod,
            MergeStrategy strategy, ClassDeclarationSyntax classDecl = null)
        {
            var details = new MethodMergeDetails
            {
                MethodName = originalMethod?.Identifier.Text ?? modifiedMethod?.Identifier.Text,
                MergedMethod = null
            };

            if (classDecl != null)
            {
                details.ClassName = ClassNameResolver.GetSimpleClassName(ClassNameResolver.GetFullClassName(classDecl));
                details.FullClassName = ClassNameResolver.GetFullClassName(classDecl);
            }

            if (originalMethod == null || modifiedMethod == null)
            {
                details.Status = MergeStatus.HasConflicts;
                details.Conflicts.Add("方法参数为空");
                details.MergedMethod = modifiedMethod ?? originalMethod;
                return details;
            }

            if (AreMethodsEquivalent(originalMethod, modifiedMethod))
            {
                details.Status = MergeStatus.NoChanges;
                details.SimilarityScore = 1.0;
                details.MergedMethod = originalMethod;
                return details;
            }

            var originalStatements = GetMethodStatements(originalMethod);
            var modifiedStatements = GetMethodStatements(modifiedMethod);
            if (!originalStatements.Any() && !modifiedStatements.Any())
            {
                details.Status = MergeStatus.NoChanges;
                details.SimilarityScore = 1.0;
                details.MergedMethod = originalMethod;
                return details;
            }

            var (mergedStatements, conflicts) = strategy switch
            {
                MergeStrategy.Intelligent => MergeWithIntelligentAnalysis(originalStatements, modifiedStatements),
                MergeStrategy.ThreeWay => MergeThreeWay(originalStatements, modifiedStatements),
                MergeStrategy.Aggressive => MergeAggressive(originalStatements, modifiedStatements),
                _ => MergeConservative(originalStatements, modifiedStatements)
            };

            details.MergedMethod = mergedStatements.Any() ? BuildMergedMethod(modifiedMethod, mergedStatements) : modifiedMethod;
            details.Conflicts = conflicts;
            details.Status = conflicts.Any() ? MergeStatus.HasConflicts : MergeStatus.FullyMerged;
            return details;
        }

        private static (List<StatementSyntax> merged, List<string> conflicts) MergeWithIntelligentAnalysis(
            List<StatementSyntax> original, List<StatementSyntax> modified)
        {
            var merged = new List<StatementSyntax>();
            var conflicts = new List<string>();
            if (original == null || modified == null) return (merged, conflicts);

            using var sequenceMatcher = new StatementSequenceMatcher(original, modified, StatementEqualityComparer);
            foreach (var op in sequenceMatcher.GetOperations())
            {
                switch (op.Operation)
                {
                    case SequenceOperationType.Equal:
                        merged.Add(op.FromA);
                        break;
                    case SequenceOperationType.Insert:
                        merged.Add(op.FromB);
                        break;
                    case SequenceOperationType.Delete:
                        if (ShouldKeepDeletedStatement(op.FromA, modified))
                            merged.Add(CreateConflictMarker(op.FromA, "可能冲突的删除"));
                        break;
                    case SequenceOperationType.Replace:
                        var mergeResult = MergeStatementPair(op.FromA, op.FromB);
                        if (mergeResult.Status == MergeStatus.HasConflicts)
                        {
                            conflicts.Add($"语句冲突: {GetStatementSummary(op.FromA)} -> {GetStatementSummary(op.FromB)}");
                            merged.Add(CreateConflictMarker(op.FromA, "原始版本"));
                            merged.Add(CreateConflictMarker(op.FromB, "修改版本"));
                        }
                        else merged.Add(mergeResult.Merged);
                        break;
                }
            }
            return (merged, conflicts);
        }

        private static (List<StatementSyntax> merged, List<string> conflicts) MergeThreeWay(
            List<StatementSyntax> original, List<StatementSyntax> modified)
        {
            var merged = new List<StatementSyntax>();
            var conflicts = new List<string>();
            if (original == null || modified == null) return (merged, conflicts);

            int i = 0, j = 0;
            while (i < original.Count || j < modified.Count)
            {
                if (i < original.Count && j < modified.Count)
                {
                    double sim = CalculateStatementSimilarity(original[i], modified[j]);
                    if (sim >= _statementSimilarityThreshold)
                    {
                        merged.Add(modified[j]);
                        i++; j++;
                    }
                    else
                    {
                        int movedForward = FindMovedStatementWithContext(original[i], modified, j, original, i);
                        int movedBackward = FindMovedStatementWithContext(modified[j], original, i, modified, j);

                        if (movedForward > j && IsValidMove(original[i], modified[movedForward], original, i, modified, movedForward))
                        {
                            for (int k = j; k < movedForward; k++) merged.Add(modified[k]);
                            merged.Add(modified[movedForward]);
                            j = movedForward + 1;
                            i++;
                        }
                        else if (movedBackward > i && IsValidMove(modified[j], original[movedBackward], modified, j, original, movedBackward))
                        {
                            for (int k = i; k < movedBackward; k++) merged.Add(CreateConflictMarker(original[k], "可能被删除"));
                            merged.Add(original[movedBackward]);
                            i = movedBackward + 1;
                            j++;
                        }
                        else
                        {
                            conflicts.Add($"三路合并冲突: {GetStatementSummary(original[i])}");
                            merged.Add(CreateConflictMarker(original[i], "原始"));
                            merged.Add(CreateConflictMarker(modified[j], "修改"));
                            i++; j++;
                        }
                    }
                }
                else if (i < original.Count) merged.Add(CreateConflictMarker(original[i++], "可能被删除"));
                else merged.Add(modified[j++]);
            }
            return (merged, conflicts);
        }

        private static int FindMovedStatementWithContext(StatementSyntax target, List<StatementSyntax> sequence,
            int startIndex, List<StatementSyntax> sourceContext, int sourceIndex)
        {
            if (target == null || sequence == null || startIndex < 0 || startIndex >= sequence.Count) return -1;

            int bestIndex = -1;
            double bestSimilarity = 0.0;

            for (int i = startIndex; i < sequence.Count; i++)
            {
                if (sequence[i] != null)
                {
                    double sim = StatementSimilarityCalculator.Calculate(target, sequence[i]);
                    if (sim > bestSimilarity && sim >= SIMILARITY_HIGH_THRESHOLD)
                    {
                        double contextSim = CalculateContextSimilarity(sourceIndex, sourceContext, i, sequence);
                        if (contextSim > 0.5)
                        {
                            bestSimilarity = sim;
                            bestIndex = i;
                        }
                    }
                }
            }
            return bestIndex;
        }

        private static double CalculateContextSimilarity(int sourceIndex, List<StatementSyntax> source,
            int targetIndex, List<StatementSyntax> target)
        {
            if (source == null || target == null) return 0.0;
            int contextWindow = 2;
            double totalSimilarity = 0.0;
            int count = 0;

            for (int offset = -contextWindow; offset <= contextWindow; offset++)
            {
                if (offset == 0) continue;
                int sourcePos = sourceIndex + offset;
                int targetPos = targetIndex + offset;

                if (sourcePos >= 0 && sourcePos < source.Count &&
                    targetPos >= 0 && targetPos < target.Count)
                {
                    totalSimilarity += CalculateStatementSimilarity(source[sourcePos], target[targetPos]);
                    count++;
                }
            }

            return count > 0 ? totalSimilarity / count : 0.0;
        }

        private static bool IsValidMove(StatementSyntax source, StatementSyntax target,
            List<StatementSyntax> sourceSeq, int sourceIndex,
            List<StatementSyntax> targetSeq, int targetIndex)
        {
            if (source == null || target == null) return false;
            double sim = StatementSimilarityCalculator.Calculate(source, target);
            if (sim < SIMILARITY_HIGH_THRESHOLD) return false;
            double contextSim = CalculateContextSimilarity(sourceIndex, sourceSeq, targetIndex, targetSeq);
            return contextSim > 0.4;
        }

        private static (List<StatementSyntax> merged, List<string> conflicts) MergeAggressive(
            List<StatementSyntax> original, List<StatementSyntax> modified)
        {
            var merged = new List<StatementSyntax>();
            var conflicts = new List<string>();
            if (original == null || modified == null) return (merged, conflicts);

            int maxLength = Math.Max(original.Count, modified.Count);
            for (int k = 0; k < maxLength; k++)
            {
                if (k < original.Count && k < modified.Count)
                {
                    if (CalculateStatementSimilarity(original[k], modified[k]) > SIMILARITY_LOW_THRESHOLD)
                        merged.Add(modified[k]);
                    else
                    {
                        conflicts.Add($"激进合并中的冲突: {GetStatementSummary(original[k])}");
                        merged.Add(modified[k]);
                    }
                }
                else if (k < original.Count) merged.Add(original[k]);
                else merged.Add(modified[k]);
            }
            return (merged, conflicts);
        }

        private static StatementMerge MergeStatementPair(StatementSyntax original, StatementSyntax modified)
        {
            if (original == null || modified == null)
                return new StatementMerge { Original = original, Modified = modified, Merged = modified ?? original, Status = MergeStatus.HasConflicts, Similarity = 0 };

            double similarity = CalculateStatementSimilarity(original, modified);
            if (similarity >= _statementSimilarityThreshold)
                return new StatementMerge { Original = original, Modified = modified, Merged = modified, Status = MergeStatus.FullyMerged, Similarity = similarity };

            if (original is BlockSyntax origBlock && modified is BlockSyntax modBlock)
            {
                var mergedBlock = TryMergeBlocks(origBlock, modBlock);
                if (mergedBlock != null)
                    return new StatementMerge { Original = original, Modified = modified, Merged = mergedBlock, Status = MergeStatus.PartiallyMerged, Similarity = similarity };
            }

            return new StatementMerge { Original = original, Modified = modified, Merged = modified, Status = MergeStatus.HasConflicts, Similarity = similarity };
        }

        private static BlockSyntax TryMergeBlocks(BlockSyntax original, BlockSyntax modified)
        {
            if (original == null || modified == null) return null;
            var (mergedStatements, conflicts) = MergeWithIntelligentAnalysis(original.Statements.ToList(), modified.Statements.ToList());
            return conflicts.Any() ? null : original.WithStatements(SyntaxFactory.List(mergedStatements));
        }

        private static bool StatementEqualityComparer(StatementSyntax a, StatementSyntax b) =>
            StatementSimilarityCalculator.AreEqual(a, b);

        private static double CalculateStatementSimilarity(StatementSyntax a, StatementSyntax b) =>
            StatementSimilarityCalculator.Calculate(a, b);

        private static List<StatementSyntax> GetMethodStatements(MethodDeclarationSyntax method)
        {
            if (method == null) return new List<StatementSyntax>();
            if (method.Body != null) return method.Body.Statements.ToList();
            if (method.ExpressionBody != null) return new List<StatementSyntax> { SyntaxFactory.ExpressionStatement(method.ExpressionBody.Expression) };
            return new List<StatementSyntax>();
        }

        private static bool AreMethodsEquivalent(MethodDeclarationSyntax a, MethodDeclarationSyntax b)
        {
            if (a == null || b == null) return a == b;
            string normalizedA = a.NormalizeWhitespace().ToString().Replace("\r\n", "\n").Replace("\r", "\n").Trim();
            string normalizedB = b.NormalizeWhitespace().ToString().Replace("\r\n", "\n").Replace("\r", "\n").Trim();
            return string.Equals(normalizedA, normalizedB, StringComparison.Ordinal);
        }

        private static MethodDeclarationSyntax BuildMergedMethod(MethodDeclarationSyntax baseMethod, List<StatementSyntax> statements)
        {
            if (baseMethod == null) return null;
            if (statements == null || !statements.Any()) return baseMethod;
            return baseMethod.WithBody(SyntaxFactory.Block(statements))
                .WithLeadingTrivia(baseMethod.GetLeadingTrivia())
                .WithTrailingTrivia(baseMethod.GetTrailingTrivia())
                .WithAttributeLists(baseMethod.AttributeLists)
                .WithModifiers(baseMethod.Modifiers);
        }

        private static string GetStatementSummary(StatementSyntax stmt)
        {
            if (stmt == null) return "(null)";
            var text = stmt.ToString();
            return text.Length > 50 ? text.Substring(0, 47) + "..." : text;
        }

        private static StatementSyntax CreateConflictMarker(StatementSyntax stmt, string version) =>
            stmt?.WithLeadingTrivia(stmt.GetLeadingTrivia().Add(SyntaxFactory.Comment($"/* 冲突标记 ({version}) */ ")));

        private static bool ShouldKeepDeletedStatement(StatementSyntax deletedStmt, List<StatementSyntax> modifiedStatements) =>
            deletedStmt == null || modifiedStatements == null || !modifiedStatements.Any(stmt => stmt != null && CalculateStatementSimilarity(deletedStmt, stmt) > SIMILARITY_MEDIUM_THRESHOLD);

        private static (List<StatementSyntax> merged, List<string> conflicts) MergeConservative(
            List<StatementSyntax> original, List<StatementSyntax> modified) =>
            ExecuteBasicMerge(original, modified, true);

        private static (List<StatementSyntax> merged, List<string> conflicts) ExecuteBasicMerge(
            List<StatementSyntax> original, List<StatementSyntax> modified, bool conservative)
        {
            var merged = new List<StatementSyntax>();
            var conflicts = new List<string>();
            if (original == null || modified == null) return (merged, conflicts);

            int maxLength = Math.Max(original.Count, modified.Count);
            for (int i = 0; i < maxLength; i++)
            {
                if (i < original.Count && i < modified.Count)
                {
                    if (StatementEqualityComparer(original[i], modified[i]))
                        merged.Add(modified[i]);
                    else
                    {
                        conflicts.Add($"位置 {i} 的语句不匹配");
                        if (conservative)
                        {
                            merged.Add(CreateConflictMarker(original[i], "原始"));
                            merged.Add(CreateConflictMarker(modified[i], "修改"));
                        }
                        else merged.Add(modified[i]);
                    }
                }
                else if (i < original.Count) merged.Add(original[i]);
                else merged.Add(modified[i]);
            }
            return (merged, conflicts);
        }

        private static string BuildFinalCode(SyntaxNode originalRoot, SyntaxNode modifiedRoot,
            List<MethodDeclarationSyntax> mergedMethods, Dictionary<string, ClassDeclarationSyntax> classCache)
        {
            if (originalRoot == null || mergedMethods == null) return string.Empty;
            var classesToUpdate = new Dictionary<string, ClassDeclarationSyntax>();

            foreach (var method in mergedMethods)
            {
                var methodClassName = GetClassNameFromMethodMetadata(method, classCache);
                if (!string.IsNullOrEmpty(methodClassName) && classCache.ContainsKey(methodClassName))
                    classesToUpdate[methodClassName] = classCache[methodClassName];
            }

            if (!classesToUpdate.Any()) return originalRoot.ToString();

            SyntaxNode newRoot = originalRoot;
            foreach (var kvp in classesToUpdate)
            {
                var fullClassName = kvp.Key;
                var classDecl = kvp.Value;
                var classMethods = mergedMethods.Where(m =>
                    GetClassNameFromMethodMetadata(m, classCache) == fullClassName).ToList();

                if (classMethods.Any())
                    newRoot = newRoot.ReplaceNode(classDecl, ReplaceMethodsInClass(classDecl, classMethods));
            }
            return newRoot.NormalizeWhitespace().ToString();
        }

        private static string GetClassNameFromMethodMetadata(MethodDeclarationSyntax method,
            Dictionary<string, ClassDeclarationSyntax> classCache)
        {
            if (method == null) return string.Empty;
            foreach (var kvp in classCache)
                if (IsMethodLikelyInClass(method, kvp.Value))
                    return kvp.Key;
            return string.Empty;
        }

        private static bool IsMethodLikelyInClass(MethodDeclarationSyntax method, ClassDeclarationSyntax classDecl)
        {
            if (method == null || classDecl == null) return false;
            var classMethods = classDecl.DescendantNodes().OfType<MethodDeclarationSyntax>();
            return classMethods.Any(m => m.Identifier.Text == method.Identifier.Text);
        }

        private static ClassDeclarationSyntax ReplaceMethodsInClass(
            ClassDeclarationSyntax classDecl, List<MethodDeclarationSyntax> newMethods)
        {
            if (classDecl == null || newMethods == null || !newMethods.Any()) return classDecl;

            var newMethodLookup = newMethods
                .GroupBy(m => m.Identifier.Text)
                .ToDictionary(g => g.Key, g => g.ToList());

            var updatedMembers = new List<MemberDeclarationSyntax>();
            foreach (var member in classDecl.Members)
            {
                if (member is MethodDeclarationSyntax existingMethod &&
                    newMethodLookup.TryGetValue(existingMethod.Identifier.Text, out var replacements))
                {
                    var bestMatch = replacements.FirstOrDefault(r =>
                        r.ParameterList.Parameters.Count == existingMethod.ParameterList.Parameters.Count)
                        ?? replacements.First();
                    updatedMembers.Add(bestMatch);
                    replacements.Remove(bestMatch);
                    if (replacements.Count == 0)
                        newMethodLookup.Remove(existingMethod.Identifier.Text);
                }
                else
                {
                    updatedMembers.Add(member);
                }
            }

            foreach (var kvp in newMethodLookup)
            {
                foreach (var method in kvp.Value)
                {
                    updatedMembers.Add(method);
                }
            }

            return classDecl.WithMembers(SyntaxFactory.List(updatedMembers));
        }
    }

    public class EnhancedMergeResult
    {
        public bool Success { get; set; }
        public string MergedCode { get; set; }
        public string ErrorMessage { get; set; }
        public List<EnhancedMethodMergeEngine.MethodMergeDetails> MethodDetails { get; set; }
        public int TotalConflicts { get; set; }
        public bool HasConflicts { get; set; }
        public EnhancedMethodMergeEngine.MergeStrategy MergeStrategy { get; set; }
    }
}
