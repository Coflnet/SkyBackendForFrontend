using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Crafts.Client.Model;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared.Goals;

public class GoalPlannerTests
{
    private static readonly HashSet<string> Bazaar = new() { "BZ_ITEM", "BZ_ORDER" };
    private static AcquisitionFill F(string s, long q, double price = 0) => new(source: s, quantity: q, unitPrice: price, cost: price * q);
    private static string Name(string t) => t.ToLowerInvariant();

    private static CraftAcquisitionPlan Buy(string tag, long qty, string method, params AcquisitionFill[] fills)
        => new(itemId: tag, quantity: qty, method: method, purchases: fills.ToList(), ingredients: new());

    private static CraftAcquisitionPlan Craft(string tag, long qty, List<CraftAcquisitionPlan> ingredients, List<AcquisitionFill> purchases = null)
        => new(itemId: tag, quantity: qty, method: "craft", ingredients: ingredients, purchases: purchases ?? new());

    [Test]
    public void MapsPurchaseKinds()
    {
        var plan = Craft("ROOT", 2, new()
        {
            Buy("BZ_ORDER", 10, "buy", F("order", 10)),
            Buy("BZ_ITEM", 5, "buy", F("insta", 5)),
            Buy("AH_ITEM", 1, "buy", F("insta", 1)),
            Buy("NPC_ITEM", 3, "npc", F("npc", 3)),
        });
        var root = GoalPlanner.FromPlan(plan, Bazaar, Name).Single();
        root.Kind.Should().Be(StepKind.Craft);
        root.Quantity.Should().Be(2);
        root.Children.Select(c => (c.Kind, c.Quantity)).Should().Equal(
            (StepKind.BuyOrder, 10L), (StepKind.InstaBuy, 5L), (StepKind.Auction, 1L), (StepKind.Npc, 3L));
    }

    [Test]
    public void NestedCraftBecomesChildCraft()
    {
        var plan = Craft("ROOT", 1, new() { Craft("SUB", 4, new() { Buy("BZ_ITEM", 8, "buy", F("insta", 8)) }) });
        var root = GoalPlanner.FromPlan(plan, Bazaar, Name).Single();
        root.Children.Single().Kind.Should().Be(StepKind.Craft);
        root.Children[0].Children.Single().Kind.Should().Be(StepKind.InstaBuy);
    }

    [Test]
    public void HybridNodeAddsPurchases()
    {
        var plan = Craft("BZ_ITEM", 10, new(), new() { new("order", 4) });
        var root = GoalPlanner.FromPlan(plan, Bazaar, Name).Single();
        root.Kind.Should().Be(StepKind.Craft);
        root.Children.Single().Kind.Should().Be(StepKind.BuyOrder);
        root.Children[0].Quantity.Should().Be(4);
    }

    [Test]
    public void ShortfallGoesToFirstBazaarStep()
    {
        var plan = Buy("BZ_ITEM", 10, "buy", F("npc", 2), F("insta", 3));
        var steps = GoalPlanner.FromPlan(plan, Bazaar, Name);
        steps.Sum(s => s.Quantity).Should().Be(10);
        steps.Single(s => s.Kind == StepKind.InstaBuy).Quantity.Should().Be(8);
    }

    [Test]
    public void SameKindSiblingsAreMerged()
    {
        var plan = Buy("BZ_ITEM", 10, "buy", F("order", 4), F("order", 6));
        var steps = GoalPlanner.FromPlan(plan, Bazaar, Name);
        steps.Single().Quantity.Should().Be(10);
    }

    [Test]
    public void DepthIsCapped()
    {
        var plan = Craft("C0", 1, new());
        var cur = plan;
        for (var i = 1; i < 20; i++)
        {
            var next = Craft("C" + i, 1, new());
            cur.Ingredients.Add(next);
            cur = next;
        }
        var root = GoalPlanner.FromPlan(plan, Bazaar, Name).Single();
        var depth = 0;
        for (var s = root; s.Children.Count > 0; s = s.Children[0])
            depth++;
        depth.Should().Be(GoalPlanner.MaxDepth);
    }

    [Test]
    public async Task SellWrapsRootAndTitleHasQuantity()
    {
        var crafts = new Mock<ICraftsApi>();
        crafts.Setup(c => c.GetAcquisitionAsync("ROOT", 3, true, 0, default))
            .ReturnsAsync(Craft("ROOT", 3, new() { Buy("BZ_ITEM", 3, "buy", F("insta", 3)) }));
        var goal = await new GoalPlanner(crafts.Object, new Mock<IForgeApi>().Object)
            .Build(StepKind.Craft, "ROOT", 3, true, Bazaar, t => "Root Item", "mod");
        goal.Root.Kind.Should().Be(StepKind.Sell);
        goal.Root.Children.Single().Kind.Should().Be(StepKind.Craft);
        goal.Title.Should().Be("Root Item ×3");
        goal.Source.Should().Be("mod");
    }

    [Test]
    public async Task ForgeRootExpandsIngredients()
    {
        var forge = new Mock<IForgeApi>();
        forge.Setup(f => f.GetAllForgeAsync(0, default)).ReturnsAsync(new List<ForgeFlip>
        {
            new(craftData: new ProfitableCraft(itemId: "FORGED", ingredients: new()
            {
                new(itemId: "BZ_ITEM", count: 2),
                new(itemId: "SUBCRAFT", count: 1, type: "craft"),
                new(itemId: "BITS_THING", count: 5, type: "bits"),
            }))
        });
        var crafts = new Mock<ICraftsApi>();
        crafts.Setup(c => c.GetAcquisitionAsync("BZ_ITEM", 4, null, 0, default)).ReturnsAsync(Buy("BZ_ITEM", 4, "buy", F("insta", 4)));
        crafts.Setup(c => c.GetAcquisitionAsync("SUBCRAFT", 2, true, 0, default)).ReturnsAsync(Craft("SUBCRAFT", 2, new()));
        var goal = await new GoalPlanner(crafts.Object, forge.Object)
            .Build(StepKind.Forge, "FORGED", 2, false, Bazaar, Name, "website");
        goal.Root.Kind.Should().Be(StepKind.Forge);
        goal.Root.Children.Select(c => c.Kind).Should().Equal(StepKind.InstaBuy, StepKind.Craft, StepKind.Npc);
        goal.Root.Children[2].Note.Should().Be("costs 5 bits");
        goal.Root.Children[2].Quantity.Should().Be(10);
    }

    [Test]
    public async Task NonForgeItemThrows()
    {
        var forge = new Mock<IForgeApi>();
        forge.Setup(f => f.GetAllForgeAsync(0, default)).ReturnsAsync(new List<ForgeFlip>());
        var act = () => new GoalPlanner(new Mock<ICraftsApi>().Object, forge.Object)
            .Build(StepKind.Forge, "X", 1, false, Bazaar, Name, "mod");
        await act.Should().ThrowAsync<System.ArgumentException>().WithMessage("not a forge item*");
    }

    [Test]
    public void FillUnitPriceIsStoredAndMergedWeighted()
    {
        var plan = Craft("ROOT", 1, new()
        {
            Buy("AH_ITEM", 1, "buy", F("insta", 1, 2_500_000)),
            Buy("BZ_ITEM", 4, "buy", F("insta", 1, 100), F("insta", 3, 200)),
            Buy("NO_PRICE", 1, "buy", F("insta", 1)),
        });
        var root = GoalPlanner.FromPlan(plan, Bazaar, Name).Single();
        root.Children.Select(c => c.UnitPrice).Should().Equal(2_500_000d, 175d, null);
        root.Children[1].Quantity.Should().Be(4);
    }
}
