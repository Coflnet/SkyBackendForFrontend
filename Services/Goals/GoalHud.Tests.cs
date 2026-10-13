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
        d.Title.Should().Be("§6§lGoal: §fGoal One §71/4 steps");
        d.Lines.Single().OnClick.Should().Be("/cofl bzorder 5 s_A");
        d.Lines[0].Text.Should().Be("§cOrder 5x a");
        var nc = Build(list, prog, false);
        nc.Lines.Single().OnClick.Should().Be("/cofl bzorder search 5 s_A");
        nc.Lines[0].Hover.Should().Be("Open the bazaar first, then click");
    }

    private static string Line(StepKind kind, string note = null, long qty = 3)
    {
        var g = new Goal { Id = "g", Title = "G" };
        var step = new GoalStep { Kind = kind, ItemTag = "A", Quantity = qty, Note = note };
        return Build(new GoalList { Goals = { g } }, new() { ["g"] = Progress(g, 0, 1, step) }, true).Lines.Single().Text;
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
        d.Title.Should().Be("§6§lGoal: §fGigantic Fishing Net §70/16 steps");
        d.Lines.Select(l => l.Text).Should().Equal("§cBuy 128x a", "§cBuy 64x b", "§cBuy 32x c", "§cBuy 1x d");
        d.Lines[2].OnClick.Should().Be("/cofl bzorder 32 s_C");
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
        Build(list, prog, true, new() { ["A"] = new AuctionOffer(FullUuid, 5) }).Lines.Single().OnClick.Should().Be("/viewauction " + FullUuid);
        Build(list, prog, true).Lines.Single().OnClick.Should().Be("/ah");
    }

    private const string FullUuid = "70e3559483ce4da8b1c2d3e4f5a6b7c8";

    [Test]
    public void ShortAuctionIdFallsBackToAh()
    {
        var step = new GoalStep { Kind = StepKind.Auction, ItemTag = "A", Quantity = 1 };
        var g = new Goal { Id = "g1", Title = "G" };
        var prog = new Dictionary<string, GoalProgress> { ["g1"] = Progress(g, 0, 1, step) };
        var line = Build(new GoalList { Goals = { g } }, prog, true, new() { ["A"] = new AuctionOffer("70e3559483ce4da8b", 5) }).Lines.Single();
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
        d.Lines[0].Text.Should().Be("§cBuy 1x legs §7(auction) §f2.5m");
        d.Lines[0].Hover.Should().Be("Click to view the cheapest auction\nPlanned 2.5m, cheapest now 2.5m");
        d.Lines[0].OnClick.Should().Be("/viewauction " + FullUuid);
        d.Lines[1].Text.Should().Be("§7then Buy 8x handle");
        d.Lines[1].Hover.Should().StartWith("Buy the auction item first, it can sell before you get to it.\n");
        d.Lines[1].OnClick.Should().Be("/cofl bzorder 8 s_HANDLE");
    }

    [Test]
    public void AuctionLineStatesLostProfit()
    {
        var d = Necron(new AuctionOffer(FullUuid, 2_900_000));
        d.Lines[0].Text.Should().Be("§cBuy 1x legs §7(auction) §f2.9m §c-400k profit");
        d.Lines[0].Hover.Should().EndWith("Planned 2.5m, cheapest now 2.9m");
    }

    [Test]
    public void AuctionLineStatesGainedProfit()
    {
        Necron(new AuctionOffer(FullUuid, 2_000_000)).Lines[0].Text.Should().Be("§cBuy 1x legs §7(auction) §f2m §a+500k profit");
    }

    [Test]
    public void ProfitNoteNeedsHalfPercentSingleItemAndPlannedPrice()
    {
        Necron(new AuctionOffer(FullUuid, 2_510_000)).Lines[0].Text.Should().Be("§cBuy 1x legs §7(auction) §f2.51m");
        Necron(new AuctionOffer(FullUuid, 2_900_000), auctionQty: 2).Lines[0].Text.Should().Be("§cBuy 2x legs §7(auction) §f2.9m");
        var unknown = Necron(new AuctionOffer(FullUuid, 2_900_000), planned: null);
        unknown.Lines[0].Text.Should().Be("§cBuy 1x legs §7(auction) §f2.9m");
        unknown.Lines[0].Hover.Should().Be("Click to view the cheapest auction");
    }

    [Test]
    public void NoOfferKeepsPlainAuctionLine()
    {
        var d = Necron(null);
        d.Lines[0].Text.Should().Be("§cBuy 1x legs §7(auction)");
        d.Lines[0].OnClick.Should().Be("/ah");
        d.Lines[1].Text.Should().Be("§7then Buy 8x handle");
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
        d.Lines.Select(l => l.Text).Should().Equal("§7then Order 2x a", "§7then Buy 3x b §7(Jerry)", "§cBuy 1x c §7(auction)", "§aCraft d: click to open /recipe");
        d.Lines[0].Hover.Should().StartWith("Buy the auction item first");
        d.Lines[3].Hover.Should().Be("Click to open the recipe");
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
        d.Lines.Select(l => l.OnClick).Should().Equal("/recipe x", "/warp forge", "/ah");
    }

    private static InfoDisplayDto SellLine(string tag, bool cookie)
    {
        var g = new Goal { Id = "g", Title = "G" };
        var step = new GoalStep { Kind = StepKind.Sell, ItemTag = tag, Quantity = 1 };
        return Build(new GoalList { Goals = { g } }, new() { ["g"] = Progress(g, 0, 1, step) }, cookie);
    }

    [Test]
    public void Sell_WithCookie_BazaarItemOpensBazaar()
        => SellLine("BZITEM", true).Lines.Single().OnClick.Should().Be("/bz bzitem");

    [Test]
    public void Sell_NoCookie_AuctionItemHasNoCommandAndSaysWhereToGo()
    {
        var line = SellLine("AHITEM", false).Lines.Single();
        line.OnClick.Should().BeNull();
        line.Text.Should().Be("§aSell ahitem §7at the Auction Master");
        line.Hover.Should().Be(GoalHud.NoCookieAuctionHover);
    }

    [Test]
    public void Sell_NoCookie_BazaarItemHasNoCommandAndSaysWhereToGo()
    {
        var line = SellLine("BZITEM", false).Lines.Single();
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

        var none = Build(list, prog, false).Lines.Single();
        none.OnClick.Should().BeNull();
        none.Text.Should().EndWith("at the Auction Master");

        var uuid = "70e3559483ce4da8b3c5a1d2e3f40516";
        var viewable = Build(list, prog, false, new() { ["AH"] = new AuctionOffer(uuid, 1000) }).Lines.Single();
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
        d.Title.Should().Contain("4/4");
        d.Lines.Should().ContainSingle().Which.Text.Should().Contain("Goal complete");
        d.Lines[0].OnClick.Should().BeNull();
    }
}
