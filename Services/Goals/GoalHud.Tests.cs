using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared.Goals;

public class GoalHudTests
{
    private static GoalProgress Progress(Goal g, int done, int total, params GoalStep[] actionable)
        => new() { Goal = g, Done = done, Total = total, Actionable = actionable.ToList() };

    private static InfoDisplayDto Build(GoalList list, Dictionary<string, GoalProgress> prog, bool cookie, Dictionary<string, string> lbin = null)
        => GoalHud.BuildDisplay(list, prog, cookie, t => t.ToLowerInvariant(), t => "s_" + t, lbin ?? new());

    [Test]
    public void BuyOrderClickDependsOnCookie()
    {
        var step = new GoalStep { Kind = StepKind.BuyOrder, ItemTag = "A", Quantity = 5 };
        var g = new Goal { Id = "g1", Title = "Goal One" };
        var list = new GoalList { Goals = { g } };
        var prog = new Dictionary<string, GoalProgress> { ["g1"] = Progress(g, 1, 4, step) };
        var d = Build(list, prog, true);
        d.Title.Should().Be("§6§lGoal: §fGoal One §71/4");
        d.Lines.Single().OnClick.Should().Be("/cofl bzorder 5 s_A");
        d.Lines[0].Text.Should().Be("§c5x a");
        var nc = Build(list, prog, false);
        nc.Lines.Single().OnClick.Should().Be("/cofl bzorder search 5 s_A");
        nc.Lines[0].Hover.Should().Be("Open the bazaar first, then click");
    }

    [Test]
    public void AuctionUsesLbinOrAh()
    {
        var step = new GoalStep { Kind = StepKind.Auction, ItemTag = "A", Quantity = 1 };
        var g = new Goal { Id = "g1", Title = "G" };
        var list = new GoalList { Goals = { g } };
        var prog = new Dictionary<string, GoalProgress> { ["g1"] = Progress(g, 0, 1, step) };
        Build(list, prog, true, new() { ["A"] = "uuid1" }).Lines.Single().OnClick.Should().Be("/viewauction uuid1");
        Build(list, prog, true).Lines.Single().OnClick.Should().Be("/ah");
    }

    [Test]
    public void OtherGoalsListedAndFocusRespected()
    {
        var g1 = new Goal { Id = "g1", Title = "One" };
        var g2 = new Goal { Id = "g2", Title = "Two" };
        var list = new GoalList { Goals = { g1, g2 }, FocusId = "g2" };
        var prog = new Dictionary<string, GoalProgress> { ["g1"] = Progress(g1, 2, 5), ["g2"] = Progress(g2, 0, 3) };
        var d = Build(list, prog, true);
        d.Title.Should().Contain("Two");
        d.Lines.Single().Text.Should().Be("§7▸ One 2/5");
        d.Lines[0].OnClick.Should().Be("/cofl goal focus g1");
    }

    [Test]
    public void CraftForgeSellClicks()
    {
        var g = new Goal { Id = "g", Title = "G" };
        var steps = new[]
        {
            new GoalStep { Kind = StepKind.Craft, ItemTag = "X", Quantity = 1 },
            new GoalStep { Kind = StepKind.Forge, ItemTag = "X", Quantity = 1 },
            new GoalStep { Kind = StepKind.Sell, ItemTag = "X", Quantity = 1 },
        };
        var d = Build(new GoalList { Goals = { g } }, new() { ["g"] = Progress(g, 0, 3, steps) }, true);
        d.Lines.Select(l => l.OnClick).Should().Equal("/recipe x", "/warp forge", "/bz x");
    }

    [Test]
    public void EmptyListClears()
    {
        var d = Build(new GoalList(), new(), true);
        d.Clear.Should().BeTrue();
        d.Lines.Should().BeEmpty();
        JObject.Parse(JsonConvert.SerializeObject(d)).Properties().Select(p => p.Name)
            .Should().BeEquivalentTo(new[] { "id", "title", "lines", "ttl", "clear" });
    }

    [Test]
    public void HashIsStableAndSensitive()
    {
        var a = new InfoDisplayDto { Title = "t", Lines = { new ChatPartDto { Text = "x" } } };
        var b = new InfoDisplayDto { Title = "t", Lines = { new ChatPartDto { Text = "x" } } };
        GoalHud.Hash(a).Should().Be(GoalHud.Hash(b));
        b.Lines[0].OnClick = "/x";
        GoalHud.Hash(a).Should().NotBe(GoalHud.Hash(b));
    }
}
