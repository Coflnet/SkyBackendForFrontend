using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class MinionCalculatorTests
{
    private static MinionService.Product Product(string tag, double perTime, double npc = 0)
        => new(tag, perTime, npc, null, null) { Tag = tag };

    private static MinionService.Cost Cost(string name, double quantity, string tag = null)
        => new(name, quantity) { Tag = tag };

    /// <summary>A cobblestone minion, whose real tier I interval is 14s, with a one-ingredient recipe per tier.</summary>
    private static MinionService.Minion Cobblestone(int tiers = 2)
        => new(
            "Cobblestone Minion",
            Enumerable.Repeat(14d, tiers).ToList(),
            Enumerable.Repeat(64, tiers).ToList(),
            "Mining",
            Enumerable.Range(0, tiers).Select(i => new List<MinionService.Cost> { Cost("Cobblestone", 80, "COBBLESTONE") }).ToList(),
            new[] { Product("COBBLESTONE", 1, npc: 1) });

    private static Dictionary<string, MinionItemPrice> Prices(params (string Tag, double Buy, double Sell)[] entries)
        => entries.ToDictionary(e => e.Tag, e => new MinionItemPrice(e.Buy, e.Sell));

    /// <summary>
    /// The wiki is explicit: a tier I cobblestone minion acts every 14 seconds but yields one
    /// cobblestone every 28. Computing off the action interval reports exactly twice the truth, which
    /// is what every minion number shipped before this test.
    /// </summary>
    [Test]
    public void AMinionYieldsOnEveryOtherAction()
    {
        var calculator = new MinionCalculator(Prices(("COBBLESTONE", 3, 2)));
        var query = new MinionQuery { OfflineSeconds = MinionCalculator.SecondsPerDay, AllowCompaction = false };

        var outlook = calculator.Evaluate(Cobblestone(), tier: 1, query);

        outlook.SecondsBetweenActions.Should().Be(14);
        outlook.SecondsBetweenHarvests.Should().Be(28);
    }

    /// <summary>Speed boosts are additive and divide the interval; the wiki works this exact case.</summary>
    [Test]
    public void SpeedBoostsDivideTheIntervalRatherThanSubtractFromIt()
    {
        var clay = new MinionService.Minion(
            "Clay Minion", new List<double> { 16 }, new List<int> { 960 }, "Mining",
            new[] { new List<MinionService.Cost>() }, new[] { Product("CLAY_BALL", 4) });
        var calculator = new MinionCalculator(Prices(("CLAY_BALL", 1, 1)));

        var outlook = calculator.Evaluate(clay, tier: 1, new MinionQuery { SpeedBoost = 0.10, AllowCompaction = false });

        outlook.SecondsBetweenActions.Should().BeApproximately(14.55, 0.01);
    }

    /// <summary>
    /// The reason "best minion" has no answer without an offline time: once storage fills the minion
    /// stops, so two days away does not pay twice what one day away pays.
    /// </summary>
    [Test]
    public void StorageCapsWhatALongAbsenceCollects()
    {
        var calculator = new MinionCalculator(Prices(("COBBLESTONE", 3, 2)));

        var oneDay = calculator.Evaluate(Cobblestone(), 1, new MinionQuery { OfflineSeconds = MinionCalculator.SecondsPerDay, AllowCompaction = false });
        var oneWeek = calculator.Evaluate(Cobblestone(), 1, new MinionQuery { OfflineSeconds = 7 * MinionCalculator.SecondsPerDay, AllowCompaction = false });

        // 64 slots at one cobblestone per 28s fill in well under an hour, so both are storage limited.
        oneDay.StorageLimited.Should().BeTrue();
        oneDay.SecondsToFill.Should().Be(64 * 28);
        // Leaving it seven times as long collects the same single fill, so the daily rate is a seventh.
        oneWeek.CoinsPerDay.Should().BeApproximately(oneDay.CoinsPerDay / 7, 0.0001);
    }

    /// <summary>A minion collected before it fills is limited by speed, not storage.</summary>
    [Test]
    public void AShortAbsenceIsLimitedBySpeedNotStorage()
    {
        var calculator = new MinionCalculator(Prices(("COBBLESTONE", 3, 2)));

        var outlook = calculator.Evaluate(Cobblestone(), 1, new MinionQuery { OfflineSeconds = 600, AllowCompaction = false });

        outlook.StorageLimited.Should().BeFalse();
        // 600s at one per 28s, sold at the buy price because a sell offer is the default.
        outlook.CoinsPerDay.Should().BeApproximately(600d / 28 * 3 * (MinionCalculator.SecondsPerDay / 600), 0.0001);
    }

    /// <summary>
    /// Compacting multiplies effective storage by the compaction ratio, which is what makes a minion
    /// survive days of absence -- but the enchanted form regularly sells for less than the raw items it
    /// replaced, so the calculator has to compare rather than assume.
    /// </summary>
    [Test]
    public void CompactionIsChosenOnValueNotHabit()
    {
        var compaction = new Dictionary<string, MinionCompaction> { ["COBBLESTONE"] = new("ENCHANTED_COBBLESTONE", 160) };
        var weekLong = new MinionQuery { OfflineSeconds = 7 * MinionCalculator.SecondsPerDay };

        // Enchanted cobblestone worth a full 160 raw: compaction is pure upside over a long absence.
        var fair = new MinionCalculator(Prices(("COBBLESTONE", 3, 2), ("ENCHANTED_COBBLESTONE", 480, 320)), null, compaction);
        var fairOutlook = fair.Evaluate(Cobblestone(), 1, weekLong);
        fairOutlook.Compacted.Should().BeTrue();

        // The wiki's own warning: 160 glowstone dust is worth far more than one enchanted glowstone.
        // Over a short absence, where storage never fills, compacting only destroys value.
        var lossy = new MinionCalculator(Prices(("COBBLESTONE", 3, 2), ("ENCHANTED_COBBLESTONE", 100, 80)), null, compaction);
        var shortOutlook = lossy.Evaluate(Cobblestone(), 1, new MinionQuery { OfflineSeconds = 600 });
        shortOutlook.Compacted.Should().BeFalse();
    }

    /// <summary>
    /// Skill experience is granted per raw item and a compactor does not change it: 160 cobblestone at
    /// 0.1 each and the one enchanted cobblestone they become at 16 are the same 16 experience.
    /// </summary>
    [Test]
    public void ExperienceSurvivesCompaction()
    {
        var compaction = new Dictionary<string, MinionCompaction> { ["COBBLESTONE"] = new("ENCHANTED_COBBLESTONE", 160) };
        var experience = new Dictionary<string, double> { ["COBBLESTONE"] = 0.1 };
        var calculator = new MinionCalculator(
            Prices(("COBBLESTONE", 3, 2), ("ENCHANTED_COBBLESTONE", 480, 320)), experience, compaction);
        var query = new MinionQuery { OfflineSeconds = 600 };

        var raw = calculator.Evaluate(Cobblestone(), 1, query with { AllowCompaction = false });
        var compacted = calculator.Evaluate(Cobblestone(), 1, query);

        compacted.ExperiencePerDay.Should().BeApproximately(raw.ExperiencePerDay, 0.0001);
    }

    /// <summary>
    /// Waiting for a sell offer to fill and dumping into the highest buy order are different trades and
    /// the gap between them is wide enough to flip a verdict, so the mode has to reach the arithmetic.
    /// </summary>
    [Test]
    public void SellOfferAndInstaSellArePricedDifferently()
    {
        var calculator = new MinionCalculator(Prices(("COBBLESTONE", 6, 1)));
        var query = new MinionQuery { OfflineSeconds = 600, AllowCompaction = false };

        var offer = calculator.Evaluate(Cobblestone(), 1, query with { Sell = MinionSellMode.SellOffer });
        var instant = calculator.Evaluate(Cobblestone(), 1, query with { Sell = MinionSellMode.InstaSell });
        var npc = calculator.Evaluate(Cobblestone(), 1, query with { Sell = MinionSellMode.Npc });

        offer.CoinsPerDay.Should().BeApproximately(instant.CoinsPerDay * 6, 0.0001);
        npc.CoinsPerDay.Should().BeApproximately(instant.CoinsPerDay, 0.0001);
    }

    /// <summary>Buying the materials with an order rather than instantly is cheaper, and setup cost says so.</summary>
    [Test]
    public void BuyOrdersCostLessThanInstantBuys()
    {
        var calculator = new MinionCalculator(Prices(("COBBLESTONE", 6, 1)));

        var (instantCost, _, _) = calculator.SetupCost(Cobblestone(), 1, MinionBuyMode.InstaBuy);
        var (orderCost, _, _) = calculator.SetupCost(Cobblestone(), 1, MinionBuyMode.BuyOrder);

        instantCost.Should().Be(80 * 6);
        orderCost.Should().Be(80 * 1);
    }

    /// <summary>
    /// Pelts, north stars and a dark-auction-only minion have no bazaar price. Skipping them silently
    /// is what makes an unreachable tier look free, so they come back named.
    /// </summary>
    [Test]
    public void AnIngredientOutsideTheCoinEconomyIsReportedNotIgnored()
    {
        var melon = new MinionService.Minion(
            "Melon Minion", new List<double> { 15 }, new List<int> { 64 }, "Farming",
            new[]
            {
                new List<MinionService.Cost>
                {
                    Cost("Enchanted Melon Block", 32, "ENCHANTED_MELON_BLOCK"),
                    Cost("Pelts", 75)
                }
            },
            new[] { Product("MELON", 4) });
        var calculator = new MinionCalculator(Prices(("ENCHANTED_MELON_BLOCK", 100, 90), ("MELON", 2, 1)));

        var (cost, missing, _) = calculator.SetupCost(melon, 1, MinionBuyMode.InstaBuy);

        cost.Should().Be(32 * 100);
        missing.Should().ContainSingle().Which.Should().Be("75x Pelts");
    }

    /// <summary>
    /// Three minions reached tier XII before the recipe table did. Summing only the steps that exist
    /// prices the missing ones at zero, which reads as a cheap top tier rather than an unknown one.
    /// </summary>
    [Test]
    public void ATierWithNoRecipeIsReportedRatherThanCostedAtNothing()
    {
        var minion = new MinionService.Minion(
            "Clay Minion", new List<double> { 16, 14 }, new List<int> { 960, 960 }, "Mining",
            new[] { new List<MinionService.Cost> { Cost("Clay", 80, "CLAY_BALL") } }, // only tier I is known
            new[] { Product("CLAY_BALL", 4) });
        var calculator = new MinionCalculator(Prices(("CLAY_BALL", 5, 4)));

        var (cost, missing, _) = calculator.SetupCost(minion, 2, MinionBuyMode.InstaBuy);

        cost.Should().Be(80 * 5);
        missing.Should().ContainSingle().Which.Should().Be("recipe for Clay Minion tier 2");
    }

    /// <summary>Several tier XII recipes ask for a flat coin sum; that is coins, not an unpriceable item.</summary>
    [Test]
    public void ACoinIngredientCountsAsCoins()
    {
        var minion = new MinionService.Minion(
            "Cobblestone Minion", new List<double> { 14 }, new List<int> { 64 }, "Mining",
            new[] { new List<MinionService.Cost> { Cost("Coins", 2_000_000) } },
            new[] { Product("COBBLESTONE", 1) });
        var calculator = new MinionCalculator(Prices(("COBBLESTONE", 3, 2)));

        var (cost, missing, _) = calculator.SetupCost(minion, 1, MinionBuyMode.InstaBuy);

        cost.Should().Be(2_000_000);
        missing.Should().BeEmpty();
    }

    /// <summary>The budget picks the tier: the best minion for someone with 50k is not the best overall.</summary>
    [Test]
    public void TheBudgetChoosesTheTier()
    {
        var calculator = new MinionCalculator(Prices(("COBBLESTONE", 10, 5)));
        var minion = Cobblestone(tiers: 4); // each tier costs another 80 cobblestone at 10 = 800 coins

        calculator.Best(minion, new MinionQuery { Budget = 10_000 }).Tier.Should().Be(4);
        calculator.Best(minion, new MinionQuery { Budget = 2_500 }).Tier.Should().Be(3);
        calculator.Best(minion, new MinionQuery { Budget = 800 }).Tier.Should().Be(1);
        calculator.Best(minion, new MinionQuery { Budget = 10 }).Should().BeNull();
    }

    /// <summary>A tier that cannot be bought with coins is skipped when picking the best affordable tier.</summary>
    [Test]
    public void ATierGatedBehindPeltsIsNotTheBestAffordableTier()
    {
        var minion = new MinionService.Minion(
            "Melon Minion", new List<double> { 15, 14 }, new List<int> { 64, 192 }, "Farming",
            new[]
            {
                new List<MinionService.Cost> { Cost("Melon", 80, "MELON") },
                new List<MinionService.Cost> { Cost("Pelts", 75) }
            },
            new[] { Product("MELON", 4) });
        var calculator = new MinionCalculator(Prices(("MELON", 2, 1)));

        calculator.Best(minion, new MinionQuery { Budget = double.PositiveInfinity }).Tier.Should().Be(1);
        // Asking for it explicitly still works, and still says what is missing.
        calculator.Evaluate(minion, 2, new MinionQuery()).MissingRequirements.Should().Contain("75x Pelts");
    }

    /// <summary>
    /// Nearly every minion's tier I recipe includes a vanilla tool, which no bazaar lists. Treating
    /// that like a pelt requirement dropped 54 of 59 minions out of the ranking entirely, so an
    /// ingredient without a price makes the cost a floor rather than disqualifying the minion.
    /// </summary>
    [Test]
    public void AnIngredientWithNoListingLowersConfidenceRatherThanHidingTheMinion()
    {
        var minion = new MinionService.Minion(
            "Cobblestone Minion", new List<double> { 14 }, new List<int> { 64 }, "Mining",
            new[]
            {
                new List<MinionService.Cost>
                {
                    Cost("Wooden Pickaxe", 1, "WOOD_PICKAXE"),
                    Cost("Cobblestone", 80, "COBBLESTONE")
                }
            },
            new[] { Product("COBBLESTONE", 1) });
        var calculator = new MinionCalculator(Prices(("COBBLESTONE", 3, 2)));

        var best = calculator.Best(minion, new MinionQuery { Budget = 1_000_000 });

        best.Should().NotBeNull("a minion is not unreachable just because a wooden pickaxe has no bazaar price");
        best.Reachable.Should().BeTrue();
        best.FullyPriced.Should().BeFalse();
        best.UnpricedIngredients.Should().ContainSingle().Which.Should().Be("1x Wooden Pickaxe");
        best.SetupCost.Should().Be(80 * 3, "the tool is a floor on the cost, not free");
    }

    /// <summary>Derpy's TURBO MINIONS doubles output; that is the whole reason to plan around his term.</summary>
    [Test]
    public void DerpyDoublesOutput()
    {
        var calculator = new MinionCalculator(Prices(("COBBLESTONE", 3, 2)));
        var query = new MinionQuery { OfflineSeconds = 600, AllowCompaction = false };

        var normal = calculator.Evaluate(Cobblestone(), 1, query);
        var derpy = calculator.Evaluate(Cobblestone(), 1, query with { OutputMultiplier = 2 });

        derpy.CoinsPerDay.Should().BeApproximately(normal.CoinsPerDay * 2, 0.0001);
        // Doubled output also halves the time to fill, which is why Derpy needs more storage, not less.
        derpy.SecondsToFill.Should().BeApproximately(normal.SecondsToFill / 2, 0.0001);
    }

    /// <summary>Derpy's MOAR SKILLZ raises skill experience by half without touching coins.</summary>
    [Test]
    public void DerpyRaisesExperienceSeparatelyFromCoins()
    {
        var experience = new Dictionary<string, double> { ["COBBLESTONE"] = 0.1 };
        var calculator = new MinionCalculator(Prices(("COBBLESTONE", 3, 2)), experience);
        var query = new MinionQuery { OfflineSeconds = 600, AllowCompaction = false };

        var normal = calculator.Evaluate(Cobblestone(), 1, query);
        var derpy = calculator.Evaluate(Cobblestone(), 1, query with { ExperienceMultiplier = 1.5 });

        derpy.ExperiencePerDay.Should().BeApproximately(normal.ExperiencePerDay * 1.5, 0.0001);
        derpy.CoinsPerDay.Should().BeApproximately(normal.CoinsPerDay, 0.0001);
    }

    /// <summary>
    /// A hopper keeps a full minion earning instead of stalling, at a fraction of npc value -- the
    /// difference between a minion that pays for years and one that stops on the first day.
    /// </summary>
    [Test]
    public void AHopperKeepsAFullMinionEarning()
    {
        var calculator = new MinionCalculator(Prices(("COBBLESTONE", 3, 2)));
        var week = new MinionQuery { OfflineSeconds = 7 * MinionCalculator.SecondsPerDay, AllowCompaction = false };

        var stalled = calculator.Evaluate(Cobblestone(), 1, week);
        var hoppered = calculator.Evaluate(Cobblestone(), 1, week with { HopperNpcShare = 0.7 });

        hoppered.CoinsPerDay.Should().BeGreaterThan(stalled.CoinsPerDay);
    }

    /// <summary>Fuel that has to be rebought is a running cost and comes off the daily figure.</summary>
    [Test]
    public void RecurringFuelCostIsSubtractedFromTheDailyProfit()
    {
        var calculator = new MinionCalculator(Prices(("COBBLESTONE", 3, 2)));
        var query = new MinionQuery { OfflineSeconds = 600, AllowCompaction = false };

        var free = calculator.Evaluate(Cobblestone(), 1, query);
        var fuelled = calculator.Evaluate(Cobblestone(), 1, query with { FuelCostPerDay = 1000 });

        fuelled.CoinsPerDay.Should().BeApproximately(free.CoinsPerDay - 1000, 0.0001);
    }

    /// <summary>A grid slot is TAG:QUANTITY, with a data-value colon written as a hyphen.</summary>
    [Test]
    public void AGridSlotSeparatesTheTagFromTheQuantity()
    {
        var totals = MinionCalculator.ParseGrid(new[] { "INK_SACK-4:32", "", "INK_SACK-4:32", null, "COBBLESTONE:1" });

        totals["INK_SACK:4"].Should().Be(64);
        totals["COBBLESTONE"].Should().Be(1);
        totals.Should().HaveCount(2);
    }

    /// <summary>
    /// An Enchanted Blaze Rod is 160 Enchanted Blaze Powder, not 160 blaze rods. Assuming the flat 160
    /// priced a blaze minion at roughly a hundred times its real output, so the ratio is followed
    /// through the chain instead of guessed at.
    /// </summary>
    [Test]
    public void CompactionRatiosFollowTheRecipeChain()
    {
        var recipes = new Dictionary<string, Dictionary<string, double>>
        {
            ["ENCHANTED_BLAZE_POWDER"] = new() { ["BLAZE_POWDER"] = 160 },
            ["ENCHANTED_BLAZE_ROD"] = new() { ["ENCHANTED_BLAZE_POWDER"] = 160 },
            ["ENCHANTED_COBBLESTONE"] = new() { ["COBBLESTONE"] = 160 },
            // A two-ingredient recipe is a craft, not a compaction step, and must not be followed.
            ["ENCHANTED_LAVA_BUCKET"] = new() { ["ENCHANTED_IRON"] = 3, ["ENCHANTED_COAL_BLOCK"] = 2 }
        };

        var steps = MinionCalculator.ResolveCompaction(recipes, new[] { "BLAZE_POWDER", "COBBLESTONE", "ENCHANTED_IRON" });

        steps["BLAZE_POWDER"].Should().Be(new MinionCompaction("ENCHANTED_BLAZE_ROD", 160 * 160));
        steps["COBBLESTONE"].Should().Be(new MinionCompaction("ENCHANTED_COBBLESTONE", 160));
        steps.Should().NotContainKey("ENCHANTED_IRON", "a multi-ingredient craft is not compaction");
    }

    /// <summary>A Super Compactor does two steps, so following more than that would overstate storage.</summary>
    [Test]
    public void CompactionStopsAtTheRequestedDepth()
    {
        var recipes = new Dictionary<string, Dictionary<string, double>>
        {
            ["TIER_ONE"] = new() { ["RAW"] = 160 },
            ["TIER_TWO"] = new() { ["TIER_ONE"] = 160 },
            ["TIER_THREE"] = new() { ["TIER_TWO"] = 160 }
        };

        MinionCalculator.ResolveCompaction(recipes, new[] { "RAW" }, maxDepth: 1)["RAW"]
            .Should().Be(new MinionCompaction("TIER_ONE", 160));
        MinionCalculator.ResolveCompaction(recipes, new[] { "RAW" }, maxDepth: 2)["RAW"]
            .Should().Be(new MinionCompaction("TIER_TWO", 160 * 160));
    }

    /// <summary>Ranking follows the objective, so the coin answer and the experience answer can differ.</summary>
    [Test]
    public void RankingFollowsTheObjective()
    {
        var coinMinion = new MinionService.Minion(
            "Coin Minion", new List<double> { 10 }, new List<int> { 640 }, "Mining",
            new[] { new List<MinionService.Cost>() }, new[] { Product("GOLD", 1) });
        var experienceMinion = new MinionService.Minion(
            "Experience Minion", new List<double> { 10 }, new List<int> { 640 }, "Mining",
            new[] { new List<MinionService.Cost>() }, new[] { Product("STONE", 1) });
        var calculator = new MinionCalculator(
            Prices(("GOLD", 100, 90), ("STONE", 1, 1)),
            new Dictionary<string, double> { ["GOLD"] = 0.1, ["STONE"] = 50 });
        var minions = new[] { coinMinion, experienceMinion };

        calculator.Rank(minions, new MinionQuery { OfflineSeconds = 600 }).First().Name.Should().Be("Coin Minion");
        calculator.Rank(minions, new MinionQuery { OfflineSeconds = 600, Objective = MinionObjective.Experience })
            .First().Name.Should().Be("Experience Minion");
    }
}
