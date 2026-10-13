using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared.Goals;

public class GoalProgressEvaluatorTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Dictionary<string, long> None = new();

    private static GoalStep S(string id, StepKind kind, string tag, long qty, params GoalStep[] children)
        => new() { Id = id, Kind = kind, ItemTag = tag, Quantity = qty, Children = children.ToList() };

    private static GoalProgress Eval(GoalStep root, Dictionary<string, long> have = null, Dictionary<string, long> orders = null, params string[] forging)
        => GoalProgressEvaluator.Evaluate(new Goal { Root = root }, have ?? None, orders ?? None, new HashSet<string>(forging), Now);

    [Test]
    public void LeafStatuses()
    {
        var root = S("r", StepKind.Craft, "R", 1,
            S("a", StepKind.BuyOrder, "A", 10),
            S("b", StepKind.InstaBuy, "B", 10),
            S("c", StepKind.Npc, "C", 10));
        root.Children[2].OrderedAt = Now;
        var p = Eval(root, new() { ["A"] = 10 }, new() { ["B"] = 10 });
        p.Status["a"].Should().Be(StepStatus.Done);
        p.Status["b"].Should().Be(StepStatus.Ordered);
        p.Status["c"].Should().Be(StepStatus.Ordered);
        p.Status["r"].Should().Be(StepStatus.Todo);
        p.Done.Should().Be(1);
        p.Total.Should().Be(4);
        p.Complete.Should().BeFalse();
    }

    [Test]
    public void CraftReadyWhenChildrenDone()
    {
        var root = S("r", StepKind.Craft, "R", 1, S("a", StepKind.BuyOrder, "A", 2));
        Eval(root, new() { ["A"] = 2 }).Status["r"].Should().Be(StepStatus.Ready);
    }

    [Test]
    public void OutputInInventoryShortCircuitsChildren()
    {
        var root = S("r", StepKind.Craft, "R", 1, S("a", StepKind.BuyOrder, "A", 2));
        var p = Eval(root, new() { ["R"] = 1 });
        p.Status["a"].Should().Be(StepStatus.Done);
        p.Complete.Should().BeTrue();
    }

    [Test]
    public void GreedyAllocationLeavesSecondShort()
    {
        var root = S("r", StepKind.Craft, "R", 1, S("a", StepKind.BuyOrder, "A", 10), S("b", StepKind.InstaBuy, "A", 10));
        var p = Eval(root, new() { ["A"] = 12 });
        p.Status["a"].Should().Be(StepStatus.Done);
        p.Status["b"].Should().Be(StepStatus.Todo);
    }

    [Test]
    public void StickyDoneSurvivesConsumption()
    {
        var root = S("r", StepKind.Craft, "R", 1, S("a", StepKind.BuyOrder, "A", 10));
        root.Children[0].DoneAt = Now;
        var p = Eval(root);
        p.Status["a"].Should().Be(StepStatus.Done);
        p.Status["r"].Should().Be(StepStatus.Ready);
    }

    [Test]
    public void ForgingStatus()
    {
        var root = S("f", StepKind.Forge, "F", 1, S("a", StepKind.BuyOrder, "A", 1));
        Eval(root, new() { ["A"] = 1 }, null, "F").Status["f"].Should().Be(StepStatus.Forging);
    }

    [Test]
    public void SellReadyWhenChildDone()
    {
        var root = S("s", StepKind.Sell, "R", 1, S("r", StepKind.Craft, "R", 1));
        var p = Eval(root, new() { ["R"] = 1 });
        p.Status["s"].Should().Be(StepStatus.Ready);
        p.Actionable.Select(a => a.Id).Should().Equal("s");
    }

    [Test]
    public void ActionableIsPreorderAndCapped()
    {
        var kids = Enumerable.Range(0, 8).Select(i => S("k" + i, StepKind.BuyOrder, "T" + i, 1)).ToArray();
        var p = Eval(S("r", StepKind.Craft, "R", 1, kids));
        p.Actionable.Select(a => a.Id).Should().Equal("k0", "k1", "k2", "k3", "k4");
    }

    [Test]
    public void MarkNewlyDoneIsIdempotent()
    {
        var root = S("r", StepKind.Craft, "R", 1, S("a", StepKind.BuyOrder, "A", 1));
        var goal = new Goal { Root = root };
        var p = GoalProgressEvaluator.Evaluate(goal, new Dictionary<string, long> { ["A"] = 1 }, None, new HashSet<string>(), Now);
        GoalProgressEvaluator.MarkNewlyDone(goal, p, Now).Select(s => s.Id).Should().Equal("a");
        root.Children[0].DoneAt.Should().Be(Now);
        GoalProgressEvaluator.MarkNewlyDone(goal, p, Now.AddHours(1)).Should().BeEmpty();
        GoalProgressEvaluator.Find(goal, "a").Should().BeSameAs(root.Children[0]);
        GoalProgressEvaluator.Reset(goal);
        root.Children[0].DoneAt.Should().BeNull();
    }

    [Test]
    public void EnumsSerializeAsCamelCase()
    {
        var json = JsonConvert.SerializeObject(S("a", StepKind.BuyOrder, "A", 1));
        json.Should().Contain("\"buyOrder\"");
        JsonConvert.SerializeObject(StepStatus.Todo).Should().Be("\"todo\"");
        JsonConvert.DeserializeObject<GoalStep>(json).Kind.Should().Be(StepKind.BuyOrder);
    }

    [Test]
    public void AuctionStepsComeFirstAndSurviveTheCut()
    {
        var kids = Enumerable.Range(0, 6).Select(i => S("b" + i, StepKind.InstaBuy, "T" + i, 1)).ToList();
        kids.Add(S("auc", StepKind.Auction, "AUC", 1));
        kids.Add(S("auc2", StepKind.Auction, "AUC2", 1));
        var p = Eval(S("r", StepKind.Craft, "R", 1, kids.ToArray()));
        p.Actionable.Should().HaveCount(GoalProgressEvaluator.MaxActionable);
        p.Actionable.Select(a => a.Id).Should().Equal("auc", "auc2", "b0", "b1", "b2");
    }

    [Test]
    public void Sell_DoneOnlyByDoneAt()
    {
        var root = S("sell", StepKind.Sell, "X", 1, S("craft", StepKind.Craft, "X", 1));
        root.Children[0].DoneAt = Now;
        root.OrderedAt = Now;
        Eval(root).Status["sell"].Should().Be(StepStatus.Ready);
        root.DoneAt = Now;
        Eval(root).Status["sell"].Should().Be(StepStatus.Done);
    }

    [Test]
    public void Reset_ClearsSellMarker()
    {
        var goal = new Goal { Root = S("sell", StepKind.Sell, "X", 1) };
        goal.Root.OrderedAt = Now;
        GoalProgressEvaluator.Reset(goal);
        goal.Root.OrderedAt.Should().BeNull();
    }
}
