using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared.Goals;

public class GoalChatProgressTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static string Name(string tag) => tag switch
    {
        "HOT_RUNE" => "Hot Rune I",
        "COAL" => "Coal",
        "AGATHA" => "Agatha's Coupon",
        _ => null
    };

    private static Goal SellGoal(string tag, long qty, bool craftDone = true, string id = null, string title = null)
    {
        var craft = new GoalStep { Id = "c", Kind = StepKind.Craft, ItemTag = tag, Quantity = qty, DoneAt = craftDone ? Now : null };
        return new Goal { Id = id ?? tag, Title = title ?? tag, Root = new GoalStep { Id = "s", Kind = StepKind.Sell, ItemTag = tag, Quantity = qty, Children = { craft } } };
    }

    private static GoalList ListOf(params Goal[] goals) => new() { Goals = goals.ToList(), FocusId = goals.First().Id };

    [TestCase("BIN Auction started for ◆ Hot Rune I!", "◆ Hot Rune I", 1, false)]
    [TestCase("Auction started for Stick!", "Stick", 1, false)]
    [TestCase("[Bazaar] Sell Offer Setup! 64x Agatha's Coupon for 764,796 coins.", "Agatha's Coupon", 64, true)]
    [TestCase("[Bazaar] Sell Offer Setup! 64x Agatha's Coupon for 764,796 coins", "Agatha's Coupon", 64, true)]
    [TestCase("[Bazaar] Sold 1,280x Coal for 3,840.2 coins!", "Coal", 1280, true)]
    [TestCase("§6BIN Auction started for §fStick§6!", "Stick", 1, false)]
    public void ParsesLines(string line, string name, long amount, bool bazaar)
    {
        GoalChatProgress.TryParse(line, out var n, out var a, out var b).Should().BeTrue();
        (n, a, b).Should().Be((name, amount, bazaar));
    }

    [TestCase("You purchased Stick for 1 coins!")]
    [TestCase("[Bazaar] Bought 5x Coal for 10 coins!")]
    [TestCase("")]
    public void IgnoresOtherLines(string line) => GoalChatProgress.TryParse(line, out _, out _, out _).Should().BeFalse();

    [Test]
    public void BinLine_CompletesQuantityOne()
    {
        var goal = SellGoal("HOT_RUNE", 1);
        GoalChatProgress.Apply(ListOf(goal), "BIN Auction started for ◆ Hot Rune I!", Name, Now).Should().BeTrue();
        goal.Root.DoneAt.Should().Be(Now);
        goal.Root.Sold.Should().Be(1);
    }

    [Test]
    public void Quantity3_NeedsThreeBinLines()
    {
        var goal = SellGoal("HOT_RUNE", 3);
        var list = ListOf(goal);
        for (var i = 0; i < 2; i++)
        {
            GoalChatProgress.Apply(list, "BIN Auction started for Hot Rune I!", Name, Now).Should().BeTrue();
            goal.Root.DoneAt.Should().BeNull();
        }
        GoalChatProgress.Apply(list, "BIN Auction started for Hot Rune I!", Name, Now);
        goal.Root.DoneAt.Should().NotBeNull();
    }

    [Test]
    public void Bazaar_SoldOrSetup_CountsAmounts()
    {
        var one = SellGoal("COAL", 64);
        GoalChatProgress.Apply(ListOf(one), "[Bazaar] Sold 64x Coal for 100 coins!", Name, Now);
        one.Root.DoneAt.Should().NotBeNull();

        var two = SellGoal("COAL", 64);
        var list = ListOf(two);
        GoalChatProgress.Apply(list, "[Bazaar] Sold 32x Coal for 100 coins!", Name, Now);
        two.Root.DoneAt.Should().BeNull();
        GoalChatProgress.Apply(list, "[Bazaar] Sell Offer Setup! 32x Coal for 100 coins.", Name, Now);
        two.Root.DoneAt.Should().NotBeNull();
    }

    [Test]
    public void ChildrenNotDone_NoChange()
    {
        var goal = SellGoal("HOT_RUNE", 1, craftDone: false);
        GoalChatProgress.Apply(ListOf(goal), "BIN Auction started for Hot Rune I!", Name, Now).Should().BeFalse();
        goal.Root.Sold.Should().Be(0);
    }

    [Test]
    public void OtherItem_NoChange()
    {
        var goal = SellGoal("HOT_RUNE", 1);
        GoalChatProgress.Apply(ListOf(goal), "BIN Auction started for Stick!", Name, Now).Should().BeFalse();
        goal.Root.DoneAt.Should().BeNull();
    }

    [Test]
    public void ReforgeAndSymbolPrefixedName_Matches()
    {
        var goal = SellGoal("AGATHA", 1);
        GoalChatProgress.Apply(ListOf(goal), "BIN Auction started for ✪ Fabled Agatha's Coupon ✪✪!", Name, Now).Should().BeTrue();
    }

    [Test]
    public void FocusedGoalWins()
    {
        var first = SellGoal("COAL", 1, id: "a");
        var focused = SellGoal("COAL", 1, id: "b");
        var list = ListOf(first, focused);
        list.FocusId = "b";
        GoalChatProgress.Apply(list, "BIN Auction started for Coal!", Name, Now).Should().BeTrue();
        focused.Root.DoneAt.Should().NotBeNull();
        first.Root.DoneAt.Should().BeNull();
    }

    [Test]
    public void AlreadyDone_NoChange()
    {
        var goal = SellGoal("COAL", 1);
        goal.Root.DoneAt = Now;
        GoalChatProgress.Apply(ListOf(goal), "BIN Auction started for Coal!", Name, Now.AddHours(1)).Should().BeFalse();
        goal.Root.DoneAt.Should().Be(Now);
    }

    [Test]
    public void UnknownTagName_FallsBackToGoalTitle()
    {
        var goal = SellGoal("MYSTERY", 2, title: "Mystery Item ×2");
        GoalChatProgress.Apply(ListOf(goal), "BIN Auction started for Mystery Item!", _ => null, Now).Should().BeTrue();
        goal.Root.Sold.Should().Be(1);
    }

    [Test]
    public void Reset_ClearsSold()
    {
        var goal = SellGoal("COAL", 3);
        GoalChatProgress.Apply(ListOf(goal), "BIN Auction started for Coal!", Name, Now);
        GoalProgressEvaluator.Reset(goal);
        goal.Root.Sold.Should().Be(0);
    }
}
