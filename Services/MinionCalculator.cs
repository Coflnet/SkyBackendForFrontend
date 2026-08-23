using System;
using System.Collections.Generic;
using System.Linq;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>How produced items are turned into coins.</summary>
public enum MinionSellMode
{
    /// <summary>Place a sell offer and wait for it to fill, receiving the bazaar buy price.</summary>
    SellOffer,
    /// <summary>Sell into the highest standing buy order right away, receiving the bazaar sell price.</summary>
    InstaSell,
    /// <summary>Sell to an npc merchant without touching the bazaar.</summary>
    Npc
}

/// <summary>How crafting and upgrade materials are paid for.</summary>
public enum MinionBuyMode
{
    /// <summary>Buy from the lowest standing sell offer right away, paying the bazaar buy price.</summary>
    InstaBuy,
    /// <summary>Place a buy order and wait for it to fill, paying the bazaar sell price.</summary>
    BuyOrder
}

/// <summary>What the ranking should maximise.</summary>
public enum MinionObjective
{
    Coins,
    Experience
}

/// <summary>
/// Both sides of an item's market. The bazaar's buy price is what a buyer hands over, so it is at
/// once the cost of an instant buy and the proceeds of a sell offer once it fills; the sell price is
/// the mirror. Collapsing the two into one number is what makes a craft look unprofitable when it is
/// not, so every caller has to supply both.
/// </summary>
public record MinionItemPrice(double InstaBuy, double InstaSell, double NpcSell = 0);

/// <summary>One compactor step: <see cref="Amount"/> of the raw item collapse into one <see cref="Tag"/>.</summary>
public record MinionCompaction(string Tag, int Amount);

/// <summary>The player-supplied situation a minion is being ranked for.</summary>
public record MinionQuery
{
    /// <summary>How long the minion is left alone between collections. This is the input that reorders
    /// the ranking: past the point where storage fills, extra speed buys nothing.</summary>
    public double OfflineSeconds { get; init; } = 24 * 3600;
    /// <summary>Coins available to craft and upgrade with.</summary>
    public double Budget { get; init; } = double.PositiveInfinity;
    public MinionSellMode Sell { get; init; } = MinionSellMode.SellOffer;
    public MinionBuyMode Buy { get; init; } = MinionBuyMode.InstaBuy;
    public MinionObjective Objective { get; init; } = MinionObjective.Coins;
    /// <summary>Additive speed from fuel, upgrade slots, beacon, crystal and pet. 0.4 means +40%.</summary>
    public double SpeedBoost { get; init; }
    /// <summary>Derpy's TURBO MINIONS doubles what every minion produces.</summary>
    public double OutputMultiplier { get; init; } = 1;
    /// <summary>Derpy's MOAR SKILLZ raises skill experience by half.</summary>
    public double ExperienceMultiplier { get; init; } = 1;
    /// <summary>Recurring coin cost of the fuel per day. Infinite fuels cost nothing to keep burning.</summary>
    public double FuelCostPerDay { get; init; }
    /// <summary>Flat coin cost of whatever sits in the two upgrade slots.</summary>
    public double UpgradeCost { get; init; }
    /// <summary>Share of npc value an automated shipping item pays for overflow once storage is full;
    /// 0.5 for a Budget Hopper, 0.7 for an Enchanted Hopper, null for no hopper.</summary>
    public double? HopperNpcShare { get; init; }
    /// <summary>Pin the tier instead of taking the best one the budget reaches.</summary>
    public int? Tier { get; init; }
    /// <summary>Whether a Super Compactor 3000 may be assumed. Compacting multiplies effective storage
    /// but frequently sells for less than the raw items it replaces, so it is a choice, not a default.</summary>
    public bool AllowCompaction { get; init; } = true;
}

/// <summary>What one minion at one tier does under a <see cref="MinionQuery"/>.</summary>
public record MinionOutlook(
    string Name,
    int Tier,
    double SecondsBetweenActions,
    double SecondsBetweenHarvests,
    double CoinsPerDay,
    double ExperiencePerDay,
    double SetupCost,
    bool Compacted,
    bool StorageLimited,
    double SecondsToFill,
    IReadOnlyList<string> MissingRequirements,
    IReadOnlyList<string> UnpricedIngredients,
    IReadOnlyList<string> ProductTags)
{
    /// <summary>Days of production needed to earn the setup cost back, or infinity when it never does.</summary>
    public double PaybackDays => CoinsPerDay > 0 ? SetupCost / CoinsPerDay : double.PositiveInfinity;

    /// <summary>True when nothing in the recipe sits outside the coin economy.</summary>
    public bool Reachable => MissingRequirements.Count == 0;

    /// <summary>True when every ingredient had a price, so the setup cost is complete rather than a floor.</summary>
    public bool FullyPriced => MissingRequirements.Count == 0 && UnpricedIngredients.Count == 0;
}

/// <summary>
/// Ranks minions for a concrete situation rather than in the abstract.
///
/// The two facts that drive every number here and that a per-day rate alone cannot express:
/// a minion only yields on every second action, and it stops dead once its storage is full.
/// </summary>
public class MinionCalculator
{
    public const double SecondsPerDay = 24 * 60 * 60;

    /// <summary>
    /// A minion acts on its listed interval but only produces on every second action -- a tier I
    /// cobblestone minion acts every 14s and yields one cobblestone every 28s. Rates computed off the
    /// raw interval come out twice the real figure.
    /// </summary>
    public const int ActionsPerHarvest = 2;

    /// <summary>Ingredient name the data uses for a plain coin cost rather than an item.</summary>
    public const string CoinIngredient = "Coins";

    /// <summary>
    /// Requirements that no amount of coins satisfies. A tier behind one of these is not something a
    /// budget can reach, which is different from an ingredient that merely has no bazaar listing.
    /// </summary>
    public static readonly IReadOnlySet<string> NonMarketRequirements =
        new HashSet<string> { "Pelts", "North Stars", "Dark Auction purchase" };

    private readonly IReadOnlyDictionary<string, MinionItemPrice> prices;
    private readonly IReadOnlyDictionary<string, double> experiencePerItem;
    private readonly IReadOnlyDictionary<string, MinionCompaction> compaction;

    public MinionCalculator(
        IReadOnlyDictionary<string, MinionItemPrice> prices,
        IReadOnlyDictionary<string, double> experiencePerItem = null,
        IReadOnlyDictionary<string, MinionCompaction> compaction = null)
    {
        this.prices = prices ?? throw new ArgumentNullException(nameof(prices));
        this.experiencePerItem = experiencePerItem ?? new Dictionary<string, double>();
        this.compaction = compaction ?? new Dictionary<string, MinionCompaction>();
    }

    /// <summary>
    /// Parses one crafting grid into ingredient totals. Slots read <c>TAG:QUANTITY</c>, and a tag that
    /// carries a data value has its colon written as a hyphen, so <c>INK_SACK-4:32</c> is 32 lapis.
    /// </summary>
    public static Dictionary<string, double> ParseGrid(IEnumerable<string> slots)
    {
        var totals = new Dictionary<string, double>();
        foreach (var slot in slots)
        {
            if (string.IsNullOrWhiteSpace(slot))
                continue;
            var split = slot.LastIndexOf(':');
            if (split < 0 || !double.TryParse(slot[(split + 1)..], System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var quantity))
                continue;
            var tag = slot[..split].Replace('-', ':');
            totals[tag] = totals.GetValueOrDefault(tag) + quantity;
        }
        return totals;
    }

    /// <summary>
    /// Works out how many of a raw item each compacted form is worth, by following the recipe rather
    /// than assuming. The assumption that <c>ENCHANTED_X</c> is always 160 <c>X</c> is wrong often
    /// enough to matter: an Enchanted Blaze Rod is 160 Enchanted Blaze Powder, so it is worth 25,600
    /// blaze rods, and pricing it as 160 overstates a blaze minion by two orders of magnitude.
    /// </summary>
    /// <param name="recipes">Ingredient totals per output tag, as produced by <see cref="ParseGrid"/></param>
    /// <param name="rawTags">The items minions actually produce</param>
    /// <param name="maxDepth">How many compaction steps to follow; a Super Compactor does two</param>
    public static Dictionary<string, MinionCompaction> ResolveCompaction(
        IReadOnlyDictionary<string, Dictionary<string, double>> recipes,
        IEnumerable<string> rawTags,
        int maxDepth = 2)
    {
        var steps = new Dictionary<string, MinionCompaction>();
        foreach (var raw in rawTags.Distinct())
        {
            string current = raw;
            double ratio = 1;
            for (var depth = 0; depth < maxDepth; depth++)
            {
                // The compacted form of the item we are holding is whatever is made purely from it.
                var next = recipes.FirstOrDefault(entry =>
                    entry.Value.Count == 1
                    && entry.Value.ContainsKey(current)
                    && entry.Value[current] > 1
                    && entry.Key != current);
                if (next.Key == null)
                    break;
                ratio *= next.Value[current];
                current = next.Key;
                steps[raw] = new MinionCompaction(current, (int)Math.Round(ratio));
            }
        }
        return steps;
    }

    /// <summary>Ranks every supplied minion, best first, at the best tier each query allows.</summary>
    public List<MinionOutlook> Rank(IEnumerable<MinionService.Minion> minions, MinionQuery query)
    {
        var ranked = minions
            .Select(minion => Best(minion, query))
            .Where(outlook => outlook != null)
            .ToList();
        return query.Objective == MinionObjective.Experience
            ? ranked.OrderByDescending(o => o.ExperiencePerDay).ToList()
            : ranked.OrderByDescending(o => o.CoinsPerDay).ToList();
    }

    /// <summary>
    /// The best outlook for one minion: the highest tier the budget reaches, and for that tier
    /// whichever of compacted or raw output is worth more.
    /// </summary>
    public MinionOutlook Best(MinionService.Minion minion, MinionQuery query)
    {
        var tiers = query.Tier.HasValue
            ? new[] { query.Tier.Value }
            : Enumerable.Range(1, minion.TierDelay.Count).Reverse().ToArray();
        foreach (var tier in tiers)
        {
            if (tier < 1 || tier > minion.TierDelay.Count)
                continue;
            var outlook = Evaluate(minion, tier, query);
            if (outlook == null)
                continue;
            // A tier whose recipe demands pelts or north stars cannot be reached with coins at all, so
            // it is not what "the best tier this budget buys" means -- fall through to the one below it.
            if (query.Tier.HasValue)
                return outlook;
            // An ingredient with no bazaar listing -- a wooden pickaxe from an npc shop, say -- makes the
            // cost a floor rather than a blocker. Skipping the minion over one would hide almost all of them.
            if (outlook.Reachable && outlook.SetupCost + query.UpgradeCost <= query.Budget)
                return outlook;
        }
        return null;
    }

    /// <summary>Works out one minion at one tier, choosing compacted or raw by what pays more.</summary>
    public MinionOutlook Evaluate(MinionService.Minion minion, int tier, MinionQuery query)
    {
        if (tier < 1 || tier > minion.TierDelay.Count || tier > minion.Storage.Count)
            return null;
        var raw = Evaluate(minion, tier, query, compact: false);
        if (!query.AllowCompaction)
            return raw;
        var compacted = Evaluate(minion, tier, query, compact: true);
        if (compacted == null)
            return raw;
        if (raw == null)
            return compacted;
        var better = query.Objective == MinionObjective.Experience
            ? compacted.ExperiencePerDay > raw.ExperiencePerDay
            : compacted.CoinsPerDay > raw.CoinsPerDay;
        return better ? compacted : raw;
    }

    private MinionOutlook Evaluate(MinionService.Minion minion, int tier, MinionQuery query, bool compact)
    {
        var interval = minion.TierDelay[tier - 1] / (1 + query.SpeedBoost);
        if (interval <= 0)
            return null;
        var harvestSeconds = interval * ActionsPerHarvest;
        var capacity = minion.Storage[tier - 1];

        // Storage counts the items as they are stored, so a compactor stretches it by the compaction
        // ratio. That is the whole reason a compacted minion can be left alone for days.
        double storedPerHarvest = 0;
        foreach (var product in minion.Products)
        {
            if (product.Tag == null)
                continue;
            var perHarvest = product.PerTime * query.OutputMultiplier;
            storedPerHarvest += compact && compaction.TryGetValue(product.Tag, out var step)
                ? perHarvest / step.Amount
                : perHarvest;
        }
        if (storedPerHarvest <= 0)
            return null;

        var secondsToFill = capacity / storedPerHarvest * harvestSeconds;
        var collectedSeconds = Math.Min(query.OfflineSeconds, secondsToFill);
        // A hopper keeps a full minion working by shipping the overflow off at a fraction of npc value.
        var overflowSeconds = query.HopperNpcShare.HasValue
            ? Math.Max(0, query.OfflineSeconds - secondsToFill)
            : 0;

        double coinsPerCycle = 0;
        double experiencePerCycle = 0;
        foreach (var product in minion.Products)
        {
            if (product.Tag == null)
                continue;
            var perSecond = product.PerTime * query.OutputMultiplier / harvestSeconds;
            var collected = perSecond * collectedSeconds;

            var soldTag = product.Tag;
            var soldCount = collected;
            if (compact && compaction.TryGetValue(product.Tag, out var step))
            {
                soldTag = step.Tag;
                soldCount = collected / step.Amount;
            }
            coinsPerCycle += soldCount * UnitProceeds(soldTag, product, query.Sell);

            // Experience is granted per raw item pulled out of storage and a compactor does not change
            // it: 160 cobblestone at 0.1 each and one enchanted cobblestone at 16 are the same 16.
            if (experiencePerItem.TryGetValue(product.Tag, out var perItem))
                experiencePerCycle += collected * perItem * query.ExperienceMultiplier;

            if (overflowSeconds > 0)
                coinsPerCycle += perSecond * overflowSeconds * product.NpcPrice * query.HopperNpcShare.Value;
        }

        var cycles = SecondsPerDay / query.OfflineSeconds;
        var coinsPerDay = coinsPerCycle * cycles - query.FuelCostPerDay;
        var experiencePerDay = experiencePerCycle * cycles;

        var (setupCost, missing, unpriced) = SetupCost(minion, tier, query.Buy);
        return new MinionOutlook(
            minion.Name,
            tier,
            interval,
            harvestSeconds,
            coinsPerDay,
            experiencePerDay,
            setupCost + query.UpgradeCost,
            compact,
            secondsToFill < query.OfflineSeconds,
            secondsToFill,
            missing,
            unpriced,
            minion.Products.Where(p => p.Tag != null).Select(p => p.Tag).ToList());
    }

    private double UnitProceeds(string tag, MinionService.Product product, MinionSellMode mode)
    {
        if (mode == MinionSellMode.Npc)
            return product.NpcPrice;
        if (!prices.TryGetValue(tag, out var price))
            return product.NpcPrice;
        var bazaar = mode == MinionSellMode.SellOffer ? price.InstaBuy : price.InstaSell;
        // An empty side of the order book is not a price of zero. Plenty of minion output has sell
        // offers standing but no bids at all, so there is nothing to dump into -- selling to a merchant
        // is what a player actually does then, and it is the honest floor for the figure.
        return bazaar > 0 ? bazaar : product.NpcPrice;
    }

    /// <summary>
    /// Coins needed to reach <paramref name="tier"/> from nothing, plus anything the coin economy
    /// cannot buy. Pelts, north stars and a dark-auction-only minion are reported rather than counted
    /// as free, which is what silently skipping an unpriced ingredient amounts to.
    /// </summary>
    public (double Cost, IReadOnlyList<string> Missing, IReadOnlyList<string> Unpriced) SetupCost(
        MinionService.Minion minion, int tier, MinionBuyMode mode)
    {
        double cost = 0;
        var missing = new List<string>();
        var unpriced = new List<string>();
        // Several minions reached a tier the recipe table has not caught up with. Costing them off the
        // steps that do exist quietly prices the missing upgrades at nothing, so say so instead.
        if (minion.Upgrade.Count < tier)
            missing.Add($"recipe for {minion.Name} tier {tier}");
        for (var step = 0; step < tier && step < minion.Upgrade.Count; step++)
        {
            foreach (var ingredient in minion.Upgrade[step])
            {
                if (ingredient == null)
                    continue;
                if (ingredient.Name == CoinIngredient)
                {
                    cost += ingredient.Quanity;
                    continue;
                }
                if (ingredient.Tag != null && prices.TryGetValue(ingredient.Tag, out var price))
                {
                    var side = mode == MinionBuyMode.InstaBuy ? price.InstaBuy : price.InstaSell;
                    // Waiting for a buy order to fill is only an option when there is a book to fill
                    // against; with nothing standing, buying outright is the only way to get the item.
                    if (side <= 0)
                        side = mode == MinionBuyMode.InstaBuy ? price.InstaSell : price.InstaBuy;
                    if (side > 0)
                    {
                        cost += ingredient.Quanity * side;
                        continue;
                    }
                }
                var requirement = $"{ingredient.Quanity:0.##}x {ingredient.Name}";
                if (NonMarketRequirements.Contains(ingredient.Name))
                    missing.Add(requirement);
                else
                    unpriced.Add(requirement);
            }
        }
        return (cost, missing, unpriced);
    }
}
