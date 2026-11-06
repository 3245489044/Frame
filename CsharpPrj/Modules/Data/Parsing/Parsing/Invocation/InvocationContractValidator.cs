using System;
using System.Collections.Generic;
using System.Linq;
using Modules.Parsing.Models;

namespace Modules.Parsing.Invocation;

/// <summary>
/// 调用面校验器（唯一判定）：绑定（声明/语法/参数）+ 名字归属（命名空间索引）。
/// 不产出业务 pass/fail，只产出结构违约清单（原因码机读）。
/// </summary>
public static class InvocationContractValidator
{
    public static InvocationContractReport Validate(IEnumerable<MetaCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var issues = new List<InvocationIssue>();
        var checkedCount = 0;
        var nameChecks = 0;
        var index = -1;

        foreach (var cmd in commands)
        {
            index++;
            if (cmd is null || string.IsNullOrWhiteSpace(cmd.Action)) continue;

            checkedCount++;
            if (!InvocationBinding.TryBind(cmd, index, out var bound, out var bindIssues))
            {
                issues.AddRange(bindIssues);
                continue;
            }

            issues.AddRange(bindIssues);

            foreach (var claim in bound!.NameClaims)
            {
                // 运行期产出空间（Control/ContainerHost/Scope）：名字由渲染或前序命令产生，
                // 加载期无声明面可对账 ⇒ 不据其缺席判违约（执行期由断言给出真实类别）。
                if (NameSpaceIds.IsRuntimeProduced(claim.Space))
                    continue;

                var hit = NameSpaceRegistry.Resolve(claim.Space, claim.Name);
                if (hit.IsUnavailable)
                    continue; // 该空间本次无来源 ⇒ 不参与判定（不计入 NameChecks，也不伪造违背）

                nameChecks++;
                if (hit.Found) continue;

                issues.Add(new InvocationIssue(
                    InvocationIssueKind.NameNotInSpace, index, bound.Action, bound.SubAction, claim.Name, claim.Space,
                    $"名字 '{claim.Name}' 不在 {claim.Space} 命名空间（{hit.Describe()}）；跨空间同名不代表可寻址"));
            }
        }

        return new InvocationContractReport(issues, checkedCount, nameChecks, Blocking: false);
    }

    /// <summary>各空间的来源可用性（门禁/日志可见化：未注册 ≠ 不存在）。</summary>
    public static IReadOnlyList<(NameSpaceId Space, bool Available)> Availability()
    {
        var registered = NameSpaceRegistry.RegisteredSpaces;
        return Enum.GetValues<NameSpaceId>()
            .Where(space => space != NameSpaceId.None)
            .Select(space => (space, registered.Contains(space)))
            .ToArray();
    }
}
