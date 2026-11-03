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

    private static InfoDisplayDto Build(GoalList list, Dictionary<string, GoalProgress> prog, bool cookie, Dictionary<string, AuctionOffer> offers = null)
        => GoalHud.BuildDisplay(list, prog, cookie, t => t.ToLowerInvariant(), t => "s_" + t, offers ?? new(), t => t == "BZITEM");

    [Test]
    public void BuyOrderClickDependsOnCookie()
    {
        var step = new GoalStep { Kind = StepKind.BuyOrder, ItemTag = "A", Quantity = 5 };
        var g = new Goal { Id = "g1", Title = "Goal One" };
        var list = new GoalList { Goals = { g } };
        var prog = new Dictionary<string, GoalProgress> { ["g1"] = Progress(g, 1, 4, step) };
        var d = Build(list, prog, true);
        d.Lines[0].Text.Should().Be("§6§lGoal: §fGoal One §71/4 steps");
        d.StepLines().Single().OnClick.Should().Be("/cofl bzorder 5 s_A");
        d.StepLines()[0].Text.Should().Be("§cOrder 5x a");
        var nc = Build(list, prog, false);
        nc.StepLines().Single().OnClick.Should().Be("/cofl bzorder search 5 s_A");
        nc.StepLines()[0].Hover.Should().Be("Open the bazaar first, then click");
    }

    private static string Line(StepKind kind, string note = null, long qty = 3)
    {
        var g = new Goal { Id = "g", Title = "G" };
        var step = new GoalStep { Kind = kind, ItemTag = "A", Quantity = qty, Note = note };
        return Build(new GoalList { Goals = { g } }, new() { ["g"] = Progress(g, 0, 1, step) }, true).StepLines().Single().Text;
    }

    [Test]
    public void PurchaseLinesCarryAVerb()
    {
        Line(StepKind.BuyOrder).Should().Be("§cOrder 3x a");
        Line(StepKind.InstaBuy).Should().Be("§cBuy 3x a");
        Line(StepKind.Auction).Should().Be("§cBuy 3x a §7(auction)");
        Line(StepKind.Npc).Should().Be("§cBuy 3x a §7(NPC)");
        Line(StepKind.Npc, "Jerry").Should().Be("§cBuy 3x a §7(Jerry)");
    }

    [Test]
    public void SameItemStepsShowAsOneLine()
    {
        var g = new Goal { Id = "g", Title = "Gigantic Fishing Net" };
        var steps = new[]
        {
            new GoalStep { Kind = StepKind.InstaBuy, ItemTag = "A", Quantity = 128 },
            new GoalStep { Kind = StepKind.InstaBuy, ItemTag = "B", Quantity = 64 },
            new GoalStep { Kind = StepKind.InstaBuy, ItemTag = "C", Quantity = 15 },
            new GoalStep { Kind = StepKind.InstaBuy, ItemTag = "C", Quantity = 17 },
            new GoalStep { Kind = StepKind.InstaBuy, ItemTag = "D", Quantity = 1 },
        };
        var d = Build(new GoalList { Goals = { g } }, new() { ["g"] = Progress(g, 0, 16, steps) }, true);
        d.Lines[0].Text.Should().Be("§6§lGoal: §fGigantic Fishing Net §70/16 steps");
        d.StepLines().Select(l => l.Text).Should().Equal("§cBuy 128x a", "§cBuy 64x b", "§cBuy 32x c", "§cBuy 1x d");
        d.StepLines()[2].OnClick.Should().Be("/cofl bzorder 32 s_C");
    }

    [Test]
    public void MergeSameItemKeepsKindsAndNonPurchasesApart()
    {
        var steps = new[]
        {
            new GoalStep { Id = "1", Kind = StepKind.BuyOrder, ItemTag = "A", Quantity = 2 },
            new GoalStep { Id = "2", Kind = StepKind.InstaBuy, ItemTag = "A", Quantity = 3 },
            new GoalStep { Id = "3", Kind = StepKind.Craft, ItemTag = "A", Quantity = 1 },
            new GoalStep { Id = "4", Kind = StepKind.Craft, ItemTag = "A", Quantity = 1 },
            new GoalStep { Id = "5", Kind = StepKind.Npc, ItemTag = null, Quantity = 1 },
            new GoalStep { Id = "6", Kind = StepKind.Npc, ItemTag = null, Quantity = 1 },
        };
        GoalHud.MergeSameItem(steps).Should().HaveCount(6);
    }

    [Test]
    public void MergeSameItemSumsWithoutMutatingAndReturnsSingles()
    {
        var first = new GoalStep { Id = "1", Kind = StepKind.Npc, ItemTag = "A", Quantity = 2, Note = "n" };
        var second = new GoalStep { Id = "2", Kind = StepKind.Npc, ItemTag = "A", Quantity = 5 };
        var single = new GoalStep { Id = "3", Kind = StepKind.BuyOrder, ItemTag = "B", Quantity = 4 };
        var merged = GoalHud.MergeSameItem(new[] { first, single, second });
        merged.Should().HaveCount(2);
        merged[0].Should().NotBeSameAs(first);
        merged[0].Id.Should().Be("1");
        merged[0].Note.Should().Be("n");
        merged[0].Quantity.Should().Be(7);
        merged[1].Should().BeSameAs(single);
        first.Quantity.Should().Be(2);
        second.Quantity.Should().Be(5);
    }

    [Test]
    public void AuctionUsesLbinOrAh()
    {
        var step = new GoalStep { Kind = StepKind.Auction, ItemTag = "A", Quantity = 1 };
        var g = new Goal { Id = "g1", Title = "G" };
        var list = new GoalList { Goals = { g } };
        var prog = new Dictionary<string, GoalProgress> { ["g1"] = Progress(g, 0, 1, step) };
        Build(list, prog, true, new() { ["A"] = new AuctionOffer(FullUuid, 5) }).StepLines().Single().OnClick.Should().Be("/viewauction " + FullUuid);
        Build(list, prog, true).StepLines().Single().OnClick.Should().Be("/ah");
    }

    private const string FullUuid = "70e3559483ce4da8b1c2d3e4f5a6b7c8";

    [Test]
    public void ShortAuctionIdFallsBackToAh()
    {
        var step = new GoalStep { Kind = StepKind.Auction, ItemTag = "A", Quantity = 1 };
        var g = new Goal { Id = "g1", Title = "G" };
        var prog = new Dictionary<string, GoalProgress> { ["g1"] = Progress(g, 0, 1, step) };
        var line = Build(new GoalList { Goals = { g } }, prog, true, new() { ["A"] = new AuctionOffer("70e3559483ce4da8b", 5) }).StepLines().Single();
        line.OnClick.Should().Be("/ah");
        line.Text.Should().NotContain("viewauction");
    }

    [Test]
    public void DashedUuidIsAccepted()
    {
        new AuctionOffer("70e35594-83ce-4da8-b1c2-d3e4f5a6b7c8", 1).ViewCommand().Should().Be("/viewauction 70e3559483ce4da8b1c2d3e4f5a6b7c8");
        new AuctionOffer(null, 1).ViewCommand().Should().BeNull();
        new AuctionOffer("zz" + FullUuid[2..], 1).ViewCommand().Should().BeNull();
    }

    private static InfoDisplayDto Necron(AuctionOffer offer, double? planned = 2_500_000, long auctionQty = 1)
    {
        var g = new Goal { Id = "g", Title = "Necron's Leggings" };
        var steps = new[]
        {
            new GoalStep { Kind = StepKind.Auction, ItemTag = "LEGS", Quantity = auctionQty, UnitPrice = planned },
            new GoalStep { Kind = StepKind.InstaBuy, ItemTag = "HANDLE", Quantity = 8 },
        };
        var offers = offer == null ? new() : new Dictionary<string, AuctionOffer> { ["LEGS"] = offer };
        return Build(new GoalList { Goals = { g } }, new() { ["g"] = Progress(g, 0, 3, steps) }, true, offers);
    }

    [Test]
    public void AuctionLineShowsPriceWithoutProfitNoteAtPlannedPrice()
    {
        var d = Necron(new AuctionOffer(FullUuid, 2_500_000));
        d.StepLines()[0].Text.Should().Be("§cBuy 1x legs §7(auction) §f2.5m");
        d.StepLines()[0].Hover.Should().Be("Click to view the cheapest auction\nPlanned 2.5m, cheapest now 2.5m");
        d.StepLines()[0].OnClick.Should().Be("/viewauction " + FullUuid);
        d.StepLines()[1].Text.Should().Be("§7then Buy 8x handle");
        d.StepLines()[1].Hover.Should().StartWith("Buy the auction item first, it can sell before you get to it.\n");
        d.StepLines()[1].OnClick.Should().Be("/cofl bzorder 8 s_HANDLE");
    }

    [Test]
    public void AuctionLineStatesLostProfit()
    {
        var d = Necron(new AuctionOffer(FullUuid, 2_900_000));
        d.StepLines()[0].Text.Should().Be("§cBuy 1x legs §7(auction) §f2.9m §c-400k profit");
        d.StepLines()[0].Hover.Should().EndWith("Planned 2.5m, cheapest now 2.9m");
    }

    [Test]
    public void AuctionLineStatesGainedProfit()
    {
        Necron(new AuctionOffer(FullUuid, 2_000_000)).StepLines()[0].Text.Should().Be("§cBuy 1x legs §7(auction) §f2m §a+500k profit");
    }

    [Test]
    public void ProfitNoteNeedsHalfPercentSingleItemAndPlannedPrice()
    {
        Necron(new AuctionOffer(FullUuid, 2_510_000)).StepLines()[0].Text.Should().Be("§cBuy 1x legs §7(auction) §f2.51m");
        Necron(new AuctionOffer(FullUuid, 2_900_000), auctionQty: 2).StepLines()[0].Text.Should().Be("§cBuy 2x legs §7(auction) §f2.9m");
        var unknown = Necron(new AuctionOffer(FullUuid, 2_900_000), planned: null);
        unknown.StepLines()[0].Text.Should().Be("§cBuy 1x legs §7(auction) §f2.9m");
        unknown.StepLines()[0].Hover.Should().Be("Click to view the cheapest auction");
    }

    [Test]
    public void NoOfferKeepsPlainAuctionLine()
    {
        var d = Necron(null);
        d.StepLines()[0].Text.Should().Be("§cBuy 1x legs §7(auction)");
        d.StepLines()[0].OnClick.Should().Be("/ah");
        d.StepLines()[1].Text.Should().Be("§7then Buy 8x handle");
    }

    [Test]
    public void OtherPurchasesWaitForPendingAuction()
    {
        var g = new Goal { Id = "g", Title = "G" };
        var steps = new[]
        {
            new GoalStep { Kind = StepKind.BuyOrder, ItemTag = "A", Quantity = 2 },
            new GoalStep { Kind = StepKind.Npc, ItemTag = "B", Quantity = 3, Note = "Jerry" },
            new GoalStep { Kind = StepKind.Auction, ItemTag = "C", Quantity = 1 },
            new GoalStep { Kind = StepKind.Craft, ItemTag = "D", Quantity = 1 },
        };
        var d = Build(new GoalList { Goals = { g } }, new() { ["g"] = Progress(g, 0, 4, steps) }, true);
        d.StepLines().Select(l => l.Text).Should().Equal("§7then Order 2x a", "§7then Buy 3x b §7(Jerry)", "§cBuy 1x c §7(auction)", "§aCraft d: click to open /recipe");
        d.StepLines()[0].Hover.Should().StartWith("Buy the auction item first");
        d.StepLines()[3].Hover.Should().Be("Click to open the recipe");
    }

    [Test]
    public void MergeSameItemCarriesWeightedUnitPrice()
    {
        var merged = GoalHud.MergeSameItem(new[]
        {
            new GoalStep { Id = "1", Kind = StepKind.InstaBuy, ItemTag = "A", Quantity = 1, UnitPrice = 100 },
            new GoalStep { Id = "2", Kind = StepKind.InstaBuy, ItemTag = "A", Quantity = 3, UnitPrice = 200 },
            new GoalStep { Id = "3", Kind = StepKind.InstaBuy, ItemTag = "A", Quantity = 4 },
            new GoalStep { Id = "4", Kind = StepKind.Npc, ItemTag = "B", Quantity = 1 },
            new GoalStep { Id = "5", Kind = StepKind.Npc, ItemTag = "B", Quantity = 1 },
        });
        merged[0].UnitPrice.Should().Be(175);
        merged[1].UnitPrice.Should().BeNull();
    }

    [Test]
    public void ShortPriceFormats()
    {
        GoalHud.ShortPrice(2_500_000).Should().Be("2.5m");
        GoalHud.ShortPrice(980_000).Should().Be("980k");
        GoalHud.ShortPrice(400_000).Should().Be("400k");
        GoalHud.ShortPrice(999_900).Should().Be("1m");
        GoalHud.ShortPrice(1_234_567).Should().Be("1.23m");
        GoalHud.ShortPrice(2_500_000_000).Should().Be("2.5b");
        GoalHud.ShortPrice(950).Should().Be("950");
        GoalHud.ShortPrice(0).Should().Be("0");
    }

    [Test]
    public void OfferPriceChangesTheHash()
    {
        GoalHud.Hash(Necron(new AuctionOffer(FullUuid, 2_500_000))).Should().NotBe(GoalHud.Hash(Necron(new AuctionOffer(FullUuid, 2_900_000))));
    }

    [Test]
    public void UnitPriceRoundTripsThroughBothSerializers()
    {
        var step = new GoalStep { Kind = StepKind.Auction, ItemTag = "A", Quantity = 1, UnitPrice = 12.5 };
        JsonConvert.DeserializeObject<GoalStep>(JsonConvert.SerializeObject(step)).UnitPrice.Should().Be(12.5);
        System.Text.Json.JsonSerializer.Deserialize<GoalStep>(System.Text.Json.JsonSerializer.Serialize(step)).UnitPrice.Should().Be(12.5);
        JsonConvert.DeserializeObject<GoalStep>("{\"quantity\":1}").UnitPrice.Should().BeNull();
    }

    [Test]
    public void OtherGoalsListedAndFocusRespected()
    {
        var g1 = new Goal { Id = "g1", Title = "One" };
        var g2 = new Goal { Id = "g2", Title = "Two" };
        var list = new GoalList { Goals = { g1, g2 }, FocusId = "g2" };
        var prog = new Dictionary<string, GoalProgress> { ["g1"] = Progress(g1, 2, 5), ["g2"] = Progress(g2, 0, 3) };
        var d = Build(list, prog, true);
        d.Lines[0].Text.Should().Contain("Two");
        d.StepLines().Single().Text.Should().Be("§7▸ One 2/5");
        d.StepLines()[0].OnClick.Should().Be("/cofl goal focus g1");
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
        d.StepLines().Select(l => l.OnClick).Should().Equal("/recipe x", "/warp forge", "/ah");
    }

    private static InfoDisplayDto SellLine(string tag, bool cookie)
    {
        var g = new Goal { Id = "g", Title = "G" };
        var step = new GoalStep { Kind = StepKind.Sell, ItemTag = tag, Quantity = 1 };
        return Build(new GoalList { Goals = { g } }, new() { ["g"] = Progress(g, 0, 1, step) }, cookie);
    }

    [Test]
    public void Sell_WithCookie_BazaarItemOpensBazaar()
        => SellLine("BZITEM", true).StepLines().Single().OnClick.Should().Be("/bz bzitem");

    [Test]
    public void Sell_NoCookie_AuctionItemHasNoCommandAndSaysWhereToGo()
    {
        var line = SellLine("AHITEM", false).StepLines().Single();
        line.OnClick.Should().BeNull();
        line.Text.Should().Be("§aSell ahitem §7at the Auction Master");
        line.Hover.Should().Be(GoalHud.NoCookieAuctionHover);
    }

    [Test]
    public void Sell_NoCookie_BazaarItemHasNoCommandAndSaysWhereToGo()
    {
        var line = SellLine("BZITEM", false).StepLines().Single();
        line.OnClick.Should().BeNull();
        line.Text.Should().Be("§aSell bzitem §7at the Bazaar");
        line.Hover.Should().Contain("/bz");
    }

    [Test]
    public void Auction_NoCookie_NoOfferOrShortId_HasNoAhCommand_ViewAuctionStillWorks()
    {
        var g = new Goal { Id = "g", Title = "G" };
        var step = new GoalStep { Kind = StepKind.Auction, ItemTag = "AH", Quantity = 1 };
        var list = new GoalList { Goals = { g } };
        var prog = new Dictionary<string, GoalProgress> { ["g"] = Progress(g, 0, 1, step) };

        var none = Build(list, prog, false).StepLines().Single();
        none.OnClick.Should().BeNull();
        none.Text.Should().EndWith("at the Auction Master");

        var uuid = "70e3559483ce4da8b3c5a1d2e3f40516";
        var viewable = Build(list, prog, false, new() { ["AH"] = new AuctionOffer(uuid, 1000) }).StepLines().Single();
        viewable.OnClick.Should().StartWith("/viewauction ");
        viewable.Text.Should().NotContain("Auction Master");
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

    [Test]
    public void CompleteGoal_ShowsOneCompleteLine()
    {
        var g = new Goal { Id = "g", Title = "G" };
        var p = Progress(g, 4, 4);
        p.Complete = true;
        var d = Build(new GoalList { Goals = { g } }, new() { ["g"] = p }, true);
        d.Lines[0].Text.Should().Contain("4/4");
        d.StepLines().Select(l => l.Text).Should().Equal("§aGoal complete, all steps are done", "§7▸ §eStart this goal again", "§7▸ §cRemove this goal");
        d.StepLines().Select(l => l.OnClick).Should().Equal(null, "/cofl goal reset g", "/cofl goal remove g");
    }

    [Test]
    public void CompleteGoal_OtherGoalsFollowTheClickLines()
    {
        var g = new Goal { Id = "g", Title = "G" };
        var other = new Goal { Id = "o", Title = "Other" };
        var p = Progress(g, 4, 4);
        p.Complete = true;
        var d = Build(new GoalList { Goals = { g, other }, FocusId = "g" }, new() { ["g"] = p }, true);
        d.StepLines().Should().HaveCount(4);
        d.StepLines()[3].OnClick.Should().Be("/cofl goal focus o");
    }

    [Test]
    public void HeaderIsTheFirstLine_HoverAndClickCancelTheFocusedGoal()
    {
        var g1 = new Goal { Id = "g1", Title = "One" };
        var g2 = new Goal { Id = "g2", Title = "Bazaar flip: Wolf Tooth x4383", Plan = new BazaarPlanInfo { Mode = "Active" } };
        var list = new GoalList { Goals = { g1, g2 }, FocusId = "g2" };
        var d = Build(list, new() { ["g2"] = Progress(g2, 0, 6) }, true);

        d.Title.Should().BeNull("the mod has no hover or click on the title");
        d.Lines[0].Text.Should().Be("§6§lGoal: §fBazaar flip: Wolf Tooth x4383 §70/6 steps §7[Active]");
        d.Lines[0].Hover.Should().Be(GoalHud.CancelHover).And.Contain("Click to cancel this goal");
        d.Lines[0].OnClick.Should().Be("/cofl goal cancel g2");
        d.StepLines().Select(l => l.OnClick).Should().Equal("/cofl goal focus g1");
    }

    private static ChatPartDto Header(Goal g)
        => GoalHud.BuildDisplay(new GoalList { Goals = { g } }, new Dictionary<string, GoalProgress>(), true, t => t, t => t, new Dictionary<string, AuctionOffer>(), null, Now).Lines[0];

    private static readonly System.DateTime Now = new(2026, 1, 1, 12, 0, 0, System.DateTimeKind.Utc);
    private const string Marker = " §c[✖]";

    private static void AssertHeader(Goal g, bool marker)
    {
        var h = Header(g);
        h.Text.EndsWith(Marker).Should().Be(marker);
        h.Hover.Should().Be(GoalHud.CancelHover);
        h.OnClick.Should().Be("/cofl goal cancel " + g.Id);
    }

    [Test]
    public void CancelMarker_AbsentForGoalCreatedOneMinuteAgo()
        => AssertHeader(new Goal { Id = "g", Title = "G", CreatedAt = Now.AddMinutes(-1) }, false);

    [Test]
    public void CancelMarker_PresentForIdleGoalCreatedThreeMinutesAgo()
        => AssertHeader(new Goal { Id = "g", Title = "G", CreatedAt = Now.AddMinutes(-3) }, true);

    [TestCase(true)]
    [TestCase(false)]
    public void CancelMarker_AbsentWhenNestedStepWasRecentlyDoneOrOrdered(bool done)
    {
        var stamp = Now.AddSeconds(-30);
        var leaf = new GoalStep { Kind = StepKind.BuyOrder, DoneAt = done ? stamp : null, OrderedAt = done ? null : stamp };
        var root = new GoalStep { Kind = StepKind.Sell, Children = { new GoalStep { Kind = StepKind.Craft, Children = { leaf } } } };
        AssertHeader(new Goal { Id = "g", Title = "G", CreatedAt = Now.AddMinutes(-10), Root = root }, false);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void CancelMarker_AbsentWhenBazaarOrderRecentlyCreatedOrClosed(bool created)
    {
        var old = Now.AddMinutes(-10);
        var order = created
            ? new BazaarFlipOrder { CreatedAt = Now.AddSeconds(-30) }
            : new BazaarFlipOrder { CreatedAt = old, ClosedAt = Now.AddSeconds(-30) };
        var g = new Goal { Id = "g", Title = "G", CreatedAt = old, BazaarFlip = new BazaarFlipState { Orders = { order } } };
        AssertHeader(g, false);
    }

    [Test]
    public void CancelMarker_PresentWhenAllActivityIsOld()
    {
        var old = Now.AddMinutes(-10);
        var g = new Goal
        {
            Id = "g", Title = "G", CreatedAt = old, Root = new GoalStep { DoneAt = old },
            BazaarFlip = new BazaarFlipState { Orders = { new BazaarFlipOrder { CreatedAt = old, ClosedAt = old } } }
        };
        AssertHeader(g, true);
    }

    [Test]
    public void MarkerAppearingChangesTheHudHash()
    {
        var g = new Goal { Id = "g", Title = "G", CreatedAt = Now.AddMinutes(-1) };
        var list = new GoalList { Goals = { g } };
        string H(System.DateTime at) => GoalHud.Hash(GoalHud.BuildDisplay(list, new Dictionary<string, GoalProgress>(), true, t => t, t => t, new Dictionary<string, AuctionOffer>(), null, at));
        H(Now).Should().NotBe(H(Now.AddMinutes(2)));
    }

    [Test]
    public void HeaderLineIsSerializedAsAModLineObject()
    {
        var g = new Goal { Id = "g", Title = "G" };
        var json = JObject.Parse(JsonConvert.SerializeObject(Build(new GoalList { Goals = { g } }, new(), true)));
        var header = (JObject)json["lines"][0];
        header.Properties().Select(p => p.Name).Should().BeEquivalentTo(new[] { "text", "hover", "onClick" });
        header["onClick"].Value<string>().Should().Be("/cofl goal cancel g");
    }

    [Test]
    public void IncompleteGoal_ShowsNoRestartOrRemoveLines()
    {
        var g = new Goal { Id = "g", Title = "G" };
        var step = new GoalStep { Kind = StepKind.Craft, ItemTag = "X", Quantity = 1 };
        var d = Build(new GoalList { Goals = { g } }, new() { ["g"] = Progress(g, 1, 2, step) }, true);
        d.StepLines().Select(l => l.OnClick).Should().NotContain(c => c.Contains("goal reset") || c.Contains("goal remove"));
    }
}
