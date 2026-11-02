using System;
using System.Collections.Generic;

namespace MathematicalAlgorithms.Spatial;

/// <summary>一个期望驻留的分块候选（键不透明，调度器不解释其空间含义）。</summary>
public readonly record struct ResidencyCandidate(long Key, float Distance);

/// <summary>驻留预算与滞后参数。</summary>
public readonly record struct ResidencyOptions(
    int MaxResident,
    int MaxEnterPerRound,
    float KeepDistance)
{
    /// <summary>默认：最多 256 个驻留、每轮最多进入 4 个、超出期望集但距离 ≤192 的已有项保留。</summary>
    public static ResidencyOptions Default => new(256, 4, 192f);
}

/// <summary>
/// 一轮驻留决策：进入 / 保留 / 离开 / 因预算延后。全部为确定性排序（距离升序、键升序）。
/// </summary>
public sealed class ResidencyPlan
{
    public IReadOnlyList<ResidencyCandidate> Enter { get; }
    public IReadOnlyList<ResidencyCandidate> Keep { get; }
    public IReadOnlyList<long> Leave { get; }
    public IReadOnlyList<ResidencyCandidate> Deferred { get; }
    public IReadOnlyList<ResidencyCandidate> Desired { get; }
    public int ResidentBefore { get; }
    public int ResidentAfter => Keep.Count + Enter.Count;

    internal ResidencyPlan(
        IReadOnlyList<ResidencyCandidate> desired,
        IReadOnlyList<ResidencyCandidate> enter,
        IReadOnlyList<ResidencyCandidate> keep,
        IReadOnlyList<long> leave,
        IReadOnlyList<ResidencyCandidate> deferred,
        int residentBefore)
    {
        Desired = desired;
        Enter = enter;
        Keep = keep;
        Leave = leave;
        Deferred = deferred;
        ResidentBefore = residentBefore;
    }
}

/// <summary>
/// 通用分块驻留调度器：只做集合/预算/滞后运算，不依赖体素、图形或任何引擎类型。
/// <para>光环（halo）由调用方通过 <c>halo</c> 委托给出相邻键；调度器不解释网格语义。</para>
/// <para>无注册层、无每类型接口：输入是键 + 距离，输出是键集合。</para>
/// </summary>
public static class ResidencyScheduler
{
    public static ResidencyPlan Plan(
        IReadOnlyList<ResidencyCandidate> desired,
        IReadOnlyList<ResidencyCandidate> resident,
        in ResidencyOptions options,
        Func<long, IEnumerable<long>>? halo = null)
    {
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(resident);

        int maxResident = options.MaxResident > 0 ? options.MaxResident : int.MaxValue;
        int maxEnter = options.MaxEnterPerRound > 0 ? options.MaxEnterPerRound : int.MaxValue;
        float keepDistance = options.KeepDistance > 0f ? options.KeepDistance : float.PositiveInfinity;

        // 期望集去重（同键保留更近距离），并按可能的 halo 扩张一层。
        var desiredMap = new Dictionary<long, float>(desired.Count * 2);
        foreach (var candidate in desired)
            Merge(desiredMap, candidate.Key, candidate.Distance);

        if (halo != null)
        {
            var seeds = new List<ResidencyCandidate>(desiredMap.Count);
            foreach (var pair in desiredMap)
                seeds.Add(new ResidencyCandidate(pair.Key, pair.Value));

            foreach (var seed in seeds)
            {
                foreach (long neighbor in halo(seed.Key))
                    Merge(desiredMap, neighbor, seed.Distance + 1f);
            }
        }

        var desiredList = new List<ResidencyCandidate>(desiredMap.Count);
        foreach (var pair in desiredMap)
            desiredList.Add(new ResidencyCandidate(pair.Key, pair.Value));
        desiredList.Sort(Compare);

        var residentMap = new Dictionary<long, float>(resident.Count);
        foreach (var candidate in resident)
            Merge(residentMap, candidate.Key, candidate.Distance);

        // 1) 进入：期望集中尚未驻留，受每轮预算与总容量约束。
        var enter = new List<ResidencyCandidate>();
        var deferred = new List<ResidencyCandidate>();
        int projected = residentMap.Count;

        foreach (var candidate in desiredList)
        {
            if (residentMap.ContainsKey(candidate.Key))
                continue;

            if (enter.Count >= maxEnter || projected >= maxResident)
            {
                deferred.Add(candidate);
                continue;
            }

            enter.Add(candidate);
            projected++;
        }

        // 2) 保留：期望集内，或已有项仍落在滞后距离内。
        var keep = new List<ResidencyCandidate>();
        var leave = new List<long>();
        var keepMap = new Dictionary<long, bool>(residentMap.Count);

        foreach (var pair in residentMap)
        {
            bool inDesired = desiredMap.ContainsKey(pair.Key);
            if (inDesired || pair.Value <= keepDistance)
            {
                keep.Add(new ResidencyCandidate(pair.Key, pair.Value));
                keepMap[pair.Key] = inDesired;
            }
            else
            {
                leave.Add(pair.Key);
            }
        }

        // 3) 容量回收：超过总容量时先淘汰非期望项，再从最远项开始。
        keep.Sort((a, b) =>
        {
            bool ad = keepMap[a.Key], bd = keepMap[b.Key];
            if (ad != bd) return ad ? 1 : -1;       // 非期望项先被淘汰
            int byDistance = b.Distance.CompareTo(a.Distance); // 远的在前
            return byDistance != 0 ? byDistance : a.Key.CompareTo(b.Key);
        });

        while (keep.Count + enter.Count > maxResident && keep.Count > 0)
        {
            var victim = keep[0];
            keep.RemoveAt(0);
            leave.Add(victim.Key);
        }

        keep.Sort(Compare);
        leave.Sort(static (a, b) => a.CompareTo(b));

        return new ResidencyPlan(desiredList, enter, keep, leave, deferred, residentMap.Count);
    }

    private static void Merge(Dictionary<long, float> map, long key, float distance)
    {
        if (map.TryGetValue(key, out float existing))
        {
            if (distance < existing) map[key] = distance;
        }
        else
        {
            map[key] = distance;
        }
    }

    private static int Compare(ResidencyCandidate a, ResidencyCandidate b)
    {
        int byDistance = a.Distance.CompareTo(b.Distance);
        return byDistance != 0 ? byDistance : a.Key.CompareTo(b.Key);
    }
}
