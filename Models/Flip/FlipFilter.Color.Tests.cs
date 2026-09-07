using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using Coflnet.Sky.Core;
using Coflnet.Sky.Core.Services;
using Coflnet.Sky.Filter;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class FlipFilterColorTests
{
    [SetUp]
    public void Setup()
    {
        DiHandler.OverrideService<FilterEngine, FilterEngine>(new FilterEngine(new Moq.Mock<INBT>().Object));
        var service = new HypixelItemService(new HttpClient(), NullLogger<HypixelItemService>.Instance);
        var item = System.Text.Json.JsonSerializer.Deserialize<Coflnet.Sky.Core.Services.Item>("""
            {"id":"TEST_ARMOR","color":"170,170,170"}
            """);
        typeof(HypixelItemService).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(service, new Dictionary<string, Coflnet.Sky.Core.Services.Item> { { "TEST_ARMOR", item } });
        DiHandler.OverrideService<HypixelItemService, HypixelItemService>(service);
    }

    [TestCase("pattern:AABBCC", "170:170:170", false)]
    [TestCase("pattern:AABBCC", "17:34:255", true)]
    [TestCase("_E_E_E", "170:170:170", false)]
    [TestCase("_E_E_E", "26:42:58", true)]
    [TestCase("AAAAAA-3", "170:170:170", false)]
    [TestCase("AAAAAA-3", "171:171:171", true)]
    [TestCase("AAAAAA-3", "171:171:172", false)]
    [TestCase("pattern:ABCABC", "208:125:7", true)]
    [TestCase("F2DF11-11", "240:222:9", true)]
    [TestCase("AAAAAA", "170:170:170", true)]
    [TestCase("pattern:AABBCC", null, false)]
    [TestCase("AAAAAA-3", null, false)]
    public void MatchesNonDefaultColors(string colorFilter, string actual, bool expected)
    {
        var filter = new FlipFilter(new() { { "Color", colorFilter } }, null);
        var auction = new SaveAuction { Tag = "TEST_ARMOR", FlatenedNBT = new() };
        if (actual != null) auction.FlatenedNBT["color"] = actual;
        var flip = new FlipInstance { Auction = auction };
        Assert.That(filter.IsMatch(flip), Is.EqualTo(expected));
        Assert.That(filter.GetExpression().Compile()(flip), Is.EqualTo(expected));
        if (actual == null)
        {
            auction.FlatenedNBT = null;
            Assert.That(filter.IsMatch(flip), Is.False);
        }
    }

    [Test]
    public void UnknownDefaultDoesNotDiscardColoredItems()
    {
        var filter = new FlipFilter(new() { { "Color", "AAAAAA-3" } }, null);
        Assert.That(filter.IsMatch(new FlipInstance { Auction = new SaveAuction
        {
            Tag = "UNKNOWN", FlatenedNBT = new() { { "color", "170:170:170" } }
        } }), Is.True);
    }

    [Test]
    public void DefaultColorSkipsRemainingAuctionFilters()
    {
        // Invalid RGB would throw if the color matcher ran; default metadata can reject it first.
        var service = DiHandler.GetService<HypixelItemService>();
        var items = (Dictionary<string, Coflnet.Sky.Core.Services.Item>)typeof(HypixelItemService)
            .GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(service);
        items["TEST_ARMOR"] = items["TEST_ARMOR"] with { Color = "invalid" };
        var filter = new FlipFilter(new() { { "Color", "AAAAAA-3" } }, null);
        Assert.That(filter.IsMatch(new FlipInstance { Auction = new SaveAuction
        {
            Tag = "TEST_ARMOR", FlatenedNBT = new() { { "color", "invalid" } }
        } }), Is.False);
    }
}
