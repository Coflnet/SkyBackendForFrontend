using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Core.Services;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Crafts.Client.Model;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.PlayerState.Client.Model;
using fNbt;
using fNbt.Tags;
using Item = Coflnet.Sky.Core.Services.Item;

namespace Coflnet.Sky.Commands.Shared;

public class AccessoryService : IAccessoryService
{
    private static readonly Dictionary<string, int> PowerByTier = new()
    {
        { "COMMON", 3 },
        { "UNCOMMON", 5 },
        { "RARE", 8 },
        { "EPIC", 12 },
        { "LEGENDARY", 16 },
        { "MYTHIC", 22 },
        { "SPECIAL", 3 },
        { "VERY_SPECIAL", 5 }
    };

    private readonly IProfileClient profileClient;
    private readonly IPlayerStateApi stateApi;
    private readonly IHypixelItemStore itemStore;
    private readonly Sniper.Client.Api.IAuctionApi auctionApi;
    private readonly ICraftsApi craftsApi;

    public AccessoryService(IProfileClient profileClient, IPlayerStateApi stateApi, IHypixelItemStore itemStore,
        Sniper.Client.Api.IAuctionApi auctionApi, ICraftsApi craftsApi)
    {
        this.profileClient = profileClient;
        this.stateApi = stateApi;
        this.itemStore = itemStore;
        this.auctionApi = auctionApi;
        this.craftsApi = craftsApi;
    }

    public async Task<OwnedAccessories> GetOwnedAccessories(string mcUuid, string profileId)
    {
        var profileTask = TryGet(() => GetFromProfile(mcUuid, profileId), new List<string>());
        var bagTask = TryGet(() => GetBag(mcUuid, profileId), new AccessoryBag());
        var bag = await bagTask ?? new AccessoryBag();
        var owned = new OwnedAccessories
        {
            BagAccessories = bag.Accessories ?? [],
            PageCount = bag.PageCount,
            SeenPages = bag.SeenPages ?? [],
            UpdatedAt = bag.UpdatedAt
        };
        owned.Tags.UnionWith(await profileTask);
        owned.Tags.UnionWith(owned.BagAccessories.Select(a => a?.Tag).Where(t => t != null));
        Activity.Current?.Log("Owned accessories: " + string.Join(", ", owned.Tags));
        return owned;
    }

    public async Task<List<CheapAccessory>> GetCheapestPerMagicalPower(string mcUuid, string profileId, int limit)
    {
        // start the market fetches first so they run while the owned accessories load
        var itemsTask = itemStore.GetItemsAsync();
        var pricesTask = auctionApi.ApiAuctionLbinsGetAsync();
        var craftsTask = craftsApi.GetAllAsync();
        var owned = await GetOwnedAccessories(mcUuid, profileId);
        return await RankMarket(itemsTask, pricesTask, craftsTask, owned, limit);
    }

    public async Task<List<CheapAccessory>> GetCheapestPerMagicalPower(OwnedAccessories owned, int limit)
    {
        return await RankMarket(itemStore.GetItemsAsync(), auctionApi.ApiAuctionLbinsGetAsync(), craftsApi.GetAllAsync(), owned, limit);
    }

    private static async Task<List<CheapAccessory>> RankMarket(Task<Dictionary<string, Item>> itemsTask,
        Task<Dictionary<string, Coflnet.Sky.Sniper.Client.Model.ReferencePrice>> pricesTask, Task<List<ProfitableCraft>> craftsTask, OwnedAccessories owned, int limit)
    {
        var prices = (await pricesTask).ToDictionary(p => p.Key, p => p.Value?.Price ?? 0);
        return Rank(await itemsTask, prices, await craftsTask, owned.Tags).Take(limit).ToList();
    }

    /// <summary>
    /// Orders not yet owned accessories by lowest bin per magical power gained.
    /// An accessory whose recipe contains another accessory is treated as its upgrade.
    /// </summary>
    public static IEnumerable<CheapAccessory> Rank(Dictionary<string, Item> items, Dictionary<string, long> prices, IEnumerable<ProfitableCraft> crafts, HashSet<string> owned)
    {
        var accessories = items.Where(i => i.Value.Category == "ACCESSORY").ToDictionary(i => i.Key, i => i.Value);
        var lowerTiers = GetLowerTiers(accessories, crafts);
        var outgrown = owned.SelectMany(o => GetTransitive(o, lowerTiers)).ToHashSet();
        var result = new List<CheapAccessory>();
        foreach (var (tag, item) in accessories)
        {
            if (!prices.TryGetValue(tag, out var price) || price <= 0 || owned.Contains(tag) || outgrown.Contains(tag))
                continue;
            var replaced = GetTransitive(tag, lowerTiers).Where(owned.Contains).Select(t => Power(accessories[t])).DefaultIfEmpty(0).Max();
            var gain = Power(item) - replaced;
            if (gain <= 0)
                continue;
            result.Add(new CheapAccessory { Tag = tag, ItemName = item.Name, PowerGain = gain, Price = price, PricePerPower = price / gain });
        }
        return result.OrderBy(r => (double)r.Price / r.PowerGain).ThenBy(r => r.Price);
    }

    private static int Power(Item item)
    {
        return PowerByTier.GetValueOrDefault(item.Tier ?? "COMMON", 0);
    }

    private static Dictionary<string, HashSet<string>> GetLowerTiers(Dictionary<string, Item> accessories, IEnumerable<ProfitableCraft> crafts)
    {
        return (crafts ?? []).Where(c => c?.ItemId != null && accessories.ContainsKey(c.ItemId))
            .GroupBy(c => c.ItemId)
            .ToDictionary(g => g.Key, g => g.SelectMany(c => c.Ingredients ?? [])
                .Select(i => i.ItemId).Where(id => id != null && id != g.Key && accessories.ContainsKey(id)).ToHashSet());
    }

    private static HashSet<string> GetTransitive(string tag, Dictionary<string, HashSet<string>> lowerTiers)
    {
        var found = new HashSet<string>();
        var queue = new Queue<string>([tag]);
        while (queue.TryDequeue(out var current))
        {
            foreach (var lower in lowerTiers.GetValueOrDefault(current) ?? [])
                if (lower != tag && found.Add(lower))
                    queue.Enqueue(lower);
        }
        return found;
    }

    private static async Task<T> TryGet<T>(Func<Task<T>> source, T fallback)
    {
        try
        {
            return await source();
        }
        catch (Exception e)
        {
            Activity.Current?.Log("Accessory source failed: " + e.Message);
            return fallback;
        }
    }

    private async Task<List<string>> GetFromProfile(string mcUuid, string profileId)
    {
        profileId = string.IsNullOrEmpty(profileId) ? "current" : profileId;
        var member = await profileClient.GetProfile(mcUuid, profileId, DateTime.UtcNow - TimeSpan.FromMinutes(10));
        var data = member?.Inventory?.BagContents?.TalismanBag?.Data;
        if (string.IsNullOrEmpty(data))
            return [];
        var list = NBT.File(Convert.FromBase64String(data)).RootTag.Get<NbtList>("i");
        return list?.OfType<NbtCompound>().Select(NBT.ItemID).Where(id => id != null).ToList() ?? [];
    }

    private async Task<AccessoryBag> GetBag(string mcUuid, string profileId)
    {
        Guid? profile = Guid.TryParse(profileId, out var parsed) ? parsed : null;
        return await stateApi.PlayerStatePlayerIdAccessoriesGetAsync(Guid.Parse(mcUuid), profile);
    }
}
