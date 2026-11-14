using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core.Services;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Crafts.Client.Model;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.PlayerState.Client.Model;
using Coflnet.Sky.Sniper.Client.Model;
using fNbt;
using fNbt.Tags;
using Moq;
using NUnit.Framework;
using Item = Coflnet.Sky.Core.Services.Item;

namespace Coflnet.Sky.Commands.Shared;

public class AccessoryServiceTests
{
    private static readonly string McUuid = Guid.NewGuid().ToString("N");

    [Test]
    public async Task RanksMissingAccessoriesByPricePerMagicalPower()
    {
        var service = Create(profile: MockProfile("OWNED_RING"), bag: Bag("SPEED_TALISMAN"));

        var result = await service.GetCheapestPerMagicalPower(McUuid, null, 100);

        // cheap talisman 30/3, speed artifact 200/(8-3) and speed ring 100/(5-3) since the talisman is owned
        Assert.That(result.Select(r => r.ItemName), Is.EqualTo(new[] { "Cheap Talisman", "Speed Artifact", "Speed Ring" }));
        Assert.That(result.Select(r => r.PricePerPower), Is.EqualTo(new long[] { 10, 40, 50 }));
    }

    [Test]
    public async Task SortsByExactPricePerMagicalPower()
    {
        var items = new Dictionary<string, Item> { ["COMMON_ONE"] = Accessory("Common One", "COMMON"), ["RARE_ONE"] = Accessory("Rare One", "RARE") };
        var prices = new Dictionary<string, long> { ["COMMON_ONE"] = 300_002, ["RARE_ONE"] = 800_005 };
        var service = Create(items: items, prices: prices);

        var result = await service.GetCheapestPerMagicalPower(McUuid, null, 100);

        // 800,005 / 8 = 100,000.6 is cheaper than 300,002 / 3 = 100,000.7 even though both truncate to 100,000
        Assert.That(result.Select(r => r.ItemName), Is.EqualTo(new[] { "Rare One", "Common One" }));
    }

    [Test]
    public async Task LimitsResult()
    {
        var result = await Create().GetCheapestPerMagicalPower(McUuid, null, 1);
        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task BagAccessoriesCountAsOwnedAndExposePages()
    {
        var bag = Bag("SPEED_TALISMAN");
        bag.PageCount = 3;
        bag.SeenPages = [1];
        var owned = await Create(bag: bag).GetOwnedAccessories(McUuid, Guid.Empty.ToString());

        Assert.That(owned.Tags, Is.EquivalentTo(new[] { "SPEED_TALISMAN" }));
        Assert.That(owned.BagAccessories, Has.Count.EqualTo(1));
        Assert.That(owned.BagPartiallySeen, Is.True);
    }

    [Test]
    public async Task FailingBagDoesNotFailResult()
    {
        var state = new Mock<IPlayerStateApi>();
        state.Setup(a => a.PlayerStatePlayerIdAccessoriesGetAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"));
        var owned = await Create(profile: MockProfile("OWNED_RING"), state: state.Object).GetOwnedAccessories(McUuid, null);

        Assert.That(owned.Tags, Is.EquivalentTo(new[] { "OWNED_RING" }));
        Assert.That(owned.PageCount, Is.EqualTo(0));
    }

    [Test]
    public async Task FailingProfileDoesNotFailResult()
    {
        var profile = new Mock<IProfileClient>();
        profile.Setup(c => c.GetProfile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>())).ThrowsAsync(new Exception("boom"));
        var owned = await Create(profile: profile.Object, bag: Bag("SPEED_TALISMAN")).GetOwnedAccessories(McUuid, null);

        Assert.That(owned.Tags, Is.EquivalentTo(new[] { "SPEED_TALISMAN" }));
    }

    private static AccessoryService Create(IProfileClient profile = null, AccessoryBag bag = null, IPlayerStateApi state = null,
        Dictionary<string, Item> items = null, Dictionary<string, long> prices = null)
    {
        var stateApi = state ?? MockState(bag ?? new AccessoryBag());
        var store = new Mock<IHypixelItemStore>();
        store.Setup(s => s.GetItemsAsync()).ReturnsAsync(items ?? DefaultItems());
        var auctions = new Mock<Sniper.Client.Api.IAuctionApi>();
        auctions.Setup(a => a.ApiAuctionLbinsGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((prices ?? DefaultPrices()).ToDictionary(p => p.Key, p => new ReferencePrice { Price = p.Value }));
        return new AccessoryService(profile ?? Mock.Of<IProfileClient>(), stateApi, store.Object, auctions.Object, MockCrafts());
    }

    private static Dictionary<string, Item> DefaultItems() => new()
    {
        ["SPEED_TALISMAN"] = Accessory("Speed Talisman", "COMMON"),
        ["SPEED_RING"] = Accessory("Speed Ring", "UNCOMMON"),
        ["SPEED_ARTIFACT"] = Accessory("Speed Artifact", "RARE"),
        ["CHEAP_TALISMAN"] = Accessory("Cheap Talisman", "COMMON"),
        ["OWNED_RING"] = Accessory("Owned Ring", "EPIC"),
        ["DIRT"] = System.Text.Json.JsonSerializer.Deserialize<Item>("{\"name\":\"Dirt\",\"tier\":\"COMMON\"}")
    };

    private static Dictionary<string, long> DefaultPrices() => new()
    {
        ["SPEED_TALISMAN"] = 1,
        ["SPEED_RING"] = 100,
        ["SPEED_ARTIFACT"] = 200,
        ["CHEAP_TALISMAN"] = 30,
        ["OWNED_RING"] = 1,
        ["DIRT"] = 1
    };

    private static Item Accessory(string name, string tier)
    {
        return System.Text.Json.JsonSerializer.Deserialize<Item>($"{{\"name\":\"{name}\",\"category\":\"ACCESSORY\",\"tier\":\"{tier}\"}}");
    }

    private static ICraftsApi MockCrafts()
    {
        var api = new Mock<ICraftsApi>();
        api.Setup(a => a.GetAllAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Craft("SPEED_RING", "SPEED_TALISMAN"), Craft("SPEED_ARTIFACT", "SPEED_RING")]);
        return api.Object;
    }

    private static ProfitableCraft Craft(string result, string ingredient)
    {
        return new ProfitableCraft { ItemId = result, Ingredients = [new Coflnet.Sky.Crafts.Client.Model.Ingredient { ItemId = ingredient, Count = 1 }] };
    }

    private static IProfileClient MockProfile(string tag)
    {
        var item = new NbtCompound { new NbtCompound("tag") { new NbtCompound("ExtraAttributes") { new NbtString("id", tag) } } };
        var file = new NbtFile(new NbtCompound("") { new NbtList("i", [item, new NbtCompound()]) });
        var data = Convert.ToBase64String(file.SaveToBuffer(NbtCompression.GZip));
        var member = new Api.Client.Model.Member
        {
            Inventory = new Api.Client.Model.Inventory { BagContents = new Api.Client.Model.BagContents { TalismanBag = new Api.Client.Model.InventoryElem { Data = data } } }
        };
        var client = new Mock<IProfileClient>();
        client.Setup(c => c.GetProfile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync(member);
        return client.Object;
    }

    private static AccessoryBag Bag(string tag)
    {
        return new AccessoryBag { Accessories = [new BagAccessory { Tag = tag, Page = 1, Slot = 0 }], PageCount = 1, SeenPages = [1] };
    }

    private static IPlayerStateApi MockState(AccessoryBag bag)
    {
        var api = new Mock<IPlayerStateApi>();
        api.Setup(a => a.PlayerStatePlayerIdAccessoriesGetAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bag);
        return api.Object;
    }
}
