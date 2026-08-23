using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>
/// Guards the shipped minion table against the ways it has actually gone wrong: silently trailing the
/// live game, losing a recipe step, and carrying scraped footnotes as if they were ingredients.
/// </summary>
public class MinionDataTests
{
    /// <summary>The minion families Hypixel served on 2026-08-23, by display name.</summary>
    private static readonly string[] LiveMinions =
    {
        "Acacia", "Birch", "Blaze", "Cactus", "Carrot", "Cave Spider", "Chicken", "Clay", "Coal",
        "Cobblestone", "Cocoa Beans", "Cow", "Creeper", "Dark Oak", "Diamond", "Emerald", "End Stone",
        "Enderman", "Fishing", "Flower", "Ghast", "Glowstone", "Gold", "Gravel", "Hard Stone", "Ice",
        "Inferno", "Iron", "Jungle", "Lapis", "Lily Pad", "Magma Cube", "Melon", "Mithril", "Mushroom",
        "Mycelium", "Nether Wart", "Oak", "Obsidian", "Pig", "Potato", "Pumpkin", "Quartz", "Rabbit",
        "Red Sand", "Redstone", "Revenant", "Sand", "Sheep", "Skeleton", "Slime", "Snow", "Spider",
        "Spruce", "Sugar Cane", "Sunflower", "Tarantula", "Vampire", "Voidling", "Wheat", "Zombie"
    };

    /// <summary>
    /// Minions the game has and the table does not. Lily Pad arrived after the table was last built
    /// and has no per-tier figures published anywhere reachable -- no wiki page, no lore on the item,
    /// nothing in the resource api -- so it is recorded rather than guessed at. Shrink this list when
    /// the numbers become available; never grow it without saying why.
    /// </summary>
    private static readonly string[] KnownMissing = { "Lily Pad" };

    /// <summary>
    /// Minions whose recipe table stops short of the tiers the game offers. Costing them off the steps
    /// that exist would price the rest at nothing, so <see cref="MinionCalculator"/> reports the gap.
    /// </summary>
    private static readonly string[] KnownShortRecipes = { "Tarantula Minion", "Clay Minion", "Fishing Minion" };

    /// <summary>Names an upgrade step may carry that are deliberately not market items.</summary>
    private static readonly string[] NonMarketRequirements = { "Coins", "Pelts", "North Stars", "Dark Auction purchase" };

    private static readonly string[] Categories = { "Farming", "Mining", "Combat", "Slayer", "Foraging", "Fishing" };

    private static MinionService Service() => new();

    [Test]
    public void TheTableCoversEveryLiveMinionExceptTheRecordedGaps()
    {
        var known = Service().MinionData.Keys.ToHashSet();

        var absent = LiveMinions.Where(m => !known.Contains($"{m} Minion")).ToArray();

        absent.Should().BeEquivalentTo(KnownMissing,
            "a minion the game has and the table does not is invisible to every ranking");
    }

    [Test]
    public void NoMinionInTheTableHasLeftTheGame()
    {
        var live = LiveMinions.Select(m => $"{m} Minion").ToHashSet();

        Service().MinionData.Keys.Where(name => !live.Contains(name)).Should().BeEmpty();
    }

    [Test]
    public void EveryMinionCarriesACategory()
    {
        foreach (var minion in Service().MinionData.Values)
            Categories.Should().Contain(minion.Type, $"{minion.Name} needs a category for pet and upgrade rules");
    }

    [Test]
    public void SpeedAndStorageAreGivenForTheSameTiers()
    {
        foreach (var minion in Service().MinionData.Values)
            minion.Storage.Count.Should().Be(minion.TierDelay.Count, $"{minion.Name} tiers must line up");
    }

    /// <summary>
    /// An upgrade step per tier, with step zero being the tier I craft. Vampire once carried an empty
    /// step zero, which shifted every tier onto the price of the one below it.
    /// </summary>
    [Test]
    public void RecipeStepsLineUpWithTiers()
    {
        var short_ = new List<string>();
        foreach (var minion in Service().MinionData.Values)
        {
            minion.Upgrade.Count.Should().BeLessThanOrEqualTo(minion.TierDelay.Count,
                $"{minion.Name} has more recipe steps than tiers");
            minion.Upgrade.FirstOrDefault().Should().NotBeEmpty($"{minion.Name} tier I needs a recipe");
            if (minion.Upgrade.Count < minion.TierDelay.Count)
                short_.Add(minion.Name);
        }
        short_.Should().BeEquivalentTo(KnownShortRecipes);
    }

    [Test]
    public void EveryProductResolvesToAnItemTag()
    {
        foreach (var minion in Service().MinionData.Values)
            foreach (var product in minion.Products)
                product.Tag.Should().NotBeNullOrEmpty($"{minion.Name} produces {product.ItemName}");
    }

    /// <summary>
    /// An ingredient with no tag is worth nothing to the cost sum, so the only untagged names allowed
    /// are the ones that genuinely sit outside the coin economy and are reported to the caller.
    /// </summary>
    [Test]
    public void UntaggedIngredientsAreOnlyTheKnownNonMarketOnes()
    {
        var untagged = Service().MinionData.Values
            .SelectMany(m => m.Upgrade)
            .SelectMany(step => step)
            .Where(cost => string.IsNullOrEmpty(cost.Tag))
            .Select(cost => cost.Name)
            .Distinct()
            .ToArray();

        untagged.Should().BeSubsetOf(NonMarketRequirements,
            "anything else is a scraped footnote being counted as a free ingredient");
    }

    /// <summary>Storage never shrinks as a minion is upgraded, and speed never gets worse.</summary>
    [Test]
    public void HigherTiersAreNeverWorse()
    {
        foreach (var minion in Service().MinionData.Values)
        {
            minion.Storage.Should().BeInAscendingOrder($"{minion.Name} storage");
            minion.TierDelay.Should().BeInDescendingOrder($"{minion.Name} time between actions");
        }
    }
}
