using System;
using System.Collections.Generic;
using System.Linq;

namespace BasicMethod.Declarative.Ledger;

/// <summary>
/// 判据正负对照结果：<c>MustBeGreen=true</c> 为 Good（必须绿）；<c>false</c> 为 Bad（必须不绿）。
/// <para>纪律：只写通过路径不算验证；每个判据件必须自带至少一对 Good/Bad。</para>
/// </summary>
/// <param name="Id">对照用例标识。</param>
/// <param name="MustBeGreen">期望是否绿。</param>
/// <param name="ActualGreen">实际是否绿。</param>
/// <param name="Coverage">面覆盖（仅对账用例）。</param>
/// <param name="Verdict">健康结论（仅健康用例）。</param>
/// <param name="Passed">是否通过对照（Good 绿 / Bad 不绿）。</param>
/// <param name="Detail">实际结论摘要。</param>
public sealed record ReconciliationControlOutcome(
    string Id,
    bool MustBeGreen,
    bool ActualGreen,
    FaceCoverage? Coverage,
    HealthVerdict? Verdict,
    bool Passed,
    string Detail);

/// <summary>
/// 对账 / 健康判据的正负对照（代码内置，随内核一起编译；Go/No-Go 由 <see cref="Run"/> 一次性给出）。
/// </summary>
public static class ReconciliationControls
{
    /// <summary>运行全部对照；返回逐例结果。</summary>
    public static IReadOnlyList<ReconciliationControlOutcome> Run()
    {
        var outcomes = new List<ReconciliationControlOutcome>();

        // ---- 多面对账：Good 必绿 / Bad 必红 ----
        outcomes.Add(FaceCase(
            "face-good-complete",
            expectGreen: true,
            faces:
            [
                new ReconcileFace(FaceReconciler.Declared, ["a", "b"]),
                new ReconcileFace(FaceReconciler.Dispatched, ["a", "b"]),
                new ReconcileFace(FaceReconciler.Assembled, ["a", "b"]),
            ]));

        outcomes.Add(FaceCase(
            "face-bad-empty-declared",
            expectGreen: false,
            faces:
            [
                new ReconcileFace(FaceReconciler.Declared, []),
                new ReconcileFace(FaceReconciler.Dispatched, ["a", "b"]),
                new ReconcileFace(FaceReconciler.Assembled, ["a", "b"]),
            ]));

        outcomes.Add(FaceCase(
            "face-bad-all-empty",
            expectGreen: false,
            faces:
            [
                new ReconcileFace(FaceReconciler.Declared, []),
                new ReconcileFace(FaceReconciler.Dispatched, []),
                new ReconcileFace(FaceReconciler.Assembled, []),
            ]));

        outcomes.Add(FaceCase(
            "face-bad-unloaded",
            expectGreen: false,
            faces:
            [
                new ReconcileFace(FaceReconciler.Declared, ["a", "b"]),
                new ReconcileFace(FaceReconciler.Dispatched, ["a", "b"], Loaded: false),
                new ReconcileFace(FaceReconciler.Assembled, ["a", "b"]),
            ]));

        outcomes.Add(FaceCase(
            "face-bad-missing-item",
            expectGreen: false,
            faces:
            [
                new ReconcileFace(FaceReconciler.Declared, ["a", "b", "c"]),
                new ReconcileFace(FaceReconciler.Dispatched, ["a", "b"]),
                new ReconcileFace(FaceReconciler.Assembled, ["a", "b"]),
            ]));

        outcomes.Add(FaceCase(
            "face-bad-no-faces",
            expectGreen: false,
            faces: []));

        // ---- 健康判定：Good 必绿 / Bad 必红 ----
        var healthyFaces = FaceReconciler.Reconcile(
        [
            new ReconcileFace(FaceReconciler.Declared, ["a", "b"]),
            new ReconcileFace(FaceReconciler.Dispatched, ["a", "b"]),
            new ReconcileFace(FaceReconciler.Assembled, ["a", "b"]),
        ]);

        var emptyFaces = FaceReconciler.Reconcile(
        [
            new ReconcileFace(FaceReconciler.Declared, []),
            new ReconcileFace(FaceReconciler.Dispatched, ["a"]),
            new ReconcileFace(FaceReconciler.Assembled, ["a"]),
        ]);

        var policy = new HealthPolicy(
            WarningsBlock: true,
            RequireReconciliation: true,
            RequiredPositiveCounters: ["attached"]);

        outcomes.Add(HealthCase(
            "health-good",
            expectGreen: true,
            new HealthEvidence(
                [new CriterionInput("attached", CriterionInputState.Loaded, 2)],
                ErrorCount: 0,
                WarningCount: 0,
                Counters: new Dictionary<string, int> { ["attached"] = 2 },
                Reconciliation: healthyFaces,
                DiagnosticsAggregated: true,
                DiagnosticsScope: FaceReconciler.Declared),
            policy));

        outcomes.Add(HealthCase(
            "health-bad-no-criteria",
            expectGreen: false,
            new HealthEvidence(
                [],
                ErrorCount: 0,
                WarningCount: 0,
                Counters: new Dictionary<string, int> { ["attached"] = 2 },
                Reconciliation: healthyFaces),
            policy));

        outcomes.Add(HealthCase(
            "health-bad-empty-criterion",
            expectGreen: false,
            new HealthEvidence(
                [new CriterionInput("attached", CriterionInputState.Loaded, 0)],
                ErrorCount: 0,
                WarningCount: 0,
                Counters: new Dictionary<string, int> { ["attached"] = 2 },
                Reconciliation: healthyFaces),
            policy));

        outcomes.Add(HealthCase(
            "health-bad-unloaded-criterion",
            expectGreen: false,
            new HealthEvidence(
                [new CriterionInput("attached", CriterionInputState.Unloaded)],
                ErrorCount: 0,
                WarningCount: 0,
                Counters: new Dictionary<string, int> { ["attached"] = 2 },
                Reconciliation: healthyFaces),
            policy));

        outcomes.Add(HealthCase(
            "health-bad-attached-zero",
            expectGreen: false,
            new HealthEvidence(
                [new CriterionInput("attached", CriterionInputState.Loaded, 1)],
                ErrorCount: 0,
                WarningCount: 0,
                Counters: new Dictionary<string, int> { ["attached"] = 0 },
                Reconciliation: healthyFaces),
            policy));

        outcomes.Add(HealthCase(
            "health-bad-diagnostics-not-aggregated",
            expectGreen: false,
            new HealthEvidence(
                [new CriterionInput("attached", CriterionInputState.Loaded, 2)],
                ErrorCount: 0,
                WarningCount: 0,
                Counters: new Dictionary<string, int> { ["attached"] = 2 },
                Reconciliation: healthyFaces,
                DiagnosticsAggregated: false),
            policy));

        outcomes.Add(HealthCase(
            "health-bad-reconcile-empty",
            expectGreen: false,
            new HealthEvidence(
                [new CriterionInput("attached", CriterionInputState.Loaded, 2)],
                ErrorCount: 0,
                WarningCount: 0,
                Counters: new Dictionary<string, int> { ["attached"] = 2 },
                Reconciliation: emptyFaces),
            policy));

        outcomes.Add(HealthCase(
            "health-bad-reconcile-missing",
            expectGreen: false,
            new HealthEvidence(
                [new CriterionInput("attached", CriterionInputState.Loaded, 2)],
                ErrorCount: 0,
                WarningCount: 0,
                Counters: new Dictionary<string, int> { ["attached"] = 2 },
                Reconciliation: null),
            policy));

        outcomes.Add(HealthCase(
            "health-bad-silent-not-applicable",
            expectGreen: false,
            new HealthEvidence(
                [new CriterionInput("attached", CriterionInputState.NotApplicableSilent)],
                ErrorCount: 0,
                WarningCount: 0,
                Counters: new Dictionary<string, int> { ["attached"] = 2 },
                Reconciliation: healthyFaces),
            policy));

        outcomes.Add(HealthCase(
            "health-not-applicable-declared",
            expectGreen: false,
            new HealthEvidence(
                [new CriterionInput("attached", CriterionInputState.NotApplicableDeclared)],
                ErrorCount: 0,
                WarningCount: 0,
                Counters: new Dictionary<string, int> { ["attached"] = 2 },
                Reconciliation: null,
                DiagnosticsAggregated: true),
            policy with { RequireReconciliation = false }));

        return outcomes;
    }

    /// <summary>全部对照是否通过（Good 全绿且 Bad 全不绿）。</summary>
    public static bool AllPassed(IReadOnlyList<ReconciliationControlOutcome> outcomes)
        => outcomes.Count > 0 && outcomes.All(outcome => outcome.Passed);

    private static ReconciliationControlOutcome FaceCase(
        string id,
        bool expectGreen,
        IReadOnlyList<ReconcileFace> faces)
    {
        var report = FaceReconciler.Reconcile(faces);
        return new ReconciliationControlOutcome(
            id,
            expectGreen,
            report.Ok,
            report.Coverage,
            null,
            report.Ok == expectGreen,
            $"coverage={report.Coverage}; ok={report.Ok}; reasons={report.Reasons.Count}");
    }

    private static ReconciliationControlOutcome HealthCase(
        string id,
        bool expectGreen,
        HealthEvidence evidence,
        HealthPolicy policy)
    {
        var result = FailClosedHealthAdjudicator.Adjudicate(evidence, policy);
        return new ReconciliationControlOutcome(
            id,
            expectGreen,
            result.IsGreen,
            evidence.Reconciliation?.Coverage,
            result.Verdict,
            result.IsGreen == expectGreen,
            $"verdict={result.Verdict}; reasons={result.Reasons.Count}");
    }
}
