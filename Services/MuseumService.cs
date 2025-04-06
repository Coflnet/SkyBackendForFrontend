using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Sniper.Client.Api;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Core.Services;
using Coflnet.Sky.Core;
using Microsoft.EntityFrameworkCore;
using System;

namespace Coflnet.Sky.Commands.Shared;

public class MuseumService
{
    private IAuctionApi sniperApi;
    private ILogger<MuseumService> logger;
    private IHypixelItemStore hypixelItemService;

    public MuseumService(IAuctionApi sniperApi, ILogger<MuseumService> logger, IHypixelItemStore hypixelItemService)
    {
        this.sniperApi = sniperApi;
        this.logger = logger;
        this.hypixelItemService = hypixelItemService;
    }

    public async Task<IEnumerable<Cheapest>> GetBestMuseumPrices(HashSet<string> alreadyDonated, int amount = 30)
    {
        Dictionary<string, (long pricePerExp, long[] auctionid)> best10 = await GetBestOptions(alreadyDonated, amount);
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
                    PricePerExp = a.Value.pricePerExp
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

    public async Task<Dictionary<string, (long pricePerExp, long[] auctionid)>> GetBestOptions(HashSet<string> alreadyDonated, int amount)
    {
        var items = await hypixelItemService.GetItemsAsync();
        var prices = await sniperApi.ApiAuctionLbinsGetAsync();
        AddDonatedParents(alreadyDonated, items);
        AddDonatedParents(alreadyDonated, items);
        AddDonatedParents(alreadyDonated, items);
        AddDonatedParents(alreadyDonated, items); // 4 layers deep

        var donateableItems = items.Where(i => i.Value.MuseumData != null);
        var single = donateableItems.Where(i => i.Value.MuseumData.DonationXp > 0).ToDictionary(i => i.Key, i => i.Value.MuseumData.DonationXp);

        var set = donateableItems.Where(i => i.Value.MuseumData.ArmorSetDonationXp != null && i.Value.MuseumData.ArmorSetDonationXp?.Count != 0)
                .SelectMany(i => i.Value.MuseumData.ArmorSetDonationXp.Select(aset => (i.Key, aset)))
                .GroupBy(i => i.aset.Key) // there are 14 items that are part of multiple sets
                .ToDictionary(i => i.Key,
                    i => (i.First().aset.Value, i.Select(j => j.Key).ToHashSet()));

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

        var parentLookup = items.Where(i => i.Value.MuseumData?.Parent?.FirstOrDefault().Value != null)
                    .DistinctBy(i => i.Value.MuseumData.Parent.First().Value)
                    .ToDictionary(i => i.Value.MuseumData?.Parent?.FirstOrDefault().Value, i => i.Value);
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

    private static int AddChildExp(Dictionary<string, Core.Services.Item> parentLookup, string currentTag, HashSet<string> alreadyDonated, int totalExp)
    {
        if (parentLookup.TryGetValue(currentTag, out var child) && !alreadyDonated.Contains(child.Id))
        {
            totalExp += child.MuseumData.DonationXp;
            totalExp += AddChildExp(parentLookup, child.Id, alreadyDonated, 0);
        }

        return totalExp;
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
    }
}