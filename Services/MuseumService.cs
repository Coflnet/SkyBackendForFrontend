using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Sniper.Client.Api;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Core.Services;
using Coflnet.Sky.Core;
using Microsoft.EntityFrameworkCore;
using System;
using Coflnet.Sky.Crafts.Client.Api;
using Newtonsoft.Json;
using System.Diagnostics;
using Coflnet.Sky.Crafts.Client.Model;

namespace Coflnet.Sky.Commands.Shared;

public class MuseumService
{
    private IAuctionApi sniperApi;
    private ILogger<MuseumService> logger;
    private IHypixelItemStore hypixelItemService;
    private IProfileClient profileClient;
    private ICraftsApi craftsApi;

    public MuseumService(IAuctionApi sniperApi, ILogger<MuseumService> logger, IHypixelItemStore hypixelItemService, IProfileClient profileClient, ICraftsApi craftsApi)
    {
        this.sniperApi = sniperApi;
        this.logger = logger;
        this.hypixelItemService = hypixelItemService;
        this.profileClient = profileClient;
        this.craftsApi = craftsApi;
    }

    public async Task<IEnumerable<Cheapest>> GetBestMuseumPrices(HashSet<string> alreadyDonated, int amount = 30, bool excludeItemsWithParent = false)
    {
        Dictionary<string, (long pricePerExp, long[] auctionid)> best10 = await GetBestOptions(alreadyDonated, amount, excludeItemsWithParent);
        var ids = best10.SelectMany(i => i.Value.auctionid).ToList();
        using (var db = new HypixelContext())
        {
            var auctions = await db.Auctions.Where(a => ids.Contains(a.UId)).ToListAsync();
            var byUid = auctions.ToDictionary(a => a.UId);
            return best10.Where(b => b.Value.auctionid.All(x => byUid.ContainsKey(x))).Select(a =>
            {
                if (a.Value.auctionid.Length > 1)
                {
                    return new Cheapest
                    {
                        Options = a.Value.auctionid.Select(x => (byUid[x].Uuid, byUid[x].ItemName)).ToArray(),
                        ItemName = a.Key,
                        PricePerExp = a.Value.pricePerExp
                    };
                }
                return new Cheapest
                {
                    AuctuinUuid = byUid[a.Value.auctionid.First()].Uuid,
                    ItemName = byUid[a.Value.auctionid.First()].ItemName,
                    PricePerExp = a.Value.pricePerExp,
                    TotalPrice = byUid[a.Value.auctionid.First()].StartingBid
                };
            });
        }
    }

    private static readonly Dictionary<string, string[]> ExtraItemsRequired = new()
    {
        { "CRIMSON_HUNTER", [ "BLAZE_BELT"] },
        // ^ items with multiple sets
        {"SNOW_SUIT", ["SNOW_NECKLACE", "SNOW_CLOAK", "SNOW_BELT", "SNOW_GLOVES"] },
        {"THUNDER", ["THUNDERBOLT_NECKLACE"]},
    };

    public async Task<Dictionary<string, (long pricePerExp, long[] auctionid)>> GetBestOptions(HashSet<string> alreadyDonated, int amount, bool excludeItemsWithParent = false)
    {
        var items = await hypixelItemService.GetItemsAsync();
        var prices = await sniperApi.ApiAuctionLbinsGetAsync();
        ProcessDonatedParents(alreadyDonated, items);

        // Base set of donateable items (may be filtered to exclude items that have a parent)
        var donateableItems = items.Where(i => i.Value.MuseumData != null);
        if (excludeItemsWithParent)
        {
            donateableItems = donateableItems.Where(i => i.Value.MuseumData?.Parent == null || !i.Value.MuseumData.Parent.Any());
        }
        var single = donateableItems.Where(i => i.Value.MuseumData.DonationXp > 0).ToDictionary(i => i.Key, i => i.Value.MuseumData.DonationXp);
        Dictionary<string, (int Value, HashSet<string>)> set = GetSets(donateableItems);

        // some sets contain extra items not listed in the api yet
        foreach (var item in set)
        {
            if (!ExtraItemsRequired.TryGetValue(item.Key, out var extraItems))
            {
                continue;
            }
            foreach (var extraItem in extraItems)
            {
                item.Value.Item2.Add(extraItem);
            }
        }

        var parentLookup = CreateParentLookup(items);
        var result = new Dictionary<string, (long pricePerExp, long[] auctionid)>();
        foreach (var item in single)
        {
            if (prices.TryGetValue(item.Key, out var price))
            {
                var totalExp = item.Value;
                totalExp = AddChildExp(parentLookup, item.Key, alreadyDonated, totalExp);
                result.Add(item.Key, (price.Price / totalExp, new[] { price.AuctionId }));
            }
        }
        foreach (var item in set)
        {
            var auctions = item.Value.Item2.Select(i => prices.GetValueOrDefault(i));
            if (auctions.Any(a => a == null))
            {
                continue;
            }
            var price = auctions.Sum(a => a.Price) / item.Value.Item1;
            result.Add(item.Key, (price, item.Value.Item2.Select(i => prices[i].AuctionId).ToArray()));
        }
        var best10 = result.Where(r => !alreadyDonated.Contains(r.Key))
            .OrderBy(i => i.Value.Item1)
            .Take(amount).ToDictionary(i => i.Key, i => i.Value);
        return best10;
    }

    private static Dictionary<string, (int Value, HashSet<string>)> GetSets(IEnumerable<KeyValuePair<string, Core.Services.Item>> donateableItems)
    {
        // Force certain items that appear in multiple sets to only belong to one chosen set.
        var forcedSet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // For helmet, chestplate, leggings and boots prefer the BLAZE set
            { "BLAZE_HELMET", "BLAZE" },
            { "BLAZE_CHESTPLATE", "BLAZE" },
            { "BLAZE_LEGGINGS", "BLAZE" },
            { "BLAZE_BOOTS", "BLAZE" },
            // The belt should belong to CRIMSON_HUNTER
            { "BLAZE_BELT", "CRIMSON_HUNTER" },
            // Fisherman set is comingled with sponge set
            { "PRISMARINE_NECKLACE", "FISHERMAN"},
            { "SPONGE_BELT", "FISHERMAN"},
            { "CLOWNFISH_CLOAK", "FISHERMAN"},
            { "CLAY_BRACELET", "FISHERMAN"}
        };

        var entries = donateableItems
            .Where(i => i.Value.MuseumData.ArmorSetDonationXp != null && i.Value.MuseumData.ArmorSetDonationXp?.Count != 0)
            .SelectMany(i => i.Value.MuseumData.ArmorSetDonationXp.Select(aset =>
                new
                {
                    ItemId = i.Key,
                    SetKey = forcedSet.TryGetValue(i.Key, out var forced) ? forced : aset.Key,
                    SetValue = aset.Value
                }))
            .GroupBy(e => e.SetKey) // there are 14 items that are part of multiple sets
            .ToDictionary(g => g.Key,
                g => (g.First().SetValue, g.Select(j => j.ItemId).ToHashSet()));

        return entries;
    }

    private static int AddChildExp(Dictionary<string, Core.Services.Item> parentLookup, string currentTag, HashSet<string> alreadyDonated, int totalExp)
    {
        if (parentLookup.TryGetValue(currentTag, out var child) && !alreadyDonated.Contains(child.Id))
        {
            totalExp += child.MuseumData.DonationXp;
            totalExp += AddChildExp(parentLookup, child.Id, alreadyDonated, 0);
        }

        return totalExp;
    }

    /// <summary>
    /// Processes donated parents by calling AddDonatedParents multiple times to handle nested relationships
    /// </summary>
    private static void ProcessDonatedParents(HashSet<string> alreadyDonated, Dictionary<string, Core.Services.Item> items)
    {
        // Process 4 layers deep to handle all parent-child relationships
        AddDonatedParents(alreadyDonated, items);
        AddDonatedParents(alreadyDonated, items);
        AddDonatedParents(alreadyDonated, items);
        AddDonatedParents(alreadyDonated, items);
    }

    /// <summary>
    /// Creates a lookup dictionary for parent items in the museum
    /// </summary>
    private static Dictionary<string, Core.Services.Item> CreateParentLookup(Dictionary<string, Core.Services.Item> items)
    {
        return items.Where(i => i.Value.MuseumData?.Parent?.FirstOrDefault().Value != null)
                    .DistinctBy(i => i.Value.MuseumData.Parent.First().Value)
                    .ToDictionary(i => i.Value.MuseumData?.Parent?.FirstOrDefault().Value, i => i.Value);
    }

    /// <summary>
    /// Checks if an item can be donated to the museum and returns its donation experience
    /// </summary>
    private static bool TryGetDonationExp(string itemId, Dictionary<string, Core.Services.Item> items, HashSet<string> alreadyDonated, out Core.Services.Item item, out int donationExp)
    {
        item = null;
        donationExp = 0;

        // Skip if already donated
        if (alreadyDonated.Contains(itemId))
            return false;

        // Check if this item can be donated to museum
        if (!items.TryGetValue(itemId, out item) || item.MuseumData == null)
            return false;

        donationExp = item.MuseumData.DonationXp;
        return donationExp > 0;
    }

    /// <summary>
    /// Gets the best museum items to craft based on experience per cost efficiency, 
    /// filtered by what the user can actually craft
    /// </summary>
    /// <param name="alreadyDonated">Items already donated to the museum</param>
    /// <param name="playerId">Player UUID</param>
    /// <param name="profileId">Profile UUID</param>
    /// <param name="amount">Number of items to return</param>
    /// <param name="excludeItemsWithParent">If true, exclude items that have a parent (only highest-tier items are returned)</param>
    /// <returns>Best craftable museum items sorted by experience per cost</returns>
    public async Task<IEnumerable<CraftableCheapest>> GetBestCraftableMuseumItems(HashSet<string> alreadyDonated, string playerId, string profileId, int amount = 30, bool excludeItemsWithParent = false)
    {
        try
        {
            var profitableCraftsTask = GetCrafts();

            // Filter craftable items based on player's progress
            var craftableItems = await profileClient.FilterProfitableCrafts(profitableCraftsTask, playerId, profileId);
            Activity.Current.Log("Found " + craftableItems.Count + " craftable items for player " + playerId);

            // Get items data for museum information
            var items = await hypixelItemService.GetItemsAsync();

            // Add donated parents to exclude items where parent is already donated
            ProcessDonatedParents(alreadyDonated, items);

            var result = new List<CraftableCheapest>();
            var parentLookup = CreateParentLookup(items);
            Activity.Current.Log("Processing " + craftableItems.Count + " craftable items for museum donation");

            foreach (var craft in craftableItems)
            {
                // Check if this item can be donated to museum
                if (!TryGetDonationExp(craft.ItemId, items, alreadyDonated, out var item, out var donationExp))
                    continue;

                // Optionally exclude items that have a parent to only get the highest-tier items
                if (excludeItemsWithParent && item.MuseumData?.Parent != null && item.MuseumData.Parent.Any())
                    continue;

                // Add child experience for parent items
                var totalExp = AddChildExp(parentLookup, craft.ItemId, alreadyDonated, donationExp);

                var costPerExp = craft.CraftCost / totalExp;

                result.Add(new CraftableCheapest
                {
                    ItemId = craft.ItemId,
                    ItemName = craft.ItemName,
                    CraftCost = (long)craft.CraftCost,
                    SellPrice = (long)craft.SellPrice,
                    PricePerExp = (long)costPerExp,
                    DonationExp = totalExp,
                    Ingredients = craft.Ingredients?.ToList() ?? new (),
                    Volume = craft.Volume,
                    Profit = (long)(craft.SellPrice - craft.CraftCost),
                    RequiredCollection = craft.ReqCollection,
                    RequiredSlayer = craft.ReqSlayer,
                    RequiredSkill = craft.ReqSkill
                });
            }
            Activity.Current.Log("Found " + result.Count + " craftable museum items for player " + playerId);

            return result
                .OrderBy(i => i.PricePerExp)
                .Take(amount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting best craftable museum items for player {PlayerId} profile {ProfileId}", playerId, profileId);
            return Enumerable.Empty<CraftableCheapest>();
        }
    }

    private async Task<List<ProfitableCraft>> GetCrafts()
    {
        var data = await craftsApi.GetAllWithHttpInfoAsync();
        return JsonConvert.DeserializeObject<List<ProfitableCraft>>(data.RawContent);
    }

    private static void AddDonatedParents(HashSet<string> alreadyDonated, Dictionary<string, Core.Services.Item> items)
    {
        foreach (var item in items)
        {
            var parent = item.Value.MuseumData?.Parent?.FirstOrDefault().Value;
            if (parent == default)
            {
                continue;
            }
            if (alreadyDonated.Contains(parent))
                alreadyDonated.Add(item.Value.MuseumData?.Parent.First().Key);
        }
    }

    public class Cheapest
    {
        public string AuctuinUuid { get; set; }
        public (string uuid, string name)[] Options { get; set; }
        public string ItemName { get; set; }
        public long PricePerExp { get; set; }
        public long TotalPrice { get; set; }
    }

    public class CraftableCheapest
    {
        public string ItemId { get; set; }
        public string ItemName { get; set; }
        public long CraftCost { get; set; }
        public long SellPrice { get; set; }
        public long PricePerExp { get; set; }
        public int DonationExp { get; set; }
        public List<Ingredient> Ingredients { get; set; }
        public double Volume { get; set; }
        public long Profit { get; set; }
        public RequiredCollection RequiredCollection { get; set; }
        public RequiredCollection RequiredSlayer { get; set; }
        public RequiredSkill RequiredSkill { get; set; }
    }
}