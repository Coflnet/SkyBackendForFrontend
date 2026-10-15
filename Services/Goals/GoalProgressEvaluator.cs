using System;
using System.Collections.Generic;
using System.Linq;

namespace Coflnet.Sky.Commands.Shared.Goals;

public static class GoalProgressEvaluator
{
    public const int MaxActionable = 5;

    public static GoalProgress Evaluate(Goal goal, IReadOnlyDictionary<string, long> playerCounts,
        IReadOnlyDictionary<string, long> openBuyOrders, ISet<string> forgingTags, DateTime now)
    {
        if (goal?.BazaarFlip is { } flip)
        {
            var action = BazaarFlipProgress.Next(goal);
            var stages = new[] { BazaarFlipProgress.Committed(flip, false) > 0, BazaarFlipProgress.Filled(flip, false) >= goal.Quantity,
                flip.BuyClaimed >= goal.Quantity, BazaarFlipProgress.Committed(flip, true) > 0,
                BazaarFlipProgress.Filled(flip, true) >= goal.Quantity, flip.SellClaimed >= goal.Quantity };
            return new GoalProgress { Goal = goal, Total = 6, Done = stages.Count(s => s), Complete = stages[5],
                Status = new() { [goal.Root.Id] = stages[5] ? StepStatus.Done : StepStatus.Todo },
                Actionable = action is BazaarFlipAction.Buy or BazaarFlipAction.Sell ? new() { new GoalStep {
                    Id = goal.Root.Id + (action == BazaarFlipAction.Buy ? "-buy" : "-sell"), Kind = action == BazaarFlipAction.Buy ? StepKind.BuyOrder : StepKind.Sell,
                    ItemTag = goal.RootItemTag, Quantity = action == BazaarFlipAction.Buy
                        ? goal.Quantity - BazaarFlipProgress.Committed(flip, false) : flip.BuyClaimed - BazaarFlipProgress.Committed(flip, true) } } : new() };
        }
        var available = new Dictionary<string, long>(playerCounts ?? new Dictionary<string, long>());
        var progress = new GoalProgress { Goal = goal };
        if (goal?.Root == null)
            return progress;
        Eval(goal.Root, available, openBuyOrders, forgingTags, progress.Status);
        var actionable = new List<GoalStep>();
        Collect(goal.Root, progress.Status, actionable);
        // auctions first (stable otherwise): they are the scarce purchases and must survive the cut
        progress.Actionable = actionable.OrderBy(s => s.Kind == StepKind.Auction ? 0 : 1).Take(MaxActionable).ToList();
        progress.Total = progress.Status.Count;
        progress.Done = progress.Status.Values.Count(s => s == StepStatus.Done);
        progress.Complete = progress.Status[goal.Root.Id] == StepStatus.Done;
        return progress;
    }

    private static long Allocate(Dictionary<string, long> available, string tag, long wanted)
    {
        if (tag == null || !available.TryGetValue(tag, out var have) || have <= 0)
            return 0;
        var take = Math.Min(have, wanted);
        available[tag] = have - take;
        return take;
    }

    private static void MarkAll(GoalStep step, Dictionary<string, StepStatus> status)
    {
        status[step.Id] = StepStatus.Done;
        foreach (var child in step.Children)
            MarkAll(child, status);
    }

    private static StepStatus Eval(GoalStep step, Dictionary<string, long> available,
        IReadOnlyDictionary<string, long> openOrders, ISet<string> forging, Dictionary<string, StepStatus> status)
    {
        StepStatus result;
        switch (step.Kind)
        {
            case StepKind.BuyOrder:
            case StepKind.InstaBuy:
            case StepKind.Npc:
            case StepKind.Auction:
                var got = step.DoneAt.HasValue ? 0 : Allocate(available, step.ItemTag, step.Quantity);
                if (step.DoneAt.HasValue || got >= step.Quantity)
                    result = StepStatus.Done;
                else if (step.OrderedAt.HasValue
                    || (openOrders != null && openOrders.TryGetValue(step.ItemTag ?? "", out var open) && open >= step.Quantity - got))
                    result = StepStatus.Ordered;
                else
                    result = StepStatus.Todo;
                status[step.Id] = result;
                return result;
            case StepKind.Craft:
            case StepKind.Forge:
                if (step.DoneAt.HasValue || Allocate(available, step.ItemTag, step.Quantity) >= step.Quantity)
                {
                    MarkAll(step, status);
                    return StepStatus.Done;
                }
                var allDone = true;
                foreach (var child in step.Children)
                    allDone &= Eval(child, available, openOrders, forging, status) == StepStatus.Done;
                if (step.Kind == StepKind.Forge && forging != null && forging.Contains(step.ItemTag))
                    result = StepStatus.Forging;
                else
                    result = allDone ? StepStatus.Ready : StepStatus.Todo;
                status[step.Id] = result;
                return result;
            default: // Sell
                var childrenDone = true;
                foreach (var child in step.Children)
                    childrenDone &= Eval(child, available, openOrders, forging, status) == StepStatus.Done;
                result = step.DoneAt.HasValue ? StepStatus.Done : childrenDone ? StepStatus.Ready : StepStatus.Todo;
                if (result == StepStatus.Done)
                    MarkAll(step, status);
                status[step.Id] = result;
                return result;
        }
    }

    private static void Collect(GoalStep step, Dictionary<string, StepStatus> status, List<GoalStep> result)
    {
        var s = status[step.Id];
        if ((s == StepStatus.Todo || s == StepStatus.Ready) && step.Children.All(c => status[c.Id] == StepStatus.Done))
            result.Add(step);
        foreach (var child in step.Children)
            Collect(child, status, result);
    }

    public static List<GoalStep> MarkNewlyDone(Goal goal, GoalProgress progress, DateTime now)
    {
        var changed = new List<GoalStep>();
        void Walk(GoalStep step)
        {
            if (!step.DoneAt.HasValue && progress.Status.TryGetValue(step.Id, out var s) && s == StepStatus.Done)
            {
                step.DoneAt = now;
                changed.Add(step);
            }
            foreach (var child in step.Children)
                Walk(child);
        }
        if (goal?.Root != null)
            Walk(goal.Root);
        return changed;
    }

    public static void Reset(Goal goal)
    {
        if (goal.BazaarFlip != null) { goal.BazaarFlip = new(); goal.CreatedAt = DateTime.UtcNow; }
        void Walk(GoalStep step)
        {
            step.DoneAt = null;
            step.OrderedAt = null;
            step.Sold = 0;
            foreach (var child in step.Children)
                Walk(child);
        }
        if (goal?.Root != null)
            Walk(goal.Root);
    }

    public static GoalStep Find(Goal goal, string stepId)
    {
        GoalStep Walk(GoalStep step)
        {
            if (step.Id == stepId)
                return step;
            foreach (var child in step.Children)
            {
                var found = Walk(child);
                if (found != null)
                    return found;
            }
            return null;
        }
        return goal?.Root == null ? null : Walk(goal.Root);
    }
}
