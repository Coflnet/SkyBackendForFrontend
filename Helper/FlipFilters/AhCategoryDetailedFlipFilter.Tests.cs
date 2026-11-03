using System.Collections.Generic;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.Items.Client.Model;
using Coflnet.Sky.Mayor.Client.Api;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class AhCategoryDetailedFlipFilterTests
{
    [SetUp]
    public void Setup()
    {
        DiHandler.OverrideService<FilterEngine, FilterEngine>(new FilterEngine(new Mock<INBT>(MockBehavior.Strict).Object));
        // seeded sets only, a load call would hit the strict items api mock
        var service = new FilterStateService(
            NullLogger<FilterStateService>.Instance,
            new Mock<IMayorApiApi>(MockBehavior.Strict).Object,
            new Mock<IItemsApi>(MockBehavior.Strict).Object);
        service.State.itemCategories[ItemCategory.HELMET] = ["MAGMA_LORD_HELMET"];
        service.State.itemCategories[ItemCategory.CHESTPLATE] = ["MAGMA_LORD_CHESTPLATE"];
        service.State.itemCategories[ItemCategory.LEGGINGS] = ["MAGMA_LORD_LEGGINGS"];
        service.State.itemCategories[ItemCategory.BOOTS] = ["FIERY_CRIMSON_BOOTS"];
        service.State.itemCategories[ItemCategory.SWORD] = ["ASPECT_OF_THE_END"];
        service.State.itemCategories[ItemCategory.LONGSWORD] = [];
        service.State.itemCategories[ItemCategory.BOW] = [];
        service.State.itemCategories[ItemCategory.WAND] = [];
        service.State.itemCategories[ItemCategory.FISHING_WEAPON] = [];
        service.State.itemCategories[ItemCategory.GAUNTLET] = [];
        service.State.itemCategories[ItemCategory.ACCESSORY] = [];
        DiHandler.OverrideService<FilterStateService, FilterStateService>(service);
    }

    [TestCase("MAGMA_LORD_CHESTPLATE", Category.UNKNOWN, "Armor", false)]
    [TestCase("FIERY_CRIMSON_BOOTS", Category.UNKNOWN, "Armor", false)]
    [TestCase("MAGMA_LORD_HELMET", Category.ARMOR, "Armor", false)]
    [TestCase("ASPECT_OF_THE_END", Category.UNKNOWN, "Armor", true)]
    [TestCase("ASPECT_OF_THE_END", Category.UNKNOWN, "Weapon", false)]
    [TestCase("ENCHANTED_BOOK", Category.UNKNOWN, "Armor", true)]
    [TestCase("ENCHANTED_BOOK", Category.UNKNOWN, "Unknown", false)]
    [TestCase("MAGMA_LORD_CHESTPLATE", Category.UNKNOWN, "Unknown", true)]
    public void BlacklistByAhCategoryUsesItemCategoriesForUnknownAuctions(string tag, Category stored, string ahCategory, bool delivered)
    {
        var settings = new FlipSettings
        {
            MinProfit = 0,
            MinVolume = 0,
            BlackList = [new ListEntry { filter = new() { { "AhCategory", ahCategory } } }]
        };
        var flip = new FlipInstance
        {
            MedianPrice = 130_000_000,
            Volume = 1,
            Auction = new SaveAuction
            {
                Tag = tag,
                Category = stored,
                StartingBid = 100_000_000,
                FlatenedNBT = new Dictionary<string, string>()
            },
            Context = new Dictionary<string, string>(),
            Finder = LowPricedAuction.FinderType.SNIPER_MEDIAN
        };

        var match = settings.MatchesSettings(flip);

        Assert.That(match.Item1, Is.EqualTo(delivered), match.Item2);
    }
}
